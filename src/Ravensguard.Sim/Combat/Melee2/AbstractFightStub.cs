using Ravensguard.Sim.Combat.Watch;

namespace Ravensguard.Sim.Combat.Melee2;

/// <summary>What became of a fight outside every live bubble (the abstract tier's report): the winning team (-1 a draw), the odds it was fought at, and who is out by team.</summary>
public sealed record AbstractFightReport(ulong FightId, FightKind Kind, int WinnerTeam, int OddsPm, IReadOnlyDictionary<int, int> OutByTeam, bool Stub, string Cause);

/// <summary>A fight that was run: live in the bubble (<see cref="Live"/>) or resolved abstractly (<see cref="Report"/>). Exactly one is set.</summary>
public sealed record FightRun(FightWorld? Live, AbstractFightReport? Report);

/// <summary>
/// The abstract tier's fight resolver (overview 3: "outside any bubble, an AbstractBattle"). This is the seam abstract-battles-v1 (P4-G, step 6) fills: its Lanchester exchange with
/// the wound tables baked from the tactical resolver implements this interface; <see cref="FightSetup.Run"/> and its callers do not change.
/// </summary>
public interface IAbstractFights
{
    AbstractFightReport Resolve(FightSetup setup, FightContent content);
}

/// <summary>
/// The stand-in until abstract-battles-v1: the side the odds favour (<see cref="RiskEstimate"/>, the square law on the setup's fighters) holds the field and nobody is hurt (no
/// wound is invented, no person is moved). It draws nothing (the abstract streams 0x4A0..0x4A3 belong to step 6) and is marked <see cref="AbstractFightReport.Stub"/>.
/// </summary>
public sealed class AbstractFightStub : IAbstractFights
{
    public static readonly AbstractFightStub Instance = new();

    public AbstractFightReport Resolve(FightSetup setup, FightContent content)
    {
        var w = setup.Build(content, null);
        var teams = setup.Kits.Select(k => k.Team).Distinct().Order().ToList();
        int best = -1, bestOdds = -1; bool tie = false;
        foreach (int t in teams)
        {
            int o = RiskEstimate.OddsOf(w, t);
            if (o > bestOdds) { best = t; bestOdds = o; tie = false; } else if (o == bestOdds) tie = true;
        }
        return new AbstractFightReport(setup.FightId, setup.Kind, tie ? -1 : best, Math.Max(0, bestOdds), teams.ToDictionary(t => t, _ => 0), Stub: true, setup.Cause);
    }
}
