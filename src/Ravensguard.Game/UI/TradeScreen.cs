using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Game.UI;

/// <summary>
/// The trade screen (trade-inventory-character spec 4.1, 4.2, 6): your goods on the left, the market's on the right, the basket in the middle with the first-unit price, the average and the total of
/// the line (the live book's per-unit integral, so a large trade moves the price against you), the bid-ask spread and the toll, your coin and the merchants'. A second tab (N) is the price notebook:
/// what you last heard in other markets, with its age (fresh, amber when aged, grey and "?" when old; the market you stand in exact and bold). With a person it is barter: their household's goods
/// for yours or coin, accepted when they do not lose by it. Every number is computed from the barrier's frozen book lines; the deal itself is a command the tick worker applies at the next barrier,
/// with the slippage guard (the quote plus or minus 2%), so a price that moved spends nothing.
/// </summary>
public partial class TradeScreen : Control
{
    readonly ScreenRouter _router;
    readonly HeroWorld _hw;
    readonly int _settlement;
    readonly EntityId _person;
    readonly Func<long> _now;
    readonly int _slipPm;
    VBoxContainer _left = null!, _right = null!, _basket = null!, _notes = null!;
    HBoxContainer _cols = null!;
    Label _status = null!, _title = null!;
    bool _notebook, _barter;
    int _pane = 1;                                   // 0 yours, 1 theirs
    int _sel;
    long _qty = 1000;
    long _waitSeq, _shownSeq;
    double _refresh, _waitSince;
    HeroView? _view;
    int _shotWait; double _dbg;
    public string? ShotPath { get; init; }
    /// <summary>Dev: the good to put in the basket for a screenshot (and the quantity).</summary>
    public string? StartGood { get; init; }
    public long StartQty { get; init; } = 20_000;
    public bool StartNotebook { get; init; }

    public TradeScreen(ScreenRouter router, HeroWorld hw, int settlement, EntityId person, Func<long> now)
    {
        _router = router; _hw = hw; _settlement = settlement; _person = person; _now = now;
        _slipPm = hw.Content.All<Ravensguard.Content.TradeSettingsDef>().FirstOrDefault()?.SlippagePm ?? 20;
    }

