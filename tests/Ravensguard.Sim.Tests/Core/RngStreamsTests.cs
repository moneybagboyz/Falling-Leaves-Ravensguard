using System.Reflection;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Taxes;
using Xunit;

namespace Ravensguard.Sim.Tests.Core;

/// <summary>Review finding 13: every counter stream of the sim is named once in <see cref="RngStreams"/>, and no two uses share an id unless the pair is declared with its reason.</summary>
public class RngStreamsTests
{
    private static List<(string Name, uint Value)> All() =>
        [.. typeof(RngStreams).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral && f.FieldType == typeof(uint)).Select(f => (f.Name, (uint)f.GetRawConstantValue()!))];

    [Fact]
    public void EveryStreamIdIsUnique_ExceptTheDeclaredSharedPairs()
    {
        var all = All();
        Assert.True(all.Count >= 20, $"only {all.Count} streams found");
        var shared = RngStreams.SharedPairs.Select(p => (p.A, p.B)).ToHashSet();
        foreach (var grp in all.GroupBy(x => x.Value).Where(g => g.Count() > 1))
        {
            var names = grp.Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal).ToList();
            for (int i = 0; i < names.Count; i++)
                for (int j = i + 1; j < names.Count; j++)
                    Assert.True(shared.Contains((names[i], names[j])) || shared.Contains((names[j], names[i])), $"streams {names[i]} and {names[j]} share 0x{grp.Key:X} without a declared reason");
        }
        foreach (var (a, b, why) in RngStreams.SharedPairs)
        {
            Assert.False(string.IsNullOrWhiteSpace(why));
            Assert.Equal(all.Single(x => x.Name == a).Value, all.Single(x => x.Name == b).Value);
        }
    }

    private static string SourcePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "Ravensguard.Sim", "Core", "RngStreams.cs"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("repo root not found"), "src", "Ravensguard.Sim", "Core", "RngStreams.cs");
    }

    [Fact]
    public void EveryStreamIsInAKnownBlock_ReadFromTheSource_SoNewStreamsNeverBreakThisTest()
    {
        // the names and ids come from Core/RngStreams.cs itself (no hand list): weather's 0xC0, Phase 2's 0x200..0x2FF, Phase 3's 0x300..0x3FF, Phase 4's 0x400..0x4FF and Phase 5's 0x500..0x5FF
        string text = File.ReadAllText(SourcePath());
        var fromSource = System.Text.RegularExpressions.Regex.Matches(text, @"\b(?<n>[A-Z][A-Za-z0-9]*)\s*=\s*0x(?<v>[0-9A-Fa-f]+)\b")
            .Select(m => (Name: m.Groups["n"].Value, Value: Convert.ToUInt32(m.Groups["v"].Value, 16))).ToList();
        var all = All();
        Assert.True(fromSource.Count >= all.Count - RngStreams.SharedPairs.Length, "the source parse found fewer streams than reflection");
        foreach (var (name, v) in all)
        {
            Assert.True(fromSource.Contains((name, v)) || text.Contains(name + " = "), $"stream {name} is not declared in RngStreams.cs");
            bool ok = v == 0xC0u || (v is >= 0x200u and <= 0x7EFu) || (v is >= 0x800u and <= 0x8EFu);                                     // P6-A2: Phase 6 owns 0x600..0x6FF; P7-0A: Phase 7 owns 0x700..0x7FF (0x7F0..0x7FF unassigned, checked below)
            Assert.True(ok, $"stream {name} = 0x{v:X} is outside weather (0xC0), Phase 2 (0x200..0x2FF), Phase 3 (0x300..0x3FF), Phase 4 (0x400..0x4FF), Phase 5 (0x500..0x5FF), Phase 6 (0x600..0x6FF) and Phase 7 (0x700..0x7EF; 0x7F0..0x7FF is the reserve)");
        }
        foreach (var (name, v) in all.Where(x => x.Name is nameof(RngStreams.LinePick) or nameof(RngStreams.BarkPick) or nameof(RngStreams.RumourWitness) or nameof(RngStreams.RumourHear) or nameof(RngStreams.RumourDistort) or nameof(RngStreams.GatherPartition)))
            Assert.InRange(v, 0x310u, 0x315u);
    }

    /// <summary>P6-A2 (overview 5): every Phase 6 stream of the table is registered under its id, and nothing sits in the block outside the table (the free ranges stay free).</summary>
    [Fact]
    public void Phase6Block_HoldsExactlyTheTableOfOverviewSection5()
    {
        uint[] table = [0x600, 0x610, 0x611, 0x612, 0x613, 0x614, 0x615, 0x620, 0x630, 0x631, 0x632, 0x633, 0x640, 0x641, 0x642, 0x643, 0x644, 0x645, 0x646, 0x650, 0x651, 0x652, 0x653, 0x654, 0x655, 0x656,
            0x660, 0x661, 0x662, 0x663, 0x664, 0x670, 0x671, 0x672, 0x680, 0x681, 0x682, 0x690, 0x691, 0x692, 0x6A0, 0x6B0, 0x6B1, 0x6B2, 0x6B3, 0x6C0, 0x6C1, 0x6C2, 0x6C3,
            0x6D0, 0x6D1, 0x6D2, 0x6D3, 0x6D4, 0x6E0, 0x6E1];
        var inBlock = All().Where(x => x.Value is >= 0x600u and <= 0x6FFu).Select(x => x.Value).Order().ToArray();
        Assert.Equal(table.Order().ToArray(), inBlock);
        Assert.Equal(0x600u, RngStreams.Physician); Assert.Equal(0x6A0u, RngStreams.ChronicleVariantsReserved); Assert.Equal(0x660u, RngStreams.PurveyanceHiding); Assert.Equal(0x6E0u, RngStreams.HoldingsReserved);
    }

    /// <summary>The Phase 5 reservations Phase 6 draws (0x500 now drawn by the NPC land market, 0x540, 0x561), and the Phase 6 reserved ones are never drawn (chronicle 5, holdings 0x6E0; the rule's 0x660 is drawn by purveyance since P7-AE2: it is unused before a purveyance, checked in the AE tests).</summary>
    [Fact]
    public void ReservedStreams_AreNeverDrawnBySimCode()
    {
        var root = new DirectoryInfo(Path.GetDirectoryName(SourcePath())!).Parent!;
        foreach (var f in Directory.EnumerateFiles(root.FullName, "*.cs", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(f) == "RngStreams.cs") continue;
            string src = File.ReadAllText(f);
            foreach (var n in new[] { nameof(RngStreams.ChronicleVariantsReserved), nameof(RngStreams.HoldingsReserved), nameof(RngStreams.CoinageReserved) })
                Assert.DoesNotContain("RngStreams." + n, src, StringComparison.Ordinal);
        }
    }

    /// <summary>P8-LM-3 (overview 6): the bestiary's streams of 0x820..0x83F are registered under their ids: lair placement 0x830, monster behaviour 0x831 and a torch put out by rain 0x832.</summary>
    [Fact]
    public void Phase8BestiaryStreams_AreRegistered()
    {
        var inBlock = All().Where(x => x.Value is >= 0x830u and <= 0x83Fu).Select(x => x.Value).Order().ToArray();
        Assert.Equal(new[] { 0x830u, 0x831u, 0x832u }, inBlock);
        Assert.Equal(0x830u, RngStreams.LairPlacement); Assert.Equal(0x831u, RngStreams.LairMonsterBehaviour); Assert.Equal(0x832u, RngStreams.TorchRainOut);
    }

    [Fact]
    public void PinnedStreamIds_NeverChange()
    {
        Assert.Equal(0x300u, RngStreams.UtilityNoise); Assert.Equal(0x301u, RngStreams.JobClaim); Assert.Equal(0x302u, RngStreams.BedFallback); Assert.Equal(0x303u, RngStreams.RoutineJobs);
        Assert.Equal(0xC0u, RngStreams.WeatherLong);
        Assert.Equal(RngStreams.Death, VitalStreams.Death); Assert.Equal(RngStreams.Name, VitalStreams.Name); Assert.Equal(RngStreams.Attributes, VitalStreams.Attributes);
        Assert.Equal(RngStreams.TaxEvents, TaxSystem.Stream);
        Assert.Equal(0x212u, RngStreams.Herds); Assert.Equal(0x221u, RngStreams.ToolWear); Assert.Equal(0x2C8u, RngStreams.Durables); Assert.Equal(0x2E0u, RngStreams.EnvJitter);
    }

    /// <summary>P7-0A (overview 5): every Phase 7 stream of the table is registered under its id; nothing else sits in 0x700..0x7FF, and 0x7F0..0x7FF stays unassigned.</summary>
    [Fact]
    public void Phase7Block_HoldsExactlyTheTableOfOverviewSection5()
    {
        uint[] table = [0x700, 0x701, 0x710, 0x720, 0x721, 0x730, 0x731, 0x740, 0x741, 0x742, 0x743, 0x750, 0x751, 0x752, 0x753, 0x754,
            0x780, 0x781, 0x782, 0x783, 0x790, 0x791, 0x792, 0x793, 0x7A0, 0x7A1, 0x7A2, 0x7A3, 0x7A4, 0x7A5, 0x7A6, 0x7A7, 0x7B0, 0x7B1, 0x7B2, 0x7B3, 0x7B4,
            0x7C0, 0x7C1, 0x7C2, 0x7C3, 0x7C4, 0x7C5, 0x7D0, 0x7D1, 0x7D2, 0x7D3, 0x7D4, 0x7D5, 0x7D6, 0x7E0, 0x7E1, 0x7E2, 0x7E3, 0x7E4, 0x7E8];          // P7-SH-D ShipIgnition 0x7D6; P7-SG-E ChariotTrample 0x7E8
        var inBlock = All().Where(x => x.Value is >= 0x700u and <= 0x7FFu).Select(x => x.Value).Order().ToArray();
        Assert.Equal(table.Order().ToArray(), inBlock);
        Assert.DoesNotContain(inBlock, v => v >= 0x7F0u);
        Assert.Equal(0x700u, RngStreams.WarLoadMusterJitter); Assert.Equal(0x701u, RngStreams.ExploitBot); Assert.Equal(0x7E4u, RngStreams.WarSoakVariation); Assert.Equal(0x710u, RngStreams.CoinageReserved);
        // the economy block is 0x700..0x77F and the military one 0x780..0x7EF
        Assert.All(inBlock.Where(v => v < 0x780u), v => Assert.InRange(v, 0x700u, 0x754u));
    }

    /// <summary>P7-0A: the bot and load streams (0x700 WarLoad, 0x701 the exploit bot, 0x7E4 the war soak) are drawn only by the audit and soak harnesses (Diagnostics), never by sim code a game started from the menu runs; the coinage reserve is never drawn (above).</summary>
    [Fact]
    public void BotAndLoadStreams_AreNeverDrawnOutsideTheHarnesses()
    {
        var root = new DirectoryInfo(Path.GetDirectoryName(SourcePath())!).Parent!;
        foreach (var f in Directory.EnumerateFiles(root.FullName, "*.cs", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(f) == "RngStreams.cs" || f.Contains(Path.DirectorySeparatorChar + "Diagnostics" + Path.DirectorySeparatorChar)) continue;
            string src = File.ReadAllText(f);
            foreach (var n in new[] { nameof(RngStreams.WarLoadMusterJitter), nameof(RngStreams.ExploitBot), nameof(RngStreams.WarSoakVariation) })
                Assert.True(!src.Contains("RngStreams." + n, StringComparison.Ordinal), $"{Path.GetFileName(f)} draws harness stream {n}; only Sim/Diagnostics may");
        }
    }
}
