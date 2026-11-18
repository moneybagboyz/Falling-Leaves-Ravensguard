using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.World;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Game.UI;

/// <summary>
/// The map's travel destination picker (travel spec 4.2, 6; overview 5A: "Map: Enter = travel here for the selected place"): click a place, or Tab through the places nearest first, to see the route on the
/// map with its distance, time (game hours, and real minutes at 1x and 8x) and how many days of food the pack holds; Enter starts the trip.
/// </summary>
public partial class WorldMapScreen
{
    /// <summary>Plans the route to a place (region metres) for the preview; null when there is none or no travel here.</summary>
    public Func<string, float, float, TravelPreview?>? TravelPlan { get; init; }
    /// <summary>Starts the journey of a preview (the street view's <c>StartTravel</c>); the map closes.</summary>
    public Func<TravelPreview, bool>? TravelStart { get; init; }
    /// <summary>Dev: pre-select the nth nearest place (the destination picker shot).</summary>
    public int PreselectNth { get; init; } = -1;

    (string Name, float X, float Y)? _tsel;
    TravelPreview? _tprev; bool _tnoWay;
    Vector2 _pressAt; float _pressDrag;
    List<(string Name, float X, float Y)>? _places;

    List<(string Name, float X, float Y)> Places()
    {
        if (_places != null) return _places;
        var r = _map.Region; var (hx, hy) = _hero();
        var l = new List<(string, float, float)>();
        foreach (var s in r.Settlements) l.Add((s.Name, s.CenterX, s.CenterY));
        foreach (var s in r.Sites) if (_map.IsExplored(s.TileX, s.TileY)) l.Add((s.Name, s.X, s.Y));
        return _places = [.. l.Where(p => Math.Abs(p.Item2 - hx) + Math.Abs(p.Item3 - hy) > 150).OrderBy(p => (p.Item2 - hx) * (p.Item2 - hx) + (p.Item3 - hy) * (p.Item3 - hy)).ThenBy(p => p.Item1, StringComparer.Ordinal)];
    }

    void TravelSelect((string Name, float X, float Y) p)
    {
        _tsel = p; _tnoWay = false;
        _tprev = TravelPlan?.Invoke(p.Name, p.X, p.Y);
        _tnoWay = _tprev == null;
    }

    void TravelSelectAt(Vector2 screen)
    {
        if (TravelPlan == null || _continent) return;
        var p = ToImage(screen); float mx = p.X / RegionMap.Px * RegionData.TileM, my = p.Y / RegionMap.Px * RegionData.TileM;
        var best = Places().Where(q => Math.Abs(q.X - mx) < RegionData.TileM * 2.5f && Math.Abs(q.Y - my) < RegionData.TileM * 2.5f).OrderBy(q => (q.X - mx) * (q.X - mx) + (q.Y - my) * (q.Y - my)).Cast<(string Name, float X, float Y)?>().FirstOrDefault();
        if (best is { } b) TravelSelect(b); else { _tsel = null; _tprev = null; _tnoWay = false; }
    }

    /// <summary>Tab: the next place, nearest first. Enter: go. True when the event was used.</summary>
    bool TravelInput(InputEvent e)
    {
        if (TravelPlan == null || _continent || e is not InputEventKey { Pressed: true } k) return false;
        if (k.Keycode == Key.Tab)
        {
            var pl = Places(); if (pl.Count == 0) return true;
            int i = _tsel is { } s ? pl.FindIndex(q => q.Name == s.Name) : -1;
            TravelSelect(pl[(i + 1) % pl.Count]);
            if (_tprev != null) CenterOn(_tprev.Path.X[^1] / 1000f, _tprev.Path.Y[^1] / 1000f);
            return true;
        }
        if (k.Keycode is Key.Enter or Key.KpEnter && _tprev != null && TravelStart != null)
        {
            if (TravelStart(_tprev)) { _router.Pop(); }
            return true;
        }
        if (k.Keycode == Key.Escape && _tsel != null) { _tsel = null; _tprev = null; _tnoWay = false; return true; }
        return false;
    }

