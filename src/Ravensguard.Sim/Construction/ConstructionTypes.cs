using System.Security.Cryptography;
using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;

namespace Ravensguard.Sim.Construction;

// P5-B: hands-on construction (docs/specs/phase5/build-mode-and-construction.md 3.2, 3.3). Integer only; every walk is in id or (z, y, x) order.

public enum SiteState : byte { Active, Paused, Finished, Abandoned }

/// <summary>Why a site is not working (spec 3.2); <see cref="Owner"/> is the only one a person sets, the others clear themselves when the cause goes.</summary>
public enum PauseReason : byte { None, NoMaterials, NoWages, Rain, Night, Owner }

public enum GhostState : byte { Planned, Blocked, Ready, InProgress, Built, Cancelled }

public enum GhostBlock : byte { None, Support, Materials, Under }

/// <summary>Which layer of the cell a piece takes: the structure (wall, floor, roof...) or the fixture on the floor (furniture, workstations).</summary>
public enum GhostSlot : byte { Structure, Fixture }

/// <summary>A ghost raises a piece, or takes a built one down (Deconstruct).</summary>
public enum GhostWork : byte { Build, Deconstruct }

/// <summary>How a crew member is paid: a follower on the hero's payroll builds without a site wage; hired hands are labourers or skilled (production's classes).</summary>
public enum CrewClass : byte { Follower, Labourer, Skilled }

/// <summary>A building site (spec 3.2): the owner, who pays, the deed it stands on, the materials pile, the crew and why it stops.</summary>
public sealed class ConstructionSite
{
    public EntityId Id;
    public int Region;
    public EntityId Owner;
    public PurseKind OwnerKind = PurseKind.Person;
    public EntityId Payer;
    public PurseKind PayerKind = PurseKind.Person;
    public EntityId Deed;
    /// <summary>The cell the pile stands on (region metres, level).</summary>
    public int PileX, PileY, PileZ;
    /// <summary>The plan it was placed from ("" for free placement).</summary>
    public string Blueprint = "";
    public SiteState State;
    public PauseReason Pause;
    public byte Priority;
    public long CreatedSec;
    /// <summary>The last game day whose 06:00 escrow ran (-1 none).</summary>
    public long EscrowDay = -1;
    /// <summary>Coin the crew was paid in all, and refunded to the payer.</summary>
    public long PaidMites, RefundedMites;
    public List<CrewMember> Crew { get; } = [];
    /// <summary>The ghosts placed for it so far (ascending id; built and cancelled ones leave).</summary>
    public int GhostsPlaced, PiecesBuilt;

    public EntityId Pile => ConstructionIds.PileOf(Id);
    public EntityId Rubble => ConstructionIds.RubbleOf(Id);
    public EntityId Escrow => ConstructionIds.EscrowOf(Id);
    public bool Open => State is SiteState.Active or SiteState.Paused;
}

/// <summary>One person on a site's crew.</summary>
public sealed class CrewMember
{
    public EntityId Person;
    /// <summary>Row in the person table (-1 for the hero or a person of another region).</summary>
    public int PersonIndex = -1;
    public CrewClass Class;
    public long WageMitesPerDay;
    /// <summary>The ghost the member works on (none between pieces).</summary>
    public EntityId Ghost;
    /// <summary>A haul job of the site the member carries (none when building).</summary>
    public EntityId Job;
    /// <summary>Seconds worked today (paid at the day's end), and seconds toward the next skill use.</summary>
    public long WorkedTodaySec, SkillSec;
    /// <summary>Days in a row the payer could not put up the wage.</summary>
    public int ShortDays;
    /// <summary>The day the member was paid last and the coin paid in all.</summary>
    public long PaidMites;
}

/// <summary>A piece to be built or taken down (spec 3.2). Progress is in standard worker-microseconds (one second of a skill-50 worker with the tools is 1,000,000).</summary>
public sealed class Ghost
{
    public EntityId Id;
    public EntityId Site;
    public int X, Y, Z;
    public string Piece = "";
    public GhostSlot Slot;
    public byte Rot;
    public GhostWork Work;
    public GhostState State;
    public GhostBlock Block;
    public long WorkDone;
    public long WorkNeed;
    /// <summary>The materials are in the ghost's own store (work started).</summary>
    public bool Materialised;
    public EntityId Builder;
    public int BuiltSkill;
    public byte Priority;
    public long PlacedSec;

    public long CellKey => ConstructionIds.CellKey(X, Y, Z);
    public long SlotKey => ConstructionIds.SlotKey(X, Y, Z, Slot);
    public bool Live => State is GhostState.Planned or GhostState.Blocked or GhostState.Ready or GhostState.InProgress;
}

