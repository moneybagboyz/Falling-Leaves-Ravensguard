using Ravensguard.Sim.Core;
using Ravensguard.Sim.Disease;
using Ravensguard.Sim.Politics;
using Ravensguard.Sim.Unrest;

namespace Ravensguard.Sim.WorldGen.History;

/// <summary>
/// History v1 (history-recalibration 4.2): the politics, the great plague, the coarse unrest and the lifespans of the legends, run through the live functions (<see cref="WarUtility"/>, <see cref="UnrestRules"/>,
/// <see cref="ContinentPlagueStep"/>, <see cref="DiseaseRules"/>) at the history's seasonal scale. Nothing here runs for a v0 world: every entry point is behind <c>_v1</c> and every draw is on a stream v0 never
/// touches or on the same key v0 already drew, so v0 regenerates byte-identically (HR-1).
/// </summary>
public sealed partial class HistoryGenerator
{
    private int[] _alliance = [], _heresyOf = [], _excommUntil = [], _topSeasons = [], _rung = [], _plagueTick = [], _unrestPm = [];
    private EntityId[] _excommRuler = [];
    private readonly EntityId[] _hitEv = new EntityId[8];
    private readonly int[] _hitTick = new int[8];
    private readonly bool[] _hitPlague = new bool[8];
    private readonly bool[] _abolished = new bool[8];
    private Dictionary<string, string>? _extra;

    private void InitV1()
    {
        _alliance = new int[_R * _R]; _heresyOf = new int[_R]; _excommUntil = new int[_R]; _topSeasons = new int[_R]; _rung = new int[_R]; _plagueTick = new int[_R]; _unrestPm = new int[_R];
        _excommRuler = new EntityId[_R];
        Array.Fill(_heresyOf, -1); Array.Fill(_plagueTick, -100);
        Array.Fill(_hitTick, -100);
    }

    // ---------- keyed entries ----------
    private static Cause CzK(CauseKind k, EntityId ev, string noteKey, params ChronParam[] ps) => new(k, Opt(ev), "", noteKey, ps.Length == 0 ? null : ps);

    /// <summary>The next keyed entry also carries this machine field (the calibration tests read it; the hash covers it).</summary>
    private void With(string key, int value) { (_extra ??= [])[key] = value.ToString(System.Globalization.CultureInfo.InvariantCulture); }

    private ChronParam PlaceOf(RealmState r) => r.Capital >= 0 ? ChronParam.Place("place", _h.Settlements[r.Capital].Id) : ChronParam.Count("place", 0);
    private ChronParam RulerParam(RealmState r, string name = "ruler") => ChronParam.Person(name, r.Ruler);

    /// <summary>The legacy writers' English causes carry no key and, for the history's chance events, no specific kind: v1 gives each a kind and the generic note key of that kind (the row lists them), so the legends read with specific causes.</summary>
    private static Cause Upgrade(string type, Cause c)
    {
        if (c.NoteKey.Length > 0) return c;
        var kind = c.Kind;
        if (kind is CauseKind.Other or CauseKind.Unknown)
            kind = type switch
            {
                "settlement_grew" or "emigration" or "settlement_abandoned" or "settlement_starved" => CauseKind.Migration,
                "plague_begins" or "plague_ends" or "plague_spreads" or "settlement_emptied" => CauseKind.Disease,
                "master_crafter" or "artefact_forged" or "trade_route" => CauseKind.Trade,
                "insult" or "realm_fell" or "tithe_dispute" => CauseKind.Diplomacy,
                "heir_born" or "marriage" => CauseKind.Family,
                "saint_rises" or "saint_martyred" => CauseKind.Faith,
                _ => CauseKind.Law,
            };
        if (c.EventId != null && kind == c.Kind) return c;                                   // an earlier entry already makes it specific
        string key = "ch_note_h_" + kind.ToString().ToLowerInvariant();
        return c with { Kind = kind, NoteKey = key };
    }

    // ---------- opinion: the live modifiers (kin and marriage, religion, heresy, excommunication, war) ----------
    private int FaithOf(RealmState r) => r.Religion >= 0 ? r.Religion : Math.Clamp(r.CultureId, 0, 7);
    private bool Excommunicated(int a) => _excommUntil.Length > a && _excommUntil[a] > _tick && Rm(a).Ruler == _excommRuler[a];

