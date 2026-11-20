using System.Globalization;
using Ravensguard.Sim.Contracts;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Holdings;

/// <summary>A soil job the hero is on: his own work on a field of his household's, or a contract he took. <see cref="Percent"/> is how much is done.</summary>
public sealed record SoilRow(int Serial, int Field, int XM, int YM, SoilKind Kind, int Percent, int Fertility, int Contract);

/// <summary>A field of the hero's household's the Q menu can improve: its place (region metres), its fertility, whether a job or an improvement already stands on it, and what the place can give it.</summary>
public sealed record SoilFieldRow(int Field, int XM, int YM, int Fertility, bool Busy, bool CanMarl, bool CanManure);

public static partial class HoldingsDesk
{
    /// <summary>Q > Marl this field (<c>kind</c> 0) or Manure this field (1): the hero improves a field of his own household's by hand, no contract (holdings 3.5).</summary>
    public static HeroCommand SoilOwn(int field, SoilKind kind, long sec) => Cmd(sec, Tag("soilown", field, (int)kind));
    /// <summary>The hero works a soil job (his own, or a contract he took) from one game second to another; the last hours finish it.</summary>
    public static HeroCommand SoilWork(int serial, long fromSec, long toSec, long sec) => Cmd(sec, Tag("soilwork", serial, fromSec, toSec));

    private static HoldingsResult? Soil(HeroContext ctx, HeroCommand c, string[] p, int day)
    {
        long N(int i) => i < p.Length && long.TryParse(p[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : long.MinValue;
        int I(int i) { long v = N(i); return v is >= int.MinValue and <= int.MaxValue ? (int)v : int.MinValue; }
        if (p[0] is not ("soilown" or "soilwork")) return null;
        var sw = SoilWorld.For(ctx.State); var hw = HoldingsWiring.For(ctx.State);
        if (sw == null || hw == null) return HoldingsResult.No("hw_soil_no_job");
        var hero = ctx.Party.Hero;
        if (p[0] == "soilown" && p.Length >= 3 && I(2) is 0 or 1) return sw.StartOwn(hero, I(1), (SoilKind)I(2), day);
        if (p[0] != "soilwork" || p.Length < 4) return null;
        var lim = HeavyWork.Begin(ctx, hw.Book, N(2), N(3), null);
        if (lim.Why != null) return HoldingsResult.No(lim.Why);
        long milli = (lim.UntilSec - N(2)) * 1000 / 3600;
        var r = sw.HeroWork(hero, I(1), milli, day, out long worked);
        if (r.Ok) HeavyWork.End(hw.Book, worked * 3600 / 1000);
        return r;
    }

    /// <summary>The hero's soil jobs and his fields as the Q menu reads them (reads only).</summary>
    public static (IReadOnlyList<SoilRow> Jobs, IReadOnlyList<SoilFieldRow> Fields) SoilView(SoilWorld sw, EntityId hero, int heroHousehold, int day)
    {
        int N = RegionData.Size, h = RegionData.TileM; var F = sw.Farming.State.Fields; var r = sw.Land.Region;
        (int X, int Y) At(int f) { int t = F.Tile[f]; return (t % N * h + h / 2, t / N * h + h / 2); }
        var jobs = new List<SoilRow>(); var cb = sw.St.Contracts;
        foreach (var w in sw.Book.Works)
        {
            if (w.State is not (SoilState.Open or SoilState.Working) || w.Field >= F.Count) continue;
            bool mine = w.Contract == 0 ? w.Issuer == hero : cb?.Get(w.Contract) is { State: Ravensguard.Sim.Contracts.WorkState.Taken } c && c.Taker == hero;
            if (!mine) continue;
            var (x, y) = At(w.Field);
            jobs.Add(new SoilRow(w.Serial, w.Field, x, y, w.Kind, (int)(w.DoneMilli * 100 / Math.Max(1, w.NeedMilli)), F.Fertility[w.Field], w.Contract));
        }
        var fields = new List<SoilFieldRow>();
        var pop = sw.Land.Pop;
        if (heroHousehold >= 0)
            for (int p = 0; p < r.Parcels.Count && p < pop.ParcelHolder.Length; p++)
            {
                if (pop.ParcelHolder[p] != heroHousehold) continue;
                var pc = r.Parcels[p];
                for (int k = 0; k < pc.FieldCount; k++)
                {
                    int f = pc.FieldStart + k; if (f >= F.Count) break;
                    var (x, y) = At(f);
                    fields.Add(new SoilFieldRow(f, x, y, F.Fertility[f], sw.Book.Live(f), sw.HasMarlPit?.Invoke(pc.Settlement) == true, sw.DungLeftKg(pc.Settlement, day) >= sw.Rules.ManureKg));
                }
            }
        return (jobs, fields);
    }
}
