using Ravensguard.Content;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Combat.Aftermath;

/// <summary>What the defeat table needs to know of one defeated person (4.3, 4.4).</summary>
public sealed record LoserFacts(EntityId Person, LoserRole Role, LoserState State, bool Wounded, bool Bleeding, bool Lone, long RansomEstimate, long PayableMites, bool AllyStanding = false);

/// <summary>
/// The Phase 4 defeat table (morale-capture-succession 4.4): one table for the hero and for NPCs (abstract battles call it too). Pure: the rolls are counter draws on stream 0x452 keyed
/// by the fight id and the loser, so the same fight decides the same fates.
/// </summary>
public static class DefeatTable
{
    /// <summary>
    /// The fate of one defeated person. Bandits rob (a cruel band kills a wounded lone victim with 50%), capture when kidnappers and the ransom is at least 1,000 mites, or when any band sees
    /// at least 1,000 mites a payer could pay; armies take prisoners (the hero and the party, surrendered and knocked-out soldiers; the rest flee); beasts maul (each minute 50% a feeding beast
    /// finishes a downed victim unless an ally drives it off, for five minutes); a draugr kills the downed; guards arrest (P6-C1: the fief's gaol); locals beat and take the purse.
    /// </summary>
    public static DefeatOutcome Decide(AftermathRules rules, ulong seed, ulong fightId, WinnerKind winner, BanditStyleDef? style, LoserFacts l, long time = 0, EntityId place = default)
    {
        var D = rules.D;
        DefeatOutcome O(DefeatKind k, bool killed = false, string st = "") => new(fightId, l.Person, winner, k, l.State, time, place, st, Killed: killed);
        if (l.State == LoserState.Dead) return O(DefeatKind.Killed, true, style?.Id ?? "");
        if (l.State == LoserState.Fled) return O(DefeatKind.Left);
        bool partyLike = l.Role is LoserRole.Hero or LoserRole.PartyMember;
        uint who = (uint)(l.Person.Value & 0xFFFF_FFFF);
        int Roll(uint draw) => DetRng.Range(seed, (long)fightId, l.Person.Value, RngStreams.Loot, draw, 0, 1000);
        switch (winner)
        {
            case WinnerKind.Bandits:
            {
                var s = style ?? rules.Styles[0];
                if (l.Role == LoserRole.Hireling) return O(DefeatKind.Left, st: s.Id);                        // hirelings flee
                if (l.Role == LoserRole.Stranger && !l.Wounded) return O(DefeatKind.Left, st: s.Id);           // strangers are robbed only when wounded
                bool capture = s.Kidnapper && l.RansomEstimate >= Math.Max(1, s.CaptureMinMites > 0 ? s.CaptureMinMites : D.KidnapMinMites)
                    || l.PayableMites >= D.KidnapMinMites;
                if (capture && partyLike) return O(DefeatKind.Captured, st: s.Id);
                if (s.KillWoundedLonePm > 0 && l.Wounded && l.Lone && Roll(who * 4 + 1) < s.KillWoundedLonePm)
                    return O(DefeatKind.Killed, true, s.Id);
                if (l.Role == LoserRole.Stranger && s.KillWoundedLonePm > 0) return O(DefeatKind.Killed, true, s.Id);
                return O(DefeatKind.Robbed, st: s.Id);
            }
            case WinnerKind.Slavers:
                // P6-D1 (slavery 4.1): slavers take the hero and his party alive (to sell them), rob a wounded stranger, and let hirelings and the unhurt go
                if (partyLike) return O(DefeatKind.Captured, st: "slavers");
                if (l.Role == LoserRole.Hireling || l.Role == LoserRole.Stranger && !l.Wounded) return O(DefeatKind.Left, st: "slavers");
                return O(DefeatKind.Robbed, st: "slavers");
            case WinnerKind.Army:
                if (partyLike) return O(DefeatKind.Captured);
                if (l.Role == LoserRole.Soldier && l.State is LoserState.Surrendered or LoserState.KnockedOut or LoserState.Dying) return O(DefeatKind.Captured);
                return O(DefeatKind.Left);                                                                     // the rest flee
            case WinnerKind.Beasts:
            {
                if (l.State is LoserState.Surrendered or LoserState.Standing) return O(DefeatKind.Mauled);
                if (l.AllyStanding) return O(DefeatKind.Mauled);                                                // an ally drives it off and pulls the victim out
                for (int minute = 0; minute < D.BeastFeedMinutes; minute++)
                    if (Roll(who * 4 + 2 + (uint)minute * 16) < D.BeastFinishPm) return O(DefeatKind.Killed, true);
                return O(DefeatKind.Mauled);
            }
            case WinnerKind.Draugr:
                return l.AllyStanding && l.State != LoserState.Dying ? O(DefeatKind.Mauled) : O(DefeatKind.Killed, true);   // a draugr finishes the downed unless allies hold the line
            case WinnerKind.Guards:
                return O(DefeatKind.Captured);                                                                 // P6-C1: an arrest (the old fine-and-release stub is gone): the fief's gaol takes him (crime-and-justice 4.4)
            case WinnerKind.Locals:
                return O(DefeatKind.Robbed, st: "purse");
            default:
                return l.State == LoserState.Surrendered ? O(DefeatKind.Yielded) : O(DefeatKind.Left);
        }
    }

    /// <summary>Mercy (4.4): before leaving, a bandit with first aid presses a wound of a bleeding victim with 50% (stream 0x452, "mercy").</summary>
    public static bool Mercy(AftermathRules rules, ulong seed, ulong fightId, EntityId loser, bool bleeding, IReadOnlyList<Robber> robbers)
    {
        if (!bleeding) return false;
        if (!robbers.Any(r => r.FirstAid)) return false;
        uint who = (uint)(loser.Value & 0xFFFF_FFFF);
        return DetRng.Range(seed, (long)fightId, loser.Value, RngStreams.Loot, who * 4 + 3, 0, 1000) < rules.D.MercyPm;   // one of them stops to press the wound
    }
}
