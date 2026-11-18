using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Onboarding;

/// <summary>The eight steps of the first hour (onboarding spec 3.5). The order is the order the Journal lists them; after step 1 any can be done in any order.</summary>
public enum OnboardingStep : byte { MeetMentor, TakeJob, FinishJob, SpendCoin, BorrowHorse, FirstRide, Deliver, FirstNight }

/// <summary>
/// The state of the first hour (saved in the "onboarding" section once a new game has set it up): who the mentor is and where the start is, which job and errand the mentor posted, when each step was done, how far the
/// hero has ridden toward the errand and how much of it mounted, and the loan. The script reacts to events (<c>On...</c>) and does nothing per tick; a step done once stays done.
/// </summary>
public sealed class OnboardingBook
{
    public const int StepCount = 8;
    public static readonly string[] StepIds = ["meet_mentor", "take_job", "finish_job", "spend_coin", "borrow_horse", "first_ride", "deliver", "first_night"];

    public string Origin = "", MentorId = "";
    /// <summary>The mentor's index in the population, and their settlement.</summary>
    public int MentorPerson = -1, MentorSettlement = -1;
    public long StartSec;
    /// <summary>Where the hero started and where the mentor stands, region millimetres.</summary>
    public long StartX, StartY, MentorX, MentorY;
    public EntityId FirstJob = EntityId.None, ErrandJob = EntityId.None;
    public bool HasFirstJob, HasErrandJob;
    public int ErrandSettlement = -1;
    public long ErrandX, ErrandY;
    /// <summary>Game second each step was done, -1 while it is not.</summary>
    public long[] DoneSec { get; } = Enumerable.Repeat(-1L, StepCount).ToArray();
    public long RideMm, MountedMm;
    public bool Complete;
    /// <summary>The mentor has offered the job / the loan in dialogue (the effects ran), so the dialogue does not offer twice.</summary>
    public bool JobOffered, LoanOffered;
    /// <summary>Real minutes of stuck time shown as expanded Journal text (not saved: a UI clock), and the safe-day end: no ambush or war event begins before this game second.</summary>
    public long SafeUntilSec;
    public LoanTable Loans { get; } = new();

    public bool IsEmpty => Origin.Length == 0;
    public bool Done(OnboardingStep s) => DoneSec[(int)s] >= 0;
    public int DoneCount => DoneSec.Count(t => t >= 0);

    /// <summary>The first step not done yet in list order, or null when all are.</summary>
    public OnboardingStep? Next { get { for (int i = 0; i < StepCount; i++) if (DoneSec[i] < 0) return (OnboardingStep)i; return null; } }

    /// <summary>Marks a step done (once). Returns true when it was new, so the caller can write the Journal line and raise the hint.</summary>
    public bool Mark(OnboardingStep s, long nowSec)
    {
        if (IsEmpty || Done(s)) return false;
        DoneSec[(int)s] = nowSec;
        if (DoneCount == StepCount) Complete = true;
        return true;
    }

    // ------------------------------------------------------------------ events (the script)

    /// <summary>Dialogue with the mentor was finished (the start node walked).</summary>
    public bool OnMentorTalked(long nowSec) => Mark(OnboardingStep.MeetMentor, nowSec);

    /// <summary>The hero took a job: the mentor's, or any other board job.</summary>
    public bool OnJobTaken(EntityId job, long nowSec) => Mark(OnboardingStep.TakeJob, nowSec);

    /// <summary>A job of the hero's was paid. The mentor's first job ends step 3 (its wage counted), the errand step 7.</summary>
    public bool OnJobDone(EntityId job, long paidMites, long nowSec, long minWageMites)
    {
        bool n = false;
        if (HasFirstJob && job == FirstJob && paidMites >= minWageMites) n |= Mark(OnboardingStep.FinishJob, nowSec);
        else if (!HasFirstJob && paidMites > 0) n |= Mark(OnboardingStep.FinishJob, nowSec);
        if (HasErrandJob && job == ErrandJob) n |= Mark(OnboardingStep.Deliver, nowSec);
        return n;
    }

    /// <summary>A buy or a sell went through the trade screen.</summary>
    public bool OnTraded(long nowSec) => Mark(OnboardingStep.SpendCoin, nowSec);

    public bool OnLoan(long nowSec) => Mark(OnboardingStep.BorrowHorse, nowSec);

    /// <summary>The hero moved <paramref name="movedMm"/> since the last call, <paramref name="mounted"/> or not; <paramref name="atErrand"/> when they stand at the errand place. Step 6 is done on arrival after a path of at least <paramref name="minRideM"/> metres with at least <paramref name="mountedSharePm"/> permille of it mounted; with no animal lent the share is not asked.</summary>
    public bool OnRide(long movedMm, bool mounted, bool atErrand, long nowSec, int minRideM, int mountedSharePm)
    {
        if (movedMm > 0 && !Done(OnboardingStep.FirstRide) && (HasErrandJob || Done(OnboardingStep.BorrowHorse))) { RideMm += movedMm; if (mounted) MountedMm += movedMm; }
        if (!atErrand || Done(OnboardingStep.FirstRide) || RideMm < minRideM * 1000L) return false;
        bool lent = Done(OnboardingStep.BorrowHorse);
        if (lent && MountedMm * 1000 < RideMm * mountedSharePm) return false;
        return Mark(OnboardingStep.FirstRide, nowSec);
    }

    /// <summary>The hero slept (bed, inn, camp) for <paramref name="hours"/> game hours in one go.</summary>
    public bool OnSlept(int hours, long nowSec, int minHours) => hours >= minHours && Mark(OnboardingStep.FirstNight, nowSec);
}
