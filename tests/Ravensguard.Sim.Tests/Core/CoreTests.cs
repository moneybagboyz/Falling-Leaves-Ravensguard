using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Tests.Core;

public class FixedTests
{
    [Fact] public void BasicArithmetic()
    {
        var a = Fixed.FromInt(3); var b = Fixed.FromRatio(1, 2);
        Assert.Equal(Fixed.FromRatio(7, 2), a + b);
        Assert.Equal(Fixed.FromRatio(5, 2), a - b);
        Assert.Equal(Fixed.FromRatio(3, 2), a * b);
        Assert.Equal(Fixed.FromInt(6), a / b);
    }
    [Fact] public void Negatives()
    {
        var a = Fixed.FromInt(-3); var b = Fixed.FromRatio(1, 2);
        Assert.Equal(Fixed.FromRatio(-3, 2), a * b);
        Assert.Equal(Fixed.FromInt(-6), a / b);
        Assert.Equal(Fixed.FromInt(6), a / -b);
        Assert.Equal(-4, Fixed.FromRatio(-7, 2).Floor());
        Assert.Equal(3, Fixed.FromInt(-3).Abs().Floor());
        Assert.Equal(Fixed.FromRatio(1, 2), Fixed.FromRatio(-3, 2).Frac());
    }
    [Fact] public void LargeMultiplyDoesNotOverflowIntermediate()
        => Assert.Equal(Fixed.FromInt(20000), Fixed.FromInt(200) * Fixed.FromInt(100));
    [Fact] public void MinMaxClampLerp()
    {
        var lo = Fixed.FromInt(1); var hi = Fixed.FromInt(5);
        Assert.Equal(lo, Fixed.Min(lo, hi)); Assert.Equal(hi, Fixed.Max(lo, hi));
        Assert.Equal(hi, Fixed.Clamp(Fixed.FromInt(9), lo, hi));
        Assert.Equal(Fixed.FromInt(3), Fixed.Lerp(lo, hi, Fixed.Half));
        Assert.True(lo < hi && hi >= lo && lo != hi);
    }
    [Fact] public void ToFloatDisplay() => Assert.Equal(0.25f, Fixed.FromRatio(1, 4).ToFloat());
}

public class DetRngTests
{
    [Fact] public void SameInputsSameOutput()
        => Assert.Equal(DetRng.Next(1, 2, 3, 4, 5), DetRng.Next(1, 2, 3, 4, 5));
    [Fact] public void DifferentEntityDifferentStream()
    {
        Assert.NotEqual(DetRng.Next(1, 2, 3, 4, 5), DetRng.Next(1, 2, 4, 4, 5));
        Assert.NotEqual(DetRng.Next(1, 2, 3, 4, 5), DetRng.Next(1, 3, 3, 4, 5));
        Assert.NotEqual(DetRng.Next(1, 2, 3, 4, 5), DetRng.Next(1, 2, 3, 5, 5));
        Assert.NotEqual(DetRng.Next(1, 2, 3, 4, 5), DetRng.Next(1, 2, 3, 4, 6));
    }
    [Fact] public void MeanNearHalf()
    {
        double sum = 0;
        for (uint i = 0; i < 10000; i++) sum += DetRng.NextFixed01(7, 1, 42, 0, i).ToDouble();
        double mean = sum / 10000;
        Assert.InRange(mean, 0.48, 0.52);
    }
    [Fact] public void Fixed01InRange()
    {
        for (uint i = 0; i < 5000; i++)
        {
            var f = DetRng.NextFixed01(3, 0, 1, 0, i);
            Assert.True(f >= Fixed.Zero && f < Fixed.One);
        }
    }
    [Fact] public void RangeBoundsAndCoverage()
    {
        var seen = new HashSet<int>();
        for (uint i = 0; i < 2000; i++)
        {
            int r = DetRng.Range(9, 0, 1, 0, i, -3, 4);
            Assert.InRange(r, -3, 3); seen.Add(r);
        }
        Assert.Equal(7, seen.Count);
        Assert.Equal(5, DetRng.Range(1, 1, 1, 1, 1, 5, 5));
    }
    [Fact] public void ChanceExtremesAndRate()
    {
        Assert.False(DetRng.Chance(1, 1, 1, 1, 1, Fixed.Zero));
        int hits = 0;
        for (uint i = 0; i < 10000; i++) { Assert.True(DetRng.Chance(1, 1, 1, 1, i, Fixed.One)); if (DetRng.Chance(2, 1, 1, 1, i, Fixed.FromRatio(1, 4))) hits++; }
        Assert.InRange(hits, 2300, 2700);
    }
    [Fact] public void StreamIsIndependentOfInterleaving()
    {
        var a = new RngStream(1, 1, 10, 0); var b = new RngStream(1, 1, 20, 0);
        var a1 = a.NextUInt(); b.NextUInt(); b.NextUInt(); var a2 = a.NextUInt();
        var a2solo = new RngStream(1, 1, 10, 0); a2solo.NextUInt();
        Assert.Equal(a1, DetRng.NextUInt(1, 1, 10, 0, 0));
        Assert.Equal(a2solo.NextUInt(), a2);
        Assert.Equal(2u, a.Draws);
    }
}

