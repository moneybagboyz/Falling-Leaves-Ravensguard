using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.WarSupply;

/// <summary>The troop roles a host's roster counts (army-economy 3.1 and the overview 8.2 wage table). Append only: the roster and the save index by it.</summary>
public enum TroopRole : byte { Levy, Spearman, Archer, Crossbowman, Sergeant, ManAtArms, Knight, Engineer, Sailor, Marine, Mercenary, Captain, Quartermaster }

/// <summary>What a host is doing (army-economy 3.1).</summary>
public enum HostMode : byte { March, Camp, Siege, Garrison, Battle }

/// <summary>Foraging policy of a host (army-economy 3.1); the ladder's forage step is P7-AE2, so the first cut only records it.</summary>
public enum ForagePolicy : byte { None, OwnLandPaid, EnemyLand }

/// <summary>The needs of a day (<see cref="DayNeeds"/>); pay is the last row.</summary>
public enum SupplyNeed : byte { Staple, Relish, Hay, Oats, Firewood, Pay }

/// <summary>Where a need was filled from (the supply ladder, army-economy 4.1). The first cut fills from <see cref="Baggage"/> and <see cref="Market"/>; the others are the rest of P7-AE1 and P7-AE2.</summary>
public enum SupplySource : byte { Baggage, FoodRent, Depot, Market, Sutlers, Purveyance, Forage, Plunder }

/// <summary>A host's id in the supply book (1 up; 0 is none).</summary>
public readonly record struct HostId(int Value) : IComparable<HostId>
{
    public static readonly HostId None = new(0);
    public bool IsNone => Value <= 0;
    public int CompareTo(HostId other) => Value.CompareTo(other.Value);
    public override string ToString() => IsNone ? "Host#none" : $"Host#{Value}";
}

/// <summary>Ids of the records a host owns, in reserved serial spaces of <see cref="IdKind.Other"/> (so they never meet a table row's id): 4 is a host's soldiers' purse, 5 the load generator's pay chests, 6 its baggage stores, 7 a settlement's war depot (a granary the host's side keeps for its army, P7-AE1).</summary>
public static class HostIds
{
    public const EntityId.ReservedSpace SoldiersSpace = (EntityId.ReservedSpace)4, ChestSpace = (EntityId.ReservedSpace)5, BaggageSpace = (EntityId.ReservedSpace)6, DepotSpace = (EntityId.ReservedSpace)7;
    /// <summary>The owner of the war depot's store at a settlement (<see cref="HostSupply.OpenDepot"/>).</summary>
    public static EntityId Depot(int settlement) => EntityId.Reserved(IdKind.Other, 0, DepotSpace, (ulong)settlement);
    public static EntityId Soldiers(HostId h) => EntityId.Reserved(IdKind.Other, 0, SoldiersSpace, (ulong)h.Value);
    public static EntityId Chest(int n) => EntityId.Reserved(IdKind.Other, 0, ChestSpace, (ulong)n);
    public static EntityId Baggage(int n) => EntityId.Reserved(IdKind.Other, 0, BaggageSpace, (ulong)n);
}

/// <summary>Men by troop role. Copied on every call that keeps one, so a caller's later edit never reaches a host's account.</summary>
public sealed class Roster : IEquatable<Roster>
{
    public static readonly int Roles = Enum.GetValues<TroopRole>().Length;
    public int[] Counts { get; }
    public Roster() { Counts = new int[Roles]; }
    public Roster(IEnumerable<(TroopRole Role, int Count)> rows) : this() { foreach (var (r, c) in rows) Counts[(int)r] += c; }
    internal Roster(int[] counts) { Counts = new int[Roles]; Array.Copy(counts, Counts, Math.Min(counts.Length, Roles)); }
    public int this[TroopRole r] { get => Counts[(int)r]; set => Counts[(int)r] = value; }
    public int Total { get { int t = 0; foreach (int c in Counts) t += c; return t; } }
    public Roster Clone() => new(Counts);
    public bool Equals(Roster? o) => o != null && Counts.AsSpan().SequenceEqual(o.Counts);
    public override bool Equals(object? obj) => Equals(obj as Roster);
    public override int GetHashCode() { int h = 17; foreach (int c in Counts) h = h * 31 + c; return h; }
}

