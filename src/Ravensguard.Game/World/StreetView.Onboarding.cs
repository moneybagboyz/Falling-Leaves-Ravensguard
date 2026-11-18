using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.Onboarding;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.Onboarding;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Game.World;

/// <summary>
/// The street view's side of the first hour (onboarding spec 3 to 6): the hint toast and the events that raise hints, the "?" button and the controls overlay, the first-hour script fed from what the hero does (the job taken
/// and paid, a deal, the ride, the loan handed back, the first night), the "Getting started" list of the Journal and the practice yard. The script's state lives in the world (<see cref="OnboardingBook"/>) and every change
/// to it goes through the tick worker's barrier; this class only watches and draws.
/// </summary>
public partial class StreetView
{
    HintEngine? _hints; HintBar? _hintToast; Button? _helpButton;
    double _obReal, _obPoll, _obNear, _obIdleGame; long _obLastClock = -1;
    readonly bool[] _obStepSeen = new bool[OnboardingBook.StepCount];
    bool _obJobHeld, _obMentorToast, _obCompleteShown;
    EntityId _obJob;
    int _obTraded = -1; (long X, long Y)? _obLastPos; long _obHour = long.MinValue;
    readonly SleepTracker _obSleep = new();
    readonly System.Collections.Concurrent.ConcurrentQueue<string> _obToasts = new();
    int _obJobDone;
    Node2D? _yardDraw; YardSite? _yard; int _yardSettlement = -2; long _yardLastBout = -1;
    bool _pinned;

    OnboardingBook? Ob => Runtime?.State.Onboarding is { IsEmpty: false } b ? b : null;

    void BuildOnboardingHud(Control hud)
    {
        if (_source?.Content is not { } db) return;
        var cfg = OnboardingContent.From(db);
        _hints = new HintEngine(cfg.Hints, Settings.Hints, L.T, id => InputBootstrap.Hint(id).Trim('[', ']'), cfg.Settings);
        _hintToast = new HintBar { OpenCodex = OpenCodexRule };
        hud.AddChild(_hintToast);
        _helpButton = new Button { Text = L.T("STREET_HELP_BUTTON"), TooltipText = L.T("STREET_HELP_TIP"), CustomMinimumSize = new Vector2(30, 30), FocusMode = FocusModeEnum.None, MouseFilter = MouseFilterEnum.Stop, Name = "HelpButton" };
        _helpButton.Position = new Vector2(12, 164);   // under the date panel at the top left: always visible, the mouse's way to the overlay
        _helpButton.Pressed += () => ShowControls();
        hud.AddChild(_helpButton);
    }

    /// <summary>The page of the controls overlay for what the hero is doing now.</summary>
    public string ControlsPage()
    {
        if (ActiveContexts.IsOn(InputContext.Build)) return "build";
        if (ActiveContexts.IsOn(InputContext.CalledShot)) return "called_shot";
        if (_ride is { IsMounted: true } or { IsDriving: true }) return "mounted";
        if (_travel?.Camp != null) return "camp";
        if (_travel?.Journey is { State: Ravensguard.Sim.Travel.JourneyState.Travelling or Ravensguard.Sim.Travel.JourneyState.Paused }) return "travel";
        return "world";
    }

    /// <summary>Opens the controls overlay (<c>?</c> or the button): the page for the current context, with a link to its Codex page.</summary>
    public void ShowControls(string? page = null)
    {
        if (_router == null || _router.Top is ControlsOverlay) return;
        page ??= ControlsPage();
        _hints?.Satisfied("hint.controls");
        _router.Push(ControlsOverlayFor(page));
    }

    Control ControlsOverlayFor(string page)
    {
        string rule = ControlsOverlay.View(page).CodexRule;
        ControlsOverlay.Open(_router!, page, () => OpenCodexRule(rule));
        return _router!.Top is ControlsOverlay o ? o : new Control();
    }

    /// <summary>Opens the Codex at a rule page (a hint's "learn more").</summary>
    void OpenCodexRule(string id)
    {
        if (_router == null || _world.Codex is not { } codex) return;
        var e = codex.Get(id) ?? codex.Get("rule.basics");
        if (e != null && !e.Unlocked) codex.Unlock(e.Id);
        _router.Push(new CodexScreen(_router, codex, _source?.History) { StartCategory = "Rule", StartEntry = e?.Id });
    }

    bool ObInput(InputEvent e)
    {
        if (_hintToast is { Showing: true } t && e is InputEventKey && Actions.Pressed(e, "world.hint_more") && _router?.Top == this) { t.Learn(); return true; }
        return false;
    }