public class DetNoiseTests
{
    [Fact] public void DeterministicAndInRange()
    {
        for (int i = 0; i < 200; i++)
        {
            var x = Fixed.FromRatio(i * 37 - 3000, 16); var y = Fixed.FromRatio(i * 11 - 500, 8);
            var v = DetNoise.Value(5, x, y); var f = DetNoise.Fbm(5, x, y, 4);
            Assert.Equal(v, DetNoise.Value(5, x, y));
            Assert.Equal(f, DetNoise.Fbm(5, x, y, 4));
            Assert.True(v >= Fixed.Zero && v < Fixed.One);
            Assert.True(f >= Fixed.Zero && f < Fixed.One);
        }
    }
    [Fact] public void SeedChangesOutputAndIsContinuous()
    {
        var x = Fixed.FromRatio(5, 2); var y = Fixed.FromRatio(7, 3);
        Assert.NotEqual(DetNoise.Value(1, x, y), DetNoise.Value(2, x, y));
        var near = DetNoise.Value(1, x + Fixed.FromRaw(1), y);
        Assert.True((near - DetNoise.Value(1, x, y)).Abs() < Fixed.FromRatio(1, 100));
    }
}

public class IdTests
{
    [Fact] public void KindsAndMonotonic()
    {
        var a = new IdAllocator();
        var p1 = a.Allocate(IdKind.Person); var p2 = a.Allocate(IdKind.Person); var h = a.Allocate(IdKind.Household);
        Assert.Equal(IdKind.Person, p1.Kind); Assert.Equal(IdKind.Household, h.Kind);
        Assert.True(p2.CompareTo(p1) > 0);
        Assert.Equal(1UL, p1.Counter); Assert.Equal(2UL, p2.Counter); Assert.Equal(1UL, h.Counter);
        Assert.NotEqual(p1, h);
    }
    [Fact] public void StateRoundTrips()
    {
        var a = new IdAllocator(); a.Allocate(IdKind.Item); a.Allocate(IdKind.Item);
        var b = IdAllocator.FromState(a.GetState());
        Assert.Equal(a.Allocate(IdKind.Item), b.Allocate(IdKind.Item));
    }
}

public class GameTimeTests
{
    [Fact] public void Constants()
    {
        Assert.Equal(60, TimeConst.DaysPerYear);
        Assert.Equal(3600, Duration.FromRealMinutes(3).GameSeconds);      // 3 real min = 1 game hour
        Assert.Equal(86400, Duration.FromRealMinutes(72).GameSeconds);    // 72 real min = 1 game day
    }
    [Fact] public void Calendar()
    {
        var t = GameTime.FromCalendar(2, Season.Autumn, 3, 14, 30);
        Assert.Equal(2, t.Year); Assert.Equal(Season.Autumn, t.Season); Assert.Equal(3, t.DayOfSeason);
        Assert.Equal(33, t.DayOfYear); Assert.Equal(14, t.Hour); Assert.Equal(30, t.Minute);
        var end = GameTime.FromGameDays(59); Assert.Equal(Season.Winter, end.Season); Assert.Equal(15, end.DayOfSeason);
        var next = GameTime.FromGameDays(60); Assert.Equal(1, next.Year); Assert.Equal(Season.Spring, next.Season); Assert.Equal(1, next.DayOfSeason);
    }
    [Fact] public void Arithmetic()
    {
        var t = GameTime.FromGameHours(5) + Duration.FromGameMinutes(90);
        Assert.Equal(6, t.Hour); Assert.Equal(30, t.Minute);
        Assert.Equal(Duration.FromGameHours(1), GameTime.FromGameHours(3) - GameTime.FromGameHours(2));
    }
}

