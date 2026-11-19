using MessagePack;
using Ravensguard.Content;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Flavour;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Resolver2;

namespace Ravensguard.Sim.Save;

/// <summary>One fighter of a saved log: who stood in the fight and the numbers of the report.</summary>
[MessagePackObject]
public sealed class LogFighterRow
{
    [Key(0)] public int Id { get; set; } [Key(1)] public string Name { get; set; } = ""; [Key(2)] public string Kind { get; set; } = "human"; [Key(3)] public string PlanId { get; set; } = "human";
    [Key(4)] public ulong Person { get; set; }
    /// <summary>Team, player, female, end state (bit packed) then the counters, in the order of <c>FightLogSave.Capture</c>.</summary>
    [Key(5)] public long[] N { get; set; } = [];
}

/// <summary>One blow's layer trace and wounds: a header, then the steps and wounds as flat integers.</summary>
[MessagePackObject]
public sealed class LogBlowRow
{
    [Key(0)] public int Id { get; set; }
    [Key(1)] public long[] H { get; set; } = [];
    [Key(2)] public ushort[] PieceShapes { get; set; } = []; [Key(3)] public ushort[] PieceMaterials { get; set; } = [];
    [Key(4)] public long[] Steps { get; set; } = [];
    [Key(5)] public long[] Wounds { get; set; } = [];
}

/// <summary>
/// The optional "fightlog" section (feedback-flavour-log 9 F7, 10): the log of the fight in progress or the last fight, so the after-fight report and the chronicle's cause chains survive a
/// save and a load. Written only while a log exists; absent otherwise, so no other save changes (no format bump). The newest 2,000 lines and up to 1,000 older serious ones are kept; the
/// formatter's picks are saved with the lines, so the prose reads the same after the load (the <c>FlavourHash</c>, test FL-13). Nothing here is read by the simulation.
/// </summary>
[MessagePackObject]
public sealed class FightLogSave
{
    public const string SectionName = "fightlog";
    public const int KeepRecent = FightLog.SaveRecent, KeepSerious = 1000;

    [Key(0)] public ulong Seed { get; set; } [Key(1)] public ulong FightId { get; set; }
    [Key(2)] public string TriggerKey { get; set; } = ""; [Key(3)] public string TriggerArg { get; set; } = ""; [Key(4)] public string Place { get; set; } = "";
    [Key(5)] public long[] Head { get; set; } = [];
    [Key(6)] public string[] Strings { get; set; } = [];
    [Key(7)] public LogFighterRow[] Fighters { get; set; } = [];
    [Key(8)] public long[] Lines { get; set; } = [];
    [Key(9)] public LogBlowRow[] Blows { get; set; } = [];

    const int LineStride = 21, StepStride = 8, WoundStride = 9;

    /// <summary>Captures the log (every pick is made first, with <paramref name="book"/>, so the saved lines carry them).</summary>
    public static FightLogSave Capture(FightLog log, FlavourBook book)
    {
        lock (log.Sync) return CaptureCore(log, book);          // the panel's formatter picks lines on the main thread meanwhile (review, game finding 8)
    }

