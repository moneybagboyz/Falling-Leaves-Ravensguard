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
    readonly Dictionary<string, Image> _images = new();
    readonly Dictionary<string, AtlasTexture> _cache = new();

    static ArtAtlas? _shared;
    /// <summary>The one decoded atlas set, shared by the street view and the blueprint editor (decoded once per run).</summary>
    public static ArtAtlas Shared => _shared ??= Load();

    /// <summary>Text of a res:// file through the engine's file access, so it works in an exported build (no host file system path).</summary>
    public static string ReadText(string resPath)
    {
        using var f = Godot.FileAccess.Open(resPath, Godot.FileAccess.ModeFlags.Read) ?? throw new System.IO.FileNotFoundException(resPath + ": " + Godot.FileAccess.GetOpenError());
        return f.GetAsText();
    }

    /// <summary>Pixels of an imported res:// texture (lossless import), RGBA8.</summary>
    public static Image ReadImage(string resPath)
    {
        var tex = ResourceLoader.Load<Texture2D>(resPath) ?? throw new System.IO.FileNotFoundException("texture " + resPath);
        var img = tex.GetImage();
        img.Convert(Image.Format.Rgba8);
        return img;
    }

    public static ArtAtlas Load()
    {
        var a = new ArtAtlas();
        string idx = ReadText(Dir + "index.json");
        using var doc = JsonDocument.Parse(idx);
        foreach (var at in doc.RootElement.GetProperty("atlases").EnumerateObject())
        {
            var img = ReadImage(Dir + at.Value.GetProperty("file").GetString());
            a._images[at.Name] = img;
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
    public bool HasAtlas(string name) => _textures.ContainsKey(name);

    /// <summary>The pixels of one sprite as its own image (for tiled textures, shader lookup strips and the chunk baker).</summary>
    public Image Crop(string id)
    {
        var r = Get(id);
        return _images[r.Atlas].GetRegion(new Rect2I(r.X, r.Y, r.W, r.H));
    }

    /// <summary>Several same-sized sprites side by side in one image (animation frames, a snow strip for a shader).</summary>
    public Image Strip(params string[] ids)
    {
        var first = Get(ids[0]);
        var img = Image.CreateEmpty(first.W * ids.Length, first.H, false, Image.Format.Rgba8);
        for (int i = 0; i < ids.Length; i++) img.BlitRect(Crop(ids[i]), new Rect2I(0, 0, first.W, first.H), new Vector2I(i * first.W, 0));
        return img;
    }

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
