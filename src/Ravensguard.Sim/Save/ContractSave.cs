using MessagePack;
using Ravensguard.Sim.Contracts;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Save;

/// <summary>
/// The optional "contracts" section (P6-C2, contracts-and-rumours-v2 3; no format bump, absent while no contract, tournament or slander exists, so a world that never used them keeps its bytes): every contract with
/// its escrow handles, the tournaments of the fairs with their brackets, the slanders told and the counters. Content ids (the kind) are strings; escrow chests are ledger purses (saved with the economy).
/// </summary>
[MessagePackObject]
public sealed class ContractSave
{
    public const string SectionName = "contracts";

    /// <summary>Per contract: id, issuer, issuer purse, escrow, pay, posted day, deadline day, settlement, target, target owner, good, grams, baseline, delivered, param, state, taker, flags (1 by company, 2 pinned), taken day, closed day, bond chest, bond mites, cause entry, posted entry, job id, then the evidence entries.</summary>
    [Key(0)] public long[][] Contracts { get; set; } = [];
    /// <summary>Per contract: the kind id.</summary>
    [Key(1)] public string[] Kinds { get; set; } = [];
    /// <summary>Per tournament: id, settlement, year, fair day, escrow, treasury purse, purse, fee, flags (1 paid, 2 cancelled, 4 drawn), winner, then the entrants.</summary>
    [Key(2)] public long[][] Tournaments { get; set; } = [];
    /// <summary>Per tournament: its bouts, five numbers each (round, slot, a, b, winner) then the angle.</summary>
    [Key(3)] public long[][] Bouts { get; set; } = [];
    /// <summary>Per slander: rumour, author, victim, day, exposed day (-1 not exposed).</summary>
    [Key(4)] public long[][] Slanders { get; set; } = [];
    [Key(5)] public int NextId { get; set; } = 1;
    [Key(6)] public int NextTournament { get; set; } = 1;
    [Key(7)] public int LastScanDay { get; set; } = -1;
    [Key(8)] public int LastSlanderSeason { get; set; } = -1;

    public static ContractSave Capture(ContractBook b) => new()
    {
        Contracts = [.. b.Contracts.Values.Select(c => new long[]
        {
            c.Id, (long)c.Issuer.Value, c.IssuerPurse, c.Escrow, c.PayMites, c.PostedDay, c.DeadlineDay, c.Settlement, (long)c.Target.Value, (long)c.TargetOwner.Value, c.Good, c.Grams, c.Baseline, c.Delivered, c.Param, (long)c.State,
            (long)c.Taker.Value, (c.ByCompany ? 1 : 0) | (c.Pinned ? 2 : 0), c.TakenDay, c.ClosedDay, c.BondChest, c.BondMites, (long)c.CauseEntry, (long)c.PostedEntry, (long)c.JobId,
        }.Concat(c.Evidence.Select(e => (long)e)).ToArray())],
        Kinds = [.. b.Contracts.Values.Select(c => c.Kind)],
        Tournaments = [.. b.Tournaments.Values.Select(t => new long[]
        {
            t.Id, t.Settlement, t.Year, t.FairDay, t.Escrow, t.TreasuryPurse, t.PurseMites, t.Fee, (t.Paid ? 1 : 0) | (t.Cancelled ? 2 : 0) | (t.Drawn ? 4 : 0), (long)t.Winner,
        }.Concat(t.Entrants.Select(e => (long)e)).ToArray())],
        Bouts = [.. b.Tournaments.Values.Select(t => t.Bouts.SelectMany(o => new long[] { o.Round, o.Slot, (long)o.A, (long)o.B, (long)o.Winner, o.AngleDeg }).ToArray())],
        Slanders = [.. b.Slanders.Select(s => new long[] { (long)s.Rumour, (long)s.Author, (long)s.Victim, s.Day, s.Exposed ? s.ExposedDay : -1 })],
        NextId = b.NextId, NextTournament = b.NextTournament, LastScanDay = b.LastScanDay, LastSlanderSeason = b.LastSlanderSeason,
    };

    public ContractBook Restore()
    {
        if (Contracts.Length != Kinds.Length || Tournaments.Length != Bouts.Length) throw new SaveException("Corrupt 'contracts' section in save.");
        var b = new ContractBook { NextId = NextId, NextTournament = NextTournament, LastScanDay = LastScanDay, LastSlanderSeason = LastSlanderSeason };
        for (int i = 0; i < Contracts.Length; i++)
        {
            var v = Contracts[i];
            if (v.Length < 25) throw new SaveException("Corrupt contract row in save.");
            var c = new WorkContract
            {
                Id = (int)v[0], Kind = Kinds[i], Issuer = new EntityId((ulong)v[1]), IssuerPurse = (int)v[2], Escrow = (int)v[3], PayMites = v[4], PostedDay = (int)v[5], DeadlineDay = (int)v[6], Settlement = (int)v[7],
                Target = new EntityId((ulong)v[8]), TargetOwner = new EntityId((ulong)v[9]), Good = (int)v[10], Grams = v[11], Baseline = v[12], Delivered = v[13], Param = v[14], State = (WorkState)v[15], Taker = new EntityId((ulong)v[16]),
                ByCompany = (v[17] & 1) != 0, Pinned = (v[17] & 2) != 0, TakenDay = (int)v[18], ClosedDay = (int)v[19], BondChest = (int)v[20], BondMites = v[21], CauseEntry = (ulong)v[22], PostedEntry = (ulong)v[23], JobId = (ulong)v[24],
            };
            for (int k = 25; k < v.Length; k++) c.Evidence.Add((ulong)v[k]);
            b.Contracts[c.Id] = c;
        }
        for (int i = 0; i < Tournaments.Length; i++)
        {
            var v = Tournaments[i];
            if (v.Length < 10) throw new SaveException("Corrupt tournament row in save.");
            var t = new Tournament { Id = (int)v[0], Settlement = (int)v[1], Year = (int)v[2], FairDay = (int)v[3], Escrow = (int)v[4], TreasuryPurse = (int)v[5], PurseMites = v[6], Fee = v[7], Paid = (v[8] & 1) != 0, Cancelled = (v[8] & 2) != 0, Drawn = (v[8] & 4) != 0, Winner = (ulong)v[9] };
            for (int k = 10; k < v.Length; k++) t.Entrants.Add((ulong)v[k]);
            var bo = Bouts[i];
            if (bo.Length % 6 != 0) throw new SaveException("Corrupt bracket in save.");
            for (int k = 0; k < bo.Length; k += 6) t.Bouts.Add(new Bout { Round = (int)bo[k], Slot = (int)bo[k + 1], A = (ulong)bo[k + 2], B = (ulong)bo[k + 3], Winner = (ulong)bo[k + 4], AngleDeg = (int)bo[k + 5] });
            b.Tournaments[t.Id] = t;
        }
        foreach (var s in Slanders)
        {
            if (s.Length < 5) throw new SaveException("Corrupt slander row in save.");
            b.Slanders.Add(new SlanderRow { Rumour = (ulong)s[0], Author = (ulong)s[1], Victim = (ulong)s[2], Day = (int)s[3], Exposed = s[4] >= 0, ExposedDay = (int)s[4] });
        }
        return b;
    }
}
