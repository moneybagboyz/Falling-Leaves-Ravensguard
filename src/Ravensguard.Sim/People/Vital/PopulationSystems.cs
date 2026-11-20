using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.People.Vital;

/// <summary>H4 slice: the daily needs of each household whose index mod 24 is the hour (households spec 3.2) and one day of spoilage in its store.</summary>
public sealed class HouseholdNeedsSystem : AbstractSystem
{
    private long _day = -1; private int _tMean;
    private ushort _veg = ushort.MaxValue, _eggs, _wood, _barley;

    public HouseholdNeedsSystem() : base("household-needs", TickPhase.H4DailySlice, SystemOrder.HouseholdNeeds) { }

    public override void Step(TickContext ctx)
    {
        var w = ctx.World; var e = w.Economy; var hht = w.Households;
        if (_day != ctx.Day) { _day = ctx.Day; _tMean = w.Env.TMeanC(ctx.Day); }
        if (_veg == ushort.MaxValue) { _veg = e.Goods.IdOf("vegetables"); _eggs = e.Goods.IdOf("eggs"); _wood = e.Food.Firewood; _barley = e.Goods.IdOf("barley"); }
        int day = (int)ctx.Day, n = hht.Count;
        for (int hh = ctx.HourOfDay; hh < n; hh += 24)
        {
            if (hht.MemberCount[hh] == 0) continue;
            if (w.Get<EconomyWorld>() != null) { HouseholdNeeds.Produce(e, hht, hh, ctx.DayOfYear, _veg, _eggs, _wood, _tMean); HouseholdNeeds.HomeBrew(e, w.People, hht, hh, day, _barley); }
            HouseholdNeeds.RunDay(e, w.People, hht, hh, day, _tMean);
            HouseholdNeeds.RollDurables(e, hht, hh, w.Seed, ctx.Hour);
            e.HouseholdStore[hh].ApplySpoilage(w.Seed, ctx.Hour, _tMean, 1, e.SpoilagePct);
        }
    }
}

/// <summary>
/// H4 slice, after the needs: births, conceptions and death rolls of the people of each sliced household (population spec 3.1, 3.2). Births come before
/// deaths so a mother who dies the day she gives birth still bears the child (spec 7). Every roll is a pure function of (seed, hour, person, stream).
/// </summary>
public sealed class VitalRollsSystem : AbstractSystem
{
    private readonly List<int> _members = [], _dead = [];

    public VitalRollsSystem() : base("vital-rolls", TickPhase.H4DailySlice, SystemOrder.VitalRolls) { }

    public override void Step(TickContext ctx)
    {
        var w = ctx.World; var hht = w.Households; int n = hht.Count;
        for (int hh = ctx.HourOfDay; hh < n; hh += 24) if (hht.MemberCount[hh] > 0) StepHousehold(ctx, hh);
    }

