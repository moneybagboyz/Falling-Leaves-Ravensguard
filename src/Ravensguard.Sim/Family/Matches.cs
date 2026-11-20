using System.Globalization;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Politics;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Succession;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Family;

/// <summary>Where an arranged match stands (family-and-lifespan 4.2): the overlord's fee still owing, betrothed with a wedding day, wed, or given up. Append only (saved as a byte).</summary>
public enum MatchState : byte { AwaitingFee, Betrothed, Wed, Lapsed }

/// <summary>
/// An arranged match (family-and-lifespan 3, 4.2): two people the heads of their houses have promised to each other, the dowry that moves at the wedding, whether it joins two lords' houses (an alliance), the fief whose heir marries
/// (the overlord's marriage right, 4.2 of rule-and-income) and what was done about his consent: <see cref="Consent"/> bought (the fee is the due <see cref="DueSerial"/>), or <see cref="Defied"/>.
/// </summary>
public sealed class Match
{
    public int Id;
    public ulong Groom, Bride;
    public long DowryMites;
    public bool Alliance;
    public int WeddingDay = -1;
    public MatchState State;
    /// <summary>The fief whose heir is one of the two (the overlord's consent is needed), -1 none.</summary>
    public int Fief = -1;
    public bool Consent, Defied;
    public int ProposedDay;
    /// <summary>Arranged by the hero (for a member of his household), else by a lord.</summary>
    public bool ByHero;
    /// <summary>The answering head's utility at the proposal.</summary>
    public int Score;
    /// <summary>The overlord's due for the consent (the rule book's serial), 0 none.</summary>
    public int DueSerial;
    /// <summary>The arranging head's person id (the hero's is 0), -1 none.</summary>
    public long Lord = -1;
}

/// <summary>The terms of the answering head's utility (4.2), in the order the panel says them.</summary>
public readonly record struct MatchTerms(int Alliance, int Standing, int Opinion, int Dowry, int AgeGap)
{
    public int Total => Alliance + Standing + Opinion + Dowry - AgeGap;
}

/// <summary>One candidate a head may arrange a match with: who, the answering house's utility terms.</summary>
public sealed record MatchCandidate(int Row, ulong Person, string Name, int Age, MatchTerms Terms, long DowryMites, bool Alliance, int Fief);

/// <summary>
/// Arranged matches and alliances (family-and-lifespan 4.2; FL-5, FL-6). The head of a house arranges a match for an unmarried member (the hero for his household's; every lord with a grown child each season, stream 0x645);
/// the answering head accepts at a utility of 50: <c>30 x alliance + standing / 10 + the heads' opinion + dowry / 20,000 - 20 x years of age gap over 10</c>. The dowry is within half the class share either way and moves at
/// the wedding (<see cref="TransferReason.Dowry"/>). A match between two lords' houses of different fiefs is an alliance: the opinions <c>marriage</c> (+30) and <c>kin</c> (+20) both ways and, between two realms, a marriage
/// treaty in the diplomacy book. A fief's heir may not marry without his overlord's consent (rule and income: the fee, <see cref="RuleDues.SellMarriage"/>; or a fine and his opinion lost,
/// <see cref="RuleDues.MarryWithoutConsent"/>). A member married against his will carries the grievance married_off. Pure in the tables and the day; the one draw is the lord's choice (0x645).
/// </summary>
public static class Matches
{
    // ------------------------------------------------------------------ who is a lord, who is an heir

    /// <summary>The fief a person holds (the live holder of its deed), -1 when he holds none.</summary>
    public static int FiefHeldBy(FamilyWorld w, EntityId person)
    {
        if (w.St.Rule is not { } rb) return -1;
        foreach (var f in rb.Fiefs.Values) if (!f.Forfeited && f.Holder >= 0 && (ulong)f.Holder == person.Value) return f.Fief;
        return -1;
    }

    /// <summary>The row of a fief holder's heir: his eldest living son, else his eldest living daughter (the law of the fief; the culture's partible rule does not split a fief), -1 none.</summary>
    public static int FiefHeirRow(FamilyWorld w, int holderRow)
    {
        if (holderRow < 0) return -1;
        var pt = w.People; int best = -1;
        // sons before daughters, the elder first, ties by row
        static long Key(PersonTable t, int r) => (t.Sex[r] == Sex.Male ? 0L : 1L << 40) + ((long)t.BirthDay[r] + (1L << 20)) * 1_000_000L + r;
        foreach (int c in w.Pop.Kin.Children(holderRow))
        {
            if (pt.Status[c] != PersonStatus.Alive) continue;
            if (best < 0 || Key(pt, c) < Key(pt, best)) best = c;
        }
        return best;
    }

