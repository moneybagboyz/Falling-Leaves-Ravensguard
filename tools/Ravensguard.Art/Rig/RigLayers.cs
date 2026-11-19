namespace Ravensguard.Art.Rig;

/// <summary>A pixel inside a piece: grid cell (I,J of W x H, in bone-local units), screen pixel and view.</summary>
public readonly record struct Cell(int I, int J, int W, int H, int Sx, int Sy, View V);

public delegate uint Shader(in Cell c);

/// <summary>Draws one layer of the paper-doll. Pieces are bone-local shapes sampled per pixel (nearest, never blurred).</summary>
public sealed class Ctx
{
    public readonly Skeleton S;
    public readonly View V;
    public readonly PixelBuffer[] Planes = { new(32, 64), new(32, 64), new(32, 64) };
    /// <summary>P4-C: the same pixels split by limb group (0 the rest; 1 left forearm and hand, 2 right forearm and hand, 3 left shin and foot, 4 right shin and foot) per depth
    /// plane: index = plane + 3 x group. The game draws these instead of <see cref="Planes"/> to leave a lost limb out (a stump overlay covers the end).</summary>
    public readonly PixelBuffer[] Split = Enumerable.Range(0, 15).Select(_ => new PixelBuffer(32, 64)).ToArray();

    public static int GroupOf(Bone b) => b switch
    {
        Bone.LArmL or Bone.HandL => 1, Bone.LArmR or Bone.HandR => 2, Bone.LLegL or Bone.FootL => 3, Bone.LLegR or Bone.FootR => 4, _ => 0,
    };
    readonly List<(int Plane, int Order, Bone Bone, Box Box, Shader Sh, BoneFrame? Frame)> _q = new();
    int _seq;

    public Ctx(Skeleton s) { S = s; V = s.V; }

    static readonly Bone[] FrontOrder =
    {
        Bone.ULegL, Bone.LLegL, Bone.FootL, Bone.ULegR, Bone.LLegR, Bone.FootR, Bone.Pelvis, Bone.Torso, Bone.Head,
        Bone.UArmL, Bone.LArmL, Bone.HandL, Bone.UArmR, Bone.LArmR, Bone.HandR,
    };
    static readonly Bone[] SideOrder =
    {
        Bone.ULegL, Bone.LLegL, Bone.FootL, Bone.UArmL, Bone.LArmL, Bone.HandL,
        Bone.Pelvis, Bone.Torso, Bone.ULegR, Bone.LLegR, Bone.FootR, Bone.Head,
        Bone.UArmR, Bone.LArmR, Bone.HandR,
    };

    /// <summary>Depth plane a bone is drawn in: 0 far (behind the body), 1 body, 2 near (in front of the body).</summary>
    public static int PlaneOf(View v, Bone b)
    {
        if (v == View.Side)
        {
            if (Dims.IsLeft(b) && b != Bone.Head) return 0;
            return Dims.IsArm(b) ? 2 : 1;
        }
        return Dims.IsArm(b) ? 2 : 1;
    }

    int OrderOf(Bone b) => Array.IndexOf(V == View.Side ? SideOrder : FrontOrder, b);

    /// <summary>Adds a piece on a bone. Side box defaults to the front box (torso and pelvis narrow by a third).</summary>
    public void P(Bone b, Box f, Shader sh, Box? side = null, int? plane = null)
    {
        var bx = f;
        if (V == View.Side)
            bx = side ?? (b is Bone.Torso or Bone.Pelvis ? new Box(MathF.Round(f.X0 * 2 / 3), f.Y0, MathF.Round(f.X1 * 2 / 3), f.Y1) : f);
        _q.Add((plane ?? PlaneOf(V, b), OrderOf(b) * 100 + _seq++, b, bx, sh, null));
    }

    /// <summary>Piece in an arbitrary frame (weapons), always drawn in the given plane after bone pieces.</summary>
    public void Free(int plane, BoneFrame fr, Box bx, Shader sh) => _q.Add((plane, 100000 + _seq++, Bone.Pelvis, bx, sh, fr));

