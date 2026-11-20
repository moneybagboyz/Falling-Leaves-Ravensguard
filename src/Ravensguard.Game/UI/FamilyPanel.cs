using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Family;

namespace Ravensguard.Game.UI;

/// <summary>
/// The Family panel (P6-B3, family-and-lifespan 6; opened from the character sheet): the hero's age and what age has taken ("Strength 14 (-3 with age)"), the old-age hazard, the spouse and the children
/// with their age, course, tutor, what the lessons have taught and how loyal they are, and the courtship under way. A child's course and tutor are set here (each press moves to the next; the sim is told through
/// the hero desk). A view over the barrier's <see cref="FamilyView"/>, refreshed while open; Back closes it. One scrolling column, readable at 960 x 540.
/// </summary>
public partial class FamilyPanel : Control
{
    /// <summary>The courses a press cycles through (a trade course needs a chosen trade, which the sim takes from the desk; the panel offers the four a household can teach).</summary>
    static readonly Course[] Courses = [Course.None, Course.Arms, Course.Letters, Course.Church, Course.Household];

    readonly ScreenRouter _router;
    readonly Func<FamilyView?> _data;
    readonly Action _refresh;
    readonly Action<EntityId, Course, EntityId?> _teach;
    readonly Action<EntityId>? _breakOff;
    /// <summary>P6-E3: arrange a match for a member of the house with the best candidate the houses would consider.</summary>
    public Action<EntityId>? Arrange { get; init; }
    VBoxContainer _body = null!;
    FamilyView? _shown;
    double _acc;
    int _shotWait;
    /// <summary>Course and tutor choices made in this window, shown before the barrier's view catches up.</summary>
    readonly Dictionary<EntityId, (Course Course, EntityId? Tutor)> _pending = [];
    /// <summary>Dev: when set, the panel saves its own picture to this path and quits.</summary>
    public string? ShotPath { get; init; }

    public FamilyPanel(ScreenRouter router, Func<FamilyView?> data, Action refresh, Action<EntityId, Course, EntityId?> teach, Action<EntityId>? breakOff = null)
    { _router = router; _data = data; _refresh = refresh; _teach = teach; _breakOff = breakOff; }