    /// <summary>The fief whose heir a person is (the one the overlord's marriage right covers), -1 when he is nobody's.</summary>
    public static int HeirOfFief(FamilyWorld w, int row)
    {
        if (w.St.Rule is not { } rb || row < 0) return -1;
        foreach (var f in rb.Fiefs.Values)
        {
            if (f.Forfeited || f.Holder < 0) continue;
            int holder = w.RowOf(new EntityId((ulong)f.Holder));
            if (holder >= 0 && FiefHeirRow(w, holder) == row) return f.Fief;
        }
        return -1;
    }

    /// <summary>The id of a house's head (the hero's is the zero id, so "no head" is asked of <see cref="HasHead"/>, never read from the id).</summary>
    private static EntityId HeadId(FamilyWorld w, int hh) { int h = w.Households.Head[hh]; return h < 0 ? EntityId.None : h == w.HeroRow ? w.HeroId : w.People.IdOf(h); }
    private static bool HasHead(FamilyWorld w, int hh) => w.Households.Head[hh] >= 0;

    /// <summary>Whether a match between two houses is an alliance (4.2): both heads are lords of different fiefs.</summary>
    public static bool IsAlliance(FamilyWorld w, int houseA, int houseB)
    {
        if (houseA < 0 || houseB < 0 || houseA == houseB) return false;
        if (!HasHead(w, houseA) || !HasHead(w, houseB)) return false;
        int fa = FiefHeldBy(w, HeadId(w, houseA)), fb = FiefHeldBy(w, HeadId(w, houseB));
        return fa >= 0 && fb >= 0 && fa != fb;
    }

    // ------------------------------------------------------------------ the utility

    /// <summary>The class share of the bride's house's coin that a dowry is measured by (4.2).</summary>
    public static long ClassShare(FamilyWorld w, int brideHouse) => w.Ledger.Balance(w.HouseholdPurse(brideHouse)).Mites * w.F.DowryPct(w.Households.Class[brideHouse]) / 100;

    /// <summary>The answering head's utility terms for a match proposed by <paramref name="propHouse"/> to <paramref name="ansHouse"/>.</summary>
    public static MatchTerms Terms(FamilyWorld w, int propHouse, int ansHouse, int groomRow, int brideRow, long dowry, int day)
    {
        var pt = w.People; var s = w.S;
        int alliance = IsAlliance(w, propHouse, ansHouse) ? s.MatchAllianceValue : 0;
        var prop = HeadId(w, propHouse); var ans = HeadId(w, ansHouse);
        bool heads = HasHead(w, propHouse) && HasHead(w, ansHouse);
        int standing = HasHead(w, propHouse) ? w.StandingOf(prop, w.Households.Settlement[ansHouse]) / Math.Max(1, s.MatchStandingDiv) : 0;
        int opinion = w.St.Rule is { } rb && heads ? Opinions.Value(rb, ans.Value, prop.Value) : 0;
        int gap = Math.Abs(pt.AgeYears(groomRow, day) - pt.AgeYears(brideRow, day));
        return new MatchTerms(alliance, standing, opinion, (int)Math.Min(100, dowry / Math.Max(1, s.MatchDowryDiv)), Math.Max(0, gap - s.MatchAgeGapFree) * s.MatchAgeGapPenalty);
    }

    // ------------------------------------------------------------------ who may be matched

    private static int MinAge(FamilyWorld w, int row) => w.People.Sex[row] == Sex.Female ? w.Rates.MarryAgeMinFemale : w.Rates.MarryAgeMinMale;