    public void Flush()
    {
        foreach (var q in _q.OrderBy(q => q.Plane).ThenBy(q => q.Order))
        {
            Fill(Planes[q.Plane], q.Frame ?? S.F[q.Bone], q.Box, q.Sh);
            Fill(Split[q.Plane + 3 * (q.Frame == null ? GroupOf(q.Bone) : 0)], q.Frame ?? S.F[q.Bone], q.Box, q.Sh);
        }
        _q.Clear();
    }

    void Fill(PixelBuffer buf, BoneFrame f, Box b, Shader sh)
    {
        float minX = 999, maxX = -999, minY = 999, maxY = -999;
        foreach (var (lx, ly) in new[] { (b.X0, b.Y0), (b.X1, b.Y0), (b.X0, b.Y1), (b.X1, b.Y1) })
        {
            var (x, y) = Skeleton.ToScreen(f, lx, ly);
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
        }
        int w = (int)MathF.Round(b.W), h = (int)MathF.Round(b.H);
        for (int py = Math.Max(0, (int)MathF.Floor(minY) - 1); py <= Math.Min(63, (int)MathF.Ceiling(maxY) + 1); py++)
            for (int px = Math.Max(0, (int)MathF.Floor(minX) - 1); px <= Math.Min(31, (int)MathF.Ceiling(maxX) + 1); px++)
            {
                float cx = px + 0.5f - f.Ox, cy = py + 0.5f - f.Oy;
                float lx = (cx * f.Dy - cy * f.Dx) / f.Ws, ly = (cx * f.Dx + cy * f.Dy) / f.Scale;
                if (lx < b.X0 || lx >= b.X1 || ly < b.Y0 || ly >= b.Y1) continue;
                var cell = new Cell(Math.Min(w - 1, (int)MathF.Floor(lx - b.X0)), Math.Min(h - 1, (int)MathF.Floor(ly - b.Y0)), w, h, px, py, V);
                uint c = sh(cell);
                if (c != 0) buf.Data[py * 32 + px] = c;
            }
    }

    /// <summary>True when the pixel lies inside the given body bone's nominal box (used to keep fists visible over weapons).</summary>
    public bool InBone(Bone bone, int px, int py)
    {
        var f = S.F[bone];
        var (bf, bs) = Dims.Of(bone);
        var b = V == View.Side ? bs : bf;
        float cx = px + 0.5f - f.Ox, cy = py + 0.5f - f.Oy;
        float lx = (cx * f.Dy - cy * f.Dx) / f.Ws, ly = (cx * f.Dx + cy * f.Dy) / f.Scale;
        return lx >= b.X0 && lx < b.X1 && ly >= b.Y0 && ly < b.Y1;
    }
}

public abstract class RigLayer
{
    public string Id { get; }
    public virtual bool Has(Stance s) => true;
    protected RigLayer(string id) { Id = id; }
    public abstract void Render(Ctx c);
}

sealed class PieceLayer : RigLayer
{
    readonly Action<Ctx> _f;
    public PieceLayer(string id, Action<Ctx> f) : base(id) { _f = f; }
    public override void Render(Ctx c) => _f(c);
}

sealed class StanceLayer : RigLayer
{
    readonly Action<Ctx> _f; readonly Stance _s;
    public StanceLayer(string id, Stance s, Action<Ctx> f) : base(id) { _f = f; _s = s; }
    public override bool Has(Stance s) => s == _s;
    public override void Render(Ctx c) => _f(c);
}

