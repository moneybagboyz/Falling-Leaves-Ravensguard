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
            bool ok = v == 0xC0u || (v is >= 0x200u and <= 0x5FFu);
            Assert.True(ok, $"stream {name} = 0x{v:X} is outside weather (0xC0), Phase 2 (0x200..0x2FF), Phase 3 (0x300..0x3FF), Phase 4 (0x400..0x4FF) and Phase 5 (0x500..0x5FF)");
        }
        foreach (var (name, v) in all.Where(x => x.Name is nameof(RngStreams.LinePick) or nameof(RngStreams.BarkPick) or nameof(RngStreams.RumourWitness) or nameof(RngStreams.RumourHear) or nameof(RngStreams.RumourDistort) or nameof(RngStreams.GatherPartition)))
            Assert.InRange(v, 0x310u, 0x315u);
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
}