    private void StepHousehold(TickContext ctx, int hh)
    {
        var w = ctx.World; var pt = w.People; var hht = w.Households; var rates = w.Rates; var e = w.Economy; var v = w.Vital;
        int day = (int)ctx.Day; long hour = ctx.Hour; ulong seed = w.Seed;
        int s = hht.Settlement[hh];
        bool town = w.Population.Region.Settlements[s].Class == SettlementClass.Town;
        int beds = hht.Beds[hh];

        Snapshot(hht, hh);
        // --- women: births first, then conceptions (ascending person index)
        foreach (int p in _members)
        {
            if (pt.Sex[p] != Sex.Female || pt.Status[p] != PersonStatus.Alive) continue;
            if (pt.PregnantUntilDay[p] >= 0)
            {
                if (pt.PregnantUntilDay[p] <= day) Demography.Birth(w, p, ctx.Time, ctx.Outbox);
                continue;
            }
            int sp = pt.Spouse[p];
            if (sp < 0 || pt.Status[sp] != PersonStatus.Alive || pt.Household[sp] != hh) continue;
            int age = w.AgeOn(p, day);
            if (age < rates.FertilityAgeMin || age > rates.FertilityAgeMax || day - pt.LastBirthDay[p] < rates.PostpartumDays) continue;
            int ppm = VitalMath.ConceptionPpm(rates, e.NutritionCd[p], VitalMath.Crowding(hht.MemberCount[hh], beds));
            if (VitalMath.Roll(seed, hour, pt.IdOf(p).Value, VitalStreams.Conception, 0, ppm)) { pt.PregnantUntilDay[p] = day + rates.PregnancyDays; pt.FatherOfUnborn[p] = sp; }
        }

        // --- deaths: roll everyone alive (newborns included) against the state at the start of the roll, then apply in ascending order
        Snapshot(hht, hh);
        int crowding = VitalMath.Crowding(hht.MemberCount[hh], beds);
        bool cold = (e.Grievances[hh] & Grievance.Cold) != 0;
        int outbreak = v.OutbreakEndDay[s] > day ? v.OutbreakMult[s] : 1000;
        bool physician = outbreak > 1000 && w.Get<Ravensguard.Sim.Medicine.PhysicianBook>() is { IsEmpty: false } pbook && pbook.Physicians(s) > 0;
        _dead.Clear();
        var causes = new List<DeathCause>();
        foreach (int p in _members)
        {
            if (pt.Status[p] != PersonStatus.Alive) continue;
            int age = w.AgeOn(p, day), cd = e.NutritionCd[p];
            int om = outbreak > 1000 && (cd < 2500 || age < 5 || age >= 60) ? outbreak : 1000;
            if (om > 1000 && physician) om = 1000 + (om - 1000) * (100 - Ravensguard.Sim.Disease.DiseaseRules.Current.S.AbstractPhysicOutbreakCutPct) / 100;       // P6-C4 (disease 4.1): where a physician treats, a sick household's outbreak multiplier is a quarter lower
            var hz = VitalMath.Hazard(rates, age, cd, crowding, town, cold, om);
            ulong id = pt.IdOf(p).Value;
            if (age >= rates.MaxAgeYears) { _dead.Add(p); causes.Add(DeathCause.Age); continue; }
            if (!VitalMath.Roll(seed, hour, id, VitalStreams.Death, 0, hz.TotalPpm)) continue;
            _dead.Add(p);
            causes.Add(hz.Pick(DetRng.Range(seed, hour, id, VitalStreams.Death, 1, 0, Math.Max(1, hz.TotalPpm))));
        }
        for (int k = 0; k < _dead.Count; k++) Demography.Kill(w, _dead[k], causes[k], ctx.Time, ctx.Outbox);
    }

    private void Snapshot(People.HouseholdTable hht, int hh)
    {
        _members.Clear();
        int st = hht.MemberStart[hh], c = hht.MemberCount[hh];
        for (int k = 0; k < c; k++) _members.Add(hht.MemberIdx[st + k]);
        _members.Sort();
    }
}

/// <summary>H5, hour 0: the settlement daily globals of the population spec: yearly ledger, outbreaks (3.6), famine detection (3.5) and the weekly marriage catch-up (3.3).</summary>
public sealed class PopulationDailySystem : AbstractSystem
{
    private int[] _hungry = [], _total = [];

    public PopulationDailySystem() : base("population-daily", TickPhase.H5DailyGlobals, SystemOrder.DailyGlobalsPopulation) { }

    public override void Step(TickContext ctx)
    {
        var w = ctx.World;
        w.Vital.Year(ctx.Year);
        Outbreaks(ctx);
        Famine(ctx);
        if (ctx.DayOfYear % 5 == 0) Marriage.Run(w, ctx.Time, ctx.Outbox, seasonal: false);
    }

