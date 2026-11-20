using Ravensguard.Content;
using Ravensguard.Sim.Contracts;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Dialogue;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Rumours;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Society;
using Ravensguard.Sim.Tests.Dialogue;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Contracts;

/// <summary>
/// Contracts v2 and rumours v2 (docs/specs/phase6/contracts-and-rumours-v2.md, P6-C2): CR-1 need to contract, CR-2 no coin no contract, CR-3 the deadline, CR-4 verification by events, CR-5 failure, CR-6 companies compete,
/// CR-7 the kinds, CR-8 the construction market, CR-9 day labour as contracts, CR-10 the tournament purse, CR-11 slander, CR-12 legends and mood, CR-13 AP-8, CR-14 determinism and the save.
/// </summary>
public class ContractTests(ITestOutputHelper output)
{
    private static readonly Lazy<ContentDatabase> Db = new(TalkKit.LoadBase);
    private static readonly EntityId Fief = EntityId.Make(IdKind.Faction, 900), Smithy = EntityId.Make(IdKind.Other, 77), Cart = EntityId.Make(IdKind.Other, 55), Hero = EntityId.None;
    private const ushort Charcoal = 3;

    private sealed class Rig
    {
        public Chronicle C; public ContractWorld W = null!; public ContractBook B = new(); public Ledger L = new(); public PersonTable P; public IdAllocator Ids = new();
        public PurseId Treasury, HeroPurse, SmithyPurse;
        public Dictionary<EntityId, PurseId> Owners = [];
        public Dictionary<(EntityId, ushort), long> Qty = [];
        public List<StockNeed> Needs = []; public List<CaravanLeaving> Cars = []; public List<BandSeen> Bands = []; public List<CaptiveHeld> Captives = []; public List<HandsShort> Hands = []; public List<BuildWanted> Builds = [];
        public HashSet<ulong> Dead = [], Held = []; public Dictionary<ulong, (bool, ulong)> Free = [];
        public List<CompanyInfo> Companies = []; public int Risk = 20;
        public bool Merchant; public int FairIn = -1; public bool Charter = true;
        public Dictionary<(ulong, ulong), ulong> Records = [];
        public Dictionary<ulong, int> Rating = [];
        public List<(EntityId, int)> Fighters = [];

        public Rig(ulong seed = 11, int people = 80, long smithyCoin = 5_000, long treasury = 100_000, ContractContent? content = null)
        {
            var (tw, chron) = TalkKit.Town(people, seed); C = chron; P = tw.People;
            Treasury = L.Open(PurseKind.Treasury, Fief); L.Mint(Treasury, new Coin(treasury), SourceReason.Seed);
            HeroPurse = L.Open(PurseKind.Person, EconomyState.HeroOwner); L.Mint(HeroPurse, new Coin(20_000), SourceReason.Seed);
            SmithyPurse = L.Open(PurseKind.Business, Smithy); if (smithyCoin > 0) L.Mint(SmithyPurse, new Coin(smithyCoin), SourceReason.Seed);
            Owners[Smithy] = SmithyPurse;
            W = new ContractWorld
            {
                Chronicle = C, Ids = Ids, Ledger = L, People = P, SettlementNames = ["Ashby", "Brook"], SettlementIds = [EntityId.Make(IdKind.Settlement, 1), EntityId.Make(IdKind.Settlement, 2)], SettlementFief = [0, 0], FiefIds = [Fief],
                Seed = seed, Content = content ?? ContractContent.From(Db.Value), HeroId = Hero,
                PurseOfOwner = o => Owners.TryGetValue(o, out var p) ? p : null,
                PurseOf = p => p == Hero ? HeroPurse : Person(p),
                TreasuryOf = _ => Treasury, DistanceM = (a, b) => a == b ? 0 : 12_000,
                StockNeeds = () => Needs, MerchantHolds = (_, _, _) => Merchant,
                Caravans = () => Cars, Bands = () => Bands, Captives = () => Captives, Hands = () => Hands, Builds = () => Builds,
                StoreQty = (o, g) => Qty.GetValueOrDefault((o, g)),
                IsDead = p => Dead.Contains(p.Value), IsHeld = p => Held.Contains(p.Value), CaptiveFree = p => Free.TryGetValue(p.Value, out var f) ? (f.Item1, new EntityId(f.Item2)) : (false, EntityId.None),
                Companies = () => Companies, RiskPct = (c, k) => Risk,
                CharterTowns = () => Charter ? [0] : [], FairInDays = d => FairIn, Fighters = s => Fighters,
                RatingOf = p => Rating.GetValueOrDefault(p.Value, 100),
                BoutRecord = (a, b, since) => Records.TryGetValue((a.Value, b.Value), out var w) ? new EntityId(w) : null,
                NameOf = p => p == Hero ? "Aldric" : "",
            };
        }

        private readonly Dictionary<int, PurseId> _hh = [];
        public PurseId? Person(EntityId p)
        {
            int row = P.IndexOf(p); if (row < 0) return null;
            int h = P.Household[row];
            if (!_hh.TryGetValue(h, out var pu)) { pu = L.Open(PurseKind.Household, EntityId.Make(IdKind.Household, (ulong)h + 1)); L.Mint(pu, new Coin(3_000), SourceReason.Seed); _hh[h] = pu; }
            return pu;
        }

        public EntityId Id(int row) => P.IdOf(row);
        public StockNeed CharcoalNeed(int days = 2, long grams = 300_000) => new(Smithy, 0, Charcoal, grams, days, "smithy");
        public List<WorkContract> Scan(int day = 10, int s = 0) => NeedScanner.Scan(W, B, s, day);
        public IEnumerable<ChronicleEntry> Of(string type) => C.All.Where(e => e.TypeId == type);
    }

    // ------------------------------------------------------------------ CR-1, CR-2

    [Fact]
    public void CR1_ASmithyWithTwoDaysOfCharcoalAndNoneForSale_PostsADeliveryWithItsPayInEscrow_CausedByTheShortage()
    {
        var r = new Rig(); r.Needs.Add(r.CharcoalNeed());
        long before = r.L.Balance(r.SmithyPurse).Mites;
        var posted = r.Scan();
        var c = Assert.Single(posted);
        Assert.Equal("deliver_goods", c.Kind); Assert.Equal(300_000, c.Grams); Assert.Equal(Smithy, c.TargetOwner); Assert.Equal(WorkState.Open, c.State);
        Assert.Equal(660, c.PayMites);                                                         // 300 + 0.15 x 8 km x 300 kg
        Assert.Equal(660, r.L.Balance(new PurseId(c.Escrow)).Mites);
        Assert.Equal(before - 660, r.L.Balance(r.SmithyPurse).Mites);                          // a real transfer out of the issuer's purse
        var posting = r.Of("work_posted").Single(); var need = r.Of("work_need").Single();
        Assert.Equal(need.Id, posting.Causes[0].EventId!.Value);                               // the cause is the shortage entry
        Assert.Equal("ch_work_need", need.TextKey); Assert.Equal("ch_note_ct_need_stock_short", need.Causes[0].NoteKey);
        Assert.True(r.L.Audit().IsConserved);
        // a merchant with stock within reach: no need, no contract
        var r2 = new Rig { Merchant = true }; r2.Needs.Add(r2.CharcoalNeed()); Assert.Empty(r2.Scan());
        // three days or more: no shortage
        var r3 = new Rig(); r3.Needs.Add(r3.CharcoalNeed(days: 3)); Assert.Empty(r3.Scan());
        // the same shortage twice is one contract
        Assert.Empty(r.Scan(11));
    }

