using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.WorldGen.History;

public sealed partial class HistoryGenerator
{
    private struct CbState { public CasusBelliKind Kind; public EntityId Event; public int Expiry; public string Note; }

    private readonly int[] _border, _opinion, _truce, _cultCount, _atWar;
    private readonly CbState[] _cb;

    private static readonly string[] InsultNotes =
    [
        "an envoy was mocked before the whole court", "a promised bride was refused", "the customary tribute was withheld", "a banner was torn down at a joint tourney",
    ];

    private bool AtWar(int a) => _atWar[a] >= 0;
    private int Grudge(int from, int to) { foreach (var g in _h.Grudges) if (g.From == from && g.To == to) return g.Strength; return 0; }

    private void AddGrudge(int from, int to, int strength, EntityId ev, string note)
    {
        foreach (var g in _h.Grudges)
            if (g.From == from && g.To == to) { g.Strength = Math.Min(100, g.Strength + strength); g.CauseEvent = ev; g.Tick = _tick; g.Note = note; return; }
        _h.Grudges.Add(new Grudge { From = from, To = to, Strength = Math.Min(100, strength), CauseEvent = ev, Tick = _tick, Note = note });
    }

    private void Op(int a, int b, int d)
    {
        _opinion[a * _R + b] = Math.Clamp(_opinion[a * _R + b] + d, -100, 100);
        _opinion[b * _R + a] = Math.Clamp(_opinion[b * _R + a] + d, -100, 100);
    }

    private void PutCb(int a, int b, CasusBelliKind kind, EntityId ev, string note)
        => _cb[a * _R + b] = new CbState { Kind = kind, Event = ev, Expiry = _tick + 32, Note = note };

    /// <summary>Creates a "claim_pressed" casus belli event for <paramref name="a"/> against <paramref name="b"/>.</summary>
    private void SetCb(int a, int b, CasusBelliKind kind, EntityId causeEv, string text, string note)
    {
        var ev = Ev("claim_pressed", $"{Rm(a).Name} presses a claim", text, Cap(Rm(a)), [Rm(a).Id, Rm(b).Id], Cz(CauseKind.Law, causeEv, note));
        PutCb(a, b, kind, ev, note);
    }

    // ---------- diplomacy: income, opinion, incidents, war declarations ----------
    private void PoliticsPass()
    {
        int nr = _h.Realms.Count;
        for (int a = 0; a < nr; a++)
        {
            var r = Rm(a);
            if (!r.Alive) continue;
            r.Treasury = (int)Math.Min(2_000_000, r.Treasury + r.Pop / 400);
            if (r.Unrest > 0) r.Unrest--;
        }
        if (_season == 0)
        {
            for (int a = 0; a < nr; a++)
                for (int b = 0; b < nr; b++)
                {
                    if (a == b || !Rm(a).Alive || !Rm(b).Alive) continue;
                    int bd = _border[a * _R + b];
                    int target = (Rm(a).CultureId == Rm(b).CultureId ? 15 : -5) - Math.Min(25, bd / 3) - Grudge(a, b) / 3;
                    int o = _opinion[a * _R + b];
                    _opinion[a * _R + b] = o + Math.Clamp(target - o, -3, 3);
                }
            foreach (var g in _h.Grudges) if (g.Strength > 0) g.Strength = Math.Max(0, g.Strength - 2);
        }
        // expire casus belli
        for (int i = 0; i < nr * _R; i++) { }
        for (int a = 0; a < nr; a++)
            for (int b = 0; b < nr; b++)
                if (_cb[a * _R + b].Kind != CasusBelliKind.None && (_cb[a * _R + b].Expiry < _tick || !Rm(a).Alive || !Rm(b).Alive)) _cb[a * _R + b] = default;

        // incidents between neighbours
        for (int a = 0; a < nr; a++)
            for (int b = a + 1; b < nr; b++)
            {
                if (_border[a * _R + b] == 0 || !Rm(a).Alive || !Rm(b).Alive) continue;
                int op = (_opinion[a * _R + b] + _opinion[b * _R + a]) / 2;
                long ppm = _s.IncidentPpmTick * (100 - op / 2) / 100;
                if (!Chance((ulong)(a * _R + b), S.Incident, ppm)) continue;
                Incident(a, b);
            }

        // war declarations (utility, plan §4A.4)
        for (int a = 0; a < nr; a++)
        {
            var ra = Rm(a);
            if (!ra.Alive || AtWar(a) || ra.Ruler.Value == 0 || _h.GetNotable(ra.Ruler) is not { Alive: true }) continue;
            int bestB = -1, bestU = int.MinValue;
            for (int b = 0; b < nr; b++)
            {
                if (a == b || !Rm(b).Alive || AtWar(b) || _border[a * _R + b] == 0) continue;
                var cb = _cb[a * _R + b];
                if (cb.Kind == CasusBelliKind.None || _truce[a * _R + b] > _tick) continue;
                int u = WarUtility(ra, Rm(b), cb);
                if (u > bestU) { bestU = u; bestB = b; }
            }
            if (bestB >= 0 && bestU >= _s.WarUtilityThreshold && Pct(ra.Id.Value, S.War, _s.WarChancePct)) DeclareWar(ra, Rm(bestB), _cb[a * _R + bestB]);
        }
    }