    private int OpinionTarget(int a, int b)
    {
        int t = 0;
        if (FaithOf(Rm(a)) == FaithOf(Rm(b))) t += HistoryRules.OpSameReligion;
        if (_alliance[a * _R + b] > _tick) t += HistoryRules.OpMarriage;
        if (FaithOf(Rm(a)) == FaithOf(Rm(b)) && (_heresyOf[a] >= 0) != (_heresyOf[b] >= 0)) t += HistoryRules.OpHeresy;
        if (Excommunicated(a) || Excommunicated(b)) t += HistoryRules.OpExcommunicated;
        if (_atWar[a] >= 0 && _atWar[a] == _atWar[b]) t += HistoryRules.OpWar;
        t -= Math.Min(25, _border[a * _R + b] / 3) + Grudge(a, b) / 3;
        return Math.Clamp(t, -100, 100);
    }

    // ---------- war declarations by the one utility ----------
    private void DeclarationsV1()
    {
        var pol = HistoryRules.Politics;
        int nr = _h.Realms.Count;
        int alive = Math.Max(1, _h.Realms.Count(q => q.Alive));
        long gate = HistoryRules.WarGatePct * 10_000L * Math.Min(alive, HistoryRules.RealmsRef) / Math.Max(alive, HistoryRules.RealmsRef);
        for (int a = 0; a < nr; a++)
        {
            var ra = Rm(a);
            if (!ra.Alive || AtWar(a) || ra.Ruler.Value == 0 || _h.GetNotable(ra.Ruler) is not { Alive: true } ruler) continue;
            var traits = LordTraits.OfRuler(_seed, ruler.Id.Value, ruler.Ambition);
            int threshold = WarUtility.Threshold(pol, traits);
            int bestB = -1, bestU = int.MinValue; bool bestExc = false;
            for (int b = 0; b < nr; b++)
            {
                if (a == b || !Rm(b).Alive || AtWar(b) || Contact(a, b) == 0) continue;
                var rb = Rm(b);
                var cb = _cb[a * _R + b];
                bool cultural = ra.CultureId != rb.CultureId && _cultCount[b * 8 + Math.Clamp(ra.CultureId, 0, 7)] >= 8;
                bool claim = cb.Kind is CasusBelliKind.BorderClaim or CasusBelliKind.Excommunication || cultural || Grudge(a, b) >= 30;   // the church's ban gives its neighbours a claim to the banned ruler's lands
                int cbw = cb.Kind switch
                {
                    CasusBelliKind.Succession => WarUtility.CbWeight(pol, CbKind.SeizedFief), CasusBelliKind.Raid => WarUtility.CbWeight(pol, CbKind.Raid), CasusBelliKind.Insult => WarUtility.CbWeight(pol, CbKind.Insult),
                    CasusBelliKind.Excommunication => WarUtility.CbWeight(pol, CbKind.Excommunication), _ => 0,
                };
                if (!claim && cb.Kind == CasusBelliKind.None) continue;
                if (_opinion[a * _R + b] >= 25 && cb.Kind != CasusBelliKind.Excommunication) continue;                                                          // kin and friends do not fight each other
                long payroll = Levy(ra) / 12 + 1;
                int lost = _tick - ra.LastFamineTick < 8 ? 300 : pol.HarvestLostPermille;
                var inp = new WarInputs(claim, cbw, Levy(ra), Levy(rb) + 1, (int)Math.Min(100, ra.Treasury / payroll), (Season)_season, WarUtility.HarvestCost(pol, lost), _alliance[a * _R + b] > _tick, _truce[a * _R + b] > _tick);
                int u = WarUtility.Score(pol, inp);
                if (u > bestU) { bestU = u; bestB = b; bestExc = cb.Kind == CasusBelliKind.Excommunication; }
            }
            if (bestB < 0 || bestU < threshold) continue;
            long ppm = bestExc ? HistoryRules.ExcommWarGatePct * 10_000L : gate;
            if (Chance(ra.Id.Value, S.War, ppm)) DeclareWarV1(ra, Rm(bestB), _cb[a * _R + bestB], bestU);
        }
    }

