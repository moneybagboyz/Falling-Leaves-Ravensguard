using System;
using System.Collections.Generic;
using System.Linq;
using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Crafting;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Inventory;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Game.UI;

/// <summary>
/// What the crafting screen works with (basic-crafting spec 4.7). In a running world (<see cref="World"/> set) the work is the world's: the hero's pack is the pack store of the economy, the
/// unfinished pieces are <see cref="CraftJob"/>s of the world's crafting state (saved), and starting, working, pausing and collecting are hero commands applied by the tick worker at a barrier
/// on the world clock (trade spec 4.7). The screen itself only reads: it plans on a mirror of the hero's pack and crafting state, refreshed from each <see cref="HeroView"/>, so every number it
/// shows is <see cref="CraftRunner.Plan"/> on the same goods and skills the work uses. Without a world (the dev hamlet) it keeps the old starter bundle and runs the work locally.
/// </summary>
public sealed class CraftingSession
{
    public RecipeDb Db { get; }
    public CraftRunner Runner { get; private set; }
    public CraftState State { get; private set; } = new();
    public EconomyState Econ { get; }
    public Store Pack { get; }
    public HeroRecord Hero { get; }
    public CraftActor Actor { get; private set; }
    public CraftSite Site { get; }
    public CraftSiteSpec SiteSpec { get; }
    public long NowSec { get; set; }
    public static readonly EntityId HeroId = EconomyState.HeroOwner;
    /// <summary>The running world's hero link; null in the dev hamlet.</summary>
    public HeroWorld? World { get; }
    /// <summary>The world clock now (game seconds), and a way to set the world's speed for x1 / x2 / x4 while crafting.</summary>
    public Func<long>? WorldNow { get; init; }
    public Action<int>? SetWorldSpeed { get; init; }
    HeroView? _seen;
    readonly IdAllocator _ids = new();

    public CraftingSession(ContentDatabase content, HeroRecord hero, long nowSec, bool starterPack, string[] stations, int light, HeroWorld? world = null)
    {
        Hero = hero; NowSec = nowSec; World = world;
        Econ = EconomyState.FromContent(content, 1, 1);
        Db = RecipeDb.Build(content, Econ.Goods);
        Pack = Econ.OpenStore(StoreKind.House, HeroId, 0, 0, false);
        Runner = MakeRunner();
        Actor = new CraftActor(HeroId, hero.Attribute(HeroAttr.Strength), hero.Attribute(HeroAttr.Wits), hero.Attribute(HeroAttr.Perception));
        // the traveller's kit: a knife, steel and flint, a needle and a pot, and whatever the hero carries (P5-A4: in a running world the pack's real item rows and goods, else the record's kit)
        var tools = new List<string> { "knife", "steel_and_flint", "needle", "pot" };
        var carried = world?.View is { } view ? view.Items.Select(i => i.Shape).Concat(view.Goods.Select(g => g.Id)) : hero.Inventory.Select(i => i.Shape);
        tools.AddRange(HeroPack.Tools(carried, Db.Tools.ContainsKey));
        SiteSpec = new CraftSiteSpec(light, true, [.. tools.Distinct()], [.. stations.Where(Db.Stations.ContainsKey)], ["hammering"]);
        Site = SiteSpec.Build(Db);
        if (world == null && starterPack) Stock();
        Refresh();
    }

    CraftRunner MakeRunner()
    {
        State.Book.Base = (p, s) => p == HeroId ? Hero.Skill(HeroRecord.SkillId(s)) : 0;
        return new CraftRunner(Db, Econ, State, 1, _ids, o => Econ.OtherStores.GetValueOrDefault(o));
    }

    /// <summary>A few days of everyday materials for the dev hamlet, which has no world economy.</summary>
    void Stock()
    {
        void G(string good, long qty) => Pack.Create(Econ.Goods.IdOf(good), qty, CreateReason.Seed);
        G("flour", 3000); G("salt", 200); G("firewood", 6000); G("straw", 1000); G("tallow", 600); G("cloth_linen", 3000); G("cloth_wool", 2000); G("yarn", 200);
        G("sticks", 2000); G("logs", 4000); G("wool_raw", 1000); G("meat_fresh", 2000); G("vegetables", 1000); G("peas", 1000); G("herbs", 300); G("iron_bar", 1000); G("charcoal", 3000);
        G("twine", 500); G("malt", 2500); G("milk", 5000); G("hide_raw", 3000); G("oak_bark", 1000); G("wheat", 2000);
    }

