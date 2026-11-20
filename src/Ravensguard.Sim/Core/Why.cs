namespace Ravensguard.Sim.Core;

/// <summary>
/// Builders for a cause that carries a text key and typed parameters instead of English (chronicle-full 3, 4.1): <c>Why.Of(CauseKind.Tax, "ch_why_tax_evasion", ChronParam.Place("place", id))</c>.
/// <see cref="Of"/> is a cause with a note and no entry behind it; <see cref="After"/> names the earlier entry that brought it about (specific whenever that entry exists).
/// </summary>
public static class Why
{
    public static Cause Of(CauseKind kind, string noteKey, params ChronParam[] ps) => new(kind, null, "", noteKey, ps.Length == 0 ? null : ps);

    public static Cause After(CauseKind kind, EntityId? entry, string noteKey, params ChronParam[] ps) => new(kind, entry, "", noteKey, ps.Length == 0 ? null : ps);
}
