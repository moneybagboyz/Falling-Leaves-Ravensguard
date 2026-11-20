using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Content;
using Ravensguard.Sim.Construction;

namespace Ravensguard.Game.World.Build;

/// <summary>
/// P5-C: what build mode would place, drawn over the street before it is placed (build-mode-and-construction 6): the brush or the plan in the piece's own art at 45%, green where it may go,
/// red with the reason's mark where it may not, amber where a tree or a prop will be cleared; the lot under the cursor with its street edge; the rectangle of the take-down brush or of
/// save as blueprint; the cursor cell. Presentation only: it draws the barrier's last answer and the main thread's brush.
/// </summary>
public partial class BuildPreviewLayer : Node2D
{
    const int Tile = 32;
    readonly ArtAtlas _art;
    readonly PieceCosts _costs;
    BuildAsk? _ask; BuildAnswer? _ans; int _z; (int X, int Y) _cursor; (int X, int Y)? _from; bool _takeDown, _saveRect;
    double _t;
    static readonly Color Ok = new(0.45f, 0.95f, 0.55f), No = new(1f, 0.3f, 0.25f), ClearCol = new(1f, 0.75f, 0.3f), LotCol = new(1f, 0.92f, 0.45f), RectCol = new(0.95f, 0.95f, 1f), TakeCol = new(0.9f, 0.4f, 0.9f);

    public BuildPreviewLayer(ArtAtlas art, PieceCosts costs) { _art = art; _costs = costs; }

    public void Clear() { _ask = null; _ans = null; QueueRedraw(); }

    public void Show(BuildAsk? ask, BuildAnswer? ans, int z, (int X, int Y) cursor, (int X, int Y)? from, bool takeDown, bool saveRect)
    {
        _ask = ask; _ans = ans; _z = z; _cursor = cursor; _from = from; _takeDown = takeDown; _saveRect = saveRect;
        QueueRedraw();
    }

    public override void _Process(double delta) { _t += delta; }

    static Rect2 Cell(int x, int y) => new(new Vector2(x * Tile, y * Tile), new Vector2(Tile, Tile));

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;
        // the lot under the cursor (a plan is fitted to it): its outline, the street edge thicker
        if (_ans?.Lot is { } lot && _ask?.Plan == true)
        {
            var r = new Rect2(lot.X * Tile, lot.Y * Tile, lot.W * Tile, lot.H * Tile);
            DrawRect(r, new Color(LotCol, 0.08f));
            DrawRect(r, new Color(LotCol, 0.8f), false, 2f);
            var (a, b) = lot.Street switch
            {
                0 => (r.Position, r.Position + new Vector2(r.Size.X, 0)),
                1 => (r.Position + new Vector2(r.Size.X, 0), r.End),
                2 => (r.Position + new Vector2(0, r.Size.Y), r.End),
                3 => (r.Position, r.Position + new Vector2(0, r.Size.Y)),
                _ => (Vector2.Zero, Vector2.Zero),
            };
            if (a != b) DrawLine(a, b, LotCol, 5f);
        }
        // the cells: the answer's when it is for this ask, else the brush as it stands (no verdict yet: outlined)
        var cells = _ans != null && _ask != null && _ans.Ask.Sig == _ask.Sig ? _ans.Cells : _ask?.Plan == true ? _ans?.Cells ?? [] : _ask?.Cells ?? [];
        bool judged = _ans != null && _ask != null && _ans.Ask.Sig == _ask.Sig;
        var refused = new Dictionary<(int, int, int), string>();
        var clears = new HashSet<(int, int, int)>();
        if (judged)
        {
            foreach (var rf in _ans!.Preview.Refused) refused.TryAdd((rf.X, rf.Y, rf.Z), rf.Key);
            foreach (var c in _ans.Preview.Clears) clears.Add(c);
        }
        foreach (var c in cells)
        {
            if (c.Z != _z && c.Z != _z + 1) continue;
            var r = Cell(c.X, c.Y);
            bool bad = refused.ContainsKey((c.X, c.Y, c.Z));
            var col = !judged ? new Color(0.7f, 0.8f, 1f) : bad ? No : Ok;
            if (c.Z != _z) { DrawDashed(r.Grow(-3), new Color(col, 0.7f)); continue; }      // the level above (a roof, an upper floor) as dashes
            var kind = _costs.Get(c.Piece)?.Kind ?? PieceKind.Floor;
            if (BuildCatalog.Sprite(c.Piece, kind) is { } id) DrawTextureRect(_art.Texture(id), r, false, new Color(col.R, col.G, col.B, 0.45f));
            else DrawRect(r, new Color(col, 0.25f));
            DrawRect(r.Grow(-1), new Color(col, 0.9f), false, 1.5f);
            if (bad)
            {
                DrawLine(r.Position + new Vector2(6, 6), r.End - new Vector2(6, 6), No, 2f);
                DrawLine(r.Position + new Vector2(Tile - 6, 6), r.Position + new Vector2(6, Tile - 6), No, 2f);
            }
            else if (clears.Contains((c.X, c.Y, c.Z)))
                DrawString(font, r.Position + new Vector2(2, 11), "x", HorizontalAlignment.Left, -1, 11, ClearCol);
        }
        // the first refusal's reason beside the cursor
        if (judged && _ans!.Preview.Refused.Count > 0)
        {
            var rf = _ans.Preview.Refused[0];
            string txt = Ravensguard.Sim.Trade.HeroText.Of(rf.Key, rf.Args).Render();
            if (txt.Length > 60) txt = txt[..57] + "...";
            var at = Cell(_cursor.X, _cursor.Y).Position + new Vector2(Tile + 4, -4);
            DrawStringOutline(font, at, txt, HorizontalAlignment.Left, -1, 12, 4, new Color(0, 0, 0, 0.95f));
            DrawString(font, at, txt, HorizontalAlignment.Left, -1, 12, No);
        }
        // a rectangle being dragged for the take-down brush or save as blueprint
        if ((_takeDown || _saveRect) && _from is { } f)
        {
            int ax = Math.Min(f.X, _cursor.X), ay = Math.Min(f.Y, _cursor.Y), w = Math.Abs(f.X - _cursor.X) + 1, h = Math.Abs(f.Y - _cursor.Y) + 1;
            var r = new Rect2(ax * Tile, ay * Tile, w * Tile, h * Tile);
            var col = _takeDown ? TakeCol : RectCol;
            DrawRect(r, new Color(col, 0.12f));
            DrawDashed(r, col);
            string label = $"{w} x {h} m";
            DrawStringOutline(font, r.Position + new Vector2(2, -4), label, HorizontalAlignment.Left, -1, 12, 4, new Color(0, 0, 0, 0.95f));
            DrawString(font, r.Position + new Vector2(2, -4), label, HorizontalAlignment.Left, -1, 12, col);
        }
        // the cursor
        float pulse = 0.6f + 0.4f * (float)Math.Abs(Math.Sin(_t * 4));
        DrawRect(Cell(_cursor.X, _cursor.Y), new Color(1, 1, 1, pulse), false, 2f);
    }

    void DrawDashed(Rect2 r, Color c)
    {
        var p = new[] { r.Position, r.Position + new Vector2(r.Size.X, 0), r.End, r.Position + new Vector2(0, r.Size.Y), r.Position };
        for (int i = 0; i < 4; i++) DrawDashedLine(p[i], p[i + 1], c, 2f, 5f);
    }
}