    /// <summary>Why a person cannot be matched ("" when they can): free, grown to marrying age, not held, not bonded.</summary>
    public static string WhyNot(FamilyWorld w, int row, int day)
    {
        var pt = w.People;
        if (row < 0 || row >= pt.Count || pt.Status[row] != PersonStatus.Alive) return "fm_gone";
        if (pt.Spouse[row] >= 0) return "fm_they_wed";
        if (pt.AgeYears(row, day) < MinAge(w, row)) return "fm_too_young";
        if (w.St.Captivity?.Find(pt.IdOf(row)) is { Held: true }) return "fm_held";
        if (w.St.Bonds?.Has(pt.IdOf(row)) == true) return "fm_held";
        if (pt.Household[row] < 0) return "fm_not_so";
        if (w.Book.Matches.Values.Any(m => m.State is MatchState.AwaitingFee or MatchState.Betrothed && (m.Groom == pt.IdOf(row).Value || m.Bride == pt.IdOf(row).Value))) return "fm_match_open";
        return "";
    }

    /// <summary>The loyalty of a kin member of the hero (4.5): his trust in the hero less 150 a grievance.</summary>
    public static int KinLoyalty(FamilyWorld w, EntityId person) => FamilyBook.Loyalty(w.TrustPm(person), w.Book.Grievances.GetValueOrDefault(person.Value), w.S.GrievanceWeightPm);

    private static bool Fits(FamilyWorld w, int member, int cand, int day)
    {
        var pt = w.People;
        if (pt.Sex[member] == pt.Sex[cand] || pt.Household[member] == pt.Household[cand]) return false;
        int hh = pt.Household[cand]; if (w.Households.Head[hh] == cand) return false;
        if (Math.Abs((int)w.Households.Class[pt.Household[member]] - (int)w.Households.Class[hh]) > 1) return false;
        if (Courting.Kin2(w, member).Contains(cand)) return false;
        return WhyNot(w, cand, day).Length == 0;
    }

    /// <summary>The candidates for a member (4.2): persons of the same or the next class who are free and not kin, with the answering house's utility at the class share of the dowry; best first (utility, then the lower row).</summary>
    public static List<MatchCandidate> Candidates(FamilyWorld w, int member, int day, int limit = 12)
    {
        var pt = w.People; var res = new List<MatchCandidate>();
        if (WhyNot(w, member, day).Length > 0) return res;
        int mh = pt.Household[member];
        for (int c = 0; c < pt.Count; c++)
        {
            if (c == member || !Fits(w, member, c, day)) continue;
            int ch = pt.Household[c]; var (groom, bride) = pt.Sex[member] == Sex.Male ? (member, c) : (c, member);
            int brideHouse = pt.Household[bride];
            long dowry = ClassShare(w, brideHouse);
            var terms = Terms(w, mh, ch, groom, bride, dowry, day);
            res.Add(new MatchCandidate(c, pt.IdOf(c).Value, pt.FullName(c), pt.AgeYears(c, day), terms, dowry, IsAlliance(w, mh, ch), HeirOfFief(w, c)));
        }
        res.Sort((a, b) => { int u = b.Terms.Total.CompareTo(a.Terms.Total); return u != 0 ? u : a.Row.CompareTo(b.Row); });
        return res.Count > limit ? res.GetRange(0, limit) : res;
    }

    // ------------------------------------------------------------------ the overlord's consent (rule and income 4.2)

    /// <summary>The fief whose overlord's consent this match needs: the fief one of the two is heir of, when it has an overlord; -1 none.</summary>
    public static int ConsentFief(FamilyWorld w, int a, int b)
    {
        if (w.St.Rule is not { } rb) return -1;
        foreach (int row in new[] { a, b })
        {
            int f = HeirOfFief(w, row);
            if (f >= 0 && rb.FiefOf(f) is { } fr && fr.Realm >= 0 || f >= 0 && rb.FiefOf(f) is { OverlordFief: >= 0 }) return f;
        }
        return -1;
    }

    /// <summary>
    /// The overlord's answer (4.2): while his opinion of the holder is at least the floor he sells his consent for a season of the fief's receipts; the fee is a due from the holder's house, paid at once as far as the house
    /// holds the coin. Returns the due (null: no overlord or he refuses, with <paramref name="refused"/>).
    /// </summary>
    public static DueRow? AskConsent(FamilyWorld w, int fief, int day, out bool refused)
    {
        refused = false;
        var c = w.RuleAt(); if (c == null || c.B.FiefOf(fief) is not { } f || f.Holder < 0) return null;
        var overlord = c.OverlordOf(f); if (overlord.IsNone) return null;
        if (Opinions.Value(c.B, c.KeyOf(overlord).Value, (ulong)f.Holder) < w.S.ConsentOpinionMin) { refused = true; return null; }
        var due = RuleDues.SellMarriage(c, fief, day); if (due == null) return null;
        Settle(c, due);
        return due;
    }

