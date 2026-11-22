using Ravensguard.Content;
using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Siege;

/// <summary>
/// Shots, landings and what a landing hits (sieges-and-engines 4.1, 4.2). A shot's landing is drawn on stream 0x7C0 (key: the engine and the shot's serial) from the same inverse-normal table the archers' aim uses; everything at the landing is hit
/// at once: the wall cell (SG-A's one damage rule on the shot's momentum), the people (the resolver, through <see cref="EngineWorld.People"/>) and the parts of any engine under it.
/// </summary>
public sealed partial class EngineBook
{
    /// <summary>
    /// Where a shot lands (mm): the aim point with a lateral error of sigma <c>SigmaMdeg x (1 - Engineering / 150)</c> thousandths of a degree at the range, and a range error of <c>RangeErrPm x (1 - Engineering / 150)</c> permille of the range along
    /// the line of fire. Two draws on stream 0x7C0 keyed by the engine and the serial, so the same shot lands in the same place whenever and however it is stepped.
    /// </summary>
    public (long X, long Y) Landing(EntityId engine, uint serial, long fromX, long fromY, long toX, long toY, int engineering, EngineStatDef st)
    {
        long dx = toX - fromX, dy = toY - fromY, range = FightMath.Len(dx, dy);
        if (range == 0) return (toX, toY);
        int div = Math.Max(1, Table.Rules.SkillDivisor), k = Math.Max(0, div - Math.Clamp(engineering, 0, 100));
        int sigma = (int)((long)st.SigmaMdeg * k / div);
        long errPm = (long)st.RangeErrPm * k / div;
        int u1 = DetRng.Range(Seed, serial, engine.Value, RngStreams.EngineShotLanding, 0, 0, 256), u2 = DetRng.Range(Seed, serial, engine.Value, RngStreams.EngineShotLanding, 1, 0, 256);
        long lat = FightWorld.AimOffsetMm(range, sigma, u1);
        long along = range * errPm / 1000 * FightMath.InvNormal256[u2] / 1000;
        var (ux, uy) = FightMath.Norm(dx, dy);
        return (toX + (along * ux - lat * uy) / FightMath.One, toY + (along * uy + lat * ux) / FightMath.One);
    }

    /// <summary>The ram's blow in milli kg m/s: its beam's mass x its swing, x1.2 with an iron head built on it (the part's own mass: 500 kg x 3 m/s = 1,500, iron-headed 1,800).</summary>
    public long RamMomentumMilli(VehicleRow v)
    {
        long mass = 0; bool head = false;
        foreach (var p in v.Parts)
        {
            if (p.State != PartState.Built) continue;
            var d = Cat.PartList[p.Part];
            if (d.Tags.Contains("ram")) mass += d.MassKg;
            if (d.Tags.Contains("iron_head")) head = true;
        }
        long m = mass * Stat(v).SwingMmps;
        return head ? m * Table.Rules.IronHeadPm / 1000 : m;
    }

    /// <summary>The 1 m cell of a world coordinate in mm (floor, so negative coordinates are right).</summary>
    public static int CellOf(long mm) => (int)(mm >= 0 ? mm / 1000 : -((-mm + 999) / 1000));

    private EngineStatDef Stat(VehicleRow v) => Table.Stat(v.Design)!;

    private void Fire(Engine e, long at, EngineWorld w)
    {
        var st = Stat(e); var v = VehicleOf(e)!;
        uint serial = e.Serial++; e.Fired++;
        long lx = e.AimX, ly = e.AimY, momentum; string note;
        if (e.Kind == EngineKind.Ram) { momentum = RamMomentumMilli(v); note = "ch_note_sg_ram"; }
        else
        {
            (lx, ly) = Landing(e.Vehicle, serial, v.XMm, v.YMm, e.AimX, e.AimY, Engineering(e, w), st);
            momentum = (long)st.ShotG * st.ShotMmps / 1000;
            note = e.Kind == EngineKind.Trebuchet ? "ch_note_sg_stone" : "ch_note_sg_bolt";
            if (st.Ammo.Length > 0 && w.AmmoOf?.Invoke(e) is { } store && w.Goods != null)
            {
                ushort g = w.Goods.IdOf(st.Ammo); long unit = Math.Max(1, w.Goods.MassG(g, 1));
                store.Destroy(g, Math.Min(store.Qty(g), (st.ShotG + unit - 1) / unit), DestroyReason.Consumed);
            }
        }
        var rep = Impact(lx, ly, e.AimZ, momentum, note, at, w, e.Vehicle.Value ^ ((ulong)serial << 40));
        w.OnShot?.Invoke(e, rep);
    }

    /// <summary>
    /// A blow of <paramref name="momentumMilli"/> (milli kg m/s) at the point (x, y) on level z at game second <paramref name="sec"/>: the wall cell takes SG-A's damage from the whole kg m/s, everyone within the splash is struck through
    /// <see cref="EngineWorld.People"/> (the resolver clamps a blow's momentum, which is far more than any armour stops), and the parts of every vehicle whose footprint holds the point break from the top of the cell down (a stone) or from the level it flew along down (a bolt, a ram).
    /// </summary>
    public ShotReport Impact(long x, long y, int z, long momentumMilli, string note, long sec, EngineWorld w, ulong key)
    {
        var wall = WallBlowKind.NotAWall;
        if (w.Walls != null)
        {
            int cx = CellOf(x), cy = CellOf(y);
            wall = w.Walls.Strike(cx, cy, z, momentumMilli / 1000, sec, w.Cause, note).Kind;
        }
        int people = w.People?.Invoke(x, y, Table.Rules.SplashMm, momentumMilli, key, note == "ch_note_sg_bolt") ?? 0;
        int broken = 0, engines = 0;
        foreach (var v in Rides.Vehicles.Rows.ToList())
        {
            if (CellAt(v, x, y) is not { } c) continue;
            engines++;
            broken += StrikeCell(v, c.X, c.Y, momentumMilli / 1000, sec, w, note == "ch_note_sg_stone" ? int.MaxValue : z);
        }
        return new ShotReport(x, y, wall, people, broken, engines);
    }
}
