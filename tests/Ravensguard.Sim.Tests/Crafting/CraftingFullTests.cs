using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MessagePack;
using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Crafting;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Building;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Tests.Production;
using Xunit.Abstractions;
using Sk = Ravensguard.Sim.People.Skill;

namespace Ravensguard.Sim.Tests.Crafting;

/// <summary>P5-E: the crafting-full gate CF-1 to CF-18 (docs/specs/phase5/crafting-full.md 7).</summary>
public class CraftingFullTests(BuildingFixture fx, ITestOutputHelper output) : IClassFixture<BuildingFixture>
{
    // ------------------------------------------------------------------ helpers

    private static readonly EntityId Maker = EntityId.Make(IdKind.Person, 500), Client2 = EntityId.Make(IdKind.Person, 501), Student = EntityId.Make(IdKind.Person, 600);

    /// <summary>A crafting context over the test fixture: every named person lives in settlement 0 (a town), the maker has a full workshop and a purse.</summary>
    private static CraftContext Ctx(CraftWorld w, int smithBusinesses = 3, Func<int, EntityId, IReadOnlyList<EntityId>>? witnesses = null, Store? makerStore = null)
    {
        var ms = makerStore ?? w.Econ.OpenStore(StoreKind.House, Maker, 0, 0, false);
        return new CraftContext
        {
            Econ = w.Econ, Runner = w.R,
            PurseOf = p => w.Econ.Ledger.Open(PurseKind.Person, p),
            StoreOf = p => p == w.Hero ? w.Pack : p == Maker ? ms : w.Econ.OpenStore(StoreKind.House, p, 0, 0, false),
            SiteOf = (p, r) => p == w.Hero ? null : CraftWorld.FullSite(w.Db),
            ActorOf = p => new CraftActor(p),
            SettlementOf = _ => 0,
            PeopleOf = s => s == 0 ? [Maker] : [],
            IsTown = s => s == 0,
            TradeOf = p => p == Maker ? "Smith" : "",
            BusinessesOf = (s, t) => s == 0 && t == "Smith" ? smithBusinesses : 0,
            WitnessesOf = witnesses ?? ((_, _) => [EntityId.Make(IdKind.Person, 701), EntityId.Make(IdKind.Person, 702)]),
        };
    }

    private static PurseId Purse(CraftWorld w, EntityId p) => w.Econ.Ledger.Open(PurseKind.Person, p);
    private static void Mint(CraftWorld w, EntityId p, long mites) => w.Econ.Ledger.Mint(Purse(w, p), new Coin(mites), SourceReason.Seed);
    private static long Coin(CraftWorld w, EntityId p) => w.Econ.Ledger.Balance(Purse(w, p)).Mites;

    private static void Stock(CraftWorld w, Store s, CraftRecipe r, int choiceAlt = 0, int batch = 1)
    {
        foreach (var slot in r.Slots) { var a = slot.Alts[Math.Min(choiceAlt, slot.Alts.Length - 1)]; s.Create(a.Good, a.Qty * batch, CreateReason.Other); }
        foreach (var st in r.Steps) if (st.FuelGood != ItemTable.NoGood) s.Create(st.FuelGood, st.FuelQty * batch, CreateReason.Other);
    }