    private static void Outbreaks(TickContext ctx)
    {
        var w = ctx.World; var v = w.Vital; var r = w.Rates; int day = (int)ctx.Day;
        var settlements = w.Population.Region.Settlements;
        for (int s = 0; s < settlements.Count; s++)
        {
            if (v.OutbreakEndDay[s] > day) continue;
            if (v.OutbreakEndDay[s] != 0) { v.OutbreakEndDay[s] = 0; v.OutbreakMult[s] = 1000; v.OutbreakEntry[s] = 0; }
            bool town = settlements[s].Class == SettlementClass.Town;
            int ppm = ctx.Season switch
            {
                Season.Winter => town ? r.OutbreakWinterTownPpm : r.OutbreakWinterVillagePpm,
                Season.Summer => town ? r.OutbreakSummerTownPpm : 0,
                _ => 0,
            };
            ulong id = settlements[s].Id.Value;
            if (!VitalMath.Roll(w.Seed, ctx.Hour, id, VitalStreams.Outbreak, 0, ppm)) continue;
            int len = DetRng.Range(w.Seed, ctx.Hour, id, VitalStreams.Outbreak, 1, 8, 21);
            bool severe = DetRng.Range(w.Seed, ctx.Hour, id, VitalStreams.Outbreak, 2, 0, 100) < r.OutbreakSeverePct;
            v.OutbreakEndDay[s] = day + len; v.OutbreakMult[s] = severe ? r.OutbreakSevereMultPermille : r.OutbreakMildMultPermille;
            var eid = ctx.Outbox.Chronicle(ctx.Time, "outbreak", [settlements[s].Id], [new Cause(CauseKind.Disease, null, ctx.Season == Season.Winter ? "winter coughs and fevers" : "summer flux")],
                ("settlement", settlements[s].Name), ("days", len.ToString()), ("severity", severe ? "severe" : "mild"));
            v.OutbreakEntry[s] = eid.Value;
        }
    }

    private void Famine(TickContext ctx)
    {
        var w = ctx.World; var v = w.Vital; var r = w.Rates; var hht = w.Households; var e = w.Economy;
        var settlements = w.Population.Region.Settlements;
        if (_hungry.Length != settlements.Count) { _hungry = new int[settlements.Count]; _total = new int[settlements.Count]; }
        Array.Clear(_hungry); Array.Clear(_total);
        for (int hh = 0; hh < hht.Count; hh++)
        {
            if (hht.MemberCount[hh] == 0) continue;
            int s = hht.Settlement[hh];
            _total[s]++;
            if (e.FoodCoverDaysX10[hh] < 30) _hungry[s]++;
        }
        for (int s = 0; s < settlements.Count; s++)
        {
            bool hit = _total[s] > 0 && _hungry[s] * 100 >= r.FamineSharePct * _total[s];
            if (hit) { v.FamineStreak[s]++; v.QuietStreak[s] = 0; }
            else { v.QuietStreak[s]++; v.FamineStreak[s] = 0; }
            if (v.FamineEntry[s] != 0)
            {
                if (v.QuietStreak[s] >= r.FamineQuietDays) v.FamineEntry[s] = 0;
                continue;
            }
            if (v.FamineStreak[s] < r.FamineDays) continue;
            var cause = FamineCause(w, ctx.Time, _hungry[s], _total[s]);
            var id = ctx.Outbox.Chronicle(ctx.Time, "famine", [settlements[s].Id], [cause], ("settlement", settlements[s].Name), ("hungry_households", _hungry[s].ToString()),
                ("households", _total[s].ToString()));
            v.FamineEntry[s] = id.Value;
            ctx.Outbox.Count("famines");
        }
    }

    /// <summary>
    /// The cause of a famine (population spec 3.5): the weather, war or raid entry of the last 120 days that appears most (a farming loss is a Weather entry),
    /// else <see cref="CauseKind.MarketDrift"/> with the hungry share as data.
    /// </summary>
    public static Cause FamineCause(SimWorld w, GameTime now, int hungry, int total)
    {
        long cutoff = now.Seconds - 120 * TimeConst.SecondsPerDay;
        var tally = new Dictionary<CauseKind, (int N, EntityId Last)>();
        var all = w.Chronicle.All;
        for (int i = all.Count - 1; i >= 0 && all[i].When.Seconds >= cutoff; i--)
        {
            var en = all[i];
            if (en.TypeId is "death" or "birth" or "marriage" or "outbreak" or "famine" or "guardianship" or "escheat") continue;
            foreach (var c in en.Causes)
                if (c.Kind is CauseKind.Weather or CauseKind.War or CauseKind.Raid)
                {
                    var t = tally.GetValueOrDefault(c.Kind);
                    if (t.N == 0) t.Last = en.Id;
                    tally[c.Kind] = (t.N + 1, t.Last);
                }
        }
        foreach (var k in new[] { CauseKind.Weather, CauseKind.War, CauseKind.Raid })
            if (tally.TryGetValue(k, out var t) && (tally.Count(x => x.Value.N > t.N) == 0)) return new Cause(k, t.Last, $"{t.N} such events in the last 120 days");
        return new Cause(CauseKind.MarketDrift, null, $"{hungry} of {total} households under 3 days of food: supply fell short of demand");
    }
}