    void CenterOn(float xM, float yM) { _origin = new Vector2(xM / RegionData.TileM * RegionMap.Px, yM / RegionData.TileM * RegionMap.Px); ClampOrigin(); }

    void TravelStartup()
    {
        if (PreselectNth >= 0 && TravelPlan != null) { var pl = Places(); if (pl.Count > 0) { TravelSelect(pl[Math.Min(PreselectNth, pl.Count - 1)]); if (_tprev != null) { CenterOn((_tprev.Path.X[0] + _tprev.Path.X[^1]) / 2000f, (_tprev.Path.Y[0] + _tprev.Path.Y[^1]) / 2000f); } } }
    }

    void DrawTravel()
    {
        if (TravelPlan == null || _continent) return;
        if (_tprev is { } pv)
        {
            Vector2 P(int i) => ToScreen(new Vector2(pv.Path.X[i] / 1000f / RegionData.TileM * RegionMap.Px, pv.Path.Y[i] / 1000f / RegionData.TileM * RegionMap.Px)).Floor();
            for (int i = 1; i < pv.Path.X.Length; i++) { DrawLine(P(i - 1), P(i), PixelTheme.Border, 4f); }
            for (int i = 1; i < pv.Path.X.Length; i++) { DrawLine(P(i - 1), P(i), new Color("fde27a"), 2f); }
            var end = P(pv.Path.X.Length - 1);
            for (int a = 0; a < 32; a++) { double t = a / 32.0 * Math.PI * 2; DrawRect(new Rect2(end.X + (int)Math.Round(Math.Cos(t) * 10), end.Y + (int)Math.Round(Math.Sin(t) * 10), 2, 2), PixelTheme.Warn); }
        }
        // the panel: bottom left, above the hint bar
        float w = 380, h = 112; var at = new Vector2(12, Size.Y - h - 40);
        DrawRect(new Rect2(at, new Vector2(w, h)), new Color(0.08f, 0.07f, 0.09f, 0.92f)); DrawRect(new Rect2(at, new Vector2(w, h)), PixelTheme.Rim, false, 2f);
        if (_tsel is { } sel)
        {
            DrawString(_font, at + new Vector2(12, 24), L.F("TRAVEL_MAP_TITLE", sel.Name), HorizontalAlignment.Left, -1, 18, PixelTheme.Gold);
            if (_tprev is { } p)
            {
                DrawString(_font, at + new Vector2(12, 46), L.F("TRAVEL_MAP_DIST", (p.DistM / 1000.0).ToString("0.0")), HorizontalAlignment.Left, -1, 14, PixelTheme.Body);
                DrawString(_font, at + new Vector2(12, 64), L.F("TRAVEL_MAP_ETA", (p.GameHoursTenths / 10.0).ToString("0.0"), (p.RealSeconds1x / 60.0).ToString("0.0"), (p.RealSeconds8x / 60.0).ToString("0.0")), HorizontalAlignment.Left, -1, 14, PixelTheme.Body);
                DrawString(_font, at + new Vector2(12, 82), L.F("TRAVEL_MAP_FOOD", p.ProvisionDays.ToString("0.0")), HorizontalAlignment.Left, -1, 14, p.ProvisionDays * 24 < p.GameHoursTenths / 10.0 ? PixelTheme.Warn : PixelTheme.Body);
                DrawString(_font, at + new Vector2(12, 102), L.T("TRAVEL_MAP_KEYS"), HorizontalAlignment.Left, -1, 13, PixelTheme.Dim);
            }
            else if (_tnoWay) DrawString(_font, at + new Vector2(12, 52), L.T("TRAVEL_MAP_NOWAY"), HorizontalAlignment.Left, -1, 14, PixelTheme.Warn);
        }
        else
        {
            DrawString(_font, at + new Vector2(12, 28), L.F("TRAVEL_MAP_HINT", "Enter"), HorizontalAlignment.Left, w - 24, 14, PixelTheme.Body);
            DrawString(_font, at + new Vector2(12, 52), L.T("TRAVEL_MAP_KEYS"), HorizontalAlignment.Left, w - 24, 13, PixelTheme.Dim);
        }
    }
}
