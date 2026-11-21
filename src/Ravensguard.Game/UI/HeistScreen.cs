using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Heists;

namespace Ravensguard.Game.UI;

/// <summary>
/// The heist panel (heists-and-jailbreaks 6): the site's map seen from above in one-metre cells, shaded by the light of each cell, with the hero, the guards he can see (by what they are doing) and the doors in their state; beside it the HUD's eye
/// (<b>Seen (n)</b> or <b>Unseen</b> before he acts, the same geometry the guards use), the ear (Silent, Quiet or Loud, and the nearest listener's state), the clock, the busy action, the keys, the livery's lord and the verbs that fit where he stands, each with
/// the time and chance of a pick. The arrow keys step (a sneaking step takes two seconds); every action is a recorded hero command answered at the barrier; this screen keeps no game state, it draws <see cref="HeistView"/>. Back closes the panel and the
/// heist stays as it is; "Give up" ends it where he stands.
/// </summary>
public partial class HeistScreen : Control
{
    readonly ScreenRouter _router;
    readonly Func<HeistView?> _data;
    readonly Action<string> _act;
    readonly Action _giveUp;
    HeistMapControl _map = null!;
    Label _title = null!, _eye = null!, _ear = null!, _clock = null!, _info = null!, _busy = null!, _help = null!;
    GridContainer _verbs = null!;
    HeistView? _shown;
    double _refresh;
    int _shotWait;
    /// <summary>Dev: when set, the panel saves its own picture to this path and quits.</summary>
    public string? ShotPath { get; init; }

    public HeistScreen(ScreenRouter router, Func<HeistView?> data, Action<string> act, Action giveUp) { _router = router; _data = data; _act = act; _giveUp = giveUp; }

