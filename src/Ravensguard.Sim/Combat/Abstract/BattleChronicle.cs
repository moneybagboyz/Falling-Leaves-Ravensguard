using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Combat.Abstract;

/// <summary>
/// A battle in the chronicle (abstract-battles-v1 4.6): one <c>battle_report</c> entry per battle, labelled as a report ("word reached Oakford that ..."), with the cause that
/// brought the sides together and the true numbers; each named casualty's chain ("killed by a blade (a grave wound) at the fight on the road from Oakford, which began when
/// bandits ambushed a caravan"). Phase 6 (chronicle-full 4.1, phase-4 S7): the sentences are en.csv's. The entry carries <see cref="Key"/>'s sibling <c>ch_report_battle</c> and
/// typed parameters (the sides as text keys, the place as a settlement id); its data keeps only machine fields (<c>lost_a</c>, <c>lost_b</c>, <c>dead_a</c>, <c>battle</c> ...).
/// </summary>
public static class BattleChronicle
{
    public const string TypeId = "battle_report", Key = "ch_report_battle";
    /// <summary>The place key of a fight on the road out of a settlement ("on the road from {place}").</summary>
    public const string RoadFrom = "ch_on_road_from";

    private static string WeaponKey(WeaponClass w) => w switch
    {
        WeaponClass.Longbow or WeaponClass.Crossbow => "ch_weapon_arrow", WeaponClass.Thrown => "ch_weapon_javelin", WeaponClass.Axe => "ch_weapon_axe", WeaponClass.Unarmed => "ch_weapon_hands",
        WeaponClass.Lance => "ch_weapon_lance", WeaponClass.Club => "ch_weapon_club", WeaponClass.Hafted => "ch_weapon_mace", WeaponClass.Polearm => "ch_weapon_spear", WeaponClass.Dagger => "ch_weapon_dagger", _ => "ch_weapon_blade",
    };

    private static string WoundKey(OutcomeClass o) => o switch
    {
        OutcomeClass.Instant => "ch_wound_killing", OutcomeClass.Severed => "ch_wound_limb", OutcomeClass.Grave => "ch_wound_grave", OutcomeClass.Serious => "ch_wound_serious", _ => "ch_wound_light",
    };

    /// <summary>
    /// The chain of a named casualty: how, where, and what began it (4.6), as a keyed cause. <paramref name="beganKey"/> is the sentence fragment of what began the fight (it uses
    /// <paramref name="began"/>'s parameters), so the chain reads "Killed by a blade (a grave wound) at the fight on the road from Oakford, which began when bandits (4) ambushed a caravan on the road from Oakford."
    /// </summary>
    public static Cause DeathChain(BattleNotable? n, BattleReport r, string beganKey, IReadOnlyList<ChronParam> began)
    {
        bool by = n != null && n.Wound >= OutcomeClass.Light;
        var ps = new List<ChronParam>
        {
            ChronParam.TextKey("how", by ? "ch_how_killed_by" : "ch_how_killed"),
            ChronParam.TextKey("where", r.PlaceKey == RoadFrom ? "ch_at_fight_road_from" : "ch_in_a_fight"),
            ChronParam.TextKey("began", beganKey),
        };
        if (by) { ps.Add(ChronParam.TextKey("weapon", WeaponKey(n!.By))); ps.Add(ChronParam.TextKey("wound", WoundKey(n.Wound))); }
        if (r.PlaceId != EntityId.None) ps.Add(ChronParam.Place("place", r.PlaceId));
        ps.AddRange(began);
        return new Cause(CauseKind.Wound, null, "", "ch_note_fight_death", ps);
    }

    /// <summary>Drafts the report entry (and returns its id, the cause of the casualties' entries).</summary>
    public static EntityId Report(RegionOutbox ob, GameTime when, BattleReport r, EntityId subject, EntityId settlement, string sideAKey, string sideBKey, Cause cause)
    {
        int lostA = r.Sides[0].Dead + r.Sides[0].Down + r.Sides[0].Captured, lostB = r.Sides[1].Dead + r.Sides[1].Down + r.Sides[1].Captured;
        var ps = new List<ChronParam>
        {
            ChronParam.Place("settlement", settlement), ChronParam.TextKey("a", sideAKey), ChronParam.TextKey("b", sideBKey),
            ChronParam.TextKey("where", r.PlaceKey.Length > 0 ? r.PlaceKey : "ch_on_the_road"),
            ChronParam.Count("lost_a", lostA), ChronParam.Count("lost_b", lostB),
            ChronParam.TextKey("outcome", r.Winner == 0 ? "ch_outcome_a" : r.Winner == 1 ? "ch_outcome_b" : "ch_outcome_none"),
        };
        if (r.PlaceId != EntityId.None) ps.Add(ChronParam.Place("place", r.PlaceId));
        return ob.Chronicle(when, TypeId, Key, ps, subject == EntityId.None ? [] : [subject], [cause],
            ("lost_a", lostA.ToString()), ("lost_b", lostB.ToString()), ("dead_a", r.Sides[0].Dead.ToString()), ("dead_b", r.Sides[1].Dead.ToString()),
            ("winner", r.Winner.ToString()), ("duration_s", r.DurationS.ToString()), ("quality_x100", r.QualityX100.ToString()), ("battle", r.Id.ToString()), ("report", "1"));
    }
}
