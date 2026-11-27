using Ravensguard.Content;
using Ravensguard.Sim.Combat.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Unrest;
using Ravensguard.Sim.WarSupply;

namespace Ravensguard.Sim.Armies;

/// <summary>Where a man who leaves the host goes (4.8): home to his household, or to the roads.</summary>
public enum DesertDest : byte { Home, Band }

public sealed partial class ArmyWorld
{
    /// <summary>One man of a host: his id and row, where he comes from (0 a levy, 1 a joined warband's, 2 a man from outside the region), the roster role he counts as and which company or warband holds him.</summary>
    internal readonly record struct HostMan(ulong Id, int Row, byte Kind, int Role, int Holder);

    /// <summary>The host's rations record, made at its first fed day with the content's starting morale.</summary>
    public HostRations RationsOf(int supplyHost)
    {
        if (!Book.Rations.TryGetValue(supplyHost, out var r)) Book.Rations[supplyHost] = r = new HostRations { Host = supplyHost, Morale = Rules.HostMoraleStart };
        return r;
    }

    /// <summary>Test hook, not saved: the desertion rule is off (a test of the supply side alone counts a host's men by hand).</summary>
    public bool NoDesertion { get; set; }

    /// <summary>P7-SG-D: permille a man a day added to the desertion rule for a host (a winter siege's cold camp: +10); set by the siege world, not saved.</summary>
    public Func<int, int>? ExtraDesertPm { get; set; }

    /// <summary>P7-SG-D: the living men a host feeds (levies, the joined warbands' men, men from outside), in the rations' fixed order.</summary>
    public List<ulong> MenIdsOf(HostSupply hs, HostId hid, ArmyHost? host) { var l = new List<ulong>(); foreach (var m in MenOf(hs, hid, host)) l.Add(m.Id); return l; }

    public ArmyHost? HostOfSupply(int supplyHost) { foreach (var h in Book.Hosts.Values) if (h.Supply == supplyHost && supplyHost != 0) return h; return null; }

    // ------------------------------------------------------------------ the rule (pure)

    /// <summary>
    /// The desertion rule of 4.8, permille a man a day, from its parts: 2 for each day of arrears past the third, 10 when hungry, 30 for a levy in the reap window, 5 for each day a levy is kept past his owed days, 10 under the low
    /// morale, 20 within days of a defeat, less 5 for each 200 of drill, clamped to 0 and the content's ceiling; a retinue rolls half. Pure: the same numbers give the same answer.
    /// </summary>
    public static int DesertionPm(ArmyRulesDef r, int arrearsDays, bool hungry, bool levyInReap, int daysBeyondOwed, int morale, bool recentDefeat, int drill, bool retinue)
    {
        long pm = (long)r.DesertArrearsPm * Math.Max(0, arrearsDays - r.DesertArrearsFreeDays);
        if (hungry) pm += r.DesertHungryPm;
        if (levyInReap) pm += r.DesertHarvestPm;
        pm += (long)r.DesertBeyondOwedPm * Math.Max(0, daysBeyondOwed);
        if (morale < r.MoraleLow) pm += r.DesertLowMoralePm;
        if (recentDefeat) pm += r.DesertDefeatPm;
        if (pm > 0) pm -= 5L * (Math.Clamp(drill, 0, 1000) / 200);
        pm = Math.Clamp(pm, 0, r.DesertMaxPm);
        return (int)(retinue ? pm / 2 : pm);
    }

    /// <summary>The note key of the reason that weighed most among the deserters (the wave's chronicle note).</summary>
    private static string ReasonKey(long unpaid, long hungry, long harvest, long service, long defeat, long morale)
    {
        long best = Math.Max(Math.Max(Math.Max(unpaid, hungry), Math.Max(harvest, service)), Math.Max(defeat, morale));
        if (best <= 0 || unpaid == best) return "ch_note_am_desert_unpaid";
        if (hungry == best) return "ch_note_am_desert_hungry";
        if (harvest == best) return "ch_note_am_desert_harvest";
        if (defeat == best) return "ch_note_am_desert_defeat";
        if (service == best) return "ch_note_am_desert_service";
        return "ch_note_am_desert_morale";
    }

    // ------------------------------------------------------------------ the men of a host

