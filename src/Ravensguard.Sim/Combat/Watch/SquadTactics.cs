using Ravensguard.Sim.Combat.Watch;

namespace Ravensguard.Sim.Combat.Watch
{
    /// <summary>The formation cards (watch-mode 4.4, plan 2.8: F1-F4): a line, a shield wall, loose skirmish order, and "fall back to me". <c>Auto</c> is the captain's own choice. Append only.</summary>
    public enum Formation : byte { Auto, Line, ShieldWall, Loose, FallBack }

    /// <summary>The player's orders to the side (watch-mode 4.1): focus a target, hold fire, fall back to the hero, protect someone, and clear them. Append only.</summary>
    public enum SquadOrderKind : byte { Clear, Focus, HoldFire, FallBack, Protect }

    /// <summary>What a side's orders are now (stance biases the brain reads; never the resolver).</summary>
    public struct TeamOrders
    {
        public Formation Formation;
        /// <summary>The formation was set by the player (an F-key), not by the captain.</summary>
        public bool Override;
        public int Focus, Protect, Leader;
        public bool HoldFire, FallBack;
        public static TeamOrders None => new() { Focus = -1, Protect = -1, Leader = -1 };
        public long Pack() => (long)Formation | (Override ? 1L << 8 : 0) | (HoldFire ? 1L << 9 : 0) | (FallBack ? 1L << 10 : 0) | (long)(ushort)(Focus + 1) << 16 | (long)(ushort)(Protect + 1) << 32 | (long)(ushort)(Leader + 1) << 48;
        public static TeamOrders Unpack(long v) => new()
        {
            Formation = (Formation)(byte)v, Override = (v & 1L << 8) != 0, HoldFire = (v & 1L << 9) != 0, FallBack = (v & 1L << 10) != 0,
            Focus = (ushort)(v >> 16) - 1, Protect = (ushort)(v >> 32) - 1, Leader = (ushort)(v >> 48) - 1,
        };
    }

    /// <summary>
    /// The squad leader's choice of formation when the player has not set one (watch-mode 4.4): Tactics 30 or more against enemy archers or riders puts the shield bearers in a wall,
    /// against foot melee a line; under 30 the squad fights loose. The player's F-key always wins. Pure.
    /// </summary>
    public static class SquadTactics
    {
        public const int WallTactics = 30;

        public static Formation Choose(int leaderTactics, bool enemyRanged, bool enemyCavalry, int shieldBearers)
        {
            if (leaderTactics < WallTactics) return Formation.Loose;
            return (enemyRanged || enemyCavalry) && shieldBearers >= 2 ? Formation.ShieldWall : Formation.Line;
        }

        /// <summary>The F-key formation (F1 line, F2 shield wall, F3 loose, F4 fall back to me), or Auto.</summary>
        public static Formation OfKey(int fKey) => fKey switch { 1 => Formation.Line, 2 => Formation.ShieldWall, 3 => Formation.Loose, 4 => Formation.FallBack, _ => Formation.Auto };
    }
}

namespace Ravensguard.Sim.Combat.Melee2
{
    public sealed partial class FightWorld
    {
        private readonly SortedDictionary<int, TeamOrders> _orders = new();

        /// <summary>A side's orders (none set: the default).</summary>
        public TeamOrders OrdersOf(int team) => _orders.TryGetValue(team, out var o) ? o : TeamOrders.None;

        /// <summary>The captain picks the side's formation by its Tactics skill (watch-mode 4.4), unless the player set one. <paramref name="leader"/> leads the side (the hero).</summary>
        public void CaptainFormation(int team, int leader, int leaderTactics)
        {
            var o = OrdersOf(team);
            o.Leader = leader;
            if (!o.Override)
            {
                bool ranged = false, cav = false; int shields = 0;
                foreach (var f in Fighters)
                {
                    if (f.Team != team) { ranged |= f.Ranged; cav |= Mounts?.PairOf(f.Id) is { Mounted: true }; }
                    else if (f.Shield != null) shields++;
                }
                o.Formation = SquadTactics.Choose(leaderTactics, ranged, cav, shields);
            }
            _orders[team] = o;
        }

        /// <summary>An F-key: the player's formation for the side (Auto hands it back to the captain).</summary>
        public void SetFormation(int team, Formation f)
        {
            var o = OrdersOf(team);
            o.Formation = f; o.Override = f != Formation.Auto;
            _orders[team] = o;
        }

        /// <summary>An order to the side (the order wheel, a click on an enemy).</summary>
        public void Order(int team, SquadOrderKind k, int fighter)
        {
            var o = OrdersOf(team);
            switch (k)
            {
                case SquadOrderKind.Focus: o.Focus = fighter; break;
                case SquadOrderKind.HoldFire: o.HoldFire = !o.HoldFire; break;
                case SquadOrderKind.FallBack: o.FallBack = !o.FallBack; break;
                case SquadOrderKind.Protect: o.Protect = fighter; break;
                default: o.Focus = -1; o.Protect = -1; o.HoldFire = false; o.FallBack = false; break;
            }
            _orders[team] = o;
        }

        public long[] PackOrders() { var r = new List<long>(); foreach (var (t, o) in _orders) { r.Add(t); r.Add(o.Pack()); } return [.. r]; }
        public void RestoreOrders(long[]? v) { _orders.Clear(); if (v == null) return; for (int i = 0; i + 1 < v.Length; i += 2) _orders[(int)v[i]] = TeamOrders.Unpack(v[i + 1]); }
    }
}
