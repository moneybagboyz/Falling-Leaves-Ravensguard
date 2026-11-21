using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Weather;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Production;

/// <summary>
/// Businesses at work (production spec 3, 4): the step functions at their tick places. <see cref="WorkHour"/> is H2 (hours 06-16: progress, completions,
/// extraction), <see cref="DailySlice"/> is H4 (wages, sales, planning, input orders for the businesses whose index mod 24 is the hour), <see cref="DailyGlobals"/>
/// is H5 (vacancies, wage offers) and <see cref="Seasonal"/> is S5 (dividends, closures). Plain class with explicit parameters; it schedules nothing.
/// </summary>
public sealed partial class ProductionSystem : IStructuralDemandSource
{
    public const int WorkdayStartHour = 6, WorkdayHours = 10;
    public EconomyState Econ { get; }
    public Population Pop { get; }
    public ProductionContent Content { get; }
    /// <summary>P5-E (crafting-full 4.4): whether a station's batch is worked live (a station-bound craft job); its progress then comes from the live work, not from this system.</summary>
    public Func<Business, int, bool>? LiveStation { get; set; }
    /// <summary>P7-LB1 (large-buildings 4.1, 4.3): the worker-hours in permille of a station while its premises' machines run (the power net's share of a machine's labour_pm); null or no answer: the business kind's <see cref="BusinessKind.LabourPm"/>.</summary>
    public Func<Business, int, int?>? StationLabourPm { get; set; }
    /// <summary>P7-LB2 (large-buildings 4.4): the span a manufactory hall adds to each of its business's overseers (one view across the floor: +4); null or 0: none.</summary>
    public Func<Business, int>? SpanBonus { get; set; }
    /// <summary>P6-B6 (holdings 3.8, HW-10): a batch finished at a station of a business (the business, the recipe), so the tool it is worked with can wear; null: nothing wears but the abstract count.</summary>
    public Action<Business, Recipe, Station>? BatchDone { get; set; }
    /// <summary>P6-B6 (HW-10): the containers within 6 m of a business's station count as its store; the inputs a batch lacks are taken from them first. Null: the business's own store only.</summary>
    public Func<Business, IEnumerable<Store>>? NearStores { get; set; }
    /// <summary>P5-F (businesses spec 4.2-4.6): the owner's and steward's orders for the businesses the ownership world steers; null (every other world) runs Phase 2 unchanged.</summary>
    public IBusinessSteering? Steering { get; set; }
    private bool Steered(Business b) => Steering != null && Steering.Steered(b);
    private bool OwnedByHero(Business b) => Steering != null && Steering.Steered(b) && Steering.Owned(b);
    /// <summary>The hourly wage a staff member of a steered business is paid (his agreed wage), else the class wage.</summary>
    private int WageOf(Business b, int person, int classHourly) => Steered(b) ? Steering!.WagePerHour(b, person, classHourly) : classHourly;
    public ulong Seed { get; }
    public MarketSystem? Markets { get; }
    public ResourceLayer? Resources { get; }
    public List<Business> Businesses { get; } = [];
    public List<MineWorks> Mines { get; } = [];
    /// <summary>P5-F (businesses spec 4.3): who has worked by the day lately (saved with the production section).</summary>
    public DayLabourBook DayLabour { get; } = new();
    /// <summary>P7-ES1 (economies-of-scale 3): appointed foremen (their day wage and their place in the span of control), licences and fines; null in a world without the book.</summary>
    public ScaleBook? Scale { get; set; }
    /// <summary>P7-IN1: the industry runtime's hooks (null without industries): stage businesses are run by <c>IndustryWorld</c>, not by the Phase 2 planner.</summary>
    public Industry.IStageHost? Stages { get; set; }
    /// <summary>Weather at a settlement and time; null means always clear.</summary>
    public Func<int, GameTime, WeatherState>? WeatherAt { get; set; }

    /// <summary>Units produced and consumed per good by recipes and extraction (grams or pieces), for the soak reports.</summary>
    public long[] ProducedUnits { get; }
    public long[] ConsumedUnits { get; }
    public long MassViolations, BatchesFinished, BatchesStarted;
    public long ForgivenArrearsMites, QuitCount, TreasuryPaidMites;
    private readonly Dictionary<EntityId, int> _byId = [];

    public ProductionSystem(EconomyState econ, Population pop, ProductionContent content, ulong seed, MarketSystem? markets, ResourceLayer? resources)
    {
        Econ = econ; Pop = pop; Content = content; Seed = seed; Markets = markets; Resources = resources;
        ProducedUnits = new long[econ.Goods.Count]; ConsumedUnits = new long[econ.Goods.Count];
        if (markets != null) markets.Production = this;
    }

    public BusinessKind KindOf(Business b) => Content.Kinds[b.Kind];
    public Business? ById(EntityId id) => _byId.TryGetValue(id, out int i) ? Businesses[i] : null;

    public Business NewBusiness(BusinessKind kind, int settlement, int lot, int owner)
    {
        int idx = Businesses.Count; var id = EconIds.Business(idx);
        var purse = Econ.Ledger.Open(PurseKind.Business, id);
        var store = Econ.OpenStore(StoreKind.House, id);
        Business? made = null;
        // a merchant buys stock only with the coin above a month of his stall's wages and costs (decision I45): stock bought with the last coin left the stalls unable to pay their hands
        var party = new MarketParty(id, purse, store, kind.Trader ? PartyKind.Merchant : PartyKind.Business, idx) { CashReserve = kind.Trader ? () => MerchantReserveDays * MerchantDayCost(made!) : null };
        var b = made = new Business
        {
            Index = idx, Id = id, Kind = kind.Index, Settlement = settlement, Lot = lot, Owner = owner, OwnerHousehold = owner >= 0 ? Pop.People.Household[owner] : -1,
            Purse = purse, Store = store, Party = party, WageOfferMites = (short)RecipeBook.WagePerDay(WageClass.Skilled),
        };
        Businesses.Add(b); _byId[id] = idx;
        return b;
    }

    public void AddStaff(Business b, int person)
    {
        b.Staff.Add(person); b.Owed.Add(0); b.ShortDays.Add(0);
        if (Pop.People.Employer[person] < 0 && b.Owner >= 0 && person != b.Owner) Pop.People.Employer[person] = b.Owner;
    }

    /// <summary>
    /// Hires a person into the business (labour market, households spec 3.5): the person's occupation, employer and workplace are updated, the wage accrues from the
    /// next worker-hour. Persons under 16 are apprentices (half wage); the owner of a business is never under 16 (<see cref="SetOwner"/>).
    /// </summary>
    public void Employ(Business b, int person, Occupation occ)
    {
        var p = Pop.People;
        p.Occupation[person] = occ; p.Employer[person] = b.Owner; p.WorkSettlement[person] = b.Settlement; p.WorkLot[person] = (short)(b.Lot < 0 ? -1 : b.Lot);
        b.Staff.Add(person); b.Owed.Add(0); b.ShortDays.Add(0);
    }

    /// <summary>
    /// Takes a staff member off the books. A living person goes back to the idle (no occupation, no employer, no workplace) unless <paramref name="keepJob"/> (he is
    /// about to become the owner); the arrears owed to someone who died or left are forgiven.
    /// </summary>
    public void DropStaff(Business b, int staffIndex, bool alive, bool keepJob = false)
    {
        int p = b.Staff[staffIndex];
        ForgivenArrearsMites += b.Owed[staffIndex];
        b.Staff.RemoveAt(staffIndex); b.Owed.RemoveAt(staffIndex); b.ShortDays.RemoveAt(staffIndex);
        if (!alive || keepJob) return;
        var pt = Pop.People;
        if (pt.Occupation[p] != Occupation.Lord) { pt.Occupation[p] = Occupation.None; pt.Employer[p] = -1; pt.WorkSettlement[p] = -1; pt.WorkLot[p] = -1; }
    }