    /// <summary>The men a host feeds and can lose, in a fixed order: its levies by person row, the men of the warbands that joined it (join order, ids ascending) and the men from outside it marched in with (ids ascending). Only the living.</summary>
    private List<HostMan> MenOf(HostSupply hs, HostId hid, ArmyHost? host)
    {
        var l = new List<HostMan>(); var pt = People;
        foreach (var kv in hs.Book.OnCampaign) if (kv.Value == hid.Value && pt.Status[kv.Key] == PersonStatus.Alive) l.Add(new HostMan(pt.IdOf(kv.Key).Value, kv.Key, 0, (int)TroopRole.Levy, 0));
        if (host != null)
            foreach (var j in host.Warbands)
                if (Book.Warbands.TryGetValue(j.Warband, out var wb) && Joined(host, wb))
                    foreach (ulong m in wb.Members) { int r = RowOf(m); if (r >= 0 && pt.Status[r] == PersonStatus.Alive) l.Add(new HostMan(m, r, 1, (int)TroopRole.Spearman, wb.Id)); }
        if (Book.Marches.TryGetValue(hid.Value, out var mc) && mc.InRegion)
            foreach (var (id, role) in mc.Outsiders) { int r = RowOf(id); if (r >= 0 && pt.Status[r] == PersonStatus.Alive) l.Add(new HostMan(id, r, 2, role, 0)); }
        return l;
    }

    /// <summary>A warband is with the host when it has arrived at the muster point on the host's call (it eats at the host's table from then on).</summary>
    private static bool Joined(ArmyHost host, Warband wb) => wb.Task == WarbandTask.JoinHost && wb.Here == host.Point && wb.Stops.Count == 0;

    private long WarbandRoll(Warband wb)
    {
        long roll = 0; int lordHh = HouseholdOf(wb.Lord);
        foreach (ulong m in wb.Members) { int row = RowOf(m); if (row < 0 || IsStaff(row)) continue; int hh = People.Household[row]; if (hh < 0 || hh == lordHh) continue; roll += WageOf(m); }
        return roll;
    }

    // ------------------------------------------------------------------ the day (ArmiesDaily, after the host slot)

    /// <summary>
    /// The hosts' belly and heart (4.7 to 4.9, overview 8.2: the military's half of the seam). For every host of the supply book, in id order, once the host slot has run: the dead are struck off (their households bear <c>levy_death</c>),
    /// the day's ration (what <see cref="DayResult"/> delivered over what it asked) sets hunger, the run of short days, the men's nutrition reserve and the morale, and every man rolls the desertion rule (stream 0x7A0, key person and day):
    /// a deserter goes home (walking, his labour away until he arrives) or takes to the roads, a lone man to a band near or home. Then tomorrow's notes go to the economy: the joined warbands' men it feeds, the foraging policy of the ground,
    /// the strip limit's cut. The host of the war-load soak, a plain client of the supply book with no <see cref="ArmyHost"/> behind it, is rolled the same way.
    /// </summary>
    public void RationsDay(ArmyEnv e)
    {
        var hs = W.Get<HostSupply>(); if (hs == null || hs.Book.Hosts.Count == 0) return;
        int day = (int)e.Day;
        foreach (var hid in hs.Hosts().ToList())
        {
            if (!hs.Exists(hid)) continue;
            var a = hs.Account(hid); if (a.Abstract) continue;
            var host = HostOfSupply(hid.Value);
            if (host != null && host.State == HostState.Disbanding) continue;
            var st = RationsOf(hid.Value);
            StrikeDead(e, hs, hid, host, day);
            var res = hs.Last(hid);
            if (res != null && res.Day == day) EatAndRoll(e, hs, hid, a, host, st, res, day);
            Tomorrow(e, hs, hid, a, host, st, res, day);
        }
        foreach (int id in Book.Rations.Keys.ToList()) if (!hs.Book.Hosts.ContainsKey(id)) Book.Rations.Remove(id);
    }

