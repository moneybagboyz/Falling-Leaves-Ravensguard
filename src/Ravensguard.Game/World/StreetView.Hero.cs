using System;
using System.Linq;
using Godot;
using Ravensguard.Game.UI;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.Trade;
using HeroLink = Ravensguard.Sim.Trade.HeroWorld;

namespace Ravensguard.Game.World;

/// <summary>
/// The street view's side of the hero's records (trade-inventory-character spec): the link to the running world's hero desk, the trade (E beside a person), inventory (I), character (C) and squad (P)
/// screens, the party bar on the HUD and the load on the hero's legs. All of it reads the barrier's <see cref="HeroView"/> and sends <see cref="HeroCommand"/>s; nothing here touches the records.
/// </summary>
public partial class StreetView
{
    HeroLink? _heroLink;
    HBoxContainer? _partyBar;
    HeroView? _partySeen;
    double _heroHud;
    long _toastSeen;

    /// <summary>Binds the hero to the running world (after the runtime started). The dev hamlet has none.</summary>
    void AttachHero()
    {
        if (Runtime is not { } rt || _source?.Content == null || rt.State.Hero == null) return;
        _heroLink = HeroLink.Attach(rt, _source.Content);
    }

    long HeroNow() => _world.Clock.Now.Seconds;

    /// <summary>The hero's position in region millimetres (what jobs, orders and settlements use).</summary>
    (long X, long Y) HeroRegionMm()
    {
        if (Runtime is not { } rt) return (0, 0);
        var home = rt.State.HomeRegion; var c = LodCentres()[0];
        return (c.X - home.OriginXM * 1000L, c.Y - home.OriginYM * 1000L);
    }

    /// <summary>The settlement the hero stands in or nearest to (within 4 km), -1 when none.</summary>
    int HeroSettlement()
    {
        if (Runtime is not { } rt) return -1;
        var (x, y) = HeroRegionMm(); var region = rt.State.Population.Region; int best = -1; long bd = long.MaxValue;
        for (int i = 0; i < region.Settlements.Count; i++) { long d = PathPlan.Dist(region.Settlements[i].CenterX * 1000L - x, region.Settlements[i].CenterY * 1000L - y); if (d < bd) { bd = d; best = i; } }
        return bd < 4_000_000 ? best : -1;
    }

    /// <summary>The live person nearest the hero within <paramref name="mm"/> on the same level.</summary>
    (EntityId Id, string Name, long X, long Y)? NearPerson(long mm = 2500)
    {
        var c = LodCentres()[0]; var (hx, hy) = HeroRegionMm();
        var near = _lod.People.Select(p => (P: p, D: PathPlan.Dist(p.XMm - c.X, p.YMm - c.Y))).Where(t => t.D < mm && t.P.Z == _world.Snapshot.Hero.Z).OrderBy(t => t.D).FirstOrDefault();
        return near.P.Name != null ? (near.P.Id, near.P.Name, hx, hy) : null;
    }

    // ------------------------------------------------------------------ the screens

    /// <summary>E beside a person: trade with the market there (and barter with them). False when nobody is near or there is no market (the portal gets the key then).</summary>
    bool TryTradeNear()
    {
        if (_heroLink == null || _router == null) return false;
        var p = NearPerson(); int s = HeroSettlement();
        if (p == null || s < 0) return false;
        OpenTrade(s, p.Value.Id);
        return true;
    }

    void OpenTrade(int settlement, EntityId person, string? shot = null, bool notebook = false)
    {
        if (_heroLink == null || _router == null) { _router?.ShowToast(L.T("MAP_NONE")); return; }
        _router.Push(new TradeScreen(_router, _heroLink, settlement, person, HeroNow) { ShotPath = shot, StartGood = shot != null ? "flour" : null, StartNotebook = notebook });
    }

    void OpenInventory()
    {
        if (_heroLink == null || _router == null) { _router?.ShowToast(L.T("MAP_NONE")); return; }
        _router.Push(new InventoryScreen(_router, _heroLink, HeroNow) { ShotPath = _opt.Panel == "inventory" ? _opt.ShotPath : null });
    }

