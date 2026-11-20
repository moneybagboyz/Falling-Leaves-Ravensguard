using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Crafting;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Abstract;

/// <summary>
/// Save, load, continue at the level of a whole <see cref="WorldRuntime"/> (the plan's rule, section 6, one level above <see cref="ResumeDeterminismTests"/>): a new game runs N hours,
/// is saved with <see cref="WorldSave.Capture"/> at a barrier, goes through the codec, is loaded with <see cref="WorldSave.Restore"/> into a fresh runtime and runs M more hours. Every save
/// section, the abstract continent (compared directly, so a section that is never written cannot hide it) and every resident region's records must equal one uninterrupted run of N + M hours.
/// The regression test of docs/reviews/resume-drift-diagnosis.md (the continent section was never written; the farming seed-debt table was not grown after a load). The last case adds the
/// Phase 5 step 1 sections: a hero (a person row since P5-A4, with a party), land bought and a stall rented through the hero's desk (P5-A2) and a named artefact in the crafting section (P5-A1);
/// and P5-B's "construction" section: a two-storey smithy half built on the bought lot by a hired crew across the save; and P5-C's "designs" section: that smithy saved as a blueprint.
/// P5-E extends it with crafting the hour pass moves: a commission at a smith (made, then sold uncollected, across the save), an apprenticeship, a guild membership, a job with a fetch still to walk and a fire burning.
/// </summary>
public class WorldRuntimeResumeTests(ITestOutputHelper output)
{
    private const string Hash = "h";

    private static (SeamWorld W, WorldState State) Game(bool hero)
    {
        var w = SeamWorld.Get(1234);
        var home = w.BlockOrigin.Offset(1, 1);
        var dir = Ravensguard.Sim.WorldGen.ContinentSettings.FindBaseContent()!;
        var cs = Ravensguard.Sim.WorldGen.ContinentSettings.LoadDefaults(dir); cs.Seed = 1234;
        var hs = Ravensguard.Sim.WorldGen.History.HistorySettings.LoadDefaults(dir); hs.Seed = 1234;
        var region = RegionGenerator.Generate(w.Continent, w.History, home, w.Settings);
        LandmarkPlacer.Place(region, w.History, w.Content);
        var pop = Populator.Populate(region, w.History, w.Content);
        var h = hero ? HeroRecord.FromOrigin("Aldric Vane", false, w.Content.All<OriginDef>().OrderBy(o => o.Order).First()) : null;
        return (w, NewGameWorld.Create(w.Content, w.Continent, w.History, cs, hs, w.Settings, home.CellX, home.CellY, pop, h, null));
    }

