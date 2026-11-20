using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.Weather;
using Ravensguard.Sim.Worlds;
using Ravensguard.Sim.WorldGen;

namespace Ravensguard.Sim.Crafting;

/// <summary>
/// The Phase 5 crafting of the running world (crafting-full 4.10, P5-E), installed on the home region: a runner and a crafting context over the world state's <see cref="CraftState"/>, the heavy-rain
/// test fires read, and the notices for the hero (his commission is ready, he learnt a recipe) the hero desk passes on.
/// </summary>
public sealed class CraftService
{
    public required CraftRunner Runner { get; init; }
    public required CraftContext Context { get; init; }
    public required EntityId Hero { get; init; }
    /// <summary>Heavy rain at the hour (over 5 mm an hour: fires go out).</summary>
    public Func<long, bool> HeavyRain { get; init; } = _ => false;
    /// <summary>What happened to the hero's crafting since the hero desk last looked (drained by it; never saved: the messages are a view).</summary>
    public List<HeroText> Notices { get; } = [];
}

/// <summary>
/// Crafting in the abstract tick (crafting-full 4.10): every hour the makers work their commissions (H2's "commissions on abstract makers") and the fires burn; at a day's start the apprenticeships'
/// day and, on a season's first day, the guild dues (the H4 slice). Runs in the outbox phase after the land. A no-op until anyone has a commission, a fire, an apprenticeship or a guild membership.
/// </summary>
[AbstractSystem]
public sealed class CraftSystem : AbstractSystem
{
    public CraftSystem() : base("crafting", TickPhase.H7Outbox, SystemOrder.Outbox + 110) { }

    public override void Step(TickContext ctx)
    {
        var svc = ctx.World.Get<CraftService>(); if (svc == null) return;
        svc.Notices.AddRange(CraftDesk.HourlyCore(svc.Context, ctx.Hour, svc.Hero, svc.HeavyRain(ctx.Hour) ? 60 : 0));
    }
}

/// <summary>Installs the crafting service on the home region (P5-E hook in <see cref="WorldRuntime"/>), with the same runner rules as the hero's desk.</summary>
public static class CraftServiceWiring
{
    public static CraftService Attach(RegionWorld home, WorldState state, ContentDatabase content, RegionWeather? weather)
    {
        var e = state.Economy ?? throw new InvalidOperationException("crafting needs the world's economy");
        // the region generator numbered its fiefs and sites from the same "Other" serials the world allocator starts at, and the economy keys their stores on those ids: crafting's job,
        // commission and fire stores take ids from the world allocator, so it starts past them (a no-op once any id beyond them was given, and in every loaded save)
        ulong top = 0; var reg = state.Population.Region;
        foreach (var f in reg.Fiefs) if (f.Id.Kind == IdKind.Other) top = Math.Max(top, f.Id.Counter);
        foreach (var x in reg.Sites) if (x.Id.Kind == IdKind.Other) top = Math.Max(top, x.Id.Counter);
        if (top > 0) state.Ids.EnsureNext(IdKind.Other, top + 1);
        var cs = state.Crafting ??= new CraftState();
        var hero = state.Party?.Hero ?? EconomyState.HeroOwner;
        var people = state.Population.People;
        cs.Book.Base ??= (p, s) =>
        {
            if (p == hero) return state.Hero?.Skill(Ravensguard.Sim.Characters.HeroRecord.SkillId(s)) ?? 0;
            int i = people.IndexOf(p); return i >= 0 ? people.SkillLevelOf(i, s) : 0;
        };
        var db = RecipeDb.Build(content, e.Goods);
        Store Pack() => state.Party != null ? state.Party.Carried.Pack(e) : e.OpenStore(StoreKind.House, hero, 0, 0, false);
        var runner = new CraftRunner(db, e, cs, state.Seed, state.Ids, o => o == hero ? Pack() : e.OtherStores.GetValueOrDefault(o))
        {
            Content = content, Chronicle = state.Chronicle, Tiers = Ravensguard.Sim.Goods.QualityTiers.From(content),
            MaterialCategory = m => content.TryGet<MaterialDef>(m, out var md) ? md.Category : null, InTick = true,
        };
        if (state.Production is { } prod)
        {
            runner.StationDone = WorkshopBridge.Done(prod.ById);
            prod.LiveStation ??= (b, i) => cs.Jobs.Count > 0 && WorkshopBridge.IsLive(cs, b, i);
        }
        PurseId? Purse() => e.Ledger.TryFind(PurseKind.Person, hero, out var p) ? p : null;
        int cell = (state.EmbarkCellY + 8) * Continent.Size + state.EmbarkCellX + 8;
        var svc = new CraftService
        {
            Runner = runner, Context = CraftWiring.For(state, e, runner, hero, Purse, Pack), Hero = hero,
            HeavyRain = weather == null ? _ => false : h => weather.AtLite(cell, h).State is WeatherState.HeavyRain or WeatherState.Thunderstorm,
        };
        home.Sim.Set(svc);
        return svc;
    }
}
