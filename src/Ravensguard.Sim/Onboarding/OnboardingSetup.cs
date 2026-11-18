using Ravensguard.Content;
using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Behaviour;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Jobs;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Onboarding;

/// <summary>What setting up a new game's first hour came to: the book (null when no safe start exists) and the site, for the embark message.</summary>
public sealed record SetupResult(OnboardingBook? Book, StartSite Site, string Message);

/// <summary>
/// The first hour's setting up and its effects on the world (onboarding spec 4.3, 4.4). <see cref="Create"/> runs once at new-game creation, before the first frame: it finds the start, picks the mentor, posts the mentor's first
/// small contract as an ordinary odd job (the poster's coin is escrowed in the ledger, so the pay is real) and opens the safe days. <see cref="Apply"/> is what the mentor's dialogue effects do at a barrier (the offer of the
/// job, the loan and the errand). Everything goes through the same tables every other job and animal does: no second system.
/// </summary>
public static class OnboardingSetup
{
    public const long DaySec = 86_400;

    static JobEnv EnvOf(WorldState st, ContentDatabase db) => new() { Ledger = st.Economy!.Ledger, Econ = st.Economy, Pop = st.Population, Tuning = BehaviourContent.From(db).OddJobs };

    /// <summary>A good in the household's store to carry, the most of it up to <paramref name="wantG"/> grams (at least two kilograms), or null.</summary>
    internal static (ushort Good, long Grams)? Carryable(WorldState st, int household, long wantG)
    {
        var e = st.Economy!; if (household < 0 || household >= e.HouseholdCount) return null;
        (ushort, long)? best = null;
        foreach (var (g, q) in e.HouseholdStore[household].Rows()) if (q >= 2000 && (best == null || Math.Min(q, wantG) > best.Value.Item2)) best = (g, Math.Min(q, wantG));
        return best;
    }

    /// <summary>The mentor's household is the poster when it can pay (and has something to carry when the job is a carry); else the first household of the settlement that can, by index.</summary>
    static int PosterHousehold(WorldState st, int mentorPerson, int settlement, long wage, long carryG = 0)
    {
        var e = st.Economy!; var pop = st.Population;
        bool Ok(int h) => h >= 0 && h < e.HouseholdCount && e.Ledger.Balance(e.HouseholdPurse[h]).Mites >= wage + 20 && (carryG == 0 || Carryable(st, h, carryG) != null);
        int m = mentorPerson >= 0 ? pop.People.Household[mentorPerson] : -1;
        if (Ok(m)) return m;
        for (int k = 0; k < e.HouseholdCount && k < pop.Households.Count; k++)
            if (pop.Households.Settlement[k] == settlement && Ok(k)) return k;
        return -1;
    }

    static long Mm(int metres) => metres * 1000L;

    public static SetupResult Create(WorldState st, ContentDatabase db, OriginDef origin, long nowSec, ulong seed)
    {
        var content = OnboardingContent.From(db); var cfg = content.Settings;
        var site = StartSiteFinder.Find(st.Population, origin.StartLocation, origin.Mentor, seed, cfg.WarSafeM, cfg.LairSafeM);
        if (!site.Found) return new SetupResult(null, site, site.Note);
        var fh = content.Of(origin.Id);
        var book = new OnboardingBook
        {
            Origin = origin.Id, MentorId = origin.Mentor, MentorPerson = site.MentorPerson, MentorSettlement = site.Settlement, StartSec = nowSec,
            StartX = Mm(site.X), StartY = Mm(site.Y), MentorX = Mm(site.MentorX), MentorY = Mm(site.MentorY), ErrandSettlement = site.ErrandSettlement,
            SafeUntilSec = nowSec + cfg.SafeDays * DaySec,
        };
        if (site.ErrandSettlement >= 0) { var es = st.Population.Region.Settlements[site.ErrandSettlement]; book.ErrandX = Mm(es.CenterX); book.ErrandY = Mm(es.CenterY); }
        if (fh != null && st.Economy != null && st.Population.Region.Settlements.Count > 0) PostFirstJob(st, db, book, fh, cfg, nowSec, seed);
        return new SetupResult(book, site, site.Note);
    }

