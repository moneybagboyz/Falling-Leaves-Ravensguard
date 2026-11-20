using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Prospecting;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Game.UI;

/// <summary>What the prospecting panel can ask the street view to send to the hero's desk.</summary>
public sealed class ProspectActions
{
    public Action<ProspectMethod>? Start;
    public Action? Cancel;
    public Action? Write;
    public Action<EntityId>? Read;
    public Action? Licence;
}

/// <summary>
/// Prospecting where the hero stands (P5-F, prospecting spec 8): the resource overlay (what <b>he</b> knows of the ground around him: an icon per tile with its confidence as its shade, unsurveyed
/// ground hatched), the methods with their hours and tools (read the land, pan, a test pit, a trial shaft), his job in progress, his hints and pan findings, the survey reports he carries (read one),
/// and the licence petition for the ore under him (the right holder, the fee, the royalty; a modal asks first). A view over the barrier's <see cref="ProspectView"/>.
/// </summary>
public partial class ProspectPanel : Control
{
    readonly ScreenRouter _router;
    readonly Func<ProspectView?> _data;
    readonly ProspectActions _act;
    VBoxContainer _body = null!;
    Label _status = null!;
    Overlay _map = null!;
    ProspectView? _shown;
    double _refresh;
    int _shotWait;
    public string? ShotPath { get; init; }
    public bool ShowLicence { get; init; }

    public ProspectPanel(ScreenRouter router, Func<ProspectView?> data, ProspectActions act) { _router = router; _data = data; _act = act; }