    /// <summary>The dead leave the host's rolls and the supply book's levy count (the lord and the host keep the cause: they were killed by the fight, the fever or the hunger); a levy's household bears <c>levy_death</c>.</summary>
    private int StrikeDead(ArmyEnv e, HostSupply hs, HostId hid, ArmyHost? host, int day)
    {
        var pt = People; int n = 0; var ub = W.Get<UnrestBook>();
        foreach (int row in hs.Book.OnCampaign.Where(kv => kv.Value == hid.Value && pt.Status[kv.Key] == PersonStatus.Dead).Select(kv => kv.Key).ToList())
        {
            ulong id = pt.IdOf(row).Value;
            hs.Release(hid, [row]); n++;
            if (host != null) foreach (var c in host.Companies) if (!c.Walking && c.Men.Remove(id)) { if (id == c.Captain) c.Captain = BestOf(c); Soldiers?.Book.Rows.Remove(id); }
            LevyDied(ub, row, host?.Entry ?? 0, day);
        }
        if (n > 0) Book.Touch();
        return n;
    }

    /// <summary>A man who died on campaign: his household is aggrieved (the content kind <c>levy_death</c>), the entry of the campaign the cause.</summary>
    private void LevyDied(UnrestBook? ub, int row, ulong entry, int day)
    {
        int hh = People.Household[row]; if (ub == null || hh < 0) return;
        ub.Add(Households.IdOf(hh).Value, "levy_death", day, entry);
    }

    private void EatAndRoll(ArmyEnv e, HostSupply hs, HostId hid, HostAccount a, ArmyHost? host, HostRations st, DayResult res, int day)
    {
        long need = res.Needed.StapleG, shortG = res.ShortG[(int)SupplyNeed.Staple];
        int fed = need > 0 ? (int)Math.Clamp((need - shortG) * 1000 / need, 0, 1000) : 1000;
        bool hungry = fed < Rules.HungryBelowPm, relishOk = res.ShortG[(int)SupplyNeed.Relish] == 0;
        st.FedPm = fed; st.HungryRun = shortG > 0 ? st.HungryRun + 1 : 0;
        if (shortG > 0) st.ShortDays++;
        if (hungry) st.HungryDays++;
        var men = MenOf(hs, hid, host);
        bool unpaid = false; for (int r = 0; r < Roster.Roles; r++) if (a.Men[r] > 0 && res.UnpaidDays[r] > 0) unpaid = true;
        // the ration's shortfall comes out of each man's nutrition reserve as it does out of a household's (the death rule reads it)
        if (shortG > 0)
        {
            var econ = W.Economy; int drain = (100 * (1000 - fed) + 500) / 1000;
            foreach (var m in men) econ.NutritionCd[m.Row] = (short)Math.Max(0, econ.NutritionCd[m.Row] - drain);
        }
        // morale: the day's hunger or the relish, and the pay (the winner and loser of a battle are FoldBack's)
        int d = hungry ? Rules.MoraleHungry : (fed >= 1000 && relishOk && !unpaid ? Rules.MoraleFed : 0);
        if (unpaid) d += Rules.MoraleUnpaid;
        st.Morale = Math.Clamp(st.Morale + d, 0, 100);
        if (!NoDesertion) Desert(e, hs, hid, a, host, st, res, men, hungry, day);
    }

    // ------------------------------------------------------------------ desertion