    static void PostFirstJob(WorldState st, ContentDatabase db, OnboardingBook book, FirstHourDef fh, OnboardingSettingsDef cfg, long nowSec, ulong seed)
    {
        var env = EnvOf(st, db); var kind = Enum.Parse<OddJobKind>(fh.JobKind, true);
        long wage = kind switch
        {
            OddJobKind.Guard => JobPay.Guard(env.Tuning, env.UnskilledWage, fh.JobHours),
            OddJobKind.Haul or OddJobKind.Deliver or OddJobKind.Fetch => JobPay.Haul(env.Tuning, env.UnskilledWage, Mm(fh.JobDistanceM), fh.JobKg * 1000L),
            _ => JobPay.Labour(env.Tuning, env.UnskilledWage, fh.JobHours),
        };
        wage = Math.Max(wage, cfg.MinWageMites);      // the first contract is never so small that the purse shows nothing: the poster's choice, still paid from the poster's own coin
        bool carry = kind is OddJobKind.Haul or OddJobKind.Deliver or OddJobKind.Fetch;
        int h = PosterHousehold(st, book.MentorPerson, book.MentorSettlement, wage, carry ? fh.JobKg * 1000L : 0);
        if (h < 0) return;
        var table = st.Jobs ??= new JobTable();
        // the destination is JobDistanceM from the mentor, in a direction from stream 0x371
        // a tenth of a degree of 3,600, as an integer direction (review finding 7: Math.Cos and Math.Sin decided it); truncated toward zero as the double version was
        var (cq, sq) = IntMath.CosSinQ30(DetRng.Range(seed, 7, (ulong)book.MentorSettlement, RngStreams.OnboardingJob, 0, 0, 3600), 3600);
        long dx = (long)((Int128)cq * Mm(fh.JobDistanceM) / IntMath.One30), dy = (long)((Int128)sq * Mm(fh.JobDistanceM) / IntMath.One30);
        long ex = Math.Clamp(book.MentorX + dx, 0, Mm(RegionSettings.RegionM - 1)), ey = Math.Clamp(book.MentorY + dy, 0, Mm(RegionSettings.RegionM - 1));
        var poster = st.Population.Households.IdOf(h);
        var job = table.Post(env, kind, poster, st.Economy!.HouseholdPurse[h], st.HomeRegion.Id, book.MentorSettlement, nowSec, nowSec + cfg.JobDeadlineHours * 3600L, wage, j =>
        {
            j.Slots = 1; j.Title = fh.JobTitle; j.MinAge = 12;
            if (kind is OddJobKind.Haul or OddJobKind.Deliver or OddJobKind.Fetch)
            {
                j.P0 = book.MentorX; j.P1 = book.MentorY; j.P2 = ex; j.P3 = ey; j.P5 = fh.JobKg * 1000L;
                // a real load out of the poster's own store, back into it at the far end: the stock is conserved, the carrying is paid
                var (gid, grams) = Carryable(st, h, fh.JobKg * 1000L)!.Value;
                j.P4 = gid; j.P5 = grams; j.SrcOwner = poster; j.DstOwner = poster;
            }
            else { j.P0 = book.MentorX; j.P1 = book.MentorY; j.P4 = fh.JobHours; }
        });
        if (job != null) { book.FirstJob = job.Id; book.HasFirstJob = true; }
    }

    // ------------------------------------------------------------------ dialogue effects

    /// <summary>Applies one of the mentor's dialogue effects. Returns true when it changed something (and the script's Journal line should be written).</summary>
    public static bool Apply(WorldState st, ContentDatabase db, OnboardingBook book, string effect, IReadOnlyDictionary<string, string> args, long nowSec, long heroXmm, long heroYmm)
    {
        string A(string k) => args.TryGetValue(k, out var v) ? v : "";
        switch (effect)
        {
            case "set_flag" when A("flag") == "mentor_met": return book.OnMentorTalked(nowSec);
            case "start_contract" when A("kind") == "first": { bool n = !book.JobOffered; book.JobOffered = true; return n; }
            case "start_contract" when A("kind") == "loan": return OfferLoan(st, db, book, nowSec, heroXmm, heroYmm);
            default: return false;
        }
    }

