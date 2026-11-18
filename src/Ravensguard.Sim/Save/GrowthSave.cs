using MessagePack;
using Ravensguard.Sim.Growth;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Save;

/// <summary>
/// The "growth" section (background growth, docs/specs/phase3/background-growth.md): projects in progress, the log of the lots and parcels built since generation (replayed over
/// the regenerated region before anything else reads it), the unmet-demand counters and the yearly tallies. Absent in a world that has not grown, so older saves keep their bytes.
/// </summary>
[MessagePackObject]
public sealed class GrowthSave
{
    [Key(0)] public int Version { get; set; } = 1;
    [Key(1)] public int NextSerial { get; set; }
    [Key(2)] public int FertilityScale { get; set; }
    [Key(3)] public long[] Totals { get; set; } = [];

    // projects, parallel arrays
    [Key(4)] public int[] PSerial { get; set; } = [];
    [Key(5)] public byte[] PKind { get; set; } = [];
    [Key(6)] public int[] PSettlement { get; set; } = [];
    [Key(7)] public int[] PStartDay { get; set; } = [];
    [Key(8)] public int[] PPayer { get; set; } = [];
    [Key(9)] public int[] PPayerFief { get; set; } = [];
    [Key(10)] public long[] PHours { get; set; } = [];       // needed, done, paid, stalled x 4 per project
    [Key(11)] public int[] PLot { get; set; } = [];
    [Key(12)] public int[] PRect { get; set; } = [];          // x, y, w, h, use, dwelling, facing, wealth, beds x 9
    [Key(13)] public string[] PBlueprint { get; set; } = [];
    [Key(14)] public long[] PMaterials { get; set; } = [];    // count then grams, per project
    [Key(15)] public int[] PTiles { get; set; } = [];         // count then tiles, per project
    [Key(16)] public int[] PBusiness { get; set; } = [];      // kind, owner x 2

    // built lots
    [Key(17)] public int[] LotInts { get; set; } = [];        // settlement, lot, new, use, dwelling, x, y, w, h, facing, wealth, beds, day x 13
    [Key(18)] public string[] LotBlueprint { get; set; } = [];

    // built parcels
    [Key(19)] public int[] ParInts { get; set; } = [];        // settlement, owner kind, fief, lot, slot, day, tile count x 6 then tiles
    [Key(20)] public byte[] ParFertility { get; set; } = [];

    [Key(21)] public long[] UnmetKey { get; set; } = [];
    [Key(22)] public int[] UnmetVal { get; set; } = [];
    [Key(23)] public long[] YearVals { get; set; } = [];      // year, 7 counts, 2 sums x 10 per year
    [Key(24)] public int[] Cleared { get; set; } = [];

    public static GrowthSave Capture(GrowthState s)
    {
        var o = new GrowthSave
        {
            NextSerial = s.NextSerial, FertilityScale = s.FertilityScalePermille, Totals = [s.WoodDrawnG, s.FirewoodMadeG, s.LogsMadeG, s.PolesGatheredG],
            PSerial = [.. s.Projects.Select(p => p.Serial)], PKind = [.. s.Projects.Select(p => (byte)p.Kind)], PSettlement = [.. s.Projects.Select(p => p.Settlement)],
            PStartDay = [.. s.Projects.Select(p => p.StartDay)], PPayer = [.. s.Projects.Select(p => p.Payer)], PPayerFief = [.. s.Projects.Select(p => p.PayerFief)],
            PHours = [.. s.Projects.SelectMany(p => new[] { p.HoursNeeded, p.HoursDone, p.PaidMites, (long)p.StalledSeasons })], PLot = [.. s.Projects.Select(p => p.Lot)],
            PRect = [.. s.Projects.SelectMany(p => new[] { p.Rect.X, p.Rect.Y, p.Rect.W, p.Rect.H, (int)p.Use, (int)p.Dwelling, (int)p.Facing, (int)p.Wealth, p.Beds })],
            PBlueprint = [.. s.Projects.Select(p => p.Blueprint)],
            PMaterials = [.. s.Projects.SelectMany(p => new long[] { p.MaterialG.Length }.Concat(p.MaterialG))],
            PTiles = [.. s.Projects.SelectMany(p => new[] { p.Tiles.Length }.Concat(p.Tiles))],
            PBusiness = [.. s.Projects.SelectMany(p => new[] { p.BusinessKind, p.Owner })],
            LotInts = [.. s.Lots.SelectMany(l => new[] { l.Settlement, l.Lot, l.NewLot ? 1 : 0, (int)l.Use, (int)l.Dwelling, l.Rect.X, l.Rect.Y, l.Rect.W, l.Rect.H, (int)l.Facing, (int)l.Wealth, l.Beds, l.Day })],
            LotBlueprint = [.. s.Lots.Select(l => l.Blueprint)],
            ParInts = [.. s.Parcels.SelectMany(p => new[] { p.Settlement, (int)p.Owner.Kind, p.Owner.FiefIndex, p.Owner.LotIndex, (int)p.Slot, p.Day, p.Tiles.Length }.Concat(p.Tiles))],
            ParFertility = [.. s.Parcels.SelectMany(p => p.Fertility)],
            UnmetKey = [.. s.Unmet.Keys], UnmetVal = [.. s.Unmet.Values],
            YearVals = [.. s.Years.SelectMany(y => new long[] { y.Year, y.CottagesStarted, y.CottagesBuilt, y.VacantRefitted, y.AcresCleared, y.ClearingsStarted, y.BusinessesOpened, y.PlotsGiven, y.WagesMites, y.MaterialsMites, y.MovedToTown })],
            Cleared = [.. s.ClearedTiles],
        };
        return o;
    }

