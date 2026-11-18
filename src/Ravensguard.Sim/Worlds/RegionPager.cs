namespace Ravensguard.Sim.Worlds;

/// <summary>Where a paged-out region's section bytes live: memory (tests, the sections of a loaded save) or a folder (the game).</summary>
public interface IRegionPager
{
    void Write(int regionId, byte[] section);
    byte[]? Read(int regionId);
    bool Has(int regionId);
    void Delete(int regionId);
    /// <summary>Ids that have a section, ascending.</summary>
    IReadOnlyList<int> Ids { get; }
}

public sealed class MemoryRegionPager : IRegionPager
{
    private readonly SortedDictionary<int, byte[]> _d = [];
    private readonly object _l = new();
    public void Write(int id, byte[] s) { lock (_l) _d[id] = s; }
    public byte[]? Read(int id) { lock (_l) return _d.TryGetValue(id, out var b) ? b : null; }
    public bool Has(int id) { lock (_l) return _d.ContainsKey(id); }
    public void Delete(int id) { lock (_l) _d.Remove(id); }
    public IReadOnlyList<int> Ids { get { lock (_l) return [.. _d.Keys]; } }
}

/// <summary>One file per region (<c>r&lt;id&gt;.region</c>) in a folder; written atomically (temp file, then rename).</summary>
public sealed class DirectoryRegionPager : IRegionPager
{
    private readonly string _dir;
    private readonly object _l = new();
    public DirectoryRegionPager(string dir) { _dir = dir; Directory.CreateDirectory(dir); }
    private string PathOf(int id) => Path.Combine(_dir, $"r{id:D3}.region");
    public void Write(int id, byte[] s)
    {
        lock (_l)
        {
            var tmp = PathOf(id) + ".tmp";
            File.WriteAllBytes(tmp, s);
            File.Move(tmp, PathOf(id), overwrite: true);
        }
    }
    public byte[]? Read(int id) { lock (_l) return File.Exists(PathOf(id)) ? File.ReadAllBytes(PathOf(id)) : null; }
    public bool Has(int id) { lock (_l) return File.Exists(PathOf(id)); }
    public void Delete(int id) { lock (_l) if (File.Exists(PathOf(id))) File.Delete(PathOf(id)); }
    public IReadOnlyList<int> Ids
    {
        get
        {
            lock (_l)
                return [.. Directory.EnumerateFiles(_dir, "r*.region").Select(f => int.TryParse(Path.GetFileNameWithoutExtension(f).AsSpan(1), out int i) ? i : -1).Where(i => i >= 0).Order()];
        }
    }
}
