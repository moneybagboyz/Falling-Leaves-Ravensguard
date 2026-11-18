using MessagePack;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Farming;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Save;

/// <summary>
/// The "farming" section: field and parcel columns, the year's cell climate, herds, the running tally and the year summaries (farming spec 2). Stores farming
/// owns (barns, parish granaries, fodder) are rows of the economy section. All arrays are in index order, so equal state gives equal bytes.
/// </summary>
[MessagePackObject]
public sealed class FarmingSave
{
    [Key(0)] public int Version { get; set; } = 1;
    [Key(1)] public int Year { get; set; }
    [Key(2)] public int FieldCount { get; set; }
    [Key(3)] public int ParcelCount { get; set; }
    [Key(4)] public int SettlementCount { get; set; }

    // fields
    [Key(5)] public byte[] FRegion { get; set; } = [];
    [Key(6)] public byte[] FFertility { get; set; } = [];
    [Key(7)] public byte[] FSlot { get; set; } = [];
    [Key(8)] public byte[] FSownFertility { get; set; } = [];
    [Key(9)] public byte[] FFlags { get; set; } = [];
    [Key(10)] public byte[] FCrop { get; set; } = [];
    [Key(11)] public byte[] FStage { get; set; } = [];
    [Key(12)] public short[] FSownDay { get; set; } = [];
    [Key(13)] public short[] FDamage { get; set; } = [];
    [Key(14)] public int[] FSeedG { get; set; } = [];
    [Key(15)] public int[] FYieldG { get; set; } = [];
    [Key(16)] public int[] FSheafG { get; set; } = [];

    // parcels
    [Key(17)] public int[] PHolder { get; set; } = [];
    [Key(18)] public int[] PLandlord { get; set; } = [];
    [Key(19)] public byte[] POwnerKind { get; set; } = [];
    [Key(20)] public byte[] PLease { get; set; } = [];
    [Key(21)] public short[] PShareBp { get; set; } = [];
    [Key(22)] public int[] PCoinMites { get; set; } = [];
    [Key(23)] public long[] PCoinArrears { get; set; } = [];
    [Key(24)] public byte[] PSlot { get; set; } = [];
    [Key(25)] public int[] PProgress { get; set; } = [];
    [Key(26)] public int[] PCarry { get; set; } = [];
    [Key(27)] public int[] PWeeded { get; set; } = [];
    [Key(28)] public int[] PHarvestSet { get; set; } = [];
    [Key(29)] public int[] PReapDone { get; set; } = [];
    [Key(30)] public int[] PBindDone { get; set; } = [];
    [Key(31)] public short[] PHired { get; set; } = [];

    // climate
    [Key(36)] public byte[] FrostKillDay { get; set; } = [];
    [Key(37)] public byte[] WinterBadDays { get; set; } = [];
    [Key(38)] public ulong[] StormMask { get; set; } = [];
    [Key(40)] public bool[] Blight { get; set; } = [];

    // herds
    [Key(41)] public int[] Sheep { get; set; } = [];
    [Key(42)] public int[] Oxen { get; set; } = [];
    [Key(43)] public int[] Meadow { get; set; } = [];
    [Key(44)] public int[] HayPpm { get; set; } = [];
    [Key(45)] public int[] HayCarry { get; set; } = [];
    [Key(46)] public bool[] Grazed { get; set; } = [];

    // tally and summaries
    [Key(47)] public long[] Tally { get; set; } = [];
    [Key(48)] public long[] LostG { get; set; } = [];
    [Key(49)] public int[] LostAcres { get; set; } = [];
    [Key(50)] public int[] SumYear { get; set; } = [];
    [Key(51)] public long[] SumValues { get; set; } = [];      // 8 per summary: harvest, seed, threshed, tithe, rent, sown, lost acres, unharvested
    [Key(52)] public long[] SumGross0 { get; set; } = [];
    [Key(53)] public long[] SumLostG { get; set; } = [];       // LossCause count per summary
    [Key(54)] public int[] SumLostAcres { get; set; } = [];
    [Key(55)] public byte[] FSheafCrop { get; set; } = [];
    [Key(56)] public long[] SeedDebt { get; set; } = [];
    [Key(57)] public int[] Cows { get; set; } = [];