    /// <summary>Makes <paramref name="heir"/> the owner (production spec 3.5): the household that gets the dividends changes, the heir takes the owner's trade and the staff's employer link follows.</summary>
    public void SetOwner(Business b, int heir)
    {
        var pt = Pop.People; var kind = KindOf(b); int old = b.Owner;
        b.Owner = heir; b.OwnerHousehold = pt.Household[heir];
        bool crewLike = kind.Extractions.Length > 0 || kind.Id is "mine" or "manor";
        if (!crewLike) { pt.Occupation[heir] = kind.OwnerOccupation; pt.WorkSettlement[heir] = b.Settlement; pt.WorkLot[heir] = (short)(b.Lot < 0 ? -1 : b.Lot); }
        pt.Employer[heir] = -1;
        foreach (int s in b.Staff) if (pt.Employer[s] == old || pt.Employer[s] < 0) pt.Employer[s] = heir;
    }

    /// <summary>
    /// P5-H (inheritance 4.1): the business passes to an heir. An heir already in another trade keeps it and owns the business without working it (the labour market hires one of its trade at its next
    /// day, <see cref="NeedsMaster"/>); an idle heir or one of the trade takes the owner's trade (<see cref="SetOwner"/>, Phase 2's rule). The owner's dividends go to the heir's household either way.
    /// </summary>
    public void Inherit(Business b, int heir)
    {
        var pt = Pop.People; var kind = KindOf(b);
        bool crewLike = kind.Extractions.Length > 0 || kind.Id is "mine" or "manor";
        if (crewLike || kind.Trader || pt.Occupation[heir] == Occupation.None || pt.Occupation[heir] == kind.OwnerOccupation) { SetOwner(b, heir); if (!crewLike && !kind.Trader && kind.Stations.Length > 0) b.OwnerWorks = true; return; }
        int old = b.Owner;
        b.Owner = heir; b.OwnerHousehold = pt.Household[heir]; b.OwnerWorks = false;
        foreach (int s in b.Staff) if (pt.Employer[s] == old || pt.Employer[s] < 0) pt.Employer[s] = heir;
    }

    /// <summary>P5-H: a workshop whose owner inherited it from another trade (does not work it): it hires one of its own trade (a master) besides its slots.</summary>
    public bool NeedsMaster(Business b)
    {
        if (b.OwnerWorks || b.Owner < 0 || b.Owner >= Pop.People.Count || b.State != BusinessState.Open) return false;
        var kind = KindOf(b);
        if (kind.Trader || kind.Extractions.Length > 0 || kind.Id is "mine" or "manor" || kind.Stations.Length == 0) return false;
        if (Steering is { } st && st.Steered(b)) return false;
        return Pop.People.Occupation[b.Owner] != kind.OwnerOccupation;
    }

    /// <summary>The hourly wage a person earns in a class: half for an apprentice (under 16).</summary>
    private int HourlyWage(int person, WageClass c, int day)
    {
        int w = RecipeBook.WagePerHour(c);
        return Pop.People.AgeYears(person, day) < 16 ? w * 50 / 100 : w;
    }

    public int AddMine(MineWorks m) { Mines.Add(m); return Mines.Count - 1; }

    /// <summary>Registers merchant businesses with their market's trader list (id order).</summary>
    public void RegisterTraders()
    {
        if (Markets == null) return;
        foreach (var b in Businesses)
            if (KindOf(b).Trader && b.State == BusinessState.Open) Markets.BookForSettlement(b.Settlement).Merchants.Add(b.Party);
    }

    // ---------- H2: work ----------

    /// <summary>H2: one hour of work in every open business, ascending id (production spec 3.1, 3.2; resources spec 3.1). Only hours 06-15 work.</summary>
    public void WorkHour(GameTime now)
    {
        if (now.Hour < WorkdayStartHour || now.Hour >= WorkdayStartHour + WorkdayHours) return;
        foreach (var b in Businesses)
        {
            if (b.State != BusinessState.Open) continue;
            var kind = KindOf(b);
            if (kind.Trader || kind.Stage != null) continue;
            if (b.Mine >= 0) { if (now.Hour == WorkdayStartHour) MineDay(b, now); continue; }
            if (kind.Extractions.Length > 0) { ExtractionHour(b, kind, now); continue; }
            WorkStations(b, kind, now);
        }
    }

    private bool IsWorker(Business b, int person, int wheelOnly)
    {
        bool spinner = Pop.People.Occupation[person] == Occupation.Spinner;
        return wheelOnly == 1 ? spinner : !spinner;
    }

    /// <summary>
    /// H2 work at a business's stations. Crews are assigned as in Phase 2 (owner first, then staff ascending, up to the recipe's workers); then each batch's progress is credited by division of labour (P7-ES1,
    /// economies-of-scale 4.1, 5.1, 5.2): the master's steps, the journeyman's and the hands' (<see cref="Crews.Rate"/>), a master's reach over the business's other stations, crowding beyond a station's crew limit
    /// and the span of control. A recipe with no grades, a crew within its limit and its span is exactly Phase 2's sum of worker-hours (so generated trades without a grade row keep their numbers).
    /// </summary>
    private void WorkStations(Business b, BusinessKind kind, GameTime now)
    {
        var people = Pop.People; var sc = Content.Scale; int day = (int)now.TotalDays;
        // gather workers: owner first (if working), then staff ascending
        Span<int> workers = stackalloc int[64];
        int nw = 0;
        if (b.OwnerWorks && b.Owner >= 0) workers[nw++] = b.Owner;
        foreach (int s in b.Staff) if (nw < 64) workers[nw++] = s;
        Span<bool> used = stackalloc bool[64];
        int ns = b.Stations.Count;
        Span<bool> credited = ns <= 64 ? stackalloc bool[64] : new bool[ns];
        int nCredited = 0; bool anyDivided = false;
        for (int si0 = 0; si0 < ns; si0++)
        {
            var st = b.Stations[si0];
            st.Crew.Clear();
            if (!st.Busy) continue;
            var r = Content.Recipes.Recipes[st.Recipe];
            // P5-E (crafting-full 4.4): a batch worked live by staff in the bubble is a craft job; the abstract credit leaves it alone and only finishes it once it is complete
            if (st.ProgressMicro < Station.Complete && LiveStation?.Invoke(b, si0) != true)
            {
                bool wheel = st.Kind == "spinning_wheel";
                for (int i = 0; i < nw && st.Crew.Count < r.MaxWorkers; i++)
                    if (!used[i] && IsWorker(b, workers[i], wheel ? 1 : 0)) { used[i] = true; st.Crew.Add(workers[i]); }
                if (st.Crew.Count > 0) { credited[si0] = true; nCredited++; anyDivided |= r.Grades.Divided; }
            }
        }
        // the master's reach (4.1): a master serves the M steps of several stations of the business at once, his time (and wage) split between them; at most one visiting master a station
        Dictionary<int, int>? served = null; Dictionary<int, List<int>>? visitors = null;
        if (anyDivided && nCredited > 1) MastersReach(b, credited, ns, day, out served, out visitors);
        // the span of control (5.2): overseers are the owner, the masters at work and the appointed foremen
        int speed = 1000;
        if (nCredited > 0)
        {
            int crew = 0; for (int i = 0; i < nw; i++) if (used[i]) crew++;
            if (crew > sc.SpanBase) speed = SpanSpeed(b, crew, credited, ns, day);
        }
        for (int si0 = 0; si0 < ns; si0++)
        {
            var st = b.Stations[si0];
            if (!st.Busy) continue;
            var r = Content.Recipes.Recipes[st.Recipe];
            if (credited[si0])
            {
                long hm = (long)r.WorkerHours * st.ScalePermille * (StationLabourPm?.Invoke(b, si0) ?? kind.LabourPm) / 1000;          // P5-F: a sawmill's wheel does most of the sawing; P7-LB1: only while its machines are powered
                bool divided = r.Grades.Divided; int k = Content.CrewMax(st.Kind);
                long cM = 0, cJ = 0, cA = 0, visitM = 0; int pos = 0;
                foreach (int p in st.Crew)
                {
                    int skill = r.Skill == Skill.None ? 40 : people.SkillLevelOf(p, r.Skill);
                    st.MinSkill = Math.Min(st.MinSkill, skill);
                    long per = Station.PerWorkerHour(hm, 1300 - 6 * skill);
                    int slot = Crews.SlotPm(pos++, k);
                    long c = slot == 1000 ? per : per * slot / 1000;
                    var cls = r.Wage; var role = CrewRole.Hand;
                    if (divided) { role = RoleAt(b, p, skill, day); cls = Crews.WageOf(role, r.Wage); }
                    if (role == CrewRole.Master) cM += c; else if (role == CrewRole.Journeyman) cJ += c; else cA += c;
                    int si = b.Staff.IndexOf(p);
                    if (si >= 0) b.Owed[si] += WageOf(b, p, HourlyWage(p, cls, day));
                }
                if (visitors != null && visitors.TryGetValue(si0, out var vs))
                    foreach (int p in vs)        // a visiting master spreads his M-step time over the stations he serves; he is paid for the hour at his own station, so no wage here
                    {
                        int skill = r.Skill == Skill.None ? 40 : people.SkillLevelOf(p, r.Skill);
                        visitM += Station.PerWorkerHour(hm, 1300 - 6 * skill) / served![p];
                    }
                long rate = divided ? Crews.Rate(cM, cJ, cA, r.Grades, sc.UnsupervisedPm, visitM) : cA;
                st.ProgressMicro += speed == 1000 ? rate : rate * speed / 1000;
            }
            if (st.ProgressMicro >= Station.Complete && now.TotalHours >= st.BusyUntilHour) Finish(b, st, now);
        }
    }

