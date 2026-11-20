using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Flavour;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Medicine;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Game.UI;

/// <summary>What the treatment screen asks of the street view: the barrier's view of the patient, the verbs it sends (each a recorded hero command), the patients in reach.</summary>
public sealed class TreatmentLink
{
    public required Func<TreatView?> View { get; init; }
    public required Action<TreatKind, int> Do { get; init; }
    public required Action DressAll { get; init; }
    public required Func<IReadOnlyList<(ulong Id, string Name)>> Patients { get; init; }
    public required Action<ulong> Select { get; init; }
    public required Func<string> PatientName { get; init; }
    /// <summary>A physician's quote: pays the lines picked (all of them when none is picked); null when the hero treats.</summary>
    public Action<IReadOnlyList<TreatVerbView>>? Pay { get; init; }
    public Func<long>? Coin { get; init; }
}

/// <summary>
/// The treatment screen (medicine 4.1, 6; P6-B1): the paper-doll on the left (2x, 240 x 400), the selected part's wounds and verbs on the right (one row each: verb, time, uses, chance, the physician's
/// fee), the bar at the bottom with the queue and Dress all bleeding / Patient / Back. Up and Down pick a part, Left and Right a verb, Enter does it, D dresses all that bleeds, Tab turns to the next patient
/// in reach, Esc goes back. A modal screen (the Treatment context, alone with Global): the street keys are off while it is up and the world goes on, so the blood lost meanwhile is lost. A view over the
/// barrier's <see cref="TreatView"/>; every verb is a hero command, performed at the second its work ends.
/// </summary>
public partial class TreatmentScreen : Control, IHandlesBack
{
    internal const int DollW = 240, DollH = 400;
    const int VerbRowH = 22;
    readonly ScreenRouter _router;
    readonly TreatmentLink _link;
    TreatView? _shown;
    int _part = -1, _verb;
    int _shotWait;
    double _refresh;
    TreatmentPaperDoll _doll = null!;
    Label _title = null!, _status = null!, _queue = null!, _keys = null!, _partTitle = null!;
    VBoxContainer _wounds = null!, _verbs = null!;
    Button _dressBtn = null!, _patientBtn = null!;
    readonly HashSet<int> _picked = [];
    /// <summary>Dev: when set, the screen saves its own picture to this path and quits.</summary>
    public string? ShotPath { get; init; }
    public int Verbs { get; private set; }
    public int SelectedPart => _part;

    public TreatmentScreen(ScreenRouter router, TreatmentLink link, int part = -1) { _router = router; _link = link; _part = part; }

