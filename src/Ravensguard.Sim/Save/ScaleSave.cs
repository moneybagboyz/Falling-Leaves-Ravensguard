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

    // ---- P7-ES2 (appended keys: an older save has none of them and loads with empty lists)
    /// <summary>Per contract: id, seller, buyer, qty per delivery, every days, start day, term seasons, price micro, carter is seller (0/1), delivered, missed, season of the last miss, misses that season, next due, end day, state.</summary>
    [Key(5)] public long[][] Contracts { get; set; } = [];
    [Key(6)] public string[] ContractGoods { get; set; } = [];
    [Key(7)] public long NextContractId { get; set; }
    /// <summary>Per pattern run row: business id, then the days of the runs.</summary>
    [Key(8)] public long[][] Runs { get; set; } = [];
    [Key(9)] public string[] RunRecipes { get; set; } = [];
    /// <summary>Per machine coupling: business id, station, x, y, z.</summary>
    [Key(10)] public long[][] Couplings { get; set; } = [];

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
            Contracts = [.. b.Contracts.Select(c => new long[] { c.Id, (long)c.Seller, (long)c.Buyer, c.QtyPerDelivery, c.EveryDays, c.StartDay, c.TermSeasons, c.PriceMicro, c.CarterIsSeller ? 1 : 0, c.Delivered, c.Missed, c.SeasonOfMiss, c.SeasonMisses, c.NextDue, c.EndDay, (long)c.State })],
            ContractGoods = [.. b.Contracts.Select(c => c.Good)],
            NextContractId = b.Contracts.Count > 0 ? b.NextContractId : 0,
            Runs = [.. b.Runs.Select(r => new[] { (long)r.Business }.Concat(r.Days.Select(d => (long)d)).ToArray())],
            RunRecipes = [.. b.Runs.Select(r => r.Recipe)],
            Couplings = [.. b.Couplings.Select(c => new long[] { (long)c.Business, c.Station, c.X, c.Y, c.Z })],
        };
    }

    public ScaleBook Restore()
    {
        if (Licences.Length != LicenceGuilds.Length || Contracts.Length != ContractGoods.Length || Runs.Length != RunRecipes.Length) throw new SaveException("Corrupt 'scale' section in save.");
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
        for (int i = 0; i < Contracts.Length; i++)
        {
            var v = Contracts[i]; if (v.Length < 16) throw new SaveException("Corrupt supply contract in save.");
            b.Contracts.Add(new SupplyContract
            {
                Id = (int)v[0], Seller = (ulong)v[1], Buyer = (ulong)v[2], Good = ContractGoods[i], QtyPerDelivery = v[3], EveryDays = (int)v[4], StartDay = (int)v[5], TermSeasons = (int)v[6], PriceMicro = v[7], CarterIsSeller = v[8] != 0,
                Delivered = (int)v[9], Missed = (int)v[10], SeasonOfMiss = (int)v[11], SeasonMisses = (int)v[12], NextDue = (int)v[13], EndDay = (int)v[14], State = (SupplyState)v[15],
            });
        }
        if (Contracts.Length > 0) b.NextContractId = (int)Math.Max(NextContractId, b.Contracts.Max(c => c.Id) + 1);
        for (int i = 0; i < Runs.Length; i++)
        {
            var v = Runs[i]; if (v.Length < 1) throw new SaveException("Corrupt pattern run in save.");
            var r = new PatternRun { Business = (ulong)v[0], Recipe = RunRecipes[i] };
            for (int k = 1; k < v.Length; k++) r.Days.Add((int)v[k]);
            b.Runs.Add(r);
        }
        foreach (var v in Couplings)
        {
            if (v.Length < 5) throw new SaveException("Corrupt machine coupling in save.");
            b.Couplings.Add(new MachineCoupling { Business = (ulong)v[0], Station = (int)v[1], X = (int)v[2], Y = (int)v[3], Z = (int)v[4] });
        }
        foreach (var v in Closings)
        {
            if (v.Length < 2) throw new SaveException("Corrupt closing order in save.");
            b.Closings.Add(((ulong)v[0], (int)v[1]));
        }
        return b;
    }
}
