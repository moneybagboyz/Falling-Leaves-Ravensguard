using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Dialogue;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.Rumours;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Game.World;

/// <summary>
/// The street view's side of talk (dialogue spec 4.3, 4.4, 4.7): E beside a person opens the talk window, barks float above the heads of people near the hero (view only), the lines of nearby
/// talk are listed, the hero overhears what passes between people close by, and J opens the Journal.
/// </summary>
public partial class StreetView
{
    TalkLink? _talk;
    readonly BarkSystem _barks = new();
    readonly TalkHistory _talkHist = new();
    readonly List<(Label L, EntityId Person, double Until)> _barkLabels = new();
    VBoxContainer? _nearbyTalk;
    readonly List<(Label L, double Until)> _nearbyLines = new();
    double _barkClock, _barkAcc, _overhearAcc, _climateAcc;
    bool _dear, _unrest;
    System.Threading.Tasks.Task<(bool, bool)>? _climateTask;
    System.Threading.Tasks.Task<JournalCard?>? _overheardTask;
    int _talkShotStage, _talkShotAt, _barksSpawned;

    void AttachTalk()
    {
        if (Runtime is not { } rt || _source?.Content == null || rt.State.Hero == null) return;
        var links = Ravensguard.Sim.Worlds.Phase6Links.AttachAll(rt, _source.Content);   // P7-0B: the runtime attached them at Start (behind the hero); this hands back the same set
        _talk = links.Talk; _familyLink = links.Family;
        if (_talk != null) _talk.Codex = _source?.Codex;                          // the legends the talk link tells (P6-C2)
    }

    void BuildTalkHud(Control hud)
    {
        _nearbyTalk = new VBoxContainer { Name = "NearbyTalk", MouseFilter = MouseFilterEnum.Ignore };
        _nearbyTalk.AddThemeConstantOverride("separation", 1);
        _nearbyTalk.AnchorTop = 1; _nearbyTalk.AnchorBottom = 1; _nearbyTalk.OffsetLeft = 12; _nearbyTalk.OffsetTop = -236; _nearbyTalk.OffsetBottom = -140; _nearbyTalk.GrowVertical = GrowDirection.Begin;
        hud.AddChild(_nearbyTalk);
    }

    ulong TalkSeed => Runtime?.State.Seed ?? 1;

    /// <summary>The moment and the hero, as lines and barks read them; the speaker's own fields are set by the caller.</summary>
    TalkContext TalkWorldContext()
    {
        var t = _world.Clock.Now; var x = new TalkContext
        {
            Hour = t.Hour, Season = t.Season, Weather = _wx.State.ToString(), HeroName = _hero?.Name ?? "", HeroFemale = _hero?.Female ?? false,
            Place = SettlementName(HeroSettlement()), MarketDear = _dear, UnrestHigh = _unrest, InMarket = t.Hour is >= 8 and < 17 && HeroSettlement() >= 0,
        };
        if (_heroLink?.View is { } v)
        {
            x.Coin = v.CoinMites; x.HeroRich = v.CoinMites > 5_000_000;
            x.HeroArmed = v.Items.Any(i => i.Slot is Ravensguard.Sim.Inventory.EquipSlot.MainHand or Ravensguard.Sim.Inventory.EquipSlot.OffHand && IsWeapon(i.Shape));
            x.Followers = v.Squad.Count(m => m.Active);
            x.Proficiency = s => (int)(v.Skills.Where(k => string.Equals(k.Skill.ToString(), s, StringComparison.OrdinalIgnoreCase)).Select(k => k.Micro).FirstOrDefault() / Ravensguard.Sim.Crafting.CraftMath.MicroPerLevel);
            x.ItemCount = s => v.Items.Count(i => i.Shape == s);
        }
        x.DaysToFeast = DayOfFeast(t);
        return x;
    }

    static bool IsWeapon(string shape) => shape.Contains("sword") || shape.Contains("axe") || shape.Contains("spear") || shape.Contains("mace") || shape.Contains("dagger") || shape.Contains("knife") && !shape.Contains("kitchen") || shape.Contains("bow");
    static int DayOfFeast(GameTime t) => (TimeConst.DaysPerSeason - t.DayOfSeason) + 1;