    /// <summary>
    /// The Phase 5 step 1 state, before the save (so the same in both runs): the hero buys a town lot and rents a stall through the hero's desk, as the game sends them (the "land" section),
    /// and a named artefact of the hero's making is on the books (the "crafting" section's names).
    /// </summary>
    internal static void Play(WorldRuntime rt, ContentDatabase content)
    {
        var s = rt.State;
        var lw = s.HomeRecords!.Sim.Get<LandWorld>()!;
        var ctx = new HeroContext(s, s.Party!, content);
        s.Economy!.Ledger.Mint(ctx.Purse, Coin.FromCrowns(2000), SourceReason.Seed);
        var reg = s.Population.Region;
        int town = reg.Settlements.FindIndex(x => x.Class == SettlementClass.Town && lw.Listings(reg.Settlements.IndexOf(x)).Count > 0);
        long sec = rt.HourDone * 3600;
        var desk = new HeroDesk();
        var lot = lw.Listings(town)[0];
        desk.Issue(LandDesk.Buy(lot, sec));
        desk.Issue(LandDesk.Stall(town, 0, false, sec));
        // P5-B: a two-storey smithy placed on the lot through the desk, its pile stocked (the test's faucet), a crew hired: it is half built across the save (the "construction" section)
        desk.Issue(Ravensguard.Sim.Construction.ConstructionDesk.Plan("smithy_house", lot.Rect.X + 1, lot.Rect.Y + 1, 0, false, sec));
        desk.ApplyDue(ctx, rt.HourDone);
        Assert.All(desk.View!.Results, r => Assert.True(r.Ok, r.Text));
        var cw = s.HomeRecords.Sim.Get<Ravensguard.Sim.Construction.ConstructionWorld>()!;
        var site = s.Construction!.Sites.Values.Single();
        foreach (var (g, q) in cw.Needed(site)) cw.PileOf(site).Create(g, q, CreateReason.Import);
        desk.Issue(Ravensguard.Sim.Construction.ConstructionDesk.Hire(site.Id, 3, false, sec));
        desk.Issue(Ravensguard.Sim.Construction.ConstructionDesk.Hire(site.Id, 2, true, sec));
        // P5-C: the planned smithy saved as the player's blueprint through the desk (the "designs" section)
        desk.Issue(Ravensguard.Sim.Construction.ConstructionDesk.SaveDesign(lot.Rect.X, lot.Rect.Y, lot.Rect.Right - 1, lot.Rect.Bottom - 1, "My smithy", sec));
        desk.ApplyDue(ctx, rt.HourDone);
        Assert.All(desk.View!.Results, r => Assert.True(r.Ok, r.Text));
        // P5-D: a growth cottage the lord pays for, 60 of its 250 hours done, on a village plot the player has seen: a real site (the hours mapped onto pieces) with its civic
        // contract on the notice board, which the hero takes through the desk as a job (the "construction" section's contracts and growth links, the growth section's project)
        var growth = s.HomeRecords.Economy.Growth;
        int vil = Enumerable.Range(0, reg.Settlements.Count).First(i => reg.Settlements[i].Class == SettlementClass.Village && Ravensguard.Sim.Growth.RegionGrowth.FindPlot(reg, i, 16, 38, 30, [], out _, out _));
        Ravensguard.Sim.Growth.RegionGrowth.FindPlot(reg, vil, 16, 38, 30, [], out var plot, out byte facing);
        var tune = growth.Tuning;
        growth.State.Projects.Add(new Ravensguard.Sim.Growth.BuildProject
        {
            Serial = growth.State.NextSerial++, Kind = Ravensguard.Sim.Growth.ProjectKind.Cottage, Settlement = vil, StartDay = (int)(sec / 86400) - 10, Payer = growth.LordOf(vil), Rect = plot, Facing = facing,
            Use = LotUse.Cottage, Dwelling = DwellingType.Cottage, Blueprint = "peasant_cottage", Beds = tune.CottageBeds, HoursNeeded = 250, HoursDone = 60, MaterialG = [.. tune.CottageMaterials.Select(m => (long)m.Kg * 1000)],
        });
        var gen = Ravensguard.Sim.WorldGen.Chunks.ChunkGenerator.For(reg, s.Population, content);
        for (int cy = plot.Y / 32; cy <= (plot.Bottom - 1) / 32; cy++)
            for (int cx = plot.X / 32; cx <= (plot.Right - 1) / 32; cx++)
            {
                var levels = gen.GenerateLevels(cx, cy);
                s.Chunks.RecordFirstVisit(new ChunkKey(cx, cy, 0), Ravensguard.Sim.WorldGen.Chunks.ChunkGenerator.Version, levels.ContentHash, levels.Levels[0]);
            }
        cw.GrowthSites!.LinkSeen(new GameTime(sec));
        var civic = s.Construction.Contracts.Values.Single();
        desk.Issue(Ravensguard.Sim.Construction.ConstructionDesk.TakeContract(civic.Id, sec));
        desk.ApplyDue(ctx, rt.HourDone);
        Assert.All(desk.View!.Results, r => Assert.True(r.Ok, r.Text));
        Assert.Equal(Ravensguard.Sim.Construction.ContractState.Signed, civic.State);
        s.Crafting ??= new Ravensguard.Sim.Crafting.CraftState();
        var heroId = s.Population.People.IdOf(s.Hero!.PersonIndex);
        s.Crafting.Artefacts.Named[EntityId.Make(IdKind.Item, 0xA27E)] = new Artefact(Artefacts.NameFor(content, s.Seed, new EntityId(77), "Aldric Vane"), heroId, "Aldric Vane", sec);
        PlayCrafting(rt, ctx, town, sec);
        PlayOwnership(rt, ctx, desk, town, sec, content);
        PlayVehicles(rt, ctx, desk, sec);
        PlayInheritance(rt, ctx, desk, town, sec);
        PlayMedicine(rt, ctx, desk, sec);
        PlayContracts(rt, ctx, desk, sec);
        PlayBondage(rt, content, town, sec);
    }

    /// <summary>
    /// P6-D1: the "bondage" section: a slave of the hero's (a Fair owner who lets him keep a tip each day), a lot standing in a pen with a band's purse behind it, and a runaway with his owner's bounty on the board. The bonds are
    /// ticked by the daily pass on both sides of the save and the section's hash must equal at the end.
    /// </summary>
    private static void PlayBondage(WorldRuntime rt, ContentDatabase content, int town, long sec)
    {
        var s = rt.State; var pt = s.Population.People; int day = (int)(sec / 86_400);
        var bw = Ravensguard.Sim.Bondage.BondWiring.World(s, content); var book = s.Bonds = new Ravensguard.Sim.Bondage.BondBook(); var hero = s.Party!.Hero;
        var adults = Enumerable.Range(0, pt.Count).Where(i => pt.IsAlive(i) && pt.AgeYears(i, day) >= 16 && pt.Status[i] != PersonStatus.Hero && pt.HomeSettlement[i] == town).Take(3).Select(pt.IdOf).ToList();
        Assert.Equal(3, adults.Count);
        var mine = Ravensguard.Sim.Bondage.Slavery.Make(bw, book, adults[0], hero, Ravensguard.Sim.Bondage.BondKind.Chattel, Ravensguard.Sim.Bondage.BondOrigin.Sold, day, town, 9_000, 0, 0);
        mine.Treatment = Ravensguard.Sim.Bondage.Treatment.Fair;
        var band = Ravensguard.Sim.Bondage.Slavery.BandId(s.HomeRegion.Id, 0); Ravensguard.Sim.Bondage.Slavery.BandPurse(bw, band);
        Ravensguard.Sim.Bondage.Slavery.List(bw, book, adults[1], band, PurseKind.Household, town, day, sec, 0);
        var run = Ravensguard.Sim.Bondage.Slavery.Make(bw, book, adults[2], band, Ravensguard.Sim.Bondage.BondKind.Chattel, Ravensguard.Sim.Bondage.BondOrigin.Sold, day, town, 9_000, 0, 0);
        run.Chain = Ravensguard.Sim.Combat.Aftermath.Restraint.None; run.Owner = pt.IdOf(adults.Count).Value;
        var crimes = s.Law ?? new Ravensguard.Sim.Law.CrimeBook();
        Ravensguard.Sim.Bondage.BondDaily.Flee(bw, book, crimes, run, day, sec); s.Law = crimes;
        Assert.True(s.Economy!.Ledger.Audit().IsConserved);
    }

