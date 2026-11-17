namespace Ravensguard.Art.Rig;

/// <summary>Pose keyframes per action. Authored for the blade stance; the pole stance re-poses the right arm to hold a spear.
/// Angles are degrees, see <see cref="Pose"/>. Frames 2 and 3 of a cycle are the mirror of frames 0 and 1.</summary>
public static class RigPoses
{
    /// <summary>Actions named in the plan (§2.7) that are not posed yet. They are listed in the index so the game can fall back.</summary>
    public static readonly string[] Todo = { "parry", "stagger", "crawl", "carry", "work" };

    public static readonly string[] Actions = { "idle", "walk", "jog", "windup", "strike", "recover", "block", "hitreact", "fall" };

    static Limb L(float swing = 0, float abd = 0, float flex = 0, float end = 0) => new(swing, abd, flex, end);

    static Pose Idle0() => new()
    {
        ArmL = L(6, 6, 24), ArmR = L(5, 6, 14), LegL = L(0, 2), LegR = L(0, 2), WSwing = 14, WAbd = 8,
    };

    public static Pose[] Frames(string action, Stance st)
    {
        var f = Blade(action);
        if (st == Stance.Pole) f = f.Select((p, i) => Pole(action, i, p.Clone())).ToArray();
        return f;
    }

    static Pose[] Blade(string action)
    {
        switch (action)
        {
            case "idle":
            {
                var a = Idle0();
                var b = Idle0(); b.UpperZ = -1; b.ArmL = L(8, 7, 26); b.ArmR = L(7, 7, 16); b.HPitch = 3;
                return new[] { a, b };
            }
            case "walk":
            {
                var c = new Pose
                {
                    Yaw = -6, TYaw = 10, TLean = 3, LegR = L(24, 2, 4, 12), LegL = L(-22, 2, 16, -28),
                    ArmR = L(-20, 4, 10), ArmL = L(24, 4, 26), WSwing = 14, WAbd = 8,
                };
                var p = new Pose
                {
                    Yaw = 0, TYaw = 0, TLean = 3, LegR = L(3, 2, 8, 0), LegL = L(16, 2, 40, -12),
                    ArmR = L(-2, 4, 10), ArmL = L(4, 4, 24), WSwing = 14, WAbd = 8,
                };
                return new[] { c, p, c.Mirror(), p.Mirror() };
            }
            case "jog":
            {
                var c = new Pose
                {
                    Yaw = -7, TYaw = 12, TLean = 13, LegR = L(34, 2, 10, 14), LegL = L(-34, 2, 64, -38),
                    ArmR = L(-36, 5, 82), ArmL = L(40, 5, 86), WSwing = 40, WAbd = 8, HPitch = -6,
                };
                var p = new Pose
                {
                    Yaw = 0, TYaw = 0, TLean = 13, LegR = L(4, 2, 12, 0), LegL = L(50, 2, 88, -25),
                    ArmR = L(-6, 5, 66), ArmL = L(8, 5, 74), WSwing = 40, WAbd = 8, HPitch = -6,
                };
                return new[] { c, p, c.Mirror(), p.Mirror() };
            }
            case "windup":
                return new[]
                {
                    new Pose { Yaw = -8, TYaw = 18, TLean = -4, HYaw = -10, LegL = L(24, 3, 12), LegR = L(-16, 3, 14), ArmR = L(95, 22, 100), ArmL = L(35, 12, 50), WSwing = 175, WAbd = 5 },
                    new Pose { Yaw = -12, TYaw = 34, TLean = -12, HYaw = -16, LegL = L(28, 3, 14), LegR = L(-20, 3, 22), ArmR = L(62, 38, 150), ArmL = L(40, 16, 60), WSwing = 212, WAbd = 10, UpperZ = -1 },
                };
            case "strike":
                return new[]
                {
                    new Pose { Yaw = 6, TYaw = -14, TLean = 12, HYaw = 6, LegL = L(32, 3, 22), LegR = L(-26, 3, 8), ArmR = L(100, 10, 40), ArmL = L(20, 20, 40), WSwing = 118, WAbd = 4 },
                    new Pose { Yaw = 12, TYaw = -26, TLean = 24, HYaw = 10, LegL = L(38, 3, 32), LegR = L(-32, 3, 6), ArmR = L(72, 6, 14), ArmL = L(-10, 30, 40), WSwing = 58, WAbd = 4, UpperZ = -1 },
                };
            case "recover":
                return new[] { new Pose { Yaw = 4, TYaw = -8, TLean = 8, LegL = L(18, 3, 14), LegR = L(-14, 3, 10), ArmR = L(45, 8, 40), ArmL = L(20, 10, 40), WSwing = 46, WAbd = 6 } };
            case "block":
                return new[] { new Pose { Yaw = -4, TYaw = 8, TLean = 8, HPitch = 6, LegL = L(26, 3, 30), LegR = L(-14, 3, 26), ArmL = L(78, 2, 62), ArmR = L(50, 12, 80), WSwing = 135, WAbd = 15, UpperZ = -1 } };
            case "hitreact":
                return new[] { new Pose { Yaw = 8, TLean = -20, HPitch = -14, UpperZ = -1, LegL = L(-10, 4, 8), LegR = L(16, 4, 10), ArmL = L(35, 35, 30), ArmR = L(30, 40, 40), WSwing = 40, WAbd = 40 } };
            case "fall":
                return new[]
                {
                    new Pose { Pitch = 18, TLean = -8, HPitch = -8, LegL = L(15, 2, 20), LegR = L(-5, 2, 10), ArmL = L(30, 40, 20), ArmR = L(40, 40, 20), WSwing = 70, WAbd = 40 },
                    new Pose { Pitch = 10, TLean = -14, HPitch = -14, UpperZ = -1, LegL = L(28, 3, 72), LegR = L(10, 3, 60), ArmL = L(45, 55, 10), ArmR = L(50, 55, 10), WSwing = 90, WAbd = 60 },
                    // the frame is 32 px wide, so a body lying flat cannot fit: the last frame is a collapse onto the knees
                    new Pose { TLean = 36, HPitch = 24, UpperZ = -1, LegL = L(4, 6, 104), LegR = L(-2, 6, 100), ArmL = L(40, 14, 30), ArmR = L(38, 14, 30), WSwing = 60, WAbd = 20 },
                };
            default: throw new ArgumentException("unknown action " + action);
        }
    }

