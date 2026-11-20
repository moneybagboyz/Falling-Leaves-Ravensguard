using System.Security.Cryptography;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Holdings;

/// <summary>Where a clearing stands: trees coming down and stumps coming out, the acre cleared and a field of its holder, or sown (the claim has grown).</summary>
public enum ClearingState : byte { Working, Cleared, Sown, Abandoned }

/// <summary>
/// An acre of a claim being cleared (holdings 3.1, the Phase 5 carry-over S6): <see cref="NeedMilli"/> worker-milli-hours in all (90 worker-hours an acre, Phase 5 OV-3's figure), the first 60% felling
/// the acre's trees one by one (each tree's logs and firewood kept in the clearing's store, the wood row drawn down), the rest grubbing the stumps. The hero works it by hand and hires hands; at the end
/// the acre is a field of the hero's household. Saved in the optional "holdings" section.
/// </summary>
public sealed class Clearing
{
    public int Serial;
    /// <summary>The claim's serial in the land book.</summary>
    public int Claim;
    /// <summary>The region tile of the acre.</summary>
    public int Tile;
    public EntityId Owner;
    public int Trees, Felled, Grubbed;
    public long NeedMilli, DoneMilli, HeroMilli;
    /// <summary>Grams of the tile's wood row when the work began (each tree takes an equal share).</summary>
    public long WoodG0;
    public long LogsG, FirewoodG;
    /// <summary>The hired hands (person ids), paid a day's wage each working day out of the hero's purse.</summary>
    public List<EntityId> Hands { get; } = [];
    /// <summary>The day and the milli-hours the hero has worked on it that day (a day's cap).</summary>
    public int HeroDay = -1; public long HeroDayMilli;
    public int StartDay, ClearedDay = -1, SownDay = -1, LastDay = -1;
    public int Parcel = -1;
    public ClearingState State;
    public string Crop = "";
    /// <summary>Wages owed to the hands that the hero could not pay.</summary>
    public int ShortDays;

    public bool Open => State == ClearingState.Working;
    public string StoreKey => "clearing:" + Serial;
}

/// <summary>A tract of coppice cut: the row's stock was drawn down on <see cref="CutDay"/> and stands full again on <see cref="DueDay"/> (7 game years).</summary>
public sealed class CoppiceCut { public int Tile; public int CutDay, DueDay; }

/// <summary>A mare in foal: the year she was covered, the sire and the day she foals.</summary>
public sealed class Pregnancy { public ulong Mare, Sire; public int Year, DueDay; }

/// <summary>
/// The saved state of holdings (the optional "holdings" section, absent while empty, so a world that never cleared an acre, coppiced a stand or bred a horse keeps its bytes): the clearings,
/// the coppice cuts, the mares in foal and the years each mare was last tried.
/// </summary>
public sealed class HoldingsBook
{
    public int NextSerial = 1;
    public List<Clearing> Clearings { get; } = [];
    public List<CoppiceCut> Coppice { get; } = [];
    public List<Pregnancy> Foaling { get; } = [];
    /// <summary>The year each mare was last tried (stream 0x6E1: once a game year): mare id, year, ascending.</summary>
    public SortedDictionary<ulong, int> Tried { get; } = [];
    /// <summary>Rooms with an owner (a household or business id): the room's lowest cell key (<c>RoomWorld.Key</c>) to the owner's id value. Beds assigned to a person: the bed's cell key to the person's id.</summary>
    public SortedDictionary<long, ulong> RoomOwner { get; } = [];
    public SortedDictionary<long, ulong> BedAssigned { get; } = [];
    /// <summary>The hero's heavy-work day (HW-9): the day, the milli-hours worked, whether the noon meal was eaten and whether there was nothing to eat.</summary>
    public int HeavyDay = -1; public long HeavyMilli; public bool Fed, Hungry;
    public long FoalsBorn, LogsFelledG;

    public bool IsEmpty => HeavyDay < 0 && RoomOwner.Count == 0 && BedAssigned.Count == 0 && NextSerial == 1 && Clearings.Count == 0 && Coppice.Count == 0 && Foaling.Count == 0 && Tried.Count == 0 && FoalsBorn == 0 && LogsFelledG == 0;

    public Clearing? Get(int serial) { foreach (var c in Clearings) if (c.Serial == serial) return c; return null; }

    public string ComputeHash()
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var b8 = new byte[8];
        void L(long v) { System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(b8, v); h.AppendData(b8); }
        L(NextSerial); L(FoalsBorn); L(LogsFelledG);
        L(Clearings.Count);
        foreach (var c in Clearings)
        {
            L(c.Serial); L(c.Claim); L(c.Tile); L((long)c.Owner.Value); L(c.Trees); L(c.Felled); L(c.Grubbed); L(c.NeedMilli); L(c.DoneMilli); L(c.HeroMilli); L(c.WoodG0); L(c.LogsG); L(c.FirewoodG);
            L(c.HeroDay); L(c.HeroDayMilli); L(c.StartDay); L(c.ClearedDay); L(c.SownDay); L(c.LastDay); L(c.Parcel); L((long)c.State); L(c.ShortDays);
            L(c.Hands.Count); foreach (var p in c.Hands) L((long)p.Value);
        }
        L(Coppice.Count); foreach (var c in Coppice) { L(c.Tile); L(c.CutDay); L(c.DueDay); }
        L(Foaling.Count); foreach (var f in Foaling) { L((long)f.Mare); L((long)f.Sire); L(f.Year); L(f.DueDay); }
        L(Tried.Count); foreach (var (k, v) in Tried) { L((long)k); L(v); }
        L(HeavyDay); L(HeavyMilli); L(Fed ? 1 : 0); L(Hungry ? 1 : 0);
        L(RoomOwner.Count); foreach (var (k, v) in RoomOwner) { L(k); L((long)v); }
        L(BedAssigned.Count); foreach (var (k, v) in BedAssigned) { L(k); L((long)v); }
        return Convert.ToHexString(h.GetHashAndReset());
    }
}
