using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Naval;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.WorldGen.Region;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Naval;

/// <summary>P7-SH-B (ships-and-naval-war 10): SH-2 points of sail, SH-3 oars, SH-4 the same speed live and abstract, SH-5 sea state, SH-6 lee shore, SH-7 harbour, SH-8 ferry and fishing; with beaches, masters and the content row.</summary>
public class SailingTests(ITestOutputHelper output)
{
    private static readonly VehicleCatalog Cat = VehicleCatalog.From(EconFixture.Content);
    private static readonly SeaRules Rules = SeaRules.From(EconFixture.Content);
    private static readonly WaterTuning Wt = Cat.Water;
    private static readonly EntityId CogId = EntityId.Make(IdKind.Other, 11), LongId = EntityId.Make(IdKind.Other, 12);

    private static SeaShip Cog(int cargoKg = 100_000) => SeaShip.Of(CogId, Cat.Get("cog"), cargoKg, 20, Rules, Wt);
    private static SeaShip Longship() => SeaShip.Of(LongId, Cat.Get("longship"), 4_000, 64, Rules, Wt);
    private static int Deg(int d) => FrameMath.FromDegrees(d);

    [Fact]
    public void TheContentRowIsLoaded()
    {
        Assert.Contains(EconFixture.Content.All<Ravensguard.Content.SeaRulesDef>(), d => d.Id == "default");
        Assert.Equal(45, Rules.D.BraceDeg); Assert.Equal(1250, Rules.D.HullSpeedPm);
    }

    // ------------------------------------------------------------------ SH-2

    [Fact]
    public void SH2_PointsOfSail_AStern_BeamReach_CloseHauled_AndHullSpeedCaps()
    {
        var cog = Cog(); var crew = new SeaCrewState();
        const int wind = 10;
        // heading east (0); the wind blows toward 0 (from astern), toward 90 (from the north: a beam wind), toward 180 (a headwind), 135 off astern (close-hauled)
        var astern = Sailing.Read(cog, crew, 0, 0, wind);
        var beam = Sailing.Read(cog, crew, 0, Deg(90), wind);
        var close = Sailing.Read(cog, crew, 0, Deg(135), wind);
        var head = Sailing.Read(cog, crew, 0, Deg(180), wind);
        output.WriteLine($"cog: astern {astern.SailN} N {astern.SpeedMmps} mm/s; beam {beam.SailN} N {beam.SpeedMmps} mm/s lee {beam.LeewayMmps}; close {close.SailN} N; hull {cog.HullSpeedMmps}");
        Assert.Equal(BoatMath.SailN(160, wind, 1000), astern.SailN);                           // 0.6 x 160 x 100 x cos 0 = 9,600 N
        Assert.Equal(9_600, astern.SailN);
        Assert.Equal(BoatMath.SailN(160, wind, 707), beam.SailN);                              // braced 45 degrees: cos 45
        Assert.InRange(beam.SailN, 6_700, 6_800);
        Assert.Equal(0, close.SailN); Assert.Equal(0, head.SailN);                              // nothing forward of the beam
        Assert.True(astern.SpeedMmps > beam.SpeedMmps && beam.SpeedMmps > 0);
        Assert.Equal(0, close.SpeedMmps);                                                      // it drifts
        Assert.InRange(astern.SpeedMmps, 2_000, cog.HullSpeedMmps);                            // cruising 2.5 to 4 m/s
        Assert.Equal(0, astern.LeewayMmps);                                                    // running free: no slip
        Assert.Equal(beam.SpeedMmps * 150 / 1000, beam.LeewayMmps);                            // a beam reach slips 150 pm of the speed
        // hull speed: 1.25 x sqrt(22 m) = 5.86 m/s; the longship 24 m: 6.12
        Assert.InRange(cog.HullSpeedMmps, 5_850, 5_870); Assert.InRange(Longship().HullSpeedMmps, 6_100, 6_130);
        var gale = Sailing.Read(cog, crew, 0, 0, 40);
        Assert.True(gale.HullCapped); Assert.Equal(cog.HullSpeedMmps, gale.SpeedMmps);
        Assert.Equal(250, Longship().LeewayPm); Assert.Equal(150, cog.LeewayPm);               // a shallow keel slips more
    }

