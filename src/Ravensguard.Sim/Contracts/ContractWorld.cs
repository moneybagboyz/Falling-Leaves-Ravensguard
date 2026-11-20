using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Contracts;

/// <summary>A store that has run short of an input it buys (spec 4.1): who owns it, which good, how many days of it are left, where it is.</summary>
public readonly record struct StockNeed(EntityId Owner, int Settlement, ushort Good, long Grams, int DaysLeft, string Name);

/// <summary>A caravan about to leave with goods (spec 4.1): its owner, where it leaves from, the value of its goods, its guards wanted, its days.</summary>
public readonly record struct CaravanLeaving(EntityId Caravan, EntityId Owner, int Settlement, long ValueMites, int Guards, int Days);

/// <summary>A band seen near a road (spec 4.1): the band's id, the settlement it threatens, the day it was last seen, its members.</summary>
public readonly record struct BandSeen(EntityId Band, int Settlement, int LastSeenDay, int Members);

/// <summary>A captive held by bandits or a lord (spec 4.1): who, the household that pays for the rescue, where he is held, whether he is the hero's kin.</summary>
public readonly record struct CaptiveHeld(EntityId Person, EntityId Household, int Settlement, long RansomMites);

/// <summary>A site its owner wants built: a cottage for a household in a visited place (spec 4.3).</summary>
public readonly record struct BuildWanted(EntityId Client, int Settlement, long BudgetMites, string What);

/// <summary>A farm or a site short of hands (spec 4.1, 4.3): day labour posted at the hiring hour.</summary>
public readonly record struct HandsShort(EntityId Owner, int Settlement, int Hands, string Kind);

/// <summary>
/// What the contracts read and write (contracts-and-rumours-v2 2), gathered so a test builds a town by hand and the runtime builds it from the world state (<see cref="ContractWiring"/>): the chronicle and
/// its ids, the ledger and the purses, the people, the settlements and their fiefs, and every question the scanner and the verifiers ask of other systems (stores, caravans, bands, captives, the crime book).
/// Every hook is optional: a need whose hook is unset posts nothing.
/// </summary>
public sealed class ContractWorld
{
    public required Chronicle Chronicle { get; init; }
    public required IdAllocator Ids { get; init; }
    public required Ledger Ledger { get; init; }
    public required PersonTable People { get; init; }
    public required string[] SettlementNames { get; init; }
    public EntityId[] SettlementIds { get; init; } = [];
    public int[] SettlementFief { get; init; } = [];
    public EntityId[] FiefIds { get; init; } = [];
    public ulong Seed { get; init; }
    public ContractContent Content { get; init; } = ContractContent.Default;
    public EntityId HeroId { get; init; } = EntityId.None;
    public ContractSettingsDef S => Content.S;

    /// <summary>The purse of an issuer id (a household, a business, a fief's treasury, a person, a caravan owner); null when it has none.</summary>
    public Func<EntityId, PurseId?> PurseOfOwner { get; init; } = _ => null;
    /// <summary>A person's own purse (the hero's, else the household's).</summary>
    public Func<EntityId, PurseId?> PurseOf { get; init; } = _ => null;
    /// <summary>The treasury of the fief of a settlement.</summary>
    public Func<int, PurseId?> TreasuryOf { get; init; } = _ => null;
    /// <summary>The metres between two settlements.</summary>
    public Func<int, int, long> DistanceM { get; init; } = (a, b) => a == b ? 0 : 5_000;
    /// <summary>The settlement a person is at (the hero's place, an NPC's home).</summary>
    public Func<EntityId, int> PlaceOf { get; init; } = _ => -1;

    // ---- the needs (4.1)
    public Func<IEnumerable<StockNeed>>? StockNeeds { get; init; }
    /// <summary>Whether a merchant of a settlement holds at least this many grams of a good.</summary>
    public Func<int, ushort, long, bool> MerchantHolds { get; init; } = (_, _, _) => false;
    public Func<IEnumerable<CaravanLeaving>>? Caravans { get; init; }
    public Func<IEnumerable<BandSeen>>? Bands { get; init; }
    public Func<IEnumerable<CaptiveHeld>>? Captives { get; init; }
    public Func<IEnumerable<BuildWanted>>? Builds { get; init; }
    /// <summary>The builders who bid on a building contract (the region's carpentry and masonry businesses), besides the hero; null: none.</summary>
    public Func<WorkContract, IEnumerable<Bidder>>? Bidders { get; init; }
    public Func<IEnumerable<HandsShort>>? Hands { get; init; }
    /// <summary>The believed monster or bandit rumours of a settlement (percent of the place who believe), 0 when none.</summary>
    public Func<int, int>? BeliefOf { get; init; }

