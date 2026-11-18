using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Crafting;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Game.UI;

/// <summary>
/// The crafting screen (N, basic-crafting spec 4.7): search and category tabs on the left, the recipes coloured by whether you can make them now (green), are missing something you can get (amber) or
/// cannot because it is too dark or you are too weak (grey), and the detail on the right: components with their alternatives and what you hold, tool qualities, light, skill, proficiencies, time, the
/// expected quality, the risk of failure and what a failure wastes. A second field answers "what can I make with this?". Craft starts the work and runs it at game speed while the screen is open;
/// Esc pauses it with the progress kept, and unfinished work nearby is listed to resume. Everything shown is computed by <see cref="CraftRunner.Plan"/>, the same numbers the work uses.
/// </summary>
public partial class CraftingScreen : Control
{
    readonly ScreenRouter _router;
    readonly CraftingSession _s;
    string _category = "all", _selected = "";
    bool _canOnly, _showUnknown;
    int _batch = 1;
    LineEdit _search = null!, _with = null!;
    ItemList _list = null!;
    RichTextLabel _detail = null!;
    HBoxContainer _tabs = null!;
    VBoxContainer _work = null!;
    Button _craft = null!, _batchBtn = null!;
    CheckBox _canBox = null!;
    List<CraftRecipe> _shown = [];
    CraftJob? _job;
    int _speed = 1, _shotWait;
    double _refresh;
    public string? ShotPath { get; init; }
    /// <summary>Dev: the recipe to select for a screenshot.</summary>
    public string? StartRecipe { get; init; }

    static readonly string[] Tabs = ["all", "fire", "food", "medical", "cloth", "craft", "smith", "practice"];

    public CraftingScreen(ScreenRouter router, CraftingSession session) { _router = router; _s = session; }

    public override void _Ready()
    {
        var margin = UiKit.Margin(this, 14);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 6); margin.AddChild(root);
        root.AddChild(UiKit.Title(L.T("CRAFT_TITLE")));
        _tabs = new HBoxContainer(); _tabs.AddThemeConstantOverride("separation", 4); root.AddChild(_tabs);
        foreach (var t in Tabs)
        {
            string tab = t; var b = new Button { Text = L.T("CRAFT_CAT_" + t.ToUpperInvariant()), ToggleMode = true, CustomMinimumSize = new Vector2(70, 28), ButtonPressed = t == _category };
            b.Pressed += () => { _category = tab; foreach (var c in _tabs.GetChildren().OfType<Button>()) c.ButtonPressed = c == b; Rebuild(); };
            _tabs.AddChild(b);
        }
        var filters = new HBoxContainer(); filters.AddThemeConstantOverride("separation", 8); root.AddChild(filters);
        _search = new LineEdit { PlaceholderText = L.T("CRAFT_SEARCH"), CustomMinimumSize = new Vector2(220, 28) }; UiKit.CaptureKeys(_search); _search.TextChanged += _ => Rebuild(); filters.AddChild(_search);
        _with = new LineEdit { PlaceholderText = L.T("CRAFT_WITH"), CustomMinimumSize = new Vector2(220, 28) }; UiKit.CaptureKeys(_with); _with.TextChanged += _ => Rebuild(); filters.AddChild(_with);
        var can = _canBox = new CheckBox { Text = L.T("CRAFT_CAN_NOW") }; can.Toggled += on => { _canOnly = on; Rebuild(); }; filters.AddChild(can);
        var unk = new CheckBox { Text = L.T("CRAFT_UNKNOWN") }; unk.Toggled += on => { _showUnknown = on; Rebuild(); }; filters.AddChild(unk);

