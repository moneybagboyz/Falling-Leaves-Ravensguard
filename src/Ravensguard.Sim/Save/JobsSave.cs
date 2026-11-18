using MessagePack;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Jobs;

namespace Ravensguard.Sim.Save;

/// <summary>One job as saved: every field of the record, in id order.</summary>
[MessagePackObject]
public sealed class JobRow
{
    [Key(0)] public ulong Id { get; set; }
    [Key(1)] public byte Kind { get; set; }
    [Key(2)] public byte State { get; set; }
    [Key(3)] public ulong Poster { get; set; }
    [Key(4)] public int PosterPurse { get; set; }
    [Key(5)] public int Region { get; set; }
    [Key(6)] public int Settlement { get; set; }
    [Key(7)] public byte Slots { get; set; }
    [Key(8)] public long[] P { get; set; } = [];
    [Key(9)] public ulong Src { get; set; }
    [Key(10)] public ulong Dst { get; set; }
    [Key(11)] public long Wage { get; set; }
    [Key(12)] public int Escrow { get; set; }
    [Key(13)] public long Posted { get; set; }
    [Key(14)] public long Deadline { get; set; }
    [Key(15)] public byte MinSkill { get; set; }
    [Key(16)] public byte MinAge { get; set; }
    [Key(17)] public ulong Worker { get; set; }
    [Key(18)] public int WorkerIndex { get; set; }
    [Key(19)] public long Claimed { get; set; }
    [Key(20)] public long StartX { get; set; }
    [Key(21)] public long StartY { get; set; }
    [Key(22)] public int Applied { get; set; }
    [Key(23)] public long Paid { get; set; }
    [Key(24)] public long Refunded { get; set; }
    [Key(25)] public long WorkMilli { get; set; }
    [Key(26)] public string Title { get; set; } = "";
}

/// <summary>
/// The "jobs" section (live-behaviour spec 4.5): the odd jobs with their escrow purse numbers (the coin itself is in the economy's ledger), the reservations and the serial the next job takes. Absent in a world
/// that has posted none, so older saves keep their bytes. Everything a job does after loading is a function of these rows and the clock, so a save made mid-job loads and saves again to the same bytes.
/// </summary>
[MessagePackObject]
public sealed class JobsSave
{
    [Key(0)] public int Version { get; set; } = 1;
    [Key(1)] public ulong NextSerial { get; set; }
    [Key(2)] public List<JobRow> Jobs { get; set; } = [];
    /// <summary>Reservations: kind, id, holder, job, expires; five values each.</summary>
    [Key(3)] public ulong[] Holds { get; set; } = [];
    [Key(4)] public long[] HoldExpiry { get; set; } = [];
    [Key(5)] public long LastPostedDay { get; set; } = -1;
    /// <summary>The game second the hero was first seen away from his job's work site (-1: none); see <see cref="JobTable.WatchHero"/>.</summary>
    [Key(6)] public long HeroAwaySince { get; set; } = -1;

    public static JobsSave Capture(JobTable t)
    {
        var s = new JobsSave { NextSerial = t.NextSerial, LastPostedDay = t.LastPostedDay, HeroAwaySince = t.HeroAwaySince };
        foreach (var j in t.Jobs.Values)
            s.Jobs.Add(new JobRow
            {
                Id = j.Id.Value, Kind = (byte)j.Kind, State = (byte)j.State, Poster = j.Poster.Value, PosterPurse = j.PosterPurse, Region = j.Region, Settlement = j.Settlement, Slots = j.Slots,
                P = [j.P0, j.P1, j.P2, j.P3, j.P4, j.P5], Src = j.SrcOwner.Value, Dst = j.DstOwner.Value, Wage = j.WageMites, Escrow = j.Escrow, Posted = j.PostedSec, Deadline = j.DeadlineSec,
                MinSkill = j.MinSkill, MinAge = j.MinAge, Worker = j.Worker.Value, WorkerIndex = j.WorkerIndex, Claimed = j.ClaimedSec, StartX = j.StartX, StartY = j.StartY, Applied = j.Applied,
                Paid = j.PaidMites, Refunded = j.RefundedMites, WorkMilli = j.WorkMilliHours, Title = j.Title,
            });
        var holds = new List<ulong>(); var exp = new List<long>();
        foreach (var (k, h) in t.Reservations.Holds) { holds.Add((ulong)k.Kind); holds.Add(k.Id); holds.Add(h.Holder.Value); holds.Add(h.Job.Value); exp.Add(h.ExpiresSec); }
        s.Holds = [.. holds]; s.HoldExpiry = [.. exp];
        return s;
    }

    public JobTable Restore()
    {
        var t = new JobTable { NextSerial = NextSerial, LastPostedDay = LastPostedDay, HeroAwaySince = HeroAwaySince };
        foreach (var r in Jobs)
            t.Restore(new Job
            {
                Id = new EntityId(r.Id), Kind = (OddJobKind)r.Kind, State = (JobState)r.State, Poster = new EntityId(r.Poster), PosterPurse = r.PosterPurse, Region = r.Region, Settlement = r.Settlement, Slots = r.Slots,
                P0 = r.P[0], P1 = r.P[1], P2 = r.P[2], P3 = r.P[3], P4 = r.P[4], P5 = r.P[5], SrcOwner = new EntityId(r.Src), DstOwner = new EntityId(r.Dst), WageMites = r.Wage, Escrow = r.Escrow,
                PostedSec = r.Posted, DeadlineSec = r.Deadline, MinSkill = r.MinSkill, MinAge = r.MinAge, Worker = new EntityId(r.Worker), WorkerIndex = r.WorkerIndex, ClaimedSec = r.Claimed,
                StartX = r.StartX, StartY = r.StartY, Applied = r.Applied, PaidMites = r.Paid, RefundedMites = r.Refunded, WorkMilliHours = r.WorkMilli, Title = r.Title,
            });
        for (int i = 0; i < HoldExpiry.Length; i++)
            t.Reservations.Restore(new ReservationTable.Key((ReserveKind)Holds[i * 4], Holds[i * 4 + 1]), new ReservationTable.Hold(new EntityId(Holds[i * 4 + 2]), new EntityId(Holds[i * 4 + 3]), HoldExpiry[i]));
        return t;
    }
}
