using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Economy;

/// <summary>Who owns a purse (money spec 2.1, plus the owner kinds the plan names). The pair (kind, owner id) identifies a purse.</summary>
public enum PurseKind : byte
{
    /// <summary>A single person (the hero, a merchant's own cash).</summary>
    Person,
    /// <summary>A household: coin of all members, the head decides.</summary>
    Household,
    /// <summary>A business cash box (production spec); owner is a business id.</summary>
    Business,
    /// <summary>A settlement's common funds (market change box, town chest).</summary>
    Settlement,
    /// <summary>A public body's treasury: faction, fief, town.</summary>
    Treasury,
    /// <summary>A physical chest (a parish chest, a strongbox) that can later be found and robbed.</summary>
    Chest,
    /// <summary>Coin buried or lost: out of circulation but still audited.</summary>
    Hoard,
    /// <summary>A market book's rounding change box (at most 1 mite per trade).</summary>
    Market,
    /// <summary>A caravan's trip float and takings in transit.</summary>
    Caravan,
}

/// <summary>Why coin moved between two purses (money spec 2.1; Mint, Melt and Seed are sources and sinks, not transfers).</summary>
public enum TransferReason : byte
{
    Trade, Wage, Rent, Hearth, Toll, Tithe, MarketToll, Dividend, Seigniorage, Inheritance, Escheat, Hoard, Stipend, Fine, Gift,
    /// <summary>Charity in coin to the destitute (lords' almonry, the parish, the fief's dole, a rich household's alms; integration decision I37). Appended: saves older than it load with the count padded.</summary>
    Alms,
    /// <summary>P5-F (businesses spec 4.1): a share of a business bought or sold back. Appended: older saves load with the count padded, as for <see cref="Alms"/>.</summary>
    ShareSale,
    /// <summary>P5-F (prospecting spec 5): the right holder's share of what a licensed mine takes. Appended.</summary>
    Royalty,
    // Phase 6 (overview 8.1, append only; saves pad the count as for Alms): justice, slavery, family, rule and income. Nothing writes them until its own step lands.
    Fee, Restitution, Bounty, Bribe, SlaveSale, Dowry, Relief, Aid, Subsidy, Tallage, Customs, Loan, Interest, Skim,
    /// <summary>P6-C2 (contracts 4.1): a contract's pay moved from its issuer's purse into the escrow chest, and a taker's bond into the bond chest. Appended: older saves load with the count padded.</summary>
    ContractEscrow,
    /// <summary>P6-C2 (contracts 4.2): the escrow paid to the taker on completion (a company's share to its purse), a tournament's purse to its winner.</summary>
    ContractPay,
    /// <summary>P6-C2 (contracts 4.2): the escrow returned to the issuer on failure, expiry or cancel; a bond given back on success or charged to the issuer on a failure.</summary>
    ContractRefund,
}

/// <summary>The only faucets: the one-off seed money of world creation, silver struck at a mint, and the coin a household brings across a region border (the other region's ledger holds the matching <see cref="SinkReason.Emigrate"/>).</summary>
public enum SourceReason : byte { Seed, Mint, Immigrate }

/// <summary>The only drains: melting coin into silver, test-only burning, and the coin a household takes across a region border (matched by <see cref="SourceReason.Immigrate"/> in the destination). Hoarding is a transfer to a Hoard purse, not a sink.</summary>
public enum SinkReason : byte { Melt, Burn, Emigrate }

/// <summary>A handle to a purse: its dense index in the ledger (ascending creation order). <see cref="None"/> is -1.</summary>
public readonly record struct PurseId(int Index) : IComparable<PurseId>
{
    public static readonly PurseId None = new(-1);
    public bool IsNone => Index < 0;
    public int CompareTo(PurseId other) => Index.CompareTo(other.Index);
    public override string ToString() => IsNone ? "Purse#none" : $"Purse#{Index}";
}

/// <summary>One logged movement of coin. <see cref="From"/> / <see cref="To"/> are <see cref="PurseId.None"/> for a source or a sink.</summary>
public readonly record struct LedgerEntry(long Seq, PurseId From, PurseId To, Coin Amount, byte Reason, bool IsSource, bool IsSink, EntityId? Event)
{
    public TransferReason? Transfer => !IsSource && !IsSink ? (TransferReason)Reason : null;
    public SourceReason? Source => IsSource ? (SourceReason)Reason : null;
    public SinkReason? Sink => IsSink ? (SinkReason)Reason : null;
}

/// <summary>
/// Audit of the closed money supply (plan §3 item 3): <c>sum(purses) + sunk - sourced</c> is constant, exactly zero. Computed from the balances themselves,
/// not from running totals, so a code path that bypassed the ledger would show as a non-zero <see cref="Imbalance"/>.
/// </summary>
public sealed record ConservationReport(Coin InPurses, Coin InHoards, Coin Seeded, Coin Minted, Coin Melted, Coin Burned, int PurseCount, long Transfers, Coin Immigrated = default, Coin Emigrated = default)
{
    public Coin Sourced => Seeded + Minted + Immigrated;
    public Coin Sunk => Melted + Burned + Emigrated;
    /// <summary>Coin in purses other than Hoard purses (the supply that prices see).</summary>
    public Coin Circulating => InPurses - InHoards;
    /// <summary><c>InPurses + Sunk - Sourced</c>; zero while conservation holds.</summary>
    public Coin Imbalance => InPurses + Sunk - Sourced;
    public bool IsConserved => Imbalance.IsZero;
    public override string ToString() => $"purses {InPurses} (hoards {InHoards}) + sunk {Sunk} - sourced {Sourced} = {Imbalance}; {PurseCount} purses, {Transfers} transfers";
}