    /// <summary>
    /// P6-C2: the "contracts" section with its escrow in the ledger: the fief of the town posts a bounty-style contract on a person (a real transfer from its treasury into the escrow chest), the hero takes it through the
    /// desk (the recorded <c>ct:take</c> command), and a slander is on the books. The section, the chest's coin and the chronicle entries must equal across the save.
    /// </summary>
    private static void PlayContracts(WorldRuntime rt, HeroContext ctx, HeroDesk desk, long sec)
    {
        var s = rt.State; var w = Ravensguard.Sim.Contracts.ContractWiring.From(s, s.Content ?? ctx.Content);
        var reg = s.Population.Region; int town = Enumerable.Range(0, reg.Settlements.Count).First(i => w.TreasuryOf(i) is { } t && w.FiefOf(i) >= 0 && s.Economy!.Ledger.Balance(t).Mites > 10_000);
        var book = s.Contracts ??= new Ravensguard.Sim.Contracts.ContractBook();
        var need = Ravensguard.Sim.Contracts.ContractSystem.NeedEntry(w, "band_seen", town, sec, []);
        var c = Ravensguard.Sim.Contracts.ContractSystem.Post(w, book, "hunt_man", w.FiefIds[w.FiefOf(town)], w.TreasuryOf(town)!.Value, town, 3_000, (int)(sec / 86_400), need, k => k.Target = s.Population.People.IdOf(5));
        Assert.NotNull(c);
        desk.Issue(Ravensguard.Sim.Contracts.ContractDesk.Take(c!.Id, sec));
        desk.ApplyDue(ctx, rt.HourDone);
        Assert.All(desk.View!.Results, r => Assert.True(r.Ok, r.Text));
        Assert.Equal(Ravensguard.Sim.Contracts.WorkState.Taken, c.State);
        var view = Ravensguard.Sim.Contracts.ContractsView.Build(s, s.Content ?? ctx.Content, town, (int)(sec / 86_400), s.Party!.Hero, w);       // the Journal's Contracts tab reads this
        Assert.Contains(view.Mine, r => r.Id == c.Id && r.State == Ravensguard.Sim.Contracts.WorkState.Taken);
        book.Slanders.Add(new Ravensguard.Sim.Contracts.SlanderRow { Rumour = 4242, Author = s.Population.People.IdOf(6).Value, Victim = s.Population.People.IdOf(7).Value, Day = (int)(sec / 86_400) });
        Assert.True(s.Economy!.Ledger.Audit().IsConserved);
    }

    /// <summary>
    /// P6-B1: the "medicine" section with a physician's treatment in progress. The new game seated its physicians at the seeding pass; a wounded townsman of a physician's town whose household can
    /// pay is on the books (the abstract physic treats him inside the day, across the save), and the hero lies hurt with a "dress all bleeding" planned and waiting for its second (his queue is saved).
    /// </summary>
    private static void PlayMedicine(WorldRuntime rt, HeroContext ctx, HeroDesk desk, long sec)
    {
        var s = rt.State; var pt = s.Population.People; var e = s.Economy!;
        var book = s.Medicine ?? throw new InvalidOperationException("the new game seated no physician");
        Assert.False(book.IsEmpty);
        var man = book.Rows.First(r => r.Kind == Ravensguard.Sim.Medicine.PhysicianKind.Physician);
        var plan = Ravensguard.Sim.Medicine.MedicineWiring.PlanOf(ctx.Content, "human")!;
        int day = (int)(sec / 86400); var ledger = s.Wounds!;
        WoundV2 Cut(string part, string structure, WoundFlags fl, int rate10)
        {
            int p = plan.IndexOf(part); var pp = plan.Parts[p]; int si = Array.FindIndex(pp.Structures, x => x.Id == structure);
            return new WoundV2 { Part = (short)p, Tissue = (short)pp.Structures[si].Tissue.Index, Structure = (short)si, Organ = -1, Kind = WoundKind.Cut, DepthPm = 1000, BleedX10 = rate10, Flags = fl };
        }
        int patient = Enumerable.Range(0, pt.Count).First(i => pt.Status[i] == PersonStatus.Alive && pt.HomeSettlement[i] == man.Settlement && pt.AgeYears(i, day) is >= 20 and < 50 && pt.Household[i] >= 0
            && pt.IdOf(i).Value != man.Person && pt.Occupation[i] != Occupation.Physician);
        e.Ledger.Mint(e.HouseholdPurse[pt.Household[patient]], Coin.FromCrowns(5), SourceReason.Seed);
        var pb = ledger.For(pt.IdOf(patient).Value, plan, sec);
        pb.Apply(plan, [Cut("upper_arm_l", "brachial_vein", WoundFlags.VeinHit, 40)], sec, sec, 0x77, 0, false);
        // the hero: hurt, bandages in the pack, a dress-all queued at the barrier and planned (its work ends a little after the save)
        ctx.Pack.Create(e.Goods.IdOf("bandage"), 4, CreateReason.Seed);
        var hb = ledger.For(ctx.Party.Hero.Value, plan, sec);
        hb.Apply(plan, [Cut("lower_leg_r", "tibial_vein", WoundFlags.VeinHit, 30)], sec, sec, 0x78, 0, false);
        desk.Issue(Ravensguard.Sim.Medicine.HeroTreatDesk.DressAll(ctx.Party.Hero.Value, 3, sec));
        desk.ApplyDue(ctx, rt.HourDone);
        Assert.True(desk.View!.Results.Last().Ok, desk.View.Results.Last().Text);
        Assert.Equal(0, Ravensguard.Sim.Medicine.HeroTreatDesk.Advance(ctx, sec));
        Assert.Single(book.Queue.Steps); Assert.True(book.Queue.Steps[0].Planned);
    }