    /// <summary>Pays what the payer holds of a due (the season's settling would, a little later).</summary>
    private static void Settle(RuleCtx c, DueRow d)
    {
        long pay = c.PayUpTo(d.Payer, d.Payee, d.Owing, TransferReason.Fee);
        d.PaidMites += pay;
        if (d.Owing == 0) d.State = DueState.Paid;
    }

    // ------------------------------------------------------------------ the hero arranges a match

    /// <summary>
    /// The hero arranges a match for an unmarried member of his household (4.2): <paramref name="candidate"/> is the person the houses consider; <paramref name="dowry"/> is negotiated within half the class share either way
    /// (-1: the share itself); <paramref name="defy"/> marries a fief's heir without the overlord's consent (the fine and his opinion lost) rather than asking. A kin member of the hero's who has turned (loyalty under the
    /// line) refuses any match.
    /// </summary>
    public static CourtResult Arrange(FamilyWorld w, ulong member, ulong candidate, long dowry, bool defy, int day)
    {
        var pt = w.People; int hero = w.HeroRow;
        int m = pt.IndexOf(new EntityId(member)), c = pt.IndexOf(new EntityId(candidate));
        if (hero < 0 || m < 0 || m == hero || pt.Household[m] != pt.Household[hero]) return new(false, "fm_not_your_member");
        string why = WhyNot(w, m, day); if (why.Length > 0) return new(false, why);
        if (c < 0) return new(false, "fm_gone");
        why = WhyNot(w, c, day); if (why.Length > 0) return new(false, why);
        if (!Fits(w, m, c, day)) return new(false, "fm_not_a_fit");
        if (KinLoyalty(w, pt.IdOf(m)) < w.S.TurnBelowPm) return new(false, "fm_kin_refuses", pt.FullName(m));
        return Propose(w, m, c, dowry, defy, day, byHero: true, lord: (long)w.HeroId.Value);
    }

    /// <summary>The proposal itself, for the hero's members and the lords' children alike: the answering head's utility, the dowry, the overlord's consent, then a betrothal.</summary>
    private static CourtResult Propose(FamilyWorld w, int member, int cand, long dowry, bool defy, int day, bool byHero, long lord)
    {
        var pt = w.People; var s = w.S;
        var (groom, bride) = pt.Sex[member] == Sex.Male ? (member, cand) : (cand, member);
        int mh = pt.Household[member], ch = pt.Household[cand], brideHouse = pt.Household[bride];
        long share = ClassShare(w, brideHouse);
        if (dowry < 0) dowry = share;
        long lo = share * (100 - s.MatchDowrySwingPct) / 100, hi = share * (100 + s.MatchDowrySwingPct) / 100;
        if (dowry < lo || dowry > hi) return new(false, "fm_dowry_range", lo, hi);
        if (w.Ledger.Balance(w.HouseholdPurse(brideHouse)).Mites < dowry) return new(false, "fm_cannot_afford");
        var terms = Terms(w, mh, ch, groom, bride, dowry, day);
        if (terms.Total < s.MatchAcceptAt) return new(false, "fm_match_no", pt.FullName(cand), terms.Total);
        int fief = ConsentFief(w, member, cand);
        var match = new Match
        {
            Id = w.Book.NextMatch++, Groom = pt.IdOf(groom).Value, Bride = pt.IdOf(bride).Value, DowryMites = dowry, Alliance = IsAlliance(w, mh, ch), Fief = fief, ProposedDay = day, ByHero = byHero, Score = terms.Total, Lord = lord,
            State = MatchState.Betrothed,
        };
        if (fief >= 0)
        {
            if (defy)
            {
                var rc = w.RuleAt();
                if (rc != null) RuleDues.MarryWithoutConsent(rc, fief, day);
                match.Defied = true;
            }
            else
            {
                var due = AskConsent(w, fief, day, out bool refused);
                if (refused) return new(false, "fm_overlord_refuses");
                if (due != null) { match.DueSerial = due.Serial; match.Consent = true; if (due.State != DueState.Paid) match.State = MatchState.AwaitingFee; }
            }
        }
        w.Book.Matches[match.Id] = match;
        if (match.State == MatchState.Betrothed) match.WeddingDay = FamilyRules.NextHolyDay(s, day);
        // a member married against his own will carries the grievance (4.5): his acceptance is the hero's trust-based score without a jitter
        if (byHero) ApplyMarriedOff(w, member, cand, day);
        Write(w, "match_arranged", "ch_family_match_arranged", day, [pt.IdOf(groom), pt.IdOf(bride)],
            [ChronParam.Person("groom", pt.IdOf(groom)), ChronParam.Person("bride", pt.IdOf(bride)), ChronParam.Place("place", w.PlaceId(w.Households.Settlement[mh]))],
            [new Cause(CauseKind.Family, null, NoteKey: byHero ? "ch_note_match_head" : "ch_note_match_lord")]);
        return new(true, match.State == MatchState.AwaitingFee ? "fm_match_waits_fee" : "fm_match_made", pt.FullName(cand), match.WeddingDay);
    }

