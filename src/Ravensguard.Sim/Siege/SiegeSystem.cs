using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Armies;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Politics;
using Ravensguard.Sim.WarSupply;

namespace Ravensguard.Sim.Siege;

/// <summary>The sieges' hour (overview 8.4 <c>Sieges</c> = 1,140, H1 after the armies): engines are built at the camps, crewed and shot, breaches are noticed. A no-op while no siege exists.</summary>
[AbstractSystem]
public sealed class SiegeHourSystem : AbstractSystem
{
    public SiegeHourSystem() : base("sieges", TickPhase.H1Caravans, SystemOrder.Sieges) { }
    public override void Step(TickContext ctx)
    {
        var sw = ctx.World.Get<SiegeWorld>(); if (sw == null || sw.Peek is not { Sieges.Count: > 0 }) return;
        sw.Hour(new ArmyEnv(ctx.World, ctx.Outbox, ctx.Hour));
    }
}

/// <summary>The sieges' day (<c>SiegesDaily</c> = 5,240, H5 at hour 0): the choices of both sides and the AI's pick of a place to besiege. A no-op while there is no siege and no war.</summary>
[AbstractSystem]
public sealed class SiegeDailySystem : AbstractSystem
{
    public SiegeDailySystem() : base("sieges_daily", TickPhase.H5DailyGlobals, SystemOrder.SiegesDaily) { }
    public override void Step(TickContext ctx)
    {
        if (ctx.HourOfDay != 0) return;
        var sw = ctx.World.Get<SiegeWorld>(); if (sw == null) return;
        var env = new ArmyEnv(ctx.World, ctx.Outbox, ctx.Hour);
        sw.Day(env); sw.AiPick(env);
    }
}

public sealed partial class SiegeWorld
{
    /// <summary>
    /// The AI's siege objective (armies-on-the-map 4.10 in its first form, until the campaign brain): a host of a realm at war, formed and standing, whose ruler is not the hero, marches on the nearest walled place of the enemy realm and invests it on arrival. A world
    /// with no walled place (no <see cref="WallsOf"/>) never picks one.
    /// </summary>
    public void AiPick(ArmyEnv e)
    {
        if (WallsOf == null) return;
        var pb = Armies.Politics; var hs = Hs; if (pb == null || hs == null) return;
        foreach (var host in Armies.Book.Hosts.Values.ToList())
        {
            if (host.Supply == 0 || host.State is not (HostState.Camped or HostState.Marching) || Hero(host.Commander) || host.War < 0 || Peek?.OfHost(host.Id) != null || (Armies.Book.Campaigns.TryGetValue(host.Id, out var camp) && camp.Objective >= 0)) continue;
            var hid = new HostId(host.Supply); if (!hs.Exists(hid)) continue;
            int target = -1, best = int.MaxValue;
            for (int p = 0; p < Armies.Region.Settlements.Count; p++)
            {
                int realm = hs.RealmOf(p); if (realm == host.Realm || realm < 0 || pb.WarOf(host.Realm, realm) == null || Peek?.OfPlace(p) != null || WallsOf(p) == null) continue;
                int d = Armies.DistM(host.Point, p); if (d < best) { best = d; target = p; }
            }
            if (target < 0) continue;
            if (Armies.Book.Marches.TryGetValue(host.Supply, out var m))
            {
                if (m.State == MarchState.Arrived && m.Stops.Count > 0 && m.Stops[^1] == target) Invest(e, host.Id, target);
                continue;
            }
            if (host.Point == target) { Invest(e, host.Id, target); continue; }
            if (host.State == HostState.Camped) { var march = Armies.BeginMarch(e, hid, [host.Point, target], "ch_note_sg_marched"); if (march != null) host.State = HostState.Marching; }
        }
    }
}

/// <summary>Installs the siege world on the home region (the P7-SG-D hook in <c>WorldRuntime</c>): the book is the world state's (made at the first siege, so a world that never besieged keeps its bytes). The walls', engines' and bubble ports are the game's to set.</summary>
public static class SiegeWiring
{
    public static SiegeWorld? Attach(Worlds.RegionWorld home, Save.WorldState state, ContentDatabase content)
    {
        var aw = home.Sim.Get<ArmyWorld>(); if (aw == null) return null;
        var sw = new SiegeWorld(home.Sim, aw, () => state.Sieges, () => state.Sieges ??= new SiegeBook());
        var cw = home.Sim.Get<Construction.ConstructionWorld>();
        if (cw != null)
        {
            var walls = new WallWorld(state.Chunks, content, state.Seed) { Construction = cw, Chronicle = sw.Writer };
            sw.WallsOf = new BuiltWalls(sw, cw, walls).Of;                                                                     // the hero's built wall pieces; the generator places no civic walls
        }
        aw.Siege = new SiegeHook(sw);
        home.Sim.Set(sw);
        return sw;
    }
}