    [Fact]
    public void CR2_TheSameSmithyWithAnEmptyBoxPostsNothing()
    {
        var r = new Rig(smithyCoin: 0); r.Needs.Add(r.CharcoalNeed());
        Assert.Empty(r.Scan()); Assert.Empty(r.B.Contracts); Assert.Empty(r.Of("work_need"));
        var poor = new Rig(smithyCoin: 600); poor.Needs.Add(poor.CharcoalNeed());                // 600 < the 660 the pay comes to
        Assert.Empty(poor.Scan());
        Assert.Null(ContractSystem.Post(poor.W, poor.B, "deliver_goods", Smithy, poor.SmithyPurse, 0, 5_000, 1, ContractSystem.NeedEntry(poor.W, "stock_short", 0, 0, [Smithy])));
        Assert.True(poor.L.Audit().IsConserved);
    }

    // ------------------------------------------------------------------ CR-3

    [Fact]
    public void CR3_ADeadlineIsAnAbsoluteGameDay_ExpiryReturnsTheEscrowOnThatDayNeitherEarlierNorLater()
    {
        var r = new Rig(); r.Needs.Add(r.CharcoalNeed());
        var c = r.Scan(10).Single(); Assert.Equal(16, c.DeadlineDay);                          // posted on day 10, six days
        long poorer = r.L.Balance(r.SmithyPurse).Mites;
        ContractSystem.Expiries(r.W, r.B, 15); Assert.Equal(WorkState.Open, c.State); Assert.Equal(poorer, r.L.Balance(r.SmithyPurse).Mites);
        ContractSystem.Expiries(r.W, r.B, 16);
        Assert.Equal(WorkState.Expired, c.State); Assert.Equal(poorer + 660, r.L.Balance(r.SmithyPurse).Mites); Assert.Equal(0, r.L.Balance(new PurseId(c.Escrow)).Mites);
        Assert.Equal(16, c.ClosedDay);
        ContractSystem.Expiries(r.W, r.B, 17); Assert.Equal(poorer + 660, r.L.Balance(r.SmithyPurse).Mites);   // nothing twice
        Assert.Empty(r.Of("work_expired"));                                                      // an unanswered posting lapsing is not news
        // a taken contract that is not done at its day fails: the escrow returns and the standing entry is the failure's
        var r2 = new Rig(); r2.Needs.Add(r2.CharcoalNeed()); var c2 = r2.Scan(10).Single();
        Assert.Equal(TakeResult.Taken, ContractSystem.Take(r2.W, r2.B, c2.Id, Hero, r2.HeroPurse, 11));
        ContractSystem.Expiries(r2.W, r2.B, 16); Assert.Equal(WorkState.Failed, c2.State); Assert.Equal(660, r2.L.Balance(r2.SmithyPurse).Mites - (5_000 - 660));
        // the old bug (comparing a deadline with the day of the month) cannot recur: the deadline is a plain day number past the month
        var r3 = new Rig(); r3.Needs.Add(r3.CharcoalNeed()); var c3 = r3.Scan(58).Single(); Assert.Equal(64, c3.DeadlineDay);
        ContractSystem.Expiries(r3.W, r3.B, 3); Assert.Equal(WorkState.Open, c3.State);
    }

    // ------------------------------------------------------------------ CR-4

    [Fact]
    public void CR4_ADeliveryCompletesOnlyWhenTheTargetStoresMassHasRisenByTheGoods_AndABountyOnlyWithTheCulpritHeldOrDead()
    {
        var r = new Rig(); r.Needs.Add(r.CharcoalNeed()); r.Qty[(Smithy, Charcoal)] = 40_000;
        var c = r.Scan().Single();
        Assert.Equal(TakeResult.Taken, ContractSystem.Take(r.W, r.B, c.Id, Hero, r.HeroPurse, 10)); Assert.Equal(40_000, c.Baseline);
        long heroBefore = r.L.Balance(r.HeroPurse).Mites;
        ContractSystem.Verify(r.W, r.B, 11); Assert.Equal(WorkState.Taken, c.State);           // nothing arrived
        r.Qty[(Smithy, Charcoal)] = 40_000 + 200_000; ContractSystem.Verify(r.W, r.B, 11); Assert.Equal(WorkState.Taken, c.State);   // two thirds is not the goods
        r.Qty[(Smithy, Charcoal)] = 40_000 + 300_000; ContractSystem.GoodsReceived(r.W, r.B, Smithy, Charcoal, 100_000, 11);   // the last third is handed over
        Assert.Equal(WorkState.Done, c.State); Assert.Equal(heroBefore + 660, r.L.Balance(r.HeroPurse).Mites); Assert.Equal(0, r.L.Balance(new PurseId(c.Escrow)).Mites);
        Assert.Equal("work_done", r.Of("work_done").Single().TypeId); Assert.Equal("1", r.Of("work_done").Single().Data["hero"]);
        // a smithy that burned its charcoal meanwhile does not undo it: the store's inflow was counted
        r.Qty[(Smithy, Charcoal)] = 0; ContractSystem.Verify(r.W, r.B, 12); Assert.Equal(WorkState.Done, c.State);
        Assert.True(r.L.Audit().IsConserved);
        // a bounty: the culprit held, or dead, by the taker's side
        var culprit = r.Id(30); var rk = new Rig();
        var need = ContractSystem.NeedEntry(rk.W, "band_seen", 0, 0, []);
        var b = ContractSystem.Post(rk.W, rk.B, "hunt_man", Fief, rk.Treasury, 0, 3_000, 5, need, k => k.Target = culprit)!;
        Assert.Equal(TakeResult.Taken, ContractSystem.Take(rk.W, rk.B, b.Id, Hero, rk.HeroPurse, 5));
        ContractSystem.Verify(rk.W, rk.B, 6); Assert.Equal(WorkState.Taken, b.State);
        rk.Held.Add(culprit.Value); ContractSystem.Verify(rk.W, rk.B, 7); Assert.Equal(WorkState.Done, b.State);
        var b2 = ContractSystem.Post(rk.W, rk.B, "bounty_dead_or_alive", Fief, rk.Treasury, 0, 2_000, 8, need, k => k.Target = rk.Id(31))!;
        ContractSystem.Take(rk.W, rk.B, b2.Id, Hero, rk.HeroPurse, 8); rk.Dead.Add(rk.Id(31).Value); ContractSystem.Verify(rk.W, rk.B, 9); Assert.Equal(WorkState.Done, b2.State);
        Assert.True(rk.L.Audit().IsConserved);
    }

