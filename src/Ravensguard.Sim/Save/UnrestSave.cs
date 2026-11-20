using MessagePack;
using Ravensguard.Sim.Unrest;

namespace Ravensguard.Sim.Save;

/// <summary>
/// The optional "unrest" section (P6-E1, unrest-and-revolt 3; no format bump, absent while the book has nothing, so a world that never had a grievance keeps its bytes): the grievances events left on households, each
/// settlement's place on the ladder, the riots and the revolts. Rows are written in the book's own (ascending) order so the bytes are deterministic.
/// </summary>
[MessagePackObject]
public sealed class UnrestSave
{
    public const string SectionName = "unrest";

    /// <summary>Per grievance row: household, points, lapse day, entry.</summary>
    [Key(0)] public long[][] Rows { get; set; } = [];
    [Key(1)] public string[] RowKinds { get; set; } = [];
    /// <summary>Per settlement: settlement, unrest, rung, high days, top days, trigger day, cooldown until, fear until, grumble season.</summary>
    [Key(2)] public long[][] Settlements { get; set; } = [];
    [Key(3)] public string[] TriggerKinds { get; set; } = [];
    /// <summary>Per riot: id, settlement, fief, leader, start day, target, state, end, rise entry, end entry, deaths, unrest, acted hour, ended hour, conceded, hero side, loot mites, loot grams, bonded.</summary>
    [Key(4)] public long[][] Riots { get; set; } = [];
    [Key(5)] public long[][] Crowds { get; set; } = [];
    /// <summary>Per riot: the top kind, then the counts' kinds; and the counts.</summary>
    [Key(6)] public string[][] RiotKinds { get; set; } = [];
    [Key(7)] public long[][] RiotCounts { get; set; } = [];
    /// <summary>Per revolt: id, fief, settlement, start, resolve, riot, state, claimant, claimant kind, old lord, leader, defenders, bonded, rise entry, end entry, rebels at the start, dead.</summary>
    [Key(8)] public long[][] Revolts { get; set; } = [];
    [Key(9)] public long[][] Rebels { get; set; } = [];
    [Key(10)] public long[][] Dole { get; set; } = [];
    [Key(11)] public long[] Pardoned { get; set; } = [];
    /// <summary>Counters: next riot, next revolt.</summary>
    [Key(12)] public long[] Counters { get; set; } = [];

    public static UnrestSave Capture(UnrestBook b)
    {
        var rows = new List<long[]>(); var kinds = new List<string>();
        foreach (var (h, l) in b.Rows) foreach (var r in l) { rows.Add([(long)h, r.Points, r.UntilDay, (long)r.Entry]); kinds.Add(r.Kind); }
        return new UnrestSave
        {
            Rows = [.. rows], RowKinds = [.. kinds],
            Settlements = [.. b.Settlements.Select(x => new long[] { x.Key, x.Value.Unrest, x.Value.Rung, x.Value.HighDays, x.Value.TopDays, x.Value.TriggerDay, x.Value.CooldownUntil, x.Value.FearUntil, x.Value.GrumbleSeason })],
            TriggerKinds = [.. b.Settlements.Select(x => x.Value.TriggerKind)],
            Riots = [.. b.Riots.Select(r => new long[] { r.Id, r.Settlement, r.Fief, (long)r.Leader, r.StartDay, (long)r.Target, (long)r.State, (long)r.End, (long)r.RiseEntry, (long)r.EndEntry, r.Deaths, r.UnrestPm, r.ActedHour, r.EndedHour,
                r.Conceded ? 1 : 0, r.HeroSide, r.LootMites, r.LootGrams, r.Bonded ? 1 : 0 })],
            Crowds = [.. b.Riots.Select(r => r.Crowd.Select(x => (long)x).ToArray())],
            RiotKinds = [.. b.Riots.Select(r => new[] { r.TopKind }.Concat(r.Counts.Select(x => x.Kind)).ToArray())],
            RiotCounts = [.. b.Riots.Select(r => r.Counts.Select(x => (long)x.Count).ToArray())],
            Revolts = [.. b.Revolts.Select(v => new long[] { v.Id, v.Fief, v.Settlement, v.StartDay, v.ResolveDay, v.RiotId, (long)v.State, (long)v.Claimant, v.ClaimantKind, (long)v.OldLord, (long)v.Leader, v.Defenders,
                v.Bonded ? 1 : 0, (long)v.RiseEntry, (long)v.EndEntry, v.Rebels0, v.Dead })],
            Rebels = [.. b.Revolts.Select(v => v.Rebels.Select(x => (long)x).ToArray())],
            Dole = [.. b.DoleUntil.Select(x => new long[] { x.Key, x.Value })],
            Pardoned = [.. b.Pardoned.Select(x => (long)x)],
            Counters = [b.NextRiot, b.NextRevolt],
        };
    }

