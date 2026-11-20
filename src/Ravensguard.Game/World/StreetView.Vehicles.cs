using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Live;

namespace Ravensguard.Game.World;

/// <summary>
/// P5-G (vehicle-designer 4.1-4.5, 6): the street view's vehicles beyond Phase 3's carts: the designer (Q > Design a vehicle, or Inspect a vehicle you stand by), work on an unfinished vehicle,
/// repair, launch, and the river boats: the water under a boat (depth and current from the waterway's width class, the ford's depth), the cells a boat may enter, boarding and landing with E.
/// </summary>
public partial class StreetView
{
    /// <summary>Wires the boats to the map (called once the ride book is attached).</summary>
    void AttachBoats()
    {
        if (_ride == null) return;
        _ride.WaterNow = BoatWaterHere;
        _world.Navigable = (x, y, z) =>
        {
            var g = _world.Map?.GroundAt(x, y, z) ?? GroundKind.Grass;
            if (g is not (GroundKind.Water or GroundKind.DeepWater or GroundKind.Ford)) return false;
            return _ride.DrivenVehicle is not { } b || g != GroundKind.Ford || BoatMath.Floats(BoatMath.Load(_ride.Vehicles.StatsOf(b), _ride.Vehicles.CargoKg(b), 85, _ride.Templates.Water).DraftMm, _ride.Templates.Water.FordDepth(RiverHighDays()), _ride.Templates.Water);
        };
    }

    int RiverHighDays() => 0;

    /// <summary>The waterway nearest a region point (metres), its width there and the downstream direction of that stretch; cached per 4 m cell.</summary>
    (int WidthCm, float Dx, float Dy)? _waterCache; long _waterKey = long.MinValue;
    (int WidthCm, float Dx, float Dy)? WaterAt(long xM, long yM)
    {
        long key = (xM / 4) * 1_000_003 + yM / 4;
        if (key == _waterKey) return _waterCache;
        _waterKey = key; _waterCache = null;
        if (Runtime?.State.Population.Region is not { } reg) return null;
        double best = 60.0 * 60;
        foreach (var w in reg.Waterways)
            for (int i = 1; i < w.Points.Length; i++)
            {
                var a = w.Points[i - 1]; var b = w.Points[i];
                double vx = b.X - a.X, vy = b.Y - a.Y, len2 = vx * vx + vy * vy; if (len2 <= 0) continue;
                double t = Math.Clamp(((xM - a.X) * vx + (yM - a.Y) * vy) / len2, 0, 1);
                double px = a.X + vx * t - xM, py = a.Y + vy * t - yM, d = px * px + py * py;
                if (d < best) { best = d; double l = Math.Sqrt(len2); _waterCache = (i < w.WidthCm.Length ? w.WidthCm[i] : 300, (float)(vx / l), (float)(vy / l)); }
            }
        return _waterCache;
    }

    /// <summary>The water under the hero's boat: depth and current by the width class (a ford its own depth), the current along the way the hero heads.</summary>
    BoatWater? BoatWaterHere()
    {
        if (_ride == null) return null;
        var h = _world.Snapshot.Hero;
        var g = _world.Map?.GroundAt(h.CellX, h.CellY, h.Z) ?? GroundKind.Grass;
        if (g is not (GroundKind.Water or GroundKind.DeepWater or GroundKind.Ford)) return null;
        var wt = _ride.Templates.Water; int high = RiverHighDays();
        var (xMm, yMm) = _world.WorldMm;
        var w = WaterAt(xMm / 1000, yMm / 1000);
        var (depth, current) = w is { } ww ? wt.River(ww.WidthCm, high) : (g == GroundKind.DeepWater ? 1800 : 900, 0);
        if (g == GroundKind.Ford) depth = wt.FordDepth(high);
        var (fx, fy) = h.Facing switch { Facing.North => (0f, -1f), Facing.South => (0f, 1f), Facing.West => (-1f, 0f), _ => (1f, 0f) };
        int along = w is { } w2 ? (int)Math.Round(current * (fx * w2.Dx + fy * w2.Dy)) : 0;
        return new BoatWater(depth, along, 0, 0, 0, w is { WidthCm: > 4000 }, high);
    }

    // ------------------------------------------------------------------ the Q menu

