using Ravensguard.Content;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Combat.Flavour;

/// <summary>The accessibility toggles as the feedback planner reads them (the Game layer backs this with its <c>Accessibility</c> facade; effect code never reads the settings itself, test FL-12).</summary>
public interface IFeedbackSettings
{
    bool ShakeOn { get; }
    bool FlashesOn { get; }
    bool SlowMotionOn { get; }
    bool CaptionsOn { get; }
    bool ReduceBlood { get; }
}

/// <summary>A fixed set of toggles (tests, tools, a default).</summary>
public sealed record FeedbackSettings(bool ShakeOn = true, bool FlashesOn = true, bool SlowMotionOn = true, bool CaptionsOn = false, bool ReduceBlood = false) : IFeedbackSettings;

/// <summary>The part callout of a blow ("Artery", "SEVERED"): the <c>tr()</c> key and the severity colour class.</summary>
public readonly record struct Callout(string Key, int Severity);

/// <summary>What one log line asks of the presentation (feedback-flavour-log 4.4, 4.9). Numbers are integers: milliseconds, pixels at 60 fps reference, permille.</summary>
public struct HitFeedback
{
    /// <summary>White flash length in milliseconds (0: none), and the red wound tint in milliseconds.</summary>
    public int FlashMs, TintMs;
    /// <summary>Screen shake amplitude in pixels (0: none) and its decay in milliseconds.</summary>
    public int ShakePx, ShakeMs;
    /// <summary>Presentation freeze in milliseconds (severing, heart, brain, kills of a named foe near the hero).</summary>
    public int HitStopMs;
    public bool SlowMotion;
    public Callout Callout, Callout2;
    /// <summary>The sound the event plays ("" none) and its intensity 0..1000.</summary>
    public string Sound;
    public int Intensity;
    public string Caption;
    /// <summary>A blood decal at the target's feet (size 0..3), unless blood is reduced.</summary>
    public int Decal;
    public bool Any => FlashMs > 0 || ShakePx > 0 || HitStopMs > 0 || SlowMotion || Callout.Key != null || Sound.Length > 0;
}

/// <summary>Turns lines into feedback (4.9) through the toggles: shake, flashes and slow motion off give exactly nothing (test FL-10).</summary>
public sealed class FeedbackPlanner(FightLog log, IReadOnlyList<AudioEventDef> audio, IFeedbackSettings settings)
{
    public IFeedbackSettings Settings { get; set; } = settings;