    private void DeclareWarV1(RealmState a, RealmState b, CbState cb, int utility)
    {
        bool naval = _border[a.Index * _R + b.Index] == 0;
        var kind = cb.Kind == CasusBelliKind.None ? CasusBelliKind.BorderClaim : cb.Kind;
        string baseName = WarName(a, b, kind == CasusBelliKind.Excommunication ? CasusBelliKind.BorderClaim : kind, naval);
        var w = new WarRecord
        {
            Index = _h.Wars.Count, Attacker = a.Index, Defender = b.Index, StartTick = _tick, Cb = kind, CbEvent = cb.Event, CbNote = cb.Note ?? (kind == CasusBelliKind.BorderClaim ? "a claim on lands held by another realm" : ""), Naval = naval,
            Name = $"{baseName} ({_year})", ArmyA = Levy(a), ArmyD = Levy(b) * 11 / 10,
        };
        string noteKey = kind switch { CasusBelliKind.Succession => "ch_note_h_cb_succession", CasusBelliKind.Raid => "ch_note_h_cb_raid", CasusBelliKind.Insult => "ch_note_h_cb_insult", CasusBelliKind.Excommunication => "ch_note_h_cb_excomm", _ => "ch_note_h_cb_claim" };
        With("utility", utility);
        w.DeclEvent = EvK("h_war_declared", "ch_h_war_declared", [RulerParam(a, "a"), RulerParam(b, "b"), ChronParam.Count("utility", utility), PlaceOf(b)], Cap(a), [a.Id, b.Id],
            CzK(CauseKind.Diplomacy, cb.Event, noteKey));
        _h.Wars.Add(w);
        _atWar[a.Index] = _atWar[b.Index] = w.Index;
        Op(a.Index, b.Index, -30);
        _cb[a.Index * _R + b.Index] = default;
        if (kind == CasusBelliKind.Excommunication)
            for (int i = _h.Excommunications.Count - 1; i >= 0; i--)
                if (_h.Excommunications[i].Realm == b.Index && _h.Excommunications[i].WarTick < 0) { _h.Excommunications[i].WarTick = _tick; break; }
        EnsureGeneral(a); EnsureGeneral(b);
    }

    // ---------- peace by war score and weariness ----------
    private bool PeaceDueV1(WarRecord w)
    {
        var pol = HistoryRules.Politics;
        int seasons = _tick - w.StartTick;
        if (seasons < 3) return false;
        var A = Rm(w.Attacker); var D = Rm(w.Defender);
        long dead = 0;
        foreach (var bf in _h.Battlefields) if (bf.War == w.Index) dead += bf.Dead;
        int pctA = (int)Math.Min(100, dead / 2 * 100 / Math.Max(1, A.Pop)), pctD = (int)Math.Min(100, dead / 2 * 100 / Math.Max(1, D.Pop));
        int trA = (int)Math.Min(100, A.Treasury / (w.ArmyA / 12 + 1)), trD = (int)Math.Min(100, D.Treasury / (w.ArmyD / 12 + 1));
        int wearA = WarUtility.Weariness(pol, seasons, pctA, trA), wearD = WarUtility.Weariness(pol, seasons, pctD, trD);
        bool sue = WarUtility.Sues(pol, wearA, Math.Max(0, w.Score)) || WarUtility.Sues(pol, wearD, Math.Max(0, -w.Score));
        if (!(sue || w.Score >= 60 || w.Score <= -60)) return false;
        return Pct(w.DeclEvent.Value + (ulong)_tick, S.War, HistoryRules.PeaceAcceptPct);
    }

