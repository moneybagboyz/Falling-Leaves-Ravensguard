using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Medicine;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Disease;

/// <summary>
/// The rules of disease (disease-and-plague 4), callable by a test over a hand-built world and by <see cref="DiseaseSystem"/> on the tick. Everything is integer; every draw is a counter RNG keyed
/// on ids and the game day, so nothing depends on the order of the calls; persons and settlements are walked in id order. Deaths go through <see cref="Demography.Kill"/> (estates, inheritance and the
/// chronicle follow) or, for the hero, the family's pending death.
/// </summary>
public static class DiseaseEngine
{
    private static DiseaseSettingsDef S => DiseaseRules.Current.S;

    /// <summary>The day's pass (H5, hour 0): the plague first (it may kill tracked persons), then everyday illness. Runs once for a day number.</summary>
    public static void Daily(SimWorld w, DiseaseBook b, int day, GameTime now, RegionOutbox ob)
    {
        if (b.LastDay == day) return;
        PlagueDaily(w, b, day, now, ob);
        EverydayDaily(w, b, day, now, ob);
        b.LastDay = day;
    }

    private static bool IsLiving(PersonTable pt, int row) => row >= 0 && row < pt.Count && pt.Status[row] is PersonStatus.Alive or PersonStatus.Hero;

    // ================================================================= keys and rolls

    /// <summary>The death check of one illness: stream 0x6D4, key the person, tick the start day (so the chance does not depend on the day it is rolled).</summary>
    public static bool DeathRoll(ulong seed, ulong person, int startDay, IllnessKind kind, IllnessCare care, PlagueSeverity severity = PlagueSeverity.Normal)
        => VitalMath.Roll(seed, startDay, person, RngStreams.IllnessDeath, 0, DiseaseRules.Current.DeathPpm(kind, care, severity));

