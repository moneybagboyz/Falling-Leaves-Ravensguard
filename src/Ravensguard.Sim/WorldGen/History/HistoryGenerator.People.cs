using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.WorldGen.History;

public sealed partial class HistoryGenerator
{
    private readonly EntityId[] _general;

    private static readonly string[] SaintEpithets = ["the Merciful", "the Healer", "the Faithful", "the Almsgiver", "the Comforter"];
    private static readonly string[] ArtPrefix = ["Dawn", "Night", "Grey", "Red", "Winter", "Oath", "Wolf", "Raven", "Storm", "Iron", "Pale", "Ash", "Bone", "Grim", "Widow", "Frost"];
    private static readonly string[][] ArtSuffix =
    [
        ["fang", "bane", "song", "edge", "brand", "biter", "cleaver"],   // sword
        ["render", "breaker", "reaper", "maul"],                           // axe
        ["thorn", "piercer", "wing", "sting"],                             // spear
        ["fall", "knell", "breaker", "hammer"],                            // warhammer
        ["string", "whisper", "wing", "sorrow"],                           // bow
        ["wall", "ward", "bulwark", "hold"],                               // shield
    ];
    private static readonly string[] ArtKinds = ["sword", "axe", "spear", "warhammer", "bow", "shield"];
    private static readonly string[] Trades = ["smith", "armourer", "bowyer", "goldsmith", "swordsmith"];

    private int Pace => _h.AgingPace;
    /// <summary>Years of age at <c>aging_pace</c>: calendar years since birth times the pace (the yearly hazards below stay per calendar year).</summary>
    private int AgeAt(Notable n) => (_year - n.BirthYear) * Pace;
    private int AgeOf(Notable n) => ((n.Alive ? _year : n.DeathYear) - n.BirthYear) * Pace;
    /// <summary>The birth-year offset of someone <paramref name="ageYears"/> old now.</summary>
    private int YearsAgo(int ageYears) => ageYears / Pace;

    // ---------- yearly and per-tick notable simulation ----------
    private void NotablesPass()
    {
        int n = _h.Notables.Count;
        for (int i = 0; i < n; i++)
        {
            var nb = _h.Notables[i];
            if (!nb.Alive) continue;
            int age = AgeAt(nb);
            int hz = age < 1 ? 200 : age < 5 ? 30 : age < 15 ? 8 : age < 50 ? 12 : age < 65 ? 30 : 100;
            if (age >= (_v1 ? 100 : 95) || Chance(nb.Id.Value, S.Death, _v1 ? NotableDeathPpmV1(age) : hz * 250L))
            {
                string why = age >= 60 ? "old age" : age < 5 ? "a childhood fever" : Roll(nb.Id.Value, S.Death, 0, 4) switch { 0 => "a wasting sickness", 1 => "a fever", 2 => "a fall from a horse", _ => "an infected wound" };
                Die(nb, why, EntityId.None);
            }
        }
        if (_season == 0)
        {
            Births(n); Marriages(n); CrafterSpawn(); ForgePass(n); ArtefactDrift();
        }
        OutlawPass(n);
    }

    // ---------- death and succession ----------
    private void Die(Notable n, string cause, EntityId causeEv)
    {
        if (!n.Alive) return;
        int age = AgeAt(n);
        RealmState? r = n.Realm >= 0 && n.Realm < _h.Realms.Count ? Rm(n.Realm) : null;
        var cz = Cz(CauseKind.Death, causeEv, cause);
        EntityId ev = EntityId.None;
        switch (n.Role)
        {
            case NotableRole.Ruler when r != null && r.Ruler == n.Id:
                n.Epithet = Epithet(n, r);
                ev = Ev("ruler_died", $"Death of {n.Full}", $"{Who(n)} of {HouseOf(n)} dies of {cause} at {age}; {r.Name} mourns", Cap(r), [n.Id, r.Id], cz);
                MarkDead(n, cause, ev);
                OnRulerDeath(r, n, ev);
                return;
            case NotableRole.Heir when age >= 16 && r != null:
                ev = Ev("heir_died", $"{n.Name} of {HouseOf(n)} dies", $"{(n.Female ? "Princess" : "Prince")} {n.Name}, heir of {r.Name}, dies of {cause} at {age}", Cap(r), [n.Id, r.Id], cz);
                break;
            case NotableRole.Saint:
                ev = Ev("saint_died", $"Saint {n.Name} dies", $"Saint {n.Full} dies of {cause}; pilgrims carry the body to a shrine", n.Residence >= 0 ? _h.Settlements[n.Residence].Cell : 0, [n.Id], cz);
                MarkDead(n, cause, ev);
                Reliquary(n, ev);
                return;
        }
        if (n.Role == NotableRole.Ruler) n.Role = NotableRole.Noble;
        MarkDead(n, cause, ev);
    }