    void VehicleVerbs(List<QMenu.Verb> v)
    {
        if (_ride == null) return;
        v.Add(new(L.T("vd_q_head"), () => { }, Heading: true));
        v.Add(new(L.T("vd_q_design"), () => OpenDesigner(null)));
        // the rows, the sites and the parts are read under the ride book's lock: the tick worker's vehicle pass changes them (P5-I review, finding 6)
        lock (_ride.Gate)
        {
            var near = _ride.NearestVehicle(4000);
            if (near == null) return;
            var bp = _ride.Vehicles.DesignOf(near);
            v.Add(new(L.F("vd_q_inspect", bp.Def.Name), () => InspectVehicle(near)));
            if (!near.Finished && _ride.Works.Sites.ContainsKey(near.Id))
                v.Add(new(L.F("vd_q_work", bp.Def.Name), () => { long now = HeroNow(); IssueVehicle(VehicleDesk.Work(near.Id, now, now + 3600)); }, _heroLink == null ? L.T("MAP_NONE") : null));
            int worst = WorstPart(near);
            if (worst >= 0) v.Add(new(L.F("vd_q_repair", _ride.Templates.PartList[near.Parts[worst].Part].Name), () => IssueVehicle(VehicleDesk.Repair(near.Id, worst, HeroNow())), _heroLink == null ? L.T("MAP_NONE") : null));
            if (near.Water == WaterState.Slipway && near.Finished)
            {
                bool team = near.Team.Count > 0;
                v.Add(new(L.T("vd_q_launch"), () => IssueVehicle(VehicleDesk.Launch(near.Id, 4, team, HeroNow()))));      // the hero and the hands of the yard
            }
        }
    }

    void IssueVehicle(Ravensguard.Sim.Trade.HeroCommand c) { if (_heroLink == null) { _router?.ShowToast(L.T("vd_no_world")); return; } _heroLink.Issue(c); }

    int WorstPart(VehicleRow v)
    {
        int best = -1; long low = long.MaxValue;
        for (int i = 0; i < v.Parts.Count; i++)
        {
            var p = v.Parts[i]; if (p.State is PartState.Ghost or PartState.InProgress) continue;
            var d = _ride!.Templates.PartList[p.Part]; long pm = (long)p.HpMilli / Math.Max(1, d.Hp);
            if (pm < 1000 && pm < low) { low = pm; best = i; }
        }
        return best;
    }

    /// <summary>The vehicle panel (vehicle-designer 6): its parts and their condition, the cargo, the team, what needs repair; the site's shopping list while it is unfinished.</summary>
    void InspectVehicle(VehicleRow v)
    {
        if (_router == null || _ride == null) return;
        string title, body, design;
        lock (_ride.Gate) (title, body, design) = InspectText(v);   // the parts are the tick worker's too (P5-I review, finding 6)
        _router.ShowModal(title, body, (L.T("vd_q_design_this"), () => OpenDesigner(design)), (L.T("vd_close"), null));
    }

    (string Title, string Body, string Design) InspectText(VehicleRow v)
    {
        var bp = _ride!.Vehicles.DesignOf(v); var st = _ride.Vehicles.StatsOf(v);
        var sb = new System.Text.StringBuilder();
        sb.Append(L.F("vd_inspect_line", st.MassKg, _ride.Vehicles.CargoKg(v), st.CargoKg, v.Team.Count, st.MaxAnimals)).Append('\n');
        var groups = v.Parts.GroupBy(p => p.Part).OrderBy(g => g.Key);
        foreach (var g in groups)
        {
            var d = _ride.Templates.PartList[g.Key];
            int built = g.Count(p => p.State == PartState.Built), broken = g.Count(p => p.State == PartState.Broken), ghosts = g.Count(p => p.State is PartState.Ghost or PartState.InProgress);
            int worst = g.Where(p => p.State is PartState.Built or PartState.Broken).Select(p => (int)(p.HpMilli / Math.Max(1, d.Hp))).DefaultIfEmpty(1000).Min();
            sb.Append(L.F("vd_inspect_part", d.Name, built, broken, ghosts, worst / 10)).Append('\n');
        }
        if (!v.Finished) sb.Append(L.F("vd_inspect_wanted", string.Join(", ", VehicleSites.Wanted(v, _ride.Templates).GroupBy(x => x).Select(x => $"{x.Count()} {_ride.Templates.Parts[x.Key].Name}"))));
        return (bp.Def.Name, sb.ToString(), bp.Id);
    }