        var cols = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; cols.AddThemeConstantOverride("separation", 10); root.AddChild(cols);
        _list = new ItemList { CustomMinimumSize = new Vector2(300, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        _list.ItemSelected += i => { _selected = _shown[(int)i].Id; _batch = 1; Detail(); };
        cols.AddChild(_list);
        _detail = new RichTextLabel { BbcodeEnabled = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, FitContent = false, ScrollActive = true };
        cols.AddChild(UiKit.Panel(_detail, 0, 0));
        ((Control)_detail.GetParent()).SizeFlagsHorizontal = SizeFlags.ExpandFill;

        _work = new VBoxContainer(); _work.AddThemeConstantOverride("separation", 4); root.AddChild(_work);
        var bar = new HBoxContainer(); bar.AddThemeConstantOverride("separation", 8); root.AddChild(bar);
        _craft = new Button { Text = L.T("CRAFT_CRAFT"), CustomMinimumSize = new Vector2(120, 32) }; _craft.Pressed += StartSelected; bar.AddChild(_craft);
        _batchBtn = new Button { Text = "", CustomMinimumSize = new Vector2(150, 32) };
        _batchBtn.Pressed += () => { var r = Selected(); if (r == null) return; _batch = _batch >= r.BatchMax ? 1 : _batch + 1; Detail(); };
        bar.AddChild(_batchBtn);
        var back = UiKit.Key(L.T("UI_BACK"), "menu.back", 160, 32); back.Pressed += Close; bar.AddChild(back);
        bar.AddChild(UiKit.Hint(L.T("CRAFT_KEYS")));
        Rebuild();
        if (StartRecipe != null) Pick(StartRecipe);
    }

    CraftRecipe? Selected() => _s.Db.TryGet(_selected, out var r) ? r : null;

    void Pick(string id)
    {
        _selected = id; var i = _shown.FindIndex(r => r.Id == id);
        if (i >= 0) { _list.Select(i); _list.EnsureCurrentIsVisible(); }
        Detail();
    }

    enum Mark { Green, Amber, Grey, Unknown }

    Mark MarkOf(CraftRecipe r, out CraftPlan p)
    {
        p = _s.Plan(r, 1);
        if (!p.Known) return Mark.Unknown;
        if (p.CanStart) return Mark.Green;
        return p.Light == LightState.TooDark || p.Missing.Any(m => m.StartsWith("needs ")) ? Mark.Grey : Mark.Amber;
    }

    void Rebuild()
    {
        string q = _search.Text.Trim().ToLowerInvariant(), with = _with.Text.Trim().ToLowerInvariant();
        _shown = [];
        var marks = new List<Mark>();
        foreach (var r in _s.Db.Recipes)
        {
            if (_category != "all" && r.Category != _category) continue;
            if (q.Length > 0 && !r.Name.ToLowerInvariant().Contains(q) && !r.Id.Contains(q)) continue;
            if (with.Length > 0 && !r.Slots.Any(sl => sl.Alts.Any(a => _s.Db.Goods.StringId(a.Good).Contains(with) || _s.Db.Goods.Def(a.Good).Name.ToLowerInvariant().Contains(with)))) continue;
            var m = MarkOf(r, out _);
            if (m == Mark.Unknown && !_showUnknown) continue;
            if (_canOnly && m != Mark.Green) continue;
            _shown.Add(r); marks.Add(m);
        }
        // green first, then amber, grey, unknown; by name within
        var order = Enumerable.Range(0, _shown.Count).OrderBy(i => (int)marks[i]).ThenBy(i => _shown[i].Name, StringComparer.Ordinal).ToList();
        _shown = [.. order.Select(i => _shown[i])]; var ms = order.Select(i => marks[i]).ToList();
        _list.Clear();
        for (int i = 0; i < _shown.Count; i++)
        {
            var r = _shown[i];
            _list.AddItem((ms[i] == Mark.Unknown ? "? " : "") + r.Name);
            _list.SetItemCustomFgColor(i, ms[i] switch { Mark.Green => new Color("8fd16b"), Mark.Amber => new Color("e0a84a"), Mark.Grey => new Color("7a6f66"), _ => new Color("5d85b0") });
        }
        int sel = _shown.FindIndex(r => r.Id == _selected);
        if (sel < 0 && _shown.Count > 0) { sel = 0; _selected = _shown[0].Id; }
        if (sel >= 0) _list.Select(sel);
        Detail();
        Work();
    }

    static string C(string hex, string t) => $"[color=#{hex}]{t}[/color]";
    string Gn(ushort g) => _s.Db.Goods.Def(g).Name.ToLowerInvariant();
    string Amount(ushort g, long qty) => _s.Db.Goods.IsPiece(g) ? $"{qty}" : _s.Db.Goods.Def(g).Unit == Ravensguard.Content.GoodUnit.Litre ? $"{qty * 1000 / _s.Db.Goods.Def(g).LitreMassG / 1000.0:0.##} L" : $"{qty / 1000.0:0.###} kg";

    void Detail()
    {
        var r = Selected();
        if (r == null) { _detail.Text = L.T("CRAFT_NONE"); _craft.Disabled = true; _batchBtn.Text = ""; return; }
        _batch = Math.Clamp(_batch, 1, r.BatchMax);
        var p = _s.Plan(r, _batch);
        var sb = new StringBuilder();
        sb.Append("[b]").Append(r.Name).Append("[/b]  ").Append(C("7a6f66", L.T("CRAFT_CAT_" + r.Category.ToUpperInvariant()))).Append('\n');
        if (r.Note.Length > 0) sb.Append(C("7a6f66", r.Note)).Append('\n');
        // components
        sb.Append("\n[b]").Append(L.T("CRAFT_COMPONENTS")).Append("[/b]\n");
        for (int i = 0; i < r.Slots.Length; i++)
        {
            var sp = p.Slots[i]; var alts = r.Slots[i].Alts;
            var parts = alts.Select((a, j) =>
            {
                bool ok = sp.Have[j] >= sp.Need[j];
                string t = $"{Amount(a.Good, sp.Need[j])} {Gn(a.Good)} ({Amount(a.Good, Math.Min(sp.Have[j], 99_999_999))})";
                if (a.NeedsStep != null) t += " +" + a.NeedsStep;
                t = ok ? C("8fd16b", t) : C("d86a5a", t);
                return j == sp.Chosen ? "[b]" + t + "[/b]" : t;
            });
            sb.Append("  ").Append(string.Join(C("7a6f66", " / "), parts)).Append('\n');
        }
        foreach (var st in r.Steps.Where(x => x.FuelGood != ItemTable.NoGood)) sb.Append("  ").Append(L.F("CRAFT_FUEL", Amount(st.FuelGood, st.FuelQty * _batch), Gn(st.FuelGood))).Append('\n');
        // tools and station
        var needs = _s.Db.AllNeeds(r);
        if (needs.Length > 0 || r.Workstation != null)
        {
            sb.Append("\n[b]").Append(L.T("CRAFT_TOOLS")).Append("[/b]\n");
            foreach (var n in needs)
            {
                var sup = _s.Site.Check(n, out _);
                string t = $"{n.Quality} {n.Level}  ({L.T("CRAFT_HAVE")} {_s.Site.Level(n.Quality)})";
                sb.Append("  ").Append(sup == CraftSite.Supply.Real ? C("8fd16b", t) : sup == CraftSite.Supply.Improvised ? C("e0a84a", t + " " + L.T("CRAFT_IMPROVISED")) : C("d86a5a", t)).Append('\n');
            }
            if (r.Workstation != null) sb.Append("  ").Append(_s.Site.Stations.Contains(r.Workstation) ? C("8fd16b", _s.Db.Stations[r.Workstation].Name) : C("d86a5a", _s.Db.Stations[r.Workstation].Name)).Append('\n');
        }
        if (r.MinLight > 0)
            sb.Append("  ").Append(p.Light == LightState.TooDark ? C("d86a5a", L.F("CRAFT_LIGHT_DARK", r.MinLight, _s.Site.Light)) : p.Light == LightState.Dim ? C("e0a84a", L.F("CRAFT_LIGHT_DIM", r.MinLight, _s.Site.Light)) : C("8fd16b", L.F("CRAFT_LIGHT_OK", r.MinLight, _s.Site.Light))).Append('\n');
        // skill and proficiency
        sb.Append("\n[b]").Append(L.T("CRAFT_SKILL")).Append("[/b]\n");
        sb.Append("  ").Append(r.Skill == Ravensguard.Sim.People.Skill.None ? "-" : $"{r.Skill} {_s.SkillLevel(r)}  ({L.T("CRAFT_DIFFICULTY")} {r.Difficulty})").Append('\n');
        foreach (var u in r.Profs)
        {
            int pm = _s.State.Know.Pm(CraftingSession.HeroId, u.Prof);
            sb.Append("  ").Append(L.F("CRAFT_PROF", _s.Db.Prof(u.Prof).Name, pm / 10, CraftMath.ProfTimePm(u.TimeMultPm, pm) / 10, CraftMath.ProfSkillPenalty(u.SkillPenalty, pm))).Append('\n');
        }
        // time, quality, risk
        sb.Append("\n[b]").Append(L.T("CRAFT_TIME")).Append("[/b]\n  ").Append(L.F("CRAFT_TIME_LINE", Dur(p.AttendedSeconds), r.Effort)).Append('\n');
        foreach (var st in r.Steps.Where(x => !x.Attended))
            sb.Append("  ").Append(L.F("CRAFT_WAIT_LINE", st.Id, Dur(st.Seconds), Dur(st.DeadlineSec), Dur(st.GraceSec))).Append('\n');
        if (!r.IsPractice && !r.IsRepair && r.Results.Length + r.Effect.Length > 0 || r.IsRepair)
        {
            sb.Append("\n[b]").Append(L.T("CRAFT_QUALITY")).Append("[/b]\n  ");
            var names = new[] { L.T("Q_CRUDE"), L.T("Q_POOR"), L.T("Q_COMMON"), L.T("Q_FINE"), L.T("Q_MASTERWORK") };
            var qs = Enumerable.Range(0, 5).Where(t => p.QualityCounts[t] > 0).Select(t => $"{names[t]} {(p.QualityCounts[t] * 100 + 20) / 41}%");
            sb.Append(string.Join(", ", qs)).Append('\n');
        }
        if (p.FailPm > 0) sb.Append("  ").Append(C("e0a84a", L.F("CRAFT_RISK", p.FailPm / 10, p.WastePct))).Append('\n');
        else sb.Append("  ").Append(C("8fd16b", L.T("CRAFT_NO_RISK"))).Append('\n');
        // results
        sb.Append("\n[b]").Append(L.T("CRAFT_RESULT")).Append("[/b]\n");
        if (r.IsPractice) sb.Append("  ").Append(L.F("CRAFT_PRACTICE", r.PracticeCap)).Append('\n');
        foreach (var x in r.Results) sb.Append("  ").Append(x.IsItem ? $"{_batch} x {x.Shape}" : $"{Amount(x.Good, x.Qty * _batch)} {Gn(x.Good)}").Append('\n');
        foreach (var b in r.Byproducts) sb.Append("  ").Append(C("7a6f66", L.F("CRAFT_BYPRODUCT", Amount(b.Good, b.Qty * _batch), Gn(b.Good)))).Append('\n');
        if (r.IsRepair) sb.Append("  ").Append(L.F("CRAFT_REPAIR", r.Repair!.Add / 10, r.Repair.Max / 10)).Append('\n');
        if (r.Effect.Length > 0) sb.Append("  ").Append(L.T("CRAFT_EFFECT_" + r.Effect.ToUpperInvariant())).Append('\n');
        // what is missing
        if (!p.Known) sb.Append("\n").Append(C("5d85b0", L.T("CRAFT_UNKNOWN_HOW"))).Append('\n');
        else if (p.Missing.Count > 0) { sb.Append("\n[b]").Append(C("e0a84a", L.T("CRAFT_MISSING"))).Append("[/b]\n"); foreach (var m in p.Missing) sb.Append("  ").Append(C("d86a5a", m)).Append('\n'); }
        _detail.Text = ""; _detail.AppendText(sb.ToString());
        _craft.Disabled = !p.CanStart || r.IsRepair;
        _batchBtn.Text = r.BatchMax > 1 ? L.F("CRAFT_BATCH", _batch, r.BatchMax) : "";
        _batchBtn.Visible = r.BatchMax > 1;
    }

    static string Dur(long sec) => sec >= 3600 ? $"{sec / 3600}h{(sec % 3600 / 60 > 0 ? $" {sec % 3600 / 60}m" : "")}" : $"{Math.Max(1, (sec + 30) / 60)} min";

    void StartSelected()
    {
        var r = Selected(); if (r == null) return;
        var plan = _s.Plan(r, _batch);
        if (!plan.CanStart) { _router.ShowToast(L.T("CRAFT_CANT")); return; }
        var job = _s.Start(plan);
        if (job == null && _s.World == null) { _router.ShowToast(L.T("CRAFT_CANT")); return; }
        _job = job; _seqSeen = _s.World?.View?.AppliedSeq ?? 0; _router.ShowToast(L.F("CRAFT_STARTED", r.Name));
        Work(); Rebuild();
    }

    void Work()
    {
        foreach (var c in _work.GetChildren()) { _work.RemoveChild(c); c.QueueFree(); }
        if (_job is { Closed: false } j)
        {
            var r = _s.Db[j.Recipe]; string step = j.StepIndex < r.Steps.Length ? r.Steps[j.StepIndex].Id : "-";
            long need = j.StepIndex < r.Steps.Length && r.Steps[j.StepIndex].Attended ? _s.Runner.StepNeed(j, j.StepIndex) : 1;
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8);
            row.AddChild(new Label { Text = L.F(j.State == CraftJobState.Waiting ? "CRAFT_WAITING" : "CRAFT_WORKING", r.Name, step) });
            row.AddChild(new ProgressBar { MinValue = 0, MaxValue = need, Value = j.State == CraftJobState.Waiting ? need : j.StepSeconds, CustomMinimumSize = new Vector2(240, 18), ShowPercentage = false });
            foreach (int sp in new[] { 1, 2, 4 }) { int x = sp; var b = new Button { Text = $"x{x}", ToggleMode = true, ButtonPressed = _speed == x, CustomMinimumSize = new Vector2(40, 24) }; b.Pressed += () => { _speed = x; _s.Speed(x); Work(); }; row.AddChild(b); }
            var pause = new Button { Text = j.State == CraftJobState.Paused ? L.T("CRAFT_RESUME") : L.T("CRAFT_PAUSE") };
            pause.Pressed += () => { if (j.State == CraftJobState.Paused) _s.Resume(j); else _s.Pause(j); Work(); };
            row.AddChild(pause);
            _work.AddChild(row);
        }
        foreach (var uj in _s.Unfinished().Where(x => x != _job).Take(3))
        {
            var r = _s.Db[uj.Recipe]; var u = uj; var row = new HBoxContainer();
            row.AddChild(new Label { Text = L.F("CRAFT_UNFINISHED", r.Name, u.StepIndex + 1, r.Steps.Length) });
            var res = new Button { Text = L.T("CRAFT_RESUME") }; res.Pressed += () => { if (u.State == CraftJobState.Waiting ? _s.Collect(u) : _s.Resume(u)) _job = u; Work(); };
            row.AddChild(res); _work.AddChild(row);
        }
    }