    /// <summary>
    /// P5-H: a will of the hero's made before the town's priest through the desk (the "wills" section), and an estate mid-settlement: a townsman who leased a stall in his own name dies owing
    /// more rent than he left; his estate is settled before the save with the debt unpaid, and the lessor's claim on the stall falls at the season's start inside M (the estates' S6 step).
    /// </summary>
    private static void PlayInheritance(WorldRuntime rt, HeroContext ctx, HeroDesk desk, int town, long sec)
    {
        var s = rt.State; var pt = s.Population.People; var e = s.Economy!;
        int day = (int)(sec / 86400);
        int priest = Enumerable.Range(0, pt.Count).First(i => pt.Status[i] == PersonStatus.Alive && pt.Occupation[i] is Occupation.Priest or Occupation.Reeve && pt.Household[i] >= 0);
        int heir = Enumerable.Range(0, pt.Count).First(i => pt.Status[i] == PersonStatus.Alive && pt.HomeSettlement[i] == town && pt.AgeYears(i, day) is >= 20 and < 40 && pt.Occupation[i] == Occupation.Merchant);
        desk.Issue(Ravensguard.Sim.Succession.WillDesk.Make(pt.IdOf(priest), [new Ravensguard.Sim.Succession.Bequest(pt.IdOf(heir), Ravensguard.Sim.Succession.BequestWhat.All)], sec));
        desk.ApplyDue(ctx, rt.HourDone);
        Assert.True(desk.View!.Results.Last().Ok, desk.View.Results.Last().Text);
        // the townsman: an empty lot of the town held in his own name, its dues long unpaid, dead today
        int man = Enumerable.Range(0, pt.Count).First(i => pt.Status[i] == PersonStatus.Alive && pt.HomeSettlement[i] == town && pt.AgeYears(i, day) is >= 40 and < 60 && pt.Occupation[i] == Occupation.Merchant && i != heir);
        var id = pt.IdOf(man);
        e.Ledger.Mint(e.Ledger.Open(PurseKind.Person, id), Coin.FromCrowns(40), SourceReason.Seed);
        var lw = s.HomeRecords!.Sim.Get<LandWorld>()!;
        var lots = s.Population.Region.Settlements[town].Plan.Lots;
        int lot = Enumerable.Range(0, lots.Count).First(l => s.Population.LotHousehold[town][l] < 0 && !lw.HoldsLot(town, l));
        var conv = lw.ConveyLot(town, lot, id, PurseKind.Person, day, DeedCause.Bought);
        Assert.True(conv.Ok, conv.Key);
        s.Land!.Deeds[conv.Deed].OwedMites = 90_000;
        var ob = new Ravensguard.Sim.Abstract.RegionOutbox(s.Ids);
        Ravensguard.Sim.People.Vital.Demography.Kill(s.HomeRecords.Sim, man, DeathCause.Age, new GameTime(sec), ob);
        var ew = s.HomeRecords.Sim.Get<Ravensguard.Sim.Succession.EstateWorld>()!;
        Assert.Equal(1, ew.Daily(day, sec));
        var est = s.Succession!.Estates.Last();
        Assert.True(est.UnpaidMites > 0);
    }

    /// <summary>
    /// P5-G: a wagon of the hero's own design commissioned from the town's carpenter (his bench and his yard work it hour by hour: half built across the save), and a laden barge on a regular run
    /// up and down the region's longest river (under way across the save). Both through the hero's desk; the materials and the barge are the test's faucet.
    /// </summary>
    private static void PlayVehicles(WorldRuntime rt, HeroContext ctx, HeroDesk desk, long sec)
    {
        var s = rt.State; var content = ctx.Content; var reg = s.Population.Region; var people = s.Population.People;
        var book = Ravensguard.Sim.Carts.VehicleDesk.BookOf(s, content);
        var des = Ravensguard.Sim.Tests.VehicleDesign.WagonEndToEndTests.Design(book);
        desk.Issue(Ravensguard.Sim.Carts.VehicleDesk.SaveDesign(des.Id, des.Text(), sec));
        var prod = s.Production!;
        var shop = prod.Businesses.Where(b => b.Owner >= 0 && prod.Content.Kinds[b.Kind].Stations.Any(k => k.Kind == "wheelwright_bay") && reg.Settlements[b.Settlement].Class == SettlementClass.Town)
            .OrderBy(b => b.Index).First();
        var maker = people.IdOf(shop.Owner);
        s.Crafting!.Book.Set(maker, Skill.Carpentry, 65 * CraftMath.MicroPerLevel);
        desk.ApplyDue(ctx, rt.HourDone);
        var bp = book.Templates.Get(des.Id);
        foreach (var p in bp.Parts)
        {
            var r = ctx.Recipes[book.Templates.PartList[p.Part].Recipe];
            foreach (var slot in r.Slots) shop.Store.Create(slot.Alts[0].Good, slot.Alts[0].Qty, CreateReason.Import);
        }
        var town = reg.Settlements[shop.Settlement];
        desk.Issue(Ravensguard.Sim.Carts.VehicleDesk.Commission(des.Id, maker, town.CenterX * 1000L, town.CenterY * 1000L, sec));
        var river = reg.Waterways.OrderByDescending(w => w.LengthM).ThenBy(w => w.Id).First();
        var barge = book.Vehicles.Create(book.Templates.Get("barge"), ctx.Party.Hero);
        barge.Cargo.Create(s.Economy!.Goods.IdOf("wheat"), 4_000_000, CreateReason.Import);
        desk.Issue(Ravensguard.Sim.Carts.VehicleDesk.Voyage(barge.Id, river.Id, 0, river.LengthM * 1000L, sec, shuttle: true));
        desk.ApplyDue(ctx, rt.HourDone);
        Assert.All(desk.View!.Results, r => Assert.True(r.Ok, r.Text));
        Assert.Single(book.Works.Commissions);
        Assert.Single(book.Works.Voyages);
    }

