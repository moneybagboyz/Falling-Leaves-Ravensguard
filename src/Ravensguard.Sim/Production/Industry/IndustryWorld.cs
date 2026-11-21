using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Production.Industry;

/// <summary>What a stage did on its last day, for the tab and the tests (derived each day, never saved).</summary>
public sealed record StageStatus(int RecognisedTier, int WorkingTier, int Have, int Need, StageStop Stop, ushort StopGood, long MadeG, long PotentialG, long MovedG, long LentMilliHours)
{
    public bool ShortHanded => WorkingTier > 0 && WorkingTier < RecognisedTier;
}

/// <summary>
/// The industry runtime in the home region (P7-IN1, industries 4): founds works (one business a stage, one purse a works), runs each works' day in its H4 slice (crews and the stage formula, real inputs from real stores, outputs on to the next stage or the gate, spare hands
/// lent to the yard, packing and shipping stages, wages from the works' purse), keeps each stage's balance ring and the payback counters, and answers the labour market (<see cref="IStageHost"/>). Every coin is one ledger transfer between real purses and every good a store move
/// or a create or destroy with its reason, so the audits stay exact. The tier a stage works at is its recognised <see cref="WorkStage.Tier"/> (IN2 derives it from what is installed) limited by its crew.
/// </summary>
public sealed class IndustryWorld : IStageHost
{
    public IndustryBook Book { get; }
    public ProductionSystem Prod { get; }
    public ContentDatabase Content { get; }
    public ulong Seed { get; }
    public RuleChron Chron { get; set; } = RuleChron.Silent;
    /// <summary>The industry output slider in percent (100 to 500; <c>Difficulty.IndustryOutputPct</c>): it multiplies every staged works' rates and capacities, the player's and the NPCs' alike.</summary>
    public Func<int> SliderPct { get; set; } = () => 100;
    /// <summary>A stage's machine share in permille (the premises' net, <c>large-buildings.md</c> 4.1); null: 1,000, no machine's say. IN2 wires the recognised machines to it.</summary>
    public Func<Works, WorkStage, int>? PowerPm { get; set; }
    /// <summary>A stop the world puts on a stage (a flooded mine, a frozen wheel, a depleted body, open hacks in winter); null or <see cref="StageStop.None"/>: none.</summary>
    public Func<Works, WorkStage, (StageStop Stop, ushort Good)>? ExternalStop { get; set; }
    /// <summary>What the ground gives an extraction stage: grams asked, grams given (an ore body, a woodland); null: unlimited.</summary>
    public Func<Works, WorkStage, long, long>? Deposit { get; set; }
    /// <summary>A forced dressed share in permille (tests); null: the draw of stream 0x741.</summary>
    public Func<int>? ForceYieldPm { get; set; }
    private readonly Dictionary<ulong, StageStatus> _last = [];

    public IndustryWorld(IndustryBook book, ProductionSystem prod, ContentDatabase content, ulong seed)
    {
        Book = book; Prod = prod; Content = content; Seed = seed;
        prod.Stages = this;
        Rebind();
    }

    private IndustryContent Ind => Prod.Content.Industries;
    private IndustrySettingsDef Set => Ind.Settings;
    private Ledger Led => Prod.Econ.Ledger;
    private PersonTable People => Prod.Pop.People;

    public StageStatus? Status(ulong business) => _last.TryGetValue(business, out var s) ? s : null;
    public StageKind KindOf(WorkStage s) => Prod.ById(new EntityId(s.Business)) is { } b ? Prod.KindOf(b).Stage! : throw new InvalidOperationException("stage without business");
    public Business? BusinessOf(WorkStage s) => Prod.ById(new EntityId(s.Business));

    /// <summary>Points every non-first stage of every works at the works' one purse (a load makes each business with a purse of its own; the first stage's is the works').</summary>
    public void Rebind()
    {
        foreach (var w in Book.Works)
        {
            if (w.Stages.Count == 0 || Prod.ById(new EntityId(w.Stages[0].Business)) is not { } head) continue;
            for (int i = 1; i < w.Stages.Count; i++) if (Prod.ById(new EntityId(w.Stages[i].Business)) is { } b) Share(b, head.Purse);
        }
    }

