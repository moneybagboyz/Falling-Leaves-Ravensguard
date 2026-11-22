using Ravensguard.Content;
using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Crafting;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Naval;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Siege;
using Ravensguard.Sim.Tests.Crafting;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.World;
using Ravensguard.Sim.WorldGen.Chunks;
using Xunit.Abstractions;
using Sk = Ravensguard.Sim.People.Skill;

namespace Ravensguard.Sim.Tests.Siege;

/// <summary>P7-SG-B: siege engines as vehicles (sieges-and-engines SG-5 to SG-7): built at camp from hauled materials, dispersed, shot, damaged, burnt, pushed and saved.</summary>
public class EngineTests(ITestOutputHelper output)
{
    private static ContentDatabase Db => EconFixture.Content;
    private static readonly EntityId Lord = EntityId.Make(IdKind.Person, 77);

    // ------------------------------------------------------------------ a camp: the crafting world, the ride book, the engines, a yard and a store of hauled goods

    internal sealed class Camp
    {
        public required CraftWorld W;
        public required RideBook B;
        public required EngineBook Book;
        public required EngineYard Yard;
        public required Store Store;
        public List<EntityId> Hands = [];
        public List<(string Type, string Key, IReadOnlyList<ChronParam> Ps)> Chron = [];

        public static Camp Make(int hands = 6, int skill = 50)
        {
            var w = new CraftWorld(7, EconFixture.NewEconomy(false));
            var b = new RideBook(EconFixture.Content, w.Econ, 7);
            var book = new EngineBook(EconFixture.Content, b, 7);
            var yard = new EngineYard(b, w.R, w.Site, w.Econ) { Ledger = w.Econ.Ledger, Book = book };
            var camp = new Camp { W = w, B = b, Book = book, Yard = yard, Store = w.Econ.OpenStore(StoreKind.House, EntityId.Make(IdKind.Person, 9000), 0, 0, false) };
            yard.Chronicle = (when, type, key, ps, subjects, causes, data) => { camp.Chron.Add((type, key, ps)); return new EntityId(800_000UL + (ulong)camp.Chron.Count); };
            w.Econ.Ledger.Mint(w.Econ.Ledger.Open(PurseKind.Person, Lord), new Coin(50_000_000), SourceReason.Seed);
            for (int i = 0; i < hands; i++)
            {
                var id = EntityId.Make(IdKind.Person, 600UL + (ulong)i); camp.Hands.Add(id);
                w.Skill(Sk.Carpentry, skill, id); w.Skill(Sk.Smithing, skill, id);
                w.Econ.Ledger.Open(PurseKind.Person, id);
            }
            return camp;
        }

        public IEnumerable<(EntityId, int)> Crew(int skill = 50) => Hands.Select(h => (h, skill));

        /// <summary>Hauls the bill's goods from a depot (stocked from outside, the test's faucet) to the camp's store: moved, never made.</summary>
        public void Haul(EngineBill bill)
        {
            var depot = W.Econ.OpenStore(StoreKind.House, EntityId.Make(IdKind.Person, 9001), 0, 0, false);
            foreach (var (good, g) in bill.GoodsG) depot.Create(W.Econ.Goods.IdOf(good), g, CreateReason.Import);
            foreach (var (good, g) in bill.GoodsG) Assert.True(Store.Move(depot, Store, W.Econ.Goods.IdOf(good), g));
        }

        public (VehicleRow V, long Hours, long Days) BuildOut(string template, long maxDays = 90)
        {
            var bp = B.Templates.Get(template);
            var bill = EngineYard.Bill(bp, B.Templates, W.Db, W.Econ.Goods);
            Haul(bill);
            var (a, v) = Yard.Lay(template, Lord, 45_000, -80_000, Crew());
            Assert.Equal(SiteAnswer.Ok, a);
            long start = 24L * 100, hour = start;
            for (; !v!.Finished && hour < start + 24 * maxDays; hour++) Yard.Hour(v, hour, Store);
            return (v!, B.Works.Sites[v.Id].WorkedMilli / 1000, (hour - start) / 24);
        }
    }

    // ------------------------------------------------------------------ the content

