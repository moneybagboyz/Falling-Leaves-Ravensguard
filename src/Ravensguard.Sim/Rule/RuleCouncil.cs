using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Rule;

/// <summary>One member of a council: the person, what he thinks of the ruler, and the weight of his households.</summary>
public readonly record struct CouncilMember(long Person, int Opinion, int Weight);

/// <summary>
/// Councils, subsidies and tallage (rule-and-income 4.3). The ruler calls a council of the great lords of the realm and asks a tenth of movables in towns and a fifteenth in the country for a stated cause; each member
/// votes yes when <c>opinion of the ruler + the cause's weight + a draw of -10..10 &gt;= 0</c> (stream 0x662, key the council and the member), a majority weighted by households passes it, and the levy is taken over the
/// next season from the households' real coin and stock; each household that pays carries a grievance for a season. Tallage is the ruler's own, at will, on one town, with no council.
/// </summary>
public static class RuleCouncil
{
    /// <summary>The vote of a council (RI-7), pure: each member's yes or no with the sum that was weighed, the totals by weight, and whether it passed. <paramref name="jitter"/> gives a member's draw (0 in the golden test).</summary>
    public static (List<(long Member, bool Yes, int Score)> Votes, int Yes, int No, long YesWeight, long NoWeight, bool Passed) Tally(RuleSettingsDef s, IReadOnlyList<CouncilMember> members, int causeWeight, Func<long, int> jitter)
    {
        var votes = new List<(long, bool, int)>(); int yes = 0, no = 0; long yw = 0, nw = 0;
        foreach (var m in members.OrderBy(x => x.Person))
        {
            int score = m.Opinion + causeWeight + jitter(m.Person);
            bool y = score >= 0; votes.Add((m.Person, y, score));
            if (y) { yes++; yw += m.Weight; } else { no++; nw += m.Weight; }
        }
        return (votes, yes, no, yw, nw, RuleMath.Passed(yw, nw));
    }

    /// <summary>The great lords of a realm who sit: the living holders of its fiefs here, in ascending person id, weighted by their households.</summary>
    public static List<CouncilMember> Members(RuleCtx c, int realm)
    {
        var list = new List<CouncilMember>(); var rk = c.RealmKey(realm).Value;
        foreach (var f in c.B.Fiefs.Values)
        {
            if (f.Realm != realm || f.Forfeited || f.Holder < 0 || !c.Alive(f.Holder)) continue;
            int op = c.B.Opinion((ulong)f.Holder, rk) + c.StandingWithRealm(new EntityId((ulong)f.Holder), realm) / 10;
            list.Add(new CouncilMember(f.Holder, op, Math.Max(1, c.HouseholdsOfFief(f.Fief).Count())));
        }
        return list;
    }

