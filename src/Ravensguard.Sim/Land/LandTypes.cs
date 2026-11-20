using System.Security.Cryptography;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Land;

/// <summary>What a deed is for (land spec 3.1). <see cref="Fief"/> is reserved for Phase 6; <see cref="FoundingCharter"/> is the charter deed of spec 4a (decided with the user, Nov 2026). Append only.</summary>
public enum HoldingKind : byte { Lot, Parcel, Building, Stall, Claim, Fief, FoundingCharter }

/// <summary>On what terms a deed is held. <see cref="Burgage"/> is a town plot owned for a burgage rent; <see cref="Leasehold"/> and <see cref="Tenancy"/> are held of a lessor. Append only.</summary>
public enum Tenure : byte { Freehold, Burgage, Leasehold, Tenancy, Customary }

/// <summary>How a deed came to its holder (the chronicle cause of the change). Append only.</summary>
public enum DeedCause : byte { Generated, Bought, Inherited, Granted, Claimed, Forfeited, Leased, LeaseEnded, Sold, Evicted, Lapsed,
    /// <summary>P5-H: fell to the lord with no heir (inheritance 4.1 rule 2).</summary>
    Escheated }

public enum ClaimState : byte { Open, Deeded, Lapsed, Pulled }
public enum ImprovementKind : byte { Dwelling, SownAcre }
public enum CharterState : byte { Granted, Fulfilled, Lapsed }
/// <summary>Who grants a founding charter: the ruler of a realm, or the lord of a fief (a landholding faction).</summary>
public enum GrantorKind : byte { Realm, Fief }

/// <summary>
/// One held thing (land spec 3.1): a lot, a parcel, a building, a market stall, a claim or a founding charter, who holds it and on what terms. Deeds that generation implies are derived
/// (<see cref="LandRegistry.Build"/>); a deed is stored in the <see cref="LandBook"/> only once play has changed it. <see cref="HolderPurse"/> says which ledger purse kind the holder pays from.
/// </summary>
public sealed class Deed
{
    public EntityId Id;
    public HoldingKind Kind;
    /// <summary>Lot index, parcel index, stall index or claim/charter serial (see <see cref="LandIds"/>).</summary>
    public long Subject;
    public int Settlement = -1;
    public EntityId Holder;
    public PurseKind HolderPurse;
    public Tenure Tenure;
    /// <summary>The fief whose lord receives the dues and gets an escheat; -1 for none (a town without a lord, wild land).</summary>
    public int Lord = -1;
    public long DuesMitesPerSeason;
    public int LeaseEndDay = -1;
    public EntityId Lessor;
    public PurseKind LessorPurse;
    public byte ArrearsSeasons;
    /// <summary>Owed and unpaid dues (mites); after <c>ArrearsDebtSeasons</c> seasons the sum is a debt (<see cref="InDebt"/>).</summary>
    public long OwedMites;
    public bool InDebt;
    /// <summary>The day the lessor's notice runs out and the lease ends (-1 none).</summary>
    public int NoticeDay = -1;
    public int AcquiredDay;
    public DeedCause Cause;
    /// <summary>The holder's asking price when he offers the deed for sale (mites, 0 not for sale).</summary>
    public long AskMites;
    /// <summary>A lease renews at its end unless the lessee gave it up.</summary>
    public bool Renew = true;
    /// <summary>The ground of a lot deed in region metres (zero for other kinds).</summary>
    public RectM Rect;

    public bool IsLease => Tenure is Tenure.Leasehold or Tenure.Tenancy;
    public Deed Clone() => (Deed)MemberwiseClone();
}

/// <summary>One change of the land book (spec 3.2), in order: the history the Ledger and the chronicle show, and what a save keeps beside the deeds it produced.</summary>
public readonly record struct DeedChange(int Day, EntityId Deed, EntityId NewHolder, Tenure NewTenure, long Dues, int LeaseEnd, DeedCause Cause);