public class SimClockTests
{
    [Fact] public void ThreeRealMinutesIsOneGameHourAt1x()
    {
        var c = new SimClock();
        int crossed = 0;
        for (int i = 0; i < 180 * 20; i++) crossed += c.Advance(0.05); // 20 Hz sim ticks
        Assert.Equal(1, crossed); Assert.Equal(1, c.Now.Hour);
    }
    [Fact] public void SpeedScalesAndPauseStops()
    {
        var c = new SimClock { Speed = SimSpeed.X8 };
        Assert.Equal(8, c.Advance(180 * 1.0)); // 180 real s * 20 * 8 = 8 game hours
        c.Speed = SimSpeed.Paused;
        Assert.Equal(0, c.Advance(1000)); Assert.Equal(8, c.Now.Hour);
    }
    [Fact] public void ReportsMultipleCrossingsInOneStep()
    {
        var c = new SimClock { Speed = SimSpeed.X4 };
        Assert.Equal(4, c.Advance(180)); // 4 hours in 180 real s at 4x
    }
}

public class SimEventsTests
{
    private record A(int N); private record B(string S);
    [Fact] public void QueuedUntilFlushInPublishOrder()
    {
        var bus = new SimEvents(); var log = new List<string>();
        bus.Subscribe<A>(a => log.Add("A" + a.N)); bus.Subscribe<B>(b => log.Add("B" + b.S));
        bus.Publish(new A(1)); bus.Publish(new B("x")); bus.Publish(new A(2));
        Assert.Empty(log); Assert.Equal(3, bus.Pending);
        bus.Flush();
        Assert.Equal(new[] { "A1", "Bx", "A2" }, log);
    }
    [Fact] public void UnsubscribeAndNestedPublish()
    {
        var bus = new SimEvents(); var log = new List<string>();
        var sub = bus.Subscribe<A>(a => log.Add("A" + a.N));
        bus.Subscribe<B>(b => { log.Add("B"); bus.Publish(new A(9)); });
        bus.Publish(new B("")); bus.Publish(new A(1));
        bus.Flush();
        Assert.Equal(new[] { "B", "A1", "A9" }, log);
        sub.Dispose(); log.Clear(); bus.Publish(new A(3)); bus.Flush();
        Assert.Empty(log);
    }
}

public class ChronicleTests
{
    private static ChronicleEntry E(IdAllocator ids, string type, params Cause[] causes)
        => new(ids.Allocate(IdKind.Event), GameTime.FromGameDays(1), type, Array.Empty<EntityId>(), causes, new Dictionary<string, string>());
    [Fact] public void AddGetByType()
    {
        var ids = new IdAllocator(); var c = new Chronicle();
        var e1 = E(ids, "fire", new Cause(CauseKind.Fire)); var e2 = E(ids, "death", new Cause(CauseKind.Wound, e1.Id, "burns"));
        c.Add(e1); c.Add(e2);
        Assert.Same(e2, c.Get(e2.Id)); Assert.Single(c.ByType("fire")); Assert.Null(c.Get(EntityId.None));
        Assert.Throws<ArgumentException>(() => c.Add(e1));
    }
    [Fact] public void CauseCoverage()
    {
        var ids = new IdAllocator(); var c = new Chronicle();
        Assert.Equal(1.0, c.CauseCoverage());
        c.Add(E(ids, "a", new Cause(CauseKind.War)));
        c.Add(E(ids, "b", new Cause(CauseKind.MarketDrift), new Cause(CauseKind.Unknown)));
        c.Add(E(ids, "c", new Cause(CauseKind.Unknown), new Cause(CauseKind.Famine)));
        c.Add(E(ids, "d"));
        Assert.Equal(0.5, c.CauseCoverage());
    }
}
