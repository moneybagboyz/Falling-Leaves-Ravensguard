using Ravensguard.Content;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.WorldGen.History;

/// <summary>
/// The rules version switch of the history (history-recalibration 3, 4.1). <see cref="V0"/> is Phase 1's history: its code path, its streams and its hashes are frozen forever, because a world already in
/// the library regenerates it and the load report refuses a save whose layer no longer regenerates identically. <see cref="V1"/> is Phase 6's: the live functions at the history's seasonal scale.
/// The numbers of v1 live here (not in content) because they are frozen with the version: a world records <c>RulesVersion = 1</c> and must regenerate the same legends whatever a mod tunes later.
/// </summary>
public static class HistoryRules
{
    public const int V0 = 0, V1 = 1;

    /// <summary>The library's label of a world's history rules ("Phase 1" or "Phase 6", history-recalibration 6).</summary>
    public static string Label(int version) => version >= V1 ? "Phase 6" : "Phase 1";

    /// <summary>The politics numbers the war utility reads: the content's defaults (a world's history never reads a mod's tuning).</summary>
    public static readonly PoliticsSettingsDef Politics = new() { Id = "politics" };

    /// <summary>The unrest numbers of the ladder (UR-10): thresholds 150, 300, 450, 600; a riot after 5 days at the riot rung; a revolt after the revolt rung holds for two seasons.</summary>
    public static readonly UnrestSettingsDef Unrest = new() { Id = "unrest" };

    // ---- war and peace (4.2). A realm that has a quarrel declares at the utility's threshold; the gate keeps a season's declarations to the ruler's will (calibrated, 4.4).
    public const int WarGatePct = 4;
    public const int ExcommWarGatePct = 60;
    public const int RealmsRef = 24;
    /// <summary>Peace: a side that sues (weariness less its score at the sue gap) is met with this chance a season.</summary>
    public const int PeaceAcceptPct = 15;
    /// <summary>A casus belli of an excommunication, a raid and an insult lasts this many years.</summary>
    public const int CbYears = 8;

    // ---- opinion modifiers (content/base/politics/opinion.jsonc, frozen with the version)
    public const int OpKin = 20, OpMarriage = 30, OpSameReligion = 10, OpHeresy = -40, OpExcommunicated = -30, OpWar = -50;

    // ---- coarse unrest of a realm, permille (UR-10): each source adds to it, then the ladder reads it
    public const int UnrestPerPoint = 8;
    public const int UnrestFamine = 130, UnrestPlague = 100, UnrestHeresy = 90, UnrestExcommunicated = 160, UnrestThinTreasury = 80, UnrestWarWeary = 3, UnrestWarWearyMax = 140;
    /// <summary>The share of a season's rung-4 realm that goes into revolt once it is due (otherwise it holds and the crown buys it off).</summary>
    public const int RevoltAfterSeasons = 2;

    // ---- faith
    public const int HamletEmptyPct = 25;              // a hamlet the plague reaches is emptied this often (4.2)
    public const int HeresyPermilleYear = 50;          // per religion and year, when a famine or plague hit it and a realm of it is aggrieved
    public const int HeresyYearsMax = 25;
    public const int SaintsPerFaithMax = 7;            // history's saints of one faith (the derived religion adds 3 to 5 founders: 4 to 12 in all)
    public const int ExcommunicationPctYear = 25;      // a heretic ruler, per year
    public const int ExcommunicationWarPctYear = 2;    // an ambitious ruler at war with a realm of his own faith, per year
    public const int ExcommunicationYears = 10;
    public const int AbbeyPermilleYear = 30;           // a pious ruler, per year
    public const int AbolitionPermilleYear = 8;        // a pious ruler of a religion that forbids slavery in a realm whose culture holds it, per year

    // ---- bondage
    public const int RaidPctYear = 3;                  // a chattel or war-captive realm against a weaker neighbour, per neighbour and year
    public const int RaidWeakerPct = 90;               // "weaker": the target's levy is under this percent of the raider's
    public const int RaidTakesPct = 5;                 // of the settlement's people, at least 4

    // ---- the vital table (family-and-lifespan 4.6; tests pin it to FamilyContent's defaults): the old-age check per year by age
    public static int OldAgePctYear(int age) => age >= 75 ? 20 : age >= 65 ? 10 : age >= 60 ? 3 : age >= 50 ? 1 : 0;

    /// <summary>The slider the history runs the great plague at (a normal world; the live sliders belong to play).</summary>
    public const PlagueSeverity Severity = PlagueSeverity.Normal;
}