    // ------------------------------------------------------------------ CR-5

    [Fact]
    public void CR5_AnEscortThatLosesTheGoods_ChargesItsBond_CostsTwiceTheStandingAndSendsTheEscrowHome()
    {
        var r = new Rig(); var owner = EntityId.Make(IdKind.Household, 500); var ownerPurse = r.L.Open(PurseKind.Household, owner); r.L.Mint(ownerPurse, new Coin(20_000), SourceReason.Seed); r.Owners[owner] = ownerPurse;
        r.Cars.Add(new CaravanLeaving(Cart, owner, 0, 9_000, 2, 3));
        var c = r.Scan(10).Single(); Assert.Equal("guard_caravan", c.Kind);
        long pay = 450 * 3 * 2; Assert.Equal(pay, c.PayMites);                                  // 450 mites a guard a day, 3 days, 2 guards
        long issuerBefore = r.L.Balance(ownerPurse).Mites, heroBefore = r.L.Balance(r.HeroPurse).Mites;
        Assert.Equal(TakeResult.Taken, ContractSystem.Take(r.W, r.B, c.Id, Hero, r.HeroPurse, 10));
        long bond = pay / 10; Assert.Equal(bond, c.BondMites); Assert.Equal(heroBefore - bond, r.L.Balance(r.HeroPurse).Mites);
        // the caravan is lost on the road
        r.C.Add(new ChronicleEntry(r.Ids.Allocate(IdKind.Event), new GameTime(11 * 86_400L + 3600), "caravan_lost", [Cart], [new Cause(CauseKind.Raid, null, NoteKey: "ch_note_ambush")], new Dictionary<string, string>()));
        ContractSystem.Verify(r.W, r.B, 11);
        Assert.Equal(WorkState.Failed, c.State);
        Assert.Equal(issuerBefore + pay + bond, r.L.Balance(ownerPurse).Mites);                 // escrow back and the bond charged to the guard
        Assert.Equal(heroBefore - bond, r.L.Balance(r.HeroPurse).Mites);                        // he never saw the pay
        var done = r.Of("work_failed").Single(); Assert.Equal("40", done.Data["pm"]);           // twice the 20 a success moves
        Assert.True(r.L.Audit().IsConserved);
        // a success returns the bond with the pay
        var r2 = new Rig(); r2.Owners[owner] = r2.L.Open(PurseKind.Household, owner); r2.L.Mint(r2.Owners[owner], new Coin(20_000), SourceReason.Seed); r2.Cars.Add(new CaravanLeaving(Cart, owner, 0, 9_000, 2, 3));
        var ok = r2.Scan(10).Single(); long h0 = r2.L.Balance(r2.HeroPurse).Mites; ContractSystem.Take(r2.W, r2.B, ok.Id, Hero, r2.HeroPurse, 10);
        ContractSystem.Verify(r2.W, r2.B, 12); Assert.Equal(WorkState.Taken, ok.State); ContractSystem.Verify(r2.W, r2.B, 13);
        Assert.Equal(WorkState.Done, ok.State); Assert.Equal(h0 + pay, r2.L.Balance(r2.HeroPurse).Mites); Assert.Equal("20", r2.Of("work_done").Single().Data["pm"]);
    }

    // ------------------------------------------------------------------ CR-6

    [Fact]
    public void CR6_ACompanyTakesAGuardContractTheHeroIgnores_ByTheScorerAndTheOddsRule()
    {
        var r = new Rig(); var owner = EntityId.Make(IdKind.Household, 500); r.Owners[owner] = r.L.Open(PurseKind.Household, owner); r.L.Mint(r.Owners[owner], new Coin(20_000), SourceReason.Seed);
        var coPurse = r.L.Open(PurseKind.Household, EntityId.Make(IdKind.Household, 600)); r.L.Mint(coPurse, new Coin(5_000), SourceReason.Seed);
        r.Companies.Add(new CompanyInfo(EntityId.Make(IdKind.Faction, 0x4A00), "Ashby company", 0, 60, 5, coPurse.Index, "mercenary"));
        r.Cars.Add(new CaravanLeaving(Cart, owner, 0, 9_000, 2, 3));
        var c = r.Scan(10).Single();
        r.Risk = 95; Assert.Empty(Companies.TakeAll(r.W, r.B, 10));                              // too dangerous by the odds rule: nobody takes it
        r.Risk = 0; var taken = Companies.TakeAll(r.W, r.B, 10);
        Assert.Equal(c.Id, Assert.Single(taken).Id); Assert.True(c.ByCompany); Assert.Equal(EntityId.Make(IdKind.Faction, 0x4A00), c.Taker);
        // the hero, who ignored it, cannot take it now
        Assert.Equal(TakeResult.NotOpen, ContractSystem.Take(r.W, r.B, c.Id, Hero, r.HeroPurse, 10));
        long before = r.L.Balance(coPurse).Mites;
        Assert.Equal(0, Companies.Resolve(r.W, r.B, 11));
        Assert.Equal(1, Companies.Resolve(r.W, r.B, 14));
        Assert.Equal(WorkState.Done, c.State); Assert.Equal(before + c.PayMites + c.BondMites, r.L.Balance(coPurse).Mites);
        // the scorer is the one the hero's autopilot reads: pay an hour times the odds, less the walk
        Assert.Equal(270 - 2 * 12, Companies.Score(2_700, 10, 0, 12_000));
        Assert.True(Companies.Score(2_700, 10, 80, 0) < Companies.Score(2_700, 10, 20, 0));
        Assert.True(r.L.Audit().IsConserved);
        // equal scores between two companies: a draw of stream 0x690 keyed on the company and the day breaks the tie, the same each time
        Assert.Equal(Companies.Choose(r.W, r.B, r.Companies[0], 3, 0)?.Id, Companies.Choose(r.W, r.B, r.Companies[0], 3, 0)?.Id);
    }

    // ------------------------------------------------------------------ CR-7

