using Ravensguard.Sim.Combat.Resolver2;
using static Ravensguard.Sim.Combat.Wounds.MedicineTables;

namespace Ravensguard.Sim.Combat.Wounds;

/// <summary>One dressing of <see cref="FieldDressing.Dress"/>: what was done, on which part, and the game second it was finished.</summary>
public readonly record struct DressStep(TreatKind Kind, int Part, long DoneSec);

/// <summary>What a field dressing did and used: the steps in order, the bandages and rope taken, and when the last one was finished.</summary>
public sealed record DressOutcome(IReadOnlyList<DressStep> Steps, int BandagesUsed, int RopeUsed, long EndSec, ulong Actor)
{
    public static readonly DressOutcome None = new([], 0, 0, 0, 0);
}

/// <summary>
/// The stop-gap until the Phase 5 treatment screen (docs/reviews/phase-4.md, "auto-dress after fights", approved by the user): when a fight ends the hero (or, when he is down, a
/// conscious companion) dresses his bleeding wounds, worst first by rate (<see cref="Treatment.WorstBleeder"/>), with what the pack holds: a tourniquet on a limb whose wound is
/// arterial when the pack has rope or two bandages for it (the treatment's own needs), else a bandage; with no bandages nothing is done. Each dressing takes its real time
/// (<see cref="Treatment.SimSeconds"/>) and is performed at the second it is finished, so the blood lost meanwhile is lost; the rules, skill floors and timings are
/// <see cref="Treatment"/>'s. Pure on the book: the caller supplies the counts and takes the outcome's use out of the pack.
/// </summary>
public static class FieldDressing
{
    /// <summary>At most this many dressings in one go (four bleeders bound are a long two minutes already).</summary>
    public const int MaxSteps = 4;
    /// <summary>The rope a tourniquet takes from the pack, grams.</summary>
    public const int TourniquetRopeG = 200;

    public static DressOutcome Dress(WoundBook b, BodyPlanV2 plan, int bandages, bool rope, int skill, ulong actor, long startSec, int max = MaxSteps)
    {
        if (b.Dead || bandages <= 0 && !rope) return DressOutcome.None;
        var bag = new Bag(bandages, rope);
        var steps = new List<DressStep>();
        long t = startSec;
        for (int n = 0; n < max; n++)
        {
            int i = Treatment.WorstBleeder(b, -1, t);
            if (i < 0) break;
            var w = b.Records[i];
            int root = TreatAi.LimbRoot(plan, w.Part);
            TreatKind k; int part;
            if (root >= 0 && w.Has(WoundFlags.ArteryHit) && (bag.Has("rope", 1) || bag.Has("bandage", 2))) { k = TreatKind.Tourniquet; part = root; }
            else if (bag.Has("bandage", 1)) { k = TreatKind.Bandage; part = w.Part; }
            else break;
            long done = t + (long)Treatment.SimSeconds(k) * TicksPerSimS;
            var r = Treatment.Perform(b, plan, new TreatRequest(k, part, done, skill, actor), bag);
            if (!r.Ok) break;
            steps.Add(new DressStep(k, part, done));
            t = done;
        }
        return steps.Count == 0 ? DressOutcome.None : new DressOutcome(steps, bandages - bag.Bandages, bag.RopeUsed, t, actor);
    }

    private sealed class Bag(int bandages, bool rope) : ITreatItems
    {
        public int Bandages = bandages, RopeUsed;
        public bool Has(string item, int count) => item == "bandage" ? Bandages >= count : item == "rope" && rope && RopeUsed == 0 && count <= 1;
        public void Take(string item, int count) { if (item == "bandage") Bandages = Math.Max(0, Bandages - count); else if (item == "rope") RopeUsed += count; }
    }
}