    private int WarUtility(RealmState a, RealmState b, CbState cb)
    {
        var ruler = _h.GetNotable(a.Ruler);
        int claim = Math.Min(35, _cultCount[b.Index * 8 + Math.Clamp(a.CultureId, 0, 7)] / 2 + Grudge(a.Index, b.Index) / 3 + _border[a.Index * _R + b.Index] / 6);
        int cbv = cb.Kind switch { CasusBelliKind.Succession => 35, CasusBelliKind.BorderClaim => 28, CasusBelliKind.Raid => 22, CasusBelliKind.Insult => 14, _ => 0 };
        long la = Levy(a), lb = Levy(b) + 1;
        int ratio = (int)Math.Clamp(la * 100 / lb - 100, -60, 60) * 2 / 3;
        int tre = (int)Math.Min(15, a.Treasury / (la * 3 + 1));
        int amb = ((ruler?.Ambition ?? 50) - 50) / 5;
        int seas = _season switch { 0 => 12, 1 => 8, 2 => -8, _ => -25 };
        int harvest = 6 + (_tick - a.LastFamineTick < 8 ? 15 : 0);
        int op = -_opinion[a.Index * _R + b.Index] / 8;
        return claim + cbv + ratio + tre + amb + seas - harvest + op - (a.Unrest > 50 ? 10 : 0);
    }

    private void Incident(int a, int b)
    {
        // the more ambitious ruler is the likelier aggressor
        int amA = _h.GetNotable(Rm(a).Ruler)?.Ambition ?? 50, amB = _h.GetNotable(Rm(b).Ruler)?.Ambition ?? 50;
        bool aActs = Roll((ulong)(a * _R + b), S.Incident, 0, amA + amB + 1) < amA;
        int actor = aActs ? a : b, victim = aActs ? b : a;
        var ra = Rm(actor); var rv = Rm(victim);
        if (_cb[victim * _R + actor].Kind != CasusBelliKind.None && _cb[actor * _R + victim].Kind != CasusBelliKind.None) return;
        int kind = Roll((ulong)(a * _R + b), S.Incident, 0, 100);
        // a claim needs people of the claimant's culture under the other realm
        int claimCells = _cultCount[victim * 8 + Math.Clamp(ra.CultureId, 0, 7)];
        if (kind >= 65 && (claimCells < 8 || ra.CultureId == rv.CultureId && claimCells < 30)) kind = Roll((ulong)(a * _R + b), S.Incident, 0, 65);
        if (kind < 35)
        {
            int si = BorderSettlement(victim, actor);
            if (si < 0) return;
            var s = _h.Settlements[si];
            if (_cb[victim * _R + actor].Kind != CasusBelliKind.None) return;
            int loss = Roll(s.Id.Value, S.Incident, 4, 13) * s.Pop / 100;
            s.Pop -= loss; Devastate(s.Cell, 2, 8);
            string note = $"raiding by the border lords of {ra.Name}";
            var ev = Ev("border_raid", $"The Raid on {s.Name}", $"Riders from {ra.Name} burn the outskirts of {s.Name} in {rv.Name}; {Num(loss)} killed or carried off", s.Cell, [ra.Id, rv.Id, s.Id], Cz(CauseKind.Raid, EntityId.None, note));
            s.LastBlow = ev; s.LastBlowTick = _tick;
            PutCb(victim, actor, CasusBelliKind.Raid, ev, note);
            Op(actor, victim, -15);
        }
        else if (kind < 65)
        {
            if (_cb[victim * _R + actor].Kind != CasusBelliKind.None) return;
            string note = InsultNotes[Roll((ulong)(a * _R + b), S.Incident, 0, InsultNotes.Length)];
            var cap = Cap(rv);
            var ev = Ev("insult", $"The Slight at {(rv.Capital >= 0 ? _h.Settlements[rv.Capital].Name : rv.Name)}", $"{ra.Name} wounds the pride of {rv.Name}: {note}", cap, [ra.Id, rv.Id], Cz(CauseKind.Other, EntityId.None, note));
            PutCb(victim, actor, CasusBelliKind.Insult, ev, $"insult: {note}");
            Op(actor, victim, -25);
        }
        else
        {
            if (_cb[actor * _R + victim].Kind != CasusBelliKind.None) return;
            string note = $"border claim over the {Culture(ra.CultureId)} lands held by {rv.Name}";
            var ev = Ev("claim_pressed", $"{ra.Name} presses a border claim", $"{ra.Name} demands the {Culture(ra.CultureId)} marches now held by {rv.Name}", Cap(ra), [ra.Id, rv.Id],
                Cz(CauseKind.Law, EntityId.None, "kin of the same tongue living under a foreign lord"));
            PutCb(actor, victim, CasusBelliKind.BorderClaim, ev, note);
            Op(actor, victim, -10);
        }
    }

    private int BorderSettlement(int victim, int actor)
    {
        for (int i = 0; i < _h.Settlements.Count; i++)
        {
            var s = _h.Settlements[i];
            if (s.IsRuin || s.Realm != victim) continue;
            int x = Cx(s.Cell), y = Cy(s.Cell);
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                    if (In(x + dx, y + dy) && _owner[(y + dy) * N + x + dx] == actor) return i;
        }
        return -1;
    }

    private void DeclareWar(RealmState a, RealmState b, CbState cb)
    {
        string baseName = cb.Kind switch
        {
            CasusBelliKind.Succession => $"War of the {b.Name} Succession",
            CasusBelliKind.BorderClaim => $"The {Culture(a.CultureId)} Reclamation",
            CasusBelliKind.Raid => $"The {a.Name} Raiders' War",
            _ => $"The War of the {a.Name} Slight",
        };
        var w = new WarRecord
        {
            Index = _h.Wars.Count, Attacker = a.Index, Defender = b.Index, StartTick = _tick, Cb = cb.Kind, CbEvent = cb.Event, CbNote = cb.Note,
            Name = $"{baseName} ({_year})", ArmyA = Levy(a), ArmyD = Levy(b) * 11 / 10,
        };
        string verb = cb.Kind switch { CasusBelliKind.Succession => "claims the throne of", CasusBelliKind.BorderClaim => "marches to take back lands from", CasusBelliKind.Raid => "answers the raids of", _ => "avenges an insult against" };
        w.DeclEvent = Ev("war_declared", $"{w.Name} begins", $"{a.Name} {verb} {b.Name}; {Num(w.ArmyA)} levies muster against {Num(w.ArmyD)}", Cap(a), [a.Id, b.Id],
            Cz(CauseKind.War, cb.Event, cb.Note));
        _h.Wars.Add(w);
        _atWar[a.Index] = _atWar[b.Index] = w.Index;
        Op(a.Index, b.Index, -30);
        _cb[a.Index * _R + b.Index] = default;
        EnsureGeneral(a); EnsureGeneral(b);
    }

