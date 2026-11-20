using Godot;
using Ravensguard.Sim.Holdings;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Game.World;

public partial class StreetView
{
    /// <summary>
    /// Dev (P6-D4, the screenshot driver only): the field nearest the hero becomes his household's (a lease through the land's own hook) with poor soil and the hero is set down beside it, a marl pit near
    /// for the Q menu's verbs, and a marl job begun on the next field so the job rows show. Returns what happened, for the log.
    /// </summary>
    public string DebugSoil()
    {
        if (Runtime is not { } rt) return "no runtime";
        var (hx, hy) = HeroRegionMm();
        var res = rt.AtBarrier(() =>
        {
            var sw = SoilWorld.For(rt.State); var lw = LandOf(rt); var st = rt.State;
            if (sw == null || lw == null || st.Party == null) return ((int, int)?)null;
            var F = sw.Farming.State.Fields; var reg = lw.Region; int N = Ravensguard.Sim.WorldGen.Region.Region.Size, T = Ravensguard.Sim.WorldGen.Region.Region.TileM;
            int best = -1; long bd = long.MaxValue;
            for (int f = 0; f < F.Count; f++)
            {
                int t = F.Tile[f]; long dx = t % N * T + T / 2 - hx / 1000, dy = t / N * T + T / 2 - hy / 1000, d2 = dx * dx + dy * dy;
                if (d2 < bd) { bd = d2; best = f; }
            }
            if (best < 0) return null;
            int parcel = F.Parcel[best]; var pc = reg.Parcels[parcel];
            lw.ParcelLet?.Invoke(parcel, st.Party.Hero, true); sw.Farming.SyncPopulation();
            sw.HasMarlPit = _ => true;
            for (int k = 0; k < pc.FieldCount; k++) F.Fertility[pc.FieldStart + k] = 38;
            if (pc.FieldCount > 1) sw.StartOwn(st.Party.Hero, pc.FieldStart + 1, SoilKind.Marl, (int)(rt.HourDone / 24));
            int tile = F.Tile[pc.FieldStart]; return (tile % N * T + T / 2, tile / N * T + T / 2);
        }).GetAwaiter().GetResult();
        if (res is { } at) { DebugPlaceHero(at.Item1 - 20, at.Item2); return $"soil field at {at.Item1},{at.Item2}"; }
        return "no field near";
    }
}
