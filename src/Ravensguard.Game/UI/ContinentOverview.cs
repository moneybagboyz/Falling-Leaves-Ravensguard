using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Game.UI;

/// <summary>
/// What the world map needs to show the whole continent (seamless-regions spec 3.8): the continent and its history for the pixel overview, the region manager for
/// the regions generated so far (drawn in detail: roads, rivers, settlements), the home region and where the hero stands in continent metres.
/// </summary>
public sealed class ContinentOverview
{
    public Continent Continent { get; }
    public WorldHistory History { get; }
    public RegionManager Regions { get; }
    public RegionCoord Home { get; }
    /// <summary>The hero's position in the home-region frame (world metres); the overview adds the home origin.</summary>
    public Func<(float X, float Y)> Hero { get; }

    ImageTexture? _tex;

    public ContinentOverview(Continent continent, WorldHistory history, RegionManager regions, RegionCoord home, Func<(float X, float Y)> hero)
    {
        Continent = continent; History = history; Regions = regions; Home = home; Hero = hero;
    }

    /// <summary>The continent as pixels (<see cref="ContinentMap.Px"/> square), built on first use.</summary>
    public Texture2D Texture => _tex ??= UiKit.Tex(ContinentMap.Render(Continent, History, new MapOptions(MapLayer.Biome, Rivers: true, Towns: true, Legends: false)), ContinentMap.Px, ContinentMap.Px);

    /// <summary>The hero in overview pixels.</summary>
    public Vector2 HeroPx()
    {
        var (x, y) = Hero();
        return new Vector2((Home.OriginXM + x) / (float)RegionSettings.CellM * ContinentMap.Scale, (Home.OriginYM + y) / (float)RegionSettings.CellM * ContinentMap.Scale);
    }

    /// <summary>One line about the region square under an overview pixel.</summary>
    public string Describe(Vector2 px)
    {
        int cx = (int)(px.X / ContinentMap.Scale), cy = (int)(px.Y / ContinentMap.Scale);
        if ((uint)cx >= Continent.Size || (uint)cy >= Continent.Size) return "";
        var rc = RegionCoord.OfCell(cx, cy);
        var g = Regions.TryGet(rc);
        int i = cy * Continent.Size + cx;
        string where = Continent.Ocean[i] ? L.T("BIOME_SEA") : L.T("BIOME_" + Continent.Biomes[i].ToString().ToUpperInvariant());
        string realm = History.FinalOwner.Length > i && History.FinalOwner[i] != Continent.None && History.FinalOwner[i] < History.Realms.Count ? History.Realms[History.FinalOwner[i]].Name : "";
        return $"{rc} {where}{(realm.Length > 0 ? "  " + realm : "")}  {(g != null ? $"{g.Region.Towns.Count()} towns, {g.Region.Villages.Count()} villages" : L.T("MAP_REGION_UNSEEN"))}";
    }

    static readonly Color RoadHigh = new("f0d27a"), RoadMid = new("d8c9a0"), RoadLow = new("a89878"), RiverCol = new("5d8fd0"), RegionTint = new(1f, 0.95f, 0.7f, 0.10f);

    /// <summary>Draws the region grid, then every generated region in detail over the pixels: roads, rivers, settlements and (when large enough) town names.</summary>
    public void Draw(CanvasItem c, Func<Vector2, Vector2> toScreen, float scale, Font font, Rect2 view)
    {
        int side = RegionCoord.CellsPerSide * ContinentMap.Scale; // pixels of one region square
        var tl = toScreen(Vector2.Zero);
        c.DrawTextureRect(Texture, new Rect2(tl, new Vector2(ContinentMap.Px, ContinentMap.Px) * scale), false);
        foreach (var g in Regions.Ready)
        {
            var o = toScreen(new Vector2(g.Coord.X * side, g.Coord.Y * side));
            var box = new Rect2(o, new Vector2(side, side) * scale);
            if (!box.Intersects(view)) continue;
            c.DrawRect(box, RegionTint);
            DrawDetail(c, g, o, side * scale / RegionSettings.RegionM, font, scale);
        }
        var gl = new Color(1, 1, 1, 0.30f);
        for (int i = 0; i <= RegionCoord.Grid; i++)
        {
            c.DrawLine(toScreen(new Vector2(i * side, 0)), toScreen(new Vector2(i * side, ContinentMap.Px)), gl, 1);
            c.DrawLine(toScreen(new Vector2(0, i * side)), toScreen(new Vector2(ContinentMap.Px, i * side)), gl, 1);
        }
        foreach (var g in Regions.Ready)
        {
            var o = toScreen(new Vector2(g.Coord.X * side, g.Coord.Y * side));
            c.DrawRect(new Rect2(o, new Vector2(side, side) * scale), g.Coord == Home ? PixelTheme.Gold : new Color(1f, 0.95f, 0.7f, 0.8f), false, g.Coord == Home ? 2 : 1);
            if (scale >= 3) c.DrawString(font, o + new Vector2(4, 12), g.Coord.ToString(), HorizontalAlignment.Left, -1, 11, PixelTheme.Dim);
        }
    }

    static void DrawDetail(CanvasItem c, GeneratedRegion g, Vector2 origin, float pxPerM, Font font, float scale)
    {
        var r = g.Region;
        Vector2 P(PointM p) => origin + new Vector2(p.X * pxPerM, p.Y * pxPerM);
        foreach (var w in r.Waterways)
        {
            if (w.Points.Length < 2) continue;
            if (w.Kind != WaterKind.River && scale < 3) continue;
            c.DrawPolyline(w.Points.Select(P).ToArray(), RiverCol, w.Kind == WaterKind.River ? Math.Max(1f, scale * 0.8f) : 1f);
        }
        foreach (var e in r.RoadEdges)
        {
            if (e.Points.Length < 2 || e.Class < RoadClass.Track || (e.Class == RoadClass.Track && scale < 2)) continue;
            c.DrawPolyline(e.Points.Select(P).ToArray(), e.Class == RoadClass.Highway ? RoadHigh : e.Class == RoadClass.Road ? RoadMid : RoadLow, e.Class == RoadClass.Highway ? Math.Max(1f, scale * 0.7f) : 1f);
        }
        foreach (var s in r.Settlements)
        {
            if (s.Class == SettlementClass.Farmstead && scale < 4) continue;
            var p = P(new PointM(s.CenterX, s.CenterY)).Floor();
            int sz = s.Class == SettlementClass.Town ? 4 : s.Class == SettlementClass.Village ? 3 : 2;
            c.DrawRect(new Rect2(p - new Vector2(sz / 2f + 1, sz / 2f + 1), new Vector2(sz + 2, sz + 2)), PixelTheme.Border);
            c.DrawRect(new Rect2(p - new Vector2(sz / 2f, sz / 2f), new Vector2(sz, sz)), s.Class == SettlementClass.Town ? PixelTheme.Gold : new Color("e8e0c8"));
            if (s.Class == SettlementClass.Town && scale >= 3) c.DrawString(font, p + new Vector2(-24, 14), s.Name, HorizontalAlignment.Left, 48, 11, PixelTheme.Body);
        }
    }
}