    /// <summary>
    /// P5-F: a business of the hero's with a steward (a smithy in the town, its staff hired through the desk), the stall rented above selling its goods minded by its smith, a prospect record (a walk
    /// and a trial shaft's finding), and a working mine on a licensed vein with its miners: all of it is stepped by the hour pass across the save (the "ownership" section).
    /// </summary>
    private static void PlayOwnership(WorldRuntime rt, HeroContext ctx, HeroDesk desk, int town, long sec, ContentDatabase content)
    {
        var s = rt.State; var ow = s.HomeRecords!.Sim.Get<Ravensguard.Sim.Production.OwnershipWorld>()!;
        var pt = s.Population.People; var prod = s.Production!;
        int day = (int)(sec / 86400);
        // the smithy: owned as founding makes it (no finished room yet: the plan above is half built), capital from the purse
        var kind = prod.Content.Kind("smithy");
        var b = prod.NewBusiness(kind, town, -1, s.Hero!.PersonIndex); b.OwnerWorks = false;
        foreach (var (st, n) in kind.Stations) for (int i = 0; i < Math.Max(1, n); i++) b.Stations.Add(new Ravensguard.Sim.Production.Station { Kind = st });
        s.Economy!.Ledger.Transfer(ctx.Purse, b.Purse, new Coin(20_000), TransferReason.Gift); b.Capital = 20_000; b.CashStart = 20_000;
        ow.Hold(b).FoundedDay = day;
        b.Store.Create(s.Economy.Goods.IdOf("iron_bar"), 60_000, CreateReason.Import);
        var idle = Enumerable.Range(0, pt.Count).Where(i => pt.Status[i] == PersonStatus.Alive && pt.HomeSettlement[i] == town && pt.Occupation[i] == Occupation.None && pt.Employer[i] < 0 && pt.AgeYears(i, day) is >= 20 and < 50
            && s.Population.Households.Class[pt.Household[i]] != SocialClass.Gentry).ToList();
        int steward = idle[0]; pt.SetSkill(steward, 0, Skill.Trading, 45); pt.SetSkill(steward, 1, Skill.Leadership, 30);
        desk.Issue(Ravensguard.Sim.Production.BusinessDesk.Hire(b.Id, idle[1], 380, sec));
        desk.Issue(Ravensguard.Sim.Production.BusinessDesk.Steward(b.Id, steward, sec));
        desk.Issue(Ravensguard.Sim.Production.BusinessDesk.Orders(b.Id, "recipe=forge_nails,send_days=3,keep_mites=2000", sec));
        desk.Issue(Ravensguard.Sim.Production.BusinessDesk.Stall(town, 0, b.Index, sec));
        desk.Issue(Ravensguard.Sim.Production.BusinessDesk.Minder(Ravensguard.Sim.Land.LandIds.StallSubject(town, 0), idle[1], sec));
        desk.Issue(Ravensguard.Sim.Production.BusinessDesk.At(town, sec));
        // a prospect record: a walk past, and a trial shaft's finding on an ore vein with a right holder
        int tile = -1; Ravensguard.Sim.Prospecting.OreBody body = default;
        foreach (var v in s.Population.Region.Veins)
        {
            if (!Ravensguard.Sim.Prospecting.ProspectTruth.IsOre(v.Type) || v.Type is Ravensguard.Sim.WorldGen.MineralType.Silver or Ravensguard.Sim.WorldGen.MineralType.Gold) continue;
            int t = Ravensguard.Sim.WorldGen.Region.Region.Index(v.TileX, v.TileY);
            if (ow.Truth.OreAt(t) is { } ob && ob.Mineral == v.Type && ow.RightHolder(t, v.Type) != null) { tile = t; body = ob; break; }
        }
        Assert.True(tile >= 0);
        desk.Issue(Ravensguard.Sim.Production.BusinessDesk.Walk(tile, sec));
        var rules = content.All<Ravensguard.Content.ProspectRulesDef>().Single();
        foreach (var r in Ravensguard.Sim.Prospecting.Prospector.TrialShaft(ow.Truth, rules, s.Seed, ctx.Party.Hero, tile, 60, day)) ow.Book.Prospects.Learn(r);
        desk.Issue(Ravensguard.Sim.Production.BusinessDesk.Licence(tile, sec));
        int N = Ravensguard.Sim.WorldGen.Region.Region.Size, M = Ravensguard.Sim.WorldGen.Region.Region.TileM; int x = tile % N * M + 8, y = tile / N * M + 8;
        desk.Issue(Ravensguard.Sim.Production.BusinessDesk.FoundSite("mine", x, y, x + 16, y + 16, 15_000, sec));
        desk.ApplyDue(ctx, rt.HourDone);
        Assert.All(desk.View!.Results.TakeLast(9), r => Assert.True(r.Ok || r.Text.Length == 0, r.Text));
        var mine = prod.Businesses.Last(x2 => x2.Owner == s.Hero.PersonIndex && x2.Mine >= 0);
        foreach (int p in idle.Skip(2).Take(4)) desk.Issue(Ravensguard.Sim.Production.BusinessDesk.Hire(mine.Id, p, 160, sec));
        desk.ApplyDue(ctx, rt.HourDone);
        Assert.All(desk.View!.Results.TakeLast(4), r => Assert.True(r.Ok, r.Text));
        Assert.Equal(4, mine.Staff.Count);
        Assert.False(ow.Book.IsEmpty);
    }


