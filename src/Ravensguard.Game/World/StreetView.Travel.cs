using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.Travel;
using Ravensguard.Sim.Weather;
using Ravensguard.Sim.WorldGen;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Game.World;

/// <summary>
/// The street view's travel (docs/specs/phase3/travel-encounters-camp.md): auto-travel along a planned route at up to 8x with the interrupt bus pausing and slowing the clock, road encounters with their people
/// on the road, the night camp (fire, cook, watch, talk, sleep), foraging, and the dev commands that hunt and butcher. The sim side is <see cref="TravelState"/>; this file is the keys, the HUD and the sprites.
/// </summary>
public partial class StreetView
{
    TravelState? _travel; TravelFeed? _tfeed; TravelHud? _thud;
    SimSpeed _wantedSpeed = SimSpeed.X8;
    bool _campOpen, _travelHinted;
    (string Name, long EndSec, Action Apply)? _campJob;
    long _lastCampHour = long.MinValue;
    EncounterEvent? _enc; bool _encOpen;
    IReadOnlyList<EncounterOption> _encOptions = [];
    bool _encScreenPending;
    Ravensguard.Sim.Caravans.RecordCast? _recordCast;
    readonly Dictionary<ulong, RigActor> _castActors = new();
    Sprite2D? _campSprite;
    string _campMsg = "";
    long _campMsgUntil;

    /// <summary>The travel state of the run: restored from a save or new on the economy's stores (null without content).</summary>
    void AttachTravel()
    {
        var content = _source?.Content; if (content == null) return;
        long now = _world.Clock.Now.Seconds;
        if (Runtime is { } rt && rt.State.Economy is { } econ)
        {
            if (rt.State.Travel == null) rt.State.Travel = new TravelState(content, rt.State.ContinentSettings.Seed, econ.Goods, econ.Mass) { Hosted = true, OpenStore = id => econ.OpenStore(StoreKind.Open, id), GameStartSec = now };
            _travel = rt.State.Travel; _travel.Hosted = true;
            _travel.OpenStore ??= id => econ.OpenStore(StoreKind.Open, id);
            _travel.PackStore = () => econ.OpenStore(StoreKind.House, EconomyState.HeroOwner, 0, 0, false);
        }
        else
        {
            var goods = new GoodCatalog(content);
            _travel = new TravelState(content, 1, goods, new MassLedger(goods.Count)) { GameStartSec = now };
            var pack = _travel.StoreOf(EntityId.Make(IdKind.Person, 0)); _travel.PackStore = () => pack;
        }
        _travel.HeroId = EconomyState.HeroOwner;
        _travel.LatitudeTenths = 480;
        if (Runtime is { } crt)
        {
            var home = crt.State.HomeRegion;
            _recordCast = new Ravensguard.Sim.Caravans.RecordCast(() => crt.Caravans, () => crt.State.Population, home.OriginXM * 1000L, home.OriginYM * 1000L);
            _travel.CastSource = _recordCast;
        }
        if (_source!.Region is { } rg)
            _tfeed = new TravelFeed(rg, _wfeed, _travel.Cfg, () => Runtime?.State.Production?.Resources, _travel.LatitudeTenths);
        _thud = new TravelHud();
        GetNodeOrNull<Control>("Hud")?.AddChild(_thud);
        Debug.DevContext.Travel = DevTravelConsole;
        if (_opt.Travel != null) DevTravelShot(_opt.Travel);
    }

    static readonly string[] ArchOutfitSpear = ["guard", "soldier", "bandit", "toll_keeper"];
    static string OutfitFor(string archetype) => ArchOutfitSpear.Contains(archetype) ? "spearman" : archetype is "traveller" or "peddler" or "pilgrim" or "tinker" or "caravan_crew" ? "traveller" : "peasant";

    // ------------------------------------------------------------------------------------------ starting a trip

    /// <summary>The route and the numbers a map screen shows for a destination (region metres): null when there is no way.</summary>
    public TravelPreview? PreviewTravel(string name, float xM, float yM)
    {
        if (_travel == null || _source?.Region is not { } rg) return null;
        var (hx, hy) = HeroWorld();
        var path = RouteFinder.Plan(new RegionRouteGrid(rg, FordClosed), (long)(hx * 1000), (long)(hy * 1000), (long)(xM * 1000), (long)(yM * 1000));
        if (path == null) return null;
        int cmps = HeroSpeedCmps();
        var eta = Eta.Of(path.LengthMm / 1000, cmps);
        double kgDay = 1.1;                                                   // a person-day of staples: 1.1 kg grain-equivalent
        var pack = _travel.PackStore?.Invoke();
        double foodKg = pack == null || _travel.Goods == null ? 0 : pack.Rows().Where(r => _travel.Goods.FoodMeasuresPpm(r.Good) > 0).Sum(r => (double)_travel.Goods.MassG(r.Good, r.Qty) * _travel.Goods.FoodMeasuresPpm(r.Good) / 909_091.0) / 1000.0;
        double ownKm = path.LengthMm / 1_000_000.0;
        return new TravelPreview(name, path, path.LengthMm / 1000, eta.GameHoursTenths, eta.RealSeconds1x, eta.RealSeconds8x, foodKg / kgDay, ownKm);
    }

