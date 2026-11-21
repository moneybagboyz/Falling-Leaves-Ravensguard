using Ravensguard.Content;
using Ravensguard.Sim.Save;

namespace Ravensguard.Sim.Worlds;

/// <summary>What a Phase 7 seeding step sees: the world's state, the content, the world seed and whether the world is being loaded from a save that has none of the Phase 7 sections.</summary>
public sealed record Phase7SeedContext(WorldState State, ContentDatabase? Content, ulong Seed, bool FromSave);

/// <summary>
/// One derivation of the Phase 7 seeding pass (overview 7): the mint per realm, money changers, the armoury keep and granary depot at each seat, lords' warbands, mercenary companies (0x7B1) and harbours.
/// Same contract as <see cref="IPhase6SeedStep"/>: a pure function of the world seed and ids (never the day it runs, never the live tick), run only when its own <see cref="Section"/> is absent from the save.
/// </summary>
public interface IPhase7SeedStep
{
    /// <summary>A short stable name for the load report and the logs.</summary>
    string Name { get; }
    /// <summary>The optional save section whose presence says this step's state is already saved (one of <see cref="Phase7Seeding.SectionNames"/>).</summary>
    string Section { get; }
    /// <summary>Derives the state into <see cref="Phase7SeedContext.State"/>; returns whether it changed anything.</summary>
    bool Seed(Phase7SeedContext ctx);
}

/// <summary>
/// The Phase 7 seeding pass skeleton (overview 7, P7-0A), appended to the Phase 6 pass at <see cref="NewGameWorld.Create"/> and at the end of <see cref="WorldSave.Restore"/> (the first load of a save
/// without the Phase 7 sections). Later parts register their steps in <see cref="DefaultSteps"/> in <see cref="PlannedOrder"/> order; until then the pass changes nothing and adds nothing to the load report,
/// so a Phase 6 save keeps its bytes. No save version bump (v8 stays).
/// </summary>
public static class Phase7Seeding
{
    /// <summary>The ten optional sections of Phase 7 (overview 7): absent while empty, so a world that never used them keeps its bytes. Economy first, then military.</summary>
    public static readonly string[] SectionNames = ["coinage", "scale", "works", "industry", "warsupply", "battle", "armies", "soldiers", "sieges", "naval"];

    /// <summary>The order the steps are meant to run in once registered (overview 7): money, civic buildings, warbands, companies, harbours.</summary>
    public static readonly string[] PlannedOrder = ["mints", "changers", "armoury_depots", "warbands", "companies", "harbours"];

    /// <summary>The note the load report shows when a pass derived something for a loaded save (overview 7).</summary>
    public const string LoadNote = "Phase 7 state derived at load";

    /// <summary>The registered steps (none at P7-0A); each Phase 7 part adds its own, kept in <see cref="PlannedOrder"/> order.</summary>
    public static readonly IReadOnlyList<IPhase7SeedStep> DefaultSteps = [];

    /// <summary>Whether a save already holds any Phase 7 section.</summary>
    public static bool HasAnySection(IReadOnlyCollection<string> sectionNames) => SectionNames.Any(sectionNames.Contains);

    /// <summary>Runs the pass; <paramref name="presentSections"/> is the save's section names (null for a new game). Returns one note, <see cref="LoadNote"/>, when a loaded save had something derived; none otherwise.</summary>
    public static IReadOnlyList<string> Run(WorldState state, ContentDatabase? content, IReadOnlyCollection<string>? presentSections, IReadOnlyList<IPhase7SeedStep>? steps = null)
    {
        steps ??= DefaultSteps;
        if (steps.Count == 0) return [];
        bool fromSave = presentSections != null;
        var ctx = new Phase7SeedContext(state, content, state.Seed, fromSave);
        bool any = false;
        foreach (var step in steps)
        {
            if (presentSections != null && presentSections.Contains(step.Section)) continue;      // its state is saved: never derived again
            any |= step.Seed(ctx);
        }
        return fromSave && any ? [LoadNote] : [];
    }
}
