using Ravensguard.Content;
using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Tests.Economy;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.VehicleDesign;

/// <summary>P5-G: the vehicle-designer gate, part 1 (docs/specs/phase5/vehicle-designer.md 7: VD-1 to VD-3, VD-5 to VD-16, VD-20).</summary>
public class VehicleDesignerTests(ITestOutputHelper output)
{
    internal const ulong Seed = 7;

    /// <summary>The nine sea parts of P7-SH-A (their art is a later part).</summary>
    internal static readonly string[] SeaPartIds = ["hull_plank_sea", "hull_plank_long", "castle_fore", "castle_aft", "fighting_top", "stem_post", "boarding_plank", "ship_ladder", "sail_square_sea"];

    internal sealed class Rig
    {
        public readonly GoodCatalog Goods = EconFixture.Catalog();
        public readonly MassLedger Ledger;
        public readonly RideBook Book;
        public Rig() { Ledger = new MassLedger(Goods.Count); Book = new RideBook(EconFixture.Content, Goods, Ledger, Seed); }
        public VehicleCatalog Cat => Book.Templates;
        public Animal Beast(string breed = "horse_draught", int trained = 900) => Book.Animals.Create(Book.Breeds.Get(breed), EntityId.None, trained);
        public VehicleRow Make(string template, params Animal[] team)
        {
            var v = Book.Vehicles.Create(Book.Templates.Get(template), EntityId.None);
            foreach (var a in team) Assert.Equal(HitchResult.Ok, Book.Vehicles.Hitch(v, a));
            return v;
        }
        public void Fill(VehicleRow v, string good, long grams) => Assert.True(v.Cargo.Create(Goods.IdOf(good), grams, CreateReason.Other));
        public VehicleDesigner Designer() => new(Book.Templates, Book.Breeds);
    }

    /// <summary>Rebuilds a content template in the designer cell by cell, stack by stack in order (each part placed only where what it needs is under it).</summary>
    internal static VehicleDesigner Rebuild(Rig r, string template, string? swapAxle = null)
    {
        var d = r.Cat.Get(template).Def;
        var des = r.Designer(); des.New(d.Rows[0].Length, d.Rows.Count);
        for (int y = 0; y < d.Rows.Count; y++)
            for (int x = 0; x < d.Rows[y].Length; x++)
                if (d.Legend.TryGetValue(d.Rows[y][x].ToString(), out var ids))
                    foreach (var id in ids) Assert.Equal(PlaceResult.Ok, des.Place(x, y, swapAxle != null && id == "axle_wood" && y == d.Rows.Count - 5 ? swapAxle : id));
        return des;
    }

    // ------------------------------------------------------------------ VD-1: content