    // ---------- the great plague, by the live function ----------
    private void PlagueStepV1()
    {
        if (!_plagueActive)
        {
            if (_season != 0 || !ContinentPlagueStep.Starts(_seed, _year, HistoryRules.Severity)) return;
            var pops = new int[_h.Settlements.Count];
            for (int i = 0; i < pops.Length; i++) { var s = _h.Settlements[i]; pops[i] = !s.IsRuin && s.Kind >= SettlementKind.Town ? s.Pop : 0; }
            int origin = ContinentPlagueStep.OriginOf(_seed, _year, pops, i => _h.Settlements[i].RouteCount > 0);
            if (origin < 0) return;
            var o = _h.Settlements[origin];
            _plagueName = PlagueNames[Roll(2, S.Plague, 0, PlagueNames.Length)];
            _plagueActive = true; _plagueDead = 0; _plagueTowns = 0; _plagueCell = o.Cell;
            for (int i = 0; i < _h.Settlements.Count; i++) SetInf(i, -1);
            _plagueStart = Ev("plague_begins", $"{_plagueName} begins", $"A sickness breaks out in {o.Name} and follows the trade roads", o.Cell,
                o.Realm >= 0 ? [o.Id, Rm(o.Realm).Id] : [o.Id], Cz(CauseKind.Other, EntityId.None, "a sickness carried in on the trade roads"));
            InfectV1(origin);
            _plagueLastInfect = _tick;
            return;
        }
        var volumes = new List<long>();
        foreach (var rt in _h.TradeRoutes) if (!rt.Abandoned) volumes.Add(Math.Min(_h.Settlements[SIdx(rt.A)].Pop, _h.Settlements[SIdx(rt.B)].Pop));
        volumes.Sort();
        long median = volumes.Count == 0 ? 0 : volumes[volumes.Count / 2];
        var fresh = new List<int>();
        foreach (var rt in _h.TradeRoutes)
        {
            if (rt.Abandoned) continue;
            int a = SIdx(rt.A), b = SIdx(rt.B);
            long vol = Math.Min(_h.Settlements[a].Pop, _h.Settlements[b].Pop);
            int ppm = ContinentPlagueStep.SpreadPpm(vol, median);
            for (int pass = 0; pass < 2; pass++)
            {
                int from = pass == 0 ? a : b, to = pass == 0 ? b : a;
                if (InfTick(from) == _tick - 1 && InfTick(to) < 0 && !fresh.Contains(to) && !_h.Settlements[to].IsRuin && Chance(rt.A.Value ^ rt.B.Value, S.Plague, ppm)) fresh.Add(to);
            }
        }
        fresh.Sort();
        foreach (var t in fresh) { InfectV1(t); _plagueLastInfect = _tick; }
        if (_tick - _plagueLastInfect >= 2)
        {
            _plagueActive = false;
            Ev("plague_ends", $"{_plagueName} ends", $"The sickness burns itself out after reaching {_plagueTowns} {Plural(_plagueTowns, "settlement", "settlements")}; about {Num(_plagueDead)} dead", _plagueCell,
                [], Cz(CauseKind.Death, _plagueStart, "the plague runs its course"));
        }
    }

    /// <summary>The loss of a place the plague reaches: its attack times the case fatality (towns about 30 percent, villages about 20), from the disease rules the live game plays by.</summary>
    private static int PlagueLossPct(SettlementKind k)
    {
        var dr = DiseaseRules.Current; var sd = dr.S;
        int attack = k >= SettlementKind.Town ? sd.AttackTownPct : k == SettlementKind.Village ? sd.AttackVillagePct : sd.AttackHamletPct;
        int fatality = dr.DeathPpm(IllnessKind.Plague, IllnessCare.None, HistoryRules.Severity) / 10_000;
        return attack * fatality / 100;
    }

