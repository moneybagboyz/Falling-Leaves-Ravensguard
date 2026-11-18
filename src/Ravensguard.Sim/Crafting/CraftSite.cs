using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Crafting;

/// <summary>
/// Where the work is done (spec 4.2, 4.7): the light at the cell (0..255), the qualities in reach (carried tools and the stations and lit fire within 6 m, each at its highest level), the
/// stations present, whether water is near, and which qualities an improvised thing could stand in for (a rock for hammering). A plain value built by the caller, so the screen, the runner,
/// the NPC and the tests all use the same rules.
/// </summary>
public sealed class CraftSite
{
    public int Light = 255;
    public bool Water, Raining;
    public SortedDictionary<string, int> Qualities { get; } = new(StringComparer.Ordinal);
    public SortedSet<string> Stations { get; } = new(StringComparer.Ordinal);
    public SortedSet<string> Improvised { get; } = new(StringComparer.Ordinal);
    /// <summary>+5 when the station offers fine tools (spec 4.3).</summary>
    public int StationBonus;

    public int Level(string quality) => Qualities.GetValueOrDefault(quality);

    public CraftSite AddQuality(string quality, int level) { Qualities[quality] = Math.Max(Level(quality), level); return this; }

    public CraftSite AddTool(RecipeDb db, string toolId)
    {
        foreach (var (q, l) in db.Tools[toolId].Qualities) AddQuality(q, l);
        return this;
    }

    public CraftSite AddStation(RecipeDb db, string stationId)
    {
        Stations.Add(stationId);
        foreach (var (q, l) in db.Stations[stationId].Provides) AddQuality(q, l);
        return this;
    }

    public CraftSite WithImprovised(params string[] qualities) { foreach (var q in qualities) Improvised.Add(q); return this; }

    /// <summary>What a tool requirement gives at this site: not met, met by a real tool (with how many levels above), or met by an improvised thing (level 0).</summary>
    public enum Supply : byte { Missing, Real, Improvised }

    public Supply Check(ToolNeed need, out int over)
    {
        over = 0;
        int have = Level(need.Quality);
        if (have >= need.Level) { over = have - need.Level; return Supply.Real; }
        return Improvised.Contains(need.Quality) ? Supply.Improvised : Supply.Missing;
    }
}

/// <summary>Why a recipe cannot be started, or how well the site serves it.</summary>
public enum LightState : byte { Fine, Dim, TooDark }

/// <summary>One slot of a plan: which alternative is chosen and what is held of each (the screen's have and need counts).</summary>
public sealed class SlotPlan
{
    public int Chosen = -1;
    public long[] Have = [];
    public long[] Need = [];
    public bool Ok => Chosen >= 0;
}

/// <summary>The screen's answer for one recipe and one crafter at one place (spec 4.7): what is missing, how well it will go, how long it takes.</summary>
public sealed class CraftPlan
{
    public CraftRecipe Recipe = null!;
    public int Batch = 1;
    public bool Known = true, CanStart;
    public List<string> Missing = [];
    public SlotPlan[] Slots = [];
    public LightState Light;
    public int Skill, EffectiveSkill, ToolBonus, LightPenalty, FailPm, WastePct, TimePm, ProfPm, ToolPm;
    public long AttendedSeconds, UnattendedSeconds;
    /// <summary>Count of the 41 rolls in each tier.</summary>
    public int[] QualityCounts = new int[5];
    public byte[] Choice = [];
    public bool MissingButGettable;
}
