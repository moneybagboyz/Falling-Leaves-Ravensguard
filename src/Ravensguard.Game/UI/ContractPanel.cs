using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI.NewGame;

namespace Ravensguard.Game.UI;

/// <summary>P5-D: what the contract board shows (filled by the street view at a barrier from the construction world): plain rows.</summary>
public sealed class ContractBoardData
{
    public sealed record Row(ulong Id, string Title, string Place, long WagesMites, long Hours10, int ProgressPm, long PaidMites, long TotalMites, long DueDays, string State, long NoticeHours);
    public sealed record Bid(ulong Builder, string Name, long MaterialsMites, long LabourMites, long HaulMites, int Crew, int DaysX10)
    {
        public long Total => MaterialsMites + LabourMites + HaulMites;
    }
    public string PlaceName = "";
    public List<Row> Board = [];
    public Row? Mine;
    public ulong SiteId;
    public string SiteName = "";
    public long SiteHours10;
    public List<Bid> Bids = [];
    public List<Row> Commissioned = [];
    public long CoinMites;
}

/// <summary>
/// P5-D: the contract board (Q > Building work, build-mode-and-construction 4.7): the building work posted on the notice board (the lord's civic work: take it as a job and build it by hand),
/// the contract the hero holds (progress, pay so far, the due day; give it up), builders' bids for the hero's own site (materials at the ask, labour, the haul, crew and days; hire one: a
/// third now into escrow) and the work he has commissioned. Every take, hire and give-up asks first. Fits 960 x 540; the lists scroll, Back stays on screen.
/// </summary>
public partial class ContractPanel : Control
{
    readonly ScreenRouter _router;
    readonly Func<ContractBoardData> _data;
    readonly Action<ulong> _take, _drop;
    readonly Action<ulong, ulong, int> _hire;
    VBoxContainer _body = null!;
    double _refresh;
    public string? ShotPath { get; init; }

    public ContractPanel(ScreenRouter router, Func<ContractBoardData> data, Action<ulong> take, Action<ulong> drop, Action<ulong, ulong, int> hire)
    { _router = router; _data = data; _take = take; _drop = drop; _hire = hire; }