    private void Desert(ArmyEnv e, HostSupply hs, HostId hid, HostAccount a, ArmyHost? host, HostRations st, DayResult res, List<HostMan> men, bool hungry, int day)
    {
        int doy = e.DayOfYear; bool reap = doy >= Rules.DesertHarvestFromDoy && doy <= Rules.DesertHarvestToDoy, defeat = day - st.DefeatDay <= Rules.DefeatDays;
        int beyond = Math.Max(0, a.ServiceDays - SupplyConfig.LevyFreeDays);
        var warbandDays = new Dictionary<int, int>();
        var leavers = new List<(HostMan Man, int Pm)>(); long rUnpaid = 0, rHungry = 0, rHarvest = 0, rService = 0, rDefeat = 0, rMorale = 0;
        var persons = new int[Roster.Roles];
        int extraPm = ExtraDesertPm?.Invoke(hid.Value) ?? 0;                // P7-SG-D: a winter siege's cold camp
        foreach (var m in men)
        {
            if (IsHero(m.Id) || (host != null && m.Id == host.Commander)) continue;
            if (m.Kind != 1) persons[m.Role]++;
            int arrears = m.Kind == 1 ? WarbandArrearsDays(m.Holder, warbandDays) : res.UnpaidDays[m.Role];
            bool levy = m.Kind == 0;
            int basePm = DesertionPm(Rules, arrears, hungry, levy && reap, levy ? beyond : 0, st.Morale, defeat, 0, m.Kind != 0 && !(m.Kind == 2 && m.Role == (int)TroopRole.Levy));
            basePm += extraPm;
            if (basePm <= 0) continue;
            int drill = MeleeOf(m.Row) * Rules.DrillPerSkill;
            int pm = DesertionPm(Rules, arrears, hungry, levy && reap, levy ? beyond : 0, st.Morale, defeat, drill, m.Kind != 0 && !(m.Kind == 2 && m.Role == (int)TroopRole.Levy));
            pm = Math.Min(Rules.DesertMaxPm, pm + extraPm);
            if (pm <= 0 || DetRng.Range(W.Seed, day, m.Id, RngStreams.Desertion, 0, 0, 1000) >= pm) continue;
            leavers.Add((m, pm));
            rUnpaid += Rules.DesertArrearsPm * Math.Max(0, arrears - Rules.DesertArrearsFreeDays); if (hungry) rHungry += Rules.DesertHungryPm; if (levy && reap) rHarvest += Rules.DesertHarvestPm;
            if (levy) rService += (long)Rules.DesertBeyondOwedPm * beyond; if (defeat) rDefeat += Rules.DesertDefeatPm; if (st.Morale < Rules.MoraleLow) rMorale += Rules.DesertLowMoralePm;
        }
        // the roster's men who are only counts (no person behind them): the same rule, a roll each by role and index
        var countGone = new int[Roster.Roles];
        for (int r = 0; r < Roster.Roles; r++)
        {
            int only = a.Men[r] - persons[r]; if (only <= 0) continue;
            if (r == (int)TroopRole.Levy) continue;                                         // a levy is always a person
            int pm = DesertionPm(Rules, res.UnpaidDays[r], hungry, false, 0, st.Morale, defeat, 0, true);
            if (pm <= 0) continue;
            for (int i = 0; i < only; i++)
                if (DetRng.Range(W.Seed, day, (1UL << 62) | ((ulong)hid.Value << 24) | ((ulong)r << 16) | (uint)i, RngStreams.Desertion, 0, 0, 1000) < pm) countGone[r]++;
            if (countGone[r] > 0) { rUnpaid += Rules.DesertArrearsPm * Math.Max(0, res.UnpaidDays[r] - Rules.DesertArrearsFreeDays) * countGone[r]; if (hungry) rHungry += Rules.DesertHungryPm * countGone[r]; }
        }
        int countsTotal = countGone.Sum();
        if (leavers.Count == 0 && countsTotal == 0) return;

        int place = a.PlaceSettlement >= 0 ? a.PlaceSettlement : host?.Point ?? -1;
        var home = new List<(HostMan Man, int Settlement)>(); var road = new List<HostMan>();
        foreach (var (m, _) in leavers)
        {
            var (dest, hs0) = Decide(m, place, day);
            if (dest == DesertDest.Home) home.Add((m, hs0)); else road.Add(m);
        }
        // the men for the roads: a band near takes them, else two or more make one, a lone man goes home if he has one (a man from outside has none: he stays)
        BanditBand? join = road.Count > 0 ? NearestBand(place) : null;
        if (join == null && road.Count == 1)
        {
            var one = road[0]; road.Clear();
            int hh = People.Household[one.Row];
            if (hh >= 0) home.Add((one, Households.Settlement[hh]));
        }
        var gone = new List<HostMan>(); gone.AddRange(home.Select(x => x.Man)); gone.AddRange(road);
        // the wave is written first: the bandits' entry stands on it
        int total = gone.Count + countsTotal;
        if (total == 0) return;
        EntityId wave = EntityId.None;
        ulong lord = host != null ? host.Commander : a.Lord;
        if (lord != 0)
        {
            var cause = new Cause(CauseKind.Desertion, host is { Entry: not 0 } ? new EntityId(host.Entry) : null, "", ReasonKey(rUnpaid, rHungry, rHarvest, rService, rDefeat, rMorale));
            wave = Chron(e, "desertion_wave", "ch_am_desertion", [ChronParam.Person("ruler", new EntityId(lord)), ChronParam.Place("place", PlaceId(place)), ChronParam.Count("men", total)], [new EntityId(lord), PlaceId(place)], cause);
        }
        foreach (var (m, s) in home) LeaveHost(e, hs, hid, a, host, m, s, place, day);
        foreach (var m in road) LeaveHost(e, hs, hid, a, host, m, -1, place, day);
        if (road.Count > 0)
        {
            var ids = road.Select(m => m.Id).ToList();
            if (join != null) JoinBand(join, ids); else FormBand(e, ids, place, BandOrigin.Deserters, wave == EntityId.None ? null : wave);
        }
        for (int r = 0; r < Roster.Roles; r++) if (countGone[r] > 0) hs.DesertCounts(hid, (TroopRole)r, countGone[r]);
        st.DesertedPersons += gone.Count; st.DesertedCounts += countsTotal;
        Book.Touch();
    }