    public static FarmingSave Capture(FarmingState s)
    {
        var f = s.Fields; var p = s.Parcels; var c = s.Climate; var l = s.Livestock;
        var o = new FarmingSave
        {
            Year = s.Year, FieldCount = f.Count, ParcelCount = p.Count, SettlementCount = l.Sheep.Length,
            FRegion = (byte[])f.Region.Clone(), FFertility = (byte[])f.Fertility.Clone(), FSlot = (byte[])f.Slot.Clone(), FSownFertility = (byte[])f.SownFertility.Clone(), FFlags = (byte[])f.Flags.Clone(),
            FCrop = f.Crop.Select(x => (byte)x).ToArray(), FStage = f.Stage.Select(x => (byte)x).ToArray(), FSownDay = (short[])f.SownDay.Clone(), FDamage = (short[])f.DamagePermille.Clone(),
            FSeedG = (int[])f.SeedG.Clone(), FYieldG = (int[])f.YieldG.Clone(), FSheafG = (int[])f.SheafG.Clone(), FSheafCrop = (byte[])f.SheafCrop.Clone(),
            PHolder = (int[])p.Holder.Clone(), PLandlord = (int[])p.Landlord.Clone(), POwnerKind = p.OwnerKind.Select(x => (byte)x).ToArray(), PLease = p.Lease.Select(x => (byte)x).ToArray(),
            PShareBp = (short[])p.ShareBp.Clone(), PCoinMites = (int[])p.CoinMitesPerFieldYear.Clone(), PCoinArrears = (long[])p.CoinArrears.Clone(), PSlot = (byte[])p.Slot.Clone(),
            PProgress = (int[])p.ProgressPpm.Clone(), PCarry = (int[])p.Carry.Clone(), PWeeded = (int[])p.WeededPpm.Clone(), PHarvestSet = (int[])p.HarvestSet.Clone(),
            PReapDone = (int[])p.ReapDone.Clone(), PBindDone = (int[])p.BindDone.Clone(), PHired = (short[])p.HiredHands.Clone(),
            FrostKillDay = (byte[])c.FrostKillDay.Clone(), WinterBadDays = (byte[])c.WinterBadDays.Clone(), StormMask = (ulong[])c.StormMask.Clone(), Blight = (bool[])c.BlightCulture.Clone(),
            Sheep = (int[])l.Sheep.Clone(), Oxen = (int[])l.Oxen.Clone(), Cows = (int[])l.Cows.Clone(), Meadow = (int[])l.MeadowAcres.Clone(), HayPpm = (int[])l.HayPpm.Clone(), HayCarry = (int[])l.HayCarry.Clone(), Grazed = (bool[])l.Grazed.Clone(),
            Tally = [s.HarvestG, s.SeedSownG, s.ThreshedG, s.TitheG, s.RentG, s.Gross0G, s.SownAcres, s.UnharvestedAcres],
            LostG = (long[])s.LostG.Clone(), LostAcres = (int[])s.LostAcres.Clone(), SeedDebt = (long[])s.SeedDebtG.Clone(),
        };
        o.SumYear = s.Summaries.Select(x => x.Year).ToArray();
        o.SumValues = s.Summaries.SelectMany(x => new[] { x.HarvestG, x.SeedSownG, x.ThreshedG, x.TitheG, x.RentG, x.SownAcres, x.LostAcres, x.UnharvestedAcres }).ToArray();
        o.SumGross0 = s.Summaries.Select(x => x.Gross0G).ToArray();
        o.SumLostG = s.Summaries.SelectMany(x => x.LostG).ToArray(); o.SumLostAcres = s.Summaries.SelectMany(x => x.LostAcresByCause).ToArray();
        return o;
    }