/// <summary>H6 / S6 on the first day of a season (hour 0): marriage matching and the dissolution sweep, and the yearly tidy of the household member list.</summary>
public sealed class PopulationSeasonalSystem : AbstractSystem
{
    public PopulationSeasonalSystem() : base("population-seasonal", TickPhase.H6Seasonal, SystemOrder.S6Population) { }

    public override void Step(TickContext ctx)
    {
        if (ctx.HourOfDay != 0 || !ctx.IsSeasonStartDay) return;
        var w = ctx.World; var hht = w.Households;
        Marriage.Run(w, ctx.Time, ctx.Outbox, seasonal: true);
        // a household without members gives its dwelling back (normally done at the death that emptied it)
        for (int hh = 0; hh < hht.Count; hh++) if (hht.MemberCount[hh] == 0) Demography.VacateDwelling(w, hh);
        if (hht.MemberIdx.Count > 2L * w.Vital.Alive + 4096) hht.CompactMembers();
    }
}

/// <summary>
/// H6 / S7, after the marriages (population spec 3.7, reduced to what a closed region needs): households crowded past 150 percent of their beds, and unmarried
/// adults who waited four seasons for a home, each leave the region with 40 percent chance per season. Households take their coin and stock with them (to the
/// continent purse, so the coin audit stays exact); single emigrants carry nothing. There is no immigration yet: the continent tick (Phase 2 step 7) owns the inflow.
/// </summary>
public sealed class PopulationMigrationSystem : AbstractSystem
{
    public PopulationMigrationSystem() : base("population-migration", TickPhase.H6Seasonal, SystemOrder.S7Migration) { }

    public override void Step(TickContext ctx)
    {
        if (ctx.HourOfDay != 0 || !ctx.IsSeasonStartDay) return;
        var w = ctx.World; var pt = w.People; var hht = w.Households; var r = w.Rates;
        int ppm = r.EmigrateChancePct * 10_000;
        for (int hh = 0; hh < hht.Count; hh++)
        {
            if (hht.MemberCount[hh] == 0 || hht.MemberCount[hh] * 100 <= hht.Beds[hh] * r.EmigrateCrowdingOverPct) continue;
            if (VitalMath.Roll(w.Seed, ctx.Hour, hht.IdOf(hh).Value, VitalStreams.Migrant, 0, ppm)) Demography.DepartHousehold(w, hh, ctx.Time, ctx.Outbox);
        }
        int day = (int)ctx.Day;
        for (int p = 0; p < pt.Count; p++)
        {
            if (pt.Status[p] != PersonStatus.Alive || pt.MarriageWait[p] < r.EmigrateAfterWaitSeasons || pt.Spouse[p] >= 0) continue;
            int hh = pt.Household[p];
            if (hh < 0 || hht.Head[hh] == p || pt.PregnantUntilDay[p] >= 0) continue;
            if (VitalMath.Roll(w.Seed, ctx.Hour, pt.IdOf(p).Value, VitalStreams.Migrant, 1, ppm)) Demography.Depart(w, p, ctx.Time, ctx.Outbox);
        }
        _ = day;
        Immigrate(ctx);
    }

