using MessagePack;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Save;

/// <summary>Chunk coordinate. Z is a vertical level in -5..+3 (plan 2.2a).</summary>
[MessagePackObject]
public readonly record struct ChunkKey([property: Key(0)] int X, [property: Key(1)] int Y, [property: Key(2)] int Z)
    : IComparable<ChunkKey>
{
    public const int MinZ = -5, MaxZ = 3;
    [IgnoreMember] public bool IsValid => Z >= MinZ && Z <= MaxZ;
    public int CompareTo(ChunkKey o)
    {
        int c = Z.CompareTo(o.Z); if (c != 0) return c;
        c = Y.CompareTo(o.Y); return c != 0 ? c : X.CompareTo(o.X);
    }
    public override string ToString() => $"({X},{Y},z{Z})";
}

[MessagePackObject]
public sealed class ObjectRecord
{
    [Key(0)] public ulong Id { get; set; }
    [Key(1)] public byte[] Data { get; set; } = Array.Empty<byte>();
    /// <summary>Sorted by key (ordinal).</summary>
    [Key(2)] public SortedDictionary<string, byte[]> Props { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>Decoded chunk content: sparse tile ids (local index y*Size+x) plus objects keyed by id.</summary>
[MessagePackObject]
public sealed class ChunkContent
{
    public const int Size = 32; // plan §1: a chunk is 32 × 32 cells
    [Key(0)] public SortedDictionary<int, int> Tiles { get; set; } = new();
    [Key(1)] public SortedDictionary<ulong, ObjectRecord> Objects { get; set; } = new();
    [IgnoreMember] public bool IsEmpty => Tiles.Count == 0 && Objects.Count == 0;

    public static int TileIndex(int localX, int localY) => localY * Size + localX;
}

/// <summary>Generated chunk as first visited. Payload is LZ4-compressed MessagePack of ChunkContent.</summary>
[MessagePackObject]
public sealed class ChunkSnapshot
{
    [Key(0)] public int GeneratorVersion { get; set; }
    [Key(1)] public string ContentHash { get; set; } = "";
    [Key(2)] public byte[] Payload { get; set; } = Array.Empty<byte>();

    public static ChunkSnapshot Create(int generatorVersion, string contentHash, ChunkContent content) => new()
    {
        GeneratorVersion = generatorVersion, ContentHash = contentHash, Payload = SaveCodec.SerializeCompressed(content),
    };
    public ChunkContent Decode() => SaveCodec.DeserializeCompressed<ChunkContent>(Payload);
}

public enum ChunkOpKind : byte { AddObject = 0, RemoveObject = 1, SetProperty = 2, SetTile = 3 }

[MessagePackObject]
public sealed class ChunkOp
{
    [Key(0)] public ChunkOpKind Kind { get; set; }
    /// <summary>EntityId.Value of the stable object id (unused for SetTile).</summary>
    [Key(1)] public ulong ObjectId { get; set; }
    [Key(2)] public string? Key { get; set; }
    [Key(3)] public byte[]? Value { get; set; }   // AddObject: object data; SetProperty: property value
    [Key(4)] public int LocalX { get; set; }
    [Key(5)] public int LocalY { get; set; }
    [Key(6)] public int TileId { get; set; }

    public static ChunkOp AddObject(EntityId id, byte[] data) => new() { Kind = ChunkOpKind.AddObject, ObjectId = id.Value, Value = data };
    public static ChunkOp RemoveObject(EntityId id) => new() { Kind = ChunkOpKind.RemoveObject, ObjectId = id.Value };
    public static ChunkOp SetProperty(EntityId id, string key, byte[] value) => new() { Kind = ChunkOpKind.SetProperty, ObjectId = id.Value, Key = key, Value = value };
    public static ChunkOp SetTile(int x, int y, int tileId)
    {
        CheckLocal(x, y);
        return Make(x, y, tileId);
    }

    internal static void CheckLocal(int x, int y)
    {
        if ((uint)x >= ChunkContent.Size) throw new ArgumentOutOfRangeException(nameof(x), x, $"Local x must be 0..{ChunkContent.Size - 1}.");
        if ((uint)y >= ChunkContent.Size) throw new ArgumentOutOfRangeException(nameof(y), y, $"Local y must be 0..{ChunkContent.Size - 1}.");
    }

    private static ChunkOp Make(int x, int y, int tileId) => new() { Kind = ChunkOpKind.SetTile, LocalX = x, LocalY = y, TileId = tileId };
}

/// <summary>Ordered list of operations applied on top of the snapshot.</summary>
[MessagePackObject]
public sealed class ChunkDelta
{
    [Key(0)] public List<ChunkOp> Ops { get; set; } = new();

    public void ApplyTo(ChunkContent c)
    {
        foreach (var op in Ops)
        {
            switch (op.Kind)
            {
                case ChunkOpKind.AddObject:
                    if (c.Objects.ContainsKey(op.ObjectId))
                        throw new InvalidOperationException($"AddObject: object {op.ObjectId} already exists (remove it first to replace it).");
                    c.Objects[op.ObjectId] = new ObjectRecord { Id = op.ObjectId, Data = op.Value ?? Array.Empty<byte>() };
                    break;
                case ChunkOpKind.RemoveObject:
                    c.Objects.Remove(op.ObjectId);
                    break;
                case ChunkOpKind.SetProperty:
                    // Removed objects ignore later property writes (stable-ID keyed, not positional).
                    if (c.Objects.TryGetValue(op.ObjectId, out var o) && op.Key is not null)
                        o.Props[op.Key] = op.Value ?? Array.Empty<byte>();
                    break;
                case ChunkOpKind.SetTile:
                    ChunkOp.CheckLocal(op.LocalX, op.LocalY);
                    c.Tiles[ChunkContent.TileIndex(op.LocalX, op.LocalY)] = op.TileId;
                    break;
                default:
                    throw new ArgumentException($"Unknown chunk op kind {(int)op.Kind}.");
            }
        }
    }

    /// <summary>Structural checks that do not need the chunk content.</summary>
    internal static void Validate(ChunkOp op)
    {
        switch (op.Kind)
        {
            case ChunkOpKind.AddObject: case ChunkOpKind.RemoveObject: break;
            case ChunkOpKind.SetProperty:
                if (string.IsNullOrEmpty(op.Key)) throw new ArgumentException("SetProperty needs a non-empty key.");
                break;
            case ChunkOpKind.SetTile: ChunkOp.CheckLocal(op.LocalX, op.LocalY); break;
            default: throw new ArgumentException($"Unknown chunk op kind {(int)op.Kind}.");
        }
    }
}

[MessagePackObject]
public sealed class ChunkEntry
{
    [Key(0)] public ChunkKey Key { get; set; }
    [Key(1)] public ChunkSnapshot? Snapshot { get; set; }
    [Key(2)] public ChunkDelta Delta { get; set; } = new();

    // Change tracking for the incremental section writer: the store bumps Version on every change it makes, and a worker may park the entry's
    // encoded bytes here for the version it encoded. The pair is one immutable reference, so a reader never sees bytes of another version.
    internal sealed record EncodedBytes(int Version, byte[] Bytes);
    private int _version;
    private EncodedBytes? _encoded;
    internal int Version => Volatile.Read(ref _version);
    internal void Bump() => Interlocked.Increment(ref _version);
    internal byte[]? CachedFor(int version) => Volatile.Read(ref _encoded) is { } e && e.Version == version ? e.Bytes : null;
    internal void Cache(int version, byte[] bytes) { if (Version == version) Volatile.Write(ref _encoded, new EncodedBytes(version, bytes)); }
}

/// <summary>A pre-encoded <see cref="ChunkEntry"/> (its MessagePack bytes), written verbatim so the section stays byte-identical to encoding the entry itself.</summary>
[MessagePackFormatter(typeof(RawEntryFormatter))]
public sealed class RawEntry(byte[] bytes) { public byte[] Bytes { get; } = bytes; }

public sealed class RawEntryFormatter : MessagePack.Formatters.IMessagePackFormatter<RawEntry>
{
    public void Serialize(ref MessagePackWriter writer, RawEntry value, MessagePackSerializerOptions options) => writer.WriteRaw(value.Bytes);
    public RawEntry Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) => throw new NotSupportedException("write-only");
}

/// <summary>Same wire layout as <see cref="ChunkStoreData"/>, but with pre-encoded entries.</summary>
[MessagePackObject]
public sealed class ChunkStoreRaw
{
    [Key(0)] public List<RawEntry> Chunks { get; set; } = new();
    /// <summary>See <see cref="ChunkStoreData.Palette"/>.</summary>
    [Key(1)] public string[] Palette { get; set; } = [];
}

[MessagePackObject]
public sealed class ChunkStoreData
{
    [Key(0)] public List<ChunkEntry> Chunks { get; set; } = new();
    /// <summary>
    /// The build-piece palette the tile codes of this section were written with (save format v4): the piece id of number i+1 ("" for an unused number). Empty in older sections and
    /// in a store no generator was ever attached to: read as <see cref="Ravensguard.Sim.WorldGen.Chunks.PiecePalette.LegacyIds"/>.
    /// </summary>
    [Key(1)] public string[] Palette { get; set; } = [];
}

/// <summary>Snapshot on first visit + appended deltas. Only chunks with content are stored.</summary>
public sealed class ChunkStore
{
    public const string SectionName = "chunks";
    private readonly SortedDictionary<ChunkKey, ChunkEntry> _chunks = new();
    // The streamer on the main thread and a region being paged in or out on the tick worker use the same store; every public member takes this lock.
    private readonly object _lock = new();

