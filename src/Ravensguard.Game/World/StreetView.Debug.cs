using System;
using System.Linq;
using Godot;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.World;
using Ravensguard.Sim.Travel;

namespace Ravensguard.Game.World;

/// <summary>Hooks for the headless smoke (<c>--smoke</c>): open each Phase 3 screen the way the keys do, and read the few states the checks need. Dev only; play never calls them.</summary>
public partial class StreetView
{
    /// <summary>No screen or dialog above the street view.</summary>
    public bool DebugForeground => Foreground;
    public int DebugBarksSpawned => _barksSpawned;
    public bool DebugEncounterScreenPending => EncScreenPending;
    public bool DebugEncounterOpen => Enc != null && EncOpen;
    public bool DebugCampOpen => _campOpen;
    public JourneyState? DebugJourneyState => _travel?.Journey?.State;
    public TravelHud? DebugTravelHud => _thud;
    public Control? DebugHud => GetNodeOrNull<Control>("Hud");

    /// <summary>Starts a staged road encounter (the dev console's <c>encounter</c>): the journey stops, the options show.</summary>
    public string DebugEncounter(string id, EntityId? person = null)
    {
        if (_travel == null) return "no travel";
        string name = DevEncounter(id);
        if (person is { } p && _travel?.Current is { } cur) _travel.Current = cur with { Cast = [p.Value] };      // a real record to trade or talk with (the staged cast is generated travellers)
        return name;
    }

    /// <summary>The camp, made where the hero stands (the dev shot's camp).</summary>
    public void DebugCamp() { if (_travel != null) DevTravelShot("camp"); }

    /// <summary>Smoke hook: walk the hero toward the nearest live person on its level (barks need someone within 12 m). Returns that person's distance in metres, or -1 when nobody is live.</summary>
    public double DebugWalkToNearestPerson()
    {
        if (Runtime is not { } rt) return -1;
        var c = LodCentres()[0]; int z = _world.Snapshot.Hero.Z;
        var near = _lod.People.Where(p => p.Z == z).OrderBy(p => Math.Abs(p.XMm - c.X) + Math.Abs(p.YMm - c.Y)).ThenBy(p => p.Id.Value).FirstOrDefault();
        if (near.Name == null) return -1;
        var home = rt.State.HomeRegion;
        int lx = (int)(near.XMm / 1000 - home.OriginXM - _world.OriginX), ly = (int)(near.YMm / 1000 - home.OriginYM - _world.OriginY);
        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1), (0, 0), (2, 0), (0, 2) })
            if (_rec.CommandMoveTo(new Cell3(lx + dx, ly + dy, z))) break;
        return Math.Sqrt(Math.Pow(near.XMm - c.X, 2) + Math.Pow(near.YMm - c.Y, 2)) / 1000.0;
    }

    /// <summary>A person of the hero's home settlement, to talk to.</summary>
    public EntityId DebugAnyPerson()
    {
        if (Runtime is not { } rt) return EntityId.None;
        int s = HeroSettlement();
        var p = rt.State.Population.People;
        return rt.AtBarrier(() => Enumerable.Range(0, p.Count).Where(i => p.IsAlive(i) && (s < 0 || p.HomeSettlement[i] == s) && p.AgeYears(i, (int)(_world.Clock.Now.Seconds / 86400)) >= 18).Select(p.IdOf).FirstOrDefault()).GetAwaiter().GetResult();
    }

    /// <summary>Opens a screen by name, through the same method its key calls. False for an unknown name.</summary>
    public bool DebugOpen(string what)
    {
        switch (what)
        {
            case "trade": OpenTrade(HeroSettlement(), EntityId.None); return true;
            case "barter": OpenTrade(HeroSettlement(), DebugAnyPerson()); return true;
            case "inventory": OpenInventory(); return true;
            case "character": OpenCharacter(); return true;
            case "squad": OpenSquad(); return true;
            case "crafting": OpenCrafting(); return true;
            case "talk": OpenTalk(DebugAnyPerson()); return true;
            case "journal": OpenJournal(); return true;
            case "autopilot": OpenAutopilot(); return true;
            case "map": OpenMap(); return true;
            case "picker": OpenMap(true); return true;
            case "jobs": OpenWork(); return true;
            case "codex": OpenCodex(); return true;
            case "qmenu": OpenQMenu(); return true;
            case "land": OpenLand(false, force: true); return true;          // P5-A2
            case "deeds": OpenLand(true); return true;                        // P5-A2
            case "ledger": OpenLedger(); return true;                         // P5-F
            case "business": if (_ledgerView?.Tab(Ravensguard.Sim.Production.LedgerTab.Businesses).FirstOrDefault() is { } brow) { OpenBusiness(brow.Id); return true; } OpenBusiness(EntityId.None); return true;
            case "prospect": OpenProspect(); return true;                     // P5-F
            case "treatment": DebugTreatment(); return true;                  // P6-B1: the treatment screen on a wounded hero
            case "court": DebugCourt(); return true;                           // P6-C1: the Court panel over a made-up docket
            case "workboard": DebugWorkBoard(); return true;                  // P6-C2: the contracts screen over a made-up board
            case "workboard_mine": DebugWorkBoard(null, true); return true;   // P6-C2: and on the Taken tab
            case "faith": DebugFaith(); return true;                          // P6-B4: the Faith panel after alms and a pilgrim's vow
            case "will": DebugInheritance("will"); return true;               // P5-H: the will panel (witnessed by the place's priest, found at the barrier)
            case "family": DebugFamily("empty"); return true;                 // P6-B3: the Family panel of a hero with no family
            case "family_full": DebugFamily("panel"); return true;            // P6-B3: married, two children, a course under way
            case "overlay": ShowControls(); return true;
            default: return false;
        }
    }

    /// <summary>A party bar with the hero and three members (long names), the widest case the HUD has.</summary>
    public void DebugPartyBar()
    {
        if (_partyBar == null) return;
        _partySeen = _heroLink?.View;                                   // HeroTick leaves the bar alone until the view changes
        foreach (var c in _partyBar.GetChildren()) { _partyBar.RemoveChild(c); c.QueueFree(); }
        _partyBar.Visible = true;
        _partyBar.AddChild(Portrait(_hero?.Name ?? "Hero", L.T("PARTY_YOU"), 1000, PixelTheme.Gold, 1000));
        foreach (var n in new[] { "Ulenor Ravenmont", "Oseuna Barstan the Younger", "Wystan" }) _partyBar.AddChild(Portrait(n, L.T("ORDER_FOLLOW"), 600, PixelTheme.Body, -1));
    }
}
