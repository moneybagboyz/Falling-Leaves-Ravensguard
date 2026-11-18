using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Onboarding;

/// <summary><c>TakenBack</c>: the lender had it taken back after the grace days (the mentor is angry).</summary>
public enum LoanState : byte { Active, Returned, Overdue, Lost, TakenBack }

/// <summary>One lent animal (onboarding spec 3.6): the animal keeps its owner, the loan is only the permission to ride it for <c>LoanDays</c> game days.</summary>
public sealed class Loan
{
    public int Id;
    public EntityId Lender;
    public EntityId Animal;
    public long FromSec, DueSec;
    /// <summary>Settlement index the animal goes back to (its stable).</summary>
    public int ReturnPlace;
    public LoanState State;
    public long PriceMites;
    /// <summary>Set once the mentor's anger (overdue past the grace days) has been applied, so it happens once.</summary>
    public bool Angry;
    public long ClosedSec;
}

/// <summary>What happened to a loan: the table reports it, the host turns it into trust, a rumour and a Journal line.</summary>
public readonly record struct LoanEvent(string Kind, Loan Loan, int TrustPm);

/// <summary>A sum the hero owes (a lost lent animal). Phase 6 collects; Phase 3 shows it in the Journal and the mentor refuses further help until it is paid.</summary>
public sealed class Debt
{
    public EntityId Creditor;
    public long Mites;
    public string Reason = "";
    public long SinceSec;
    public bool Paid;
}

/// <summary>
/// The loans of the first hour and their debts. The animal itself is a <see cref="RideBook"/> row (and a <see cref="HorseLoan"/> there, which takes the animal back after the grace days if nobody returned it);
/// this table adds the rules around it: returning it to its place ends the loan with trust earned, keeping it past the due day then the grace makes the mentor angry once, and losing it (dead, or gone while on loan)
/// puts its price on the hero as a debt.
/// </summary>
public sealed class LoanTable
{
    public List<Loan> Rows { get; } = [];
    public List<Debt> Debts { get; } = [];
    public int NextId { get; set; } = 1;
    public bool IsEmpty => Rows.Count == 0 && Debts.Count == 0;
    public Loan? Active => Rows.FirstOrDefault(l => l.State is LoanState.Active or LoanState.Overdue);
    public bool Owes => Debts.Any(d => !d.Paid);

    /// <summary>Lends the breed at a place: the animal is created in the ride book, owned by the lender.</summary>
    public Loan Lend(RideBook rides, Breed breed, EntityId lender, long nowSec, long xMm, long yMm, int returnPlace, int days, int graceDays)
    {
        // the ride book takes the animal back at its own due time: after the grace days, so an overdue loan is first angry, then taken
        var a = rides.Loan(breed, lender, nowSec, xMm, yMm, days + graceDays);
        var l = new Loan { Id = NextId++, Lender = lender, Animal = a.Id, FromSec = nowSec, DueSec = nowSec + days * 86_400L, ReturnPlace = returnPlace, PriceMites = breed.Def.PriceMites, State = LoanState.Active };
        Rows.Add(l); return l;
    }

    /// <summary>The hero hands the animal back at <paramref name="settlement"/>. Returns the event, or null when nothing is on loan or it is the wrong place.</summary>
    public LoanEvent? Return(RideBook rides, int settlement, long nowSec, int returnTrustPm)
    {
        var l = Active; if (l == null || settlement != l.ReturnPlace) return null;
        var a = rides.Animals.Find(l.Animal);
        if (a != null) { if (rides.Mounted == a.Id) rides.Dismount(); a.State = AnimalState.Stabled; rides.Animals.Remove(a); }
        rides.Loans.RemoveAll(h => h.Animal == l.Animal);
        bool wasAngry = l.Angry;
        l.State = LoanState.Returned; l.ClosedSec = nowSec;
        return new LoanEvent("returned", l, wasAngry ? 0 : returnTrustPm);
    }

    /// <summary>The hourly check: past the due day the loan is overdue; past the grace days the mentor is angry once (trust change, a rumour); an animal that is dead or gone from the book while on loan is lost, and its price becomes a debt.</summary>
    public List<LoanEvent> Tick(RideBook rides, long nowSec, int graceDays, int overdueTrustPm)
    {
        var ev = new List<LoanEvent>();
        foreach (var l in Rows)
        {
            if (l.State is not (LoanState.Active or LoanState.Overdue)) continue;
            var a = rides.Animals.Find(l.Animal);
            if (a == null || !a.Alive)
            {
                // the ride book took it back after the grace days (a late return in effect) or it died: only a dead or vanished animal before the grace ends is a loss
                if (nowSec >= l.DueSec + graceDays * 86_400L) { if (!l.Angry) { l.Angry = true; ev.Add(new LoanEvent("angry", l, overdueTrustPm)); } l.State = LoanState.TakenBack; l.ClosedSec = nowSec; rides.Loans.RemoveAll(h => h.Animal == l.Animal); }
                else { l.State = LoanState.Lost; l.ClosedSec = nowSec; Debts.Add(new Debt { Creditor = l.Lender, Mites = l.PriceMites, Reason = "lost_loan", SinceSec = nowSec }); rides.Loans.RemoveAll(h => h.Animal == l.Animal); ev.Add(new LoanEvent("lost", l, 0)); }
                continue;
            }
            if (nowSec > l.DueSec && l.State == LoanState.Active) { l.State = LoanState.Overdue; ev.Add(new LoanEvent("overdue", l, 0)); }
            if (l.State == LoanState.Overdue && !l.Angry && nowSec >= l.DueSec + graceDays * 86_400L) { l.Angry = true; ev.Add(new LoanEvent("angry", l, overdueTrustPm)); }
        }
        return ev;
    }

    /// <summary>Pays a debt out of coin the caller already took from the purse; true when it was a debt of this table.</summary>
    public bool Settle(int debtIndex) { if (debtIndex < 0 || debtIndex >= Debts.Count || Debts[debtIndex].Paid) return false; Debts[debtIndex].Paid = true; return true; }
}
