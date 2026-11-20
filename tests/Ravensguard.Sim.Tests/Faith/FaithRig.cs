using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Faith;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Abstract;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Tests.Faith;

/// <summary>A new game of seed 1234's embark region with its church seeded (P6-B4), as the faith tests start from; each rig is its own world, so a test may change it.</summary>
public sealed class FaithRig : IDisposable
{
    public SeamWorld W { get; }
    public WorldState State { get; }
    public WorldRuntime Runtime { get; }
    public ChurchBook Church => State.Church!;
    public ContentDatabase Content => W.Content;
    public HeroContext Ctx { get; }
    public HeroDesk Desk { get; } = new();

    public FaithRig(bool runtime = true)
    {
        W = SeamWorld.Get(1234);
        var home = W.BlockOrigin.Offset(1, 1);
        var dir = Ravensguard.Sim.WorldGen.ContinentSettings.FindBaseContent()!;
        var cs = Ravensguard.Sim.WorldGen.ContinentSettings.LoadDefaults(dir); cs.Seed = 1234;
        var hs = Ravensguard.Sim.WorldGen.History.HistorySettings.LoadDefaults(dir); hs.Seed = 1234;
        var region = RegionGenerator.Generate(W.Continent, W.History, home, W.Settings);
        LandmarkPlacer.Place(region, W.History, W.Content);
        var pop = Populator.Populate(region, W.History, W.Content);
        var h = HeroRecord.FromOrigin("Aldric Vane", false, W.Content.All<OriginDef>().OrderBy(o => o.Order).First());
        State = NewGameWorld.Create(W.Content, W.Continent, W.History, cs, hs, W.Settings, home.CellX, home.CellY, pop, h, null);
        State.Content = W.Content;
        if (runtime) Runtime = new WorldRuntime(State, W.Content, new MemoryRegionPager());
        else Runtime = null!;
        Ctx = new HeroContext(State, State.Party!, W.Content);
    }

    public void Dispose() => Runtime?.Dispose();

    public RegionData Region => State.Population.Region;
    public PersonTable People => State.Population.People;
    public SimWorld Sim => State.HomeRecords!.Sim;
    public Ravensguard.Sim.Abstract.EconomyWorld Econ => State.HomeRecords!.Economy;
    public EntityId Hero => State.Party!.Hero;
    public ReligionBook Religions => FaithWiring.ReligionsOf(State);

    /// <summary>Issues a hero command at a second and applies it at once, as the barrier after that hour does.</summary>
    public HeroResult Do(HeroCommand c, long sec)
    {
        var cmd = Desk.Issue(c with { IssuedSec = sec });
        Desk.ApplyDue(Ctx, cmd.Hour);
        return Desk.View!.Results.Last(r => r.Seq == cmd.Seq);
    }

    /// <summary>The hero's own settlement and religion.</summary>
    public (int Settlement, int Religion) HeroFaith()
    {
        int row = Ravensguard.Sim.Succession.HeroPerson.RowOf(People, State.Hero, Hero);
        return (People.HomeSettlement[row], Religions.OfCulture(People.Culture[row]));
    }
}
