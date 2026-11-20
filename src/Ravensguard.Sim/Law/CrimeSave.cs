using MessagePack;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Law;

namespace Ravensguard.Sim.Save;

/// <summary>
/// The optional "law" section (P6-B2, crime-and-justice 3, overview 7; no format bump, absent while no crime has been seen, so a world that never used it keeps its bytes): crimes in id order with
/// their witnesses and description, and the conviction counts. Kinds are content ids (strings), so a content change cannot renumber them.
/// </summary>
[MessagePackObject]
public sealed class CrimeSave
{
    public const string SectionName = "law";

    /// <summary>Per crime: id, perpetrator, doer, victim, settlement, day, sec, value, damage, entry, witnessed, reported, bounty job, state, flags (1 cried), reporter, build, witness count, bounty entry.</summary>
    [Key(0)] public long[][] Crimes { get; set; } = [];
    [Key(1)] public string[] Kinds { get; set; } = [];
    /// <summary>Per crime, flattened: witness person, flags, then the look's colours.</summary>
    [Key(2)] public long[][] People { get; set; } = [];
    /// <summary>Per conviction: person, count.</summary>
    [Key(3)] public long[][] Convictions { get; set; } = [];
    [Key(4)] public string[] ConvictionKinds { get; set; } = [];
    [Key(5)] public int NextId { get; set; } = 1;
    [Key(6)] public int LastDay { get; set; } = -1;

    public static CrimeSave Capture(CrimeBook b) => new()
    {
        Crimes = [.. b.Crimes.Values.Select(c => new long[]
        {
            c.Id, c.Perpetrator, (long)c.Doer, (long)c.Victim, c.Settlement, c.Day, c.Sec, c.ValueMites, c.Damage, (long)c.Entry, (long)c.Witnessed, (long)c.Reported, (long)c.BountyJob,
            (long)c.State, c.Cried ? 1 : 0, (long)c.Reporter, c.Look.Build, c.Witnesses.Count, (long)c.BountyEntry,
        })],
        Kinds = [.. b.Crimes.Values.Select(c => c.Kind)],
        People = [.. b.Crimes.Values.Select(c => c.Witnesses.SelectMany(w => new long[] { (long)w.Person, w.Flags }).Concat(c.Look.Colours.Select(x => (long)x)).ToArray())],
        Convictions = [.. b.Convictions.Select(kv => new long[] { (long)kv.Key.Person, kv.Value })],
        ConvictionKinds = [.. b.Convictions.Keys.Select(k => k.Kind)],
        NextId = b.NextId, LastDay = b.LastDay,
    };

    public CrimeBook Restore()
    {
        if (Crimes.Length != Kinds.Length || Crimes.Length != People.Length || Convictions.Length != ConvictionKinds.Length) throw new SaveException("Corrupt 'law' section in save.");
        var b = new CrimeBook { NextId = NextId, LastDay = LastDay };
        for (int i = 0; i < Crimes.Length; i++)
        {
            var v = Crimes[i]; var p = People[i];
            if (v.Length < 19 || p.Length < v[17] * 2) throw new SaveException("Corrupt crime in save.");
            var c = new CrimeRecord
            {
                Id = (int)v[0], Kind = Kinds[i], Perpetrator = v[1], Doer = (ulong)v[2], Victim = (ulong)v[3], Settlement = (int)v[4], Day = (int)v[5], Sec = v[6], ValueMites = v[7], Damage = v[8],
                Entry = (ulong)v[9], Witnessed = (ulong)v[10], Reported = (ulong)v[11], BountyJob = (ulong)v[12], State = (CrimeState)v[13], Cried = v[14] != 0, Reporter = (ulong)v[15], BountyEntry = (ulong)v[18],
            };
            int n = (int)v[17];
            for (int k = 0; k < n; k++) c.Witnesses.Add(new WitnessRow((ulong)p[k * 2], (byte)p[k * 2 + 1]));
            c.Look = new ActorLook([.. p.Skip(n * 2).Select(x => (uint)x)], (byte)v[16]);
            b.Crimes[c.Id] = c;
        }
        for (int i = 0; i < Convictions.Length; i++)
        {
            var r = Convictions[i]; if (r.Length < 2) throw new SaveException("Corrupt conviction in save.");
            b.Convictions[((ulong)r[0], ConvictionKinds[i])] = (int)r[1];
        }
        return b;
    }
}