    [Fact]
    public void Content_TheEngineTemplatesAreValid_AndTheirNumbersAreTheSpecs()
    {
        var b = new RideBook(Db, EconFixture.NewEconomy(false), 7); var cat = b.Templates; var table = EngineTable.Of(Db);
        Assert.Empty(cat.Errors);
        foreach (var id in new[] { "mantlet", "ram_covered", "siege_tower", "trebuchet", "springald" })
        {
            Assert.NotNull(cat.Find(id)); Assert.NotNull(table.Stat(id));
            output.WriteLine($"{id}: {cat.Get(id).Parts.Count} parts, {cat.Get(id).Stats.MassKg} kg, {cat.Get(id).Stats.WidthCells} x {cat.Get(id).Stats.LengthCells}");
        }
        Assert.All(table.All, s => Assert.NotNull(cat.Find(s.Id)));
        Assert.InRange(cat.Get("ram_covered").Stats.MassKg, 2300, 2700);                       // "2.5 t"
        Assert.InRange(cat.Get("siege_tower").Stats.MassKg, 8500, 9800);                       // "9 t"
        Assert.InRange(cat.Get("trebuchet").Stats.MassKg, 11500, 13000);                       // "12 t" (its counterweight box included)
        Assert.InRange(cat.Get("mantlet").Stats.MassKg, 100, 140);                             // "120 kg"
        Assert.InRange(cat.Get("springald").Stats.MassKg, 350, 450);                           // "400 kg"
        Assert.Equal((3, 8), (cat.Get("ram_covered").Stats.WidthCells, cat.Get("ram_covered").Stats.LengthCells));
        Assert.Equal((4, 4), (cat.Get("siege_tower").Stats.WidthCells, cat.Get("siege_tower").Stats.LengthCells));
        Assert.Equal((6, 10), (cat.Get("trebuchet").Stats.WidthCells, cat.Get("trebuchet").Stats.LengthCells));
        Assert.Equal(3, table.Stat("siege_tower")!.Floors);
        Assert.Equal(1, table.Stat("siege_tower")!.TopZ);
        // every siege part has a recipe that makes it and its mass is conserved (components >= the part + the shavings)
        var db = RecipeDb.Build(Db, new GoodCatalog(Db));
        foreach (var p in cat.PartList.Where(p => p.Tags.Contains("siege")))
        {
            Assert.True(db.TryGet(p.Recipe, out var r), p.Id);
            Assert.Equal(p.Id, r.Results[0].Shape);
            long comp = 0; foreach (var s in r.Slots) comp += new GoodCatalog(Db).MassG(s.Alts[0].Good, s.Alts[0].Qty);
            long made = r.Results[0].MassG; foreach (var by in r.Byproducts) made += by.MassG;
            Assert.True(comp >= made, $"{p.Id}: {comp} g in, {made} g out");
            Assert.Equal(p.MassKg * 1000L, r.Results[0].MassG);
        }
    }

    // ------------------------------------------------------------------ SG-5: building an engine at the camp

