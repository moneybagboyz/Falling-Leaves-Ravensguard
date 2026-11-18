namespace Ravensguard.Art.Rig;

/// <summary>Three drawn views. The fourth direction (west) is the side view mirrored; the side view faces east.</summary>
public enum View { Front, Back, Side }

public enum Stance { Blade, Pole }

public enum Bone
{
    Pelvis, Torso, Head,
    UArmL, LArmL, HandL, UArmR, LArmR, HandR,
    ULegL, LLegL, FootL, ULegR, LLegR, FootR,
}

/// <summary>Rotation angles of one limb in degrees. Swing = forward/back at the shoulder or hip, Abd = outward,
/// Flex = elbow (forearm forward) or knee (shin back), End = wrist or ankle.</summary>
public struct Limb
{
    public float Swing, Abd, Flex, End;
    public Limb(float swing = 0, float abd = 0, float flex = 0, float end = 0) { Swing = swing; Abd = abd; Flex = flex; End = end; }
}

/// <summary>One rig pose. Body frame: X right, Y forward, Z up. Weapon angles are relative to the torso: swing 0 = tip down,
/// 90 = tip forward, 180 = tip up; abd tilts outward (90 = across the body).</summary>
public sealed class Pose
{
    public float Yaw, Roll, Pitch;              // whole body (pelvis); Pitch > 0 tips the top backward (falling back)
    public float TYaw, TRoll, TLean;            // torso relative to pelvis; TLean > 0 leans forward
    public float HYaw, HPitch;                  // head; HPitch > 0 looks down
    public float Tilt;                          // projection tilt of the front and back views for this pose; 0 uses the standard 0.42 (a body lying down is seen from straight above: 1)
    public float Fwd;                           // pixels the whole figure is shifted forward (+) or back (-), to keep long reaches in the frame
    public float UpperZ;                        // pixels torso, head and arms sit above (+) or below (-) the pelvis (breathing, crouch)
    public float Lift;                          // pixels the whole figure is raised off the ground after planting
    public Limb ArmL, ArmR, LegL, LegR;
    public float WSwing, WAbd;                  // weapon in the right hand
    public Pose Clone() => (Pose)MemberwiseClone();

    /// <summary>Left/right mirrored copy (the second half of a walk cycle).</summary>
    public Pose Mirror()
    {
        var p = Clone();
        p.Yaw = -Yaw; p.Roll = -Roll; p.TYaw = -TYaw; p.TRoll = -TRoll; p.HYaw = -HYaw;
        p.ArmL = ArmR; p.ArmR = ArmL; p.LegL = LegR; p.LegR = LegL;
        return p;
    }
}