    private static void ApplyMarriedOff(FamilyWorld w, int member, int cand, int day)
    {
        var pt = w.People; var s = w.S;
        int gap = Math.Abs((int)w.Households.Class[pt.Household[member]] - (int)w.Households.Class[pt.Household[cand]]);
        int own = FamilyRules.Acceptance(s, w.TrustPm(pt.IdOf(member)), gap, 0, 0, 0);
        if (own >= s.MarriedOffBelow) return;
        var book = w.Book; ulong id = pt.IdOf(member).Value;
        book.Grievances[id] = book.Grievances.GetValueOrDefault(id) | KinGrievance.MarriedOff;
    }

    // ------------------------------------------------------------------ lords arrange their children's matches (each season)

    /// <summary>
    /// A season's first day (S6 in the spec; run in the family's day at the season's start): each lord with a grown unmarried child of his house, in fief order, arranges a match with the chance <c>LordMatchPct</c> (stream
    /// 0x645, key his house and the season): the best candidate among the other lords' grown unmarried children (the answering head's utility, ties by row), at the class share, with the overlord's fee when a fief's heir weds.
    /// The hero's own house is left to him. Returns the matches made.
    /// </summary>
    public static int LordsSeason(FamilyWorld w, int day)
    {
        if (w.St.Rule is not { } rb) return 0;
        int season = day / TimeConst.DaysPerSeason;
        var pt = w.People; var hht = w.Households; int made = 0, hero = w.HeroRow;
        foreach (var f in rb.Fiefs.Values.OrderBy(x => x.Fief).ToList())
        {
            if (f.Forfeited || f.Holder < 0) continue;
            int holder = w.RowOf(new EntityId((ulong)f.Holder));
            if (holder < 0 || holder == hero || pt.Status[holder] != PersonStatus.Alive) continue;
            int hh = pt.Household[holder]; if (hh < 0 || hht.Head[hh] != holder) continue;
            ulong key = hht.IdOf(hh).Value;
            int child = -1;
            foreach (int k in w.Pop.Kin.Children(holder).ToArray().OrderBy(x => x))
                if (pt.Household[k] == hh && WhyNot(w, k, day).Length == 0) { child = k; break; }
            if (child < 0) continue;
            if (DetRng.Range(w.Seed, season, key, RngStreams.LordMatches, 0, 0, 100) >= w.S.LordMatchPct) continue;
            // only other lords' children: the alliance of two houses
            foreach (var cand in Candidates(w, child, day, 64).Where(x => x.Alliance).Take(4))
                if (Propose(w, child, cand.Row, -1, false, day, byHero: false, lord: (long)pt.IdOf(holder).Value).Ok) { made++; break; }
        }
        return made;
    }

    // ------------------------------------------------------------------ the day: fees, weddings

