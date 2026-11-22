#pragma warning disable CS0675   // hash mixing: sign extension of a small id is deterministic and harmless here
using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Naval;

namespace Ravensguard.Sim.Siege;

public enum SallyStage : byte { Out, Back, Done }

/// <summary>
/// A sally (sieges-and-engines 4.5): a formation leaves by a gate, runs at an engine of the besiegers, sets fire to it with torches and pitch (the engine's own ignition rule: wet hides resist, so a man tries again), and returns; the gate stands
/// open until the sally is back in (or has died). It is a battle bubble when it happens near enough to be live; <see cref="AssaultMath.SallyWanted"/> is the garrison's decision.
/// </summary>
public sealed class Sally
{
    public int Id, Team;
    public List<int> Men = [];
    public ulong Engine;
    /// <summary>The gate cells it goes out by (world cells x, y at ground level, cell keys of z 0).</summary>
    public List<(int X, int Y)> Gate = [];
    /// <summary>Where the sally comes home to (world mm).</summary>
    public long HomeX, HomeY;
    public SallyStage Stage;
    public long Since;
    public int Burned, Torches;
    public bool Reported;
}

public sealed partial class AssaultLayer
{
    private readonly List<Sally> _sallies = [];
    private int _nextSally = 1;
    public IReadOnlyList<Sally> Sallies => _sallies;

    /// <summary>The longest a sally stays out before it turns for home (ticks): 3 minutes.</summary>
    public const int SallyOutTicks = 3600;

    /// <summary>The garrison sallies: the gate is opened and the men set out for the engine.</summary>
    public Sally StartSally(int team, IEnumerable<int> men, IEnumerable<(int X, int Y)> gate, long homeXMm, long homeYMm, EntityId engine)
    {
        var s = new Sally { Id = _nextSally++, Team = team, Men = [.. men], Gate = [.. gate], HomeX = homeXMm, HomeY = homeYMm, Engine = engine.Value, Since = World.Tick };
        _sallies.Add(s);
        foreach (var (gx, gy) in s.Gate) SetGate(gx, gy, true);
        return s;
    }

    private void SalliesStep(int t)
    {
        foreach (var s in _sallies)
        {
            if (s.Stage == SallyStage.Done) continue;
            var e = Engines?.Find(new EntityId(s.Engine)); var v = e == null ? null : Engines!.VehicleOf(e);
            s.Men.RemoveAll(m => !World.Fighters[m].Active);
            if (s.Stage == SallyStage.Out)
            {
                bool burning = e != null && e.Burning.Count > 0;
                if (burning || v == null || s.Men.Count == 0 || t - s.Since > SallyOutTicks)
                {
                    if (burning && s.Burned == 0) s.Burned = 1;
                    s.Stage = SallyStage.Back;
                    foreach (int m in s.Men) if (_holds.TryGetValue(m, out var h) && h.Kind == HoldKind.Torch) Release(m);
                }
                else Torch(s, e!, v, t);
            }
            if (s.Stage == SallyStage.Back)
            {
                bool home = true;
                foreach (int m in s.Men)
                {
                    var f = World.Fighters[m];
                    if (DistTo(f, s.HomeX, s.HomeY) > 1500) home = false;
                    if (Free(f)) March(f, s.HomeX, s.HomeY, 1000);
                }
                if (home || s.Men.Count == 0) Close(s);
            }
        }
    }

    /// <summary>The centre (world mm) of the cell of the engine that holds parts and is nearest to a man: where his torch goes.</summary>
    private (long X, long Y)? NearestPartCell(VehicleRow v, Fighter f)
    {
        var (w, l) = Engines!.Size(v); var (cos, sin) = FrameMath.CosSin(v.Facing << 16);
        long best = long.MaxValue; (long X, long Y)? at = null;
        for (int cy = 0; cy < l; cy++)
            for (int cx = 0; cx < w; cx++)
            {
                if (Engines.Stack(v, cx, cy).Count == 0) continue;
                var (px, py) = FrameMath.ToWorld((int)v.XMm, (int)v.YMm, cos, sin, cx * 1000 + 500 - w * 500, cy * 1000 + 500 - l * 500);
                long d = DistTo(f, px, py);
                if (d < best) { best = d; at = (px, py); }
            }
        return at;
    }

    /// <summary>The men go at the engine; one who is at a part of it lights it (a torch takes 2 s to catch) and tries again if the hides were wet.</summary>
    private void Torch(Sally s, Engine e, VehicleRow v, int t)
    {
        foreach (int m in s.Men)
        {
            var f = World.Fighters[m];
            if (_holds.TryGetValue(m, out var h))
            {
                if (h.Kind == HoldKind.Torch && t >= h.Until)
                {
                    Release(m); s.Torches++;
                    if (NearestPartCell(v, f) is { } at) Engines!.Ignite(e, at.X, at.Y, World.GameSec);
                }
                continue;
            }
            if (!f.Active || NearestPartCell(v, f) is not { } p) continue;
            if (DistTo(f, p.X, p.Y) <= R.SallyReachMm + 500) HoldFor(m, HoldKind.Torch, s.Id, t + R.SallyTorchTicks);
            else March(f, p.X, p.Y, 1200);
        }
    }

    /// <summary>The sally is home (or gone): the gate is shut and the chronicle has it.</summary>
    private void Close(Sally s)
    {
        s.Stage = SallyStage.Done;
        foreach (var (gx, gy) in s.Gate) SetGate(gx, gy, false);
        if (s.Reported || Chronicle == null) return;
        s.Reported = true;
        Chronicle(new GameTime(World.GameSec), "siege_sally", "ch_sg_sally",
            [ChronParam.Place("place", Place), ChronParam.Count("engines", s.Burned)], [],
            [new Cause(CauseKind.Siege, Cause == default ? null : Cause, "", "ch_note_sg_sally")],
            [("engines", s.Burned.ToString()), ("men", s.Men.Count.ToString())]);
    }

    private ulong HashSally(ulong h, Sally s)
    {
        h = FightMath.Mix(h, s.Id | (long)s.Team << 8 | (long)s.Stage << 16 | (s.Reported ? 1L << 24 : 0)); h = FightMath.Mix(h, (long)s.Engine); foreach (var (gx, gy) in s.Gate) h = FightMath.Mix(h, gx | (long)gy << 24);
        h = FightMath.Mix(h, s.HomeX); h = FightMath.Mix(h, s.HomeY); h = FightMath.Mix(h, s.Since | (long)s.Burned << 40 | (long)s.Torches << 48);
        foreach (int m in s.Men) h = FightMath.Mix(h, m);
        return h;
    }
}