    private int Cap(RealmState r) => r.Capital >= 0 ? _h.Settlements[r.Capital].Cell : _c.Realms[Math.Min(r.Index, _c.Realms.Count - 1)].SeedCell;

    private void MarkDead(Notable n, string cause, EntityId ev)
    {
        n.DeathYear = Math.Max(_year, n.BirthYear); n.DeathCause = cause; n.DeathEvent = ev;
        if (n.Role == NotableRole.Ruler) { }
        foreach (var a in _h.Artefacts)
        {
            if (a.Status != ArtefactStatus.Held || a.Holder != n.Id || n.Role == NotableRole.Ruler) continue;
            var r = n.Realm >= 0 ? Rm(n.Realm) : null;
            if (ev.Value != 0 && Pct(a.Id.Value, S.Art, 25)) { ArtefactVanish(a, ArtefactStatus.Buried, r != null ? Cap(r) : 0, ev, $"buried with {n.Full}"); continue; }
            if (r != null && r.Alive)
            {
                a.Holder = r.Ruler.Value != 0 && _h.GetNotable(r.Ruler)?.Alive == true ? r.Ruler : r.Id; a.Transfers++;
                if (ev.Value != 0)
                    Ev("artefact_inherited", $"{a.Name} passes to the crown", $"On the death of {n.Full}, {a.Name} returns to the keeping of {r.Name}", Cap(r), [a.Id, n.Id, r.Id], Cz(CauseKind.Death, ev, "the holder's death"));
            }
            else ArtefactVanish(a, ArtefactStatus.Lost, r != null ? Cap(r) : 0, ev, $"lost when {n.Full} died far from home");
        }
    }

    private string Epithet(Notable n, RealmState r)
    {
        int age = AgeAt(n);
        if (n.Victories >= 5) return "the Conqueror";
        if (n.Victories >= 3) return "the Victorious";
        if (_tick - r.LastFamineTick < 24 && Pct(n.Id.Value, S.Misc, 60)) return "the Hungry";
        if (r.WarsLost > r.WarsWon + 1) return "the Unlucky";
        if (age >= 68) return "the Old";
        if (age < 30) return "the Young";
        if (n.Ambition >= 82) return "the Ambitious";
        if (n.Ambition <= 22) return "the Meek";
        return Pct(n.Id.Value, S.Misc, 40) ? (n.Skill > 65 ? "the Wise" : "the Stern") : "";
    }

    private Notable? FindHeir(RealmState r, Notable dead, out Notable? rival)
    {
        var kids = new List<Notable>(); var sibs = new List<Notable>(); var others = new List<Notable>();
        foreach (var n in _h.Notables)
        {
            if (!n.Alive || n.Id == dead.Id || n.Realm != r.Index || n.Role is NotableRole.Ruler or NotableRole.General or NotableRole.Rebel or NotableRole.Outlaw or NotableRole.Saint or NotableRole.MasterCrafter) continue;
            if (n.Father == dead.Id || n.Mother == dead.Id) kids.Add(n);
            else if (dead.Father.Value != 0 && n.Father == dead.Father && n.Realm == r.Index) sibs.Add(n);
            else if (n.Dynasty == dead.Dynasty && n.Realm == r.Index && AgeAt(n) >= 16) others.Add(n);
        }
        int Order(Notable n) => (n.Female ? 1000 : 0) + (n.BirthYear + 500);
        kids.Sort((a, b) => Order(a) != Order(b) ? Order(a).CompareTo(Order(b)) : a.Id.CompareTo(b.Id));
        sibs.Sort((a, b) => Order(a) != Order(b) ? Order(a).CompareTo(Order(b)) : a.Id.CompareTo(b.Id));
        others.Sort((a, b) => a.BirthYear != b.BirthYear ? a.BirthYear.CompareTo(b.BirthYear) : a.Id.CompareTo(b.Id));
        var all = new List<Notable>(); all.AddRange(kids); all.AddRange(sibs); all.AddRange(others);
        rival = null;
        if (all.Count == 0) return null;
        for (int i = 1; i < all.Count; i++)
            if (all[i].Ambition >= 55 && AgeAt(all[i]) >= 16 && !all[i].Female) { rival = all[i]; break; }
        return all[0];
    }

