using Ravensguard.Sim.Core;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.WorldGen.Chunks;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Growth;

/// <summary>A change to the region that visited chunks must learn about: <see cref="Commit"/> after the region has been changed.</summary>
public interface IGrowthChange { void Commit(); }

/// <summary>
/// Where growth tells the chunk store about new buildings and fields (plan 2.2: a place the player has visited never changes silently). <see cref="Begin"/> is called with
/// the region rectangles a change touches BEFORE the region is changed; the returned change is committed after it.
/// </summary>
public interface IGrowthChunkSink { IGrowthChange Begin(IReadOnlyList<RectM> touched); }

/// <summary>
/// Writes the difference growth makes to every visited chunk as ID-keyed <see cref="ChunkOp"/>s (SetTile for cells, AddObject and RemoveObject for props, which are keyed by stable
/// ids), never a regeneration: the chunk as generated before the change is compared with the chunk generated after it, so whatever the player changed elsewhere stays.
/// A chunk without a snapshot was never visited: it takes no delta and is generated from the changed plan when it is first seen.
/// </summary>
public sealed class ChunkDeltaWriter : IGrowthChunkSink
{
    private readonly ChunkGenerator _gen; private readonly ChunkStore _store; private readonly int _ox, _oy;
    public ChunkDeltaWriter(ChunkGenerator generator, ChunkStore store, int chunkOffsetX = 0, int chunkOffsetY = 0) { _gen = generator; _store = store; _ox = chunkOffsetX; _oy = chunkOffsetY; }
    public long OpsWritten, ChunksChanged;
    public const int PadM = 8;

    public IGrowthChange Begin(IReadOnlyList<RectM> touched)
    {
        var keys = new SortedSet<(int X, int Y)>();
        foreach (var q in touched)
        {
            int x0 = Math.Max(0, (q.X - PadM) / ChunkGenerator.Size), x1 = Math.Min(ChunkGenerator.ChunksPerSide - 1, (q.Right + PadM) / ChunkGenerator.Size);
            int y0 = Math.Max(0, (q.Y - PadM) / ChunkGenerator.Size), y1 = Math.Min(ChunkGenerator.ChunksPerSide - 1, (q.Bottom + PadM) / ChunkGenerator.Size);
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) keys.Add((x, y));
        }
        var before = new List<((int X, int Y) Key, GeneratedChunk Old)>();
        foreach (var k in keys)
            if (_store.GetSnapshot(new ChunkKey(_ox + k.X, _oy + k.Y, 0)) != null) before.Add((k, _gen.GenerateLevels(k.X, k.Y)));
        return new Change(this, before);
    }

    private sealed class Change(ChunkDeltaWriter w, List<((int X, int Y) Key, GeneratedChunk Old)> before) : IGrowthChange
    {
        public void Commit()
        {
            foreach (var (k, old) in before)
            {
                var neu = w._gen.GenerateLevels(k.X, k.Y); bool any = false;
                var zs = new SortedSet<int>(old.Levels.Keys); zs.UnionWith(neu.Levels.Keys);
                foreach (int z in zs)
                {
                    var key = new ChunkKey(w._ox + k.X, w._oy + k.Y, z);
                    var ops = Diff(old.Levels.GetValueOrDefault(z), neu.Levels.GetValueOrDefault(z), w._store.Rebuild(key));
                    if (ops.Count == 0) continue;
                    w._store.AppendDelta(key, ops); w.OpsWritten += ops.Count; any = true;
                }
                if (any) w.ChunksChanged++;
            }
        }
    }

    /// <summary>The ops that turn <paramref name="old"/> into <paramref name="neu"/>: SetTile for every cell that differs (0 clears), RemoveObject for props that are gone, AddObject for new ones not already present in <paramref name="current"/>.</summary>
    public static List<ChunkOp> Diff(ChunkContent? old, ChunkContent? neu, ChunkContent? current)
    {
        var ops = new List<ChunkOp>(); old ??= new ChunkContent(); neu ??= new ChunkContent();
        var idx = new SortedSet<int>(old.Tiles.Keys); idx.UnionWith(neu.Tiles.Keys);
        foreach (int i in idx)
        {
            int a = old.Tiles.GetValueOrDefault(i), b = neu.Tiles.GetValueOrDefault(i);
            if (a != b) ops.Add(ChunkOp.SetTile(i % ChunkContent.Size, i / ChunkContent.Size, b));
        }
        foreach (var (id, o) in old.Objects) if (!neu.Objects.ContainsKey(id)) ops.Add(ChunkOp.RemoveObject(new EntityId(id)));
        foreach (var (id, o) in neu.Objects)
        {
            if (old.Objects.TryGetValue(id, out var oo) && oo.Data.AsSpan().SequenceEqual(o.Data)) continue;
            if (current != null && current.Objects.ContainsKey(id)) { if (old.Objects.ContainsKey(id)) ops.Add(ChunkOp.RemoveObject(new EntityId(id))); else continue; }
            ops.Add(ChunkOp.AddObject(new EntityId(id), o.Data));
        }
        return ops;
    }
}