    private static void Share(Business b, PurseId purse)
    {
        b.Purse = purse;
        b.Party = new MarketParty(b.Id, purse, b.Store, Ravensguard.Sim.Economy.Markets.PartyKind.Business, b.Index);
    }

    // ------------------------------------------------------------------ founding

    /// <summary>
    /// Founds a works of a chain at a settlement (industries 3.2): one stage business a stage kind, the first tier, the crew target at the tier's minimum, every stage's output on to the next and the last stage's to the gate. The first stage's cash box is the works' one purse; the
    /// capital moves into it from <paramref name="payer"/> (<c>Gift</c>, recorded as the works' capital). The buildings' bill is recorded as each plan completes (IN2).
    /// </summary>
    public OwnResult Found(string chain, int owner, int settlement, PurseId payer, long capital, int day)
    {
        var ck = Ind.Chain(chain); if (ck == null) return OwnResult.No("in_no_such_chain", chain);
        if (owner < 0 || owner >= People.Count) return OwnResult.No("in_no_owner");
        if ((uint)settlement >= (uint)Prod.Pop.Region.Settlements.Count) return OwnResult.No("bz_no_place");
        if (capital < 0 || Led.Balance(payer).Mites < capital) return new OwnResult(false, "bz_cannot_afford", [Quotes.Money(capital)], capital, CannotAfford: true);
        var w = new Works { Owner = owner, Chain = chain, Settlement = settlement, StartedDay = day };
        Business? head = null;
        for (int i = 0; i < ck.Stages.Length; i++)
        {
            var sk = ck.Stages[i];
            var b = Prod.NewBusiness(sk.Business, settlement, -1, owner);
            b.OwnerWorks = false;
            if (head == null) head = b; else Share(b, head.Purse);
            var t1 = sk.Tier(1);
            var ws = new WorkStage { Business = b.Id.Value, Tier = 1, CrewTarget = t1.CrewMin, Route = i == ck.Stages.Length - 1 ? Routing.Gate : Routing.Next, DistanceM = sk.Role == StageRole.Haul ? sk.RefDistanceM : 0 };
            w.Stages.Add(ws);
            SetWage(b, sk, ws);
            if (i > 0) w.Links.Add(new WorkLink { From = i - 1, To = i });
        }
        if (capital > 0 && !Led.Transfer(payer, head!.Purse, new Coin(capital), TransferReason.Gift)) return new OwnResult(false, "bz_cannot_afford", [Quotes.Money(capital)], capital, CannotAfford: true);
        head!.Capital = capital; head.CashStart = capital;
        w.EndBalance = Led.Balance(head.Purse).Mites;
        Book.Works.Add(w);
        var place = Prod.Pop.Region.Settlements[settlement].Id;
        var who = People.IdOf(owner);
        Chron.Chron(GameTime.FromGameHours((long)day * 24 + 9), "works_founded", "ch_ind_founded",
            [ChronParam.Person("owner", who), ChronParam.TextKey("chain", "ind_chain_" + chain), ChronParam.Place("place", place)],
            [who, head.Id], [new Cause(CauseKind.Industry, null, NoteKey: "ch_note_works_founded")]);
        return new OwnResult(true, "in_founded", [ck.Name, Quotes.Money(capital)], capital, head.Id);
    }

    private void SetWage(Business b, StageKind k, WorkStage ws)
        => b.WageOfferMites = (short)Math.Clamp((long)RecipeBook.WagePerDay(k.Wage) * ws.WageOfferPm / 1000 * Prod.Econ.WageMultPm / 1000, 1, short.MaxValue);

    // ------------------------------------------------------------------ the owner's settings

    private (Works W, WorkStage S, StageKind K)? Find(ulong business)
    {
        var r = Book.StageOf(business); if (r == null) return null;
        var s = r.Value.Works.Stages[r.Value.Index];
        return (r.Value.Works, s, KindOf(s));
    }

    /// <summary>The crew target (4.1): between the recognised tier's minimum and maximum; hiring stops at the maximum.</summary>
    public OwnResult SetCrew(ulong business, int target)
    {
        if (Find(business) is not { } f) return OwnResult.No("in_no_such_stage");
        var t = f.K.Tier(f.S.Tier);
        f.S.CrewTarget = Math.Clamp(target, t.CrewMin, t.CrewMax);
        return OwnResult.Yes("in_crew_set", f.S.CrewTarget);
    }