    public override void _Ready()
    {
        var margin = UiKit.Margin(this, 14);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 6); margin.AddChild(root);
        root.AddChild(UiKit.Title(L.T("fam_title")));
        root.AddChild(UiKit.Hint(L.T("fam_intro"), true));
        _body = new VBoxContainer(); _body.AddThemeConstantOverride("separation", 4);
        var panel = UiKit.Panel(UiKit.Scroll(_body), 0, 0); panel.SizeFlagsVertical = SizeFlags.ExpandFill; root.AddChild(panel);          // the panel takes the height the Back bar leaves
        var bottom = new HBoxContainer(); bottom.AddThemeConstantOverride("separation", 8);
        var back = UiKit.Key(L.T("UI_BACK"), "menu.back", 200); back.Pressed += _router.Pop; bottom.AddChild(back);
        root.AddChild(bottom);
        _refresh();
        Build(_data());
    }

    public override void _Process(double delta)
    {
        _acc += delta;
        if (_acc >= 1.0) { _acc = 0; _refresh(); var v = _data(); if (!ReferenceEquals(v, _shown)) { _pending.Clear(); Build(v); } }
        if (ShotPath == null || !Visible || ++_shotWait != 12) return;
        var img = GetViewport().GetTexture().GetImage();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ShotPath) ?? ".");
        GD.Print($"[family] screenshot {ShotPath}: {img.SavePng(ShotPath)}");
        GetTree().Quit();
    }

    static string AttrName(string id) => L.T("ATTR_" + id.ToUpperInvariant());

    void Build(FamilyView? v)
    {
        _shown = v;
        foreach (var c in _body.GetChildren()) c.QueueFree();
        if (v == null) { _body.AddChild(UiKit.Body(L.T("fam_loading"))); return; }
        // the hero's age and what it has taken
        _body.AddChild(new Label { Text = L.F("fam_age", v.HeroName, v.HeroAge), ThemeTypeVariation = "GoldLabel" });
        string hazard = v.OldAgePct > 0 ? L.F("fam_hazard", v.OldAgePct, v.NextBirthdayInDays) : L.F("fam_hazard_none", v.NextBirthdayInDays);
        _body.AddChild(UiKit.Hint(hazard, true));
        var grid = new GridContainer { Columns = 5 }; grid.AddThemeConstantOverride("h_separation", 14); grid.AddThemeConstantOverride("v_separation", 1);
        foreach (var a in v.Ageing)
            grid.AddChild(new Label { Text = a.Lost > 0 ? L.F("fam_attr_lost", AttrName(a.Attr), a.Value, -a.Lost) : L.F("fam_attr_plain", AttrName(a.Attr), a.Value), CustomMinimumSize = new Vector2(150, 0), ThemeTypeVariation = a.Lost > 0 ? "" : "HintLabel" });
        _body.AddChild(grid);

        // the spouse
        _body.AddChild(UiKit.Hint(L.T("fam_spouse")));
        if (v.Spouse is { } sp) _body.AddChild(UiKit.Body(L.F(sp.Alive ? "fam_spouse_line" : "fam_member_dead", sp.Name, sp.Age, sp.Place, Loyalty(sp)), true));
        else _body.AddChild(UiKit.Body(L.T("fam_no_spouse"), true));
        if (v.CourtedName.Length > 0)
        {
            if (v.CourtState == CourtState.Betrothed)
                _body.AddChild(UiKit.Body(v.WeddingDay >= 0 ? L.F("fam_betrothed", v.CourtedName, v.WeddingDay) : L.F("fam_betrothed_waits", v.CourtedName), true));
            else _body.AddChild(UiKit.Body(L.F("fam_courting", v.CourtedName, v.Meetings, v.Gifts), true));
            if (!v.HasRoof) _body.AddChild(UiKit.Hint(L.T("fam_no_roof"), true));
            if (_breakOff is { } br) { var b = new Button { Text = L.T("fam_break"), CustomMinimumSize = new Vector2(180, 30) }; b.Pressed += () => br(v.Courted); _body.AddChild(b); }
        }

        // P6-E3: the matches under way and the members the head could match (family 4.2)
        if (v.OpenMatches.Count > 0 || v.Arrangeable.Count > 0) _body.AddChild(UiKit.Hint(L.T("fam_matches")));
        foreach (var m in v.OpenMatches)
            _body.AddChild(UiKit.Body(m.State switch
            {
                MatchState.AwaitingFee => L.F("fam_match_fee", m.Groom, m.Bride),
                MatchState.Wed => L.F("fam_match_wed", m.Groom, m.Bride),
                _ => L.F(m.Alliance ? "fam_match_alliance" : "fam_match_betrothed", m.Groom, m.Bride, m.WeddingDay, m.DowryMites),
            }, true));
        foreach (var a in v.Arrangeable)
        {
            if (a.Refuses) { _body.AddChild(UiKit.Hint(L.F("fam_arrange_refuses", a.Name), true)); continue; }
            if (a.Candidate.Length == 0) { _body.AddChild(UiKit.Hint(L.F("fam_arrange_none", a.Name, a.Age), true)); continue; }
            if (!a.Accepts) { _body.AddChild(UiKit.Hint(L.F("fam_arrange_low", a.Name, a.Age, a.Candidate, a.Utility), true)); continue; }
            _body.AddChild(UiKit.Body(L.F(a.NeedsConsent ? "fam_arrange_line_consent" : a.Alliance ? "fam_arrange_line_alliance" : "fam_arrange_line", a.Name, a.Age, a.Candidate, a.Utility, a.DowryMites), true));
            if (Arrange is { } ar) { var ab = new Button { Text = L.F("fam_btn_arrange", a.Name), CustomMinimumSize = new Vector2(260, 30), ClipText = true }; var who = a.Member; ab.Pressed += () => ar(who); _body.AddChild(ab); }
        }

        // the children
        _body.AddChild(UiKit.Hint(L.T("fam_children")));
        var kids = v.Children.ToList();
        if (kids.Count == 0) _body.AddChild(UiKit.Body(L.T("fam_no_children"), true));
        foreach (var k in kids)
        {
            _body.AddChild(new Label { Text = k.Alive ? L.F("fam_child_line", k.Name, k.Age, k.Held ? L.T("fam_held") : k.Place, Loyalty(k)) : L.F("fam_member_dead", k.Name, k.Age, "", ""), ThemeTypeVariation = "GoldLabel" });
            if (!k.Alive || k.Age >= 16) continue;
            if (k.Age < 7) { _body.AddChild(UiKit.Hint(L.T("fam_too_young"), true)); continue; }
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8);
            var (course, tutor) = _pending.TryGetValue(k.Id, out var p) ? p : (k.Course, k.HasTutor ? k.Tutor : (EntityId?)null);
            var cb = new Button { Text = L.F("fam_course", L.T("fam_course_" + course.ToString().ToLowerInvariant())), CustomMinimumSize = new Vector2(190, 30) };
            var tb = new Button { Text = L.F("fam_tutor", TutorName(v, tutor)), CustomMinimumSize = new Vector2(260, 30), ClipText = true };
            var kid = k;
            cb.Pressed += () => { var next = Courses[(Array.IndexOf(Courses, course) + 1) % Courses.Length]; Set(v, kid.Id, next, tutor ?? v.Tutors[0].Id); };
            tb.Pressed += () => { var i = tutor is { } t ? v.Tutors.ToList().FindIndex(x => x.Id == t) : -1; var nt = v.Tutors[(i + 1) % v.Tutors.Count].Id; Set(v, kid.Id, course == Course.None ? Course.Household : course, nt); };
            row.AddChild(cb); row.AddChild(tb);
            _body.AddChild(row);
            _body.AddChild(UiKit.Hint(k.Skills.Length > 0 ? L.F("fam_learned", k.Skills) : (course == Course.None ? L.T("fam_untaught") : L.T("fam_lessons_begin")), true));
        }
    }

    static string TutorName(FamilyView v, EntityId? t) => t is { } id ? v.Tutors.FirstOrDefault(x => x.Id == id)?.Name ?? L.T("fam_tutor_none") : L.T("fam_tutor_none");

    string Loyalty(FamilyMemberView m) => m.Grievances > 0 ? L.F("fam_loyalty_why", m.LoyaltyPm, m.Grievances) : L.F("fam_loyalty", m.LoyaltyPm);

    void Set(FamilyView v, EntityId child, Course course, EntityId tutor)
    {
        _pending[child] = (course, course == Course.None ? null : tutor);
        _teach(child, course, course == Course.None ? null : tutor);
        Build(v);
    }
}