    private void InfectV1(int i)
    {
        var s = _h.Settlements[i];
        SetInf(i, _tick);
        _plagueTowns++;
        int cu = _c.CultureId[s.Cell] < 8 ? _c.CultureId[s.Cell] : 0;
        _hitEv[cu] = _plagueStart; _hitTick[cu] = _tick; _hitPlague[cu] = true;
        if (s.Realm >= 0) _plagueTick[s.Realm] = _tick;
        int mort = Math.Max(3, PlagueLossPct(s.Kind) * Roll(s.Id.Value, S.Plague, 85, 116) / 100);
        if (s.Minor && s.Kind == SettlementKind.Hamlet && Pct(s.Id.Value, S.Plague, HistoryRules.HamletEmptyPct) && !RuinCrowded(s.Cell)) { PlagueEmpties(i); return; }
        int dead = s.Pop * mort / 100;
        s.Pop -= dead;
        int rural = Kill(s.Cell, RuralPop25(s) * 5 / 100, 2);
        _plagueDead += dead + rural;
        s.LastBlow = _plagueStart; s.LastBlowTick = _tick;
        if (s.Kind >= SettlementKind.Town && _plagueTowns > 1)
        {
            With("lost_pm", mort * 10); With("people", dead);
            EvK("h_plague_reaches", "ch_h_plague_reaches", [ChronParam.Place("place", s.Id), ChronParam.Count("dead", dead)], s.Cell, s.Realm >= 0 ? [s.Id, Rm(s.Realm).Id] : [s.Id],
                CzK(CauseKind.Disease, _plagueStart, "ch_note_h_contagion"));
        }
        if (s.Pop < 5) MakeRuin(i, "plague", _plagueStart, _plagueStart);
        else if (Pct(s.Id.Value, S.Saint, 12)) MaybeSaint(i, _plagueStart, _plagueName);
        int fat = DiseaseRules.Current.DeathPpm(IllnessKind.Plague, IllnessCare.None, HistoryRules.Severity);
        for (int k = 0, cnt = _h.Notables.Count; k < cnt; k++)
        {
            var n = _h.Notables[k];
            if (n.Alive && n.Residence == i && Chance(n.Id.Value, S.Death, (long)(s.Kind >= SettlementKind.Town ? DiseaseRules.Current.S.AttackTownPct : DiseaseRules.Current.S.AttackVillagePct) * fat / 100)) Die(n, "the " + _plagueName.ToLowerInvariant().Replace("the ", ""), _plagueStart);
        }
    }

    // ---------- the coarse unrest of a realm and the ladder ----------
    private int RealmUnrestPm(RealmState r)
    {
        long pm = (long)r.Unrest * HistoryRules.UnrestPerPoint;
        if (_tick - r.LastFamineTick < 8) pm += HistoryRules.UnrestFamine;
        if (_tick - _plagueTick[r.Index] < 6) pm += HistoryRules.UnrestPlague;
        if (_heresyOf[r.Index] >= 0) pm += HistoryRules.UnrestHeresy;
        if (Excommunicated(r.Index)) pm += HistoryRules.UnrestExcommunicated;
        if (r.Treasury < Levy(r) * 2) pm += HistoryRules.UnrestThinTreasury;
        if (_atWar[r.Index] >= 0) { var w = _h.Wars[_atWar[r.Index]]; pm += Math.Min(HistoryRules.UnrestWarWearyMax, (w.Attacker == r.Index ? w.WearyA : w.WearyD) * HistoryRules.UnrestWarWeary); }
        return (int)Math.Min(1000, pm);
    }

    private void LifecycleV1(RealmState r, Notable? ruler)
    {
        var u = HistoryRules.Unrest;
        int a = r.Index;
        int pm = RealmUnrestPm(r); _unrestPm[a] = pm;
        int rung = UnrestRules.RungFor(u, pm);
        if (rung >= 3 && rung > _rung[a])
        {
            var (kind, ev, key) = UnrestCause(r);
            With("unrest", pm); With("rung", rung);
            EvK("h_unrest_rung", rung >= 4 ? "ch_h_unrest_revolt" : "ch_h_unrest_riot", [PlaceOf(r), RulerParam(r), ChronParam.Count("unrest", pm)], Cap(r), [r.Id], CzK(kind, ev, key));
        }
        _rung[a] = rung;
        _topSeasons[a] = rung >= 4 ? _topSeasons[a] + 1 : 0;
        if (r.Cells >= 100 && !AtWar(a) && UnrestRules.RevoltDue(u, pm, _topSeasons[a] * TimeConst.DaysPerSeason, 0)) { RebellionV1(r, pm); _topSeasons[a] = 0; }
        if (!r.Alive) return;
        if (rung >= 3 && ruler is { Alive: true } && Chance(r.Id.Value, S.Coup, _s.CoupPpmTick * (30L + r.Unrest * 2) / 100)) Coup(r, ruler);
    }