    [Fact]
    public void VD1_FiftyParts_FifteenTemplatesValidate_EveryPartRecipeResolves_ThePhase3StatsUnchanged()
    {
        var r = new Rig(); var cat = r.Cat;
        Assert.Empty(cat.Errors);
        Assert.Equal(71, cat.PartList.Count);                                                   // "about 40": the 19 of Phase 3, 22 new, the 9 sea parts of P7-SH-A the 16 siege parts of P7-SG-B and the 5 war parts of P7-SG-E
        Assert.Equal(22, cat.All.Count);                                                          // + longship, cog, war cog (P7-SH-A); mantlet, covered ram, siege tower, trebuchet, springald (P7-SG-B)
        var recipes = Ravensguard.Sim.Crafting.RecipeDb.Build(EconFixture.Content, r.Goods);
        foreach (var p in cat.PartList)
        {
            Assert.False(string.IsNullOrEmpty(p.Recipe), p.Id);
            var rec = recipes[p.Recipe];
            Assert.Contains(rec.Results, x => x.Kind == Ravensguard.Sim.Crafting.ResultKind.VehiclePart && x.Shape == p.Id);
            if (p.Repair.Length > 0) Assert.Equal("repair_cart", recipes[p.Repair].Effect);
            Assert.False(string.IsNullOrEmpty(p.Category), p.Id);
            Assert.Contains(p.Category, VehicleDesigner.Categories);
        }
        // H-13 still exact
        var cart = cat.Get("cart").Stats; var ox = cat.Get("ox_wagon").Stats;
        Assert.Equal((500, 1000, 165, 1, 700, 40), (cart.CargoKg, cart.CargoL, cart.MassKg, cart.HitchSlots, cart.AxleLimitKg, cart.TurningRadiusDm));
        Assert.Equal((1000, 2000, 300, 2, 1500, 80), (ox.CargoKg, ox.CargoL, ox.MassKg, ox.HitchSlots, ox.AxleLimitKg, ox.TurningRadiusDm));
        foreach (var b in cat.All)
        {
            output.WriteLine($"{b.Id}: {b.Stats.MassKg} kg, {b.Stats.CargoKg} kg / {b.Stats.CargoL} L, radius {b.Stats.TurningRadiusDm} dm, steer {b.Stats.Steer}, boat {b.Stats.Boat}" +
                (b.Stats.Boat ? $" A_wp {b.Stats.WaterplaneMm2 / 1000} cm2x10, rowers {b.Stats.Rowers}, polers {b.Stats.Polers}" : "") + $" warnings [{string.Join(", ", b.Warnings.Select(w => w.Code))}]");
            Assert.DoesNotContain(b.Warnings, w => w.IsError);
        }
        Assert.True(cat.Get("barge").Stats.Boat && cat.Get("barge").Stats.TowPost && cat.Get("barge").Stats.Polers == 4);
        Assert.Equal(2, cat.Get("rowing_boat").Stats.Rowers);
        Assert.True(cat.Get("fishing_boat").Stats.FishWell && cat.Get("ferry").Stats.Ramps && cat.Get("peddler_wagon").Stats.Shop);
    }