    /// <summary>The effects of a line. <paramref name="near"/>: the line is the hero's or within 6 m of the camera focus (slow motion and shake only then).</summary>
    public HitFeedback Plan(in FightLine l, bool near)
    {
        var f = new HitFeedback { Sound = "", Caption = "" };
        bool heroTarget = log.Hero >= 0 && l.Target == log.Hero && l.Kind is FightEventKind.Hit or FightEventKind.Stopped;
        bool heroInvolved = log.Hero >= 0 && (l.Actor == log.Hero || l.Target == log.Hero);
        var b = log.BlowOf(l);
        long units = b == null ? 0 : b.FinalMilli / 1000;
        long initial = b == null ? 0 : b.InitialMilli / 1000;
        int sev = Math.Max(0, (int)l.Severity);
        switch (l.Kind)
        {
            case FightEventKind.Hit:
            {
                if (Settings.FlashesOn) { f.FlashMs = 33; f.TintMs = sev >= 2 ? 100 : 0; }
                if (sev >= 1) f.Callout = CalloutOf(l, b);
                if (sev >= 3 && b != null) f.Callout2 = CalloutOf(l, b, second: true);
                if (heroTarget && near && Settings.ShakeOn && (units >= 60 || sev >= 3 || (l.Flags & LineFlags.Severed) != 0))
                    f.ShakePx = sev >= 4 ? 8 : (int)Math.Clamp(units / 16, 1, 8) + (sev >= 3 ? 2 : 0);
                if (f.ShakePx > 8) f.ShakePx = 8;
                if (f.ShakePx > 0) f.ShakeMs = 150;
                if (Settings.SlowMotionOn && near && (sev >= 4 || heroTarget && units >= 100)) f.SlowMotion = true;
                if (Settings.SlowMotionOn && near && sev >= 4) f.HitStopMs = 60;
                if (!Settings.ReduceBlood) f.Decal = sev >= 3 ? 3 : sev >= 2 ? 2 : sev >= 1 ? 1 : 0;
                break;
            }
            case FightEventKind.Stopped: if (Settings.FlashesOn) f.FlashMs = 33; f.Callout = new Callout("fl_call_armour", 0); break;
            case FightEventKind.Blocked: case FightEventKind.Overpowered: f.Callout = new Callout("fl_call_block", 0); if (Settings.FlashesOn && (l.Flags & LineFlags.Shield) != 0) f.FlashMs = 33; break;
            case FightEventKind.Parried: f.Callout = new Callout("fl_call_parry", 0); break;
            case FightEventKind.Dodged: f.Callout = new Callout("fl_call_dodge", 0); break;
            case FightEventKind.Missed: case FightEventKind.Whiff: f.Callout = new Callout("fl_call_miss", 0); break;
            case FightEventKind.Down: f.Callout = new Callout("fl_call_down", 3); break;
            case FightEventKind.Death:
                f.Callout = new Callout("fl_call_killed", 4);
                if (Settings.SlowMotionOn && near && (heroInvolved || NamedOrBeast(l))) { f.SlowMotion = true; f.HitStopMs = 60; }
                break;
            case FightEventKind.Rout: case FightEventKind.Rally: case FightEventKind.Surrender: f.Callout = new Callout("fl_call_banner", 1); break;
        }
        var cue = AudioMap.Resolve(log, l, audio);
        if (cue != null && !cue.Silent) { f.Sound = cue.Id; f.Intensity = (int)Math.Clamp(initial * 1000 / 128, 0, 1000); if (Settings.CaptionsOn) f.Caption = cue.Caption; }
        return f;
    }

    bool NamedOrBeast(in FightLine l) => log.TryInfo(l.Actor, out var f) && (f.Named || f.Kind != "human");

    /// <summary>The floating word over a wound: SEVERED first, then the structure that matters, else the part.</summary>
    Callout CalloutOf(in FightLine l, BlowSnap? b, bool second = false)
    {
        var fl = WoundFlags.None;
        if (b != null) foreach (var w in b.Wounds) fl |= w.Flags;
        if ((l.Flags & LineFlags.Severed) != 0 || (fl & WoundFlags.Severed) != 0) return new Callout("fl_call_severed", 4);
        if (!second)
        {
            if ((fl & WoundFlags.Vital) != 0) return new Callout("fl_call_killed", 4);
            if ((fl & WoundFlags.ArteryHit) != 0) return new Callout("fl_call_artery", 3);
            if ((fl & WoundFlags.SpineHit) != 0) return new Callout("fl_call_spine", 3);
            if ((fl & WoundFlags.OrganHit) != 0) return new Callout("fl_call_organ", 3);
            if ((fl & WoundFlags.BoneBroken) != 0) return new Callout("fl_call_bone", 2);
            if ((fl & (WoundFlags.TendonHit | WoundFlags.LigamentHit)) != 0) return new Callout("fl_call_tendon", 2);
            if ((fl & (WoundFlags.NerveHit | WoundFlags.MotorNerveHit | WoundFlags.SensoryNerveHit)) != 0) return new Callout("fl_call_nerve", 2);
            if ((fl & WoundFlags.VeinHit) != 0) return new Callout("fl_call_vein", 2);
            if ((fl & WoundFlags.Concussion) != 0) return new Callout("fl_call_concussed", 2);
        }
        string part = b != null && b.Wounds.Length > 0 ? Severity.BaseOf(log.Str(b.Wounds[0].Part)) : b != null ? Severity.BaseOf(log.Str(b.Part)) : "";
        return part.Length > 0 ? new Callout("fl_call_part_" + part, Math.Max(1, (int)l.Severity)) : new Callout("fl_call_hit", 1);
    }
}