    /// <summary>The biggest thing behind a realm's unrest, as a cause: the plague or famine that hit it, a ruler cut off from the church, a heresy, a lost war, else the taxes.</summary>
    private (CauseKind Kind, EntityId Event, string Key) UnrestCause(RealmState r)
    {
        int cu = Math.Clamp(r.CultureId, 0, 7);
        if (_tick - _plagueTick[r.Index] < 6 && _hitEv[cu].Value != 0 && _hitPlague[cu]) return (CauseKind.Disease, _hitEv[cu], "ch_note_h_plague_unrest");
        if (_tick - r.LastFamineTick < 8 && _hitEv[cu].Value != 0) return (CauseKind.Famine, _hitEv[cu], "ch_note_h_famine_unrest");
        if (Excommunicated(r.Index)) { var x = _h.Excommunications.LastOrDefault(e => e.Realm == r.Index); if (x != null) return (CauseKind.Faith, x.Event, "ch_note_h_excomm_unrest"); }
        if (_heresyOf[r.Index] >= 0) { var x = _h.Heresies.LastOrDefault(e => e.Realm == r.Index && e.Alive); if (x != null) return (CauseKind.Faith, x.Event, "ch_note_h_heresy_unrest"); }
        if (r.LastDefeatEvent.Value != 0 && _h.Chronicle.Get(r.LastDefeatEvent)!.When.Year >= _year - 12) return (CauseKind.War, r.LastDefeatEvent, "ch_note_h_defeat_unrest");
        return (CauseKind.Tax, EntityId.None, "ch_note_h_tax_unrest");
    }

    private void RebellionV1(RealmState r, int pm)
    {
        int pick = -1, bp = -1, cap = r.Capital;
        for (int i = 0; i < _h.Settlements.Count; i++)
        {
            var s = _h.Settlements[i];
            if (s.IsRuin || s.Realm != r.Index || i == cap) continue;
            if (cap >= 0 && Dist(s.Cell, _h.Settlements[cap].Cell) < 6) continue;
            int score = s.Pop + (s.LastBlow.Value != 0 && _tick - s.LastBlowTick < 48 ? 400 : 0);
            if (score > bp) { bp = score; pick = i; }
        }
        if (pick < 0) return;
        var seat = _h.Settlements[pick];
        int cu = _c.CultureId[seat.Cell] < 8 ? _c.CultureId[seat.Cell] : r.CultureId;
        var dyn = NewDynasty(cu, EntityId.None, _year);
        var rebel = NewNotable(NotableRole.Rebel, cu, r.Index, dyn.Id, Pct(seat.Id.Value, S.Rebel, 15), _year - YearsAgo(Roll(seat.Id.Value, S.Rebel, 25, 50)), EntityId.None, EntityId.None);
        dyn.Founder = rebel.Id; rebel.Residence = pick; rebel.Ambition = 70 + Roll(seat.Id.Value, S.Rebel, 0, 30);
        var (kind, cev, key) = UnrestCause(r);
        With("unrest", pm); With("rung", UnrestRules.RungFor(HistoryRules.Unrest, pm)); With("top_seasons", Math.Max(HistoryRules.RevoltAfterSeasons, _topSeasons[r.Index]));
        var rising = EvK("h_rebellion", "ch_h_rebellion", [ChronParam.Person("leader", rebel.Id), ChronParam.Place("place", seat.Id), RulerParam(r)], seat.Cell, [rebel.Id, seat.Id, r.Id], CzK(kind, cev, key));
        r.Unrest = Math.Min(100, r.Unrest + 8);
        if (Pct(rebel.Id.Value, S.Rebel, 45) && SplitRealm(r, rebel, rising, "a successful rebellion")) return;
        var ev = Ev("rebellion_crushed", $"The Rising at {seat.Name} is crushed", $"{rebel.Full}'s rebels are broken by the army of {r.Name}; the leader is hanged and {seat.Name} is made to pay", seat.Cell, [rebel.Id, seat.Id, r.Id], Cz(CauseKind.War, rising, "the rebellion's defeat"));
        { string rn = BattleName(seat.Cell, seat); int rd = Roll(seat.Id.Value, S.Rebel, 40, 300); Kill(seat.Cell, rd, 3); _h.Battlefields.Add(new Battlefield(seat.Cell, ev, rn, _tick, -1, rd)); }
        seat.Pop -= seat.Pop * 12 / 100;
        MarkDead(rebel, "hanged for rebellion", ev);
        r.Unrest = Math.Max(0, r.Unrest - 20);
    }

    // ---------- lifespans: the vital table's old-age hazard ----------
    private static long NotableDeathPpmV1(int age) => (age < 1 ? 200 : age < 5 ? 30 : age < 15 ? 8 : 12) * 250L + HistoryRules.OldAgePctYear(age) * 2_500L;
}
