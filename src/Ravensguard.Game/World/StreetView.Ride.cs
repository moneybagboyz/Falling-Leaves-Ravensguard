using System;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Live;

namespace Ravensguard.Game.World;

/// <summary>The street view's horses and carts (horses-and-carts spec 4 and 6): the ride book on the live world, E to mount and dismount and to drive, O to walk while mounted, the hitch, push, dig and unload keys, the bars, and the sprites.</summary>
public partial class StreetView
{
    RideBook? _ride; RideSprites? _rideSprites; RideHud? _rideHud;
    int _rideMsg; bool _walkToggle; long _rideHour = long.MinValue; bool _rideHinted, _mudHinted;

    /// <summary>The ride book of the run: restored from a save, or new on the economy's stores; a dev start (<c>--shot-ride</c>) adds a horse and a cart beside the hero. Null without content (the bare hamlet).</summary>
    void AttachRide()
    {
        var content = _source?.Content; if (content == null) return;
        if (Runtime is { } rt && rt.State.Economy is { } econ)
        {
            _ride = rt.State.Mounts ??= new RideBook(content, econ, rt.State.ContinentSettings.Seed);
        }
        else
        {
            var goods = new GoodCatalog(content);
            _ride = new RideBook(content, goods, new MassLedger(goods.Count), 1);
        }
        _world.AttachRide(_ride);
        _ride.SurfaceNow = SurfaceHere;
        if (_hero != null)
        {
            _ride.RidingSkill = _hero.Skill("riding"); _ride.HusbandrySkill = _hero.Skill("husbandry"); _ride.Strength = _hero.Attribute(Ravensguard.Sim.Characters.HeroAttr.Strength);
        }
        _rideSprites = new RideSprites(_ysort, _art);
        _rideHud = new RideHud();
        GetNodeOrNull<Control>("Hud")?.AddChild(_rideHud);
        Debug.DevContext.Ride = DevRideConsole;
        Debug.DevContext.Rooms = DevRooms;      // P5-A3
        Debug.DevContext.Build = DevBuild;      // P5-B
        if (_opt.Rooms) ShowRooms();            // P5-A3
        if (_opt.Ride != null) DevRide(_opt.Ride);
        AttachBoats();                          // P5-G: boats on the rivers (StreetView.Vehicles.cs)
    }

    string DevRideConsole(string cmd, string[] a)
    {
        var r = _ride; if (r == null) return "no horses here (no content)";
        var (x, y) = _world.WorldMm;
        switch (cmd)
        {
            case "horse":
            {
                string id = a.Length > 0 ? (a[0] == "ox" || a[0] == "mule" || a[0] == "donkey" ? a[0] : "horse_" + a[0]) : "horse_palfrey";
                var b = r.Breeds.Find(id); if (b == null) return "unknown breed " + id;
                var an = r.Animals.Create(b, Ravensguard.Sim.Core.EntityId.None, 900, 0, x + 1500, y); return $"{b.Def.Name} {an.Id} beside you: press {H("world.act")} to mount";
            }
            case "loan": { var an = r.Loan(r.Breeds.Get("horse_palfrey"), Ravensguard.Sim.Core.EntityId.None, _world.Clock.Now.Seconds, x + 1500, y); return $"palfrey {an.Id} on loan for 3 game days"; }
            case "cart":
            {
                var bp = r.Templates.Find(a.Length > 0 ? a[0] : "cart"); if (bp == null) return "unknown template";
                int animals = a.Length > 1 && int.TryParse(a[1], out int n) ? Math.Clamp(n, 0, bp.Stats.MaxAnimals) : bp.Stats.HitchSlots;
                var v = BesideCart(r, bp, x, y, animals);
                return $"{bp.Def.Name} {bp.Stats.WidthCells} x {bp.Stats.LengthCells} m with {v.Team.Count} animals (up to {bp.Stats.MaxAnimals}): press {H("world.act")} next to it to drive";
            }
            case "mud": r.SurfaceOverride = a.Length > 0 && a[0] == "off" ? null : Surface.DeepMud; return r.SurfaceOverride == null ? "mud off" : "deep mud";
        }
        return DevVehicles(cmd, a);             // P5-G: designer, site, boat, break
    }

