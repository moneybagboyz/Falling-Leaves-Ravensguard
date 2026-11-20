using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI.NewGame;

namespace Ravensguard.Game.UI;

/// <summary>One page of the controls overlay: the actions of a context (read from <see cref="InputDefs"/> and the live key map, so a rebind shows) and the keys a screen reads itself.</summary>
public sealed record OverlayView(string Id, string[] Actions, (string Keys, string TextKey)[] Local, InputContext? Backing)
{
    public string TitleKey => "OVERLAY_" + Id.ToUpperInvariant();
    public string CodexRule => "rule.controls." + (Backing?.ToString().ToLowerInvariant() ?? "world");
}

/// <summary>
/// The controls overlay (<c>?</c>, onboarding spec 3.3, plan 2.8): the keys of the context you are in, as a table of action, key and mouse built from the same <see cref="InputDefs"/> table that CI conflict-checks and the
/// key map the player has now, so it cannot disagree with the bindings. Contexts with their own <see cref="InputContext"/> list every action of it; the travel, camp and crafting pages gather the world keys that matter there
/// plus the keys their screens read themselves. Opening it pauses the game (it is a screen on the router, never a cheat); Esc or <c>?</c> closes it.
/// </summary>
public partial class ControlsOverlay : Control
{
    static IEnumerable<string> Of(InputContext c) => InputDefs.All.Where(a => a.Context == c).Select(a => a.Id);

    /// <summary>Every page, in the order the player meets them. Built from the table at call time.</summary>
    public static IReadOnlyList<OverlayView> Views() =>
    [
        new("world", [.. Of(InputContext.World)], [], InputContext.World),
        new("mounted", [.. Of(InputContext.Mounted), "world.act", "world.sprint", "world.camp", "world.map", "world.cart_hitch", "world.cart_unload", "world.cart_reload", "world.cart_dig", "world.speed_1", "world.speed_2", "world.speed_4", "world.speed_8", "world.pause"], [], InputContext.Mounted),
        new("trade", [.. Of(InputContext.Trade)], [("Shift / Ctrl", "OVERLAY_TRADE_QTY")], InputContext.Trade),
        new("crafting", ["world.make", "world.speed_1", "world.pause"], [("/", "OVERLAY_CRAFT_SEARCH"), ("F", "OVERLAY_CRAFT_FILTER"), ("Enter", "OVERLAY_CRAFT_START"), ("Esc", "OVERLAY_CLOSE")], null),
        new("camp", ["world.camp", "world.forage", "world.act", "world.context_menu", "world.torch", "choice.pick_1", "choice.pick_2", "choice.pick_3", "choice.pick_4", "choice.pick_5", "choice.pick_6", "choice.pick_7", "world.pause", "world.panel_inventory"], [], null),
        new("travel", ["world.map", "world.context_menu", "world.camp", "world.forage", "world.speed_1", "world.speed_2", "world.speed_4", "world.speed_8", "choice.pick_1", "choice.pick_2", "choice.pick_3", "world.pause", "world.sprint", "world.panel_journal"], [("Enter", "OVERLAY_TRAVEL_HERE")], null),
        new("called_shot", [.. Of(InputContext.CalledShot)], [], InputContext.CalledShot),
        new("build", [.. Of(InputContext.Build)], [], InputContext.Build),
        new("vehicle_designer", [.. Of(InputContext.VehicleDesigner)], [], InputContext.VehicleDesigner),          // P5-G
        new("ledger", [.. Of(InputContext.Ledger)], [], InputContext.Ledger),                            // P5-F
        new("treatment", [.. Of(InputContext.Treatment)], [], InputContext.Treatment),                    // P6-B1
        new("faction", [.. Of(InputContext.Faction)], [], InputContext.Faction),                          // P6-D2
    ];

    public static OverlayView View(string id) => Views().First(v => v.Id == id);