    string SettlementName(int s) => Runtime is { } rt && s >= 0 && s < rt.State.Population.Region.Settlements.Count ? rt.State.Population.Region.Settlements[s].Name : "";
    IReadOnlyList<string> SettlementNames() => Runtime is { } rt ? [.. rt.State.Population.Region.Settlements.Select(s => s.Name)] : [];

    /// <summary>The nearest other settlement's name (for "where is..." questions), null when none.</summary>
    string? FarPlace()
    {
        if (Runtime is not { } rt) return null;
        int here = HeroSettlement(); var (x, y) = HeroRegionMm(); var region = rt.State.Population.Region; string? best = null; long bd = long.MaxValue;
        for (int i = 0; i < region.Settlements.Count; i++)
        {
            if (i == here) continue;
            long d = PathPlan.Dist(region.Settlements[i].CenterX * 1000L - x, region.Settlements[i].CenterY * 1000L - y);
            if (d < bd) { bd = d; best = region.Settlements[i].Name; }
        }
        return best;
    }

    /// <summary>Which unique dialogue speaks for this person: the master of a caravan is the caravan master.</summary>
    string? UniqueOf(EntityId person)
    {
        if (Runtime is not { } rt) return null;
        int i = rt.State.Population.People.IndexOf(person);
        if (i >= 0 && Ob is { } mentor && i == mentor.MentorPerson) return mentor.MentorId;   // the first hour's mentor speaks the authored dialogue of their kind
        return i >= 0 && rt.Caravans.Any(c => c.Master == i) ? "caravan_master" : null;
    }

    /// <summary>E beside a person: the talk window (the trade window is one of its entries). False when nobody is near, so the portal gets the key.</summary>
    bool TryTalkNear()
    {
        if (_heroLink == null || _talk == null || _router == null) return TryTradeNear();
        var p = NearPerson(); if (p == null) return false;
        OpenTalk(p.Value.Id);
        return true;
    }

    void OpenTalk(EntityId person, string? shot = null, int[]? press = null)
    {
        if (_heroLink == null || _talk == null || _router == null) { _router?.ShowToast(L.T("MAP_NONE")); return; }
        Raise("dialogue_opened");
        _router.Push(new TalkScreen(_router, _heroLink, _talk, _talk.Book, person, HeroNow, TalkWorldContext, _talkHist, id => { _router.Pop(); OpenTrade(HeroSettlement(), id); }, TalkSeed, UniqueOf(person), FarPlace)
        { ShotPath = shot, ShotPress = press ?? (shot != null ? [2, 3, 3, 4] : []), SettlementNames = SettlementNames, CourtQuery = CourtOfferFor, OnChoice = i => _rec.Action(Ravensguard.Sim.Diagnostics.HeroAction.TalkChoice, i, (long)person.Value) });
    }

    void OpenJournal(string? shot = null, bool trace = false)
    {
        if (_talk == null || _router == null) { OpenWork(); return; }
        _router.Push(new JournalScreen(_router, _talk, SettlementNames, OpenWork) { ShotPath = shot, StartTrace = trace, GettingStarted = GettingStarted, PinMentor = Ob?.Complete == false && !_pinned ? PinMentor : null, OpenContracts = OpenWorkBoard, EndingOffers = EndingOffersNow, TakeEnding = TakeEnding, LieLabel = id => _lies.GetValueOrDefault(id.Value, ""), OpenHistory = id => OpenChronicle(id, ChronicleScope.Here) });
    }

    // ------------------------------------------------------------------ barks and overhearing (a view over the live people; nothing here changes a record)