    /// <summary>
    /// P5-E: crafting state that the hour pass moves (the runtime's <see cref="CraftSystem"/>): a commission at the region's best smith, made from the hero's steel over the next days; the hero
    /// apprenticed to him; a guild membership; a job of the hero's with a fetch still to walk; and a fire burning down its wood. All of it must resume exactly.
    /// </summary>
    private static void PlayCrafting(WorldRuntime rt, HeroContext ctx, int town, long sec)
    {
        var s = rt.State; var cs = s.Crafting!; var people = s.Population.People; var prod = s.Production!;
        var svc = s.HomeRecords!.Sim.Get<CraftService>()!; var w = svc.Context;
        var hero = ctx.Party.Hero;
        var smiths = prod.Businesses.Where(b => b.Owner >= 0 && prod.Content.Kinds[b.Kind].Stations.Any(k => k.Kind == "anvil")).Select(b => people.IdOf(b.Owner))
            .OrderByDescending(p => cs.Book.Level(p, Skill.Smithing)).ThenBy(p => p).ToList();
        Assert.NotEmpty(smiths);
        var smith = smiths[0]; int lv = cs.Book.Level(smith, Skill.Smithing);
        var r = ctx.Recipes.Recipes.Where(x => x.IsSmithing && x.Results.Any(y => y.IsItem) && x.Difficulty <= lv && x.Guild == null).OrderBy(x => x.Difficulty).ThenBy(x => x.Id, StringComparer.Ordinal).First();
        foreach (var slot in r.Slots) ctx.Pack.Create(slot.Alts[0].Good, slot.Alts[0].Qty, CreateReason.Seed);
        foreach (var st in r.Steps) if (st.FuelGood != ItemTable.NoGood) ctx.Pack.Create(st.FuelGood, st.FuelQty, CreateReason.Seed);
        var (a, cm) = Commissions.Order(w, r, hero, smith, true, CraftSources.Of(ctx.Pack), sec, s.Ids);
        Assert.Equal(CommissionAnswer.Ok, a);
        cs.Book.Set(hero, Skill.Smithing, Math.Max(1, lv - 40) * CraftMath.MicroPerLevel);
        if (lv >= 40) Assert.Equal(ApprenticeAnswer.Ok, Apprenticeships.Start(w, hero, smith, Skill.Smithing, (int)(sec / 86400), s.Ids).Item1);
        Guilds.SeeAt(cs, hero, w.SettlementOf(smith), (int)(sec / 86400));
        cs.Members[("smiths", town, hero.Value)] = new GuildMember { Guild = "smiths", Settlement = town, Person = hero, JoinedDay = (int)(sec / 86400), PaidSeason = (int)(sec / 86400) / Guilds.SeasonDays };
        // a job with materials to fetch from a store elsewhere in the building: started, nothing walked yet
        var shed = s.Economy!.OpenStore(StoreKind.House, s.Ids.Allocate(IdKind.Other), 0, 0, false);
        var torch = ctx.Recipes["make_torch"];
        shed.Create(s.Economy.Goods.IdOf("sticks"), 400, CreateReason.Seed); ctx.Pack.Create(s.Economy.Goods.IdOf("cloth_linen"), 100, CreateReason.Seed); ctx.Pack.Create(s.Economy.Goods.IdOf("tallow"), 50, CreateReason.Seed);
        var srcs = CraftSources.Of(ctx.Pack); srcs.Add(new CraftSource { Store = shed, Name = "shed", Fetch = true, DistMm = 9000 });
        var site = new CraftSite { Light = 255, Water = true }; site.AddTool(ctx.Recipes, "knife");
        var plan = ctx.Craft.Plan(torch, ctx.Actor(), site, srcs);
        Assert.True(plan.CanStart, string.Join("; ", plan.Missing));
        var job = ctx.Craft.Start(plan, ctx.Actor(), hero, srcs, sec)!;
        Assert.True(job.FetchLeft > 0);
        // a fire burning: kindled, with an extra kilo of wood
        var kindle = ctx.Recipes["kindle_fire"];
        foreach (var slot in kindle.Slots) ctx.Pack.Create(slot.Alts[0].Good, slot.Alts[0].Qty, CreateReason.Seed);
        site.AddTool(ctx.Recipes, "steel_and_flint");
        var fj = ctx.Craft.Start(ctx.Craft.Plan(kindle, ctx.Actor(), site, ctx.Pack), ctx.Actor(), hero, ctx.Pack, sec, 5000, 5000)!;
        ctx.Craft.Advance(fj, 3600, sec);
        var fire = cs.Fires.All.Values.Single();
        ctx.Pack.Create(s.Economy.Goods.IdOf("firewood"), 1000, CreateReason.Seed);
        Assert.True(cs.Fires.Feed(s.Economy, fire.Id, ctx.Pack, s.Economy.Goods.IdOf("firewood"), 1000));
        Assert.NotNull(cm);
        Assert.True(s.Economy!.MassAuditHolds());
    }