    /// <summary>The mentor lends the first-ride animal and posts the errand (a Deliver job at the errand place, paid for the real distance). Refuses while a debt is owed or a loan is out.</summary>
    public static bool OfferLoan(WorldState st, ContentDatabase db, OnboardingBook book, long nowSec, long heroX, long heroY)
    {
        var content = OnboardingContent.From(db); var cfg = content.Settings; var fh = content.Of(book.Origin);
        if (fh == null || book.LoanOffered || book.Loans.Owes || book.Loans.Active != null || book.ErrandSettlement < 0 || st.Economy == null) return false;
        var rides = st.Mounts ??= new RideBook(db, st.Economy, st.Seed);
        var breed = rides.Breeds.Find(fh.LoanBreed) ?? rides.Breeds.Find("horse_palfrey");
        if (breed == null || !breed.CanRide) return false;
        var lender = book.MentorPerson >= 0 ? st.Population.People.IdOf(book.MentorPerson) : EntityId.None;
        var loan = book.Loans.Lend(rides, breed, lender, nowSec, heroX, heroY, fh.ReturnAt == "errand" ? book.ErrandSettlement : book.MentorSettlement, cfg.LoanDays, cfg.OverdueGraceDays);
        book.LoanOffered = true; book.OnLoan(nowSec);
        // the errand: pay for the real distance, held in the mentor household's escrow now, claimed by the hero on arrival (the hand-over is the whole job)
        var env = EnvOf(st, db); var table = st.Jobs ??= new JobTable();
        long dist = Ravensguard.Sim.Production.ProductionSystem.ISqrt((book.ErrandX - book.StartX) * (book.ErrandX - book.StartX) + (book.ErrandY - book.StartY) * (book.ErrandY - book.StartY));
        long wage = Math.Max(cfg.MinWageMites, JobPay.Haul(env.Tuning, env.UnskilledWage, dist, 12_000));
        int h = PosterHousehold(st, book.MentorPerson, book.MentorSettlement, wage, 2000);
        if (h >= 0)
        {
            var poster = st.Population.Households.IdOf(h); var (egood, egrams) = Carryable(st, h, 12_000)!.Value;
            var job = table.Post(env, OddJobKind.Deliver, poster, st.Economy.HouseholdPurse[h], st.HomeRegion.Id, book.ErrandSettlement, nowSec, nowSec + cfg.LoanDays * DaySec, wage, j =>
            {
                j.Slots = 1; j.Title = fh.ErrandTitle; j.SrcOwner = poster; j.DstOwner = poster; j.P4 = egood; j.P0 = j.P2 = book.ErrandX; j.P1 = j.P3 = book.ErrandY; j.P5 = egrams; j.MinAge = 12;
            });
            if (job != null) { book.ErrandJob = job.Id; book.HasErrandJob = true; }
        }
        return loan != null;
    }

    // ------------------------------------------------------------------ hourly

    /// <summary>Hour tick of the script: the loan's due and anger. Trust goes to the mentor in the hero's party; the chronicle notes an angry lender (rumours seed from it). Returns the loan events.</summary>
    public static List<LoanEvent> Hour(WorldState st, ContentDatabase db, OnboardingBook book, long nowSec)
    {
        var rides = st.Mounts; if (rides == null || book.Loans.Rows.Count == 0) return [];
        var cfg = OnboardingContent.From(db).Settings;
        var ev = book.Loans.Tick(rides, nowSec, cfg.OverdueGraceDays, cfg.OverdueTrustPm);
        foreach (var e in ev) AfterLoanEvent(st, book, e, nowSec);
        return ev;
    }

    public static LoanEvent? ReturnAnimal(WorldState st, ContentDatabase db, OnboardingBook book, int settlement, long nowSec)
    {
        var rides = st.Mounts; if (rides == null) return null;
        var e = book.Loans.Return(rides, settlement, nowSec, OnboardingContent.From(db).Settings.ReturnTrustPm);
        if (e != null) AfterLoanEvent(st, book, e.Value, nowSec);
        return e;
    }

    static void AfterLoanEvent(WorldState st, OnboardingBook book, LoanEvent e, long nowSec)
    {
        if (e.TrustPm != 0 && st.Party != null && e.Loan.Lender != EntityId.None) { var k = st.Party.Know(e.Loan.Lender); k.TrustPm = Math.Clamp(k.TrustPm + e.TrustPm, -1000, 1000); }
        if (e.Kind is "angry" or "lost" or "returned")
            st.Chronicle.Add(new ChronicleEntry(st.Ids.Allocate(IdKind.Event), new GameTime(nowSec), "loan_" + e.Kind, e.Loan.Lender == EntityId.None ? [] : [e.Loan.Lender], [],
                new Dictionary<string, string> { ["settlement"] = book.MentorSettlement >= 0 && book.MentorSettlement < st.Population.Region.Settlements.Count ? st.Population.Region.Settlements[book.MentorSettlement].Name : "", ["animal"] = e.Loan.Animal.Value.ToString() }));
    }
}