    // ---------- wars: battles, sieges, peace ----------
    private void WarsPass()
    {
        int n = _h.Wars.Count;
        for (int i = 0; i < n; i++)
        {
            var w = _h.Wars[i];
            if (!w.Alive) continue;
            StepWar(w);
            if (w.Alive) PeaceCheck(w);
        }
    }

    private void StepWar(WarRecord w)
    {
        var A = Rm(w.Attacker); var D = Rm(w.Defender);
        if (!A.Alive || !D.Alive) { EndWar(w, "a realm destroyed", EntityId.None); return; }
        w.ArmyA += (Levy(A) - w.ArmyA) / 4; w.ArmyD += (Levy(D) * 11 / 10 - w.ArmyD) / 4;
        A.Treasury = Math.Max(0, A.Treasury - w.ArmyA / 12); D.Treasury = Math.Max(0, D.Treasury - w.ArmyD / 12);
        w.WearyA += 2 + (A.Treasury == 0 ? 3 : 0); w.WearyD += 2 + (D.Treasury == 0 ? 3 : 0);
        bool attInv = w.Score >= -25;
        var inv = attInv ? A : D; var vic = attInv ? D : A;
        if (w.SiegeTicks >= 0) { StepSiege(w, inv, vic, attInv); return; }
        if (_season == 3) return;   // winter quarters
        if (w.Target < 0 || _h.Settlements[w.Target].IsRuin || _h.Settlements[w.Target].Realm != vic.Index) w.Target = ChooseTarget(inv, vic);
        if (w.Target < 0) { if (vic.Cells > 0) w.Score += attInv ? 3 : -3; return; }
        var t = _h.Settlements[w.Target];
        int armyInv = attInv ? w.ArmyA : w.ArmyD, armyVic = attInv ? w.ArmyD : w.ArmyA;
        if (!Pct(w.DeclEvent.Value + (ulong)w.Index, S.Battle, _season == 2 ? 35 : 60))
        {
            if (w.Score * (attInv ? 1 : -1) >= 10 && armyInv * 10 >= armyVic * 8 && Pct(w.DeclEvent.Value, S.Siege, 35)) BeginSiege(w, inv, vic, t, w.DeclEvent, "the enemy gives no battle");
            return;
        }
        // ----- battle -----
        int near = NearestSettlement(inv.Index, t.Cell);
        int from = near >= 0 ? _h.Settlements[near].Cell : Cap(inv);
        int sx = Math.Sign(Cx(from) - Cx(t.Cell)), sy = Math.Sign(Cy(from) - Cy(t.Cell));
        int off = Roll(w.DeclEvent.Value, S.Battle, 1, 5);
        int bx = Math.Clamp(Cx(t.Cell) + sx * off, 0, N - 1), by = Math.Clamp(Cy(t.Cell) + sy * off, 0, N - 1);
        int site = by * N + bx;
        if (_c.Ocean[site]) site = t.Cell;
        string bname = BattleName(site, t);
        var gi = EnsureGeneral(inv); var gv = EnsureGeneral(vic);
        long si = (long)armyInv * (80 + Roll(w.DeclEvent.Value, S.Battle, 0, 41)) / 100 * (100 + (gi.Skill - 50) / 3) / 100;
        long sv = (long)armyVic * (80 + Roll(w.DeclEvent.Value, S.Battle, 0, 41)) / 100 * (100 + (gv.Skill - 50) / 3) / 100;
        int def = _c.Biomes[site] switch { Biome.Mountain => 20, Biome.TemperateForest or Biome.Taiga or Biome.Marsh => 10, _ => 0 } + (t.Kind >= SettlementKind.Town ? 15 : 0);
        sv = sv * (100 + def) / 100;
        bool invWins = si > sv;
        int margin = (int)(Math.Abs(si - sv) * 100 / Math.Max(1, Math.Max(si, sv)));
        int loserPct = Math.Min(55, 18 + margin / 3 + Roll(w.DeclEvent.Value, S.Battle, 0, 10)), winnerPct = 6 + Roll(w.DeclEvent.Value, S.Battle, 0, 8);
        var win = invWins ? inv : vic; var lose = invWins ? vic : inv;
        int winArmy = invWins ? armyInv : armyVic, loseArmy = invWins ? armyVic : armyInv;
        int loseDead = loseArmy * loserPct / 100, winDead = winArmy * winnerPct / 100;
        int deaths = loseDead + winDead;
        int newInv = armyInv - (invWins ? winDead : loseDead), newVic = armyVic - (invWins ? loseDead : winDead);
        if (attInv) { w.ArmyA = newInv; w.ArmyD = newVic; } else { w.ArmyD = newInv; w.ArmyA = newVic; }
        Kill(site, deaths, 4);
        int d = 10 + margin / 3;
        w.Score = Math.Clamp(w.Score + ((invWins == attInv) ? d : -d), -100, 100);
        bool loseIsAtt = lose.Index == w.Attacker;
        if (loseIsAtt) { w.WearyA += 10; w.WearyD += 4; } else { w.WearyD += 10; w.WearyA += 4; }
        var gw = invWins ? gi : gv; var gl = invWins ? gv : gi;
        gw.Victories++;
        lose.Unrest = Math.Min(100, lose.Unrest + 4);
        w.Battles++;
        string extra = "";
        int fate = Roll(w.DeclEvent.Value, S.Battle, 0, 100);
        bool glSlain = fate < 18, glTaken = fate >= 18 && fate < 35;
        if (glSlain) extra = $" {Title(gl)} {gl.Full} of {lose.Name} falls in the fighting.";
        else if (glTaken) extra = $" {Title(gl)} {gl.Full} is taken and ransomed.";
        if (glTaken) { lose.Treasury = Math.Max(0, lose.Treasury - 200); win.Treasury += 200; }
        var bev = Ev("battle", $"The Battle of {bname}", $"{win.Name} defeats {lose.Name}; {Num(deaths)} dead.{extra}".TrimEnd(), site, [win.Id, lose.Id, gw.Id, gl.Id, t.Id],
            Cz(CauseKind.War, w.DeclEvent, w.CbNote));
        w.LastBattleEvent = bev;
        _h.Battlefields.Add(new Battlefield(site, bev, bname, _tick, w.Index, deaths));
        lose.LastDefeatEvent = bev;
        foreach (var a in _h.Artefacts)
        {
            if (a.Status != ArtefactStatus.Held || a.Holder != gl.Id || !(glSlain || glTaken)) continue;
            if (Pct(a.Id.Value, S.Art, 65))
            {
                a.Holder = win.Ruler.Value != 0 ? win.Ruler : win.Id; a.Transfers++;
                Ev("artefact_captured", $"{a.Name} is taken", $"{win.Name} carries off {a.Name} from the field of {bname}", site, [a.Id, gl.Id, win.Id], Cz(CauseKind.Battle, bev, "spoils of the battle"));
            }
            else ArtefactVanish(a, ArtefactStatus.Lost, site, bev, $"lost in the rout at {bname}");
        }
        if (glSlain) { MarkDead(gl, $"slain at the Battle of {bname}", bev); }
        else if (!glTaken && Pct(gl.Id.Value, S.Battle, 4) && win.Alive)
        {
            var bt = Ev("betrayal", $"{gl.Name} betrays {lose.Name}", $"{Who(gl)} abandons {lose.Name} after {bname} and takes service with {win.Name}", site, [gl.Id, lose.Id, win.Id], Cz(CauseKind.War, bev, "a general who saw the war was lost"));
            gl.Realm = win.Index; _general[lose.Index] = EntityId.None; _general[win.Index] = gl.Id; lose.Unrest = Math.Min(100, lose.Unrest + 6); _ = bt;
        }
        var ruler = _h.GetNotable(lose.Ruler);
        if (margin > 45 && ruler is { Alive: true } && Pct(ruler.Id.Value, S.Battle, 12)) Die(ruler, $"slain at the Battle of {bname}", bev);
        // a decisive win lets the invader invest a town
        if (invWins && w.Target >= 0 && Pct(w.DeclEvent.Value, S.Siege, 70)) BeginSiege(w, inv, vic, t, bev, "the victory in the field");
    }

