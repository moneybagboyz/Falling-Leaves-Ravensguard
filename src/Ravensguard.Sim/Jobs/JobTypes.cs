using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;

namespace Ravensguard.Sim.Jobs;

/// <summary>The kinds of paid work a person can take (live-behaviour spec 4.6). Append only.</summary>
public enum OddJobKind : byte { Haul, Guard, Escort, TendShop, FarmWork, Deliver, Fetch, Craft,
    /// <summary>P5-F (businesses spec 4.3): a day's work taken at the hiring hour in the square (the hiring hour fills and pays them at once; transient). Appended.</summary>
    DayLabour,
    /// <summary>Phase 6 (overview 8.1, append only): a paid hand at a fire (fire and burning 4.4). Nothing writes it until B5.</summary>
    FightFire,
    /// <summary>Phase 6 (crime-and-justice 4.3, P6-B2): a bounty on a culprit, its coin escrowed from the fief treasury; nobody works it, whoever brings the culprit in is paid by <c>JobTable.Settle</c>. Appended.</summary>
    Bounty }

/// <summary><c>Open -> Claimed -> Active -> Done | Failed | Expired | Cancelled</c>.</summary>
public enum JobState : byte { Open, Claimed, Active, Done, Failed, Expired, Cancelled }

/// <summary>What a reservation holds (live-behaviour spec 3.3).</summary>
public enum ReserveKind : byte { Bed, Tile, Station, Item, Stock, Cart, Animal, Seat }

/// <summary>The steps of a job, in the order a worker does them. A phase is where the worker is going or what they do there.</summary>
public enum PhaseKind : byte { Go, Load, Carry, Unload, Report, Work, Handover }

/// <summary>
/// Which ledger reason each coin move of a job uses. The economy's own enum has no job reasons yet (its files belong to the economy owner), so all three map to the nearest existing ones; switching them to
/// dedicated <c>JobEscrow</c>, <c>JobPay</c>, <c>JobRefund</c> values later is a change in this one place.
/// </summary>
public static class JobReasons
{
    public static readonly TransferReason Escrow = TransferReason.Wage, Pay = TransferReason.Wage, Refund = TransferReason.Gift;
}

/// <summary>
/// One odd job (live-behaviour spec 3.2). Params by kind, positions in region millimetres: Haul and Deliver and Fetch <c>P0,P1</c> from, <c>P2,P3</c> to, <c>P4</c> good id, <c>P5</c> grams
/// (<see cref="SrcOwner"/> and <see cref="DstOwner"/> own the stores); Guard <c>P0,P1</c> post, <c>P4</c> hours; Escort <c>P0,P1</c> meeting point, <c>P2,P3</c> destination, <c>P4</c> hours of the trip;
/// TendShop <c>P0,P1</c> stall, <c>P4</c> hours; FarmWork <c>P0,P1</c> the tile, <c>P4</c> hours, <c>P5</c> the farm task the hours are credited to.
/// </summary>
public sealed class Job
{
    public EntityId Id;
    public OddJobKind Kind;
    public JobState State;
    public EntityId Poster;
    public int PosterPurse = -1;
    public int Region, Settlement;
    public byte Slots = 1;
    public long P0, P1, P2, P3, P4, P5;
    public EntityId SrcOwner, DstOwner;
    public long WageMites;
    public int Escrow = -1;
    public long PostedSec, DeadlineSec;
    public byte MinSkill, MinAge;
    public EntityId Worker;
    public int WorkerIndex = -1;
    public long ClaimedSec, StartX, StartY;
    /// <summary>The steps whose effects (a store move, the farm credit) have been applied.</summary>
    public int Applied;
    public long PaidMites, RefundedMites;
    /// <summary>Worker hours credited to the abstract tier as WorkDone, thousandths of an hour.</summary>
    public long WorkMilliHours;
    public string Title = "";

    public bool Live => State is JobState.Claimed or JobState.Active;
    public bool Closed => State is JobState.Done or JobState.Failed or JobState.Expired or JobState.Cancelled;
}

/// <summary>One leg of a job's day: how long it takes (game seconds) and where it goes (region millimetres).</summary>
public readonly record struct Phase(PhaseKind Kind, int DurSec, long X0, long Y0, long X1, long Y1);

/// <summary>A command from a player or NPC: take this job. Applied at the next barrier in <c>(Frame, Seq)</c> order, ties broken by the claim stream.</summary>
public readonly record struct ClaimRequest(EntityId Job, EntityId Person, int PersonIndex, long Frame, int Seq, long X, long Y, int Skill, int Age)
{
    /// <summary>Who asked: a player, the autopilot layer or an NPC (the command log tells them apart; the claim is resolved the same way).</summary>
    public CommandOrigin Origin { get; init; }
}

/// <summary>Where completed work is credited. The abstract tier's labour book reads it: the live tier never writes worker-hours anywhere else, so work is counted once.</summary>
public interface IWorkSink
{
    void WorkDone(long taskId, long workerMilliHours);
}

/// <summary>A sink that only adds up (tests, and a world whose labour book is not wired).</summary>
public sealed class WorkLog : IWorkSink
{
    public SortedDictionary<long, long> ByTask { get; } = [];
    public long Total { get; private set; }
    public void WorkDone(long taskId, long workerMilliHours) { ByTask[taskId] = ByTask.GetValueOrDefault(taskId) + workerMilliHours; Total += workerMilliHours; }
}