    void TalkTick(double delta)
    {
        if (_talk == null || _lodSprites == null) return;
        _barkClock += delta; _barkAcc += delta; _overhearAcc += delta; _climateAcc += delta;
        // floating barks follow the head they belong to and fade out
        for (int i = _barkLabels.Count - 1; i >= 0; i--)
        {
            var (l, id, until) = _barkLabels[i];
            if (_barkClock >= until || !_lodSprites.TryPosition(id, out var pos) || !IsInstanceValid(l)) { if (IsInstanceValid(l)) l.QueueFree(); _barkLabels.RemoveAt(i); continue; }
            l.Position = pos + new Vector2(-90, -112); l.Modulate = new Color(1, 1, 1, (float)Math.Clamp((until - _barkClock) * 2, 0, 1));
        }
        for (int i = _nearbyLines.Count - 1; i >= 0; i--) if (_barkClock >= _nearbyLines[i].Until) { if (IsInstanceValid(_nearbyLines[i].L)) _nearbyLines[i].L.QueueFree(); _nearbyLines.RemoveAt(i); }
        if (_climateTask is { IsCompleted: true } ct) { if (ct.IsCompletedSuccessfully) (_dear, _unrest) = ct.Result; _climateTask = null; }       // a faulted task is dropped, never read (reading throws every frame)
        if (_climateAcc > 20 && _climateTask == null) { _climateAcc = 0; _climateTask = _talk.Climate(HeroSettlement()); }
        if (_barkAcc >= 0.25 && Foreground)
        {
            _barkAcc = 0; BarkStep();
        }
        if (_overheardTask is { IsCompleted: true } ot) { if (ot.IsCompletedSuccessfully && ot.Result != null) _router?.ShowToast(L.T("TALK_OVERHEARD")); _overheardTask = null; }
        if (_overhearAcc >= 0.5 && _overheardTask == null && Foreground) { _overhearAcc = 0; OverhearStep(); }
    }

    List<LivePerson> PeopleNear(double radiusM)
    {
        var c = LodCentres()[0]; int z = _world.Snapshot.Hero.Z;
        return [.. _lod.People.Where(p => p.Z == z && Math.Sqrt(Math.Pow(p.XMm - c.X, 2) + Math.Pow(p.YMm - c.Y, 2)) / 1000.0 <= radiusM)];
    }

