using System.Runtime.CompilerServices;
using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;

namespace Ravensguard.Sim.Combat.Aftermath;

/// <summary>Who won a fight, for the defeat table (morale-capture-succession 4.4).</summary>
public enum WinnerKind : byte { None, Bandits, Army, Beasts, Draugr, Guards, Locals, Folk }

/// <summary>What a defeat cost (3.2 <c>DefeatOutcome.Kind</c>); <see cref="Left"/> is a person the winners did not touch.</summary>
public enum DefeatKind : byte { Robbed, Captured, Killed, Mauled, Ignored, Yielded, Left }

/// <summary>How a defeated person ended the fight (4.3).</summary>
public enum LoserState : byte { Dead, Dying, KnockedOut, Surrendered, Fled, Standing }

/// <summary>Who the loser is to the hero (4.4's two columns): the hero, a party member (follower), a hireling (flees), a stranger, a soldier of a beaten detachment.</summary>
public enum LoserRole : byte { Hero, PartyMember, Hireling, Stranger, Soldier }

public enum Restraint : byte { None, Bound, Chained, Caged }
public enum CaptiveState : byte { Held, Ransomed, Escaped, Rescued, Released, Dead }
public enum CareLevel : byte { None, Basic }
public enum CaptorKind : byte { Bandits, Army,
    /// <summary>Phase 6 (overview 8.1, append only): held in a lord's gaol (crime and justice 4.5). Nothing writes it until C1.</summary>
    Gaol,
    /// <summary>Phase 6 (overview 8.1, append only): taken by slavers (slavery spec). Nothing writes it until D1.</summary>
    Slavers }
/// <summary>Who pays a ransom (4.6): the faction the captive serves, the captive's household, friends (companions' purses), or nobody.</summary>
public enum PayerKind : byte { None, Faction, Household, Friends }

/// <summary>Rank for a ransom (4.6 <c>rank_base</c>); a wanderer hero with no faction is priced by purse and property.</summary>
public enum CaptiveRank : byte { Wanderer, Levy, ManAtArms, Knight, Baron }

/// <summary>What kind of thing a carried item is, for the robbers' order (4.4).</summary>
public enum LootKind : byte { Weapon, Mount, Armour, Clothing, Goods, Tool, Food }

/// <summary>One thing a victim has (an item row, a mount, a stack of goods), as the robbers judge it.</summary>
public sealed record LootItem(EntityId Id, string Shape, LootKind Kind, long ValueMites, long MassG, int MadeForCm3 = 70000, bool Dagger = false, ushort Good = ushort.MaxValue, long Qty = 0);

/// <summary>A robber: strength (1250 typical) and size, for the carry capacity and the fit of armour.</summary>
public readonly record struct Robber(int Strength = 1250, int SizeCm3 = 70000, bool FirstAid = false);

/// <summary>What the robbers take (4.4): coin, then the things in their order until they are full; and what they leave.</summary>
public sealed record RobberyPlan(long CoinTaken, long CoinLeft, IReadOnlyList<LootItem> Taken, IReadOnlyList<LootItem> Left, long FoodTakenG, long FoodLeftG);

/// <summary>One defeated person's fate (3.2 <c>DefeatOutcome</c>), with what the robbers took when they robbed.</summary>
public sealed record DefeatOutcome(ulong Fight, EntityId Loser, WinnerKind Winner, DefeatKind Kind, LoserState State, long Time, EntityId Place, string Style = "",
    bool Mercy = false, RobberyPlan? Loot = null, bool Killed = false);

/// <summary>
/// The defeat, captivity and ransom rules (4.4 to 4.7) from <c>combat/captivity.jsonc</c> and the bandit styles from <c>combat/bandit_styles.jsonc</c>, read once per content database.
/// </summary>
public sealed class AftermathRules
{
    public CaptivityRulesDef D { get; }
    public IReadOnlyList<BanditStyleDef> Styles { get; }

    public AftermathRules(CaptivityRulesDef d, IReadOnlyList<BanditStyleDef> styles)
    {
        D = d;
        Styles = styles.Count > 0 ? styles : [new BanditStyleDef { Id = "robber", SharePm = 1000 }];
    }