    /// <summary>Right arm holds a spear; the rest of the body keeps the blade-stance motion.</summary>
    static Pose Pole(string action, int i, Pose p)
    {
        switch (action)
        {
            case "idle": case "walk": case "jog":
                p.ArmR = L(20, 10, 40); p.WSwing = 176; p.WAbd = 3; break;
            case "windup":
                if (i == 0) { p.ArmR = L(12, 22, 70); p.WSwing = 112; p.WAbd = 0; }
                else { p.ArmR = L(-8, 28, 58); p.WSwing = 96; p.WAbd = 0; }
                break;
            case "strike":
                if (i == 0) { p.ArmR = L(62, 4, 12); p.WSwing = 92; p.WAbd = 0; }
                else { p.ArmR = L(86, 2, 2); p.WSwing = 86; p.WAbd = 0; }
                break;
            case "recover": p.ArmR = L(40, 10, 40); p.WSwing = 128; p.WAbd = 0; break;
            case "block": p.ArmR = L(60, -8, 70); p.WSwing = 8; p.WAbd = 82; break;
            case "hitreact": p.WSwing = 150; p.WAbd = 30; break;
            case "fall": p.WSwing = i == 0 ? 150 : i == 1 ? 115 : 92; p.WAbd = i * 30; break;
        }
        return p;
    }
}
