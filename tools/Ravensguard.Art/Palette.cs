using System.Text.Json;

namespace Ravensguard.Art;

/// <summary>Colour lookup by name; colours are packed 0xAARRGGBB (0 = transparent).</summary>
public sealed class ColorLookup
{
    private readonly Dictionary<string, uint> _map;
    internal ColorLookup(Dictionary<string, uint> map) => _map = map;
    public uint this[string name] => _map.TryGetValue(name, out var c) ? c : throw new KeyNotFoundException($"Unknown palette colour '{name}'");
}

/// <summary>Ramp lookup by name; entries are dark to light.</summary>
public sealed class RampLookup
{
    private readonly Dictionary<string, uint[]> _map;
    internal RampLookup(Dictionary<string, uint[]> map) => _map = map;
    public uint[] this[string name] => _map.TryGetValue(name, out var r) ? r : throw new KeyNotFoundException($"Unknown palette ramp '{name}'");
    public bool Has(string name) => _map.ContainsKey(name);
}

public sealed class Palette
{
    public Dictionary<string, uint> Colors { get; }
    public Dictionary<string, string[]> RampNames { get; }
    public Dictionary<string, uint[]> Ramps { get; }
    public ColorLookup P { get; }
    public RampLookup R { get; }
    private readonly HashSet<uint> _set;

    private Palette(Dictionary<string, uint> colors, Dictionary<string, string[]> rampNames)
    {
        Colors = colors;
        RampNames = rampNames;
        Ramps = rampNames.ToDictionary(kv => kv.Key, kv => kv.Value.Select(n => colors[n]).ToArray());
        P = new ColorLookup(colors);
        R = new RampLookup(Ramps);
        _set = new HashSet<uint>(colors.Values);
    }

    public bool Contains(uint c) => _set.Contains(c);
    public string NameOf(uint c) => Colors.FirstOrDefault(kv => kv.Value == c).Key ?? $"#{c & 0xFFFFFF:x6}";

    public static uint ParseHex(string hex)
    {
        var s = hex.TrimStart('#');
        return 0xFF000000u | Convert.ToUInt32(s, 16);
    }

    public static Palette Load(string path)
    {
        var opts = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        using var doc = JsonDocument.Parse(File.ReadAllText(path), opts);
        var colors = new Dictionary<string, uint>();
        foreach (var p in doc.RootElement.GetProperty("colors").EnumerateObject())
            colors[p.Name] = ParseHex(p.Value.GetString()!);
        var ramps = new Dictionary<string, string[]>();
        foreach (var p in doc.RootElement.GetProperty("ramps").EnumerateObject())
        {
            var names = p.Value.EnumerateArray().Select(e => e.GetString()!).ToArray();
            foreach (var n in names)
                if (!colors.ContainsKey(n)) throw new InvalidDataException($"Ramp '{p.Name}' references unknown colour '{n}'");
            ramps[p.Name] = names;
        }
        return new Palette(colors, ramps);
    }
}