    private void OnRulerDeath(RealmState r, Notable dead, EntityId deathEv)
    {
        var heir = FindHeir(r, dead, out var rival);
        if (heir == null) { NoHeir(r, dead, deathEv); return; }
        var succ = Coronate(r, heir, dead, deathEv, "succeeds");
        // heirlooms: some are buried with the ruler, the rest pass down
        foreach (var a in _h.Artefacts)
        {
            if (a.Status != ArtefactStatus.Held || a.Holder != dead.Id) continue;
            if (Pct(a.Id.Value, S.Art, 22)) ArtefactVanish(a, ArtefactStatus.Buried, Cap(r), deathEv, $"buried with {dead.Full} in the royal barrow near {(r.Capital >= 0 ? _h.Settlements[r.Capital].Name : r.Name)}");
            else
            {
                a.Holder = heir.Id; a.Transfers++;
                Ev("artefact_inherited", $"{heir.Name} inherits {a.Name}", $"{a.Name} passes from {dead.Full} to {heir.Full}", Cap(r), [a.Id, dead.Id, heir.Id], Cz(CauseKind.Death, succ, "inheritance"));
            }
        }
        int age = AgeAt(heir);
        if (rival != null && Pct(dead.Id.Value, S.Succ, 10 + (age < 16 ? 15 : 0) + rival.Ambition / 10)) SuccessionCrisis(r, heir, rival, succ);
    }

    private EntityId Coronate(RealmState r, Notable heir, Notable dead, EntityId causeEv, string verb)
    {
        int age = AgeAt(heir);
        heir.Role = NotableRole.Ruler; heir.Realm = r.Index; heir.Residence = r.Capital;
        r.Ruler = heir.Id; r.Dynasty = heir.Dynasty;
        var d = _h.GetDynasty(heir.Dynasty); if (d != null) d.Ruling = true;
        foreach (var n in _h.Notables)
            if (n.Alive && n.Role == NotableRole.Heir && n.Realm == r.Index && n.Id != heir.Id && n.Father != heir.Id && n.Mother != heir.Id) n.Role = NotableRole.Noble;
        string rel = heir.Father == dead.Id || heir.Mother == dead.Id ? (heir.Female ? "daughter" : "son") : heir.Father.Value != 0 && heir.Father == dead.Father ? (heir.Female ? "sister" : "brother") : "kinsman";
        string minor = age < 16 ? $" (a child of {age}; a regent rules)" : "";
        if (age < 16) r.Unrest = Math.Min(100, r.Unrest + 8);
        var ev = Ev("succession", $"{heir.Name} takes the crown of {r.Name}", $"{Who(heir)} of {HouseOf(heir)}, {rel} of {dead.Name}, {verb} to the throne{minor}", Cap(r), [heir.Id, dead.Id, r.Id],
            Cz(CauseKind.Law, causeEv, "the law of succession after the ruler's death"));
        // kin of the dead ruler in other realms may press a claim
        for (int a = 0; a < _h.Realms.Count; a++)
        {
            var ra = Rm(a);
            if (a == r.Index || !ra.Alive || _border[a * _R + r.Index] == 0 || ra.Ruler.Value == 0) continue;
            var rul = _h.GetNotable(ra.Ruler);
            var sp = rul != null ? _h.GetNotable(rul.Spouse) : null;
            if (sp != null && sp.Dynasty == dead.Dynasty && sp.Alive && _cb[a * _R + r.Index].Kind == CasusBelliKind.None)
                SetCb(a, r.Index, CasusBelliKind.Succession, ev, $"{Who(rul!)} claims the crown of {r.Name} through his {(sp.Female ? "wife" : "husband")} of {HouseOf(sp)}", "claim on the succession");
        }
        return ev;
    }

    private void SuccessionCrisis(RealmState r, Notable heir, Notable rival, EntityId succ)
    {
        var crisis = Ev("succession_crisis", $"The Succession Crisis of {r.Name}", $"{Who(rival)} disputes the right of {heir.Full} to rule {r.Name}", Cap(r), [rival.Id, heir.Id, r.Id],
            Cz(CauseKind.Law, succ, "a disputed succession"));
        r.Unrest = Math.Min(100, r.Unrest + 15);
        int roll = Roll(heir.Id.Value, S.Succ, 0, 100);
        if (roll < 35)
        {
            var ev = Ev("usurpation", $"{rival.Name} seizes the throne", $"{Who(rival)} deposes {heir.Full} and is crowned in {r.Name}", Cap(r), [rival.Id, heir.Id, r.Id], Cz(CauseKind.Law, crisis, "civil war over the succession"));
            MarkDead(heir, "murdered on the usurper's order", ev);
            heir.DeathCause = $"murdered on the order of {rival.Name}";
            Coronate(r, rival, heir, ev, "seizes");
        }
        else if (roll < 75 && SplitRealm(r, rival, crisis, "the claimant's war")) { }
        else
        {
            var ev = Ev("claimant_executed", $"{rival.Name} is executed", $"{Who(rival)}'s rising for the throne of {r.Name} is crushed; the claimant is beheaded", Cap(r), [rival.Id, r.Id], Cz(CauseKind.War, crisis, "the failed claimant"));
            MarkDead(rival, "executed for treason", ev);
        }
    }