    /// <summary>Raises a hint event. Cheap: the engine looks at the hints of that event only.</summary>
    void Raise(string ev, string kind = "", long value = 0) => _hints?.Raise(ev, kind, value, _obReal);

    // ------------------------------------------------------------------ per frame

    void OnboardingTick(double delta)
    {
        if (_hints == null || _hintToast == null) return;
        _obReal += delta;
        long clock = _world.Clock.Now.Seconds; _hints.GameNow = clock;
        if (_obLastClock >= 0 && _world.Clock.Speed != SimSpeed.Paused && !Settings.Hints.OnboardingComplete) _obIdleGame += Math.Max(0, clock - _obLastClock);
        _obLastClock = clock;
        while (_obIdleGame >= 60) { _obIdleGame -= 60; Settings.Hints.FirstCharacterMinutes++; }
        while (_obToasts.TryDequeue(out var msg)) _router?.ShowToast(msg, 4f);
        if ((_obPoll += delta) >= 0.25) { _obPoll = 0; PollHints(delta); PollScript(clock); AttachYard(); }
        bool blocked = _router == null || _router.Top != this || _router.HasModal;
        if (!_hintToast.Showing && !blocked && _hints.Next(_obReal, false) is { } t) _hintToast.Show(new HintBar.HintToastData(t.Id, t.Text, t.LearnMore));
        else if (_hintToast.Showing && blocked) _hintToast.Dismiss();
        if (_yardSettlement != -2) YardTick();
    }

    void PollHints(double dt)
    {
        long clock = _world.Clock.Now.Seconds;
        Raise("time_in_control", "", (long)_obReal);
        Raise("time_idle_real", "", (long)(_obReal - _lastSaveReal));
        if (_world.Clock.Speed == SimSpeed.X1) Raise("time_idle", "", (clock - _hintGameStart) / 3600);
        if (NearPerson(3000) != null) { Raise("near_object", "person", 3); _obNear += 0.25; if (_obNear >= 3) Raise("near_object", "person_3s", 3); } else _obNear = 0;
        if (_ride?.NearestAnimal(3000) is { } a && !_ride.IsMounted && _ride.Breeds.At(a.Breed).CanRide) Raise("near_object", "rideable_animal", 3);
        if (_clothes?.Hero is { } h && h.Last.ColdStressPm > 0) Raise("need_above", "cold", h.Last.ColdStressPm);
        if (_wx.Wet > 0.2f) Raise("weather_changed", "rain");
        if (_heroLink?.View is { } v) { Raise("inventory_over", "", v.RatioPm); }
        if (_ride is { IsMounted: true } r)
        {
            var view = r.View(clock);
            if (r.Want == Ravensguard.Sim.Animals.Gait.Gallop) Raise("mounted", "gallop");
            if (r.MountedAnimal is { } m && m.WorkFatiguePm >= 800) Raise("mounted", "tired", m.WorkFatiguePm);
            if (view.FeedCenti < 100) Raise("mounted", "fodder_low");
        }
        if (_ride?.DrivenVehicle is { } cart) { if (cart.Stuck) Raise("cart_stuck"); else Raise("cart_ratio", "", _ride.View(clock).RatioPm); }
        if (_travel?.Camp == null && _hintHourOfDay >= 19) Raise("camp_possible");
        Raise("zoom_needed");
    }

    long _hintGameStart = long.MinValue; double _lastSaveReal;
    int _hintHourOfDay => (int)(_world.Clock.Now.Seconds / 3600 % 24);

    // ------------------------------------------------------------------ the script

