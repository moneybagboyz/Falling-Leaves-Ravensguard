using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Codex;
using Ravensguard.Sim.WorldGen.History;

namespace Ravensguard.Game.UI;

/// <summary>
/// The Codex (K, plan 2.8 and 3f): the in-game manual and the legends of the world you have heard of. Categories (legends, places, people, landmarks, rules),
/// a search box, a list in which locked entries show only as "?", and the page with a link into the legends viewer for entries that rest on a chronicle event.
/// </summary>
public partial class CodexScreen : Control
{
    readonly ScreenRouter _router;
    readonly CodexBook _book;
    readonly WorldHistory? _history;
    readonly List<CodexEntry> _shown = new();
    int _cat = -1; // -1 = all
    CodexEntry? _current;
    LineEdit _search = null!;
    Button _catBtn = null!, _open = null!;
    ItemList _list = null!;
    Label _count = null!;
    RichTextLabel _detail = null!;
    int _shotWait;
    double _debounce = -1; // seconds until the pending search runs; < 0 = nothing pending
    (CodexEntry E, string Blob)[] _blobs = Array.Empty<(CodexEntry, string)>(); // lower-cased "title text", built once, not on every key
    /// <summary>Dev: when set, the screen saves its own picture to this path and quits.</summary>
    public string? ShotPath { get; init; }
    /// <summary>Dev: category to start on (CodexCategory name) and entry id to show.</summary>
    public string? StartCategory { get; init; }
    public string? StartEntry { get; init; }

    public CodexScreen(ScreenRouter router, CodexBook book, WorldHistory? history) { _router = router; _book = book; _history = history; }

    static readonly CodexCategory[] Cats = Enum.GetValues<CodexCategory>();

    public override void _Ready()
    {
        if (StartCategory != null && Enum.TryParse<CodexCategory>(StartCategory, true, out var sc)) _cat = Array.IndexOf(Cats, sc);
        var margin = UiKit.Margin(this, 12);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 6);
        margin.AddChild(root);
        root.AddChild(UiKit.Title(L.T("CODEX_TITLE")));
        var cols = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        cols.AddThemeConstantOverride("separation", 10);
        root.AddChild(cols);

        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        left.AddThemeConstantOverride("separation", 6);
        cols.AddChild(left);
        _search = new LineEdit { PlaceholderText = L.T("CODEX_SEARCH") + "  " + InputBootstrap.Hint("menu.search"), CustomMinimumSize = new Vector2(0, 30), ClearButtonEnabled = true };
        UiKit.CaptureKeys(_search);
        _search.TextChanged += _ => _debounce = 0.18; // typing runs the search once the player pauses
        left.AddChild(_search);
        _catBtn = UiKit.Key("", "menu.filter_type", 0, 30); _catBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill; _catBtn.ClipText = true;
        _catBtn.Pressed += CycleCategory;
        left.AddChild(_catBtn);
        _count = UiKit.Hint("");
        left.AddChild(_count);
        _list = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill, AllowReselect = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        _list.ItemSelected += i => { if (i >= 0 && i < _shown.Count) Show(_shown[(int)i]); };
        left.AddChild(_list);

        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", 6);
        cols.AddChild(right);
        _detail = new RichTextLabel { BbcodeEnabled = true, SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var dp = UiKit.Panel(_detail);
        dp.SizeFlagsVertical = SizeFlags.ExpandFill;
        right.AddChild(dp);
        _open = UiKit.Key(L.T("CODEX_OPEN_LEGENDS"), "menu.open_here", 0, 32);
        _open.Pressed += OpenLegends;
        right.AddChild(_open);

        var bottom = new HBoxContainer();
        bottom.AddThemeConstantOverride("separation", 8);
        var back = UiKit.Key(L.T("UI_BACK"), "menu.back", 200);
        back.Pressed += _router.Pop;
        bottom.AddChild(back);
        bottom.AddChild(UiKit.Hint(L.F("CODEX_CLOSE_HINT", InputBootstrap.Hint("world.panel_codex"))));
        root.AddChild(bottom);

        _blobs = _book.Entries.Select(e => (e, (e.Title + " " + e.Text).ToLowerInvariant())).ToArray();
        Refilter();
        var start = StartEntry != null ? _book.Get(StartEntry) : null;
        start ??= _shown.FirstOrDefault(e => e.Unlocked);
        if (start != null) Show(start); else Show(null);
    }

    string CatName() => _cat < 0 ? L.T("LG_ALL") : L.T("CODEX_CAT_" + Cats[_cat].ToString().ToUpperInvariant());

