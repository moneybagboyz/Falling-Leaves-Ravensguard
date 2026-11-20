using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Core;

namespace Ravensguard.Game.UI;

/// <summary>
/// The Chronicle (H; P6-E4, chronicle-full 4.4, 6): the game's own record in four views (My life, Here, Realm, Legends), a list on the left (55%) and the entry on the right with its Why (the causes and the
/// chain back to where it began) and What followed (the effect index), each a link: F follows a cause back, D an effect forward, Backspace returns, O opens the place, T cycles the type family, E the view, / searches.
/// A view over the barrier's <see cref="ChronicleView"/> and the sim's <see cref="ChronicleBrowser"/> (the logic lives there and is tested there); shares the legends viewer's keys (the Menu context). Fits 960 x 540.
/// </summary>
public partial class ChronicleScreen : Control
{
    readonly ScreenRouter _router;
    readonly Func<ChronicleView?> _data;
    readonly Action _refresh;
    readonly ChronicleTypes _types;
    ChronicleView? _view;
    ChronicleBrowser? _b;
    readonly EntityId? _focus;
    ChronicleScope _scope = ChronicleScope.MyLife;
    LineEdit _search = null!;
    Button _familyBtn = null!, _prev = null!, _here = null!;
    readonly Button[] _tabs = new Button[4];
    ItemList _list = null!;
    Label _count = null!;
    RichTextLabel _detail = null!, _whyText = null!;
    VBoxContainer _links = null!;
    readonly List<EntityId> _shown = [];
    double _acc; int _shotWait;

    /// <summary>The screen opens at this entry (the rumour card's History link, a defeat's "why").</summary>
    public EntityId? Focus { init => _focus = value; }
    /// <summary>The view to start in.</summary>
    public ChronicleScope StartScope { init => _scope = value; }
    /// <summary>O: the place an entry happened (its settlement) opened on the map.</summary>
    public Action<EntityId>? OpenPlace { get; init; }
    public string? ShotPath { get; init; }
    /// <summary>Open at the latest entry that names the hero (a defeat's why).</summary>
    public bool FocusLatestMine { get; init; }
    /// <summary>Dev: open at the entry with the longest chain of causes.</summary>
    public bool FocusDeepest { get; init; }
    /// <summary>Dev (smoke, shots): open this entry first and follow its causes back.</summary>
    public int StartFollow { get; init; }

    public ChronicleScreen(ScreenRouter router, Func<ChronicleView?> data, Action refresh, ChronicleTypes types) { _router = router; _data = data; _refresh = refresh; _types = types; }

    /// <summary>The browser, for the smoke and the shots.</summary>
    public ChronicleBrowser? Browser => _b;

    static string Family(string f) => f.Length == 0 ? L.T("LG_ALL") : L.T("ch_ui_family_" + f);

