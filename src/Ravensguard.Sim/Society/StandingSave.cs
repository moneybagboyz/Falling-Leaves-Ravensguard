using MessagePack;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Society;

namespace Ravensguard.Sim.Save;

/// <summary>
/// The optional "standing" section (P6-A3, reputation 3, overview 7; no format bump, absent while no deed has been counted and no mark is held, so a world that never used it keeps its bytes):
/// the counted deeds with their hearings by settlement, the standing rows, renown and the marks, each in ascending order.
/// </summary>
[MessagePackObject]
public sealed class StandingSave
{
    public const string SectionName = "standing";

    /// <summary>Per deed: entry, doer, day, place, amount, weight (permille), and for a deed the church proclaimed a seventh value, 1 (P6-B4; absent otherwise, so older saves keep their bytes).</summary>
    [Key(0)] public long[][] Deeds { get; set; } = [];
    /// <summary>Per deed: its content id.</summary>
    [Key(1)] public string[] DeedIds { get; set; } = [];
    /// <summary>Per deed, flattened: settlement, count.</summary>
    [Key(2)] public long[][] Heard { get; set; } = [];
    /// <summary>Per row: person, group (packed), value, heard, last day.</summary>
    [Key(3)] public long[][] Rows { get; set; } = [];
    /// <summary>Per renown: person, share, decay, last deed day.</summary>
    [Key(4)] public long[][] Renown { get; set; } = [];
    /// <summary>Per marked person: person, brands, flags (1 enslaved, 2 pardoned), outlaw realm count, then the realms, then the excommunicating religions.</summary>
    [Key(5)] public long[][] Marks { get; set; } = [];
    [Key(6)] public int LastDay { get; set; } = -1;

    public static StandingSave Capture(StandingBook b) => new()
    {
        Deeds = [.. b.Deeds.Values.Select(d => d.Proclaimed ? new long[] { (long)d.Entry.Value, (long)d.Doer.Value, d.Day, d.Place, d.Amount, d.WeightPm, 1 } : new long[] { (long)d.Entry.Value, (long)d.Doer.Value, d.Day, d.Place, d.Amount, d.WeightPm })],
        DeedIds = [.. b.Deeds.Values.Select(d => d.DeedId)],
        Heard = [.. b.Deeds.Values.Select(d => d.Heard.SelectMany(kv => new long[] { kv.Key, kv.Value }).ToArray())],
        Rows = [.. b.Rows.Select(kv => new long[] { (long)kv.Key.Person, kv.Key.Group, kv.Value.ValuePm, kv.Value.Heard, kv.Value.LastDay })],
        Renown = [.. b.Renowns.Select(kv => new long[] { (long)kv.Key, kv.Value.SharePm, kv.Value.DecayPm, kv.Value.LastDeedDay })],
        Marks = [.. b.Marks.All.Select(kv => new long[] { (long)kv.Key, (long)kv.Value.Brands, (kv.Value.Enslaved ? 1 : 0) | (kv.Value.Pardoned ? 2 : 0), kv.Value.OutlawRealms.Count }
            .Concat(kv.Value.OutlawRealms.Select(x => (long)x)).Concat(kv.Value.Excommunicated.Select(x => (long)x)).ToArray())],
        LastDay = b.LastDay,
    };

    public StandingBook Restore()
    {
        if (Deeds.Length != DeedIds.Length || Deeds.Length != Heard.Length) throw new SaveException("Corrupt 'standing' section in save.");
        var b = new StandingBook { LastDay = LastDay };
        for (int i = 0; i < Deeds.Length; i++)
        {
            var v = Deeds[i]; var h = Heard[i];
            if (v.Length < 6 || h.Length % 2 != 0) throw new SaveException("Corrupt deed in save.");
            var d = new DeedRecord { Entry = new EntityId((ulong)v[0]), Doer = new EntityId((ulong)v[1]), DeedId = DeedIds[i], Day = (int)v[2], Place = (int)v[3], Amount = (int)v[4], WeightPm = (int)v[5], Proclaimed = v.Length > 6 && v[6] != 0 };
            for (int k = 0; k < h.Length; k += 2) d.Heard[(int)h[k]] = (int)h[k + 1];
            b.AddDeed(d);
        }
        foreach (var r in Rows)
        {
            if (r.Length < 5) throw new SaveException("Corrupt standing row in save.");
            b.Rows[((ulong)r[0], (uint)r[1])] = new StandingRow { ValuePm = (int)r[2], Heard = (int)r[3], LastDay = (int)r[4] };
        }
        foreach (var r in Renown)
        {
            if (r.Length < 4) throw new SaveException("Corrupt renown row in save.");
            b.Renowns[(ulong)r[0]] = new RenownRow { SharePm = (int)r[1], DecayPm = (int)r[2], LastDeedDay = (int)r[3] };
        }
        foreach (var m in Marks)
        {
            if (m.Length < 4 || m.Length < 4 + m[3]) throw new SaveException("Corrupt marks in save.");
            var set = new MarkSet { Brands = (Brand)m[1], Enslaved = (m[2] & 1) != 0, Pardoned = (m[2] & 2) != 0 };
            for (int k = 0; k < m[3]; k++) set.OutlawRealms.Add((int)m[4 + k]);
            for (int k = 4 + (int)m[3]; k < m.Length; k++) set.Excommunicated.Add((int)m[k]);
            b.Marks.Restore((ulong)m[0], set);
        }
        return b;
    }
}