    [Fact]
    public void SH2_Steering_RudderRateScalesWithSpeed_NoneAtRest_OarsTurnALongshipOnTheSpot()
    {
        var cog = Cog(); var lg = Longship(); var rest = new SeaCrewState(); var oars = new SeaCrewState { OarsOut = true };
        int perSec(int q) => (int)((long)q * 20 * 360 / FrameMath.Turn);                       // degrees a second (rounded down)
        Assert.InRange(perSec(Sailing.TurnPerTick(cog, rest, 3000, 1)), 2, 3);                  // 3 deg/s at 3 m/s
        Assert.InRange(perSec(Sailing.TurnPerTick(cog, rest, 1500, 1)), 1, 2);                  // half at half the speed
        Assert.Equal(0, Sailing.TurnPerTick(cog, rest, 0, 1));                                 // none at 0 speed
        Assert.InRange(perSec(Sailing.TurnPerTick(lg, oars, 3000, -1)), -6, -5);               // 6 deg/s under oars, to port
        Assert.InRange(perSec(Sailing.TurnPerTick(lg, oars, 0, 1)), 9, 10);                    // on the spot at 10
        Assert.Equal(0, Sailing.TurnPerTick(lg, rest, 0, 1));                                  // oars shipped: nothing to turn her
    }

    // ------------------------------------------------------------------ SH-3

    [Fact]
    public void SH3_ALongshipOf32RowersInACalm_AboutTwoAndAHalfMetresASecond_MakesWayAgainstA6MetreHeadwind_AndTires()
    {
        var lg = Longship(); var crew = new SeaCrewState { OarsOut = true };
        var calm = Sailing.Read(lg, crew, 0, 0, 0);
        var head = Sailing.Read(lg, crew, 0, Deg(180), 6);
        output.WriteLine($"longship: calm {calm.SpeedMmps} mm/s ({calm.OarN} N), 6 m/s headwind {head.SpeedMmps} mm/s (windage {head.WindageN} N)");
        Assert.InRange(calm.SpeedMmps, 2_000, 3_000);
        Assert.True(head.SpeedMmps > 0 && head.SpeedMmps < calm.SpeedMmps);
        Assert.Equal(32L * Wt.RowerN, calm.OarN);
        // a hard stroke is x3 for a minute and tires: after 10 minutes of it the crew has little left
        var hard = new SeaCrewState { OarsOut = true, Hard = true };
        long fresh = Sailing.OarForceN(lg, hard);
        hard.Tire(Rules, 60); long after1 = Sailing.OarForceN(lg, hard);
        hard.Tire(Rules, 600); long after11 = Sailing.OarForceN(lg, hard);
        Assert.Equal(3 * calm.OarN, fresh); Assert.True(after1 < fresh && after11 < after1);
        Assert.True(after11 >= 3 * calm.OarN * 400 / 1000 - 1);                                 // never under the floor
        hard.OarsOut = false; hard.Tire(Rules, 300); Assert.True(hard.StaminaPm > 0);            // resting brings it back
        var easy = new SeaCrewState { OarsOut = true }; easy.Tire(Rules, 600); Assert.Equal(900, easy.StaminaPm);
    }

    // ------------------------------------------------------------------ SH-4

    [Fact]
    public void SH4_A20KmSeaLeg_TakesTheSameSecondsLiveAndAbstract()
    {
        var cog = Cog(); var crew = new SeaCrewState();
        const int wind = 10; const long leg = 20_000_000;
        var r = Sailing.Read(cog, crew, 0, 0, wind);
        long want = SeaLegs.Seconds(leg, r.SpeedMmps);
        // live: 20 Hz ticks of the frame at the same rules, until it has covered the leg
        var f = ShipFrame.Of(CogId, Cat.Get("cog"), Cat, 0, 0, 0); var slip = new SlipAcc();
        long ticks = 0;
        while (f.XMm < leg && ticks < want * 2) { Sailing.Tick(cog, crew, f, 0, wind, 0, ref slip); ticks++; }
        output.WriteLine($"20 km at {r.SpeedMmps} mm/s: abstract {want} s, live {ticks} ticks, x {f.XMm}");
        Assert.Equal(want, ticks);
        // abstract hourly: the hour's distance is the live distance of 3,600 ticks
        var g = ShipFrame.Of(CogId, Cat.Get("cog"), Cat, 0, 0, 0); var s2 = new SlipAcc();
        for (int i = 0; i < 3600; i++) Sailing.Tick(cog, crew, g, 0, wind, 0, ref s2);
        Assert.InRange(SeaLegs.HourMm(r.SpeedMmps) - g.XMm, 0, 1);
    }

