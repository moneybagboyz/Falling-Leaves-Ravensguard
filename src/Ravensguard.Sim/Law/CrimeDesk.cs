using System.Globalization;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Law;

/// <summary>
/// The hero's act at the barrier (crime-and-justice 4.1, 4.2, 4.9): the street view saw who saw it (geometry on the live grids, at the act's tick) and passes the witnesses with their flags, the cry and the
/// look of his clothes in the command, so the barrier writes the record, the chronicle chain and the rumour exactly as the replay will ("cj:act|kind|victim|settlement|value|damage|note|build|colours|witnesses|cry").
/// An act nobody saw writes nothing. Recorded for replay like every hero command.
/// </summary>
public static class CrimeDesk
{
    public const string Prefix = "cj:";

    private static string U(ulong v) => v.ToString(CultureInfo.InvariantCulture);
    private static string N(long v) => v.ToString(CultureInfo.InvariantCulture);

    /// <summary>The command for an act the street saw. <paramref name="seen"/> are the sightings of <see cref="Witnessing.Evaluate"/> (each with flags: knew him, kin or friend, fears him); <paramref name="cry"/> the shout, if any.</summary>
    public static HeroCommand Act(string kind, EntityId victim, int settlement, long value, long damage, string noteKey, ActorLook look, IReadOnlyList<WitnessRow> seen, CryInfo? cry, long sec) => new()
    {
        Kind = HeroCommandKind.Crime, IssuedSec = sec, Origin = CommandOrigin.Human, Settlement = settlement,
        Tag = Prefix + "act|" + kind + "|" + U(victim.Value) + "|" + N(settlement) + "|" + N(value) + "|" + N(damage) + "|" + noteKey + "|" + look.Build + "|" + string.Join(",", look.Colours) + "|"
            + string.Join(",", seen.Select(w => U(w.Person) + ":" + w.Flags)) + "|" + (cry is { } c ? U(c.Raiser.Value) + ":" + c.Hearers + ":" + c.Guards : ""),
    };

    private static HeroResult R(HeroCommand c, HeroResultCode code, HeroText t) => new(c.Seq, c.Kind, code, t, 0, 0, c.IssuedSec);

    public static HeroResult Apply(HeroContext ctx, HeroCommand c)
    {
        if (!c.Tag.StartsWith(Prefix + "act|", StringComparison.Ordinal)) return R(c, HeroResultCode.Invalid, HeroText.Of("HERO_UNKNOWN_COMMAND"));
        var f = c.Tag[(Prefix.Length + 4)..].Split('|');
        if (f.Length < 10 || !ulong.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong victim) || !int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int settlement)
            || !long.TryParse(f[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) || !long.TryParse(f[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out long damage)
            || !byte.TryParse(f[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte build) || value < 0 || damage < 0)
            return R(c, HeroResultCode.Invalid, HeroText.Of("HERO_MAKES_NO_SENSE"));
        var st = ctx.State; var pt = st.Population.People;
        var rows = new List<WitnessRow>();
        if (f[8].Length > 0)
            foreach (var q in f[8].Split(','))
            {
                var p = q.Split(':');
                if (p.Length != 2 || !ulong.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong who) || !byte.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte fl)) return R(c, HeroResultCode.Invalid, HeroText.Of("HERO_MAKES_NO_SENSE"));
                int row = pt.IndexOf(new EntityId(who)); if (row < 0 || !pt.IsAlive(row)) continue;          // he died between the act and the barrier: no longer a witness
                rows.Add(new WitnessRow(who, fl));
            }
        if (rows.Count == 0) return R(c, HeroResultCode.Ok, HeroText.Of("cj_unseen"));
        CryInfo? cry = null;
        if (f.Length > 9 && f[9].Length > 0)
        {
            var p = f[9].Split(':');
            if (p.Length == 3 && ulong.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong raiser) && int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int heard) && int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int guards))
                cry = new CryInfo(new EntityId(raiser), heard, guards);
        }
        var colours = f[7].Length == 0 ? [] : f[7].Split(',').Select(x => uint.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        var law = LawWorld.From(st, ctx.Content);
        var book = st.Law ??= new CrimeBook();
        var rec = CrimeSystem.Commit(law, book, new ActSpec(f[0], ctx.Party.Hero, victim == 0 ? EntityId.None : new EntityId(victim), settlement, c.IssuedSec, value, damage, f[5]), rows, new ActorLook(colours, build), cry);
        return new HeroResult(c.Seq, c.Kind, HeroResultCode.Ok, HeroText.Of("cj_seen", rows.Count), value, rec.Id, c.IssuedSec);
    }
}