/// <summary>Which sound a line plays (feedback-flavour-log 4.6), from the content rows. Audio never touches the sim.</summary>
public static class AudioMap
{
    public static AudioEventDef? Resolve(FightLog log, in FightLine l, IReadOnlyList<AudioEventDef> rows)
    {
        string ev = FlavourDescribe.EventName(l.Kind);
        string attack = l.AKind < AttackKinds.Names.Length ? AttackKinds.Names[l.AKind] : "";
        string armour = "none"; WoundFlags flags = WoundFlags.None;
        var b = log.BlowOf(l);
        if (b != null)
        {
            var d = FlavourDescribe.Describe(log, l, NoText.Instance);
            armour = (l.Flags & LineFlags.Shield) != 0 ? "shield" : d.Ctx.ArmourFamily.Length > 0 && d.Ctx.ArmourOutcome != "none" ? d.Ctx.ArmourFamily : "none";
            flags = d.Ctx.Flags;
        }
        AudioEventDef? best = null;
        foreach (var r in rows)
        {
            if (!r.Events.Contains(ev)) continue;
            if (l.Severity < r.MinSeverity) continue;
            if (r.Armour.Count > 0 && !r.Armour.Contains(armour)) continue;
            if (r.Attack.Count > 0 && !r.Attack.Contains(attack)) continue;
            if (r.Flags.Count > 0 && (FlavourBook.FlagsOf(r.Flags) & flags) == 0) continue;
            if (best == null || r.Priority > best.Priority) best = r;
        }
        return best;
    }
}

/// <summary>One sound asking for a voice.</summary>
public readonly record struct Voice(string Sound, int Severity, long DistanceMm, int Order);

/// <summary>At most 16 voices (4.6): the closest and most severe win; sounds within 8 m are full volume, none beyond 60 m.</summary>
public static class VoicePool
{
    public const int Max = 16;

    public static List<Voice> Select(IEnumerable<Voice> asking, int max = Max)
    {
        return [.. asking.Where(v => v.DistanceMm <= 60_000).OrderByDescending(v => v.Severity).ThenBy(v => v.DistanceMm).ThenBy(v => v.Order).Take(max)];
    }

    /// <summary>Volume 0..1000 by distance: full inside 8 m, falling to nothing at 60 m, 40 percent less behind a wall.</summary>
    public static int Volume(long distanceMm, bool occluded)
    {
        long v = distanceMm <= 8000 ? 1000 : distanceMm >= 60_000 ? 0 : 1000 * (60_000 - distanceMm) / 52_000;
        return (int)(occluded ? v * 600 / 1000 : v);
    }

    /// <summary>The bearing word of a source relative to the listener's facing, for a caption: ahead, left, right or behind.</summary>
    public static string Bearing(int dxMm, int dyMm, int faceX, int faceY)
    {
        long dot = (long)dxMm * faceX + (long)dyMm * faceY, cross = (long)faceX * dyMm - (long)faceY * dxMm;
        if (Math.Abs(dot) >= Math.Abs(cross)) return dot >= 0 ? "ahead" : "behind";
        return cross >= 0 ? "right" : "left";
    }
}

/// <summary>
/// Slow motion (feedback-flavour-log 4.5): the presentation time scale drops to 35 percent for 0.4 s of real time (80 ms in, 150 ms out), at most 6 a minute with a 3 s cooldown. It
/// changes how many 20 Hz steps run per real second, never the step (test FL-9), and is off in watch mode, the abstract tier and with the setting off. Integer milliseconds.
/// </summary>
public sealed class SlowMotionGovernor
{
    public const int ScalePm = 350, HoldMs = 400, InMs = 80, OutMs = 150, CooldownMs = 3000, MaxPerMinute = 6;
    private long _startMs = -1_000_000, _cooldownUntil;
    private readonly Queue<long> _starts = new();