    private int NearestSettlement(int realm, int cell)
    {
        int best = -1, bd = int.MaxValue;
        for (int i = 0; i < _h.Settlements.Count; i++)
        {
            var s = _h.Settlements[i];
            if (s.IsRuin || s.Realm != realm) continue;
            int d = Dist(s.Cell, cell);
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }

    private int ChooseTarget(RealmState inv, RealmState vic)
    {
        int best = -1; long bs = long.MinValue;
        for (int i = 0; i < _h.Settlements.Count; i++)
        {
            var s = _h.Settlements[i];
            if (s.IsRuin || s.Realm != vic.Index) continue;
            int near = NearestSettlement(inv.Index, s.Cell);
            int d = near >= 0 ? Dist(_h.Settlements[near].Cell, s.Cell) : 40;
            long score = (s.Pop + 80L) * 100 / (d + 4);
            if (score > bs) { bs = score; best = i; }
        }
        return best;
    }

    private string BattleName(int site, Settlement t)
    {
        var rs = Rs((ulong)site, S.Text);
        int x = Cx(site), y = Cy(site);
        bool river = false;
        for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) if (In(x + dx, y + dy) && _c.RiverFlow[(y + dy) * N + x + dx] >= _c.Settings.RiverFlowThreshold && !_c.Ocean[(y + dy) * N + x + dx]) river = true;
        if (rs.Range(0, 100) < 30) return t.Name;
        string adj = _names.Feature(0, ref rs);
        if (river) return $"{adj} Ford";
        return _c.Biomes[site] switch
        {
            Biome.Mountain => $"{adj} Pass", Biome.TemperateForest or Biome.Taiga => $"{adj} Wood", Biome.Marsh => $"{adj} Fen",
            Biome.Grassland or Biome.RiverValley => $"{adj} Field", Biome.Steppe => $"{adj} Heath", _ => $"{adj} Hill",
        };
    }

    private void BeginSiege(WarRecord w, RealmState inv, RealmState vic, Settlement t, EntityId cause, string why)
    {
        w.SiegeTicks = 0; w.Sieges++;
        w.SiegeEvent = Ev("siege_begins", $"The Siege of {t.Name}", $"{inv.Name} lays siege to {t.Name} ({Num(t.Pop)} souls) in {vic.Name}", t.Cell, [inv.Id, vic.Id, t.Id], Cz(CauseKind.War, cause, why));
    }

