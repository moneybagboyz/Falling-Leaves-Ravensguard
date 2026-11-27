using Ravensguard.Content;
using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Combat.Mounted;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Naval;
using Ravensguard.Sim.Tests.Economy;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Siege;

/// <summary>P7-SG-E: war wagons chained into a fort, and chariots (sieges-and-engines SG-17, SG-18; 4.8, 4.9).</summary>
public class WagonChariotTests(ITestOutputHelper output)
{
    private static ContentDatabase Db => EconFixture.Content;
    private static readonly EntityId Lord = EntityId.Make(IdKind.Person, 77);
    private static MountedRefusalDef Refusal => new();

    private static RideBook Rides(ulong seed = 7) { var b = new RideBook(Db, EconFixture.NewEconomy(false), seed); Assert.Empty(b.Templates.Errors); return b; }

    private static (long X, long Y) Centre(WagonFort f, VehicleRow v, int cx, int cy)
    {
        var (w, l) = f.Book.Size(v);
        var (cos, sin) = FrameMath.CosSin(v.Facing << 16);
        var (x, y) = FrameMath.ToWorld((int)v.XMm, (int)v.YMm, cos, sin, cx * 1000 + 500 - w * 500, cy * 1000 + 500 - l * 500);
        return (x, y);
    }

    private static (RideBook B, WagonFort F, List<VehicleRow> Wagons, List<(string Type, string Key, EntityId Id)> Chron) Fort(int n = 20, ulong seed = 7)
    {
        var b = Rides(seed); var f = new WagonFort(Db, b, seed);
        var chron = new List<(string, string, EntityId)>();
        f.Chronicle = (when, type, key, ps, subj, causes, data) => { var id = new EntityId(900_000UL + (ulong)chron.Count); chron.Add((type, key, id)); return id; };
        var wagons = new List<VehicleRow>();
        for (int i = 0; i < n; i++)        // a column marching in from the west, 6 m apart, the nearest 8 m from the ring's middle
            wagons.Add(b.Vehicles.Create(b.Templates.Get("war_wagon"), Lord, -8_000 - i * 6_000, (i % 2) * 2_000));
        return (b, f, wagons, chron);
    }

    // ------------------------------------------------------------------ SG-17

