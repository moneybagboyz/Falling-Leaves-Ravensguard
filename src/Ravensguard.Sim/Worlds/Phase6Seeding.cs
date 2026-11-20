using Ravensguard.Content;
using Ravensguard.Sim.Save;

namespace Ravensguard.Sim.Worlds;

/// <summary>What a Phase 6 seeding step sees: the world's state, the content, the world seed and whether the world is being loaded from a save that has none of the Phase 6 sections.</summary>
public sealed record Phase6SeedContext(WorldState State, ContentDatabase? Content, ulong Seed, bool FromSave);

/// <summary>
/// One derivation of the Phase 6 seeding pass (overview 7): religions (pure), fief deeds from the Phase 1 lords, offices bound to people, physicians and barbers (0x600), honesty (0x540),
/// lord traits (0x670), empty standing. A step is a pure function of the world seed and ids (never the day it runs, never the live tick), so a Phase 5 save loaded today and a new game of
/// the same world derive the same state; it runs only when its own <see cref="Section"/> is absent from the save, so what play has changed since is never derived again.
/// </summary>
public interface IPhase6SeedStep
{
    /// <summary>A short stable name for the load report and the logs.</summary>
    string Name { get; }
    /// <summary>The optional save section whose presence says this step's state is already saved (one of <see cref="Phase6Seeding.SectionNames"/>).</summary>
    string Section { get; }
    /// <summary>Derives the state into <see cref="Phase6SeedContext.State"/>; returns whether it changed anything.</summary>
    bool Seed(Phase6SeedContext ctx);
}

/// <summary>
/// The Phase 6 seeding pass skeleton (overview 7, P6-A2): runs at a new game after the population (<see cref="NewGameWorld.Create"/>) and at the first load of a save that lacks the Phase 6
/// sections (<see cref="WorldSave.Restore"/>: every v8 save from Phase 5). Later parts register their steps in <see cref="DefaultSteps"/> in the order listed in <see cref="PlannedOrder"/>;
/// until a step is registered the pass changes nothing and adds nothing to the load report, so a Phase 5 save keeps its bytes. No save version bump (v8 stays).
/// </summary>
public static class Phase6Seeding
{
    /// <summary>The optional sections of Phase 6 (overview 7): absent when empty, so a world that never used them keeps its bytes.</summary>
    public static readonly string[] SectionNames = ["medicine", "standing", "law", "bondage", "family", "faith", "fiefs", "diplomacy", "unrest", "contracts", "fire", "disease", "heists"];

    /// <summary>The order the steps are meant to run in once registered (religions first, standing last); a step derives only from earlier steps' state, the seed and ids.</summary>
    public static readonly string[] PlannedOrder = ["religions", "fiefs", "offices", "physicians", "honesty", "lord_traits", "standing"];

    /// <summary>The note the load report shows when a pass derived something for a loaded save (overview 7).</summary>
    public const string LoadNote = "Phase 6 state derived at load";

    /// <summary>The registered steps (physicians at P6-B1); each Phase 6 part adds its own (kept in <see cref="PlannedOrder"/> order).</summary>
    public static readonly IReadOnlyList<IPhase6SeedStep> DefaultSteps = [new Ravensguard.Sim.Faith.FaithSeedStep(), new Ravensguard.Sim.Rule.RuleSeedStep(), new Ravensguard.Sim.Medicine.PhysicianSeedStep()];

    /// <summary>Whether a save already holds any Phase 6 section.</summary>
    public static bool HasAnySection(IReadOnlyCollection<string> sectionNames) => SectionNames.Any(sectionNames.Contains);

    /// <summary>
    /// Runs the pass. <paramref name="presentSections"/> is the save's section names (null for a new game: nothing is saved yet). Returns the notes for the load report: one line,
    /// <see cref="LoadNote"/>, when a loaded save had something derived; none for a new game or when no step changed anything.
    /// </summary>
    public static IReadOnlyList<string> Run(WorldState state, ContentDatabase? content, IReadOnlyCollection<string>? presentSections, IReadOnlyList<IPhase6SeedStep>? steps = null)
    {
        steps ??= DefaultSteps;
        if (steps.Count == 0) return [];
        bool fromSave = presentSections != null;
        var ctx = new Phase6SeedContext(state, content, state.Seed, fromSave);
        bool any = false;
        foreach (var step in steps)
        {
            if (presentSections != null && presentSections.Contains(step.Section)) continue;      // its state is saved: never derived again
            any |= step.Seed(ctx);
        }
        return fromSave && any ? [LoadNote] : [];
    }
}