    private int WarbandArrearsDays(int wbId, Dictionary<int, int> cache)
    {
        if (cache.TryGetValue(wbId, out int v)) return v;
        int days = 0;
        if (Book.Warbands.TryGetValue(wbId, out var wb) && wb.ArrearsMites > 0) { long roll = WarbandRoll(wb); days = roll > 0 ? (int)Math.Min(1000, wb.ArrearsMites / roll) : 0; }
        return cache[wbId] = days;
    }

    /// <summary>Where a deserter goes (4.8): home when it lies within two days' walk and he has 50 cp or two days of food there; with neither, half of them take to the roads (stream 0x7A1, key person and day); from further off, or with no home at all, all of them.</summary>
    private (DesertDest Dest, int Settlement) Decide(HostMan m, int place, int day)
    {
        int hh = People.Household[m.Row];
        if (hh < 0) return (DesertDest.Band, -1);
        int homeS = Households.Settlement[hh];
        if (place < 0 || (uint)homeS >= (uint)Region.Settlements.Count) return (DesertDest.Band, -1);
        if (DistM(homeS, place) > Rules.DeserterHomeM) return (DesertDest.Band, -1);
        var econ = W.Economy;
        bool has = econ.Ledger.Balance(econ.HouseholdPurse[hh]).Mites >= Rules.DesertHomeCoinMites || econ.FoodCoverDaysX10[hh] >= 20;
        if (has) return (DesertDest.Home, homeS);
        return DetRng.Range(W.Seed, day, m.Id, RngStreams.DeserterDrift, 7, 0, 1000) < Rules.DesertNothingBandPm ? (DesertDest.Band, -1) : (DesertDest.Home, homeS);
    }

    private BanditBand? NearestBand(int place)
    {
        BanditBand? best = null; int bd = int.MaxValue;
        if (place < 0) return null;
        foreach (var b in Book.Bands.Values)
        {
            if (b.Lair < 0 || b.Members.Count == 0) continue;
            int d = DistM(b.Lair, place); if (d > Rules.DesertJoinBandM) continue;
            if (d < bd) { bd = d; best = b; }
        }
        return best;
    }

    /// <summary>
    /// The man leaves the host's books: a levy is struck off the supply book (his arrears forgiven) and the company, his own kit goes home with him; a warband man is let go (his trade given back); a man from outside is struck off the
    /// roster. A man bound home with a company to walk in is put in a walking company (his labour is away until he arrives); one for the roads is taken in by the band.
    /// </summary>
    private void LeaveHost(ArmyEnv e, HostSupply hs, HostId hid, HostAccount a, ArmyHost? host, HostMan m, int homeSettlement, int place, int day)
    {
        switch (m.Kind)
        {
            case 0:
                hs.Desert(hid, [m.Row]);
                if (host != null) foreach (var c in host.Companies) if (!c.Walking && c.Men.Remove(m.Id)) { if (m.Id == c.Captain) c.Captain = BestOf(c); break; }
                if (homeSettlement >= 0) GiveKitBackOne(m.Id);
                break;
            case 1:
                if (Book.Warbands.TryGetValue(m.Holder, out var wb)) Release(wb, m.Id);
                break;
            default:
                if (Book.Marches.TryGetValue(hid.Value, out var mc)) mc.Outsiders.Remove(m.Id);
                a.Men[m.Role] = Math.Max(0, a.Men[m.Role] - 1); a.Outside = Math.Max(0, a.Outside - 1);
                break;
        }
        if (homeSettlement >= 0 && host != null) WalkHomeCompany(host, homeSettlement, place, m.Id, day);
    }

