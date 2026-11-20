using Ravensguard.Sim.Core;
using Ravensguard.Sim.Politics;

namespace Ravensguard.Sim.WorldGen.History;

/// <summary>
/// History v1, faith (history-recalibration 4.2): a religion per culture founded at year 0, heresies after a famine or a plague when a realm of the faith is aggrieved, excommunications of rulers (a casus belli for
/// neighbours of the faith), and church land grants. Schisms (a heresy held for twenty years becoming a faith) are the overview's cut item 5 and are not built: a heresy that outlasts its years is suppressed or
/// reconciled. The legends' <see cref="WorldHistory.Heresies"/> and <see cref="WorldHistory.Excommunications"/> are the small hook the live faith politics read.
/// </summary>
public sealed partial class HistoryGenerator
{
    private HistoryCultureRule? RuleOf(int culture) => _s.CultureRules != null && culture >= 0 && culture < _s.CultureRules.Count ? _s.CultureRules[culture] : null;
    private static string FaithKey(HistoryCultureRule? r) => r == null || r.ReligionId.Length == 0 ? "ch_h_rel_local" : "ch_h_rel_" + r.ReligionId;
    private ChronParam FaithParam(RealmState r) => ChronParam.TextKey("faith", FaithKey(RuleOf(FaithOf(r))));

    private void SetupFaithV1()
    {
        int nc = Math.Min(8, _c.Cultures.Count);
        for (int c = 0; c < nc; c++)
        {
            var rule = RuleOf(c);
            // the holy site: the greatest settlement of the culture (the capital of its first realm when none)
            Settlement? site = null;
            foreach (var s in _h.Settlements) if (!s.IsRuin && _c.CultureId[s.Cell] == c && (site == null || s.Pop > site.Pop)) site = s;
            if (site == null) foreach (var r in _h.Realms) if (r.CultureId == c && r.Capital >= 0) { site = _h.Settlements[r.Capital]; break; }
            var rel = new HistoryReligion { Index = c, Name = rule is { Religion.Length: > 0 } ? rule.Religion : $"The Faith of the {Culture(c)}", Culture = c, FoundedTick = 0, HolySite = site?.Id ?? EntityId.None };
            if (site != null)
                rel.FoundedEvent = EvK("h_religion_founded", "ch_h_religion_founded", [ChronParam.TextKey("faith", FaithKey(rule)), ChronParam.Place("place", site.Id)], site.Cell, [site.Id],
                    CzK(CauseKind.Faith, EntityId.None, "ch_note_h_faith_ancient"));
            // the faith's founder-saint, alive at the holy site when the records open (the derived religion adds its founders to the history's saints)
            if (site != null && site.Realm >= 0)
            {
                bool female = Pct(site.Id.Value, S.Faith, 50);
                var sa = NewNotable(NotableRole.Saint, c, site.Realm, EntityId.None, female, _year - YearsAgo(Roll(site.Id.Value, S.Faith, 30, 61)), EntityId.None, EntityId.None);
                sa.Residence = _h.Settlements.IndexOf(site);
                sa.Epithet = SaintEpithets[Roll(site.Id.Value, S.Faith, 0, SaintEpithets.Length)];
                rel.Founder = sa.Id;
                sa.RiseEvent = Ev("saint_rises", $"Saint {sa.Name} of {site.Name}", $"{sa.Full} keeps the first shrine of the faith at {site.Name}, and the people call {(female ? "her" : "him")} holy", site.Cell, [sa.Id, site.Id],
                    Cz(CauseKind.Faith, rel.FoundedEvent, "the founding of the faith"));
            }
            _h.Religions.Add(rel);
        }
        foreach (var r in _h.Realms) r.Religion = Math.Clamp(r.CultureId, 0, Math.Max(0, nc - 1));
    }

