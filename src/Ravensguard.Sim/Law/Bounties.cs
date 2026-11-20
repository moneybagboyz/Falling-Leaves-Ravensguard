using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Jobs;

namespace Ravensguard.Sim.Law;

/// <summary>
/// Bounties (crime-and-justice 4.3): the lord of the place posts one on a report, its coin escrowed from the fief treasury into a job of kind <see cref="OddJobKind.Bounty"/> (Phase 3 jobs until contracts
/// v2 take them over). Alive pays all, dead pays half (all for an outlaw or a murderer, <see cref="CrimeDef.DeadPaysAll"/>); the rest goes back to the treasury. An expired job refunds like any other.
/// </summary>
public static class Bounties
{
    /// <summary>Job parameter P2 bits.</summary>
    public const long FlagDeadPaysAll = 1, FlagPermanent = 2;

    /// <summary>The bounty by kind (4.3), before the cap: <c>flat + valueX x value + damage / damageDiv</c>.</summary>
    public static long Amount(CrimeDef d, long value, long damage) => d.BountyFlat + d.BountyValueX * value + (d.BountyDamageDiv > 0 ? damage / d.BountyDamageDiv : 0);

    /// <summary>Posts the bounty of a reported crime, escrowed from the treasury of the place's fief (capped at a quarter of it). Returns the job, or null when the crime carries none, the lord has no treasury or it cannot pay.</summary>
    public static Job? Post(LawWorld w, CrimeRecord rec, long nowSec)
    {
        var d = w.Law.Get(rec.Kind);
        if (!d.Bounty || d.Civil || w.Jobs == null || w.JobEnv == null || w.Ledger == null || w.TreasuryOf?.Invoke(rec.Settlement) is not { } treasury) return null;
        if (!rec.Known && rec.Look.Colours.Length == 0) return null;          // nobody to name and nothing to describe: the lord only notes it
        long amount = Amount(d, rec.ValueMites, rec.Damage);
        long cap = w.Ledger.Balance(treasury).Mites * w.S.BountyCapPct / 100;
        amount = Math.Min(amount, cap);
        if (amount <= 0) return null;
        int f = w.FiefOf(rec.Settlement);
        var poster = f >= 0 && f < w.FiefIds.Length ? w.FiefIds[f] : w.Ledger.OwnerOf(treasury);
        long deadline = d.Permanent ? long.MaxValue / 4 : nowSec + w.S.BountyExpiryDays * 86_400L;
        return w.Jobs.Post(w.JobEnv, OddJobKind.Bounty, poster, treasury, w.RegionId, rec.Settlement, nowSec, deadline, amount, j =>
        {
            j.P0 = rec.Id; j.P1 = rec.Known ? rec.Perpetrator : -1; j.P2 = (d.DeadPaysAll ? FlagDeadPaysAll : 0) | (d.Permanent ? FlagPermanent : 0);
            j.Title = "bounty:" + rec.Kind;
        });
    }

    /// <summary>The bounty jobs open on a person (the Journal's Wanted list), in job order.</summary>
    public static IEnumerable<Job> OnPerson(JobTable jobs, EntityId person) =>
        jobs.Jobs.Values.Where(j => j.Kind == OddJobKind.Bounty && j.State == JobState.Open && j.P1 == (long)person.Value).OrderBy(j => j.Id);

    /// <summary>
    /// Pays the bounty to whoever brings the culprit in: all when alive, half when dead (all when the posting says so); the remainder returns to the treasury. Returns the mites paid, 0 when the job is not an
    /// open bounty or nobody can be paid.
    /// </summary>
    public static long Collect(LawWorld w, CrimeBook book, EntityId job, EntityId collector, PurseId payee, bool alive, long nowSec)
    {
        if (w.Jobs?.Get(job) is not { Kind: OddJobKind.Bounty, State: JobState.Open } j || w.JobEnv == null) return 0;
        bool all = alive || (j.P2 & FlagDeadPaysAll) != 0;
        long pay = all ? j.WageMites : j.WageMites / 2;
        long paid = w.Jobs.Settle(w.JobEnv, job, payee, pay);
        if (paid > 0)
        {
            var rec = book.Get((int)j.P0);
            var cause = rec != null && rec.BountyEntry != 0 ? new Cause(CauseKind.Crime, new EntityId(rec.BountyEntry)) : new Cause(CauseKind.Crime);
            w.Chronicle.Add(new ChronicleEntry(w.Ids.Allocate(IdKind.Event), new GameTime(nowSec), "bounty_collected", [collector], [cause], new Dictionary<string, string> { ["crime"] = j.P0.ToString(System.Globalization.CultureInfo.InvariantCulture), ["mites"] = paid.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                TextKey: "ch_crime_bounty_paid", Params: [ChronParam.Person("collector", collector), ChronParam.Mites("amount", paid)]));
        }
        return paid;
    }
}