    public UnrestBook Restore()
    {
        if (Counters.Length < 2 || Rows.Length != RowKinds.Length || Settlements.Length != TriggerKinds.Length || Riots.Length != Crowds.Length || Riots.Length != RiotKinds.Length || Riots.Length != RiotCounts.Length || Revolts.Length != Rebels.Length)
            throw new SaveException("Corrupt 'unrest' section in save.");
        var b = new UnrestBook { NextRiot = (int)Counters[0], NextRevolt = (int)Counters[1] };
        for (int i = 0; i < Rows.Length; i++)
        {
            var v = Rows[i]; if (v.Length < 4) throw new SaveException("Corrupt grievance in save.");
            ulong h = (ulong)v[0]; if (!b.Rows.TryGetValue(h, out var l)) b.Rows[h] = l = [];
            l.Add(new GrievanceRow { Kind = RowKinds[i], Points = (int)v[1], UntilDay = (int)v[2], Entry = (ulong)v[3] });
        }
        for (int i = 0; i < Settlements.Length; i++)
        {
            var v = Settlements[i]; if (v.Length < 9) throw new SaveException("Corrupt settlement unrest in save.");
            b.Settlements[(int)v[0]] = new SettlementUnrest { Unrest = (int)v[1], Rung = (int)v[2], HighDays = (int)v[3], TopDays = (int)v[4], TriggerDay = (int)v[5], CooldownUntil = (int)v[6], FearUntil = (int)v[7], GrumbleSeason = (int)v[8], TriggerKind = TriggerKinds[i] };
        }
        for (int i = 0; i < Riots.Length; i++)
        {
            var v = Riots[i]; if (v.Length < 19 || RiotKinds[i].Length != RiotCounts[i].Length + 1) throw new SaveException("Corrupt riot in save.");
            var r = new Riot { Id = (int)v[0], Settlement = (int)v[1], Fief = (int)v[2], Leader = (ulong)v[3], StartDay = (int)v[4], Target = (RiotTarget)v[5], State = (RiotState)v[6], End = (RiotEnd)v[7], RiseEntry = (ulong)v[8], EndEntry = (ulong)v[9],
                Deaths = (int)v[10], UnrestPm = (int)v[11], ActedHour = v[12], EndedHour = v[13], Conceded = v[14] != 0, HeroSide = (byte)v[15], LootMites = v[16], LootGrams = v[17], Bonded = v[18] != 0, TopKind = RiotKinds[i][0] };
            r.Crowd = [.. Crowds[i].Select(x => (ulong)x)];
            for (int k = 0; k < RiotCounts[i].Length; k++) r.Counts.Add((RiotKinds[i][k + 1], (int)RiotCounts[i][k]));
            b.Riots.Add(r);
        }
        for (int i = 0; i < Revolts.Length; i++)
        {
            var v = Revolts[i]; if (v.Length < 17) throw new SaveException("Corrupt revolt in save.");
            b.Revolts.Add(new Revolt { Id = (int)v[0], Fief = (int)v[1], Settlement = (int)v[2], StartDay = (int)v[3], ResolveDay = (int)v[4], RiotId = (int)v[5], State = (RevoltState)v[6], Claimant = (ulong)v[7], ClaimantKind = (byte)v[8], OldLord = (ulong)v[9],
                Leader = (ulong)v[10], Defenders = (int)v[11], Bonded = v[12] != 0, RiseEntry = (ulong)v[13], EndEntry = (ulong)v[14], Rebels0 = (int)v[15], Dead = (int)v[16], Rebels = [.. Rebels[i].Select(x => (ulong)x)] });
        }
        foreach (var v in Dole) { if (v.Length < 2) throw new SaveException("Corrupt dole in save."); b.DoleUntil[(int)v[0]] = (int)v[1]; }
        foreach (long p in Pardoned) b.Pardoned.Add((ulong)p);
        return b;
    }
}