    public OwnResult SetWageOffer(ulong business, int pm)
    {
        if (Find(business) is not { } f) return OwnResult.No("in_no_such_stage");
        f.S.WageOfferPm = Math.Clamp(pm, 500, 2000);
        SetWage(BusinessOf(f.S)!, f.K, f.S);
        return OwnResult.Yes("in_wage_set", f.S.WageOfferPm / 10);
    }

    public OwnResult SetRoute(ulong business, Routing route)
    {
        if (Find(business) is not { } f) return OwnResult.No("in_no_such_stage");
        if (route is Routing.Contract or Routing.Caravan) return OwnResult.No("in_route_later");
        f.S.Route = route;
        return OwnResult.Yes("in_route_set");
    }

    /// <summary>Sets the link into stage <paramref name="to"/> of a works (4.3): its distance, the carts and wagons that serve it, a crane, packed goods.</summary>
    public OwnResult SetLink(ulong works, int to, int distanceM, int carts, int wagons)
    {
        var w = Book.Works.FirstOrDefault(x => x.Id == works); if (w == null) return OwnResult.No("in_no_such_works");
        var l = w.Links.FirstOrDefault(x => x.To == to); if (l == null) return OwnResult.No("in_no_such_link");
        l.DistanceM = Math.Max(0, distanceM); l.Carts = Math.Max(0, carts); l.Wagons = Math.Max(0, wagons);
        return OwnResult.Yes("in_link_set");
    }

    /// <summary>The owner puts coin into the works' purse (<c>Gift</c>) or takes it out (<c>Dividend</c>).</summary>
    public OwnResult Cash(ulong works, PurseId owner, long mites)
    {
        var w = Book.Works.FirstOrDefault(x => x.Id == works); if (w == null) return OwnResult.No("in_no_such_works");
        var purse = Prod.ById(new EntityId(w.Id))!.Purse;
        if (mites > 0)
        {
            if (Led.Balance(owner).Mites < mites || !Led.Transfer(owner, purse, new Coin(mites), TransferReason.Gift)) return new OwnResult(false, "bz_cannot_afford", [Quotes.Money(mites)], mites, CannotAfford: true);
            w.Injected += mites; return OwnResult.Yes("in_cash_in", Quotes.Money(mites));
        }
        long take = Math.Min(-mites, Led.Balance(purse).Mites);
        if (take <= 0 || !Led.Transfer(purse, owner, new Coin(take), TransferReason.Dividend)) return OwnResult.No("bz_box_empty");
        w.Injected -= take; return OwnResult.Yes("in_cash_out", Quotes.Money(take));
    }

    /// <summary>The recognised tier of a stage (IN2's recognition calls this when a piece changes); the crew target moves to the tier's band.</summary>
    public void SetTier(ulong business, int tier)
    {
        if (Find(business) is not { } f) return;
        f.S.Tier = Math.Clamp(tier, 1, f.K.TierCount);
        var t = f.K.Tier(f.S.Tier);
        f.S.CrewTarget = Math.Clamp(f.S.CrewTarget, t.CrewMin, t.CrewMax);
        if (f.S.CrewTarget < t.CrewMin) f.S.CrewTarget = t.CrewMin;
    }

    // ------------------------------------------------------------------ the labour market's view (IStageHost)

    public bool SlotsFor(Business b, out (Occupation Occupation, int Count, WageClass Wage)[] slots)
    {
        slots = [];
        if (Prod.KindOf(b).Stage == null) return false;
        if (Book.StageOf(b.Id.Value) is not { } r) { slots = []; return true; }
        var ws = r.Works.Stages[r.Index]; var k = Prod.KindOf(b).Stage!;
        if (r.Works.Closed) return true;
        var t = k.Tier(ws.Tier);
        int target = Math.Clamp(ws.CrewTarget, t.CrewMin, t.CrewMax);
        if (target <= 0) return true;
        if (t.SupportPm > 0)
        {
            int prim = (int)(((long)target * 1000 + 1000 + t.SupportPm - 1) / (1000 + t.SupportPm)); prim = Math.Clamp(prim, 1, target);
            slots = prim >= target ? [(k.Primary, target, k.Wage)] : [(k.Primary, prim, k.Wage), (k.Support, target - prim, WageClass.Unskilled)];
        }
        else slots = [(k.Primary, target, k.Wage)];
        return true;
    }