    /// <summary>Winter: heresies, excommunications, abbeys and abolitions for the year (each its own stream and key, so v0 never meets them).</summary>
    private void FaithPassV1()
    {
        if (_season != 3) return;
        int nr = _h.Realms.Count;
        // a heresy runs its years, or ends when its realm does
        foreach (var hz in _h.Heresies)
        {
            if (!hz.Alive) continue;
            var r = Rm(hz.Realm);
            bool over = !r.Alive || _tick - hz.StartTick >= HistoryRules.HeresyYearsMax * 4 || Pct(hz.Event.Value + (ulong)_tick, S.Heresy, 6);
            if (!over) continue;
            hz.EndTick = _tick; _heresyOf[hz.Realm] = -1;
            if (r.Alive)
                EvK("h_heresy_ends", "ch_h_heresy_ends", [FaithParam(r), PlaceOf(r)], Cap(r), [r.Id], CzK(CauseKind.Faith, hz.Event, "ch_note_h_heresy_run"));
        }
        // a new heresy after a famine or a plague, in an aggrieved realm of the faith
        for (int c = 0; c < _h.Religions.Count && c < 8; c++)
        {
            if (_hitEv[c].Value == 0 || _tick - _hitTick[c] > 8 || _h.Heresies.Any(x => x.Alive && x.Religion == c)) continue;
            int pick = -1, best = -1;
            for (int a = 0; a < nr; a++)
            {
                var r = Rm(a);
                if (!r.Alive || FaithOf(r) != c || _heresyOf[a] >= 0 || r.Ruler.Value == 0) continue;
                int pm = RealmUnrestPm(r);
                if (pm >= HistoryRules.Unrest.Rung1 && pm > best) { best = pm; pick = a; }
            }
            if (pick < 0 || !Chance((ulong)c + 1, S.Heresy, HistoryRules.HeresyPermilleYear * 1000L)) continue;
            var realm = Rm(pick);
            var ev = EvK("h_heresy_arises", "ch_h_heresy_arises", [FaithParam(realm), PlaceOf(realm), RulerParam(realm)], Cap(realm), [realm.Id],
                CzK(_hitPlague[c] ? CauseKind.Disease : CauseKind.Famine, _hitEv[c], _hitPlague[c] ? "ch_note_h_heresy_plague" : "ch_note_h_heresy_famine"));
            _h.Heresies.Add(new Heresy { Religion = c, Realm = pick, StartTick = _tick, Event = ev, CauseEvent = _hitEv[c] });
            _heresyOf[pick] = c;
        }
        for (int a = 0; a < nr; a++)
        {
            var r = Rm(a);
            if (!r.Alive || r.Ruler.Value == 0 || _h.GetNotable(r.Ruler) is not { Alive: true } ruler) continue;
            // excommunication of a ruler: a heretic, or an ambitious one at war with a realm of his own faith
            if (!Excommunicated(a))
            {
                EntityId cause = EntityId.None; string key = "";
                if (_heresyOf[a] >= 0 && Pct(r.Id.Value, S.Faith, HistoryRules.ExcommunicationPctYear)) { cause = _h.Heresies.Last(x => x.Realm == a && x.Alive).Event; key = "ch_note_h_heresy_ruler"; }
                else if (_atWar[a] >= 0 && ruler.Ambition >= 85)
                {
                    var w = _h.Wars[_atWar[a]]; int other = w.Attacker == a ? w.Defender : w.Attacker;
                    if (FaithOf(Rm(other)) == FaithOf(r) && Pct(r.Id.Value, S.Faith, HistoryRules.ExcommunicationWarPctYear)) { cause = w.DeclEvent; key = "ch_note_h_war_on_kin"; }
                }
                if (key.Length > 0) Excommunicate(r, ruler, cause, key);
            }
            var traits = LordTraits.OfRuler(_seed, ruler.Id.Value, ruler.Ambition);
            if (LordTraits.Has(traits, LordTrait.Pious) && !Excommunicated(a))
            {
                if (Chance(r.Id.Value, S.Faith, HistoryRules.AbbeyPermilleYear * 1000L)) AbbeyGrant(r, ruler);
                var rule = RuleOf(r.CultureId);
                if (rule != null && rule.Stance == "forbid" && rule.Slavery != "none" && !_abolished[Math.Clamp(r.CultureId, 0, 7)] && Chance(r.Id.Value, S.Slavery, HistoryRules.AbolitionPermilleYear * 1000L))
                {
                    _abolished[Math.Clamp(r.CultureId, 0, 7)] = true;
                    EvK("h_abolition", "ch_h_abolition", [RulerParam(r), PlaceOf(r), FaithParam(r)], Cap(r), [r.Id, ruler.Id], CzK(CauseKind.Faith, EntityId.None, "ch_note_h_piety"));
                }
            }
        }
    }

    private void Excommunicate(RealmState r, Notable ruler, EntityId cause, string key)
    {
        var ev = EvK("h_excommunication", "ch_h_excommunication", [RulerParam(r), PlaceOf(r), FaithParam(r)], Cap(r), [ruler.Id, r.Id], CzK(CauseKind.Faith, cause, key));
        _excommUntil[r.Index] = _tick + HistoryRules.ExcommunicationYears * 4; _excommRuler[r.Index] = ruler.Id;
        var exc = new Excommunication { Realm = r.Index, Tick = _tick, Ruler = ruler.Id, Event = ev };
        _h.Excommunications.Add(exc);
        // neighbours of the faith may use it as a casus belli
        for (int x = 0; x < _h.Realms.Count; x++)
        {
            var rx = Rm(x);
            if (x == r.Index || !rx.Alive || _heresyOf[x] >= 0 || Contact(x, r.Index) == 0 || rx.Ruler.Value == 0) continue;
            // a realm of the faith, or any realm whose ruler is pious (the ban is a call to arms for the devout of other creeds too)
            if (FaithOf(rx) != FaithOf(r) && !(_h.GetNotable(rx.Ruler) is { } rn && LordTraits.Has(LordTraits.OfRuler(_seed, rn.Id.Value, rn.Ambition), LordTrait.Pious))) continue;
            if (_cb[x * _R + r.Index].Kind != CasusBelliKind.None && _cb[x * _R + r.Index].Kind != CasusBelliKind.Insult) continue;
            PutCb(x, r.Index, CasusBelliKind.Excommunication, ev, "excommunication");
            exc.Given++;
        }
    }

