using Ravensguard.Sim.Building;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Holdings;

// P6-B6, holdings 3.6: room ownership and who sleeps where. A room's owner (a household or a business) and a bed's person are kept by cell key, so they survive the room being detected again.
public sealed partial class HoldingsWorld
{
    /// <summary>Crafting's service (the NPC apprentices of the labour market start through it); null without crafting.</summary>
    public Ravensguard.Sim.Crafting.CraftService? Craft { get; set; }

    /// <summary>The season's NPC apprentices (HW-10): see <see cref="Crafters.TakeApprentices"/>.</summary>
    public int TakeApprentices(int day) => Craft == null ? 0 : Crafters.TakeApprentices(Craft, Pop, Ids, day, id => id == HeroId);

    /// <summary>The hero's id (the masters the labour market names never include him).</summary>
    public EntityId HeroId { get; set; }
    /// <summary>The world's id allocator (apprenticeship ids).</summary>
    public IdAllocator Ids { get; set; } = new();

    private static long RoomKey(RoomV2 r) => RoomWorld.Key(r.Origin.X, r.Origin.Y, r.Z);

    /// <summary>The household or business that owns a room (none when nobody does).</summary>
    public EntityId OwnerOf(RoomV2 room) => Book.RoomOwner.TryGetValue(RoomKey(room), out var o) ? new EntityId(o) : EntityId.None;

    /// <summary>Makes a household or business the owner of a room.</summary>
    public void Own(RoomV2 room, EntityId owner) { if (owner == EntityId.None) Book.RoomOwner.Remove(RoomKey(room)); else Book.RoomOwner[RoomKey(room)] = owner.Value; }

    /// <summary>Assigns the bed at a cell to a person (it is his own: nobody else is given it); an assignment elsewhere of the same person is moved.</summary>
    public void AssignBed(int x, int y, int z, EntityId person)
    {
        foreach (var k in Book.BedAssigned.Where(kv => kv.Value == person.Value).Select(kv => kv.Key).ToList()) Book.BedAssigned.Remove(k);
        Book.BedAssigned[RoomWorld.Key(x, y, z)] = person.Value;
    }

    /// <summary>The cell of a person's own bed, or null when he has none.</summary>
    public (int X, int Y, int Z)? BedOf(EntityId person)
    {
        foreach (var (k, v) in Book.BedAssigned) if (v == person.Value) return RoomWorld.Unpack(k);
        return null;
    }

    /// <summary>
    /// Where a guest sleeps in a structure: the first bed (in room, then cell order) nobody has been given; null when every bed is someone's. A member of the household sleeps in his own bed
    /// (<see cref="BedOf"/>), never in a guest's.
    /// </summary>
    public (int X, int Y, int Z)? GuestBed(RoomWorld rooms, ConstructionBedLookup beds, int structure)
    {
        var f = rooms.Facts(structure); if (f == null) return null;
        foreach (var r in f.Rooms.Where(r => r.Indoor).OrderBy(r => r.Z))
            foreach (var (x, y) in r.Cells)
                if (beds(x, y, r.Z) && !Book.BedAssigned.ContainsKey(RoomWorld.Key(x, y, r.Z))) return (x, y, r.Z);
        return null;
    }
}

/// <summary>Whether a cell holds a bed (a built fixture whose piece has beds).</summary>
public delegate bool ConstructionBedLookup(int x, int y, int z);
