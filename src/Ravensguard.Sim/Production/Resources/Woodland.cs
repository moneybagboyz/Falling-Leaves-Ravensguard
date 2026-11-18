using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Production;

/// <summary>
/// The woodland a settlement's households gather fuel in and its woodwards cut for the market (review finding 4; integration decisions I22, I38). Fuel is no longer
/// created from nothing: every gram comes off a wood row of the <see cref="ResourceLayer"/> within <see cref="EconomyTuningDef.GatherRadiusM"/> of the settlement
/// (nearest rows first, never below the extraction floor), and it takes labour: a household gathers at
/// <see cref="EconomyTuningDef.GatherFuelGPerWorkerHour"/> with the hours its members can spare (an adult without work 4, an adult with work or a farm 1, a child of 8 to
/// 15 one), a woodward cuts at the firewood extraction's rate with the hours of its idle adults. The rows regrow each season (resources spec 3.3). The settlements'
/// row lists are a pure function of the region and the layer (built once); the stock lives in the layer, which is saved with production. Created into the store with
/// reason Cut, so the mass books record it.
/// </summary>
public sealed class Woodland : IFuelGatherer
{
    private readonly SimWorld _w;
    private readonly ResourceLayer _layer;
    private readonly int[][] _rows;
    private readonly long _cutGPerHour;

    /// <summary>Running totals (diagnostics, not saved): fuel gathered by households and cut by woodwards, the worker-hours they took, and what they wanted and could not get (no wood within reach or no hands).</summary>
    public long GatheredG, CutG, GatherHours, CutHours, ShortOfWoodG, ShortOfHandsG;

    public Woodland(SimWorld w, ResourceLayer layer, ExtractionKind? firewoodCutting)
    {
        _w = w; _layer = layer;
        var t = w.Economy.Tuning; var reg = w.Population.Region;
        _cutGPerHour = firewoodCutting == null ? 70_000 : firewoodCutting.RateGPerWorkerDay / ProductionSystem.WorkdayHours;
        _rows = new int[reg.Settlements.Count][];
        int r = Math.Max(0, t.GatherRadiusM) / RegionData.TileM; long r2 = (long)t.GatherRadiusM * t.GatherRadiusM;
        for (int s = 0; s < _rows.Length; s++)
        {
            int tx = reg.Settlements[s].CenterX / RegionData.TileM, ty = reg.Settlements[s].CenterY / RegionData.TileM;
            var list = new List<(long D2, int Row)>();
            for (int y = Math.Max(0, ty - r); y <= Math.Min(RegionData.Size - 1, ty + r); y++)
                for (int x = Math.Max(0, tx - r); x <= Math.Min(RegionData.Size - 1, tx + r); x++)
                {
                    long dx = (x - tx) * (long)RegionData.TileM, dy = (y - ty) * (long)RegionData.TileM, d2 = dx * dx + dy * dy;
                    if (d2 > r2) continue;
                    int row = layer.Find(RegionData.Index(x, y), ResourceKind.Wood);
                    if (row >= 0) list.Add((d2, row));
                }
            _rows[s] = [.. list.OrderBy(v => v.D2).ThenBy(v => v.Row).Select(v => v.Row)];
        }
    }

    /// <summary>Wood rows within reach of a settlement, nearest first.</summary>
    public IReadOnlyList<int> RowsOf(int settlement) => _rows[settlement];

    /// <summary>Grams of wood a settlement's rows hold above their floor.</summary>
    public long AvailableG(int settlement)
    {
        int floor = _w.Economy.Tuning.GatherStockFloorPct; long a = 0;
        foreach (int row in _rows[settlement]) a += Math.Max(0, _layer.StockG(row) - _layer.CapG(row) * floor / 100);
        return a;
    }

    /// <summary>Takes up to <paramref name="g"/> from the settlement's rows, nearest first, never below the floor; returns what was taken.</summary>
    public long Draw(int settlement, long g, int day)
    {
        int floor = _w.Economy.Tuning.GatherStockFloorPct; long got = 0;
        foreach (int row in _rows[settlement])
        {
            if (got >= g) break;
            long over = _layer.StockG(row) - _layer.CapG(row) * floor / 100;
            if (over <= 0) continue;
            got += _layer.Take(row, Math.Min(over, g - got), day);
        }
        return got;
    }

    private static bool Idle(PersonTable p, int i) => p.Employer[i] < 0 && p.Occupation[i] is Occupation.None or Occupation.Labourer;

    /// <summary>Hours the household's members can spend gathering fuel today.</summary>
    public int GatherHoursOf(int hh)
    {
        var p = _w.People; var t = _w.Economy.Tuning; int day = (int)(_w.State.HourDone / 24); int h = 0;
        foreach (int i in _w.Households.Members(hh))
        {
            if (p.Status[i] != PersonStatus.Alive) continue;
            int age = p.AgeYears(i, day);
            if (age < 8) continue;
            h += age < 16 ? t.GatherHoursChild : Idle(p, i) && age < 60 ? t.GatherHoursIdleAdult : t.GatherHoursWorkingAdult;
        }
        return h;
    }

    /// <summary>Hours the household's idle adults (16 to 59, no post) can cut wood for the market today.</summary>
    public int WoodwardHoursOf(int hh)
    {
        var p = _w.People; int day = (int)(_w.State.HourDone / 24); int n = 0;
        foreach (int i in _w.Households.Members(hh))
            if (p.Status[i] == PersonStatus.Alive && Idle(p, i) && p.AgeYears(i, day) is >= 16 and < 60) n++;
        return n * _w.Economy.Tuning.WoodwardHoursPerIdleAdult;
    }

    /// <summary>A household gathers up to <paramref name="wantG"/> of fuel into its store (the day's burn and a small buffer): limited by its members' hours and the wood in reach.</summary>
    public long Gather(int hh, long wantG)
    {
        if (wantG <= 0) return 0;
        long rate = Math.Max(1, _w.Economy.Tuning.GatherFuelGPerWorkerHour);
        long byHands = GatherHoursOf(hh) * rate;
        long want = Math.Min(wantG, byHands); if (byHands < wantG) ShortOfHandsG += wantG - byHands;
        int s = _w.Households.Settlement[hh], day = (int)(_w.State.HourDone / 24);
        long got = Draw(s, want, day); if (got < want) ShortOfWoodG += want - got;
        if (got <= 0) return 0;
        if (!_w.Economy.HouseholdStore[hh].Create(_w.Economy.Food.Firewood, got, CreateReason.Cut)) return 0;
        GatheredG += got; GatherHours += (got + rate - 1) / rate;
        return got;
    }

    /// <summary>A woodward cuts up to <paramref name="wantG"/> of firewood for the market into its store: limited by its idle adults' hours and the wood in reach. Returns the grams cut.</summary>
    public long Cut(int hh, long wantG)
    {
        if (wantG <= 0) return 0;
        long byHands = WoodwardHoursOf(hh) * _cutGPerHour; long want = Math.Min(wantG, byHands);
        if (want <= 0) return 0;
        int s = _w.Households.Settlement[hh], day = (int)(_w.State.HourDone / 24);
        long got = Draw(s, want, day);
        if (got <= 0 || !_w.Economy.HouseholdStore[hh].Create(_w.Economy.Food.Firewood, got, CreateReason.Cut)) return 0;
        CutG += got; CutHours += (got + _cutGPerHour - 1) / _cutGPerHour;
        return got;
    }
}