    // ------------------------------------------------------------------ SH-5

    [Fact]
    public void SH5_SeaState_10MpsOver50KmOfFetchIs2MetreWaves_ATenderCogCapsizesAt20PmACheck_SwampingAndBailing()
    {
        Assert.Equal(2_000, SeaState.WaveMm(Rules, 10, 50));
        Assert.Equal(2_000, SeaState.WaveMm(Rules, 10, 90));                                     // the fetch caps at 50 km
        Assert.Equal(400, SeaState.WaveMm(Rules, 10, 10));
        // the fetch of open sea upwind: whole kilometres of sea until land
        Assert.Equal(7, SeaState.FetchKm(Rules, (x, y) => x > -7_500_000, 0, 0, FrameMath.FromDegrees(180)));
        Assert.Equal(50, SeaState.FetchKm(Rules, (x, y) => true, 0, 0, FrameMath.FromDegrees(180)));
        Assert.True(SeaState.IsBeamSea(Rules, 0, Deg(90))); Assert.True(SeaState.IsBeamSea(Rules, 0, Deg(270)));
        Assert.False(SeaState.IsBeamSea(Rules, 0, 0)); Assert.False(SeaState.IsBeamSea(Rules, 0, Deg(180)));
        // a tender cog (GM 100 mm): 20 pm of 20-second checks over a long night; a steady one never; GM <= 0 always
        const ulong seed = 0xABCDEF;
        int hits = 0, n = 50_000;
        for (int i = 0; i < n; i++) if (SeaState.Capsizes(seed, CogId, i * 20L, 100, 10, true, Wt)) hits++;
        output.WriteLine($"tender cog in a beam sea: {hits}/{n} = {hits * 1000 / n} pm");
        Assert.InRange(hits * 1000 / n, 17, 23);
        for (int i = 0; i < 2_000; i++)
        {
            Assert.False(SeaState.Capsizes(seed, CogId, i * 20L, Wt.TenderGmMm, 10, true, Wt));    // steady
            Assert.False(SeaState.Capsizes(seed, CogId, i * 20L, 100, 9, true, Wt));               // under 10 m/s
            Assert.False(SeaState.Capsizes(seed, CogId, i * 20L, 100, 10, false, Wt));             // not a beam sea
            Assert.True(SeaState.Capsizes(seed, CogId, i * 20L, 0, 0, false, Wt));                // GM nothing
        }
        Assert.Equal(SeaState.Capsizes(seed, CogId, 4_000, 100, 10, true, Wt), SeaState.Capsizes(seed, CogId, 4_019, 100, 10, true, Wt));   // one draw a 20 s step
        // swamping: a 2 m wave over 1.5 m of freeboard ships 500 mm x 20 L/s a metre = 10 L/s; bailers 5 each, a pump 15
        Assert.Equal(10, SeaState.InflowLps(Wt, 2_000, 1_500, 0));
        Assert.Equal(0, SeaState.InflowLps(Wt, 1_000, 1_500, 0));
        Assert.Equal(20, SeaState.InflowLps(Wt, 0, 1_500, 1));                                   // a broken cell takes 20 L/s
        Assert.Equal(25, SeaState.BailLps(Wt, 5, 0)); Assert.Equal(15, SeaState.BailLps(Wt, 0, 1));
        long w = SeaState.WaterAfter(0, 10, 0, 60); Assert.Equal(600, w);
        Assert.Equal(0, SeaState.WaterAfter(w, 10, 25, 60));                                     // bailed out
        var cog = Cog();
        int free0 = SeaState.FreeboardNow(cog.Load, 0, cog.Stats.WaterplaneMm2, cog.Stats.BlockPm), free1 = SeaState.FreeboardNow(cog.Load, 20_000, cog.Stats.WaterplaneMm2, cog.Stats.BlockPm);
        Assert.Equal(cog.Load.FreeboardMm, free0); Assert.True(free1 < free0);
    }

