using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Weather;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.Chunks;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Burning;

/// <summary>
/// The fire's place in the abstract tick (fire 4.9): every hour (H1) the abstract building fires burn an hour and spread, a storm hour may strike a thatched roof, and a live fire the live tier
/// has not stepped for a game hour is handed back to the abstract tick; at hour 0 (H5) the day's hearth accidents are rolled. A no-op without a <see cref="FireWorld"/>, and free while nothing burns.
/// </summary>
[AbstractSystem]
public sealed class FireSystem : AbstractSystem
{
    public FireSystem() : base("fire", TickPhase.H1Caravans | TickPhase.H5DailyGlobals, SystemOrder.Caravans + 50) { }

    public override void Step(TickContext ctx)
    {
        var fw = ctx.World.Get<FireWorld>(); if (fw == null) return;
        var prev = fw.Chronicle;
        fw.Chronicle = (when, type, key, ps, subjects, causes, data) => ctx.Outbox.Chronicle(when, type, key, ps, subjects, causes, data);
        try
        {
            if (ctx.Phase == TickPhase.H5DailyGlobals) { if (ctx.HourOfDay == 0) fw.DayAccidents(ctx.Day, ctx.Season == Season.Winter); return; }
            long now = ctx.Hour * 3600;
            if (fw.Book.Cells.Count > 0 && now - fw.Book.SecDone * FireClock.GameSecPerSimS > 3600) fw.Retire(ctx.Hour);
            fw.StormHour(ctx.Hour);
            fw.HourStep(ctx.Hour);
        }
        finally { fw.Chronicle = prev; }
    }
}

/// <summary>Installs fire on the home region (P6-B5 hook in <see cref="WorldRuntime"/>): the world state's book (the "fire" section), the world in the sim's services, its hooks to construction, weather, wounds and the chunk store.</summary>
public static class FireWiring
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ContentDatabase, FuelTable> Tables = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ContentDatabase, BodyPlanV2> Humans = new();

    public static FuelTable TableOf(ContentDatabase db) => Tables.GetValue(db, d => new FuelTable(d));

    public static FireWorld Attach(RegionWorld home, WorldState state, ContentDatabase content, RegionWeather? weather)
    {
        var cw = home.Sim.Get<ConstructionWorld>();
        var fuels = TableOf(content);
        var rooms = cw?.Rooms ?? BuildingLibrary.FromDatabase(content).NewWorld();
        var book = state.Fire ??= new FireBook();
        var fw = new FireWorld(book, rooms, fuels, state.Seed);
        Hook(fw, cw, state, content, weather, home);
        home.Sim.Set(fw);
        return fw;
    }

    public static void Hook(FireWorld fw, ConstructionWorld? cw, WorldState state, ContentDatabase content, RegionWeather? weather, RegionWorld? home)
    {
        if (cw != null)
        {
            fw.Destroy = (x, y, z, fixture, burnt, sec, cause) => cw.FireDestroy(x, y, z, fixture, burnt, fw.Fuels.BurnableGoodClasses, sec, cause);
            fw.Tiles = cw.Tiles;
            fw.StoresAt = (x, y, z) => StoresOn(cw, x, y, z);
        }
        fw.Wounds = state.Wounds;
        fw.PlanOf = _ => Humans.GetValue(content, d => new CombatTables(d).V2.Plan("human"));
        if (weather != null)
        {
            int cell = (state.EmbarkCellY + 8) * Continent.Size + state.EmbarkCellX + 8;
            fw.Weather = hour =>
            {
                var w = weather.AtLite(cell, hour);
                int rain = w.PrecipIsSnow || w.PrecipMmH.Raw <= 0 ? 0 : Math.Max(1, (int)((long)w.PrecipMmH.Raw >> Fixed.FracBits));
                long day = hour / 24; int doy = (int)(((day % 60) + 60) % 60) + 1; int year = (int)(day / 60);
                var lc = weather.Fronts.Long.For(weather.Continent.CultureId[cell], year);
                return new FireWeather((int)((long)w.WindMps.Raw * 1000 >> Fixed.FracBits), w.WindFromDeg, rain, lc.Kind == LongKind.Drought && lc.Covers(doy), w.State == WeatherState.Thunderstorm);
            };
        }
        var region = state.Population.Region;
        fw.IsTown = (x, y) =>
        {
            RegionSettlement? best = null; long bd = long.MaxValue;
            foreach (var s in region.Settlements) { long d = (long)(s.CenterX - x) * (s.CenterX - x) + (long)(s.CenterY - y) * (s.CenterY - y); if (d < bd) { bd = d; best = s; } }
            return best != null && bd <= 200L * 200 && best.Class == SettlementClass.Town;
        };
        var pal = new PiecePalette(PieceCosts.Of(content).Library.Pieces);
        if (state.Chunks != null)
            fw.Adopt = (x0, y0, x1, y1) => Scan(state.Chunks, pal, x0, y0, x1, y1);
    }

    private static IEnumerable<Ravensguard.Sim.Goods.Store> StoresOn(ConstructionWorld cw, int x, int y, int z)
    {
        foreach (var p in cw.Book.Placed.Values)
            if (p.X == x && p.Y == y && p.Z == z && cw.Econ.OtherStores.TryGetValue(p.Owner, out var st)) yield return st;
    }

    /// <summary>The generated pieces of the chunk store inside a box (every visited chunk), as cell edits: structure and fixture at each tile that holds one, levels -2 to 3.</summary>
    public static IEnumerable<CellEdit> Scan(ChunkStore store, PiecePalette pal, int x0, int y0, int x1, int y1)
    {
        for (int cy = ChunkStreamer.FloorDiv(y0, ChunkContent.Size); cy <= ChunkStreamer.FloorDiv(y1, ChunkContent.Size); cy++)
            for (int cx = ChunkStreamer.FloorDiv(x0, ChunkContent.Size); cx <= ChunkStreamer.FloorDiv(x1, ChunkContent.Size); cx++)
            {
                if (store.GetSnapshot(new ChunkKey(cx, cy, 0)) == null) continue;
                for (int z = -2; z <= 3; z++)
                {
                    if (z < ChunkKey.MinZ || z > ChunkKey.MaxZ) continue;
                    var c = store.Rebuild(new ChunkKey(cx, cy, z));
                    if (c == null) continue;
                    foreach (var (idx, code) in c.Tiles.OrderBy(kv => kv.Key))
                    {
                        int x = cx * ChunkContent.Size + idx % ChunkContent.Size, y = cy * ChunkContent.Size + idx / ChunkContent.Size;
                        if (x < x0 || x > x1 || y < y0 || y > y1) continue;
                        string? s = pal.Id(TileCode.Structure(code)), f = pal.Id(TileCode.Fixture(code));
                        if (s != null) yield return new CellEdit(x, y, z, CellLayer.Structure, s);
                        if (f != null) yield return new CellEdit(x, y, z, CellLayer.Fixture, f);
                    }
                }
            }
    }
}
