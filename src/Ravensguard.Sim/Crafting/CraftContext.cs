using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Crafting;

/// <summary>
/// What the Phase 5 crafting rules need to know about the world beyond the crafting state (crafting-full 4.6 to 4.8): whose purse and store a person draws on, the site a maker works at, the
/// crafter record of a person, what a good costs at his ask, where a person lives and how good he is. The world sets these once (<see cref="Ravensguard.Sim.Trade.HeroContext"/>); tests set them by hand.
/// Every list it returns is in id order, so the rules that read it are deterministic.
/// </summary>
public sealed class CraftContext
{
    public required EconomyState Econ { get; init; }
    public required CraftRunner Runner { get; init; }
    public Func<EntityId, PurseId?> PurseOf { get; init; } = _ => null;
    public Func<EntityId, Store?> StoreOf { get; init; } = _ => null;
    /// <summary>The site a maker works a recipe at (his workshop's stations and tools); null: he has none.</summary>
    public Func<EntityId, CraftRecipe, CraftSite?> SiteOf { get; init; } = (_, _) => null;
    public Func<EntityId, CraftActor> ActorOf { get; init; } = p => new CraftActor(p);
    /// <summary>What <paramref name="qty"/> of a good costs at a maker's ask, mites (default: the catalogue's base price, rounded up).</summary>
    public Func<ushort, long, long>? AskMites { get; init; }
    /// <summary>The settlement a person lives in (-1: none).</summary>
    public Func<EntityId, int> SettlementOf { get; init; } = _ => -1;
    /// <summary>The people of a settlement who might make, teach or join (alive adults), in id order.</summary>
    public Func<int, IEnumerable<EntityId>> PeopleOf { get; init; } = _ => [];
    /// <summary>Whether a settlement is a chartered town (guilds form only there).</summary>
    public Func<int, bool> IsTown { get; init; } = _ => false;
    /// <summary>The trade (occupation name) of a person, and the businesses of each trade in a settlement.</summary>
    public Func<EntityId, string> TradeOf { get; init; } = _ => "";
    public Func<int, string, int> BusinessesOf { get; init; } = (_, _) => 0;
    /// <summary>The ids of the people who saw something happen at a place (a guild offence's witnesses), in id order.</summary>
    public Func<int, EntityId, IReadOnlyList<EntityId>> WitnessesOf { get; init; } = (_, _) => [];
    public Ledger Ledger => Econ.Ledger;

    public long Ask(ushort good, long qty)
    {
        if (AskMites != null) return AskMites(good, qty);
        // the content price once to integer milli-mites per kg (per piece), then integer arithmetic, rounded up
        var cat = Econ.Goods; long milli = (long)Math.Round(cat.Def(good).BasePriceMitesPerKg * 1000);
        return cat.IsPiece(good) ? (milli * qty + 999) / 1000 : (milli * qty + 999_999) / 1_000_000;
    }
}