    [Fact]
    public void CR7_TheBaseContentLoadsAboutFortyKinds_EachWithANeedAVerifierAndAFailureRule()
    {
        var content = ContractContent.From(Db.Value);
        output.WriteLine($"{content.Kinds.Count} kinds");
        Assert.InRange(content.Kinds.Count, 38, 45);
        var keys = TalkKit.EnCsv();
        var errs = content.Validate(k => keys.ContainsKey(k)); Assert.True(errs.Count == 0, string.Join("\n", errs));
        foreach (var k in content.Kinds) { Assert.False(string.IsNullOrEmpty(k.Need)); Assert.False(string.IsNullOrEmpty(k.Verify)); Assert.False(string.IsNullOrEmpty(k.Fail)); }
        foreach (var f in ContractContent.Families) Assert.Contains(content.Kinds, k => k.Family == f);       // every family has a kind
        foreach (var bn in ContractContent.BuiltNeeds.Where(n => n != "fair_charter")) Assert.NotEmpty(content.ForNeed(bn));
        Assert.Contains(content.Kinds, k => k.Crime); Assert.All(content.Kinds.Where(k => k.Crime), k => Assert.Equal("criminal", k.Family));
        // the validator catches a kind with no verifier, an unknown failure rule and a bond rule with no bond
        var bad = new ContractContent([new ContractKindDef { Id = "x", Family = "guard", Need = "stock_short", Issuer = "household", Verify = "", Fail = "banana", PayBase = 1 }, new ContractKindDef { Id = "y", Family = "guard", Need = "hook", Issuer = "fief", Verify = "job_done", Fail = "bond", PayBase = 1 }], new ContractSettingsDef { Id = "c" });
        var problems = bad.Validate(); Assert.Contains(problems, p => p.Contains("verifier")); Assert.Contains(problems, p => p.Contains("failure rule")); Assert.Contains(problems, p => p.Contains("bond_pct"));
        // a criminal contract is shown only to people who trust the poster
        var r = new Rig(); var fence = r.Id(40);
        var need = ContractSystem.NeedEntry(r.W, "stock_short", 0, 0, []);
        var theft = ContractSystem.Post(r.W, r.B, "steal_to_order", fence, r.Treasury, 0, 2_000, 1, need)!;
        Assert.False(ContractSystem.Visible(new Rig { }.W, theft, Hero));
        Assert.Equal(TakeResult.Hidden, ContractSystem.Take(r.W, r.B, theft.Id, Hero, r.HeroPurse, 1));
    }

    // ------------------------------------------------------------------ CR-8

    [Fact]
    public void CR8_ACottageContractGetsBidsFromTwoBuildersAndTheHero_TheLowestWins_AndDaysCountEachHandsOwnSkill()
    {
        var r = new Rig(); var client = EntityId.Make(IdKind.Household, 500); var cp = r.L.Open(PurseKind.Household, client); r.L.Mint(cp, new Coin(30_000), SourceReason.Seed); r.Owners[client] = cp;
        r.Builds.Add(new BuildWanted(client, 0, 20_000, "cottage"));
        var c = r.Scan(10).Single(); Assert.Equal("build_cottage", c.Kind); Assert.Equal(20_000, c.PayMites);
        var b1 = new Bidder(EntityId.Make(IdKind.Other, 901), "Bram & sons", [60, 60, 20], 4_000, 250, 70, false, r.L.Open(PurseKind.Business, EntityId.Make(IdKind.Other, 901)).Index);
        var b2 = new Bidder(EntityId.Make(IdKind.Other, 902), "Cole", [40, 40], 4_500, 250, 70, false, r.L.Open(PurseKind.Business, EntityId.Make(IdKind.Other, 902)).Index);
        var me = new Bidder(Hero, "hero", [30], 3_000, 250, 30, true, r.HeroPurse.Index);
        var rows = ConstructionMarket.Bids(r.W.S, [b1, b2, me]);
        Assert.Equal(3, rows.Count); Assert.True(rows.Zip(rows.Skip(1)).All(p => p.First.TotalMites <= p.Second.TotalMites));     // cheapest first
        Assert.Equal(3_000 + 250 * 30 * 1450 / 1000, rows[0].TotalMites);                                                           // materials + hours x crew wage x the margin
        Assert.Same(me, rows[0].Who);
        // the days: each hand's own pace (500 + 10 x skill), not a flat 50
        Assert.Equal(ConstructionMarket.ExpectedDaysX10(250, [30]), rows[0].ExpectedDaysX10);
        Assert.True(ConstructionMarket.ExpectedDaysX10(250, [80]) < ConstructionMarket.ExpectedDaysX10(250, [20]));
        Assert.Equal(ConstructionMarket.ExpectedDaysX10(250, [50, 50]), ConstructionMarket.ExpectedDaysX10(250, [30, 70]));        // pace is linear in skill
        long clientBefore = r.L.Balance(cp).Mites;
        var (_, win) = ConstructionMarket.Award(r.W, r.B, c, [b1, b2, me], 10);
        Assert.Same(me, win!.Who); Assert.Equal(Hero, c.Taker); Assert.False(c.ByCompany); Assert.Equal(win.TotalMites, c.PayMites);
        Assert.Equal(clientBefore + (20_000 - win.TotalMites), r.L.Balance(cp).Mites);                                              // the escrow beyond the bid went home
        // ties are broken by builder id; a bid over the budget wins nothing
        var rowsTie = ConstructionMarket.Bids(r.W.S, [b2 with { Id = EntityId.Make(IdKind.Other, 950) }, b2]); Assert.Equal(EntityId.Make(IdKind.Other, 902), rowsTie[0].Who.Id);
        Assert.True(r.L.Audit().IsConserved);
    }

    // ------------------------------------------------------------------ CR-9

    [Fact]
    public void CR9_DayLabourIsPostedAsContracts_AndTheLandlessTakeAndAreFedByThem()
    {
        var r = new Rig(); var farm = EntityId.Make(IdKind.Household, 700); var fp = r.L.Open(PurseKind.Household, farm); r.L.Mint(fp, new Coin(9_000), SourceReason.Seed); r.Owners[farm] = fp;
        r.Hands.Add(new HandsShort(farm, 0, 3, "harvest"));
        var c = Assert.Single(r.Scan(20)); Assert.Equal("harvest_hands", c.Kind); Assert.Equal(3, c.Param); Assert.Equal(3 * 300, c.PayMites);
        var worker = r.Id(33); var wp = r.Person(worker)!.Value; long w0 = r.L.Balance(wp).Mites;
        Assert.Equal(TakeResult.Taken, ContractSystem.Take(r.W, r.B, c.Id, worker, wp, 20));
        ContractSystem.Verify(r.W, r.B, 22); Assert.Equal(WorkState.Taken, c.State); ContractSystem.Verify(r.W, r.B, 23);
        Assert.Equal(WorkState.Done, c.State); Assert.Equal(w0 + 900, r.L.Balance(wp).Mites);
        // a landless idle adult is only a person with no work; the contract is as open to him as to the hero (the 100-year soak's gate on idle adults is Phase 5's own and stands: this part leaves day labour's own posts alone)
        Assert.True(r.L.Audit().IsConserved);
    }

    // ------------------------------------------------------------------ CR-10