    // ------------------------------------------------------------------ SH-6

    [Fact]
    public void SH6_LeeShore_AStormOnshoreDrivesACogAshore_BeachedOnSand_WreckedOnRocks_ASailorsHeaveToBuysTime()
    {
        var cog = Cog(); var crew = new SeaCrewState();
        const ulong seed = 77;
        Assert.Equal(LeeFate.Safe, LeeShore.Resolve(seed, cog, crew, 12, 0, ShoreKind.Rock, 5_000).Fate);       // no storm
        Assert.Equal(LeeFate.Safe, LeeShore.Resolve(seed, cog, crew, 25, 0, ShoreKind.Open, 5_000).Fate);       // no shore
        var sand = LeeShore.Resolve(seed, cog, crew, 25, 0, ShoreKind.Sand, 5_000_000);
        var rock = LeeShore.Resolve(seed, cog, crew, 25, 0, ShoreKind.Rock, 5_000_000);
        output.WriteLine($"cog in 25 m/s: drift {sand.DriftMmps} mm/s, net upwind {sand.NetUpwindMmps}, {sand.SecondsToShore} s to a shore 5 km off; rocks broke {rock.BrokenCells.Length} cells");
        Assert.Equal(LeeFate.Beached, sand.Fate); Assert.Empty(sand.BrokenCells);
        Assert.True(sand.NetUpwindMmps <= 0 && sand.SecondsToShore > 0);
        Assert.True(rock.Fate is LeeFate.Wrecked or LeeFate.Aground);
        // the same draw twice: the wreck is deterministic; across the cog's hull the 250 pm breaks a good many cells
        Assert.Equal(rock.BrokenCells, LeeShore.Resolve(seed, cog, crew, 25, 0, ShoreKind.Rock, 5_000_000).BrokenCells);
        Assert.Equal(LeeFate.Wrecked, rock.Fate);
        // a master with Seamanship 40 heaves to: the drift is two thirds, so the shore is further off in time
        var skilled = LeeShore.Resolve(seed, cog, crew, 25, 60, ShoreKind.Sand, 5_000_000);
        Assert.True(skilled.DriftMmps < sand.DriftMmps && skilled.SecondsToShore > sand.SecondsToShore);
        // a longship with 32 men on hard oars claws off a moderate gale (17 m/s) but not a full storm
        var lg = Longship(); var oars = new SeaCrewState { OarsOut = true };
        var gale = LeeShore.Resolve(seed, lg, oars, 17, 0, ShoreKind.Rock, 1_000_000);
        var storm = LeeShore.Resolve(seed, lg, oars, 30, 0, ShoreKind.Rock, 1_000_000);
        output.WriteLine($"longship on hard oars: 17 m/s up {LeeShore.UpwindMmps(lg, oars, 17)} drift {gale.DriftMmps} -> {gale.Fate}; 30 m/s up {LeeShore.UpwindMmps(lg, oars, 30)} drift {storm.DriftMmps} -> {storm.Fate}");
        Assert.Equal(LeeFate.Safe, gale.Fate);
        Assert.NotEqual(LeeFate.Safe, storm.Fate);
    }

    // ------------------------------------------------------------------ SH-7