    private void StepSiege(WarRecord w, RealmState inv, RealmState vic, bool attInv)
    {
        if (w.Target < 0 || _h.Settlements[w.Target].IsRuin || _h.Settlements[w.Target].Realm != vic.Index) { w.SiegeTicks = -1; w.Target = -1; return; }
        var t = _h.Settlements[w.Target];
        w.SiegeTicks++;
        int sgn = attInv ? 1 : -1;
        if (_season == 3 && Pct(w.DeclEvent.Value + (ulong)w.SiegeTicks, S.Siege, 25))
        {
            Ev("siege_lifted", $"The Siege of {t.Name} is lifted", $"Frost and hunger drive {inv.Name}'s army from the walls of {t.Name}", t.Cell, [inv.Id, t.Id], Cz(CauseKind.Weather, w.SiegeEvent, "a winter siege starves and freezes the besiegers"));
            if (attInv) w.ArmyA -= w.ArmyA / 10; else w.ArmyD -= w.ArmyD / 10;
            w.Score = Math.Clamp(w.Score - 6 * sgn, -100, 100);
            w.SiegeTicks = -1; w.Target = -1;
            return;
        }
        int need = Math.Min(3, 1 + t.Pop / 800);
        if (w.SiegeTicks < need) return;
        int armyInv = attInv ? w.ArmyA : w.ArmyD, armyVic = attInv ? w.ArmyD : w.ArmyA;
        int p = Math.Clamp(52 + (int)((long)armyInv * 100 / Math.Max(1, armyVic) - 100) / 4 - (t.Kind >= SettlementKind.Town ? 10 : 0), 20, 90);
        if (!Pct(w.SiegeEvent.Value, S.Siege, p))
        {
            Ev("siege_lifted", $"The Siege of {t.Name} fails", $"The garrison of {t.Name} holds and {inv.Name} withdraws", t.Cell, [inv.Id, vic.Id, t.Id], Cz(CauseKind.War, w.SiegeEvent, "the defenders held the walls"));
            w.Score = Math.Clamp(w.Score - 5 * sgn, -100, 100);
            w.SiegeTicks = -1; w.Target = -1;
            return;
        }
        int ambition = _h.GetNotable(inv.Ruler)?.Ambition ?? 50;
        bool sack = Pct(w.SiegeEvent.Value, S.Siege, 22 + (100 - ambition) / 4 + (t.Pop < 200 ? 18 : 0));
        if (sack) Sack(w, inv, vic, t, attInv); else Capture(w, inv, vic, t, attInv);
        w.SiegeTicks = -1; w.Target = -1;
    }

    private void Sack(WarRecord w, RealmState inv, RealmState vic, Settlement t, bool attInv)
    {
        int ti = SIdx(t.Id);
        int loss = t.Pop * Roll(t.Id.Value, S.Siege, 45, 86) / 100;
        t.Pop -= loss;
        Devastate(t.Cell, 2, 30);
        bool razed = t.Pop < 15 || (t.Pop < 300 && Pct(t.Id.Value, S.Siege, 18));
        var ev = Ev("settlement_sacked", razed ? $"The Razing of {t.Name}" : $"The Sack of {t.Name}",
            razed ? $"{inv.Name}'s army burns {t.Name} to the ground; {Num(loss)} perish and none are left to rebuild" : $"{inv.Name}'s army storms {t.Name} in {vic.Name}; {Num(loss)} are slain and the town is plundered",
            t.Cell, [inv.Id, vic.Id, t.Id], Cz(CauseKind.War, w.SiegeEvent, "the town fell to storm"));
        t.LastBlow = ev; t.LastBlowTick = _tick;
        vic.LastLossEvent = ev; vic.Unrest = Math.Min(100, vic.Unrest + 10);
        inv.Treasury += loss * 2;
        w.Score = Math.Clamp(w.Score + (attInv ? 20 : -20), -100, 100);
        Op(inv.Index, vic.Index, -20);
        AddGrudge(vic.Index, inv.Index, 30, ev, $"the sack of {t.Name}");
        foreach (var n in _h.Notables)
            if (n.Alive && n.Role == NotableRole.Saint && n.Residence == ti && Pct(n.Id.Value, S.Saint, 55))
            {
                var mev = Ev("saint_martyred", $"Saint {n.Name} is martyred", $"Saint {n.Full} is cut down before the altar of {t.Name}", t.Cell, [n.Id, t.Id], Cz(CauseKind.War, ev, "slain in the sack"));
                MarkDead(n, "martyred in the sack", mev);
                Reliquary(n, mev);
            }
        foreach (var a in _h.Artefacts)
        {
            if (a.Status != ArtefactStatus.Held || a.Holder != t.Id) continue;
            int roll = Roll(a.Id.Value, S.Art, 0, 100);
            if (roll < 40) { a.Holder = inv.Id; a.Transfers++; Ev("artefact_captured", $"{a.Name} is carried off", $"{inv.Name}'s soldiers carry {a.Name} from {t.Name}", t.Cell, [a.Id, inv.Id], Cz(CauseKind.War, ev, "plunder")); }
            else if (roll < 75) ArtefactVanish(a, ArtefactStatus.Buried, t.Cell, ev, $"hidden by the priests of {t.Name} before the sack");
            else ArtefactVanish(a, ArtefactStatus.Lost, t.Cell, ev, $"lost in the burning of {t.Name}");
        }
        if (razed) MakeRuin(ti, "razed", ev, w.SiegeEvent);
    }

