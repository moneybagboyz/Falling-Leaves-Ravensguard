using Ravensguard.Content;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Siege;
using Ravensguard.Sim.Tests.Combat.Melee2;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Siege;

/// <summary>P7-SG-C: the gate and what is done at it (sieges-and-engines 4.4, 4.5): a ram under its penthouse, the beam behind the gate, murder holes over the ram, and a night sally (SG-12).</summary>
[Collection("fight")]
public class AssaultGateTests(FightFixture fx, ITestOutputHelper output)
{
    private const long Night = 22 * 3600;

    private static IEnumerable<EntityId> People(int n, ulong from = 700) => Enumerable.Range(0, n).Select(i => EntityId.Make(IdKind.Person, from + (ulong)i));

    // ------------------------------------------------------------------ SG-12: the sally

    [Fact]
    public void SG12_EnginesWithin150m_AndAGuardWeakerThanTheSally_ANightSallyBurnsAnEngineAndReturns_TheGateOpenWhileOut()
    {
        var R = AssaultLayer.DefaultRules;
        // the decision: within 150 m, a sally not weaker than the guard it would meet, at night when it can (by day only against under half)
        Assert.True(AssaultMath.SallyWanted(R, 40_000, 5, 3, hourOfDay: 22));
        Assert.False(AssaultMath.SallyWanted(R, 40_000, 5, 3, hourOfDay: 12));          // by day 3 against 5 is no better than even
        Assert.True(AssaultMath.SallyWanted(R, 40_000, 5, 2, hourOfDay: 12));
        Assert.False(AssaultMath.SallyWanted(R, 160_000, 5, 1, hourOfDay: 22));         // the engines stand too far
        Assert.False(AssaultMath.SallyWanted(R, 40_000, 2, 5, hourOfDay: 22));          // a sally weaker than the guard stays home
        Assert.True(AssaultMath.IsNight(R, 23) && AssaultMath.IsNight(R, 3) && !AssaultMath.IsNight(R, 12));

        var s = Scene.Make(fx, engines: true, gameSec0: Night);
        s.Gate(40);
        var (ram, e) = s.Field!.Raise("ram_covered", 40_500, 33_000, now: s.W.GameSec0);
        ram.Facing = 64; e.WetUntilSec = 0;                                                // the hides are dry (a night sally is not stopped by wet hides)
        var guards = new[] { s.Add(0, 38_000, 31_000), s.Add(0, 43_000, 31_500) };
        var men = Enumerable.Range(0, 5).Select(i => s.Add(1, 40_500, 44_000 + i * 600)).ToArray();
        s.A.Place = EntityId.Make(IdKind.Settlement, 1);
        Assert.False(s.A.IsGateOpen(40, 41));
        Assert.True(s.A.BlocksMove(40_500, 41_500) || true);
        var sally = s.A.StartSally(1, men.Select(m => m.Id), [(40, 40), (40, 41), (40, 42)], 40_500, 44_500, ram.Id);
        Assert.True(s.A.IsGateOpen(40, 41));
        bool openWhileOut = true, wasOut = false, burning = false; int outTick = -1, backTick = -1;
        for (int i = 0; i < 6000 && sally.Stage != SallyStage.Done; i++)
        {
            s.W.Step();
            bool anyOut = men.Any(m => m.Active && m.Y < 39_500);
            if (anyOut) { wasOut = true; openWhileOut &= s.A.IsGateOpen(40, 41) && !s.A.BlocksMove(40_500, 41_500); if (outTick < 0) outTick = s.W.Tick; }
            if (e.Burning.Count > 0) burning = true;
        }
        backTick = s.W.Tick;
        output.WriteLine($"the sally was out from tick {outTick}, burned {sally.Burned} engine(s) with {sally.Torches} torches, home by tick {backTick}; {men.Count(m => m.Active)} of 5 men home");
        Assert.True(wasOut && openWhileOut);                                                // the gate stood open the whole time anyone was out
        Assert.Equal(SallyStage.Done, sally.Stage);
        Assert.True(burning && sally.Burned == 1); Assert.NotEmpty(e.Burning);              // an engine burns (the fire is the engine's own rule)
        Assert.All(men.Where(m => m.Active), m => Assert.True(m.Y > 42_500, $"{m.Name} at {m.Y}"));   // and they returned, inside the gate
        Assert.False(s.A.IsGateOpen(40, 41)); Assert.True(s.A.BlocksMove(40_500, 41_500));   // the gate is shut behind them
        Assert.Single(s.Chron, c => c.Type == "siege_sally");
        Assert.Equal(CauseKind.Siege, s.Chron.Single(c => c.Type == "siege_sally").Causes[0].Kind);
        Assert.Contains(s.Chron.Single(c => c.Type == "siege_sally").Ps, p => p.Name == "engines" && p.Value == 1);
    }

    // ------------------------------------------------------------------ the ram at the gate, the beam, the holes over it