    void PollScript(long clock)
    {
        var ob = Ob; if (ob == null || Runtime is not { } rt) return;
        var db = _source!.Content!; var cfg = OnboardingContent.From(db).Settings;
        if (_hintGameStart == long.MinValue) _hintGameStart = clock;
        // the job: taken (a live job of the hero's) and paid (it was live and is not any more)
        var mine = _jobSnap?.Live.FirstOrDefault(j => j.Worker == HeroId);
        if (mine != null && !_obJobHeld) { _obJobHeld = true; _obJob = mine.Id; var id = mine.Id; _ = rt.AtBarrier(() => ob.OnJobTaken(id, clock)); Raise("job_taken"); Raise("job_taken", "escrow"); }
        else if (mine == null && _obJobHeld)
        {
            _obJobHeld = false; var id = _obJob;
            _ = rt.AtBarrier(() =>
            {
                var j = rt.State.Jobs?.Get(id);
                if (j is { State: Ravensguard.Sim.Jobs.JobState.Done }) { ob.OnJobDone(id, j.PaidMites, clock, cfg.MinWageMites); System.Threading.Interlocked.Increment(ref _obJobDone); }
                return 0;
            });
        }
        if (System.Threading.Interlocked.Exchange(ref _obJobDone, 0) > 0) Raise("job_done");
        // a deal at the market
        int traded = _heroLink?.View?.Traded.Count ?? 0;
        if (_obTraded < 0) _obTraded = traded; else if (traded > _obTraded) { _obTraded = traded; _ = rt.AtBarrier(() => ob.OnTraded(clock)); }
        // the ride: how far the hero moved, mounted or not, and whether they stand at the errand place
        var (hx, hy) = HeroRegionMm(); var mounted = _ride?.IsMounted == true;
        if (_obLastPos is { } lp)
        {
            long moved = PathPlan.Dist(hx - lp.X, hy - lp.Y);
            bool atErrand = ob.ErrandSettlement >= 0 && PathPlan.Dist(hx - ob.ErrandX, hy - ob.ErrandY) <= 30_000;
            if (moved > 0 || atErrand) _ = rt.AtBarrier(() => ob.OnRide(moved, mounted, atErrand, clock, cfg.MinRideM, cfg.MountedSharePm));
        }
        _obLastPos = (hx, hy);
        // the animal handed back beside its owner, on foot
        if (ob.Loans.Active is { } loan && !mounted && PathPlan.Dist(hx - ob.MentorX, hy - ob.MentorY) <= 60_000 && HeroSettlement() == loan.ReturnPlace)
            _ = rt.AtBarrier(() => { var e = OnboardingSetup.ReturnAnimal(rt.State, db, ob, loan.ReturnPlace, clock); if (e != null) _obToasts.Enqueue(L.F("ONB_RETURNED", MentorName(rt))); return 0; });
        // each game hour: the loan's due day and anger, and the night's rest
        long hour = clock / 3600;
        if (_obHour == long.MinValue) _obHour = hour;
        if (hour > _obHour)
        {
            _obHour = hour; bool sheltered = _travel?.Camp != null || (HeroSettlement() is var s && s >= 0 && PathPlan.Dist(hx - rt.State.Population.Region.Settlements[s].CenterX * 1000L, hy - rt.State.Population.Region.Settlements[s].CenterY * 1000L) < 150_000);
            int rested = _obSleep.Feed(clock, hx, hy, sheltered, cfg.FirstNightHours);
            _ = rt.AtBarrier(() =>
            {
                if (rested > 0) ob.OnSlept(rested, clock, cfg.FirstNightHours);
                foreach (var e in OnboardingSetup.Hour(rt.State, db, ob, clock))
                    _obToasts.Enqueue(e.Kind switch { "overdue" => L.F("ONB_LOAN_OVERDUE", MentorName(rt)), "angry" => L.F("ONB_LOAN_ANGRY", MentorName(rt)), "lost" => L.F("ONB_LOAN_LOST", e.Loan.PriceMites), _ => "" });
                return 0;
            });
            if (ob.Loans.Active is { } l && clock >= l.DueSec - 3600 && clock < l.DueSec) _obToasts.Enqueue(L.T("ONB_LOAN_DUE"));
        }
        // the mentor: a quiet pointer on screen only when the hero is near them and has not spoken yet (the game never marks the map by itself)
        if (!_obMentorToast && !ob.Done(OnboardingStep.MeetMentor) && _frames > 150 && NearPerson(9000) is { } np && ob.MentorPerson >= 0 && np.Id == rt.State.Population.People.IdOf(ob.MentorPerson))
        { _obMentorToast = true; _router?.ShowToast(L.F("ONB_MENTOR_TOAST", H("world.act")), 5f); }
        // steps done: a line each, and the flag in the profile when all are
        for (int i = 0; i < OnboardingBook.StepCount; i++)
            if (ob.DoneSec[i] >= 0 && !_obStepSeen[i]) { _obStepSeen[i] = true; if (_frames > 5) _router?.ShowToast(L.F("ONB_STEP_DONE", StepText(ob, rt, i)), 3f); }
        if (ob.Complete && !_obCompleteShown) { _obCompleteShown = true; if (!Settings.Hints.OnboardingComplete) { Settings.Hints.OnboardingComplete = true; Settings.Save(); _router?.ShowToast(L.T("ONB_COMPLETE"), 6f); } }
    }

    string MentorName(WorldRuntime rt) => Ob is { MentorPerson: >= 0 } b ? rt.State.Population.People.FullName(b.MentorPerson) : "";