    /// <summary>A person's role at a batch of the business: the owner at work is the master of his own house whatever his skill (an owner-operator supervises himself), else by skill and age (<see cref="Crews.RoleOf"/>).</summary>
    public CrewRole RoleAt(Business b, int person, int skill, int day)
        => person == b.Owner && b.OwnerWorks ? CrewRole.Master : Crews.RoleOf(Content.Scale, skill, Pop.People.AgeYears(person, day));

    /// <summary>Which masters serve more than one station (4.1's reach, ES-3): a master at one station of a divided recipe also visits the business's other credited divided stations that have no master, up to his reach.</summary>
    private void MastersReach(Business b, Span<bool> credited, int ns, int day, out Dictionary<int, int>? served, out Dictionary<int, List<int>>? visitors)
    {
        served = null; visitors = null;
        var people = Pop.People; var sc = Content.Scale;
        var hasMaster = new bool[ns];
        bool IsMaster(int p, Recipe r) => RoleAt(b, p, r.Skill == Skill.None ? 40 : people.SkillLevelOf(p, r.Skill), day) == CrewRole.Master;
        for (int s = 0; s < ns; s++)
        {
            if (!credited[s]) continue; var r = Content.Recipes.Recipes[b.Stations[s].Recipe];
            if (r.Grades.Divided && r.Grades.M > 0) foreach (int p in b.Stations[s].Crew) if (IsMaster(p, r)) { hasMaster[s] = true; break; }
        }
        for (int s = 0; s < ns; s++)
        {
            if (!credited[s]) continue; var r = Content.Recipes.Recipes[b.Stations[s].Recipe];
            if (!r.Grades.Divided) continue;
            foreach (int p in b.Stations[s].Crew)
            {
                if (!IsMaster(p, r)) continue;
                int reach = Crews.Reach(sc, people.SkillLevelOf(p, Skill.Leadership)), visits = 0;
                for (int t = 0; t < ns && visits < reach - 1; t++)
                {
                    if (t == s || !credited[t] || hasMaster[t]) continue;
                    var rt = Content.Recipes.Recipes[b.Stations[t].Recipe];
                    if (!rt.Grades.Divided || rt.Grades.M == 0 || !IsMaster(p, rt)) continue;
                    hasMaster[t] = true; visits++;
                    visitors ??= [];
                    if (!visitors.TryGetValue(t, out var l)) visitors[t] = l = [];
                    l.Add(p);
                }
                if (visits > 0) (served ??= [])[p] = 1 + visits;
            }
        }
    }

    /// <summary>The speed of a crew larger than the overseers' span (5.2, ES-6): the owner, the masters at work and the appointed foremen each oversee <c>6 + Leadership / 10</c> workers.</summary>
    private int SpanSpeed(Business b, int crew, Span<bool> credited, int ns, int day)
    {
        var people = Pop.People; var sc = Content.Scale; var seen = new HashSet<int>(); int span = 0;
        int hall = SpanBonus?.Invoke(b) ?? 0;
        void Overseer(int p) { if (seen.Add(p)) span += Crews.Span(sc, people.SkillLevelOf(p, Skill.Leadership)) + hall; }
        if (b.Owner >= 0) Overseer(b.Owner);
        for (int s = 0; s < ns; s++)
        {
            if (!credited[s]) continue; var r = Content.Recipes.Recipes[b.Stations[s].Recipe];
            if (!r.Grades.Divided) continue;
            foreach (int p in b.Stations[s].Crew) if (RoleAt(b, p, r.Skill == Skill.None ? 40 : people.SkillLevelOf(p, r.Skill), day) == CrewRole.Master) Overseer(p);
        }
        if (Scale != null && Scale.Foremen.TryGetValue(b.Id.Value, out var fm)) foreach (int p in fm) if (b.Staff.Contains(p)) Overseer(p);
        return Crews.SpeedPm(sc, crew, span);
    }

    private void Finish(Business b, Station st, GameTime now)
    {
        var r = Content.Recipes.Recipes[st.Recipe]; var cat = Econ.Goods;
        long outMass = 0;
        foreach (var (g, q) in r.Outputs)
        {
            long qty = q * st.ScalePermille / 1000;
            if (qty <= 0) continue;
            b.Store.Create(g, qty, r.Id == "burn_charcoal" ? CreateReason.Crafted : CreateReason.Crafted);
            ProducedUnits[g] += qty; outMass += cat.MassG(g, qty);
        }
        long inMass = 0; foreach (var (g, q) in r.Inputs) inMass += cat.MassG(g, CeilScale(q, st.ScalePermille));
        if (outMass > inMass + r.AddsG * st.ScalePermille / 1000 + 1) MassViolations++;
        int minSkill = st.MinSkill == 100 && st.Crew.Count == 0 ? 50 : st.MinSkill;
        var q2 = ItemQualityRules.FromScore(ItemQualityRules.Score(Seed, now.TotalHours, b.Id, (uint)b.Batches, minSkill, 0));
        b.Quality[(int)q2]++; b.LastQuality = (int)q2; b.Batches++; BatchesFinished++;
        if (r.WearPpmPerBatch > 0 && DetRng.NextUInt(Seed, now.TotalHours, b.Id.Value, RngStreams.ToolWear, r.Index) < (ulong)r.WearPpmPerBatch * 4_294_967_296UL / 1_000_000UL) b.ToolPiecesWorn++;
        int recipe = st.Recipe;
        BatchDone?.Invoke(b, r, st);
        st.Recipe = -1; st.ProgressMicro = 0; st.MinSkill = 100; st.Crew.Clear();
        // keep the station busy: the same recipe again if the inputs are on hand
        TryStart(b, st, Content.Recipes.Recipes[recipe], now);
    }

    private static long CeilScale(long q, int scale) => (q * scale + 999) / 1000;