    private void NoHeir(RealmState r, Notable dead, EntityId deathEv)
    {
        var consort = _h.GetNotable(dead.Spouse);
        if (consort != null && consort.Alive && consort.HomeRealm != r.Index && consort.HomeRealm >= 0 && Rm(consort.HomeRealm).Alive && Pct(dead.Id.Value, S.Succ, 70))
        {
            var home = Rm(consort.HomeRealm);
            var crisis = Ev("succession_crisis", $"The line of {HouseOf(dead)} fails", $"{dead.Full} leaves no heir; the widowed {consort.Full} turns to {home.Name}", Cap(r), [dead.Id, consort.Id, r.Id, home.Id], Cz(CauseKind.Law, deathEv, "no heir of the ruler's body"));
            MergeRealms(home.Index, r.Index, "marriage", crisis);
            return;
        }
        Notable? pick = null;
        foreach (var n in _h.Notables)
            if (n.Alive && n.Realm == r.Index && n.Role is NotableRole.General or NotableRole.Noble && AgeAt(n) >= 18 && !n.Female && n.Id != dead.Id && n.Role != NotableRole.MasterCrafter)
            { if (pick == null || n.Ambition + n.Skill > pick.Ambition + pick.Skill) pick = n; }
        if (pick != null)
        {
            var d = NewDynasty(pick.Culture, pick.Id, _year);
            pick.Dynasty = d.Id;
            var crisis = Ev("succession_crisis", $"The line of {HouseOf(dead)} fails", $"{dead.Full} leaves no heir; the lords of {r.Name} elect {Who(pick)}", Cap(r), [dead.Id, pick.Id, r.Id], Cz(CauseKind.Law, deathEv, "no heir of the ruler's body"));
            var ev = Coronate(r, pick, dead, crisis, "is elected");
            _ = ev;
            r.Unrest = Math.Min(100, r.Unrest + 12);
            return;
        }
        // a distant cousin is found
        var cousin = NewNotable(NotableRole.Noble, r.CultureId, r.Index, dead.Dynasty, Pct(dead.Id.Value, S.Succ, 25), _year - YearsAgo(Roll(dead.Id.Value, S.Succ, 18, 40)), EntityId.None, EntityId.None);
        var cr = Ev("succession_crisis", $"The line of {HouseOf(dead)} thins", $"{dead.Full} leaves no heir; a distant cousin, {cousin.Name}, is found", Cap(r), [dead.Id, cousin.Id, r.Id], Cz(CauseKind.Law, deathEv, "no heir of the ruler's body"));
        Coronate(r, cousin, dead, cr, "is raised");
        r.Unrest = Math.Min(100, r.Unrest + 10);
    }

    // ---------- births and marriages ----------
    private void Births(int count)
    {
        for (int i = 0; i < count; i++)
        {
            var m = _h.Notables[i];
            if (!m.Female || !m.Alive || m.Spouse.Value == 0) continue;
            var f = _h.GetNotable(m.Spouse);
            if (f == null || !f.Alive) continue;
            int age = AgeAt(m);
            if (age < 18 || age > MaxMotherAge || m.Kids >= 8) continue;
            int p = 30;
            int realm = m.Role == NotableRole.Ruler ? m.Realm : f.Realm;
            if (realm >= 0 && realm < _h.Realms.Count && _tick - Rm(realm).LastFamineTick < 48) p = 18;
            if (!Pct(m.Id.Value, S.Birth, p)) continue;
            bool ruling = m.Role == NotableRole.Ruler || f.Role == NotableRole.Ruler;
            bool royal = ruling || m.Role == NotableRole.Heir || f.Role == NotableRole.Heir;
            var dyn = m.Role == NotableRole.Ruler ? m.Dynasty : f.Dynasty;
            bool girl = Pct(m.Id.Value, S.Birth, 50);
            var kid = NewNotable(royal ? NotableRole.Heir : NotableRole.Noble, m.Culture, realm, dyn, girl, _year, f.Id, m.Id);
            kid.Residence = m.Residence >= 0 ? m.Residence : f.Residence;
            m.Kids++; f.Kids++;
            bool firstHeir = ruling && realm >= 0 && !_h.Notables.Any(n => n.Alive && n.Id != kid.Id && n.Role == NotableRole.Heir && (n.Father == f.Id || n.Mother == m.Id) && AgeAt(n) < 16);
            if (firstHeir && realm >= 0)
            {
                var parent = m.Role == NotableRole.Ruler ? m : f;
                Ev("heir_born", $"{kid.Name} is born", $"{Who(parent)} of {HouseOf(parent)} has {(girl ? "a daughter" : "a son")}, {kid.Name}, to secure the line of {Rm(realm).Name}", Cap(Rm(realm)),
                    [kid.Id, parent.Id, Rm(realm).Id], Cz(CauseKind.Other, EntityId.None, "the royal marriage"));
            }
        }
    }

