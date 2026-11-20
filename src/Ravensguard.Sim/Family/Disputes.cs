using System.Globalization;
using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Succession;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Family;

/// <summary>
/// Disputed inheritances (family-and-lifespan 4.5, FL-12; the stream Phase 5 reserved, 0x561: drawn here and nowhere else). When the hero dies and his will passed over an adult child, the child holds the grievance
/// <see cref="KinGrievance.PassedOver"/>; if his loyalty (trust in the dead hero less 150 a grievance) is below the turn line he contests the will before the court of the place the hero lived in. The court decides law against
/// will: the law overrides the will with <c>ContestLawPm</c> permille plus a tenth of the difference of the two claimants' standing there (held to 5..95 percent), one draw on the reserved stream keyed on the claimant and the day. When the
/// law wins he is paid an equal share of the coin the heir holds (the adult children counted, the heir with them) through the ledger (<see cref="TransferReason.Inheritance"/>); otherwise the will stands and he keeps his
/// grievance. One dispute for each child. The heir who takes up the dead hero's id keeps what the will gave him in every case but the share. Called by the succession desk once the estate has settled.
/// </summary>
public static class Disputes
{
    /// <summary>The permille at which the law beats the will for a claimant (before the draw).</summary>
    public static int LawPm(FamilySettingsDef s, int claimantStandingPm, int heirStandingPm) =>
        Math.Clamp(s.ContestLawPm + (claimantStandingPm - heirStandingPm) / Math.Max(1, s.ContestStandingDiv), 50, 950);

    /// <summary>The court's draw: does the law override the will for this claimant on this day (one draw, keyed on the claimant and the day)?</summary>
    public static bool Overturns(ulong seed, int day, ulong claimant, int lawPm) => DetRng.Range(seed, day, claimant, RngStreams.InheritanceDisputesReserved, 0, 0, 1000) < lawPm;

    /// <summary>The contests of a hero's death; returns how many were brought. <paramref name="will"/> is the dead hero's will as it stood before the estate settled (null: no will, the law ran, nobody was passed over).</summary>
    public static int Contest(WorldState st, ContentDatabase? db, HeroParty party, PurseId heirPurse, int deadRow, int heirRow, Will? will, int day, long sec)
    {
        if (will == null || deadRow < 0 || st.Economy == null) return 0;
        var w = FamilyWorld.From(st, db); var pt = w.People; var book = w.Book; var s = w.S; int brought = 0;
        var kids = w.Pop.Kin.Children(deadRow).ToArray().Where(c => pt.Status[c] == PersonStatus.Alive && pt.AgeYears(c, day) >= s.AdultAge).OrderBy(c => c).ToList();
        int share = kids.Count + (heirRow >= 0 && !kids.Contains(heirRow) ? 1 : 0);
        int place = pt.HomeSettlement[deadRow];
        foreach (int c in kids)
        {
            var id = pt.IdOf(c);
            if (c == heirRow || will.Names(id) || book.Contested.Contains(id.Value) || st.Bonds?.Has(id) == true) continue;
            var g = book.Grievances.GetValueOrDefault(id.Value) | KinGrievance.PassedOver;
            book.Grievances[id.Value] = g;
            int trust = party.Known.TryGetValue(id, out var a) ? a.TrustPm : 0;
            int loyalty = FamilyBook.Loyalty(trust, g, s.GrievanceWeightPm);
            if (loyalty >= s.TurnBelowPm) continue;
            book.Contested.Add(id.Value); brought++;
            int claimant = w.StandingOf(id, place), heir = w.StandingOf(party.Hero, place);
            int p = LawPm(s, claimant, heir);
            bool overturned = Overturns(w.Seed, day, id.Value, p);
            long paid = 0;
            if (overturned && share > 0)
            {
                long coin = st.Economy.Ledger.Balance(heirPurse).Mites;
                long due = coin / share;
                int hh = pt.Household[c];
                if (due > 0 && hh >= 0 && hh < st.Economy.HouseholdCount && st.Economy.Ledger.Transfer(heirPurse, st.Economy.HouseholdPurse[hh], Coin.FromMites(due), TransferReason.Inheritance)) paid = due;
            }
            var causes = KinTurning.CausesOf(g); causes.Add(new Cause(CauseKind.Law, null, NoteKey: overturned ? "ch_note_court_law" : "ch_note_court_will"));
            var ps = new List<ChronParam>
            {
                ChronParam.Person("person", id), ChronParam.Person("heir", party.Hero), ChronParam.Place("place", w.PlaceId(place)),
                ChronParam.TextKey("outcome", overturned ? "ch_family_contest_law" : "ch_family_contest_will"), ChronParam.Mites("share", paid),
            };
            w.Chronicle.Add(new ChronicleEntry(w.Ids.Allocate(IdKind.Event), new GameTime(sec), "will_contested", [id, party.Hero], causes,
                new Dictionary<string, string> { ["share"] = paid.ToString(CultureInfo.InvariantCulture) }, TextKey: "ch_family_contested", Params: ps));
        }
        if (book.IsEmpty) st.Family = null;
        return brought;
    }
}