/// <summary>A host's animals by the rows of <c>animals/species.jsonc</c> that matter to the fodder rule: riding horses (palfrey), pack horses, draught horses and oxen.</summary>
public readonly record struct AnimalCounts(int Riding, int Pack, int Draught, int Oxen)
{
    public int Total => Riding + Pack + Draught + Oxen;
}

/// <summary>Where a host is: the region's settlement it stands at or beside and its place in region metres; <see cref="Pasture"/> is the military's note that it is camped on pasture (hay is halved in summer and autumn).</summary>
public readonly record struct HostPlace(int Settlement, int XM, int YM, bool Pasture = false);

public sealed record SupplyPolicy(bool Purveyance = false, ForagePolicy Forage = ForagePolicy.None, int[]? Depots = null, int BuyRadiusM = SupplyConfig.BuyRadiusM, bool Plunder = false);

/// <summary>What a host is made of, passed to <see cref="HostSupply.Register"/>. <c>PayChest</c> is a <see cref="PurseKind.Chest"/> purse and <c>Baggage</c> a store (the carts' cargo), both opened by the caller; <c>Treasury</c> is where the unspent coin goes at <c>Disband</c> (none: it stays in the chest).</summary>
public sealed record HostSpec(int Realm, EntityId Lord, PurseId PayChest, Store Baggage, Roster Men, int Followers, AnimalCounts Animals, int Carts, SupplyPolicy Policy)
{
    public PurseId Treasury { get; init; } = PurseId.None;
    /// <summary>The chronicle entry of the campaign this host serves (the war's or the muster's): the cause of the host's own entries (army_bought, siege_starving, depot_emptied). None: those entries stand on their note keys.</summary>
    public EntityId Campaign { get; init; } = EntityId.None;
    /// <summary>Men of the roster who come from outside the region (the realm's, a gate's): their pay leaves with them at <c>Disband</c> (the continent side takes it), the others' is spent at home.</summary>
    public int OutsideMen { get; init; }
}

/// <summary>A day's needs (army-economy 3.2): grams of staple-equivalent, relish, hay, oats and firewood, and pay in mites.</summary>
public readonly record struct DayNeeds(long StapleG, long RelishG, long FodderHayG, long FodderOatsG, long FirewoodG, long PayMites)
{
    public long Of(SupplyNeed n) => n switch { SupplyNeed.Staple => StapleG, SupplyNeed.Relish => RelishG, SupplyNeed.Hay => FodderHayG, SupplyNeed.Oats => FodderOatsG, SupplyNeed.Firewood => FirewoodG, _ => PayMites };
}

/// <summary>What <see cref="HostSupply.Supply"/> found and did (army-economy 3.2).</summary>
public sealed class DayResult
{
    public static readonly int Needs = Enum.GetValues<SupplyNeed>().Length, Sources = Enum.GetValues<SupplySource>().Length;
    public int Day { get; init; }
    public HostId Host { get; init; }
    public DayNeeds Needed { get; init; }
    /// <summary>Grams (mites for pay) by need then source.</summary>
    public long[,] Delivered { get; } = new long[Needs, Sources];
    /// <summary>What was not found, by need (pay: mites not paid).</summary>
    public long[] ShortG { get; } = new long[Needs];
    public long[] Paid { get; } = new long[Roster.Roles];
    public long[] Arrears { get; } = new long[Roster.Roles];
    public int[] HungerDays { get; } = new int[Roster.Roles];
    public int[] UnpaidDays { get; } = new int[Roster.Roles];
    /// <summary>Baggage staple over today's staple need, in thousandths of a day; the pay chest over today's pay, likewise.</summary>
    public long DaysOfFoodMilli { get; set; }
    public long DaysFundedMilli { get; set; }
    /// <summary>The chronicle entry ids written today (army_bought where the host's buying moved a price; later parts add purveyance and forage).</summary>
    public List<EntityId> EventIds { get; } = [];
    public IReadOnlyList<EntityId> Events => EventIds;
    /// <summary>The books the host bought staple from today: settlement to (the ask before its first buy, the ask after its last, grams bought, the good): what <c>army_bought</c> is written from.</summary>
    internal SortedDictionary<int, (long Ask0, long Ask1, long Grams, ushort Good)> Purchases { get; } = [];
    /// <summary>Coin the market was paid today for the host's purchases.</summary>
    public long BoughtMites { get; set; }
    public long DeliveredOf(SupplyNeed n) { long t = 0; for (int s = 0; s < Sources; s++) t += Delivered[(int)n, s]; return t; }
    public long DeliveredFrom(SupplySource s) { long t = 0; for (int n = 0; n < Needs - 1; n++) t += Delivered[n, (int)s]; return t; }
    public long PaidTotal { get { long t = 0; foreach (long p in Paid) t += p; return t; } }
    public bool Hungry(TroopRole r) => HungerDays[(int)r] > 0;
}

