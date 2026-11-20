using Ravensguard.Content;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Crafting;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Jobs;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.World;

namespace Ravensguard.Sim.Tests.Construction;

/// <summary>
/// A construction world over its own economy (seed 1234's people, shared and read only; the economy fresh), with the hooks a test needs: skills by a table, the training it records,
/// the weather it is told, people on cells it is told. Day 10 at 06:00 is the usual start.
/// </summary>
internal sealed class ConstructionRig
{
    public required EconomyState Econ { get; init; }
    public required ConstructionWorld W { get; init; }
    public ContentDatabase Content => EconFixture.Content;
    public EntityId Hero => EconomyState.HeroOwner;
    public PurseId HeroPurse { get; init; }
    public Dictionary<(ulong, Skill), int> Skills { get; } = [];
    public List<(EntityId P, Skill S, long Sec)> Trained { get; } = [];
    public (int Rain, int Temp) Wx = (0, 12);
    public List<(EntityId P, int X, int Y, int Z)> Standing { get; } = [];
    public List<(EntityId P, int Mm, ulong Ev, long Sec)> Falls { get; } = [];
    public List<(string Type, long Sec)> Chron { get; } = [];
    public JobTable Jobs { get; } = new();
    public JobEnv Env { get; init; } = null!;

    public const long Day10 = 10L * 86400 + 6 * 3600;

    public static ConstructionRig Make(int crowns = 1000)
    {
        var econ = EconFixture.NewEconomy(goods: false);
        var purse = econ.SeedHero(crowns);
        var costs = PieceCosts.Of(EconFixture.Content);
        var w = new ConstructionWorld(new ConstructionBook(), econ, costs) { Pop = EconFixture.Pop, Content = EconFixture.Content };
        var env = new JobEnv { Ledger = econ.Ledger, Econ = econ, Pop = EconFixture.Pop, Tuning = Ravensguard.Sim.Behaviour.BehaviourContent.From(EconFixture.Content).OddJobs };
        var rig = new ConstructionRig { Econ = econ, W = w, HeroPurse = purse, Env = env };
        w.SkillOf = (p, s) => rig.Skills.TryGetValue((p.Value, s), out int v) ? v : 0;
        w.Train = (p, s, dx, diff, sec) => rig.Trained.Add((p, s, sec));
        w.Weather = _ => (rig.Wx.Rain, rig.Wx.Temp);
        w.PeopleOn = cells => rig.Standing.Where(s => cells.Contains(ConstructionIds.CellKey(s.X, s.Y, s.Z))).ToList();
        w.Fall = (p, mm, ev, sec) => rig.Falls.Add((p, mm, ev, sec));
        w.Jobs = () => (env, rig.Jobs);
        w.Chronicle = (when, type, subjects, causes, data) => { rig.Chron.Add((type, when.Seconds)); return EntityId.None; };
        w.PayeeOf = p => econ.Ledger.TryFind(PurseKind.Person, p, out var pp) ? pp : econ.Ledger.Open(PurseKind.Person, p);
        return rig;
    }

    public ushort G(string good) => Econ.Goods.IdOf(good);

    /// <summary>Puts grams of goods straight into a site's pile (created as an import: the test's faucet).</summary>
    public void Stock(EntityId site, params (string Good, long Grams)[] goods)
    {
        var pile = W.PileOf(W.Site(site)!);
        foreach (var (g, q) in goods) pile.Create(G(g), q, CreateReason.Import);
        W.Refresh(W.Site(site)!);
    }

    /// <summary>Everything the site's ghosts still need, into the pile.</summary>
    public void StockAll(EntityId site)
    {
        var need = W.Needed(W.Site(site)!);
        var pile = W.PileOf(W.Site(site)!);
        foreach (var (g, q) in need) pile.Create(g, q, CreateReason.Import);
        W.Refresh(W.Site(site)!);
    }

    /// <summary>A follower crew of <paramref name="n"/> (no site wage), with the given skill in Carpentry and Masonry.</summary>
    public List<EntityId> Crew(EntityId site, int n, int skill = 50)
    {
        var ids = new List<EntityId>();
        for (int i = 0; i < n; i++)
        {
            var p = EntityId.Make(IdKind.Person, 9_000_000UL + (ulong)i + (ulong)W.Book.Sites.Count * 100);
            Skills[(p.Value, Skill.Carpentry)] = skill; Skills[(p.Value, Skill.Masonry)] = skill;
            Assert.True(W.AddWorker(site, p, -1, CrewClass.Skilled, 0).Ok);
            ids.Add(p);
        }
        return ids;
    }

    /// <summary>Every good: what all stores hold equals what the mass ledger says was made less what was destroyed (items 2.2).</summary>
    public void AssertMass()
    {
        var sums = new long[Econ.Goods.Count];
        for (int h = 0; h < Econ.HouseholdCount; h++) foreach (var (g, q) in Econ.HouseholdStore[h].Rows()) sums[g] += q;
        foreach (var s in Econ.OtherStores.Values) foreach (var (g, q) in s.Rows()) sums[g] += q;
        for (ushort g = 0; g < Econ.Goods.Count; g++) Assert.True(Econ.Mass.Net(g) == sums[g], $"{Econ.Goods.StringId(g)}: ledger {Econ.Mass.Net(g)} vs stores {sums[g]}");
        // what construction built in and took out again equals what stands built
        var built = new SortedDictionary<ushort, long>();
        foreach (var b in W.Book.Built.Values) foreach (var (good, grams) in W.Costs[b.Piece].Materials) built[G(good)] = built.GetValueOrDefault(G(good)) + grams;
        var keys = W.Book.MaterialisedG.Keys.Union(W.Book.DematerialisedG.Keys).Union(built.Keys);
        foreach (var k in keys) Assert.Equal(W.Book.MaterialisedG.GetValueOrDefault(k) - W.Book.DematerialisedG.GetValueOrDefault(k), built.GetValueOrDefault(k));
    }

    public void AssertCoin() => Assert.Equal(0, Econ.Ledger.Audit().Imbalance.Mites);

    /// <summary>A level grid of the built cells in a rectangle (local coordinates from (x0, y0)): ground open, walkable pieces open, stairs and ladders as portals (the street's rule).</summary>
    public LevelGrid Grid(int x0, int y0, int w, int h)
    {
        var g = new LevelGrid(w, h, 0, 3);
        for (int z = 0; z <= 3; z++)
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var s = W.BuiltStructure(x0 + x, y0 + y, z);
                    var f = W.BuiltAt(x0 + x, y0 + y, z, GhostSlot.Fixture) is { } fb ? W.Costs[fb.Piece] : null;
                    bool open = s == null ? z == 0 : Ravensguard.Sim.Building.PieceCatalog.IsWalkable(s.Kind) && !(f != null && Ravensguard.Sim.Building.PieceCatalog.IsSolidFixture(f.Kind));
                    g.SetOpen(x, y, z, open);
                }
        ConstructionPortals.Sync(g, (x, y, z) => W.BuiltStructure(x0 + x, y0 + y, z)?.Kind);
        return g;
    }
}