    string StepText(OnboardingBook ob, WorldRuntime rt, int i)
    {
        string errand = ob.ErrandSettlement >= 0 && ob.ErrandSettlement < rt.State.Population.Region.Settlements.Count ? rt.State.Population.Region.Settlements[ob.ErrandSettlement].Name : "";
        return i switch
        {
            0 => L.F("ONB_STEP_MEET_MENTOR", MentorName(rt)), 1 => L.T("ONB_STEP_TAKE_JOB"), 2 => L.T("ONB_STEP_FINISH_JOB"), 3 => L.T("ONB_STEP_SPEND_COIN"), 4 => L.T("ONB_STEP_BORROW_HORSE"),
            5 => L.F("ONB_STEP_FIRST_RIDE", errand), 6 => L.T("ONB_STEP_DELIVER"), _ => L.T("ONB_STEP_FIRST_NIGHT"),
        };
    }

    /// <summary>The "Getting started" lines of the Journal: the ambition, then each step ticked or not. Empty once the first hour is complete (it is hidden).</summary>
    IReadOnlyList<(string Text, bool Done)> GettingStarted()
    {
        var ob = Ob; var list = new List<(string, bool)>();
        if (ob == null || ob.Complete || Runtime is not { } rt) return list;
        var amb = _source?.Content?.TryGet<Ravensguard.Content.AmbitionDef>(_hero?.AmbitionId ?? "", out var a) == true ? a : null;
        if (amb != null) list.Add((L.F("ONB_AMBITION", amb.Name), false));
        for (int i = 0; i < OnboardingBook.StepCount; i++) list.Add((StepText(ob, rt, i), ob.DoneSec[i] >= 0));
        return list;
    }

    /// <summary>The optional pin (spec 6): the Journal asks "Pin to map?" and the player chooses; the game never points at anything by itself. Returns the toast text, or null when nobody is to pin.</summary>
    string? PinMentor()
    {
        var ob = Ob; if (ob == null || Runtime is not { } rt) return null;
        _pinned = true;
        var (hx, hy) = HeroRegionMm(); long dx = ob.MentorX - hx, dy = ob.MentorY - hy; long d = PathPlan.Dist(dx, dy) / 1000;
        string dir = Math.Abs(dx) > Math.Abs(dy) * 2 ? (dx > 0 ? "east" : "west") : Math.Abs(dy) > Math.Abs(dx) * 2 ? (dy > 0 ? "south" : "north") : (dy > 0 ? (dx > 0 ? "south-east" : "south-west") : (dx > 0 ? "north-east" : "north-west"));
        return L.F("ONB_PINNED", $"{MentorName(rt)}, about {d} m {dir}");
    }

    // ------------------------------------------------------------------ the practice yard

    void AttachYard()
    {
        if (Runtime is not { } rt) return;
        int s = HeroSettlement(); if (s == _yardSettlement) return;
        _yardSettlement = s; _yard = s < 0 ? null : PracticeYard.SiteOf(rt.State.Population, s, rt.State.Seed, OnboardingContent.From(_source!.Content!).Settings.YardMinPopulation);
        _yardDraw?.QueueFree(); _yardDraw = null;
        if (_yard == null) return;
        var y = _yard;
        _yardDraw = new YardNode(y, Tile) { Name = "PracticeYard", ZIndex = 1 };
        _ysort.AddChild(_yardDraw);
    }

    bool _yardHinted;
    void YardTick()
    {
        if (_yard == null || _router == null || _router.Top != this) return;
        var (hx, hy) = HeroRegionMm();
        bool near = _yard.Contains(hx, hy) || PathPlan.Dist(hx - _yard.X * 1000L, hy - _yard.Y * 1000L) <= 12_000;
        if (near && !_yardHinted) { _yardHinted = true; _router.ShowToast(L.F("YARD_NEAR", H("world.act")), 4f); }
        if (!near) _yardHinted = false;
    }