    /// <summary>The rows of a page: (name, keys, mouse) with each action's keys and mouse buttons as the key map has them now.</summary>
    public static List<(string Name, string Keys, string Mouse)> Rows(OverlayView v, KeyMap map)
    {
        var rows = new List<(string, string, string)>();
        foreach (var id in v.Actions)
        {
            var a = InputDefs.All.FirstOrDefault(x => x.Id == id); if (a == null) continue;
            var bs = map.Get(id);
            rows.Add((L.T(a.LocKey), bs.Count == 0 ? L.T("CONTROLS_UNBOUND") : string.Join(" / ", bs.Where(b => b.IsKeyboard).Select(b => b.Describe())), string.Join(" / ", bs.Where(b => !b.IsKeyboard).Select(b => b.Describe()))));
        }
        foreach (var (keys, text) in v.Local) rows.Add((L.T(text), keys, ""));
        // the rows that name no keys at all (an unbound action) stay: the overlay shows what is bound, honestly
        return rows;
    }

    /// <summary>Opens the overlay for a page unless one is open already.</summary>
    public static void Open(ScreenRouter router, string viewId, System.Action? openCodex = null)
    {
        if (router.Top is ControlsOverlay) return;
        router.Push(new ControlsOverlay(router, View(viewId), openCodex) { Name = "ControlsOverlay" });
    }

    readonly ScreenRouter _router; readonly OverlayView _view; readonly System.Action? _codex;
    public string? ShotPath { get; init; }
    int _shotWait;

    ControlsOverlay(ScreenRouter router, OverlayView view, System.Action? codex) { _router = router; _view = view; _codex = codex; }

    public override void _Ready()
    {
        var margin = UiKit.Margin(this, 24);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 8); margin.AddChild(root);
        root.AddChild(UiKit.Title(L.F("OVERLAY_TITLE", L.T(_view.TitleKey))));
        var tabs = new HFlowContainer(); tabs.AddThemeConstantOverride("h_separation", 6); tabs.AddThemeConstantOverride("v_separation", 4); root.AddChild(tabs);   // P5-G, P5-F: the pages wrap at 960 px
        foreach (var v in Views())
        {
            var b = new Button { Text = L.T(v.TitleKey), ToggleMode = true, ButtonPressed = v.Id == _view.Id, CustomMinimumSize = new Vector2(96, 26) };
            var id = v.Id; b.Pressed += () => { _router.Pop(); Open(_router, id, _codex); };
            tabs.AddChild(b);
        }
        var grid = new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill }; grid.AddThemeConstantOverride("h_separation", 28); grid.AddThemeConstantOverride("v_separation", 2);
        grid.AddChild(UiKit.Hint(L.T("OVERLAY_COL_ACTION"))); grid.AddChild(UiKit.Hint(L.T("OVERLAY_COL_KEY"))); grid.AddChild(UiKit.Hint(L.T("OVERLAY_COL_MOUSE")));
        foreach (var (name, keys, mouse) in Rows(_view, InputBootstrap.Map))
        {
            grid.AddChild(new Label { Text = name, CustomMinimumSize = new Vector2(300, 0) });
            var k = new Label { Text = keys, CustomMinimumSize = new Vector2(160, 0) }; k.AddThemeColorOverride("font_color", PixelTheme.Gold); grid.AddChild(k);
            grid.AddChild(new Label { Text = mouse, CustomMinimumSize = new Vector2(160, 0) });
        }
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(grid); var panel = UiKit.Panel(scroll, 0, 0); panel.SizeFlagsVertical = SizeFlags.ExpandFill; root.AddChild(panel);
        var bottom = new HBoxContainer(); bottom.AddThemeConstantOverride("separation", 10); root.AddChild(bottom);
        var close = UiKit.Key(L.T("UI_BACK"), "menu.back", 200); close.Pressed += _router.Pop; bottom.AddChild(close);
        if (_codex != null) { var more = new Button { Text = L.T("OVERLAY_CODEX") }; more.Pressed += () => { _router.Pop(); _codex(); }; bottom.AddChild(more); }
        bottom.AddChild(UiKit.Hint(L.F("OVERLAY_CLOSE_HINT", InputBootstrap.Hint("world.controls_overlay"))));
    }

    public override void _Input(InputEvent e)
    {
        if (!Visible) return;
        if (Actions.PressedAlways(e, "world.controls_overlay")) { _router.Pop(); GetViewport().SetInputAsHandled(); }
    }

    public override void _Process(double delta)
    {
        if (ShotPath == null || !Visible || ++_shotWait != 8) return;
        var img = GetViewport().GetTexture().GetImage();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ShotPath) ?? ".");
        img.SavePng(ShotPath); GD.Print("shot: " + ShotPath);
    }
}
