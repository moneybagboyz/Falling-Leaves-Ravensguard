using Ravensguard.Sim.Codex;

namespace Ravensguard.Sim.Dialogue;

/// <summary>A legend a villager tells: its Codex page's id, title and text.</summary>
public sealed record LegendTold(string Id, string Title, string Text);

/// <summary>
/// The legends fallback (contracts-and-rumours-v2 4.5): a person who has no fresh rumour to tell when the hero asks for the news tells a legend of a place within ten kilometres of where they live, from the
/// history's Codex (the legend a landmark hangs on, Phase 1). No dice: the legends near are walked nearest first (ties by id) and the person's own index picks one, so the same person tells the same tale.
/// Telling it opens the Codex page, as seeing the landmark would.
/// </summary>
public static class Legends
{
    public const int RadiusM = 10_000;

    /// <summary>The legends within <paramref name="radiusM"/> of the place (region metres), nearest landmark first, one entry per legend.</summary>
    public static List<CodexEntry> Near(CodexBook codex, int xM, int yM, int radiusM = RadiusM)
    {
        var found = new List<(long D2, CodexEntry E)>(); var seen = new HashSet<string>(StringComparer.Ordinal); long r2 = (long)radiusM * radiusM;
        foreach (var (e, lx, ly) in codex.LandmarkPlaces)
        {
            long dx = lx - xM, dy = ly - yM, d2 = dx * dx + dy * dy;
            if (d2 > r2) continue;
            foreach (var src in e.Sources)
                if (codex.Get("legend." + src.Counter) is { } leg && seen.Add(leg.Id)) found.Add((d2, leg));
        }
        return [.. found.OrderBy(f => f.D2).ThenBy(f => f.E.Id, StringComparer.Ordinal).Select(f => f.E)];
    }

    /// <summary>The legend a person tells (<paramref name="person"/> is a stable index, e.g. the person's table row), or null when no legend lies within reach. Unlocks the page.</summary>
    public static LegendTold? Tell(CodexBook codex, int xM, int yM, int person, int radiusM = RadiusM)
    {
        var near = Near(codex, xM, yM, radiusM); if (near.Count == 0) return null;
        var leg = near[(int)((uint)person % (uint)near.Count)];
        codex.Unlock(leg.Id);
        return new LegendTold(leg.Id, leg.Title, leg.Text);
    }
}
