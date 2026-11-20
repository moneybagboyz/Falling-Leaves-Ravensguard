using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Faith;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Rule;

/// <summary>
/// Loans and mortgages that only move coin that exists (rule-and-income 4.6, RI-13, RI-14). A lender with at least twice the principal in his purse lends at 4% a season (and 2% more to a borrower with arrears or bad
/// standing); the principal moves lender to borrower (<c>Loan</c>), interest each season (<c>Interest</c>), the principal at the term. A mortgage is a deed: on default (a payment two seasons late) the deed passes to
/// the lender, his credit closes for two years and the two think the worse of each other. A faith that forbids usury keeps its believers from lending at interest (<see cref="ChurchServices.CanOfferLoan"/>).
/// </summary>
public static class RuleLoans
{
    private static EntityId HeadOf(RuleCtx c, Party p)
    {
        if (p.Kind == PartyKind.Person) return new EntityId((ulong)p.Id);
        if (p.Kind != PartyKind.Household) return EntityId.None;
        int h = c.Households.IndexOf(new EntityId((ulong)p.Id)); if (h < 0) return EntityId.None;
        foreach (int m in c.Households.Members(h)) if (c.People.IsAlive(m)) return c.People.IdOf(m);
        return EntityId.None;
    }

    /// <summary>Whether the lender may lend at interest by his faith (a faith that forbids usury keeps its believers out; a lender with no faith row may).</summary>
    public static bool MayLend(RuleCtx c, Party lender)
    {
        var religions = c.W.Get<ChurchBook>()?.Religions; if (religions == null) return true;
        var head = HeadOf(c, lender); if (head == EntityId.None) return true;
        int ri = ChurchServices.ReligionOf(religions, c.People, head);
        return ChurchServices.CanOfferLoan(religions, ri, atInterest: true);
    }

    /// <summary>A borrower with arrears or standing below zero pays two points more (4.6).</summary>
    public static bool IsRisky(RuleCtx c, Party borrower)
    {
        foreach (var d in c.B.Dues) if (d.Payer == borrower && d.State is DueState.Arrears or DueState.Grievance) return true;
        if (borrower.Kind == PartyKind.Household)
        {
            int h = c.Households.IndexOf(new EntityId((ulong)borrower.Id));
            if (h >= 0 && c.Tax?.State.Row(h) is { Total: > 0 }) return true;
        }
        return false;
    }

    public static bool CreditClosed(RuleBook b, Party borrower, int day) => b.CreditClosedUntil.TryGetValue(((byte)borrower.Kind, borrower.Id), out int until) && day < until;

    /// <summary>
    /// A lender lends <paramref name="principal"/> to a borrower for <paramref name="termSeasons"/> seasons (4.6). Refused when the borrower's credit is closed, the lender's faith forbids interest or he holds less than
    /// twice the principal. The coin moves by a ledger transfer; nothing is made. Returns the loan, or null with the refusal key.
    /// </summary>
    public static (Loan? Loan, string Refusal) Lend(RuleCtx c, Party lender, Party borrower, long principal, int termSeasons, LoanSecurity security, ulong deedId, int day)
    {
        if (principal <= 0 || termSeasons <= 0) return (null, "ru_loan_bad_terms");
        if (CreditClosed(c.B, borrower, day)) return (null, "ru_loan_credit_closed");
        if (!MayLend(c, lender)) return (null, "ru_loan_usury_forbidden");
        if (c.Balance(lender) < principal * c.S.LoanLenderMinMultiple) return (null, "ru_loan_lender_short");
        int rate = RuleMath.RatePermille(c.S, IsRisky(c, borrower));
        var loan = new Loan { Serial = c.B.NextLoan++, Lender = lender, Borrower = borrower, PrincipalMites = principal, RatePmPerSeason = rate, StartDay = day, TermSeasons = termSeasons, Security = security, DeedId = deedId, State = LoanState.Open };
        if (!c.Pay(lender, borrower, principal, TransferReason.Loan)) { c.B.NextLoan--; return (null, "ru_loan_lender_short"); }
        var ps = new List<ChronParam> { ChronParam.Mites("amount", principal), ChronParam.Count("seasons", termSeasons) };
        loan.Entry = c.Chron.Chron(RuleCtx.At(day), "loan_made", "ch_rule_loan_made", ps, [c.KeyOf(lender), c.KeyOf(borrower)], [new Cause(CauseKind.Law, null, NoteKey: "ch_note_loan_asked")]).Value;
        c.B.Loans.Add(loan);
        return (loan, "");
    }