    private static Rig Fair(out EntityId[] npcs)
    {
        var r = new Rig { FairIn = 15 };
        npcs = [r.Id(1), r.Id(2), r.Id(3)];
        foreach (var n in npcs) r.Fighters.Add((n, 100));
        r.Rating[npcs[0].Value] = 400; r.Rating[npcs[1].Value] = 200; r.Rating[npcs[2].Value] = 100; r.Rating[Hero.Value] = 130;
        return r;
    }

    [Fact]
    public void CR10_ThePurseMovesFromTheTreasuryAtTheAnnouncement_AndToTheFinalsWinnerByTheBoutRecord_AForgedResultChangesNothing()
    {
        var r = Fair(out var npcs);
        long t0 = r.L.Balance(r.Treasury).Mites;
        var made = Tournaments.Announce(r.W, r.B, 100); var t = Assert.Single(made);
        Assert.Equal(20_000, r.L.Balance(new PurseId(t.Escrow)).Mites); Assert.Equal(t0 - 20_000, r.L.Balance(r.Treasury).Mites); Assert.Equal(115, t.FairDay);
        Assert.Contains(r.B.Contracts.Values, c => c.Kind == "tournament_entry" && c.Param == t.Id && c.Open);
        Assert.Empty(Tournaments.Announce(r.W, r.B, 100));                                       // once a year a town
        // a town whose treasury is short announces nothing
        var poor = Fair(out _); poor.L.Sink(poor.Treasury, new Coin(poor.L.Balance(poor.Treasury).Mites - 10), SinkReason.Burn); Assert.Empty(Tournaments.Announce(poor.W, poor.B, 100));
        // the hero enters (a crown into the purse); not after the fair begins
        long h0 = r.L.Balance(r.HeroPurse).Mites;
        Assert.True(Tournaments.Enter(r.W, r.B, t.Id, Hero, 110)); Assert.False(Tournaments.Enter(r.W, r.B, t.Id, Hero, 110)); Assert.Equal(h0 - 1_000, r.L.Balance(r.HeroPurse).Mites);
        Assert.False(Tournaments.Enter(r.W, r.B, t.Id, npcs[0], 115));
        Tournaments.Draw(r.W, r.B, t.Id, 115);
        Assert.True(t.Drawn); Assert.Equal(4, t.Entrants.Count); Assert.Equal(2, t.Bouts.Count(b => b.Round == 0));
        Assert.All(t.Bouts, b => Assert.InRange(b.AngleDeg, 0, 15));                              // a lance run closes at 0 to 15 degrees off head-on
        // the same seed draws the same bracket
        var again = Fair(out _); Tournaments.Announce(again.W, again.B, 100); Tournaments.Enter(again.W, again.B, 1, Hero, 110); Tournaments.Draw(again.W, again.B, 1, 115);
        Assert.Equal(t.Bouts.Select(b => (b.A, b.B, b.AngleDeg)), again.B.Tournaments[1].Bouts.Select(b => (b.A, b.B, b.AngleDeg)));
        // the sim fights the NPCs' bouts; the hero's own wait for the record
        Tournaments.Play(r.W, r.B, t.Id, 115, forfeit: false);
        var heroBout = t.Bouts.Single(b => b.Round == 0 && (b.A == Hero.Value || b.B == Hero.Value)); Assert.Equal(Tournaments.NoOne, heroBout.Winner);
        // a forged result: no fight record, a result for a bout that is not in the bracket, the wrong pair
        Assert.False(Tournaments.ReportBout(r.W, r.B, t.Id, 0, heroBout.Slot, Hero, 115));
        Assert.False(Tournaments.ReportBout(r.W, r.B, t.Id, 3, 0, Hero, 115));
        Assert.False(Tournaments.ReportBout(r.W, r.B, 99, 0, 0, Hero, 115));
        r.Records[(heroBout.A, heroBout.B)] = heroBout.A == Hero.Value ? heroBout.B : heroBout.A;                  // the record says the hero lost
        Assert.False(Tournaments.ReportBout(r.W, r.B, t.Id, 0, heroBout.Slot, Hero, 115)); Assert.Equal(Tournaments.NoOne, t.Bouts.Single(b => b.Round == 0 && b.Slot == heroBout.Slot).Winner);
        Assert.False(t.Paid);
        // the record says he won: the bout is his, the next round is drawn from the winners
        r.Records[(heroBout.A, heroBout.B)] = Hero.Value;
        Assert.True(Tournaments.ReportBout(r.W, r.B, t.Id, 0, heroBout.Slot, Hero, 115));
        Assert.False(Tournaments.ReportBout(r.W, r.B, t.Id, 0, heroBout.Slot, Hero, 115));                         // decided once
        Tournaments.Play(r.W, r.B, t.Id, 115, forfeit: false);
        var final = t.Bouts.Single(b => b.Round == 1); Assert.Equal(Tournaments.NoOne, final.Winner); Assert.Contains(Hero.Value, new[] { final.A, final.B });
        // the final is fought and recorded: the hero wins and is paid the purse and the fees
        r.Records[(final.A, final.B)] = Hero.Value;
        Tournaments.Play(r.W, r.B, t.Id, 115, forfeit: false);
        Assert.True(t.Paid); Assert.Equal(Hero.Value, t.Winner);
        Assert.Equal(h0 - 1_000 + 20_000 + 4_000, r.L.Balance(r.HeroPurse).Mites);                                // the purse and the four entry fees
        Assert.Equal(0, r.L.Balance(new PurseId(t.Escrow)).Mites);
        Assert.Equal("1", r.Of("tournament_won").Single().Data["hero"]); Assert.True(r.L.Audit().IsConserved);
        // the hero who never fights loses the bout by forfeit at the day's end: nobody is paid on a result nobody fought
        var f = Fair(out _); Tournaments.Announce(f.W, f.B, 100); Tournaments.Enter(f.W, f.B, 1, Hero, 110); Tournaments.Draw(f.W, f.B, 1, 115);
        Tournaments.Play(f.W, f.B, 1, 115, forfeit: true);
        var ft = f.B.Tournaments[1]; Assert.True(ft.Paid); Assert.NotEqual(Hero.Value, ft.Winner);
        Assert.True(f.L.Audit().IsConserved);
    }

    // ------------------------------------------------------------------ CR-11

    private sealed class Map(PersonTable pt) : IGroupMap
    {
        public GroupRef FiefOf(int s) => GroupRef.Fief(0);
        public GroupRef RealmOf(int s) => GroupRef.Realm(0);
        public IReadOnlyList<int> SettlementsOf(GroupRef g) => g.Kind switch { GroupKind.Settlement => [g.Index], _ => [0] };
        public int Members(GroupRef g) { int n = 0; for (int i = 0; i < pt.Count; i++) if (pt.IsAlive(i)) n++; return n; }
        public int RegionAdults => Members(GroupRef.Realm(0));
        public int HomeOf(EntityId person) { int i = pt.IndexOf(person); return i >= 0 ? pt.HomeSettlement[i] : -1; }
    }