    /// <summary>The day's matches (4.2): a match whose overlord's fee is paid is betrothed; a betrothal whose day has come is wed if the church allows and a bed is free; one that waits too long lapses.</summary>
    public static void Daily(FamilyWorld w, int day)
    {
        if (w.St.Family is not { } book || book.Matches.Count == 0) return;
        foreach (var m in book.Matches.Values.OrderBy(x => x.Id).ToList())
        {
            if (m.State == MatchState.AwaitingFee)
            {
                var rc = w.RuleAt();
                var due = rc?.B.Dues.FirstOrDefault(d => d.Serial == m.DueSerial);
                if (rc != null && due != null && due.State is not DueState.Paid) { Settle(rc, due); }
                if (due == null || due.State == DueState.Paid) { m.State = MatchState.Betrothed; m.WeddingDay = FamilyRules.NextHolyDay(w.S, day); }
                else if (day - m.ProposedDay > w.S.WeddingGiveUpDays) m.State = MatchState.Lapsed;
                continue;
            }
            if (m.State != MatchState.Betrothed || day < m.WeddingDay) continue;
            var r = Wed(w, m, day);
            if (!r.Ok && day - m.WeddingDay > w.S.WeddingGiveUpDays) m.State = MatchState.Lapsed;
        }
        // a season after its end a done match is forgotten (the chronicle keeps it)
        foreach (var m in book.Matches.Values.Where(x => x.State is MatchState.Wed or MatchState.Lapsed && day - Math.Max(x.WeddingDay, x.ProposedDay) > 2 * TimeConst.DaysPerSeason).ToList()) book.Matches.Remove(m.Id);
    }

    /// <summary>The wedding of an arranged match: the church's word for both, a bed (the groom's house, else the bride's), the priest's fee, the dowry through the ledger, the couple's link, and the alliance.</summary>
    public static CourtResult Wed(FamilyWorld w, Match m, int day)
    {
        var pt = w.People; var hht = w.Households; var ledger = w.Ledger;
        int g = pt.IndexOf(new EntityId(m.Groom)), b = pt.IndexOf(new EntityId(m.Bride));
        if (g < 0 || b < 0 || pt.Status[g] != PersonStatus.Alive || pt.Status[b] != PersonStatus.Alive || pt.Spouse[g] >= 0 || pt.Spouse[b] >= 0) { m.State = MatchState.Lapsed; return new(false, "fm_gone"); }
        if (!Courting.ChurchAllows(w, pt.IdOf(g)) || !Courting.ChurchAllows(w, pt.IdOf(b))) return new(false, "fm_church_refuses");
        int gh = pt.Household[g], bh = pt.Household[b]; if (gh < 0 || bh < 0) return new(false, "fm_gone");
        // where they live: the groom's house if it has a free bed, else the bride's
        int into;
        if (gh == bh) into = gh;
        else if (hht.MemberCount[gh] < hht.Beds[gh] || hht.Dwelling[gh] == DwellingType.None && hht.MemberCount[gh] < 12) into = gh;
        else if (hht.MemberCount[bh] < hht.Beds[bh]) into = bh;
        else return new(false, "fm_no_roof");
        int mover = into == gh ? b : g, from = pt.Household[mover];
        int settlement = hht.Settlement[into];
        // the priest's fee, from the house that takes the couple in, as far as it can pay
        var fee = Courting.FeePurse(w, settlement); long fee0 = Math.Min(w.S.WeddingFeeMites, ledger.Balance(w.HouseholdPurse(into)).Mites);
        if (fee is { } fp && fee0 > 0 && fp != w.HouseholdPurse(into)) ledger.Transfer(w.HouseholdPurse(into), fp, Coin.FromMites(fee0), TransferReason.Fee);
        // the dowry: the bride's own house gives it to the house that takes the couple in (nothing moves when that is her own)
        long dowry = 0;
        if (into != bh)
        {
            dowry = Math.Min(m.DowryMites, ledger.Balance(w.HouseholdPurse(bh)).Mites);
            if (dowry > 0) ledger.Transfer(w.HouseholdPurse(bh), w.HouseholdPurse(into), Coin.FromMites(dowry), TransferReason.Dowry);
        }
        if (from != into)
        {
            bool heroMover = mover == w.HeroRow;
            hht.RemoveMember(from, mover); hht.AddMemberLater(into, mover);
            pt.Household[mover] = into; pt.HomeSettlement[mover] = hht.Settlement[into]; pt.HomeLot[mover] = hht.Lot[into]; pt.Bed[mover] = -1; pt.Class[mover] = hht.Class[into];
            if (pt.Homeless[mover] != HomelessReason.None) pt.Homeless[mover] = HomelessReason.None;
            _ = heroMover;
        }
        pt.Spouse[g] = b; pt.Spouse[b] = g; pt.MarriageWait[g] = 0; pt.MarriageWait[b] = 0;
        w.Pop.ResetKin();
        m.State = MatchState.Wed; m.WeddingDay = day;
        var ps = new List<ChronParam> { ChronParam.Person("bride", pt.IdOf(b)), ChronParam.Person("groom", pt.IdOf(g)), ChronParam.Place("place", w.PlaceId(settlement)), ChronParam.Mites("dowry", dowry) };
        var cause = new Cause(CauseKind.Family, null, NoteKey: m.Alliance ? "ch_note_match_alliance" : "ch_note_match_wed");
        var entry = Write(w, "match_wed", "ch_family_match_wed", day, [pt.IdOf(g), pt.IdOf(b)], ps, [cause]);
        if (m.Alliance) Ally(w, m, g, b, day, entry);
        return new(true, "fm_wed", pt.FullName(b), dowry);
    }

