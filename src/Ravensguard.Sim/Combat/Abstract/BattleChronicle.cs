using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Combat.Abstract;

/// <summary>
/// A battle in the chronicle (abstract-battles-v1 4.6): one <c>battle_report</c> entry per battle, labelled as a report ("word reached Oakford that ..."), with the cause that
/// brought the sides together and the true numbers; each named casualty's chain ("killed by a blade (a grave wound) at the fight on the road from Oakford, which began when
/// bandits ambushed a caravan"). The entry's data carries the translation key and its arguments (<c>key</c>, <c>a</c>, <c>b</c>, <c>place</c>, <c>settlement</c>, <c>lost_a</c>,
/// <c>lost_b</c>) for the game's <c>tr()</c>, and an English <c>title</c> and <c>text</c> as every chronicle entry has.
/// </summary>
public static class BattleChronicle
{
    public const string TypeId = "battle_report", Key = "ab_chron_report";

    private static string Article(WeaponClass w) => w switch
    {
        WeaponClass.Longbow or WeaponClass.Crossbow => "an arrow", WeaponClass.Thrown => "a thrown javelin", WeaponClass.Axe => "an axe", WeaponClass.Unarmed => "bare hands",
        WeaponClass.Lance => "a lance", WeaponClass.Club => "a club", WeaponClass.Hafted => "a mace", WeaponClass.Polearm => "a spear", WeaponClass.Dagger => "a dagger", _ => "a blade",
    };

    private static string WoundWord(OutcomeClass o) => o switch
    {
        OutcomeClass.Instant => "a killing blow", OutcomeClass.Severed => "a limb taken", OutcomeClass.Grave => "a grave wound", OutcomeClass.Serious => "a serious wound", _ => "a wound",
    };

    /// <summary>The chain of a named casualty: how, where, and what began it (4.6).</summary>
    public static string DeathChain(BattleNotable? n, BattleReport r, string cause)
    {
        string how = n != null && n.Wound >= OutcomeClass.Light ? $"killed by {Article(n.By)} ({WoundWord(n.Wound)})" : "killed";
        string where = r.Place.Length > 0 ? $" at the fight on {r.Place}" : " in a fight";
        return $"{how}{where}, which began when {cause}";
    }

    /// <summary>Drafts the report entry (and returns its id, the cause of the casualties' entries).</summary>
    public static EntityId Report(RegionOutbox ob, GameTime when, BattleReport r, EntityId subject, string settlement, string nameA, string nameB, Cause cause)
    {
        int lostA = r.Sides[0].Dead + r.Sides[0].Down + r.Sides[0].Captured, lostB = r.Sides[1].Dead + r.Sides[1].Down + r.Sides[1].Captured;
        string place = r.Place.Length > 0 ? r.Place : "the road";
        string winner = r.Winner == 0 ? nameA : r.Winner == 1 ? nameB : "";
        string text = $"Word reached {settlement} that {nameA} and {nameB} fought on {place}; {nameA} lost {lostA}, {nameB} lost {lostB}"
            + (winner.Length > 0 ? $", and {winner} held the field." : "; neither held the field.");
        return ob.Chronicle(when, TypeId, subject == EntityId.None ? [] : [subject], [cause],
            ("key", Key), ("title", $"A fight on {place} (report)"), ("text", text), ("settlement", settlement), ("place", place), ("a", nameA), ("b", nameB),
            ("lost_a", lostA.ToString()), ("lost_b", lostB.ToString()), ("dead_a", r.Sides[0].Dead.ToString()), ("dead_b", r.Sides[1].Dead.ToString()),
            ("winner", r.Winner.ToString()), ("duration_s", r.DurationS.ToString()), ("quality_x100", r.QualityX100.ToString()), ("battle", r.Id.ToString()), ("report", "1"));
    }
}