    /// <summary>
    /// The ruler of <paramref name="realm"/> calls a council and asks a subsidy for <paramref name="cause"/> ("defence", "casus_belli" or "none") (4.3). Returns the record, with each member's vote and reason. A passed subsidy
    /// is a levy due at the next season's start; the chronicle says who called it and how it went.
    /// </summary>
    public static CouncilRecord Call(RuleCtx c, int realm, string cause, int day)
    {
        var s = c.S;
        bool second = c.B.LastSubsidyDay.TryGetValue(realm, out int last) && day - last < s.SecondSubsidyYears * TimeConst.DaysPerYear;
        int weight = RuleMath.CauseWeight(s, cause, second);
        var rec = new CouncilRecord { Serial = c.B.NextCouncil++, Day = day, Realm = realm, Cause = cause };
        var t = Tally(s, Members(c, realm), weight, m => DetRng.Range(c.Seed, day, (ulong)rec.Serial, RngStreams.CouncilVotes, (uint)(m & 0xFFFFFFFFL), -s.VoteJitter, s.VoteJitter + 1));
        rec.Yes = t.Yes; rec.No = t.No; rec.Passed = t.Passed && t.Yes + t.No > 0; rec.Votes.AddRange(t.Votes);
        c.B.Councils.Add(rec);
        string note = cause switch { "defence" => "ch_note_council_defence", "casus_belli" => "ch_note_council_casus_belli", _ => "ch_note_council_no_war" };
        var held = c.Chron.Chron(RuleCtx.At(day), rec.Passed ? "subsidy_granted" : "subsidy_refused", rec.Passed ? "ch_rule_subsidy_granted" : "ch_rule_subsidy_refused",
            [ChronParam.Count("yes", rec.Yes), ChronParam.Count("no", rec.No)], [c.RealmKey(realm)], [new Cause(CauseKind.Diplomacy, null, NoteKey: note)]);
        if (rec.Passed)
        {
            c.B.LastSubsidyDay[realm] = day;
            int due = day - day % TimeConst.DaysPerSeason + TimeConst.DaysPerSeason;
            c.B.Levies.Add(new LevyRow { Serial = rec.Serial, Realm = realm, DueDay = due, TownPermille = s.SubsidyTownPermille, CountryPermille = s.SubsidyCountryPermille });
        }
        _ = held;
        return rec;
    }

    // ------------------------------------------------------------------ movables

    /// <summary>A household's movables (4.3): the coin in its purse and the stock in its store at book value.</summary>
    public static long Movables(RuleCtx c, int household)
    {
        var e = c.W.Economy; long v = c.Led.Balance(e.HouseholdPurse[household]).Mites;
        var store = e.HouseholdStore[household]; var cat = e.Goods;
        foreach (var (g, q) in store.Rows())
        {
            long milli = cat.BasePriceMilliMites(g);
            v += cat.IsPiece(g) ? milli * q / 1000 : milli * q / 1_000_000;
        }
        return v;
    }

    /// <summary>The realm a settlement pays: its fief's, else its own.</summary>
    public static int RealmOfSettlement(RuleCtx c, int s)
    {
        var st = c.Region.Settlements[s];
        return st.FiefIndex >= 0 && st.FiefIndex < c.Region.Fiefs.Count && c.Region.Fiefs[st.FiefIndex].Realm >= 0 ? c.Region.Fiefs[st.FiefIndex].Realm : st.Realm;
    }

    /// <summary>
    /// Collects the levies that have come due (the season's collection, after the officials' rounds): each household of the realm pays its share of movables (a tenth in a town, a fifteenth in the country) to the realm's
    /// treasury by a ledger transfer (reason <c>Subsidy</c>) from the coin it holds, and carries a grievance for a season (4.3). Returns the coin collected.
    /// </summary>
    public static long CollectLevies(RuleCtx c, int day)
    {
        long total = 0; var hht = c.Households;
        foreach (var lv in c.B.Levies.ToList())
        {
            if (day < lv.DueDay) continue;
            long got = 0; int payers = 0;
            for (int h = 0; h < hht.Count; h++)
            {
                if (hht.MemberCount[h] == 0) continue;
                int s = hht.Settlement[h]; if (RealmOfSettlement(c, s) != lv.Realm) continue;
                int permille = c.Region.Settlements[s].Class == SettlementClass.Town ? lv.TownPermille : lv.CountryPermille;
                long take = RuleMath.OfMovables(Movables(c, h), permille);
                long paid = c.PayUpTo(Party.OfHousehold(hht.IdOf(h)), Party.OfRealm(lv.Realm), take, TransferReason.Subsidy);
                if (paid <= 0) continue;
                got += paid; payers++;
                RuleDues.AddGrievance(c, h, c.S.SubsidyGrievances, c.S.GrievanceSeasons, day, EntityId.None);
            }
            total += got;
            var rec = c.B.Councils.FirstOrDefault(r => r.Serial == lv.Serial); if (rec != null) rec.AmountMites += got;
            c.Chron.Chron(RuleCtx.At(day), "subsidy_collected", "ch_rule_subsidy_collected", [ChronParam.Mites("amount", got), ChronParam.Count("households", payers)], [c.RealmKey(lv.Realm)], [new Cause(CauseKind.Diplomacy, null, NoteKey: "ch_note_council_granted")]);
            c.B.Levies.Remove(lv);
        }
        return total;
    }

