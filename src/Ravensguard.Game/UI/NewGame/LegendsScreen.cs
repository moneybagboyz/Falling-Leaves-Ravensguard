using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Game.UI.NewGame;

/// <summary>
/// Legends viewer (plan §5 Phase 1): the world's chronicle as a searchable list (year, type, realm, person) with cause links you can follow
/// back to the event that explains each war, ruin or death, and consequence links that lead forward again.
/// </summary>
public partial class LegendsScreen : Control
{
    const int MaxShown = 3000;
    readonly ScreenRouter _router;
    readonly WorldHistory _h;
    readonly IReadOnlyList<ChronicleEntry> _all;
    readonly string[] _blob;
    readonly int[][] _realms;          // realm indices per entry
    readonly string[] _subjects;       // subject names per entry
    readonly Dictionary<EntityId, int> _index = new();
    readonly Dictionary<EntityId, List<int>> _effects = new();
    readonly List<string> _types;
    readonly List<string> _realmNames;
    readonly List<int> _shown = new();
    readonly Stack<int> _trail = new();
    readonly EntityId? _focus;
    int _typeIdx, _realmIdx, _current = -1;
    LineEdit _search = null!;
    Button _typeBtn = null!, _realmBtn = null!, _prev = null!;
    ItemList _list = null!;
    Label _count = null!;
    RichTextLabel _detail = null!;
    VBoxContainer _links = null!;

    public LegendsScreen(ScreenRouter router, LoadedWorld w, EntityId? focus = null) : this(router, w.History, focus) { }

    /// <summary>The viewer over a history alone (the in-game Codex opens it on an entry's source event).</summary>
    public LegendsScreen(ScreenRouter router, WorldHistory history, EntityId? focus = null)
    {
        _router = router; _h = history; _all = _h.Chronicle.All; _focus = focus;
        _blob = new string[_all.Count]; _realms = new int[_all.Count][]; _subjects = new string[_all.Count];

        var realmOf = new Dictionary<EntityId, int>();
        foreach (var r in _h.Realms) realmOf[r.Id] = r.Index;
        foreach (var s in _h.Settlements) if (s.Realm >= 0) realmOf[s.Id] = s.Realm;
        foreach (var n in _h.Notables) { if (n.Realm >= 0) realmOf[n.Id] = n.Realm; else if (n.HomeRealm >= 0) realmOf[n.Id] = n.HomeRealm; }
        string NameOf(EntityId id) => _h.GetNotable(id)?.Full ?? _h.GetSettlement(id)?.Name ?? _h.GetDynasty(id)?.Name ?? _h.GetArtefact(id)?.Name
            ?? (_h.Realms.FirstOrDefault(r => r.Id == id)?.Name ?? "");
        for (int i = 0; i < _all.Count; i++)
        {
            var e = _all[i];
            _index[e.Id] = i;
            foreach (var c in e.Causes) if (c.EventId is { } cid) { if (!_effects.TryGetValue(cid, out var l)) _effects[cid] = l = new(); l.Add(i); }
            _realms[i] = e.Subjects.Where(realmOf.ContainsKey).Select(s => realmOf[s]).Distinct().ToArray();
            _subjects[i] = string.Join(", ", e.Subjects.Select(NameOf).Where(n => n.Length > 0));
            _blob[i] = $"{e.When.Year} {e.When.Season} {e.TypeId} {e.Data.GetValueOrDefault("title", "")} {e.Data.GetValueOrDefault("text", "")} {_subjects[i]}".ToLowerInvariant();
        }
        _types = [""]; _types.AddRange(_all.Select(e => e.TypeId).Distinct().OrderBy(t => t, StringComparer.Ordinal));
        _realmNames = ["", .. _h.Realms.Select(r => r.Name)];
    }

    static string TypeName(string t) => t.Length == 0 ? "" : UiKit.Pretty(t.Replace('.', ' ').Replace('_', ' '));