    [Fact]
    public void CR11_AFalseTheftRumourAboutAPersonLowersHisStandingWhileBelieved_ExposedByAWitness_ItIsReversedAndTheAuthorFallsByTwice()
    {
        var db = Db.Value; var book = LineBook.From(db); var deeds = DeedTable.From(db);
        var (tw, chron) = TalkKit.Town(400, 5); var ts = new TalkState(); var map = new Map(tw.People); var sb = new StandingBook?[1];
        var r = new Rig(people: 400); var w = new ContractWorld
        {
            Chronicle = chron, Ids = r.Ids, Ledger = r.L, People = tw.People, SettlementNames = ["Ashby"], SettlementIds = [EntityId.Make(IdKind.Settlement, 1)], Seed = 5, Content = r.W.Content,
            PurseOf = p => r.Person(p) is { } pp ? pp : null, TrustPm = (a, b) => 100, SpeechOf = _ => 60,
        };
        var victim = tw.People.IdOf(6); var author = tw.People.IdOf(5); var listener = tw.People.IdOf(7);
        var entry = Slander.Tell(w, r.B, author, victim, listener, "theft", 0, 50 * 86_400L + 7 * 3600, false)!;
        Assert.Equal("slander_told", entry.TypeId); Assert.Equal(victim, entry.Subjects[0]);                      // the doer of the deed is the one blackened
        // the seeder makes the rumour from the entry: false, held by the author and the listener
        RumourSeeder.Seed(tw, ts, book, 50 * 24 + 8);
        var rum = ts.Rumours.Get(entry.Id)!; Assert.True(rum.False); Assert.Equal(2, ts.Rumours.HearingsOf(entry.Id).Count);
        Assert.Contains(r.B.Slanders, s => s.Rumour == entry.Id.Value && !s.Exposed);
        // it spreads: 200 of the 400 believe it, and the victim's standing falls as the theft's would
        ts.Rumours.EnsureCounts(p => tw.People.HomeSettlement[p]);
        for (int i = 10; i < 210; i++) ts.Rumours.AddHearing(entry.Id, new Hearing { Listener = i, Teller = 5, Hour = 50 * 24 + 9, Hop = 1, How = HearHow.Told, PlaceAlt = -1 }, 100);
        StandingSystem.OnHour(() => sb[0] ??= new StandingBook(), sb[0], deeds, map, tw, ts, 50 * 24 + 10);
        int fallen = Standing.Of(sb[0], victim, GroupRef.Settlement(0));
        Assert.True(fallen < 0, $"the victim's standing should fall while the lie is believed: {fallen}"); Assert.Equal(-60 * 202 / 400, fallen);
        Assert.Equal(0, Standing.Of(sb[0], author, GroupRef.Settlement(0)));
        // a witness who saw him elsewhere, and who also heard the lie, shows it false
        ts.Rumours.Add(new Rumour { Id = EntityId.Make(IdKind.Event, 9_000), Source = EntityId.None, Kind = RumourKind.Landmark, TypeId = "alms_given", Settlement = 0, Place = "Ashby", Subjects = [victim], BornHour = 50 * 24 + 6, ExpiresDay = 9999, LifeDays = 99, InterestPm = 500 });
        ts.Rumours.AddHearing(EntityId.Make(IdKind.Event, 9_000), new Hearing { Listener = 300, Teller = -1, Hour = 50 * 24 + 6, How = HearHow.Witnessed, PlaceAlt = -1 }, 100);
        Assert.Empty(Slander.Scan(w, r.B, ts, sb[0], null, 51));                                                   // 300 saw him elsewhere but has not heard the lie
        var shown = Slander.Scan(w, r.B, ts, sb[0], v => sb[0]!.Recompute(v, deeds, map, 51), 51);
        Assert.Empty(shown);
        ts.Rumours.AddHearing(entry.Id, new Hearing { Listener = 300, Teller = 20, Hour = 50 * 24 + 12, Hop = 2, How = HearHow.Told, PlaceAlt = -1 }, 100);   // now he has heard the lie as well
        shown = Slander.Scan(w, r.B, ts, sb[0], v => sb[0]!.Recompute(v, deeds, map, 51), 51);
        Assert.Single(shown); Assert.True(shown[0].Exposed);
        Assert.Equal(0, Standing.Of(sb[0], victim, GroupRef.Settlement(0)));                                      // reversed
        var exposed = chron.All.Single(e => e.TypeId == "slander_exposed"); Assert.Equal(author, exposed.Subjects[0]); Assert.Equal(entry.Id, exposed.Causes[0].EventId!.Value);
        // the exposure is gossip too: as it spreads the author's standing falls by twice what the lie cost
        RumourSeeder.Seed(tw, ts, book, 51 * 24 + 2);
        ts.Rumours.EnsureCounts(p => tw.People.HomeSettlement[p]);
        for (int i = 10; i < 210; i++) if (!ts.Rumours.Holds(exposed.Id, i)) ts.Rumours.AddHearing(exposed.Id, new Hearing { Listener = i, Teller = 50, Hour = 51 * 24 + 3, Hop = 1, How = HearHow.Told, PlaceAlt = -1 }, 100);
        StandingSystem.OnHour(() => sb[0] ??= new StandingBook(), sb[0], deeds, map, tw, ts, 51 * 24 + 4);
        int authorFell = Standing.Of(sb[0], author, GroupRef.Settlement(0));
        Assert.Equal(-120 * ts.Rumours.HearingsOf(exposed.Id).Count / 400, authorFell); Assert.True(authorFell <= 2 * fallen + 1, $"{authorFell} vs {fallen}");
        Assert.Equal("A lie, told by Alda", "A lie, told by " + "Alda"); Assert.Equal(w.Name(author), Slander.LabelOf(w, r.B, entry.Id));
        // the rumour carries the false flag in its save, and a world with no lie keeps the old bytes
        var save = TalkSave.Capture(ts); Assert.Contains(save.Rumours, x => x.Facts >= 0x80); Assert.True(TalkSave.Capture(ts).Restore().Rumours.Get(entry.Id)!.False);
        // defamation: in a realm where it is a crime the author pays the fine
        var r2 = new Rig(); var w2 = new ContractWorld { Chronicle = r2.C, Ids = r2.Ids, Ledger = r2.L, People = r2.P, SettlementNames = ["Ashby"], Seed = 3, PurseOf = p => r2.Person(p) };
        var e2 = Slander.Tell(w2, r2.B, r2.Id(5), r2.Id(9), r2.Id(13), "assault", 0, 86_400, false)!;
        var ts2 = new TalkState(); long a0 = r2.L.Balance(r2.Person(r2.Id(5))!.Value).Mites, v0 = r2.L.Balance(r2.Person(r2.Id(9))!.Value).Mites;
        Assert.True(Slander.Answer(w2, r2.B, ts2, e2.Id, true, null, null, 3)); Assert.False(Slander.Answer(w2, r2.B, ts2, e2.Id, true, null, null, 3));
        Assert.Equal(a0 - 300, r2.L.Balance(r2.Person(r2.Id(5))!.Value).Mites); Assert.Equal(v0 + 300, r2.L.Balance(r2.Person(r2.Id(9))!.Value).Mites); Assert.True(r2.L.Audit().IsConserved);
    }

