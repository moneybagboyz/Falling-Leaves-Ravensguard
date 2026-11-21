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
    Button _craft = null!, _batchBtn = null!, _targetBtn = null!;
    CheckBox _canBox = null!, _skipTemper = null!, _bringMats = null!;
    Label _stationLbl = null!;
    VBoxContainer _commPanel = null!;
    Control _detailPanel = null!;
    bool _commission;
    List<CraftRecipe> _shown = [];
    CraftJob? _job;
    int _speed = 1, _shotWait;
    double _refresh;
    public string? ShotPath { get; init; }
    /// <summary>Dev: the recipe to select for a screenshot.</summary>
    public string? StartRecipe { get; init; }
    /// <summary>Dev: open on the Commission tab (P5-E screenshots).</summary>
    public bool StartCommission { get; init; }

    static readonly string[] Tabs = ["all", "fire", "food", "medical", "cloth", "craft", "smith", "build", "repair", "practice"];

    public CraftingScreen(ScreenRouter router, CraftingSession session) { _router = router; _s = session; }

    public override void _Ready()
    {
        var margin = UiKit.Margin(this, 14);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 6); margin.AddChild(root);
        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 12); root.AddChild(head);
        head.AddChild(UiKit.Title(L.T("CRAFT_TITLE")));
        // P5-E (crafting-full 6): the station panel reads the real room ("Smithy: forge x2, anvil (fine)")
        _stationLbl = new Label { Text = _s.StationLine(), VerticalAlignment = VerticalAlignment.Center, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true };
        _stationLbl.AddThemeColorOverride("font_color", new Color("c9b98f"));
        head.AddChild(_stationLbl);
        _tabs = new HBoxContainer(); _tabs.AddThemeConstantOverride("separation", 3); root.AddChild(_tabs);
        foreach (var t in Tabs)
        {
            string tab = t; var b = new Button { Text = t is "build" or "repair" ? L.T("cf_ui_cat_" + t) : L.T("CRAFT_CAT_" + t.ToUpperInvariant()), ToggleMode = true, CustomMinimumSize = new Vector2(56, 26), ButtonPressed = t == _category };
            b.Pressed += () => { _category = tab; _commission = false; foreach (var c in _tabs.GetChildren().OfType<Button>()) c.ButtonPressed = c == b; Rebuild(); };
            _tabs.AddChild(b);
        }
        var comm = new Button { Text = L.T("cf_ui_tab_commission"), ToggleMode = true, CustomMinimumSize = new Vector2(96, 26) };
        comm.Pressed += () => { _commission = true; _s.LookAround(); foreach (var c in _tabs.GetChildren().OfType<Button>()) c.ButtonPressed = c == comm; Rebuild(); };
        _tabs.AddChild(comm);
        var filters = new HBoxContainer(); filters.AddThemeConstantOverride("separation", 8); root.AddChild(filters);
        _search = new LineEdit { PlaceholderText = L.T("CRAFT_SEARCH"), CustomMinimumSize = new Vector2(220, 28) }; UiKit.CaptureKeys(_search); _search.TextChanged += _ => Rebuild(); filters.AddChild(_search);
        _with = new LineEdit { PlaceholderText = L.T("CRAFT_WITH"), CustomMinimumSize = new Vector2(220, 28) }; UiKit.CaptureKeys(_with); _with.TextChanged += _ => Rebuild(); filters.AddChild(_with);
        var can = _canBox = new CheckBox { Text = L.T("CRAFT_CAN_NOW") }; can.Toggled += on => { _canOnly = on; Rebuild(); }; filters.AddChild(can);
        var unk = new CheckBox { Text = L.T("CRAFT_UNKNOWN") }; unk.Toggled += on => { _showUnknown = on; Rebuild(); }; filters.AddChild(unk);
        var keys = UiKit.Hint(L.T("CRAFT_KEYS")); keys.ClipText = true; keys.SizeFlagsHorizontal = SizeFlags.ExpandFill; keys.CustomMinimumSize = new Vector2(0, 0); filters.AddChild(keys);                // P5-E: in the filter row, so the action bar has room for the temper and target controls at 960 x 540

        var cols = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; cols.AddThemeConstantOverride("separation", 10); root.AddChild(cols);
        _list = new ItemList { CustomMinimumSize = new Vector2(300, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        _list.ItemSelected += i => { _selected = _shown[(int)i].Id; _batch = 1; Detail(); };
        cols.AddChild(_list);
        _detail = new RichTextLabel { BbcodeEnabled = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, FitContent = false, ScrollActive = true };
        _detailPanel = UiKit.Panel(_detail, 0, 0);
        cols.AddChild(_detailPanel);
        ((Control)_detail.GetParent()).SizeFlagsHorizontal = SizeFlags.ExpandFill;
        // P5-E (crafting-full 4.7): the Commission tab: the makers of the place with price and days, and your commissions
        var scroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, Visible = false, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _commPanel = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; _commPanel.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_commPanel); cols.AddChild(scroll);

        _work = new VBoxContainer(); _work.AddThemeConstantOverride("separation", 4); root.AddChild(_work);
        var bar = new HBoxContainer(); bar.AddThemeConstantOverride("separation", 8); root.AddChild(bar);
        _craft = new Button { Text = L.T("CRAFT_CRAFT"), CustomMinimumSize = new Vector2(120, 32) }; _craft.Pressed += StartSelected; bar.AddChild(_craft);
        _batchBtn = new Button { Text = "", CustomMinimumSize = new Vector2(150, 32) };
        _batchBtn.Pressed += () => { var r = Selected(); if (r == null) return; _batch = _batch >= r.BatchMax ? 1 : _batch + 1; Detail(); };
        bar.AddChild(_batchBtn);
        _skipTemper = new CheckBox { Text = L.T("cf_ui_skip_temper"), Visible = false };
        _skipTemper.Toggled += _ => Detail(); bar.AddChild(_skipTemper);
        _targetBtn = new Button { Text = "", Visible = false, CustomMinimumSize = new Vector2(150, 32), ClipText = true };
        _targetBtn.Pressed += () => { if (Selected() is { } r) { _s.NextTarget(r); Detail(); } }; bar.AddChild(_targetBtn);
        _bringMats = new CheckBox { Text = L.T("cf_ui_bring_materials"), Visible = false, ButtonPressed = false };
        _bringMats.Toggled += _ => Detail(); bar.AddChild(_bringMats);
        var back = UiKit.Key(L.T("UI_BACK"), "menu.back", 120, 32); back.Pressed += Close; bar.AddChild(back);
        Rebuild();
        if (StartRecipe != null) Pick(StartRecipe);
        if (StartCommission) comm.EmitSignal(BaseButton.SignalName.Pressed);
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
        if (_commission) { p = null!; return Mark.Green; }
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
            if (!_commission && _category != "all" && r.Category != _category) continue;
            if (_commission && (!r.Commissionable || r.Business)) continue;
            if (q.Length > 0 && !r.Name.ToLowerInvariant().Contains(q) && !r.Id.Contains(q)) continue;
            if (with.Length > 0 && !r.Slots.Any(sl => sl.Alts.Any(a => _s.Db.Goods.StringId(a.Good).Contains(with) || _s.Db.Goods.Def(a.Good).Name.ToLowerInvariant().Contains(with)))) continue;
            var m = MarkOf(r, out _);
            if (m == Mark.Unknown && !_showUnknown && !_commission) continue;
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
    string Amount(ushort g, long qty) => _s.Db.Goods.IsPiece(g) ? $"{qty}" : _s.Db.Goods.Def(g).Unit == Ravensguard.Content.GoodUnit.Litre ? $"{qty * 1000 / _s.Db.Goods.Def(g).LitreMassG / 1000.0:0.##} L" : Ravensguard.Sim.Trade.Quotes.Weight(qty);

    void Detail()
    {
        var r = Selected();
        _detailPanel.Visible = !_commission; ((Control)_commPanel.GetParent()).Visible = _commission;
        if (_commission) { CommissionPane(r); return; }
        if (r == null) { _detail.Text = L.T("CRAFT_NONE"); _craft.Disabled = true; _batchBtn.Text = ""; _skipTemper.Visible = false; _targetBtn.Visible = false; return; }
        _batch = Math.Clamp(_batch, 1, r.BatchMax);
        bool canSkip = r.IsSmithing && r.Steps.Any(s => s.Smith == SmithStep.Temper && s.Optional);
        _skipTemper.Visible = canSkip; _bringMats.Visible = false;
        var p = _s.Plan(r, _batch, null, canSkip && _skipTemper.ButtonPressed);
        var sb = new StringBuilder();
        sb.Append("[b]").Append(r.Name).Append("[/b]  ").Append(C("7a6f66", r.Category is "build" or "repair" ? L.T("cf_ui_cat_" + r.Category) : L.T("CRAFT_CAT_" + r.Category.ToUpperInvariant()))).Append('\n');
        if (r.Note.Length > 0) sb.Append(C("7a6f66", r.Note)).Append('\n');
        if (r.Guild != null) sb.Append(C("d8b04a", _s.Db.Guilds.TryGetValue(r.Guild, out var gd) ? L.F("cf_ui_guild_recipe", gd.Name) : L.T("cf_guild_only"))).Append('\n');
        // the piece it works on
        _targetBtn.Visible = r.NeedsTarget;
        if (r.NeedsTarget)
        {
            var t = _s.Targets(r); var cur = _s.TargetFor(r);
            var row = t.FirstOrDefault(x => x.Id == cur);
            _targetBtn.Text = L.T("cf_ui_other_piece"); _targetBtn.Disabled = t.Count < 2;
            sb.Append(t.Count == 0 ? C("d86a5a", L.T("cf_ui_no_target")) : C("8fd16b", L.F("cf_ui_target", row.Name, row.Condition / 10))).Append('\n');
        }
        // the expected result at a glance: the tiers with the masterwork chance and the terms of the score (station, material, mastery, field kit, smithing)
        if (r.Results.Any(x => x.IsItem) || r.IsRepair || r.Reforge) sb.Append(C("c9b98f", L.F("cf_ui_expected", QualityLine(p) + TermsLine(p, r)))).Append('\n');
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
                if (sup == CraftSite.Supply.Real && _s.Site.KitOnly(n)) t += "  (" + L.T("cf_ui_kit") + " -5)";
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
        if (!r.IsPractice && !r.IsRepair && r.Results.Length + r.Effect.Length > 0 || r.IsRepair || r.Reforge)
        {
            sb.Append("\n[b]").Append(L.T("CRAFT_QUALITY")).Append("[/b]\n  ");
            // P5-E (items 7): the expected tiers with the masterwork chance (the inspiration roll), to a tenth of a percent
            sb.Append(QualityLine(p)).Append('\n');
            string terms = TermsLine(p, r);
            if (terms.Length > 0) sb.Append("  ").Append(C("c9b98f", terms.Trim())).Append('\n');
            if (p.SkipTemper) sb.Append("  ").Append(C("e0a84a", L.T("cf_ui_brittle"))).Append('\n');
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
        // P5-E: where a missing tool could come from (a field kit among them), and what is fetched from elsewhere in the building
        foreach (var h in _s.ToolHints(p)) sb.Append("  ").Append(C("9fb4c8", h)).Append('\n');
        _detail.Text = ""; _detail.AppendText(sb.ToString());
        _craft.Disabled = !p.CanStart;
        _batchBtn.Text = r.BatchMax > 1 ? L.F("CRAFT_BATCH", _batch, r.BatchMax) : "";
        _batchBtn.Visible = r.BatchMax > 1;
    }

    static string Signed(int v) => v > 0 ? "+" + v : v.ToString();

    static string QualityLine(CraftPlan p)
    {
        var names = new[] { L.T("Q_CRUDE"), L.T("Q_POOR"), L.T("Q_COMMON"), L.T("Q_FINE"), L.T("Q_MASTERWORK") };
        return string.Join(", ", Enumerable.Range(0, 5).Where(t => p.QualityMilli[t] > 0).Select(t => $"{names[t]} {p.QualityMilli[t] / 410.0:0.#}%"));
    }

    static string TermsLine(CraftPlan p, CraftRecipe r)
    {
        var terms = new List<string>();
        if (p.StationBonus != 0) terms.Add(L.F("cf_ui_term_station", Signed(p.StationBonus)));
        if (p.MaterialBonus != 0) terms.Add(L.F("cf_ui_term_material", Signed(p.MaterialBonus)));
        if (p.MasteryBonus != 0) terms.Add(L.F("cf_ui_term_mastery", Signed(p.MasteryBonus)));
        if (p.KitPenalty != 0) terms.Add(L.F("cf_ui_term_kit", Signed(p.KitPenalty)));
        if (p.ProcessBonus != 0 || r.IsSmithing) terms.Add(L.F("cf_ui_term_process", Signed(p.ProcessBonus)));
        return terms.Count == 0 ? "" : "   " + string.Join("  ", terms);
    }

    /// <summary>The Commission tab: the makers of the place who could make the selected recipe (price, days, Order), then your commissions (state, where, Collect or Cancel).</summary>
    void CommissionPane(CraftRecipe? r)
    {
        foreach (var c in _commPanel.GetChildren()) { _commPanel.RemoveChild(c); c.QueueFree(); }
        _skipTemper.Visible = false; _targetBtn.Visible = false; _bringMats.Visible = true; _craft.Disabled = true; _batchBtn.Visible = false;
        Label Line(string t, string col = "e8dcc0") { var l = new Label { Text = t, AutowrapMode = TextServer.AutowrapMode.WordSmart }; l.AddThemeColorOverride("font_color", new Color(col)); _commPanel.AddChild(l); return l; }
        if (r != null)
        {
            Line(L.F("cf_ui_commission_for", r.Name), "f0d890");
            var quotes = _s.Quotes(r, _bringMats.ButtonPressed);
            if (quotes.Count == 0) Line(L.T(_s.World != null && _s.World.View?.CraftPlace == null ? "cf_ui_asking" : "cf_ui_no_makers"), "9a8f80");
            foreach (var q in quotes.Take(5))
            {
                var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8);
                var info = new Label { Text = L.F("cf_ui_maker_line", q.Name, q.Trade, q.Skill, UiKit.Money(q.PriceMites), q.Days), SizeFlagsHorizontal = SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart };
                info.AddThemeColorOverride("font_color", new Color(q.Knows ? "e8dcc0" : "7a6f66"));
                var order = new Button { Text = L.T("cf_ui_order"), Disabled = !q.Knows || q.PriceMites / 2 > _s.Coin, CustomMinimumSize = new Vector2(70, 26) };
                var qq = q; order.Pressed += () => { if (_s.Order(r, qq, _bringMats.ButtonPressed)) _router.ShowToast(L.F("cf_ui_ordered", r.Name, qq.Name)); Detail(); };
                row.AddChild(order);                                            // the button first: it stays on screen however long the line
                row.AddChild(info);
                _commPanel.AddChild(row);
            }
            Line(L.T(_bringMats.ButtonPressed ? "cf_ui_note_bring" : "cf_ui_note_his"), "9a8f80");
        }
        Line(" ");
        Line(L.T("cf_ui_your_commissions"), "f0d890");
        var mine = _s.MyCommissions().Take(6).ToList();
        if (mine.Count == 0) Line(L.T("cf_ui_none_yet"), "9a8f80");
        foreach (var c in mine)
        {
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8);
            long nowH = (_s.WorldNow?.Invoke() ?? _s.NowSec) / 3600;
            string when = c.State == CommissionState.Ready ? L.T("cf_ui_ready_now") : c.State is CommissionState.Queued or CommissionState.Working ? L.F("cf_ui_ready_in", Math.Max(1, (c.DueHour - nowH + 23) / 24)) : "";
            var l = new Label { Text = L.F("cf_ui_commission_line", _s.Db[c.Recipe].Name, _s.MakerName(c.Maker), L.T("cf_cstate_" + c.State.ToString().ToLowerInvariant()), when, UiKit.Money(c.PriceMites - c.Escrow)), SizeFlagsHorizontal = SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart };
            if (c.State == CommissionState.Ready) { var b = new Button { Text = L.T("cf_ui_collect"), CustomMinimumSize = new Vector2(70, 26) }; var cc = c; b.Pressed += () => { _s.CollectCommission(cc); Detail(); }; row.AddChild(b); }
            if (c.State is CommissionState.Queued or CommissionState.Working) { var b = new Button { Text = L.T("cf_ui_cancel"), CustomMinimumSize = new Vector2(70, 26) }; var cc = c; b.Pressed += () => { _s.CancelCommission(cc); Detail(); }; row.AddChild(b); }
            row.AddChild(l);
            _commPanel.AddChild(row);
        }
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