    /// <summary>
    /// P5-F (businesses spec 4.2, BS-5): someone's own hands at a station for <paramref name="seconds"/> (the hero at his smithy's anvil): an idle station starts a batch of <paramref name="start"/>
    /// from the business's store, the time is credited at the one worker-hour rule (<c>1300 - 6 x skill</c>), a finished batch is made into the store and counted in the books. False when
    /// the station is idle and the inputs are short.
    /// </summary>
    public bool CreditHands(Business b, int si, Recipe? start, int skill, long seconds, GameTime now)
    {
        var st = b.Stations[si];
        if (!st.Busy && (start == null || !TryStart(b, st, start, now))) return false;
        var r = Content.Recipes.Recipes[st.Recipe];
        long hm = (long)r.WorkerHours * st.ScalePermille * (StationLabourPm?.Invoke(b, si) ?? KindOf(b).LabourPm) / 1000;
        long per = Station.PerWorkerHour(hm, 1300 - 6 * Math.Clamp(skill, 0, 100));
        st.ProgressMicro = Math.Min(Station.Complete, st.ProgressMicro + (long)((Int128)per * seconds / 3600));
        st.MinSkill = Math.Min(st.MinSkill, skill);
        if (st.ProgressMicro >= Station.Complete && now.TotalHours >= st.BusyUntilHour) Finish(b, st, now);
        return true;
    }

    /// <summary>Starts a batch of <paramref name="r"/> on a station: whole batches, or a part batch down to the recipe's minimum when inputs are short. False (nothing consumed) when too short.</summary>
    public bool TryStart(Business b, Station st, Recipe r, GameTime now)
    {
        if (st.Busy || b.State != BusinessState.Open) return false;
        long scale = 1000;
        if (NearStores != null)
            foreach (var (g, q) in r.Inputs)
            {
                long lack = q - b.Store.Qty(g);
                if (lack > 0) foreach (var near in NearStores(b)) { lack -= Store.MoveUpTo(near, b.Store, g, lack); if (lack <= 0) break; }
            }
        foreach (var (g, q) in r.Inputs) scale = Math.Min(scale, b.Store.Qty(g) * 1000 / q);
        if (scale < r.MinBatchPermille) return false;
        foreach (var (g, q) in r.Inputs)
        {
            long take = CeilScale(q, (int)scale);
            if (!b.Store.Destroy(g, take, DestroyReason.Consumed)) throw new InvalidOperationException("batch inputs vanished");
            ConsumedUnits[g] += take;
        }
        st.Recipe = r.Index; st.ProgressMicro = 0; st.ScalePermille = (int)scale; st.StartedHour = now.TotalHours; st.MinSkill = 100;
        st.BusyUntilHour = r.DurationDays > 0 ? now.TotalHours + r.DurationDays * 24L : 0;
        BatchesStarted++;
        return true;
    }

    // ---------- extraction ----------

    private int WeatherMultFor(Business b, ExtractionKind k, GameTime now)
        => Extraction.WeatherMult(k, WeatherAt == null ? WeatherState.Clear : WeatherAt(b.Settlement, now));

    private static CreateReason ReasonFor(ExtractionKind k) => k.IsMine ? CreateReason.Mined : k.Resource switch
    {
        ResourceKind.Fish => CreateReason.Caught, ResourceKind.Wood or ResourceKind.Peat => CreateReason.Cut, ResourceKind.Game => CreateReason.Hunted,
        ResourceKind.Stone or ResourceKind.Clay or ResourceKind.Sand => CreateReason.Mined, _ => CreateReason.Harvest,
    };

    private void ExtractionHour(Business b, BusinessKind kind, GameTime now)
    {
        if (Resources == null || b.Jobs.Count == 0) return;
        var people = Pop.People; int workerCursor = 0;
        foreach (var job in b.Jobs)
        {
            var k = Content.Extractions[job.Kind];
            if (job.Row >= 0 && Resources.StockG(job.Row) <= 0)
            {
                // the tile is empty: the crew moves to the best other tile of the same resource at once (resources spec 3.6)
                var s = Pop.Region.Settlements[b.Settlement];
                job.Row = PickTile(k, s.CenterX, s.CenterY, k.Resource ?? ResourceKind.Wood);
                if (Steered(b)) job.Row = Steering!.TileFor(b, k, job.Row);                 // P5-F: an extraction business founded on its own tiles works those
            }
            if (job.Row < 0 || job.Workers <= 0) { workerCursor += job.Workers; continue; }
            int weather = WeatherMultFor(b, k, now);
            for (int w = 0; w < job.Workers && workerCursor < b.Staff.Count; w++, workerCursor++)
            {
                int p = b.Staff[workerCursor];
                int skill = k.Skill == Skill.None ? 40 : people.SkillLevelOf(p, k.Skill);
                b.Owed[workerCursor] += WageOf(b, p, HourlyWage(p, WageClass.Unskilled, (int)now.TotalDays));
                long g = Extraction.GramsPerWorkerHour(k, now, weather, skill, Resources.StockG(job.Row), Resources.CapG(job.Row), WorkdayHours);
                if (g <= 0) continue;
                long got = Resources.Take(job.Row, g, (int)now.TotalDays);
                if (got <= 0) continue;
                b.Store.Create(k.OutGood, got, ReasonFor(k)); ProducedUnits[k.OutGood] += got;
                foreach (var (bg, pm) in k.Byproducts) { long by = got * pm / 1000; if (by > 0) { b.Store.Create(bg, by, ReasonFor(k)); ProducedUnits[bg] += by; } }
                if (Steered(b)) Steering!.Extracted(b, k.OutGood, got, now);
            }
        }
    }

    private void MineDay(Business b, GameTime now)
    {
        var mine = Mines[b.Mine];
        string? ore = MineWorks.OreGood(mine.Mineral); if (ore == null) return;
        var k = Content.Extractions.FirstOrDefault(e => e.IsMine && e.OutGood == Econ.Goods.IdOf(ore)); if (k == null) return;
        int miners = b.Staff.Count(p => Pop.People.Occupation[p] == Occupation.Miner);
        int haulers = b.Staff.Count - miners;
        for (int i = 0; i < b.Staff.Count; i++) b.Owed[i] += WageOf(b, b.Staff[i], RecipeBook.WagePerHour(WageClass.Unskilled)) * WorkdayHours * (Pop.People.AgeYears(b.Staff[i], (int)now.TotalDays) < 16 ? 50 : 100) / 100;
        // P7-IN1 (industries 5.12): a generated iron mine yields dressed ore at the composite rate of the first-tier chain (hewing, carrying and dressing: 109 kg a worker-day), not 500 kg of rough ore that skipped the dressing; the body gives up the rough ore behind it
        var ind = Content.Industries; bool composite = ind.CompositeGPerWorkerDay > 0 && k.OutGood == ind.CompositeGood;
        long g = mine.DayYieldG(k, miners, b.Staff.Count > 0 && haulers == 0 && miners > 0 ? -1 : haulers, 1000, (int)now.TotalDays, composite ? ind.CompositeGPerWorkerDay : -1);
        if (composite) { long rough = mine.Mine(g * 1000 / ind.Settings.CompositeYieldPm); g = rough * ind.Settings.CompositeYieldPm / 1000; }
        else g = mine.Mine(g);
        if (g > 0) { b.Store.Create(k.OutGood, g, CreateReason.Mined); ProducedUnits[k.OutGood] += g; if (Steered(b)) Steering!.Extracted(b, k.OutGood, g, now); }
    }

    /// <summary>S3: mine hazards for every mine (flood, cave-in), returns the events for the chronicle.</summary>
    public List<(Business Business, MineHazard Hazard)> MineHazards(GameTime now, bool wetYear, bool riverHigh)
    {
        var res = new List<(Business, MineHazard)>();
        foreach (var b in Businesses)
        {
            if (b.Mine < 0 || Mines[b.Mine].SiteIndex < 0) continue;              // P5-F: a mine the hero opened rolls its own hazards (stream 0x5C2, ownership seasonal)
            var h = Mines[b.Mine].RollHazards(Seed, now, wetYear, riverHigh, Content.Settings);
            if (h.Flood || h.CaveIn) res.Add((b, h));
        }
        return res;
    }

    // ---------- H4 slice: wages, sales, planning ----------