/// <summary>A staked claim on wild land (spec 4.4): its acre tiles (ascending, connected), the banner, its licence or squatting, its improvements and the deed it became.</summary>
public sealed class Claim
{
    public int Serial;
    public EntityId Claimant;
    public PurseKind ClaimantPurse;
    public int[] Tiles = [];
    public int BannerX, BannerY;
    public int StakeDay;
    /// <summary>The fief whose waste this is (-1: outside every fief, free and unprotected).</summary>
    public int Fief = -1;
    public bool Licensed, Squatting;
    public long LicenceMites;
    public ClaimState State;
    public int Improvements;
    public int DwellingDay = -1;
    public int SownAcres;
    public EntityId DeedId;
    public int EndDay = -1;
}

/// <summary>A founding charter (spec 4a): grantor and grantee, the site, the deadline and the households needed, dues, service, the fee, and its state.</summary>
public sealed class Charter
{
    public int Serial;
    public GrantorKind GrantorKind;
    /// <summary>The realm's history index or the fief's index.</summary>
    public int Grantor;
    public EntityId Grantee;
    public PurseKind GranteePurse;
    public int[] Tiles = [];
    public int CenterX, CenterY;
    /// <summary>The settlement nearest the site: settlers are counted there until the place is a settlement of its own (Phase 6 zoning).</summary>
    public int Anchor = -1;
    public int GrantDay, SettleByDay, MinHouseholds;
    public long DuesMitesPerYear, FeeMites;
    public int ServiceMen, ServiceDays;
    public CharterState State;
    /// <summary>Settled households (ids), in the order they came.</summary>
    public List<EntityId> Households { get; } = [];
    public EntityId GrantEvent, DeedId;
    public int FulfilDay = -1, LapseDay = -1, VillageDay = -1;
}

/// <summary>Deed ids (spec 3.1: <c>IdKind.Other</c>, serial base 0x73 &lt;&lt; 40): a generated deed's id is a function of (kind, region, subject); claims, charters and leases take a serial of the book.</summary>
public static class LandIds
{
    public const ulong Base = 0x73UL << 40;
    /// <summary>The id slot of deeds numbered by the book's serial (leases of parcels and houses, claims, charters).</summary>
    public const int SerialSlot = 7;

    public static EntityId Of(HoldingKind kind, int region, long subject) => EntityId.MakeIn(IdKind.Other, region, Base | ((ulong)kind << 36) | ((ulong)subject & ((1UL << 36) - 1)));
    public static EntityId Serial(int region, int serial) => EntityId.MakeIn(IdKind.Other, region, Base | ((ulong)SerialSlot << 36) | (uint)serial);
    public static long LotSubject(int settlement, int lot) => (long)settlement * 65536 + lot;
    public static (int Settlement, int Lot) SplitLot(long subject) => ((int)(subject / 65536), (int)(subject % 65536));
    public static long StallSubject(int settlement, int stall) => (long)settlement * 256 + stall;
}

/// <summary>
/// The saved state of land for the home region (the optional "land" save section; absent while empty so a world that never touched land keeps its bytes): every deed play has changed,
/// the change log, claims, charters, the hero's standing with grantors, and the serial counter. Everything else is derived from the generated region and the people (<see cref="LandRegistry"/>).
/// </summary>
public sealed class LandBook
{
    public int NextSerial = 1;
    /// <summary>Deeds play has made or changed, by id (ascending): the result of replaying <see cref="Changes"/> over the derived set.</summary>
    public SortedDictionary<EntityId, Deed> Deeds { get; } = [];
    public List<DeedChange> Changes { get; } = [];
    public List<Claim> Claims { get; } = [];
    public List<Charter> Charters { get; } = [];
    /// <summary>The petitioner's standing with a grantor (key <c>kind &lt;&lt; 32 | index</c>); absent is 0. Phase 6 politics will move it; Phase 5 only reads it.</summary>
    public SortedDictionary<long, int> Standing { get; } = [];
    /// <summary>Distraint taken at evictions, mites at bid, total (spec 4.3).</summary>
    public long DistrainedMites;