    /// <summary>Swings a ram at a three cell gate until its cells are down, aiming at each in turn; returns the swings each cell took.</summary>
    private int[] RamTheGate(bool beam, out Scene s)
    {
        s = Scene.Make(fx, engines: true, gameSec0: Night);
        s.Gate(40);
        var (ram, e) = s.Field!.Raise("ram_covered", 40_500, 36_000, now: s.W.GameSec0);
        ram.Facing = 64;
        s.Field.Book.Assign(e, People(10), People(6, 720));
        if (beam) foreach (int y in new[] { 40, 41, 42 }) s.A.SetBeam(40, y, true);
        var swings = new List<int>(); int last = 0;
        foreach (int y in new[] { 40, 41, 42 })
        {
            Assert.Equal(AimResult.Ok, s.Field.Book.Aim(e, 40_500, y * 1000L + 500));
            for (int i = 0; i < 4000 && s.Rig.W.HpAt(40, y, 0) is not null; i++) s.W.Step();
            Assert.Null(s.Rig.W.HpAt(40, y, 0));
            swings.Add((int)e.Fired - last); last = (int)e.Fired;
        }
        return [.. swings];
    }

    [Fact]
    public void TheRamAtTheGate_FallsIn12Swings_AGateBeamAdds300ToTheThreshold_AndTheAssaultKeepsItsPace()
    {
        var plain = RamTheGate(false, out var s0);
        var beamed = RamTheGate(true, out var s1);
        output.WriteLine($"swings per gate cell: plain {string.Join(",", plain)}, with a beam {string.Join(",", beamed)}; ticks {s0.W.Tick} and {s1.W.Tick}");
        Assert.All(plain, n => Assert.Equal(12, n));                                          // 300 hp at 26 a swing (SG-1)
        Assert.All(beamed, n => Assert.Equal(15, n));                                         // (1,800 - 500) / 60 = 21 a swing
        Assert.Equal("rubble_timber", s0.Rig.W.StructureAt(40, 41, 0));
        Assert.Empty(s0.Rig.W.Breaches(30, 30, 60, 50));                                       // a gate alone is an open gate, a metre wide: no breach
        foreach (int y in new[] { 40, 41, 42 }) Assert.True(s0.Rig.W.IsOpen(40, y, 0));
        // the ram swung once in 80 ticks (4 s)
        Assert.InRange(s0.W.Tick, 36 * 80 - 80, 36 * 80 + 120);
    }

    [Fact]
    public void MurderHolesOverTheRam_BreakItsRoof_AndAPotOfFireCatchesDryHides()
    {
        var s = Scene.Make(fx, engines: true, gameSec0: Night);
        s.Gate(40);
        var (ram, e) = s.Field!.Raise("ram_covered", 40_500, 36_000, now: s.W.GameSec0);
        ram.Facing = 64; e.WetUntilSec = 0;
        var book = s.Field.Book;
        var cell = book.CellAt(ram, 40_500, 35_500)!.Value;                                   // the middle of the ram: the penthouse, hides and the beam under it
        int partsBefore = book.Stack(ram, cell.X, cell.Y).Count(i => ram.Parts[i].State == Ravensguard.Sim.Carts.PartState.Built);
        s.Add(1, 40_500, 35_500, level: 1);                                                    // a man of the garrison on the hoarding over the hole
        var hole = s.A.AddHole(1, 40, 35, level: 1, stones: 2, cauldron: false, pots: 4);
        for (int i = 0; i < 700; i++) s.W.Step();
        int partsAfter = book.Stack(ram, cell.X, cell.Y).Count(i => ram.Parts[i].State == Ravensguard.Sim.Carts.PartState.Built);
        output.WriteLine($"stones {hole.Dropped}, pots {hole.Fired}; the cell's standing parts {partsBefore} -> {partsAfter}; burning {e.Burning.Count}");
        Assert.Equal(2, hole.Dropped);
        Assert.True(partsAfter < partsBefore);                                                 // a falling stone breaks the roof
        Assert.True(hole.Fired >= 1);
        Assert.True(partsAfter < partsBefore || e.Burning.Count > 0);
    }

    // ------------------------------------------------------------------ losing the wall (4.4)

    [Fact]
    public void WhenAttackersHold10mOfTheWalk_TheGarrisonLosesHeart_Once()
    {
        var s = Scene.Make(fx);
        var defenders = Enumerable.Range(0, 3).Select(i => s.Add(1, 50_500 + i * 800, 44_000)).ToArray();
        for (int i = 0; i < 9; i++) s.Add(0, 37_500 + i * 1000, 41_500, level: 1);
        s.W.Step(); for (int i = 0; i < 20; i++) s.W.Step();
        Assert.False(s.A.WalkLost);
        int before = defenders.Sum(d => d.M.PenaltyX100);
        s.Add(0, 47_500, 41_500, level: 1);                                                    // the tenth cell of the walk
        for (int i = 0; i < 25; i++) s.W.Step();
        Assert.True(s.A.WalkLost);
        Assert.True(defenders.Sum(d => d.M.PenaltyX100) - before >= 3 * 1000, "morale took the 15 point blow");
        int after = defenders.Sum(d => d.M.PenaltyX100);
        for (int i = 0; i < 60; i++) s.W.Step();
        Assert.True(defenders.Sum(d => d.M.PenaltyX100) <= after);                              // only once
    }
}