    public int Count { get { lock (_lock) return _chunks.Count; } }
    public bool Contains(ChunkKey k) { lock (_lock) return _chunks.ContainsKey(k); }

    private string[] _palette = [];
    /// <summary>The build-piece palette the tile codes in this store are numbered by (piece id of number i+1); empty until a generator is attached (<see cref="AdoptPalette"/>). Saved with every section.</summary>
    public IReadOnlyList<string> Palette { get { lock (_lock) return _palette; } }

    /// <summary>
    /// Makes the store's tile codes use <paramref name="palette"/> (a streamer attaches its generator's): entries written with another palette (a save made before a content update or with
    /// other mods, or before v4 when none was stored) are renumbered, snapshot and delta alike, so a visited place keeps its furniture. Returns the entries changed.
    /// </summary>
    public int AdoptPalette(Ravensguard.Sim.WorldGen.Chunks.PiecePalette palette)
    {
        lock (_lock)
        {
            var ids = palette.Ids.ToArray();
            if (_palette.SequenceEqual(ids)) return 0;
            int n = 0;
            if (_chunks.Count > 0 && palette.MapFrom(_palette) is { } map) foreach (var e in _chunks.Values) if (Renumber(e, map)) n++;
            _palette = ids;
            return n;
        }
    }

    /// <summary>Renumbers one entry's tile codes (snapshot payload and SetTile ops) with a palette map; the content hash stays (it is taken over piece ids).</summary>
    private static bool Renumber(ChunkEntry e, int[] map)
    {
        bool changed = false;
        if (e.Snapshot is { } snap)
        {
            var c = snap.Decode(); bool any = false;
            foreach (var k in c.Tiles.Keys.ToList()) { int t = c.Tiles[k], nt = Ravensguard.Sim.WorldGen.Chunks.PiecePalette.Remap(t, map); if (nt != t) { c.Tiles[k] = nt; any = true; } }
            if (any) { e.Snapshot = ChunkSnapshot.Create(snap.GeneratorVersion, snap.ContentHash, c); changed = true; }
        }
        for (int i = 0; i < e.Delta.Ops.Count; i++)
        {
            var op = e.Delta.Ops[i];
            if (op.Kind != ChunkOpKind.SetTile) continue;
            int nt = Ravensguard.Sim.WorldGen.Chunks.PiecePalette.Remap(op.TileId, map);
            if (nt != op.TileId) { e.Delta.Ops[i] = ChunkOp.SetTile(op.LocalX, op.LocalY, nt); changed = true; }
        }
        if (changed) e.Bump();
        return changed;
    }
    public IEnumerable<ChunkKey> Keys { get { lock (_lock) return _chunks.Keys.ToList(); } }

