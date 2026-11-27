using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Save;

namespace Ravensguard.Sim.Siege;

public sealed partial class AssaultLayer
{
    /// <summary>The assault as a record of the <c>sieges</c> section: everything the layer keeps (the fight's fighters and the engine book are saved by their own sections).</summary>
    public AssaultRecord Capture()
    {
        var lv = new List<int>();
        for (int i = 0; i < _level.Length; i++) if (_level[i] != 0 || _rise[i] != 0) { lv.Add(i); lv.Add(_level[i]); lv.Add(_rise[i]); }
        var holds = new List<long>();
        foreach (var (id, h) in _holds) { holds.Add(id); holds.Add((long)h.Kind); holds.Add(h.Ref); holds.Add(h.Until); }
        return new AssaultRecord
        {
            OriginX = OriginX, OriginY = OriginY, AttackTeam = AttackTeam, DefendTeam = DefendTeam, Levels = [.. lv], Holds = [.. holds],
            Ladders = [.. _ladders.Select(l => new LadderRow
            {
                Id = l.Id, Team = l.Team, FootX = l.FootX, FootY = l.FootY, TopX = l.TopX, TopY = l.TopY, Stage = (int)l.Stage, StageSince = l.StageSince, Carriers = [.. l.Carriers], Queue = [.. l.Queue],
                Climbers = [.. l.Climbers.Select(c => c.Id)], Progress = [.. l.Climbers.Select(c => c.Progress)], Pusher = l.Pusher, PushTries = l.PushTries, PushSince = l.PushSince, Pushed = l.Pushed, Pusher2 = l.Pusher2, Hooked = l.Hooked,
            })],
            Towers = [.. _towers.Select(t => new TowerRow { Vehicle = t.Vehicle, X = t.X, Y = t.Y, Facing = t.Facing, Down = t.Down, Fallen = t.Fallen, Aboard = t.Aboard, Deck = [.. t.Deck], Bridge = [.. t.Bridge] })],
            Holes = [.. _holes.Select(o => new HoleRow { Team = o.Team, X = o.X, Y = o.Y, Level = o.Level, Stones = o.Stones, Pots = o.Pots, Cauldron = o.Cauldron, NextTick = o.NextTick, Dropped = o.Dropped, Poured = o.Poured, Fired = o.Fired })],
            Sallies = [.. _sallies.Select(s => new SallyRow
            {
                Id = s.Id, Team = s.Team, Men = [.. s.Men], Engine = s.Engine, GateX = [.. s.Gate.Select(g => g.X)], GateY = [.. s.Gate.Select(g => g.Y)], HomeX = s.HomeX, HomeY = s.HomeY, Stage = (int)s.Stage,
                Since = s.Since, Burned = s.Burned, Torches = s.Torches, Reported = s.Reported,
            })],
            OpenGates = [.. _openGates], Beams = [.. _beams], WalkLost = _held10, BreachLost = _heldBreach, NextLadder = _nextLadder, NextSally = _nextSally, Cause = Cause.Value, Place = Place.Value,
            Inside = Inside is { } b ? [b.X0, b.Y0, b.X1, b.Y1] : [],
            Orders = [.. _orders.Select(o => new OrderRow { Fighter = o.Fighter, Index = o.Index, Stop = o.Stop, Points = [.. o.Points.SelectMany(p => new[] { p.X, p.Y })] })],
            Live = Live, Siege = SiegeId, LadderBudget = LadderBudget, AiTeams = AiTeams, FromWest = FromWest, PlanAt = _planAt,
            Cmds = [.. _cmds.SelectMany(c => new long[] { (long)c.Kind, c.Team, c.Due, c.X0, c.Y0, c.X1, c.Y1 })], Goals = [.. _goals.SelectMany(g => new long[] { (long)g.Vehicle, g.X, g.Y })],
        };
    }