    private void Capture(WarRecord w, RealmState inv, RealmState vic, Settlement t, bool attInv)
    {
        int ti = SIdx(t.Id);
        bool wasCapital = vic.Capital == ti;
        int max = Math.Min(350, 30 + t.Pop / 6 + (wasCapital ? 70 : 0));
        var cells = Region(t.Cell, vic.Index, max);
        Transfer(cells, inv.Index);
        t.Realm = inv.Index;
        t.Pop = t.Pop * Roll(t.Id.Value, S.Siege, 75, 91) / 100;
        var ev = Ev("settlement_captured", $"The Fall of {t.Name}", $"{inv.Name} takes {t.Name} from {vic.Name}; some {Num(cells.Count * 4)} km2 of land change hands", t.Cell, [inv.Id, vic.Id, t.Id],
            Cz(CauseKind.War, w.SiegeEvent, w.CbNote));
        t.LastBlow = ev; t.LastBlowTick = _tick;
        vic.LastLossEvent = ev; vic.Unrest = Math.Min(100, vic.Unrest + 20);
        if (wasCapital) FixCapital(vic);
        w.Score = Math.Clamp(w.Score + (attInv ? 30 : -30), -100, 100);
        Op(inv.Index, vic.Index, -15);
        AddGrudge(vic.Index, inv.Index, 40, ev, $"the loss of {t.Name}");
        foreach (var a in _h.Artefacts)
        {
            if (a.Status != ArtefactStatus.Held || a.Holder != t.Id) continue;
            int roll = Roll(a.Id.Value, S.Art, 0, 100);
            if (roll < 55) { a.Holder = t.Id; a.Transfers++; }
            else ArtefactVanish(a, ArtefactStatus.Buried, t.Cell, ev, $"hidden by the priests of {t.Name} when the town fell");
        }
        var ruler = _h.GetNotable(vic.Ruler);
        if (wasCapital && ruler is { Alive: true } && Pct(ruler.Id.Value, S.Siege, 25)) Die(ruler, $"slain at the fall of {t.Name}", ev);
        if (vic.Cells <= 45 && vic.Alive && Pct(vic.Id.Value, S.Siege, 60)) MergeRealms(inv.Index, vic.Index, "conquest", ev);
    }

    private void PeaceCheck(WarRecord w)
    {
        int years = (_tick - w.StartTick) / 4;
        bool attWin = w.Score >= 60, defWin = w.Score <= -60;
        bool tired = w.WearyA + w.WearyD >= 170 || w.WearyA >= 110 || w.WearyD >= 110;
        bool drawn = years >= 12;
        if (!(attWin || defWin || tired || drawn) || _tick - w.StartTick < 3) return;
        if (!Pct(w.DeclEvent.Value + (ulong)_tick, S.War, 40)) return;
        var A = Rm(w.Attacker); var D = Rm(w.Defender);
        string outcome = w.Score >= 40 ? $"victory for {A.Name}" : w.Score <= -40 ? $"victory for {D.Name}" : "a stalemate";
        RealmState? winner = w.Score >= 40 ? A : w.Score <= -40 ? D : null;
        var loser = winner == null ? null : winner == A ? D : A;
        string place = D.Capital >= 0 ? _h.Settlements[D.Capital].Name : D.Name;
        string text = winner == null ? $"{w.Name} ends in a weary stalemate between {A.Name} and {D.Name}" : $"{w.Name} ends: {winner.Name} prevails over {loser!.Name}";
        if (winner != null && loser != null)
        {
            int tribute = loser.Treasury * 30 / 100; loser.Treasury -= tribute; winner.Treasury += tribute;
            text += $"; {Num(tribute)} in tribute is paid";
        }
        var ev = Ev("peace_signed", $"The Peace of {place}", text, Cap(D), [A.Id, D.Id], Cz(CauseKind.War, w.DeclEvent, $"the exhaustion of the {w.Name}"));
        int truceYears = Roll(w.DeclEvent.Value, S.War, _s.TruceYearsMin, _s.TruceYearsMax + 1);
        _truce[A.Index * _R + D.Index] = _truce[D.Index * _R + A.Index] = _tick + truceYears * 4;
        _h.Treaties.Add(new Treaty(A.Index, D.Index, TreatyKind.Truce, _tick, _tick + truceYears * 4, ev));
        if (winner != null && loser != null)
        {
            winner.WarsWon++; loser.WarsLost++; loser.Unrest = Math.Min(100, loser.Unrest + 10); winner.Unrest = Math.Max(0, winner.Unrest - 5);
            AddGrudge(loser.Index, winner.Index, 25 + Math.Abs(w.Score) / 3, ev, $"defeat in the {w.Name}");
        }
        Op(A.Index, D.Index, 10);
        w.Outcome = outcome; w.EndTick = _tick; w.EndEvent = ev;
        _atWar[A.Index] = _atWar[D.Index] = -1;
    }

    private void EndWar(WarRecord w, string outcome, EntityId ev)
    {
        if (!w.Alive) return;
        w.Outcome = outcome; w.EndTick = _tick; w.EndEvent = ev;
        if (_atWar[w.Attacker] == w.Index) _atWar[w.Attacker] = -1;
        if (_atWar[w.Defender] == w.Index) _atWar[w.Defender] = -1;
    }

    // ---------- realm lifecycle: falls, rebellions, coups, splits, mergers ----------
    private void LifecyclePass()
    {
        int nr = _h.Realms.Count;
        for (int a = 0; a < nr; a++)
        {
            var r = Rm(a);
            if (!r.Alive) continue;
            if (r.Cells <= 0) { FallRealm(r); continue; }
            var ruler = _h.GetNotable(r.Ruler);
            if (r.Cells >= 100 && !AtWar(a) && Chance(r.Id.Value, S.Rebel, _s.RebellionPpmTick * (40L + r.Unrest * 3) / 100)) Rebellion(r);
            if (!r.Alive) continue;
            if (ruler is { Alive: true } && Chance(r.Id.Value, S.Coup, _s.CoupPpmTick * (30L + r.Unrest * 2) / 100)) Coup(r, ruler);
        }
    }

