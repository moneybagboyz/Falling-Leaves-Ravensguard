using MessagePack;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Growth;
using Ravensguard.Sim.Holdings;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Save;

/// <summary>
/// The optional "landworks" section (P6-D4, holdings-and-works-v2 3.4 and 3.5; no format bump, written only once a street was laid or a soil job exists, so a world that never did either keeps its bytes):
/// the streets laid since generation (replayed over the regenerated region before the lots) and the soil works and improvements standing, each in order.
/// </summary>
[MessagePackObject]
public sealed class LandWorksSave
{
    public const string SectionName = "landworks";

    /// <summary>Per street: settlement, ax, ay, bx, by, width, plots, day.</summary>
    [Key(0)] public int[][] Streets { get; set; } = [];
    [Key(1)] public int SoilNextSerial { get; set; } = 1;
    /// <summary>Per work: serial, field, parcel, settlement, kind, state, contract, issuer, need milli, done milli, posted day, done day.</summary>
    [Key(2)] public long[][] Works { get; set; } = [];
    /// <summary>Per effect: field, kind, amount, start day, end day, last fade day.</summary>
    [Key(3)] public int[][] Effects { get; set; } = [];

    public static LandWorksSave Capture(IReadOnlyList<StreetRecord> streets, SoilBook? soil) => new()
    {
        Streets = [.. streets.Select(s => new[] { s.Settlement, s.A.X, s.A.Y, s.B.X, s.B.Y, s.WidthM, s.Plots, s.Day })],
        SoilNextSerial = soil?.NextSerial ?? 1,
        Works = soil == null ? [] : [.. soil.Works.Select(w => new long[] { w.Serial, w.Field, w.Parcel, w.Settlement, (long)w.Kind, (long)w.State, w.Contract, (long)w.Issuer.Value, w.NeedMilli, w.DoneMilli, w.PostedDay, w.DoneDay })],
        Effects = soil == null ? [] : [.. soil.Effects.Select(e => new[] { e.Field, (int)e.Kind, e.Amount, e.StartDay, e.EndDay, e.LastFade })],
    };

    public List<StreetRecord> RestoreStreets()
    {
        var l = new List<StreetRecord>();
        foreach (var r in Streets)
        {
            if (r.Length < 8) throw new SaveException("Corrupt street in save.");
            l.Add(new StreetRecord(r[0], new PointM(r[1], r[2]), new PointM(r[3], r[4]), r[5], r[6], r[7]));
        }
        return l;
    }

    public SoilBook RestoreSoil()
    {
        var b = new SoilBook { NextSerial = SoilNextSerial };
        foreach (var r in Works)
        {
            if (r.Length < 12) throw new SaveException("Corrupt soil work in save.");
            b.Works.Add(new SoilWork { Serial = (int)r[0], Field = (int)r[1], Parcel = (int)r[2], Settlement = (int)r[3], Kind = (SoilKind)r[4], State = (SoilState)r[5], Contract = (int)r[6], Issuer = new EntityId((ulong)r[7]),
                NeedMilli = r[8], DoneMilli = r[9], PostedDay = (int)r[10], DoneDay = (int)r[11] });
        }
        foreach (var r in Effects)
        {
            if (r.Length < 6) throw new SaveException("Corrupt soil effect in save.");
            b.Effects.Add(new SoilEffect { Field = r[0], Kind = (SoilKind)r[1], Amount = r[2], StartDay = r[3], EndDay = r[4], LastFade = r[5] });
        }
        return b;
    }
}
