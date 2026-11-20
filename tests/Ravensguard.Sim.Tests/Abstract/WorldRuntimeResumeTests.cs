using Ravensguard.Content;
using Ravensguard.Sim.Characters;
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
/// and P5-B's "construction" section: a two-storey smithy half built on the bought lot by a hired crew across the save.
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
    private static void Play(WorldRuntime rt, ContentDatabase content)
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
            foreach (var k in new[] { LandSave.SectionName, WorldSave.HeroSection, WorldSave.PartySection, WorldSave.CraftingSection, WorldSave.PeopleSection, ConstructionSave.SectionName })
                Assert.Contains(k, want.Keys);
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
            // P5-E: the commission was worked (and, uncollected, sold) through the save in both runs, the fire burned, the apprentice and the guild member are there, the fetch job is waiting
            foreach (var s in new[] { straight.State, resumed.State })
            {
                var c = s.Crafting!.Commissions.Values.Single();
                Assert.True(c.State is CommissionState.Working or CommissionState.Ready or CommissionState.Sold, c.State.ToString());   // made over the days, and left uncollected past 15 days: sold
                Assert.Single(s.Crafting.Members);
                Assert.Contains(s.Crafting.Jobs.Values, j => j.FetchLeft > 0);
            }
            Assert.Equal(straight.State.Crafting!.Commissions.Values.Single().State, resumed.State.Crafting!.Commissions.Values.Single().State);
        }
        var bad = want.Keys.Union(got.Keys).Where(k => !(want.TryGetValue(k, out var x) && got.TryGetValue(k, out var y) && x.AsSpan().SequenceEqual(y))).ToList();
        output.WriteLine($"N {n} M {m} worker {worker} neighbours {neighbours} hero {hero}: {want.Count} sections [{string.Join(", ", want.Keys)}], differing [{string.Join(", ", bad)}]");
        Assert.Empty(bad);
    }
}