    /// <summary>
    /// Immigration from the continent tick (spec 3.7, replaces the stand-in below when the region has a continent): the tick has posted migrants for this region (persons, the coin and
    /// grain they bring, already taken out of the neighbour's summary); departed households come back first, then new families settle in vacant dwellings. What the region
    /// takes is booked (<see cref="Ravensguard.Sim.Continental.RegionLink.TakenPersons"/>); the rest goes back at the next tick.
    /// </summary>
    private static void ImmigrateFromContinent(TickContext ctx, Ravensguard.Sim.Economy.Markets.ContinentLink link)
    {
        var w = ctx.World; var pt = w.People; var hht = w.Households; var rl = link.Mine;
        // the region's own quota, as the stand-in had it: a sixth of the shortfall, at most 1.2 percent of the starting head count; the coin and grain of the stake come from the continent
        int left = (int)ImmigrationQuota(w); if (left <= 0) return;
        var edge = rl.Edges.FirstOrDefault(e => !link.State.Summaries[e.Neighbour].Detailed);
        if (edge == null) return;                                // no road to a region the continent holds coin for
        long coinHead = w.Economy.Tuning.ImmigrantStakeMitesPerHead, measuresHead = w.Economy.Tuning.ImmigrantStakeMeasuresPerHead;   // review finding 12: content, not constants
        Demography.Stake StakeFor(int n) => new(coinHead * n, measuresHead * n);
        void Took(int n)
        {
            rl.TotalCame += n; rl.TotalPosted += n;
            { edge.CoinNetIn += coinHead * n; edge.StockNetOut[0] -= measuresHead * n * 1_000_000 / Ravensguard.Sim.Continental.ContinentRules.MeasuresPerKgX1000; }
        }
        var byHousehold = new SortedDictionary<int, List<int>>();
        for (int p = 0; p < pt.Count; p++)
        {
            if (pt.Status[p] != PersonStatus.Departed) continue;
            int hh = pt.Household[p]; if (hh < 0 || hht.MemberCount[hh] != 0) continue;
            if (!byHousehold.TryGetValue(hh, out var l)) byHousehold[hh] = l = [];
            l.Add(p);
        }
        foreach (var (hh, members) in byHousehold)
        {
            if (left < members.Count) continue;
            if (Demography.ReturnHousehold(w, hh, members, ctx.Time, ctx.Outbox, StakeFor(members.Count))) { Took(members.Count); left -= members.Count; }
        }
        // singles who left a household that still stands come home to it when it has a bed
        for (int p = 0; p < pt.Count && left > 0; p++)
        {
            if (pt.Status[p] != PersonStatus.Departed) continue;
            int hh = pt.Household[p]; if (hh < 0 || hht.MemberCount[hh] == 0 || hht.MemberCount[hh] >= hht.Beds[hh]) continue;
            if (Demography.ReturnPerson(w, p, hh, ctx.Time, ctx.Outbox, StakeFor(1))) { Took(1); left--; }
        }
        int salt = 0;
        while (left >= 2)
        {
            int n = Math.Min(left, 3 + (salt % 3));
            if (Demography.SettleFamily(w, n, StakeFor, ctx.Time, ctx.Outbox, salt++) < 0) { break; }
            int settled = Demography.LastSettled;
            Took(settled); left -= settled;
        }
    }