    private static void Check(ChunkKey k)
    {
        if (!k.IsValid) throw new ArgumentOutOfRangeException(nameof(k), $"Z must be in {ChunkKey.MinZ}..{ChunkKey.MaxZ}.");
    }

    /// <summary>
    /// Records the generated chunk on first visit. Returns false if a snapshot is already recorded or the chunk is empty (not stored).
    /// A delta-only entry (ops that landed before any snapshot) gets the snapshot attached and keeps its ops.
    /// </summary>
    public bool RecordFirstVisit(ChunkKey key, int generatorVersion, string contentHash, ChunkContent content)
        => RecordFirstVisit(key, content, () => ChunkSnapshot.Create(generatorVersion, contentHash, content));

    /// <summary>Same, with the snapshot already built (it can be made on a worker thread: serialising and compressing a town chunk takes milliseconds).</summary>
    public bool RecordFirstVisit(ChunkKey key, ChunkSnapshot snapshot, ChunkContent content) => RecordFirstVisit(key, content, () => snapshot);

    private bool RecordFirstVisit(ChunkKey key, ChunkContent content, Func<ChunkSnapshot> snapshot)
    {
        lock (_lock) return RecordFirstVisitLocked(key, content, snapshot);
    }

    private bool RecordFirstVisitLocked(ChunkKey key, ChunkContent content, Func<ChunkSnapshot> snapshot)
    {
        Check(key);
        if (content.IsEmpty) return false;
        if (_chunks.TryGetValue(key, out var e))
        {
            if (e.Snapshot != null) return false;
            var snap = snapshot();
            e.Delta.ApplyTo(snap.Decode()); // the old ops must still apply cleanly
            e.Snapshot = snap;
            e.Bump();
            return true;
        }
        _chunks[key] = new ChunkEntry { Key = key, Snapshot = snapshot() };
        return true;
    }

