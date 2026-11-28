using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Tests.Economy;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Construction;

/// <summary>P8-LM-3 (carried in from Phase 6, holdings 3.9): cellars in library plans. A stair down makes a cellar level under the plan, not floor.</summary>
public class CellarPlanTests(ITestOutputHelper output)
{
    private const long T0 = ConstructionRig.Day10;
    private static PieceCosts Costs => PieceCosts.Of(EconFixture.Content);
    private static PlanLibrary Lib => PlanLibrary.For(EconFixture.Content);
    private static PieceCatalog Pieces => Costs.Library.Pieces;
    private static readonly FitLot TownPlot = new(500, 300, 11, 28, 0);

    [Fact]
    public void ABurgageWithACellar_KeepsItsCellarLevel_AndItsStairDown_WhereGenerationsOwnReadersStillSeeNoCellar()
    {
        var plain = Lib.Grid("burgage_middling")!;
        var dug = Lib.GridWithCellar("burgage_middling")!;
        Assert.DoesNotContain(plain.Levels.Keys, z => z < 0);                       // world generation's readers are unchanged
        Assert.Contains(dug.Levels.Keys, z => z < 0);
        var ground = dug.Level(0)!;
        Assert.Contains(Enumerable.Range(0, ground.Structure.Length), i => Pieces.KindOf(ground.Structure[i]) is PieceKind.StairsDown or PieceKind.Trapdoor);   // the stair down is a stair
        Assert.True(dug.Levels.Count > plain.Levels.Count);
        var pvPlain = Lib.Preview("burgage_middling", plain, Costs); var pvDug = Lib.Preview("burgage_middling", dug, Costs);
        Assert.True(pvDug.Cells > pvPlain.Cells && pvDug.MilliHours > pvPlain.MilliHours);       // digging and walling the cellar costs
        output.WriteLine($"cells {pvPlain.Cells} -> {pvDug.Cells}, milli-hours {pvPlain.MilliHours} -> {pvDug.MilliHours}");
    }

    [Fact]
    public void TheCellarIsPlacedWithThePlan_AndOnlyWithIt_AndNoDeeperThanTheRulesAllow()
    {
        var dug = Lib.GridWithCellar("burgage_middling")!;
        var fit = PlanFitter.Fit(dug, TownPlot, Pieces);
        Assert.True(fit.Ok, fit.Key);
        var r = ConstructionRig.Make();
        var pr = r.W.PlacePlanGrid(r.Hero, PurseKind.Person, -1, fit.Grid!, "burgage_middling", fit.X, fit.Y, T0);
        Assert.True(pr.Ok && pr.Refused.Count == 0, pr.Key + " " + string.Join(", ", pr.Refused.Take(6).Select(x => $"{x.Piece}@{x.X},{x.Y},{x.Z}:{x.Key}")));
        Assert.Contains(r.W.Book.Ghosts.Values, x => x.Z == -1);
        Assert.All(r.W.Book.Ghosts.Values, x => Assert.InRange(x.Z, -2, 2));
        // a floor dug under nothing, by hand, is still refused
        string cellarPiece = dug.Level(-1)!.Structure.First(s => s != null)!;
        var r2 = ConstructionRig.Make();
        var one = r2.W.Place(r2.Hero, PurseKind.Person, -1, [new PlaceCell(fit.X, fit.Y, -1, cellarPiece)], T0);
        Assert.False(one.Ok);
        Assert.Contains(one.Refused, x => x.Key == "cn_below_ground");
        // and a plan's cell deeper than the rule is refused
        var deep = r2.W.Place(r2.Hero, PurseKind.Person, -1, [new PlaceCell(fit.X, fit.Y, -3, cellarPiece)], T0, default, false, "burgage_middling");
        Assert.Contains(deep.Refused, x => x.Key == "cn_below_ground");
    }
}