    private void FallRealm(RealmState r)
    {
        var cause = r.LastLossEvent.Value != 0 ? r.LastLossEvent : r.LastDefeatEvent;
        var ev = Ev("realm_fell", $"The Fall of {r.Name}", $"The Realm of {r.Name} passes from the world; its last lands are lost", Cap(r), [r.Id],
            cause.Value != 0 ? Cz(CauseKind.War, cause, "its last territory was taken") : Cz(CauseKind.Other, EntityId.None, "dissolution after long decline"));
        EndRealm(r, "fell", ev);
    }

    private void EndRealm(RealmState r, string kind, EntityId ev)
    {
        r.Alive = false; r.EndTick = _tick; r.EndKind = kind; r.EndEvent = ev;
        for (int i = 0; i < _h.Wars.Count; i++)
        {
            var w = _h.Wars[i];
            if (w.Alive && (w.Attacker == r.Index || w.Defender == r.Index)) EndWar(w, $"{r.Name} ended", ev);
        }
        _atWar[r.Index] = -1;
        for (int b = 0; b < _R; b++) { _cb[r.Index * _R + b] = default; _cb[b * _R + r.Index] = default; }
        var d = _h.GetDynasty(r.Dynasty); if (d != null) d.Ruling = false;
        foreach (var n in _h.Notables) if (n.Alive && n.Realm == r.Index && n.Role == NotableRole.Ruler) n.Role = NotableRole.Noble;
    }

    private void MergeRealms(int winnerIdx, int loserIdx, string kind, EntityId causeEv)
    {
        var win = Rm(winnerIdx); var lose = Rm(loserIdx);
        if (!win.Alive || !lose.Alive || winnerIdx == loserIdx) return;
        var cells = new List<int>();
        for (int i = 0; i < A; i++) if (_owner[i] == loserIdx) cells.Add(i);
        Transfer(cells, winnerIdx);
        win.Treasury += lose.Treasury; lose.Treasury = 0;
        var oldRuler = _h.GetNotable(lose.Ruler);
        string text = kind == "marriage"
            ? $"The crowns of {lose.Name} and {win.Name} are united by marriage; {Num(cells.Count * 4)} km2 pass under one rule"
            : $"{win.Name} annexes the broken remnants of {lose.Name}";
        var ev = Ev("realm_merged", kind == "marriage" ? $"{lose.Name} joins {win.Name}" : $"{lose.Name} is annexed", text, Cap(win), [win.Id, lose.Id],
            Cz(kind == "marriage" ? CauseKind.Law : CauseKind.War, causeEv, kind == "marriage" ? "a royal marriage and a dying line" : "conquest after the fall of its capital"));
        foreach (var n in _h.Notables) if (n.Alive && n.Realm == loserIdx) { n.Realm = winnerIdx; if (n.Role == NotableRole.Heir) n.Role = NotableRole.Noble; }
        foreach (var a in _h.Artefacts)
            if (a.Status == ArtefactStatus.Held && (a.Holder == lose.Id || (oldRuler != null && a.Holder == oldRuler.Id))) { a.Holder = win.Id; a.Transfers++; }
        for (int b = 0; b < _h.Realms.Count; b++) _opinion[winnerIdx * _R + b] = (_opinion[winnerIdx * _R + b] + _opinion[loserIdx * _R + b]) / 2;
        EndRealm(lose, kind == "marriage" ? "merged by marriage" : "annexed", ev);
        lose.Cells = 0;
    }

    private string NewRealmName(int culture, ulong salt)
    {
        var pool = _c.Settings.RealmNames;
        if (pool.Count > 0)
        {
            int start = Roll(salt, S.Rebel, 0, pool.Count);
            for (int k = 0; k < pool.Count; k++)
            {
                string nm = pool[(start + k) % pool.Count];
                if (!_h.Realms.Any(r => r.Name == nm)) return nm;
            }
        }
        var rs = Rs(salt, S.Name);
        for (int k = 0; k < 8; k++)
        {
            string nm = _names.Place(culture, ref rs);
            if (!_h.Realms.Any(r => r.Name == nm)) return nm;
        }
        return $"{_names.Place(culture, ref rs)}{_h.Realms.Count}";
    }