    private static FightLogSave CaptureCore(FightLog log, FlavourBook book)
    {
        FlavourFormatter.PickUpTo(log, book, log.Lines.Count);
        // keep the newest lines and some serious older ones, in order
        int n = log.Lines.Count, recentFrom = Math.Max(0, n - KeepRecent);
        var keep = new List<int>();
        var serious = new List<int>();
        for (int i = 0; i < recentFrom; i++) if (log.Lines[i].IsSerious) serious.Add(i);
        if (serious.Count > KeepSerious) serious = serious.GetRange(serious.Count - KeepSerious, KeepSerious);
        keep.AddRange(serious);
        for (int i = recentFrom; i < n; i++) keep.Add(i);
        int dropped = log.Dropped + (n - keep.Count);

        var s = new FightLogSave { Seed = log.Seed, FightId = log.FightId, TriggerKey = log.TriggerKey, TriggerArg = log.TriggerArg, Place = log.Place };
        s.Head = [log.Hero, log.HeroTeam, log.StartTick, log.EndTick, log.Winner, log.Finished ? 1 : 0, log.GameSec0, dropped, log.NextSerial, keep.Count, log.NextBlowId];
        s.Strings = [.. log.Strings];
        s.Fighters = [.. log.Fighters.Values.Select(f => new LogFighterRow
        {
            Id = f.Id, Name = f.Name, Kind = f.Kind, PlanId = f.PlanId, Person = f.Person,
            N = [f.Team, f.Player ? 1 : 0, f.Female ? 1 : 0, f.End, f.Swings, f.Landed, f.Blocked, f.Parried, f.Dodged, f.Missed, f.Edge, f.Point, f.Blunt, f.Missile, f.LossHighPm, f.StaminaLow, f.BestLine, f.BestSeverity,
                f.BestMomentum, f.LastHitSerial, f.WorstHitSerial, f.WorstBleedX10, f.DownSerial, f.DeathSerial],
        })];
        var lines = new long[keep.Count * LineStride];
        var blowIds = new SortedSet<int>();
        for (int k = 0; k < keep.Count; k++)
        {
            var l = log.Lines[keep[k]]; int o = k * LineStride;
            lines[o] = l.Serial; lines[o + 1] = l.Tick; lines[o + 2] = (long)l.Kind; lines[o + 3] = l.Severity; lines[o + 4] = (long)l.Flags; lines[o + 5] = l.Actor; lines[o + 6] = l.Target; lines[o + 7] = l.Part;
            lines[o + 8] = l.A; lines[o + 9] = l.B; lines[o + 10] = l.Blow; lines[o + 11] = l.WeaponShape; lines[o + 12] = l.WeaponMaterial; lines[o + 13] = l.Attack; lines[o + 14] = l.AKind;
            lines[o + 15] = l.Verb; lines[o + 16] = l.Family; lines[o + 17] = l.P0 | (long)l.P1 << 16 | (long)l.P2 << 32 | (long)l.P3 << 48; lines[o + 18] = l.P4; lines[o + 19] = l.Picked ? 1 : 0; lines[o + 20] = 0;
            if (l.Blow >= 0) blowIds.Add(l.Blow);
        }
        s.Lines = lines;
        var blows = new List<LogBlowRow>();
        foreach (var id in blowIds)
        {
            if (!log.Blows.TryGetValue(id, out var b)) continue;
            var row = new LogBlowRow
            {
                Id = id, PieceShapes = b.PieceShapes, PieceMaterials = b.PieceMaterials,
                H =
                [
                    b.Tick, b.Attacker, b.Defender, b.WeaponShape, b.WeaponMaterial, b.Attack, b.AKind, b.Verb, b.Family, (long)b.AttackType,
                    (b.Missile ? 1 : 0) | (b.Thrown ? 2 : 0) | (b.Called ? 4 : 0) | (b.Redirected ? 8 : 0) | (b.Severed ? 16 : 0) | (b.Hit ? 32 : 0) | (b.Blocked ? 64 : 0),
                    (long)b.Block, (long)b.Facing, (long)b.Style, b.Part, b.InitialMilli, b.FinalMilli, b.ExcessMilli,
                ],
            };
            var steps = new long[b.Steps.Length * StepStride];
            for (int i = 0; i < b.Steps.Length; i++)
            {
                var st = b.Steps[i]; int o = i * StepStride;
                steps[o] = st.Piece; steps[o + 1] = st.Tissue; steps[o + 2] = st.Part; steps[o + 3] = st.IsArmour ? 1 : 0; steps[o + 4] = (long)st.Outcome; steps[o + 5] = st.InMilli; steps[o + 6] = st.OutMilli;
                steps[o + 7] = st.NeedMilli;   // the wear is not kept
            }
            row.Steps = steps;
            var wounds = new long[b.Wounds.Length * WoundStride];
            for (int i = 0; i < b.Wounds.Length; i++)
            {
                var w = b.Wounds[i]; int o = i * WoundStride;
                wounds[o] = w.Part; wounds[o + 1] = w.Tissue; wounds[o + 2] = w.Structure; wounds[o + 3] = w.Organ; wounds[o + 4] = (long)w.Kind | (long)w.Grade << 8 | (long)w.Count << 16;
                wounds[o + 5] = w.DepthPm; wounds[o + 6] = w.BleedX10; wounds[o + 7] = (long)w.Flags; wounds[o + 8] = 0;
            }
            row.Wounds = wounds;
            blows.Add(row);
        }
        s.Blows = [.. blows];
        return s;
    }