    /// <summary>A deserter walking home: the company of those who left to the same village that day (a levy company that is already walking), so the household is without him, and the farming planner's away list says so, until he is there.</summary>
    private void WalkHomeCompany(ArmyHost host, int homeSettlement, int from, ulong person, int day)
    {
        int arrive = day + WalkDays(from, homeSettlement);
        foreach (var c in host.Companies)
            if (c.Walking && c.Quota == 0 && c.Seat == homeSettlement && c.HomeDay == arrive) { c.Men.Add(person); c.Men.Sort(); return; }
        var w = new LevyCompany { Fief = Region.Settlements[homeSettlement].FiefIndex, Seat = homeSettlement, Quota = 0, Heard = -1, Drawn = day, Arrives = day, HomeDay = arrive, Walking = true, Kind = ContingentKind.Levy };
        w.Men.Add(person); host.Companies.Add(w);
    }

    // ------------------------------------------------------------------ tomorrow's notes to the economy

    private void Tomorrow(ArmyEnv e, HostSupply hs, HostId hid, HostAccount a, ArmyHost? host, HostRations st, DayResult? res, int day)
    {
        foreach (var (p, until) in st.Wounded.ToList()) { int r = RowOf(p); if (day >= until || r < 0 || People.Status[r] != PersonStatus.Alive) st.Wounded.Remove(p); }
        if (host == null) return;
        // P8-WT-1: an AI lord's host of a war with under 10 days of food in its baggage (7 for a cruel or a poor lord) may purvey on its own realm's land when the depot, the market and the sutlers leave it short (the ladder reaches the step only then)
        if (res != null && res.Day == day && host.War >= 0 && !IsHero(host.Commander))
        {
            bool hard = (Ravensguard.Sim.Politics.LordTraits.Of(W.Seed, host.Commander) & Ravensguard.Sim.Politics.LordTrait.Cruel) != 0 || res.DaysFundedMilli < 10_000;
            bool purvey = res.DaysOfFoodMilli < (hard ? SupplyConfig.PurveyBelowDaysHard : SupplyConfig.PurveyBelowDays) * 1000L;
            if (a.Purveyance != purvey) hs.SetPurveyance(hid, purvey);
        }
        // the joined warbands' men eat at the host's table
        int retainers = 0;
        foreach (var j in host.Warbands) if (Book.Warbands.TryGetValue(j.Warband, out var wb) && Joined(host, wb)) retainers += wb.Members.Count(m => RowOf(m) >= 0 && People.IsAlive(RowOf(m)));
        if (a.Retainers != retainers) hs.SetRetainers(hid, retainers);
        if (host.State == HostState.Retreating && (!Book.Marches.TryGetValue(hid.Value, out var rm) || rm.State is MarchState.Arrived or MarchState.Left)) host.State = HostState.Camped;
        // the ground it stands on: enemy land is plundered, the strip limit cuts a third day's yield
        var want = PolicyAt(host, a.PlaceSettlement);
        if (a.Forage != want) hs.SetForage(hid, want);
        int place = a.PlaceSettlement;
        if (res != null && res.Day == day && a.Forage != ForagePolicy.None && (res.DeliveredFrom(SupplySource.Forage) > 0 || res.ShortG[(int)SupplyNeed.Staple] > 0))
        {                                                                                           // a day on the same ground living off it (what it gave, or its stores found empty)
            if (st.StripPlace == place) st.StripDays++; else { st.StripPlace = place; st.StripDays = 1; }
        }
        else if (st.StripPlace != place) { st.StripPlace = place; st.StripDays = 0; }
        int cut = st.StripPlace == place && st.StripDays >= Rules.ForageStripDays ? Rules.ForageStripCutPm : 0;
        if (a.ForageCutPm != cut) hs.SetForageCut(hid, cut);
    }

    /// <summary>The foraging policy of the ground a host stands on (4.6): the land of a realm it is at war with is plundered, any other is not foraged (with the ruler's warrant the economy buys by purveyance; the brain that grants it is AM-E's).</summary>
    public ForagePolicy PolicyAt(ArmyHost host, int settlement)
    {
        var rb = Rule; var pb = Politics;
        if (rb == null || pb == null || host.Realm < 0 || (uint)settlement >= (uint)Region.Settlements.Count) return ForagePolicy.None;
        int fi = Region.Settlements[settlement].FiefIndex;
        int owner = fi >= 0 && rb.Fiefs.TryGetValue(fi, out var f) ? f.Realm : -1;
        return owner >= 0 && owner != host.Realm && pb.WarOf(host.Realm, owner) != null ? ForagePolicy.EnemyLand : ForagePolicy.None;
    }

