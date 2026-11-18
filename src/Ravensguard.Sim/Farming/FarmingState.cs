using System.Security.Cryptography;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Farming;

/// <summary>
/// Struct-of-arrays field state (farming spec 2.1), index = Phase 1 field index. Phase 1 <c>Region.Fields</c> stays the generation record;
/// this table is the running state and never writes back to it.
/// </summary>
public sealed class FieldTable
{
    public int Count { get; private set; }
    public EntityId[] Id;
    public int[] Tile, Parcel;
    public byte[] Region, Fertility, Slot, SownFertility, Flags, SheafCrop;
    public Crop[] Crop;
    public FieldStage[] Stage;
    public short[] SownDay, DamagePermille;
    public int[] SeedG, YieldG, SheafG;

    public FieldTable(int n)
    {
        Count = n;
        Id = new EntityId[n]; Tile = new int[n]; Parcel = new int[n]; Region = new byte[n]; Fertility = new byte[n]; Slot = new byte[n]; SownFertility = new byte[n]; Flags = new byte[n]; SheafCrop = new byte[n];
        Crop = new Crop[n]; Stage = new FieldStage[n]; SownDay = new short[n]; DamagePermille = new short[n]; SeedG = new int[n]; YieldG = new int[n]; SheafG = new int[n];
    }

    /// <summary>Grows every column to <paramref name="n"/> rows (new rows zero); background growth appends fields.</summary>
    public void Grow(int n)
    {
        if (n <= Count) return;
        Array.Resize(ref Id, n); Array.Resize(ref Tile, n); Array.Resize(ref Parcel, n); Array.Resize(ref Region, n); Array.Resize(ref Fertility, n); Array.Resize(ref Slot, n);
        Array.Resize(ref SownFertility, n); Array.Resize(ref Flags, n); Array.Resize(ref SheafCrop, n); Array.Resize(ref Crop, n); Array.Resize(ref Stage, n);
        Array.Resize(ref SownDay, n); Array.Resize(ref DamagePermille, n); Array.Resize(ref SeedG, n); Array.Resize(ref YieldG, n); Array.Resize(ref SheafG, n);
        Count = n;
    }

    public bool Has(int f, FieldFlags flag) => (Flags[f] & (byte)flag) != 0;
    public void Set(int f, FieldFlags flag) => Flags[f] |= (byte)flag;
    public void Clear(int f, FieldFlags flag) => Flags[f] &= (byte)~(byte)flag;
}

/// <summary>Per-parcel working state (farming spec 2.2). Index = Phase 1 parcel index.</summary>
public sealed class ParcelWork
{
    public const int ProgressTasks = 8;
    public int Count { get; private set; }
    /// <summary>Household index that works the parcel (-1 none) and the household that owns the rent (-1 none).</summary>
    public int[] Holder, Landlord;
    public OwnerKind[] OwnerKind;
    public LeaseKind[] Lease;
    public short[] ShareBp;
    public int[] CoinMitesPerFieldYear;
    public long[] CoinArrears;
    /// <summary>Slot of the parcel's fields this year (rotates yearly).</summary>
    public byte[] Slot;
    public int[] ProgressPpm, Carry;
    public int[] WeededPpm, HarvestSet, ReapDone, BindDone;
    public short[] HiredHands;

    public ParcelWork(int n)
    {
        Count = n;
        Holder = new int[n]; Landlord = new int[n]; OwnerKind = new OwnerKind[n]; Lease = new LeaseKind[n]; ShareBp = new short[n]; CoinMitesPerFieldYear = new int[n];
        CoinArrears = new long[n]; Slot = new byte[n]; ProgressPpm = new int[n * ProgressTasks]; Carry = new int[n * ProgressTasks];
        WeededPpm = new int[n]; HarvestSet = new int[n]; ReapDone = new int[n]; BindDone = new int[n]; HiredHands = new short[n];
    }

    /// <summary>Grows every column to <paramref name="n"/> parcels (new rows zero, no holder).</summary>
    public void Grow(int n)
    {
        if (n <= Count) return;
        int old = Count;
        Array.Resize(ref Holder, n); Array.Resize(ref Landlord, n); Array.Resize(ref OwnerKind, n); Array.Resize(ref Lease, n); Array.Resize(ref ShareBp, n); Array.Resize(ref CoinMitesPerFieldYear, n);
        Array.Resize(ref CoinArrears, n); Array.Resize(ref Slot, n); Array.Resize(ref ProgressPpm, n * ProgressTasks); Array.Resize(ref Carry, n * ProgressTasks);
        Array.Resize(ref WeededPpm, n); Array.Resize(ref HarvestSet, n); Array.Resize(ref ReapDone, n); Array.Resize(ref BindDone, n); Array.Resize(ref HiredHands, n);
        for (int i = old; i < n; i++) { Holder[i] = -1; Landlord[i] = -1; }
        Count = n;
    }

    public int Progress(int parcel, FarmTask t) => ProgressPpm[parcel * ProgressTasks + (int)t];
    public void ResetYear(int parcel)
    {
        Array.Clear(ProgressPpm, parcel * ProgressTasks, ProgressTasks);
        // the thresh carry survives the year boundary (unthreshed sheaves stay); every other carry resets
        int keep = Carry[parcel * ProgressTasks + (int)FarmTask.Thresh];
        Array.Clear(Carry, parcel * ProgressTasks, ProgressTasks);
        Carry[parcel * ProgressTasks + (int)FarmTask.Thresh] = keep;
        WeededPpm[parcel] = 0; HarvestSet[parcel] = 0; ReapDone[parcel] = 0; BindDone[parcel] = 0; HiredHands[parcel] = 0;
    }
}