    /// <summary>
    /// How many people may come to the region this season (background growth, decision G9; replaces the cap of integration decision I27, which let people in only while the region
    /// held fewer than it started with). Two pulls: the shortfall against the starting head count, a sixth of it a season as before, and the free dwellings the region's own
    /// waiting couples do not need, a <see cref="GrowthTuningDef.ImmigrationSurplusDivisor"/>th of them a season, but only while jobs are there (idle adults under
    /// <see cref="GrowthTuningDef.ImmigrationMaxIdlePermille"/> of the people). Never more than <see cref="GrowthTuningDef.ImmigrationMaxPermilleOfAlive"/> of the people in a season.
    /// </summary>
    public static long ImmigrationQuota(SimWorld w)
    {
        var g = w.Get<Ravensguard.Sim.Abstract.EconomyWorld>()?.Content.Growth ?? new Ravensguard.Content.GrowthTuningDef { Id = "growth" };
        var v = w.Vital; var pt = w.People; var hht = w.Households; var pop = w.Population; long alive = v.Alive;
        long start = v.Years.Count > 0 ? v.Years[0].PopulationAtStart : alive;
        long shortfall = Math.Max(0, start - alive);
        long pull = shortfall > 0 ? Math.Max(1, shortfall / ShortfallDivisor) : 0;
        int vacant = 0;
        for (int s = 0; s < pop.Region.Settlements.Count; s++)
        {
            var lots = pop.Region.Settlements[s].Plan.Lots;
            for (int l = 0; l < lots.Count; l++) if (pop.LotBeds[s][l] >= 2 && pop.LotHousehold[s][l] < 0 && lots[l].IsDwelling && lots[l].Dwelling != DwellingType.Manor) vacant++;
        }
        long waiting = 0, idle = 0; long day = w.State.HourDone / 24;
        for (int p = 0; p < pt.Count; p++)
        {
            if (pt.Status[p] != PersonStatus.Alive) continue;
            if (pt.Sex[p] == Sex.Female && pt.Spouse[p] < 0 && pt.MarriageWait[p] > 0) waiting++;
            if (pt.Employer[p] >= 0 || pt.Occupation[p] is not (Occupation.None or Occupation.Labourer)) continue;
            int hh = pt.Household[p]; if (hh < 0 || hht.Class[hh] == SocialClass.Gentry || hht.ParcelCount[hh] > 0) continue;
            int age = w.AgeOn(p, day); if (age >= 16 && age < 60) idle++;
        }
        if (idle * 1000 <= alive * g.ImmigrationMaxIdlePermille) pull += Math.Max(0, vacant - waiting) / Math.Max(1, g.ImmigrationSurplusDivisor);
        return Math.Min(alive * g.ImmigrationMaxPermilleOfAlive / 1000, pull);
    }

    /// <summary>Share of the starting population (permille) that may return in one season, and the part of the shortfall they make up.</summary>
    public const int ReturnQuotaPermille = 12, ShortfallDivisor = 6;

    /// <summary>
    /// Immigration (integration decision I27): while the region holds fewer people than it started with, households that left (crowding, a long wait for a home) come back to
    /// vacant dwellings, a sixth of the shortfall a season and at most 1.2 percent of the starting population. Deterministic: households in ascending order. Balanced against
    /// the emigration above, which only crowded households and long-waiting singles take: the region cannot lose people to the continent for good and never gains more
    /// than it lost.
    /// </summary>
    private void Immigrate(TickContext ctx)
    {
        if (ctx.World.Get<Ravensguard.Sim.Economy.Markets.ContinentLink>() is { } link) { ImmigrateFromContinent(ctx, link); return; }
        var w = ctx.World; var pt = w.People; var hht = w.Households; var v = w.Vital;
        long quota = ImmigrationQuota(w); if (quota <= 0) return;
        var byHousehold = new SortedDictionary<int, List<int>>();
        for (int p = 0; p < pt.Count; p++)
        {
            if (pt.Status[p] != PersonStatus.Departed) continue;
            int hh = pt.Household[p]; if (hh < 0 || hht.MemberCount[hh] != 0) continue;
            if (!byHousehold.TryGetValue(hh, out var l)) byHousehold[hh] = l = [];
            l.Add(p);
        }
        foreach (var (hh, members) in byHousehold)
        {
            if (quota <= 0) break;
            if (Demography.ReturnHousehold(w, hh, members, ctx.Time, ctx.Outbox)) quota -= members.Count;
        }
        // singles who left a household that still stands come home to it when it has a bed
        for (int p = 0; p < pt.Count && quota > 0; p++)
        {
            if (pt.Status[p] != PersonStatus.Departed) continue;
            int hh = pt.Household[p]; if (hh < 0 || hht.MemberCount[hh] == 0 || hht.MemberCount[hh] >= hht.Beds[hh]) continue;
            if (Demography.ReturnPerson(w, p, hh, ctx.Time, ctx.Outbox)) quota--;
        }
    }
}