    private static readonly ConditionalWeakTable<ContentDatabase, AftermathRules> Cache = new();

    public static AftermathRules For(ContentDatabase db) => Cache.GetValue(db, d => new AftermathRules(
        d.All<CaptivityRulesDef>().OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault() ?? new CaptivityRulesDef { Id = "default" },
        [.. d.All<BanditStyleDef>().OrderBy(x => x.Id, StringComparer.Ordinal)]));

    /// <summary>The spec's defaults (tests without content): robber 70%, cruel 15%, kidnapper 15%.</summary>
    public static AftermathRules Default { get; } = new(new CaptivityRulesDef { Id = "default" },
    [
        new BanditStyleDef { Id = "cruel", SharePm = 150, KillWoundedLonePm = 500 },
        new BanditStyleDef { Id = "kidnapper", SharePm = 150, Kidnapper = true, CaptureMinMites = 1000 },
        new BanditStyleDef { Id = "robber", SharePm = 700 },
    ]);

    /// <summary>Who won, by the encounter group's archetype (content table); unknown archetypes are folk.</summary>
    public WinnerKind WinnerOf(string archetype) => D.WinnerByArchetype.TryGetValue(archetype, out var k) ? Parse(k) : WinnerKind.Folk;

    public static WinnerKind Parse(string k) => k switch
    {
        "bandits" => WinnerKind.Bandits, "army" => WinnerKind.Army, "beasts" => WinnerKind.Beasts, "draugr" => WinnerKind.Draugr, "guards" => WinnerKind.Guards, "locals" => WinnerKind.Locals,
        _ => WinnerKind.Folk,
    };

    /// <summary>The band's style, one per band (3.2): drawn by the fight id on stream 0x452 (draw 0) over the styles' shares, in id order.</summary>
    public BanditStyleDef StyleOf(ulong seed, ulong fightId)
    {
        int total = Math.Max(1, Styles.Sum(s => s.SharePm));
        int r = DetRng.Range(seed, (long)fightId, 0x0BAD, RngStreams.Loot, 0, 0, total);
        foreach (var s in Styles) { if (r < s.SharePm) return s; r -= s.SharePm; }
        return Styles[^1];
    }

    /// <summary>The morale side kind a winner kind fights as (who accepts surrenders, 4.2).</summary>
    public static Morale.SideKind SideOf(WinnerKind k) => k switch
    {
        WinnerKind.Army => Morale.SideKind.Army, WinnerKind.Bandits => Morale.SideKind.Bandits, WinnerKind.Beasts => Morale.SideKind.Beasts, WinnerKind.Draugr => Morale.SideKind.Draugr,
        WinnerKind.Guards => Morale.SideKind.Guards, WinnerKind.Locals => Morale.SideKind.Locals, _ => Morale.SideKind.Folk,
    };
}

/// <summary>Coin reasons of the aftermath (the ledger's reasons are shared with saves and the treasury view, so these are aliases, as the jobs' escrow is).</summary>
public static class AftermathCoin
{
    /// <summary>Robbed coin and a guard's fine.</summary>
    public static readonly TransferReason Plunder = TransferReason.Fine;
    public static readonly TransferReason Ransom = TransferReason.Fine;
    public static readonly TransferReason Funeral = TransferReason.Tithe;
    public static readonly TransferReason Inherit = TransferReason.Inheritance;

    /// <summary>A bandit band's purse and stash owner: a household account under a band id made from the fight (no allocator: the same fight gives the same band).</summary>
    public static EntityId BandId(ulong fightId) => EntityId.Make(IdKind.Other, 0x45_0000_0000_0000UL | (fightId & 0xFFFF_FFFF_FFFFUL));
    /// <summary>An army detachment's pay chest (the faction treasury is its purse when known).</summary>
    public static EntityId DetachmentId(ulong fightId) => EntityId.Make(IdKind.Other, 0x46_0000_0000_0000UL | (fightId & 0xFFFF_FFFF_FFFFUL));
}