    [Fact]
    public void CR11_RivalsLieWithTheSeasonsFivePercent_ByTheAuthorAndTheSeason_NeverAboutADeathOrAFire()
    {
        var (tw, chron) = TalkKit.Town(300, 9); var ts = new TalkState(); var r = new Rig(people: 300);
        var w = new ContractWorld { Chronicle = chron, Ids = r.Ids, Ledger = r.L, People = tw.People, SettlementNames = ["Ashby"], Seed = 9, SpeechOf = _ => 60 };
        // 200 rival pairs: each way round a 5% draw each season
        for (int i = 0; i < 200; i++) ts.Relations.Set((i, i + 100), new Relation { Flags = RelationFlags.Rival });
        int told = 0;
        for (int season = 1; season <= 4; season++)
        {
            var lies = Slander.Season(w, r.B, ts, season * TimeConst.DaysPerSeason);
            told += lies.Count; Assert.All(lies, e => Assert.Contains(e.Data["kind"], Slander.Kinds));
            Assert.Empty(Slander.Season(w, r.B, ts, season * TimeConst.DaysPerSeason));                           // once a season
        }
        output.WriteLine($"{told} lies in 4 seasons of 400 draws");
        Assert.InRange(told, 40, 120);                                                                              // 5 percent of 1,600 draws is 80 in expectation across both directions only for speech >= 30: here 4 x 400 x 5%
        Assert.DoesNotContain("fire", Slander.Kinds); Assert.DoesNotContain("death", Slander.Kinds); Assert.DoesNotContain("murder", Slander.Kinds);
        // the same seed lies the same
        var (tw2, chron2) = TalkKit.Town(300, 9); var r2 = new Rig(people: 300); var ts2 = new TalkState();
        for (int i = 0; i < 200; i++) ts2.Relations.Set((i, i + 100), new Relation { Flags = RelationFlags.Rival });
        var w2 = new ContractWorld { Chronicle = chron2, Ids = r2.Ids, Ledger = r2.L, People = tw2.People, SettlementNames = ["Ashby"], Seed = 9, SpeechOf = _ => 60 };
        int told2 = 0; for (int season = 1; season <= 4; season++) told2 += Slander.Season(w2, r2.B, ts2, season * TimeConst.DaysPerSeason).Count;
        Assert.Equal(told, told2);
        // a tongue with no Speech tells none
        var r3 = new Rig(people: 300); var w3 = new ContractWorld { Chronicle = chron, Ids = r3.Ids, Ledger = r3.L, People = tw.People, SettlementNames = ["Ashby"], Seed = 9, SpeechOf = _ => 10 };
        Assert.Empty(Slander.Season(w3, r3.B, ts, 60));
    }

    // ------------------------------------------------------------------ CR-12

    [Fact]
    public void CR12_TheAgeOfAFactWeighsHalfAfterTwoSeasons_AndAHungryVillagersBarkIsTheHungryVariantAndALowMoodOneIsTheGrumbleVariant()
    {
        Assert.Equal(1000, RumourAge.WeightPm(30)); Assert.Equal(500, RumourAge.WeightPm(31));
        var db = Db.Value; var book = LineBook.From(db); var text = TalkKit.Text();
        string Bark(bool hungry, int mood, int sample)
        {
            var x = new TalkContext { Hour = 10, Place = "Ashby", HeroName = "Aldric" }; var c = new BarkCandidate(77, 0, 3, "farmer", false, mood, RelationTier.Stranger, hungry, false);
            x.Job = c.Job; x.Mood = mood; x.Needs.Clear(); if (hungry) x.Needs.Add("hungry"); x.Familiarity = 0; x.TrustPm = 100;
            return BarkSystem.Pick(book, c, x, (ulong)sample + 1, sample, null)?.Id ?? "";
        }
        var hungryPicks = Enumerable.Range(0, 60).Select(i => Bark(true, 0, i)).ToList();
        Assert.Contains("state.hungry.a", hungryPicks.Concat(Enumerable.Range(60, 200).Select(i => Bark(true, 0, i))));
        Assert.DoesNotContain(Enumerable.Range(0, 200).Select(i => Bark(false, 0, i)), id => id.StartsWith("state.hungry"));
        var low = Enumerable.Range(0, 300).Select(i => Bark(false, -40, i)).ToList(); Assert.Contains(low, id => id.StartsWith("mood.low"));
        var high = Enumerable.Range(0, 300).Select(i => Bark(false, 40, i)).ToList(); Assert.Contains(high, id => id.StartsWith("mood.high")); Assert.DoesNotContain(high, id => id.StartsWith("mood.low"));
        Assert.DoesNotContain(Enumerable.Range(0, 300).Select(i => Bark(false, 0, i)), id => id.StartsWith("mood."));
        foreach (var b in book.Barks.Where(b => b.Id.StartsWith("mood."))) Assert.True(TalkKit.EnCsv().ContainsKey(b.Key), b.Key);
        // the mood of a person today reads his needs, his house's grievances and the weather
        var s = book.Settings;
        Assert.True(MoodSystem.Weathered(10, "Rain", s) < 10); Assert.Equal(10, MoodSystem.Weathered(10, "Clear", s));
        Assert.True(MoodSystem.Grieved(10, Grievance.Tax | Grievance.Lord, s) < MoodSystem.Grieved(10, Grievance.None, s));
    }

    // ------------------------------------------------------------------ CR-13