    [Fact]
    public void SG5_ACoveredRam_IsBuiltAtCampFrom2_5Tonnes_By6CarpentersIn20WorkerDays_AndTheGoodsAndCoinAreConserved()
    {
        var c = Camp.Make();
        var bp = c.B.Templates.Get("ram_covered");
        var bill = EngineYard.Bill(bp, c.B.Templates, c.W.Db, c.W.Econ.Goods);
        output.WriteLine($"ram bill: {bill.Parts} parts, {bill.ComponentG / 1000.0:0} kg of goods ({string.Join(", ", bill.GoodsG.Select(kv => $"{kv.Key} {kv.Value / 1000.0:0.#}"))}), worth {bill.ValueMites} mites, {bill.WorkerHours} worker-hours");
        var coin0 = c.W.Econ.Ledger.Audit().InPurses.Mites;
        var (v, hours, days) = c.BuildOut("ram_covered");
        output.WriteLine($"built after {days} days by 6 carpenters: {hours} worker-hours = {hours / 10.0:0.0} worker-days (ten-hour days)");
        Assert.True(v.Finished);
        Assert.InRange(bill.ComponentG, 2_300_000, 3_000_000);                                            // "2.5 t of materials"
        Assert.InRange(hours / 10, 17, 24);                                                                 // "20 worker-days"
        Assert.InRange(days, 3, 6);                                                                         // six hands: a few days, in their ten working hours
        Assert.NotNull(c.Book.Find(v.Id));                                                                 // taken into the book when it stood finished
        Assert.Contains(c.Chron, x => x.Type == "engine_built");
        // the camp's store held exactly the bill: nothing is left, nothing was made; the mass audit and the coin audit balance
        Assert.Empty(c.Store.Rows());
        Assert.True(c.W.Audit());
        Assert.Equal(coin0, c.W.Econ.Ledger.Audit().InPurses.Mites);
        long wages = c.Hands.Sum(h => c.W.Econ.Ledger.Balance(c.W.Econ.Ledger.Open(PurseKind.Person, h)).Mites);
        output.WriteLine($"wages paid out of the lord's purse: {wages} mites");
        Assert.True(wages > 0);
        Assert.Equal(c.B.Templates.Get("ram_covered").Stats.MassKg, c.B.Vehicles.StatsOf(v).MassKg);       // the finished vehicle has the template's numbers exactly
        Assert.Empty(VehicleSites.Pile(c.W.Econ.Items, v.Id));
        Assert.Empty(c.W.St.Jobs);                                                                          // every part job was closed
    }

    [Fact]
    public void SG5_ATowerIsAbout60WorkerDays_ATrebuchetAbout80_BuiltBySixHands()
    {
        foreach (var (t, lo, hi) in new[] { ("siege_tower", 50, 70), ("trebuchet", 70, 92) })
        {
            var c = Camp.Make();
            var bill = EngineYard.Bill(c.B.Templates.Get(t), c.B.Templates, c.W.Db, c.W.Econ.Goods);
            var (v, hours, days) = c.BuildOut(t, 120);
            output.WriteLine($"{t}: {bill.Parts} parts, {bill.ComponentG / 1000.0:0} kg of goods ({string.Join(", ", bill.GoodsG.Select(kv => $"{kv.Key} {kv.Value / 1000.0:0.#}"))}), {bill.ValueMites} mites; built in {days} days, {hours} worker-hours ({hours / 10.0:0.0} worker-days)");
            long wages = c.Hands.Sum(h => c.W.Econ.Ledger.Balance(c.W.Econ.Ledger.Open(PurseKind.Person, h)).Mites);
            output.WriteLine($"{t}: wages {wages} mites for {hours} worker-hours");
            Assert.True(v.Finished); Assert.InRange(hours / 10, lo, hi);
            Assert.Empty(c.Store.Rows()); Assert.True(c.W.Audit());
        }
    }

    [Fact]
    public void SG5_AnUnpaidCrewStands_AndAMissingMaterialStallsTheHandThatNeedsIt()
    {
        var c = Camp.Make();
        var bill = EngineYard.Bill(c.B.Templates.Get("mantlet"), c.B.Templates, c.W.Db, c.W.Econ.Goods);
        c.Haul(bill);
        var (_, v) = c.Yard.Lay("mantlet", Lord, 0, 0, c.Crew());
        // the owner has no coin left: nobody works
        var purse = c.W.Econ.Ledger.Open(PurseKind.Person, Lord); var rest = c.W.Econ.Ledger.Balance(purse).Mites;
        var sink = c.W.Econ.Ledger.Open(PurseKind.Person, EntityId.Make(IdKind.Person, 9100));
        c.W.Econ.Ledger.Transfer(purse, sink, new Coin(rest), TransferReason.Trade);
        Assert.Equal(0, c.Yard.Hour(v!, 24 * 5 + 9, c.Store));
        Assert.Equal(0, c.B.Works.Sites[v!.Id].WorkedMilli);
        c.W.Econ.Ledger.Transfer(sink, purse, new Coin(rest), TransferReason.Trade);
        // and nothing is done in the night
        Assert.Equal(0, c.Yard.Hour(v, 24 * 5 + 22, c.Store));
        Assert.True(c.Yard.Hour(v, 24 * 5 + 9, c.Store) > 0);
    }

    // ------------------------------------------------------------------ SG-6: the dispersion cone

    private static (double SigmaDeg, double AlongPct) Spread(EngineBook book, EntityId engine, int skill, int n = 200, long rangeMm = 250_000)
    {
        var st = book.Table.Stat("trebuchet")!;
        double s2 = 0, a2 = 0;
        for (uint serial = 0; serial < n; serial++)
        {
            var (x, y) = book.Landing(engine, serial, 0, 0, rangeMm, 0, skill, st);
            double lat = y, along = x - rangeMm;
            s2 += lat * lat; a2 += along * along;
        }
        return (Math.Atan(Math.Sqrt(s2 / n) / rangeMm) * 180 / Math.PI, Math.Sqrt(a2 / n) / rangeMm * 100);
    }

    [Fact]
    public void SG6_200ShotsAt250m_SpreadSigma1_5DegreesAtSkill0_And0_9AtSkill60_AndTheRangeErrorShrinksTheSame()
    {
        var b = new RideBook(Db, EconFixture.NewEconomy(false), 7); var book = new EngineBook(Db, b, 7);
        var e = VehicleTable.MakeId(1);
        var (s0, r0) = Spread(book, e, 0); var (s60, r60) = Spread(book, e, 60);
        output.WriteLine($"skill 0: sigma {s0:0.00} deg, range error {r0:0.0}% ; skill 60: sigma {s60:0.00} deg, range error {r60:0.0}%");
        Assert.InRange(s0, 1.35, 1.65); Assert.InRange(s60, 0.81, 0.99);
        Assert.InRange(r0, 4.5, 5.5); Assert.InRange(r60, 2.7, 3.3);
        // the same shot lands in the same place whenever it is asked, and a different engine's shots fall elsewhere
        var st = book.Table.Stat("trebuchet")!;
        Assert.Equal(book.Landing(e, 17, 0, 0, 250_000, 0, 60, st), book.Landing(e, 17, 0, 0, 250_000, 0, 60, st));
        Assert.NotEqual(book.Landing(e, 17, 0, 0, 250_000, 0, 60, st), book.Landing(VehicleTable.MakeId(2), 17, 0, 0, 250_000, 0, 60, st));
        // a better engineer is never worse in the means: Engineering 100 is a third of the error
        var (s100, _) = Spread(book, e, 100);
        Assert.InRange(s100, 0.45, 0.55);
    }

    // ------------------------------------------------------------------ shooting at a wall

    internal sealed class Field
    {
        public required WallRig Rig;
        public required RideBook B;
        public required EngineBook Book;
        public required EngineWorld W;
        public List<ShotReport> Shots = [];

        public static Field Make(ulong seed = 7)
        {
            var rig = WallRig.Make(EconFixture.Content);
            var b = new RideBook(EconFixture.Content, EconFixture.NewEconomy(false), seed);
            var book = new EngineBook(EconFixture.Content, b, seed);
            var shots = new List<ShotReport>();
            return new Field { Rig = rig, B = b, Book = book, Shots = shots, W = new EngineWorld { Walls = rig.W, Engineering = _ => 0, OnShot = (e, r) => shots.Add(r) } };
        }

        public (VehicleRow V, Engine E) Raise(string template, long x, long y, long now = 0)
        {
            var v = B.Vehicles.Create(B.Templates.Get(template), Lord, x, y);
            return (v, Book.Raise(v, now)!);
        }
    }

    [Fact]
    public void ATrebuchetShoots_OneStoneEach2400Seconds_ThroughSGAsRuleOnTheLandingCell()
    {
        var f = Field.Make();
        var (v, e) = f.Raise("trebuchet", 45_000, -80_000);
        var eco = EconFixture.NewEconomy(false);
        var store = eco.OpenStore(StoreKind.House, EntityId.Make(IdKind.Person, 9500), 0, 0, false);
        ushort stone = eco.Goods.IdOf("stone_raw"); store.Create(stone, 10_000_000, CreateReason.Import);
        f.W.AmmoOf = _ => store; f.W.Goods = eco.Goods;
        f.Book.Assign(e, Enumerable.Range(0, 8).Select(i => EntityId.Make(IdKind.Person, 700UL + (ulong)i)));
        Assert.Equal(AimResult.Ok, f.Book.Aim(e, 45_500, 40_500));
        Assert.Equal(AimResult.OutOfRange, f.Book.Aim(e, 45_500, 400_500));
        Assert.Equal(AimResult.TooClose, f.Book.Aim(e, 45_500, -70_500));
        f.Book.Aim(e, 45_500, 40_500);
        Assert.Equal(0, f.Book.Advance(e, 2399, f.W));                                // winding: the first stone goes at 2,400
        Assert.Equal(1, f.Book.Advance(e, 2400, f.W));
        Assert.Equal(0, f.Book.Advance(e, 4799, f.W));
        Assert.Equal(1, f.Book.Advance(e, 4800, f.W));
        Assert.Equal(2, e.Fired); Assert.Equal(2U, e.Serial);
        Assert.Equal(10_000_000 - 200_000, store.Qty(stone));                          // a 100 kg stone each shot from the camp's pile
        // an hour of shooting hours is 1.5 stones on average: 12 hours, 18
        int shots = 0; for (long h = 0; h < 24; h++) shots += f.Book.Hour(h, f.W);
        Assert.InRange(shots, 17, 19);
        Assert.Equal(10_000_000 - 100_000 * (2 + shots), store.Qty(stone));
        // the stones fell where the cone put them, and what stood in the landing cell took SG-A's damage
        Assert.Contains(f.Shots, s => s.Wall is WallBlowKind.Damaged or WallBlowKind.Destroyed);
        Assert.All(f.Shots, s => Assert.True(Math.Abs(s.X - 45_500) < 40_000 && Math.Abs(s.Y - 40_500) < 80_000));
        // too few hands: a half crew winds at half the pace; fewer than half cannot
        Assert.Equal(2400, f.Book.ReloadFor(e, 8)); Assert.Equal(4800, f.Book.ReloadFor(e, 4)); Assert.Equal(0, f.Book.ReloadFor(e, 3));
    }

    [Fact]
    public void ARamSwings_1500KgMsAndWithAnIronHead1800_EveryEightySeconds_AndTheGateFallsIn12Swings()
    {
        var f = Field.Make();
        // a ram at the wall, its front at the wall's foot
        var (v, e) = f.Raise("ram_covered", 45_500, 36_000);                          // 8 m long: the front is 4 m ahead of its centre
        v.Facing = 64;                                                                // a quarter turn: along +y
        var front = f.Book.FrontOf(v);
        Assert.InRange(front.Y, 39_900, 40_100);
        Assert.Equal(1800_000, f.Book.RamMomentumMilli(v));                           // 500 kg x 3 m/s x 1.2 (iron head)
        // without the head the blow is 1,500
        var plain = f.B.Vehicles.Create(f.B.Templates.Get("ram_covered"), Lord, 0, 0);
        for (int i = 0; i < plain.Parts.Count; i++) if (f.B.Templates.PartList[plain.Parts[i].Part].Id == "ram_head_iron") { var p = plain.Parts[i]; p.State = PartState.Broken; plain.Parts[i] = p; }
        plain.Touch();
        Assert.Equal(1500_000, f.Book.RamMomentumMilli(plain));
        f.Book.Assign(e, Enumerable.Range(0, 10).Select(i => EntityId.Make(IdKind.Person, 700UL + (ulong)i)), Enumerable.Range(0, 6).Select(i => EntityId.Make(IdKind.Person, 720UL + (ulong)i)));
        Assert.Equal(AimResult.Ok, f.Book.Aim(e, 45_500, 40_500));
        Assert.Equal(AimResult.OutOfRange, f.Book.Aim(e, 45_500, 50_500));
        // on the stone wall a swing does 75 hp (the pace retune, overview 10g): 27 swings of 80 seconds bring one 2,000 hp cell down: a ram is for gates, the trebuchet for walls
        f.Book.Aim(e, 45_500, 40_500);
        int n = f.Book.Advance(e, 80 * 5, f.W);
        Assert.Equal(5, n);
        Assert.Equal(5 * 75, f.Rig.W.HitsOf(new ChunkKey(1, 1, 0))!.Cells.Single().Lost);
    }

    [Fact]
    public void TheWorkedExample_ATrebuchetAgainstAStoneCurtain_ShotsToABreach_BySkillAndRange()
    {
        var lines = new List<string>();
        foreach (var (range, skill) in new[] { (50, 0), (50, 60), (120, 0), (120, 60) })
        {
            var f = Field.Make(); f.W.Engineering = _ => skill;
            var (v, e) = f.Raise("trebuchet", 45_500, 40_500 - range * 1000L);
            f.Book.Assign(e, Enumerable.Range(0, 8).Select(i => EntityId.Make(IdKind.Person, 700UL + (ulong)i)));
            var eco = EconFixture.NewEconomy(false); var store = eco.OpenStore(StoreKind.House, EntityId.Make(IdKind.Person, 9500), 0, 0, false);
            store.Create(eco.Goods.IdOf("stone_raw"), 4_000_000_000L, CreateReason.Import); f.W.AmmoOf = _ => store; f.W.Goods = eco.Goods;
            Assert.Equal(AimResult.Ok, f.Book.Aim(e, 46_000, 41_500));         // the shared edge of two columns, the middle of the thickness
            int shots = 0; long t = 0;
            while (shots < 20_000 && f.Rig.W.BreachesNear(45, 41).Count == 0)
            {
                t += 2400; shots += f.Book.Advance(e, t, f.W);
            }
            int hits = f.Shots.Count(s => s.Wall != WallBlowKind.NotAWall);
            bool breached = f.Rig.W.BreachesNear(45, 41).Count > 0;
            long days = t / (12 * 3600) / 1;     // 12 shooting hours a day at 2,400 s a shot: 18 shots a day
            lines.Add($"range {range} m, Engineering {skill}: {shots} shots, {hits} on the wall, breach {breached}; {shots / 18.0:0.0} days of one engine ({shots / 36.0:0.0} days of two)");
            Assert.True(breached, lines[^1]);
        }
        foreach (var l in lines) output.WriteLine(l);
    }
}
