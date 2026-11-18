using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Crafting;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Tests.Economy;

namespace Ravensguard.Sim.Tests.Crafting;

/// <summary>One crafter with a pack, a site that has every tool and station, and the recipe database of the base content.</summary>
public sealed class CraftWorld
{
    public EconomyState Econ;
    public RecipeDb Db;
    public CraftState St = new();
    public CraftRunner R;
    public IdAllocator Ids = new();
    public Store Pack;
    public EntityId Hero = EntityId.Make(IdKind.Person, 77), Other = EntityId.Make(IdKind.Person, 78);
    public CraftSite Site;
    public long Now = 8 * 3600;

    public CraftWorld(ulong seed = 7, EconomyState? econ = null)
    {
        Econ = econ ?? EconomyState.FromContent(EconFixture.Content, 1, 1);
        Db = RecipeDb.Build(EconFixture.Content, Econ.Goods);
        Pack = Econ.OpenStore(StoreKind.House, Hero, 0, 0, false);
        R = new CraftRunner(Db, Econ, St, seed, Ids, o => Econ.OtherStores.GetValueOrDefault(o));
        Site = FullSite(Db);
    }

    public static CraftSite FullSite(RecipeDb db)
    {
        var s = new CraftSite { Light = 255, Water = true };
        foreach (var t in db.Tools.Keys) s.AddTool(db, t);
        foreach (var st in db.Stations.Keys) s.AddStation(db, st);
        return s;
    }

    public CraftActor Actor(EntityId? who = null) => new(who ?? Hero);
    public void Skill(Skill s, int level, EntityId? who = null) => St.Book.Set(who ?? Hero, s, level * CraftMath.MicroPerLevel);
    public void Give(string good, long qty) => Pack.Create(Econ.Goods.IdOf(good), qty, CreateReason.Other);
    public long Qty(string good, Store? s = null) => (s ?? Pack).Qty(Econ.Goods.IdOf(good));

    /// <summary>Stocks the pack for <paramref name="batch"/> of a recipe (first alternative of every slot, plus the fuel) and returns the plan.</summary>
    public CraftPlan Stock(string recipe, int batch = 1)
    {
        var r = Db[recipe];
        foreach (var slot in r.Slots) Pack.Create(slot.Alts[0].Good, slot.Alts[0].Qty * batch, CreateReason.Other);
        foreach (var s in r.Steps) if (s.FuelGood != ItemTable.NoGood) Pack.Create(s.FuelGood, s.FuelQty * batch, CreateReason.Other);
        return R.Plan(r, Actor(), Site, Pack, batch);
    }

    public CraftJob StartNew(string recipe, int batch = 1, EntityId target = default)
    {
        var plan = Stock(recipe, batch);
        Assert.True(plan.CanStart, string.Join("; ", plan.Missing));
        return R.Start(plan, Actor(), Hero, Pack, Now, 1000, 2000, target) ?? throw new Xunit.Sdk.XunitException("start refused");
    }

    /// <summary>Works the job until it is no longer active, in steps of <paramref name="chunk"/> seconds; returns every event.</summary>
    public List<CraftEvent> Work(CraftJob j, long chunk = 600)
    {
        var ev = new List<CraftEvent>(); long t0 = j.TotalSeconds, start = Now;
        while (j.State == CraftJobState.Active) { ev.AddRange(R.Advance(j, chunk, start + (j.TotalSeconds - t0))); }
        Now = start + (j.TotalSeconds - t0);
        return ev;
    }

    /// <summary>The goods audit over the stores the test opened: per good, created minus destroyed equals what the stores hold.</summary>
    public bool Audit()
    {
        var sums = new long[Econ.Goods.Count];
        foreach (var s in Econ.OtherStores.Values) foreach (var (g, q) in s.Rows()) { if (q < 0) return false; sums[g] += q; }
        for (ushort g = 0; g < Econ.Goods.Count; g++) if (Econ.Mass.Net(g) != sums[g]) return false;
        return true;
    }

    public EntityId Garment(int condition) => Econ.Items.Add(Ids, "shirt", ItemQuality.Common, Hero, [new ItemComponent("linen", 700, 1000)], conditionPermille: condition);

    public string Digest()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var (s, name) in Econ.OtherStores.Select(kv => (kv.Value, kv.Key.Value.ToString())).Prepend((Pack, "pack")))
            sb.Append(name).Append(':').Append(string.Join(",", s.Rows().Select(r => $"{r.Good}x{r.Qty}"))).Append(';');
        foreach (var i in Econ.Items.LiveRows()) { var r = Econ.Items.At(i); sb.Append($"{r.Id.Value}{r.Shape}{(int)r.Quality}c{r.ConditionPermille}f{(int)r.Flags}|"); }
        foreach (var j in St.Jobs.Values) sb.Append($"J{j.Id.Value}s{j.StepIndex}t{j.TotalSeconds}{j.State}|");
        return sb.ToString();
    }
}