/// <summary>Per continental cell of the region (16 x 16 = 256) crop climate of the running year (farming spec 2.3; one year, not a 2-year ring: see the notes).</summary>
public sealed class CellCropClimate
{
    public const int Cells = 256;
    public byte[] FrostKillDay = new byte[Cells], WinterBadDays = new byte[Cells];
    public ulong[] StormMask = new ulong[Cells];
    /// <summary>Blight this year, per culture index (0..255).</summary>
    public bool[] BlightCulture = new bool[256];
    public int ColdSummerCultureMask;
}

/// <summary>Herds that couple to farming (oxen pull, sheep graze the fallow, hay feeds both), per settlement (farming spec 3.6; resources spec).</summary>
public sealed class LivestockState
{
    public int[] Sheep, Oxen, Cows, MeadowAcres, HayPpm, HayCarry;
    public bool[] Grazed;
    public LivestockState(int settlements)
    {
        Sheep = new int[settlements]; Oxen = new int[settlements]; Cows = new int[settlements]; MeadowAcres = new int[settlements]; HayPpm = new int[settlements]; HayCarry = new int[settlements]; Grazed = new bool[settlements];
    }
}

/// <summary>Everything farming owns that is saved state: field and parcel tables, year climate, tally, herds and the year summaries.</summary>
public sealed class FarmingState
{
    public int Year { get; set; }
    public FieldTable Fields { get; private set; }
    public ParcelWork Parcels { get; private set; }
    public CellCropClimate Climate { get; } = new();
    public LivestockState Livestock { get; }
    /// <summary>This year's running tally: harvest, seed, tithe, rent, threshed, and loss by cause.</summary>
    public long HarvestG, SeedSownG, ThreshedG, TitheG, RentG, Gross0G, SownAcres, UnharvestedAcres, GleanedG;
    public long[] LostG = new long[Enum.GetValues<LossCause>().Length];
    public int[] LostAcres = new int[Enum.GetValues<LossCause>().Length];
    public List<YearSummary> Summaries { get; } = [];
    /// <summary>Seed corn each household owes its lord or parish, in grams including the 20% (farming spec 7: the lord lends seed at S1 and S3, repaid in kind next harvest).</summary>
    public long[] SeedDebtG;

    public FarmingState(int fields, int parcels, int settlements, int households = 0)
    {
        Fields = new FieldTable(fields); Parcels = new ParcelWork(parcels); Livestock = new LivestockState(settlements); SeedDebtG = new long[households];
    }

    /// <summary>Background growth appended fields and parcels to the region: the tables grow to match (new rows zero, parcels without a holder).</summary>
    public void GrowTo(int fields, int parcels) { Fields.Grow(fields); Parcels.Grow(parcels); }

    public void AddLoss(LossCause c, long grams, int acres) { LostG[(int)c] += grams; LostAcres[(int)c] += acres; }

    /// <summary>Digest of every saved column for determinism and round-trip tests.</summary>
    public string ComputeHash()
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var b8 = new byte[8];
        void L(long v) { System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(b8, v); h.AppendData(b8); }
        L(Year);
        var f = Fields;
        for (int i = 0; i < f.Count; i++)
        {
            L((long)f.Id[i].Value); L(f.Tile[i]); L(f.Parcel[i]); L(f.Region[i]); L(f.Fertility[i]); L(f.Slot[i]); L(f.SownFertility[i]); L(f.Flags[i]); L(f.SheafCrop[i]); L((long)f.Crop[i]); L((long)f.Stage[i]);
            L(f.SownDay[i]); L(f.DamagePermille[i]); L(f.SeedG[i]); L(f.YieldG[i]); L(f.SheafG[i]);
        }
        var p = Parcels;
        for (int i = 0; i < p.Count; i++)
        {
            L(p.Holder[i]); L(p.Landlord[i]); L((long)p.OwnerKind[i]); L((long)p.Lease[i]); L(p.ShareBp[i]); L(p.CoinMitesPerFieldYear[i]); L(p.CoinArrears[i]); L(p.Slot[i]);
            L(p.WeededPpm[i]); L(p.HarvestSet[i]); L(p.ReapDone[i]); L(p.BindDone[i]); L(p.HiredHands[i]);
        }
        foreach (int v in p.ProgressPpm) L(v);
        foreach (int v in p.Carry) L(v);
        var c = Climate;
        for (int i = 0; i < CellCropClimate.Cells; i++) { L(c.FrostKillDay[i]); L(c.WinterBadDays[i]); L((long)c.StormMask[i]); }
        foreach (bool b in c.BlightCulture) L(b ? 1 : 0);
        L(c.ColdSummerCultureMask);
        var l = Livestock;
        for (int i = 0; i < l.Sheep.Length; i++) { L(l.Sheep[i]); L(l.Oxen[i]); if (l.Cows[i] != 0) L(l.Cows[i]); L(l.MeadowAcres[i]); L(l.HayPpm[i]); L(l.HayCarry[i]); L(l.Grazed[i] ? 1 : 0); }
        foreach (long v in SeedDebtG) L(v);
        L(HarvestG); L(SeedSownG); L(ThreshedG); L(TitheG); L(RentG); L(Gross0G); L(SownAcres); L(UnharvestedAcres);
        foreach (long v in LostG) L(v); foreach (int v in LostAcres) L(v);
        foreach (var s in Summaries)
        {
            L(s.Year); L(s.HarvestG); L(s.SeedSownG); L(s.ThreshedG); L(s.TitheG); L(s.RentG); L(s.SownAcres); L(s.LostAcres); L(s.UnharvestedAcres); L(s.Gross0G);
            foreach (long v in s.LostG) L(v); foreach (int v in s.LostAcresByCause) L(v);
        }
        return Convert.ToHexString(h.GetHashAndReset());
    }
}