    /// <summary>
    /// The alliance (4.2, D2's hook): the opinions <c>marriage</c> (+30) and <c>kin</c> (+20) between the two heads in both directions, and a marriage treaty between their realms when they differ; the treaty's cause is the
    /// wedding's chronicle entry.
    /// </summary>
    public static void Ally(FamilyWorld w, Match m, int g, int b, int day, EntityId entry)
    {
        var pc = w.PoliticsAt(day); if (pc == null) return;
        var pt = w.People; var rb = pc.RB;
        // the two lords: the heads of the groom's and the bride's houses before the wedding; after it, the bride's father's house is whichever head holds a fief
        int ga = FatherHouseHead(w, g), ba = FatherHouseHead(w, b);
        if (ga < 0 || ba < 0) return;
        var lg = ga == w.HeroRow ? w.HeroId : pt.IdOf(ga); var lb = ba == w.HeroRow ? w.HeroId : pt.IdOf(ba);
        if (lg == lb) return;
        foreach (var (x, y) in new[] { (lg, lb), (lb, lg) })
        {
            Opinions.Add(pc.B, rb, x.Value, y.Value, "marriage", day, entry.Value);
            Opinions.Add(pc.B, rb, x.Value, y.Value, "kin", day, entry.Value);
        }
        int fa = FiefHeldBy(w, lg), fb = FiefHeldBy(w, lb);
        int ra = fa >= 0 ? rb.FiefOf(fa)?.Realm ?? -1 : -1, rbm = fb >= 0 ? rb.FiefOf(fb)?.Realm ?? -1 : -1;
        if (ra >= 0 && rbm >= 0 && ra != rbm && !pc.B.Pacts.Any(p => !p.Ended && p.Kind == PactKind.MarriagePact && p.Between(ra, rbm)))
            Diplomacy.Sign(pc, PactKind.MarriagePact, ra, rbm, 0, entry.Value, noteKey: "ch_note_marriage_pact");
    }

    /// <summary>The head of the house a person was born into (his father's house: the head of the household of his father, else of his mother), -1 when unknown. After a wedding the mover's own household is the new one.</summary>
    private static int FatherHouseHead(FamilyWorld w, int row)
    {
        var pt = w.People; var hht = w.Households;
        foreach (int par in new[] { pt.Father[row], pt.Mother[row] })
        {
            if (par < 0 || par >= pt.Count) continue;
            int hh = pt.Household[par]; if (hh < 0) continue;
            int head = hht.Head[hh];
            if (head >= 0 && FiefHeldBy(w, head == w.HeroRow ? w.HeroId : pt.IdOf(head)) >= 0) return head;
            if (head >= 0 && par == head) return head;
        }
        int own = pt.Household[row]; return own >= 0 ? hht.Head[own] : -1;
    }

    private static EntityId Write(FamilyWorld w, string type, string key, int day, IReadOnlyList<EntityId> subjects, IReadOnlyList<ChronParam> ps, IReadOnlyList<Cause> causes)
    {
        var id = w.Ids.Allocate(IdKind.Event);
        w.Chronicle.Add(new ChronicleEntry(id, new GameTime(day * 86400L + 12 * 3600), type, subjects, causes, new Dictionary<string, string>(), TextKey: key, Params: ps));
        return id;
    }

    internal static string U(ulong v) => v.ToString(CultureInfo.InvariantCulture);
}