    /// <summary>H4: the daily slice of every business whose index mod 24 is the hour of day (production spec 3.2, 3.3).</summary>
    public void DailySlice(GameTime now)
    {
        int hour = now.Hour, day = (int)now.TotalDays;
        foreach (var b in Businesses)
        {
            if (b.State != BusinessState.Open || b.Index % 24 != hour) continue;
            var kind = KindOf(b);
            if (kind.Stage != null) continue;                                                      // P7-IN1: a works' stage is run by the industry world
            if (kind.Salaried) { AccrueDayWages(b, kind, day); if (kind.Id == "manor") TopUpFromOwner(b); }
            if (Scale != null && Scale.Foremen.Count > 0) AccrueForemen(b);
            PayWages(b, now);
            if (kind.Trader || kind.Salaried && kind.Stations.Length == 0) continue;
            if (kind.Extractions.Length == 0 && b.Mine < 0 && !OwnedByHero(b)) TopUpWorkshop(b, day);   // P5-F: the hero's business gets no Phase 2 top-ups: he puts coin in himself
            SellOutputs(b, kind, day);
            if (kind.Extractions.Length > 0 || b.Mine >= 0) PlanExtraction(b, kind, now);
            else PlanStations(b, kind, now);
        }
    }

    /// <summary>P7-ES1 (economies-of-scale 5.2): an appointed foreman's extra day wage (50 mites) accrues with the rest of his wages; one who left the staff stops being a foreman.</summary>
    private void AccrueForemen(Business b)
    {
        if (!Scale!.Foremen.TryGetValue(b.Id.Value, out var set)) return;
        foreach (int p in set.ToArray())
        {
            int i = b.Staff.IndexOf(p);
            if (i < 0) { Scale.Forget(b.Id.Value, p); continue; }
            b.Owed[i] += Content.Scale.ForemanWageMitesPerDay;
        }
    }

    /// <summary>A day's wage for each person on the books of a salaried business: the class of the slot of his occupation (skilled for a trader's hands), half for an apprentice.</summary>
    private void AccrueDayWages(Business b, BusinessKind kind, int day)
    {
        for (int i = 0; i < b.Staff.Count; i++)
        {
            int p = b.Staff[i]; var occ = Pop.People.Occupation[p]; var wc = kind.Trader ? WageClass.Skilled : WageClass.Unskilled;
            foreach (var sl in kind.Slots) if (sl.Occupation == occ) { wc = sl.Wage; break; }
            b.Owed[i] += RecipeBook.WagePerDay(wc) * Econ.WageMultPm / 1000 * (Pop.People.AgeYears(p, day) < 16 ? 50 : 100) / 100;
            if (kind.Id == "manor") Livery(b, p);
        }
    }

    /// <summary>Livery (integration decision I17): a manor's staff are given a measure of grain a day from the lord's house store, as part of the wage, while he has it.</summary>
    private void Livery(Business b, int person)
    {
        int lh = b.OwnerHousehold, sh = Pop.People.Household[person]; if (lh < 0 || sh < 0 || lh == sh) return;
        long left = Pop.Households.MemberCount[sh] > 0 ? HouseholdNeeds.GramsPerMeasure : 0;
        foreach (ushort g in Econ.Food.Staples) { if (left <= 0) break; if (Econ.Food.IsGrain(g)) left -= Store.MoveUpTo(Econ.HouseholdStore[lh], Econ.HouseholdStore[sh], g, left); }
    }

    /// <summary>The lord keeps his manor's cash box at 20 days of the wage bill from his own purse (reason Stipend) as long as he has the coin.</summary>
    private void TopUpFromOwner(Business b)
    {
        int hh = b.OwnerHousehold; if (hh < 0 || b.Staff.Count == 0) return;
        long bill = 0; foreach (long o in b.Owed) bill += Math.Max(o, 200);
        long bal = Econ.Ledger.Balance(b.Purse).Mites;
        if (bal >= 10 * bill) return;
        long need = 20 * bill - bal;
        // the fief's treasury pays first (the market tolls it collects are the payroll of the retinue and the public works: taxes spec 3.6), the lord makes up the rest
        int fief = b.Settlement >= 0 ? Pop.Region.Settlements[b.Settlement].FiefIndex : -1;
        if (fief >= 0 && fief < Pop.Region.Fiefs.Count && Econ.Ledger.TryFind(PurseKind.Treasury, Pop.Region.Fiefs[fief].Id, out var treasury))
        {
            long t = Math.Min(need, Econ.Ledger.Balance(treasury).Mites);
            if (t > 0 && Econ.Ledger.Transfer(treasury, b.Purse, new Coin(t), TransferReason.Stipend)) { need -= t; TreasuryPaidMites += t; }
        }
        long pay = Math.Min(need, Econ.Ledger.Balance(Econ.HouseholdPurse[hh]).Mites);
        if (pay > 0) Econ.Ledger.Transfer(Econ.HouseholdPurse[hh], b.Purse, new Coin(pay), TransferReason.Stipend);
    }

    /// <summary>Pays what each staff member has accrued, ascending person order, from the cash box until it runs dry; three short days in a row make a worker quit.</summary>
    public void PayWages(Business b, GameTime now)
    {
        var econ = Econ;
        var quitters = new List<int>();
        for (int i = 0; i < b.Staff.Count; i++)
        {
            long owed = b.Owed[i];
            if (owed <= 0) { b.ShortDays[i] = 0; continue; }
            int hh = Pop.People.Household[b.Staff[i]];
            long pay = Math.Min(owed, econ.Ledger.Balance(b.Purse).Mites);
            if (pay > 0 && hh >= 0 && econ.Ledger.Transfer(b.Purse, econ.HouseholdPurse[hh], new Coin(pay), TransferReason.Wage)) { b.Owed[i] -= pay; b.WagesSeason += pay; }
            else pay = 0;
            if (b.Owed[i] > 0) { b.ShortDays[i]++; if (b.ShortDays[i] >= 3) quitters.Add(i); } else b.ShortDays[i] = 0;
        }
        for (int q = quitters.Count - 1; q >= 0; q--) Quit(b, quitters[q]);
    }

    private void Quit(Business b, int staffIndex)
    {
        DropStaff(b, staffIndex, alive: Pop.People.Status[b.Staff[staffIndex]] == PersonStatus.Alive);
        QuitCount++;
    }

    /// <summary>Sends the business's finished goods (outputs that no recipe of its own consumes) to market: merchants first, the rest as its own lot at floor 0.9.</summary>
    public void SellOutputs(Business b, BusinessKind kind, int day)
    {
        if (Markets == null) return;
        var book = Markets.BookForSettlement(b.Settlement);
        var consumed = new HashSet<ushort>();
        foreach (var r in kind.Recipes) foreach (var (g, _) in r.Inputs) consumed.Add(g);
        var sellable = new SortedSet<ushort>();
        foreach (var r in kind.Recipes) foreach (var (g, _) in r.Outputs) if (!consumed.Contains(g)) sellable.Add(g);
        foreach (var e in kind.Extractions) { sellable.Add(e.OutGood); foreach (var (bg, _) in e.Byproducts) sellable.Add(bg); }
        if (b.Mine >= 0) foreach (var e in Content.Extractions) if (e.IsMine) sellable.Add(e.OutGood);
        bool steered = Steered(b);
        foreach (ushort g in sellable)
        {
            long have = b.Store.Qty(g); int floor = 900;
            if (steered && Steering!.Sale(b, g) is { } sale) { floor = sale.FloorPm; have -= sale.Keep; }    // P5-F: the owner's floor price and the stock he keeps (or sells at his stall)
            if (have > 0) book.SellOrList(b.Party, g, have, floor, day);
        }
    }

    // ---------- planning ----------

