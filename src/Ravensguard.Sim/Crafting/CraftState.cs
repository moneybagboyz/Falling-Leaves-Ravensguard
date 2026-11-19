using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Crafting;

/// <summary><c>Active</c> being worked, <c>Paused</c> put down with its progress kept, <c>Waiting</c> in an unattended step, then <c>Done</c> or <c>Ruined</c>.</summary>
public enum CraftJobState : byte { Active, Paused, Waiting, Done, Ruined }

/// <summary>
/// An unfinished piece of work in the world (spec 3.3): the recipe, who is at it, how far it has got and the components in its own store (an entry of the economy's other stores keyed by the job id,
/// so the goods are physical and counted). Anyone who knows the recipe can pick it up; the result goes to whoever finishes it.
/// </summary>
public sealed class CraftJob
{
    public EntityId Id;
    public ushort Recipe;
    public EntityId Crafter, Owner;
    public long PlaceX, PlaceY;
    public int Batch = 1;
    public int StepIndex;
    /// <summary>Seconds worked inside the current step, and in all.</summary>
    public long StepSeconds, TotalSeconds;
    public CraftJobState State;
    public long StartedSec, ReadySec, DeadlineSec, RuinSec;
    /// <summary>Which alternative of each component slot was taken.</summary>
    public byte[] Choice = [];
    /// <summary>The time factors fixed at the start (permille): proficiencies, tool and skill.</summary>
    public int ProfPm = 1000, ToolPm = 1000, SkillPm = 1000;
    public int EffSkill, ToolBonus, LightPenalty;
    /// <summary>Tiers lost to lateness in an unattended step.</summary>
    public int TierDrop;
    /// <summary>Percent of the components a failure wasted (0 when nothing failed); the yield of goods is what is left.</summary>
    public int WastePct;
    public bool Failed;
    /// <summary>The item a mending recipe works on.</summary>
    public EntityId Target;
    /// <summary>Bits of the alerts already given: 1 ready, 2 past the deadline, 4 about to be ruined.</summary>
    public byte AlertMask;
    public bool Rained;
    public bool Closed => State is CraftJobState.Done or CraftJobState.Ruined;
}

/// <summary>What a crafted item was made of (spec 7 K-19): the recipe and the components it consumed per unit, so taking it apart can give them back.</summary>
public sealed record CraftedRecord(ushort Recipe, (ushort Good, long Qty)[] Parts);

public enum CraftEventKind : byte { StepDone, Waiting, Ready, PastDeadline, Ruining, Ruined, Failed, Finished, Learned, SkillGain }

/// <summary>Something that happened to a job or a learner; the host turns these into HUD alerts and toasts ("Your dough is ready").</summary>
public readonly record struct CraftEvent(CraftEventKind Kind, EntityId Job, EntityId Who, long AtSec, string Text = "", int Value = 0);

/// <summary>The crafting part of the world state (saved in the "crafting" section): jobs, skills by use, what people know, the record of what items were made of, and teaching under way.</summary>
public sealed class CraftState
{
    private readonly SortedDictionary<EntityId, CraftJob> _jobs = [];
    public IReadOnlyDictionary<EntityId, CraftJob> Jobs => _jobs;
    public SkillBook Book { get; } = new();
    public Knowledge Know { get; } = new();
    public SortedDictionary<EntityId, CraftedRecord> Records { get; } = [];
    public List<TeachSession> Sessions { get; } = [];
    /// <summary>P5-A1: the named artefacts of the world (items-quality-and-wear 4.2).</summary>
    public ArtefactBook Artefacts { get; } = new();

    public bool IsEmpty => _jobs.Count == 0 && Book.Count == 0 && Records.Count == 0 && Sessions.Count == 0 && Artefacts.Count == 0 && Know.RecipeRows().Length == 0 && Know.ProfRows().Length == 0 && Know.WatchRows().Length == 0;

    public CraftJob? Get(EntityId id) => _jobs.GetValueOrDefault(id);
    public void Add(CraftJob j) => _jobs[j.Id] = j;
    public void Remove(EntityId id) => _jobs.Remove(id);

    /// <summary>The open jobs within <paramref name="radiusMm"/> of a place, in id order (the "Resume" list of the screen).</summary>
    public IEnumerable<CraftJob> Nearby(long x, long y, long radiusMm)
        => _jobs.Values.Where(j => !j.Closed && Math.Abs(j.PlaceX - x) <= radiusMm && Math.Abs(j.PlaceY - y) <= radiusMm);

    /// <summary>The unattended jobs' due times: each Waiting job's next alert or ruin, ordered; derived from the jobs, never saved.</summary>
    public IEnumerable<(long At, EntityId Job, CraftEventKind Kind)> DueQueue()
    {
        var q = new SortedSet<(long, EntityId, CraftEventKind)>();
        foreach (var j in _jobs.Values)
        {
            if (j.State != CraftJobState.Waiting) continue;
            if ((j.AlertMask & 1) == 0) q.Add((j.ReadySec, j.Id, CraftEventKind.Ready));
            if ((j.AlertMask & 2) == 0) q.Add((j.DeadlineSec, j.Id, CraftEventKind.PastDeadline));
            if ((j.AlertMask & 4) == 0) q.Add((Math.Max(j.ReadySec, j.RuinSec - 3600), j.Id, CraftEventKind.Ruining));
            q.Add((j.RuinSec + 1, j.Id, CraftEventKind.Ruined));
        }
        return q;
    }
}