    // ------------------------------------------------------------------ forage parties (4.6, overview 8.2: who goes)

    /// <summary>
    /// The men who go out foraging on a day: a tenth of the host's men (the economy's rule counts the same tenth), taken from the host's men in their order by a rotation that moves on the day, so every man takes his turn; the carried
    /// wounded stay in camp. Pure: the same host and day give the same men. The live foragers of a bubble are these.
    /// </summary>
    public List<ulong> ForageParty(ArmyHost host, int day)
    {
        var hs = W.Get<HostSupply>(); var party = new List<ulong>(); if (hs == null || host.Supply == 0 || !hs.Exists(new HostId(host.Supply))) return party;
        var hid = new HostId(host.Supply); var a = hs.Account(hid);
        Book.Rations.TryGetValue(hid.Value, out var st);
        var men = MenOf(hs, hid, host).Where(m => st == null || !st.Wounded.ContainsKey(m.Id)).Select(m => m.Id).ToList();
        int n = (int)((long)(a.Total + a.Retainers) * SupplyConfig.ForageSharePm / 1000);
        n = Math.Min(n, men.Count); if (n <= 0) return party;
        int start = (int)((long)day * n % men.Count);
        for (int i = 0; i < n; i++) party.Add(men[(start + i) % men.Count]);
        return party;
    }

    // ------------------------------------------------------------------ after a battle (4.9)

    /// <summary>What a battle did to a host (the numbers the tests and the chronicle read).</summary>
    public sealed record FoldResult(int Before, int Dead, int Captured, int Carried, int LeftBehind, int Routed, int Left, bool Retreating, bool Disbanded);

    /// <summary>
    /// A battle, a raid or a siege assault is over and the host fights on only with who is left (4.9): <paramref name="commands"/> are the battle's status commands for the persons of this host (the dead were killed by the battle with
    /// their chain; the host's rolls are cut to match, their households bearing <c>levy_death</c>); the captured leave the rolls and are away from their fields until a ransom or a release (a month); the wounded ride in the baggage's
    /// carts (four to a cart) and are fit again after the carry days, the rest are left at the nearest village and walk home when they can; the routed stay on the rolls but the host is a beaten one for the next days (desertion). Morale
    /// of the winner rises 15, the loser's falls 25. A host that lost more than half its men turns back to its muster point; under a quarter left it is disbanded where it stands, the levies walking home.
    /// </summary>
    public FoldResult FoldBack(ArmyEnv e, ArmyHost host, IEnumerable<BattleCommand> commands, bool won)
    {
        var hs = Supply; int day = (int)e.Day;
        if (hs == null || host.Supply == 0 || !hs.Exists(new HostId(host.Supply))) return new FoldResult(0, 0, 0, 0, 0, 0, 0, false, false);
        var hid = new HostId(host.Supply); var a = hs.Account(hid); var st = RationsOf(hid.Value); var pt = People; var ub = W.Get<UnrestBook>();
        var mine = MenOf(hs, hid, host).ToDictionary(m => m.Id);
        int dead = DeadOnRolls(hs, hid, host), captured = 0, carried = 0, left = 0, routed = 0, before = mine.Count + dead;
        // the dead (and the people who died outside the commands: sickness in the line) leave the rolls first
        var status = new SortedDictionary<ulong, SoldierStatus>(); var wounded = new SortedSet<ulong>();
        foreach (var c in commands)
        {
            if (c.Kind == BattleCommandKind.Status) status[c.Person] = c.Status;
            else if (c.Kind == BattleCommandKind.Wounds && c.Book is { Dead: false }) wounded.Add(c.Person);
        }
        foreach (var (id, s) in status) if (s == SoldierStatus.Down) wounded.Add(id);
        StrikeDead(e, hs, hid, host, day);
        foreach (var j in host.Warbands) if (Book.Warbands.TryGetValue(j.Warband, out var jw)) PruneDead(jw);
        if (Book.Marches.TryGetValue(hid.Value, out var mc)) SyncDead(hs, mc);
        foreach (var (id, m) in mine.OrderBy(x => x.Key))
        {
            status.TryGetValue(id, out var s);
            if (s is SoldierStatus.Captured or SoldierStatus.Surrendered)
            {
                LeaveHost(e, hs, hid, a, host, m, m.Kind == 2 ? -1 : HomeOf(m), a.PlaceSettlement, day);
                AwayUntil(host, m, day + Rules.CapturedAwayDays); captured++;
            }
            else if (s == SoldierStatus.Fled) routed++;
            else if (wounded.Contains(id))
            {
                int room = a.Carts * Rules.WoundedPerCart - st.Wounded.Count;
                if (room > 0) { st.Wounded[id] = day + Rules.WoundedCarryDays; carried++; }
                else { LeaveHost(e, hs, hid, a, host, m, m.Kind == 2 ? -1 : HomeOf(m), a.PlaceSettlement, day); AwayUntil(host, m, day + Rules.WoundedAwayDays); left++; }
            }
        }
        int after = MenOf(hs, hid, host).Count;
        st.Morale = Math.Clamp(st.Morale + (won ? Rules.MoraleWin : Rules.MoraleLose), 0, 100);
        if (!won) st.DefeatDay = day;
        int lost = before - after; bool retreat = false, disband = false;
        if (before > 0 && !won)
        {
            if (after * 100 < before * Rules.DisbandLeftPct) disband = true;
            else if (lost * 100 > before * Rules.RetreatLostPct) retreat = true;
        }
        if (disband) Release(e, hs, host, "ch_note_am_defeat");
        else if (retreat)
        {
            host.State = HostState.Retreating;
            int at = a.PlaceSettlement; if (at >= 0 && at != host.Point && BeginMarch(e, hid, [at, host.Point], "ch_note_am_retreat") == null) host.State = HostState.Camped;
        }
        Book.Touch();
        return new FoldResult(before, dead, captured, carried, left, routed, after, retreat, disband);
    }

