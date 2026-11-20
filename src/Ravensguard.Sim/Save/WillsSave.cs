using MessagePack;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Succession;

namespace Ravensguard.Sim.Save;

/// <summary>
/// The optional "wills" section (P5-H, overview 7; no format bump, absent while no will exists, so a world that never made one keeps its bytes): each will by its testator (ascending id), its witness,
/// day and fee, and its bequests in the order made (person, what, thing, coin permille).
/// </summary>
[MessagePackObject]
public sealed class WillsSave
{
    public const string SectionName = "wills";

    /// <summary>Per will: testator, witness, day, fee.</summary>
    [Key(0)] public long[][] Wills { get; set; } = [];
    /// <summary>Per will, its bequests flattened: person, what, thing, coin permille.</summary>
    [Key(1)] public long[][] Bequests { get; set; } = [];

    public static WillsSave Capture(WillBook b) => new()
    {
        Wills = [.. b.ByTestator.Values.Select(w => new long[] { (long)w.Testator.Value, (long)w.Witness.Value, w.Day, w.FeeMites })],
        Bequests = [.. b.ByTestator.Values.Select(w => w.Bequests.SelectMany(q => new long[] { (long)q.Person.Value, (long)q.What, (long)q.Thing.Value, q.CoinPm }).ToArray())],
    };

    public WillBook Restore()
    {
        if (Wills.Length != Bequests.Length) throw new SaveException("Corrupt 'wills' section in save.");
        var b = new WillBook();
        for (int i = 0; i < Wills.Length; i++)
        {
            var v = Wills[i]; var q = Bequests[i];
            if (v.Length < 4 || q.Length % 4 != 0) throw new SaveException("Corrupt will in save.");
            var w = new Will { Testator = new EntityId((ulong)v[0]), Witness = new EntityId((ulong)v[1]), Day = (int)v[2], FeeMites = v[3] };
            for (int k = 0; k < q.Length; k += 4) w.Bequests.Add(new Bequest(new EntityId((ulong)q[k]), (BequestWhat)q[k + 1], new EntityId((ulong)q[k + 2]), (int)q[k + 3]));
            b.ByTestator[w.Testator] = w;
        }
        return b;
    }
}