    /// <summary>Micro-mites per unit (gram, or piece) the business sees: the base price without a book, else the book's ask (buying) or mid (selling).</summary>
    private long PriceMicro(MarketBook? book, ushort g, bool ask)
        => book == null ? Econ.Goods.BasePriceMilliMites(g) * (Econ.Goods.IsPiece(g) ? 1000 : 1) : ask ? book.AskMicro(g) : book.MidMicro(g);

    /// <summary>
    /// Expected margin of a full batch at the prices the business sees, in micro-mites: outputs at mid less the toll, inputs at ask, less wages (production spec 3.2).
    /// Integer throughout (review finding 8: it was doubles, and drove the recipe choice).
    /// </summary>
    public long ExpectedMarginMicro(Business b, Recipe r)
    {
        var book = Markets?.BookForSettlement(b.Settlement);
        Int128 rev = 0, cost = 0; int keep = 1000 - Econ.Money.MarketTollPermille;
        foreach (var (g, q) in r.Outputs) rev += (Int128)q * PriceMicro(book, g, false) * keep / 1000;
        foreach (var (g, q) in r.Inputs) cost += (Int128)q * PriceMicro(book, g, true);
        Int128 m = rev - cost - (Int128)r.WorkerHours * RecipeBook.WagePerHour(r.Wage) * PriceCurve.Micro;
        return (long)Int128.Clamp(m, long.MinValue, long.MaxValue);
    }

    /// <summary>The same in mites, for display.</summary>
    public long ExpectedMargin(Business b, Recipe r) => ExpectedMarginMicro(b, r) / PriceCurve.Micro;

    /// <summary>Batches a station can finish in a working day with the people the business has (at least one).</summary>
    private int CapacityBatchesPerDay(Business b, Recipe r)
    {
        int workers = Math.Min(Math.Max(1, r.MaxWorkers), b.Staff.Count + (b.OwnerWorks && b.Owner >= 0 ? 1 : 0));
        return Math.Max(1, workers * WorkdayHours / Math.Max(1, r.WorkerHours));
    }

    /// <summary>
    /// How many batches of the recipe the business keeps its inputs for (integration decision I6): what the market lacks of the main output against its target cover,
    /// shared among the businesses of the same kind in the settlement, but at least one and at most two days of the business's own capacity, so a mill is not
    /// held to a batch a day while the town runs short of flour, and a bakery does not buy flour for batches the market would not take.
    /// </summary>
    private int BatchesWanted(Business b, Recipe r, MarketBook? book)
    {
        int cap = CapacityBatchesPerDay(b, r);
        if (book == null) return cap;
        var gb = book.Goods[r.Outputs[0].Good];
        long gap = gb.Demand * gb.TargetCoverDays - gb.StockUnits;
        long outQ = Math.Max(1, r.Outputs[0].Qty);
        int sharers = Math.Max(1, SameKind(b));
        long wanted = gap <= 0 ? 1 : (gap / sharers + outQ - 1) / outQ;
        return (int)Math.Clamp(wanted, 1, 2L * cap);
    }

    /// <summary>How many open businesses of the kind the settlement has (counted on the spot, not cached: a cache would depend on when a loaded world first asked, review finding 2).</summary>
    private int SameKind(Business b)
    {
        int n = 0;
        foreach (var x in Businesses) if (x.State == BusinessState.Open && x.Settlement == b.Settlement && x.Kind == b.Kind) n++;
        return Math.Max(1, n);
    }

    private void PlanStations(Business b, BusinessKind kind, GameTime now)
    {
        var book = Markets?.BookForSettlement(b.Settlement);
        // what the cash box can still promise to the market this morning, shared by the stations: nine tenths of it, in micro-mites (integer, review finding 8)
        int busy = 0; Int128 budget = (Int128)Econ.Ledger.Balance(b.Purse).Mites * 9 * PriceCurve.Micro / 10;
        foreach (var st in b.Stations)
        {
            if (st.Busy) { busy++; continue; }
            Recipe? best = null; long bestMargin = 0; int bestHours = 1;
            bool steered = Steered(b);
            var ordered = steered ? Steering!.Ordered(b, kind, st.Kind) : null;
            foreach (var r in ordered != null ? [ordered] : kind.RecipesFor(st.Kind))
            {
                if (book != null)
                {
                    ushort main = r.Outputs[0].Good; var gb = book.Goods[main];
                    if (gb.StockUnits >= 2L * gb.Demand * gb.TargetCoverDays) continue;     // do not flood the market
                }
                // the best margin per worker-hour, compared as margin_a x hours_b > margin_b x hours_a (no division); only a positive margin is worth starting
                long m = (steered ? Steering!.MarginMicro(b, r) : null) ?? ExpectedMarginMicro(b, r); int h = Math.Max(1, r.WorkerHours);
                if (ordered != null && m <= 0) m = 1;                                         // the owner's order is made whatever its margin
                if (m > 0 && (Int128)m * bestHours > (Int128)bestMargin * h) { bestMargin = m; bestHours = h; best = r; }
            }
            if (best == null) continue;
            bool started = TryStart(b, st, best, now);
            if (started) busy++;
            // the orders are for what the cash box can pay, all inputs in proportion, so a brewer is not left with barley and no coin for the firewood (decision I19)
            int batches = BatchesWanted(b, best, book); int permille = 1000;
            Int128 perBatch = 0; foreach (var (g, q) in best.Inputs) perBatch += (Int128)q * PriceMicro(book, g, true);
            if (perBatch > 0 && budget < perBatch * batches)
            {
                Int128 affordable = budget > 0 ? budget / perBatch : 0;
                if (affordable >= 1) batches = (int)affordable; else { batches = 1; permille = budget > 0 ? (int)(budget * 1000 / perBatch) : 0; }
            }
            if (permille > 0 && (!steered || Steering!.MayBuyInputs(b))) { PostInputOrders(b, best, book, batches, permille); budget -= perBatch * batches * permille / 1000; }
        }
        if (busy == 0 && b.Stations.Count > 0) b.IdleDays++; else b.IdleDays = 0;
    }

    public void PostInputOrders(Business b, Recipe r, MarketBook? book, int batches = 1, int permille = 1000)
    {
        if (book == null) return;
        foreach (var (g, q) in r.Inputs)
        {
            long have = b.Store.Qty(g), want = q * Math.Max(1, batches) * permille / 1000;
            if (have >= want) continue;
            book.Post(new BuyOrder(b.Party, g, want - have, 3000) { Priority = 5 + r.Inputs.Length, OnFilled = InputHook(b), HookKind = 1, HookArg = b.Index });
        }
    }

    private void PlanExtraction(Business b, BusinessKind kind, GameTime now)
    {
        if (b.Mine >= 0) return;
        if (Resources == null) return;
        var book = Markets?.BookForSettlement(b.Settlement);
        var s = Pop.Region.Settlements[b.Settlement];
        // crew split: each extraction kind in season gets workers in proportion to the value of its hour of work; ties go to the lowest kind
        var open = kind.Extractions.Where(k => Extraction.SeasonMult(k, now) > 0).ToList();
        b.Jobs.Clear();
        if (open.Count == 0 || b.Staff.Count == 0) return;
        var scored = open.Select(k => (k, v: ExtractionValue(k, book))).OrderByDescending(x => x.v).ThenBy(x => x.k.Index).ToList();
        int n = b.Staff.Count;
        // everything goes to the best-paying kind unless its market is flooded (cover over 2 T); then to the next
        foreach (var (k, _) in scored)
        {
            if (book != null) { var gb = book.Goods[k.OutGood]; if (gb.StockUnits >= 2L * gb.Demand * gb.TargetCoverDays && scored.Count > 1) continue; }
            int row = PickTile(k, s.CenterX, s.CenterY, k.Resource ?? ResourceKind.Wood);
            if (Steered(b)) row = Steering!.TileFor(b, k, row);                                       // P5-F: on its own tiles
            b.Jobs.Add(new CrewJob { Kind = k.Index, Row = row, Workers = n });
            return;
        }
        var first = scored[0].k;
        int frow = PickTile(first, s.CenterX, s.CenterY, first.Resource ?? ResourceKind.Wood);
        if (Steered(b)) frow = Steering!.TileFor(b, first, frow);
        b.Jobs.Add(new CrewJob { Kind = first.Index, Row = frow, Workers = n });
    }

