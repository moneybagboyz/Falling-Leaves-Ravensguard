using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Live;

/// <summary>What a person is doing this hour (abstract spec 2.3). Append only.</summary>
public enum JobKind : byte { Sleep, Home, Commute, Work, Shop, Eat, Church, Forage, Travel, Haul, GuardCaravan, Idle }

/// <summary>Where a job is done: a settlement's lot (door or inside), or a spot in region metres (a field tile, the square).</summary>
public readonly record struct JobPlace(int Settlement, int Lot, int XM, int YM);

/// <summary>
/// The abstract tier's answer to "what is this person doing, and since when" (abstract spec 2.3). It is a pure function of the person's records and the game
/// second (<see cref="Schedule.Resolve"/>), so there is nothing to save and nothing that can drift; the live tier reads it on promotion and the bridge re-derives
/// it on demotion (fold-back, <see cref="AbstractJobTable"/>). <see cref="ProgressPpm"/> is how far through the block the person is (for a commute: along its
/// <see cref="PathPlan"/>).
/// </summary>
public readonly record struct AbstractJob(JobKind Kind, JobPlace Place, long StartSec, long EndSec, int ProgressPpm)
{
    public long StartHour => StartSec / TimeConst.SecondsPerHour;
}

/// <summary>
/// Per region: the job each person was left with when the live tier gave them back (fold-back, abstract spec 3.6; G-A5: re-derived from now, so
/// <see cref="AbstractJob.StartSec"/> is the block start the live position maps to). Written only at a barrier, never saved (it is derived state: a loaded game derives the same).
/// </summary>
public sealed class AbstractJobTable
{
    private readonly SortedDictionary<int, AbstractJob> _folded = [];
    public IReadOnlyDictionary<int, AbstractJob> Folded => _folded;
    public int FoldBacks { get; private set; }
    /// <summary>Game seconds of work the live tier has watched people do (the abstract tick has already credited it; a later phase's leased stations will credit it here).</summary>
    public long LiveWorkSec { get; private set; }
    public void AddLiveWork(long sec) => LiveWorkSec += sec;
    public void Set(int person, AbstractJob job) { _folded[person] = job; FoldBacks++; }
    public bool TryGet(int person, out AbstractJob job) => _folded.TryGetValue(person, out job);
}