    public override void _Ready()
    {
        var margin = UiKit.Margin(this, 14);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 6); margin.AddChild(root);
        root.AddChild(UiKit.Title(L.T("CONTRACT_TITLE")));
        _body = new VBoxContainer(); _body.AddThemeConstantOverride("separation", 4);
        var p = UiKit.Panel(UiKit.Scroll(_body), 0, 0); p.SizeFlagsVertical = SizeFlags.ExpandFill; root.AddChild(p);
        var back = UiKit.Key(L.T("UI_BACK"), "menu.back", 200); back.Pressed += _router.Pop; root.AddChild(back);
        Build();
    }

    int _shotWait;

    public override void _Process(double delta)
    {
        _refresh += delta; if (_refresh > 1.0) { _refresh = 0; Build(); }
        if (ShotPath == null || !Visible || ++_shotWait != 20) return;
        var img = GetViewport().GetTexture().GetImage();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ShotPath) ?? ".");
        GD.Print($"[contracts] screenshot {ShotPath}: {img.SavePng(ShotPath)}");
        GetTree().Quit();
    }

    static string Money(long mites) => UiKit.Money(mites);
    static string Hours(long h10) => (h10 / 10.0).ToString("0.#");

    Label Row(string text) => new() { Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, TooltipText = text };

    void Build()
    {
        foreach (var c in _body.GetChildren()) { _body.RemoveChild(c); c.QueueFree(); }
        var d = _data();
        // the hero's own contract
        _body.AddChild(UiKit.Hint(L.T("CONTRACT_MINE")));
        if (d.Mine is { } m)
        {
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8);
            row.AddChild(Row(L.F("CONTRACT_MINE_ROW", m.Title, m.Place, m.ProgressPm / 10, Money(m.PaidMites), Money(m.WagesMites), m.DueDays)));
            var drop = new Button { Text = L.T("CONTRACT_DROP"), CustomMinimumSize = new Vector2(110, 26) };
            drop.Pressed += () => _router.ShowModal(L.T("CONTRACT_DROP_TITLE"), L.F("CONTRACT_DROP_BODY", m.Title, Money(m.PaidMites)), (L.T("CONTRACT_DROP"), () => { _drop(m.Id); Build(); }), (L.T("UI_CANCEL"), null));
            row.AddChild(drop); _body.AddChild(row);
            _body.AddChild(UiKit.Hint(L.T("CONTRACT_MINE_HOW"), true));
        }
        else _body.AddChild(UiKit.Body(L.T("CONTRACT_MINE_NONE")));
        // the notice board
        _body.AddChild(UiKit.Hint(L.F("CONTRACT_BOARD", d.PlaceName)));
        if (d.Board.Count == 0) _body.AddChild(UiKit.Body(L.T("CONTRACT_BOARD_EMPTY")));
        foreach (var b in d.Board.Take(6))
        {
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8);
            row.AddChild(Row(L.F("CONTRACT_BOARD_ROW", b.Title, b.Place, Money(b.WagesMites), Hours(b.Hours10), b.NoticeHours)));
            var take = new Button { Text = L.T("CONTRACT_TAKE"), Disabled = d.Mine != null, CustomMinimumSize = new Vector2(110, 26) };
            take.Pressed += () => _router.ShowModal(L.T("CONTRACT_TAKE_TITLE"), L.F("CONTRACT_TAKE_BODY", b.Title, Money(b.WagesMites), Hours(b.Hours10)),
                (L.T("CONTRACT_TAKE"), () => { _take(b.Id); _router.ShowToast(L.T("CONTRACT_TAKEN")); Build(); }), (L.T("UI_CANCEL"), null));
            row.AddChild(take); _body.AddChild(row);
        }
        // a builder for the hero's own site
        if (d.SiteId != 0)
        {
            _body.AddChild(UiKit.Hint(L.F("CONTRACT_BIDS", d.SiteName, Hours(d.SiteHours10))));
            if (d.Bids.Count == 0) _body.AddChild(UiKit.Body(L.T("CONTRACT_NO_BUILDERS")));
            foreach (var b in d.Bids.Take(4))
            {
                var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8);
                var label = Row(L.F("CONTRACT_BID_ROW", b.Name, Money(b.Total), Money(b.MaterialsMites), Money(b.LabourMites), Money(b.HaulMites), b.Crew, Hours(b.DaysX10)));
                label.TooltipText = L.F("CONTRACT_BID_TIP", b.Name, Money(b.Total), Money(b.MaterialsMites), Money(b.LabourMites), Money(b.HaulMites), b.Crew, Hours(b.DaysX10));
                row.AddChild(label);
                var tip = UiKit.Hint("   " + label.TooltipText); 
                var hire = new Button { Text = L.T("CONTRACT_HIRE"), Disabled = d.CoinMites < b.Total / 3, CustomMinimumSize = new Vector2(110, 26) };
                hire.Pressed += () => _router.ShowModal(L.T("CONTRACT_HIRE_TITLE"), L.F("CONTRACT_HIRE_BODY", b.Name, Money(b.Total), Money(b.Total / 3), Hours(b.DaysX10)),
                    (L.T("CONTRACT_HIRE"), () => { _hire(d.SiteId, b.Builder, b.Crew); _router.ShowToast(L.T("CONTRACT_HIRED")); Build(); }), (L.T("UI_CANCEL"), null));
                row.AddChild(hire); _body.AddChild(row);
                _body.AddChild(tip);
            }
        }
        if (d.Commissioned.Count > 0)
        {
            _body.AddChild(UiKit.Hint(L.T("CONTRACT_COMMISSIONED")));
            foreach (var c in d.Commissioned.Take(4))
                _body.AddChild(Row(L.F("CONTRACT_COMMISSION_ROW", c.Title, c.State, c.ProgressPm / 10, Money(c.PaidMites), Money(c.TotalMites))));
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible) return;
        if (Actions.Pressed(e, "world.context_menu")) _router.Pop();
        else return;
        GetViewport().SetInputAsHandled();
    }
}
