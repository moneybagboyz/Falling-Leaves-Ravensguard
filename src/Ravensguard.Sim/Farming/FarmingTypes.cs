using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Farming;

/// <summary>Visible and task-gating state of a field (farming spec 2.1).</summary>
public enum FieldStage : byte { Stubble, Ploughed, Sown, Growing, Ripe, Cut, Threshed }

/// <summary>Per-parcel farm tasks (farming spec 3.2). The first <see cref="ProgressTaskCount"/> are tracked as ppm progress; <see cref="Thresh"/> counts acres; <see cref="Hay"/> is per settlement.</summary>
public enum FarmTask : byte { PloughSpring, SowSpring, Weed, Reap, BindCart, PloughFallow, SowWinter, Thresh, Hay }

public enum LeaseKind : byte { None, Share, Coin }

[Flags] public enum FieldFlags : byte { None = 0, Resown = 1, Burned = 2, Grazed = 4, Lost = 8, FallowCredited = 16, Bound = 32 }

/// <summary>Concrete causes of yield loss, for the chronicle and the year tally (farming spec 3.3, 3.5).</summary>
public enum LossCause : byte { Drought, ColdSummer, Winterkill, Frost, Hail, Blight, WetHarvest, Overdue, Weeds, Trampling, Fire, Waterlogged }

/// <summary>Kinds of farm store a household or settlement owns (ids built by <see cref="FarmIds"/>).</summary>
public enum FarmStoreKind : byte { Barn = 1, ParishGranary = 2, Fodder = 3 }

/// <summary>Grain and legume kinds (content crop <c>kind</c>).</summary>
public enum CropKind : byte { None, WinterGrain, SpringGrain, SpringLegume }

/// <summary>Where the harvest of one field goes, in grams (farming spec 3.4). <c>Tithe + Rent + Holder == YieldG</c>.</summary>
public readonly record struct HarvestSplit(long YieldG, long SeedG, long NetG, long TitheG, long RentG, long HolderG);

/// <summary>Per-year totals kept for the year summary (grams of grain unless noted).</summary>
public sealed record YearSummary(int Year, long HarvestG, long SeedSownG, long ThreshedG, long TitheG, long RentG, long SownAcres, long LostAcres, long UnharvestedAcres,
    long Gross0G, long[] LostG, int[] LostAcresByCause);

public static class FarmIds
{
    private const ulong Tag = 0xFA << 40;
    public static Core.EntityId Of(FarmStoreKind kind, int index) => Core.EntityId.Make(Core.IdKind.Other, Tag | ((ulong)kind << 32) | (uint)index);
    public static Core.EntityId Barn(int household) => Of(FarmStoreKind.Barn, household);
    public static Core.EntityId ParishGranary(int settlement) => Of(FarmStoreKind.ParishGranary, settlement);
    public static Core.EntityId Fodder(int settlement) => Of(FarmStoreKind.Fodder, settlement);
}