    public bool IsEmpty => NextSerial == 1 && Deeds.Count == 0 && Changes.Count == 0 && Claims.Count == 0 && Charters.Count == 0 && Standing.Count == 0 && DistrainedMites == 0;

    public static long StandingKey(GrantorKind k, int index) => ((long)k << 32) | (uint)index;
    public int StandingWith(GrantorKind k, int index) => Standing.TryGetValue(StandingKey(k, index), out int v) ? v : 0;

    public Claim? ClaimOf(int serial) { foreach (var c in Claims) if (c.Serial == serial) return c; return null; }
    public Charter? CharterOf(int serial) { foreach (var c in Charters) if (c.Serial == serial) return c; return null; }

    /// <summary>Hash of the whole book (the "LandHash" of the hash trail, overview 8.9).</summary>
    public string ComputeHash()
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var b8 = new byte[8];
        void L(long v) { System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(b8, v); h.AppendData(b8); }
        L(NextSerial); L(DistrainedMites); L(Deeds.Count);
        foreach (var d in Deeds.Values) HashDeed(d, L);
        L(Changes.Count);
        foreach (var c in Changes) { L(c.Day); L((long)c.Deed.Value); L((long)c.NewHolder.Value); L((long)c.NewTenure); L(c.Dues); L(c.LeaseEnd); L((long)c.Cause); }
        L(Claims.Count);
        foreach (var c in Claims)
        {
            L(c.Serial); L((long)c.Claimant.Value); L((long)c.ClaimantPurse); L(c.BannerX); L(c.BannerY); L(c.StakeDay); L(c.Fief); L(c.Licensed ? 1 : 0); L(c.Squatting ? 1 : 0); L(c.LicenceMites);
            L((long)c.State); L(c.Improvements); L(c.DwellingDay); L(c.SownAcres); L((long)c.DeedId.Value); L(c.EndDay); L(c.Tiles.Length); foreach (int t in c.Tiles) L(t);
        }
        L(Charters.Count);
        foreach (var c in Charters)
        {
            L(c.Serial); L((long)c.GrantorKind); L(c.Grantor); L((long)c.Grantee.Value); L((long)c.GranteePurse); L(c.CenterX); L(c.CenterY); L(c.Anchor); L(c.GrantDay); L(c.SettleByDay); L(c.MinHouseholds);
            L(c.DuesMitesPerYear); L(c.FeeMites); L(c.ServiceMen); L(c.ServiceDays); L((long)c.State); L((long)c.GrantEvent.Value); L((long)c.DeedId.Value); L(c.FulfilDay); L(c.LapseDay); L(c.VillageDay);
            L(c.Tiles.Length); foreach (int t in c.Tiles) L(t);
            L(c.Households.Count); foreach (var hh in c.Households) L((long)hh.Value);
        }
        L(Standing.Count); foreach (var (k, v) in Standing) { L(k); L(v); }
        return Convert.ToHexString(h.GetHashAndReset());
    }

    internal static void HashDeed(Deed d, Action<long> L)
    {
        L((long)d.Id.Value); L((long)d.Kind); L(d.Subject); L(d.Settlement); L((long)d.Holder.Value); L((long)d.HolderPurse); L((long)d.Tenure); L(d.Lord); L(d.DuesMitesPerSeason); L(d.LeaseEndDay);
        L((long)d.Lessor.Value); L((long)d.LessorPurse); L(d.ArrearsSeasons); L(d.OwedMites); L(d.InDebt ? 1 : 0); L(d.NoticeDay); L(d.AcquiredDay); L((long)d.Cause); L(d.AskMites); L(d.Renew ? 1 : 0);
        L(d.Rect.X); L(d.Rect.Y); L(d.Rect.W); L(d.Rect.H);
    }
}