    [Fact]
    public void EveryPart_HasItsFourDrawnViewsAndFourBakedDiagonals_InTheGamesArtAtlas()
    {
        string? dir = AppContext.BaseDirectory; while (dir != null && !File.Exists(Path.Combine(dir, "Ravensguard.slnx"))) dir = Path.GetDirectoryName(dir);
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(dir!, "src", "Ravensguard.Game", "assets", "art", "index.json")));
        var sprites = doc.RootElement.GetProperty("sprites");
        foreach (var p in new Rig().Cat.PartList.Where(x => !SeaPartIds.Contains(x.Id) && !x.Tags.Contains("siege") && !x.Tags.Contains("war")))                // the sea parts' and the siege parts' art is a later part (P7-SH-A and P7-SG-B left it out)
            foreach (var v in new[] { "e", "n", "w", "s", "ne", "se", "sw", "nw" })
            {
                Assert.True(sprites.TryGetProperty($"vpart.{p.Id}.{v}", out var sp), $"vpart.{p.Id}.{v}");
                Assert.Equal(v.Length == 1 ? 32 : 46, sp.GetProperty("w").GetInt32());
            }
    }

    // ------------------------------------------------------------------ VD-2, VD-3: designer stats = template

    [Fact]
    public void VD2_TheDesignerRebuildsTheCartAndTheOxWagon_CellByCell_ToTheTemplatesNumbers()
    {
        var r = new Rig();
        var cart = Rebuild(r, "cart").Stats();
        Assert.True(cart.CanBuild, string.Join(", ", cart.Issues.Select(i => i.Code)));
        Assert.Equal((165, 500, 1000, 40), (cart.Stats.MassKg, cart.Stats.CargoKg, cart.Stats.CargoL, cart.Stats.TurningRadiusDm));
        var ox = Rebuild(r, "ox_wagon").Stats();
        Assert.True(ox.CanBuild, string.Join(", ", ox.Issues.Select(i => i.Code)));
        Assert.Equal((300, 1000, 2000, 1500, 80), (ox.Stats.MassKg, ox.Stats.CargoKg, ox.Stats.CargoL, ox.Stats.AxleLimitKg, ox.Stats.TurningRadiusDm));
        // the pane: the pull needed fully loaded at full speed (ratio 600) and the team that gives it
        output.WriteLine($"ox wagon: pull {ox.PullRoadN} N on dry road ({ox.AnimalsRoad} {ox.Beast}), {ox.PullMudN} N in mud ({ox.AnimalsMud}); speeds {string.Join(", ", ox.Speeds.Select(s => $"{s.Surface} {s.SpeedCmps}"))}");
        Assert.Equal("ox", ox.Beast);
        Assert.Equal(2, ox.AnimalsRoad);                                                        // "needs 2 oxen on dry road ..."
        Assert.Equal(6, ox.AnimalsMud);                                                         // "... 6 in mud"
        Assert.Equal(6, ox.Speeds.Count);
        // the text round-trips through the mod loader and builds the same numbers
        var des = Rebuild(r, "ox_wagon"); des.Id = "player.my_ox_wagon"; des.Name = "My ox wagon";
        var text = des.Text();
        var def = VehicleCatalog.ParseDesign(text, "player.my_ox_wagon", out var why);
        Assert.NotNull(def); Assert.Equal("", why);
        var errs = new List<string>(); var bp = r.Cat.Register(def!, errs);
        Assert.Empty(errs); Assert.NotNull(bp);
        Assert.Equal((300, 1000, 2000, 1500, 80), (bp!.Stats.MassKg, bp.Stats.CargoKg, bp.Stats.CargoL, bp.Stats.AxleLimitKg, bp.Stats.TurningRadiusDm));
        Assert.Equal(text, VehicleDesigner.WriteText(def!));
        Assert.Equal(23, r.Cat.All.Count);
    }

    [Fact]
    public void VD3_APivotingFrontAxle_TurnsTheOxWagonIn48Metres()
    {
        var r = new Rig();
        var des = Rebuild(r, "ox_wagon", swapAxle: "axle_pivot");
        var st = des.Stats();
        Assert.True(st.CanBuild, string.Join(", ", st.Issues.Select(i => i.Code)));
        Assert.True(st.Stats.PivotAxle);
        Assert.Equal(48, st.Stats.TurningRadiusDm);
        Assert.Equal(305, st.Stats.MassKg);                                                     // the pivoting axle is 5 kg heavier
    }

    // ------------------------------------------------------------------ VD-5: warnings

    [Fact]
    public void VD5_Warnings_NoSteering_AnUnpairedWheel_AHullGap_GmBelowZero_EachNamed_TheBuildRefused()
    {
        var r = new Rig();
        // a four-wheeler with traces and no pole: no steering
        var a = r.Designer(); a.New(2, 5);
        foreach (var (x, y) in new[] { (0, 1), (0, 3) }) a.PlaceStack(x, y, ["axle_wood", "wheel_spoked", "frame_plank", "cargo_bed"]);
        foreach (var (x, y) in new[] { (1, 1), (1, 3) }) a.PlaceStack(x, y, ["wheel_spoked", "frame_plank", "cargo_bed"]);
        a.PlaceStack(0, 0, ["frame_plank"]); a.PlaceStack(1, 0, ["frame_plank"]); a.PlaceStack(0, 2, ["frame_plank"]); a.PlaceStack(1, 2, ["frame_plank"]);
        a.PlaceStack(0, 4, ["traces"]); a.PlaceStack(1, 4, ["traces"]);
        var sa = a.Stats();
        Assert.Contains(sa.Issues, i => i.Code == "no_steering" && i.IsError);
        Assert.False(sa.CanBuild);
        // an unpaired wheel
        Assert.True(a.RemoveTop(1, 3) && a.RemoveTop(1, 3) && a.RemoveTop(1, 3));
        Assert.Contains(a.Stats().Issues, i => i.Code == "unpaired_wheel");
        // undo brings the wheel back
        Assert.True(a.Undo() && a.Undo() && a.Undo());
        Assert.DoesNotContain(a.Stats().Issues, i => i.Code == "unpaired_wheel");
        Assert.True(a.Redo());
        // placing on nothing: a cargo bed needs a frame under it
        Assert.Equal(PlaceResult.NeedsBelow, a.Place(1, 4, "cargo_bed"));
        // a hull with a gap
        var b = r.Designer(); b.Load(r.Cat.Get("rowing_boat").Def); b.Id = "player.leaky";
        while (b.Stack(1, 2).Count > 0) b.RemoveTop(1, 2);
        var sb = b.Stats();
        Assert.Contains(sb.Issues, i => i.Code == "hull_gap" && i.X == 1 && i.Y == 2);
        Assert.False(sb.CanBuild);
        // a boat that capsizes: a narrow hull with a mast and a sail high up (GM <= 0)
        var c = r.Designer(); c.New(1, 4);
        for (int y = 0; y < 4; y++) c.PlaceStack(0, y, ["keel", "hull_plank", "gunwale"]);
        c.PlaceStack(0, 1, ["thwart", "rowlock"]); c.PlaceStack(0, 2, ["thwart", "rowlock"]); c.PlaceStack(0, 3, ["mast", "sail_square"]); c.PlaceStack(0, 0, ["mast", "sail_square"]);
        var sc = c.Stats();
        output.WriteLine($"narrow boat: GM full {sc.Full.GmMm} mm, empty {sc.Empty.GmMm} mm; issues {string.Join(", ", sc.Issues.Select(i => i.Code))}");
        Assert.Contains(sc.Issues, i => i.Code == "capsizes" && i.IsError);
        Assert.False(sc.CanBuild);
    }

    // ------------------------------------------------------------------ VD-6, VD-7: wear and breakage

    [Fact]
    public void VD6_WearParity_APhase3Cart_10KmOfRoadAt95PercentAxleLoad_EachWheelEqualsWheelPmTimesHp()
    {
        var r = new Rig(); var c = r.Make("cart", r.Beast("horse_palfrey")); r.Fill(c, "oats", 500_000);   // 665 kg of 700: 95 percent
        r.Book.Vehicles.Wear(c, 10_000_000);
        // Phase 3: 1 permille a 20 m at full load, x0.95: 475 permille gone
        int phase3Pm = 1000 - 475;
        foreach (var p in c.Parts)
        {
            var d = r.Cat.PartList[p.Part]; if (!d.Tags.Contains("wheel")) continue;
            Assert.InRange(p.HpMilli / 1000, phase3Pm * d.Hp / 1000 - 1, phase3Pm * d.Hp / 1000 + 1);
            Assert.Equal(63_000, p.HpMilli);
        }
        Assert.Equal(525, c.WheelPm);
        // a Fine wheel wears x0.8; off the road twice as fast
        var f = r.Make("cart", r.Beast("horse_palfrey")); r.Fill(f, "oats", 500_000);
        for (int i = 0; i < f.Parts.Count; i++) { var p = f.Parts[i]; if (r.Cat.PartList[p.Part].Tags.Contains("wheel")) { p.Tier = ItemQuality.Fine; f.Parts[i] = p; } }
        r.Book.Vehicles.Wear(f, 10_000_000);
        Assert.Equal(1000 - 475 * 8 / 10, f.WheelPm);
        var g = r.Make("cart", r.Beast("horse_palfrey")); r.Fill(g, "oats", 500_000);
        r.Book.Vehicles.Wear(g, 5_000_000, VehicleTable.RoughPm(Surface.Grass));
        Assert.Equal(525, g.WheelPm);
    }

    [Fact]
    public void VD7_AWheelWornToNothingOnRock_StopsTheCart_PushingDoesNotFreeIt_ANewWheelFitted_ItMoves()
    {
        var r = new Rig(); var c = r.Make("cart", r.Beast()); r.Fill(c, "oats", 500_000);
        r.Book.Vehicles.Wear(c, 6_000_000, VehicleTable.RoughPm(Surface.Sand));                // 4,000 permille ground: 6 km wear like 24 km of road
        Assert.True(c.Broken);
        Assert.Contains(c.Parts, p => p.State == PartState.Broken && r.Cat.PartList[p.Part].Id == "wheel_spoked");
        r.Book.Driving = c.Id; c.Driven = true;
        Assert.True(r.Book.Plan(false, false, 0).Blocked);
        c.Stuck = true; Assert.False(r.Book.Push(6, 0));                                      // Stuck actions do not free a broken wheel
        // the broken wheel replaced: the old part out, a new one fitted (what VehicleSites.Install does with a crafted wheel)
        int wi = c.Parts.FindIndex(p => p.State == PartState.Broken);
        var w = c.Parts[wi]; w.State = PartState.Built; w.HpMilli = r.Cat.PartList[w.Part].Hp * 1000; c.Parts[wi] = w;
        for (int i = 0; i < c.Parts.Count; i++) if (c.Parts[i].State == PartState.Broken) { var p = c.Parts[i]; p.State = PartState.Built; p.HpMilli = 1000 * r.Cat.PartList[p.Part].Hp; c.Parts[i] = p; }
        c.Touch(); c.Stuck = false;
        Assert.False(c.Broken);
        Assert.False(r.Book.Plan(false, false, 0).Blocked);
    }

    // ------------------------------------------------------------------ VD-8 to VD-12: boats

    [Fact]
    public void VD8_RowingBoatDraft_AWp5_Draft196_Freeboard254_MaxLoadToFreeboard100Is875()
    {
        long awp = BoatMath.WaterplaneOf(10, 500);
        Assert.Equal(5_000_000, awp);
        var l = BoatMath.Float(awp, 500, 450, 2, 490, 0);
        Assert.Equal((196, 254), (l.DraftMm, l.FreeboardMm));
        Assert.Equal(875, BoatMath.MaxMassKg(awp, 500, 450, 100));
    }

    [Fact]
    public void VD9_RowingBoatStability_Kb98_Bm1667_Kg330_Gm1435_SixStandingPeopleMakeItTender()
    {
        int kg = BoatMath.KgMm(140, 140L * 225, 200, 200, 150, 0);
        var l = BoatMath.Float(BoatMath.WaterplaneOf(10, 500), 500, 450, 2, 490, kg);
        Assert.Equal((98, 1667, 330, 1435), (l.KbMm, l.BmMm, l.KgMm, l.GmMm));
        Assert.Equal(BoatStability.Steady, l.Stability(150));
        // the same boat with six people standing in it as well: higher and heavier
        int kg6 = BoatMath.KgMm(140, 140L * 225, 200, 200, 150, 6 * 75);
        var l6 = BoatMath.Float(BoatMath.WaterplaneOf(10, 500), 500, 450, 2, 490 + 450, kg6);
        output.WriteLine($"six standing: draft {l6.DraftMm}, KB {l6.KbMm}, BM {l6.BmMm}, KG {l6.KgMm}, GM {l6.GmMm}, freeboard {l6.FreeboardMm}");
        Assert.Equal(l6.KbMm + l6.BmMm - l6.KgMm, l6.GmMm);
        Assert.Equal(l6.GmMm < 150 ? BoatStability.Tender : BoatStability.Steady, l6.Stability(150));
        // a narrow boat of one cell's beam with six standing is tender
        var thin = BoatMath.Float(BoatMath.WaterplaneOf(5, 500), 500, 450, 1, 590, BoatMath.KgMm(70, 70L * 225, 0, 200, 0, 6 * 75 - 30));
        Assert.True(thin.GmMm < 150, thin.GmMm.ToString());
        Assert.NotEqual(BoatStability.Steady, thin.Stability(150));
    }

    [Fact]
    public void VD10_TwoRowers_140N_Awet774_150CentimetresASecond_Downstream230_Upstream70()
    {
        var wt = WaterTuning.Default;
        long awp = BoatMath.WaterplaneOf(10, 500);
        long wet = BoatMath.WetMm2(awp, 14, 196);
        Assert.Equal(7_744_000, wet);
        long f = BoatMath.PowerN(wt, 2, 0, 900, 0, 0);
        Assert.Equal(140, f);
        int v = BoatMath.WaterSpeedMmps(f, wt.DragK, wet);
        Assert.InRange(v / 10, 149, 151);
        int cur = wt.River(2000, 0).CurrentMmps;                                               // a river: 0.8 m/s
        Assert.Equal(800, cur);
        Assert.InRange(BoatMath.GroundMmps(v, cur, true) / 10, 229, 231);
        Assert.InRange(BoatMath.GroundMmps(v, cur, false) / 10, 69, 71);
        Assert.Equal(cur, BoatMath.GroundMmps(0, cur, true));                                   // no power: it drifts at the current
    }

    [Fact]
    public void VD11_ABargeTowedByOneDraughtHorse_Draft131And654_Speed179And149()
    {
        var wt = WaterTuning.Default;
        long awp = BoatMath.WaterplaneOf(30, 900);
        int e = BoatMath.DraftMm(3000, awp, 850), l = BoatMath.DraftMm(15000, awp, 850);
        Assert.Equal((131, 654), (e, l));
        var r = new Rig(); int horse = r.Book.Breeds.Get("horse_draught").PullN; Assert.Equal(780, horse);
        int ve = BoatMath.WaterSpeedMmps(BoatMath.PowerN(wt, 0, 0, 1800, horse, 0), wt.DragK, BoatMath.WetMm2(awp, 26, e));
        int vl = BoatMath.WaterSpeedMmps(BoatMath.PowerN(wt, 0, 0, 1800, horse, 0), wt.DragK, BoatMath.WetMm2(awp, 26, l));
        Assert.Equal((179, 149), ((ve + 5) / 10, (vl + 5) / 10));                               // 1,790 and 1,488 mm/s: 1.79 and 1.49 m/s
        // the template barge: its own hull is that hull
        var b = r.Cat.Get("barge").Stats;
        Assert.Equal((30, 3, 10, 26, 900, 850), (b.HullCells, b.BeamCells, b.HullLengthCells, b.PerimeterM, b.WaterplanePm, b.BlockPm));
        Assert.Equal(awp, b.WaterplaneMm2);
    }

    [Fact]
    public void VD12_Aground_ALoadedBargeEntersAStream_NotAFord_UntilTwoRiverHighDaysDeepenIt()
    {
        var wt = WaterTuning.Default;
        Assert.Equal(900, wt.River(500, 0).DepthMm);                                          // a 5 m stream
        Assert.True(BoatMath.Floats(654, 900, wt));                                             // 654 + 150 = 804 <= 900
        Assert.Equal(500, wt.FordDepth(0));
        Assert.False(BoatMath.Floats(654, wt.FordDepth(0), wt));
        Assert.Equal(1100, wt.FordDepth(2));
        Assert.True(BoatMath.Floats(654, wt.FordDepth(2), wt));
        Assert.Equal(1400, wt.FordDepth(9));                                                    // three days at most
        // the live boat runs aground in the ford and floats in the stream
        var r = new Rig(); var barge = r.Make("barge", r.Beast());
        r.Fill(barge, "wheat", 9_000_000);
        r.Book.Driving = barge.Id; barge.Driven = true;
        r.Book.WaterNow = () => new BoatWater(wt.FordDepth(0), 0, 0, 0, 0, false, 0);
        var s = r.Book.Plan(false, false, 0);
        Assert.True(s.Blocked && s.Stuck); Assert.Equal(WaterState.Aground, barge.Water);
        r.Book.WaterNow = () => new BoatWater(wt.FordDepth(2), 0, 0, 0, 0, false, 2);
        Assert.False(r.Book.Plan(false, false, 0).Blocked); Assert.Equal(WaterState.Afloat, barge.Water);
    }

    // ------------------------------------------------------------------ VD-13, VD-14: capsize and swamping

    [Fact]
    public void VD13_ATenderBoatInAnElevenMetreCrossWind_RollsThirtyTimesInTenMinutes_SameSeedSameResult_ASteadyBoatNever()
    {
        var wt = WaterTuning.Default; var boat = EntityId.MakeIn(IdKind.Other, 0, 0x720_0001);
        int rolls = 0, capsized = 0; long first = -1;
        for (long t = 0; t < 600; t += 20) { rolls++; if (BoatMath.CapsizeCheck(Seed, boat, 3600 + t, 100, 11, wt)) { capsized++; if (first < 0) first = t; } }
        Assert.Equal(30, rolls);
        int again = 0; for (long t = 0; t < 600; t += 20) if (BoatMath.CapsizeCheck(Seed, boat, 3600 + t, 100, 11, wt)) again++;
        Assert.Equal(capsized, again);
        // the rate is 20 pm a roll: over many boats about 2 percent of the rolls
        int hits = 0; for (int b = 0; b < 500; b++) for (long t = 0; t < 600; t += 20) if (BoatMath.CapsizeCheck(Seed, EntityId.MakeIn(IdKind.Other, 0, 0x720_0000 + (ulong)b), t, 100, 11, wt)) hits++;
        Assert.InRange(hits, 15_000 * 12 / 1000, 15_000 * 28 / 1000);
        for (long t = 0; t < 600; t += 20) Assert.False(BoatMath.CapsizeCheck(Seed, boat, t, 200, 11, wt));           // GM 200: steady
        for (long t = 0; t < 600; t += 20) Assert.False(BoatMath.CapsizeCheck(Seed, boat, t, 100, 9, wt));            // under 10 m/s: no beam sea
        Assert.True(BoatMath.CapsizeCheck(Seed, boat, 0, 0, 0, wt));                                                   // GM 0: at once
    }

    [Fact]
    public void VD14_AnOverloadedRowingBoatSwamps_SinksAndItsCargoGoesToTheRiverBed_MassExact()
    {
        var r = new Rig(); var wt = r.Cat.Water;
        var boat = r.Make("rowing_boat");
        var st = r.Book.Vehicles.StatsOf(boat);
        // loaded to the brim (400 kg of grain), and then people climbing in until the freeboard is -10 mm
        ushort stone = r.Goods.IdOf("wheat");
        long before = r.Ledger.Net(stone);
        Assert.True(boat.Cargo.Create(stone, st.CargoKg * 1000L, CreateReason.Other));
        r.Book.RiderKg = BoatMath.MaxMassKg(st.WaterplaneMm2, st.BlockPm, st.HullDepthMm, -10) - st.MassKg - st.CargoKg - st.Rowers * BoatMath.PersonKg + 1;
        int people = r.Book.RiderKg + st.Rowers * BoatMath.PersonKg;
        var load = BoatMath.Load(st, r.Book.Vehicles.CargoKg(boat), people, wt);
        output.WriteLine($"rowing boat {st.MassKg} kg, cargo {r.Book.Vehicles.CargoKg(boat)} kg: draft {load.DraftMm}, freeboard {load.FreeboardMm}");
        Assert.True(load.FreeboardMm <= 0, load.FreeboardMm.ToString());
        long cargo = boat.Cargo.TotalMassG();
        r.Book.Driving = boat.Id; boat.Driven = true;
        r.Book.WaterNow = () => new BoatWater(1800, 0, 0, 0, 0, false, 0);
        r.Book.Account(r.Book.Plan(false, false, 20), 0, 20, false);
        Assert.Equal(WaterState.Sunk, boat.Water);
        Assert.False(r.Book.IsDriving);
        Assert.Equal(0, boat.Cargo.TotalMassG());
        Assert.Equal(cargo, r.Book.RiverBed.TotalMassG());
        Assert.Equal(r.Ledger.Net(stone), r.Book.RiverBed.Qty(stone) + before);                   // the ledger counts every gram where it lies
    }

    // ------------------------------------------------------------------ VD-15, VD-16: fishing boats and ferries

    [Fact]
    public void VD15_AFishingBoatWithACrewOfTwo_24KgADayInSummer_96InWinter()
    {
        var wt = WaterTuning.Default;
        Assert.Equal(24_000, BoatMath.FishGramsPerDay(2, false, wt));
        Assert.Equal(9_600, BoatMath.FishGramsPerDay(2, true, wt));
        Assert.Equal(4, wt.FishShoreKgDay);
        // the catch comes out of the lake's stock and into the boat's well (Fishing.Day)
        var r = new Rig(); var boat = r.Make("fishing_boat"); ushort fish = r.Goods.IdOf("fish_fresh");
        long stock = 500_000; long got = Fishing.Day(boat, r.Book.Vehicles, fish, 2, false, ref stock, wt);
        Assert.Equal(24_000, got); Assert.Equal(476_000, stock); Assert.Equal(24_000, boat.Cargo.Qty(fish));
        long low = 10_000; Assert.Equal(10_000, Fishing.Day(boat, r.Book.Vehicles, fish, 2, false, ref low, wt)); Assert.Equal(0, low);
    }

    [Fact]
    public void VD16_ACartCrossesA30MetreRiverByFerry_60SecondsAt05_40MitesToTheFerryman_TheRampsArePortals()
    {
        var wt = WaterTuning.Default;
        Assert.Equal(60, BoatMath.FerrySeconds(30_000, wt));
        Assert.Equal(40, BoatMath.FerryFee(0, 0, 1, wt));
        Assert.Equal(40 + 15 + 15, BoatMath.FerryFee(1, 1, 1, wt));
        var econ = EconFixture.NewEconomy(false);
        var hero = EntityId.Make(IdKind.Person, 1); var man = EntityId.Make(IdKind.Person, 2);
        var hp = econ.Ledger.Open(Ravensguard.Sim.Economy.PurseKind.Person, hero); var mp = econ.Ledger.Open(Ravensguard.Sim.Economy.PurseKind.Person, man);
        econ.Ledger.Mint(hp, new Ravensguard.Sim.Economy.Coin(100), Ravensguard.Sim.Economy.SourceReason.Seed);
        long total = econ.Ledger.Audit().InPurses.Mites;
        var x = Ferry.Cross(econ.Ledger, hp, mp, 30_000, people: 0, animals: 0, carts: 1, wt);
        Assert.True(x.Paid); Assert.Equal(60, x.Seconds); Assert.Equal(40, x.Fee);
        Assert.Equal(60, econ.Ledger.Balance(hp).Mites); Assert.Equal(40, econ.Ledger.Balance(mp).Mites);
        Assert.Equal(total, econ.Ledger.Audit().InPurses.Mites);                                // coin conserved
        var r = new Rig(); var ferry = r.Cat.Get("ferry");
        var ramps = Ferry.Ramps(ferry, r.Cat);
        Assert.Equal(6, ramps.Count);                                                          // both ends: the cells a cart drives on and off over
        Assert.All(ramps, c => Assert.True(c.Y == 0 || c.Y == ferry.Stats.LengthCells - 1));
    }

    // ------------------------------------------------------------------ 4.4: NPC drivers, fords

    [Fact]
    public void NpcDrivers_KeepFivePercentBelowTheStuckLine_AvoidMud_AndFordsCloseToWagonsFirst()
    {
        var tu = VehicleTuning.Default;
        Assert.Equal(650, NpcDriving.MaxRatioPm);
        Assert.Equal(3000, NpcDriving.RouteCostPm(tu, Surface.MudRoad, false));
        Assert.Equal(1000, NpcDriving.RouteCostPm(tu, Surface.DryRoad, false));
        Assert.True(NpcDriving.WillDrive(1200, tu, Surface.DryRoad, false));                     // no bog on a dry road
        Assert.True(NpcDriving.WillDrive(640, tu, Surface.MudRoad, false));
        Assert.False(NpcDriving.WillDrive(690, tu, Surface.MudRoad, false));                     // the hero would still try at 690 (the stuck rolls start above 700)
        Assert.Equal(0, Stuck.StuckPm(690, Surface.MudRoad));
        // fords close to wagons first (Phase 3 4.7), and to loaded barges too (vehicle-designer 4.5)
        Assert.False(Fords.IsOpen(Crosser.Wagon, 1)); Assert.True(Fords.IsOpen(Crosser.Rider, 1)); Assert.True(Fords.IsOpen(Crosser.Walker, 2));
        Assert.False(BoatMath.Floats(654, WaterTuning.Default.FordDepth(0), WaterTuning.Default));
    }

    // ------------------------------------------------------------------ VD-20: determinism

    [Fact]
    public void VD20_TheVehicleHashOfTheBoatGoldens_IsPinned()
    {
        var h = VehicleHash.Of(Seed);
        output.WriteLine("vehicle hash " + h);
        Assert.Equal(VehicleHash.Of(Seed), h);
        Assert.Equal(GoldenVehicleHash, h);
    }

    /// <summary>The pinned value (the same on Linux and Windows: integer maths only).</summary>
    internal const string GoldenVehicleHash = "F3D46C98BA51FA09";
}
