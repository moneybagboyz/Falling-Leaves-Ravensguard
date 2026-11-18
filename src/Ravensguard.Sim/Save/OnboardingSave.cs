using MessagePack;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Onboarding;

namespace Ravensguard.Sim.Save;

[MessagePackObject]
public sealed class LoanRowSave
{
    [Key(0)] public int Id { get; set; } [Key(1)] public ulong Lender { get; set; } [Key(2)] public ulong Animal { get; set; } [Key(3)] public long FromSec { get; set; } [Key(4)] public long DueSec { get; set; }
    [Key(5)] public int ReturnPlace { get; set; } [Key(6)] public byte State { get; set; } [Key(7)] public long PriceMites { get; set; } [Key(8)] public bool Angry { get; set; } [Key(9)] public long ClosedSec { get; set; }
}

[MessagePackObject]
public sealed class DebtRowSave { [Key(0)] public ulong Creditor { get; set; } [Key(1)] public long Mites { get; set; } [Key(2)] public string Reason { get; set; } = ""; [Key(3)] public long SinceSec { get; set; } [Key(4)] public bool Paid { get; set; } }

/// <summary>The "onboarding" section (onboarding spec 4.4): the first hour's step states, the mentor and the jobs, the ride tallies and the loan rows. Absent in a world with no first hour set up. Under 5 KB.</summary>
[MessagePackObject]
public sealed class OnboardingSave
{
    public const string SectionName = "onboarding";
    [Key(0)] public string Origin { get; set; } = ""; [Key(1)] public string MentorId { get; set; } = ""; [Key(2)] public int MentorPerson { get; set; } [Key(3)] public int MentorSettlement { get; set; }
    [Key(4)] public long StartSec { get; set; } [Key(5)] public long StartX { get; set; } [Key(6)] public long StartY { get; set; } [Key(7)] public long MentorX { get; set; } [Key(8)] public long MentorY { get; set; }
    [Key(9)] public ulong FirstJob { get; set; } [Key(10)] public ulong ErrandJob { get; set; } [Key(11)] public bool HasFirstJob { get; set; } [Key(12)] public bool HasErrandJob { get; set; }
    [Key(13)] public int ErrandSettlement { get; set; } [Key(14)] public long ErrandX { get; set; } [Key(15)] public long ErrandY { get; set; }
    [Key(16)] public long[] DoneSec { get; set; } = []; [Key(17)] public long RideMm { get; set; } [Key(18)] public long MountedMm { get; set; }
    [Key(19)] public bool Complete { get; set; } [Key(20)] public bool JobOffered { get; set; } [Key(21)] public bool LoanOffered { get; set; } [Key(22)] public long SafeUntilSec { get; set; }
    [Key(23)] public LoanRowSave[] Loans { get; set; } = []; [Key(24)] public DebtRowSave[] Debts { get; set; } = []; [Key(25)] public int NextLoanId { get; set; } = 1;

    public static OnboardingSave Capture(OnboardingBook b) => new()
    {
        Origin = b.Origin, MentorId = b.MentorId, MentorPerson = b.MentorPerson, MentorSettlement = b.MentorSettlement, StartSec = b.StartSec, StartX = b.StartX, StartY = b.StartY, MentorX = b.MentorX, MentorY = b.MentorY,
        FirstJob = b.FirstJob.Value, ErrandJob = b.ErrandJob.Value, HasFirstJob = b.HasFirstJob, HasErrandJob = b.HasErrandJob, ErrandSettlement = b.ErrandSettlement, ErrandX = b.ErrandX, ErrandY = b.ErrandY,
        DoneSec = [.. b.DoneSec], RideMm = b.RideMm, MountedMm = b.MountedMm, Complete = b.Complete, JobOffered = b.JobOffered, LoanOffered = b.LoanOffered, SafeUntilSec = b.SafeUntilSec, NextLoanId = b.Loans.NextId,
        Loans = [.. b.Loans.Rows.Select(l => new LoanRowSave { Id = l.Id, Lender = l.Lender.Value, Animal = l.Animal.Value, FromSec = l.FromSec, DueSec = l.DueSec, ReturnPlace = l.ReturnPlace, State = (byte)l.State, PriceMites = l.PriceMites, Angry = l.Angry, ClosedSec = l.ClosedSec })],
        Debts = [.. b.Loans.Debts.Select(d => new DebtRowSave { Creditor = d.Creditor.Value, Mites = d.Mites, Reason = d.Reason, SinceSec = d.SinceSec, Paid = d.Paid })],
    };

    public OnboardingBook Restore()
    {
        var b = new OnboardingBook
        {
            Origin = Origin, MentorId = MentorId, MentorPerson = MentorPerson, MentorSettlement = MentorSettlement, StartSec = StartSec, StartX = StartX, StartY = StartY, MentorX = MentorX, MentorY = MentorY,
            FirstJob = new EntityId(FirstJob), ErrandJob = new EntityId(ErrandJob), HasFirstJob = HasFirstJob, HasErrandJob = HasErrandJob, ErrandSettlement = ErrandSettlement, ErrandX = ErrandX, ErrandY = ErrandY,
            RideMm = RideMm, MountedMm = MountedMm, Complete = Complete, JobOffered = JobOffered, LoanOffered = LoanOffered, SafeUntilSec = SafeUntilSec,
        };
        for (int i = 0; i < b.DoneSec.Length && i < DoneSec.Length; i++) b.DoneSec[i] = DoneSec[i];
        b.Loans.NextId = NextLoanId;
        foreach (var l in Loans) b.Loans.Rows.Add(new Loan { Id = l.Id, Lender = new EntityId(l.Lender), Animal = new EntityId(l.Animal), FromSec = l.FromSec, DueSec = l.DueSec, ReturnPlace = l.ReturnPlace, State = (LoanState)l.State, PriceMites = l.PriceMites, Angry = l.Angry, ClosedSec = l.ClosedSec });
        foreach (var d in Debts) b.Loans.Debts.Add(new Debt { Creditor = new EntityId(d.Creditor), Mites = d.Mites, Reason = d.Reason, SinceSec = d.SinceSec, Paid = d.Paid });
        return b;
    }
}
