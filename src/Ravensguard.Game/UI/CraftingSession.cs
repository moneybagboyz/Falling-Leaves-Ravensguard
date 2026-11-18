using System;
using System.Collections.Generic;
using System.Linq;
using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Crafting;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;

namespace Ravensguard.Game.UI;

/// <summary>
/// What the crafting screen works with (basic-crafting spec 4.7): the recipe database, a crafting runner, the hero as crafter (skills read from the <see cref="HeroRecord"/>, grown by use), a pack of goods
/// and the site (light, tools and stations in reach). It holds its own small economy for the pack: the running world's economy lives on the tick worker's thread, so the screen never touches it; moving
/// the pack into the world's inventory is the trade spec's work.
/// </summary>
public sealed class CraftingSession
{
    public RecipeDb Db { get; }
    public CraftRunner Runner { get; }
    public CraftState State { get; } = new();
    public EconomyState Econ { get; }
    public Store Pack { get; }
    public HeroRecord Hero { get; }
    public CraftActor Actor { get; private set; }
    public CraftSite Site { get; }
    public long NowSec { get; set; }
    public static readonly EntityId HeroId = EconomyState.HeroOwner;

    public CraftingSession(ContentDatabase content, HeroRecord hero, long nowSec, bool starterPack, string[] stations, int light)
    {
        Hero = hero; NowSec = nowSec;
        Econ = EconomyState.FromContent(content, 1, 1);
        Db = RecipeDb.Build(content, Econ.Goods);
        Pack = Econ.OpenStore(StoreKind.House, HeroId, 0, 0, false);
        var ids = new IdAllocator();
        Runner = new CraftRunner(Db, Econ, State, 1, ids, o => Econ.OtherStores.GetValueOrDefault(o));
        State.Book.Base = (p, s) => p == HeroId ? hero.Skill(HeroRecord.SkillId(s)) : 0;
        Actor = new CraftActor(HeroId, hero.Attribute(HeroAttr.Strength), hero.Attribute(HeroAttr.Wits), hero.Attribute(HeroAttr.Perception));
        Site = new CraftSite { Light = light, Water = true };
        // the traveller's kit: a knife, steel and flint, a needle and a pot, and whatever the hero carries
        foreach (var t in new[] { "knife", "steel_and_flint", "needle", "pot" }) Site.AddTool(Db, t);
        foreach (var it in hero.Inventory)
        {
            string id = it.Shape is "dagger" ? "knife" : it.Shape;
            if (Db.Tools.ContainsKey(id)) Site.AddTool(Db, id);
        }
        foreach (var st in stations) if (Db.Stations.ContainsKey(st)) Site.AddStation(Db, st);
        Site.WithImprovised("hammering");
        if (starterPack) Stock();
    }

    /// <summary>A few days of everyday materials so the screen has something to show (the pack is the trade spec's inventory later).</summary>
    void Stock()
    {
        void G(string good, long qty) => Pack.Create(Econ.Goods.IdOf(good), qty, CreateReason.Seed);
        G("flour", 3000); G("salt", 200); G("firewood", 6000); G("straw", 1000); G("tallow", 600); G("cloth_linen", 3000); G("cloth_wool", 2000); G("yarn", 200);
        G("sticks", 2000); G("logs", 4000); G("wool_raw", 1000); G("meat_fresh", 2000); G("vegetables", 1000); G("peas", 1000); G("herbs", 300); G("iron_bar", 1000); G("charcoal", 3000);
        G("twine", 500); G("malt", 2500); G("milk", 5000); G("hide_raw", 3000); G("oak_bark", 1000); G("wheat", 2000);
    }

    public bool KnowsIt(CraftRecipe r) => State.Know.Knows(r, HeroId, State.Book);
    public CraftPlan Plan(CraftRecipe r, int batch, int[]? forced = null) => Runner.Plan(r, Actor, Site, Pack, batch, forced);

    public IEnumerable<CraftJob> Unfinished() => State.Jobs.Values.Where(j => !j.Closed).OrderBy(j => j.Id);
    public int SkillLevel(CraftRecipe r) => r.Skill == Skill.None ? 0 : State.Book.Level(HeroId, r.Skill);

    /// <summary>The goods in the pack, for "what can I make with this".</summary>
    public IEnumerable<string> PackGoods() => Pack.Rows().Select(r => Econ.Goods.StringId(r.Good));
}