    void BarkStep()
    {
        var c = LodCentres()[0];
        var cands = new List<BarkCandidate>(); int idx = 0;
        foreach (var p in PeopleNear(12).OrderBy(p => p.Id.Value))
        {
            double d = Math.Sqrt(Math.Pow(p.XMm - c.X, 2) + Math.Pow(p.YMm - c.Y, 2)) / 1000.0;
            var (mood, hungry) = _moods.GetValueOrDefault(p.Id.Value);          // P6-C2: the mood of the day (needs, the house's grievances, the weather) and the hungry variant
            cands.Add(new BarkCandidate(p.Id.Value, idx++, d, p.Occupation.ToString().ToLowerInvariant(), p.Sex == Ravensguard.Sim.People.Sex.Female, mood, RelationTier.Stranger, hungry, false));
        }
        if (cands.Count == 0) return;
        RefreshMoods([.. cands.Select(c => new EntityId(c.Id))]);
        var ctx = TalkWorldContext();
        var barks = _barks.Update(_talk!.Book, TalkSeed, _world.Clock.Now.Seconds, _barkClock, cands, ctx, GameText.Instance);
        foreach (var b in barks)
        {
            var id = new EntityId(b.Person);
            var label = new Label { Text = b.Text, CustomMinimumSize = new Vector2(180, 0), HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore, ZIndex = 60, ThemeTypeVariation = "HintLabel" };
            label.AddThemeColorOverride("font_color", b.Channel is "alarm" or "combat" ? PixelTheme.Warn : PixelTheme.Gold);
            label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0)); label.AddThemeConstantOverride("outline_size", 4);
            _ysort.AddChild(label); _barkLabels.Add((label, id, _barkClock + b.DurationS)); _barksSpawned++;
            string name = _lod.People.FirstOrDefault(p => p.Id == id).Name ?? "";
            if (_nearbyTalk != null)
            {
                var line = new Label { Text = L.F("LOG_BARK", name.Split(' ')[0], b.Text), ThemeTypeVariation = "HintLabel", MouseFilter = Control.MouseFilterEnum.Ignore, AutowrapMode = TextServer.AutowrapMode.Off };
                line.AddThemeColorOverride("font_color", PixelTheme.Body); line.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0)); line.AddThemeConstantOverride("outline_size", 4);
                _nearbyTalk.AddChild(line); _nearbyLines.Add((line, _barkClock + 8));
                while (_nearbyLines.Count > 4) { _nearbyLines[0].L.QueueFree(); _nearbyLines.RemoveAt(0); }
            }
        }
    }

    void OverhearStep()
    {
        if (HeroSettlement() < 0) return;
        var near = PeopleNear(6).Select(p => p.Id).ToList();
        if (near.Count < 2) return;
        _overheardTask = _talk!.Overhear(near, HeroSettlement());
    }

    // ------------------------------------------------------------------ dev screenshots: --shot-panel talk | barks | journal | journal-trace

    void TalkShots()
    {
        if (_opt.Panel is not ("talk" or "barks" or "journal" or "journal-trace")) return;
        if (_frames % 300 == 1) GD.Print($"[talk] shot check: hero link {(_heroLink != null)}, talk {(_talk != null)}, runtime {(Runtime != null)}");
        if (_heroLink == null || _talk == null || Runtime is not { } rt) return;
        if (_talkShotStage == 0 && _frames % 300 == 0) GD.Print($"[talk] waiting: frame {_frames}, hour done {rt.HourDone}, clock hour {_world.Clock.Now.TotalHours}");
        if (_talkShotStage == 0 && _frames >= 60 && rt.HourDone >= _world.Clock.Now.TotalHours) { _talkShotStage = 1; _talkShotAt = _frames; }
        if (_talkShotStage == 1)
        {
            _talkShotStage = 2;
            int s = HeroSettlement();
            if (_opt.Panel is "journal" or "journal-trace")
            {
                // a rumour the town already holds: the dev shot seeds a famine entry in the world and has the hero ask the first person who holds it
                _ = Bar(rt, () =>
                {
                    var st = rt.State; var ts = st.Talk ??= new TalkState();
                    string place = SettlementName(s);
                    st.Chronicle.Add(new ChronicleEntry(st.Ids.Allocate(IdKind.Event), new GameTime(rt.HourDone * 3600), "famine", [], [new Cause(CauseKind.Famine, null, "two poor harvests running")], new Dictionary<string, string> { ["settlement"] = place, ["hungry_households"] = "14" }));
                    var w = TalkWorld.From(st);
                    for (long h = rt.HourDone + 1; h <= rt.HourDone + 30; h++) TalkSystem.AfterHour(w, ts, _talk.Book, h);   // days of gossip, as the world would have had
                    var pick = ts.Rumours.Held.Where(k => k.Value.Count > 0 && ts.Rumours.Find(k.Value[0], k.Key)!.Value.Hop >= 2).Select(k => k.Key).OrderBy(k => k).FirstOrDefault(-1);
                    if (pick < 0) pick = ts.Rumours.Held.Keys.OrderBy(k => k).FirstOrDefault(-1);
                    if (pick >= 0) TalkSystem.HeroHears(w, ts, ts.Rumours.HeldBy(pick)[0], pick, rt.HourDone + 30, HearHow.Told);
                    return 0;
                }).ContinueWith(_ => _talk.Refresh());
            }
        }
        if (_talkShotStage != 2 || _frames != _talkShotAt + 90) return;
        switch (_opt.Panel)
        {
            case "talk":
                {
                    var p = NearPerson(12000); GD.Print($"[talk] shot: near {(p == null ? "nobody" : p.Value.Name)}");
                    if (p != null) OpenTalk(p.Value.Id, _opt.ShotPath);
                    break;
                }
            case "journal": OpenJournal(_opt.ShotPath); break;
            case "journal-trace": OpenJournal(_opt.ShotPath, true); break;
            case "barks": _talkShotStage = 3; break;
        }
    }

    /// <summary>Dev shot "barks": waits until a few barks are up over the heads of people near the hero, then takes the picture.</summary>
    void BarkShotStep()
    {
        if (_opt.Panel != "barks" || _talkShotStage != 3) return;
        if (_barkLabels.Count >= 2 || _frames > _talkShotAt + 900) { Shoot(); _talkShotStage = 4; }
    }
}
