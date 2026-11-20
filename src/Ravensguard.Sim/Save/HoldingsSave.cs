using MessagePack;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Holdings;

namespace Ravensguard.Sim.Save;

/// <summary>
/// The optional "holdings" section (P6-B6, holdings-and-works-v2 3.1 and 3.7; no format bump, absent while nothing was cleared, coppiced or foaled, so a world that never used holdings keeps its
/// bytes): the clearings with their hands, the coppice cuts, the mares in foal and the mares tried this year, each in ascending order.
/// </summary>
[MessagePackObject]
public sealed class HoldingsSave
{
    public const string SectionName = "holdings";

    [Key(0)] public int NextSerial { get; set; } = 1;
    /// <summary>Per clearing: serial, claim, tile, owner, trees, felled, grubbed, need, done, hero, wood0, logs, firewood, hero day, hero day milli, start, cleared, sown, last, parcel, state, short days.</summary>
    [Key(1)] public long[][] Clearings { get; set; } = [];
    [Key(2)] public string[] Crops { get; set; } = [];
    [Key(3)] public ulong[][] Hands { get; set; } = [];
    /// <summary>Per cut: tile, cut day, due day.</summary>
    [Key(4)] public int[][] Coppice { get; set; } = [];
    /// <summary>Per mare in foal: mare, sire, year, due day.</summary>
    [Key(5)] public long[][] Foaling { get; set; } = [];
    /// <summary>Per mare tried: mare, year.</summary>
    [Key(6)] public long[][] Tried { get; set; } = [];
    [Key(7)] public long FoalsBorn { get; set; }
    [Key(8)] public long LogsFelledG { get; set; }
    /// <summary>Per room owner: the room's lowest cell key, the owner's id. Per assigned bed: the bed's cell key, the person's id.</summary>
    [Key(9)] public long[][] RoomOwner { get; set; } = [];
    [Key(10)] public long[][] BedAssigned { get; set; } = [];
    /// <summary>The heavy-work day: day, milli-hours, fed, hungry.</summary>
    [Key(11)] public long[] Heavy { get; set; } = [];

    public static HoldingsSave Capture(HoldingsBook b) => new()
    {
        NextSerial = b.NextSerial, FoalsBorn = b.FoalsBorn, LogsFelledG = b.LogsFelledG,
        Clearings = [.. b.Clearings.Select(c => new long[] { c.Serial, c.Claim, c.Tile, (long)c.Owner.Value, c.Trees, c.Felled, c.Grubbed, c.NeedMilli, c.DoneMilli, c.HeroMilli, c.WoodG0, c.LogsG, c.FirewoodG,
            c.HeroDay, c.HeroDayMilli, c.StartDay, c.ClearedDay, c.SownDay, c.LastDay, c.Parcel, (long)c.State, c.ShortDays })],
        Crops = [.. b.Clearings.Select(c => c.Crop)],
        Hands = [.. b.Clearings.Select(c => c.Hands.Select(p => p.Value).ToArray())],
        Coppice = [.. b.Coppice.Select(c => new[] { c.Tile, c.CutDay, c.DueDay })],
        Foaling = [.. b.Foaling.Select(f => new long[] { (long)f.Mare, (long)f.Sire, f.Year, f.DueDay })],
        Tried = [.. b.Tried.Select(kv => new long[] { (long)kv.Key, kv.Value })],
        Heavy = b.HeavyDay < 0 ? [] : [b.HeavyDay, b.HeavyMilli, b.Fed ? 1 : 0, b.Hungry ? 1 : 0],
        RoomOwner = [.. b.RoomOwner.Select(kv => new long[] { kv.Key, (long)kv.Value })],
        BedAssigned = [.. b.BedAssigned.Select(kv => new long[] { kv.Key, (long)kv.Value })],
    };

    public HoldingsBook Restore()
    {
        if (Clearings.Length != Crops.Length || Clearings.Length != Hands.Length) throw new SaveException("Corrupt 'holdings' section in save.");
        var b = new HoldingsBook { NextSerial = NextSerial, FoalsBorn = FoalsBorn, LogsFelledG = LogsFelledG };
        for (int i = 0; i < Clearings.Length; i++)
        {
            var r = Clearings[i];
            if (r.Length < 22) throw new SaveException("Corrupt clearing in save.");
            var c = new Clearing
            {
                Serial = (int)r[0], Claim = (int)r[1], Tile = (int)r[2], Owner = new EntityId((ulong)r[3]), Trees = (int)r[4], Felled = (int)r[5], Grubbed = (int)r[6], NeedMilli = r[7], DoneMilli = r[8], HeroMilli = r[9],
                WoodG0 = r[10], LogsG = r[11], FirewoodG = r[12], HeroDay = (int)r[13], HeroDayMilli = r[14], StartDay = (int)r[15], ClearedDay = (int)r[16], SownDay = (int)r[17], LastDay = (int)r[18], Parcel = (int)r[19],
                State = (ClearingState)r[20], ShortDays = (int)r[21], Crop = Crops[i],
            };
            foreach (var p in Hands[i]) c.Hands.Add(new EntityId(p));
            b.Clearings.Add(c);
        }
        foreach (var r in Coppice) { if (r.Length < 3) throw new SaveException("Corrupt coppice cut in save."); b.Coppice.Add(new CoppiceCut { Tile = r[0], CutDay = r[1], DueDay = r[2] }); }
        foreach (var r in Foaling) { if (r.Length < 4) throw new SaveException("Corrupt foaling row in save."); b.Foaling.Add(new Pregnancy { Mare = (ulong)r[0], Sire = (ulong)r[1], Year = (int)r[2], DueDay = (int)r[3] }); }
        foreach (var r in Tried) { if (r.Length < 2) throw new SaveException("Corrupt breeding row in save."); b.Tried[(ulong)r[0]] = (int)r[1]; }
        if (Heavy.Length >= 4) { b.HeavyDay = (int)Heavy[0]; b.HeavyMilli = Heavy[1]; b.Fed = Heavy[2] != 0; b.Hungry = Heavy[3] != 0; }
        foreach (var r in RoomOwner) { if (r.Length < 2) throw new SaveException("Corrupt room owner in save."); b.RoomOwner[r[0]] = (ulong)r[1]; }
        foreach (var r in BedAssigned) { if (r.Length < 2) throw new SaveException("Corrupt bed assignment in save."); b.BedAssigned[r[0]] = (ulong)r[1]; }
        return b;
    }
}