    [Fact]
    public void SH7_ACogMoorsUnloads40TonnesByHaulingJobs_PaysCustoms_AndLeaves_TheMooringIsFreed()
    {
        var r = new VehicleDesign.VehicleDesignerTests.Rig();
        var cogRow = r.Make("cog"); var other = r.Make("longship");
        ushort grain = r.Goods.IdOf("wheat");
        r.Fill(cogRow, "wheat", 40_000_000);                                              // 40 t
        var wh = new Store(r.Goods, r.Ledger, StoreKind.Barn, EntityId.None);
        var h = new Harbour(1, wh, r.Goods, Rules) { Porters = 20, Crane = true };
        h.AddMooring(0, 0); h.AddMooring(30_000, 0);
        Assert.Equal(2, h.FreeMoorings);
        var m = h.Moor(cogRow.Id, 1_000, 0); Assert.NotNull(m); Assert.Equal(cogRow.Id, m!.Ship); Assert.Equal(1, h.FreeMoorings);
        var m2 = h.Moor(other.Id, 1_000, 0); Assert.NotNull(m2); Assert.NotSame(m, m2);           // the nearest free cell
        Assert.Null(h.Moor(EntityId.Make(IdKind.Other, 99), 0, 0));                              // full
        Assert.Null(h.Moor(cogRow.Id, 0, 0));                                                    // already here
        Assert.True(h.Release(other.Id)); Assert.False(h.Release(other.Id));
        Assert.Equal(Voyages.LegSeconds(60_000, 300), h.WarpSeconds(60_000));                    // warped in at 0.3 m/s

        // customs: 5% of the cargo's value, from the master's purse to the realm's treasury; an unpaid duty holds the cargo
        var led = EconFixture.NewEconomy(false).Ledger;
        var master = led.Open(PurseKind.Person, EntityId.Make(IdKind.Person, 1)); var treasury = led.Open(PurseKind.Person, EntityId.Make(IdKind.Person, 2));
        led.Mint(master, new Coin(1_000), SourceReason.Seed);
        long total = led.Audit().InPurses.Mites;
        var poor = h.PayCustoms(led, treasury, master, 100_000); Assert.False(poor.Paid); Assert.Equal(5_000, poor.Duty);   // the treasury has nothing to pay with
        var duty = h.PayCustoms(led, master, treasury, 4_000); Assert.True(duty.Paid); Assert.Equal(200, duty.Duty);
        Assert.Equal(800, led.Balance(master).Mites); Assert.Equal(200, led.Balance(treasury).Mites); Assert.Equal(total, led.Audit().InPurses.Mites);
        Assert.False(Harbour.MayLeave(cogRow.Cargo, true));                                      // a full hold

        // 20 porters at 30 kg a 90 s trip (6.7 kg/s) and a crane at 500 kg a 120 s lift (4.2 kg/s): about 10.8 kg/s, so 40 t in about an hour
        long moved = 0; int secs = 0;
        while (cogRow.Cargo.TotalMassG() > 0 && secs < 20_000) { moved += h.Unload(cogRow.Cargo, 10); secs += 10; }
        output.WriteLine($"40 t hauled in {secs} s");
        Assert.Equal(40_000_000, moved); Assert.Equal(40_000_000, wh.Qty(grain)); Assert.Equal(0, cogRow.Cargo.Qty(grain));
        Assert.InRange(secs, 3_000, 4_500);
        Assert.True(Harbour.MayLeave(cogRow.Cargo, true)); Assert.False(Harbour.MayLeave(cogRow.Cargo, false));
        Assert.True(h.Release(cogRow.Id)); Assert.Equal(2, h.FreeMoorings);
        Assert.Equal(40_000_000, r.Ledger.TotalCreated(grain)); Assert.Equal(0, r.Ledger.TotalDestroyed(grain));   // goods only moved: nothing created or destroyed by the hauling
    }

    [Fact]
    public void Harbours_AreDerivedFromTheTiles_BeachesAreSandWhereFlatAndLow_RockWhereNot()
    {
        var reg = new Region(); int S = Region.Size;
        // a sea on the left of a 200-tile-wide land: x < 100 is sea; the shore column is x = 100; tiles 100..149 are flat sand except y 300..320 (a cliff)
        for (int y = 0; y < S; y++) for (int x = 0; x < S; x++) { int t = y * S + x; if (x < 100) reg.Water[t] = WaterFlags.Sea; reg.SlopePermille[t] = (short)(y is >= 300 and <= 320 ? 400 : 20); reg.HeightCm[t] = 100; }
        var all = Beaches.Of(reg, Rules);
        Assert.Equal(S, all.Count);                                                               // one shore tile a row
        Assert.Equal(S - 21, all.Count(b => b.Kind == ShoreKind.Sand)); Assert.Equal(21, all.Count(b => b.Kind == ShoreKind.Rock));
        Assert.Equal(all.Select(b => b.Tile), all.Select(b => b.Tile).OrderBy(t => t));           // stable order
        int xM = 100 * Region.TileM + 32, yM = 100 * Region.TileM + 32;
        var near = Beaches.Near(all, xM, yM, 2_000);
        Assert.NotEmpty(near); Assert.All(near, b => Assert.Equal(ShoreKind.Sand, b.Kind));
        Assert.Equal(100 * S + 100, near[0].Tile);
        var moor = Beaches.Moorings(all, xM, yM, 4);
        Assert.Equal(4, moor.Count);
        for (int i = 0; i < moor.Count; i++) for (int j = i + 1; j < moor.Count; j++) Assert.True(Math.Abs(moor[i].Tile / S - moor[j].Tile / S) >= 2);
    }