    private void Marriages(int count)
    {
        for (int i = 0; i < count; i++)
        {
            var n = _h.Notables[i];
            if (!n.Alive || n.Spouse.Value != 0 || n.Role is not (NotableRole.Ruler or NotableRole.Heir)) continue;
            int age = AgeAt(n);
            if (age < 16 || age > 46 || n.Realm < 0 || !Rm(n.Realm).Alive) continue;
            if (!Pct(n.Id.Value, S.Marry, _s.MarriagePctYear)) continue;
            Notable? mate = null;
            if (Pct(n.Id.Value, S.Marry, 45))
            {
                int start = Roll(n.Id.Value, S.Marry, 0, Math.Max(1, count));
                for (int k = 0; k < count && mate == null; k++)
                {
                    var m = _h.Notables[(start + k) % count];
                    if (!m.Alive || m.Spouse.Value != 0 || m.Female == n.Female || m.Role is not (NotableRole.Ruler or NotableRole.Heir)) continue;
                    if (m.Realm == n.Realm || m.Realm < 0 || !Rm(m.Realm).Alive) continue;
                    int ma = AgeAt(m);
                    if (ma < 16 || ma > 46) continue;
                    if (_opinion[n.Realm * _R + m.Realm] < -10 || _atWar[n.Realm] == _atWar[m.Realm] && _atWar[n.Realm] >= 0) continue;
                    if (m.Dynasty == n.Dynasty) continue;
                    mate = m;
                }
            }
            if (mate != null)
            {
                var groom = n.Female ? mate : n;
                var bride = n.Female ? n : mate;
                int ra = groom.Realm, rb = bride.Realm;
                // the bride moves to the groom's realm unless she rules (then the groom moves)
                if (bride.Role != NotableRole.Ruler) { bride.Realm = groom.Realm; bride.Residence = groom.Residence; if (bride.Role == NotableRole.Heir) bride.Role = NotableRole.Noble; }
                else if (groom.Role != NotableRole.Ruler) { groom.Realm = bride.Realm; groom.Residence = bride.Residence; if (groom.Role == NotableRole.Heir) groom.Role = NotableRole.Noble; }
                bride.Spouse = groom.Id; groom.Spouse = bride.Id;
                var ev = Ev("marriage", $"{groom.Name} weds {bride.Name}", $"{Who(groom)} of {HouseOf(groom)} marries {bride.Full} of {HouseOf(bride)}; {Rm(ra).Name} and {Rm(rb).Name} swear fifteen years of peace", Cap(Rm(ra)),
                    [groom.Id, bride.Id, Rm(ra).Id, Rm(rb).Id], Cz(CauseKind.Law, EntityId.None, "a dynastic alliance sealed by marriage"));
                _opinion[ra * _R + rb] = Math.Min(100, _opinion[ra * _R + rb] + 25); _opinion[rb * _R + ra] = Math.Min(100, _opinion[rb * _R + ra] + 25);
                _truce[ra * _R + rb] = _truce[rb * _R + ra] = _tick + 60;
                _h.Treaties.Add(new Treaty(ra, rb, TreatyKind.MarriageAlliance, _tick, _tick + 60, ev));
                if (_v1) _alliance[ra * _R + rb] = _alliance[rb * _R + ra] = _tick + 60;
                // a dowry heirloom may change realms
                foreach (var a in _h.Artefacts)
                {
                    if (a.Status != ArtefactStatus.Held || !(a.Holder == Rm(rb).Ruler) || !Pct(a.Id.Value, S.Art, 15)) continue;
                    a.Holder = groom.Role == NotableRole.Ruler ? groom.Id : Rm(ra).Id; a.Transfers++;
                    Ev("artefact_gifted", $"{a.Name} is given as a dowry", $"{a.Name} travels with {bride.Name} to {Rm(ra).Name}", Cap(Rm(ra)), [a.Id, bride.Id], Cz(CauseKind.Law, ev, "a bride's dowry"));
                    break;
                }
            }
            else
            {
                var spouse = NewConsort(n, age + Roll(n.Id.Value, S.Marry, -4, 4));
                if (n.Role == NotableRole.Ruler)
                    Ev("marriage", $"{n.Name} takes a spouse", $"{Who(n)} of {HouseOf(n)} weds {spouse.Full} of {HouseOf(spouse)}", Cap(Rm(n.Realm)), [n.Id, spouse.Id, Rm(n.Realm).Id],
                        Cz(CauseKind.Law, EntityId.None, "the need for an heir"));
            }
        }
    }