    /// <summary>
    /// Appends ops (all or nothing). Every op is validated and dry-run against the current content first: bad coordinates,
    /// unknown kinds and AddObject over an existing id throw and leave the store untouched.
    /// Creates a snapshot-less entry if a delta lands on a chunk that had no stored content.
    /// </summary>
    public void AppendDelta(ChunkKey key, IEnumerable<ChunkOp> ops)
    {
        lock (_lock) AppendDeltaLocked(key, ops);
    }

    private void AppendDeltaLocked(ChunkKey key, IEnumerable<ChunkOp> ops)
    {
        Check(key);
        var list = ops.ToList();
        if (list.Count == 0) return;
        foreach (var op in list) ChunkDelta.Validate(op);
        _chunks.TryGetValue(key, out var e);
        var probe = e == null ? new ChunkContent() : RebuildLocked(key)!;
        new ChunkDelta { Ops = list }.ApplyTo(probe);
        if (e == null) _chunks[key] = e = new ChunkEntry { Key = key };
        e.Delta.Ops.AddRange(list);
        e.Bump();
    }

    public ChunkSnapshot? GetSnapshot(ChunkKey key) { lock (_lock) return _chunks.TryGetValue(key, out var e) ? e.Snapshot : null; }
    public int DeltaOpCount(ChunkKey key) { lock (_lock) return _chunks.TryGetValue(key, out var e) ? e.Delta.Ops.Count : 0; }

    /// <summary>Current state = snapshot + deltas. Null if the chunk was never stored.</summary>
    public ChunkContent? Rebuild(ChunkKey key)
    {
        lock (_lock) return RebuildLocked(key);
    }

    private ChunkContent? RebuildLocked(ChunkKey key)
    {
        if (!_chunks.TryGetValue(key, out var e)) return null;
        var c = e.Snapshot?.Decode() ?? new ChunkContent();
        e.Delta.ApplyTo(c);
        return c;
    }

    /// <summary>
    /// A point-in-time copy for background encoding: O(entries) on the calling thread (entries whose encoding is already cached are only referenced; the
    /// others copy their op list). Snapshot payloads and ops are never mutated after they are created, so the worker can read them while play goes on.
    /// </summary>
    public ChunkStoreSnapshot Snapshot() => Snapshot(null);

    /// <summary>The same for the entries <paramref name="pick"/> accepts (null: all).</summary>
    public ChunkStoreSnapshot Snapshot(Func<ChunkKey, bool>? pick)
    {
        lock (_lock) return SnapshotLocked(pick);
    }

    private ChunkStoreSnapshot SnapshotLocked(Func<ChunkKey, bool>? pick)
    {
        var list = pick == null ? _chunks.Values.ToList() : _chunks.Values.Where(e => pick(e.Key)).ToList();
        var items = new ChunkStoreSnapshot.Item[list.Count];
        int i = 0;
        foreach (var e in list)
        {
            int v = e.Version;
            var cached = e.CachedFor(v);
            items[i++] = cached != null ? new(e, v, cached, null, null) : new(e, v, null, e.Snapshot, e.Delta.Ops.ToArray());
        }
        return new ChunkStoreSnapshot(items, _palette);
    }

    public byte[] ToSection() => Snapshot().Encode();

