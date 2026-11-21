using Ravensguard.Sim.Combat.Melee2;

namespace Ravensguard.Sim.Battle;

/// <summary>What the renderer needs of a fighter, as flags (P7-BB-E). Rendering only: nothing in the sim reads a snapshot.</summary>
[Flags]
public enum SnapFlags : byte { None = 0, Blood = 1, Hurt = 2, LegWound = 4, Routing = 8, Dead = 16, Down = 32, Surrendered = 64, Gone = 128 }

/// <summary>One formation as the Command band draws it: the centre of the men still standing, the extent along and across the facing (mm), the counts.</summary>
public readonly record struct FormationMark(int Row, int Team, FormationKind Kind, int CentreX, int CentreY, int FaceX, int FaceY, int FrontMm, int DepthMm, int Active, int Total, int Routing, byte Role);

/// <summary>
/// The immutable picture of a fight for the renderer (plan 2.4: rendering reads a snapshot published once per tick, never the live fighters; outcomes never depend on the camera).
/// Struct-of-arrays indexed by fighter id. <see cref="Capture"/> only reads the world; the caller must not hand a snapshot to the renderer and then capture into it again (double-buffer:
/// capture into the spare, publish, swap). The troop role is derived from the kit (the fight has no role field): see <see cref="RoleOf"/>.
/// </summary>
public sealed class BattleSnapshot
{
    /// <summary>The troop roles of <c>war/troops.jsonc</c> that a fighter's kit can tell apart, in the order the art bakes them (index into <c>TroopMinis.Roles</c>).</summary>
    public const byte Levy = 0, Spearman = 1, Archer = 2, Crossbowman = 3, Sergeant = 4, ManAtArms = 5, Knight = 6, Engineer = 7, Sailor = 8, Marine = 9, RoleCount = 10;

    public int Tick, Count, FormCount;
    public int[] X = [], Y = [], Fx = [], Fy = [], Team = [], Form = [];
    public byte[] Role = [], Act = [], Flags = [];
    public FormationMark[] Forms = [];

    /// <summary>The troop role a kit looks like: a crossbow, a bow, a polearm with a shield, heavy armour, a shield with a blade, else a levy. A caller who knows better (an army's roster) passes <c>roleOf</c>.</summary>
    public static byte RoleOf(FightWorld w, Fighter f)
    {
        string? l = f.Kit.Launcher;
        if (l != null) return l.Contains("crossbow", StringComparison.Ordinal) ? Crossbowman : Archer;
        bool heavy = f.ArmourG > w.K.HeavyArmourKg * 1000;
        if (heavy) return f.Shield != null ? (f.ArmourG > w.K.HeavyArmourKg * 1500 ? Knight : ManAtArms) : Knight;
        if (f.Family == "polearms") return f.Shield != null ? Spearman : Levy;
        if (f.Shield != null) return f.ArmourG > 0 && f.ArmourG > w.K.HeavyArmourKg * 500 ? Sergeant : Marine;
        return Levy;
    }

    public static BattleSnapshot Capture(FightWorld w, BattleSnapshot? spare = null, Func<Fighter, byte>? roleOf = null)
    {
        var s = spare ?? new BattleSnapshot();
        int n = w.Fighters.Count;
        if (s.X.Length < n)
        {
            int cap = Math.Max(n, s.X.Length * 2);
            s.X = new int[cap]; s.Y = new int[cap]; s.Fx = new int[cap]; s.Fy = new int[cap]; s.Team = new int[cap]; s.Form = new int[cap];
            s.Role = new byte[cap]; s.Act = new byte[cap]; s.Flags = new byte[cap];
        }
        s.Tick = w.Tick; s.Count = n;
        for (int i = 0; i < n; i++)
        {
            var f = w.Fighters[i];
            s.X[i] = f.X; s.Y[i] = f.Y; s.Fx[i] = f.FX; s.Fy[i] = f.FY; s.Team[i] = f.Team; s.Form[i] = f.FmRow;
            s.Role[i] = roleOf != null ? roleOf(f) : RoleOf(w, f);
            s.Act[i] = (byte)f.Act;
            var fl = SnapFlags.None;
            var book = f.Book;
            if (book.Records.Count > 0 && book.OpenCount > 0) fl |= SnapFlags.Blood;
            if (book.Weakened || book.PainX100 > 2000) fl |= SnapFlags.Hurt;
            if (f.WalkPm < 1000 || !f.CanStand) fl |= SnapFlags.LegWound;
            if (f.Routing) fl |= SnapFlags.Routing;
            switch (f.Act)
            {
                case FAct.Dead: fl |= SnapFlags.Dead; break;
                case FAct.Down: case FAct.Asleep: fl |= SnapFlags.Down; break;
                case FAct.Surrendered: fl |= SnapFlags.Surrendered; break;
                case FAct.Fled: fl |= SnapFlags.Gone; break;
            }
            if (f.Crawling) fl |= SnapFlags.Down;
            s.Flags[i] = (byte)fl;
        }
        s.CaptureForms(w);
        return s;
    }

    private void CaptureForms(FightWorld w)
    {
        var rows = w.Formations?.Rows;
        int m = rows?.Count ?? 0;
        if (Forms.Length < m) Forms = new FormationMark[Math.Max(m, Forms.Length * 2)];
        FormCount = 0;
        if (rows == null) return;
        foreach (var r in rows)
        {
            long sx = 0, sy = 0; int act = 0, rout = 0;
            foreach (int id in r.Members)
            {
                var f = w.Fighters[id];
                if (!f.Active) continue;
                act++; sx += f.X; sy += f.Y; if (f.Routing) rout++;
            }
            if (act == 0) continue;
            int cx = (int)(sx / act), cy = (int)(sy / act), fx = r.FaceX, fy = r.FaceY;
            int minL = int.MaxValue, maxL = int.MinValue, minB = int.MaxValue, maxB = int.MinValue;
            int[] cnt = new int[RoleCount];
            foreach (int id in r.Members)
            {
                var f = w.Fighters[id];
                if (!f.Active) continue;
                int dx = f.X - cx, dy = f.Y - cy;
                int lat = (int)(((long)-fy * dx + (long)fx * dy) >> 10), back = (int)(((long)fx * dx + (long)fy * dy) >> 10);
                if (lat < minL) minL = lat; if (lat > maxL) maxL = lat; if (back < minB) minB = back; if (back > maxB) maxB = back;
                cnt[Role[id]]++;
            }
            byte role = 0; for (byte k = 1; k < RoleCount; k++) if (cnt[k] > cnt[role]) role = k;
            Forms[FormCount++] = new FormationMark(r.Index, r.Side, r.Kind, cx, cy, fx, fy, Math.Max(1000, maxL - minL + 1000), Math.Max(1000, maxB - minB + 1000), act, r.Members.Length, rout, role);
        }
    }
}
