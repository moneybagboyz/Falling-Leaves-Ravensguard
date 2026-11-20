using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Construction;

// P5-B: the live tier's hero on the cells, and the daily rust and rot of things kept in stores that stand on cells (items-quality-and-wear 4.4, now that rooms know Indoor).
public sealed partial class ConstructionWorld
{
    private long _heroCell = long.MinValue;

    /// <summary>The cell the hero stands on in the live tier (the main thread sets it each frame; a fall reads it on the tick worker). Null when not known.</summary>
    public (int X, int Y, int Z)? HeroCell
    {
        get { long v = Interlocked.Read(ref _heroCell); return v == long.MinValue ? null : ConstructionIds.Unpack(v); }
        set => Interlocked.Exchange(ref _heroCell, value is { } c ? ConstructionIds.CellKey(c.X, c.Y, c.Z) : long.MinValue);
    }

    /// <summary>The hero came down with a floor: the street view moves him to the level he landed on.</summary>
    public void HeroLanded(int x, int y, int z) => Event(ConstructionEventKind.HeroFell, Book.AdvancedSec, x, y, z, "", default);

    /// <summary>
    /// One day of rust and rot (items 4.4, the H4 daily slice; run at hour 3) for the item rows kept in stores that stand on cells: a site's pile and rubble, a chest. Indoors (a room
    /// RoomDetector v2 calls Indoor) they stay dry; out of doors they lie damp. Iron rusts 2 pm a day damp (mail twice), leather and wood rot only when wet.
    /// </summary>
    public int CorrosionDay(int day)
    {
        if (Book.Placed.Count == 0) return 0;
        var where = new Dictionary<EntityId, Exposure>();
        foreach (var p in Book.Placed.Values) where[p.Owner] = Rooms.RoomAt(p.X, p.Y, p.Z) is { Indoor: true } ? Exposure.Dry : Exposure.Damp;
        var items = Econ.Items; int lost = 0;
        foreach (int row in items.LiveRows().ToList())
        {
            ref var r = ref items.At(row);
            if (!where.TryGetValue(r.Owner, out var ex) || ex == Exposure.Dry) continue;
            var owner = r.Owner;
            lost += Corrosion.Day(items, row, ex, day, r.Shape.Contains("mail", StringComparison.Ordinal), int.MaxValue, Econ.OtherStores.GetValueOrDefault(owner), MaterialCategory);
        }
        return lost;
    }

    private string? MaterialCategory(string material) => Content != null && Content.TryGet<MaterialDef>(material, out var m) ? m.Category : null;
}

/// <summary>
/// Dev tools (the dev console's <c>build</c> and the screenshot driver only; play never calls them): materials put into a pile out of nowhere (an Import, counted by the mass ledger), and
/// pieces finished without the hours, in the order a crew would build them. Building is hands-on in play: these exist to look at a half-built site without waiting a game week.
/// </summary>
public sealed partial class ConstructionWorld
{
    public void DevStock(EntityId siteId, long nowSec)
    {
        if (Site(siteId) is not { } s) return;
        var pile = PileOf(s);
        foreach (var (g, q) in Needed(s)) { long have = pile.Qty(g); if (have < q) pile.Create(g, q - have, CreateReason.Import); }
        Refresh(s);
        Publish(nowSec);
    }

    /// <summary>Finishes up to <paramref name="pieces"/> ready ghosts of a site in build order, then starts the next <paramref name="started"/> to <paramref name="startedPm"/> of their work.</summary>
    public int DevFinish(EntityId siteId, int pieces, int started, int startedPm, long nowSec)
    {
        if (Site(siteId) is not { } s) return 0;
        int done = 0;
        var who = new CrewMember { Person = s.Owner };
        while (done < pieces)
        {
            bool rb = false;
            var g = Pick(s, who, false, ref rb);
            if (g == null || !Begin(s, g)) break;
            g.WorkDone = g.WorkNeed; g.Builder = s.Owner; g.BuiltSkill = 40;
            Finish(g, nowSec, s.Owner); done++;
            Refresh(s);
        }
        int k = 0;
        foreach (var g in GhostsOf(s.Id).Where(g => g.State == GhostState.Ready).OrderBy(g => g.Z).ThenBy(g => g.Id).ToList())
        {
            if (k++ >= started || !Begin(s, g)) break;
            g.WorkDone = g.WorkNeed * startedPm / 1000; g.State = GhostState.InProgress;
        }
        SyncStructures();
        Publish(nowSec);
        return done;
    }
}