    bool TryPracticeYard()
    {
        if (_yard == null || _router == null || _heroLink == null || Runtime is not { } rt) return false;
        var (hx, hy) = HeroRegionMm();
        if (PathPlan.Dist(hx - _yard.X * 1000L, hy - _yard.Y * 1000L) > 12_000 && !_yard.Contains(hx, hy)) return false;
        int hour = _hintHourOfDay; bool trainer = hour is >= 8 and < 16 && _yard.TrainerPerson >= 0;
        var cfg = OnboardingContent.From(_source!.Content!).Settings;
        int Melee() => (int)((_heroLink.View?.Skills.FirstOrDefault(k => k.Skill == Ravensguard.Sim.People.Skill.Melee).Micro ?? 0) / Ravensguard.Sim.Crafting.CraftMath.MicroPerLevel);
        int Wait() => PracticeYard.CanSpar(_yardLastBout, _world.Clock.Now.Seconds, cfg.SparCooldownMin) ? 0 : (int)Math.Ceiling((_yardLastBout + cfg.SparCooldownMin * 60L - _world.Clock.Now.Seconds) / 60.0);
        string Gain(SwingResult sw, bool boutDone)
        {
            long now = _world.Clock.Now.Seconds; var outcome = UseOutcome.NoSkill;
            if (boutDone) { _yardLastBout = now; _ = rt.AtBarrier(() => PracticeYard.FinishBout(_heroLink.Context().Craft.State.Book, EconomyState.HeroOwner, now)); return ""; }
            if (sw == SwingResult.Good) outcome = Melee() >= cfg.YardCapLevel ? UseOutcome.AtCap : UseOutcome.Gained;
            if (sw == SwingResult.Good) _ = rt.AtBarrier(() => PracticeYard.HitDummy(_heroLink.Context().Craft.State.Book, EconomyState.HeroOwner, sw, now));
            Raise("skill_gained");
            var reason = PracticeYard.Reason(sw, outcome);
            return reason switch { "too early" => L.T("YARD_TOO_EARLY"), "too late" => L.T("YARD_TOO_LATE"), "outgrown" => L.T("YARD_OUTGROWN"), _ => L.T("YARD_GOOD") };
        }
        _router.Push(new PracticeYardScreen(_router, Gain, Melee, Wait, trainer));
        return true;
    }

    /// <summary>Dev (<c>--ng-shot onboarding</c>): the hero stands at the mentor, and the first screens of the first hour are staged for a picture.</summary>
    public void DebugOnboarding(string what)
    {
        switch (what)
        {
            case "overlay": ShowControls(); break;
            case "overlay_mounted": ShowControls("mounted"); break;
            case "hint": { int n = (_hints?.Raise("near_object", "rideable_animal", 3, _obReal) ?? -1) + (_hints?.Raise("job_board_seen", "", 0, _obReal) ?? 0); GD.Print($"[onboarding] hint queued {n}, level {_hints?.Level}, pending {_hints?.Pending}, depth {_router?.Depth}, modal {_router?.HasModal}, toast {(_hintToast != null)}"); break; }
            case "mentor": if (Ob is { } ob && Runtime is { } rt && ob.MentorPerson >= 0) OpenTalk(rt.State.Population.People.IdOf(ob.MentorPerson)); break;
            case "yard": TryPracticeYard(); break;
        }
    }

    /// <summary>The mentor of the run (name and person), for the dev driver.</summary>
    public string DebugMentorLine() => Ob is { } b && Runtime is { } rt ? $"{b.MentorId} {MentorName(rt)} settlement {b.MentorSettlement}" : "none";
}

/// <summary>The yard on the ground: the fence, three dummy posts and a rack, drawn in world pixels (a stand-in until the blueprint is placed by settlement generation).</summary>
public partial class YardNode : Node2D
{
    readonly YardSite _y; readonly int _tile;
    public YardNode(YardSite y, int tile) { _y = y; _tile = tile; }
    public override void _Draw()
    {
        float t = _tile; float w = YardSite.WidthM * t, d = YardSite.DepthM * t; var o = new Vector2((_y.X - YardSite.WidthM / 2f) * t, (_y.Y - YardSite.DepthM / 2f) * t);
        DrawRect(new Rect2(o, new Vector2(w, d)), new Color(0.42f, 0.33f, 0.22f, 0.55f));
        DrawRect(new Rect2(o, new Vector2(w, d)), new Color(0.35f, 0.24f, 0.14f), false, 3f);
        for (int i = 0; i < 3; i++) { var p = o + new Vector2((3 + i * 3.5f) * t, 4.5f * t); DrawRect(new Rect2(p, new Vector2(0.5f * t, 1.4f * t)), new Color(0.55f, 0.4f, 0.22f)); DrawRect(new Rect2(p - new Vector2(0.3f * t, -0.3f * t), new Vector2(1.1f * t, 0.5f * t)), new Color(0.6f, 0.45f, 0.26f)); }
        DrawRect(new Rect2(o + new Vector2(11.5f * t, 1f * t), new Vector2(1.6f * t, 0.6f * t)), new Color(0.5f, 0.38f, 0.2f));
    }
}
