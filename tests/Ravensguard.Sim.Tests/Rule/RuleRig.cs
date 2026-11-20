using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Tests.Land;
using Ravensguard.Sim.Tests.Production;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Tests.Rule;

/// <summary>
/// A private headless world of seed 1234 (its region, people and economy: the rule tests change them) with the rule book derived from its lords as the seeding pass derives it, the land installed (as the runtime
/// installs it) and the rule's links wired to recorders, so a test drives the passes by hand with real purses and the real ledger.
/// </summary>
internal sealed class RuleRig : IDisposable
{
    public required SoakWorld W { get; init; }
    public required RuleBook B { get; init; }
    public required LandWorld L { get; init; }
    public List<SkimCrime> Crimes { get; } = [];
    public int Day { get; set; }
    public SimWorld Sim => W.Sim;
    public EconomyState Econ => W.Econ;
    public Ledger Led => W.Econ.Ledger;
    public RegionData Region => W.Pop.Region;
    public PersonTable People => W.Pop.People;
    public Chronicle Chronicle => W.Sim.Chronicle;

    public static RuleRig Make(bool ticker = false)
    {
        var w = SoakWorld.Create(withTicker: ticker);
        var lw = new LandWorld(new LandBook(), w.Pop, w.Econ, EconFixture.Content, () => w.Economy.Growth.State, null, w.Markets);
        w.Sim.Set(lw);
        w.Economy.Growth.LandHolds = lw.HoldsLot;
        w.Economy.Farming.LeasedOut = lw.IsLeased;
        var book = RuleSeeding.Build(w.Pop, EconFixture.Content, SoakWorld.Seed);
        var rig = new RuleRig { W = w, B = book, L = lw, Day = (int)((w.Sim.State.HourDone + 1) / 24) };
        var links = RuleWiring.Install(w.Sim, w.Economy, book, w.Pop.Region);
        links.Crime = sc => { rig.Crimes.Add(sc); return EntityId.None; };
        return rig;
    }

    /// <summary>The rule's context with the chronicle going straight to the sim's chronicle.</summary>
    public RuleCtx C => RuleWiring.Context(W.Sim, B, RuleChron.Of(W.Sim.Chronicle, W.Sim.Ids));

    /// <summary>Puts coin into a party's purse (a seed mint, so the audit's sourced side grows with it).</summary>
    public void Fund(RuleCtx c, Party p, long mites)
    {
        var pr = c.PurseOf(p); if (pr.IsNone) throw new InvalidOperationException("no purse for " + p);
        Led.Mint(pr, new Coin(mites), SourceReason.Seed);
    }

    /// <summary>Empties a party's purse into a test chest (the coin stays in the audit).</summary>
    public void Drain(RuleCtx c, Party p)
    {
        var pr = c.PurseOf(p); long bal = Led.Balance(pr).Mites;
        if (bal > 0) Led.Transfer(pr, Led.Open(PurseKind.Chest, EntityId.Make(IdKind.Other, 999_999)), new Coin(bal), TransferReason.Gift);
    }

    /// <summary>The first fief (lowest index) with a living holder, a realm and at least <paramref name="households"/> households.</summary>
    public FiefRow Fief(int households = 1, bool realm = true) => B.Fiefs.Values.First(f => f.Holder >= 0 && (!realm || f.Realm >= 0) && C.HouseholdsOfFief(f.Fief).Count() >= households);

    public long Total() => Led.Audit().InPurses.Mites;

    public void Dispose() => W.Dispose();
}
