using System.Linq;
using Godot;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Combat.Abstract;

namespace Ravensguard.Game.World.Combat;

/// <summary>
/// The report viewer (abstract-battles-v1 7): a battle nobody watched, told as a report. The sides and their strengths, where and when, a timeline strip of the steps with each
/// side's fallen, the notables' fates with their cause chain, the quality factor. No replay and no skip: a report is a report. Everything is read from the sim's
/// <see cref="BattleReport"/>; every word is a <c>tr()</c> key. Readable at 960 x 540 (one column, a scroll body).
/// </summary>
public partial class BattleReportView : PanelContainer
{
    readonly BattleReport _r;
    readonly string _settlement, _nameA, _nameB;

    public BattleReportView(BattleReport r, string settlement, string nameA, string nameB) { _r = r; _settlement = settlement; _nameA = nameA; _nameB = nameB; }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(820, 470);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 5);
        AddChild(UiKit.Scroll(box));
        box.AddChild(UiKit.Title(L.F("ab_report_title", _r.Place)));
        box.AddChild(new Label { Text = L.F("ab_report_word", _settlement), ThemeTypeVariation = "GoldLabel" });
        string winner = _r.Winner == 0 ? _nameA : _r.Winner == 1 ? _nameB : "";
        box.AddChild(UiKit.Body(Cap(winner.Length > 0 ? L.F("ab_report_held", winner, _r.DurationS) : L.F("ab_report_neither", _r.DurationS))));
        for (int s = 0; s < 2; s++)
        {
            var t = _r.Sides[s];
            box.AddChild(UiKit.Body(Cap(L.F("ab_report_side", s == 0 ? _nameA : _nameB, t.Start, t.Dead, t.Down, t.Captured, t.Fled + t.Surrendered, t.Standing))));
        }
        box.AddChild(UiKit.Hint(L.F("ab_report_quality", _r.QualityX100 / 100, (_r.QualityX100 % 100).ToString("00"))));
        box.AddChild(UiKit.Hint(L.T("ab_report_timeline")));
        box.AddChild(Strip());
        if (_r.Notables.Count > 0)
        {
            box.AddChild(new Label { Text = L.T("ab_report_notables"), ThemeTypeVariation = "GoldLabel" });
            foreach (var n in _r.Notables.Where(n => n.Leader || n.Fate != BattleFate.Unhurt).Take(8))
            {
                string fate = L.T("ab_fate_" + Ravensguard.Sim.Combat.Flavour.FlavourBook.Snake(n.Fate.ToString()));
                string how = n.Wound >= OutcomeClass.Light ? L.F("ab_report_by", L.T("ab_wound_" + AbstractClasses.Name(n.Wound)), L.T("ab_weapon_" + AbstractClasses.Name(n.By))) : "";
                box.AddChild(UiKit.Body(L.F(n.Leader ? "ab_report_leader" : "ab_report_notable", n.Name, fate, how)));
            }
        }
        box.AddChild(UiKit.Hint(L.T("ab_report_no_replay"), wrap: true));
    }

    static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>The timeline strip: one column per few steps, the first side's fallen up from the line, the second's down from it.</summary>
    Control Strip()
    {
        var c = new Control { CustomMinimumSize = new Vector2(800, 70) };
        c.Draw += () =>
        {
            var lines = _r.Timeline;
            if (lines.Count == 0) return;
            int per = System.Math.Max(1, (lines.Count + 59) / 60);
            int cols = (lines.Count + per - 1) / per;
            float w = 800f / System.Math.Max(1, cols), mid = 35f;
            c.DrawLine(new Vector2(0, mid), new Vector2(800, mid), new Color(0.6f, 0.55f, 0.45f), 1);
            for (int i = 0; i < cols; i++)
            {
                int a = 0, b = 0, fa = 0, fb = 0;
                for (int j = i * per; j < System.Math.Min(lines.Count, (i + 1) * per); j++) { a += lines[j].Down0; b += lines[j].Down1; fa += lines[j].Fled0; fb += lines[j].Fled1; }
                if (a > 0) c.DrawRect(new Rect2(i * w + 1, mid - System.Math.Min(32, a * 6), System.Math.Max(2, w - 2), System.Math.Min(32, a * 6)), new Color(0.75f, 0.25f, 0.2f));
                if (b > 0) c.DrawRect(new Rect2(i * w + 1, mid + 1, System.Math.Max(2, w - 2), System.Math.Min(32, b * 6)), new Color(0.3f, 0.45f, 0.8f));
                if (fa > 0) c.DrawRect(new Rect2(i * w + 1, mid - 3, System.Math.Max(2, w - 2), 2), new Color(0.9f, 0.8f, 0.3f));
                if (fb > 0) c.DrawRect(new Rect2(i * w + 1, mid + 1, System.Math.Max(2, w - 2), 2), new Color(0.9f, 0.8f, 0.3f));
            }
        };
        return c;
    }
}
