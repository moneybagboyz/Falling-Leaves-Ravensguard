using MessagePack;
using Ravensguard.Sim.Production;

namespace Ravensguard.Sim.Save;

/// <summary>
/// The optional "scale" section (P7-ES1, economies-of-scale 6; no version bump, written only while the book holds something, so a world that never used it keeps its bytes): appointed foremen, manufactory licences,
/// guild fines and closing orders. Rows are written in the book's own (ascending) order, so the bytes are deterministic. ES2 appends its supply contracts and pattern runs as further keys.
/// </summary>
[MessagePackObject]
public sealed class ScaleSave
{
    public const string SectionName = "scale";

    /// <summary>Per foreman: business id, person index.</summary>
    [Key(0)] public long[][] Foremen { get; set; } = [];
    /// <summary>Per licence: business id, fief, granted day, fee, last season paid.</summary>
    [Key(1)] public long[][] Licences { get; set; } = [];
    [Key(2)] public string[] LicenceGuilds { get; set; } = [];
    /// <summary>Per fine: business id, season, day, workers over, fine, paid.</summary>
    [Key(3)] public long[][] Fines { get; set; } = [];
    /// <summary>Per closing order: business id, day.</summary>
    [Key(4)] public long[][] Closings { get; set; } = [];

    public static ScaleSave Capture(ScaleBook b)
    {
        var foremen = new List<long[]>();
        foreach (var (biz, set) in b.Foremen) foreach (int p in set) foremen.Add([(long)biz, p]);
        return new ScaleSave
        {
            Foremen = [.. foremen],
            Licences = [.. b.Licences.Select(l => new long[] { (long)l.Business, l.Fief, l.GrantedDay, l.FeeMites, l.PaidSeason })],
            LicenceGuilds = [.. b.Licences.Select(l => l.Guild)],
            Fines = [.. b.Fines.Select(f => new long[] { (long)f.Business, f.Season, f.Day, f.Over, f.Mites, f.Paid })],
            Closings = [.. b.Closings.Select(c => new long[] { (long)c.Business, c.Day })],
        };
    }

    public ScaleBook Restore()
    {
        if (Licences.Length != LicenceGuilds.Length) throw new SaveException("Corrupt 'scale' section in save.");
        var b = new ScaleBook();
        foreach (var v in Foremen)
        {
            if (v.Length < 2) throw new SaveException("Corrupt foreman in save.");
            ulong biz = (ulong)v[0]; if (!b.Foremen.TryGetValue(biz, out var set)) b.Foremen[biz] = set = [];
            set.Add((int)v[1]);
        }
        for (int i = 0; i < Licences.Length; i++)
        {
            var v = Licences[i]; if (v.Length < 5) throw new SaveException("Corrupt licence in save.");
            b.Licences.Add(new ManufactoryLicence { Business = (ulong)v[0], Fief = (int)v[1], GrantedDay = (int)v[2], FeeMites = (int)v[3], PaidSeason = (int)v[4], Guild = LicenceGuilds[i] });
        }
        foreach (var v in Fines)
        {
            if (v.Length < 6) throw new SaveException("Corrupt guild fine in save.");
            b.Fines.Add(new GuildFine { Business = (ulong)v[0], Season = (int)v[1], Day = (int)v[2], Over = (int)v[3], Mites = v[4], Paid = v[5] });
        }
        foreach (var v in Closings)
        {
            if (v.Length < 2) throw new SaveException("Corrupt closing order in save.");
            b.Closings.Add(((ulong)v[0], (int)v[1]));
        }
        return b;
    }
}