    // ---- the verifiers (4.2)
    /// <summary>The grams of a good in the store an owner id holds.</summary>
    public Func<EntityId, ushort, long> StoreQty { get; init; } = (_, _) => 0;
    public Func<EntityId, bool> IsDead { get; init; } = _ => false;
    /// <summary>The person is held (in a gaol, or by the taker's side).</summary>
    public Func<EntityId, bool> IsHeld { get; init; } = _ => false;
    /// <summary>How a captive's hold stands (S2): unknown (no captivity row), still held, or ended, with how ("rescued", "ransomed", "released", "escaped", "dead"), the day it ended and whether the hero's side freed him.</summary>
    public Func<EntityId, CaptiveEnding> CaptiveFree { get; init; } = _ => default;
    /// <summary>The day a person was last taken into a gaol or a captor's hold, while he is held (-1 when he is not). Null: the day is not known (any hold counts).</summary>
    public Func<EntityId, int>? HeldSince { get; init; }
    /// <summary>The day a person died (-1 while alive). Null: the day is not known (any death counts).</summary>
    public Func<EntityId, int>? DeadSince { get; init; }
    /// <summary>Whether a band is gone (its members dead, fled or captured).</summary>
    public Func<EntityId, bool> BandGone { get; init; } = _ => false;
    /// <summary>Whether a taker's side includes a person (the hero's party, a company's members).</summary>
    public Func<EntityId, EntityId, bool> SideOf { get; init; } = (taker, who) => taker == who;
    /// <summary>The companies for hire (mercenaries, guards, builders), in id order.</summary>
    public Func<IEnumerable<CompanyInfo>> Companies { get; init; } = () => [];
    /// <summary>The danger (percent chance of losing) a company runs on a contract: the odds rule of watch mode's risk estimate.</summary>
    public Func<CompanyInfo, WorkContract, int> RiskPct { get; init; } = (c, k) => Math.Clamp(60 - c.Strength / 2, 5, 90);
    /// <summary>Told when a company's contract succeeds, so the world sees its effect (the band cleared, the captive freed); null: nobody listens.</summary>
    public Action<WorkContract>? CompanyDone { get; init; }

    // ---- tournaments (4.4) and slander (4.5)
    /// <summary>The settlements with a market charter (they hold the great fair's tournament).</summary>
    public Func<IEnumerable<int>>? CharterTowns { get; init; }
    /// <summary>Days from a game day until the great fair begins (0: today), -1 when none is coming.</summary>
    public Func<long, int>? FairInDays { get; init; }
    /// <summary>The fighters of a settlement who would enter a tournament, with their rating (the fight odds' power).</summary>
    public Func<int, IEnumerable<(EntityId Person, int Rating)>>? Fighters { get; init; }
    /// <summary>The rating of any entrant (the hero's included); 0 when unknown.</summary>
    public Func<EntityId, int> RatingOf { get; init; } = _ => 100;
    /// <summary>The winner the sim's own fight records name for a bout of two people since a day (a Phase 4 practice fight's result), or None when no such fight was fought: a tournament's purse is paid from these and nothing else.</summary>
    public Func<EntityId, EntityId, int, EntityId?>? BoutRecord { get; init; }
    /// <summary>Whether a person is a rival of another (the <c>Rival</c> flag of the talk book) and his Speech skill.</summary>
    public Func<EntityId, EntityId, bool>? IsRival { get; init; }
    public Func<EntityId, int> SpeechOf { get; init; } = _ => 0;
    /// <summary>The hero's standing with a settlement's fief, permille, and a person's trust of another.</summary>
    public Func<EntityId, int, int> StandingWithFief { get; init; } = (_, _) => 0;
    /// <summary>A person's trust in another, permille (the talk book's).</summary>
    public Func<EntityId, EntityId, int> TrustPm { get; init; } = (_, _) => 0;
    /// <summary>The hero's daily wage in mites (the daily wage the autopilot's offer rule weighs a contract against).</summary>
    public long DailyWageMites { get; init; } = 300;
    /// <summary>A person's name for the chronicle's data.</summary>
    public Func<EntityId, string>? NameOf { get; init; }
    /// <summary>The names of goods for the chronicle's data.</summary>
    public Func<ushort, string>? GoodName { get; init; }

    public string Name(EntityId p)
    {
        if (NameOf?.Invoke(p) is { Length: > 0 } n) return n;
        int i = People.IndexOf(p); return i >= 0 ? People.FullName(i) : "";
    }

    public EntityId PlaceId(int settlement) => settlement >= 0 && settlement < SettlementIds.Length ? SettlementIds[settlement] : EntityId.None;
    public string PlaceName(int settlement) => settlement >= 0 && settlement < SettlementNames.Length ? SettlementNames[settlement] : "";
    public int FiefOf(int settlement) => settlement >= 0 && settlement < SettlementFief.Length ? SettlementFief[settlement] : -1;
}

/// <summary>A captive's hold as a contract reads it (S2): whether a captivity row exists, whether he is still held, how and on which day it ended, and whether the hero's side freed him.</summary>
public readonly record struct CaptiveEnding(bool Known, bool Held, string How, int EndDay, bool ByHero);
