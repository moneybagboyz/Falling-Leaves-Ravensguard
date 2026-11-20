using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;

namespace Ravensguard.Sim.Construction;

// P5-D: building contracts (docs/specs/phase5/build-mode-and-construction.md 4.7). Integer money; every walk in id order.

/// <summary>
/// Who wrote a contract: <see cref="Commission"/> a site's owner hired a builder (Q > Hire a builder); <see cref="Civic"/> the lord's planner posted work on the notice board (a cottage
/// for waiting couples the lord pays for); <see cref="Household"/> and <see cref="Business"/> are kept for P5-F's day labour and premises.
/// </summary>
public enum ContractKind : byte { Commission, Civic, Household, Business }

/// <summary>Open: on the board, nobody has taken it; Signed: a builder works it; Stopped: the client missed a stage (or the hero gave it up), the half-built work stays the client's; Done: handed over.</summary>
public enum ContractState : byte { Open, Signed, Stopped, Done, Cancelled }

/// <summary>A builder who can take a contract: a carpentry or masonry business of the region (its owner and staff build), or the hero by hand.</summary>
public sealed record BuilderInfo(EntityId Id, PurseKind Kind, string Name, int Settlement, int X, int Y, int BestSkill, IReadOnlyList<int> People, Ravensguard.Sim.Goods.Store? Store,
    Ravensguard.Sim.Economy.Markets.MarketParty? Party);

/// <summary>A bid (spec 4.7): materials at the local ask, labour (piece-hours x the skilled wage x the margin), the haul from the market; the crew and the days it expects.</summary>
public sealed record ContractBid(BuilderInfo Builder, long MaterialsMites, long LabourMites, long HaulMites, long MilliHours, int Crew, int ExpectedDaysX10)
{
    public long TotalMites => MaterialsMites + LabourMites + HaulMites;
}

/// <summary>One building contract: who pays, who builds, for which site, the price and how much of it was put up and paid out.</summary>
public sealed class Contract
{
    public EntityId Id;
    public ContractKind Kind;
    public ContractState State;
    public EntityId Site;
    public EntityId Client;
    public PurseKind ClientKind;
    /// <summary>The builder (a business, or the hero as a person); none while the contract is open.</summary>
    public EntityId Contractor;
    public PurseKind ContractorKind;
    public string Blueprint = "";
    public int Settlement = -1;
    public long MaterialsMites, LabourMites, HaulMites;
    /// <summary>The work of the contract (standard worker units of its build ghosts when signed) and the crew asked for.</summary>
    public long WorkUnits;
    public int Crew;
    /// <summary>Client payments made (0 to 3 thirds), and what the escrow paid the builder.</summary>
    public int Stages;
    public long ReleasedMites;
    public long PostedSec, SignedSec, DoneSec, DueSec;
    /// <summary>A growth project the contract builds (-1 none).</summary>
    public int GrowthSerial = -1;

    public long TotalMites => MaterialsMites + LabourMites + HaulMites;
    public EntityId Escrow => ConstructionIds.EscrowOfContract(Id);
    public bool Live => State is ContractState.Open or ContractState.Signed;
}

/// <summary>
/// P5-D: a growth project of background growth made a real building site in a chunk the player has seen (spec 4.9), and a clearing shown where the player has been: the project's serial,
/// what it is, its site (none for a clearing) and the rectangle it covers.
/// </summary>
public sealed class GrowthLink
{
    public int Serial;
    public byte Kind;
    public EntityId Site;
    public int Settlement;
    public int X, Y, W, H;
    /// <summary>Clearings (assarts): the work in milli-hours done and needed, as the seasonal step last left them (the street view's progress).</summary>
    public long DoneMilli, NeedMilli;
    public long LinkedSec;
}