    [Fact]
    public void SG17_TwentyWarWagonsFormARing_ChainInThreeMinutes_AndAChainCutIn30SecondsOpensAGap()
    {
        var (b, f, wagons, chron) = Fort();
        Assert.True(f.Form(wagons, 0, 0, 0, _ => 50));
        output.WriteLine($"ring radius {f.RadiusMm} mm, perimeter of chords {f.Slots.Count * (5000 + 1000)} mm, wagon {b.Vehicles.StatsOf(wagons[0]).MassKg} kg");
        Assert.InRange(b.Vehicles.StatsOf(wagons[0]).MassKg, 1200, 1800);                // a war wagon is about 1.5 t
        Assert.InRange(f.Slots.Count * 6_000, 100_000, 130_000);                         // about 100 m of boards, 120 with the chains
        f.Step(180);
        Assert.True(f.Formed, "the ring is chained within three minutes");
        Assert.InRange(f.FormedAtSec, 1, 180);
        Assert.True(f.Sealed); Assert.All(f.Chains, c => Assert.Equal(ChainState.Chained, c.State));
        Assert.Single(chron, c => c.Type == "wagon_fort_formed");
        f.Step(500); Assert.Single(chron, c => c.Type == "wagon_fort_formed");           // written once

        // the boards are walls: an armour board blocks bodies and missiles, a slit board bodies only, a chain bodies only, the middle of the ring neither
        var w0 = b.Vehicles.Find(f.Slots[0].Vehicle)!;
        var arm = Centre(f, w0, 0, 1); Assert.True(f.BlocksMove((int)arm.X, (int)arm.Y)); Assert.True(f.BlocksMissile((int)arm.X, (int)arm.Y));
        var pav = Centre(f, w0, 0, 2); Assert.True(f.BlocksMove((int)pav.X, (int)pav.Y)); Assert.True(f.BlocksMissile((int)pav.X, (int)pav.Y));
        var slit = Centre(f, w0, 1, 2); Assert.True(f.BlocksMove((int)slit.X, (int)slit.Y)); Assert.False(f.BlocksMissile((int)slit.X, (int)slit.Y));
        Assert.Equal(800, f.SlitCoverPm((int)slit.X, (int)slit.Y)); Assert.Equal(0, f.SlitCoverPm((int)arm.X, (int)arm.Y));
        Assert.False(f.BlocksMove(0, 0)); Assert.True(f.Inside(0, 0));
        var chain = f.Chains[0];
        Assert.True(f.BlocksMove((int)chain.X, (int)chain.Y)); Assert.False(f.BlocksMissile((int)chain.X, (int)chain.Y));

        // cavalry refuses by the fort's count: the standing boards within the arc are braced polearms through the one refusal formula
        long px = chain.X - chain.X / 8, py = chain.Y - chain.Y / 8;                      // a little outside... a point near the chain, inside the arc
        int n = f.BracedAt(px, py);
        Assert.InRange(n, 1, f.Rules.MaxBraced);
        int p = f.RefusalPm(px, py, 700, 40, Refusal);
        Assert.Equal(ChargeMath.RefusalPm(n, 700, 40, 200, 150, 2, 700, 2, 950, 1000), p);
        Assert.True(p > ChargeMath.RefusalPm(0, 700, 40));
        Assert.Equal(0, f.BracedAt(0, 0));                                                // the open middle of the ring is out of every board's arc
        // a broken board is a gap and lowers the count
        Assert.True(f.Strike(0, 0, 2, 30) == 0);                                          // 30 kg m/s: 50 hit points of 200, it holds
        Assert.True(f.BlocksMissile((int)pav.X, (int)pav.Y));
        Assert.True(f.Strike(0, 0, 2, 2_000) >= 1);
        Assert.False(f.BlocksMove((int)pav.X, (int)pav.Y)); Assert.False(f.BlocksMissile((int)pav.X, (int)pav.Y));
        Assert.Equal(0, f.CellKind(w0, 0, 2)); Assert.Equal(1, f.CellKind(w0, 0, 1));                          // the cell is a gap, its neighbour stands

        // a chain cut in 30 seconds by one man with an axe opens a gap
        Assert.True(f.Cut(5, 1));
        f.Step(29); var c5 = f.Chains[5]; Assert.True(f.BlocksMove((int)c5.X, (int)c5.Y)); Assert.Empty(f.OpenGaps());
        f.Step(1); Assert.Equal(ChainState.Open, c5.State); Assert.False(f.BlocksMove((int)c5.X, (int)c5.Y)); Assert.Equal([5], f.OpenGaps().ToArray());
        // a sortie looses a chain in 20 seconds with two men and hangs it again in as many
        Assert.True(f.Sortie(9)); f.Step(19); Assert.DoesNotContain(9, f.OpenGaps()); f.Step(1); Assert.Contains(9, f.OpenGaps());
        Assert.True(f.Close(9)); f.Step(20); Assert.DoesNotContain(9, f.OpenGaps()); Assert.Equal(ChainState.Chained, f.Chains[9].State);
        Assert.True(f.Close(5)); f.Step(20); Assert.True(f.Sealed);
    }

    [Fact]
    public void SG17_Pack_RoundTrips_AndTheRingIsTheSameEveryRun()
    {
        var (b, f, wagons, _) = Fort(8);
        f.Form(wagons, 5_000, -3_000, 100, _ => 30); f.Step(70);
        var packed = f.Pack();
        var g = new WagonFort(Db, b, 7); g.Unpack(packed);
        Assert.Equal(packed, g.Pack());
        var (b2, f2, wagons2, _) = Fort(8);
        f2.Form(wagons2, 5_000, -3_000, 100, _ => 30); f2.Step(70);
        Assert.Equal(packed, f2.Pack());
        Assert.False(new WagonFort(Db, b, 7).Form(wagons.Take(3), 0, 0, 0));                  // fewer than the rules' four make no ring
    }

    // ------------------------------------------------------------------ SG-18