/// <summary>A built piece of a construction (the cells of the structure record, spec 3.3): the piece, its hit points and the skill it was built with.</summary>
public sealed class BuiltCell
{
    public int X, Y, Z;
    public GhostSlot Slot;
    public string Piece = "";
    public int Hp;
    public int BuiltSkill;
    public EntityId Site;
    public long BuiltSec;
    public long SlotKey => ConstructionIds.SlotKey(X, Y, Z, Slot);
}

/// <summary>Cells that lost their support and groan before they fall (decided with the user: about 10 game seconds of groaning and dust, then the fall).</summary>
public sealed class Groan
{
    public ulong EventId;
    public long StartSec, FallSec;
    /// <summary>Structure cells (cell keys, ascending) that will fall unless they are held up again in time.</summary>
    public long[] Cells = [];
    /// <summary>Who or what took the support away.</summary>
    public EntityId Cause;
    public string Why = "";
}

/// <summary>A store that stands on a cell: a site's pile and rubble, a chest (spec 4.5: what stands on a fallen floor lands below).</summary>
public readonly record struct PlacedStore(EntityId Owner, int X, int Y, int Z);

/// <summary>Ids of construction (IdKind.Other, serial base 0x74 &lt;&lt; 40 in the region's serial space, beside the land's 0x73): a slot in bits 36-39, the book's serial below.</summary>
public static class ConstructionIds
{
    public const ulong Base = 0x74UL << 40;
    public const int SiteSlot = 1, GhostSlotId = 2, PileSlot = 3, RubbleSlot = 4, EscrowSlot = 5, EventSlot = 6, ChestSlot = 7;

    public static EntityId Make(int region, int slot, ulong serial) => EntityId.MakeIn(IdKind.Other, region, Base | ((ulong)slot << 36) | (serial & ((1UL << 36) - 1)));
    public static int SlotOf(EntityId id) => (int)((id.Serial >> 36) & 0xF);
    public static ulong SerialOf(EntityId id) => id.Serial & ((1UL << 36) - 1);
    public static bool IsConstruction(EntityId id) => id.Kind == IdKind.Other && (id.Serial & (0xFFUL << 40)) == Base;
    public static EntityId PileOf(EntityId site) => Make(site.Region, PileSlot, SerialOf(site));
    public static EntityId RubbleOf(EntityId site) => Make(site.Region, RubbleSlot, SerialOf(site));
    public static EntityId EscrowOf(EntityId site) => Make(site.Region, EscrowSlot, SerialOf(site));

    private const int Bias = 1 << 19;
    /// <summary>(z, y, x) ordered cell key (the same packing as RoomWorld's).</summary>
    public static long CellKey(int x, int y, int z) => ((long)(z + 128) << 42) | ((long)(y + Bias) << 21) | (long)(x + Bias);
    public static (int X, int Y, int Z) Unpack(long k) => ((int)(k & 0x1FFFFF) - Bias, (int)((k >> 21) & 0x1FFFFF) - Bias, (int)(k >> 42) - 128);
    public static long SlotKey(int x, int y, int z, GhostSlot slot) => CellKey(x, y, z) * 2 + (long)slot;
}

/// <summary>
/// The saved state of construction for the home region (the optional "construction" save section, absent while empty, no format bump): sites, ghosts, the built cells and their
/// structure ids, pending groans, stores on cells, the engine's clock and the hero's hours toward his next skill gain. RoomWorld rooms and facts are rebuilt from the cells at load.
/// </summary>
public sealed class ConstructionBook
{
    public ulong NextSerial = 1;
    /// <summary>The game second up to which crews have worked (whole quanta) and groans were resolved.</summary>
    public long AdvancedSec = -1;
    /// <summary>The game second up to which the hero's own work has been credited (a work command never counts the same seconds twice).</summary>
    public long HeroWorkedTo = -1;
    public SortedDictionary<EntityId, ConstructionSite> Sites { get; } = [];
    public SortedDictionary<EntityId, Ghost> Ghosts { get; } = [];
    /// <summary>Built cells by slot key ((z, y, x, slot) order).</summary>
    public SortedDictionary<long, BuiltCell> Built { get; } = [];
    public List<Groan> Groans { get; } = [];
    public SortedDictionary<EntityId, PlacedStore> Placed { get; } = [];
    /// <summary>RoomWorld structure ids as saved (structure id to its cell keys), and the next id (roomdetector-v2 Notes: step 2 saves them).</summary>
    public List<(int Id, long[] Cells)> Structures { get; set; } = [];
    public int NextStructureId = 1;
    /// <summary>The hero's attended seconds toward the next skill use, per skill byte.</summary>
    public SortedDictionary<byte, long> HeroSkillSec { get; } = [];
    /// <summary>Grams of each good built into pieces and taken out of them again (the construction's own mass audit, BC-21).</summary>
    public SortedDictionary<ushort, long> MaterialisedG { get; } = [];
    public SortedDictionary<ushort, long> DematerialisedG { get; } = [];
    /// <summary>Claims a recognised dwelling has improved (claim serial, structure id), so an improvement counts once.</summary>
    public SortedSet<long> Improved { get; } = [];