    public override void _Ready()
    {
        ActiveContexts.Enter(InputContext.Treatment);
        var margin = UiKit.Margin(this, 6);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 2); margin.AddChild(root);
        _title = UiKit.Title(""); root.AddChild(_title);
        _status = UiKit.Hint(""); _status.ClipText = true; root.AddChild(_status);
        var cols = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; cols.AddThemeConstantOverride("separation", 8); root.AddChild(cols);
        _doll = new TreatmentPaperDoll { CustomMinimumSize = new Vector2(DollW, DollH), Picked = i => { _part = i; _verb = 0; _picked.Clear(); Build(); } };
        var dp = UiKit.Panel(_doll, 0, 0); dp.CustomMinimumSize = new Vector2(DollW + 12, 0); cols.AddChild(dp);
        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; right.AddThemeConstantOverride("separation", 3);
        _partTitle = new Label { ThemeTypeVariation = "TitleLabel" }; right.AddChild(_partTitle);
        _wounds = new VBoxContainer(); _wounds.AddThemeConstantOverride("separation", 0);
        var wp = UiKit.Panel(_wounds, 0, 0); wp.CustomMinimumSize = new Vector2(0, 84); right.AddChild(wp);
        _verbs = new VBoxContainer(); _verbs.AddThemeConstantOverride("separation", 1);
        var vp = UiKit.Panel(UiKit.Scroll(_verbs), 0, 0); vp.SizeFlagsVertical = SizeFlags.ExpandFill; right.AddChild(vp);
        _queue = UiKit.Hint("", true); right.AddChild(_queue);
        cols.AddChild(right);
        var bar = new HBoxContainer(); bar.AddThemeConstantOverride("separation", 8); root.AddChild(bar);
        _dressBtn = UiKit.Key(L.T("tr_btn_dress"), "treatment.dress_all", 210, 28); _dressBtn.Pressed += () => { if (_link.Pay != null) PayQuote(); else _link.DressAll(); }; bar.AddChild(_dressBtn);
        _patientBtn = UiKit.Key(L.T("tr_btn_patient"), "treatment.next_patient", 150, 28); _patientBtn.Pressed += NextPatient; bar.AddChild(_patientBtn);
        var back = UiKit.Key(L.T("tr_btn_back"), "treatment.back", 120, 28); back.Pressed += _router.Pop; bar.AddChild(back);
        _keys = UiKit.Hint(L.F("tr_keys", "Up/Down", "Left/Right", InputBootstrap.Hint("treatment.accept"), InputBootstrap.Hint("treatment.dress_all"), InputBootstrap.Hint("treatment.next_patient"), InputBootstrap.Hint("treatment.back")));
        _keys.ClipText = true; _keys.SizeFlagsHorizontal = SizeFlags.ExpandFill; _keys.TooltipText = _keys.Text; bar.AddChild(_keys);
        Build();
    }

    public override void _ExitTree() => ActiveContexts.Exit(InputContext.Treatment);

    public override void _Process(double delta)
    {
        _refresh += delta;
        if (_refresh > 0.25) { _refresh = 0; if (!ReferenceEquals(_link.View(), _shown)) Build(); }
        if (ShotPath == null || !Visible || _shown == null || ++_shotWait != 14) return;
        var img = GetViewport().GetTexture().GetImage();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ShotPath) ?? ".");
        GD.Print($"[treat] screenshot {ShotPath}: {img.SavePng(ShotPath)}");
        GetTree().Quit();
    }

    static string PartName(string id) => FlavourDescribe.PartName(GameText.Instance, id, out _, out _);

    static string Verb(TreatKind k) => L.T("tr_verb_" + k switch
    {
        TreatKind.SetBone => "set_bone", TreatKind.TendonRepair => "tendon_repair", TreatKind.LigamentRepair => "ligament_repair", TreatKind.OrganRepair => "organ_repair",
        TreatKind.ReleasePressure => "release_pressure", _ => k.ToString().ToLowerInvariant(),
    });

    static string UsesText(TreatVerbView v)
    {
        if (v.Uses.Count == 0) return L.T("tr_uses_nothing");
        string t = string.Join(" / ", v.Uses.Select(u => u.Count > 1 ? $"{u.Item.Replace('_', ' ')} x{u.Count}" : u.Item.Replace('_', ' ')));
        return v.HasItems ? t : L.F("tr_uses_missing", t);
    }

    string WoundText(TreatWoundView w)
    {
        string kind = L.T("tr_kind_" + w.Kind.ToString().ToLowerInvariant()), cat = L.T("tr_cat_" + w.Cat.ToString().ToLowerInvariant());
        string state = w.Dressing != Dressing.None ? L.T("tr_dressing_" + w.Dressing.ToString().ToLowerInvariant()) : L.T("tr_state_" + w.State.ToString().ToLowerInvariant());
        string extra = (w.BleedCentiMlPerS > 0 ? L.F("tr_wound_bleed", (w.BleedCentiMlPerS / 100.0).ToString("0.#")) : "") + (w.Infection > 0 ? L.F("tr_wound_infection", w.Infection) : "");
        return L.F("tr_wound_line", kind, cat, state, extra);
    }

    /// <summary>The verbs shown for the selected part: the physician's quote when one is in hand, else the part's own.</summary>
    IReadOnlyList<TreatVerbView> VerbsOfPart(TreatView v)
    {
        if (v.Quote != null) return v.Quote;
        return v.Parts.FirstOrDefault(p => p.Part == _part)?.Verbs ?? [];
    }

    void Build()
    {
        var v = _link.View(); _shown = v;
        foreach (var c in _wounds.GetChildren()) { _wounds.RemoveChild(c); c.QueueFree(); }
        foreach (var c in _verbs.GetChildren()) { _verbs.RemoveChild(c); c.QueueFree(); }
        string name = _link.PatientName();
        _title.Text = L.F("tr_title", name);
        var patients = _link.Patients();
        _patientBtn.Visible = patients.Count > 1;
        _dressBtn.Text = _link.Pay != null ? L.T("tr_btn_pay") : L.T("tr_btn_dress");
        if (v == null) { _status.Text = L.F("tr_no_wounds_at_all", name); _partTitle.Text = ""; _doll.Set(null, -1); _queue.Text = ""; Verbs = 0; return; }
        if (_part < 0 || _part >= v.Parts.Count + 64) _part = -1;
        if (_part < 0 || v.Parts.All(p => p.Part != _part)) _part = v.WorstBleederPart >= 0 ? v.WorstBleederPart : v.Parts.FirstOrDefault(p => p.Wounds.Count > 0)?.Part ?? v.Parts.FirstOrDefault()?.Part ?? -1;
        string rate = v.RateMilli > 0 ? L.F("tr_bleeding", (v.RateMilli / 1000.0).ToString("0.#")) + (v.SecondsToCollapse > 0 ? ", " + L.F("tr_collapse", v.SecondsToCollapse) : "") : L.T("tr_not_bleeding");
        _status.Text = v.Dead ? L.F("tr_dead", name) : L.F("tr_status", v.BloodMl, v.BloodMaxMl, rate, v.PainPct, v.Skill, v.Bandages); _status.TooltipText = _status.Text;
        _doll.Set(v, _part);
        var part = v.Parts.FirstOrDefault(p => p.Part == _part);
        _partTitle.Text = part == null ? "" : PartName(part.Id);
        if (part == null || part.Wounds.Count == 0) _wounds.AddChild(UiKit.Hint(L.T("tr_nothing_here")));
        else
            foreach (var w in part.Wounds.Take(6))
            {
                var l = new Label { Text = WoundText(w), ClipText = true, TooltipText = WoundText(w) };
                if (w.BleedCentiMlPerS > 0 && w.Dressing == Dressing.None) l.AddThemeColorOverride("font_color", PixelTheme.Warn);
                _wounds.AddChild(l);
                if (w.TourniquetNextS >= 0) { var t = UiKit.Hint(L.F("tr_tq_clock", w.TourniquetNextS)); t.AddThemeColorOverride("font_color", w.TourniquetNextS <= 60 ? PixelTheme.Warn : PixelTheme.Gold); _wounds.AddChild(t); }
            }
        if (part != null && part.Wounds.Count > 6) _wounds.AddChild(UiKit.Hint($"+{part.Wounds.Count - 6}"));
        // the verbs
        var verbs = VerbsOfPart(v); Verbs = verbs.Count;
        _verb = Math.Clamp(_verb, 0, Math.Max(0, verbs.Count - 1));
        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 4);
        head.AddChild(Col(v.Quote != null ? L.F("tr_quote_head", PartnerName()) : L.T("tr_col_verb"), 200, PixelTheme.Gold));
        head.AddChild(Col(L.T("tr_col_time"), 54, PixelTheme.Gold)); head.AddChild(Col(L.T("tr_col_uses"), 170, PixelTheme.Gold)); head.AddChild(Col(L.T("tr_col_chance"), 76, PixelTheme.Gold));
        if (v.Quote != null || v.Physician != 0) head.AddChild(Col(L.T("tr_col_fee"), 70, PixelTheme.Gold));
        _verbs.AddChild(head);
        if (verbs.Count == 0) _verbs.AddChild(UiKit.Hint(v.Quote != null ? L.T("tr_quote_empty") : L.T("tr_nothing_here"), true));
        for (int i = 0; i < verbs.Count; i++)
        {
            var vb = verbs[i]; int ii = i;
            var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, VerbRowH), MouseFilter = MouseFilterEnum.Stop }; row.AddThemeConstantOverride("separation", 4);
            var color = i == _verb ? PixelTheme.Gold : !vb.Allowed ? PixelTheme.Dim : PixelTheme.Body;
            string label = Verb(vb.Kind) + (v.Quote != null ? " " + PartName(v.Parts.FirstOrDefault(p => p.Part == vb.Part)?.Id ?? "") : "") + (!vb.Allowed ? $" ({L.T(vb.Refusal == "tr_skill" ? "tr_refuse_skill" : "tr_refuse_two_hands").Replace("{0}", vb.MinSkill.ToString())})" : "");
            if (v.Quote != null) label = (_picked.Contains(i) ? L.T("tr_pick_mark") : L.T("tr_pick_none")) + " " + label;
            row.AddChild(Col(label, 200, color));
            row.AddChild(Col(L.F("tr_time", vb.SimS), 54, color));
            row.AddChild(Col(UsesText(vb), 170, vb.HasItems || vb.Uses.Count == 0 ? color : PixelTheme.Warn));
            row.AddChild(Col(vb.ChancePct < 100 ? L.F("tr_chance", vb.ChancePct) : "", 76, color));
            if (v.Quote != null || v.Physician != 0) row.AddChild(Col(vb.FeeMites > 0 ? Quotes.Money(vb.FeeMites) : "", 70, color));
            row.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb) { _verb = ii; if (mb.DoubleClick) DoVerb(); else Build(); } };
            row.TooltipText = $"{Verb(vb.Kind)}: {L.F("tr_time", vb.SimS)}, {UsesText(vb)}" + (vb.ChancePct < 100 ? ", " + L.F("tr_chance", vb.ChancePct) : "");
            _verbs.AddChild(row);
        }
        if (v.Quote != null && verbs.Count > 0) _verbs.AddChild(UiKit.Hint(L.F("tr_quote_total", Quotes.Money(Total(verbs)), Quotes.Money(_link.Coin?.Invoke() ?? 0)), true));
        _queue.Text = v.Queue.Count == 0 ? L.T("tr_queue_empty") : L.F("tr_queue", string.Join("; ", v.Queue.Select(s => s.DressAll && !s.Planned ? L.T("tr_step_dress_all")
            : L.F("tr_step", Verb(s.Kind), PartName(v.Parts.FirstOrDefault(p => p.Part == s.Part)?.Id ?? "") is { Length: > 0 } pn ? pn : "?", Math.Max(0, (int)((s.FinishSec - v.NowSec + 19) / 20))))));
        if (v.Log.Count > 0 && v.Queue.Count == 0)
        {
            var last = v.Log[^1];
            _queue.Text = last.Done ? L.F("tr_log_done", Verb(last.Kind)) : L.F("tr_log_failed", Verb(last.Kind), last.Why);
        }
    }

    string PartnerName() => _link.PatientName();

    long Total(IReadOnlyList<TreatVerbView> verbs) => verbs.Where((_, i) => _picked.Count == 0 || _picked.Contains(i)).Sum(x => (long)x.FeeMites);

    static Label Col(string text, int w, Color c)
    {
        var l = new Label { Text = text, CustomMinimumSize = new Vector2(w, 0), ClipText = true, MouseFilter = MouseFilterEnum.Pass, SizeFlagsHorizontal = w >= 200 ? SizeFlags.ExpandFill : SizeFlags.Fill, TooltipText = text };
        l.AddThemeColorOverride("font_color", c);
        return l;
    }

    void MovePart(int dir)
    {
        if (_shown == null) return;
        var parts = _shown.Parts; if (parts.Count == 0) return;
        int at = parts.ToList().FindIndex(p => p.Part == _part);
        _part = parts[((at + dir) % parts.Count + parts.Count) % parts.Count].Part; _verb = 0; _picked.Clear();
        Build();
    }

    void DoVerb()
    {
        if (_shown is not { } v) return;
        var verbs = VerbsOfPart(v);
        if (_verb < 0 || _verb >= verbs.Count) return;
        if (v.Quote != null) { if (!_picked.Remove(_verb)) _picked.Add(_verb); Build(); return; }       // a quote's lines are marked, then paid together
        var vb = verbs[_verb];
        if (!vb.Allowed) { _router.ShowToast(L.T(vb.Refusal == "tr_skill" ? "tr_skill" : "tr_two_hands").Replace("{0}", vb.MinSkill.ToString()), 3f); return; }
        _link.Do(vb.Kind, vb.Part);
        _refresh = 0.2;
    }

    void PayQuote()
    {
        if (_shown is not { Quote: { } q } || _link.Pay == null) return;
        var lines = q.Where((_, i) => _picked.Count == 0 || _picked.Contains(i)).ToList();
        if (lines.Count == 0) return;
        _link.Pay(lines); _picked.Clear(); _refresh = 0.2;
    }

    void NextPatient()
    {
        var ps = _link.Patients(); if (ps.Count < 2) return;
        ulong cur = _shown?.Patient ?? ps[0].Id;
        int at = ps.ToList().FindIndex(p => p.Id == cur);
        _link.Select(ps[(at + 1) % ps.Count].Id); _part = -1; _verb = 0; _picked.Clear(); _shown = null; Build();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible || _router.HasModal) return;
        if (Actions.Pressed(e, "treatment.back")) _router.Pop();
        else if (Actions.Pressed(e, "treatment.up")) MovePart(-1);
        else if (Actions.Pressed(e, "treatment.down")) MovePart(1);
        else if (Actions.Pressed(e, "treatment.left")) { _verb = Math.Max(0, _verb - 1); Build(); }
        else if (Actions.Pressed(e, "treatment.right")) { _verb = Math.Min(Math.Max(0, Verbs - 1), _verb + 1); Build(); }
        else if (Actions.Pressed(e, "treatment.accept")) DoVerb();
        else if (Actions.Pressed(e, "treatment.dress_all")) { if (_link.Pay != null) PayQuote(); else _link.DressAll(); _refresh = 0.2; }
        else if (Actions.Pressed(e, "treatment.next_patient")) NextPatient();
        else return;
        GetViewport().SetInputAsHandled();
    }
}