    public bool CountsCash(Business b) => Book.StageOf(b.Id.Value) is not { } r || r.Index == 0;

    // ------------------------------------------------------------------ the day

    private sealed class Run
    {
        public Business B = null!; public StageKind K = null!; public WorkStage S = null!;
        public List<StageWorker> Crew = [];
        public StagePlan Plan;
        public long MadeG, MovedG, IdleMilli, LentMilli;
        public StageStop Stop; public ushort StopGood = ushort.MaxValue;
        public bool Working;
    }

    /// <summary>Runs the works that are due in this game hour (a works' slice is its index modulo 24, as a business's is), once a day.</summary>
    public void Hour(GameTime now)
    {
        int hour = now.Hour, day = (int)now.TotalDays;
        for (int i = 0; i < Book.Works.Count; i++)
            if (i % 24 == hour && !Book.Works[i].Closed) Day(Book.Works[i], day, now);
    }

    /// <summary>One works' day (industries 4.1): arrears, the stages' work in chain order, spare hands lent, the goods on their way, wages, the next day's purchases, the rings and the payback.</summary>
    public void Day(Works w, int day, GameTime now)
    {
        if (w.Closed || w.Stages.Count == 0) return;
        var head = Prod.ById(new EntityId(w.Id)); if (head == null) return;
        if (People.Status[w.Owner] is not (PersonStatus.Alive or PersonStatus.Hero)) { CloseWorks(w); return; }
        long bal0 = Led.Balance(head.Purse).Mites;
        var runs = new List<Run>(w.Stages.Count);
        long inputs = 0;
        foreach (var s in w.Stages)
        {
            var b = Prod.ById(new EntityId(s.Business)); if (b == null || b.State != BusinessState.Open) { runs.Add(null!); continue; }
            var r = new Run { B = b, K = Prod.KindOf(b).Stage!, S = s };
            runs.Add(r);
            long seen = b.InputsSeason - s.InputsSeen; if (seen < 0) seen = b.InputsSeason;
            if (seen > 0) { s.Add(day, WorkStage.Inputs, seen); inputs += seen; }
            s.InputsSeen = b.InputsSeason;
        }
        // the income the market paid since the last works day: what the box gained that the owner and the wages do not explain
        long income = Math.Max(0, bal0 - w.EndBalance + inputs - w.Injected);
        w.Injected = 0;
        SplitIncome(w, runs, day, income);
        // arrears first: a stage that cannot pay what it owes does not work (workers quit after three short days)
        foreach (var r in runs)
        {
            if (r == null) continue;
            PayWages(r.B, now);
            if (r.B.ArrearsMites > 0) { r.Stop = StageStop.Unpaid; }
        }
        for (int i = 0; i < runs.Count; i++) if (runs[i] != null) Staff(w, runs[i], day);
        long pool = 0;
        for (int i = 0; i < runs.Count; i++)
        {
            var r = runs[i]; if (r == null || r.K.IsHandling) continue;
            Work(w, i, r, day);
            pool += r.IdleMilli;
        }
        for (int i = 0; i < runs.Count; i++) if (runs[i] != null) Forward(w, i, runs, ref pool, day);
        for (int i = 0; i < runs.Count; i++) if (runs[i] != null) Wage(runs[i], day, now);
        for (int i = 0; i < runs.Count; i++) if (runs[i] != null) Buy(w, i, runs[i], day);
        for (int i = 0; i < runs.Count; i++) if (runs[i] != null) Finish(w, runs[i], day, now);
        w.EndBalance = Led.Balance(head.Purse).Mites;
        long net = 0; foreach (var s in w.Stages) net += s.Net(day, 1);
        w.CumulativeNetMites += net;
        if (w.PaidBackDay < 0 && w.BuildingsCostMites > 0 && w.CumulativeNetMites >= w.BuildingsCostMites) w.PaidBackDay = day;
    }

