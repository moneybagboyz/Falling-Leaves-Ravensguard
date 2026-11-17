using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace Ravensguard.Game.World;

/// <summary>Baked sprite atlases (tools/Ravensguard.Art output copied to assets/art) plus the id to region index.</summary>
public sealed class ArtAtlas
{
    public const string Dir = "res://assets/art/";
    public readonly record struct Region(string Atlas, int X, int Y, int W, int H);

    readonly Dictionary<string, Region> _sprites = new();
    readonly Dictionary<string, ImageTexture> _textures = new();
    readonly Dictionary<string, AtlasTexture> _cache = new();

    public static ArtAtlas Load()
    {
        var a = new ArtAtlas();
        string idx = System.IO.File.ReadAllText(ProjectSettings.GlobalizePath(Dir + "index.json"));
        using var doc = JsonDocument.Parse(idx);
        foreach (var at in doc.RootElement.GetProperty("atlases").EnumerateObject())
        {
            var img = Image.LoadFromFile(ProjectSettings.GlobalizePath(Dir + at.Value.GetProperty("file").GetString()));
            img.Convert(Image.Format.Rgba8);
            a._textures[at.Name] = ImageTexture.CreateFromImage(img);
        }
        foreach (var sp in doc.RootElement.GetProperty("sprites").EnumerateObject())
        {
            var v = sp.Value;
            a._sprites[sp.Name] = new Region(v.GetProperty("atlas").GetString()!, v.GetProperty("x").GetInt32(), v.GetProperty("y").GetInt32(), v.GetProperty("w").GetInt32(), v.GetProperty("h").GetInt32());
        }
        return a;
    }

    public bool Has(string id) => _sprites.ContainsKey(id);
    public Region Get(string id) => _sprites.TryGetValue(id, out var r) ? r : throw new KeyNotFoundException("no sprite " + id);
    public ImageTexture Atlas(string name) => _textures[name];
    public IEnumerable<KeyValuePair<string, Region>> All => _sprites;

    public AtlasTexture Texture(string id)
    {
        if (_cache.TryGetValue(id, out var t)) return t;
        var r = Get(id);
        t = new AtlasTexture { Atlas = _textures[r.Atlas], Region = new Rect2(r.X, r.Y, r.W, r.H) };
        _cache[id] = t;
        return t;
    }
}
