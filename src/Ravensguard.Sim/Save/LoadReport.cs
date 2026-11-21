using System.Text;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Save;

/// <summary>
/// What loading a save (or opening a library world) made with other content would do, worked out before anything is loaded (plan §2.11: "if content or mods are missing, a readable
/// report lists what's affected before loading"). A save is refused only when it cannot be loaded faithfully (<see cref="Refusals"/>, each naming its cause): a world layer that no
/// longer regenerates identically (and is not a snapshot), content the save uses that the game no longer has and no migration names, or a save from a newer game.
/// Anything else loads, and the changes (goods written off, recipes moved, mods added or gone) are listed so the player can choose to continue or cancel.
/// </summary>
public sealed class LoadReport
{
    /// <summary>The game content (every mod in it) is not the content the save or world was made with.</summary>
    public bool ContentChanged { get; init; }
    /// <summary>The save is from an older format and is upgraded when read (always done; nothing to decide).</summary>
    public bool NeedsMigration { get; init; }
    public IReadOnlyList<ModEntry> MissingMods { get; init; } = [];
    public IReadOnlyList<ModChange> ChangedMods { get; init; } = [];
    public IReadOnlyList<ModEntry> ExtraMods { get; init; } = [];
    /// <summary>What <see cref="ContentRemap"/> will do: tables changed, goods written off, records dropped (its notes, in order).</summary>
    public IReadOnlyList<string> Changes { get; init; } = [];
    /// <summary>Whether the regenerated layers (continent, history, region, landmarks, the people they are diffed against) were compared with the save's hashes. Done when the content changed.</summary>
    public bool LayersChecked { get; init; }
    /// <summary>Why the load would be refused, one cause each. Empty when the load can go ahead.</summary>
    public IReadOnlyList<string> Refusals { get; init; } = [];

    /// <summary>
    /// P7-CN1 (coinage 2.3): informational notices that never ask for confirmation (<see cref="NeedsConfirmation"/> ignores them): a save written before Phase 7 is told that money now shows as gold, silver and copper and that nothing in the save changed.
    /// </summary>
    public IReadOnlyList<string> Notices { get; init; } = [];

    /// <summary>The first game version that writes Phase 7 saves (the build's major.minor); a header below it is an older game's.</summary>
    public const string Phase7FirstVersion = "0.4.0";

    /// <summary>The coinage notice (coinage 2.3).</summary>
    public const string CoinageNotice = "Money is now shown as gold, silver and copper. One old crown is one silver piece (ten copper). Nothing in this save changed.";

    /// <summary>Whether a save's header version is older than <see cref="Phase7FirstVersion"/>: an unreadable version counts as older (the game wrote "0.1" for a long time).</summary>
    public static bool IsBeforePhase7(string gameVersion)
    {
        static bool Parse(string v, out int major, out int minor)
        {
            major = minor = 0; var parts = v.Split('.', '-', ' ');
            return parts.Length >= 2 && int.TryParse(parts[0], out major) && int.TryParse(parts[1], out minor);
        }
        Parse(Phase7FirstVersion, out int fm, out int fn);
        if (!Parse(gameVersion ?? "", out int m, out int n)) return true;
        return m < fm || (m == fm && n < fn);
    }

    public bool CanLoad => Refusals.Count == 0;
    /// <summary>Whether the player is asked before loading: something differs from the content the save was made with.</summary>
    public bool NeedsConfirmation => CanLoad && (ContentChanged || MissingMods.Count > 0 || ChangedMods.Count > 0 || ExtraMods.Count > 0 || Changes.Count > 0);

    /// <summary>A world of the library: made with other content when its stored hash differs. Its continent and history regenerate from the settings and are verified when it opens.</summary>
    public static LoadReport ForWorld(WorldFile world, string currentContentHash)
    {
        bool changed = world.ContentHash.Length > 0 && world.ContentHash != currentContentHash;
        return new LoadReport
        {
            ContentChanged = changed,
            Changes = changed ? ["This world was made with other game content. Its land and history are made again from its settings and checked as it opens; it is refused, naming the cause, if they would differ."] : [],
        };
    }

    public string ToText()
    {
        var sb = new StringBuilder();
        foreach (var r in Refusals) sb.AppendLine("Cannot load: " + r);
        if (NeedsMigration) sb.AppendLine("This save is from an older version and is upgraded as it loads.");
        if (ContentChanged) sb.AppendLine("The game content changed since this was made.");
        foreach (var m in MissingMods) sb.AppendLine($"Missing mod: {m.Id} (version {m.Version}). What it added is no longer in the game.");
        foreach (var m in ChangedMods) sb.AppendLine($"Mod changed: {m.Id} (made with {m.SavedVersion}, installed {m.InstalledVersion}).");
        foreach (var m in ExtraMods) sb.AppendLine($"New mod not in this save: {m.Id} ({m.Version}).");
        foreach (var c in Changes) sb.AppendLine(c);
        foreach (var n in Notices) sb.AppendLine(n);
        if (CanLoad && LayersChecked) sb.AppendLine("The world, its history and its people still regenerate exactly as saved.");
        if (sb.Length == 0) sb.AppendLine("Nothing changed.");
        return sb.ToString().TrimEnd();
    }
}