    // ------------------------------------------------------------------ SH-8

    [Fact]
    public void SH8_AFerryBusinessTakesFares_FishingBoatsLand12KgACrewDay_WinterX04()
    {
        var wt = Wt;
        var r = new VehicleDesign.VehicleDesignerTests.Rig();
        var econ = EconFixture.NewEconomy(false); var led = econ.Ledger;
        var ferryman = led.Open(PurseKind.Person, EntityId.Make(IdKind.Person, 5));
        var ferry = r.Make("ferry");
        var biz = new FerryBusiness(3, ferry.Id, EntityId.Make(IdKind.Person, 5), ferryman, 30_000);
        Assert.True(FerryBusiness.CanOperate(10, 0)); Assert.True(FerryBusiness.CanOperate(0, 30)); Assert.False(FerryBusiness.CanOperate(5, 9));
        var a = led.Open(PurseKind.Person, EntityId.Make(IdKind.Person, 6)); var b = led.Open(PurseKind.Person, EntityId.Make(IdKind.Person, 7));
        led.Mint(a, new Coin(100), SourceReason.Seed); led.Mint(b, new Coin(10), SourceReason.Seed);
        long total = led.Audit().InPurses.Mites;
        var x1 = biz.Take(led, a, 1, 1, 1, wt);                                                  // 15 + 15 + 40
        var x2 = biz.Take(led, b, 1, 0, 0, wt);                                                  // 15 mites: he has 10
        Assert.True(x1.Paid); Assert.Equal(70, x1.Fee); Assert.Equal(60, x1.Seconds); Assert.False(x2.Paid);
        Assert.Equal(1, biz.Crossings); Assert.Equal(70, led.Balance(ferryman).Mites); Assert.Equal(30, led.Balance(a).Mites); Assert.Equal(10, led.Balance(b).Mites);
        Assert.Equal(total, led.Audit().InPurses.Mites);
        Assert.Equal(70, biz.Daily()); Assert.Equal(0, biz.Takings);

        ushort fish = r.Goods.IdOf("fish_fresh");
        var boats = new[] { (r.Make("fishing_boat"), 2), (r.Make("fishing_boat"), 1) };
        var landing = new Store(r.Goods, r.Ledger, StoreKind.Barn, EntityId.None);
        long stock = 1_000_000;
        long summer = FisheryRun.Day(r.Book.Vehicles, boats, fish, false, ref stock, landing, wt);
        Assert.Equal(12_000 * 3, summer); Assert.Equal(36_000, landing.Qty(fish)); Assert.Equal(1_000_000 - 36_000, stock);   // 12 kg a crew member
        long winter = FisheryRun.Day(r.Book.Vehicles, boats, fish, true, ref stock, landing, wt);
        Assert.Equal(12_000 * 3 * 400 / 1000, winter);                                              // x0.4
        long thin = 5_000; Assert.Equal(5_000, FisheryRun.Day(r.Book.Vehicles, boats, fish, false, ref thin, landing, wt)); Assert.Equal(0, thin);   // never more than the stock holds
    }

    [Fact]
    public void Masters_TheBestSeamanCommands_FishermenCountHalf_TiesGoToTheLowerId()
    {
        Assert.Equal(3UL, Masters.Pick([(5UL, 20, 0), (3UL, 40, 0), (9UL, 10, 60)]));
        Assert.Equal(9UL, Masters.Pick([(5UL, 20, 0), (9UL, 0, 70)]));                            // fishing 70 counts as 35
        Assert.Equal(2UL, Masters.Pick([(7UL, 30, 0), (2UL, 30, 0)]));
        Assert.Null(Masters.Pick([]));
    }
}
