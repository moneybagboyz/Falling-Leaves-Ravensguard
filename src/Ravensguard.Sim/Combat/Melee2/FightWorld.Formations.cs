using Ravensguard.Sim.Battle;

namespace Ravensguard.Sim.Combat.Melee2;

/// <summary>
/// P7 BB-B: the fight's formation book (battle-bubble-and-formations 3.2, 4.3 to 4.7), absent until a formation is made, so a fight without one runs, hashes and saves exactly as before. The
/// small hooks the brain and the morale step call live here; the rules are in <c>Sim/Battle/FormationBook*.cs</c>.
/// </summary>
public sealed partial class FightWorld
{
    public FormationBook? Formations { get; private set; }

    /// <summary>The book, made on first use.</summary>
    public FormationBook EnsureFormations() => Formations ??= new FormationBook(FormationRules.For(Content.Db));

    internal void RestoreFormations(long[]? packed) { if (packed != null) Formations = FormationBook.Restore(this, packed); }

    internal bool FormationHoldsFire(Fighter f) => Formations != null && f.FmRow >= 0 && Formations.HoldsFire(f);
    internal bool FormationSafeShot(Fighter f, Fighter t) => Formations == null || f.FmRow < 0 || Formations.SafeShot(this, f, t);
}