    public override void _Ready()
    {
        var margin = UiKit.Margin(this, 14);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 6); margin.AddChild(root);
        _title = UiKit.Title(L.T("TRADE_TITLE")); root.AddChild(_title);
        var tabs = new HBoxContainer(); tabs.AddThemeConstantOverride("separation", 6); root.AddChild(tabs);
        var market = new Button { Text = L.T("TRADE_TAB_MARKET"), ToggleMode = true, ButtonPressed = true, CustomMinimumSize = new Vector2(120, 28) };
        var barter = new Button { Text = L.T("TRADE_TAB_BARTER"), ToggleMode = true, Disabled = _person == EntityId.None, CustomMinimumSize = new Vector2(120, 28) };
        var notes = new Button { Text = L.T("TRADE_TAB_PRICES"), ToggleMode = true, CustomMinimumSize = new Vector2(120, 28) };
        market.Pressed += () => { _notebook = false; _barter = false; market.ButtonPressed = true; barter.ButtonPressed = false; notes.ButtonPressed = false; Build(); };
        barter.Pressed += () => { _notebook = false; _barter = true; market.ButtonPressed = false; barter.ButtonPressed = true; notes.ButtonPressed = false; _sel = 0; Build(); };
        notes.Pressed += () => { _notebook = true; market.ButtonPressed = false; barter.ButtonPressed = false; notes.ButtonPressed = true; Build(); };
        tabs.AddChild(market); tabs.AddChild(barter); tabs.AddChild(notes);
        tabs.AddChild(UiKit.Hint(L.T("TRADE_KEYS")));
        _cols = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; _cols.AddThemeConstantOverride("separation", 10); root.AddChild(_cols);
        _left = Column(_cols, 300); _basket = Column(_cols, 360); _right = Column(_cols, 300);
        _notes = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill, Visible = false }; _notes.AddThemeConstantOverride("separation", 2); root.AddChild(_notes);
        _status = UiKit.Body("", true); root.AddChild(_status);
        var back = UiKit.Key(L.T("UI_BACK"), "menu.back", 200); back.Pressed += _router.Pop; root.AddChild(back);
        // looking at the market reads its prices into the notebook (spec 4.2)
        _hw.Issue(new HeroCommand { Kind = HeroCommandKind.VisitMarket, IssuedSec = _now(), Settlement = _settlement });
        if (_person != EntityId.None) _hw.Issue(new HeroCommand { Kind = HeroCommandKind.ViewPerson, IssuedSec = _now(), Person = _person });
        _notebook = StartNotebook;
        if (StartNotebook) { notes.ButtonPressed = true; market.ButtonPressed = false; }
        Build();
    }

    static VBoxContainer Column(HBoxContainer parent, int w)
    {
        var v = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill }; v.AddThemeConstantOverride("separation", 2);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(w, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(v);
        var p = UiKit.Panel(scroll, w, 0); p.SizeFlagsHorizontal = SizeFlags.ExpandFill; p.SizeFlagsVertical = SizeFlags.ExpandFill;
        parent.AddChild(p);
        return v;
    }

    // ------------------------------------------------------------------ the lines

    sealed record Line(ushort Good, string Name, long Have, long Stock, BookLine? Book, bool Piece, long LotUnits);

    List<Line> Mine()
    {
        var v = _view; if (v == null) return [];
        var lines = v.Market?.Lines;
        return [.. v.Goods.Select(g => new Line(g.Good, g.Name, g.Qty, lines != null && g.Good < lines.Count ? lines[g.Good].StockUnits : 0, lines != null && g.Good < lines.Count ? lines[g.Good] : null, g.Piece, 0))];
    }

    List<Line> Theirs()
    {
        var v = _view; if (v == null) return [];
        if (_barter)
        {
            if (v.Person == null) return [];
            var lines = v.Market?.Lines;
            return [.. v.Person.Goods.Select(g => new Line(g.Good, GoodName(g.Good), v.Goods.FirstOrDefault(x => x.Good == g.Good)?.Qty ?? 0, g.Qty, lines != null && g.Good < lines.Count ? lines[g.Good] : null, Piece(g.Good), g.Qty))];
        }
        if (v.Market == null) return [];
        return [.. v.Market.Lines.Where(l => l.LotUnits > 0).OrderBy(l => GoodName(l.Good), StringComparer.Ordinal)
            .Select(l => new Line(l.Good, GoodName(l.Good), v.Goods.FirstOrDefault(x => x.Good == l.Good)?.Qty ?? 0, l.StockUnits, l, l.Piece, l.LotUnits))];
    }

    string GoodName(ushort g) => _hw.Runtime.State.Economy is { } e && g < e.Goods.Count ? e.Goods.Def(g).Name : $"good {g}";
    bool Piece(ushort g) => _hw.Runtime.State.Economy is { } e && g < e.Goods.Count && e.Goods.IsPiece(g);

    static string Amount(long q, bool piece) => piece ? $"{q}" : $"{q / 1000.0:0.###} kg";
    static string Unit(bool piece) => piece ? L.T("TRADE_PER_PIECE") : L.T("TRADE_PER_KG");

    Line? Current()
    {
        var l = _pane == 0 ? Mine() : Theirs();
        if (l.Count == 0) return null;
        _sel = Math.Clamp(_sel, 0, l.Count - 1);
        return l[_sel];
    }

    TradeSide Side => _pane == 0 ? TradeSide.Sell : TradeSide.Buy;

    long MaxQty(Line l) => _pane == 0 ? l.Have : _barter ? l.Stock : l.LotUnits;

    // ------------------------------------------------------------------ building

    void Build()
    {
        _view = _hw.View;
        var v = _view;
        foreach (var c in _left.GetChildren()) { _left.RemoveChild(c); c.QueueFree(); }
        foreach (var c in _right.GetChildren()) { _right.RemoveChild(c); c.QueueFree(); }
        foreach (var c in _basket.GetChildren()) { _basket.RemoveChild(c); c.QueueFree(); }
        foreach (var c in _notes.GetChildren()) { _notes.RemoveChild(c); c.QueueFree(); }
        _cols.Visible = !_notebook; _notes.Visible = _notebook;
        if (v == null || v.Market == null) { _status.Text = L.T("TRADE_SETTLING"); return; }
        if (_waitSeq == 0 && _status.Text == L.T("TRADE_SETTLING")) _status.Text = "";
        _title.Text = _barter && v.Person != null ? L.F("TRADE_TITLE_PERSON", v.Person.Name) : L.F("TRADE_TITLE_MARKET", v.Market.Name);
        if (_notebook) { BuildNotebook(v); return; }

        _left.AddChild(UiKit.Hint(L.F("TRADE_YOURS", Quotes.Money(v.CoinMites))));
        var mine = Mine();
        for (int i = 0; i < mine.Count; i++)
        {
            var l = mine[i];
            string bid = l.Book is { } b ? Quotes.PerUnit(b.BidMicro, l.Piece) : "-";
            _left.AddChild(Row($"{l.Name}", $"{Amount(l.Have, l.Piece)}", $"{L.T("TRADE_BID")} {bid}", _pane == 0 && i == _sel, () => { _pane = 0; _sel = mine.IndexOf(l); _qty = Math.Min(_qty, l.Have); Build(); }));
        }
        if (mine.Count == 0) _left.AddChild(UiKit.Body(L.T("TRADE_NOTHING")));

        if (_barter && v.Person is { } pv)
        {
            _right.AddChild(UiKit.Hint(L.F("TRADE_THEIRS_PERSON", pv.Name, Quotes.Money(pv.CoinMites))));
            if (pv.Survive) _right.AddChild(UiKit.Hint(L.T("TRADE_THEY_NEED_FOOD"), true));
        }
        else _right.AddChild(UiKit.Hint(L.F("TRADE_THEIRS", Quotes.Money(v.Market.MerchantCash), v.Market.Open ? L.T("TRADE_OPEN") : L.T("TRADE_SHUT"))));
        var theirs = Theirs();
        for (int i = 0; i < theirs.Count; i++)
        {
            var l = theirs[i];
            string ask = l.Book is { } b ? Quotes.PerUnit(_barter ? b.MidMicro : b.AskMicro, l.Piece) : "-";
            _right.AddChild(Row(l.Name, Amount(_barter ? l.Stock : l.LotUnits, l.Piece), $"{(_barter ? L.T("TRADE_WORTH") : L.T("TRADE_ASK"))} {ask}", _pane == 1 && i == _sel, () => { _pane = 1; _sel = theirs.IndexOf(l); Build(); }));
        }
        if (theirs.Count == 0) _right.AddChild(UiKit.Body(L.T("TRADE_NOTHING")));
        BuildBasket(v);
    }

    Control Row(string name, string qty, string price, bool selected, Action pick)
    {
        var b = new Button { Text = "", Flat = !selected, ToggleMode = true, ButtonPressed = selected, CustomMinimumSize = new Vector2(0, 26), Alignment = HorizontalAlignment.Left };
        var h = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; h.SetAnchorsPreset(LayoutPreset.FullRect); h.AddThemeConstantOverride("separation", 6);
        h.AddChild(new Label { Text = " " + name, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, MouseFilter = MouseFilterEnum.Ignore });
        h.AddChild(new Label { Text = qty, CustomMinimumSize = new Vector2(72, 0), HorizontalAlignment = HorizontalAlignment.Right, MouseFilter = MouseFilterEnum.Ignore });
        h.AddChild(new Label { Text = price + " ", CustomMinimumSize = new Vector2(96, 0), HorizontalAlignment = HorizontalAlignment.Right, ThemeTypeVariation = "HintLabel", MouseFilter = MouseFilterEnum.Ignore });
        b.AddChild(h);
        b.Pressed += pick;
        return b;
    }

    void BuildBasket(HeroView v)
    {
        _basket.AddChild(UiKit.Hint(L.T("TRADE_BASKET")));
        var l = Current();
        if (l == null) { _basket.AddChild(UiKit.Body(L.T("TRADE_PICK"))); return; }
        long max = MaxQty(l);
        _qty = Math.Clamp(_qty, Math.Min(1, max), Math.Max(1, max));
        _basket.AddChild(new Label { Text = $"{(Side == TradeSide.Buy ? L.T("TRADE_BUYING") : L.T("TRADE_SELLING"))}: {l.Name}", ThemeTypeVariation = "GoldLabel" });
        var qrow = new HBoxContainer(); qrow.AddThemeConstantOverride("separation", 4);
        long step = l.Piece ? 1 : 1000;
        void Add(string t, Func<long> f) { var b = new Button { Text = t, CustomMinimumSize = new Vector2(44, 28) }; b.Pressed += () => { _qty = Math.Clamp(f(), Math.Min(1, max), Math.Max(1, max)); Build(); }; qrow.AddChild(b); }
        Add("-10", () => _qty - 10 * step); Add("-1", () => _qty - step);
        qrow.AddChild(new Label { Text = Amount(_qty, l.Piece), CustomMinimumSize = new Vector2(84, 0), HorizontalAlignment = HorizontalAlignment.Center });
        Add("+1", () => _qty + step); Add("+10", () => _qty + 10 * step); Add("+50%", () => _qty + Math.Max(step, _qty / 2)); Add(L.T("TRADE_MAX"), () => max);
        _basket.AddChild(qrow);
        var sb = new StringBuilder();
        if (_barter && v.Person != null && l.Book is { } bb)
        {
            long value = (long)((Int128)bb.MidMicro * _qty / PriceCurve.Micro);
            long coin = BarterCoin(value);
            sb.Append(L.F("TRADE_BARTER_VALUE", Quotes.PerUnit(bb.MidMicro, l.Piece), Unit(l.Piece), Quotes.Money(value))).Append('\n');
            sb.Append(Side == TradeSide.Buy ? L.F("TRADE_BARTER_PAY", Quotes.Money(coin)) : L.F("TRADE_BARTER_GET", Quotes.Money(coin))).Append('\n');
            _basket.AddChild(UiKit.Body(sb.ToString(), true));
            var deal = UiKit.Key(L.T("TRADE_DEAL"), "trade.confirm", 220); deal.Pressed += Confirm; deal.Disabled = _waitSeq > 0; _basket.AddChild(deal);
            return;
        }
        if (l.Book is not { } book) { _basket.AddChild(UiKit.Body(L.T("TRADE_NO_PRICE"))); return; }
        var q = book.Quote(Side, _qty);
        if (q.Qty == 0) { _basket.AddChild(UiKit.Body(L.T("TRADE_NO_STOCK"))); return; }
        sb.Append(L.F("TRADE_FIRST", Quotes.PerUnit(q.FirstUnitMicro, l.Piece), Unit(l.Piece))).Append('\n');
        sb.Append(L.F("TRADE_AVERAGE", Quotes.PerUnit(q.AverageMicro, l.Piece), Unit(l.Piece))).Append('\n');
        sb.Append(L.F("TRADE_TOTAL", Quotes.Money(q.TotalMites))).Append('\n');
        if (Side == TradeSide.Sell) sb.Append(L.F("TRADE_TOLL", Quotes.Money(q.TollMites), Quotes.Money(q.NetMites))).Append('\n');
        sb.Append(L.F("TRADE_SPREAD", Quotes.PerUnit(book.BidMicro, l.Piece), Quotes.PerUnit(book.AskMicro, l.Piece), book.HalfSpreadPm / 10)).Append('\n');
        if (q.AverageMicro > q.FirstUnitMicro * 1005 / 1000 && Side == TradeSide.Buy) sb.Append(L.T("TRADE_MOVES_UP")).Append('\n');
        if (q.AverageMicro < q.FirstUnitMicro * 995 / 1000 && Side == TradeSide.Sell) sb.Append(L.T("TRADE_MOVES_DOWN")).Append('\n');
        // what was last heard elsewhere for this good (O4)
        var elsewhere = v.Notes.Where(n => n.Good == l.Good && n.Book != v.Market!.Settlement).OrderBy(n => v.Day - n.HeardDay).Take(3).ToList();
        foreach (var n in elsewhere)
        {
            int age = n.AgeDays(v.Day);
            sb.Append(L.F("TRADE_HEARD", v.BookNames.GetValueOrDefault(n.Book, "?"), LastHeard.Cell(n.AskMicro, l.Piece, age, LastHeard.AgeClass(age)))).Append('\n');
        }
        long after = Side == TradeSide.Buy ? v.CoinMites - q.TotalMites : v.CoinMites + q.NetMites;
        sb.Append(L.F("TRADE_COIN_AFTER", Quotes.Money(v.CoinMites), Quotes.Money(after)));
        var body = UiKit.Body(sb.ToString(), true);
        if (Side == TradeSide.Buy && q.TotalMites > v.CoinMites) body.AddThemeColorOverride("font_color", PixelTheme.Warn);
        _basket.AddChild(body);
        var confirm = UiKit.Key(L.T("TRADE_DEAL"), "trade.confirm", 220); confirm.Pressed += Confirm; confirm.Disabled = _waitSeq > 0 || !v.Market!.Open; _basket.AddChild(confirm);
    }

    /// <summary>Coin offered in a barter: a quarter over the worth when you take their goods (so they do not lose even on food they need), four fifths of it when you give yours.</summary>
    long BarterCoin(long value) => Side == TradeSide.Buy ? (value * 1250 + 999) / 1000 : value * 800 / 1000;

    void BuildNotebook(HeroView v)
    {
        _notes.AddChild(UiKit.Hint(L.T("TRADE_NOTEBOOK_HINT"), true));
        var books = v.Notes.Select(n => n.Book).Distinct().OrderBy(b => b == v.Market!.Settlement ? -1 : b).Take(7).ToList();
        var goods = v.Traded.Concat(v.Goods.Select(g => g.Good)).Distinct().ToList();
        if (goods.Count < 8) goods.AddRange(v.Market!.Lines.Where(l => l.LotUnits > 0).OrderByDescending(l => l.LotUnits).Select(l => l.Good).Where(g => !goods.Contains(g)).Take(8 - goods.Count));
        var grid = new GridContainer { Columns = books.Count + 1 }; grid.AddThemeConstantOverride("h_separation", 14); grid.AddThemeConstantOverride("v_separation", 2);
        grid.AddChild(new Label { Text = L.T("TRADE_GOOD"), ThemeTypeVariation = "HintLabel" });
        foreach (var b in books) grid.AddChild(new Label { Text = v.BookNames.GetValueOrDefault(b, "?") + (b == v.Market!.Settlement ? " *" : ""), ThemeTypeVariation = b == v.Market.Settlement ? "GoldLabel" : "HintLabel" });
        foreach (var g in goods.Take(16))
        {
            grid.AddChild(new Label { Text = GoodName(g), CustomMinimumSize = new Vector2(140, 0) });
            foreach (var b in books)
            {
                var n = v.Notes.FirstOrDefault(x => x.Book == b && x.Good == g);
                if (n.HeardDay == 0 && n.AskMicro == 0) { grid.AddChild(new Label { Text = "-", ThemeTypeVariation = "HintLabel" }); continue; }
                bool local = b == v.Market!.Settlement;
                int age = n.AgeDays(v.Day);
                var cls = LastHeard.AgeClass(age, local: local && age == 0);
                long ask = local && v.Market.Lines.Count > g ? v.Market.Lines[g].AskMicro : n.AskMicro;
                var lab = new Label { Text = LastHeard.Cell(ask, Piece(g), age, cls) };
                lab.AddThemeColorOverride("font_color", cls switch { PriceAge.Exact => PixelTheme.Gold, PriceAge.Fresh => PixelTheme.Body, PriceAge.Aged => new Color("e0a84a"), _ => PixelTheme.Dim });
                grid.AddChild(lab);
            }
        }
        _notes.AddChild(UiKit.Panel(grid));
        _notes.AddChild(UiKit.Hint(L.T("TRADE_NOTEBOOK_LEGEND"), true));
    }

    // ------------------------------------------------------------------ dealing

    void Confirm()
    {
        var v = _view; var l = Current(); if (v == null || l == null || _waitSeq > 0) return;
        HeroCommand c;
        if (_barter && v.Person != null && l.Book is { } bb)
        {
            long value = (long)((Int128)bb.MidMicro * _qty / PriceCurve.Micro);
            c = Side == TradeSide.Buy
                ? new HeroCommand { Kind = HeroCommandKind.Barter, Person = v.Person.Id, Take = [(l.Good, _qty)], CoinGive = Math.Max(1, BarterCoin(value)) }
                : new HeroCommand { Kind = HeroCommandKind.Barter, Person = v.Person.Id, Give = [(l.Good, _qty)], CoinTake = BarterCoin(value) };
        }
        else
        {
            if (l.Book is not { } book) return;
            var q = book.Quote(Side, _qty); if (q.Qty == 0) return;
            c = Side == TradeSide.Buy
                ? new HeroCommand { Kind = HeroCommandKind.Buy, Good = l.Good, Qty = _qty, Limit = Quotes.MaxTotal(q.TotalMites, _slipPm) }
                : new HeroCommand { Kind = HeroCommandKind.Sell, Good = l.Good, Qty = _qty, Limit = Quotes.MinTotal(q.NetMites, _slipPm) };
        }
        var sent = _hw.Issue(c with { IssuedSec = _now(), Settlement = _settlement });
        _waitSeq = sent.Seq; _waitSince = 0;
        _status.Text = L.T("TRADE_SETTLING");
        Build();
    }

    public override void _Process(double delta)
    {
        _refresh += delta; _waitSince += delta;
        if (_refresh > 0.15)
        {
            _refresh = 0;
            var v = _hw.View;
            if (!ReferenceEquals(v, _view))
            {
                if (_waitSeq > 0 && v?.Results.FirstOrDefault(r => r.Seq == _waitSeq) is { } res)
                {
                    _status.Text = res.Text; _status.AddThemeColorOverride("font_color", res.Ok ? PixelTheme.Body : PixelTheme.Warn);
                    _router.ShowToast(res.Text);
                    _waitSeq = 0;
                }
                Build();
            }
            if (_waitSeq > 0 && _waitSince > 0.15) _status.Text = L.T("TRADE_SETTLING");
        }
        if (StartGood != null && _shotWait == 0 && _view?.Market != null)
        {
            var t = Theirs(); int i = t.FindIndex(x => _hw.Runtime.State.Economy!.Goods.StringId(x.Good) == StartGood);
            if (i < 0) i = t.Count > 0 ? t.Select((x, k) => (x, k)).OrderByDescending(p => p.x.LotUnits).First().k : 0;
            _pane = 1; _sel = i; _qty = StartQty; _shotWait = 1; Build();
        }
        if (ShotPath != null && (_dbg += delta) > 5) { _dbg = -1e9; GD.Print($"[trade] waiting: view {(_view == null ? "none" : $"market {_view.Market?.Name ?? "-"}, results {string.Join(" | ", _view.Results.Select(r => r.Text))}")}"); }
        if (ShotPath == null || !Visible || _view?.Market == null) return;
        if (++_shotWait != 12) return;
        var img = GetViewport().GetTexture().GetImage();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ShotPath) ?? ".");
        GD.Print($"[trade] screenshot {ShotPath}: {img.SavePng(ShotPath)}");
        GetTree().Quit();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible) return;
        var l = Current(); long step = l == null || l.Piece ? 1 : 1000;
        bool shift = Input.IsKeyPressed(Key.Shift), ctrl = Input.IsKeyPressed(Key.Ctrl);
        if (Actions.Pressed(e, "trade.notebook")) { _notebook = !_notebook; Build(); }
        else if (Actions.Pressed(e, "trade.pane")) { _pane = 1 - _pane; _sel = 0; Build(); }
        else if (Actions.Pressed(e, "trade.up")) { _sel--; Build(); }
        else if (Actions.Pressed(e, "trade.down")) { _sel++; Build(); }
        else if (Actions.Pressed(e, "trade.less")) { _qty = ctrl ? 1 : _qty - step * (shift ? 10 : 1); Build(); }
        else if (Actions.Pressed(e, "trade.more")) { _qty = ctrl && l != null ? MaxQty(l) : _qty + step * (shift ? 10 : 1); Build(); }
        else if (Actions.Pressed(e, "trade.confirm")) Confirm();
        else if (Actions.Pressed(e, "trade.cancel")) _router.Pop();
        else return;
        GetViewport().SetInputAsHandled();
    }
}
