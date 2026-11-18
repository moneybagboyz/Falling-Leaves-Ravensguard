using Ravensguard.Sim.Core;
using Ravensguard.Sim.Live;

namespace Ravensguard.Sim.Caravans;

/// <summary>What a live entity of a promoted caravan is.</summary>
public enum LivePartKind : byte { Master, Driver, Guard, PackHorse, Cart, OxWagon, Draught }

/// <summary>One live entity of a promoted caravan: where it stands (continent millimetres) and which way it faces (unit vector x1024).</summary>
public readonly record struct LivePart(LivePartKind Kind, int Index, long XMm, long YMm, int HeadX, int HeadY, int Person = -1);

/// <summary>A caravan inside a bubble: the lead's position and every live entity it is made of.</summary>
public sealed record LiveCaravan(EntityId Id, int Home, CaravanState State, long LeadXMm, long LeadYMm, long LoadKg, IReadOnlyList<LivePart> Parts);

/// <summary>
/// The LODBridge for caravans (abstract spec 3.5, caravans spec 3.7): decides which caravans the bubble around a point holds, and what each is made of. It owns no state of the
/// record: the position of every part is a pure function of the caravan row and the game second (<see cref="CaravanMotion"/>), so a promoted caravan can never drift from the
/// abstract one, and a demoted one needs no state handed back. A caravan is promoted when its lead comes within <see cref="PromoteM"/> of the centre, demoted when it
/// has stayed beyond <see cref="DemoteM"/> for <see cref="DemoteAfterGameSeconds"/> (5 real seconds at 20 game seconds per real second).
/// </summary>
public sealed class CaravanBridge
{
    // the bubble rules are the people bridge's too (Live/Bubble.cs)
    public const int PromoteM = Bubble.PromoteM, DemoteM = Bubble.DemoteM, SpacingM = 12;
    public const long DemoteAfterGameSeconds = Bubble.DemoteAfterGameSeconds;

    private readonly HashSet<EntityId> _live = [];
    private readonly Dictionary<EntityId, long> _outsideSince = [];
    /// <summary>Caravans that were promoted since the bridge began (the hysteresis test counts them).</summary>
    public int Promotions { get; private set; }
    public int Demotions { get; private set; }
    public IReadOnlySet<EntityId> Live => _live;

    /// <summary>The caravans (active ones, ascending id) the bubble holds at <paramref name="nowSec"/>, with their parts.</summary>
    public IReadOnlyList<LiveCaravan> Update(IEnumerable<Caravan> caravans, long centreXMm, long centreYMm, long nowSec)
    {
        var res = new List<LiveCaravan>();
        var seen = new HashSet<EntityId>();
        foreach (var c in caravans.OrderBy(c => c.Id))
        {
            if (!c.Active) continue;
            var (lx, ly, _, _) = CaravanMotion.PositionOf(c, nowSec);
            long d2 = Bubble.Sq(lx - centreXMm) + Bubble.Sq(ly - centreYMm);
            if (!_live.Contains(c.Id))
            {
                if (!Bubble.InsidePromote(d2)) continue;
                _live.Add(c.Id); _outsideSince.Remove(c.Id); Promotions++;
            }
            else
            {
                long since = _outsideSince.TryGetValue(c.Id, out long s0) ? s0 : -1;
                bool demote = Bubble.ShouldDemote(d2, nowSec, ref since);
                if (since < 0) _outsideSince.Remove(c.Id); else _outsideSince[c.Id] = since;
                if (demote) { _live.Remove(c.Id); _outsideSince.Remove(c.Id); Demotions++; continue; }
            }
            seen.Add(c.Id);
            res.Add(Build(c, nowSec));
        }
        foreach (var id in _live.Where(id => !seen.Contains(id)).ToList()) { _live.Remove(id); _outsideSince.Remove(id); Demotions++; }   // the caravan reached its journey's end or was lost
        return res;
    }

    /// <summary>The live entities of a caravan at <paramref name="nowSec"/>: vehicles 12 m apart behind the lead, a draught animal or two ahead of each, a driver beside it, the master up front, guards spread along the train.</summary>
    public static LiveCaravan Build(Caravan c, long nowSec)
    {
        long lead = CaravanMotion.DistAt(c, nowSec);
        long limit = c.State == CaravanState.Outbound || c.State == CaravanState.AtGate ? c.Path.LengthMm : 2 * c.Path.LengthMm;
        (long X, long Y, int Hx, int Hy) At(long d) { var p = c.Path.At(Math.Clamp(d, 0, limit)); return (p.XMm, p.YMm, p.HeadX, p.HeadY); }
        var parts = new List<LivePart>();
        long spacing = SpacingM * 1000L;
        int drivers = Math.Max(0, c.Crew.Length - c.Guards);
        for (int i = 0; i < c.Fielded; i++)
        {
            long d = lead - i * spacing;
            var p = At(d);
            var vk = c.Vehicles[i] == VehicleKind.PackHorse ? LivePartKind.PackHorse : c.Vehicles[i] == VehicleKind.Cart ? LivePartKind.Cart : LivePartKind.OxWagon;
            parts.Add(new LivePart(vk, i, p.X, p.Y, p.Hx, p.Hy));
            int draught = Vehicles.Draught(c.Vehicles[i]);
            for (int a = 0; a < draught; a++) { var q = At(d + 3_000 + a * 1_800); parts.Add(new LivePart(LivePartKind.Draught, i * 2 + a, q.X, q.Y, q.Hx, q.Hy)); }
            if (i < drivers) { var q = At(d + 2_500); parts.Add(new LivePart(LivePartKind.Driver, i, q.X + q.Hy * 700 / 1024, q.Y - q.Hx * 700 / 1024, q.Hx, q.Hy, c.Crew[i])); }
        }
        var m = At(lead + 6_500);
        parts.Add(new LivePart(LivePartKind.Master, 0, m.X, m.Y, m.Hx, m.Hy, c.Master));
        long train = Math.Max(1, c.Fielded) * spacing;
        for (int g = 0; g < c.Guards && drivers + g < c.Crew.Length; g++)
        {
            var q = At(lead + 3_000 - (g + 1) * train / (c.Guards + 1));
            parts.Add(new LivePart(LivePartKind.Guard, g, q.X + q.Hy * 1500 / 1024, q.Y - q.Hx * 1500 / 1024, q.Hx, q.Hy, c.Crew[drivers + g]));
        }
        var (lx, ly, _, _) = CaravanMotion.PositionOf(c, nowSec);
        return new LiveCaravan(c.Id, c.Home, c.State, lx, ly, 0, parts);
    }
}