    void OpenSquad()
    {
        if (_heroLink == null || _router == null) { _router?.ShowToast(L.T("MAP_NONE")); return; }
        _router.Push(new SquadScreen(_router, _heroLink, HeroNow, () => NearPerson(4000) ?? (_shotPerson != EntityId.None ? (_shotPerson, "", HeroRegionMm().X, HeroRegionMm().Y) : null)) { ShotPath = _opt.Panel == "squad" ? _opt.ShotPath : null });
    }

    string HeroBodyLine() => _clothes?.HudLine() ?? "";

    /// <summary>The crafting session: bound to the world's hero when there is one (the pack, the jobs and the clock are the world's), else the dev hamlet's own starter pack.</summary>
    CraftingSession NewCraftingSession(int light)
        => new(_source!.Content!, _hero!, _world.Clock.Now.Seconds, true, ["hearth", "workbench", "oven", "forge", "anvil"], light, _heroLink)
        {
            WorldNow = _heroLink == null ? null : HeroNow,
            SetWorldSpeed = _heroLink == null ? null : x => _rec.SetSpeed(x switch { 2 => Ravensguard.Sim.Core.SimSpeed.X2, 4 => Ravensguard.Sim.Core.SimSpeed.X4, _ => Ravensguard.Sim.Core.SimSpeed.X1 }),
        };

    // ------------------------------------------------------------------ the HUD: party bar, the load, toasts