    /// <summary>The smallest daily chance (ppm) that makes the chance of catching it at least once in <paramref name="days"/> days equal <paramref name="attackPct"/> (integer, a bisection on powers of 1e-6).</summary>
    public static int SolveDailyPpm(int attackPct, int days)
    {
        if (attackPct <= 0 || days <= 0) return 0;
        if (attackPct >= 100) return 1_000_000;
        long target = (100 - attackPct) * 10_000_000L;                    // (1 - attack) at 1e9
        int lo = 0, hi = 1_000_000;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2; long acc = 1_000_000_000; long q = 1_000_000 - mid;
            for (int i = 0; i < days; i++) acc = acc * q / 1_000_000;
            if (acc <= target) hi = mid; else lo = mid + 1;
        }
        return lo;
    }

    // ================================================================= everyday illness (4.1)

    private static void EverydayDaily(SimWorld w, DiseaseBook b, int day, GameTime now, RegionOutbox ob)
    {
        var pt = w.People;
        // 1. the sick go on (all of them, tracked or not any longer)
        foreach (var id in b.Illnesses.Keys.ToList())
        {
            var ill = b.Illnesses[id]; int row = pt.IndexOf(new EntityId(id));
            if (!IsLiving(pt, row)) { b.Illnesses.Remove(id); continue; }
            Advance(w, b, ill, row, day, now, ob);
        }
        // 2. the well catch it
        var tracked = b.Tracked?.Invoke(); if (tracked == null) return;
        foreach (var t in tracked)
        {
            if (b.Illnesses.ContainsKey(t.Id) || (b.Plague?.Cases.ContainsKey(t.Id) ?? false)) continue;
            int row = pt.IndexOf(new EntityId(t.Id)); if (!IsLiving(pt, row)) continue;
            TryOnset(w, b, t.Id, row, day, now, ob);
        }
    }

    private static int SickHousemates(SimWorld w, DiseaseBook b, int row, ulong self)
    {
        var pt = w.People; int hh = pt.Household[row]; if (hh < 0) return 0;
        int n = 0;
        foreach (int m in w.Households.Members(hh))
        {
            ulong mid = pt.IdOf(m).Value; if (mid == self) continue;
            if (b.Illnesses.TryGetValue(mid, out var i) && DiseaseRules.Current.Row(i.Kind).Contagious) n++;
            else if (b.Plague?.Cases.ContainsKey(mid) == true) n++;
        }
        return Math.Min(n, 3);
    }

    private static void TryOnset(SimWorld w, DiseaseBook b, ulong id, int row, int day, GameTime now, RegionOutbox ob)
    {
        var s = S; var pt = w.People; ulong seed = w.Seed;
        int hh = pt.Household[row];
        bool winter = now.Season == Season.Winter, summer = now.Season == Season.Summer;
        // flux: bad water at camp, then a crowded army camp in summer
        int unboiled = b.UnboiledWaterDays?.Invoke(id) ?? 0, army = b.ArmyCampSize?.Invoke(id) ?? 0;
        int fluxPm = unboiled > 0 ? s.FluxUnboiledPm : summer && army >= s.ArmyCampMin ? s.FluxArmyCampPm : 0;
        if (fluxPm > 0 && VitalMath.Roll(seed, day, id, RngStreams.IllnessOnset, 2, fluxPm * 1000))
        { Begin(w, b, id, row, IllnessKind.Flux, unboiled > 0 ? "ch_note_ill_camp_water" : "ch_note_ill_army_camp", day, now, ob); return; }
        // ague: marsh in summer
        if (summer && s.AguePm > 0 && (b.OnMarsh?.Invoke(id) ?? false) && VitalMath.Roll(seed, day, id, RngStreams.IllnessOnset, 1, s.AguePm * 1000))
        { Begin(w, b, id, row, IllnessKind.Ague, "ch_note_ill_marsh", day, now, ob); return; }
        // cough: winter, cold and wet; a sick housemate adds
        int mates = SickHousemates(w, b, row, id);
        int hours = b.ExposureHours?.Invoke(id) ?? (hh >= 0 && (w.Economy.Grievances[hh] & Grievance.Cold) != 0 ? 6 : 0);
        int pm = winter ? (hours >= s.ExposedHours ? s.CoughExposedPm : s.CoughWinterPm) : 0;
        pm += mates * s.HousemateAddPm;
        if (pm > 0 && VitalMath.Roll(seed, day, id, RngStreams.IllnessOnset, 0, pm * 1000))
            Begin(w, b, id, row, IllnessKind.Cough, mates > 0 ? "ch_note_ill_housemate" : winter && hours >= s.ExposedHours ? "ch_note_ill_cold_wet" : "ch_note_ill_winter", day, now, ob);
    }

    /// <summary>A person falls ill (the hero's party and kin; also the way a test or a later system gives someone an illness). One illness at a time.</summary>
    public static Illness? Begin(SimWorld w, DiseaseBook b, ulong id, int row, IllnessKind kind, string onsetKey, int day, GameTime now, RegionOutbox ob)
    {
        var pt = w.People;
        if (b.Illnesses.ContainsKey(id)) return null;
        var ill = new Illness { Person = id, Kind = kind, StartDay = day, Settlement = pt.HomeSettlement[row], OnsetKey = onsetKey };
        var e = ob.Chronicle(now, "illness_began", "ch_ill_began", [ChronParam.Person("person", new EntityId(id)), ChronParam.TextKey("illness", DiseaseRules.NameKey(kind))],
            [new EntityId(id)], [new Cause(CauseKind.Illness, null, NoteKey: onsetKey)], ("kind", DiseaseRules.IdOf(kind)));
        ill.Entry = e.Value;
        b.Illnesses[id] = ill;
        ob.Count("illnesses");
        return ill;
    }

    private static int CourseDays(Illness ill)
    {
        var r = DiseaseRules.Current.Row(ill.Kind);
        return Math.Max(1, r.CourseDays - (ill.Cared ? r.CareShortensDays : 0));
    }

    private static void Advance(SimWorld w, DiseaseBook b, Illness ill, int row, int day, GameTime now, RegionOutbox ob)
    {
        var rules = DiseaseRules.Current; var r = rules.Row(ill.Kind);
        int n = day - ill.StartDay + 1;
        if (n < 1) return;
        ill.Care = CareFor(w, b, ill.Person, row, ill.Kind, ill.Settlement, ill.NursedUntil, ill.PhysicianUntil, day, now, ob, abstractPhysician: !IsHero(b, ill.Person));
        if (ill.Care != IllnessCare.None) ill.Cared = true;
        if (!ill.Checked && n >= r.WorstDay)
        {
            ill.Checked = true;
            if (DeathRoll(w.Seed, ill.Person, ill.StartDay, ill.Kind, ill.Care, b.Severity))
            {
                var cause = new Cause(CauseKind.Illness, ill.Entry != 0 ? new EntityId(ill.Entry) : null, NoteKey: "ch_note_ill_death",
                    NoteParams: [ChronParam.TextKey("illness", DiseaseRules.NameKey(ill.Kind)), ChronParam.Days("days", n)]);
                b.Illnesses.Remove(ill.Person);
                Die(w, b, ill.Person, row, now, ob, cause);
                return;
            }
        }
        if (n >= CourseDays(ill))
        {
            b.Illnesses.Remove(ill.Person);
            if (ill.Kind == IllnessKind.Cough && (w.AgeOn(row, day) >= S.OldAge || w.Economy.NutritionCd[row] < S.StarvingCd)
                && VitalMath.Roll(w.Seed, day, ill.Person, RngStreams.IllnessOnset, 5, S.LungFeverPct * 10_000))
                Begin(w, b, ill.Person, row, IllnessKind.LungFever, "ch_note_ill_after_cough", day, now, ob);
        }
    }

    private static bool IsHero(DiseaseBook b, ulong person)
    {
        var t = b.Tracked?.Invoke(); if (t == null) return false;
        foreach (var x in t) if (x.Id == person) return x.Hero;
        return false;
    }

    private static void Die(SimWorld w, DiseaseBook b, ulong person, int row, GameTime now, RegionOutbox ob, Cause chain)
    {
        if (w.People.Status[row] == PersonStatus.Hero) b.HeroDies?.Invoke(person, now, chain, ob);
        else Demography.Kill(w, row, DeathCause.Disease, now, ob, chain: chain);
    }

    // ================================================================= care (4.1)

    /// <summary>
    /// The care a sick person has today. A physician's visit (the hero's order, or for a household that can pay and has one in town) costs the daily fee and wins; else a household member at home
    /// or a nursing order is Nursed. <paramref name="abstractPhysician"/>: an NPC's household calls the physician by itself when the illness is serious (a death chance of 1 percent or more nursed).
    /// </summary>
    public static IllnessCare CareFor(SimWorld w, DiseaseBook b, ulong person, int row, IllnessKind kind, int settlement, int nursedUntil, int physicianUntil, int day, GameTime now, RegionOutbox? ob, bool abstractPhysician)
    {
        var pt = w.People; var rules = DiseaseRules.Current;
        bool order = physicianUntil >= day;
        bool serious = rules.DeathPpm(kind, IllnessCare.Nursed, b.Severity) >= 10_000;
        if ((order || (abstractPhysician && serious)) && PayPhysician(w, b, person, row, settlement, kind == IllnessKind.Plague, order)) return IllnessCare.Physician;
        if (nursedUntil >= day) return IllnessCare.Nursed;
        int hh = pt.Household[row];
        if (hh >= 0)
            foreach (int m in w.Households.Members(hh))
                if (m != row && pt.Status[m] is PersonStatus.Alive or PersonStatus.Hero && w.AgeOn(m, day) >= 12 && !b.Illnesses.ContainsKey(pt.IdOf(m).Value) && (b.Plague == null || !b.Plague.Cases.ContainsKey(pt.IdOf(m).Value))) return IllnessCare.Nursed;
        return IllnessCare.None;
    }

    /// <summary>The fee of one visit, mites: 100, doubled in a plague.</summary>
    public static int VisitFee(bool plague) => S.PhysicianVisitMites * (plague ? S.PlagueFeeMultPct : 100) / 100;

    /// <summary>The physician of a settlement, or null (barber-surgeons do not treat fevers).</summary>
    public static PhysicianRow? PhysicianIn(SimWorld w, int settlement)
    {
        var book = w.Get<PhysicianBook>(); if (book == null || settlement < 0) return null;
        foreach (var r in book.In(settlement)) if (r.Kind == PhysicianKind.Physician) return r;
        return null;
    }

    /// <summary>One visit is paid: from the person's own purse (the hero's, when the wiring names one) or his household's, to the physician's business box or his household. False when there is no physician or no coin.</summary>
    public static bool PayPhysician(SimWorld w, DiseaseBook b, ulong person, int row, int settlement, bool plague, bool ordered)
    {
        var man = PhysicianIn(w, settlement); if (man is not { } m) return false;
        var pt = w.People; var econ = w.Economy; int hh = pt.Household[row];
        int mrow = pt.IndexOf(new EntityId(m.Person)); if (!IsLiving(pt, mrow)) return false;
        PurseId payer = b.PurseOf?.Invoke(person) ?? (hh >= 0 ? econ.HouseholdPurse[hh] : PurseId.None);
        if (payer == PurseId.None) return false;
        int fee = VisitFee(plague);
        if (!ordered && econ.Ledger.Balance(payer).Mites < fee * 5L) return false;           // a household calls him only when it can well pay
        var ew = w.Get<EconomyWorld>();
        PurseId payee = m.Business >= 0 && ew != null && m.Business < ew.Production.Businesses.Count ? ew.Production.Businesses[m.Business].Purse : econ.HouseholdPurse[pt.Household[mrow]];
        if (payer == payee) return true;
        return econ.Ledger.Transfer(payer, payee, new Coin(fee), TransferReason.Fee);
    }

    // ================================================================= the great plague in the region (4.3)

    private static int SettlementOf(SimWorld w, int row) => w.People.HomeSettlement[row];

    private static void PlagueDaily(SimWorld w, DiseaseBook b, int day, GameTime now, RegionOutbox ob)
    {
        CheckGates(w, b, day, now, ob);
        var gp = b.Plague; if (gp == null || !gp.Active) return;
        ResolveCases(w, b, gp, day, now, ob);
        InfectPeople(w, b, gp, day, now, ob);
        SpreadAmongSettlements(w, b, gp, day, now, ob);
        CloseEpidemics(w, b, gp, day, now, ob);
        if (gp.Active && b.Continent.Active) b.Continent.Report(RegionOf(w), day);
    }

    private static int RegionOf(SimWorld w) => w.Get<ContinentLink>()?.Region ?? -1;

    /// <summary>How big a settlement's attack rate is (town 50, village 35, hamlet 30 percent).</summary>
    public static int AttackPct(SettlementClass c) => c == SettlementClass.Town ? S.AttackTownPct : c == SettlementClass.Village ? S.AttackVillagePct : S.AttackHamletPct;

    /// <summary>Starts the region's plague (4.2): the origin settlement or the first the carriers reach has its epidemic. Writes <c>plague_begins</c> unless the continent's tick wrote one.</summary>
    public static GreatPlague BeginRegion(SimWorld w, DiseaseBook b, int day, GameTime now, RegionOutbox ob, int firstSettlement, int nameIndex, int originRegion, ulong beginEntry = 0, int plagueId = 1)
    {
        var gp = new GreatPlague { Id = plagueId, NameIndex = nameIndex, OriginRegion = originRegion, StartDay = day };
        if (beginEntry == 0)
            beginEntry = ob.Chronicle(now, "plague_begins", "ch_plague_begins", [ChronParam.TextKey("plague", ContinentPlague.PlagueNames[nameIndex])], [], [new Cause(CauseKind.Disease, null, NoteKey: "ch_note_plague_roads")],
                ("plague", ContinentPlague.PlagueNames[nameIndex])).Value;
        gp.BeginEntry = beginEntry;
        b.Plague = gp;
        StartEpidemic(w, b, gp, firstSettlement, day, now, ob, new EntityId(beginEntry), "ch_note_plague_arrived");
        return gp;
    }

    /// <summary>A settlement's epidemic starts (4.3): 30 to 60 days (stream 0x6D1 draw 1, key the settlement), its daily chance from its class's attack rate.</summary>
    public static Epidemic? StartEpidemic(SimWorld w, DiseaseBook b, GreatPlague gp, int s, int day, GameTime now, RegionOutbox ob, EntityId cause, string noteKey)
    {
        var settlements = w.Population.Region.Settlements;
        if ((uint)s >= (uint)settlements.Count || gp.Epidemics.ContainsKey(s)) return null;
        var st = settlements[s]; var sd = S;
        int len = DetRng.Range(w.Seed, day, st.Id.Value, RngStreams.EpidemicRoll, 1, sd.EpidemicMinDays, sd.EpidemicMaxDays + 1);
        int pop = 0; var pt = w.People;
        for (int i = 0; i < pt.Count; i++) if (pt.Status[i] is PersonStatus.Alive or PersonStatus.Hero && pt.HomeSettlement[i] == s) pop++;
        var ep = new Epidemic { Settlement = s, StartDay = day, EndDay = day + len, PopStart = pop, DailyPpm = SolveDailyPpm(AttackPct(st.Class), len) };
        var e = ob.Chronicle(now, "plague_settlement", "ch_plague_settlement", [ChronParam.TextKey("plague", ContinentPlague.PlagueNames[gp.NameIndex]), ChronParam.Place("place", st.Id), ChronParam.Days("days", len)],
            [st.Id], [new Cause(CauseKind.Disease, cause.Value != 0 ? cause : null, NoteKey: noteKey)], ("days", len.ToString()));
        ep.Entry = e.Value;
        gp.Epidemics[s] = ep;
        gp.Active = true;
        return ep;
    }

    /// <summary>The plague comes through the gates (4.3): at the origin on the day it starts, and after that on a caravan home from an infected neighbour with P 30 percent (stream 0x6D3 draw 1, key the caravan).</summary>
    private static void CheckGates(SimWorld w, DiseaseBook b, int day, GameTime now, RegionOutbox ob)
    {
        var link = w.Get<ContinentLink>(); var cp = b.Continent;
        if (link == null || !cp.Active) return;
        if (b.Plague != null && b.Plague.Id == cp.Id) return;                       // it has been here (or is): the roads inside carry it now
        int region = link.Region;
        var settlements = w.Population.Region.Settlements; if (settlements.Count == 0) return;
        if (cp.Origin == region)
        {
            int s = BiggestSettlement(w);
            if (s >= 0) BeginRegion(w, b, day, now, ob, s, cp.NameIndex, cp.Origin, 0, cp.Id);
            return;
        }
        foreach (var c in link.State.Caravans.All.OrderBy(c => c.Id.Value))
        {
            if (c.Home != region || c.State != Ravensguard.Sim.Caravans.CaravanState.Inbound || !cp.IsInfected(c.Neighbour)) continue;
            if (!VitalMath.Roll(w.Seed, day, c.Id.Value, RngStreams.PlagueSpread, 1, S.CarrierPct * 10_000)) continue;
            int s = c.Settlement >= 0 && c.Settlement < settlements.Count ? c.Settlement : BiggestSettlement(w);
            if (s >= 0) BeginRegion(w, b, day, now, ob, s, cp.NameIndex, cp.Origin, 0, cp.Id);
            return;
        }
    }

    private static int BiggestSettlement(SimWorld w)
    {
        int best = -1, bp = -1; var pop = new int[w.Population.Region.Settlements.Count]; var pt = w.People;
        for (int i = 0; i < pt.Count; i++) if (pt.Status[i] == PersonStatus.Alive && (uint)pt.HomeSettlement[i] < (uint)pop.Length) pop[pt.HomeSettlement[i]]++;
        for (int s = 0; s < pop.Length; s++) if (pop[s] > bp) { bp = pop[s]; best = s; }
        return best;
    }

    private static void InfectPeople(SimWorld w, DiseaseBook b, GreatPlague gp, int day, GameTime now, RegionOutbox ob)
    {
        var pt = w.People; var hht = w.Households; var sd = S;
        if (gp.Epidemics.Values.All(e => !e.Active || day < e.StartDay)) return;
        for (int i = 0; i < pt.Count; i++)
        {
            if (pt.Status[i] is not (PersonStatus.Alive or PersonStatus.Hero)) continue;
            int s = pt.HomeSettlement[i];
            if (!gp.Epidemics.TryGetValue(s, out var ep) || !ep.Active || day >= ep.EndDay) continue;
            ulong id = pt.IdOf(i).Value;
            if (gp.Immune.Contains(id) || gp.Cases.ContainsKey(id)) continue;
            long ppm = ep.DailyPpm; int hh = pt.Household[i];
            if (hh >= 0)
            {
                if (hht.MemberCount[hh] > hht.Beds[hh]) ppm = ppm * sd.CrowdingMultPct / 100;
                if (gp.Burned.TryGetValue(hh, out int until) && until >= day) ppm = ppm * (100 - sd.BeddingBurnedPct) / 100;
            }
            if (!VitalMath.Roll(w.Seed, day, id, RngStreams.EpidemicRoll, 2, (int)Math.Min(1_000_000, ppm))) continue;
            Infect(w, b, gp, ep, id, day);
        }
    }

    /// <summary>One person catches it (4.3): incubation 3 days, the illness 7; death, if it comes, at day 4 to 8 (stream 0x6D4 draw 1).</summary>
    public static PlagueCase Infect(SimWorld w, DiseaseBook b, GreatPlague gp, Epidemic ep, ulong id, int day)
    {
        var sd = S;
        int span = sd.DeathDayMax - sd.DeathDayMin + 1;
        var c = new PlagueCase
        {
            Person = id, Settlement = ep.Settlement, InfectedDay = day,
            DeathDay = day + sd.DeathDayMin + DetRng.Range(w.Seed, day, id, RngStreams.IllnessDeath, 1, 0, span),
            EndDay = day + sd.IncubationDays + sd.IllnessDays,
        };
        gp.Cases[id] = c; ep.Infected++;
        return c;
    }

    private static void ResolveCases(SimWorld w, DiseaseBook b, GreatPlague gp, int day, GameTime now, RegionOutbox ob)
    {
        var pt = w.People; var tracked = TrackedSet(b);
        foreach (var id in gp.Cases.Keys.ToList())
        {
            var c = gp.Cases[id]; int row = pt.IndexOf(new EntityId(id));
            if (!IsLiving(pt, row)) { gp.Cases.Remove(id); continue; }
            if (!c.Checked && day >= c.DeathDay)
            {
                c.Checked = true;
                bool trk = tracked.Contains(id);
                var care = trk ? CareFor(w, b, id, row, IllnessKind.Plague, c.Settlement, c.NursedUntil, c.PhysicianUntil, day, now, ob, abstractPhysician: false) : IllnessCare.None;
                if (DeathRoll(w.Seed, id, c.InfectedDay, IllnessKind.Plague, care, b.Severity))
                {
                    gp.Cases.Remove(id); gp.Dead++;
                    if (gp.Epidemics.TryGetValue(c.Settlement, out var ep)) ep.Dead++;
                    var cause = new Cause(CauseKind.Disease, gp.Epidemics.TryGetValue(c.Settlement, out var e2) && e2.Entry != 0 ? new EntityId(e2.Entry) : null, NoteKey: "ch_note_plague_death",
                        NoteParams: [ChronParam.TextKey("plague", ContinentPlague.PlagueNames[gp.NameIndex]), ChronParam.Days("days", day - c.InfectedDay)]);
                    Die(w, b, id, row, now, ob, cause);
                    continue;
                }
            }
            if (day >= c.EndDay) { gp.Cases.Remove(id); gp.Immune.Add(id); gp.Recovered++; }
        }
    }

    private static HashSet<ulong> TrackedSet(DiseaseBook b)
    {
        var set = new HashSet<ulong>(); var t = b.Tracked?.Invoke();
        if (t != null) foreach (var x in t) set.Add(x.Id);
        return set;
    }

    private static long Dist2(RegionSettlement a, RegionSettlement b) { long dx = a.CenterX - b.CenterX, dy = a.CenterY - b.CenterY; return dx * dx + dy * dy; }

    /// <summary>The plague walks the roads inside the region (4.3): each infected settlement and each other within 12 km; 3 percent a day x traffic (1 to 5) over a distance factor, a fifth of it into a closed town.</summary>
    private static void SpreadAmongSettlements(SimWorld w, DiseaseBook b, GreatPlague gp, int day, GameTime now, RegionOutbox ob)
    {
        var settlements = w.Population.Region.Settlements; var sd = S; long reach = sd.RoadKm * 1000L;
        var pops = new int[settlements.Count]; var pt = w.People;
        for (int i = 0; i < pt.Count; i++) if (pt.Status[i] == PersonStatus.Alive && (uint)pt.HomeSettlement[i] < (uint)pops.Length) pops[pt.HomeSettlement[i]]++;
        foreach (var (a, epa) in gp.Epidemics.ToList())
        {
            if (!epa.Active || day >= epa.EndDay) continue;
            for (int t = 0; t < settlements.Count; t++)
            {
                if (t == a || gp.Epidemics.ContainsKey(t)) continue;
                long d2 = Dist2(settlements[a], settlements[t]); if (d2 > reach * reach) continue;
                long dist = Ravensguard.Sim.Production.ProductionSystem.ISqrt(d2);
                long traffic = Math.Clamp(1 + (pops[a] + pops[t]) / 1500, 1, 5);
                long distFactorPm = 1000 + dist * 1000 / reach;
                long ppm = sd.RegionSpreadPermille * 1000L * traffic * 1000 / distFactorPm;
                if (gp.Quarantined(t, day)) ppm = ppm * sd.QuarantineMultPct / 100;
                ulong key = settlements[a].Id.Value * 1_000_003UL ^ settlements[t].Id.Value;
                if (VitalMath.Roll(w.Seed, day, key, RngStreams.PlagueSpread, 2, (int)Math.Min(1_000_000, ppm)))
                    StartEpidemic(w, b, gp, t, day, now, ob, new EntityId(epa.Entry), "ch_note_plague_road");
            }
        }
        // a lord closes the gates of a town a neighbour's sickness is near (4.4)
        foreach (var (a, epa) in gp.Epidemics.ToList())
        {
            if (!epa.Active) continue;
            for (int t = 0; t < settlements.Count; t++)
            {
                if (t == a || gp.Epidemics.ContainsKey(t) || gp.Quarantined(t, day) || settlements[t].Class == SettlementClass.Farmstead) continue;
                if (Dist2(settlements[a], settlements[t]) > reach * reach) continue;
                if (VitalMath.Roll(w.Seed, day, settlements[t].Id.Value, RngStreams.PlagueSpread, 3, sd.QuarantineLordPct * 10_000)) Quarantine(w, b, gp, t, day, sd.QuarantineDays, now, ob, new EntityId(epa.Entry));
            }
        }
    }

    /// <summary>A settlement's gates are closed for N days (4.4): spread into it is a fifth, and it keeps its caravans out.</summary>
    public static void Quarantine(SimWorld w, DiseaseBook b, GreatPlague gp, int settlement, int day, int days, GameTime now, RegionOutbox ob, EntityId cause)
    {
        var st = w.Population.Region.Settlements[settlement];
        gp.Quarantine[settlement] = day + days;
        ob.Chronicle(now, "plague_quarantine", "ch_plague_quarantine", [ChronParam.Place("place", st.Id), ChronParam.Days("days", days)], [st.Id],
            [new Cause(CauseKind.Disease, cause.Value != 0 ? cause : null, NoteKey: "ch_note_plague_near")], ("days", days.ToString()));
    }

    private static void CloseEpidemics(SimWorld w, DiseaseBook b, GreatPlague gp, int day, GameTime now, RegionOutbox ob)
    {
        var settlements = w.Population.Region.Settlements; var sd = S; var pt = w.People;
        foreach (var (s, ep) in gp.Epidemics.ToList())
        {
            if (!ep.Active || day < ep.EndDay) continue;
            ep.Active = false;
            var st = settlements[s];
            if (st.Class == SettlementClass.Farmstead && ep.PopStart > 0 && ep.PopStart < sd.HamletPopMax && ep.Dead * 100 >= ep.PopStart * sd.HamletDeadPct
                && VitalMath.Roll(w.Seed, day, st.Id.Value, RngStreams.EpidemicRoll, 4, sd.HamletAbandonPct * 10_000))
            {
                for (int i = 0; i < pt.Count; i++)
                    if (pt.Status[i] == PersonStatus.Alive && pt.HomeSettlement[i] == s) Demography.Depart(w, i, now, ob);
                ob.Chronicle(now, "settlement_emptied", "ch_plague_emptied", [ChronParam.TextKey("plague", ContinentPlague.PlagueNames[gp.NameIndex]), ChronParam.Place("place", st.Id), ChronParam.Count("dead", ep.Dead)],
                    [st.Id], [new Cause(CauseKind.Disease, new EntityId(ep.Entry), NoteKey: "ch_note_plague_emptied")], ("dead", ep.Dead.ToString()));
            }
        }
        if (gp.Epidemics.Values.Any(e => e.Active) || gp.Cases.Count > 0) return;
        gp.Active = false;
        ob.Chronicle(now, "plague_ends", "ch_plague_waned", [ChronParam.TextKey("plague", ContinentPlague.PlagueNames[gp.NameIndex]), ChronParam.Count("dead", gp.Dead), ChronParam.Count("places", gp.Epidemics.Count)],
            [], [new Cause(CauseKind.Disease, new EntityId(gp.BeginEntry), NoteKey: "ch_note_plague_course")], ("dead", gp.Dead.ToString()));
    }

    // ================================================================= the hero's acts (4.3, 4.4)

    /// <summary>A party member nurses the sick one for the rest of his course (2 hours a day); false when he is not ill.</summary>
    public static bool Nurse(DiseaseBook b, ulong person, int day)
    {
        if (b.Illnesses.TryGetValue(person, out var i)) { i.NursedUntil = day + 30; return true; }
        if (b.Plague?.Cases.TryGetValue(person, out var c) == true) { c.NursedUntil = day + 30; return true; }
        return false;
    }

    /// <summary>A physician is asked to call every day (the fee is paid at each pass); false when he is not ill.</summary>
    public static bool CallPhysician(DiseaseBook b, ulong person, int day)
    {
        if (b.Illnesses.TryGetValue(person, out var i)) { i.PhysicianUntil = day + 30; return true; }
        if (b.Plague?.Cases.TryGetValue(person, out var c) == true) { c.PhysicianUntil = day + 30; return true; }
        return false;
    }

    /// <summary>The household burns the dead's bedding (4.3): contagion in it is halved until the epidemic's end.</summary>
    public static bool BurnBedding(DiseaseBook b, int household, int untilDay)
    {
        if (b.Plague == null || !b.Plague.Active) return false;
        b.Plague.Burned[household] = untilDay; return true;
    }
}
