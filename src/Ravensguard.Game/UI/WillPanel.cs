using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Succession;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Game.UI;

/// <summary>
/// The will panel (P5-H, inheritance 6): made in person before a priest or the reeve (Q > Make a will). The left column lists the people the hero knows (kin, companions, followers, acquaintances);
/// each row's button cycles what that person gets (nothing, everything, a quarter or half of the coin, one of the hero's deeds or businesses). The right column is the estate as it stands today: what
/// the heir would receive, what goes to other kin and to the lord, the debts. "Make the will" sends one recorded command (the 2-crown fee to the witness); a newer will replaces the old. A view over
/// the barrier's <see cref="WillView"/>; the router's Back closes it, and the street keys stay off while it is up (a pushed screen).
/// </summary>
public partial class WillPanel : Control
{
    readonly ScreenRouter _router;
    readonly Func<WillView?> _data;
    readonly Action<List<Bequest>> _make;
    readonly string _witness;
    VBoxContainer _people = null!, _estate = null!;
    Label _status = null!;
    Button _makeBtn = null!;
    WillView? _shown;
    double _refresh;
    int _shotWait;
    readonly Dictionary<EntityId, int> _choice = [];
    /// <summary>Dev: when set, the panel saves its own picture to this path and quits.</summary>
    public string? ShotPath { get; init; }
    /// <summary>Dev (shots): choices preset by person (index into the person's options).</summary>
    public Dictionary<EntityId, int>? Preset { get; init; }

    public WillPanel(ScreenRouter router, Func<WillView?> data, Action<List<Bequest>> make, string witness) { _router = router; _data = data; _make = make; _witness = witness; }

