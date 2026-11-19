using Ravensguard.Sim.Core;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Sites;

/// <summary>
/// What a site's history gives its contents (exploration-and-sites 4.2): the settlement's peak population and name for a ruin, the years since it fell, the event's year,
/// the dead of a battle, the artefact of a hoard, the lair, the mine's state, and (for a hideout) the band's recorded robberies. Built from the Phase 1 <see cref="Site"/> and
/// the <see cref="WorldHistory"/> by <see cref="Of"/>; tests build their own.
/// </summary>
public sealed record SiteHistoryInfo
{
    public ulong SiteId { get; init; }
    public SiteKind Kind { get; init; }
    public string Sub { get; init; } = "";
    public string Name { get; init; } = "";
    public int Levels { get; init; } = 1;
    public int PeakPop { get; init; }
    public string SettlementName { get; init; } = "";
    public int EventYear { get; init; }
    public int YearsSince { get; init; }
    public int Dead { get; init; }
    public string ArtefactName { get; init; } = "";
    public bool MonsterLair { get; init; }
    public string LairKind { get; init; } = "";
    public MineState Mine { get; init; }
    public MineralType Mineral { get; init; }
    public int Tonnage { get; init; }
    public int Culture { get; init; }
    /// <summary>A hideout's stash: the goods of the band's recorded robberies, and the coin (mites) the hoard purse of those robberies holds.</summary>
    public IReadOnlyList<LootItem> Stash { get; init; } = [];
    public long StashCoinMites { get; init; }
    /// <summary>The region's bandit pressure (0..1000, 500 normal) for reoccupation.</summary>
    public int BanditPressure { get; init; } = 500;
    /// <summary>A legend's name for the monsters of a lair (the barrow's lord), "" to draw one.</summary>
    public string LegendName { get; init; } = "";

    /// <summary>The inputs of a Phase 1 site from the world history; <paramref name="gameYear"/> is the current game year (0 at the start of play).</summary>
    public static SiteHistoryInfo Of(Site s, WorldHistory? h, int gameYear, int culture = 0)
    {
        int peak = 0; string sname = "", art = "", legend = "";
        if (h != null && s.HistorySettlement != EntityId.None && h.GetSettlement(s.HistorySettlement) is { } hs) { peak = hs.PeakPop; sname = hs.Name; }
        if (h != null && s.Artefact != EntityId.None && h.GetArtefact(s.Artefact) is { } a) art = a.Name;
        if (h != null && s.HistoryEvent != EntityId.None && h.Chronicle.Get(s.HistoryEvent) is { } e && e.Data.TryGetValue("title", out var t)) legend = t;
        int years = h == null ? 0 : Math.Max(0, h.Years - s.Year + gameYear);
        return new SiteHistoryInfo
        {
            SiteId = s.Id.Value, Kind = s.Kind, Sub = s.SubKind, Name = s.Name, Levels = s.Levels, PeakPop = peak > 0 ? peak : s.Kind == SiteKind.Ruin ? s.Dead : 0, SettlementName = sname,
            EventYear = s.Year, YearsSince = years, Dead = s.Dead, ArtefactName = art, MonsterLair = s.MonsterLair, LairKind = s.LairKind, Mine = s.Mine, Mineral = s.Mineral,
            Tonnage = s.Tonnage, Culture = culture, LegendName = "",
        } with { SettlementName = sname.Length > 0 ? sname : s.Name.StartsWith("Ruins of ", StringComparison.Ordinal) ? s.Name[9..] : s.Name };
    }

    /// <summary>The loot budget from history (4.2): a ruin's <c>pop_peak x base x survive/1000 x max(floor, 1000 - decay x years)/1000</c> mites, else the template's flat budget.</summary>
    public static int LootBudget(Ravensguard.Content.SiteLootRuleDef rule, SiteHistoryInfo h)
    {
        if (h.Kind == SiteKind.Ruin && h.PeakPop > 0)
        {
            long keep = Math.Max(rule.DecayFloorPm, 1000 - (long)rule.DecayPmPerYear * h.YearsSince);
            return (int)((long)h.PeakPop * rule.BaseMitesPerPop * rule.SurvivePm / 1000 * keep / 1000);
        }
        return rule.FlatMites;
    }

    /// <summary>What remains of a battlefield's scatter, permille (4.2): <c>max(100, 1000 - 8 x years)</c>.</summary>
    public static int BattlefieldRemainingPm(int years, int decayPmPerYear = 8) => Math.Max(100, 1000 - decayPmPerYear * years);
}