    /// <summary>Opens the designer on the run's catalog (the player's saved designs included); saving and building go through the hero's desk.</summary>
    public VehicleDesignerScreen? OpenDesigner(string? open)
    {
        if (_router == null || _ride == null) return null;
        var (x, y) = HeroRegionMm();
        int depth = WaterAt(x / 1000, y / 1000) is { } w ? _ride.Templates.Water.River(w.WidthCm, 0).DepthMm : 0;
        var scr = new VehicleDesignerScreen(_router, _ride.Templates, _ride.Breeds, c => { if (_heroLink == null) return false; _heroLink.Issue(c); return true; }, () => { var p = _world.WorldMm; return (p.XMm, p.YMm, HeroNow()); }, open, depth);
        _router.Push(scr);
        return scr;
    }

    // ------------------------------------------------------------------ dev and screenshots

    string DevVehicles(string cmd, string[] a)
    {
        var r = _ride; if (r == null) return "no vehicles here";
        var (x, y) = _world.WorldMm;
        switch (cmd)
        {
            case "designer": return OpenDesigner(a.Length > 0 ? a[0] : null) != null ? "designer opened" : "no designer";
            case "site":
            {
                var (ans, v) = VehicleSites.Order(r, a.Length > 0 ? a[0] : "ox_wagon", Ravensguard.Sim.Core.EntityId.None, x + 4000, y);
                if (v == null) return "site: " + ans;
                // half the parts fitted, a third more on the pile (the dev faucet)
                var econ = Runtime?.State.Economy; if (econ == null) return "site ordered (no economy for parts)";
                int n = 0; foreach (var part in VehicleSites.Wanted(v, r.Templates)) { if (n++ % 3 == 2) continue; VehicleSites.Deposit(r, econ.Items, v, econ.Items.Add(Runtime!.State.Ids, part, Ravensguard.Sim.Goods.ItemQuality.Common, Ravensguard.Sim.Core.EntityId.None, [new("oak", 1000, 1000)]), Ravensguard.Sim.Core.EntityId.None); }
                VehicleSites.Work(r, econ.Items, v, 9000, HeroNow());
                return $"an unfinished {r.Vehicles.DesignOf(v).Def.Name} beside you";
            }
            case "boat":
            {
                var bp = r.Templates.Find(a.Length > 0 ? a[0] : "barge"); if (bp == null) return "unknown boat";
                // the nearest water of the loaded window, with water on every side of it (a boat of three cells' beam floats there)
                var h = _world.Snapshot.Hero; int bx = -1, by = -1; long bd = long.MaxValue;
                bool Wet(int cx, int cy) => (_world.Map?.GroundAt(cx, cy, h.Z) ?? GroundKind.Grass) is GroundKind.Water or GroundKind.DeepWater;
                for (int cy = 2; cy < _world.Grid.Height - 2; cy++)
                    for (int cx = 2; cx < _world.Grid.Width - 2; cx++)
                    {
                        long d = (long)(cx - h.CellX) * (cx - h.CellX) + (long)(cy - h.CellY) * (cy - h.CellY);
                        if (d >= bd || !Wet(cx, cy) || !Wet(cx - 2, cy) || !Wet(cx + 2, cy) || !Wet(cx, cy - 2) || !Wet(cx, cy + 2)) continue;
                        bd = d; bx = cx; by = cy;
                    }
                if (bx < 0) return "no water in the loaded window";
                long wx = (_world.OriginX + bx) * 1000L + 500, wy = (_world.OriginY + by) * 1000L + 500;
                var v = r.Vehicles.Create(bp, Ravensguard.Sim.Core.EntityId.None, wx, wy);
                if (Runtime?.State.Economy is { } e) v.Cargo.CreateUpTo(e.Goods.IdOf("wheat"), bp.Stats.CargoKg * 300L, Ravensguard.Sim.Goods.CreateReason.Other);
                _world.TravelPlace(wx, wy, Facing.East); _world.SetInput(new MoveInput(0, 0, false));
                r.PlaceHero(wx, wy); r.Driving = v.Id; v.Driven = true;
                return $"{bp.Def.Name} on the water at {bx},{by}: you are aboard";
            }
            case "break":
            {
                var v = r.DrivenVehicle ?? r.NearestVehicle(); if (v == null) return "no vehicle";
                int i = v.Parts.FindIndex(p => r.Templates.PartList[p.Part].Tags.Contains("wheel") && p.State == PartState.Built);
                if (i < 0) return "no wheel"; r.Vehicles.Damage(v, i, int.MaxValue); return "a wheel broke";
            }
        }
        return "?";
    }