/// <summary>The levies a <c>Muster</c> picked: the persons (table rows, ascending household), how many households evaded and how many were asked.</summary>
public sealed record MusterResult(IReadOnlyList<int> Persons, int Evaded, int HouseholdsAsked);

/// <summary>The numbers of the supply rules (army-economy 5, the overview 8.2 wage table and Q1); content files (<c>war/supply.jsonc</c>) arrive with the rest of P7-AE1, so for now they are constants here, in one place.</summary>
public static class SupplyConfig
{
    public const int RationG = 1000, FollowerRationG = 800, RelishG = 150, FirewoodG = 1500, BuyRadiusM = 8000, ForageRadiusM = 4000;
    /// <summary>Overview Q1 (the user's decision): a levy serves this many days of a campaign year unpaid.</summary>
    public const int LevyFreeDays = 10;
    /// <summary>Hours of a mustered man's day the household loses (army-economy 4.9).</summary>
    public const int LevyHoursPerDay = 10;
    /// <summary>Measures of food per kg of grain x 1,000,000 (1 measure = 1,100 g): a good's staple equivalent is its mass times its own measures over this.</summary>
    public const int GrainMeasurePpm = 909_091;
    /// <summary>The price clamp the quartermaster pays up to, permille of the reference (the books' clamp is 4,000).</summary>
    public const int BuyMaxRatioPermille = 4000;
    /// <summary>The age a levy may be drawn at (overview 8.2: 16 to 45).</summary>
    public const int LevyMinAge = 16, LevyMaxAge = 45;
    /// <summary>A settlement under this many households is a farmstead, not a village: the levy asks it after the villages (overview 10d).</summary>
    public const int LevyVillageMinHouseholds = 5;
    /// <summary>A depot within this many metres of the host delivers the same day (army-economy 4.1); beyond it the quartermaster sends a convoy.</summary>
    public const int DepotSameDayM = 8000;
    /// <summary>The lord's barn keeps this many days of its own household's grain when it gives to his host (army-economy 4.3's reserve, read for 4.1's food rents).</summary>
    public const int ReserveGrainDays = 30;
    /// <summary>A convoy is sent when the baggage holds fewer than this many days of staple; it loads up to this many days of the host's staple, on at most this many carts.</summary>
    public const int ConvoyBelowDays = 3, ConvoyLoadDays = 10, ConvoyMaxCarts = 8;
    /// <summary>A siege's larder under this many days of food writes <c>siege_starving</c> once (army-economy 4.8).</summary>
    public const int StarvingDays = 5;
    /// <summary>A purchase writes <c>army_bought</c> when it moved the staple's ask by at least this many per mille.</summary>
    public const int PriceNotePermille = 50;