    // ---------- generals ----------
    private Notable EnsureGeneral(RealmState r)
    {
        var g = _h.GetNotable(_general[r.Index]);
        if (g != null && g.Alive && g.Realm == r.Index && g.Role == NotableRole.General) return g;
        var house = MinorHouse(r.CultureId, r.Id.Value + (ulong)_tick);
        g = NewNotable(NotableRole.General, r.CultureId, r.Index, house.Id, false, _year - YearsAgo(Roll(r.Id.Value, S.War, 28, 52)), EntityId.None, EntityId.None);
        g.Residence = r.Capital;
        _general[r.Index] = g.Id;
        foreach (var a in _h.Artefacts)
            if (a.Status == ArtefactStatus.Held && a.Holder == r.Ruler && a.Kind is "sword" or "axe" or "spear" or "warhammer" && Pct(a.Id.Value, S.Art, 45))
            { a.Holder = g.Id; a.Transfers++; break; }
        return g;
    }

    // ---------- crafters, saints, outlaws ----------
    private void CrafterSpawn()
    {
        int n = _h.Settlements.Count;
        for (int i = 0; i < n; i++)
        {
            var s = _h.Settlements[i];
            if (s.IsRuin || s.Kind < SettlementKind.Town || s.Realm < 0) continue;
            if (!Chance(s.Id.Value, S.Craft, _s.CrafterPermilleYear * 1000L * (s.Kind == SettlementKind.City ? 2 : 1))) continue;
            int cu = _c.CultureId[s.Cell] < 8 ? _c.CultureId[s.Cell] : 0;
            var house = MinorHouse(cu, s.Id.Value + (ulong)_year);
            var c = NewNotable(NotableRole.MasterCrafter, cu, s.Realm, house.Id, Pct(s.Id.Value, S.Craft, 20), _year - YearsAgo(Roll(s.Id.Value, S.Craft, 22, 45)), EntityId.None, EntityId.None);
            c.Residence = i; c.Skill = 70 + Roll(s.Id.Value, S.Craft, 0, 30);
            string trade = Trades[Roll(s.Id.Value, S.Craft, 0, Trades.Length)];
            c.RiseEvent = Ev("master_crafter", $"{c.Name} the {trade}", $"{c.Full}, master {trade} of {s.Name}, gains renown across {Rm(s.Realm).Name}", s.Cell, [c.Id, s.Id, Rm(s.Realm).Id],
                Cz(CauseKind.Other, EntityId.None, "a guild's patronage and a rich town's custom"));
        }
    }

    private void ForgePass(int count)
    {
        for (int i = 0; i < count; i++)
        {
            var c = _h.Notables[i];
            if (!c.Alive || c.Role != NotableRole.MasterCrafter || c.Residence < 0) continue;
            int age = AgeAt(c);
            if (age < 25 || age > 65) continue;
            if (_h.Artefacts.Count(a => a.Creator == c.Id) >= 3) continue;
            if (!Chance(c.Id.Value, S.Forge, _s.ArtefactPermilleYear * 1000L)) continue;
            var s = _h.Settlements[c.Residence];
            if (s.IsRuin || s.Realm < 0) continue;
            var r = Rm(s.Realm);
            var id = _ids.Allocate(IdKind.Item);
            var rs = Rs(id.Value, S.Art);
            int k = rs.Range(0, ArtKinds.Length);
            string name = ArtPrefix[rs.Range(0, ArtPrefix.Length)] + ArtSuffix[k][rs.Range(0, ArtSuffix[k].Length)];
            var ruler = _h.GetNotable(r.Ruler);
            var a = new Artefact { Id = id, Name = name, Kind = ArtKinds[k], Creator = c.Id, CreatedTick = _tick, Holder = ruler is { Alive: true } ? ruler.Id : r.Id, Cell = s.Cell };
            a.CreatedEvent = Ev("artefact_forged", $"{name} is forged", $"{c.Full} of {s.Name} forges the {a.Kind} {name} for {(ruler != null ? Who(ruler) : r.Name)}", s.Cell, [id, c.Id, s.Id, r.Id],
                Cz(CauseKind.Other, c.RiseEvent, "the master's finest work, commissioned by the court"));
            _h.Add(a);
        }
    }