    // ------------------------------------------------------------------ tallage

    /// <summary>
    /// Tallage (4.3, RI-8): the ruler takes up to a tenth of a town's movables at will, no council, to <paramref name="to"/> (the realm's or a lord's treasury); each household that pays carries two grievances for a season
    /// and the town's mayor's opinion of the ruler falls by 30. Returns the coin taken.
    /// </summary>
    public static long Tallage(RuleCtx c, int settlement, Party to, int day)
    {
        long total = 0; var hht = c.Households; int payers = 0;
        for (int h = 0; h < hht.Count; h++)
        {
            if (hht.MemberCount[h] == 0 || hht.Settlement[h] != settlement) continue;
            long take = RuleMath.OfMovables(Movables(c, h), c.S.TallagePermille);
            long paid = c.PayUpTo(Party.OfHousehold(hht.IdOf(h)), to, take, TransferReason.Tallage);
            if (paid <= 0) continue;
            total += paid; payers++;
            RuleDues.AddGrievance(c, h, c.S.TallageGrievances, c.S.GrievanceSeasons, day, EntityId.None);
        }
        var pt = c.People; EntityId mayor = EntityId.None;
        for (int i = 0; i < pt.Count; i++) if (pt.IsAlive(i) && pt.Occupation[i] == Occupation.Mayor && pt.HomeSettlement[i] == settlement) { mayor = pt.IdOf(i); break; }
        if (mayor != EntityId.None) c.B.AddOpinion(mayor.Value, c.KeyOf(to).Value, c.S.TallageMayorOpinion);
        var place = settlement >= 0 && settlement < c.Region.Settlements.Count ? c.Region.Settlements[settlement].Id : EntityId.None;
        var ps = new List<ChronParam> { ChronParam.Mites("amount", total), ChronParam.Count("households", payers) }; if (place != EntityId.None) ps.Add(ChronParam.Place("place", place));
        c.Chron.Chron(RuleCtx.At(day), "tallage_taken", "ch_rule_tallage_taken", ps, place == EntityId.None ? [c.KeyOf(to)] : [place, c.KeyOf(to)], [new Cause(CauseKind.Tax, null, NoteKey: "ch_note_tallage_at_will")]);
        return total;
    }

    // ------------------------------------------------------------------ NPC rulers

    /// <summary>
    /// An NPC ruler calls a council when the realm's treasury holds less than two seasons of the fiefs' payroll and a war is on (4.3): <see cref="RuleCtx.WarCause"/> says whether and why (no war exists before the diplomacy
    /// step, so this is idle in a Phase 6 world until D2). At most one council a realm a season.
    /// </summary>
    public static int NpcCouncils(RuleCtx c, int day)
    {
        int n = 0; var realms = c.B.Fiefs.Values.Select(f => f.Realm).Where(r => r >= 0).Distinct().OrderBy(r => r);
        foreach (int r in realms)
        {
            string war = c.WarCause(r); if (war.Length == 0) continue;
            if (c.B.Councils.Any(x => x.Realm == r && day - x.Day < TimeConst.DaysPerSeason)) continue;
            var tax = c.Tax; if (tax == null) continue;
            long payroll = 0; foreach (var f in c.B.Fiefs.Values) if (f.Realm == r) payroll += tax.PayrollSeason(f.Fief);
            if (c.Balance(Party.OfRealm(r)) >= payroll * c.S.NpcCouncilPayrollSeasons) continue;
            Call(c, r, war, day); n++;
        }
        return n;
    }
}
