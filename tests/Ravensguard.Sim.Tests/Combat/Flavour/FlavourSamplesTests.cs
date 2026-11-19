using System.Text;
using Ravensguard.Sim.Combat.Flavour;
using Ravensguard.Sim.Combat.Melee2;

namespace Ravensguard.Sim.Tests.Combat.Flavour;

/// <summary>Prints the prose of seeded duels (set FL_SAMPLES to a file path) so a human can judge it; also checks that every shipped line of a few fights is non-empty.</summary>
public class FlavourSamplesTests(FlavourFixture fx) : IClassFixture<FlavourFixture>
{
    [Fact]
    public void Seeded_duels_read_as_prose_and_can_be_dumped()
    {
        var sb = new StringBuilder();
        foreach (var (seed, wa, ma, wb, mb) in new[] { (7UL, "arming_sword", "steel", "axe", "iron"), (11UL, "mace", "iron", "spear", "steel"), (23UL, "longsword", "steel", "falchion", "bronze") })
        {
            var (w, r) = fx.Duel(seed, wa, ma, wb, mb);
            sb.AppendLine($"=== seed {seed}: {wa} vs {wb}, {FightLog.Clock(w.Tick)} ===");
            for (int i = 0; i < r.Log.Lines.Count; i++)
            {
                string s = fx.Line(r.Log, i);
                Assert.False(string.IsNullOrWhiteSpace(s), $"line {i} {r.Log.Lines[i].Kind} is empty");
                Assert.DoesNotContain("{", s);
                var l = r.Log.Lines[i];
                sb.AppendLine($"{FightLog.Clock(l.Tick)} [{l.Kind,-10} s{l.Severity}] {s}");
            }
        }
        var path = Environment.GetEnvironmentVariable("FL_SAMPLES");
        if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, sb.ToString());
    }
}