    /// <summary>Lays a saved assault over a restored fight (its fighters already in place) and the walls and engines it works against; the layer then continues exactly as the one that was saved.</summary>
    public static AssaultLayer Restore(AssaultRecord r, FightWorld world, WallWorld walls, EngineBook? engines = null, EngineWorld? engineWorld = null, Ravensguard.Content.AssaultRulesDef? rules = null)
    {
        var a = new AssaultLayer(world, walls, r.AttackTeam, r.DefendTeam, r.OriginX, r.OriginY, rules) { Engines = engines, EngineW = engineWorld };
        for (int i = 0; i + 2 < r.Levels.Length; i += 3) { a.Ensure(r.Levels[i]); a._level[r.Levels[i]] = r.Levels[i + 1]; a._rise[r.Levels[i]] = r.Levels[i + 2]; }
        for (int i = 0; i + 3 < r.Holds.Length; i += 4) a._holds[(int)r.Holds[i]] = new Hold { Kind = (HoldKind)r.Holds[i + 1], Ref = (int)r.Holds[i + 2], Until = r.Holds[i + 3] };
        foreach (var l in r.Ladders)
        {
            var s = new SiegeLadder { Id = l.Id, Team = l.Team, FootX = l.FootX, FootY = l.FootY, TopX = l.TopX, TopY = l.TopY, Stage = (LadderStage)l.Stage, StageSince = l.StageSince, Carriers = [.. l.Carriers], Queue = [.. l.Queue], Pusher = l.Pusher, Pusher2 = l.Pusher2, Hooked = l.Hooked, PushTries = l.PushTries, PushSince = l.PushSince, Pushed = l.Pushed };
            for (int i = 0; i < l.Climbers.Length; i++) s.Climbers.Add(new Climb { Id = l.Climbers[i], Progress = l.Progress[i] });
            a._ladders.Add(s);
        }
        foreach (var t in r.Towers) { a._towers.Add(new TowerPost { Vehicle = t.Vehicle, X = t.X, Y = t.Y, Facing = t.Facing, Down = t.Down, Fallen = t.Fallen, Aboard = t.Aboard, Deck = [.. t.Deck], Bridge = [.. t.Bridge] }); a.SetDeck(t.Deck, true); a.SetDeck(t.Bridge, true); }
        foreach (var o in r.Holes) a._holes.Add(new MurderHole { Team = o.Team, X = o.X, Y = o.Y, Level = o.Level, Stones = o.Stones, Pots = o.Pots, Cauldron = o.Cauldron, NextTick = o.NextTick, Dropped = o.Dropped, Poured = o.Poured, Fired = o.Fired });
        foreach (var s in r.Sallies)
        {
            var sa = new Sally { Id = s.Id, Team = s.Team, Men = [.. s.Men], Engine = s.Engine, HomeX = s.HomeX, HomeY = s.HomeY, Stage = (SallyStage)s.Stage, Since = s.Since, Burned = s.Burned, Torches = s.Torches, Reported = s.Reported };
            for (int i = 0; i < s.GateX.Length; i++) sa.Gate.Add((s.GateX[i], s.GateY[i]));
            a._sallies.Add(sa);
        }
        foreach (long g in r.OpenGates) a._openGates.Add(g);
        foreach (long b in r.Beams) a._beams.Add(b);
        a._held10 = r.WalkLost; a._heldBreach = r.BreachLost; a._nextLadder = r.NextLadder; a._nextSally = r.NextSally; a.Cause = new EntityId(r.Cause); a.Place = new EntityId(r.Place);
        if (r.Inside.Length == 4) a.Inside = (r.Inside[0], r.Inside[1], r.Inside[2], r.Inside[3]);
        foreach (var o in r.Orders) { var m = new MarchOrder { Fighter = o.Fighter, Index = o.Index, Stop = o.Stop }; for (int i = 0; i + 1 < o.Points.Length; i += 2) m.Points.Add((o.Points[i], o.Points[i + 1])); a._orders.Add(m); }
        a.Live = r.Live; a.SiegeId = r.Siege; a.LadderBudget = r.LadderBudget; a.AiTeams = r.AiTeams; a.FromWest = r.FromWest; a._planAt = r.PlanAt;
        for (int i = 0; i + 6 < r.Cmds.Length; i += 7) a._cmds.Add(new AssaultCmd { Kind = (AssaultOrder)r.Cmds[i], Team = (int)r.Cmds[i + 1], Due = (int)r.Cmds[i + 2], X0 = (int)r.Cmds[i + 3], Y0 = (int)r.Cmds[i + 4], X1 = (int)r.Cmds[i + 5], Y1 = (int)r.Cmds[i + 6] });
        for (int i = 0; i + 2 < r.Goals.Length; i += 3) a._goals.Add(new EngineGoal { Vehicle = (ulong)r.Goals[i], X = r.Goals[i + 1], Y = r.Goals[i + 2] });
        a._seenVersion = -1;
        return a;
    }
}