    private static (RideBook B, ChariotBook C, VehicleRow V, Chariot Ch, List<(string, string)> Chron) Car(bool spear = true)
    {
        var b = Rides(); var c = new ChariotBook(Db, b, 7);
        var chron = new List<(string, string)>();
        c.Chronicle = (when, type, key, ps, subj, causes, data) => { chron.Add((type, key)); return new EntityId(910_000UL + (ulong)chron.Count); };
        var v = b.Vehicles.Create(b.Templates.Get("war_chariot"), Lord, 0, 0);
        var ch = c.Raise(v, EntityId.Make(IdKind.Person, 801), EntityId.Make(IdKind.Person, 802), spear, 50)!;
        return (b, c, v, ch, chron);
    }

    [Fact]
    public void SG18_ASpearThrustFromAChariotAt10MetresASecondAdds40800Milli()
    {
        var (b, c, v, ch, _) = Car();
        output.WriteLine($"car {b.Vehicles.StatsOf(v).MassKg} kg, total {c.TotalKg(ch)} kg");
        Assert.Equal(300, b.Vehicles.StatsOf(v).MassKg);                                   // car 300 + team 900 + crew 160
        Assert.Equal(1360, c.TotalKg(ch));
        Assert.Equal(40_800, c.ThrustMilli(ch, 10_000));
        Assert.Equal(40_800, ChargeMath.ExtraMilli(10, 1360, 10_000, 300));
        Assert.Equal(c.Rules.ChargeK * 1360L * 100 * 300 / 10_000, c.ThrustMilli(ch, 10_000));
        // the team tramples, a horse each (the hit is a roll on 0x7E8, fixed by the key)
        var t = c.Trample(ch, 10_000, 5); Assert.Equal(2, t.Length);
        Assert.All(t, m => Assert.True(m is 0 or 36_000));                                // 8 x 450 kg x 10 m/s = 36,000 milli a horse
        Assert.Equal(t, c.Trample(ch, 10_000, 5));
        Assert.Contains(Enumerable.Range(1, 40), sec => c.Trample(ch, 10_000, sec).Any(m => m > 0));                  // over some seconds the hooves do hit
        // braced spears turn the team by the Phase 4 refusal
        Assert.Equal(ChargeMath.RefusalPm(3, 700, 50, 200, 150, 2, 700, 2, 950, 1000), ChariotBook.RefusalPm(3, 700, 50, Refusal));
    }

    [Fact]
    public void SG18_TheGroundRulesTheCar_WoodsForbidden_Mud1MetreASecond_RoughX04_DitchesCrash()
    {
        var (_, c, v, ch, chron) = Car();
        Assert.Equal(10_000, c.Cap(Footing.Road, 12_000));
        Assert.Equal(8_500, c.Cap(Footing.Field, 10_000));
        Assert.Equal(0, c.Cap(Footing.Woods, 10_000));
        Assert.Equal(1_000, c.Cap(Footing.Mud, 10_000));
        Assert.Equal(4_000, c.Cap(Footing.Rough, 10_000));
        Assert.True(c.Cap(Footing.Field, 10_000, 100) < 8_500);                            // uphill slows it
        // it speeds up on a road to 10 m/s, slows in mud to 1 m/s and stops at a wood
        for (int s = 0; s < 8; s++) c.Step(ch, 10_000, 0, Footing.Road, 0, s);
        Assert.Equal(10_000, ch.SpeedMmps); Assert.True(v.XMm > 20_000);
        c.Step(ch, 10_000, 0, Footing.Mud, 0, 9); Assert.Equal(6_000, ch.SpeedMmps);
        c.Step(ch, 10_000, 0, Footing.Mud, 0, 10); c.Step(ch, 10_000, 0, Footing.Mud, 0, 11); Assert.Equal(1_000, ch.SpeedMmps);
        c.Step(ch, 10_000, 0, Footing.Woods, 0, 12); Assert.Equal(0, ch.SpeedMmps);
        Assert.Empty(chron);
    }

