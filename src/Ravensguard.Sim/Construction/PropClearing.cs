using Ravensguard.Sim.Save;
using Ravensguard.Sim.WorldGen.Chunks;

namespace Ravensguard.Sim.Construction;

/// <summary>
/// P5-C: trees and props under a footprint (docs/reviews/phase-5-open.md: "clearing them should be part of building"). A piece built on a cell clears what grows or lies there (trees,
/// bushes, rocks, stumps, hay, crates, carts, hurdles ...): the prop leaves the chunk as an ID-keyed <c>RemoveObject</c> delta when the piece is laid. A few things are not a builder's to
/// clear and refuse placement: a well, graves and their lychgate, a market stall, a signpost, a lit brazier, and the markers of the old places (barrows, caves, mines, ruins, hoards).
/// The felled tree's wood is not kept (left for later: the logs of a clearing are Phase 3's gathering).
/// </summary>
public static class PropClearing
{
    public static PropHere Of(PropKind k) => k switch
    {
        PropKind.Well or PropKind.Headstone or PropKind.GraveMound or PropKind.Lychgate or PropKind.Stall or PropKind.Signpost or PropKind.Brazier => PropHere.Blocks,
        PropKind.SiteBarrow or PropKind.SiteCave or PropKind.SiteMine or PropKind.SiteRuin or PropKind.SiteBattlefield or PropKind.SiteHoard => PropHere.Blocks,
        _ => PropHere.Clears,
    };

    /// <summary>The props of a chunk's content standing on a local cell, with their object ids.</summary>
    public static IEnumerable<(ulong Id, PropRecord Prop)> At(ChunkContent c, int lx, int ly)
    {
        foreach (var (id, o) in c.Objects)
        {
            if (o.Data.Length < PropRecord.Bytes) continue;
            var p = PropRecord.Decode(o.Data);
            if (p.X == lx && p.Y == ly) yield return (id, p);
        }
    }

    /// <summary>What stands on a local cell of a chunk's content: the strongest of its props (a blocking one wins).</summary>
    public static PropHere Here(ChunkContent? c, int lx, int ly)
    {
        if (c == null) return PropHere.None;
        var here = PropHere.None;
        foreach (var (_, p) in At(c, lx, ly)) { var k = Of(p.Kind); if (k > here) here = k; }
        return here;
    }
}
