using Ravensguard.Sim.Core;
using Ravensguard.Sim.Lighting;

namespace Ravensguard.Sim.Tests.Lighting;

/// <summary>Phase 1 review: every torch step used to allocate two fresh frames; now two frames are recycled and only the torch's square is rewritten.</summary>
public class LightFramePairTests
{
    private static LightGrid Hall()
    {
        var g = new LightGrid(60, 40, -1, 1);
        for (int y = 0; y < 40; y++) if (y is not (10 or 11)) g.SetOpaque(30, y, 0, true);   // a wall with a door
        for (int x = 0; x < 20; x++) for (int y = 20; y < 30; y++) g.SetIndoor(x, y, 0, true);
        g.SetStaticLights([new LightSource(10, 25, 0, 7, 215), new LightSource(33, 10, 0, 6, 200)]);
        return g;
    }

    private static void SameLight(LightFrame a, LightFrame b)
    {
        Assert.True(a.AmbientSpan.SequenceEqual(b.AmbientSpan), "ambient differs");
        Assert.True(a.PointSpan.SequenceEqual(b.PointSpan), "point light differs");
    }

    [Fact]
    public void Patched_frames_equal_a_full_compute_for_a_moving_flickering_torch()
    {
        var g = Hall();
        var pair = new LightFramePair(g);
        var night = new Ambient(0, 0, DayPhase.Night);
        var rng = new Random(7);
        int x = 8, y = 25;
        for (int step = 0; step < 300; step++)
        {
            x = Math.Clamp(x + rng.Next(-1, 2), 2, 57); y = Math.Clamp(y + rng.Next(-1, 2), 2, 37);
            LightSource[] torch = step % 50 == 49 ? [] : [new LightSource(x, y, 0, LightModel.TorchRadius, LightModel.TorchIntensity + rng.Next(-10, 11)), new LightSource(x + 2, y, 0, 4, 120)];
            var f = pair.Next(night, torch);
            SameLight(f, g.Compute(night, torch));
        }
        Assert.True(pair.Patches > 250, $"{pair.Patches} patches, {pair.FullComputes} full computes");
        Assert.Equal(2, pair.FullComputes); // one per buffer
    }

    [Fact]
    public void A_new_ambient_or_a_changed_wall_recomputes_the_frame_in_place()
    {
        var g = Hall();
        var pair = new LightFramePair(g);
        var night = new Ambient(0, 0, DayPhase.Night); var day = new Ambient(200, 0, DayPhase.Day);
        LightSource[] torch = [new LightSource(20, 10, 0, 6, 230)];
        for (int i = 0; i < 4; i++) pair.Next(night, torch);
        Assert.Equal(2, pair.FullComputes);
        SameLight(pair.Next(day, torch), g.Compute(day, torch));
        SameLight(pair.Next(day, torch), g.Compute(day, torch));
        Assert.Equal(4, pair.FullComputes);
        g.SetOpaque(25, 10, 0, true); // a wall changes where the torch reaches
        SameLight(pair.Next(day, torch), g.Compute(day, torch));
        SameLight(pair.Next(day, torch), g.Compute(day, torch));
        Assert.Equal(6, pair.FullComputes);
    }

    [Fact]
    public void The_frame_just_published_stays_intact_while_the_next_is_written()
    {
        var g = Hall();
        var pair = new LightFramePair(g);
        var night = new Ambient(0, 0, DayPhase.Night);
        var f1 = pair.Next(night, [new LightSource(5, 5, 0, 6, 230)]);
        var copy = f1.PointSpan.ToArray();
        var f2 = pair.Next(night, [new LightSource(15, 5, 0, 6, 230)]);
        Assert.NotSame(f1, f2);
        Assert.True(f1.PointSpan.SequenceEqual(copy), "the previous frame was rewritten while it was still current");
        Assert.Same(f1, pair.Next(night, [new LightSource(25, 5, 0, 6, 230)])); // two calls later it is recycled
    }

    [Fact]
    public void Plain_compute_still_returns_independent_frames()
    {
        var g = Hall();
        var night = new Ambient(0, 0, DayPhase.Night);
        var a = g.Compute(night, [new LightSource(5, 5, 0, 6, 230)]);
        var b = g.Compute(night, [new LightSource(15, 5, 0, 6, 230)]);
        Assert.NotSame(a, b);
        Assert.NotEqual(a.Point(5, 5, 0), b.Point(5, 5, 0));
    }
}
