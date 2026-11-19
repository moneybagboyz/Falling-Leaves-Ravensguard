namespace Ravensguard.Sim.Behaviour;

/// <summary>
/// What a layer above the brain feeds <see cref="UtilityBrain"/> instead of keyboard input (docs/specs/phase3/autopilot-v1.md 4.1): a bias in permille added to the score of each action kind (the current
/// directive's wishes), and a mask of kinds removed from the candidate set (the limits). Null on a <see cref="ChoiceContext"/> for a plain NPC. Reference type, read only while scoring.
/// </summary>
public sealed class ActionPolicy
{
    /// <summary>Every <see cref="ActionKind"/> (15 in Phase 3; Phase 4 appended Treat and Fight: 17).</summary>
    public const int Kinds = 17;
    /// <summary>Added to the score of the action kind (index = <see cref="ActionKind"/>).</summary>
    public readonly int[] BiasPm = new int[Kinds];
    /// <summary>Bit per <see cref="ActionKind"/>: set = the action is removed (a limit forbids it).</summary>
    public uint FilterMask;

    public void Clear() { Array.Clear(BiasPm); FilterMask = 0; }
    public void Bias(ActionKind k, int pm) => BiasPm[(int)k] += pm;
    public void Forbid(ActionKind k) => FilterMask |= 1u << (int)k;
    public bool Forbids(ActionKind k) => (FilterMask & (1u << (int)k)) != 0;
    public bool IsNeutral { get { if (FilterMask != 0) return false; foreach (int b in BiasPm) if (b != 0) return false; return true; } }
}