    /// <summary>
    /// The season's loans (S5, 4.6): each open loan whose next season has come pays its interest and, at the term, the principal. A payment missed is a season late; two seasons late is a default: a mortgage's deed
    /// passes to the lender (<see cref="DeedCause.Forfeited"/>), the borrower's credit closes for two years, each thinks 40 the worse of the other, the chronicle says why. Returns the coin that moved as interest and principal.
    /// </summary>
    public static (long Interest, long Principal) Season(RuleCtx c, int day)
    {
        long ti = 0, tp = 0;
        foreach (var l in c.B.Loans)
        {
            if (l.State != LoanState.Open) continue;
            int due = l.StartDay + (l.SeasonsPaid + l.LateSeasons + 1) * TimeConst.DaysPerSeason;
            if (day < due - TimeConst.DaysPerSeason / 2) continue;
            long interest = RuleMath.Interest(l.PrincipalMites, l.RatePmPerSeason);
            bool last = l.SeasonsPaid + 1 >= l.TermSeasons;
            long principal = last ? l.Owing : 0;
            if (c.Balance(l.Borrower) >= interest + principal && c.Pay(l.Borrower, l.Lender, interest, TransferReason.Interest) && c.Pay(l.Borrower, l.Lender, principal, TransferReason.Loan))
            {
                l.InterestPaid += interest; ti += interest; l.SeasonsPaid++; l.LateSeasons = 0;
                if (last) { l.Repaid += principal; tp += principal; l.State = LoanState.Repaid; c.Chron.Chron(RuleCtx.At(day), "loan_repaid", "ch_rule_loan_repaid", [ChronParam.Mites("amount", l.PrincipalMites), ChronParam.Mites("interest", l.InterestPaid)], [c.KeyOf(l.Lender), c.KeyOf(l.Borrower)], [new Cause(CauseKind.Law, l.Entry == 0 ? null : new EntityId(l.Entry), NoteKey: "ch_note_loan_term_ended")]); }
                continue;
            }
            l.LateSeasons++;
            if (l.LateSeasons >= c.S.LoanDefaultLateSeasons) Default(c, l, day);
        }
        return (ti, tp);
    }

    /// <summary>A loan in default (4.6): the lender keeps the loss, a mortgaged deed passes to him, the borrower's credit closes and they think the worse of each other.</summary>
    public static void Default(RuleCtx c, Loan l, int day)
    {
        l.State = LoanState.Default;
        c.B.CreditClosedUntil[((byte)l.Borrower.Kind, l.Borrower.Id)] = day + c.S.LoanCreditClosedYears * TimeConst.DaysPerYear;
        c.B.AddOpinion(c.KeyOf(l.Lender).Value, c.KeyOf(l.Borrower).Value, c.S.LoanDefaultOpinion);
        c.B.AddOpinion(c.KeyOf(l.Borrower).Value, c.KeyOf(l.Lender).Value, c.S.LoanDefaultOpinion);
        bool took = false;
        if (l.Security == LoanSecurity.Deed && l.DeedId != 0 && c.Land != null)
        {
            var (to, kind) = l.Lender.Kind switch { PartyKind.Household => (new EntityId((ulong)l.Lender.Id), PurseKind.Household), PartyKind.Fief => (c.FiefKey((int)l.Lender.Id), PurseKind.Treasury), PartyKind.Realm => (c.RealmKey((int)l.Lender.Id), PurseKind.Treasury), _ => (new EntityId((ulong)l.Lender.Id), PurseKind.Person) };
            took = c.Land.Transfer(new EntityId(l.DeedId), to, kind, day, DeedCause.Forfeited);
        }
        var entry = c.Chron.Chron(RuleCtx.At(day), "loan_default", "ch_rule_loan_default", [ChronParam.Mites("amount", l.Owing), ChronParam.Count("seasons", l.LateSeasons)], [c.KeyOf(l.Lender), c.KeyOf(l.Borrower)],
            [new Cause(CauseKind.Law, l.Entry == 0 ? null : new EntityId(l.Entry), NoteKey: took ? "ch_note_loan_deed_forfeit" : "ch_note_loan_unpaid")]);
        var head = HeadOf(c, l.Borrower);
        if (head != EntityId.None) { int row = c.People.IndexOf(head); c.Deed?.Invoke(head, "loan_default", entry, row >= 0 ? c.People.HomeSettlement[row] : -1, day); }       // a defaulter's name travels
    }

    /// <summary>
    /// The lender a borrower finds (4.6, the NPC rule and the hero's "ask a lender"): the households of the borrower's realm with at least twice the principal whose faith allows interest, the richest first, ties broken by the
    /// draw 0x663 (key the borrower and the season); none when the borrower's credit is closed or nobody has the coin.
    /// </summary>
    public static Party? FindLender(RuleCtx c, Party borrower, long principal, int day)
    {
        if (CreditClosed(c.B, borrower, day)) return null;
        var hht = c.Households; var e = c.W.Economy; var cands = new List<(long Bal, int H)>();
        for (int h = 0; h < hht.Count; h++)
        {
            if (hht.MemberCount[h] == 0 || hht.Class[h] == SocialClass.Peasant) continue;
            var lender = Party.OfHousehold(hht.IdOf(h)); if (lender == borrower) continue;
            long bal = c.Led.Balance(e.HouseholdPurse[h]).Mites;
            if (bal >= principal * c.S.LoanLenderMinMultiple && MayLend(c, lender)) cands.Add((bal, h));
        }
        if (cands.Count == 0) return null;
        long best = cands.Max(x => x.Bal); var top = cands.Where(x => x.Bal == best).OrderBy(x => x.H).ToList();
        int pick = top.Count == 1 ? 0 : DetRng.Range(c.Seed, day / TimeConst.DaysPerSeason, borrower.Kind == PartyKind.Fief || borrower.Kind == PartyKind.Realm ? (ulong)(borrower.Id + 1) : (ulong)borrower.Id, RngStreams.LenderTies, 0, 0, top.Count);
        return Party.OfHousehold(hht.IdOf(top[pick].H));
    }

    /// <summary>The coin a party owes in open loans (the Ledger's loans line).</summary>
    public static long Owed(RuleBook b, Party borrower) { long t = 0; foreach (var l in b.Loans) if (l.State == LoanState.Open && l.Borrower == borrower) t += l.Owing; return t; }
}