    public override void _Ready()
    {
        if (Preset != null) foreach (var (k, v) in Preset) _choice[k] = v;
        var margin = UiKit.Margin(this, 12);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 4); margin.AddChild(root);
        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 8);
        var title = UiKit.Title(L.T("in_wl_title")); title.SizeFlagsHorizontal = SizeFlags.ExpandFill; head.AddChild(title);
        head.AddChild(UiKit.Hint(L.F("in_wl_witness", _witness)));
        root.AddChild(head);
        _status = UiKit.Hint("", true); root.AddChild(_status);
        var cols = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; cols.AddThemeConstantOverride("separation", 8); root.AddChild(cols);
        _people = new VBoxContainer(); _people.AddThemeConstantOverride("separation", 2);
        var lp = UiKit.Panel(UiKit.Scroll(_people), 0, 0); lp.SizeFlagsHorizontal = SizeFlags.ExpandFill; lp.SizeFlagsStretchRatio = 1.15f; cols.AddChild(lp);
        _estate = new VBoxContainer(); _estate.AddThemeConstantOverride("separation", 2);
        var rp = UiKit.Panel(UiKit.Scroll(_estate), 0, 0); rp.SizeFlagsHorizontal = SizeFlags.ExpandFill; cols.AddChild(rp);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End }; buttons.AddThemeConstantOverride("separation", 8);
        _makeBtn = new Button { Text = L.F("in_wl_btn_make", Quotes.Money(WillBook.FeeMites)), CustomMinimumSize = new Vector2(240, 32) };
        _makeBtn.Pressed += Confirm; buttons.AddChild(_makeBtn);
        var back = UiKit.Key(L.T("UI_BACK"), "menu.back", 160, 32); back.Pressed += _router.Pop; buttons.AddChild(back);
        root.AddChild(buttons);
        Build();
    }

    public override void _Process(double delta)
    {
        _refresh += delta;
        if (_refresh > 0.5) { _refresh = 0; if (!ReferenceEquals(_data(), _shown)) Build(); }
        if (ShotPath == null || !Visible || _shown == null || ++_shotWait != 12) return;
        var img = GetViewport().GetTexture().GetImage();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ShotPath) ?? ".");
        GD.Print($"[will] screenshot {ShotPath}: {img.SavePng(ShotPath)}");
        GetTree().Quit();
    }

    static string M(long mites) => Quotes.Money(mites);

    /// <summary>What a person can be given: nothing, everything, a quarter or half of the coin, each deed and business.</summary>
    List<(string Text, Bequest? Line)> Options(WillView v, EntityId person)
    {
        var o = new List<(string, Bequest?)> { (L.T("in_wl_nothing"), null), (L.T("in_wl_all"), new Bequest(person, BequestWhat.All)),
            (L.F("in_wl_coin", 25), new Bequest(person, BequestWhat.Coin, default, 250)), (L.F("in_wl_coin", 50), new Bequest(person, BequestWhat.Coin, default, 500)) };
        foreach (var t in v.Things) o.Add((t.Name, new Bequest(person, t.What, t.Id)));
        return o;
    }

    List<Bequest> Lines(WillView v)
    {
        var l = new List<Bequest>();
        foreach (var p in v.People) if (_choice.TryGetValue(p.Person, out int k) && Options(v, p.Person) is var o && k > 0 && k < o.Count && o[k].Line is { } b) l.Add(b);
        return l;
    }

    Label Row(string text, Color? c = null)
    {
        var l = new Label { Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, TooltipText = text, MouseFilter = MouseFilterEnum.Stop };
        if (c is { } cc) l.AddThemeColorOverride("font_color", cc);
        return l;
    }

    void Build()
    {
        var v = _data(); _shown = v;
        foreach (var c in _people.GetChildren()) { _people.RemoveChild(c); c.QueueFree(); }
        foreach (var c in _estate.GetChildren()) { _estate.RemoveChild(c); c.QueueFree(); }
        if (v == null) { _status.Text = L.T("lg_waiting"); _makeBtn.Disabled = true; return; }
        // the will already made, if any
        if (v.Current is { } cur && _choice.Count == 0)
            foreach (var b in cur.Bequests)
            {
                var o = Options(v, b.Person);
                int k = o.FindIndex(x => x.Line is { } q && q.What == b.What && q.Thing == b.Thing && q.CoinPm == b.CoinPm);
                if (k > 0) _choice[b.Person] = k;
            }
        var lines = Lines(v);
        _status.Text = L.F("in_wl_status", M(v.FeeMites), M(v.CoinMites), v.Current != null ? L.F("in_wl_has_will", v.Current.Bequests.Count) : L.T("in_wl_no_will"));
        _makeBtn.Disabled = lines.Count == 0 || v.CoinMites < v.FeeMites;
        _people.AddChild(Row(L.T("in_wl_people"), PixelTheme.Gold));
        if (v.People.Count == 0) _people.AddChild(UiKit.Hint(L.T("in_wl_nobody"), true));
        foreach (var p in v.People.Take(24))
        {
            var line = new HBoxContainer(); line.AddThemeConstantOverride("separation", 4);
            line.AddChild(Row(L.F("in_wl_person", p.Name, L.T("in_wl_how_" + p.How), p.Age, p.Place)));
            var o = Options(v, p.Person);
            int k = _choice.GetValueOrDefault(p.Person);
            var b = new Button { Text = o[Math.Clamp(k, 0, o.Count - 1)].Text, CustomMinimumSize = new Vector2(170, 24), ClipText = true, TooltipText = L.T("in_wl_cycle") };
            var pp = p.Person;
            b.Pressed += () => { _choice[pp] = (_choice.GetValueOrDefault(pp) + 1) % o.Count; Build(); };
            line.AddChild(b);
            _people.AddChild(line);
        }
        // the estate as it stands (inheritance 6): what each would get today
        var e = v.Estate;
        _estate.AddChild(Row(L.T("in_wl_estate"), PixelTheme.Gold));
        _estate.AddChild(Row(L.F("in_su_coin", M(e.CoinMites), M(e.FuneralMites), M(e.DebtsMites))));
        if (e.Heir.Length > 0) _estate.AddChild(Row(L.F("in_su_heir_gets", e.Heir, e.Law == "partible" ? L.T("in_su_law_partible") : L.T("in_su_law_primo"))));
        foreach (var l in e.YouInherit.Take(8)) _estate.AddChild(Row("  " + L.F("in_su_line", l.Name, M(l.ValueMites))));
        if (e.ToOthers.Any()) { _estate.AddChild(Row(L.T("in_su_to_kin"))); foreach (var l in e.ToOthers.Take(6)) _estate.AddChild(Row("  " + L.F("in_su_line_to", l.Name, M(l.ValueMites), l.To))); }
        if (e.ToTheLord.Any()) { _estate.AddChild(Row(L.T("in_su_to_lord"), new Color(0.95f, 0.6f, 0.4f))); foreach (var l in e.ToTheLord.Take(6)) _estate.AddChild(Row("  " + L.F("in_su_line_to", l.Name, M(l.ValueMites), l.To))); }
        if (e.Lines.Count == 0) _estate.AddChild(UiKit.Hint(L.T("in_su_nothing"), true));
        _estate.AddChild(UiKit.Hint(L.T("in_wl_note"), true));
    }

    void Confirm()
    {
        if (_shown is not { } v) return;
        var lines = Lines(v);
        if (lines.Count == 0) return;
        string body = string.Join("\n", lines.Select(b =>
        {
            var who = v.People.FirstOrDefault(p => p.Person == b.Person)?.Name ?? "?";
            string what = b.What switch
            {
                BequestWhat.All => L.T("in_wl_all"), BequestWhat.Coin => L.F("in_wl_coin", b.CoinPm / 10),
                _ => v.Things.FirstOrDefault(t => t.Id == b.Thing)?.Name ?? "?",
            };
            return L.F("in_wl_confirm_line", who, what);
        }));
        _router.ShowModal(L.T("in_wl_confirm_title"), body + "\n\n" + L.F("in_wl_confirm_fee", Quotes.Money(v.FeeMites), _witness),
            (L.T("in_wl_btn_sign"), () => { _make(lines); _router.Pop(); }), (L.T("UI_CANCEL"), null));
    }
}