    public GrowthState Restore()
    {
        var s = new GrowthState { NextSerial = NextSerial, FertilityScalePermille = FertilityScale };
        if (Totals.Length == 4) { s.WoodDrawnG = Totals[0]; s.FirewoodMadeG = Totals[1]; s.LogsMadeG = Totals[2]; s.PolesGatheredG = Totals[3]; }
        int mi = 0, ti = 0;
        for (int i = 0; i < PSerial.Length; i++)
        {
            var p = new BuildProject
            {
                Serial = PSerial[i], Kind = (ProjectKind)PKind[i], Settlement = PSettlement[i], StartDay = PStartDay[i], Payer = PPayer[i], PayerFief = PPayerFief[i],
                HoursNeeded = PHours[i * 4], HoursDone = PHours[i * 4 + 1], PaidMites = PHours[i * 4 + 2], StalledSeasons = (int)PHours[i * 4 + 3], Lot = PLot[i],
                Rect = new RectM(PRect[i * 9], PRect[i * 9 + 1], PRect[i * 9 + 2], PRect[i * 9 + 3]), Use = (LotUse)PRect[i * 9 + 4], Dwelling = (DwellingType)PRect[i * 9 + 5],
                Facing = (byte)PRect[i * 9 + 6], Wealth = (byte)PRect[i * 9 + 7], Beds = PRect[i * 9 + 8], Blueprint = PBlueprint[i], BusinessKind = PBusiness[i * 2], Owner = PBusiness[i * 2 + 1],
            };
            int nm = (int)PMaterials[mi++]; p.MaterialG = PMaterials.AsSpan(mi, nm).ToArray(); mi += nm;
            int nt = PTiles[ti++]; p.Tiles = PTiles.AsSpan(ti, nt).ToArray(); ti += nt;
            s.Projects.Add(p);
        }
        for (int i = 0; i * 13 < LotInts.Length; i++)
        {
            var v = LotInts.AsSpan(i * 13, 13);
            s.Lots.Add(new LotRecord(v[0], v[1], v[2] != 0, (LotUse)v[3], (DwellingType)v[4], LotBlueprint[i], new RectM(v[5], v[6], v[7], v[8]), (byte)v[9], (byte)v[10], v[11], v[12]));
        }
        int pi = 0, fi = 0;
        while (pi < ParInts.Length)
        {
            int n = ParInts[pi + 6];
            s.Parcels.Add(new ParcelRecord(ParInts[pi], new ParcelOwner((OwnerKind)ParInts[pi + 1], ParInts[pi + 2], ParInts[pi + 3]), (byte)ParInts[pi + 4], ParInts.AsSpan(pi + 7, n).ToArray(), ParFertility.AsSpan(fi, n).ToArray(), ParInts[pi + 5]));
            pi += 7 + n; fi += n;
        }
        for (int i = 0; i < UnmetKey.Length; i++) s.Unmet[UnmetKey[i]] = UnmetVal[i];
        for (int i = 0; i * 11 < YearVals.Length; i++)
        {
            var v = YearVals.AsSpan(i * 11, 11);
            s.Years.Add(new GrowthYear { Year = (int)v[0], CottagesStarted = (int)v[1], CottagesBuilt = (int)v[2], VacantRefitted = (int)v[3], AcresCleared = (int)v[4], ClearingsStarted = (int)v[5], BusinessesOpened = (int)v[6], PlotsGiven = (int)v[7], WagesMites = v[8], MaterialsMites = v[9], MovedToTown = (int)v[10] });
        }
        s.ClearedTiles.AddRange(Cleared);
        return s;
    }
}