    bool FordClosed(int tx, int ty)
    {
        var s = _tfeed?.Sample(_world.Clock.Now.Seconds, (tx * RegionData.TileM + 32) * 1000L, (ty * RegionData.TileM + 32) * 1000L);
        return s is { } v && (v.Terrain & TerrainFlags.RiverCrossing) != 0 && v.Weather is WeatherState.HeavyRain or WeatherState.Thunderstorm;
    }

    /// <summary>The hero's own speed in cm per real second: jog 3 m/s on foot, the mount's gait or the cart's speed when riding or driving.</summary>
    int HeroSpeedCmps()
    {
        if (_ride is { IsMounted: true } or { IsDriving: true } && _ride.View(_world.Clock.Now.Seconds).SpeedCmps is > 0 and var v) return v;
        return 300;
    }

    /// <summary>Travel here: plans the route, starts the journey at 8x (the speed gate and the bus slow it), closes the map.</summary>
    public bool StartTravel(TravelPreview p)
    {
        if (_travel == null || _tfeed == null) return false;
        long now = _world.Clock.Now.Seconds;
        _travel.Begin(p.Name, p.Path, now);
        _rec.Action(Ravensguard.Sim.Diagnostics.HeroAction.TravelBegin, p.DistM, now);
        _enc = null; _encOpen = false; _wantedSpeed = SimSpeed.X8;
        _rec.SetSpeed(SimSpeed.X8);
        _router?.ShowToast(L.F("TRAVEL_START", p.Name, (p.DistM / 1000.0).ToString("0.0"), (p.GameHoursTenths / 10.0).ToString("0")), 4f);
        return true;
    }

    // ------------------------------------------------------------------------------------------ per tick

    /// <summary>Once per sim tick: the journey (movement, interrupts, encounters), the camp's fire and hours, the speed the bus allows.</summary>
    void TravelTick()
    {
        var t = _travel; if (t == null || _tfeed == null) return;
        long now = _world.Clock.Now.Seconds;
        _tfeed.Hero.BaseMmPerGs = VehicleSpeedMm();
        _tfeed.Hero.HoursSinceMeal = t.HoursSinceMeal(now);
        _tfeed.Hero.HostileM = _enc is { } e && IsHostileKind(e.Kind) && _encOpen ? (int)(DistToHeroM(e.SpawnXMm, e.SpawnYMm)) : int.MaxValue;
        _tfeed.Hero.HostileName = _enc?.Id == "wolves" ? "Wolves" : _enc?.Id == "bear" ? "A bear" : "Bandits";
        if (t.Journey is { State: JourneyState.Travelling } j)
        {
            t.Advance(_tfeed, now);
            var (x, y, hx, hy) = j.Path.At(j.ProgressMm);
            _rec.TravelPlace(x, y, Math.Abs(hx) >= Math.Abs(hy) ? (hx < 0 ? Facing.West : Facing.East) : (hy < 0 ? Facing.North : Facing.South));
            if (t.PendingEncounter is { } ev)
            {
                _enc = ev; _encOpen = true; t.PendingEncounter = null; _router?.ShowToast(ev.Bark.Length > 0 ? ev.Bark : ev.Id, 3f);
                _encOptions = t.Table.Defs.FirstOrDefault(d => d.Id == ev.Id) is { } def ? EncounterOutcomes.Options(def, _heroLink?.View?.CoinMites ?? 0) : [];
            }
        }
        if (_encScreenPending && _router?.Depth <= 1) { _encScreenPending = false; EncounterOutcomes.Settle(t, new EncounterOutcome(EncounterChoice.GoOn, "went_on", 0, EntityId.None, EntityId.None, 0, ""), now); }
        if (t.Snares.Count > 0 && Runtime?.State.Production?.Resources is { } snareLayer) t.StepSnares(snareLayer, now, 20);
        if (t.Camp is { } c) { t.StepFire(c, now); CampHour(c, now); }
        if (_campJob is { } job && now >= job.EndSec) { _campJob = null; job.Apply(); }
        var allowed = t.Journey is { State: JourneyState.Travelling or JourneyState.Paused } ? t.Bus.AllowedSpeed(_wantedSpeed, now) : _campJob != null ? SimSpeed.X8 : _wantedSpeed;
        if (t.Journey is { State: JourneyState.Arrived } && _wantedSpeed == SimSpeed.X8 && !_travelHinted) { _wantedSpeed = SimSpeed.X1; }
        if (t.Journey is { State: JourneyState.Travelling or JourneyState.Paused } || _campJob != null)
            if (_world.Clock.Speed != allowed && !(allowed == SimSpeed.Paused && _world.Clock.Speed == SimSpeed.Paused)) { if (allowed != SimSpeed.Paused) _speedBeforePause = allowed; _rec.SetSpeed(allowed); }
        if (t.Journey is { State: JourneyState.Arrived } && _world.Clock.Speed > SimSpeed.X1) _rec.SetSpeed(SimSpeed.X1);
    }

