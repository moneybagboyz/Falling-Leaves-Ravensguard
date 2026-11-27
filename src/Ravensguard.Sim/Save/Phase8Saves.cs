namespace Ravensguard.Sim.Save;

/// <summary>
/// The Phase 8 save registry (overview 7, P8-0A). No version bump: v8 stays and 1.0 ships v8. Every addition is an optional section or an additive key, absent while empty, so a world that never used one keeps its bytes.
/// There is no seeding pass: new content is mapped by name by <c>ContentRemap</c> and old worlds keep placer v1.
/// </summary>
public static class Phase8Saves
{
    /// <summary>The one new optional section: <c>ending</c>, written only after an ending is taken.</summary>
    public const string EndingSection = "ending";

    /// <summary>The optional sections of Phase 8.</summary>
    public static readonly string[] SectionNames = [EndingSection];

    /// <summary>The additive keys (existing section, what the new keys hold); each is absent while empty.</summary>
    public static readonly (string Section, string Keys)[] AdditiveKeys =
    [
        ("hero", "ambition history, branch choices"),
        ("autopilot", "the planner's sub-directive and rates"),
        ("sites", "lair kind, legend, met state"),
        ("codex", "manual entries by name"),
        ("onboarding", "hints seen"),
        ("armies", "refugee rows"),
        ("sieges", "assault waves, ladders"),
        ("naval", "captain state"),
        ("battle", "fort walls, chariot cars"),
        ("soldiers", "pen takes"),
    ];

    /// <summary>Whether a save already holds a Phase 8 section.</summary>
    public static bool HasAnySection(IReadOnlyCollection<string> sectionNames) => SectionNames.Any(sectionNames.Contains);
}