    public override void _Ready()
    {
        var margin = UiKit.Margin(this, 12);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 6);
        margin.AddChild(root);
        root.AddChild(UiKit.Title(L.T("LG_TITLE")));
        var cols = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        cols.AddThemeConstantOverride("separation", 10);
        root.AddChild(cols);

        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        left.AddThemeConstantOverride("separation", 6);
        cols.AddChild(left);
        _search = new LineEdit { PlaceholderText = L.T("LG_SEARCH") + "  " + InputBootstrap.Hint("menu.search"), CustomMinimumSize = new Vector2(0, 30), ClearButtonEnabled = true };
        UiKit.CaptureKeys(_search);
        _search.TextChanged += _ => Refilter();
        left.AddChild(_search);
        var filters = new HBoxContainer();
        filters.AddThemeConstantOverride("separation", 6);
        _typeBtn = UiKit.Key("", "menu.filter_type", 0, 30); _typeBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill; _typeBtn.ClipText = true;
        _typeBtn.Pressed += CycleType;
        _realmBtn = UiKit.Key("", "menu.filter_realm", 0, 30); _realmBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill; _realmBtn.ClipText = true;
        _realmBtn.Pressed += CycleRealm;
        filters.AddChild(_typeBtn); filters.AddChild(_realmBtn);
        left.AddChild(filters);
        _count = UiKit.Hint("");
        left.AddChild(_count);
        _list = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill, AllowReselect = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        _list.ItemSelected += i => { if (i >= 0 && i < _shown.Count) Show(_shown[(int)i], push: true); };
        left.AddChild(_list);

        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", 6);
        cols.AddChild(right);
        _detail = new RichTextLabel { BbcodeEnabled = true, SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var dp = UiKit.Panel(_detail);
        dp.SizeFlagsVertical = SizeFlags.ExpandFill;
        right.AddChild(dp);
        _links = new VBoxContainer();
        _links.AddThemeConstantOverride("separation", 3);
        right.AddChild(_links);

        var bottom = new HBoxContainer();
        bottom.AddThemeConstantOverride("separation", 8);
        var back = UiKit.Key(L.T("UI_BACK"), "menu.back", 200);
        back.Pressed += _router.Pop;
        _prev = UiKit.Key(L.T("LG_PREVIOUS"), "menu.previous", 260);
        _prev.Pressed += Previous;
        bottom.AddChild(back); bottom.AddChild(_prev);
        root.AddChild(bottom);

        Refilter();
        if (_focus is { } f && _index.TryGetValue(f, out var fi)) Show(fi, push: false);
        else if (_shown.Count > 0) Show(_shown[0], push: false);
        UpdateButtons();
    }

    void UpdateButtons()
    {
        _typeBtn.Text = L.F("LG_TYPE", _typeIdx == 0 ? L.T("LG_ALL") : TypeName(_types[_typeIdx]));
        _realmBtn.Text = L.F("LG_REALM", _realmIdx == 0 ? L.T("LG_ALL") : _realmNames[_realmIdx]);
        _prev.Disabled = _trail.Count == 0;
    }

    void CycleType() { _typeIdx = (_typeIdx + 1) % _types.Count; Refilter(); }
    void CycleRealm() { _realmIdx = (_realmIdx + 1) % _realmNames.Count; Refilter(); }

    bool Matches(int i, string[] terms)
    {
        if (_typeIdx > 0 && _all[i].TypeId != _types[_typeIdx]) return false;
        if (_realmIdx > 0 && Array.IndexOf(_realms[i], _realmIdx - 1) < 0) return false;
        foreach (var t in terms) if (!_blob[i].Contains(t, StringComparison.Ordinal)) return false;
        return true;
    }

    void Refilter()
    {
        var terms = _search.Text.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        _shown.Clear(); _list.Clear();
        int total = 0;
        for (int i = 0; i < _all.Count; i++)
        {
            if (!Matches(i, terms)) continue;
            total++;
            if (_shown.Count >= MaxShown) continue;
            _shown.Add(i);
            var e = _all[i];
            _list.AddItem($"{e.When.Year,3} {UiKit.SeasonName(e.When.Season)[..Math.Min(3, UiKit.SeasonName(e.When.Season).Length)]}  {e.Data.GetValueOrDefault("title", e.TypeId)}");
        }
        _count.Text = total > MaxShown ? L.F("LG_COUNT_MORE", MaxShown, total) : L.F("LG_COUNT", total, _all.Count);
        if (_current >= 0) { int p = _shown.IndexOf(_current); if (p >= 0) _list.Select(p); }
        UpdateButtons();
    }