    /// <summary>What a worker-day of the extraction is worth at the book's mid, micro-mites (integer, review finding 8; only its order matters).</summary>
    private Int128 ExtractionValue(ExtractionKind k, MarketBook? book)
    {
        long micro = book == null ? Econ.Goods.BasePriceMilliMites(k.OutGood) : book.MidMicro(k.OutGood);
        return (Int128)k.RateGPerWorkerDay * micro;
    }

    /// <summary>Floor of the square root of a non-negative integer (Newton's method; review finding 8 replaced <c>Math.Sqrt</c>).</summary>
    public static long ISqrt(long n)
    {
        if (n <= 0) return 0;
        long x = n, y = (x + 1) / 2;
        while (y < x) { x = y; y = (x + n / x) / 2; }
        return x;
    }

    /// <summary>
    /// The tile a crew works (resources spec 3.6): <c>argmax (stock / cap x 1000 - distance_m / 8)</c> over tiles of the resource within the search radius of the
    /// settlement, ties to the lowest tile index. -1 when none.
    /// </summary>
    public int PickTile(ExtractionKind k, int cx, int cy, ResourceKind kind)
    {
        if (Resources == null) return -1;
        int r = k.RadiusM / Region.TileM, tx = cx / Region.TileM, ty = cy / Region.TileM;
        int best = -1; long bestScore = long.MinValue;
        for (int y = Math.Max(0, ty - r); y <= Math.Min(Region.Size - 1, ty + r); y++)
            for (int x = Math.Max(0, tx - r); x <= Math.Min(Region.Size - 1, tx + r); x++)
            {
                int row = Resources.Find(Region.Index(x, y), kind);
                if (row < 0) continue;
                long dx = (x - tx) * (long)Region.TileM, dy = (y - ty) * (long)Region.TileM;
                long dist = ISqrt(dx * dx + dy * dy);
                if (dist > k.RadiusM) continue;
                long cap = Resources.CapG(row); if (cap <= 0) continue;
                long score = Resources.StockG(row) * 1000 / cap - dist / 8;
                if (score > bestScore) { bestScore = score; best = row; }
            }
        return best;
    }

    // ---------- H5 daily globals ----------

    /// <summary>H5: vacancy counters and wage offers (production spec 3.3: +5% each 15 unfilled days, -3% when 3+ applicants queued; floor, x0.8 to x1.5 of the class wage).</summary>
    public void DailyGlobals(GameTime now)
    {
        foreach (var b in Businesses)
        {
            if (b.State != BusinessState.Open) continue;
            var kind = KindOf(b);
            if (kind.Stage != null) continue;                                                      // P7-IN1: the works sets its stages' crews and wage offers
            bool vacancy = b.Staff.Count < kind.StaffCapacity && kind.StaffCapacity > 0 && !kind.Trader && kind.Slots.Length > 0;
            if (!vacancy) { b.UnfilledDays = 0; continue; }
            b.UnfilledDays++;
            if (b.UnfilledDays % 15 == 0)
                b.WageOfferMites = (short)Labour.NextOffer(RecipeBook.WagePerDay(kind.Slots[0].Wage) * Econ.WageMultPm / 1000, b.WageOfferMites);
        }
    }

    // ---------- S5 seasonal ----------

    /// <summary>S5: dividends to owners, loss counting and closures (production spec 3.5).</summary>
    public void Seasonal(GameTime now)
    {
        var ledger = Econ.Ledger;
        Markets?.ExtraCoin.Clear();
        foreach (var b in Businesses)
        {
            if (b.State != BusinessState.Open) continue;
            long cash = ledger.Balance(b.Purse).Mites;
            b.WageBillPerDay = b.WagesSeason / TimeConst.DaysPerSeason; b.InputSpendPerDay = b.InputsSeason / TimeConst.DaysPerSeason;
            long net = cash + b.DividendsSeason - b.CashStart;
            var kind = KindOf(b);
            if (kind.Stage != null)                                                                 // P7-IN1: no dividends or closing for losses; the works' one purse is counted once
            {
                if (Stages?.CountsCash(b) != false) Markets?.ExtraCoin.Add((b.Settlement, cash));
                b.WagesSeason = 0; b.DividendsSeason = 0; b.CashStart = cash;
                continue;
            }
            if (kind.Trader)
            {
                // a merchant pays out half of the cash it holds above twice its working capital each season (integration decision I41; was half of the season's gain
                // over the cash it kept last season, a ratchet: the stalls' boxes held 47 percent of the region's coin by year 40)
                // and it keeps at least two months of its wages and costs (decision I44): paid down to twice a small start-up capital, most stalls ran on under a
                // month's wages and one bad season broke them
                long keepCash = Math.Max(2 * b.Capital, MerchantKeepDays * MerchantDayCost(b));
                long gain = cash - keepCash, div = gain > 0 ? gain * Labour.PayoutPct / 100 : 0;
                if (div > 0 && b.OwnerHousehold >= 0 && ledger.Transfer(b.Purse, Econ.HouseholdPurse[b.OwnerHousehold], new Coin(div), TransferReason.Dividend)) { b.Dividends += div; b.DividendsSeason += div; cash -= div; }
                // a stall with no coin and nothing on the book two seasons running has failed (integration decision I38): it closes, and opens again under a new
                // merchant with his own capital when the book needs one (LabourMarket.Reopen), instead of standing as a broke merchant for ever
                bool broke = cash < MerchantSolventMites && !HoldsLots(b);
                b.LossSeasons = broke ? (byte)Math.Min(255, b.LossSeasons + 1) : (byte)0;
                if (b.LossSeasons >= 2) Close(b);
            }
            else
            {
                long div = kind.Salaried && kind.Stations.Length == 0 ? 0 : Labour.Dividend(cash, b.WageBillPerDay, b.InputSpendPerDay, Labour.PayoutPct, b.Capital);
                bool steered = Steered(b);
                // P5-F: a business with shareholders pays every holder pro rata (the ownership world); else the Phase 2 transfer to the owner's household
                if (div > 0 && steered && Steering!.PayDividend(b, div)) { b.Dividends += div; b.DividendsSeason += div; cash -= div; }
                else if (div > 0 && b.OwnerHousehold >= 0 && ledger.Transfer(b.Purse, Econ.HouseholdPurse[b.OwnerHousehold], new Coin(div), TransferReason.Dividend)) { b.Dividends += div; b.DividendsSeason += div; cash -= div; }
                bool costCentre = (kind.Salaried && kind.Stations.Length == 0) || kind.Extractions.Length > 0;       // a manor has no revenue, a crew works the lord's woods and waters: neither is closed for losses
                b.LossSeasons = net < 0 && !costCentre ? (byte)Math.Min(255, b.LossSeasons + 1) : (byte)0;
                if (steered) Steering!.SeasonClosed(b, net, cash, now);
                if (b.LossSeasons >= 3 && cash < 5 * b.WageBillPerDay && !OwnedByHero(b)) Close(b);             // P5-F: the hero's business closes by his order (the "close if loss" directive), not by Phase 2's rule
            }
            if (b.State == BusinessState.Open) Markets?.ExtraCoin.Add((b.Settlement, cash));
            b.WagesSeason = 0; b.InputsSeason = 0; b.DividendsSeason = 0; b.CashStart = ledger.Balance(b.Purse).Mites;
        }
    }

    /// <summary>Days of its wages and costs a stall keeps in its box before it pays its owner (decision I44).</summary>
    public const int MerchantKeepDays = 90;
    /// <summary>Days of its wages and costs a merchant keeps back when he buys stock (decision I45).</summary>
    public const int MerchantReserveDays = 45;

