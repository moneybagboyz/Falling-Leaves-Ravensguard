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
    /// <summary>P5-E (crafting-full 4.3): the forge here is already at working heat, so a smithing recipe's cold-start heat step is left out.</summary>
    public bool ForgeHot;
    public SortedDictionary<string, int> Qualities { get; } = new(StringComparer.Ordinal);
    public SortedSet<string> Stations { get; } = new(StringComparer.Ordinal);
    public SortedSet<string> Improvised { get; } = new(StringComparer.Ordinal);
    /// <summary>+5 when the station offers fine tools (spec 4.3).</summary>
    public int StationBonus;
    /// <summary>P5-E (crafting-full 4.2): the best tier of each station in reach (the station bonus reads it; a station added without one is Common).</summary>
    public SortedDictionary<string, ItemQuality> StationTiers { get; } = new(StringComparer.Ordinal);
    /// <summary>P5-E: the levels supplied by something other than a field kit (a kit's qualities give the -5 penalty when nothing better is in reach).</summary>
    public SortedDictionary<string, int> RealQualities { get; } = new(StringComparer.Ordinal);
    /// <summary>P5-E (items-quality-and-wear 4.3): qualities whose best real tool is blunt (condition below 300): tool bonus -3.</summary>
    public SortedSet<string> Blunt { get; } = new(StringComparer.Ordinal);
    /// <summary>P5-E: the qualities each station gives, so the bonus can follow the station that supplies a need.</summary>
    public SortedDictionary<string, string> SupplierStation { get; } = new(StringComparer.Ordinal);
    /// <summary>P5-E: where the stations were read from ("Smithy"), for the screen; empty when the site was not read from a room.</summary>
    public string RoomName = "";

    public int Level(string quality) => Qualities.GetValueOrDefault(quality);

    public CraftSite AddQuality(string quality, int level) { Qualities[quality] = Math.Max(Level(quality), level); RealQualities[quality] = Math.Max(RealQualities.GetValueOrDefault(quality), level); return this; }

    public CraftSite AddTool(RecipeDb db, string toolId, bool blunt = false)
    {
        var t = db.Tools[toolId];
        foreach (var (q, l) in t.Qualities)
        {
            if (t.FieldKit) { Qualities[q] = Math.Max(Level(q), l); continue; }
            int before = RealQualities.GetValueOrDefault(q);
            AddQuality(q, l);
            if (blunt) { if (l > before) Blunt.Add(q); }
            else if (l >= before) Blunt.Remove(q);
        }
        return this;
    }

    public CraftSite AddStation(RecipeDb db, string stationId, ItemQuality tier = ItemQuality.Common)
    {
        Stations.Add(stationId);
        StationTiers[stationId] = StationTiers.TryGetValue(stationId, out var had) && had > tier ? had : tier;
        foreach (var (q, l) in db.Stations[stationId].Provides)
        {
            if (l >= RealQualities.GetValueOrDefault(q) && !SupplierStation.ContainsKey(q)) SupplierStation[q] = stationId;
            AddQuality(q, l);
            Blunt.Remove(q);
        }
        return this;
    }

    /// <summary>Station bonus of a tier (crafting-full 4.2): Crude -5, Poor -2, Common 0, Fine +3, Masterwork +5.</summary>
    public static int TierBonus(ItemQuality q) => q switch { ItemQuality.Crude => -5, ItemQuality.Poor => -2, ItemQuality.Fine => 3, ItemQuality.Masterwork => 5, _ => 0 };

    /// <summary>True when a need is met only by a field kit (the kit's -5).</summary>
    public bool KitOnly(ToolNeed need) => Level(need.Quality) >= need.Level && RealQualities.GetValueOrDefault(need.Quality) < need.Level;

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
    /// <summary>P5-E (crafting-full 4.2, 4.3): the score terms beyond Phase 3's: station, material (the chosen alternatives), mastery, the field kit's penalty and the smithing process (expected).</summary>
    public int StationBonus, MaterialBonus, MasteryBonus, KitPenalty, ProcessBonus;
    /// <summary>Leave the temper out (crafting-full 4.3: 30 minutes faster, -5 and a brittle blade).</summary>
    public bool SkipTemper;
    /// <summary>P5-E (decided with the user): seconds of fetching materials from elsewhere in the building before the first attended step, and from where.</summary>
    public long FetchSeconds;
    public List<(string Where, long MassG, int Trips)> Fetches = [];
    /// <summary>The sum of the score terms other than skill, roll and tool bonus.</summary>
    public int OtherBonus => LightPenalty + StationBonus + MaterialBonus + MasteryBonus + KitPenalty + ProcessBonus;
    /// <summary>The expected tiers in thousandths of a roll with the masterwork chance (sums to 41,000).</summary>
    public long[] QualityMilli = new long[5];
    public long AttendedSeconds, UnattendedSeconds;
    /// <summary>Count of the 41 rolls in each tier.</summary>
    public int[] QualityCounts = new int[5];
    public byte[] Choice = [];
    public bool MissingButGettable;
    /// <summary>P5-E: the stores the plan draws on, the steps it leaves out, and the tool qualities missing (the screen's hint names them).</summary>
    public CraftSources Sources = [];
    public uint SkipMask;
    public List<ToolNeed> MissingTools = [];
}
