using MessagePack;
using Ravensguard.Sim.Combat.Wounds;

namespace Ravensguard.Sim.Medicine;

/// <summary>
/// One queued piece of work of the hero's treatment (medicine 4.1): who, what, where, when it began and the game second it ends. <see cref="Kind"/> is a <see cref="TreatKind"/>, or
/// <see cref="TreatQueue.DressAllKind"/> for a "dress all bleeding" that has not yet picked its next bandage (it picks it when its turn comes, as the field dressing always did, so the choice
/// is made on the blood the patient has then). A chain of dressings carries <see cref="Remaining"/>, the number still to do including this one.
/// </summary>
[MessagePackObject]
public sealed class TreatStep
{
    [Key(0)] public ulong Patient { get; set; }
    [Key(1)] public byte Kind { get; set; }
    [Key(2)] public int Part { get; set; }
    [Key(3)] public int Remaining { get; set; }
    [Key(4)] public long StartSec { get; set; }
    /// <summary>The game second the work ends (when it is performed); 0 while a dress-all has not picked its dressing.</summary>
    [Key(5)] public long FinishSec { get; set; }
    [Key(6)] public ulong Actor { get; set; }
    [Key(7)] public bool Light { get; set; } = true;
    [Key(8)] public bool Chain { get; set; }

    [IgnoreMember] public bool Planned => FinishSec > 0;
    [IgnoreMember] public TreatStep Copy => (TreatStep)MemberwiseClone();
}

/// <summary>What became of one step, for the screen's log (not saved): the second, the work, whether it was done and whether it worked.</summary>
public readonly record struct TreatOutcome(long Sec, ulong Patient, TreatKind Kind, int Part, bool Done, bool Success, string Why);

/// <summary>
/// The hero's queue of treatments (medicine 4.1, MT-5, MT-13): steps in order, each begun when the one before ends, each performed at the second its work ends. Moving more than a metre,
/// starting a fight or being hit cancels what is left (<see cref="Cancel"/>); the items of a cancelled step were never taken. Saved in the optional <c>medicine</c> section, so a save made
/// mid-queue comes back with the same steps (the screen keeps its own timer from them).
/// </summary>
public sealed class TreatQueue
{
    public const byte DressAllKind = 200;
    public const int MaxSteps = 12;
    private readonly List<TreatStep> _steps = [];
    private readonly List<TreatOutcome> _log = [];

    public IReadOnlyList<TreatStep> Steps => _steps;
    public IReadOnlyList<TreatOutcome> Log => _log;
    public int Count => _steps.Count;
    public bool IsEmpty => _steps.Count == 0;
    /// <summary>The game second the last queued step ends (a new step begins then), or 0 when the queue is empty.</summary>
    public long EndSec { get { long t = 0; foreach (var s in _steps) t = Math.Max(t, s.Planned ? s.FinishSec : s.StartSec); return t; } }

    public bool Add(TreatStep s) { if (_steps.Count >= MaxSteps) return false; _steps.Add(s); return true; }

    /// <summary>Drops every step still waiting; returns how many.</summary>
    public int Cancel() { int n = _steps.Count; _steps.Clear(); return n; }

    internal TreatStep? Head => _steps.Count > 0 ? _steps[0] : null;
    internal void PopHead() => _steps.RemoveAt(0);
    internal void PushHead(TreatStep s) => _steps.Insert(0, s);
    internal void Note(TreatOutcome o) { _log.Add(o); if (_log.Count > 24) _log.RemoveAt(0); }

    public TreatStep[] Capture() => [.. _steps.Select(s => s.Copy)];

    public static TreatQueue Restore(IEnumerable<TreatStep>? rows)
    {
        var q = new TreatQueue();
        if (rows != null) foreach (var r in rows) q._steps.Add(r.Copy);
        return q;
    }

    public ulong Hash(ulong h = 14695981039346656037UL)
    {
        void L(long v) { for (int i = 0; i < 8; i++) { h ^= (byte)(v >> (i * 8)); h *= 1099511628211UL; } }
        foreach (var s in _steps) { L((long)s.Patient); L(s.Kind); L(s.Part); L(s.Remaining); L(s.StartSec); L(s.FinishSec); L((long)s.Actor); L(s.Light ? 1 : 0); L(s.Chain ? 1 : 0); }
        return h;
    }
}