    /// <summary>Takes the newest hero view into the mirror (its pack and crafting state). Cheap when nothing changed. Returns true when the mirror changed.</summary>
    public bool Refresh()
    {
        if (World?.View is not { } v || ReferenceEquals(v, _seen)) return false;
        _seen = v;
        foreach (var (g, q) in Pack.Rows()) Pack.Destroy(g, q, DestroyReason.Other);
        foreach (var g in v.Goods) Pack.Create(g.Good, g.Qty, CreateReason.Other);
        State = v.Crafting ?? new CraftState();
        Runner = MakeRunner();
        int A(HeroAttr a) => v.Attributes[(int)a];
        Actor = new CraftActor(HeroId, A(HeroAttr.Strength), A(HeroAttr.Wits), A(HeroAttr.Perception));
        if (WorldNow != null) NowSec = WorldNow();
        return true;
    }

    public bool KnowsIt(CraftRecipe r) => State.Know.Knows(r, HeroId, State.Book);
    public CraftPlan Plan(CraftRecipe r, int batch, int[]? forced = null) => Runner.Plan(r, Actor, Site, Pack, batch, forced);

    public IEnumerable<CraftJob> Unfinished() => State.Jobs.Values.Where(j => !j.Closed && (World == null || j.Owner == HeroId)).OrderBy(j => j.Id);
    public CraftJob? Find(EntityId id) => State.Get(id);
    public int SkillLevel(CraftRecipe r) => r.Skill == Skill.None ? 0 : State.Book.Level(HeroId, r.Skill);

    /// <summary>The goods in the pack, for "what can I make with this".</summary>
    public IEnumerable<string> PackGoods() => Pack.Rows().Select(r => Econ.Goods.StringId(r.Good));

    long Now => WorldNow?.Invoke() ?? NowSec;

    // ------------------------------------------------------------------ the work (commands to the world, or local in the hamlet)

    /// <summary>Starts the plan. Local: the job at once. World: null (the job appears in the next view; <see cref="PendingStart"/> says it is on its way).</summary>
    public CraftJob? Start(CraftPlan plan)
    {
        if (World == null) return Runner.Start(plan, Actor, HeroId, Pack, NowSec);
        World.Issue(new HeroCommand { Kind = HeroCommandKind.CraftStart, IssuedSec = Now, Recipe = plan.Recipe.Index, Batch = plan.Batch, Choice = [.. plan.Choice.Select(c => (int)c)], Site = SiteSpec });
        PendingStart = plan.Recipe.Index;
        return null;
    }

    public int PendingStart { get; set; } = -1;

    /// <summary>Works on for <paramref name="dt"/> game seconds (local), or tells the world the hero worked until now (world: sent at most twice a real second).</summary>
    public List<CraftEvent> Work(CraftJob j, long dt)
    {
        if (World == null) { var ev = Runner.Advance(j, dt, NowSec); NowSec += dt; return ev; }
        World.Issue(new HeroCommand { Kind = HeroCommandKind.CraftWork, IssuedSec = Now, Job = j.Id, UntilSec = Now });
        return [];
    }

    public void Pause(CraftJob j) { if (World == null) Runner.Pause(j); else World.Issue(new HeroCommand { Kind = HeroCommandKind.CraftPause, IssuedSec = Now, Job = j.Id }); }

    public bool Resume(CraftJob j)
    {
        if (World == null) return Runner.Resume(j, Actor);
        World.Issue(new HeroCommand { Kind = HeroCommandKind.CraftResume, IssuedSec = Now, Job = j.Id });
        return true;
    }

    public bool Collect(CraftJob j)
    {
        if (World == null) return Runner.Collect(j, Actor, NowSec).Ok;
        World.Issue(new HeroCommand { Kind = HeroCommandKind.CraftCollect, IssuedSec = Now, Job = j.Id });
        return true;
    }

    public void Speed(int x) => SetWorldSpeed?.Invoke(x);

    /// <summary>The results of the hero's craft commands since <paramref name="afterSeq"/> (world only).</summary>
    public IEnumerable<HeroResult> Results(long afterSeq) => World?.View?.Results.Where(r => r.Seq > afterSeq && r.Kind is >= HeroCommandKind.CraftStart && r.Text.Length > 0) ?? [];
}