    void BuildPartyBar(Control hud)
    {
        _partyBar = new HBoxContainer { Name = "PartyBar", MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _partyBar.AddThemeConstantOverride("separation", 6);
        _partyBar.AnchorTop = 1; _partyBar.AnchorBottom = 1; _partyBar.OffsetLeft = 12; _partyBar.OffsetTop = -96; _partyBar.OffsetBottom = -12;
        _partyBar.GrowVertical = GrowDirection.Begin;
        hud.AddChild(_partyBar);
    }

    void HeroTick(double delta)
    {
        if (_heroLink == null || (_heroHud += delta) < 0.25) return;
        _heroHud = 0;
        var v = _heroLink.View; if (v == null || ReferenceEquals(v, _partySeen)) return;
        _partySeen = v;
        // what the world did for the hero without being asked (wages, a craft ready), once each
        foreach (var r in v.Results.Where(r => r.Seq == 0 && r.AtSec > _toastSeen)) { _router?.ShowToast(r.Text); _toastSeen = r.AtSec; }
        if (_partyBar == null) return;
        foreach (var c in _partyBar.GetChildren()) { _partyBar.RemoveChild(c); c.QueueFree(); }
        var members = v.Squad.Where(m => m.Active).ToList();
        _partyBar.Visible = members.Count > 0;
        // the hero first, then the party in join order (spec 4.6: portraits with health, stamina, order icon and a needs dot)
        _partyBar.AddChild(Portrait(_hero?.Name ?? "", L.T("PARTY_YOU"), 1000, v.RatioPm > 1000 ? PixelTheme.Warn : PixelTheme.Gold, v.SpeedPm));
        foreach (var m in members)
            _partyBar.AddChild(Portrait(m.Name, L.T("ORDER_" + m.Order.ToString().ToUpperInvariant()), m.Loyalty, m.UnpaidDays > 0 ? PixelTheme.Warn : PixelTheme.Body, -1));
    }

    static Control Portrait(string name, string order, int loyalty, Color dot, int speedPm)
    {
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; box.AddThemeConstantOverride("separation", 1);
        var head = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; head.AddThemeConstantOverride("separation", 4);
        var face = new ColorRect { Color = new Color("3a2f28"), CustomMinimumSize = new Vector2(28, 28), MouseFilter = MouseFilterEnum.Ignore };
        face.AddChild(new Label { Text = name.Length > 0 ? name[..1] : "?", Position = new Vector2(8, 2), MouseFilter = MouseFilterEnum.Ignore });
        head.AddChild(face);
        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; col.AddThemeConstantOverride("separation", 0);
        col.AddChild(new Label { Text = name.Split(' ')[0], MouseFilter = MouseFilterEnum.Ignore });
        var o = new Label { Text = order + (speedPm >= 0 && speedPm < 1000 ? $" x{speedPm / 1000.0:0.00}" : ""), ThemeTypeVariation = "HintLabel", MouseFilter = MouseFilterEnum.Ignore };
        col.AddChild(o);
        head.AddChild(col);
        head.AddChild(new ColorRect { Color = dot, CustomMinimumSize = new Vector2(6, 6), SizeFlagsVertical = SizeFlags.ShrinkBegin, MouseFilter = MouseFilterEnum.Ignore });
        box.AddChild(head);
        box.AddChild(new ProgressBar { MaxValue = 1000, Value = loyalty, ShowPercentage = false, CustomMinimumSize = new Vector2(96, 6), MouseFilter = MouseFilterEnum.Ignore });
        var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        panel.AddChild(box);
        return panel;
    }

    /// <summary>
    /// The load on the hero's legs (spec 4.3): over half the capacity the hero cannot sprint, over the whole of it only walks, over one and a half not at all. The exact speed factor
    /// (<see cref="Ravensguard.Sim.Inventory.Encumbrance.SpeedPm"/>) needs the live world's step to take it (Live/LiveWorld, not this view); until then the gait stands in for it.
    /// </summary>
    MoveInput LoadInput(MoveInput m)
    {
        int r = _heroLink?.View?.RatioPm ?? 0;
        if (r <= 500) return m;
        if (!Ravensguard.Sim.Inventory.Encumbrance.CanWalk(r)) return new MoveInput(0, 0);
        return r > 1000 ? m with { Sprint = false, Walk = true } : m with { Sprint = false };
    }

    // ------------------------------------------------------------------ dev screenshots: --shot-panel trade | prices | inventory | squad | party

    EntityId _shotPerson;

    void HeroShots()
    {
        if (_opt.Panel is not ("trade" or "prices" or "inventory" or "squad" or "party") || _heroLink == null || Runtime is not { } rt) return;
        if (_frames == 60)
        {
            int s = HeroSettlement(); long now = HeroNow();
            // something in the pack to show: a little of the market's goods
            if (_opt.Panel is "inventory" or "party" or "squad")
            {
                _heroLink.Issue(new HeroCommand { Kind = HeroCommandKind.VisitMarket, Settlement = s, IssuedSec = now });
                foreach (var (g, q) in new[] { ("flour", 4_000L), ("salt", 500L), ("ale", 2_000L), ("bread", 3L) })
                    if (rt.State.Economy!.Goods.TryIdOf(g, out var id)) _heroLink.Issue(new HeroCommand { Kind = HeroCommandKind.Buy, Settlement = s, Good = id, Qty = q, IssuedSec = now });
            }
            if (_opt.Panel is "party" or "squad")
            {
                // a townsman of this place: talk (the dev shot dates the first talk an hour back, so the two count), invite them to join, hire a second hand
                var pick = rt.AtBarrier(() =>
                {
                    var p = rt.State.Population.People; int day = (int)(now / 86400);
                    return Enumerable.Range(0, p.Count).Where(i => p.IsAlive(i) && p.HomeSettlement[i] == s && p.AgeYears(i, day) is >= 18 and < 45).Take(2).Select(p.IdOf).ToArray();
                }).GetAwaiter().GetResult();
                if (pick.Length > 0)
                {
                    _shotPerson = pick[0];
                    _heroLink.Issue(new HeroCommand { Kind = HeroCommandKind.Talk, Person = pick[0], IssuedSec = now - 3600 });
                    _heroLink.Issue(new HeroCommand { Kind = HeroCommandKind.Talk, Person = pick[0], IssuedSec = now });
                    _heroLink.Issue(new HeroCommand { Kind = HeroCommandKind.Recruit, Person = pick[0], Wage = 180, IssuedSec = now });
                    if (pick.Length > 1) _heroLink.Issue(new HeroCommand { Kind = HeroCommandKind.Hire, Person = pick[1], Wage = 400, Days = 5, IssuedSec = now });
                }
            }
        }
        if (_frames != 150) return;
        int st = HeroSettlement();
        switch (_opt.Panel)
        {
            case "trade": OpenTrade(st, NearPerson(8000)?.Id ?? EntityId.None, _opt.ShotPath); break;
            case "prices": OpenTrade(st, EntityId.None, _opt.ShotPath, notebook: true); break;
            case "inventory": OpenInventory(); break;
            case "squad": OpenSquad(); break;
            case "party": _heroHud = 1; _partySeen = null; HeroTick(0); Shoot(); break;
        }
    }
}