    /// <summary>The ring-1 neighbours, built now and taken in (they are then stepped hourly, in parallel with home).</summary>
    private static void AddNeighbours(WorldRuntime rt)
    {
        rt.Manager.Synchronous = true;
        var home = rt.State.HomeRegion;
        foreach (var (dx, dy) in new[] { (1, 0), (0, 1), (-1, 0), (0, -1), (1, 1) }) { var rc = home.Offset(dx, dy); if (rc.IsValid) rt.Manager.GenerateNow(rc); }
        rt.Records.Sync();
    }

    /// <summary>Steps to <paramref name="to"/>: on the calling thread, or on the tick worker with barriers, pumped the way the street view pumps it.</summary>
    private static void Run(WorldRuntime rt, long to, bool worker)
    {
        if (!worker) { rt.AdvanceTo(GameTime.FromGameHours(to)); return; }
        rt.Start();
        var home = rt.State.HomeRegion; var t = GameTime.FromGameHours(to);
        while (rt.HourDone < to) { rt.Pump(t, home.OriginXM + 16000, home.OriginYM + 16000); Thread.Sleep(1); }
        rt.Quiesce();
        Assert.Null(rt.Fault);
    }

    /// <summary>The save, taken at a barrier (as the game takes it).</summary>
    private static SaveFile SaveAt(WorldRuntime rt) => rt.AtBarrier(() => WorldSave.Capture(rt.State, Hash, "0.1")).GetAwaiter().GetResult();