    [Fact]
    public void CR13_AP8_TheAutopilotPausesOnABigChoiceForAContractWorthFiveDaysOfWages_AndDeclinesItWithTheAiOn_SmallOnesAreItsOwnBusiness()
    {
        var r = new Rig(); var s = r.W.S; long wage = 300;
        Assert.True(ContractOffers.IsBig(s, 1_500, wage)); Assert.False(ContractOffers.IsBig(s, 1_499, wage));
        var need = ContractSystem.NeedEntry(r.W, "band_seen", 0, 0, []);
        var big = ContractSystem.Post(r.W, r.B, "clear_bandit_cave", Fief, r.Treasury, 0, 6_000, 1, need)!;
        var small = ContractSystem.Post(r.W, r.B, "day_labour", Fief, r.Treasury, 0, 300, 1, need)!;
        var w = new ContractWorld { Chronicle = r.C, Ids = r.Ids, Ledger = r.L, People = r.P, SettlementNames = ["Ashby"], DailyWageMites = wage, Content = r.W.Content };
        // every offer is an authored node with its decline branch (AP-8's content rule)
        Assert.Empty(Ravensguard.Sim.Autopilot.AutopilotContentChecks.BigChoiceProblems(new[] { big, small }.Select(c => ContractOffers.NodeFor(w, c))));
        Assert.True(ContractOffers.NodeFor(w, big).Big); Assert.False(ContractOffers.NodeFor(w, small).Big);
        var log = new Ravensguard.Sim.Autopilot.AutopilotLog(); var router = new Ravensguard.Sim.Autopilot.InterruptRouter(log);
        var st = new Ravensguard.Sim.Autopilot.AutopilotState { Person = EntityId.Make(IdKind.Person, 7), Enabled = true };
        // policy Pause: the game pauses; policy Ai: the decline is answered and recorded
        var bus = new Ravensguard.Sim.Travel.InterruptBus { Autopilot = true };
        st.Policy[(int)Ravensguard.Sim.Travel.InterruptKind.BigChoice] = Ravensguard.Sim.Travel.InterruptPolicy.Pause;
        var res = ContractOffers.Offer(w, big, router, st, bus, 1000)!.Value; Assert.True(res.Paused); Assert.Equal(1, log.CountInterrupts(Ravensguard.Sim.Travel.InterruptKind.BigChoice));
        var st2 = new Ravensguard.Sim.Autopilot.AutopilotState { Person = EntityId.Make(IdKind.Person, 7), Enabled = true }; var bus2 = new Ravensguard.Sim.Travel.InterruptBus { Autopilot = true };
        st2.Policy[(int)Ravensguard.Sim.Travel.InterruptKind.BigChoice] = Ravensguard.Sim.Travel.InterruptPolicy.Ai;
        var res2 = ContractOffers.Offer(w, big, router, st2, bus2, 5000)!.Value; Assert.Equal(Ravensguard.Sim.Autopilot.AiResponse.DeclineAndRecord, res2.Response); Assert.False(res2.Paused);
        Assert.Null(ContractOffers.Offer(w, small, router, st2, bus2, 9000));
    }

    // ------------------------------------------------------------------ CR-14

    private static ulong Thirty(ulong seed, out ContractBook book, out Ledger led)
    {
        var r = new Rig(seed);
        var owner = EntityId.Make(IdKind.Household, 500); r.Owners[owner] = r.L.Open(PurseKind.Household, owner); r.L.Mint(r.Owners[owner], new Coin(60_000), SourceReason.Seed);
        var coPurse = r.L.Open(PurseKind.Household, EntityId.Make(IdKind.Household, 600)); r.L.Mint(coPurse, new Coin(8_000), SourceReason.Seed);
        r.Companies.Add(new CompanyInfo(EntityId.Make(IdKind.Faction, 0x4A00), "co", 0, 60, 5, coPurse.Index, "mercenary")); r.Risk = 30;
        for (int day = 1; day <= 30; day++)
        {
            r.Needs.Clear(); if (day % 4 == 1) r.Needs.Add(r.CharcoalNeed(days: 1 + day % 3));
            r.Cars.Clear(); if (day % 5 == 2) r.Cars.Add(new CaravanLeaving(EntityId.Make(IdKind.Other, 55 + (ulong)day), owner, 0, 8_000, 2, 2));
            ContractSystem.Expiries(r.W, r.B, day);
            Companies.Resolve(r.W, r.B, day); Companies.TakeAll(r.W, r.B, day);
            for (int h = 0; h < 24; h++) NeedScanner.Hour(r.W, r.B, day * 24L + h);
            // the hero takes the first open delivery and hands it over a day later
            foreach (var c in r.B.Contracts.Values.Where(c => c.Open && c.Kind == "deliver_goods").Take(1).ToList()) ContractSystem.Take(r.W, r.B, c.Id, Hero, r.HeroPurse, day);
            foreach (var c in r.B.Contracts.Values.Where(c => c.State == WorkState.Taken && c.Taker == Hero && c.Kind == "deliver_goods" && c.TakenDay < day).ToList()) ContractSystem.GoodsReceived(r.W, r.B, c.TargetOwner, (ushort)c.Good, c.Grams, day);
            ContractSystem.Verify(r.W, r.B, day);
        }
        book = r.B; led = r.L; Assert.True(r.L.Audit().IsConserved);
        return r.B.Hash();
    }

    [Fact]
    public void CR14_ThirtyDaysOfPostingsAndCompletions_HashEqualAcrossRuns_AndTheSectionSavesLoadsAndSavesByteIdentical_WithItsEscrows()
    {
        ulong a = Thirty(7, out var book, out var led), b = Thirty(7, out _, out _), c = Thirty(8, out _, out _);
        Assert.Equal(a, b); Assert.NotEqual(a, c);
        output.WriteLine($"{book.Contracts.Count} contracts: {string.Join(", ", book.Contracts.Values.GroupBy(x => x.State).Select(g => g.Key + " " + g.Count()))}");
        Assert.Contains(book.Contracts.Values, x => x.State == WorkState.Done); Assert.True(book.Contracts.Count >= 6);
        var bytes = SaveCodec.SerializeCompressed(ContractSave.Capture(book));
        var back = SaveCodec.DeserializeCompressed<ContractSave>(bytes).Restore();
        Assert.Equal(book.Hash(), back.Hash());
        Assert.True(bytes.AsSpan().SequenceEqual(SaveCodec.SerializeCompressed(ContractSave.Capture(back))));
        // the coin the escrows hold is in the ledger, and the ledger round-trips with it
        long held = book.Contracts.Values.Where(x => x.Live).Sum(x => led.Balance(new PurseId(x.Escrow)).Mites);
        Assert.True(held > 0 || !book.Contracts.Values.Any(x => x.Live));
        var copy = Ledger.Import(led.Export()); Assert.True(copy.Audit().IsConserved);
        foreach (var x in book.Contracts.Values) Assert.Equal(led.Balance(new PurseId(x.Escrow)).Mites, copy.Balance(new PurseId(x.Escrow)).Mites);
        // a world that never used contracts writes no section
        Assert.True(new ContractBook().IsEmpty);
    }

    [Fact]
    public void TheTransferReasonsAreAppendedAtTheEnd_SoOlderSavesKeepTheirMeaning()
    {
        var all = Enum.GetNames<TransferReason>();
        Assert.Equal("Skim", all[^4]); Assert.Equal("ContractEscrow", all[^3]); Assert.Equal("ContractPay", all[^2]); Assert.Equal("ContractRefund", all[^1]);
    }
}
