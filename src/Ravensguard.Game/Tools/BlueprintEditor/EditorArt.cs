using System.Collections.Generic;
using Godot;
using Ravensguard.Content;
using Ravensguard.Game.World;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.World;

namespace Ravensguard.Game.Tools.BlueprintEditor;

/// <summary>The real game sprites for build pieces: the baked atlas plus the same sprite rules <see cref="TileSprites"/> gives the street view.</summary>
public static class EditorArt
{
    static ArtAtlas? _atlas;
    public static ArtAtlas Atlas => _atlas ??= ArtAtlas.Load();

    /// <summary>First sprite of the alternatives list the atlas holds (the generator may name sprites that are not baked yet).</summary>
    public static string? Resolve(string? id)
    {
        if (id == null) return null;
        foreach (var alt in TileSprites.Alternatives(id)) if (Atlas.Has(alt)) return alt;
        return null;
    }

    public static AtlasTexture? Texture(string? id) => Resolve(id) is { } r ? Atlas.Texture(r) : null;

    /// <summary>A map holding one blueprint level (z) for <see cref="TileSprites"/>; ground outside buildings is the hamlet's default grass.</summary>
    public static HamletMap BuildMap(BuildingGrid g, PieceCatalog pieces)
    {
        var grid = new LevelGrid(g.Width, g.Height, BlueprintParser.MinZ, BlueprintParser.MaxZ);
        var m = new HamletMap(grid, pieces);
        foreach (var (z, lv) in g.Levels)
            for (int y = 0; y < lv.Height; y++)
                for (int x = 0; x < lv.Width; x++)
                {
                    int i = m.Idx(x, y, z), s = lv.Idx(x, y);
                    m.Structure[i] = lv.Structure[s];
                    m.Fixture[i] = lv.Fixture[s];
                }
        for (int y = 0; y < g.Height; y++)
            for (int x = 0; x < g.Width; x++)
                m.Ground[m.Idx(x, y, 0)] = GroundKind.Grass;
        return m;
    }

    static readonly Dictionary<(PieceCatalog, string), (string? Terrain, string? Object)> _icons = new();

    /// <summary>Terrain and object sprite ids that draw one piece on its own (for the palette). Furniture stands on a plank floor.</summary>
    public static (string? Terrain, string? Object) Icon(PieceCatalog pieces, string id)
    {
        if (_icons.TryGetValue((pieces, id), out var hit)) return hit;
        var m = new HamletMap(new LevelGrid(3, 3, 0, 0), pieces);
        for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++) m.Ground[m.Idx(x, y, 0)] = GroundKind.Grass;
        int c = m.Idx(1, 1, 0);
        var kind = pieces.KindOf(id);
        if (kind is PieceKind.Furniture or PieceKind.Workstation) { m.Structure[c] = "floor_plank"; m.Fixture[c] = id; }
        else m.Structure[c] = id;
        var r = (TileSprites.Terrain(m, 1, 1, 0), TileSprites.Object(m, 1, 1, 0));
        _icons[(pieces, id)] = r;
        return r;
    }
}