    private static List<RecipeDef> Phase2Defs()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "tests", "Ravensguard.Sim.Tests", "Crafting", "Data", "phase2_recipes.jsonc"))) dir = dir.Parent;
        var text = File.ReadAllText(Path.Combine(dir!.FullName, "tests", "Ravensguard.Sim.Tests", "Crafting", "Data", "phase2_recipes.jsonc"));
        var arr = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!.AsArray();
        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
        var list = new List<RecipeDef>();
        foreach (var n in arr) { var o = n!.AsObject(); o.Remove("type"); list.Add(o.Deserialize<RecipeDef>(opts)!); }
        return list;
    }

    private static void SameRecipeIgnoringIndex(Recipe a, Recipe b)
    {
        Assert.Equal((a.Id, a.Station, a.Skill, a.WorkerHours, a.MaxWorkers, a.DurationDays, a.MinBatchPermille, a.WearPpmPerBatch, a.AddsG, a.HasTool, a.Quality, a.Wage),
                     (b.Id, b.Station, b.Skill, b.WorkerHours, b.MaxWorkers, b.DurationDays, b.MinBatchPermille, b.WearPpmPerBatch, b.AddsG, b.HasTool, b.Quality, b.Wage));
        Assert.Equal(a.Inputs, b.Inputs); Assert.Equal(a.Outputs, b.Outputs);
    }

    private static void SameRecipe(Recipe a, Recipe b)
    {
        Assert.Equal((a.Index, a.Id, a.Station, a.Skill, a.WorkerHours, a.MaxWorkers, a.DurationDays, a.MinBatchPermille, a.WearPpmPerBatch, a.AddsG, a.HasTool, a.Quality, a.Wage),
                     (b.Index, b.Id, b.Station, b.Skill, b.WorkerHours, b.MaxWorkers, b.DurationDays, b.MinBatchPermille, b.WearPpmPerBatch, b.AddsG, b.HasTool, b.Quality, b.Wage));
        Assert.Equal(a.Inputs, b.Inputs); Assert.Equal(a.Outputs, b.Outputs);
    }

    // ------------------------------------------------------------------ CF-1: one recipe format

    [Fact]
    public void CF1_ThePhase2RecipesConvert_TheRecipeBookViewEqualsTheOldBookFieldByField()
    {
        var cat = EconFixture.Catalog();
        var old = Phase2Defs();
        Assert.Equal(41, old.Count);
        var oldBook = RecipeBook.Build(old, cat);
        var view = RecipeBook.Build(EconFixture.Content, cat);                       // the derived view of the business craft recipes
        Assert.Equal(oldBook.Recipes.Count + 1, view.Recipes.Count);                  // P7-IN2: the Phase 7 make_dry_cask is the one business recipe beyond Phase 2's 41 (appended after them)
        foreach (var o in oldBook.Recipes) SameRecipeIgnoringIndex(o, view.Recipes.Single(v => v.Id == o.Id));   // P7-IN2: make_dry_cask sorts in among them, so the indices after it move; the rows match by id
        // a mod's Phase 2 row converts into the one format and back to the same book row
        var round = RecipeBook.Build(old.Select(d => RecipeUnifier.ToBook(EconFixture.Content, RecipeUnifier.ToCraft(d))), cat);
        for (int i = 0; i < oldBook.Recipes.Count; i++) SameRecipe(oldBook.Recipes[i], round.Recipes[i]);
        // every business recipe is in the recipe database too, under its book id (salt_meat kept its Phase 2 id in the book; its craft id is salt_meat_tub)
        var db = RecipeDb.Build(EconFixture.Content, new EconomyState[] { EconomyState.FromContent(EconFixture.Content, 1, 1) }[0].Goods);
        foreach (var r in view.Recipes) Assert.True(db.ByBookId(r.Id) is { Business: true }, r.Id);
        Assert.Equal("salt_meat_tub", db.ByBookId("salt_meat")!.Id);
        Assert.False(db["salt_meat"].Business);
    }

    // ------------------------------------------------------------------ CF-2: content

    [Fact]
    public void CF2_EightyRecipesLoad_MassChecks_FortyProficienciesAcyclic_EveryStationHasAPiece()
    {
        var w = new CraftWorld();
        output.WriteLine($"{w.Db.Recipes.Count} recipes ({w.Db.Recipes.Count(r => r.Business)} business), {w.Db.Proficiencies.Count} proficiencies, {w.Db.Stations.Count} stations, {w.Db.Manuscripts.Count} manuscripts");
        Assert.True(w.Db.Recipes.Count >= 80);
        Assert.Equal(42, w.Db.Recipes.Count(r => r.Business));   // P7-IN2: make_dry_cask (41 before)
        foreach (var r in w.Db.Recipes) Assert.True(w.Db.OutputMassG(r) <= w.Db.InputMassG(r) + r.AddsG, r.Id);
        Assert.True(w.Db.Proficiencies.Count >= 40);
        Assert.Equal(8, w.Db.Manuscripts.Count);
        Assert.True(w.Db.Stations.Count >= 34);
        foreach (var st in w.Db.Stations.Values)
        {
            if (st.Id == "campfire") continue;                                          // the lit fire is a world object, not a piece
            Assert.NotEmpty(st.Pieces);
            foreach (var p in st.Pieces) Assert.True(EconFixture.Content.Contains("build_piece", p), $"{st.Id}: {p}");
        }
        foreach (var r in w.Db.Recipes.Where(r => r.Business)) Assert.Contains(RecipeUnifier.KindOf(EconFixture.Content, r.Workstation!), RecipeBook.StationKinds);
        Assert.Contains(w.Db.Recipes, r => r.IsSmithing && r.Edged);
        Assert.Contains(w.Db.Recipes, r => r.Results.Any(x => x.Kind == ResultKind.VehiclePart));
        Assert.Contains(w.Db.Recipes, r => r.Guild == "smiths");
        // a cycle is refused at load (the Phase 3 rule still holds over the larger graph)
        Assert.Contains(w.Db.Proficiencies, p => p.Id == "plate_armour" && p.Requires.Any(q => q.Id == "armoursmithing"));
    }

    // ------------------------------------------------------------------ CF-3: stations and stores from the world

    private static List<CellEdit> Box(int x0, int y0, int iw, int ih, string floor = "floor_packed_earth")
    {
        var e = new List<CellEdit>();
        for (int y = y0 - 1; y <= y0 + ih; y++)
            for (int x = x0 - 1; x <= x0 + iw; x++)
                e.Add(new CellEdit(x, y, 0, CellLayer.Structure, x >= x0 && x < x0 + iw && y >= y0 && y < y0 + ih ? floor : "wall_timber"));
        return e;
    }

    /// <summary>The RD-7 smithy (forges at 1,1 and 2,1, an anvil at 4,3) with a store room east of it through a door (one building), and a shed 2 m beyond.</summary>
    private RoomWorld Smithy()
    {
        var w = fx.Lib.NewWorld();
        var e = Box(1, 1, 4, 3);
        e.AddRange(Box(6, 1, 4, 3).Where(c => c.X != 5));                              // the store room shares the wall at x = 5
        e.Add(new CellEdit(5, 2, 0, CellLayer.Structure, "door_wood"));
        e.Add(new CellEdit(3, 4, 0, CellLayer.Structure, "door_wood"));
        e.Add(new CellEdit(1, 1, 0, CellLayer.Fixture, "forge")); e.Add(new CellEdit(2, 1, 0, CellLayer.Fixture, "forge")); e.Add(new CellEdit(4, 3, 0, CellLayer.Fixture, "anvil"));
        e.Add(new CellEdit(6, 2, 0, CellLayer.Fixture, "chest")); e.Add(new CellEdit(8, 2, 0, CellLayer.Fixture, "chest"));
        e.AddRange(Box(13, 1, 2, 2));                                                    // a shed of its own (another structure)
        e.Add(new CellEdit(13, 2, 0, CellLayer.Fixture, "chest"));
        w.Apply(e);
        w.RoofOver("roof_thatch");
        return w;
    }

    [Fact]
    public void CF3_TheHeroInTheSmithy_ForgesFromTheRoom_ToolsInAChestInReachCount_AChestFurtherInTheBuildingIsFetched_AnotherBuildingNot()
    {
        var cw = new CraftWorld(); var world = Smithy();
        Assert.Equal(world.StructureAt(1, 2, 0), world.StructureAt(8, 2, 0));
        Assert.NotEqual(world.StructureAt(1, 2, 0), world.StructureAt(13, 2, 0));
        var near = cw.Econ.OpenStore(StoreKind.House, EntityId.Make(IdKind.Other, 9001), 0, 0, false);
        var far = cw.Econ.OpenStore(StoreKind.House, EntityId.Make(IdKind.Other, 9002), 0, 0, false);
        var shed = cw.Econ.OpenStore(StoreKind.House, EntityId.Make(IdKind.Other, 9003), 0, 0, false);
        var iron = cw.Econ.Goods.IdOf("iron_bar");
        far.Create(iron, 3000, CreateReason.Other); shed.Create(iron, 3000, CreateReason.Other);
        cw.Give("charcoal", 2000);
        var containers = new[]
        {
            new ReachContainer(EntityId.Make(IdKind.Other, 9001), "chest", 6, 2, 0, near, ["tongs"]),       // 5 m from the hero, through the door
            new ReachContainer(EntityId.Make(IdKind.Other, 9002), "chest", 8, 2, 0, far, ["saw"]),          // 7 m: the same building
            new ReachContainer(EntityId.Make(IdKind.Other, 9003), "chest", 13, 2, 0, shed, ["axe"]),        // 12 m: another building
        };
        var scan = CraftReach.Scan(world, cw.Db, 1, 2, 0, cw.Pack, containers, 255);
        Assert.Equal("Smithy", scan.RoomName);
        var forge = scan.Stations.Single(s => s.Station == "forge"); var anvil = scan.Stations.Single(s => s.Station == "anvil");
        Assert.Equal((2, true), (forge.Count, forge.InRoom)); Assert.Equal(1, anvil.Count);
        Assert.Equal(1, scan.Site.Level("gripping"));                                  // the tongs in the chest in reach count
        Assert.Equal(0, scan.Site.Level("sawing"));                                    // the saw 7 m away does not until it is carried
        Assert.Equal(0, scan.Site.Level("chopping"));
        Assert.Equal(new[] { "pack", "chest", "chest" }, scan.Sources.Select(s => s.Name).ToArray());
        Assert.True(scan.Sources[2].Fetch); Assert.Equal(7000, scan.Sources[2].DistMm);
        // the iron is fetched from the store room as part of the job: real walking time (7 m there and back at 1.2 m/s, plus handling), one trip under the carry limit
        cw.Skill(Sk.Smithing, 30);
        var nails = cw.Db["forge_nails_small"];
        var plan = cw.R.Plan(nails, cw.Actor(), scan.Site, scan.Sources);
        Assert.True(plan.CanStart, string.Join("; ", plan.Missing));
        Assert.Equal(1, plan.Fetches.Single().Trips);
        Assert.Equal((2 * 7000 + 1199) / 1200 + 30, plan.FetchSeconds);
        var j = cw.R.Start(plan, cw.Actor(), cw.Hero, scan.Sources, cw.Now)!;
        Assert.Equal(plan.FetchSeconds, j.FetchLeft);
        Assert.Equal(2500, far.Qty(iron)); Assert.Equal(3000, shed.Qty(iron));
        var ev = cw.R.Advance(j, plan.FetchSeconds, cw.Now);
        Assert.Contains(ev, e => e.Kind == CraftEventKind.StepDone && e.Text == "fetch");
        Assert.Equal(0, j.StepSeconds);                                                 // the fetching came first
        // a heavy load takes more trips: 60 kg at a 25 kg carry is three
        Assert.Equal((3 * ((2 * 7000 + 1199) / 1200 + 30), 3), CraftSources.FetchTime(60_000, 7000, 25_000, 1200, 30));
        Assert.True(cw.Audit());
    }

    // ------------------------------------------------------------------ CF-4: the crafting section round trip with every Phase 5 key

    [Fact]
    public void CF4_TheCraftingSection_WithJobsCommissionsApprenticeshipsGuildsFiresAndFits_SavesLoadsAndSavesByteIdentical()
    {
        var w = new CraftWorld(); var ctx = Ctx(w); w.Skill(Sk.Smithing, 70); w.Skill(Sk.Smithing, 70, Maker);
        Mint(w, w.Hero, 100_000);
        var sword = w.Db["forge_arming_sword"]; w.St.Know.Learn(w.Hero, sword.Index);
        Stock(w, w.Pack, sword);
        var plan = w.R.Plan(sword, w.Actor(), w.Site, CraftSources.Of(w.Pack), 1, null, true, skipTemper: true);
        var j = w.R.Start(plan, w.Actor(), w.Hero, plan.Sources, w.Now, 3000, 4000)!; w.R.Advance(j, 7200, w.Now);
        Stock(w, w.Pack, sword);
        var (a, cm) = Commissions.Order(ctx, sword, w.Hero, Maker, true, CraftSources.Of(w.Pack), w.Now, w.Ids);
        Assert.Equal(CommissionAnswer.Ok, a);
        Apprenticeships.Start(ctx, w.Hero, Maker, Sk.Smithing, 1, w.Ids);
        w.St.Members[("smiths", 0, w.Hero.Value)] = new GuildMember { Guild = "smiths", Settlement = 0, Person = w.Hero, JoinedDay = 1 };
        Guilds.SeeAt(w.St, w.Hero, 0, 1);
        w.St.Fires.Light(EntityId.Make(IdKind.Other, 77777), w.Hero, 1, 2, w.Now);
        w.St.Fits[EntityId.Make(IdKind.Item, 5)] = 60_000; w.St.OiledUntil[EntityId.Make(IdKind.Item, 6)] = 40;
        WorkshopBridge.Enqueue(w.St, EntityId.Make(IdKind.Other, 4242), 1, sword, 1, 10, w.Ids, cm!.Id);
        Guilds.Record(ctx, "smiths", 0, w.Hero, "taught_outsider", sword.Index, w.Now, w.Ids, null);
        var bytes = MessagePackSerializer.Serialize(CraftingSave.Capture(w.St));
        var back = MessagePackSerializer.Deserialize<CraftingSave>(bytes).Restore();
        Assert.Equal(bytes, MessagePackSerializer.Serialize(CraftingSave.Capture(back)));
        var lj = back.Get(j.Id)!;
        Assert.Equal((j.SkipMask, j.ProcessBonus, j.StepIndex, j.StepSeconds), (lj.SkipMask, lj.ProcessBonus, lj.StepIndex, lj.StepSeconds));
        Assert.Equal(cm.PriceMites, back.Commissions[cm.Id].PriceMites);
        Assert.Single(back.Apprenticeships); Assert.Single(back.Members); Assert.Single(back.Offences); Assert.Equal(1, back.Fires.Count); Assert.Single(back.Queue);
        Assert.False(back.IsEmpty);
    }

    // ------------------------------------------------------------------ CF-5: station bonus

    [Fact]
    public void CF5_AFineAnvilVersusACommonOne_IsPlusThreeOnEveryScore_TheDistributionShiftsByExactlyThree()
    {
        var w = new CraftWorld(); w.Skill(Sk.Smithing, 60);
        var r = w.Db["forge_knife"]; w.St.Know.Learn(w.Hero, r.Index);
        Stock(w, w.Pack, r);
        CraftSite Site(ItemQuality anvil) { var s = new CraftSite { Light = 255, Water = true }; foreach (var t in w.Db.Tools.Keys) s.AddTool(w.Db, t); s.AddStation(w.Db, "forge"); s.AddStation(w.Db, "anvil", anvil); return s; }
        var common = w.R.Plan(r, w.Actor(), Site(ItemQuality.Common), w.Pack);
        var fine = w.R.Plan(r, w.Actor(), Site(ItemQuality.Fine), w.Pack);
        Assert.Equal((0, 3), (common.StationBonus, fine.StationBonus));
        Assert.Equal(fine.OtherBonus, common.OtherBonus + 3);
        Assert.Equal(CraftMath.QualityCounts(common.EffectiveSkill + 3, common.ToolBonus, common.OtherBonus), fine.QualityCounts);
        Assert.Equal(-5, w.R.Plan(r, w.Actor(), Site(ItemQuality.Crude), w.Pack).StationBonus);
        Assert.Equal(5, w.R.Plan(r, w.Actor(), Site(ItemQuality.Masterwork), w.Pack).StationBonus);
        // the job keeps it and every unit's score has it
        var j = w.R.Start(fine, w.Actor(), w.Hero, w.Pack, w.Now)!;
        Assert.Equal(3, j.StationBonus);
    }

    // ------------------------------------------------------------------ CF-6: smithing process

    [Fact]
    public void CF6_ArmingSword_Skill70Difficulty45_ProcessPlusFive_Skill60Nothing_SkippingTheTemperMinusFiveAndBrittle()
    {
        var w = new CraftWorld(); var r = w.Db["forge_arming_sword"];
        Assert.Equal(45, r.Difficulty); Assert.True(r.IsSmithing && r.Edged);
        w.St.Know.Learn(w.Hero, r.Index); w.St.Know.SetPm(w.Hero, w.Db.ProfIndex("bladesmithing"), 1000);
        Stock(w, w.Pack, r); Stock(w, w.Pack, r);
        w.Skill(Sk.Smithing, 70);
        var p70 = w.R.Plan(r, w.Actor(), w.Site, w.Pack);
        Assert.Equal(70, p70.EffectiveSkill); Assert.Equal(5, p70.ProcessBonus);
        w.Skill(Sk.Smithing, 60);
        Assert.Equal(0, w.R.Plan(r, w.Actor(), w.Site, w.Pack).ProcessBonus);
        w.Skill(Sk.Smithing, 70);
        var skip = w.R.Plan(r, w.Actor(), w.Site, CraftSources.Of(w.Pack), 1, null, true, skipTemper: true);
        Assert.Equal(5 - 5, skip.ProcessBonus);
        Assert.Equal(p70.AttendedSeconds - w.R.StepNeed(new CraftJob { Recipe = r.Index, ProfPm = p70.ProfPm, ToolPm = p70.ToolPm, SkillPm = p70.TimePm }, 3), skip.AttendedSeconds);
        // made with the temper skipped: brittle, with the quench's reading of the colours on top of the -5
        var j = w.R.Start(skip, w.Actor(), w.Hero, skip.Sources, w.Now)!;
        Assert.Equal(-5, j.ProcessBonus);
        w.Work(j, 3600);
        Assert.Equal(CraftJobState.Done, j.State);
        Assert.Equal(0, j.ProcessBonus);                                               // -5 + 5 at the quench
        var sword = w.Econ.Items.LiveRows().Select(i => w.Econ.Items.At(i)).Single(x => x.Shape == "arming_sword");
        Assert.True(sword.Flags.HasFlag(ItemFlags.Brittle));
        Assert.Equal("steel", w.Econ.Items.Component(sword.ComponentStart).Material);
        // iron where the recipe wants steel: -5 and the result is iron
        w.Pack.Destroy(w.Econ.Goods.IdOf("steel_bar"), w.Qty("steel_bar"), DestroyReason.Other);
        w.Give("iron_bar", 1400);
        var iron = w.R.Plan(r, w.Actor(), w.Site, w.Pack);
        Assert.True(iron.CanStart, string.Join(";", iron.Missing));
        Assert.Equal(-5, iron.MaterialBonus);
        var j2 = w.R.Start(iron, w.Actor(), w.Hero, w.Pack, w.Now)!; w.Work(j2, 3600);
        var isword = w.Econ.Items.LiveRows().Select(i => w.Econ.Items.At(i)).Where(x => x.Shape == "arming_sword").Last();
        Assert.Equal("wrought_iron", w.Econ.Items.Component(isword.ComponentStart).Material);
        Assert.False(isword.Flags.HasFlag(ItemFlags.Brittle));
        // a forge still hot needs no cold start; a cold one costs its 30 minutes
        var hot = new CraftSite { Light = 255, Water = true, ForgeHot = true }; foreach (var t in w.Db.Tools.Keys) hot.AddTool(w.Db, t); foreach (var s in w.Db.Stations.Keys) hot.AddStation(w.Db, s);
        Stock(w, w.Pack, r);
        var cold = w.R.Plan(r, w.Actor(), w.Site, w.Pack);
        Assert.Equal(CraftMath.StepSeconds(1800, cold.ProfPm, cold.ToolPm, cold.TimePm), cold.AttendedSeconds - w.R.Plan(r, w.Actor(), hot, w.Pack).AttendedSeconds);
        Assert.True(w.R.ForgeIsHot(1000, 2000, w.Now) || w.R.ForgeIsHot(0, 0, w.Now));
    }

    // ------------------------------------------------------------------ CF-7: brittle breakage

    [Fact]
    public void CF7_ABrittleBladeStoppedByPlate_BreaksThirtyInAThousand_ATemperedOneNever()
    {
        var blade = EntityId.Make(IdKind.Item, 4321);
        int breaks = 0, tempered = 0;
        for (long tick = 1; tick <= 1000; tick++)
        {
            if (Smithing.BrittleBreaks(99, tick, blade, brittle: true, stoppedByRigid: true)) breaks++;
            if (Smithing.BrittleBreaks(99, tick, blade, brittle: false, stoppedByRigid: true)) tempered++;
        }
        output.WriteLine($"{breaks} breaks in 1,000 stopped blows");
        Assert.InRange(breaks, 30 - 16, 30 + 16);
        Assert.Equal(0, tempered);
        Assert.False(Smithing.BrittleBreaks(99, 5, blade, true, stoppedByRigid: false));
        Assert.Equal(0x532u, RngStreams.BrittleBreak);
    }

    // ------------------------------------------------------------------ CF-8, CF-9: the live and abstract bridge

    private sealed class Shop
    {
        public ProdKit Kit = new(households: 3);
        public Business B = null!;
        public RecipeDb Db = null!;
        public CraftState St = new();
        public CraftRunner R = null!;
        public IdAllocator Ids = new();
        public int Forge;

        public static Shop Smithy(long ironG = 10_000)
        {
            var s = new Shop();
            s.B = s.Kit.Make("smithy", 100_000, staff: 1, occ: Occupation.Smith, skill: Sk.Smithing, level: 50);
            s.Db = RecipeDb.Build(EconFixture.Content, s.Kit.Econ.Goods);
            s.R = new CraftRunner(s.Db, s.Kit.Econ, s.St, 42, s.Ids, o => s.Kit.Econ.OtherStores.GetValueOrDefault(o)) { StationDone = WorkshopBridge.Done(id => id == s.B.Id ? s.B : null) };
            s.Kit.Sys.LiveStation = (b, i) => WorkshopBridge.IsLive(s.St, b, i);
            s.Kit.Give(s.B, "iron_bar", ironG);
            s.Forge = s.B.Stations.FindIndex(x => x.Kind == "anvil");
            Assert.True(s.Kit.Sys.TryStart(s.B, s.B.Stations[s.Forge], s.Kit.R("forge_nails"), GameTime.FromGameHours(0)));
            return s;
        }
    }

    [Fact]
    public void CF8_SmithyStaffPromotedMidBatch_AtEightHoursOfTwenty_FinishedLive_OrDemotedAtFifteenAt750000_TheBatchCompletesOnce()
    {
        var s = Shop.Smithy(); var st = s.B.Stations[s.Forge];
        Assert.Equal(20, s.Kit.R("forge_nails").WorkerHours);
        st.ProgressMicro = 400_000L * 1_000_000;
        var worker = s.Kit.People.IdOf(1);
        var j = WorkshopBridge.Promote(s.R, s.Kit.Used.Recipes, s.B, s.Forge, worker, 50, 0, s.Ids)!;
        Assert.Equal(8 * 3600, j.StepSeconds);
        Assert.Equal(20 * 3600, s.R.StepNeed(j, j.StepIndex));
        // promoted, the abstract hours do not move it
        s.Kit.RunHours(6, 10); Assert.Equal(400_000L * 1_000_000, st.ProgressMicro);
        // finished live: the station completes and the production system makes the nails once
        s.R.Advance(j, 12 * 3600, 0);
        Assert.Equal(CraftJobState.Done, j.State);
        Assert.Equal(Station.Complete, st.ProgressMicro);
        long before = s.Kit.Sys.BatchesFinished;
        s.Kit.RunHours(10, 40);
        Assert.Equal(before + 1, s.Kit.Sys.BatchesFinished);
        Assert.Equal(9000, s.Kit.Have(s.B, "nails"));
        // demoted at 15 h of 20: 750,000 ppm
        var d = Shop.Smithy(); var dst = d.B.Stations[d.Forge];
        dst.ProgressMicro = 400_000L * 1_000_000;
        var dj = WorkshopBridge.Promote(d.R, d.Kit.Used.Recipes, d.B, d.Forge, d.Kit.People.IdOf(1), 50, 0, d.Ids)!;
        d.R.Advance(dj, 7 * 3600, 0);
        WorkshopBridge.Demote(d.R, d.B, dj);
        Assert.Equal(750_000L * 1_000_000, dst.ProgressMicro);
        Assert.Empty(d.St.Jobs);
        d.Kit.RunHours(6, 60);
        Assert.Equal(9000, d.Kit.Have(d.B, "nails"));
        Assert.Equal(1, d.Kit.Sys.BatchesFinished);
    }

    [Fact]
    public void CF9_AThousandPromoteAndDemoteCycles_InputsAndOutputsExact_NoBatchDoneTwice()
    {
        var s = Shop.Smithy(); var st = s.B.Stations[s.Forge];
        var worker = s.Kit.People.IdOf(1); ulong rng = 7; long worked = 0;
        for (int n = 0; n < 1000; n++)
        {
            var j = WorkshopBridge.Promote(s.R, s.Kit.Used.Recipes, s.B, s.Forge, worker, 50, 0, s.Ids);
            if (j == null) break;
            rng = DetRng.Mix(rng + 0x9E37); long sec = 10 + (long)(rng % 60);
            long ppmBefore = st.ProgressMicro / 1_000_000;
            s.R.Advance(j, sec, 0); worked += sec;
            if (j.Closed) { s.St.Remove(j.Id); break; }
            WorkshopBridge.Demote(s.R, s.B, j);
            Assert.True(st.ProgressMicro / 1_000_000 <= ppmBefore + sec * 1_000_000 / (20 * 3600) + 1);        // floor both ways: never more than was worked
        }
        Assert.True(st.ProgressMicro <= worked * 1_000_000 / (20 * 3600) * 1_000_000);
        Assert.Equal(0, s.Kit.Have(s.B, "iron_bar"));                                   // the inputs went once, at the start
        s.Kit.RunHours(6, 200);
        Assert.Equal(1, s.Kit.Sys.BatchesFinished);
        Assert.Equal(9000, s.Kit.Have(s.B, "nails"));                                    // made once
        Assert.Empty(s.St.Jobs);
    }

    // ------------------------------------------------------------------ CF-10: apprenticeship

    [Fact]
    public void CF10_MasterSixty_ApprenticeFifteen_LearnsSmithRecipesUpToFortyFive_OneEveryTwoDaysAscending_SevenInATerm()
    {
        var w = new CraftWorld(); var ctx = Ctx(w);
        w.Skill(Sk.Smithing, 60, Maker); w.Skill(Sk.Smithing, 15);
        var (a, ap) = Apprenticeships.Start(ctx, w.Hero, Maker, Sk.Smithing, 10, w.Ids);
        Assert.Equal(ApprenticeAnswer.Ok, a); Assert.Equal(45, ap!.Cap);
        var expected = w.Db.Recipes.Where(r => r.Skill == Sk.Smithing && r.MasterTeaches && !r.IsPractice && r.Difficulty <= 45 && r.Guild == null && !w.St.Know.Knows(r, w.Hero, w.St.Book))
            .OrderBy(r => r.Difficulty).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
        var learnt = new List<CraftRecipe>();
        for (int day = 10; day <= 30; day++) learnt.AddRange(Apprenticeships.Day(ctx, day, _ => true).Select(x => x.R));
        output.WriteLine(string.Join(", ", learnt.Select(r => $"{r.Id} {r.Difficulty}")));
        Assert.Equal(7, learnt.Count);
        Assert.All(learnt, r => Assert.True(r.Difficulty <= 45 && r.Skill == Sk.Smithing));
        for (int i = 1; i < learnt.Count; i++) Assert.True(learnt[i - 1].Difficulty <= learnt[i].Difficulty);
        Assert.True(expected.Count >= 7 && learnt[0].Difficulty >= expected.Min(r => r.Difficulty));
        Assert.True(ap.Ended);
        Assert.True(w.St.Book.Level(w.Hero, Sk.Smithing) > 15);                        // the work raised his skill, at double rate
        // the master's consent: a third apprentice is refused, and so is one too far below him
        w.Skill(Sk.Smithing, 5, Student);
        Assert.Equal(ApprenticeAnswer.CandidateTooUnskilled, Apprenticeships.Check(ctx, Student, Maker, Sk.Smithing));
        w.Skill(Sk.Smithing, 30, Student); w.Skill(Sk.Smithing, 30, Client2);
        Assert.Equal(ApprenticeAnswer.Ok, Apprenticeships.Start(ctx, Student, Maker, Sk.Smithing, 40, w.Ids).Item1);
        Assert.Equal(ApprenticeAnswer.Ok, Apprenticeships.Start(ctx, Client2, Maker, Sk.Smithing, 40, w.Ids).Item1);
        w.Skill(Sk.Smithing, 30, EntityId.Make(IdKind.Person, 777));
        Assert.Equal(ApprenticeAnswer.MasterFull, Apprenticeships.Check(ctx, EntityId.Make(IdKind.Person, 777), Maker, Sk.Smithing));
        // leaving early keeps what was learnt
        var mine = Apprenticeships.Start(ctx, w.Hero, Maker, Sk.Smithing, 100, w.Ids);
        Assert.Equal(ApprenticeAnswer.MasterFull, mine.Item1);
    }

    // ------------------------------------------------------------------ CF-11, CF-12: commissions

    [Fact]
    public void CF11_ArmingSwordCommission_24Hours_MasterWage50_Labour1740_PlusHisSteelAndCharcoalAtAsk_HalfToEscrow()
    {
        var w = new CraftWorld(); var ctx = Ctx(w); w.Skill(Sk.Smithing, 70, Maker);
        var r = w.Db["forge_arming_sword"];
        Assert.Equal(24, Commissions.LabourHours(r, 1));
        Assert.Equal(1740, Commissions.LabourMites(24, Commissions.WagePerHour(70)));
        Mint(w, w.Hero, 50_000);
        var q = Commissions.Quote(ctx, r, w.Hero, Maker, false, w.Now)!;
        var cat = w.Econ.Goods;
        long mats = ctx.Ask(cat.IdOf("steel_bar"), 1400) + ctx.Ask(cat.IdOf("logs"), 300) + ctx.Ask(cat.IdOf("charcoal"), 500) + ctx.Ask(cat.IdOf("charcoal"), 22_500);
        Assert.Equal(1740, q.LabourMites);
        Assert.Equal(1740 + mats, q.PriceMites);
        long before = Coin(w, w.Hero);
        var (a, c) = Commissions.Order(ctx, r, w.Hero, Maker, false, CraftSources.Of(w.Pack), w.Now, w.Ids);
        Assert.Equal(CommissionAnswer.Ok, a);
        Assert.Equal(q.PriceMites / 2, before - Coin(w, w.Hero));
        Assert.Equal(q.PriceMites / 2, w.Econ.Ledger.Balance(w.Econ.Ledger.Open(PurseKind.Chest, c!.Id)).Mites);
        // a skilled maker (skill 40) charges 35 an hour
        Assert.Equal(24 * 35 * 1450 / 1000, Commissions.LabourMites(24, Commissions.WagePerHour(40)));
    }

    [Fact]
    public void CF12_CommissionQueue_MakerBusy30Hours_DueAfterHisQueue_CollectedInPersonOnly_Uncollected15DaysSold_MaterialsRefunded()
    {
        var w = new CraftWorld(); var makerStore = w.Econ.OpenStore(StoreKind.House, Maker, 0, 0, false); var ctx = Ctx(w, makerStore: makerStore);
        w.Skill(Sk.Smithing, 70, Maker); w.St.Know.SetPm(Maker, w.Db.ProfIndex("bladesmithing"), 1000);
        var r = w.Db["forge_arming_sword"];
        Mint(w, w.Hero, 50_000); Mint(w, Maker, 100);
        // something of 30 working hours already in his queue
        var busy = EntityId.Make(IdKind.Other, 99_999);
        w.St.Commissions[busy] = new Commission { Id = busy, Client = Client2, Maker = Maker, Recipe = r.Index, WorkSec = 30 * 3600, State = CommissionState.Queued, Choice = new byte[r.Slots.Length] };
        var plan = w.R.Plan(r, new CraftActor(Maker), CraftWorld.FullSite(w.Db), (Store?)null);
        long now = 8 * 3600;
        var q = Commissions.Quote(ctx, r, w.Hero, Maker, true, now)!;
        Assert.Equal(30 * 3600, q.AheadSec);
        Assert.Equal(plan.AttendedSeconds, q.WorkSec);                                  // his time factor: 24 h at his speed
        Assert.Equal(Commissions.DueHour(8, 30 * 3600 + plan.AttendedSeconds), q.DueHour);
        output.WriteLine($"work {q.WorkSec / 3600.0:0.0} h, due day {q.DueHour / 24} hour {q.DueHour % 24}");
        w.St.Commissions.Remove(busy);
        // the client brings the steel, the grip wood and the charcoal; it moves into the maker's store, marked his
        Stock(w, w.Pack, r);
        var (a, c) = Commissions.Order(ctx, r, w.Hero, Maker, true, CraftSources.Of(w.Pack), now, w.Ids);
        Assert.Equal(CommissionAnswer.Ok, a);
        Assert.Equal(0, w.Qty("steel_bar"));
        Assert.Equal(CommissionState.Working, c!.State);                                // laid out on his bench at once: in the job's store, waiting for his hours
        Assert.Equal(1400, w.Econ.OtherStores[c.Job].Qty(w.Econ.Goods.IdOf("steel_bar")));
        Assert.False(Commissions.WorkDone(w.St, c));
        Assert.Equal(c.LabourMites, c.PriceMites);
        // never instant: he works it in his hours, day by day
        long h = 8; while (c.State != CommissionState.Ready && h < 24 * 20) Commissions.Hourly(ctx, h++);
        Assert.Equal(CommissionState.Ready, c.State);
        Assert.True(c.ReadyHour >= 8 + 24, $"ready at {c.ReadyHour}");
        Assert.True(c.ReadyHour <= Commissions.DueHour(8, c.WorkSec) + 24);
        var item = Assert.Single(c.Items);
        Assert.Equal(c.Id, w.Econ.Items.Get(item).Owner);                               // in his store, the client's to collect
        // in person only: not by another, not from another place
        Assert.Equal(CommissionAnswer.NotHere, Commissions.Collect(ctx, c, Client2, 0, w.Pack));
        Assert.Equal(CommissionAnswer.NotHere, Commissions.Collect(ctx, c, w.Hero, 3, w.Pack));
        // 15 days uncollected: his to sell, the client's materials' value refunded
        long coinBefore = Coin(w, w.Hero), refund = c.ClientMaterialsMites;
        Assert.True(refund > 0);
        Mint(w, Maker, refund);
        for (long hh = h; hh <= c.ReadyHour + 15 * 24; hh++) Commissions.Hourly(ctx, hh);
        Assert.Equal(CommissionState.Sold, c.State);
        Assert.Equal(coinBefore + refund, Coin(w, w.Hero));
        Assert.Equal(Maker, w.Econ.Items.Get(item).Owner);
        Assert.Equal(CommissionAnswer.Gone, Commissions.Collect(ctx, c, w.Hero, 0, w.Pack));
        Assert.True(w.Audit());
    }

    [Fact]
    public void Commission_CollectedInPerson_PaysTheRest_TheItemIsTheClients_CancelBeforeWorkRefundsAll()
    {
        var w = new CraftWorld(); var ctx = Ctx(w); w.Skill(Sk.Smithing, 70, Maker);
        var r = w.Db["forge_knife"]; Mint(w, w.Hero, 50_000);
        Stock(w, ctx.StoreOf(Maker)!, r);
        long total = Coin(w, w.Hero) + Coin(w, Maker);
        var (_, c) = Commissions.Order(ctx, r, w.Hero, Maker, false, CraftSources.Of(w.Pack), 8 * 3600, w.Ids);
        long h = 8; while (c!.State != CommissionState.Ready && h < 24 * 10) Commissions.Hourly(ctx, h++);
        Assert.Equal(CommissionAnswer.Ok, Commissions.Collect(ctx, c, w.Hero, 0, w.Pack));
        Assert.Equal(w.Hero, w.Econ.Items.Get(c.Items[0]).Owner);
        Assert.Equal(c.PriceMites, Coin(w, Maker));
        Assert.Equal(total, Coin(w, w.Hero) + Coin(w, Maker));                           // coin conserved to the mite
        // cancelled before he starts: everything back
        Stock(w, w.Pack, r);
        long coin = Coin(w, w.Hero);
        var (_, c2) = Commissions.Order(ctx, r, w.Hero, Maker, true, CraftSources.Of(w.Pack), 30 * 24 * 3600 + 20 * 3600, w.Ids);
        Assert.Equal(CommissionAnswer.Ok, Commissions.Cancel(ctx, c2!, w.Hero, w.Pack));
        Assert.Equal(coin, Coin(w, w.Hero)); Assert.Equal(350, w.Qty("steel_bar"));
        Assert.True(w.Audit());
    }

    // ------------------------------------------------------------------ CF-13: guilds

    [Fact]
    public void CF13_ANonMemberIsRefusedTheSecret_AfterJoiningFifteenDaysAndTheFee_Taught_TeachingAnOutsiderIsAnOffenceWithWitnesses()
    {
        var w = new CraftWorld(); var ctx = Ctx(w); w.Skill(Sk.Smithing, 75, Maker); w.Skill(Sk.Smithing, 45);
        var secret = w.Db["forge_longsword"];
        Assert.Equal("smiths", secret.Guild);
        var g = Assert.Single(Guilds.Of(ctx, 0));
        Assert.Equal(Maker, g.Master);
        Assert.Empty(Guilds.Of(Ctx(w, smithBusinesses: 2), 0));                        // three businesses of the trade make a guild
        Assert.False(Guilds.MayTeach(ctx, secret, Maker, w.Hero, w.Hero, out _));     // "taught only in the guild"
        Mint(w, w.Hero, 6000);
        Guilds.SeeAt(w.St, w.Hero, 0, 100);
        Assert.Equal(GuildAnswer.NotResident, Guilds.Join(ctx, w.Hero, "smiths", 0, 110));
        w.Skill(Sk.Smithing, 30);
        Assert.Equal(GuildAnswer.TooUnskilled, Guilds.Join(ctx, w.Hero, "smiths", 0, 115));
        w.Skill(Sk.Smithing, 45);
        Assert.Equal(GuildAnswer.Ok, Guilds.Join(ctx, w.Hero, "smiths", 0, 115));
        Assert.Equal(1000, Coin(w, w.Hero)); Assert.Equal(5000, Coin(w, Maker));       // the fee to the guild master's purse
        Assert.True(Guilds.MayTeach(ctx, secret, Maker, w.Hero, w.Hero, out bool off) && !off);
        w.St.Know.Learn(Maker, secret.Index);
        Assert.NotNull(w.R.StartTeaching(w.Actor(), new CraftActor(Maker), secret, byLabour: true));
        // the hero, a member, teaches an outsider: allowed, and an offence with its witnesses
        w.St.Know.Learn(w.Hero, secret.Index);
        Assert.True(Guilds.MayTeach(ctx, secret, w.Hero, Student, w.Hero, out bool offence));
        Assert.True(offence);
        var chron = new Chronicle();
        var o = Guilds.Record(ctx, "smiths", 0, w.Hero, "taught_outsider", secret.Index, w.Now, w.Ids, chron);
        Assert.Equal([EntityId.Make(IdKind.Person, 701), EntityId.Make(IdKind.Person, 702)], o.Witnesses);
        Assert.Single(w.St.Offences);
        Assert.Single(chron.ByType("guild_offence"));
        // dues each season; two seasons owed and he is out
        Assert.Empty(Guilds.Dues(ctx, 120));
        Assert.Equal(700, Coin(w, w.Hero));
        w.Econ.Ledger.Transfer(Purse(w, w.Hero), Purse(w, Maker), new Coin(700), TransferReason.Trade);
        Assert.Empty(Guilds.Dues(ctx, 135));
        Assert.Single(Guilds.Dues(ctx, 150));
        Assert.False(Guilds.IsMember(w.St, "smiths", w.Hero));
    }

    // ------------------------------------------------------------------ CF-14 to CF-16: repair cap, refit, salvage

    [Fact]
    public void CF14_FineMailReriveted_ByACommonSmith_CapsAt950()
    {
        var w = new CraftWorld(); var r = w.Db["rerivet_mail"]; w.St.Know.Learn(w.Hero, r.Index); w.St.Know.SetPm(w.Hero, w.Db.ProfIndex("riveted_mail"), 1000);
        var mail = w.Econ.Items.Add(w.Ids, "mail_hauberk", ItemQuality.Fine, w.Hero, [new ItemComponent("wrought_iron", 9000, 1000)], conditionPermille: 800);
        w.Skill(Sk.Smithing, 45);                                                       // expected tier Common: cap min(950, 900 + 50)
        Assert.Equal(950, w.R.RepairCap(r, ItemQuality.Fine, 45));
        var j = w.StartNew("rerivet_mail", 1, mail); w.Work(j);
        Assert.Equal(950, w.Econ.Items.Get(mail).ConditionPermille);
        // skill 35 is Poor on the one scale (15-39): its cap is 800 + 50
        Assert.Equal(850, w.R.RepairCap(r, ItemQuality.Fine, 35));
    }

    [Fact]
    public void CF15_RefitPlateTwoSeventhsTooLarge_TwoRefitsOfFourHours_PlateTooSmallIsRefused()
    {
        var w = new CraftWorld(); var r = w.Db["refit_plate"]; w.St.Know.Learn(w.Hero, r.Index); w.St.Know.SetPm(w.Hero, w.Db.ProfIndex("plate_armour"), 1000);
        w.Skill(Sk.Smithing, 55);                                                       // no risk of failure (difficulty 50 + 5)
        int body = 70_000;
        var plate = w.Econ.Items.Add(w.Ids, "breastplate", ItemQuality.Common, w.Hero, [new ItemComponent("steel", 5000, 1000)]);
        w.St.Fits[plate] = body + 2 * body / 7;
        for (int n = 0; n < 2; n++)
        {
            var plan = w.Stock("refit_plate");
            Assert.Equal(4, Commissions.LabourHours(r, 1));
            Assert.Equal(CraftMath.StepSeconds(4 * 3600, plan.ProfPm, plan.ToolPm, plan.TimePm), plan.AttendedSeconds);
            var j = w.R.Start(plan, w.Actor(), w.Hero, CraftSources.Of(w.Pack), w.Now, 0, 0, plate, body)!;
            Assert.NotNull(j); w.Work(j);
        }
        Assert.Equal(body, w.R.FitSize(plate));
        Assert.Null(w.R.Start(w.Stock("refit_plate"), w.Actor(), w.Hero, CraftSources.Of(w.Pack), w.Now, 0, 0, plate, body));   // it fits: nothing to do
        var small = w.Econ.Items.Add(w.Ids, "breastplate", ItemQuality.Common, w.Hero, [new ItemComponent("steel", 5000, 1000)]);
        w.St.Fits[small] = body - body / 7;
        Assert.Equal(0, w.R.RefitStep(r, small, body));
        Assert.Null(w.R.Start(w.Stock("refit_plate"), w.Actor(), w.Hero, CraftSources.Of(w.Pack), w.Now, 0, 0, small, body));   // plate cannot be let out
        // mail can grow: the wire is woven in
        var mr = w.Db["refit_mail"]; w.St.Know.Learn(w.Hero, mr.Index); w.Skill(Sk.Smithing, 40);
        var mail = w.Econ.Items.Add(w.Ids, "mail_hauberk", ItemQuality.Common, w.Hero, [new ItemComponent("wrought_iron", 9000, 1000)]);
        w.St.Fits[mail] = body - body / 7;
        var mj = w.R.Start(w.Stock("refit_mail"), w.Actor(), w.Hero, CraftSources.Of(w.Pack), w.Now, 0, 0, mail, body)!; w.Work(mj);
        Assert.Equal(body, w.R.FitSize(mail));
        Assert.Equal(9900, w.Econ.Items.Component(w.Econ.Items.At(w.Econ.Items.IndexOf(mail)).ComponentStart).MassG);
    }

    [Fact]
    public void CF16_SalvageMail9kg_Gives6_3kgRings_2_7kgScrap_MassExact()
    {
        var w = new CraftWorld(); var r = w.Db["salvage_mail"];
        var mail = w.Econ.Items.Add(w.Ids, "mail_hauberk", ItemQuality.Common, w.Hero, [new ItemComponent("wrought_iron", 9000, 1000)]);
        var j = w.R.Start(w.R.Plan(r, w.Actor(), w.Site, w.Pack), w.Actor(), w.Hero, w.Pack, w.Now, 0, 0, mail)!;
        w.Work(j);
        Assert.Equal(6300, w.Qty("mail_rings")); Assert.Equal(2700, w.Qty("scrap_iron"));
        Assert.False(w.Econ.Items.IsLive(mail));
        // armour by material: the plates to scrap, the straps to rags, the rest as waste
        var parts = new List<ItemComponent> { new("steel", 5000, 1000), new("leather", 300, 1000) };
        var lines = CraftRunner.SalvageOf(w.Db["salvage_armour"], parts, m => m == "steel" ? "metal" : "leather");
        Assert.Equal(5300, lines.Sum(l => l.Grams));
        Assert.Contains((w.Econ.Goods.IdOf("scrap_iron"), 4000L), lines);
        Assert.True(w.Audit());
    }

    // ------------------------------------------------------------------ CF-17: fire

    [Fact]
    public void CF17_KindleAFire_OneKgOfFuel_HeatAndLightForTwoHoursThenOut_Rain6mmPutsItOut()
    {
        var w = new CraftWorld(); w.Skill(Sk.Forestry, 20);
        var j = w.StartNew("kindle_fire"); long lit = w.Now; w.Work(j);
        var fire = Assert.Single(w.St.Fires.All.Values);
        Assert.Equal(500, w.Econ.OtherStores[fire.Id].Qty(w.Econ.Goods.IdOf("firewood")));
        w.Give("firewood", 500);
        Assert.True(w.St.Fires.Feed(w.Econ, fire.Id, w.Pack, w.Econ.Goods.IdOf("firewood"), 500));
        long t0 = fire.LitSec;
        Assert.Empty(w.R.BurnFires(t0 + 7199, 0));
        Assert.True(w.St.Fires.StationNear(1000, 2000)); Assert.Equal(200, w.St.Fires.LightAt(1000 + 3000, 2000));
        Assert.Equal(0, w.St.Fires.LightAt(1000 + 4500, 2000));
        Assert.Single(w.R.BurnFires(t0 + 7200, 0));
        Assert.True(fire.Out); Assert.False(w.St.Fires.StationNear(1000, 2000));
        _ = lit;
        // a second fire under 6 mm an hour of rain goes out at once
        var j2 = w.StartNew("kindle_fire"); w.Work(j2);
        var f2 = w.St.Fires.All.Values.Single(f => !f.Out);
        Assert.Single(w.R.BurnFires(f2.LitSec + 600, 60));
        Assert.True(f2.Out);
        w.St.Fires.Sweep(w.Econ, o => w.Pack);
        Assert.Equal(0, w.St.Fires.Count);
        Assert.True(w.Audit());
    }

    // ------------------------------------------------------------------ CF-18: determinism

    private static string RecordedDay(ulong seed, bool saveMidway)
    {
        var w = new CraftWorld(seed); var ctx = Ctx(w);
        foreach (var s in Enum.GetValues<Sk>()) { w.Skill(s, 45); w.Skill(s, 70, Maker); }
        Mint(w, w.Hero, 100_000); Mint(w, Maker, 1000);
        foreach (var id in new[] { "forge_knife", "forge_arming_sword", "fletch_arrows", "sew_gambeson" }) w.St.Know.Learn(w.Hero, w.Db[id].Index);
        var knife = w.StartNew("forge_knife"); w.Work(knife, 900);
        w.Give("arrowheads", 10); var arrows = w.StartNew("fletch_arrows"); w.Work(arrows, 600);
        Stock(w, w.Pack, w.Db["forge_arming_sword"]);
        var (_, c) = Commissions.Order(ctx, w.Db["forge_arming_sword"], w.Hero, Maker, true, CraftSources.Of(w.Pack), 8 * 3600, w.Ids);
        w.Skill(Sk.Smithing, 30, Student);
        Apprenticeships.Start(ctx, Student, Maker, Sk.Smithing, 0, w.Ids);
        var sb = new System.Text.StringBuilder();
        for (long h = 8; h < 8 + 24 * 4; h++)
        {
            Commissions.Hourly(ctx, h);
            if (h % 24 == 0) Apprenticeships.Day(ctx, (int)(h / 24), _ => true);
            if (saveMidway && h == 30)
            {
                // save and load the crafting section mid-commission; the rest of the run goes on from the loaded state
                var bytes = MessagePackSerializer.Serialize(CraftingSave.Capture(w.St));
                var back = MessagePackSerializer.Deserialize<CraftingSave>(bytes).Restore();
                Assert.Equal(bytes, MessagePackSerializer.Serialize(CraftingSave.Capture(back)));
                back.Book.Base = w.St.Book.Base;
                var st = w.St; w.St = back;
                w.R = new CraftRunner(w.Db, w.Econ, back, seed, w.Ids, o => w.Econ.OtherStores.GetValueOrDefault(o));
                ctx = Ctx(w, makerStore: ctx.StoreOf(Maker));
                _ = st;
            }
        }
        var cc = w.St.Commissions[c!.Id];
        sb.Append(w.Digest()).Append('|').Append(cc.State).Append(cc.ReadyHour).Append('|');
        foreach (var a in w.St.Apprenticeships.Values) sb.Append(string.Join(",", a.Learnt)).Append(';');
        sb.Append(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(MessagePackSerializer.Serialize(CraftingSave.Capture(w.St)))));
        return sb.ToString();
    }

    [Fact]
    public void CF18_ARecordedDayOfCraftingCommissionsAndAnApprenticeship_IsIdentical_AndSaveLoadContinueEqualsTheUninterruptedRun()
    {
        string a = RecordedDay(11, false), b = RecordedDay(11, false), c = RecordedDay(11, true);
        Assert.Equal(a, b);
        Assert.Equal(a, c);
        Assert.NotEqual(a, RecordedDay(12, false));
    }

    // ------------------------------------------------------------------ field kits, masterwork chance, the strings

    [Fact]
    public void FieldKits_GiveLevelOneAnywhereAtMinusFive_EnoughForMendingAndFieldRepair_ButNotForSmithing()
    {
        var w = new CraftWorld(); w.Skill(Sk.Smithing, 30); w.Skill(Sk.Tailoring, 30);
        var kit = new CraftSite { Light = 255, Water = true }; kit.AddTool(w.Db, "field_smith_kit"); kit.AddTool(w.Db, "sewing_kit");
        var mail = w.Econ.Items.Add(w.Ids, "mail_hauberk", ItemQuality.Common, w.Hero, [new ItemComponent("wrought_iron", 9000, 1000)], conditionPermille: 300);
        w.Give("mail_rings", 200);
        var patch = w.R.Plan(w.Db["field_patch_mail"], w.Actor(), kit, w.Pack);
        Assert.True(patch.CanStart, string.Join(";", patch.Missing));
        Assert.Equal(-5, patch.KitPenalty);
        var j = w.R.Start(patch, w.Actor(), w.Hero, w.Pack, w.Now, 0, 0, mail)!; w.Work(j);
        Assert.Equal(450, w.Econ.Items.Get(mail).ConditionPermille);
        w.Give("yarn", 100);
        Assert.Equal(-5, w.R.Plan(w.Db["mend_garment"], w.Actor(), kit, w.Pack).KitPenalty);
        // a real needle in reach is better than the kit: no penalty
        kit.AddTool(w.Db, "needle");
        Assert.Equal(0, w.R.Plan(w.Db["mend_garment"], w.Actor(), kit, w.Pack).KitPenalty);
        // smithing still needs the forge and the anvil
        w.St.Know.Learn(w.Hero, w.Db["forge_knife"].Index); Stock(w, w.Pack, w.Db["forge_knife"]);
        var knife = w.R.Plan(w.Db["forge_knife"], w.Actor(), kit, w.Pack);
        Assert.False(knife.CanStart);
        Assert.Contains(knife.MissingTools, n => n.Quality is "heat" or "anvil");
    }

    [Fact]
    public void TheScreensMasterworkChance_IsTheItemsSpecDistribution_AndABluntToolCostsThree()
    {
        var w = new CraftWorld(); w.Skill(Sk.Smithing, 75);
        var r = w.Db["forge_knife"]; w.St.Know.Learn(w.Hero, r.Index); w.St.Know.SetPm(w.Hero, w.Db.ProfIndex("blacksmithing"), 999);
        Stock(w, w.Pack, r);
        var p = w.R.Plan(r, w.Actor(), w.Site, w.Pack);
        Assert.Equal(41_000, p.QualityMilli.Sum());
        Assert.Equal(CraftMath.QualityMilli(p.EffectiveSkill, p.ToolBonus, p.OtherBonus), p.QualityMilli);
        var blunt = new CraftSite { Light = 255, Water = true }; blunt.AddTool(w.Db, "hammer", blunt: true); blunt.AddStation(w.Db, "forge"); blunt.AddStation(w.Db, "anvil"); blunt.AddTool(w.Db, "tongs");
        var sharp = new CraftSite { Light = 255, Water = true }; sharp.AddTool(w.Db, "hammer"); sharp.AddStation(w.Db, "forge"); sharp.AddStation(w.Db, "anvil"); sharp.AddTool(w.Db, "tongs");
        // the anvil's hammering 3 is better than the hammer, so the blunt hammer does not matter here; a blunt knife where only the knife cuts does
        Assert.Equal(w.R.Plan(r, w.Actor(), sharp, w.Pack).ToolBonus, w.R.Plan(r, w.Actor(), blunt, w.Pack).ToolBonus);
        var shirt = w.Db["sew_shirt"]; w.Skill(Sk.Tailoring, 40); Stock(w, w.Pack, shirt);
        var k1 = new CraftSite { Light = 255 }; k1.AddTool(w.Db, "needle"); k1.AddTool(w.Db, "knife", blunt: true); k1.AddStation(w.Db, "workbench");
        var k2 = new CraftSite { Light = 255 }; k2.AddTool(w.Db, "needle"); k2.AddTool(w.Db, "knife"); k2.AddStation(w.Db, "workbench");
        Assert.Equal(w.R.Plan(shirt, w.Actor(), k2, w.Pack).ToolBonus - 3, w.R.Plan(shirt, w.Actor(), k1, w.Pack).ToolBonus);
    }

    [Fact]
    public void TheCfStrings_AreInEnCsv_InOneBlockBeforeTheFlavourRows()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Ravensguard.Sim"))) dir = dir.Parent;
        var root = dir!.FullName;
        var lines = File.ReadAllLines(Path.Combine(root, "src", "Ravensguard.Game", "localization", "en.csv"));
        var keys = lines.Select(l => l.Split(',')[0]).ToList();
        var csv = keys.ToHashSet();
        var used = new HashSet<string>();
        foreach (var f in Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
            foreach (Match m in Regex.Matches(File.ReadAllText(f), "\"(cf_[a-z0-9_]+)\""))
                if (!m.Groups[1].Value.EndsWith('_')) used.Add(m.Groups[1].Value);
        foreach (var a in Enum.GetNames<GuildAnswer>()) used.Add("cf_join_" + a.ToLowerInvariant());
        foreach (var a in Enum.GetNames<ApprenticeAnswer>()) used.Add("cf_apprentice_" + a.ToLowerInvariant());
        foreach (var s in Enum.GetNames<CommissionState>()) used.Add("cf_cstate_" + s.ToLowerInvariant());
        used.Remove("cf_join_ok"); used.Remove("cf_apprentice_ok");
        var missing = used.Where(k => !csv.Contains(k)).ToList();
        Assert.True(missing.Count == 0, "missing from en.csv: " + string.Join(", ", missing));
        int first = keys.FindIndex(k => k.StartsWith("cf_", StringComparison.Ordinal)), last = keys.FindLastIndex(k => k.StartsWith("cf_", StringComparison.Ordinal));
        Assert.True(first > 0 && keys.Skip(first).Take(last - first + 1).All(k => k.StartsWith("cf_", StringComparison.Ordinal)));
        Assert.True(last < keys.FindIndex(k => k.StartsWith("fl_", StringComparison.Ordinal)));
    }
}