    /// <summary>Screenshot hook (<c>--ng-shot vehicles --ng-scene designer | wagon | diagonal | stuck | site | boat</c>).</summary>
    public string DebugVehicles(string scene)
    {
        var r = _ride; if (r == null) return "no ride";
        var (x, y) = _world.WorldMm;
        switch (scene)
        {
            case "designer":
            {
                var s = OpenDesigner(null); if (s == null) return "no designer";
                s.DebugLoad("ox_wagon"); s.DebugName("Market wagon");
                s.Model.RemoveTop(0, 3); s.Model.RemoveTop(0, 3); s.Model.RemoveTop(0, 3); s.Model.RemoveTop(0, 3); s.Model.RemoveTop(0, 3);
                s.DebugStack(0, 3, "axle_pivot", "wheel_spoked", "iron_tyre", "frame_plank", "cargo_bed");
                s.Select("axle_pivot");
                return "designer";
            }
            case "wagon": case "diagonal": case "stuck":
            {
                var des = new VehicleDesigner(r.Templates, r.Breeds); des.Load(r.Templates.Get("ox_wagon").Def); des.Id = "player.market_wagon"; des.Name = "Market wagon";
                for (int k = 0; k < 5; k++) des.RemoveTop(0, 3);
                des.PlaceStack(0, 3, ["axle_pivot", "wheel_spoked", "iron_tyre", "frame_plank", "cargo_bed"]);
                var designs = Runtime?.State.Designs ?? new Ravensguard.Sim.Construction.DesignBook(); designs.Put(des.Id, des.Text(), Ravensguard.Sim.Construction.DesignKind.Vehicle);
                r.AttachDesigns(designs);
                var bp = scene == "stuck" ? r.Templates.Get("ox_wagon") : r.Templates.Get(des.Id);
                bool drive = scene == "stuck";
                long vx = drive ? x : x + 5000, vy = drive ? y : y - 1000;
                var v = r.Vehicles.Create(bp, Ravensguard.Sim.Core.EntityId.None, vx, vy);
                for (int i = 0; i < 2; i++) r.Vehicles.Hitch(v, r.Animals.Create(r.Breeds.Get("ox"), Ravensguard.Sim.Core.EntityId.None, 900, 0, vx, vy));
                v.Cargo.CreateUpTo(r.Goods.IdOf("wheat"), 900_000, Ravensguard.Sim.Goods.CreateReason.Other);
                if (drive) { r.PlaceHero(x, y); r.Driving = v.Id; v.Driven = true; r.SurfaceOverride = Surface.MudRoad; v.Stuck = true; }
                // a second one beside it: the template ox wagon (east) next to the designed one, or the designed one on the diagonal
                RideSprites.DebugDir8 = scene == "diagonal" ? 1 : -1;
                if (scene == "wagon") { var t = r.Vehicles.Create(r.Templates.Get("ox_wagon"), Ravensguard.Sim.Core.EntityId.None, x + 5000, y + 3000); _ = t; }
                return scene;
            }
            case "river":
            {
                // the hero set down on the bank of the widest water near him (a river's middle point), so the window streams the river in
                if (Runtime?.State.Population.Region is not { } reg) return "no region";
                var (hx, hy) = HeroRegionMm(); long best = long.MaxValue; (int X, int Y) at = (0, 0);
                foreach (var w in reg.Waterways.Where(w => w.WidthCm.Length > 0 && w.WidthCm.Max() >= 600))
                    for (int i = 0; i < w.Points.Length; i++)
                    {
                        if (i >= w.WidthCm.Length || w.WidthCm[i] < 600) continue;
                        long dx = w.Points[i].X - hx / 1000, dy = w.Points[i].Y - hy / 1000, d = dx * dx + dy * dy;
                        if (d < best) { best = d; at = (w.Points[i].X, w.Points[i].Y); }
                    }
                if (best == long.MaxValue) return "no river in the region";
                DebugPlaceHero(at.X, at.Y + 12);
                return $"set down by the river at {at.X},{at.Y}";
            }
            case "site": return DevVehicles("site", ["ox_wagon"]);
            case "boat": return DevVehicles("boat", ["barge"]);
        }
        return "?";
    }
}