    /// <summary>Rebuilds the state; throws <see cref="SaveException"/> when the section does not fit the region it is loaded against.</summary>
    public FarmingState Restore(RegionData region)
    {
        try
        {
            if (FieldCount != region.Fields.Length || ParcelCount != region.Parcels.Count || SettlementCount != region.Settlements.Count)
                throw new SaveException("The farming section does not match the region.");
            var s = new FarmingState(FieldCount, ParcelCount, SettlementCount, SeedDebt.Length) { Year = Year };
            Array.Copy(SeedDebt, s.SeedDebtG, SeedDebt.Length);
            var f = s.Fields;
            for (int i = 0; i < FieldCount; i++) { f.Id[i] = region.Fields[i].Id; f.Tile[i] = region.Fields[i].Tile; f.Parcel[i] = region.Fields[i].Parcel; }
            Array.Copy(FRegion, f.Region, FieldCount); Array.Copy(FFertility, f.Fertility, FieldCount); Array.Copy(FSlot, f.Slot, FieldCount); Array.Copy(FSownFertility, f.SownFertility, FieldCount);
            Array.Copy(FFlags, f.Flags, FieldCount); Array.Copy(FSownDay, f.SownDay, FieldCount); Array.Copy(FDamage, f.DamagePermille, FieldCount);
            Array.Copy(FSeedG, f.SeedG, FieldCount); Array.Copy(FYieldG, f.YieldG, FieldCount); Array.Copy(FSheafG, f.SheafG, FieldCount); Array.Copy(FSheafCrop, f.SheafCrop, FieldCount);
            for (int i = 0; i < FieldCount; i++) { f.Crop[i] = (Crop)FCrop[i]; f.Stage[i] = (FieldStage)FStage[i]; }
            var p = s.Parcels;
            Array.Copy(PHolder, p.Holder, ParcelCount); Array.Copy(PLandlord, p.Landlord, ParcelCount); Array.Copy(PShareBp, p.ShareBp, ParcelCount); Array.Copy(PCoinMites, p.CoinMitesPerFieldYear, ParcelCount);
            Array.Copy(PCoinArrears, p.CoinArrears, ParcelCount); Array.Copy(PSlot, p.Slot, ParcelCount); Array.Copy(PProgress, p.ProgressPpm, PProgress.Length); Array.Copy(PCarry, p.Carry, PCarry.Length);
            Array.Copy(PWeeded, p.WeededPpm, ParcelCount); Array.Copy(PHarvestSet, p.HarvestSet, ParcelCount); Array.Copy(PReapDone, p.ReapDone, ParcelCount); Array.Copy(PBindDone, p.BindDone, ParcelCount);
            Array.Copy(PHired, p.HiredHands, ParcelCount);
            for (int i = 0; i < ParcelCount; i++) { p.OwnerKind[i] = (OwnerKind)POwnerKind[i]; p.Lease[i] = (LeaseKind)PLease[i]; }
            var c = s.Climate;
            Array.Copy(FrostKillDay, c.FrostKillDay, CellCropClimate.Cells); Array.Copy(WinterBadDays, c.WinterBadDays, CellCropClimate.Cells); Array.Copy(StormMask, c.StormMask, CellCropClimate.Cells);
            Array.Copy(Blight, c.BlightCulture, c.BlightCulture.Length);
            var l = s.Livestock;
            Array.Copy(Sheep, l.Sheep, SettlementCount); Array.Copy(Oxen, l.Oxen, SettlementCount); if (Cows.Length == SettlementCount) Array.Copy(Cows, l.Cows, SettlementCount); Array.Copy(Meadow, l.MeadowAcres, SettlementCount);
            Array.Copy(HayPpm, l.HayPpm, SettlementCount); Array.Copy(HayCarry, l.HayCarry, SettlementCount); Array.Copy(Grazed, l.Grazed, SettlementCount);
            s.HarvestG = Tally[0]; s.SeedSownG = Tally[1]; s.ThreshedG = Tally[2]; s.TitheG = Tally[3]; s.RentG = Tally[4]; s.Gross0G = Tally[5]; s.SownAcres = Tally[6]; s.UnharvestedAcres = Tally[7];
            Array.Copy(LostG, s.LostG, s.LostG.Length); Array.Copy(LostAcres, s.LostAcres, s.LostAcres.Length);
            int causes = s.LostG.Length;
            for (int i = 0; i < SumYear.Length; i++)
            {
                var v = SumValues.AsSpan(i * 8, 8);
                s.Summaries.Add(new YearSummary(SumYear[i], v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], SumGross0[i], SumLostG.AsSpan(i * causes, causes).ToArray(), SumLostAcres.AsSpan(i * causes, causes).ToArray()));
            }
            return s;
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or InvalidCastException)
        {
            throw new SaveException("Corrupt 'farming' section in save.", ex);
        }
    }
}