    public override void _Process(double delta)
    {
        if (_s.World != null) WorldProcess(delta);
        else if (_job is { State: CraftJobState.Active } j)
        {
            long dt = (long)Math.Max(1, delta * TimeConst.GameSecondsPerRealSecond * _speed);
            foreach (var e in _s.Work(j, dt)) if (e.Kind == CraftEventKind.Finished) _router.ShowToast(L.F("CRAFT_DONE", e.Text));
            _refresh += delta;
            if (_refresh > 0.25 || j.Closed) { _refresh = 0; Work(); if (j.Closed) Rebuild(); }
        }
        else if (_job is { State: CraftJobState.Waiting } w) { _s.NowSec += (long)(delta * TimeConst.GameSecondsPerRealSecond * _speed); }
        if (ShotPath == null || !Visible || ++_shotWait != 8) return;
        var img = GetViewport().GetTexture().GetImage();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ShotPath) ?? ".");
        GD.Print($"[crafting] screenshot {ShotPath}: {img.SavePng(ShotPath)}");
        GetTree().Quit();
    }

    long _seqSeen; double _sendWork;

    /// <summary>In a running world: the job is the world's. Twice a real second the hero's work so far goes to the world; each new view refreshes the mirror and the job shown.</summary>
    void WorldProcess(double delta)
    {
        if (_job is { State: CraftJobState.Active } j && (_sendWork += delta) > 0.5) { _sendWork = 0; _s.Work(j, 0); }
        _refresh += delta;
        if (_refresh < 0.2) return;
        _refresh = 0;
        if (!_s.Refresh()) return;
        foreach (var r in _s.Results(_seqSeen)) { _seqSeen = Math.Max(_seqSeen, r.Seq); _router.ShowToast(r.Text); }
        if (_job != null) _job = _s.Find(_job.Id);
        if (_job == null && _s.PendingStart >= 0)
        {
            _job = _s.Unfinished().Where(x => x.Recipe == _s.PendingStart && x.State == CraftJobState.Active).OrderByDescending(x => x.Id).FirstOrDefault();
            if (_job != null) _s.PendingStart = -1;
        }
        Work(); Rebuild();
    }

    void Close()
    {
        if (_job is { State: CraftJobState.Active } j) { if (_s.World != null) _s.Work(j, 0); _s.Pause(j); }     // Esc pauses with the progress kept (in a world: worked up to now first)
        if (_s.World != null && _speed != 1) _s.Speed(1);
        _router.Pop();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible) return;
        if (Actions.Pressed(e, "world.make")) Close();
        else if (e is InputEventKey { Pressed: true, Echo: false } k && !Actions.UiCaptured)
        {
            if (k.Keycode == Key.Slash) { _search.GrabFocus(); }
            else if (k.Keycode == Key.F) _canBox.ButtonPressed = !_canBox.ButtonPressed;
            else if (k.Keycode == Key.Enter) { StartSelected(); }
            else return;
        }
        else return;
        GetViewport().SetInputAsHandled();
    }
}