    public override void _Ready()
    {
        var margin = UiKit.Margin(this, 12);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 4); margin.AddChild(root);
        root.AddChild(UiKit.Title(L.T("px_ui_title")));
        _status = UiKit.Hint(""); root.AddChild(_status);
        var split = new HBoxContainer(); split.AddThemeConstantOverride("separation", 10); split.SizeFlagsVertical = SizeFlags.ExpandFill; root.AddChild(split);
        _map = new Overlay { CustomMinimumSize = new Vector2(300, 300) };
        var mp = UiKit.Panel(_map, 310, 310); mp.SizeFlagsVertical = SizeFlags.ShrinkBegin; split.AddChild(mp);
        _body = new VBoxContainer(); _body.AddThemeConstantOverride("separation", 3);
        var p = UiKit.Panel(UiKit.Scroll(_body), 0, 0); p.SizeFlagsHorizontal = SizeFlags.ExpandFill; p.SizeFlagsVertical = SizeFlags.ExpandFill; split.AddChild(p);
        var back = UiKit.Key(L.T("UI_BACK"), "menu.back", 200); back.Pressed += _router.Pop; root.AddChild(back);
        Build();
    }

    public override void _Process(double delta)
    {
        _refresh += delta;
        if (_refresh > 0.5) { _refresh = 0; if (!ReferenceEquals(_data(), _shown)) Build(); }
        if (!Visible || _shown == null) return;
        _shotWait++;
        if (_shotWait == 8 && ShowLicence) Petition();
        if (ShotPath == null || _shotWait != 16) return;
        var img = GetViewport().GetTexture().GetImage();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ShotPath) ?? ".");
        GD.Print($"[prospect] screenshot {ShotPath}: {img.SavePng(ShotPath)}");
        GetTree().Quit();
    }

    static string Res(int code) => L.T(ResourceCode.Key(code));
    static string M(long m) => Quotes.Money(m);

    Label Row(string text, bool dim = false)
    {
        var l = new Label { Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, TooltipText = text, MouseFilter = MouseFilterEnum.Stop };
        if (dim) l.AddThemeColorOverride("font_color", PixelTheme.Dim);
        return l;
    }

    HBoxContainer Line(string text, string? button, Action? press, bool enabled = true)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 6);
        var l = UiKit.Body(text); l.SizeFlagsHorizontal = SizeFlags.ExpandFill; row.AddChild(l);       // long lines wrap at 960 x 540
        if (button != null) { var b = new Button { Text = button, Disabled = !enabled || press == null, CustomMinimumSize = new Vector2(110, 24) }; b.Pressed += () => press?.Invoke(); row.AddChild(b); }
        return row;
    }

    void Build()
    {
        foreach (var c in _body.GetChildren()) { _body.RemoveChild(c); c.QueueFree(); }
        var v = _data(); _shown = v;
        if (v == null) { _status.Text = L.T("lg_waiting"); return; }
        int N = Ravensguard.Sim.WorldGen.Region.Region.Size;
        _status.Text = L.F("px_ui_status", v.Tile % N, v.Tile / N, v.Skill, v.Stream ? L.T("px_ui_stream") : "");
        _map.View = v; _map.QueueRedraw();
        // here: what the hero knows of this very tile
        _body.AddChild(UiKit.Hint(L.T("px_ui_here")));
        var here = v.Known.Where(k => k.Tile == v.Tile).ToList();
        if (here.Count == 0) _body.AddChild(Row(L.T("px_ui_unsurveyed"), true));
        foreach (var k in here) _body.AddChild(Row(L.F("px_ui_known_row", Res(k.Resource), Quotes.Weight(k.EstG), k.GradePm > 0 ? L.F("px_ui_grade", k.GradePm / 10) : "", k.ConfidencePm / 10, L.T("px_m_" + k.Method.ToString().ToLowerInvariant()))));
        // the methods
        _body.AddChild(UiKit.Hint(L.T("px_ui_methods")));
        if (v.Job is { } j) _body.AddChild(Line(L.F("px_ui_job", L.T("px_m_" + j.Method.ToString().ToLowerInvariant()), (j.EndSec - j.StartSec) / 3600), L.T("px_ui_btn_stop"), () => _act.Cancel?.Invoke()));
        foreach (var m in new[] { ProspectMethod.ReadLand, ProspectMethod.Pan, ProspectMethod.TestPit, ProspectMethod.TrialShaft })
        {
            var need = Prospector.Tools(m);
            bool tools = Prospector.HasTools(m, v.Tools.ToList());
            string why = m == ProspectMethod.ReadLand && v.Skill < 20 ? L.F("px_need_skill", 20, v.Skill) : !tools ? L.F("px_need_tools", string.Join(" + ", need.Select(g => string.Join("/", g)))) : m == ProspectMethod.Pan && !v.Stream ? L.T("px_no_stream") : "";
            var mm = m;
            _body.AddChild(Line(L.F("px_ui_method_row", L.T("px_m_" + m.ToString().ToLowerInvariant()), HoursOf(m), why), L.T("px_ui_btn_start"), () => _act.Start?.Invoke(mm), why.Length == 0 && v.Job == null));
        }
        // hints and pans
        if (v.Hints.Count + v.Pans.Count > 0) _body.AddChild(UiKit.Hint(L.T("px_ui_signs")));
        foreach (var h in v.Hints.TakeLast(4)) _body.AddChild(Row(L.F("px_ui_hint_row", Res(ResourceCode.Ore((Ravensguard.Sim.WorldGen.MineralType)h.Mineral)), L.T("px_dir_" + h.Direction), h.Tile % N, h.Tile / N)));
        foreach (var p in v.Pans.TakeLast(3)) _body.AddChild(Row(L.F("px_ui_pan_row", p.Tile % N, p.Tile / N, p.Minerals.Length == 0 ? L.T("px_ui_nothing") : string.Join(", ", p.Minerals.Select(x => Res(ResourceCode.Ore((Ravensguard.Sim.WorldGen.MineralType)x)))))));
        // survey reports
        _body.AddChild(UiKit.Hint(L.T("px_ui_reports")));
        _body.AddChild(Line(L.T("px_ui_write"), L.T("px_ui_btn_write"), () => _act.Write?.Invoke(), here.Count > 0));
        foreach (var (item, rows) in v.Reports) { var it = item; _body.AddChild(Line(L.F("px_ui_report_row", rows, item.Value % 100000), L.T("px_ui_btn_read"), () => _act.Read?.Invoke(it))); }
        // rights
        _body.AddChild(UiKit.Hint(L.T("px_ui_rights")));
        if (v.OreKnownHere) _body.AddChild(Line(L.F("px_ui_right_row", v.RightHolder, M(v.LicenceFee)), L.T("px_ui_btn_petition"), Petition, v.RightHolder.Length > 0));
        else _body.AddChild(Row(L.T("px_ui_no_ore_here"), true));
        foreach (var l in v.Licences) _body.AddChild(Row(L.F("px_ui_licence_row", Res(ResourceCode.Ore(l.Mineral)), l.Tile % N, l.Tile / N, l.RoyaltyPm / 10)));
    }

    static int HoursOf(ProspectMethod m) => m switch { ProspectMethod.ReadLand => 1, ProspectMethod.Pan => 2, ProspectMethod.TestPit => 6, ProspectMethod.TrialShaft => 48, _ => 0 };

    void Petition()
    {
        if (_shown is not { OreKnownHere: true } v || v.RightHolder.Length == 0) return;
        _router.ShowModal(L.T("px_ui_petition_title"), L.F("px_ui_petition_body", v.RightHolder, M(v.LicenceFee), 10), (L.T("px_ui_btn_petition"), () => _act.Licence?.Invoke()), (L.T("UI_CANCEL"), null));
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible || _router.HasModal) return;
        if (Actions.Pressed(e, "world.context_menu")) { _router.Pop(); GetViewport().SetInputAsHandled(); }
    }

    /// <summary>The resource overlay (prospecting spec 8): the hero's knowledge of the 25 x 25 acres around him, an icon colour per resource with its confidence as the shade, unsurveyed ground hatched.</summary>
    public partial class Overlay : Control
    {
        public ProspectView? View;
        const int R = 12;

        static Color ColorOf(int code) => ResourceCode.IsOre(code) ? ResourceCode.Mineral(code) switch
        {
            Ravensguard.Sim.WorldGen.MineralType.Iron => new Color("b5543c"), Ravensguard.Sim.WorldGen.MineralType.Copper => new Color("d0883c"), Ravensguard.Sim.WorldGen.MineralType.Tin => new Color("b8c0c8"),
            Ravensguard.Sim.WorldGen.MineralType.Silver => new Color("e8e8f0"), Ravensguard.Sim.WorldGen.MineralType.Gold => new Color("f0cc40"), Ravensguard.Sim.WorldGen.MineralType.Coal => new Color("303030"), _ => new Color("d8d0c0"),
        } : (ResourceKind)code switch
        {
            ResourceKind.Wood => new Color("3c7a3c"), ResourceKind.Stone => new Color("9a9a90"), ResourceKind.Clay => new Color("a8643c"), ResourceKind.Peat => new Color("5a4030"), ResourceKind.Salt => new Color("e0e0e8"),
            ResourceKind.Fish => new Color("4070b0"), ResourceKind.Sand => new Color("d8c890"), _ => new Color("7a7a60"),
        };

        public override void _Draw()
        {
            var v = View; float cell = Size.X / (2 * R + 1);
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.12f, 0.1f, 0.09f));
            if (v == null) return;
            int N = Ravensguard.Sim.WorldGen.Region.Region.Size, tx = v.Tile % N, ty = v.Tile / N;
            var known = new Dictionary<int, List<KnownView>>();
            foreach (var k in v.Known) { if (!known.TryGetValue(k.Tile, out var l)) known[k.Tile] = l = []; l.Add(k); }
            for (int dy = -R; dy <= R; dy++)
                for (int dx = -R; dx <= R; dx++)
                {
                    int x = tx + dx, y = ty + dy; var at = new Vector2((dx + R) * cell, (dy + R) * cell);
                    if (x < 0 || y < 0 || x >= N || y >= N) continue;
                    if (!known.TryGetValue(y * N + x, out var ks))
                    {
                        // unsurveyed: hatched
                        DrawLine(at, at + new Vector2(cell, cell), new Color(0.3f, 0.27f, 0.22f));
                        continue;
                    }
                    var best = ks.OrderByDescending(k => ResourceCode.IsOre(k.Resource) ? 2 : 1).ThenByDescending(k => k.ConfidencePm).First();
                    var c = ColorOf(best.Resource); float shade = 0.35f + 0.65f * best.ConfidencePm / 1000f;
                    DrawRect(new Rect2(at + new Vector2(1, 1), new Vector2(cell - 2, cell - 2)), new Color(c.R * shade, c.G * shade, c.B * shade));
                    if (ks.Count > 1) DrawRect(new Rect2(at + new Vector2(cell - 4, 1), new Vector2(3, 3)), PixelTheme.Gold);
                }
            var me = new Vector2(R * cell, R * cell);
            DrawRect(new Rect2(me, new Vector2(cell, cell)), PixelTheme.Gold, false, 2);
            foreach (var h in v.Hints.Where(h => h.Tile == v.Tile))
            {
                var dir = h.Direction switch { 0 => new Vector2(0, -1), 1 => new Vector2(1, -1), 2 => new Vector2(1, 0), 3 => new Vector2(1, 1), 4 => new Vector2(0, 1), 5 => new Vector2(-1, 1), 6 => new Vector2(-1, 0), _ => new Vector2(-1, -1) };
                DrawLine(me + new Vector2(cell / 2, cell / 2), me + new Vector2(cell / 2, cell / 2) + dir * cell * 3, ColorOf(ResourceCode.Ore((Ravensguard.Sim.WorldGen.MineralType)h.Mineral)), 2);
            }
        }
    }
}
