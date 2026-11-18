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

    [Fact]
    public void PhaseTwoStreams_AreInTheirBlock_AndTheOldNamesPointAtThem()
    {
        // Phase 2 owns 0x200..0x2FF; Phase 3 (live behaviour and jobs) owns 0x300..0x3FF
        var phaseThree = new[] { nameof(RngStreams.UtilityNoise), nameof(RngStreams.JobClaim), nameof(RngStreams.BedFallback), nameof(RngStreams.RoutineJobs),
            nameof(RngStreams.CraftFailure), nameof(RngStreams.CraftByproduct), nameof(RngStreams.CraftWatch),
            nameof(RngStreams.Stains), nameof(RngStreams.ColdTolerance), nameof(RngStreams.Wardrobe),
            nameof(RngStreams.TradeAttrJitter), nameof(RngStreams.HireMood), nameof(RngStreams.PackQuality),
            nameof(RngStreams.AnimalInjury), nameof(RngStreams.VehicleStuck), nameof(RngStreams.AnimalBreed), nameof(RngStreams.ShoeLoss), nameof(RngStreams.Taming) };
        foreach (var (name, v) in All().Where(x => x.Name != nameof(RngStreams.WeatherLong) && !phaseThree.Contains(x.Name)))
            Assert.InRange(v, 0x200u, 0x2FFu);
        foreach (var (name, v) in All().Where(x => phaseThree.Contains(x.Name))) Assert.InRange(v, 0x300u, 0x3FFu);
        Assert.Equal(0x300u, RngStreams.UtilityNoise); Assert.Equal(0x301u, RngStreams.JobClaim); Assert.Equal(0x302u, RngStreams.BedFallback); Assert.Equal(0x303u, RngStreams.RoutineJobs);
        Assert.Equal(0xC0u, RngStreams.WeatherLong);
        Assert.Equal(RngStreams.Death, VitalStreams.Death); Assert.Equal(RngStreams.Name, VitalStreams.Name); Assert.Equal(RngStreams.Attributes, VitalStreams.Attributes);
        Assert.Equal(RngStreams.TaxEvents, TaxSystem.Stream);
        Assert.Equal(0x212u, RngStreams.Herds); Assert.Equal(0x221u, RngStreams.ToolWear); Assert.Equal(0x2C8u, RngStreams.Durables); Assert.Equal(0x2E0u, RngStreams.EnvJitter);
    }
}
