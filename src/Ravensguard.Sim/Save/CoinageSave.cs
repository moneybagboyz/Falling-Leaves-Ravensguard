using MessagePack;
using Ravensguard.Sim.Economy;

namespace Ravensguard.Sim.Save;

/// <summary>
/// The optional "coinage" section (P7-CN1, coinage 6; no version bump, written only while the book holds something, so a world that never struck gold or copper keeps its bytes): the gold book (struck, melted, copper pieces,
/// loose, gold pieces by purse, the purses opted in beyond the kinds that always are), the metal waiting at each mint and what each struck today and this season. Its absence means no gold anywhere, no copper coin and
/// every purse silver: exactly a world from before Phase 7, so an older save derives it with nothing to write. Rows are written in the book's own (ascending or queue) order, so the bytes are deterministic.
/// </summary>
[MessagePackObject]
public sealed class CoinageSave
{
    public const string SectionName = "coinage";

    [Key(0)] public long GoldStruck { get; set; }
    [Key(1)] public long GoldMelted { get; set; }
    /// <summary>Copper pieces struck less melted (the cap's measure); <see cref="CopperMelted"/> is kept apart so the book restores exactly.</summary>
    [Key(2)] public long CopperStruck { get; set; }
    [Key(3)] public long CopperMelted { get; set; }
    [Key(4)] public long Loose { get; set; }
    /// <summary>Per opted-in purse holding gold: purse index, pieces (ascending purse).</summary>
    [Key(5)] public long[][] OptedGold { get; set; } = [];
    /// <summary>Purses opted in beyond the always-opted kinds (ascending purse index).</summary>
    [Key(6)] public int[] OptedPurses { get; set; } = [];
    /// <summary>Per mint delivery in queue order: realm, metal, mg waiting, seller purse (-1: the mint's own dust).</summary>
    [Key(7)] public long[][] MintQueues { get; set; } = [];
    /// <summary>Per realm's season row: realm, gold, silver, copper pieces, seigniorage, grams of copper turned away.</summary>
    [Key(8)] public long[][] MintSeason { get; set; } = [];
    [Key(9)] public int SeasonNo { get; set; } = -1;
    /// <summary>The day the moneyers' counts belong to, and per realm the pieces struck that day (realm, pieces).</summary>
    [Key(10)] public int UsedDay { get; set; } = -1;
    [Key(11)] public long[][] Used { get; set; } = [];
    /// <summary>Metal left over from melting coin, mg short of a gram: gold, silver, copper.</summary>
    [Key(12)] public long[] MeltDust { get; set; } = [];

    public static CoinageSave Capture(CoinageBook b)
    {
        var s = b.Stock;
        return new CoinageSave
        {
            GoldStruck = s.GoldStruck, GoldMelted = s.GoldMelted, CopperStruck = s.CopperStruck, CopperMelted = s.CopperMelted, Loose = s.Loose,
            OptedGold = [.. s.Opted.Select(kv => new long[] { kv.Key, kv.Value })],
            OptedPurses = [.. s.Extra],
            MintQueues = [.. b.Queue.Select(d => new long[] { d.Realm, (long)d.Metal, d.Mg, d.Seller })],
            MintSeason = [.. b.Season.Select(r => new long[] { r.Realm, r.Gold, r.Silver, r.Copper, r.Seigniorage, r.CopperCapped })],
            SeasonNo = b.SeasonNo, UsedDay = b.UsedDay,
            Used = [.. b.Used.Select(kv => new long[] { kv.Key, kv.Value })],
            MeltDust = s.GoldMeltDustMg == 0 && s.SilverMeltDustMg == 0 && s.CopperMeltDustMg == 0 ? [] : [s.GoldMeltDustMg, s.SilverMeltDustMg, s.CopperMeltDustMg],
        };
    }

    public CoinageBook Restore()
    {
        var b = new CoinageBook(); var s = b.Stock;
        if (GoldStruck < 0 || GoldMelted < 0 || GoldMelted > GoldStruck || CopperStruck < 0 || CopperMelted < 0 || CopperMelted > CopperStruck || Loose < 0) throw new SaveException("Corrupt 'coinage' section in save.");
        s.GoldStruck = GoldStruck; s.GoldMelted = GoldMelted; s.CopperStruck = CopperStruck; s.CopperMelted = CopperMelted; s.Loose = Loose;
        foreach (var v in OptedGold) { if (v.Length < 2 || v[1] <= 0) throw new SaveException("Corrupt gold row in save."); s.Opted[(int)v[0]] = v[1]; }
        foreach (int p in OptedPurses) s.Extra.Add(p);
        foreach (var v in MintQueues)
        {
            if (v.Length < 4 || v[1] is < 0 or > 2 || v[2] < 0) throw new SaveException("Corrupt mint queue in save.");
            b.Queue.Add(new MintDelivery { Realm = (int)v[0], Metal = (MintMetal)v[1], Mg = v[2], Seller = (int)v[3] });
        }
        foreach (var v in MintSeason)
        {
            if (v.Length < 6) throw new SaveException("Corrupt mint season row in save.");
            b.Season.Add(new MintSeasonRow { Realm = (int)v[0], Gold = v[1], Silver = v[2], Copper = v[3], Seigniorage = v[4], CopperCapped = v[5] });
        }
        b.SeasonNo = SeasonNo; b.UsedDay = UsedDay;
        foreach (var v in Used) { if (v.Length < 2) throw new SaveException("Corrupt moneyers' count in save."); b.Used[(int)v[0]] = v[1]; }
        if (MeltDust.Length >= 3) { s.GoldMeltDustMg = MeltDust[0]; s.SilverMeltDustMg = MeltDust[1]; s.CopperMeltDustMg = MeltDust[2]; }
        return b;
    }
}