    // ---- P7-AE2: purveyance, scutage, forage, sutlers, finance (army-economy 4.2, 4.3, 4.4, 4.5, 4.7, 5)
    /// <summary>Purveyance (4.3): settlements within this many metres of the host, grain above this many days of a household's own need, at this many per mille of the book's mid price (the king's price), by a tally due after this many days and defaulted this many seasons past due; a household hides grain with P = base + unrest / 20 per cent (stream 0x660) and gives half.</summary>
    public const int PurveyanceRadiusM = 16000, KingPricePm = 750, TallyDueDays = 30, TallyDefaultSeasons = 4, HideBasePct = 20;
    /// <summary>Purveyance fills the baggage to this many days of staple (not only the day's need: the officials' round is not repeated daily).</summary>
    public const int PurveyStockDays = 10;
    /// <summary>A settlement at or above this unrest (per mille) when purveyed has a riot triggered (4.3, the trigger kind <c>tallage</c>).</summary>
    public const int PurveyRiotUnrest = 350;
    /// <summary>Foraging (4.2): foragers are this many per mille of the men, each brings up to this many grams a day times a draw of 800 to 1,200 per mille (stream 0x750, key the host's tile and the day), from within <see cref="ForageRadiusM"/>; a robbed household's opinion of the host's realm falls by this much.</summary>
    public const int ForageSharePm = 100, ForageGramsEach = 30_000, ForageRobbedOpinion = -40;
    /// <summary>Service and scutage (4.4): a vassal's war service is this many days a man; scutage is this many mites a man-day.</summary>
    public const int WarServiceDays = 40, ScutageMitesPerManDay = 300;
    /// <summary>Sutlers (4.7): a host of at least this many men, merchants within <see cref="PurveyanceRadiusM"/> holding at least this much coin and this many grams of staple, joining with this per cent a day (stream 0x751), at most one per this many men, selling at this per mille of their book's ask.</summary>
    public const int SutlerMinMen = 200, SutlerJoinPct = 10, SutlerPerMen = 100, SutlerMarkupPm = 1300, SutlerMinMargin = 20;
    public const long SutlerMinMites = 5_000, SutlerMinStockG = 50_000;
    /// <summary>Finance (4.5): a campaign starts at this many days funded; below the second number the realm seeks a loan, a subsidy or peace.</summary>
    public const int CampaignStartDaysFunded = 30, CampaignSeekFundsDays = 10;

    /// <summary>Daily fodder in grams by animal row: hay and oats (species.jsonc: riding horse 8 kg and 2.5 kg, pack horse 8 and 0, draught horse 9 and 3, ox 10 and 0).</summary>
    public static (long HayG, long OatsG) Fodder(AnimalCounts a) => (a.Riding * 8000L + a.Pack * 8000L + a.Draught * 9000L + a.Oxen * 10000L, a.Riding * 2500L + a.Draught * 3000L);

    /// <summary>The wage of a role a day in mites (overview 8.2): levy 150 after the free days, spearman and archer 250, crossbowman 300, sergeant 500, man-at-arms 750, knight 1,500, engineer 400, sailor 250, marine 300, a mercenary 1.5 x a spearman, captain and quartermaster the spearman's plus 100.</summary>
    public static int Wage(TroopRole r) => r switch
    {
        TroopRole.Levy => 150, TroopRole.Spearman => 250, TroopRole.Archer => 250, TroopRole.Crossbowman => 300, TroopRole.Sergeant => 500, TroopRole.ManAtArms => 750, TroopRole.Knight => 1500,
        TroopRole.Engineer => 400, TroopRole.Sailor => 250, TroopRole.Marine => 300, TroopRole.Mercenary => 375, TroopRole.Captain => 350, TroopRole.Quartermaster => 350, _ => 0,
    };

    /// <summary>The order roles are paid in (overview 8.2): mercenaries, then knights, men-at-arms and sergeants, then the rest.</summary>
    public static readonly TroopRole[] PayOrder =
    [
        TroopRole.Mercenary, TroopRole.Knight, TroopRole.ManAtArms, TroopRole.Sergeant,
        TroopRole.Captain, TroopRole.Quartermaster, TroopRole.Engineer, TroopRole.Crossbowman, TroopRole.Spearman, TroopRole.Archer, TroopRole.Marine, TroopRole.Sailor, TroopRole.Levy,
    ];
}
