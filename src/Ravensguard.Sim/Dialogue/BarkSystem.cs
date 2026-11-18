using Ravensguard.Content;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Dialogue;

/// <summary>A person near the hero who may call something out.</summary>
public readonly record struct BarkCandidate(ulong Id, int Index, double DistM, string Job, bool Female, int Mood, RelationTier Tier, bool Hungry, bool Child);

/// <summary>A floating line: who, which bark, the words and for how long.</summary>
public sealed record BarkEvent(ulong Person, string BarkId, string Channel, double DurationS, string Text);

/// <summary>
/// Barks in the street (dialogue spec 4.4, live tier only): a person within the bark's radius may call out when the bark's conditions hold (drawn from real state: the hour, the weather, the job,
/// the market's prices, the unrest, how the hero looks) and their cooldown (real seconds) has run out. At most 2 per real second and 3 visible at once; the closest people first, ties by index.
/// The pick uses stream 0x311 keyed on the game window of 20 seconds, so the same moment gives the same bark at 1x and 8x. Barks are a view: they change no record.
/// </summary>
public sealed class BarkSystem
{
    private readonly Dictionary<ulong, double> _next = [];
    private readonly List<(ulong Person, double Until)> _visible = [];
    private long _second = long.MinValue; private int _inSecond;

    public int VisibleCount => _visible.Count;

    public List<BarkEvent> Update(LineBook book, ulong seed, long gameSec, double realSec, IReadOnlyList<BarkCandidate> people, TalkContext world, ITextSource text, bool chatty = true)
    {
        var res = new List<BarkEvent>(); var set = book.Settings;
        _visible.RemoveAll(v => v.Until <= realSec);
        long sec = (long)Math.Floor(realSec); if (sec != _second) { _second = sec; _inSecond = 0; }
        if (!chatty || book.Barks.Count == 0) return res;
        long window = gameSec / 20;
        foreach (var c in people.OrderBy(p => p.DistM).ThenBy(p => p.Index))
        {
            if (_visible.Count >= set.BarkVisibleMax || _inSecond >= set.BarkGlobalPerSecond) break;
            if (_next.TryGetValue(c.Id, out double t) && t > realSec) continue;
            if (DetRng.Range(seed, window, c.Id, RngStreams.BarkPick, 2, 0, 1000) >= 300) continue;         // not every window: a street is not a chorus
            world.Job = c.Job; world.Mood = c.Mood; world.Female = c.Female; world.Child = c.Child; world.Familiarity = c.Tier switch { RelationTier.Stranger => 0, RelationTier.Acquaintance => 20, RelationTier.Friend => 50, _ => 80 };
            world.TrustPm = c.Tier switch { RelationTier.Disliked => -300, RelationTier.Hated => -700, RelationTier.Trusted => 700, RelationTier.Friend => 400, _ => 100 };
            world.Needs.Clear(); if (c.Hungry) world.Needs.Add("hungry");
            BarkDef? pick = Pick(book, c, world, seed, window);
            if (pick == null) continue;
            ParamResolver.Fill(world, text);
            string line = ParamResolver.Say(pick.Key, world, text, c.Female);
            res.Add(new BarkEvent(c.Id, pick.Id, pick.Channel, pick.DurationS, line));
            double jitter = DetRng.Range(seed, window, c.Id, RngStreams.BarkPick, 1, 0, Math.Max(1, set.BarkPersonCooldownS / 4));
            _next[c.Id] = realSec + Math.Max(pick.CooldownS, set.BarkPersonCooldownS) + jitter;
            _visible.Add((c.Id, realSec + pick.DurationS)); _inSecond++;
        }
        return res;
    }

    /// <summary>The bark a person would call at this window: weight times one plus the number of conditions, a draw on stream 0x311.</summary>
    public static BarkDef? Pick(LineBook book, in BarkCandidate c, TalkContext x, ulong seed, long window)
    {
        BarkDef? only = null; long total = 0; var pool = new List<(BarkDef B, long W)>();
        foreach (var b in book.Barks)
        {
            if (b.RadiusM < c.DistM || !TalkConditions.All(b.Conditions, x)) continue;
            long w = (long)b.WeightPm * (1 + b.Conditions.Count); pool.Add((b, w)); total += w; only = b;
        }
        if (pool.Count == 0) return null;
        long r = (long)(DetRng.Next(seed, window, c.Id, RngStreams.BarkPick, 0) % (ulong)total);
        foreach (var (b, w) in pool) { if (r < w) return b; r -= w; }
        return only;
    }
}