    /// <summary>A template vehicle standing with its seat level with the hero and its near side a metre and a half in front of them, the first <paramref name="animals"/> of its team hitched, and a load.</summary>
    static Ravensguard.Sim.Carts.VehicleRow BesideCart(RideBook r, Ravensguard.Sim.Carts.VehicleBlueprint bp, long x, long y, int animals)
    {
        var st = bp.Stats;
        var v = r.Vehicles.Create(bp, Ravensguard.Sim.Core.EntityId.None, x, y - 1500 - st.WidthCells * 500);
        string beast = bp.Id is "ox_wagon" or "hay_wain" ? "ox" : "horse_draught";
        for (int i = 0; i < animals; i++) r.Vehicles.Hitch(v, r.Animals.Create(r.Breeds.Get(beast), Ravensguard.Sim.Core.EntityId.None, 900, 0, v.XMm, v.YMm));
        v.Cargo.CreateUpTo(r.Goods.IdOf("oats"), st.CargoKg * 600L, Ravensguard.Sim.Goods.CreateReason.Other);
        return v;
    }

    /// <summary>The surface under the hero's wheels, from the ground tile and the weather of now (spec 4.5).</summary>
    Surface SurfaceHere()
    {
        var h = _world.Snapshot.Hero;
        var g = _world.Map?.GroundAt(h.CellX, h.CellY, h.Z) ?? GroundKind.Grass;
        var kind = g switch
        {
            GroundKind.Cobble or GroundKind.Bridge => Ground.Paved, GroundKind.Dirt or GroundKind.Gravel => Ground.Road, GroundKind.Sand => Ground.Sand, GroundKind.Ford => Ground.FordApproach,
            GroundKind.Mud or GroundKind.Marsh => Ground.Field, _ => Ground.Field,
        };
        var wet = new WetState((int)(_wx.Wet * 350f), (int)_wx.SnowCm, _wx.Freeze > 0.5f, false);
        var s = SurfaceModel.Classify(kind, wet);
        return g is GroundKind.Mud or GroundKind.Marsh && s is Surface.Grass ? Surface.MudRoad : s;
    }

    int HeroRiderKg() => 85;

    bool HeroWalkInput() => _ride is { IsMounted: true } or { IsDriving: true } ? _walkToggle : Actions.Held("world.sneak");

    /// <summary>Per sim tick: the game hour's thirst and the day's rest for every animal, loans that fall due, and the Riding uses the ride has earned.</summary>
    void RideTick()
    {
        if (_ride == null) return;
        long now = _world.Clock.Now.Seconds, hour = now / 3600;
        if (_rideHour == long.MinValue) _rideHour = hour;
        lock (_ride.Gate) for (; _rideHour < hour; _rideHour++) _ride.Hour(_rideHour + 1);
        if (_world.Tick % 20 == 0 && _ride.ExpireLoans(now) > 0) _router?.ShowToast(L.T("RIDE_LOAN_DUE"));
        if (_ride.IsMounted || _ride.IsDriving) { if (!_rideHinted) { _rideHinted = true; _router?.ShowToast(L.F("RIDE_HINT_FIRST", H("world.act"), H("world.sprint"), H("mounted.walk_toggle")), 5f); } }
        else _walkToggle = false;
        if (_ride.DrivenVehicle is { Stuck: true } && !_mudHinted) { _mudHinted = true; _router?.ShowToast(L.F("RIDE_HINT_MUD", H("world.act"), H("world.cart_unload"), H("world.cart_dig")), 6f); }
    }

    /// <summary>Keeps the Mounted input layer on while the hero rides or drives.</summary>
    void RideContext()
    {
        bool on = _ride is { IsMounted: true } or { IsDriving: true };
        if (on != ActiveContexts.IsOn(InputContext.Mounted)) { if (on) ActiveContexts.Enter(InputContext.Mounted); else ActiveContexts.Exit(InputContext.Mounted); }
    }

