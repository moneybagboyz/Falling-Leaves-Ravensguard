using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Content;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Dialogue;
using Ravensguard.Sim.Rumours;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Game.UI;

/// <summary>The words of the game: <c>tr()</c> through the localisation helper, for the pure talk code.</summary>
public sealed class GameText : ITextSource
{
    public static readonly GameText Instance = new();
    public string T(string key) => L.T(key);
    public bool Has(string key) => L.T(key) != key;
}

/// <summary>
/// The talk window (dialogue spec 4.3, 6): the person's name, trade, mood and how well you know them; their line in a text box; the topic menu (Greet, Ask about work, Ask the news, Ask about
/// someone or somewhere, Trade, Hire, Recruit, Leave) numbered 1-9, or, for a unique NPC, the authored dialogue's choices. E advances a line, Esc leaves. A conversation is a Talk command (the party's
/// familiarity and trust), the news a rumour told at the barrier (the Journal gains a card), Trade opens the trade window, Hire and Recruit are the squad's own commands.
/// </summary>
public partial class TalkScreen : Control
{
    readonly ScreenRouter _router; readonly HeroWorld _hw; readonly TalkLink _link; readonly LineBook _book; readonly EntityId _person; readonly Func<long> _now;
    readonly Func<TalkContext> _world; readonly TalkHistory _hist; readonly Action<EntityId> _openTrade; readonly string? _unique; readonly ulong _seed; readonly Func<string?> _farPlace;
    Label _head = null!, _sub = null!; VBoxContainer _log = null!, _menu = null!; ScrollContainer _scroll = null!; Label _hint = null!;
    Task<SpeakerInfo?>? _spk; Task<HashSet<string>>? _flagsTask; Task<NewsAnswer>? _news;
    SpeakerInfo? _speaker; TalkContext _ctx = new(); DialogueRunner? _run; bool _ready, _closing; int _turn;
    HeroView? _seen; long _waitSeq; string _lastCmd = "";
    readonly List<Button> _buttons = new();
    int _shotWait; bool _scrollDown;
    /// <summary>Dev: when set, the window saves its own picture to this path and quits once the first line is up.</summary>
    public string? ShotPath { get; init; }
    /// <summary>Dev: menu entries (1-based) the window presses by itself, in order, a few frames apart, before the picture.</summary>
    public int[] ShotPress { get; init; } = [];

    /// <summary>Called with the index of each dialogue choice the player picks (the session recorder writes it to the replay trace).</summary>
    public Action<int>? OnChoice { get; init; }

    public TalkScreen(ScreenRouter router, HeroWorld hw, TalkLink link, LineBook book, EntityId person, Func<long> now, Func<TalkContext> world, TalkHistory hist, Action<EntityId> openTrade, ulong seed, string? unique = null, Func<string?>? farPlace = null)
    { _router = router; _hw = hw; _link = link; _book = book; _person = person; _now = now; _world = world; _hist = hist; _openTrade = openTrade; _seed = seed; _unique = unique; _farPlace = farPlace ?? (() => null); }