/// <summary>The paper-doll layers in §2.6 draw order: body, under (hose, linen), over (tunic, gambeson), armour (mail, plate),
/// cover (surcoat, cloak), then hood, helm, shield, weapons. Each layer is baked on its own so the game can stack any outfit.</summary>
public static class RigLayers
{
    public static readonly string[] Order =
        { "body", "hose", "linen", "tunic", "gambeson", "mail", "plate", "surcoat", "cloak", "hood", "helm",
          // P4-C wound overlays (plan 2.7): drawn over the clothes, under the shield and weapon; the game adds the ones a fighter's wound book calls for
          "blood_head", "blood_torso", "blood_arm_l", "blood_arm_r", "blood_leg_l", "blood_leg_r",
          "bandage_head", "bandage_torso", "bandage_arm_l", "bandage_arm_r", "bandage_leg_l", "bandage_leg_r",
          "splint_arm_l", "splint_arm_r", "splint_leg_l", "splint_leg_r", "stump_arm_l", "stump_arm_r", "stump_leg_l", "stump_leg_r",
          "shield", "sword", "spear" };

    /// <summary>The wound overlay layers (not part of any outfit; the fight view stacks them per fighter).</summary>
    public static readonly string[] Overlays = Order.Where(o => o.StartsWith("blood_", StringComparison.Ordinal) || o.StartsWith("bandage_", StringComparison.Ordinal)
        || o.StartsWith("splint_", StringComparison.Ordinal) || o.StartsWith("stump_", StringComparison.Ordinal)).ToArray();

    static uint[] Rp(Palette pal, params string[] n) => n.Select(x => pal.P[x]).ToArray();

    /// <summary>Standard block shading, same rule as PixelBuffer.Shade (light from the top left).</summary>
    public static uint Std(uint[] r, in Cell c, bool topHi = false)
    {
        int n = r.Length, k = Math.Min(n - 2, Math.Max(1, n >> 1));
        if (c.I == 0) k = Math.Min(n - 1, k + 1);
        if (c.I >= c.W - 2) k = Math.Max(0, k - 1);
        if (c.J >= c.H - 1) k = Math.Max(0, k - 1);
        if (c.J == 0 && topHi) k = Math.Min(n - 1, k + 1);
        return r[k];
    }