    private void AbbeyGrant(RealmState r, Notable ruler)
    {
        int pick = -1, bp = int.MaxValue;
        for (int i = 0; i < _h.Settlements.Count; i++)
        {
            var s = _h.Settlements[i];
            if (s.IsRuin || s.Realm != r.Index || !s.Minor || s.Kind > SettlementKind.Village || s.Name.EndsWith(" Abbey", StringComparison.Ordinal)) continue;
            if (s.Pop < bp) { bp = s.Pop; pick = i; }
        }
        if (pick < 0) return;
        var site = _h.Settlements[pick];
        _usedNames.Remove(site.Name);
        site.Name = _usedNames.Add(site.Name + " Abbey") ? site.Name + " Abbey" : site.Name + " Priory";
        _usedNames.Add(site.Name);
        site.Pop += 24;
        EvK("h_abbey_founded", "ch_h_abbey_founded", [RulerParam(r), ChronParam.Place("place", site.Id), FaithParam(r)], site.Cell, [site.Id, r.Id, ruler.Id], CzK(CauseKind.Faith, EntityId.None, "ch_note_h_piety"));
    }

    // ---------- law: the culture's punishment for an outlaw taken ----------
    private EntityId OutlawDealtV1(Notable o, Settlement lair)
    {
        var rule = RuleOf(o.Culture);
        string pun = rule == null || rule.Crime.Length == 0 ? "hanging" : rule.Crime;
        return EvK("h_outlaw_dealt", "ch_h_outlaw_dealt", [ChronParam.Person("outlaw", o.Id), ChronParam.Place("place", lair.Id), ChronParam.TextKey("punishment", "ch_h_pun_" + pun)], lair.Cell, [o.Id],
            CzK(CauseKind.Justice, o.RiseEvent, "ch_note_h_law_dealt"));
    }

    // ---------- bondage: slave raids by chattel and war-captive realms on weaker neighbours ----------
    private void SocietyPassV1()
    {
        if (_season != 1) return;
        int nr = _h.Realms.Count;
        for (int a = 0; a < nr; a++)
        {
            var ra = Rm(a);
            var rule = RuleOf(ra.CultureId);
            if (!ra.Alive || rule == null || (rule.Slavery != "chattel" && rule.Slavery != "war_captives") || rule.Stance == "forbid" || _abolished[Math.Clamp(ra.CultureId, 0, 7)] || ra.Ruler.Value == 0) continue;
            for (int b = 0; b < nr; b++)
            {
                var rb = Rm(b);
                if (b == a || !rb.Alive || Contact(a, b) == 0 || _alliance[a * _R + b] > _tick || VassalBond(a, b)) continue;
                if (Levy(rb) * 100 >= Levy(ra) * HistoryRules.RaidWeakerPct) continue;
                bool atWarWith = _atWar[a] >= 0 && _atWar[a] == _atWar[b];
                if (rule.Slavery == "war_captives" && !atWarWith) continue;
                if (!Pct((ulong)(a * _R + b), S.Slavery, HistoryRules.RaidPctYear * (atWarWith ? 2 : 1))) continue;
                int si = _border[b * _R + a] > 0 ? BorderSettlement(b, a) : CoastSettlement(b, a);
                if (si < 0) continue;
                var s = _h.Settlements[si];
                int take = Math.Min(s.Pop / 2, Math.Max(4, s.Pop * HistoryRules.RaidTakesPct / 100));
                if (take <= 0) continue;
                s.Pop -= take; s.LastBlowTick = _tick;
                var cause = atWarWith ? CzK(CauseKind.Slavery, _h.Wars[_atWar[a]].DeclEvent, "ch_note_h_captives_of_war") : CzK(CauseKind.Slavery, EntityId.None, "ch_note_h_weak_neighbour");
                var ev = EvK("h_slave_raid", "ch_h_slave_raid", [RulerParam(ra, "raider"), ChronParam.Place("place", s.Id), ChronParam.Count("count", take)], s.Cell, [s.Id, ra.Id, rb.Id], cause);
                s.LastBlow = ev;
                Settlement? market = null;
                foreach (var m in _h.Settlements) if (!m.IsRuin && m.Realm == a && m.Kind >= SettlementKind.Town && (market == null || m.Pop > market.Pop)) market = m;
                var sold = market == null ? EntityId.None : EvK("h_slaves_sold", "ch_h_slaves_sold", [ChronParam.Count("count", take), ChronParam.Place("place", market.Id)], market.Cell, [market.Id, ra.Id],
                    CzK(CauseKind.Slavery, ev, "ch_note_h_market"));
                _h.SlaveRaids.Add(new SlaveRaid { Raider = a, Victim = b, Captives = take, Tick = _tick, Event = ev, MarketEvent = sold, Market = market == null ? -1 : _h.Settlements.IndexOf(market) });
                AddGrudge(b, a, 20, ev, "slave raid");
                if (_cb[b * _R + a].Kind == CasusBelliKind.None) PutCb(b, a, CasusBelliKind.Raid, ev, "slave raid");
                break;
            }
        }
    }
}