    private void SplitIncome(Works w, List<Run> runs, int day, long income)
    {
        if (income <= 0) return;
        var sellers = new List<WorkStage>();
        foreach (var r in runs) if (r != null && r.S.Route == Routing.Gate) sellers.Add(r.S);
        if (sellers.Count == 0) sellers.Add(w.Stages[0]);
        long each = income / sellers.Count, rem = income - each * sellers.Count;
        for (int i = 0; i < sellers.Count; i++) sellers[i].Add(day, WorkStage.Income, each + (i == 0 ? rem : 0));
    }

    private void CloseWorks(Works w)
    {
        w.Closed = true;
        foreach (var s in w.Stages) if (Prod.ById(new EntityId(s.Business)) is { State: BusinessState.Open } b) Prod.Close(b);
    }

    /// <summary>Reads the crew: persons on the books who are alive, with the skill the stage's trade asks and the share of a full worker their age gives.</summary>
    private void Staff(Works w, Run r, int day)
    {
        var pt = People; var k = r.K;
        bool support = k.Tiers.Any(t => t.SupportPm > 0);
        foreach (int p in r.B.Staff)
        {
            if (pt.Status[p] != PersonStatus.Alive) continue;
            int skill = k.Skill == Skill.None ? 40 : pt.SkillLevelOf(p, k.Skill);
            int weight = pt.AgeYears(p, day) < 16 ? Set.ApprenticePm : 1000;
            r.Crew.Add(new StageWorker(skill, weight, !support || pt.Occupation[p] == k.Primary));
        }
    }

    private int SpanPm(Works w, Run r, int day)
    {
        var sc = Prod.Content.Scale; var pt = People; var seen = new HashSet<int>(); int span = 0;
        void Overseer(int p) { if (seen.Add(p)) span += Crews.Span(sc, pt.SkillLevelOf(p, Skill.Leadership)); }
        if (r.B.Owner >= 0) Overseer(r.B.Owner);
        foreach (int p in r.B.Staff)
            if (pt.Status[p] == PersonStatus.Alive && Crews.RoleOf(sc, r.K.Skill == Skill.None ? 40 : pt.SkillLevelOf(p, r.K.Skill), pt.AgeYears(p, day)) == CrewRole.Master) Overseer(p);
        if (Prod.Scale != null && Prod.Scale.Foremen.TryGetValue(r.B.Id.Value, out var fm)) foreach (int p in fm) if (r.B.Staff.Contains(p)) Overseer(p);
        return Crews.SpeedPm(sc, r.Crew.Count, span);
    }

    private int PowerOf(Works w, Run r)
    {
        if (r.K.Tier(r.S.Tier).PowerW <= 0 || PowerPm == null) return 1000;
        return Math.Clamp(PowerPm(w, r.S), 0, 1000);
    }

    private static long StoreMass(Business b) => b.Store.TotalMassG();

