using Ravensguard.Content;
using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Naval;

namespace Ravensguard.Sim.Battle;

/// <summary>
/// P8-LW-3 on a battle opening (live-war 3.3): when a side's army owns war wagons or chariots (finished vehicles of the ride book whose owner is one of the side's <see cref="BattleSideSpec.VehicleOwners"/>: a warband's lord,
/// a host's baggage or its commander), they come onto the field with it. The wagons are laid behind the side's line and the <see cref="FortLayer"/> is attached (the AI rings them against horse; a field work, so it refuses an
/// assault's walls); each chariot is raised with the two nearest-to-first men of its side as driver and fighter and a <see cref="ChariotLayer"/> carries the cars. Nothing is drawn at random; sides and vehicles go in id order.
/// A battle with no ride book on its opening, or no such vehicle, is untouched.
/// </summary>
public static class BattleVehicles
{
    /// <summary>What was put on the field (null layers when none).</summary>
    public sealed record Result(FortLayer? Fort, ChariotLayer? Chariots);

    public static Result Attach(BattleBubble b, BattleOpening o)
    {
        if (o.Rides is not { } rides || b.W is not { } w) return new Result(null, null);
        var db = w.Content.Db;
        string wagon = (db.All<WagonRulesDef>().OrderBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault() ?? new WagonRulesDef { Id = "wagon_rules" }).Template;
        string car = (db.All<ChariotRulesDef>().OrderBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault() ?? new ChariotRulesDef { Id = "chariot_rules" }).Template;
        var sides = o.Sides.Where(s => s.VehicleOwners.Count > 0).OrderBy(s => s.Team).ToList();
        if (sides.Count == 0) return new Result(null, null);
        (int X, int Y)? Centre(int team) { var f = w.Fighters.Where(x => x.Team == team && x.Alive).ToList(); return f.Count == 0 ? null : ((int)f.Average(x => x.X), (int)f.Average(x => x.Y)); }
        FortLayer? fort = null; ChariotLayer? chariots = null; var crewed = new HashSet<int>();
        // the fort: the side with the most wagons rings them (one fort a fight)
        int bestTeam = -1; List<VehicleRow> bestWagons = [];
        foreach (var s in o.NoFort ? [] : sides)
        {
            var wagons = rides.Vehicles.Rows.Where(v => v.Finished && v.Design == wagon && s.VehicleOwners.Contains(v.Owner)).OrderBy(v => v.Id).ToList();
            if (wagons.Count > bestWagons.Count) { bestWagons = wagons; bestTeam = s.Team; }
        }
        if (bestWagons.Count > 0 && Centre(bestTeam) is { } own)
        {
            var foe = w.Fighters.Where(x => x.Team != bestTeam && x.Alive).ToList();
            int dx = foe.Count == 0 ? -1 : Math.Sign(own.X - (int)foe.Average(x => x.X)); if (dx == 0) dx = 1;
            for (int i = 0; i < bestWagons.Count; i++) { bestWagons[i].XMm = own.X + dx * (20_000 + (i / 2) * 6_000); bestWagons[i].YMm = own.Y + (i % 2 == 0 ? 1 : -1) * (3_000 + (i / 2) * 2_000); }
            var wf = new WagonFort(db, rides, w.Seed) { OriginX = o.OriginMm.X, OriginY = o.OriginMm.Y };
            fort = new FortLayer(w, wf, bestTeam);
            if (fort.Attach()) fort.Wagons.AddRange(bestWagons); else fort = null;
        }
        // the chariots: each takes its side's first two men not yet on a car
        foreach (var s in sides)
        {
            var cars = rides.Vehicles.Rows.Where(v => v.Finished && v.Design == car && s.VehicleOwners.Contains(v.Owner)).OrderBy(v => v.Id).ToList();
            if (cars.Count == 0 || Centre(s.Team) is not { } mid) continue;
            var men = w.Fighters.Where(x => x.Team == s.Team && x.Active && x.Person != 0 && !crewed.Contains(x.Id)).OrderBy(x => x.Id).ToList();
            chariots ??= new ChariotLayer(w, new ChariotBook(db, rides, w.Seed));
            for (int i = 0; i < cars.Count && men.Count >= 2 * (i + 1); i++)
            {
                var driver = men[2 * i]; var fighter = men[2 * i + 1]; crewed.Add(driver.Id); crewed.Add(fighter.Id);
                var v = cars[i]; v.XMm = mid.X; v.YMm = mid.Y + (i - cars.Count / 2) * 4_000;
                if (chariots.Book.Raise(v, new EntityId(driver.Person), new EntityId(fighter.Person), spear: true) is { } c) chariots.Add(c, s.Team, driver.Id, fighter.Id, new EntityId(driver.Person), new EntityId(fighter.Person));
            }
        }
        return new Result(fort, chariots);
    }
}