    /// <summary>The ride keys. E: a portal under the feet first, then dismount, step down, push a stuck cart, mount the nearest animal, or climb on the nearest cart's bench. True when the event was used.</summary>
    bool RideInput(InputEvent e)
    {
        if (_ride == null) return false;
        if (e is InputEventKey && Actions.Pressed(e, "world.act"))
        {
            if (_rec.UsePortal()) return true;
            var r = _ride;
            if (r.DrivenVehicle is { Stuck: true }) { _rec.RideCommand(RideCode.Push); return true; }
            if (r.IsMounted || r.IsDriving || r.Busy || r.NearestAnimal() != null) { _rec.RideCommand(RideCode.Interact); AfterRideCommand(); return true; }
            if (r.NearestVehicle() != null) { _rec.RideCommand(RideCode.Drive); return true; }
            return true;
        }
        if (Actions.Pressed(e, "mounted.walk_toggle")) { _walkToggle = !_walkToggle; return true; }
        if (Actions.Pressed(e, "world.cart_hitch")) { _rec.RideCommand(_ride.NearestVehicle() is { Team.Count: > 0 } && _ride.NearestAnimal(4000) == null ? RideCode.Unhitch : RideCode.Hitch); return true; }
        if (Actions.Pressed(e, "world.cart_unload")) { _rec.RideCommand(RideCode.Unload); return true; }
        if (Actions.Pressed(e, "world.cart_reload")) { _rec.RideCommand(RideCode.Reload); return true; }
        if (Actions.Pressed(e, "world.cart_dig")) { _rec.RideCommand(RideCode.Dig); return true; }
        return false;
    }

    void AfterRideCommand() { }

    /// <summary>Per frame: draws the mount, the cart and the animals standing about, lifts the hero onto the horse or the bench, and updates the bars.</summary>
    void RideRender(HeroSnapshot h, Vector2 feet)
    {
        if (_ride == null || _rideSprites == null) return;
        RideContext();
        RideSprites.HeroSeat seat;
        lock (_ride.Gate) seat = _rideSprites.Sync(_ride, h.Facing, h.Moving, feet, GetProcessDeltaTime(), h.Z == _viewZ);   // P5-G: the tick worker's vehicle pass writes under the same lock
        _heroSprite.Lift = seat.Seated ? seat.Lift : 0f;
        if (seat.Seated) _heroSprite.Pose(seat.Facing, "ride", 0);                      // sitting astride, side view; the horse's and cart's sprites are side-on too
        lock (_ride.Gate) _rideHud?.Update(_ride, _world.Clock.Now.Seconds);
        if (_world.RideMsgSerial != _rideMsg) { _rideMsg = _world.RideMsgSerial; _router?.ShowToast(L.F(_world.LastRideKey, _world.LastRideArg)); }
    }

    /// <summary>Dev start (<c>--shot-ride horse|cart|wagon|stuck|cart_beside|wagon_beside</c>): a palfrey beside the hero, mounted at once for <c>horse</c>; a cart with a loaded bed and a horse for the cart shots.</summary>
    void DevRide(string what)
    {
        var r = _ride!; var (x, y) = _world.WorldMm;
        var palfrey = r.Breeds.Get("horse_palfrey");
        if (what.StartsWith("horse"))
        {
            var a = r.Animals.Create(palfrey, Ravensguard.Sim.Core.EntityId.None, 900, 0, x + 1500, y);
            if (what == "horse") { a.XMm = x; a.YMm = y; a.State = AnimalState.Ridden; r.Mounted = a.Id; }
        }
        else if (what.EndsWith("_beside"))                                                   // the hero on foot, level with the vehicle's seat, beside the vehicle and its team (screenshots for scale)
            BesideCart(r, r.Templates.Get(what.StartsWith("wagon") ? "ox_wagon" : "cart"), x, y, what.StartsWith("wagon") ? 4 : 1);
        else
        {
            var a = r.Animals.Create(r.Breeds.Get(what == "wagon" || what == "stuck" ? "ox" : "horse_draught"), Ravensguard.Sim.Core.EntityId.None, 900, 0, x, y);
            var b = r.Animals.Create(r.Breeds.Get(what == "wagon" || what == "stuck" ? "ox" : "horse_draught"), Ravensguard.Sim.Core.EntityId.None, 900, 0, x, y);
            var cart = r.Vehicles.Create(r.Templates.Get(what == "wagon" || what == "stuck" ? "ox_wagon" : "cart"), Ravensguard.Sim.Core.EntityId.None, x, y);
            r.Vehicles.Hitch(cart, a); if (cart.Team.Count < r.Vehicles.StatsOf(cart).HitchSlots) r.Vehicles.Hitch(cart, b);
            ushort oats = r.Goods.IdOf("oats");
            cart.Cargo.CreateUpTo(oats, what == "stuck" ? 700_000 : 300_000, Ravensguard.Sim.Goods.CreateReason.Other);
            r.PlaceHero(x, y); r.Driving = cart.Id; cart.Driven = true;
            if (what == "stuck") { r.SurfaceOverride = Surface.MudRoad; cart.Stuck = true; }
        }
    }
}