    private void ArtefactDrift()
    {
        string[] notes = ["lost in the crossing of a flooded ford", "dropped into a deep lake during a hunt", "stolen by a servant and never found", "hidden in troubled times and the hiding place forgotten"];
        int n = _h.Artefacts.Count;
        for (int i = 0; i < n; i++)
        {
            var a = _h.Artefacts[i];
            if (a.Status != ArtefactStatus.Held || a.Holder.Kind == IdKind.Settlement) continue;
            if (!Chance(a.Id.Value, S.Art, 10_000)) continue;
            int cell = 0; RealmState? r = null;
            if (a.Holder.Kind == IdKind.Person) { var h = _h.GetNotable(a.Holder); if (h == null) continue; r = h.Realm >= 0 ? Rm(h.Realm) : null; }
            else if (a.Holder.Kind == IdKind.Faction) r = Rm(RIdx(a.Holder));
            if (r == null || !r.Alive) continue;
            cell = Cap(r);
            string note = notes[Roll(a.Id.Value, S.Art, 0, notes.Length)];
            ArtefactVanish(a, Pct(a.Id.Value, S.Art, 60) ? ArtefactStatus.Lost : ArtefactStatus.Buried, cell, EntityId.None, note);
        }
    }

    private void ArtefactVanish(Artefact a, ArtefactStatus status, int cell, EntityId causeEv, string note)
    {
        a.Status = status; a.Cell = cell; a.LostTick = _tick; a.LossNote = note; a.Holder = EntityId.None;
        var cause = causeEv.Value != 0 ? Cz(CauseKind.Other, causeEv, note) : Cz(CauseKind.Other, EntityId.None, note);
        a.LossEvent = Ev(status == ArtefactStatus.Lost ? "artefact_lost" : "artefact_buried",
            status == ArtefactStatus.Lost ? $"{a.Name} is lost" : $"{a.Name} is laid in the earth",
            $"The {a.Kind} {a.Name} is {note}", cell, [a.Id], cause);
    }

    private void Reliquary(Notable saint, EntityId deathEv)
    {
        var id = _ids.Allocate(IdKind.Item);
        var s = saint.Residence >= 0 ? _h.Settlements[saint.Residence] : null;
        var a = new Artefact
        {
            Id = id, Name = $"Reliquary of Saint {saint.Name}", Kind = "reliquary", Creator = saint.Id, CreatedTick = _tick, CreatedEvent = deathEv,
            Holder = s != null && !s.IsRuin ? s.Id : (saint.Realm >= 0 ? Rm(saint.Realm).Id : EntityId.None), Cell = s?.Cell ?? 0,
        };
        _h.Add(a);
        if (a.Holder.Value == 0) ArtefactVanish(a, ArtefactStatus.Lost, a.Cell, deathEv, "the saint's bones, scattered in troubled times");
    }

    private void MaybeSaint(int sIdx, EntityId causeEv, string why)
    {
        var s = _h.Settlements[sIdx];
        if (s.IsRuin || s.Realm < 0 || s.Pop < 40) return;
        if (!Pct(s.Id.Value, S.Saint, 8)) return;
        if (_h.Notables.Any(n => n.Alive && n.Role == NotableRole.Saint && n.Residence == sIdx)) return;
        int cu = _c.CultureId[s.Cell] < 8 ? _c.CultureId[s.Cell] : 0;
        if (_v1 && _h.Notables.Count(n => n.Role == NotableRole.Saint && n.Culture == cu) >= HistoryRules.SaintsPerFaithMax) return;      // a faith keeps a feast for few saints (with its founders, 4 to 12)
        bool female = Pct(s.Id.Value, S.Saint, 50);
        var sa = NewNotable(NotableRole.Saint, cu, s.Realm, EntityId.None, female, _year - YearsAgo(Roll(s.Id.Value, S.Saint, 28, 60)), EntityId.None, EntityId.None);
        sa.Residence = sIdx;
        sa.Epithet = SaintEpithets[Roll(s.Id.Value, S.Saint, 0, SaintEpithets.Length)];
        sa.RiseEvent = Ev("saint_rises", $"Saint {sa.Name} of {s.Name}", $"{sa.Full} tends the stricken of {s.Name} after {why}, and the people call {(female ? "her" : "him")} holy", s.Cell, [sa.Id, s.Id],
            Cz(CauseKind.Other, causeEv, $"charity in the wake of {why}"));
    }