    /// <summary>The section bytes of the entries <paramref name="pick"/> accepts (a region's share of the store); empty store gives the encoding of an empty list.</summary>
    public byte[] ToSection(Func<ChunkKey, bool> pick)
    {
        lock (_lock) return ToSectionLocked(pick);
    }

    private byte[] ToSectionLocked(Func<ChunkKey, bool> pick)
    {
        var raw = new ChunkStoreRaw { Palette = _palette };
        foreach (var e in _chunks.Values)
        {
            if (!pick(e.Key)) continue;
            int v = e.Version;
            var bytes = e.CachedFor(v);
            if (bytes == null) { bytes = SaveCodec.Serialize(new ChunkEntry { Key = e.Key, Snapshot = e.Snapshot, Delta = new ChunkDelta { Ops = [.. e.Delta.Ops] } }); e.Cache(v, bytes); }
            raw.Chunks.Add(new RawEntry(bytes));
        }
        return SaveCodec.SerializeCompressed(raw);
    }

    /// <summary>Removes the entries <paramref name="pick"/> accepts (a region is paged out) and returns how many went.</summary>
    public int RemoveWhere(Func<ChunkKey, bool> pick)
    {
        lock (_lock) return RemoveWhereLocked(pick);
    }

    private int RemoveWhereLocked(Func<ChunkKey, bool> pick)
    {
        var gone = _chunks.Keys.Where(pick).ToList();
        foreach (var k in gone) _chunks.Remove(k);
        return gone.Count;
    }

    /// <summary>Adds the entries of a section (a region is paged in); existing keys are kept (they are newer).</summary>
    public int Absorb(ReadOnlyMemory<byte> section)
    {
        if (section.Length == 0) return 0;
        var sec = SaveCodec.DeserializeCompressed<ChunkStoreData>(section); var data = sec.Chunks; // decoded outside the lock
        int n = 0;
        lock (_lock)
        {
            // a section written with another palette is renumbered to the store's (the store has one once a streamer adopted it)
            if (_palette.Length == 0 && _chunks.Count == 0) _palette = sec.Palette ?? [];        // an empty store takes the section's numbering as it is
            int[]? map = _palette.SequenceEqual(sec.Palette ?? []) ? null
                : new Ravensguard.Sim.WorldGen.Chunks.PiecePalette(_palette.Length > 0 ? _palette.Where(i => i.Length > 0) : Ravensguard.Sim.WorldGen.Chunks.PiecePalette.LegacyIds).MapFrom(sec.Palette);
            foreach (var e in data) if (_chunks.TryAdd(e.Key, e)) { n++; if (map != null) Renumber(e, map); }
        }
        return n;
    }

    public static ChunkStore FromSection(ReadOnlyMemory<byte> bytes)
    {
        var data = SaveCodec.DeserializeCompressed<ChunkStoreData>(bytes);
        var s = new ChunkStore { _palette = data.Palette ?? [] };
        foreach (var e in data.Chunks) s._chunks[e.Key] = e;
        return s;
    }
}

/// <summary>See <see cref="ChunkStore.Snapshot"/>. <see cref="Encode"/> may run on any thread.</summary>
public sealed class ChunkStoreSnapshot
{
    internal readonly record struct Item(ChunkEntry Entry, int Version, byte[]? Cached, ChunkSnapshot? Snapshot, ChunkOp[]? Ops);
    private readonly Item[] _items;
    private readonly string[] _palette;
    internal ChunkStoreSnapshot(Item[] items, string[] palette) { _items = items; _palette = palette; }
    public int Count => _items.Length;
    /// <summary>Entries encoded by the last <see cref="Encode"/> call (the rest came from the per-entry cache).</summary>
    public int ReEncoded { get; private set; }

    public byte[] Encode()
    {
        var raw = new ChunkStoreRaw { Palette = _palette };
        int fresh = 0;
        foreach (var it in _items)
        {
            var bytes = it.Cached;
            if (bytes == null)
            {
                var copy = new ChunkEntry { Key = it.Entry.Key, Snapshot = it.Snapshot, Delta = new ChunkDelta { Ops = [.. it.Ops!] } };
                bytes = SaveCodec.Serialize(copy);
                it.Entry.Cache(it.Version, bytes);
                fresh++;
            }
            raw.Chunks.Add(new RawEntry(bytes));
        }
        ReEncoded = fresh;
        return SaveCodec.SerializeCompressed(raw);
    }
}
