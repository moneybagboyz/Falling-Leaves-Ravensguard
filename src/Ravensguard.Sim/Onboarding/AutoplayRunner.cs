using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Onboarding;

/// <summary>
/// The <c>--autoplay</c> run (onboarding spec 7, O-4): for each seed, one world; for each origin, a fresh new game over it (its own region and people, its own economy), played through the first hour by <see cref="FirstHourBot"/>.
/// Nothing is shared between two origins but the continent and its history, so one origin's coin cannot help another's. The tool prints each timeline and a summary line; the caller turns failures into the exit code.
/// </summary>
public static class AutoplayRunner
{
    public static List<BotReport> Run(ContentDatabase db, string contentDir, IEnumerable<ulong> seeds, string? onlyOrigin, Action<string> print)
    {
        var all = new List<BotReport>();
        var origins = db.All<OriginDef>().OrderBy(o => o.Order).ThenBy(o => o.Id, StringComparer.Ordinal).Where(o => onlyOrigin == null || o.Id == onlyOrigin).ToList();
        foreach (var seed in seeds)
        {
            var cs = ContinentSettings.LoadDefaults(contentDir); cs.Seed = seed;
            var hs = HistorySettings.LoadDefaults(contentDir); hs.Seed = seed;
            var rs = RegionSettings.LoadDefaults(contentDir); rs.Seed = seed;
            var continent = ContinentGenerator.Generate(cs);
            var history = HistoryGenerator.Generate(continent, hs);
            var pick = EmbarkSite.AutoPick(continent, history);
            foreach (var o in origins)
            {
                var region = RegionGenerator.Generate(continent, history, pick.CellX, pick.CellY, rs);
                LandmarkPlacer.Place(region, history, db);
                var pop = Populator.Populate(region, history, db);
                var hw = HeadlessWorld.FromPopulation(seed, pop, db, continent, history, cs, hs, rs, pick.CellX, pick.CellY);
                var rep = FirstHourBot.Play(hw, db, o, seed);
                print(rep.Render());
                all.Add(rep);
            }
        }
        print(FirstHourBot.Summary(all));
        return all;
    }
}
