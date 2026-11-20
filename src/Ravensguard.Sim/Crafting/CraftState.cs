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

    // ---- P5-E (crafting-full 4.1 to 4.7) ----
    /// <summary>Steps left out, one bit per step index (temper skipped; the cold-start heat of a forge already hot).</summary>
    public uint SkipMask;
    /// <summary>Seconds of fetching materials from elsewhere in the building still to walk before the first attended step.</summary>
    public long FetchLeft;
    /// <summary>The score terms fixed at the start (station, material, mastery, field kit) and the smithing process term (its quench part is set at the quench).</summary>
    public int StationBonus, MaterialBonus, MasteryBonus, KitPenalty, ProcessBonus;
    /// <summary>A business station's batch worked live (the bridge, crafting-full 4.4): the business id and the station's index; the batch's goods stay with the station.</summary>
    public EntityId Business;
    public int StationIndex = -1;
    /// <summary>The commission this job makes (crafting-full 4.7), or none.</summary>
    public EntityId Commission;
    /// <summary>A refit's target size: the wearer's body in cm3.</summary>
    public int FitTo;
    public bool Skipped(int step) => step < 32 && (SkipMask & (1u << step)) != 0;
    public int OtherBonus => LightPenalty + StationBonus + MaterialBonus + MasteryBonus + KitPenalty + ProcessBonus;
    public bool StationBound => StationIndex >= 0;
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

    // ---- P5-E (crafting-full 3.4, 4.1, 4.3 to 4.8): saved as additive keys of the "crafting" section ----
    public SortedDictionary<EntityId, Commission> Commissions { get; } = [];
    public SortedDictionary<EntityId, Apprenticeship> Apprenticeships { get; } = [];
    /// <summary>Guild members by (guild template, settlement, person).</summary>
    public SortedDictionary<(string Guild, int Settlement, ulong Person), GuildMember> Members { get; } = new(Comparer<(string, int, ulong)>.Create((a, b) =>
    { int c = string.CompareOrdinal(a.Item1, b.Item1); if (c != 0) return c; c = a.Item2.CompareTo(b.Item2); return c != 0 ? c : a.Item3.CompareTo(b.Item3); }));
    public List<GuildOffence> Offences { get; } = [];
    /// <summary>Where a person has lived since when (guild residence): person -> (settlement, first day).</summary>
    public SortedDictionary<ulong, (int Settlement, int SinceDay)> Residence { get; } = [];
    public FireBook Fires { get; } = new();
    /// <summary>Forges at working heat: the job place (x, y in mm) -> the second it cools.</summary>
    public SortedDictionary<(long X, long Y), long> ForgeHot { get; } = [];
    /// <summary>The size a worn piece was made for, cm3, when a refit changed it (else the clothing book's, or 70,000).</summary>
    public SortedDictionary<EntityId, int> Fits { get; } = [];
    /// <summary>Oiled items and the day their oil is gone (items-quality-and-wear 4.4).</summary>
    public SortedDictionary<EntityId, int> OiledUntil { get; } = [];
    /// <summary>Recipes queued at business stations (crafting-full 4.4).</summary>
    public List<QueuedCraft> Queue { get; } = [];
    /// <summary>Stores emptied inside the abstract tick, taken out of the economy at the next barrier.</summary>
    public SortedSet<EntityId> Dead { get; } = [];

    public bool IsEmpty => _jobs.Count == 0 && Book.Count == 0 && Records.Count == 0 && Sessions.Count == 0 && Artefacts.Count == 0 && Know.RecipeRows().Length == 0 && Know.ProfRows().Length == 0 && Know.WatchRows().Length == 0
        && Commissions.Count == 0 && Apprenticeships.Count == 0 && Members.Count == 0 && Offences.Count == 0 && Residence.Count == 0 && Fires.Count == 0 && ForgeHot.Count == 0 && Fits.Count == 0
        && OiledUntil.Count == 0 && Queue.Count == 0 && Dead.Count == 0;

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