    /// <summary>Extraction and process stages (4.1): the crew's day, limited by the tier's capacity, the slider, the ground, the inputs and the room; real goods only.</summary>
    private void Work(Works w, int idx, Run r, int day)
    {
        var k = r.K; var b = r.B; var s = r.S;
        r.Plan = StageMath.Plan(k, s.Tier, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(r.Crew), SpanPm(w, r, day), PowerOf(w, r), SliderPct());
        if (r.Stop == StageStop.Unpaid) return;
        if (r.Plan.UnderCrewed) { r.Stop = StageStop.UnderCrewed; return; }
        if (ExternalStop?.Invoke(w, s) is { Stop: not StageStop.None } ext) { r.Stop = ext.Stop; r.StopGood = ext.Good; return; }
        var t = k.Tier(r.Plan.WorkingTier);
        long want = r.Plan.PotentialG;
        if (want <= 0) return;
        long mass = StoreMass(b), room = Math.Max(0, t.StoreG - mass);
        if (k.Role == StageRole.Extract)
        {
            if (room <= 0) { r.Stop = StageStop.Full; return; }
            want = Math.Min(want, room);
            if (Deposit != null) { long got = Deposit(w, s, want); if (got <= 0) { r.Stop = StageStop.Depleted; return; } want = Math.Min(want, got); }
            foreach (var (g, pm) in k.Outputs)
            {
                long q = want * pm / 1000; if (q <= 0) continue;
                b.Store.Create(g, q, CreateReason.Mined); Prod.ProducedUnits[g] += q;
            }
            r.MadeG = want; r.Working = true;
            r.IdleMilli = IdleOf(r, want);
            return;
        }
        // process: the main good, then every input, then the room
        long haveMain = b.Store.Qty(k.Main);
        if (haveMain <= 0) { r.Stop = StageStop.NoInput; r.StopGood = k.Main; return; }
        want = Math.Min(want, haveMain);
        foreach (var (g, pm, fuel) in k.Inputs)
        {
            long perM = (long)pm * t.InputPm / 1000; if (perM <= 0) continue;
            long have = b.Store.Qty(g);
            if (have <= 0) { r.Stop = fuel ? StageStop.NoFuel : StageStop.NoInput; r.StopGood = g; return; }
            want = Math.Min(want, have * 1000 / perM);
        }
        if (want <= 0) { r.Stop = StageStop.NoInput; r.StopGood = k.Main; return; }
        int yieldPm = k.YieldLoPm > 0 ? DrawYield(b, s, day) : -1;
        long outMass = 0, inMass = 0;
        foreach (var (g, pm) in k.Outputs) outMass += want * (k.YieldLoPm > 0 && g == k.Outputs[0].Good ? yieldPm : pm) / 1000;
        foreach (var (g, pm, _) in k.Inputs) inMass += want * ((long)pm * t.InputPm / 1000) / 1000;
        long net = outMass - want - inMass;
        if (net > 0)
        {
            long growth = Math.Max(0, t.StoreG - mass);
            if (growth <= 0) { r.Stop = StageStop.Full; return; }
            if (growth < net) { want = want * growth / net; if (want <= 0) { r.Stop = StageStop.Full; return; } }
        }
        long made = 0;
        foreach (var (g, pm, _) in k.Inputs)
        {
            long q = want * ((long)pm * t.InputPm / 1000) / 1000; if (q <= 0) continue;
            if (!b.Store.Destroy(g, q, DestroyReason.Consumed)) throw new InvalidOperationException("stage inputs vanished");
            Prod.ConsumedUnits[g] += q;
        }
        long firstOut = 0;
        for (int o = 0; o < k.Outputs.Length; o++)
        {
            var (g, pm) = k.Outputs[o];
            long q = want * (o == 0 && yieldPm >= 0 ? yieldPm : pm) / 1000; if (q <= 0) continue;
            b.Store.Create(g, q, CreateReason.Crafted); Prod.ProducedUnits[g] += q; made += q; if (o == 0) firstOut = q;
        }
        long waste = k.YieldLoPm > 0 ? Math.Max(0, want - firstOut) : want * k.WastePm / 1000;
        long consumed = want - waste;
        if (waste > 0 && !b.Store.Destroy(k.Main, waste, DestroyReason.Waste)) throw new InvalidOperationException("stage main good vanished");
        if (consumed > 0 && !b.Store.Destroy(k.Main, consumed, DestroyReason.Consumed)) throw new InvalidOperationException("stage main good vanished");
        Prod.ConsumedUnits[k.Main] += want;
        r.MadeG = want; r.Working = true;
        r.IdleMilli = IdleOf(r, want);
    }

    private int DrawYield(Business b, WorkStage s, int day)
    {
        if (ForceYieldPm != null) { s.Batch++; return ForceYieldPm(); }
        var k = Prod.KindOf(b).Stage!;
        int span = Math.Max(1, k.YieldHiPm - k.YieldLoPm + 1);
        uint u = DetRng.NextUInt(Seed, s.Batch++, b.Id.Value, RngStreams.OreYield, 0);
        return k.YieldLoPm + (int)((ulong)u * (ulong)span >> 32);
    }

    /// <summary>The thousandths of a worker-hour the stage's crew had left when the day's work was done (the lent hours of 4.1): the share of the day's potential it did not use, over the whole crew's day.</summary>
    private long IdleOf(Run r, long madeG)
    {
        long pot = r.Plan.PotentialG; if (pot <= 0 || madeG >= pot) return 0;
        return (long)r.Crew.Count * Set.WorkdayHours * 1000 * (pot - madeG) / pot;
    }

    private static ushort[] ForwardGoods(StageKind k) => k.IsHandling ? [k.Main] : [.. k.Outputs.Select(o => o.Good)];