    private void SpawnOutlaw(int sIdx, EntityId causeEv, string why)
    {
        var s = _h.Settlements[sIdx];
        int alive = 0; foreach (var o in _h.Notables) if (o.Alive && o.Role == NotableRole.Outlaw) alive++;
        if (alive >= 5 || s.Realm < 0) return;
        int cu = _c.CultureId[s.Cell] < 8 ? _c.CultureId[s.Cell] : 0;
        var o2 = NewNotable(NotableRole.Outlaw, cu, s.Realm, EntityId.None, false, _year - YearsAgo(Roll(s.Id.Value, S.Outlaw, 22, 45)), EntityId.None, EntityId.None);
        o2.Residence = sIdx; o2.Epithet = "the Hooded";
        o2.RiseEvent = Ev("outlaw_band", $"{o2.Name} raises an outlaw band", $"Hungry and landless men gather under {o2.Name} in the hills above {s.Name}", s.Cell, [o2.Id, s.Id, Rm(s.Realm).Id],
            Cz(CauseKind.Crime, causeEv, $"desperation after the {why}"));
    }

    private void OutlawPass(int count)
    {
        for (int i = 0; i < count; i++)
        {
            var o = _h.Notables[i];
            if (!o.Alive || o.Role != NotableRole.Outlaw || o.Residence < 0) continue;
            var lair = _h.Settlements[o.Residence];
            if (Chance(o.Id.Value, S.Outlaw, 70_000))
            {
                var ev = _v1 ? OutlawDealtV1(o, lair) : Ev("outlaw_slain", $"{o.Name} is taken", $"{o.Full} is hunted down by the men of {(lair.Realm >= 0 ? Rm(lair.Realm).Name : "the shire")} and hanged", lair.Cell, [o.Id],
                    Cz(CauseKind.Law, o.RiseEvent, "the king's justice"));
                foreach (var a in _h.Artefacts)
                {
                    if (a.Status != ArtefactStatus.Held || a.Holder != o.Id) continue;
                    if (lair.Realm >= 0 && Pct(a.Id.Value, S.Art, 55)) { a.Holder = Rm(lair.Realm).Id; a.Transfers++; Ev("artefact_recovered", $"{a.Name} is recovered", $"{a.Name} is taken from {o.Name}'s hoard and returned to {Rm(lair.Realm).Name}", lair.Cell, [a.Id], Cz(CauseKind.Law, ev, "the outlaw's capture")); }
                    else ArtefactVanish(a, ArtefactStatus.Buried, lair.Cell, ev, $"hidden in {o.Name}'s lair, which was never found");
                }
                MarkDead(o, _v1 ? "justice by the law of his culture" : "hanged", ev);
                continue;
            }
            if (!Chance(o.Id.Value, S.Outlaw, 110_000)) continue;
            int best = -1, bp = -1;
            for (int k = 0; k < _h.Settlements.Count; k++)
            {
                var t = _h.Settlements[k];
                if (t.IsRuin || k == o.Residence || Dist(t.Cell, lair.Cell) > 8) continue;
                if (t.Pop > bp) { bp = t.Pop; best = k; }
            }
            if (best < 0) continue;
            var tg = _h.Settlements[best];
            int loss = Math.Max(1, Roll(o.Id.Value, S.Outlaw, 2, 7) * tg.Pop / 100);
            tg.Pop -= loss;
            var rev = Ev("outlaw_raid", $"{o.Name} raids {tg.Name}", $"{o.Full}'s band burns barns and carries off goods near {tg.Name}; {Num(loss)} killed or taken", tg.Cell,
                tg.Realm >= 0 ? [o.Id, tg.Id, Rm(tg.Realm).Id] : [o.Id, tg.Id], Cz(CauseKind.Raid, o.RiseEvent, "outlaws of the hills"));
            tg.LastBlow = rev; tg.LastBlowTick = _tick;
            if (tg.Realm >= 0 && Pct(o.Id.Value, S.Outlaw, 12))
            {
                var ruler = Rm(tg.Realm).Ruler;
                foreach (var a in _h.Artefacts)
                {
                    if (a.Status != ArtefactStatus.Held || a.Holder != ruler) continue;
                    a.Holder = o.Id; a.Transfers++;
                    Ev("artefact_stolen", $"{a.Name} is stolen", $"{o.Name}'s band robs the keep at {tg.Name} and makes off with {a.Name}", tg.Cell, [a.Id, o.Id], Cz(CauseKind.Crime, rev, "a daring theft"));
                    break;
                }
            }
        }
    }
}