    /// <summary>The dead still on a host's rolls (levies, joined warbands' men, men from outside): what a battle or the fever killed.</summary>
    private int DeadOnRolls(HostSupply hs, HostId hid, ArmyHost host)
    {
        var pt = People; int n = hs.Book.OnCampaign.Count(kv => kv.Value == hid.Value && pt.Status[kv.Key] == PersonStatus.Dead);
        foreach (var j in host.Warbands) if (Book.Warbands.TryGetValue(j.Warband, out var wb)) foreach (ulong m in wb.Members) { int r = RowOf(m); if (r < 0 || pt.Status[r] == PersonStatus.Dead) n++; }
        if (Book.Marches.TryGetValue(hid.Value, out var mc)) foreach (var id in mc.Outsiders.Keys) { int r = RowOf(id); if (r >= 0 && pt.Status[r] == PersonStatus.Dead) n++; }
        return n;
    }

    private int HomeOf(HostMan m) { int hh = People.Household[m.Row]; return hh >= 0 ? Households.Settlement[hh] : -1; }

    /// <summary>A man taken or left behind is away from his fields until the day (his household's labour is lost to it): a walking company marks him, as a deserter's.</summary>
    private void AwayUntil(ArmyHost host, HostMan m, int until)
    {
        foreach (var c in host.Companies) if (c.Walking && c.Men.Contains(m.Id)) { c.HomeDay = Math.Max(c.HomeDay, until); return; }
        if (m.Kind == 2) return;                                                                  // a man from outside has no fields
        int home = HomeOf(m); if (home < 0) return;
        var w = new LevyCompany { Fief = Region.Settlements[home].FiefIndex, Seat = home, Quota = 0, Heard = -1, Drawn = (int)(until - 1), Arrives = until, HomeDay = until, Walking = true, Kind = ContingentKind.Levy };
        w.Men.Add(m.Id); host.Companies.Add(w);
    }

    /// <summary>The men who can still fight: the roll less the wounded the wagons carry.</summary>
    public int FitMen(ArmyHost host)
    {
        var hs = W.Get<HostSupply>(); if (hs == null || host.Supply == 0 || !hs.Exists(new HostId(host.Supply))) return 0;
        var hid = new HostId(host.Supply); int n = MenOf(hs, hid, host).Count;
        return Book.Rations.TryGetValue(hid.Value, out var st) ? Math.Max(0, n - st.Wounded.Count) : n;
    }
}