    public override void _Ready()
    {
        var margin = UiKit.Margin(this, 8);
        var root = new HBoxContainer(); root.AddThemeConstantOverride("separation", 8); margin.AddChild(root);
        _map = new HeistMapControl { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(480, 300) };
        var mp = UiKit.Panel(_map, 0, 0); mp.SizeFlagsHorizontal = SizeFlags.ExpandFill; mp.SizeFlagsVertical = SizeFlags.ExpandFill; root.AddChild(mp);
        var right = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) }; right.AddThemeConstantOverride("separation", 3); root.AddChild(right);
        _title = UiKit.Title(""); right.AddChild(_title);
        _clock = UiKit.Hint("", true); right.AddChild(_clock);
        _eye = new Label(); right.AddChild(_eye);
        _ear = new Label(); right.AddChild(_ear);
        _info = UiKit.Body("", true); right.AddChild(_info);
        _busy = UiKit.Hint("", true); right.AddChild(_busy);
        _verbs = new GridContainer { Columns = 2 }; _verbs.AddThemeConstantOverride("h_separation", 4); _verbs.AddThemeConstantOverride("v_separation", 3); right.AddChild(_verbs);
        var spacer = new Control { SizeFlagsVertical = SizeFlags.ExpandFill }; right.AddChild(spacer);
        _help = UiKit.Hint(L.T("hj_help"), true); right.AddChild(_help);
        var foot = new HBoxContainer(); foot.AddThemeConstantOverride("separation", 4); right.AddChild(foot);
        var give = new Button { Text = L.T("hj_give_up"), CustomMinimumSize = new Vector2(120, 28) }; give.Pressed += () => { _giveUp(); }; foot.AddChild(give);
        var back = UiKit.Key(L.T("UI_BACK"), "menu.back", 100, 28); back.Pressed += _router.Pop; foot.AddChild(back);
        Build();
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
        (int dx, int dy)? d = k.Keycode switch
        {
            Key.Up or Key.Kp8 => (0, -1), Key.Down or Key.Kp2 => (0, 1), Key.Left or Key.Kp4 => (-1, 0), Key.Right or Key.Kp6 => (1, 0),
            Key.Kp7 => (-1, -1), Key.Kp9 => (1, -1), Key.Kp1 => (-1, 1), Key.Kp3 => (1, 1), _ => null,
        };
        if (d is not { } m || _shown is not { Outcome: RunOutcome.Active }) return;
        _act($"mv|{m.dx}|{m.dy}");
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        _refresh += delta;
        if (_refresh > 0.25) { _refresh = 0; var v = _data(); if (!ReferenceEquals(v, _shown)) Build(); }
        if (ShotPath == null || !Visible || ++_shotWait != 14) return;
        var img = GetViewport().GetTexture().GetImage();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ShotPath) ?? ".");
        GD.Print($"[heist] screenshot {ShotPath}: {img.SavePng(ShotPath)}");
        GetTree().Quit();
    }

    static string Listener(GState s) => L.T(s switch
    {
        GState.Asleep => "hj_ls_asleep", GState.Down => "hj_ls_down", GState.Bribed => "hj_ls_bribed", GState.Investigate or GState.Looking or GState.Fire => "hj_ls_investigating",
        GState.Alert or GState.Pursue => "hj_ls_alert", _ => "hj_ls_listening",
    });

    Button Verb(string text, string action, bool enabled = true)
    {
        var b = new Button { Text = text, Disabled = !enabled, CustomMinimumSize = new Vector2(144, 26), ClipText = true, Alignment = HorizontalAlignment.Left };
        b.Pressed += () => _act(action);
        return b;
    }

    void Build()
    {
        _shown = _data();
        foreach (var c in _verbs.GetChildren()) { _verbs.RemoveChild(c); c.QueueFree(); }
        var v = _shown; _map.View = v; _map.QueueRedraw();
        if (v == null) { _title.Text = L.T("hj_none"); _eye.Text = ""; _ear.Text = ""; _clock.Text = ""; _info.Text = ""; _busy.Text = ""; return; }
        _title.Text = L.T("hj_site_" + v.Kind.ToString().ToLowerInvariant());
        _clock.Text = L.F("hj_clock", v.TimeText) + (v.Alarm ? "   " + L.T("hj_alarm") : "");
        _eye.Text = v.Disguised ? L.T("hj_disguised") : v.Eye > 0 ? L.F("CJ_HUD_SEEN", v.Eye) : L.T("CJ_HUD_UNSEEN");
        _eye.Modulate = v.Eye > 0 ? new Color(1f, 0.55f, 0.4f) : new Color(0.6f, 0.9f, 0.6f);
        string loud = L.T(v.Ear switch { 0 => "hj_ear_silent", 1 => "hj_ear_quiet", _ => "hj_ear_loud" });
        _ear.Text = v.NearestM >= 0 ? L.F("hj_ear", loud, Listener(v.Nearest), v.NearestM) : L.F("hj_ear_none", loud);
        _ear.Modulate = v.Ear == 2 ? new Color(1f, 0.7f, 0.4f) : new Color(0.8f, 0.85f, 0.95f);
        var lines = new List<string> { L.F("hj_keys", v.Keys) };
        if (v.Followers > 0) lines.Add(L.F("hj_followers", v.Followers));
        if (v.Livery) lines.Add(L.T("hj_livery_on"));
        if (v.Torch) lines.Add(L.T("hj_torch_on"));
        if (v.Stolen > 0) lines.Add(L.F("hj_stolen", UiKit.Money(v.Stolen)));
        if (v.OuterLocked) lines.Add(L.T("hj_outer_locked"));
        _info.Text = string.Join("\n", lines);
        _busy.Text = v.Busy > 0 ? L.F("hj_busy", L.T("hj_bk_" + v.BusyKind), v.Busy) : v.Outcome switch { RunOutcome.Caught => L.T("hj_outcome_caught"), RunOutcome.Left => L.T("hj_outcome_left"), _ => "" };
        if (v.Outcome != RunOutcome.Active) return;
        // the verbs that fit where he stands
        int hx = v.HeroX, hy = v.HeroY; bool busy = v.Busy > 0;
        int Cheb(int x, int y) => Math.Max(Math.Abs(x - hx), Math.Abs(y - hy));
        void Add(Button b) { b.Disabled |= busy; _verbs.AddChild(b); }
        foreach (var d in v.Doors.Where(d => Cheb(d.X, d.Y) <= 1 && d.State < 2).Take(2))
        {
            if (d.Grate) { Add(Verb(L.T("hj_v_pry"), $"pry|{d.Index}")); continue; }
            if (d.HasKey) Add(Verb(L.T("hj_v_key"), $"key|{d.Index}"));
            if (d.Quality >= 0 && d.State == 0) Add(Verb(L.F("hj_v_pick", v.PickS[Math.Clamp(d.Quality, 0, 4)], v.PickPct[Math.Clamp(d.Quality, 0, 4)]), $"pick|{d.Index}"));
            Add(Verb(L.T("hj_v_break"), $"brk|{d.Index}"));
        }
        if (v.OuterLocked && Cheb(v.Exit.X, v.Exit.Y) <= 1) Add(Verb(L.F("hj_v_pickout", v.PickS[2], v.PickPct[2]), "pickout"));
        foreach (var g in v.Guards.Where(g => Cheb(g.X, g.Y) <= 1 && g.State is GState.Asleep or GState.Down).Take(1)) Add(Verb(L.T("hj_v_search"), $"srch|{g.Index}"));
        foreach (var g in v.Guards.Where(g => Cheb(g.X, g.Y) <= 1 && g.State is not (GState.Down or GState.Bribed or GState.Pursue)).Take(1)) Add(Verb(L.T("hj_v_subdue"), $"sub|{g.Index}"));
        foreach (var g in v.Guards.Where(g => Cheb(g.X, g.Y) <= 3 && g.State is not (GState.Asleep or GState.Down or GState.Bribed or GState.Pursue) && g.Visible).Take(1))
            foreach (int offer in new[] { 600, 900, 2400 }) Add(Verb(L.F("hj_v_bribe", offer), $"brb|{g.Index}|{offer}"));
        foreach (var s in v.Sconces.Where(s => Cheb(s.X, s.Y) <= 1 && !s.Out).Take(1)) Add(Verb(L.T("hj_v_douse"), $"dse|{s.Index}"));
        foreach (var p in v.Slots.Where(p => p.Person != 0 && !p.Freed && Cheb(p.X, p.Y) <= 1).Take(1)) Add(Verb(L.T("hj_v_free"), $"free|{p.Slot}"));
        if (v.Chest.X >= 0 && Cheb(v.Chest.X, v.Chest.Y) <= 1) Add(Verb(L.T("hj_v_loot"), "loot", !v.ChestTaken));
        if (v.OnPortal) Add(Verb(L.T("hj_v_portal"), "portal"));
        if (hx == v.Exit.X && hy == v.Exit.Y) Add(Verb(L.T("hj_v_leave"), "leave", !v.OuterLocked));
        Add(Verb(L.T(v.Sneak == 1 ? "hj_v_sneak_off" : "hj_v_sneak_on"), $"sneak|{(v.Sneak == 1 ? 0 : 1)}"));
        Add(Verb(L.T(v.Torch ? "hj_v_torch_off" : "hj_v_torch_on"), $"torch|{(v.Torch ? 0 : 1)}"));
        Add(Verb(L.T(v.Livery ? "hj_v_livery_off" : "hj_v_livery_on"), $"liv|{(v.Livery ? 0 : 1)}"));
        Add(Verb(L.T("hj_v_throw"), $"thr|{Math.Clamp(hx + 5, 0, v.W - 1)}|{hy}"));
        Add(Verb(L.T("hj_v_wait"), "wait|5"));
    }

}
