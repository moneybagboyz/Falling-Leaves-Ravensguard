using Ravensguard.Sim.Combat.Flavour;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Mounted;
using Ravensguard.Sim.Tests.Combat.Flavour;

namespace Ravensguard.Sim.Tests.Combat.Mounted;

/// <summary>The mounted events in the flavour log (feedback-flavour-log 3.2 and 4.6, mounted-combat 7): every kind tells a sentence of its own and maps to a sound or a caption.</summary>
public class MountedFlavourTests(FlavourFixture fx) : IClassFixture<FlavourFixture>
{
    private static readonly FightEventKind[] Mounted =
        [FightEventKind.Charge, FightEventKind.Unhorsed, FightEventKind.Fell, FightEventKind.HorseFell, FightEventKind.Balked, FightEventKind.Reared, FightEventKind.Bolted,
         FightEventKind.Trampled, FightEventKind.BraceStrike, FightEventKind.Shattered];

    [Fact]
    public void MountedFights_TellEveryMountedEvent_WithItsOwnLine()
    {
        var seen = new HashSet<FightEventKind>();
        var pairings = MountedCalibration.MainPairings();
        for (int i = 0; i < 60 && seen.Count < Mounted.Length - 2; i++)
        {
            var w = MountedCalibration.ChargeWorld(fx.C, pairings[i % pairings.Count], i);
            var r = FlavourRecorder.Attach(w, null, fx.Book);
            while (!w.IsOver && w.Tick < 20 * 60) w.StepWithAi();
            r.Finish();
            for (int k = 0; k < r.Log.Lines.Count; k++)
            {
                var kind = r.Log.Lines[k].Kind;
                if (Array.IndexOf(Mounted, kind) < 0) continue;
                string s = fx.Line(r.Log, k);
                Assert.False(string.IsNullOrWhiteSpace(s), $"{kind}: empty line");
                Assert.DoesNotContain("trade blows", s);                        // not the fallback
                Assert.DoesNotMatch(@"\{[a-z_A-Z]+\}", s);
                seen.Add(kind);
            }
        }
        // the charge, the braced strike, falls, unhorsing, shattered lances and trampling all come up in a few dozen charges
        foreach (var k in new[] { FightEventKind.Charge, FightEventKind.BraceStrike, FightEventKind.Shattered, FightEventKind.Trampled, FightEventKind.Unhorsed, FightEventKind.Fell })
            Assert.Contains(k, seen);
        foreach (var k in Mounted)
            Assert.True(fx.Audio.Any(a => a.Events.Contains(FlavourDescribe.EventName(k))), $"{k} has no audio row");
    }
}
