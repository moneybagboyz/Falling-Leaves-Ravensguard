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
        int cellX, int cellY, Population pop, HeroRecord? hero, CodexProgress? codex, GameTime? clock = null, int startSettlement = -1, Difficulty? difficulty = null)
    {
        var home = RegionCoord.Snap(cellX, cellY);
        var sv = new RegionServices(cs.Seed, content, continent, home) { History = history };
        // P8-LM-2: the unique NPCs this region fits, put into the populated region after generation (no generation hash moves); the economy opens over them
        Ravensguard.Sim.People.UniqueNpcs.Place(pop, history, content, cs.Seed, hero?.Origin ?? "", null, [.. continent.Cultures.Select(c => c.Name)]);
        var rw = RegionWorld.CreateFresh(sv, home, pop);
        if (hero != null) rw.Sim.Economy.SeedHero((difficulty ?? Difficulty.Default).ScaleOriginCoin(hero.Coin));
        var cont = sv.AbstractContinent!;                          // made by the home region itself (RegionServices.EnsureContinent), over this world's history
        var start = clock ?? GameTime.FromCalendar(pop.NowYear, Season.Spring, 1, 8);
        Ravensguard.Sim.Succession.SuccessionBook? kin = null;
        if (hero != null)
        {
            // P5-A4 (inheritance 3): the hero is a row of the person table, in a household of the hero's own, with the origin's kin (stream 0x560) or, by default, the Phase 4 family
            var links = new List<Ravensguard.Sim.Succession.HeroKinLink>();
            hero.PersonIndex = Ravensguard.Sim.Succession.HeroPerson.Insert(pop, rw.Sim.Economy, content, hero, Ravensguard.Sim.Economy.EconomyState.HeroOwner, cs.Seed, (int)start.TotalDays,
                startSettlement, links, rw.Sim, rw.Ids);
            if (links.Count > 0) { kin = new Ravensguard.Sim.Succession.SuccessionBook(); kin.Kin.AddRange(links); }
        }
        var state = new WorldState
        {
            Succession = kin,
            Hero = hero, Codex = codex, Content = content, HomeRecords = rw,
            Party = hero != null ? Ravensguard.Sim.Trade.HeroParty.Create(rw.Sim.Economy, content, rw.Ids, hero, (clock ?? GameTime.FromCalendar(pop.NowYear, Season.Spring, 1, 8)).Seconds) : null, // the kit as item rows in the world (trade spec 3.2)
            Economy = rw.Sim.Economy, Farming = rw.Economy.Farming.State, Production = rw.Economy.Production, Markets = rw.Economy.Markets, Growth = rw.Economy.Growth.State, Abstract = rw.Sim.State,
            ContinentSettings = cs, HistorySettings = hs, RegionSettings = rs, EmbarkCellX = cellX, EmbarkCellY = cellY,
            Continent = continent, History = history, Population = pop, Clock = new SimClock(clock ?? GameTime.FromCalendar(pop.NowYear, Season.Spring, 1, 8)),
            Ids = rw.Ids, Chunks = new ChunkStore(), Chronicle = rw.Chronicle, ContinentState = cont,
        };
        if (difficulty != null) state.Difficulty = difficulty;            // after the economy is set: it hands the multipliers on
        Phase6Seeding.Run(state, content, null);                    // P6-A2 (overview 7): the seeding pass after the population and difficulty
        Phase7Seeding.Run(state, content, null);                    // P7-0A (overview 7): the Phase 7 pass, appended to the Phase 6 one (no steps registered yet)
        return state;
    }
}
