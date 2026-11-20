using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Heists;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.Weather;

namespace Ravensguard.Game.World;

/// <summary>
/// The street view's side of the heists (P6-D3, heists-and-jailbreaks 4, 6): the Q menu's "slip into the gaol, the strongroom, the vault, the undercroft, the drain" of the place he stands in, the heist panel over the view the barrier built
/// (<see cref="HeroView.Heist"/>), and every action sent as a recorded hero command (<see cref="HeistDesk"/>). The world clock is held while the panel is up: a heist plays on its own second-by-second clock. No state here but which
/// answers have been toasted.
/// </summary>
public partial class StreetView
{
    long _hjSeen;
    HeistScreen? _heistScreen;

    int WeatherClass() => _wx.State switch { WeatherState.LightRain => 1, WeatherState.HeavyRain => 2, WeatherState.Thunderstorm => 3, _ => 0 };

    void HeistAct(string action)
    {
        if (_heroLink == null) return;
        _heroLink.Issue(HeistDesk.Do(action, HeroNow()));
    }

    void HeistGiveUp()
    {
        if (_heroLink == null) return;
        _heroLink.Issue(HeistDesk.End(HeroNow()));
    }

    void OpenHeistPanel()
    {
        if (_router == null || _heroLink == null || _router.Top is HeistScreen) return;
        _rec.SetWantedSpeed(SimSpeed.Paused); _rec.SetSpeed(SimSpeed.Paused);
        _heistScreen = new HeistScreen(_router, () => _heroLink.View?.Heist, HeistAct, HeistGiveUp);
        _router.Push(_heistScreen);
    }

    void EnterHeist(HeistKind kind)
    {
        if (_heroLink == null) return;
        int s = HeroSettlement(); if (s < 0) { _router?.ShowToast(L.T("MAP_NONE")); return; }
        _heroLink.Issue(HeistDesk.Enter(s, kind, WeatherClass(), HeroNow()));
        OpenHeistPanel();
    }

    /// <summary>Per frame: the answers to heist commands, toasted once.</summary>
    void HeistTick()
    {
        if (_heroLink?.View is not { } v) return;
        foreach (var r in v.Results.Where(r => r.Kind == HeroCommandKind.Heist && r.Seq > _hjSeen)) { if (r.Text != L.T("hj_done")) _router?.ShowToast(r.Text, 5f); _hjSeen = r.Seq; }
    }

    /// <summary>The Q menu: the sites of the place he stands in; with a heist in play, back to it.</summary>
    void HeistVerbs(List<QMenu.Verb> v)
    {
        if (Runtime?.State is not { } st || _heroLink == null) return;
        if (_heroLink.View?.Heist is { Outcome: RunOutcome.Active })
        {
            v.Add(new(L.T("hj_q_head"), () => { }, Heading: true));
            v.Add(new(L.T("hj_q_return"), OpenHeistPanel));
            return;
        }
        int s = HeroSettlement(); if (s < 0) return;
        var plan = HeistSites.Plan(st.Population.Region, st.Population.People).Where(p => p.Settlement == s).Select(p => p.Kind).ToList();
        if (plan.Count == 0) return;
        v.Add(new(L.T("hj_q_head"), () => { }, Heading: true));
        foreach (var k in plan) { var kind = k; v.Add(new(L.T("hj_q_" + kind.ToString().ToLowerInvariant()), () => EnterHeist(kind))); }
    }

    // ------------------------------------------------------------------ dev (--ng-shot heist, smoke)

    /// <summary>Dev: the heist panel over a castle's dungeon at night, the hero in the corridor before the cells, two guards on their round, one asleep with the keys.</summary>
    public void DebugHeist(string? shot = null)
    {
        if (_router == null) return;
        var rules = HeistRules.From(_source?.Content);
        var book = new HeistBook();
        var run = HeistSites.Start(book, rules, 1234, 3, HeistKind.Gaol, 2, 5 * 86_400L + 3600, 0, [], [], null);
        HeistView? view = null;
        if (run != null)
        {
            run.Lockpicking = 70; run.Stealth = 60; run.SetSneak(true);
            run.Here.Slot[0] = 1; run.Here.Slot[2] = 2;
            run.Here.Door[6] = 2; run.Here.Door[7] = 2;                            // the way in is open: the demo starts in the cell block
            run.X = 2; run.Y = 4; run.Advance(1);                                  // beside the first cell's bars, in the light of a sconce, the round at the far end of the corridor
            view = HeistView.Of(run);
        }
        _router.Push(new HeistScreen(_router, () => view, _ => { }, () => { }) { ShotPath = shot });
    }
}