    private static SortedDictionary<string, byte[]> Sections(WorldRuntime rt) => rt.AtBarrier(() =>
    {
        var d = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (k, v) in WorldSave.Capture(rt.State, Hash, "0.1").Sections) if (k != WorldSave.MetaSection) d[k] = v.ToArray();
        d["~continent"] = SaveCodec.SerializeCompressed(ContinentSave.Capture(rt.State.ContinentState!));
        foreach (var r in rt.Records.Resident) d[$"~records.{r.Coord.Id:D3}"] = System.Text.Encoding.ASCII.GetBytes(r.RecordsHash());
        return d;
    }).GetAwaiter().GetResult();

    [Theory]
    [InlineData(13, 48, false, false, false)]    // mid-day
    [InlineData(48, 48, false, false, false)]    // on a day boundary
    [InlineData(385, 48, false, false, false)]   // the continent's season tick (hour 0 of day 2 of summer) falls inside M
    [InlineData(1430, 48, false, false, false)]  // across the year boundary (a game year is 1,440 hours)
    [InlineData(48, 48, true, false, false)]     // on the tick worker with barriers
    [InlineData(13, 48, true, true, false)]      // the worker, five resident neighbours stepped in parallel
    [InlineData(340, 72, false, false, true)]    // a hero with land, a stall and a named artefact; the season's turn (dues) and the continent's tick fall inside M
    public void SaveLoadContinue_OfAWholeRuntime_EqualsTheUninterruptedRun(int n, int m, bool worker, bool neighbours, bool hero)
    {
        var (w, a) = Game(hero);
        using var straight = new WorldRuntime(a, w.Content, new MemoryRegionPager());
        if (neighbours) AddNeighbours(straight);
        if (hero) Play(straight, w.Content);
        long h0 = straight.HourDone;
        Run(straight, h0 + n, worker);
        var file = SaveAt(straight);
        Assert.Contains(WorldSave.ContinentSection, file.Sections.Keys);                      // fix 1: the continent is written
        var bytes = SaveCodec.Encode(file);
        Run(straight, h0 + n + m, worker);
        var want = Sections(straight);

        var st = WorldSave.Restore(SaveCodec.Decode(bytes), w.Content, Hash, null, w.Continent, w.History);
        st.Content = w.Content;
        using var resumed = new WorldRuntime(st, w.Content, new MemoryRegionPager());
        if (neighbours) resumed.Manager.Synchronous = true; // the saved neighbours page back in at the first barrier
        Run(resumed, h0 + n + m, worker);
        var got = Sections(resumed);

        if (hero)
        {
            // the Phase 5 step 1 sections are there to compare: land, the hero (a person row in "people", with the party) and the crafting section's named artefact
            foreach (var k in new[] { LandSave.SectionName, WorldSave.HeroSection, WorldSave.PartySection, WorldSave.CraftingSection, WorldSave.PeopleSection, ConstructionSave.SectionName, DesignsSave.SectionName, MountsSave.SectionName, OwnershipSave.SectionName })
                Assert.Contains(k, want.Keys);
            Assert.Contains(Ravensguard.Sim.Save.BondSave.SectionName, want.Keys);                  // P6-D1: the bondage section: a slave, a pen's lot, a runaway
            Assert.Equal(straight.State.Bonds!.Hash(), resumed.State.Bonds!.Hash());
            Assert.Contains(ContractSave.SectionName, want.Keys);                                  // P6-C2: the contracts section, with the escrow chest in the economy section
            Assert.Equal(straight.State.Contracts!.Hash(), resumed.State.Contracts!.Hash());
            foreach (var s in new[] { straight.State, resumed.State })
            {
                Assert.Equal(PersonStatus.Hero, s.Population.People.Status[s.Hero!.PersonIndex]);
                Assert.Equal(1, s.Crafting!.Artefacts.Count);
                Assert.False(s.Land!.IsEmpty);
            }
            Assert.Equal(straight.State.Hero!.PersonIndex, resumed.State.Hero!.PersonIndex);
            Assert.Equal(straight.State.Land!.ComputeHash(), resumed.State.Land!.ComputeHash());
            Assert.Equal(straight.State.Crafting!.Artefacts.Hash(), resumed.State.Crafting!.Artefacts.Hash());
            // P5-B: the smithy is still being built at the end, and both runs built the same pieces at the same seconds
            Assert.NotEmpty(straight.State.Construction!.Ghosts);
            Assert.NotEmpty(straight.State.Construction.Built);
            Assert.Equal(straight.State.Construction.ComputeHash(), resumed.State.Construction!.ComputeHash());
            // P5-D: the civic contract the hero took and the visible growth site are there at the end of both runs
            Assert.NotEmpty(straight.State.Construction.Contracts);
            Assert.NotEmpty(straight.State.Construction.Growth);
            // P5-C: the saved design is in both runs, the same text
            Assert.Contains("player.my_smithy", resumed.State.Designs!.Designs.Keys);
            Assert.Equal(straight.State.Designs!.ComputeHash(), resumed.State.Designs.ComputeHash());
            // P5-E: the commission was worked (and, uncollected, sold) through the save in both runs, the fire burned, the apprentice and the guild member are there, the fetch job is waiting
            foreach (var s in new[] { straight.State, resumed.State })
            {
                var c = s.Crafting!.Commissions.Values.Where(x => x.Client.Kind == IdKind.Person).Single();     // the hero's own (P5-G's wagon parts are the wagon site's)
                Assert.True(c.State is CommissionState.Working or CommissionState.Ready or CommissionState.Sold, c.State.ToString());   // made over the days, and left uncollected past 15 days: sold
                Assert.Single(s.Crafting.Members);
                Assert.Contains(s.Crafting.Jobs.Values, j => j.FetchLeft > 0);
            }
            Assert.Equal(straight.State.Crafting!.Commissions.Values.Where(c => c.Client.Kind == IdKind.Person).Single().State, resumed.State.Crafting!.Commissions.Values.Where(c => c.Client.Kind == IdKind.Person).Single().State);
            // P5-F: the steward ran the smithy and sent profit, the stall sold, the mine mined and paid its royalty, in both runs alike
            foreach (var s in new[] { straight.State, resumed.State })
            {
                var o = s.Ownership!;
                var smithy = o.Holdings.Values.Single(h => h.Steward >= 0);
                Assert.True(s.Production!.ById(smithy.Business)!.Batches > 0 || smithy.PriceDays.Count > 0);
                var mh = o.Holdings.Values.Single(h => h.Vein >= 0);
                Assert.True(mh.LastMinedG > 0); Assert.True(mh.RoyaltyPaid > 0);
                Assert.NotEmpty(o.Prospects.Rows); Assert.Single(o.Licences); Assert.Single(o.Stalls);
            }
            Assert.Equal(straight.State.Ownership!.ComputeHash(), resumed.State.Ownership!.ComputeHash());
            // P5-G: the commissioned wagon is still being made at the end (parts made and fitted on both sides of the save, at the same hours), the barge is on its run
            foreach (var s in new[] { straight.State, resumed.State })
            {
                var vc = s.Mounts!.Works.Commissions.Values.Single();
                var wagon = s.Mounts.Vehicles.Find(vc.Vehicle)!;
                Assert.Equal(Ravensguard.Sim.Carts.VehicleCommissionState.Making, vc.State);
                Assert.Contains(s.Crafting!.Commissions.Values, c => c.Client == vc.Vehicle && c.State == CommissionState.Collected);
                Assert.False(wagon.Finished);
                var y = s.Mounts.Works.Voyages.Values.Single();
                Assert.Equal(0, y.ArrivedSec);
                // P5-F's Ledger covers every property: the half-built wagon and the barge are on its Vehicles tab
                var ow2 = s.HomeRecords!.Sim.Get<Ravensguard.Sim.Production.OwnershipWorld>()!; var lv = ow2.Ledger(ow2.Today, null, s.Mounts).Tab(Ravensguard.Sim.Production.LedgerTab.Vehicles).ToList();
                Assert.Contains(lv, r => r.Id == vc.Vehicle && r.Cells[2] == "building");
                Assert.Contains(lv, r => r.Id == y.Boat);
            }
            Assert.Equal(Ravensguard.Sim.Carts.VehicleHash.Of(straight.State.Mounts!), Ravensguard.Sim.Carts.VehicleHash.Of(resumed.State.Mounts!));
            // P5-H: the will is in both runs; the townsman's estate was mid-settlement at the save and the lord's claim on his lot fell at the season's start in both runs alike
            foreach (var s in new[] { straight.State, resumed.State })
            {
                Assert.NotNull(s.Wills!.Of(EconomyState.HeroOwner));
                var est = s.Succession!.Estates.Last();
                Assert.Contains(est.Holdings, h => h.Kind == Ravensguard.Sim.Succession.EstateThing.Deed && h.ClaimedBy != EntityId.None);
            }
            Assert.Contains(Ravensguard.Sim.Save.WillsSave.SectionName, want.Keys);
            Assert.Equal(straight.State.Succession!.Hash(straight.State.Wills), resumed.State.Succession!.Hash(resumed.State.Wills));
            Assert.Contains("player.market_wagon", resumed.State.Designs!.Designs.Keys);
        }
        var bad = want.Keys.Union(got.Keys).Where(k => !(want.TryGetValue(k, out var x) && got.TryGetValue(k, out var y) && x.AsSpan().SequenceEqual(y))).ToList();
        output.WriteLine($"N {n} M {m} worker {worker} neighbours {neighbours} hero {hero}: {want.Count} sections [{string.Join(", ", want.Keys)}], differing [{string.Join(", ", bad)}]");
        Assert.Empty(bad);
    }
}