    public FightLog Restore()
    {
        var log = new FightLog { Seed = Seed, FightId = FightId, TriggerKey = TriggerKey, TriggerArg = TriggerArg, Place = Place };
        log.SetStrings(Strings);
        var h = Head;
        log.Hero = (int)h[0]; log.HeroTeam = (int)h[1]; log.StartTick = (int)h[2]; log.EndTick = (int)h[3]; log.Winner = (int)h[4]; log.Finished = h[5] != 0; log.GameSec0 = h[6]; log.Dropped = (int)h[7];
        log.NextSerial = (int)h[8]; log.SetNextBlow((int)h[10]);
        foreach (var f in Fighters)
        {
            var n = f.N;
            log.Fighters[f.Id] = new FighterInfo
            {
                Id = f.Id, Name = f.Name, Kind = f.Kind, PlanId = f.PlanId, Person = f.Person, Team = (int)n[0], Player = n[1] != 0, Female = n[2] != 0, End = (byte)n[3], Swings = (int)n[4], Landed = (int)n[5],
                Blocked = (int)n[6], Parried = (int)n[7], Dodged = (int)n[8], Missed = (int)n[9], Edge = (int)n[10], Point = (int)n[11], Blunt = (int)n[12], Missile = (int)n[13], LossHighPm = (int)n[14],
                StaminaLow = (int)n[15], BestLine = (int)n[16], BestSeverity = (int)n[17], BestMomentum = n[18], LastHitSerial = (int)n[19], WorstHitSerial = (int)n[20], WorstBleedX10 = (int)n[21],
                DownSerial = (int)n[22], DeathSerial = (int)n[23],
            };
        }
        for (int o = 0; o + LineStride <= Lines.Length; o += LineStride)
        {
            var d = Lines;
            long p = d[o + 17];
            log.Lines.Add(new FightLine
            {
                Serial = (int)d[o], Tick = (int)d[o + 1], Kind = (FightEventKind)d[o + 2], Severity = (sbyte)d[o + 3], Flags = (LineFlags)d[o + 4], Actor = (short)d[o + 5], Target = (short)d[o + 6], Part = (short)d[o + 7],
                A = (int)d[o + 8], B = (int)d[o + 9], Blow = (int)d[o + 10], WeaponShape = (ushort)d[o + 11], WeaponMaterial = (ushort)d[o + 12], Attack = (byte)d[o + 13], AKind = (byte)d[o + 14],
                Verb = (ushort)d[o + 15], Family = (ushort)d[o + 16], P0 = (ushort)(p & 0xFFFF), P1 = (ushort)(p >> 16 & 0xFFFF), P2 = (ushort)(p >> 32 & 0xFFFF), P3 = (ushort)(p >> 48 & 0xFFFF), P4 = (ushort)d[o + 18],
                Picked = d[o + 19] != 0,
            });
        }
        log.PickCursor = log.Lines.Count;
        foreach (var r in Blows)
        {
            var hd = r.H;
            var b = new BlowSnap
            {
                Tick = (int)hd[0], Attacker = (short)hd[1], Defender = (short)hd[2], WeaponShape = (ushort)hd[3], WeaponMaterial = (ushort)hd[4], Attack = (byte)hd[5], AKind = (byte)hd[6], Verb = (ushort)hd[7],
                Family = (ushort)hd[8], AttackType = (AttackType)hd[9], Missile = (hd[10] & 1) != 0, Thrown = (hd[10] & 2) != 0, Called = (hd[10] & 4) != 0, Redirected = (hd[10] & 8) != 0, Severed = (hd[10] & 16) != 0,
                Hit = (hd[10] & 32) != 0, Blocked = (hd[10] & 64) != 0, Block = (BlockKind)hd[11], Facing = (Facing)hd[12], Style = (AttackStyle)hd[13], Part = (ushort)hd[14], InitialMilli = hd[15], FinalMilli = hd[16],
                ExcessMilli = hd[17], PieceShapes = r.PieceShapes, PieceMaterials = r.PieceMaterials,
            };
            b.Steps = new StepSnap[r.Steps.Length / StepStride];
            for (int i = 0; i < b.Steps.Length; i++)
            {
                int o = i * StepStride; var d = r.Steps;
                b.Steps[i] = new StepSnap { Piece = (short)d[o], Tissue = (ushort)d[o + 1], Part = (ushort)d[o + 2], IsArmour = d[o + 3] != 0, Outcome = (LayerOutcome)d[o + 4], InMilli = d[o + 5], OutMilli = d[o + 6], NeedMilli = d[o + 7] };
            }
            b.Wounds = new WoundSnap[r.Wounds.Length / WoundStride];
            for (int i = 0; i < b.Wounds.Length; i++)
            {
                int o = i * WoundStride; var d = r.Wounds;
                b.Wounds[i] = new WoundSnap
                {
                    Part = (ushort)d[o], Tissue = (ushort)d[o + 1], Structure = (ushort)d[o + 2], Organ = (ushort)d[o + 3], Kind = (WoundKind)(d[o + 4] & 0xFF), Grade = (byte)(d[o + 4] >> 8 & 0xFF), Count = (byte)(d[o + 4] >> 16 & 0xFF),
                    DepthPm = (int)d[o + 5], BleedX10 = (int)d[o + 6], Flags = (WoundFlags)d[o + 7],
                };
            }
            log.RestoreBlow(r.Id, b);
        }
        return log;
    }
}