    public override void _Ready()
    {
        var margin = UiKit.Margin(this, 14);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 6); margin.AddChild(root);
        root.AddChild(UiKit.Title(L.T("TALK_TITLE")));
        _head = new Label { ThemeTypeVariation = "GoldLabel" }; root.AddChild(_head);
        _sub = UiKit.Hint("", true); root.AddChild(_sub);
        _log = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; _log.AddThemeConstantOverride("separation", 6);
        _scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; _scroll.AddChild(_log);
        var p = UiKit.Panel(_scroll, 0, 150); p.SizeFlagsVertical = SizeFlags.ExpandFill; root.AddChild(p);
        _menu = new VBoxContainer(); _menu.AddThemeConstantOverride("separation", 3); root.AddChild(_menu);
        var bottom = new HBoxContainer(); bottom.AddThemeConstantOverride("separation", 8); root.AddChild(bottom);
        var back = UiKit.Key(L.T("TALK_LEAVE"), "menu.back", 160); back.Pressed += _router.Pop; bottom.AddChild(back);
        _hint = UiKit.Hint(L.T("TALK_HINT")); bottom.AddChild(_hint);
        _hw.Issue(new HeroCommand { Kind = HeroCommandKind.Talk, Person = _person, IssuedSec = _now() });
        _spk = _link.Speaker(_person); _flagsTask = _link.Flags();
    }

    // ------------------------------------------------------------------ the context of what is said

    void Begin()
    {
        _speaker = _spk!.Result; _ready = true;
        if (_speaker == null) { _router.Pop(); return; }
        _ctx = _world();
        _ctx.Job = _speaker.Job; _ctx.Mood = _speaker.Mood; _ctx.Female = _speaker.Female; _ctx.Child = _speaker.Child; _ctx.Shopkeeper = _speaker.Shopkeeper; _ctx.Name = _speaker.Name;
        _ctx.Needs.Clear(); foreach (var n in _speaker.Needs) _ctx.Needs.Add(n);
        if (_ctx.Place.Length == 0) _ctx.Place = _speaker.Settlement;
        foreach (var f in _flagsTask!.Result) _ctx.Flags.Add(f);
        SyncRelation();
        ParamResolver.Fill(_ctx, GameText.Instance);
        if (_unique != null && _book.Dialogues.FirstOrDefault(d => d.Speaker == _unique) is { } def) { _run = new DialogueRunner(def, _ctx, GameText.Instance); ShowNode(); }
        else Open();
        Head();
    }

    void SyncRelation()
    {
        if (_hw.View?.Person is { } p && p.Id == _person) { _ctx.Familiarity = p.Familiarity; _ctx.TrustPm = p.TrustPm; }
    }

    void Head()
    {
        if (_speaker == null) return;
        _head.Text = $"{_speaker.Name}  ({UiKit.Pretty(_speaker.Job)}, {_speaker.Age})";
        var band = MoodSystem.Band(_ctx.Mood, _book.Settings);
        _sub.Text = L.F("TALK_SUB", L.T("TALK_MOOD_" + band.ToString().ToUpperInvariant()), L.T("TALK_TIER_" + _ctx.Tier.ToString().ToUpperInvariant()), _ctx.Familiarity);
    }

    string Say(string topic, Func<LineDef, bool>? only = null)
    {
        var l = LineSelector.Pick(_book, topic, _ctx, _seed, _now(), _person.Value, 0, _turn++, _hist, 0, only);
        if (l == null) return "";
        _hist.Record(_person.Value, 0, l.Id);
        return ParamResolver.Say(l.Key, _ctx, GameText.Instance, _ctx.Female);
    }

    void Add(string who, string text, bool you = false)
    {
        if (text.Length == 0) return;
        var l = new Label { Text = you ? $"> {text}" : (who.Length > 0 ? $"{who}: {text}" : text), AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill, ThemeTypeVariation = you ? "HintLabel" : "" };
        _log.AddChild(l); _scrollDown = true;
        while (_log.GetChildCount() > 60) { var c = _log.GetChild(0); _log.RemoveChild(c); c.QueueFree(); }
    }

    // ------------------------------------------------------------------ the topic menu

    void Open()
    {
        _run = null;
        var tier = _ctx.Tier;
        bool refuses = tier is RelationTier.Disliked or RelationTier.Hated || _ctx.HeroBloody;
        if (refuses) Add(_speaker!.Name.Split(' ')[0], Say(tier == RelationTier.Hated ? "hostile" : "refusal"));
        else Add(_speaker!.Name.Split(' ')[0], Say("greeting"));
        Menu(refuses);
    }

    void Menu(bool refuses)
    {
        ClearMenu();
        if (!refuses)
        {
            Choice(L.T("TALK_GREET"), () => Add(Who(), Say("greeting")));
            if (!_ctx.Child) Choice(L.T("TALK_WORK"), () => Add(Who(), Say("work_offer")));
            Choice(L.T("TALK_NEWS"), AskNews);
            Choice(L.T("TALK_PERSON"), () => { _ctx.Params["place"] = _farPlace() ?? _ctx.Place; _ctx.Params["n"] = "2"; Add(Who(), Say("directions")); });
            if (_ctx.Shopkeeper || _ctx.Job.Length > 0 && !_ctx.Child) Choice(L.T("TALK_TRADE"), () => { Add(Who(), Say("trade")); _openTrade(_person); });
            if (!_ctx.Child) Choice(L.T("TALK_HIRE"), HireMenu);
            if (!_ctx.Child) Choice(L.T("TALK_RECRUIT"), RecruitMenu);
        }
        Choice(L.T("TALK_LEAVE"), Leave);
    }

    void Leave()
    {
        _router.ShowToast(Say("farewell")); _closing = true; _router.Pop();
    }

    string Who() => _speaker?.Name.Split(' ')[0] ?? "";

    void AskNews()
    {
        if (_news != null) return;
        _news = _link.AskNews(_person);
        ClearMenu();
    }

    void Told(NewsAnswer a)
    {
        if (a.Card == null) { Add(Who(), Say("rumour", l => l.Id == "rum.nothing")); Menu(false); return; }
        var c = a.Card;
        var names = _settlementNames();
        string said = RumourText.Render(c.Key, c.Place, c.Who, c.About, c.Number, c.Flags, c.PlaceAlt, names, GameText.Instance);
        _ctx.Params["rumour"] = said; _ctx.Params["teller"] = c.Steps.Count > 1 ? c.Steps[1].Person : L.T("R_WHO_TRAVELLER"); _ctx.Params["place"] = c.Place.Length > 0 ? c.Place : _ctx.Place;
        _ctx.RumourKind = c.Kind; _ctx.RumourHop = a.Hop; _ctx.RumourViaTraveller = a.Traveller; _ctx.RumourGarbled = c.Flags != Distortion.None;
        Add(Who(), Say("rumour", l => l.Params.Contains("rumour")));
        _ctx.RumourKind = null; _ctx.RumourHop = -1; _ctx.RumourViaTraveller = false; _ctx.RumourGarbled = false;
        _router.ShowToast(L.T("TALK_JOURNAL_ADDED"));
        Menu(false);
    }

    /// <summary>Settlement names for a misattributed place; the street view's region.</summary>
    public Func<IReadOnlyList<string>> SettlementNames { set => _settlementNamesF = value; }
    Func<IReadOnlyList<string>>? _settlementNamesF;
    IReadOnlyList<string> _settlementNames() => _settlementNamesF?.Invoke() ?? [];

    void HireMenu()
    {
        var p = _hw.View?.Person; if (p == null || p.Id != _person) { Add(Who(), Say("hire", l => l.Id == "hire.no")); return; }
        _ctx.Params["price"] = Quotes.Money(p.FairOffer);
        Add(Who(), Say("hire", l => l.Params.Contains("price") || l.Id.StartsWith("hire.stranger")));
        ClearMenu();
        Choice(L.F("TALK_HIRE_OFFER", Quotes.Money(p.FairOffer)), () => Cmd(new HeroCommand { Kind = HeroCommandKind.Hire, Person = _person, Wage = p.FairOffer, Days = 7 }, "hire"), false);
        Choice(L.T("TALK_BACK"), () => Menu(false), false);
    }

    void RecruitMenu()
    {
        var p = _hw.View?.Person; if (p == null || p.Id != _person) { Add(Who(), Say("recruit", l => l.Id == "recruit.no.know")); return; }
        string id = p.InSquad ? "recruit.yes" : p.RecruitCheck switch
        {
            RecruitAnswer.Accept => "recruit.yes", RecruitAnswer.NotFamiliar => "recruit.no.know", RecruitAnswer.NoTrust => "recruit.no.trust", RecruitAnswer.NoReason => "recruit.no.reason",
            RecruitAnswer.SquadFull => "recruit.full", RecruitAnswer.CannotPay => "recruit.nopay", _ => "recruit.family",
        };
        Add(Who(), Say("recruit", l => l.Id == id));
        if (p.RecruitCheck == RecruitAnswer.Accept && !p.InSquad)
        {
            ClearMenu();
            Choice(L.F("TALK_RECRUIT_OFFER", Quotes.Money(p.FairOffer * 5)), () => Cmd(new HeroCommand { Kind = HeroCommandKind.Recruit, Person = _person, Wage = p.FairOffer }, "recruit"), false);
            Choice(L.T("TALK_BACK"), () => Menu(false), false);
        }
    }

    void Cmd(HeroCommand c, string kind)
    {
        _lastCmd = kind; _waitSeq = _hw.Issue(c with { IssuedSec = _now() }).Seq; ClearMenu();
    }

    void CmdDone(HeroResult r)
    {
        _waitSeq = 0;
        Add("", r.Text); Add(Who(), Say(_lastCmd, l => r.Ok ? (_lastCmd == "hire" ? l.Id == "hire.deal" : l.Id == "recruit.yes") : (_lastCmd == "hire" ? l.Id == "hire.no" : l.Id == "recruit.no.trust")));
        Menu(false);
    }

    // ------------------------------------------------------------------ an authored dialogue

    void ShowNode()
    {
        ClearMenu();
        if (_run == null) return;
        Flush();
        if (_run.Ended) { Add("", ""); _run = null; Add(Who(), Say("greeting")); Menu(false); return; }
        Add(Who(), _run.Text);
        foreach (var c in _run.Choices)
        {
            var cc = c; var b = Choice(cc.Text, () => { Add(L.T("TALK_YOU"), cc.Text, true); OnChoice?.Invoke(cc.Index); _run!.Choose(cc.Index); ShowNode(); }, false);
            if (cc.Locked) { b.Disabled = true; b.TooltipText = cc.NeedText; b.Text += $"   [{cc.NeedText}]"; }
        }
        if (_run.NeedsAdvance) Choice(L.T("TALK_CONTINUE"), () => { _run!.Advance(); ShowNode(); }, false);
    }

    /// <summary>Hands the dialogue's effects to the world at the barrier (flags, trust, rumours); a contract or an item is noted for the player.</summary>
    void Flush()
    {
        if (_run == null) return;
        foreach (var e in _run.Effects.ToList())
        {
            var task = _link.Apply(e.Id, e.Args, _person);
            if (e.Id is "start_contract" or "give_item") _effectTasks.Add((task, e.Id, e.Args.GetValueOrDefault("item") ?? e.Args.GetValueOrDefault("kind") ?? ""));        // the toast says what really happened, once the barrier has done it
        }
        _run.Effects.Clear();
    }

    // ------------------------------------------------------------------ widgets

    void ClearMenu() { foreach (var c in _menu.GetChildren()) { _menu.RemoveChild(c); c.QueueFree(); } _buttons.Clear(); }

    Button Choice(string text, Action act, bool numbered = true)
    {
        var b = new Button { Text = $"{_buttons.Count + 1}. {text}", Alignment = HorizontalAlignment.Left, CustomMinimumSize = new Vector2(0, 28) };
        b.Pressed += () => { if (_news == null && _waitSeq == 0) act(); };
        _menu.AddChild(b); _buttons.Add(b); return b;
    }

    readonly List<(System.Threading.Tasks.Task<bool> Task, string Id, string Arg)> _effectTasks = [];

    public override void _Process(double delta)
    {
        for (int i = _effectTasks.Count - 1; i >= 0; i--)
        {
            var (task, id, arg) = _effectTasks[i]; if (!task.IsCompleted) continue;
            _effectTasks.RemoveAt(i);
            bool done = task.IsCompletedSuccessfully && task.Result;
            _router.ShowToast(L.F(id == "start_contract" ? (done ? "TALK_EFFECT_CONTRACT" : "TALK_EFFECT_CONTRACT_NO") : (done ? "TALK_EFFECT_ITEM" : "TALK_EFFECT_ITEM_NO"), arg.Replace('_', ' ')));
        }
        if (!_ready)
        {
            if (_spk != null && _spk.IsCompleted && _flagsTask != null && _flagsTask.IsCompleted) Begin();
            return;
        }
        if (_news is { IsCompleted: true }) { var a = _news.Result; _news = null; SyncRelation(); Told(a); Head(); }
        var v = _hw.View;
        if (!ReferenceEquals(v, _seen))
        {
            _seen = v; SyncRelation(); Head();
            if (_waitSeq > 0 && v?.Results.FirstOrDefault(r => r.Seq == _waitSeq) is { } res) CmdDone(res);
        }
        if (_scrollDown) { _scrollDown = false; _scroll.ScrollVertical = 100000; }
        if (ShotPath == null) return;
        _shotWait++;
        int step = (_shotWait - 8) / 14;
        if (_shotWait >= 8 && (_shotWait - 8) % 14 == 0 && step < ShotPress.Length && _news == null && _waitSeq == 0 && ShotPress[step] - 1 < _buttons.Count) _buttons[ShotPress[step] - 1].EmitSignal(BaseButton.SignalName.Pressed);
        if (_shotWait != 20 + 14 * ShotPress.Length + (ShotPress.Length > 0 ? 40 : 0)) return;
        var img = GetViewport().GetTexture().GetImage();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ShotPath) ?? ".");
        GD.Print($"[talk] screenshot {ShotPath}: {img.SavePng(ShotPath)}");
        GetTree().Quit();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible || !_ready || _closing || e is not InputEventKey { Pressed: true, Echo: false } k) return;
        int n = k.Keycode is >= Key.Key1 and <= Key.Key9 ? (int)k.Keycode - (int)Key.Key1 : -1;
        if (n >= 0 && n < _buttons.Count && !_buttons[n].Disabled) { _buttons[n].EmitSignal(BaseButton.SignalName.Pressed); GetViewport().SetInputAsHandled(); return; }
        if (k.Keycode == Key.E && _run is { NeedsAdvance: true }) { _run.Advance(); ShowNode(); GetViewport().SetInputAsHandled(); }
    }
}
