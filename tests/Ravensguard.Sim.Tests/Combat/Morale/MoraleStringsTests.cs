using System.Text.RegularExpressions;
using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Succession;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Tests.Combat.Morale;

/// <summary>Every text key the aftermath uses (the sim's result keys and the screens') is a row of en.csv, the enum-built ones included (P4-D: strings through tr()).</summary>
public class MoraleStringsTests
{
    [Fact]
    public void EveryAftermathKeyIsInEnCsv()
    {
        var csv = EnCsv.Load(EnCsv.Find());
        string root = Path.GetDirectoryName(EnCsv.Find())!;
        while (!File.Exists(Path.Combine(root, "Ravensguard.slnx"))) root = Path.GetDirectoryName(root)!;
        var files = new[]
        {
            "src/Ravensguard.Sim/Combat/Aftermath/Captivity.cs", "src/Ravensguard.Sim/Combat/Aftermath/Rescue.cs", "src/Ravensguard.Sim/Combat/Aftermath/AftermathDesk.cs",
            "src/Ravensguard.Game/World/Combat/AftermathViews.cs", "src/Ravensguard.Game/World/StreetView.Captivity.cs",
        };
        var text = string.Concat(files.Select(f => File.ReadAllText(Path.Combine(root, f))));
        var keys = Regex.Matches(text, @"""(MC_[A-Z0-9_]+)""").Select(m => m.Groups[1].Value).Where(k => !k.EndsWith('_')).Distinct().ToList();
        Assert.True(keys.Count > 60, $"{keys.Count} keys");
        foreach (var k in Enum.GetValues<DefeatKind>()) { keys.Add("MC_DEFEAT_" + Up(k)); keys.Add("MC_DEFEAT_TITLE_" + Up(k)); keys.Add("MC_DEFEAT_BODY_" + Up(k)); }
        foreach (var k in Enum.GetValues<WinnerKind>()) keys.Add("MC_WINNER_" + Up(k));
        foreach (var k in Enum.GetValues<CaptiveState>()) keys.Add("MC_FREE_" + Up(k));
        foreach (var k in Enum.GetValues<PayerKind>()) keys.Add("MC_PAYER_" + Up(k));
        foreach (var k in Enum.GetValues<Restraint>()) keys.Add("MC_RESTRAINT_" + Up(k));
        foreach (var k in Enum.GetValues<SuccessionRule>()) keys.Add("MC_RULE_" + Up(k));
        Assert.All(keys, k => Assert.True(csv.ContainsKey(k), $"{k} is not in en.csv"));
    }

    private static string Up<T>(T e) where T : Enum => e.ToString().ToUpperInvariant();
}