    /// <summary>Asks for a slow-motion moment at <paramref name="nowMs"/> real time; true when it starts.</summary>
    public bool Request(long nowMs, bool allowed)
    {
        if (!allowed || nowMs < _cooldownUntil) return false;
        while (_starts.Count > 0 && nowMs - _starts.Peek() > 60_000) _starts.Dequeue();
        if (_starts.Count >= MaxPerMinute) return false;
        _starts.Enqueue(nowMs); _startMs = nowMs; _cooldownUntil = nowMs + CooldownMs;
        return true;
    }

    /// <summary>The scale in permille now (1000 normal): eases in over 80 ms, holds, eases out over 150 ms inside the 0.4 s.</summary>
    public int Scale(long nowMs)
    {
        long t = nowMs - _startMs;
        if (t < 0 || t >= HoldMs) return 1000;
        int drop = 1000 - ScalePm;
        if (t < InMs) return (int)(1000 - drop * t / InMs);
        if (t >= HoldMs - OutMs) return (int)(ScalePm + drop * (t - (HoldMs - OutMs)) / OutMs);
        return ScalePm;
    }

    public bool Active(long nowMs) => nowMs - _startMs is >= 0 and < HoldMs;
}

/// <summary>Turns real time into fight steps: 20 a second at scale 1000, fewer when slowed. The remainder is kept, so the total of steps follows the scaled time exactly.</summary>
public sealed class StepPacer
{
    public const long StepUs = 50_000;
    private long _acc;
    public long Steps { get; private set; }

    /// <summary>Real microseconds that passed and the scale in permille: the number of steps to run now.</summary>
    public int Advance(long realUs, int scalePm)
    {
        _acc += realUs * Math.Clamp(scalePm, 0, 1000) / 1000;
        int n = (int)(_acc / StepUs);
        _acc -= n * StepUs; Steps += n;
        return n;
    }
}

public enum MusicLayer : byte { Calm, Tension, Battle }

/// <summary>The adaptive music state (4.6): Calm by default, Tension (an enemy aware within 30 m but no fight, or a night cave), Battle (a fight with the hero, or five fighters within 30 m); 8 s of hysteresis before it falls back; layers fade over 3 s.</summary>
public sealed class MusicState
{
    public const int HysteresisMs = 8000, FadeMs = 3000, BattleCrowd = 5;
    public MusicLayer Layer { get; private set; } = MusicLayer.Calm;
    private long _since, _lowerSince = -1;
    private readonly int[] _vol = [1000, 0, 0];

    public int Volume(MusicLayer l) => _vol[(int)l];

    public MusicLayer Wanted(bool enemyAware30, bool nightCave, bool heroFight, int fightersWithin30) =>
        heroFight || fightersWithin30 >= BattleCrowd ? MusicLayer.Battle : enemyAware30 || nightCave ? MusicLayer.Tension : MusicLayer.Calm;

    /// <summary>Steps the state by <paramref name="dtMs"/> toward what is wanted at <paramref name="nowMs"/>.</summary>
    public void Update(long nowMs, long dtMs, MusicLayer wanted)
    {
        if (wanted > Layer) { Layer = wanted; _since = nowMs; _lowerSince = -1; }
        else if (wanted < Layer)
        {
            if (_lowerSince < 0) _lowerSince = nowMs;
            if (nowMs - _lowerSince >= HysteresisMs) { Layer = wanted; _since = nowMs; _lowerSince = -1; }
        }
        else _lowerSince = -1;
        int step = (int)(1000 * dtMs / FadeMs);
        for (int i = 0; i < 3; i++) _vol[i] = i == (int)Layer ? Math.Min(1000, _vol[i] + step) : Math.Max(0, _vol[i] - step);
    }
}
