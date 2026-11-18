using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Codex;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Worlds;

/// <summary>
/// The state of a new game: the embark region's people with the whole economy over them (markets, businesses, resources, farming, the abstract tick's own state), built the
/// same way the headless soak builds its world (<see cref="Abstract.HeadlessWorld"/>), so a new game saves and ticks exactly what the soak measures (seamless-regions N21).
/// </summary>
public static class NewGameWorld
{
    /// <summary>
    /// <paramref name="pop"/> is the populated embark region (landmarks already placed). The hero's crowns enter the ledger as seed money. The returned state carries
    /// <see cref="WorldState.HomeRecords"/>, which <see cref="WorldRuntime"/> adopts as it is; its clock is <paramref name="clock"/> (default: 08:00 on the first day of spring of the year history ended).
    /// </summary>
    public static WorldState Create(ContentDatabase content, Continent continent, WorldHistory history, ContinentSettings cs, HistorySettings hs, RegionSettings rs,
        int cellX, int cellY, Population pop, HeroRecord? hero, CodexProgress? codex, GameTime? clock = null)
    {
        var home = RegionCoord.Snap(cellX, cellY);
        var sv = new RegionServices(cs.Seed, content, continent, home) { History = history };
        var rw = RegionWorld.CreateFresh(sv, home, pop);
        if (hero != null) rw.Sim.Economy.SeedHero(hero.Coin);
        return new WorldState
        {
            Hero = hero, Codex = codex, Content = content, HomeRecords = rw,
            Economy = rw.Sim.Economy, Farming = rw.Economy.Farming.State, Production = rw.Economy.Production, Markets = rw.Economy.Markets, Abstract = rw.Sim.State,
            ContinentSettings = cs, HistorySettings = hs, RegionSettings = rs, EmbarkCellX = cellX, EmbarkCellY = cellY,
            Continent = continent, History = history, Population = pop, Clock = new SimClock(clock ?? GameTime.FromCalendar(pop.NowYear, Season.Spring, 1, 8)),
            Ids = rw.Ids, Chunks = new ChunkStore(), Chronicle = rw.Chronicle,
        };
    }
}