    private bool SplitRealm(RealmState parent, Notable leader, EntityId causeEv, string why)
    {
        if (parent.Cells < 80 || _h.Realms.Count >= _R - 1 || _h.Realms.Count(r => r.Alive) >= _s.MaxRealms) return false;
        int pick = -1, bp = -1, cap = parent.Capital;
        for (int i = 0; i < _h.Settlements.Count; i++)
        {
            var s = _h.Settlements[i];
            if (s.IsRuin || s.Realm != parent.Index || i == cap) continue;
            int d = cap >= 0 ? Dist(s.Cell, _h.Settlements[cap].Cell) : 20;
            if (d < 6) continue;
            if (s.Pop > bp) { bp = s.Pop; pick = i; }
        }
        if (pick < 0) return false;
        var seat = _h.Settlements[pick];
        var cells = Region(seat.Cell, parent.Index, parent.Cells * Roll(parent.Id.Value, S.Rebel, 25, 46) / 100);
        if (cells.Count < 20) return false;
        int cu = _c.CultureId[seat.Cell] < 8 ? _c.CultureId[seat.Cell] : parent.CultureId;
        var nr = new RealmState
        {
            Index = _h.Realms.Count, Id = _ids.Allocate(IdKind.Faction), Name = NewRealmName(cu, leader.Id.Value), CultureId = cu, Capital = pick,
            Parent = parent.Index, FoundedTick = _tick,
        };
        _h.Realms.Add(nr);
        int share = (int)((long)parent.Treasury * cells.Count / Math.Max(1, parent.Cells));
        Transfer(cells, nr.Index);
        parent.Treasury -= share; nr.Treasury = share;
        leader.Role = NotableRole.Ruler; leader.Realm = nr.Index; leader.Residence = pick;
        if (leader.Dynasty.Value == 0) { var d = NewDynasty(cu, leader.Id, _year); leader.Dynasty = d.Id; }
        nr.Ruler = leader.Id; nr.Dynasty = leader.Dynasty;
        var dyn = _h.GetDynasty(leader.Dynasty); if (dyn != null) dyn.Ruling = true;
        foreach (var n in _h.Notables) if (n.Alive && (n.Father == leader.Id || n.Mother == leader.Id || n.Spouse == leader.Id)) n.Realm = nr.Index;
        for (int b = 0; b < _h.Realms.Count; b++) { _opinion[nr.Index * _R + b] = _opinion[parent.Index * _R + b]; _opinion[b * _R + nr.Index] = _opinion[b * _R + parent.Index]; }
        _opinion[nr.Index * _R + parent.Index] = _opinion[parent.Index * _R + nr.Index] = -50;
        var ev = Ev("realm_split", $"{parent.Name} splits", $"{Who(leader)} of {HouseOf(leader)} breaks away from {parent.Name} and founds the Realm of {nr.Name} around {seat.Name} ({Num(cells.Count * 4)} km2)", seat.Cell,
            [parent.Id, nr.Id, leader.Id, seat.Id], Cz(CauseKind.Other, causeEv, why));
        nr.FoundedEvent = ev;
        parent.Unrest = Math.Min(100, parent.Unrest + 10);
        AddGrudge(parent.Index, nr.Index, 40, ev, $"the loss of the {nr.Name} provinces");
        SetCb(parent.Index, nr.Index, CasusBelliKind.BorderClaim, ev, $"{parent.Name} refuses to recognise the breakaway {nr.Name}", $"border claim over the rebel provinces of {nr.Name}");
        return true;
    }

    private void Rebellion(RealmState r)
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
        var rebel = NewNotable(NotableRole.Rebel, cu, r.Index, dyn.Id, Pct(seat.Id.Value, S.Rebel, 15), _year - Roll(seat.Id.Value, S.Rebel, 25, 50), EntityId.None, EntityId.None);
        dyn.Founder = rebel.Id; rebel.Residence = pick; rebel.Ambition = 70 + Roll(seat.Id.Value, S.Rebel, 0, 30);
        Cause cz;
        string note;
        if (seat.LastBlow.Value != 0 && _tick - seat.LastBlowTick < 48) { note = "anger after the disaster at " + seat.Name; cz = Cz(CauseKind.Other, seat.LastBlow, note); }
        else if (r.LastDefeatEvent.Value != 0 && _tick - r.EndTick < 400) { note = "contempt for a ruler who lost battles"; cz = Cz(CauseKind.War, r.LastDefeatEvent, note); }
        else { note = "heavy taxes and a hated steward"; cz = Cz(CauseKind.Law, EntityId.None, note); }
        var rising = Ev("rebellion", $"Rebellion in {seat.Name}", $"{rebel.Full} raises the banner of revolt against {(_h.GetNotable(r.Ruler) is { } k ? Who(k) : r.Name)} in {seat.Name}", seat.Cell, [rebel.Id, seat.Id, r.Id], cz);
        r.Unrest = Math.Min(100, r.Unrest + 8);
        if (Pct(rebel.Id.Value, S.Rebel, 45) && SplitRealm(r, rebel, rising, "a successful rebellion")) return;
        var ev = Ev("rebellion_crushed", $"The Rising at {seat.Name} is crushed", $"{rebel.Full}'s rebels are broken by the army of {r.Name}; the leader is hanged and {seat.Name} is made to pay", seat.Cell, [rebel.Id, seat.Id, r.Id], Cz(CauseKind.War, rising, "the rebellion's defeat"));
        seat.Pop -= seat.Pop * 12 / 100;
        MarkDead(rebel, "hanged for rebellion", ev);
        r.Unrest = Math.Max(0, r.Unrest - 20);
    }

    private void Coup(RealmState r, Notable ruler)
    {
        Notable? g = null;
        foreach (var n in _h.Notables)
            if (n.Alive && n.Realm == r.Index && n.Role == NotableRole.General && (n.Victories >= 1 || n.Ambition >= 70) && n.Ambition >= 50) { g = n; break; }
        if (g == null) return;
        string note = r.Unrest > 40 ? "a ruler who had lost the loyalty of the army" : "the ambition of a victorious general";
        var ev = Ev("coup", $"The Betrayal of {ruler.Name}", $"{Who(g)} turns the guard against {Who(ruler)} and takes the crown of {r.Name}", Cap(r), [g.Id, ruler.Id, r.Id], Cz(CauseKind.Crime, r.LastDefeatEvent, note));
        foreach (var a in _h.Artefacts)
            if (a.Status == ArtefactStatus.Held && a.Holder == ruler.Id) { a.Holder = g.Id; a.Transfers++; }
        MarkDead(ruler, $"murdered by {g.Name}", ev);
        var d = NewDynasty(g.Culture, g.Id, _year);
        g.Dynasty = d.Id;
        Coronate(r, g, ruler, ev, "seizes");
        r.Unrest = Math.Min(100, r.Unrest + 10);
    }
}
