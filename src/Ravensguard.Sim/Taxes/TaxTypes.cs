using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;

namespace Ravensguard.Sim.Taxes;

/// <summary>The four named hearth tax levels of the plan (section 4A.2): a fief's lord sets one; the amounts are content (<c>tax_policy_defaults</c>).</summary>
public enum TaxLevel : byte { Low, Normal, High, Crushing }

/// <summary>Whose chest a treasury record describes (taxes spec 2.1). Mint treasuries come with the mint (money spec 3.7), later.</summary>
public enum TreasuryKind : byte { Realm, Fief, Town, Parish }

/// <summary>Counters the tax systems keep for the soak and the tests (saved, so a resumed world reports the same totals).</summary>
public enum TaxTally : byte
{
    HearthCollected, HearthDue, HearthWaived, HearthEvaded, DwellingCollected, DebtRepaid, WorkedOff, CrownForwarded,
    Evictions, EvictedClaim, EvasionEvents, Riots, RiotGrainG, RumourSeeds, RevoltsBrewing, LordStipend, CrownWorks, ParishWages, ParishBanked, Converted, Waivers, LatePaid, WrittenOff,
}

/// <summary>
/// A public body's chest (taxes spec 2.1): the realm's account, a fief's, a chartered town's (a settlement without a fief) or a parish's. The coin is a real purse
/// of the ledger (a <see cref="PurseKind.Treasury"/> or <see cref="PurseKind.Chest"/>), so it can be raided later; the record only says whose it is.
/// </summary>
public sealed class TreasuryRecord
{
    public int Slot { get; init; }
    public TreasuryKind Kind { get; init; }
    public EntityId Id { get; init; }
    public PurseId Purse { get; init; }
    public string Name { get; init; } = "";
    /// <summary>Fief index, settlement index (the seat, the town, the parish) and realm index of the record, -1 where none.</summary>
    public int Fief { get; init; } = -1;
    public int Settlement { get; init; } = -1;
    public int Realm { get; init; } = -1;
}

/// <summary>One household's unpaid dues (taxes spec 2.1 <c>Arrears</c> rows): arrears carry over, become a debt at the third unpaid season and end in eviction at the sixth.</summary>
public sealed class ArrearsRow
{
    public long ArrHearth, ArrDwelling, DebtHearth, DebtDwelling;
    /// <summary>Consecutive seasons with something unpaid (hearth, dwelling or parcel rent).</summary>
    public int Seasons;
    public long Arrears => ArrHearth + ArrDwelling;
    public long Debt => DebtHearth + DebtDwelling;
    public long Total => Arrears + Debt;
    public bool IsClear => Seasons == 0 && Total == 0;
}