public readonly record struct V3(float X, float Y, float Z)
{
    public static V3 operator +(V3 a, V3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static V3 operator -(V3 a, V3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static V3 operator *(V3 a, float s) => new(a.X * s, a.Y * s, a.Z * s);
}

public readonly struct M3
{
    readonly float a, b, c, d, e, f, g, h, i;
    M3(float a, float b, float c, float d, float e, float f, float g, float h, float i) { this.a = a; this.b = b; this.c = c; this.d = d; this.e = e; this.f = f; this.g = g; this.h = h; this.i = i; }
    public static readonly M3 Id = new(1, 0, 0, 0, 1, 0, 0, 0, 1);
    static (float s, float c) Sc(float deg) { double r = deg * Math.PI / 180; return ((float)Math.Sin(r), (float)Math.Cos(r)); }
    public static M3 Rx(float deg) { var (s, c) = Sc(deg); return new(1, 0, 0, 0, c, -s, 0, s, c); }
    public static M3 Ry(float deg) { var (s, c) = Sc(deg); return new(c, 0, s, 0, 1, 0, -s, 0, c); }
    public static M3 Rz(float deg) { var (s, c) = Sc(deg); return new(c, -s, 0, s, c, 0, 0, 0, 1); }
    public static M3 operator *(M3 m, M3 n) => new(
        m.a * n.a + m.b * n.d + m.c * n.g, m.a * n.b + m.b * n.e + m.c * n.h, m.a * n.c + m.b * n.f + m.c * n.i,
        m.d * n.a + m.e * n.d + m.f * n.g, m.d * n.b + m.e * n.e + m.f * n.h, m.d * n.c + m.e * n.f + m.f * n.i,
        m.g * n.a + m.h * n.d + m.i * n.g, m.g * n.b + m.h * n.e + m.i * n.h, m.g * n.c + m.h * n.f + m.i * n.i);
    public V3 Apply(V3 v) => new(a * v.X + b * v.Y + c * v.Z, d * v.X + e * v.Y + f * v.Z, g * v.X + h * v.Y + i * v.Z);
}

/// <summary>A rectangle in bone-local pixels: x across the bone, y along it (down for limbs, up is negative for torso and head).</summary>
public readonly record struct Box(float X0, float Y0, float X1, float Y1)
{
    public float W => X1 - X0;
    public float H => Y1 - Y0;
}

/// <summary>Screen-space frame of a bone: origin, unit direction of local +y, length scale and width scale.</summary>
public sealed class BoneFrame
{
    public float Ox, Oy, Dx, Dy, Scale = 1, Ws = 1;
    public V3 Pos3;          // continuous 3D origin (for attachments)
    public M3 M = M3.Id;
}

/// <summary>Nominal body dimensions per bone: front/back box and side box, plus the origin's pixel parity.</summary>
public static class Dims
{
    public static (Box F, Box S) Of(Bone b) => b switch
    {
        Bone.Pelvis => (new(-6, 0, 6, 3), new(-4, 0, 4, 3)),
        Bone.Torso => (new(-6, -17, 6, 0), new(-4, -17, 4, 0)),
        Bone.Head => (new(-5, -15, 5, -3), new(-5, -15, 5, -3)),
        Bone.UArmL or Bone.UArmR => (new(-1.5f, 0, 1.5f, 8), new(-1.5f, 0, 1.5f, 8)),
        Bone.LArmL or Bone.LArmR => (new(-1.5f, 0, 1.5f, 7), new(-1.5f, 0, 1.5f, 7)),
        Bone.HandL or Bone.HandR => (new(-1.5f, 0, 1.5f, 3), new(-1.5f, 0, 1.5f, 3)),
        Bone.ULegL or Bone.ULegR => (new(-2, 0, 2, 9), new(-2, 0, 2, 9)),
        Bone.LLegL or Bone.LLegR => (new(-2, 0, 2, 8), new(-2, 0, 2, 8)),
        _ => (new(-2, 0, 2, 5), new(-2, 0, 5, 5)),
    };
    public static bool IsArm(Bone b) => b is Bone.UArmL or Bone.LArmL or Bone.HandL or Bone.UArmR or Bone.LArmR or Bone.HandR;
    public static bool IsLeft(Bone b) => b is Bone.UArmL or Bone.LArmL or Bone.HandL or Bone.ULegL or Bone.LLegL or Bone.FootL;
    public static float Parity(Bone b) => IsArm(b) ? 0.5f : 0f;
}

/// <summary>Forward kinematics + orthographic projection of one pose into one view.</summary>
public sealed class Skeleton
{
    public const float Tilt = 0.42f, SideDepth = 0.12f;
    public const float UpperArm = 8, LowerArm = 7, HandLen = 3, UpperLeg = 9, LowerLeg = 8, TorsoLen = 17, ShoulderX = 7.5f, HipX = 3;
    public readonly Dictionary<Bone, BoneFrame> F = new();
    public readonly View V;
    public readonly Pose P;
    public M3 TorsoM, HandRM;
    public V3 HandLCentre, HandRCentre;
    public float GroundShift;
    /// <summary>Screen column of the body axis: 16 in front/back, 15 in the side view (more room to reach forward).</summary>
    public float BaseX => V == View.Side ? SideBase : 16;
    public const float SideBase = 15;
    float T => P.Tilt > 0 ? P.Tilt : Tilt;
    public (float X, float Y) Proj(V3 p) => V switch
    {
        View.Front => (-p.X, -p.Z + T * p.Y),
        View.Back => (p.X, -p.Z - T * p.Y),
        _ => (p.Y, -p.Z + SideDepth * p.X),
    };

    public Skeleton(Pose p, View v)
    {
        P = p; V = v;
        var down = new V3(0, 0, -1);
        var mp = M3.Rz(p.Yaw) * M3.Ry(p.Roll) * M3.Rx(p.Pitch);
        var o = new V3(0, p.Fwd, 0);
        Add(Bone.Pelvis, o, mp);
        var ot = o + mp.Apply(new V3(0, 0, p.UpperZ));
        var mt = mp * M3.Rz(p.TYaw) * M3.Ry(p.TRoll) * M3.Rx(-p.TLean);
        TorsoM = mt;
        Add(Bone.Torso, ot, mt);
        var neck = ot + mt.Apply(new V3(0, 0, TorsoLen));
        var mh = mt * M3.Rz(p.HYaw) * M3.Rx(p.HPitch);
        Add(Bone.Head, neck, mh);
        Arm(+1, ot, mt, p.ArmR, Bone.UArmR, Bone.LArmR, Bone.HandR);
        Arm(-1, ot, mt, p.ArmL, Bone.UArmL, Bone.LArmL, Bone.HandL);
        Leg(+1, o, mp, p.LegR, Bone.ULegR, Bone.LLegR, Bone.FootR);
        Leg(-1, o, mp, p.LegL, Bone.ULegL, Bone.LLegL, Bone.FootL);
        Ground();
    }

    void Arm(int s, V3 ot, M3 mt, Limb l, Bone u, Bone lo, Bone h)
    {
        var sh = ot + mt.Apply(new V3(s * ShoulderX, 0, TorsoLen - 2));
        var mu = mt * M3.Ry(-s * l.Abd) * M3.Rx(l.Swing);
        Add(u, sh, mu);
        var el = sh + mu.Apply(new V3(0, 0, -UpperArm));
        var ml = mu * M3.Rx(l.Flex);
        Add(lo, el, ml);
        var wr = el + ml.Apply(new V3(0, 0, -LowerArm));
        var mhd = ml * M3.Rx(l.End);
        Add(h, wr, mhd);
        var centre = wr + mhd.Apply(new V3(0, 0, -HandLen / 2));
        if (s > 0) { HandRCentre = centre; HandRM = mhd; } else HandLCentre = centre;
    }

    void Leg(int s, V3 o, M3 mp, Limb l, Bone u, Bone lo, Bone f)
    {
        var hip = o + mp.Apply(new V3(s * HipX, 0, 0));
        var mu = mp * M3.Ry(-s * l.Abd) * M3.Rx(l.Swing);
        Add(u, hip, mu);
        var kn = hip + mu.Apply(new V3(0, 0, -UpperLeg));
        var ml = mu * M3.Rx(-l.Flex);
        Add(lo, kn, ml);
        var an = kn + ml.Apply(new V3(0, 0, -LowerLeg));
        var mf = M3.Rz(P.Yaw) * M3.Rx(l.End);
        Add(f, an, mf);
    }

    /// <summary>Weapon frame in the right hand: direction relative to the torso.</summary>
    public (V3 Origin, M3 M) WeaponFrame()
        => (HandRCentre, TorsoM * M3.Ry(-P.WAbd) * M3.Rx(P.WSwing));

    public BoneFrame MakeFrame(V3 origin, M3 m, float parity = 0)
    {
        var f = new BoneFrame { Pos3 = origin, M = m };
        var (px, py) = Proj(origin);
        var dv = m.Apply(new V3(0, 0, -1));
        var (dx, dy) = Lin(dv);
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 0.3f) { dx = 0; dy = 1; len = 0.3f; } else { dx /= len; dy /= len; }
        f.Dx = dx; f.Dy = dy; f.Scale = len;
        var (rx, ry) = Lin(m.Apply(new V3(1, 0, 0)));
        var (fx, fy) = Lin(m.Apply(new V3(0, 1, 0)));
        float rl = MathF.Sqrt(rx * rx + ry * ry), fl = MathF.Sqrt(fx * fx + fy * fy);
        float r0 = V == View.Side ? SideDepth : 1, f0 = V == View.Side ? 1 : T;
        f.Ws = Math.Clamp(MathF.Sqrt(rl * rl * 1 + fl * fl * 1) / MathF.Sqrt(r0 * r0 + f0 * f0), 0.55f, 1.35f);
        f.Ox = px; f.Oy = py;
        return f;
    }

    (float, float) Lin(V3 d) => Proj(d);

    void Add(Bone b, V3 origin, M3 m) => F[b] = MakeFrame(origin, m);

    /// <summary>Plants the lowest body point on the ground row (y = 60) and snaps origins to the pixel grid.</summary>
    void Ground()
    {
        float maxY = -999;
        foreach (var (b, fr) in F)
        {
            var (bf, bs) = Dims.Of(b);
            var bx = V == View.Side ? bs : bf;
            foreach (var (lx, ly) in new[] { (bx.X0, bx.Y0), (bx.X1, bx.Y0), (bx.X0, bx.Y1), (bx.X1, bx.Y1) })
                maxY = Math.Max(maxY, ToScreen(fr, lx, ly).Y);
        }
        GroundShift = 60 - 38 - maxY - P.Lift;
        foreach (var (b, fr) in F)
        {
            float par = Dims.Parity(b);
            fr.Ox = MathF.Floor(fr.Ox + BaseX - par + 0.5f) + par;
            fr.Oy = MathF.Floor(fr.Oy + 38 + GroundShift + 0.5f);
        }
    }

    /// <summary>Screen position of a bone-local point (continuous).</summary>
    public static (float X, float Y) ToScreen(BoneFrame f, float lx, float ly)
        => (f.Ox + lx * f.Ws * f.Dy + ly * f.Scale * f.Dx, f.Oy - lx * f.Ws * f.Dx + ly * f.Scale * f.Dy);

    /// <summary>Screen position of a 3D point attached to the skeleton (after ground shift and snapping).</summary>
    public (float X, float Y) Screen(V3 p)
    {
        var (x, y) = Proj(p);
        return (x + BaseX, y + 38 + GroundShift);
    }
}
