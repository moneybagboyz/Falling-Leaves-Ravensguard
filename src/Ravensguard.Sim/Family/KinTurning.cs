using System.Globalization;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Succession;

namespace Ravensguard.Sim.Family;

/// <summary>
/// Kin who turn (family-and-lifespan 4.5, FL-12). A kin member's loyalty is his trust in the hero (permille) less 150 for each grievance he holds (<see cref="FamilyBook.Loyalty"/>); below the turn line (-300) his yearly check, on his
/// birthday, may make him leave the household (30 percent, stream 0x642, key the person and the game year), and a match for him is refused outright (<see cref="Matches.Arrange"/>). A son the hero's will passed over contests it
/// at the hero's death (<see cref="Disputes"/>, the stream Phase 5 reserved). The other turns of the spec (siding with a rival claimant, telling kidnappers where the family is) are cut (cut order 4). Every turn is a chronicle entry whose causes
/// are the grievances.
/// </summary>
public static class KinTurning
{
    /// <summary>The note key of a grievance flag (the causes of a turn).</summary>
    public static string NoteOf(KinGrievance g) => g switch
    {
        KinGrievance.Neglected => "ch_note_kin_neglected", KinGrievance.Punished => "ch_note_kin_punished", KinGrievance.PassedOver => "ch_note_kin_passed_over",
        KinGrievance.MarriedOff => "ch_note_kin_married_off", KinGrievance.Favoured => "ch_note_kin_favoured", _ => "ch_note_kin_distrust",
    };

    /// <summary>The causes of a turn: one per grievance held (in flag order), and the hero's lost trust when there is no grievance to name.</summary>
    public static List<Cause> CausesOf(KinGrievance g)
    {
        var causes = new List<Cause>();
        foreach (var f in new[] { KinGrievance.Neglected, KinGrievance.Punished, KinGrievance.PassedOver, KinGrievance.MarriedOff, KinGrievance.Favoured })
            if ((g & f) != 0) causes.Add(new Cause(CauseKind.Family, null, NoteKey: NoteOf(f)));
        if (causes.Count == 0) causes.Add(new Cause(CauseKind.Family, null, NoteKey: NoteOf(KinGrievance.None)));
        return causes;
    }

    /// <summary>The yearly draw (stream 0x642, key the person and the game year): does a kin member below the turn line leave?</summary>
    public static bool Leaves(Ravensguard.Content.FamilySettingsDef s, ulong seed, ulong person, int year) => DetRng.Range(seed, year, person, RngStreams.KinTurning, 0, 0, 100) < s.KinLeavePct;

    /// <summary>Whether it is the person's birthday on <paramref name="day"/> (the day his age first reaches the next year at the world's pace).</summary>
    private static bool Birthday(PersonTable pt, int row, int day) => pt.AgeYears(row, day) > pt.AgeYears(row, day - 1);

    /// <summary>
    /// The day's check of the hero's kin (4.5): each adult kin member alive, not the hero, on his birthday, with his loyalty below the turn line, leaves the household with the chance of <c>KinLeavePct</c> (stream 0x642, key the
    /// person and the year). He goes to a household of the same settlement that has a free bed (the lowest row); with none to take him he stays and is asked again next year. Returns how many left.
    /// </summary>
    public static int Daily(FamilyWorld w, int day, int hero)
    {
        var sb = w.St.Succession; if (sb == null) return 0;
        var pt = w.People; var hht = w.Households; int left = 0;
        foreach (var k in sb.Kin.OrderBy(k => k.Person.Value).ToList())
        {
            int row = pt.IndexOf(k.Person);
            if (row < 0 || row == hero || pt.Status[row] != PersonStatus.Alive || pt.AgeYears(row, day) < w.S.AdultAge || !Birthday(pt, row, day)) continue;
            int hh = pt.Household[row]; if (hh < 0 || hh != pt.Household[hero]) continue;            // only those under his roof can leave it
            var g = w.Book.Grievances.GetValueOrDefault(k.Person.Value);
            int loyalty = FamilyBook.Loyalty(w.TrustPm(k.Person), g, w.S.GrievanceWeightPm);
            if (loyalty >= w.S.TurnBelowPm) continue;
            if (!Leaves(w.S, w.Seed, k.Person.Value, day / TimeConst.DaysPerYear)) continue;
            int to = Shelter(w, row, hh);
            if (to < 0) continue;
            hht.RemoveMember(hh, row); hht.AddMemberLater(to, row);
            pt.Household[row] = to; pt.HomeSettlement[row] = hht.Settlement[to]; pt.HomeLot[row] = hht.Lot[to]; pt.Bed[row] = -1; pt.Class[row] = hht.Class[to];
            w.Pop.ResetKin();
            left++;
            var ps = new List<ChronParam> { ChronParam.Person("person", k.Person), ChronParam.Person("hero", w.HeroId), ChronParam.Place("place", w.PlaceId(hht.Settlement[to])) };
            w.Chronicle.Add(new ChronicleEntry(w.Ids.Allocate(IdKind.Event), new GameTime(day * 86400L + 4 * 3600), "kin_left", [k.Person, w.HeroId], CausesOf(g),
                new Dictionary<string, string> { ["loyalty"] = loyalty.ToString(CultureInfo.InvariantCulture) }, TextKey: "ch_family_kin_left", Params: ps));
        }
        return left;
    }

    /// <summary>The household that takes in a kin member who leaves: the lowest row of his settlement, not his own, with a free bed and the same or the next class; -1 when none.</summary>
    private static int Shelter(FamilyWorld w, int row, int from)
    {
        var hht = w.Households; int s = hht.Settlement[from];
        for (int h = 0; h < hht.Count; h++)
        {
            if (h == from || hht.MemberCount[h] == 0 || hht.Settlement[h] != s || hht.Head[h] < 0) continue;
            if (hht.MemberCount[h] >= hht.Beds[h] || Math.Abs((int)hht.Class[h] - (int)w.People.Class[row]) > 1) continue;
            return h;
        }
        return -1;
    }
}