    /// <summary>Moves a stage's goods on (4.3, 5.10): to the next stage, bounded by the link's hauling, the stage's own handling (a handling stage's crews, and the spare hours lent at the first tier) and the next store's room; or to the gate.</summary>
    private void Forward(Works w, int idx, List<Run> runs, ref long pool, int day)
    {
        var r = runs[idx]; var k = r.K; var b = r.B; var s = r.S;
        long limit = long.MaxValue;
        if (k.IsHandling)
        {
            r.Plan = StageMath.Plan(k, s.Tier, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(r.Crew), SpanPm(w, r, day), PowerOf(w, r), SliderPct());
            if (r.Stop == StageStop.Unpaid) return;
            if (r.Plan.UnderCrewed) { r.Stop = StageStop.UnderCrewed; return; }
            if (ExternalStop?.Invoke(w, s) is { Stop: not StageStop.None } ext) { r.Stop = ext.Stop; r.StopGood = ext.Good; return; }
            limit = r.Plan.PotentialG * StageMath.HaulScalePm(k, k.Tier(Math.Max(1, r.Plan.WorkingTier)), s.DistanceM) / 1000;
            // spare hands: a first tier that takes no hands may run on the hours other stages could not use (never counted toward a minimum)
            if (k.Tier(1).CrewMin == 0 && pool > 0)
            {
                long onHand = 0; foreach (var g in ForwardGoods(k)) onHand += b.Store.Qty(g);
                long short_ = Math.Max(0, onHand - limit);
                if (short_ > 0)
                {
                    long need = StageMath.LentMilliHoursFor(k, short_, Set.WorkdayHours), take = Math.Min(pool, need);
                    pool -= take; r.LentMilli = take; limit += StageMath.LentG(k, take, Set.WorkdayHours);
                }
            }
        }
        if (s.Route == Routing.Store) return;
        long moved = 0;
        if (s.Route == Routing.Gate)
        {
            if (Prod.Markets == null) return;
            var book = Prod.Markets.BookForSettlement(b.Settlement);
            foreach (var g in ForwardGoods(k))
            {
                long q = Math.Min(b.Store.Qty(g), limit - moved); if (q <= 0) continue;
                moved += book.SellOrList(b.Party, g, q, 900, day);
            }
            r.MovedG = moved; return;
        }
        if (s.Route != Routing.Next || idx + 1 >= runs.Count || runs[idx + 1] == null) return;
        var next = runs[idx + 1]; var link = w.Links.FirstOrDefault(l => l.From == idx && l.To == idx + 1);
        if (link != null) limit = Math.Min(limit, StageMath.LinkCapacityG(Set, link));
        long nextRoom = Math.Max(0, next.K.Tier(Math.Max(1, next.S.Tier)).StoreG - StoreMass(next.B));
        foreach (var g in ForwardGoods(k))
        {
            long q = Math.Min(Math.Min(b.Store.Qty(g), limit - moved), nextRoom - moved); if (q <= 0) continue;
            if (!Store.Move(b.Store, next.B.Store, g, q)) throw new InvalidOperationException("stage store lost goods");
            moved += q;
        }
        r.MovedG = moved;
        if (!k.IsHandling && moved == 0 && ForwardGoods(k).Any(g => b.Store.Qty(g) > 0) && nextRoom <= 0) { r.Stop = r.Stop == StageStop.None ? StageStop.Full : r.Stop; }
    }

    /// <summary>A day's wage for each person on the books (the class wage by role, never above the stage's class; half for an apprentice), paid from the works' purse; a stage that could not pay what it owes does not work tomorrow.</summary>
    private void Wage(Run r, int day, GameTime now)
    {
        var b = r.B; var k = r.K; var pt = People; var sc = Prod.Content.Scale;
        if (r.Stop != StageStop.Unpaid)
            for (int i = 0; i < b.Staff.Count; i++)
            {
                int p = b.Staff[i]; if (pt.Status[p] != PersonStatus.Alive) continue;
                int age = pt.AgeYears(p, day);
                var role = Crews.RoleOf(sc, k.Skill == Skill.None ? 40 : pt.SkillLevelOf(p, k.Skill), age);
                var wc = role == CrewRole.Master && pt.Occupation[p] != k.Primary ? WageClass.Unskilled : Crews.WageOf(role, pt.Occupation[p] == k.Primary ? k.Wage : WageClass.Unskilled);
                long wage = (long)RecipeBook.WagePerDay(wc) * r.S.WageOfferPm / 1000 * Prod.Econ.WageMultPm / 1000 * (age < 16 ? 50 : 100) / 100;
                b.Owed[i] += wage;
            }
        PayWages(b, now);
    }