    public static List<RigLayer> Build(Palette pal)
    {
        var P = pal.P; var R = pal.R;
        var skin = R["skin"]; var brown = R["brown"]; var boots = Rp(pal, "w1", "w1", "w2", "w3"); var iron = R["iron"]; var linen = R["linen"];
        var l = new List<RigLayer>();

        // ---- body: skin, face, hair
        var hairC = P["hbr"]; var hairD = P["hb"];
        l.Add(new PieceLayer("body", c =>
        {
            Shader S(uint[] r, bool top = false) { var rr = r; return (in Cell k) => Std(rr, k, top); }
            c.P(Bone.ULegL, new(-2, 0, 2, 9), S(skin)); c.P(Bone.ULegR, new(-2, 0, 2, 9), S(skin));
            c.P(Bone.LLegL, new(-2, 0, 2, 8), S(skin)); c.P(Bone.LLegR, new(-2, 0, 2, 8), S(skin));
            c.P(Bone.Pelvis, new(-6, 0, 6, 3), S(skin));
            c.P(Bone.Torso, new(-6, -17, 6, 0), S(skin));
            foreach (var b in new[] { Bone.UArmL, Bone.UArmR }) c.P(b, new(-1.5f, 0, 1.5f, 8), S(skin));
            foreach (var b in new[] { Bone.LArmL, Bone.LArmR }) c.P(b, new(-1.5f, 0, 1.5f, 7), S(skin));
            foreach (var b in new[] { Bone.HandL, Bone.HandR }) c.P(b, new(-1.5f, 0, 1.5f, 3), (in Cell k) => k.J == 2 ? skin[1] : skin[2]);
            c.P(Bone.Head, new(-2, -3, 2, 0), (in Cell k) => k.J == 0 ? skin[0] : skin[1]);
            bool side = c.V == View.Side, back = c.V == View.Back;
            c.P(Bone.Head, new(-5, -15, 5, -3), (in Cell k) =>
            {
                if ((k.J == 0 || k.J == 11) && (k.I == 0 || k.I == 9)) return 0;
                if (!back && !side && k.J == 6 && (k.I == 2 || k.I == 7)) return P["out"];
                if (side && k.J == 6 && k.I == 7) return P["out"];
                if (!back && !side && k.J == 9 && (k.I == 4 || k.I == 5)) return skin[0];
                if (side && k.J == 9 && k.I >= 6) return skin[0];
                return Std(skin, k, true);
            });
            if (back)
                c.P(Bone.Head, new(-6, -16, 6, -5), (in Cell k) =>
                {
                    if ((k.J == 0) && (k.I < 2 || k.I > 9)) return 0;
                    if (k.J >= 10 && (k.I == 0 || k.I == 11)) return 0;
                    return k.J >= k.H - 2 || k.I >= k.W - 2 ? hairD : (((k.I + k.J) % 7 == 3) ? hairD : hairC);
                });
            else if (side)
            {
                c.P(Bone.Head, new(-5, -16, 5, -13), (in Cell k) => (k.J == 0 && (k.I == 0 || k.I == 9)) ? 0u : (k.J == 2 && k.I > 6 ? hairD : hairC));
                c.P(Bone.Head, new(-6, -15, -1, -7), (in Cell k) => (k.J == 0 && k.I == 0) ? 0u : (k.I < 2 || k.J > 5 ? hairD : hairC));
            }
            else
            {
                c.P(Bone.Head, new(-5, -16, 5, -13), (in Cell k) => (k.J == 0 && (k.I == 0 || k.I == 9)) ? 0u : (k.J == 2 && (k.I < 2 || k.I > 7) ? hairD : hairC));
                c.P(Bone.Head, new(-6, -15, -5, -10), (in Cell k) => hairD);
                c.P(Bone.Head, new(5, -15, 6, -10), (in Cell k) => hairD);
            }
            if (!side)
            {
                c.P(Bone.Head, new(-6, -10, -5, -9), (in Cell k) => skin[2]);
                c.P(Bone.Head, new(5, -10, 6, -9), (in Cell k) => skin[1]);
            }
            else
            {
                c.P(Bone.Head, new(5, -9, 6, -7), (in Cell k) => skin[1]);
                c.P(Bone.Head, new(-2, -10, 0, -8), (in Cell k) => skin[0]);
            }
        }));

        // ---- under layer: hose and boots, linen shirt
        l.Add(new PieceLayer("hose", c =>
        {
            foreach (var b in new[] { Bone.ULegL, Bone.ULegR }) c.P(b, new(-2, 0, 2, 9), (in Cell k) => Std(brown, k));
            foreach (var b in new[] { Bone.LLegL, Bone.LLegR })
                c.P(b, new(-2, 0, 2, 8), (in Cell k) => k.J < 3 ? Std(brown, k) : Std(boots, k with { J = k.J - 3, H = 5 }));
            foreach (var b in new[] { Bone.FootL, Bone.FootR })
                c.P(b, new(-2, 0, 2, 5), (in Cell k) => Std(boots, k), new Box(-2, 0, 5, 5));
            c.P(Bone.Pelvis, new(-6, 0, 6, 3), (in Cell k) => Std(brown, k));
        }));
        l.Add(new PieceLayer("linen", c =>
        {
            bool front = c.V == View.Front;
            c.P(Bone.Torso, new(-6, -17, 6, 0), (in Cell k) =>
            {
                if (front && ((k.J == 0 && k.I is >= 4 and <= 7) || (k.J == 1 && k.I is 5 or 6))) return 0;
                return Std(linen, k);
            });
            c.P(Bone.Pelvis, new(-6, 0, 6, 2), (in Cell k) => Std(linen, k));
            foreach (var b in new[] { Bone.UArmL, Bone.UArmR }) c.P(b, new(-1.5f, 0, 1.5f, 8), (in Cell k) => Std(linen, k));
        }));

        // ---- over layer: tunic (recolour via ramp swap), gambeson
        var tun = R["green"];
        l.Add(new PieceLayer("tunic", c =>
        {
            bool front = c.V == View.Front;
            c.P(Bone.Torso, new(-6, -17, 6, 0), (in Cell k) =>
            {
                if (k.J == 15) return P["br1"];
                if (front && k.J == 15 - 0 && k.I == 5) return P["au3"];
                return Std(tun, k);
            });
            c.P(Bone.Pelvis, new(-6, 0, 6, 3), (in Cell k) => Std(tun, k));
            foreach (var b in new[] { Bone.ULegL, Bone.ULegR }) c.P(b, new(-3, 0, 3, 6), (in Cell k) => k.J == 5 ? tun[0] : Std(tun, k));
            foreach (var b in new[] { Bone.UArmL, Bone.UArmR }) c.P(b, new(-1.5f, 0, 1.5f, 8), (in Cell k) => Std(tun, k));
        }));
        l.Add(new PieceLayer("gambeson", c =>
        {
            Shader Q = (in Cell k) => (k.J % 3 == 2 && k.J < k.H - 1) ? P["ln1"] : Std(linen, k);
            c.P(Bone.Torso, new(-6, -17, 6, 1), Q);
            c.P(Bone.Pelvis, new(-6, 0, 6, 3), Q);
            foreach (var b in new[] { Bone.ULegL, Bone.ULegR }) c.P(b, new(-3, 0, 3, 7), Q);
            foreach (var b in new[] { Bone.UArmL, Bone.UArmR }) c.P(b, new(-1.5f, 0, 1.5f, 8), Q);
            foreach (var b in new[] { Bone.LArmL, Bone.LArmR }) c.P(b, new(-1.5f, 0, 1.5f, 7), Q);
        }));

        // ---- armour: mail, plate
        l.Add(new PieceLayer("mail", c =>
        {
            Shader M = (in Cell k) =>
            {
                bool odd = ((k.Sx + k.Sy) & 1) != 0;
                if (k.J == 0 && k.W >= 10) return P["m4"];
                if (k.I == 0) return odd ? P["m4"] : P["m3"];
                if (k.I >= k.W - 2) return odd ? P["m2"] : P["m1"];
                return odd ? P["m3"] : P["m2"];
            };
            c.P(Bone.Torso, new(-6, -17, 6, 1), M);
            c.P(Bone.Pelvis, new(-6, 0, 6, 3), M);
            foreach (var b in new[] { Bone.ULegL, Bone.ULegR }) c.P(b, new(-3, 0, 3, 6), M);
            foreach (var b in new[] { Bone.UArmL, Bone.UArmR }) c.P(b, new(-1.5f, 0, 1.5f, 8), M);
            foreach (var b in new[] { Bone.LArmL, Bone.LArmR }) c.P(b, new(-1.5f, 0, 1.5f, 3), M);
        }));
        l.Add(new PieceLayer("plate", c =>
        {
            bool front = c.V == View.Front;
            c.P(Bone.Torso, new(-6, -17, 6, -3), (in Cell k) =>
            {
                if (k.J == 0) return k.I is 0 or 11 ? 0u : P["m4"];
                if (k.J % 5 == 4) return P["m2"];
                if (front && (k.I == 5 || k.I == 6)) return P["m5"];
                return Std(iron, k);
            });
            foreach (var b in new[] { Bone.UArmL, Bone.UArmR })
                c.P(b, new(-2.5f, -1, 2.5f, 5), (in Cell k) => (k.J == 0 && (k.I == 0 || k.I == 4)) ? 0u : (k.J == 5 ? P["m2"] : Std(iron, k, true)));
            c.P(Bone.Pelvis, new(-6, 0, 6, 3), (in Cell k) => k.J == 2 ? P["m2"] : Std(iron, k));
            foreach (var b in new[] { Bone.ULegL, Bone.ULegR }) c.P(b, new(-3, 0, 3, 4), (in Cell k) => Std(iron, k));
            foreach (var b in new[] { Bone.LArmL, Bone.LArmR }) c.P(b, new(-1.5f, 0, 1.5f, 6), (in Cell k) => Std(iron, k));
        }));

        // ---- cover: surcoat (tabard hangs from the pelvis), cloak
        var sur = R["red"];
        l.Add(new PieceLayer("surcoat", c =>
        {
            bool front = c.V == View.Front;
            c.P(Bone.Torso, new(-5, -14, 5, 0), (in Cell k) =>
            {
                if (front && ((k.I is 4 or 5 && k.J is >= 4 and <= 8) || (k.I is >= 2 and <= 7 && k.J is 5 or 6) || (k.I is 3 or 6 && k.J == 3))) return P["out"];
                return Std(sur, k);
            });
            c.P(Bone.Pelvis, new(-5, 0, 5, 11), (in Cell k) => k.J == 0 ? P["br1"] : (k.J == 10 ? sur[0] : Std(sur, k)));
        }));
        var cl = R["blue"];
        l.Add(new PieceLayer("cloak", c =>
        {
            Shader C = (in Cell k) => k.J >= k.H - 1 ? cl[0] : ((k.I + k.J / 4) % 6 == 5 ? cl[1] : Std(cl, k));
            if (c.V == View.Back)
            {
                c.P(Bone.Torso, new(-7, -17, 7, 0), C, plane: 1);
                c.P(Bone.Pelvis, new(-7, 0, 7, 19), C, plane: 1);
            }
            else
            {
                c.P(Bone.Torso, new(-8, -16, 8, 0), C, new Box(-9, -16, 3, 0), plane: 0);
                c.P(Bone.Pelvis, new(-8, 0, 8, 17), C, new Box(-9, 0, 3, 17), plane: 0);
                if (c.V == View.Front)
                    c.P(Bone.Torso, new(-6, -17, 6, -13), (in Cell k) => k.J == 3 ? cl[0] : (k.I is 5 or 6 && k.J == 2 ? P["au3"] : Std(cl, k)), plane: 1);
            }
        }));

        // ---- head gear: hood, kettle helm
        var hood = R["brown"];
        l.Add(new PieceLayer("hood", c =>
        {
            bool side = c.V == View.Side, back = c.V == View.Back;
            c.P(Bone.Head, new(-7, -17, 7, 5), (in Cell k) =>
            {
                if (k.J < 2 && (k.I < 3 || k.I > 10)) return 0;
                if (k.J >= 5 && k.J <= 14 && k.I >= 3 && k.I <= 10 && !back && !side) return 0;
                if (side && k.J >= 5 && k.J <= 14 && k.I >= 7) return 0;
                return hood[k.I < 3 ? 3 : k.I > 10 ? 1 : 2];
            }, new Box(-6, -17, 6, 5));
        }));
        l.Add(new PieceLayer("helm", c =>
        {
            c.P(Bone.Head, new(-6, -18, 6, -12), (in Cell k) => (k.J == 0 && (k.I == 0 || k.I == 11)) ? 0u : (k.I == 2 && k.J == 1 ? P["m5"] : Std(iron, k, true)));
            c.P(Bone.Head, new(-7, -12, 7, -11), (in Cell k) => P["m1"]);
            if (c.V != View.Back) c.P(Bone.Head, new(-1, -11, 1, -5), (in Cell k) => P["m3"], new Box(4, -11, 6, -6));
        }));

        // ---- wound overlays (P4-C, plan 2.7): blood, bandage, splint and the stump of a lost limb, one layer per body region
        uint b1 = P["r1"], b2 = P["r2"], ln = P["ln3"], lnD = P["ln1"], wd = P["w3"], wdD = P["w2"];
        Shader Blood = (in Cell k) => ((k.Sx * 7 + k.Sy * 3) % 5) switch { 0 => b2, 4 => 0u, _ => b1 };
        Shader Wrap = (in Cell k) => k.J % 2 == 0 ? ln : lnD;
        void Region(string id, Action<Ctx> f) => l.Add(new PieceLayer(id, f));
        Region("blood_head", c => c.P(Bone.Head, new(-4, -14, 2, -10), Blood));
        Region("blood_torso", c => { c.P(Bone.Torso, new(-4, -12, 3, -5), Blood); c.P(Bone.Pelvis, new(-1, 0, 2, 3), (in Cell k) => k.I == 1 ? b1 : 0u); });
        Region("blood_arm_l", c => { c.P(Bone.LArmL, new(-1.5f, 1, 1.5f, 6), Blood); c.P(Bone.HandL, new(-1, 0, 1, 2), (in Cell k) => b1); });
        Region("blood_arm_r", c => { c.P(Bone.LArmR, new(-1.5f, 1, 1.5f, 6), Blood); c.P(Bone.HandR, new(-1, 0, 1, 2), (in Cell k) => b1); });
        Region("blood_leg_l", c => { c.P(Bone.ULegL, new(-2, 2, 2, 8), Blood); c.P(Bone.LLegL, new(-1, 0, 1, 3), (in Cell k) => b1); });
        Region("blood_leg_r", c => { c.P(Bone.ULegR, new(-2, 2, 2, 8), Blood); c.P(Bone.LLegR, new(-1, 0, 1, 3), (in Cell k) => b1); });
        Region("bandage_head", c => c.P(Bone.Head, new(-6, -13, 6, -10), (in Cell k) => k.J == 1 && k.I == 3 ? b2 : Wrap(k)));
        Region("bandage_torso", c => c.P(Bone.Torso, new(-6.5f, -11, 6.5f, -6), (in Cell k) => k.J == 2 ? 0u : (k.J == 1 && k.I == 4 ? b2 : Wrap(k))));
        Region("bandage_arm_l", c => c.P(Bone.LArmL, new(-2, 1, 2, 6), (in Cell k) => k.J == 2 && k.I == 1 ? b2 : Wrap(k)));
        Region("bandage_arm_r", c => c.P(Bone.LArmR, new(-2, 1, 2, 6), (in Cell k) => k.J == 2 && k.I == 1 ? b2 : Wrap(k)));
        Region("bandage_leg_l", c => c.P(Bone.LLegL, new(-2.5f, 0, 2.5f, 5), (in Cell k) => k.J == 2 && k.I == 2 ? b2 : Wrap(k)));
        Region("bandage_leg_r", c => c.P(Bone.LLegR, new(-2.5f, 0, 2.5f, 5), (in Cell k) => k.J == 2 && k.I == 2 ? b2 : Wrap(k)));
        Shader Splint = (in Cell k) => k.J % 3 == 1 ? ln : (k.I == 0 || k.I == k.W - 1 ? (k.I == 0 ? wdD : wd) : 0u);
        Region("splint_arm_l", c => c.P(Bone.LArmL, new(-2.5f, 0, 2.5f, 7), Splint));
        Region("splint_arm_r", c => c.P(Bone.LArmR, new(-2.5f, 0, 2.5f, 7), Splint));
        Region("splint_leg_l", c => c.P(Bone.LLegL, new(-3, 0, 3, 8), Splint));
        Region("splint_leg_r", c => c.P(Bone.LLegR, new(-3, 0, 3, 8), Splint));
        Shader Stump = (in Cell k) => k.J >= k.H - 1 ? b1 : k.J >= k.H - 2 ? b2 : Wrap(k);
        Region("stump_arm_l", c => c.P(Bone.UArmL, new(-2, 5, 2, 9), Stump));
        Region("stump_arm_r", c => c.P(Bone.UArmR, new(-2, 5, 2, 9), Stump));
        Region("stump_leg_l", c => c.P(Bone.ULegL, new(-3, 6, 3, 10), Stump));
        Region("stump_leg_r", c => c.P(Bone.ULegR, new(-3, 6, 3, 10), Stump));

        // ---- round shield on the left forearm
        var shr = R["red"];
        l.Add(new PieceLayer("shield", c =>
        {
            var sk = c.S;
            var (cx, cy) = sk.Screen(sk.HandLCentre + sk.TorsoM.Apply(new V3(1.0f, 3, 3.5f)));
            var n = sk.TorsoM.Apply(new V3(0, 1, 0));
            float vis = c.V == View.Side ? MathF.Abs(n.X) : MathF.Abs(n.Y);
            float rx = Math.Max(1.6f, 5.5f * vis), ry = 5.5f;
            int icx = (int)MathF.Floor(cx + 0.5f), icy = (int)MathF.Floor(cy + 0.5f);
            bool back = c.V == View.Back;
            int plane = c.V == View.Front ? 2 : 0;
            for (int py = icy - 7; py <= icy + 7; py++)
                for (int px = icx - 7; px <= icx + 7; px++)
                {
                    float dx = (px + 0.5f - icx) / rx, dy = (py + 0.5f - icy) / ry;
                    float d = dx * dx + dy * dy;
                    if (d > 1.0f) continue;
                    uint col;
                    float edge = 1 - MathF.Sqrt(d);
                    if (edge < 1.1f / 6f) col = (dx + dy > 0.3f) ? P["m2"] : P["m3"];
                    else if (back) col = (px + py) % 4 == 0 ? P["w2"] : P["w3"];
                    else if (MathF.Abs(dx * rx) < 1.6f && MathF.Abs(dy * ry) < 1.6f) col = (dx + dy < 0) ? P["au3"] : P["au2"];
                    else col = dx * rx + dy * ry < -2 ? shr[3] : dx * rx + dy * ry > 2.5f ? shr[1] : shr[2];
                    c.Planes[plane].Set(px, py, col);
                }
        }));

        // ---- weapons in the right hand
        l.Add(new StanceLayer("sword", Stance.Blade, c =>
        {
            var (o, m) = c.S.WeaponFrame();
            var fr = Frame(c.S, o, m);
            Shader noHand(Shader s) => (in Cell k) => c.InBone(Bone.HandR, k.Sx, k.Sy) ? 0u : s(k);
            c.Free(2, fr, new(-1, -5, 1, -4), noHand((in Cell k) => P["au3"]));
            c.Free(2, fr, new(-1, -4, 1, 2), noHand((in Cell k) => k.I == 0 ? P["w3"] : P["w2"]));
            c.Free(2, fr, new(-3, 2, 3, 3), (in Cell k) => k.I is 0 or 5 ? P["au2"] : P["au3"]);
            c.Free(2, fr, new(-1, 3, 1, 17), (in Cell k) => (k.J == 13 && k.I == 1) ? 0u : (k.I == 0 ? P["m5"] : P["m4"]));
        }));
        l.Add(new StanceLayer("spear", Stance.Pole, c =>
        {
            var (o, m) = c.S.WeaponFrame();
            var fr = Frame(c.S, o, m);
            Shader noHand(Shader s) => (in Cell k) => c.InBone(Bone.HandR, k.Sx, k.Sy) ? 0u : s(k);
            c.Free(2, fr, new(-1, -9, 1, 21), noHand((in Cell k) => k.I == 0 ? P["w4"] : P["w3"]));
            c.Free(2, fr, new(-1, 20, 1, 21), (in Cell k) => P["au2"]);
            c.Free(2, fr, new(-2, 21, 2, 28), (in Cell k) =>
                k.J switch { <= 2 => P["m4"], <= 4 => (k.I is 1 or 2 ? P["m4"] : 0u), 5 => (k.I == 1 ? P["m5"] : 0u), _ => 0u });
        }));
        return l;
    }

    static BoneFrame Frame(Skeleton s, V3 o, M3 m)
    {
        var f = s.MakeFrame(o, m);
        f.Ox = MathF.Floor(f.Ox + s.BaseX + 0.5f);
        f.Oy = MathF.Floor(f.Oy + 38 + s.GroundShift + 0.5f);
        return f;
    }
}