    int VehicleSpeedMm() => Ravensguard.Sim.Carts.VehicleMath.MmPerGs(HeroSpeedCmps());
    static bool IsHostileKind(string k) => k is "Ambush" or "Animal" or "Demand";
    double DistToHeroM(long xMm, long yMm) { var (hx, hy) = _world.WorldMm; double dx = (xMm - hx) / 1000.0, dy = (yMm - hy) / 1000.0; return Math.Sqrt(dx * dx + dy * dy); }

    /// <summary>The camp's hour: a night event (stream 0x346) while camped at night, shown as a toast.</summary>
    void CampHour(Camp c, long now)
    {
        long hour = now / 3600; if (hour == _lastCampHour) return;
        _lastCampHour = hour;
        var s = _tfeed!.Sample(now, c.XMm, c.YMm);
        if (!s.Night) return;
        var ev = _travel!.RollNightEvent(c, now, s.DangerPm, c.Sentries.Count > 0, s.Season);
        if (ev.Effect != "none" && ev.Name.Length > 0) { _router?.ShowToast(ev.Name, 3f); _campMsg = ev.Name; _campMsgUntil = now + 3600; }
    }

    // ------------------------------------------------------------------------------------------ input

    /// <summary>The travel keys. True when the event was used.</summary>
    bool TravelInput(InputEvent e)
    {
        var t = _travel; if (t == null) return false;
        var j = t.Journey;
        bool going = j is { State: JourneyState.Travelling or JourneyState.Paused };
        if (e is InputEventKey { Pressed: true } && going)
        {
            if (Actions.Pressed(e, "world.back")) { t.Cancel(_world.Clock.Now.Seconds); _rec.Action(Ravensguard.Sim.Diagnostics.HeroAction.TravelCancel); _enc = null; _encOpen = false; _rec.SetSpeed(SimSpeed.X1); _wantedSpeed = SimSpeed.X1; _router?.ShowToast(L.T("TRAVEL_STOPPED")); return true; }
            if (t.Bus.Paused && _enc != null && _encOpen && _encOptions.Count > 0 && !_encScreenPending && EncounterKey(e)) return true;
            if (t.Bus.Paused && (Actions.Pressed(e, "world.act") || Actions.Pressed(e, "world.pause") || e is InputEventKey { Keycode: Key.Enter or Key.KpEnter }))
            {
                t.Resume(); _rec.Action(Ravensguard.Sim.Diagnostics.HeroAction.TravelResume); _encOpen = false; var allowed = t.Bus.AllowedSpeed(_wantedSpeed, _world.Clock.Now.Seconds); _rec.SetSpeed(allowed == SimSpeed.Paused ? SimSpeed.X1 : allowed); return true;
            }
            if (Actions.Pressed(e, "world.speed_1")) { _wantedSpeed = SimSpeed.X1; _rec.SetSpeed(SimSpeed.X1); return true; }
            if (Actions.Pressed(e, "world.speed_2")) { _wantedSpeed = SimSpeed.X2; _rec.SetSpeed(SimSpeed.X2); return true; }
            if (Actions.Pressed(e, "world.speed_4")) { _wantedSpeed = SimSpeed.X4; _rec.SetSpeed(SimSpeed.X4); return true; }
            if (Actions.Pressed(e, "world.speed_8"))
            {
                var tf = _tfeed!.Hero; var (ok, why) = t.Gate8x(tf);
                if (!ok) { _router?.ShowToast(why); return true; }
                _wantedSpeed = SimSpeed.X8; _rec.SetSpeed(SimSpeed.X8); return true;
            }
        }
        if (e is InputEventKey { Pressed: true })
        {
            if (_campOpen && t.Camp is { } c && CampKey(e, t, c)) return true;
            if (Actions.Pressed(e, "world.camp")) { ToggleCamp(); return true; }
            if (Actions.Pressed(e, "world.forage")) { Forage(); return true; }
            if (Actions.Pressed(e, "world.hunt")) { HuntGame(); return true; }
            if (Actions.Pressed(e, "world.snare")) { SnareVerb(); return true; }
        }
        return false;
    }

    /// <summary>A movement key during auto-travel: the journey stops where the hero stands and the player has the hero again.</summary>
    void TakeControl()
    {
        var t = _travel!; t.Cancel(_world.Clock.Now.Seconds); _rec.Action(Ravensguard.Sim.Diagnostics.HeroAction.TravelCancel); _enc = null; _encOpen = false; _wantedSpeed = SimSpeed.X1; _rec.SetSpeed(SimSpeed.X1);
        _router?.ShowToast(L.T("TRAVEL_STOPPED"));
    }

    // ------------------------------------------------------------------------------------------ encounter choices