/// <summary>
/// The paper-doll, drawn at 2x (240 x 400): a part is a rectangle of the body, red when it bleeds undressed, amber with a dressed or untreated wound, outlined gold when selected. A click picks a part.
/// Parts the plan has that the figure has no box for (a creature) are laid out in a row at the foot.
/// </summary>
public sealed partial class TreatmentPaperDoll : Control
{
    TreatView? _v; int _sel = -1;
    readonly List<(Rect2 R, int Part)> _hit = [];
    public Action<int>? Picked { get; init; }
    // body boxes at 1x (120 x 200): the figure faces the viewer, so the person's left is drawn on the viewer's right (side 1 = left)
    static readonly Dictionary<string, Rect2> Boxes = new()
    {
        ["head"] = new(46, 0, 28, 26), ["neck"] = new(54, 26, 12, 8), ["upper_body"] = new(34, 34, 52, 50), ["lower_body"] = new(38, 84, 44, 36),
        ["upper_arm_r"] = new(20, 36, 14, 34), ["lower_arm_r"] = new(16, 70, 14, 30), ["hand_r"] = new(14, 100, 16, 14),
        ["upper_arm_l"] = new(86, 36, 14, 34), ["lower_arm_l"] = new(90, 70, 14, 30), ["hand_l"] = new(90, 100, 16, 14),
        ["upper_leg_r"] = new(38, 122, 20, 38), ["lower_leg_r"] = new(38, 160, 20, 30), ["foot_r"] = new(32, 190, 26, 10),
        ["upper_leg_l"] = new(62, 122, 20, 38), ["lower_leg_l"] = new(62, 160, 20, 30), ["foot_l"] = new(62, 190, 26, 10),
    };