    [Fact]
    public void SG18_ABrokenWheelCrashesIt_EachCrewmanFalls_AndTheCarTurnsNoFasterThan45DegreesASecond()
    {
        var (b, c, v, ch, chron) = Car();
        for (int s = 0; s < 6; s++) c.Step(ch, 10_000, 0, Footing.Road, 0, s);
        Assert.Equal(10_000, ch.SpeedMmps);
        // a wanted turn of 180 degrees at speed takes four seconds at 32 units (45 degrees) a second
        for (int s = 6; s < 8; s++) c.Step(ch, 10_000, 127, Footing.Road, 0, s);
        Assert.Equal(64, v.Facing);
        // a wheel breaks at speed
        int wheel = v.Parts.FindIndex(p => b.Templates.PartList[p.Part].Tags.Contains("wheel"));
        b.Vehicles.Damage(v, wheel, int.MaxValue);
        Assert.True(v.Broken);
        var r = c.Step(ch, 10_000, 64, Footing.Road, 0, 8);
        Assert.True(r.Crashed); Assert.True(ch.Crashed); Assert.Equal(0, ch.SpeedMmps);
        Assert.Equal(2, r.Falls.Count);
        long expect = ChargeMath.FallMilli(80, 10_000, 1000);
        Assert.All(r.Falls, f => Assert.Equal(expect, f.Milli));
        output.WriteLine($"fall at 10 m/s from 1 m: {expect} milli");
        Assert.Equal(("chariot_crash", "ch_sg_chariot_crash"), Assert.Single(chron));
        Assert.False(c.Step(ch, 10_000, 64, Footing.Road, 0, 9).Crashed);                  // it crashes once
    }

    [Fact]
    public void SG18_ADitchOrStakesAtSpeedCrashAChariot_AtAWalkTheyDoNot()
    {
        var (_, c, _, ch, chron) = Car();
        for (int s = 0; s < 6; s++) c.Step(ch, 10_000, 0, Footing.Road, 0, s);
        Assert.True(c.Step(ch, 10_000, 0, Footing.Stakes, 0, 7).Crashed);
        Assert.Single(chron);
        var (_, c2, _, ch2, chron2) = Car();
        ch2.SpeedMmps = 2_000;
        Assert.False(c2.Step(ch2, 10_000, 0, Footing.Ditch, 0, 1).Crashed); Assert.Empty(chron2);
    }

    [Fact]
    public void P7R21_MoreHandsHelpAtMostDouble()
    {
        Assert.Equal(WagonFort.Seconds(60, 4, 8), WagonFort.Seconds(60, 4, 100));
        Assert.Equal(30, WagonFort.Seconds(60, 4, 8)); Assert.Equal(60, WagonFort.Seconds(60, 4, 4)); Assert.Equal(240, WagonFort.Seconds(60, 4, 0));
    }

    [Fact]
    public void P7R22_TheFortAndTheChariotsRoundTrip_KeepingTheOriginTheCauseAndTheCrash()
    {
        var (b, f, wagons, _) = Fort(8);
        Assert.True(f.Form(wagons, 0, 0, 0, _ => 50)); f.Step(60); f.OriginX = 12_345; f.OriginY = -6_789; f.Cause = EntityId.Make(IdKind.Event, 4242);
        var packed = f.Pack(); var g = new WagonFort(Db, b, 7); g.Unpack(packed);
        Assert.Equal(packed, g.Pack()); Assert.Equal((12_345L, -6_789L), (g.OriginX, g.OriginY)); Assert.Equal(f.Cause, g.Cause);
        var older = packed[..^3]; var h = new WagonFort(Db, b, 7); h.Unpack(older); Assert.Equal(0, h.OriginX);                 // an older save ends before the appended fields
        var (b2, c, v, ch, _) = Car();
        ch.SpeedMmps = 7_000; ch.Crashed = true; c.Cause = EntityId.Make(IdKind.Event, 77);
        var cp = c.Pack(); var c2 = new ChariotBook(Db, b2, c.Seed); c2.Unpack(cp);
        Assert.Equal(cp, c2.Pack()); var back = c2.Find(v.Id)!; Assert.True(back.Crashed); Assert.Equal(7_000, back.SpeedMmps); Assert.Equal(c.Cause, c2.Cause);
        Assert.Equal((ch.Driver, ch.Fighter, ch.Riding, ch.Horses, ch.Spear), (back.Driver, back.Fighter, back.Riding, back.Horses, back.Spear));
    }
}