    public bool IsEmpty => NextSerial == 1 && Sites.Count == 0 && Ghosts.Count == 0 && Built.Count == 0 && Groans.Count == 0 && Placed.Count == 0;

    // ---- presentation only (never saved, never hashed) ----
    /// <summary>The newest view for the main thread (swapped whole at a barrier).</summary>
    public volatile ConstructionView? View;
    /// <summary>Recent events for the street view (a piece built, a groan, a fall), newest last; bounded.</summary>
    public List<ConstructionEvent> Events { get; } = [];
    public long EventSerial;

    public ulong Take() => NextSerial++;

    /// <summary>The SiteHash of the daily trail (overview 8.9): sites, ghosts, built cells, groans and placed stores in id order.</summary>
    public string ComputeHash()
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var b8 = new byte[8];
        void L(long v) { System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(b8, v); h.AppendData(b8); }
        void S(string s) { L(s.Length); foreach (char c in s) L(c); }
        L((long)NextSerial); L(AdvancedSec); L(HeroWorkedTo); L(NextStructureId);
        L(Sites.Count);
        foreach (var s in Sites.Values)
        {
            L((long)s.Id.Value); L((long)s.Owner.Value); L((long)s.OwnerKind); L((long)s.Payer.Value); L((long)s.PayerKind); L((long)s.Deed.Value); L(s.PileX); L(s.PileY); L(s.PileZ);
            S(s.Blueprint); L((long)s.State); L((long)s.Pause); L(s.Priority); L(s.CreatedSec); L(s.EscrowDay); L(s.PaidMites); L(s.RefundedMites); L(s.GhostsPlaced); L(s.PiecesBuilt);
            L(s.Crew.Count);
            foreach (var c in s.Crew) { L((long)c.Person.Value); L(c.PersonIndex); L((long)c.Class); L(c.WageMitesPerDay); L((long)c.Ghost.Value); L((long)c.Job.Value); L(c.WorkedTodaySec); L(c.SkillSec); L(c.ShortDays); L(c.PaidMites); }
        }
        L(Ghosts.Count);
        foreach (var g in Ghosts.Values)
        {
            L((long)g.Id.Value); L((long)g.Site.Value); L(g.X); L(g.Y); L(g.Z); S(g.Piece); L((long)g.Slot); L(g.Rot); L((long)g.Work); L((long)g.State); L((long)g.Block);
            L(g.WorkDone); L(g.WorkNeed); L(g.Materialised ? 1 : 0); L((long)g.Builder.Value); L(g.BuiltSkill); L(g.Priority); L(g.PlacedSec);
        }
        L(Built.Count);
        foreach (var b in Built.Values) { L(b.X); L(b.Y); L(b.Z); L((long)b.Slot); S(b.Piece); L(b.Hp); L(b.BuiltSkill); L((long)b.Site.Value); L(b.BuiltSec); }
        L(Groans.Count);
        foreach (var g in Groans) { L((long)g.EventId); L(g.StartSec); L(g.FallSec); L((long)g.Cause.Value); S(g.Why); L(g.Cells.Length); foreach (var c in g.Cells) L(c); }
        L(Placed.Count);
        foreach (var p in Placed.Values) { L((long)p.Owner.Value); L(p.X); L(p.Y); L(p.Z); }
        L(Structures.Count);
        foreach (var (id, cells) in Structures) { L(id); L(cells.Length); foreach (var c in cells) L(c); }
        foreach (var (k, v) in HeroSkillSec) { L(k); L(v); }
        foreach (var (k, v) in MaterialisedG) { L(k); L(v); }
        foreach (var (k, v) in DematerialisedG) { L(k); L(v); }
        foreach (var k in Improved) L(k);
        return Convert.ToHexString(h.GetHashAndReset());
    }
}

public enum ConstructionEventKind : byte { Built, Removed, Groan, Fell, Paused, Finished, HeroFell }

/// <summary>Something the street view shows once (a piece flashes, dust falls, a toast): presentation only.</summary>
public readonly record struct ConstructionEvent(long Serial, ConstructionEventKind Kind, long Sec, int X, int Y, int Z, string Piece, EntityId Site);