    void CycleCategory() { _cat = _cat + 1 >= Cats.Length ? -1 : _cat + 1; Refilter(); }

    void Refilter()
    {
        _shown.Clear(); _list.Clear();
        CodexCategory? c = _cat < 0 ? null : Cats[_cat];
        var query = _search.Text;
        int total = 0, known = 0;
        foreach (var e in Filter(query, c).OrderBy(e => e.Unlocked ? 0 : 1)) // known pages first, then the "?" ones
        {
            total++;
            if (e.Unlocked) known++;
            // a search shows only what is known; the plain list shows the locked pages as "?" so you can see how much there is to find
            _shown.Add(e);
            _list.AddItem(e.Unlocked ? ItemText(e) : "?  " + L.T("CODEX_LOCKED"));
            if (!e.Unlocked) _list.SetItemCustomFgColor(_list.ItemCount - 1, PixelTheme.Dim);
        }
        _catBtn.Text = L.F("CODEX_CATEGORY", CatName());
        _count.Text = L.F("CODEX_COUNT", known, total);
        if (_current != null) { int p = _shown.IndexOf(_current); if (p >= 0) _list.Select(p); }
    }

    /// <summary>Same rules as <see cref="CodexBook.Search"/> (every term must appear; locked pages only show with an empty query) over the precomputed lower-case text.</summary>
    IEnumerable<CodexEntry> Filter(string query, CodexCategory? category)
    {
        var terms = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var (e, blob) in _blobs)
        {
            if (category != null && e.Category != category) continue;
            if (terms.Length == 0) { yield return e; continue; }
            if (!e.Unlocked) continue;
            bool all = true;
            foreach (var t in terms) if (!blob.Contains(t, StringComparison.Ordinal)) { all = false; break; }
            if (all) yield return e;
        }
    }

    static string ItemText(CodexEntry e) => e.Year >= 0 && e.Category is CodexCategory.Legend ? $"{e.Year,3}  {e.Title}" : e.Title;

    void Show(CodexEntry? e)
    {
        _current = e;
        string gold = PixelTheme.Gold.ToHtml(false), dim = PixelTheme.Dim.ToHtml(false);
        if (e == null) { _detail.Text = $"[color=#{dim}]{L.T("CODEX_EMPTY")}[/color]"; _open.Visible = false; return; }
        int pos = _shown.IndexOf(e);
        if (pos >= 0) { _list.Select(pos); _list.EnsureCurrentIsVisible(); }
        var sb = new StringBuilder();
        if (!e.Unlocked)
        {
            sb.Append($"[color=#{gold}]?[/color]\n[color=#{dim}]{L.F("CODEX_KIND", L.T("CODEX_CAT_" + e.Category.ToString().ToUpperInvariant()))}[/color]\n\n{L.T("CODEX_LOCKED_TEXT")}");
            _open.Visible = false;
        }
        else
        {
            sb.Append($"[color=#{gold}]{e.Title}[/color]\n");
            sb.Append($"[color=#{dim}]{L.F("CODEX_KIND", L.T("CODEX_CAT_" + e.Category.ToString().ToUpperInvariant()))}{(e.Year >= 0 ? "  -  " + L.F("CODEX_YEAR", e.Year) : "")}[/color]\n\n");
            sb.Append(CodexRules.ResolveKeys(e.Text));
            _open.Visible = e.Sources.Count > 0 && _history != null;
        }
        _detail.Text = sb.ToString();
    }

    void OpenLegends()
    {
        if (_current is not { Unlocked: true } e || e.Sources.Count == 0 || _history == null) return;
        _router.Push(new LegendsScreen(_router, _history, e.Sources[0]));
    }

    public override void _Process(double delta)
    {
        if (_debounce >= 0 && (_debounce -= delta) < 0) { _debounce = -1; Refilter(); }
        if (ShotPath == null || !Visible || ++_shotWait != 8) return;
        var img = GetViewport().GetTexture().GetImage();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ShotPath) ?? ".");
        GD.Print($"[codex] screenshot {ShotPath}: {img.SavePng(ShotPath)}");
        GetTree().Quit();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible) return;
        if (Actions.Pressed(e, "world.panel_codex")) _router.Pop();
        else if (Actions.Pressed(e, "menu.search")) _search.GrabFocus();
        else if (Actions.Pressed(e, "menu.filter_type")) CycleCategory();
        else if (Actions.Pressed(e, "menu.open_here")) OpenLegends();
        else return;
        GetViewport().SetInputAsHandled();
    }
}