    public void Set(TreatView? v, int sel) { _v = v; _sel = sel; QueueRedraw(); }

    public override void _Draw()
    {
        _hit.Clear();
        DrawRect(new Rect2(0, 0, Size.X, Size.Y), new Color(0.1f, 0.08f, 0.09f));
        if (_v == null) return;
        int extra = 0;
        foreach (var p in _v.Parts)
        {
            Rect2 r;
            if (Boxes.TryGetValue(p.Id, out var b)) r = new Rect2(b.Position * 2, b.Size * 2);
            else { r = new Rect2(4 + extra * 36, TreatmentScreen.DollH - 36, 32, 32); extra++; }
            bool bleeds = p.Wounds.Any(w => w.BleedCentiMlPerS > 0 && w.Dressing == Dressing.None);
            bool hurt = p.Wounds.Count > 0;
            var fill = bleeds ? new Color(0.72f, 0.18f, 0.14f) : hurt ? new Color(0.78f, 0.55f, 0.2f) : new Color(0.32f, 0.28f, 0.24f);
            DrawRect(r, fill);
            DrawRect(r, p.Part == _sel ? PixelTheme.Gold : PixelTheme.Rim, false, p.Part == _sel ? 3 : 1);
            if (p.Wounds.Any(w => w.Dressing == Dressing.Tourniquet)) DrawRect(new Rect2(r.Position.X, r.Position.Y + 2, r.Size.X, 4), PixelTheme.Body);
            else if (p.Wounds.Any(w => w.Dressing == Dressing.Bandage)) DrawRect(new Rect2(r.Position.X + 2, r.Position.Y + r.Size.Y / 2 - 2, r.Size.X - 4, 4), PixelTheme.Body);
            if (hurt) DrawString(ThemeDB.FallbackFont, r.Position + new Vector2(3, 13), p.Wounds.Count.ToString(), HorizontalAlignment.Left, -1, 11, Colors.White);
            _hit.Add((r, p.Part));
        }
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb) return;
        foreach (var (r, part) in _hit) if (r.HasPoint(mb.Position)) { Picked?.Invoke(part); return; }
    }
}