    /// <summary>1 to 5 choose an option of the encounter panel; Enter takes the way on (or running, when the road is not free to go on).</summary>
    bool EncounterKey(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true } k) return false;
        int i = k.Keycode switch { Key.Key1 => 0, Key.Key2 => 1, Key.Key3 => 2, Key.Key4 => 3, Key.Key5 => 4, _ => -1 };
        if (i < 0 && (Actions.Pressed(e, "world.act") || k.Keycode is Key.Enter or Key.KpEnter))
        {
            i = _encOptions.ToList().FindIndex(o => o.Choice == EncounterChoice.GoOn);
            if (i < 0) i = _encOptions.ToList().FindIndex(o => o.Choice is EncounterChoice.Flee or EncounterChoice.Detour or EncounterChoice.Wait);
        }
        if (i < 0 || i >= _encOptions.Count) return false;
        ChooseEncounter(_encOptions[i]);
        return true;
    }

    /// <summary>The player's choice on the road: pay goes through the hero desk (a ledger transfer at the barrier), trade and talk open their screens with the real person, flee, detour and wait cost time, a fight is a stub until Phase 4.</summary>
    void ChooseEncounter(EncounterOption opt)
    {
        var t = _travel!; var ev = _enc; if (ev == null) return;
        var def = t.Table.Defs.FirstOrDefault(d => d.Id == ev.Id); long now = _world.Clock.Now.Seconds;
        if (def == null || opt.Choice == EncounterChoice.GoOn) { FinishEncounter(new EncounterOutcome(EncounterChoice.GoOn, "went_on", 0, EntityId.None, EntityId.None, 0, ""), now, false); return; }
        long coin = _heroLink?.View?.CoinMites ?? 0;
        var o = EncounterOutcomes.Resolve(t, def, ev, opt.Choice, coin, _tfeed!.Hero.PartyStrengthPm, id => !t.Travellers.Any(x => x.Id == id));
        if (o.PayMites > 0 && _heroLink != null)
            _heroLink.Issue(new HeroCommand { Kind = HeroCommandKind.EncounterPay, IssuedSec = now, CoinGive = o.PayMites, Person = o.Payee, Tag = ev.Id, Origin = CommandOrigin.Human });
        bool screen = false;
        if (o.Result == "trade")
        {
            if (o.Counterpart != EntityId.None) { OpenTrade(-1, o.Counterpart); screen = true; }
            else _router?.ShowToast(L.T("ENC_NOTHING_TO_TRADE"), 3f);
        }
        else if (o.Result == "talk")
        {
            if (o.Counterpart != EntityId.None) { OpenTalk(o.Counterpart); screen = true; }
            else _router?.ShowToast(ev.Bark.Length > 0 ? "\"" + ev.Bark + "\"" : L.T("ENC_NOTHING_TO_SAY"), 3f);
        }
        else if (o.Text.Length > 0) _router?.ShowToast(o.PayMites > 0 ? L.F(o.Text, Quotes.Money(o.PayMites)) : L.T(o.Text), 4f);
        FinishEncounter(o, now, screen);
    }

    void FinishEncounter(EncounterOutcome o, long now, bool screen)
    {
        var t = _travel!; _encOpen = false; _encOptions = [];
        if (screen) { _encScreenPending = true; return; }       // the clock stays held while the trade or talk window is open
        EncounterOutcomes.Settle(t, o, now);
        var allowed = t.Bus.AllowedSpeed(_wantedSpeed, now); _rec.SetSpeed(allowed == SimSpeed.Paused ? SimSpeed.X1 : allowed);
    }

    // ------------------------------------------------------------------------------------------ camp

    void ToggleCamp()
    {
        var t = _travel!; long now = _world.Clock.Now.Seconds;
        if (t.Camp != null) { _campOpen = !_campOpen; return; }
        var (hx, hy) = _world.WorldMm;
        var s = _tfeed!.Sample(now, hx, hy);
        var (ok, why) = t.CanMakeCamp(s, 5);
        if (!ok) { _router?.ShowToast(why); return; }
        if (t.Journey is { State: JourneyState.Travelling or JourneyState.Paused }) t.Cancel(now);
        var members = new List<EntityId> { EconomyState.HeroOwner };
        var camp = t.MakeCamp(hx, hy, now, members);
        _rec.Action(Ravensguard.Sim.Diagnostics.HeroAction.CampMake, hx, hy);
        EnsureCompanions(t);
        // the pack's food goes into the camp stock (it comes back when camp is broken)
        var pack = t.PackStore?.Invoke(); var stock = t.StoreOf(camp.Id);
        if (pack != null && t.Goods != null)
            foreach (var (g, q) in pack.Rows()) if (t.Goods.FoodMeasuresPpm(g) > 0 || t.Goods.StringId(g) == "firewood") Store.MoveUpTo(pack, stock, g, q);
        _campOpen = true; _rec.SetSpeed(SimSpeed.X1); _wantedSpeed = SimSpeed.X1;
        _router?.ShowToast(L.T("TRAVEL_CAMP_MADE"), 3f);
    }

    static void EnsureCompanions(TravelState t)
    {
        if (t.Companions.All(m => m.Person != EconomyState.HeroOwner)) t.Companions.Add(new CampMember { Person = EconomyState.HeroOwner, Name = "You", Loyalty = 1000, Perception = 45 });
    }

    bool CampKey(InputEvent e, TravelState t, Camp c)
    {
        long now = _world.Clock.Now.Seconds; var people = t.Companions.Count;
        if (_campJob != null) return Actions.Pressed(e, "world.act") ? true : false;
        var K = (e as InputEventKey)!.Keycode;
        var comps = t.Companions.Where(m => m.Person != EconomyState.HeroOwner).ToList();
        switch (K)
        {
            case Key.Key1: FireAction(t, c, now); return true;
            case Key.Key2:
            {
                var eaters = t.Companions.ToList();
                var r = t.CampMeal(c, eaters, now, cookSkill: 20);
                _campMsg = r.Fed > 0 ? L.F("TRAVEL_CAMP_ATE", r.Fed, (r.GramsEaten / 1000.0).ToString("0.0")) : L.T("TRAVEL_CAMP_NOFOOD"); _campMsgUntil = now + 1200;
                return true;
            }
            case Key.Key3:
                if (comps.Count == 0) { _campMsg = L.T("TRAVEL_CAMP_NOWATCH"); _campMsgUntil = now + 1200; return true; }
                t.SetSentries(c, c.Sentries.Count == 0 ? comps.Take(t.Cfg.SentryMax).Select(m => m.Person) : []);
                return true;
            case Key.Key4:
                StartCampJob(L.T("TRAVEL_CAMP_TALKING"), t.Cfg.TalkMinutes * 60, () =>
                {
                    long ts = _world.Clock.Now.Seconds;
                    var speaker = t.FireSpeaker(t.Companions, EconomyState.HeroOwner, ts);            // chosen before the talk marks the night, so the one with a story left sits down first
                    var r = t.TalkAroundFire(c, t.Companions, ts, false); _campMsg = L.F("TRAVEL_CAMP_TALKED", r.Count(x => x.Gained)); _campMsgUntil = ts + 1800;
                    if (speaker != null) { _campOpen = false; OpenTalk(speaker.Person); }               // then the talk window opens with that companion (dialogue spec 4.3)
                });
                return true;
            case Key.Key5:
            {
                var tm = new GameTime(now); int ss = Daylight.SunsetMinute(t.LatitudeTenths, tm.DayOfYear), sr = Daylight.SunriseMinute(t.LatitudeTenths, tm.DayOfYear);
                int m = tm.Hour * 60 + tm.Minute; long wait = (m < sr ? sr - m : 24 * 60 - m + sr) * 60L;
                StartCampJob(L.T("TRAVEL_CAMP_SLEEPING"), wait, () => { _campMsg = L.T("TRAVEL_CAMP_DAWN"); _campMsgUntil = _world.Clock.Now.Seconds + 1800; });
                return true;
            }
            case Key.Key6: c.Shelter = (ShelterKind)(((int)c.Shelter + 1) % 4); return true;
            case Key.Key7:
                StartCampJob(L.T("TRAVEL_CAMP_BREAKING"), t.Cfg.BreakCampMinutes * 60, () => { t.BreakCamp(_world.Clock.Now.Seconds); _rec.Action(Ravensguard.Sim.Diagnostics.HeroAction.CampBreak); _campOpen = false; _router?.ShowToast(L.T("TRAVEL_CAMP_BROKEN")); _wantedSpeed = SimSpeed.X1; });
                return true;
            case Key.Escape: _campOpen = false; return true;
        }
        return false;
    }

    void StartCampJob(string name, long secs, Action apply) { long now = _world.Clock.Now.Seconds; _campJob = (name, now + Math.Max(60, secs), apply); _rec.Action(Ravensguard.Sim.Diagnostics.HeroAction.CampJob, secs, now); _rec.SetSpeed(SimSpeed.X8); }

    void FireAction(TravelState t, Camp c, long now)
    {
        var stock = t.StoreOf(c.Id); ushort wood = t.Goods!.IdOf("firewood");
        int people = Math.Max(1, t.Companions.Count);
        if (c.FuelG < 5000 && stock.Qty(wood) < 10_000)
        {
            StartCampJob(L.T("TRAVEL_CAMP_GATHER"), 20 * 60, () => { stock.Create(wood, (long)t.Cfg.GatherGPerMinute * 20 * people, CreateReason.Cut); FireAction(t, c, _world.Clock.Now.Seconds); });
            return;
        }
        t.AddFuel(c, Math.Min(15_000, stock.Qty(wood)));
        if (c.FireLit) { _campMsg = L.T("TRAVEL_CAMP_FUEL"); _campMsgUntil = now + 1200; return; }
        var wx = _wfeed.Sample(new GameTime(now), _world.WorldCell.X, _world.WorldCell.Y).State;
        bool ok = t.TryLightFire(c, EconomyState.HeroOwner, 20, wx, dryTinder: true, now, attempt: (int)(now % 7));
        _campMsg = ok ? L.T("TRAVEL_CAMP_LIT") : L.T("TRAVEL_CAMP_NOLIGHT"); _campMsgUntil = now + 1800;
    }

    /// <summary>True when the hero stands within the fire's warmth of a lit camp fire (the clothing model's "by a fire").</summary>
    bool CampFireNear()
    {
        if (_travel?.Camp is not { FireLit: true } c) return false;
        var (hx, hy) = _world.WorldMm; double dx = (c.XMm - hx) / 1000.0, dy = (c.YMm - hy) / 1000.0;
        return Math.Sqrt(dx * dx + dy * dy) <= _travel.Cfg.FireWarmthM;
    }

    // ------------------------------------------------------------------------------------------ foraging

    void Forage()
    {
        var t = _travel!; var layer = Runtime?.State.Production?.Resources;
        if (layer == null || t.PackStore?.Invoke() is not { } pack) { _router?.ShowToast(L.T("TRAVEL_FORAGE_NONE")); return; }
        var (wx, wy) = _world.WorldCell; int tile = RegionData.Index(Math.Clamp(wx / RegionData.TileM, 0, RegionData.Size - 1), Math.Clamp(wy / RegionData.TileM, 0, RegionData.Size - 1));
        var season = _world.Clock.Now.Season; int day = (int)_world.Clock.Now.TotalDays;
        if (layer.Find(tile, ResourceKind.Forage) < 0) { _router?.ShowToast(L.T("TRAVEL_FORAGE_NONE")); return; }
        StartCampJob(L.T("TRAVEL_FORAGING"), t.Cfg.ForageSweepMinutes * 60, () =>
        {
            var r = t.Sweep(layer, tile, 20, season, day, pack);
            _router?.ShowToast(r.Grams > 0 ? L.F("TRAVEL_FORAGE_GOT", r.Grams, (r.TileLeftG / 1000.0).ToString("0.0")) : L.T("TRAVEL_FORAGE_EMPTY"), 3f);
            _wantedSpeed = SimSpeed.X1;
        });
    }

    // ------------------------------------------------------------------------------------------ view

    /// <summary>Per frame: the banner, the alert, the camp panel, the people of an encounter and the camp's fire.</summary>
    void TravelRender()
    {
        var t = _travel; var hud = _thud; if (t == null || hud == null) return;
        long now = _world.Clock.Now.Seconds;
        if (t.Journey is { State: JourneyState.Travelling or JourneyState.Paused } j)
        {
            double leftKm = (j.TotalMm - j.ProgressMm) / 1_000_000.0;
            double hoursLeft = (j.TotalMm - j.ProgressMm) / (double)Math.Max(1, _tfeed!.Hero.BaseMmPerGs == 0 ? 150 : _tfeed.Hero.BaseMmPerGs) / 3600.0;
            var (ok, why) = t.Gate8x(_tfeed.Hero);
            string speed = _world.Clock.Speed == SimSpeed.Paused ? "paused" : $"{(int)_world.Clock.Speed}x";
            hud.Banner(L.F("TRAVEL_BANNER", j.DestName, leftKm.ToString("0.0"), hoursLeft.ToString("0")), j.TotalMm == 0 ? 1f : (float)j.ProgressMm / j.TotalMm,
                L.F("TRAVEL_BANNER_LINE", speed, H("world.back"), H("world.speed_8")), ok ? "" : why);
        }
        else hud.HideBanner();
        if (t.Bus.PausedBy is { } p && t.Journey is { State: JourneyState.Paused })
        {
            string body = p.Text;
            string keys = L.F("TRAVEL_ALERT_KEYS", H("world.act"), H("world.back"), H("world.camp"));
            if (_enc is { } e2 && _encOpen)
            {
                body = (e2.Bark.Length > 0 ? "\"" + e2.Bark + "\"\n" : "") + L.F("TRAVEL_ALERT_PEOPLE", e2.Cast.Length);
                if (_encOptions.Count > 0) { body += "\n" + string.Join("\n", _encOptions.Select((o, i) => $"[{i + 1}] {L.T(o.TextKey)}" + (o.Mites > 0 ? $"  ({Quotes.Money(o.Mites)})" : ""))); keys = L.F("ENC_KEYS", H("world.back")); }
            }
            hud.Alert(L.F("TRAVEL_ALERT_" + p.Kind.ToString().ToUpperInvariant(), _enc is { } e3 ? EncounterName(e3.Id) : p.Text), body, keys, p.Kind is InterruptKind.Hostile or InterruptKind.Attacked);
        }
        else hud.HideAlert();
        if (t.Camp is { } c && _campOpen) hud.Camp(L.T("TRAVEL_CAMP_TITLE"), CampLines(t, c, now)); else hud.HideCamp();
        SyncCastSprites(now);
        SyncCampSprite();
        SyncSnareSprites();
    }

    string EncounterName(string id) { var d = _travel!.Table.Defs.FirstOrDefault(x => x.Id == id); return d?.Name ?? id; }

    List<string> CampLines(TravelState t, Camp c, long now)
    {
        var stock = t.StoreOf(c.Id);
        double food = t.Goods == null ? 0 : stock.Rows().Where(r => t.Goods.FoodMeasuresPpm(r.Good) > 0).Sum(r => (double)t.Goods.MassG(r.Good, r.Qty)) / 1000.0;
        double wood = stock.Qty("firewood") / 1000.0;
        var l = new List<string>
        {
            L.F("TRAVEL_CAMP_FIRE", c.FireLit ? L.T("TRAVEL_CAMP_LITWORD") : L.T("TRAVEL_CAMP_OUTWORD"), (c.FuelG / 1000.0).ToString("0.0")),
            L.F("TRAVEL_CAMP_STOCK", food.ToString("0.0"), wood.ToString("0.0")),
            L.F("TRAVEL_CAMP_SHELTER", c.Shelter.ToString(), t.RestPerHour(c)),
            L.F("TRAVEL_CAMP_WATCH", c.Sentries.Count, t.Cfg.SentryMax),
            "",
            $"[1] {L.T("TRAVEL_CAMP_K1")}", $"[2] {L.T("TRAVEL_CAMP_K2")}", $"[3] {L.T("TRAVEL_CAMP_K3")}", $"[4] {L.T("TRAVEL_CAMP_K4")}", $"[5] {L.T("TRAVEL_CAMP_K5")}", $"[6] {L.T("TRAVEL_CAMP_K6")}", $"[7] {L.T("TRAVEL_CAMP_K7")}",
        };
        if (_campJob is { } job) l[4] = L.F("TRAVEL_CAMP_BUSY", job.Name, Math.Max(0, (job.EndSec - now) / 60));
        else if (now < _campMsgUntil) l[4] = _campMsg;
        return l;
    }

    void SyncCampSprite()
    {
        var c = _travel?.Camp;
        if (c == null) { if (_campSprite != null) { _campSprite.QueueFree(); _campSprite = null; } return; }
        if (_campSprite == null)
        {
            var tex = _art.Texture("object.campfire_lit");
            _campSprite = new Sprite2D { Texture = tex, Centered = false, Offset = new Vector2(-tex.GetWidth() / 2f, -tex.GetHeight() + 6), TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
            _ysort.AddChild(_campSprite);
        }
        _campSprite.Position = new Vector2((float)Math.Round(c.XMm / 1000.0 * Tile), (float)Math.Round(c.YMm / 1000.0 * Tile));
        _campSprite.Texture = _art.Texture(c.FireLit ? "object.campfire_lit" : "object.campfire");
        _campSprite.Modulate = Colors.White;
    }

    /// <summary>The people of the encounter standing on the road where the director put them: real rig figures, facing the hero.</summary>
    void SyncCastSprites(long now)
    {
        var want = new HashSet<ulong>();
        if (_enc is { } ev && _encOpen)
        {
            var arch = _travel!.Travellers.ToDictionary(x => x.Id.Value, x => x.Archetype);
            string groupArch = _travel.Table.Defs.FirstOrDefault(d => d.Id == ev.Id)?.Groups.FirstOrDefault()?.Archetype ?? "traveller";
            int i = 0;
            foreach (var id in ev.Cast)
            {
                want.Add(id);
                if (!_castActors.TryGetValue(id, out var a))
                {
                    a = new RigActor(_rig, OutfitFor(arch.GetValueOrDefault(id, groupArch))); _ysort.AddChild(a); _castActors[id] = a;
                }
                var (hx, hy) = _world.WorldMm; double ox = (i % 3 - 1) * 1.1, oy = (i / 3) * 1.0 - 0.5;
                a.Position = new Vector2((float)Math.Round((ev.SpawnXMm / 1000.0 + ox) * Tile), (float)Math.Round((ev.SpawnYMm / 1000.0 + oy) * Tile));
                a.Pose(hx >= ev.SpawnXMm ? Facing.East : Facing.West, "idle", (int)(now / 2 + i));
                i++;
            }
        }
        foreach (var id in _castActors.Keys.Where(k => !want.Contains(k)).ToList()) { _castActors[id].QueueFree(); _castActors.Remove(id); }
    }

    // ------------------------------------------------------------------------------------------ dev

    string DevTravelConsole(string cmd, string[] a)
    {
        var t = _travel; if (t == null) return "no travel here (no content)";
        long now = _world.Clock.Now.Seconds; var (hx, hy) = _world.WorldMm;
        switch (cmd)
        {
            case "camp": ToggleCamp(); return t.Camp != null ? "camp made" : "could not make camp here";
            case "wood": { if (t.Camp == null) return "no camp"; t.StoreOf(t.Camp.Id).Create(t.Goods!.IdOf("firewood"), 50_000, CreateReason.Other); t.StoreOf(t.Camp.Id).Create(t.Goods.IdOf("bread"), 6_000, CreateReason.Other); return "50 kg firewood and 6 kg bread in the camp stock"; }
            case "encounter": return DevEncounter(a.Length > 0 ? a[0] : "bandit_ambush_small");
            case "kill":
            {
                var layer = Runtime?.State.Production?.Resources; var sp = t.Species(a.Length > 0 ? a[0] : "red_deer");
                if (sp == null || layer == null) return "no game layer or unknown species";
                var tile = RegionData.Index(Math.Clamp(_world.WorldCell.X / RegionData.TileM, 0, RegionData.Size - 1), Math.Clamp(_world.WorldCell.Y / RegionData.TileM, 0, RegionData.Size - 1));
                var car = t.Kill(layer, tile, sp, now, hx, hy);
                return car == null ? "the land holds too little game for that" : $"{sp.Name} {car.MassG / 1000.0:0.0} kg killed; butcher it with the 'butcher' command";
            }
            case "butcher":
            {
                var car = t.Carcasses.FirstOrDefault(); if (car == null) return "no carcass";
                var pack = t.PackStore?.Invoke(); if (pack == null) return "no pack";
                var r = t.Butcher(car, pack, 50, now); if (r == null) return "too old to butcher";
                return "butchered: " + string.Join(", ", r.Made.Select(m => $"{m.Good} {m.Grams / 1000.0:0.00} kg")) + $"; waste {r.WasteG / 1000.0:0.0} kg";
            }
        }
        return "?";
    }

    string DevEncounter(string id)
    {
        var t = _travel!; var d = t.Table.Defs.FirstOrDefault(x => x.Id == id); if (d == null) return "unknown encounter " + id;
        var (hx, hy) = _world.WorldMm; long now = _world.Clock.Now.Seconds;
        if (t.Journey is not { State: JourneyState.Travelling or JourneyState.Paused })
        {
            var path = new RoutePath([hx, hx + 4_000_000], [hy, hy]);
            t.Begin("the next village", path, now); t.Journey!.ProgressMm = 0;
        }
        var cast = new List<ulong>(); var g = d.Groups.FirstOrDefault();
        long sx = hx + (Debug.DevContext.Travel != null && _opt.Travel != null ? 9_000 : 70_000), sy = hy;
        if (g != null) { int n = Math.Min(4, g.Count.Count > 1 ? g.Count[1] : 1); foreach (var id2 in EncounterPlacement.Cast(t, d, g, n, now, sx, sy)) cast.Add(id2.Value); }
        var ev = new EncounterEvent(now, d.Id, d.Kind, hx, hy, sx, sy, t.LegSerial, [.. cast], d.Bark);
        t.Encounters.Add(ev); _enc = ev; _encOpen = true; _encOptions = EncounterOutcomes.Options(d, Math.Max(20_000, _heroLink?.View?.CoinMites ?? 0));
        t.Bus.Raise(IsHostileKind(d.Kind) ? InterruptKind.Hostile : InterruptKind.Contract, now, hx, hy, d.Name);
        if (t.Journey != null) t.Journey.State = JourneyState.Paused;
        return d.Name;
    }

    /// <summary>--shot-travel encounter|camp: the dev start for the screenshots.</summary>
    void DevTravelShot(string what)
    {
        if (what == "encounter") DevEncounter("bandit_ambush_small");
        else if (what == "camp")
        {
            var (cx, cy) = _world.WorldMm;
            _travel!.MakeCamp(cx, cy, _world.Clock.Now.Seconds, [EconomyState.HeroOwner]); _rec.Action(Ravensguard.Sim.Diagnostics.HeroAction.CampMake, cx, cy); _campOpen = true;
            if (_travel.Camp is { } c)
            {
                c.XMm += 2_500; c.YMm -= 600;
                _travel.StoreOf(c.Id).Create(_travel.Goods!.IdOf("firewood"), 50_000, CreateReason.Other); _travel.StoreOf(c.Id).Create(_travel.Goods.IdOf("bread"), 6_000, CreateReason.Other);
                _travel.AddFuel(c, 20_000); c.FireLit = true; c.FireLastSec = _world.Clock.Now.Seconds; c.Shelter = ShelterKind.Tent;
                EnsureCompanions(_travel);
                _travel.Companions.Add(new CampMember { Person = EntityId.Make(IdKind.Person, 901), Name = "Aldric", Loyalty = 520, Familiarity = 30, AmbitionProgress = 1 });
                _travel.SetSentries(c, [EntityId.Make(IdKind.Person, 901)]);
                _campMsg = L.T("TRAVEL_CAMP_LIT"); _campMsgUntil = _world.Clock.Now.Seconds + 99_999;
            }
        }
    }
}

/// <summary>What the world map shows before a trip: the route, the distance, the time in game hours and real seconds at 1x and 8x, and how many days the pack's food lasts.</summary>
public sealed record TravelPreview(string Name, RoutePath Path, long DistM, int GameHoursTenths, long RealSeconds1x, long RealSeconds8x, double ProvisionDays, double Km);