    private void PayWages(Business b, GameTime now)
    {
        long before = Led.Balance(b.Purse).Mites;
        Prod.PayWages(b, now);
        long paid = before - Led.Balance(b.Purse).Mites;
        if (paid > 0 && Book.StageOf(b.Id.Value) is { } r) r.Works.Stages[r.Index].Add((int)now.TotalDays, WorkStage.Wages, paid);
    }

    /// <summary>The market buys the stage's inputs for tomorrow: what the chain does not supply (charcoal, firewood, or ore for a smeltery with no mine behind it), up to two days of the stage's day, at up to three times the reference, from the works' purse.</summary>
    private void Buy(Works w, int idx, Run r, int day)
    {
        if (Prod.Markets == null || r.K.Role != StageRole.Process) return;
        var book = Prod.Markets.BookForSettlement(r.B.Settlement);
        var t = r.K.Tier(r.S.Tier);
        long dayG = r.Plan.PotentialG > 0 ? r.Plan.PotentialG : Math.Min(t.CapacityG > 0 ? t.CapacityG : t.RateG * Math.Max(1, t.CrewMin), t.RateG * Math.Max(1, r.S.CrewTarget)) * SliderPct() / 100;
        if (dayG <= 0) return;
        void Order(ushort g, long perMainPm)
        {
            if (idx > 0 && ProducedUpstream(w, idx, g)) return;
            long want = dayG * perMainPm / 1000 * 2, have = r.B.Store.Qty(g);
            if (have >= want) return;
            book.Post(new BuyOrder(r.B.Party, g, want - have, 3000) { Priority = 5, OnFilled = Prod.InputHook(r.B), HookKind = 1, HookArg = r.B.Index });
        }
        Order(r.K.Main, 1000);
        foreach (var (g, pm, _) in r.K.Inputs) Order(g, (long)pm * t.InputPm / 1000);
    }

    private bool ProducedUpstream(Works w, int idx, ushort good)
    {
        for (int i = 0; i < idx; i++)
        {
            var k = KindOf(w.Stages[i]);
            if (k.Main == good || k.Outputs.Any(o => o.Good == good)) return true;
        }
        return false;
    }

    private void Finish(Works w, Run r, int day, GameTime now)
    {
        var s = r.S; var k = r.K;
        s.LastDay = day;
        bool stopped = r.Stop != StageStop.None;
        if (stopped)
        {
            if (s.Stop == r.Stop && s.StopGood == r.StopGood) s.StopDays++; else { s.Stop = r.Stop; s.StopGood = r.StopGood; s.StopDays = 1; }
            if (s.StopDays == Set.StopChronicleDays + 1) WriteStopped(w, r, now);
        }
        else { s.Stop = StageStop.None; s.StopGood = ushort.MaxValue; s.StopDays = 0; }
        var plan = r.Plan;
        _last[s.Business] = new StageStatus(s.Tier, plan.WorkingTier, plan.Have == 0 ? r.Crew.Count : plan.Have, plan.Need, r.Stop, r.StopGood, r.MadeG, plan.PotentialG, r.MovedG, r.LentMilli);
    }

    private void WriteStopped(Works w, Run r, GameTime now)
    {
        var who = People.IdOf(w.Owner); var place = Prod.Pop.Region.Settlements[w.Settlement].Id;
        var ps = new List<ChronParam> { ChronParam.Person("owner", who), ChronParam.Place("place", place), ChronParam.TextKey("stage", "ind_stage_" + r.K.Id), ChronParam.TextKey("reason", "ind_stop_" + r.S.Stop.ToString().ToLowerInvariant()), ChronParam.Days("days", r.S.StopDays) };
        Chron.Chron(now, "works_stopped", "ch_ind_stopped", ps, [who, r.B.Id], [new Cause(CauseKind.Industry, null, NoteKey: "ch_note_stage_stopped")]);
    }
}
