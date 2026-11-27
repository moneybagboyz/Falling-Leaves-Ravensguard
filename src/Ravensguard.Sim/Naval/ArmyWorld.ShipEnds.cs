using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Naval;

namespace Ravensguard.Sim.Armies;

/// <summary>
/// P7-SH-D: the ships' fight rows in the hour and in the chronicle (ships-and-naval-war 4.6, 8). An hour with no row in the book does nothing. A ship that is taken or sinks is written as <c>ship_taken</c> or <c>ship_sunk</c>, with the raid's
/// entry as its Event cause when the ship was a raider's, else a root (cause kind <c>Naval</c>); the entry's id is kept in the ship's row.
/// </summary>
public sealed partial class ArmyWorld
{
    /// <summary>The ship fighting of this world (null: no ride book or no naval book yet).</summary>
    public ShipCombat? ShipsAt()
    {
        var sea = Sea; var rides = sea?.Rides(); var book = sea?.Peek();
        if (rides == null || book == null) return null;
        var c = new ShipCombat(rides, book, W.Seed);
        if (W.Economy is { } eco)
        {
            c.Led = eco.Ledger;
            c.CaptorPurse = who =>
            {
                int r = RowOf(who.Value);                                                       // a captor with a household takes the plunder into its purse, any other into a purse of its own id
                int hh = r >= 0 ? People.Household[r] : -1;
                return hh >= 0 && hh < eco.HouseholdPurse.Length ? eco.HouseholdPurse[hh] : eco.Ledger.Open(PurseKind.Treasury, who);
            };
        }
        return c;
    }

    /// <summary>The hour: what burns burns on, water comes into holed hulls, ships founder (no crew puts out a fire or bails in the hour: a fight does that live).</summary>
    public void ShipsHour(ArmyEnv e)
    {
        var sea = Sea; var book = sea?.Peek(); if (book == null || book.Ships.Count == 0) return;
        var c = ShipsAt(); if (c == null) return;
        RecordShipEnds(e, c.Step(3600, e.Hour * 3600L));
    }

    private static string ShipKindKey(string template) => template is "longship" or "cog" or "war_cog" ? "ch_sh_ship_" + template : "ch_sh_ship_other";

    /// <summary>Writes the chronicle entry of each ship that ended.</summary>
    public void RecordShipEnds(ArmyEnv e, IEnumerable<ShipEnd> ends)
    {
        var sea = Sea; var rides = sea?.Rides(); var book = sea?.Peek(); if (rides == null || book == null) return;
        foreach (var end in ends)
        {
            var v = rides.Vehicles.Find(new EntityId(end.Ship)); if (v == null) continue;
            var raid = book.Raids.Values.FirstOrDefault(r => r.Ships.Contains(end.Ship));
            var leader = new EntityId(raid == null ? 0 : raid.Raiders.Count > 0 ? raid.Raiders[0] : raid.Guards.Count > 0 ? raid.Guards[0] : 0);
            var master = end.Master != EntityId.None ? end.Master : leader;
            var place = PlaceId(raid?.Settlement ?? 0);
            string kind = ShipKindKey(rides.Vehicles.DesignOf(v).Id);
            string note = end.Kind == ShipEndKind.Taken ? "ch_note_sh_boarded" : end.Cause == "fire" ? "ch_note_sh_burnt" : "ch_note_sh_holed";
            var cause = new Cause(CauseKind.Naval, raid is { Entry: not 0 } ? new EntityId(raid.Entry) : null, "", note);
            var subjects = new List<EntityId>(); if (master != EntityId.None) subjects.Add(master); if (place != EntityId.None) subjects.Add(place);
            EntityId entry = end.Kind == ShipEndKind.Taken
                ? Chron(e, "ship_taken", "ch_sh_taken", [ChronParam.TextKey("ship", kind), ChronParam.Person("master", master), ChronParam.Person("captor", end.Captor), ChronParam.Place("place", place)], subjects, cause)
                : Chron(e, "ship_sunk", "ch_sh_sunk", [ChronParam.TextKey("ship", kind), ChronParam.Person("master", master), ChronParam.Place("place", place)], subjects, cause);
            if (book.Ships.TryGetValue(end.Ship, out var row)) { row.Entry = entry.Value; book.Touch(); }
        }
    }
}