    void Show(int idx, bool push)
    {
        if (push && _current >= 0 && _current != idx) _trail.Push(_current);
        _current = idx;
        int pos = _shown.IndexOf(idx);
        if (pos < 0 && (_typeIdx != 0 || _realmIdx != 0 || _search.Text.Length > 0))
        {
            _typeIdx = 0; _realmIdx = 0; _search.Text = "";
            Refilter();
            pos = _shown.IndexOf(idx);
        }
        if (pos >= 0) { _list.Select(pos); _list.EnsureCurrentIsVisible(); }
        var e = _all[idx];
        string gold = PixelTheme.Gold.ToHtml(false), dim = PixelTheme.Dim.ToHtml(false);
        var sb = new StringBuilder();
        sb.Append($"[color=#{gold}]{e.Data.GetValueOrDefault("title", e.TypeId)}[/color]\n");
        sb.Append($"[color=#{dim}]{UiKit.SeasonName(e.When.Season)} {e.When.Year}  -  {TypeName(e.TypeId)}[/color]\n\n");
        sb.Append(e.Data.GetValueOrDefault("text", "")).Append("\n\n");
        if (_subjects[idx].Length > 0) sb.Append($"[color=#{dim}]{L.T("LG_PEOPLE")}[/color] {_subjects[idx]}\n");
        if (_realms[idx].Length > 0) sb.Append($"[color=#{dim}]{L.T("LG_REALMS")}[/color] {string.Join(", ", _realms[idx].Select(r => _h.Realms[r].Name))}\n");
        sb.Append($"\n[color=#{dim}]{L.T("LG_WHY")}[/color]\n");
        foreach (var c in e.Causes)
            sb.Append("- ").Append(c.Note.Length > 0 ? c.Note : c.Kind.ToString()).Append('\n');
        _detail.Text = sb.ToString();

        foreach (var ch in _links.GetChildren()) ch.QueueFree();
        bool firstCause = true;
        foreach (var c in e.Causes)
        {
            if (c.EventId is not { } cid || !_index.TryGetValue(cid, out var ci)) continue;
            var ce = _all[ci];
            string text = L.F("LG_CAUSE", ce.When.Year, ce.Data.GetValueOrDefault("title", ce.TypeId));
            var b = firstCause ? UiKit.Key(text, "menu.follow_cause", 0, 30) : new Button { Text = text, CustomMinimumSize = new Vector2(0, 30), Alignment = HorizontalAlignment.Left };
            b.ClipText = true;
            firstCause = false;
            int target = ci;
            b.Pressed += () => Show(target, push: true);
            _links.AddChild(b);
        }
        if (_effects.TryGetValue(e.Id, out var eff))
        {
            bool first = true;
            foreach (var ei in eff.Take(4))
            {
                var fe = _all[ei];
                string text = L.F("LG_EFFECT", fe.When.Year, fe.Data.GetValueOrDefault("title", fe.TypeId));
                var b = first ? UiKit.Key(text, "menu.follow_effect", 0, 30) : new Button { Text = text, CustomMinimumSize = new Vector2(0, 30), Alignment = HorizontalAlignment.Left };
                b.ClipText = true;
                first = false;
                int target = ei;
                b.Pressed += () => Show(target, push: true);
                _links.AddChild(b);
            }
            if (eff.Count > 4) _links.AddChild(UiKit.Hint(L.F("LG_MORE_EFFECTS", eff.Count - 4)));
        }
        UpdateButtons();
    }

    public void DebugFollow() { Follow(true); }

    void Previous()
    {
        if (_trail.Count == 0) return;
        Show(_trail.Pop(), push: false);
    }

    void Follow(bool cause)
    {
        if (_current < 0) return;
        var e = _all[_current];
        int? t = cause
            ? e.Causes.Select(c => c.EventId).Where(i => i != null && _index.ContainsKey(i.Value)).Select(i => (int?)_index[i!.Value]).FirstOrDefault()
            : _effects.TryGetValue(e.Id, out var l) && l.Count > 0 ? l[0] : null;
        if (t is { } ti) Show(ti, push: true);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible) return;
        if (Actions.Pressed(e, "menu.search")) _search.GrabFocus();
        else if (Actions.Pressed(e, "menu.filter_type")) CycleType();
        else if (Actions.Pressed(e, "menu.filter_realm")) CycleRealm();
        else if (Actions.Pressed(e, "menu.previous")) Previous();
        else if (Actions.Pressed(e, "menu.follow_cause")) Follow(true);
        else if (Actions.Pressed(e, "menu.follow_effect")) Follow(false);
        else return;
        GetViewport().SetInputAsHandled();
    }
}
