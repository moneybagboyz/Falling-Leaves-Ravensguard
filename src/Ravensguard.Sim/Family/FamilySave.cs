using MessagePack;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Family;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Save;

/// <summary>
/// The optional "family" section (P6-B3, family-and-lifespan 3, overview 7; no format bump, absent while the hero has no courtship, no lessons, no kin grievance and no ageing state, so a world that never
/// used it keeps its bytes): courtships, upbringing with the thousandths gained, kin grievances, the hero's ageing, the children noticed and the pending death. Pregnancies are the person table's.
/// </summary>
[MessagePackObject]
public sealed class FamilySave
{
    public const string SectionName = "family";

    /// <summary>Per courtship: id, suitor, courted, start day, gifts, meetings, consent, state, wedding day, last meeting day, asked day, score.</summary>
    [Key(0)] public long[][] Courtships { get; set; } = [];
    /// <summary>Per upbringing: child, tutor, course, trade skill, hours, has tutor, then pairs of (milli key, thousandths).</summary>
    [Key(1)] public long[][] Upbringings { get; set; } = [];
    /// <summary>Per kin member: person, grievance flags.</summary>
    [Key(2)] public long[][] Grievances { get; set; } = [];
    /// <summary>The children noticed.</summary>
    [Key(3)] public long[] Born { get; set; } = [];
    /// <summary>The hero's ageing: hero, row, last age, last roll year, warned year, then the five losses (empty: none).</summary>
    [Key(4)] public long[] Ageing { get; set; } = [];
    /// <summary>The pending death: day, row, age, cause (empty: none).</summary>
    [Key(5)] public long[] Death { get; set; } = [];
    [Key(6)] public int NextCourtship { get; set; } = 1;
    [Key(7)] public int LastDay { get; set; } = -1;
    [Key(8)] public int LastKidnapSeason { get; set; } = -1;
    [Key(9)] public int Kidnaps { get; set; }

    public static FamilySave Capture(FamilyBook b) => new()
    {
        Courtships = [.. b.Courtships.Values.Select(c => new long[] { c.Id, (long)c.Suitor, (long)c.Courted, c.StartDay, c.Gifts, c.Meetings, (long)c.Consent, (long)c.State, c.WeddingDay, c.LastMeetingDay, c.AskedDay, c.Score })],
        Upbringings = [.. b.Upbringings.Values.Select(u => new long[] { (long)u.Child, (long)u.Tutor, (long)u.Course, (long)u.TradeSkill, u.Hours, u.HasTutor ? 1 : 0 }.Concat(u.Milli.SelectMany(kv => new long[] { kv.Key, kv.Value })).ToArray())],
        Grievances = [.. b.Grievances.Select(kv => new long[] { (long)kv.Key, (long)kv.Value })],
        Born = [.. b.Born.Select(x => (long)x)],
        Ageing = b.Ageing is { } a ? new long[] { (long)a.Hero, a.Row, a.LastAge, a.LastRollYear, a.WarnedYear }.Concat(a.Lost.Select(x => (long)x)).ToArray() : [],
        Death = b.Death is { } d ? [d.Day, d.Row, d.Age, (long)d.Cause] : [],
        NextCourtship = b.NextCourtship, LastDay = b.LastDay, LastKidnapSeason = b.LastKidnapSeason, Kidnaps = b.Kidnaps,
    };

    public FamilyBook Restore()
    {
        var b = new FamilyBook { NextCourtship = NextCourtship, LastDay = LastDay, LastKidnapSeason = LastKidnapSeason, Kidnaps = Kidnaps };
        foreach (var v in Courtships)
        {
            if (v.Length < 12) throw new SaveException("Corrupt courtship in save.");
            var c = new Courtship
            {
                Id = (int)v[0], Suitor = (ulong)v[1], Courted = (ulong)v[2], StartDay = (int)v[3], Gifts = v[4], Meetings = (int)v[5], Consent = (ConsentState)v[6], State = (CourtState)v[7],
                WeddingDay = (int)v[8], LastMeetingDay = (int)v[9], AskedDay = (int)v[10], Score = (int)v[11],
            };
            b.Courtships[c.Id] = c;
        }
        foreach (var v in Upbringings)
        {
            if (v.Length < 6 || (v.Length - 6) % 2 != 0) throw new SaveException("Corrupt upbringing in save.");
            var u = new Upbringing { Child = (ulong)v[0], Tutor = (ulong)v[1], Course = (Course)v[2], TradeSkill = (Skill)v[3], Hours = (int)v[4], HasTutor = v[5] != 0 };
            for (int k = 6; k < v.Length; k += 2) u.Milli[(int)v[k]] = v[k + 1];
            b.Upbringings[u.Child] = u;
        }
        foreach (var v in Grievances) { if (v.Length < 2) throw new SaveException("Corrupt grievance in save."); b.Grievances[(ulong)v[0]] = (KinGrievance)v[1]; }
        foreach (long x in Born) b.Born.Add((ulong)x);
        if (Ageing.Length >= 5 + PersonTable.AttrCount)
        {
            var a = new HeroAgeing { Hero = (ulong)Ageing[0], Row = (int)Ageing[1], LastAge = (int)Ageing[2], LastRollYear = (int)Ageing[3], WarnedYear = (int)Ageing[4] };
            for (int k = 0; k < PersonTable.AttrCount; k++) a.Lost[k] = (int)Ageing[5 + k];
            b.Ageing = a;
        }
        if (Death.Length >= 4) b.Death = new PendingDeath { Day = (int)Death[0], Row = (int)Death[1], Age = (int)Death[2], Cause = (DeathCause)Death[3] };
        return b;
    }
}