    /// <summary>A stall's daily wages and costs: its wage bill, at least a skilled day wage for each hand (one for a stall the merchant keeps alone: his own keep).</summary>
    public long MerchantDayCost(Business b) => Math.Max(b.WageBillPerDay, (long)Math.Max(1, b.Staff.Count) * RecipeBook.WagePerDay(WageClass.Skilled));

    /// <summary>A merchant with under this much coin is not solvent (the soak's measure, 10 crowns).</summary>
    public const long MerchantSolventMites = 1000;

    private bool HoldsLots(Business b)
    {
        if (Markets == null) return false;
        foreach (var gb in Markets.BookForSettlement(b.Settlement).Goods) foreach (var l in gb.Lots) if (l.Owner == b.Party && l.Qty > 0) return true;
        return false;
    }

    /// <summary>Closes a business: staff released, goods and cash go back to the owner's household (production spec 3.5).</summary>
    public void Close(Business b)
    {
        b.State = BusinessState.Closed;
        for (int i = b.Staff.Count - 1; i >= 0; i--) Quit(b, i);
        foreach (var st in b.Stations) { st.Recipe = -1; st.ProgressMicro = 0; }
        int hh = b.OwnerHousehold;
        if (hh >= 0)
        {
            long cash = Econ.Ledger.Balance(b.Purse).Mites;
            if (cash > 0) Econ.Ledger.Transfer(b.Purse, Econ.HouseholdPurse[hh], new Coin(cash), TransferReason.Dividend);
            foreach (var (g, q) in b.Store.Rows()) Store.Move(b.Store, Econ.HouseholdStore[hh], g, q);
        }
        if (KindOf(b).Trader && Markets != null) Markets.BookForSettlement(b.Settlement).Merchants.Remove(b.Party);
    }

    /// <summary>Opens a closed workshop under a new owner (integration decision I19): half the owner's household coin goes in as the working capital (reason Gift).</summary>
    public void Reopen(Business b, int owner)
    {
        b.State = BusinessState.Open; b.LossSeasons = 0; b.IdleDays = 0; b.UnfilledDays = 0; b.WagesSeason = b.InputsSeason = b.DividendsSeason = 0;
        SetOwner(b, owner);
        int hh = b.OwnerHousehold;
        if (hh >= 0)
        {
            long amt = Econ.Ledger.Balance(Econ.HouseholdPurse[hh]).Mites / 2;
            if (amt > 0) Econ.Ledger.Transfer(Econ.HouseholdPurse[hh], b.Purse, new Coin(amt), TransferReason.Gift);
        }
        b.CashStart = Econ.Ledger.Balance(b.Purse).Mites; b.Capital = Math.Max(b.CashStart, 1000);
        if (KindOf(b).Trader && Markets != null)
        {
            var ms = Markets.BookForSettlement(b.Settlement).Merchants;
            if (!ms.Contains(b.Party)) { int at = 0; while (at < ms.Count && ms[at].Index < b.Party.Index) at++; ms.Insert(at, b.Party); }   // id order, as RegisterTraders made it
        }
    }

    /// <summary>
    /// A workshop's owner puts coin back into the cash box when it has run down to under half its capital (integration decision I19): up to the capital, never more than
    /// half of what his household holds, at most once a week. Keeps a baker or a brewer in business through a lean season; the dividends bring it back.
    /// </summary>
    private void TopUpWorkshop(Business b, int day)
    {
        int hh = b.OwnerHousehold; if (hh < 0 || b.Capital <= 0 || (day + b.Index) % 7 != 0) return;
        long cash = Econ.Ledger.Balance(b.Purse).Mites; if (cash >= b.Capital / 2) return;
        long pay = Math.Min(b.Capital - cash, Econ.Ledger.Balance(Econ.HouseholdPurse[hh]).Mites / 2);
        if (pay > 0 && Econ.Ledger.Transfer(Econ.HouseholdPurse[hh], b.Purse, new Coin(pay), TransferReason.Gift)) cash += pay;
        // still short: the fief's treasury advances the market craftsmen's working capital, at most a fiftieth of its coin and half the capital (the treasury's idle coin goes back to work, decision I20)
        if (cash < b.Capital / 4)
        {
            int fief = b.Settlement >= 0 ? Pop.Region.Settlements[b.Settlement].FiefIndex : -1;
            if (fief >= 0 && fief < Pop.Region.Fiefs.Count && Econ.Ledger.TryFind(PurseKind.Treasury, Pop.Region.Fiefs[fief].Id, out var t))
            {
                long adv = Math.Min(b.Capital / 2, Econ.Ledger.Balance(t).Mites / 50);
                if (adv > 0 && Econ.Ledger.Transfer(t, b.Purse, new Coin(adv), TransferReason.Stipend)) { TreasuryPaidMites += adv; AdvancesMites += adv; }
            }
        }
    }
    public long AdvancesMites;

    /// <summary>The callback of a business's input order: the coin paid goes to the season's input spend.</summary>
    public Action<TradeResult> InputHook(Business b) => res => { b.InputsSeason += res.PaidMites; };

    // ---------- structural demand ----------

    private int[]? _recipeOrder;

    /// <summary>Recipes ordered so that those making goods nearer the consumer come first (flour's recipe after bread's), for propagating demand down the chains.</summary>
    private int[] RecipeOrder()
    {
        if (_recipeOrder != null) return _recipeOrder;
        var recipes = Content.Recipes.Recipes; int ng = Econ.Goods.Count;
        var depth = new int[ng];
        for (int pass = 0; pass < 24; pass++)
        {
            bool changed = false;
            foreach (var r in recipes)
            {
                int d = 0; foreach (var (g, _) in r.Inputs) d = Math.Max(d, depth[g] + 1);
                foreach (var (g, _) in r.Outputs) if (depth[g] < d) { depth[g] = d; changed = true; }
            }
            if (!changed) break;
        }
        return _recipeOrder = [.. Enumerable.Range(0, recipes.Count).OrderByDescending(i => depth[recipes[i].Outputs[0].Good]).ThenBy(i => i)];
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Derived demand (integration decision I7): the units of its main output the households and the other businesses of a book want each day set how many batches
    /// the book's workshops of that recipe need to run, up to what their people can finish, and those batches' inputs are demand in turn, down the chain
    /// (bread, flour, wheat). The old rule of at most one batch a day a station is gone.
    /// </remarks>
    public void AddStructuralDemand(MarketSystem markets, long[][] demand)
    {
        var recipes = Content.Recipes.Recipes; int nr = recipes.Count;
        var capByBook = new SortedDictionary<int, long[]>();     // milli-batches a day per recipe
        foreach (var b in Businesses)
        {
            if (b.State != BusinessState.Open) continue;
            var kind = KindOf(b); if (kind.Trader || kind.Stations.Length == 0) continue;
            int book = markets.BookOf[b.Settlement]; if (demand[book] == null) continue;
            if (!capByBook.TryGetValue(book, out var cap)) capByBook[book] = cap = new long[nr];
            foreach (var st in b.Stations.GroupBy(x => x.Kind))
            {
                var rs = kind.RecipesFor(st.Key).ToList(); if (rs.Count == 0) continue;
                foreach (var r in rs) cap[r.Index] += 1000L * st.Count() * CapacityBatchesPerDay(b, r) / rs.Count;
            }
        }
        var order = RecipeOrder();
        foreach (var (book, cap) in capByBook)
        {
            var arr = demand[book];
            foreach (int ri in order)
            {
                if (cap[ri] == 0) continue;
                var r = recipes[ri]; var (main, outQ) = r.Outputs[0];
                long same = 0; foreach (var o in recipes) if (o.Outputs[0].Good == main) same += cap[o.Index];
                long milli = Math.Min(cap[ri], arr[main] * 1000 / Math.Max(1, outQ) * cap[ri] / Math.Max(1, same));
                if (milli <= 0) continue;
                foreach (var (g, q) in r.Inputs) arr[g] += q * milli / 1000;
            }
        }
    }
}