    public override void _Ready()
    {
        var margin = UiKit.Margin(this, 12);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 4); margin.AddChild(root);
        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 6); root.AddChild(head);
        var title = UiKit.Title(L.T("ch_ui_title")); title.SizeFlagsHorizontal = SizeFlags.ExpandFill; head.AddChild(title);
        foreach (var s in Enum.GetValues<ChronicleScope>())
        {
            var sc = s;
            var t = new Button { Text = L.T("ch_ui_scope_" + s.ToString().ToLowerInvariant()), ToggleMode = true, CustomMinimumSize = new Vector2(0, 26), FocusMode = FocusModeEnum.None };
            t.Pressed += () => { _scope = sc; _b?.SetScope(sc); Fill(); };
            _tabs[(int)s] = t; head.AddChild(t);
        }
        var bar = new HBoxContainer(); bar.AddThemeConstantOverride("separation", 6); root.AddChild(bar);
        _search = new LineEdit { PlaceholderText = L.T("LG_SEARCH") + "  " + InputBootstrap.Hint("menu.search"), CustomMinimumSize = new Vector2(0, 28), ClearButtonEnabled = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        UiKit.CaptureKeys(_search);
        _search.TextChanged += s => { _b?.SetSearch(s); Fill(); };
        bar.AddChild(_search);
        _familyBtn = UiKit.Key("", "menu.filter_type", 230, 28); _familyBtn.Pressed += () => { _b?.CycleFamily(); Fill(); };
        bar.AddChild(_familyBtn);
        _count = UiKit.Hint(""); root.AddChild(_count);

        var cols = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; cols.AddThemeConstantOverride("separation", 8); root.AddChild(cols);
        _list = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 55, AllowReselect = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        _list.ItemSelected += i => { if (_b != null && i >= 0 && i < _shown.Count) { _b.Select(_shown[(int)i]); ShowEntry(); } };
        cols.AddChild(_list);
        var rightBox = new VBoxContainer(); rightBox.AddThemeConstantOverride("separation", 4);
        _detail = new RichTextLabel { BbcodeEnabled = true, FitContent = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, ScrollActive = false };
        rightBox.AddChild(_detail);
        _links = new VBoxContainer(); _links.AddThemeConstantOverride("separation", 3); rightBox.AddChild(_links);
        _whyText = new RichTextLabel { BbcodeEnabled = true, FitContent = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, ScrollActive = false };
        rightBox.AddChild(_whyText);
        var scroll = UiKit.Scroll(rightBox);
        var panel = UiKit.Panel(scroll); panel.SizeFlagsHorizontal = SizeFlags.ExpandFill; panel.SizeFlagsVertical = SizeFlags.ExpandFill; panel.SizeFlagsStretchRatio = 45;
        cols.AddChild(panel);

        var bottom = new HBoxContainer(); bottom.AddThemeConstantOverride("separation", 8); root.AddChild(bottom);
        var back = UiKit.Key(L.T("UI_BACK"), "menu.back", 130, 30); back.Pressed += _router.Pop; bottom.AddChild(back);
        _prev = UiKit.Key(L.T("ch_ui_previous"), "menu.previous", 230, 30); _prev.Pressed += Previous; bottom.AddChild(_prev);
        _here = UiKit.Key(L.T("ch_ui_open_here"), "menu.open_here", 230, 30); _here.Pressed += OpenHere; bottom.AddChild(_here);
        bottom.AddChild(UiKit.Hint(L.F("ch_ui_close", InputBootstrap.Hint("world.panel_chronicle"))));
        _refresh();
        Take();
    }

    /// <summary>Takes the barrier's view when there is a new one, keeping the view, the filters and the open entry.</summary>
    void Take()
    {
        var v = _data();
        if (v == null || ReferenceEquals(v, _view)) { if (_view == null) { _count.Text = L.T("ch_ui_loading"); _list.Clear(); } return; }
        var old = _b; _view = v;
        _b = new ChronicleBrowser(v, _types, e => ChronicleReader.Line(e, _types, v, L.T, x => x));
        if (old != null) { _b.SetScope(old.Scope); _b.SetFamily(old.Family); _b.SetSearch(old.Search); if (old.Current != null) _b.Select(old.Current.Id); }
        else
        {
            _b.SetScope(_scope);
            if (_focus is { } f) _b.Open(f);
            else if (FocusLatestMine && _b.Rows.Count > 0) _b.Open(_b.View.Entries.LastOrDefault(e => e.Subjects.Contains(_b.View.Hero))?.Id ?? _b.Rows[0].Id);
            else if (FocusDeepest && _b.Deepest() is { } deep) _b.Open(deep.Id);
            for (int i = 0; i < StartFollow; i++) _b.FollowCause();
        }
        Fill();
    }

    void Fill()
    {
        if (_b == null) return;
        _shown.Clear(); _list.Clear();
        foreach (var e in _b.Rows)
        {
            _shown.Add(e.Id);
            string season = UiKit.SeasonName(e.When.Season);
            _list.AddItem($"{e.When.Year,3} {season[..Math.Min(3, season.Length)]}  {_b.Text(e)}");
        }
        int sel = _b.Current != null ? _shown.IndexOf(_b.Current.Id) : -1;
        if (sel < 0 && _b.Rows.Count > 0 && _b.Current == null) { _b.Select(_b.Rows[0].Id); sel = 0; }
        if (sel >= 0) { _list.Select(sel); _list.EnsureCurrentIsVisible(); }
        _count.Text = _b.Matched > ChronicleBrowser.MaxRows ? L.F("LG_COUNT_MORE", ChronicleBrowser.MaxRows, _b.Matched) : L.F("LG_COUNT", _b.Matched, _b.Scope == ChronicleScope.Legends ? _b.View.History.Count : _b.View.Entries.Count);
        foreach (var s in Enum.GetValues<ChronicleScope>()) _tabs[(int)s].ButtonPressed = s == _b.Scope;
        _familyBtn.Text = L.F("LG_TYPE", Family(_b.Family));
        ShowEntry();
    }

    void ShowEntry()
    {
        if (_b == null) return;
        _prev.Disabled = _b.TrailDepth == 0;
        foreach (var ch in _links.GetChildren()) { _links.RemoveChild(ch); ch.QueueFree(); }
        var e = _b.Current;
        _here.Disabled = e == null || PlaceOf(e) == null;
        if (e == null) { _detail.Text = L.T("ch_ui_empty"); _whyText.Text = ""; return; }
        var v = _b.View;
        string gold = PixelTheme.Gold.ToHtml(false), dim = PixelTheme.Dim.ToHtml(false);
        string Esc(string s) => s.Replace("[", "[lb]");
        string heading = _types.Get(e.TypeId) is { Def.Legacy: true } row && L.T(row.TextKey) is { Length: > 0 } h && h != row.TextKey ? h : ChronicleReader.Pretty(e.TypeId);
        var sb = new StringBuilder();
        sb.Append($"[color=#{gold}]{Esc(_b.Text(e))}[/color]\n");
        string fam = ChronicleFamilies.Of(e);
        sb.Append($"[color=#{dim}]{UiKit.SeasonName(e.When.Season)} {e.When.Year}, day {e.When.DayOfSeason}  -  {Esc(heading)}{(fam.Length > 0 ? "  -  " + Family(fam) : "")}[/color]\n");
        var who = e.Subjects.Select(s => v.NameOf(s.Value)).Where(n => n.Length > 0).ToList();
        if (who.Count > 0) sb.Append($"\n[color=#{dim}]{L.T("LG_PEOPLE")}[/color] {Esc(string.Join(", ", who))}\n");
        _detail.Text = sb.ToString();
        sb.Clear();
        sb.Append($"[color=#{dim}]{L.T("ch_ui_why")}[/color]\n");
        if (e.Causes.Count == 0) sb.Append("- " + L.T("ch_ui_no_cause") + "\n");
        foreach (var (c, ce) in _b.Why(e))
        {
            bool drift = c.Kind == CauseKind.MarketDrift;
            string note = ChronicleTypes.FormatNote(c, L.T, ChronicleReader.Names(v, x => x));
            if (note.Length == 0 && ce == null) note = c.Kind.ToString();
            sb.Append("- ").Append(Esc(drift ? L.F("ch_ui_drift", note) : note));
            if (ce != null) sb.Append($"{(note.Length > 0 ? "  " : "")}[color=#{dim}]<- {Esc(_b.Text(ce))} ({ce.When.Year})[/color]");
            sb.Append('\n');
        }
        // the chain back to where it began: the first cause that names an entry, step by step
        var chain = new List<ChronicleEntry>(); var cur = e; var seen = new HashSet<EntityId> { e.Id };
        while (chain.Count < 7 && _b.Why(cur).FirstOrDefault(w => w.Entry != null).Entry is { } up && seen.Add(up.Id)) { chain.Add(up); cur = up; }
        if (chain.Count > 1)
        {
            sb.Append($"\n[color=#{dim}]{L.T("ch_ui_chain")}[/color]\n");
            for (int i = 0; i < chain.Count; i++) sb.Append($"{new string(' ', i * 2)}<- {Esc(_b.Text(chain[i]))} ({chain[i].When.Year})\n");
            var rootCause = _b.Why(chain[^1]).FirstOrDefault(w => w.Entry == null);
            sb.Append(new string(' ', chain.Count * 2)).Append($"[color=#{dim}]{L.T("ch_ui_root")}[/color] {(rootCause.Cause != null ? Esc(ChronicleReader.Note(rootCause.Cause, v, L.T, x => x)) : "")}\n");
        }
        _whyText.Text = sb.ToString();

        bool first = true;
        foreach (var (c, ce) in _b.Why(e).Where(w => w.Entry != null))
        {
            string text = L.F("LG_CAUSE", ce!.When.Year, _b.Text(ce));
            var b = first ? UiKit.Key(text, "menu.follow_cause", 0, 28) : new Button { Text = text, CustomMinimumSize = new Vector2(0, 28), Alignment = HorizontalAlignment.Left };
            b.ClipText = true; first = false;
            var id = ce.Id; b.Pressed += () => { _b!.Open(id); Fill(); };
            _links.AddChild(b);
        }
        var after = _b.Followed(e);
        if (after.Count > 0) _links.AddChild(UiKit.Hint(L.T("ch_ui_followed")));
        first = true;
        foreach (var fe in after.Take(5))
        {
            string text = L.F("LG_EFFECT", fe.When.Year, _b.Text(fe));
            var b = first ? UiKit.Key(text, "menu.follow_effect", 0, 28) : new Button { Text = text, CustomMinimumSize = new Vector2(0, 28), Alignment = HorizontalAlignment.Left };
            b.ClipText = true; first = false;
            var id = fe.Id; b.Pressed += () => { _b!.Open(id); Fill(); };
            _links.AddChild(b);
        }
        if (after.Count > 5) _links.AddChild(UiKit.Hint(L.F("LG_MORE_EFFECTS", after.Count - 5)));
    }

    /// <summary>The settlement an entry concerns: its first settlement subject or place parameter.</summary>
    EntityId? PlaceOf(ChronicleEntry e)
    {
        foreach (var s in e.Subjects) if (s.Kind == IdKind.Settlement) return s;
        if (e.Params != null) foreach (var p in e.Params) if (p.Kind == ChronParamKind.Place && new EntityId((ulong)p.Value).Kind == IdKind.Settlement) return new EntityId((ulong)p.Value);
        return null;
    }

    /// <summary>Smoke: redraws after the browser was moved from outside.</summary>
    public void DebugRefresh() => Fill();

    void Previous() { if (_b != null && _b.Back()) Fill(); }
    void OpenHere() { if (_b?.Current is { } e && PlaceOf(e) is { } p) OpenPlace?.Invoke(p); }

    public override void _Process(double delta)
    {
        _acc += delta;
        if (_acc > 1.0) { _acc = 0; _refresh(); Take(); }
        if (ShotPath == null || !Visible || ++_shotWait != 20) return;
        var img = GetViewport().GetTexture().GetImage();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ShotPath) ?? ".");
        GD.Print($"[chronicle] screenshot {ShotPath}: {img.SavePng(ShotPath)}");
        GetTree().Quit();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible || _b == null) return;
        if (Actions.Pressed(e, "world.panel_chronicle")) { _router.Pop(); GetViewport().SetInputAsHandled(); return; }
        if (Actions.Pressed(e, "menu.search")) _search.GrabFocus();
        else if (Actions.Pressed(e, "menu.filter_type")) { _b.CycleFamily(); Fill(); }
        else if (Actions.Pressed(e, "menu.filter_realm")) { _b.CycleScope(); Fill(); }
        else if (Actions.Pressed(e, "menu.previous")) Previous();
        else if (Actions.Pressed(e, "menu.follow_cause")) { if (_b.FollowCause()) Fill(); }
        else if (Actions.Pressed(e, "menu.follow_effect")) { if (_b.FollowEffect()) Fill(); }
        else if (Actions.Pressed(e, "menu.open_here")) OpenHere();
        else return;
        GetViewport().SetInputAsHandled();
    }
}
