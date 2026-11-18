using Ravensguard.Sim.Core;
using Ravensguard.Sim.Live;

namespace Ravensguard.Sim.Behaviour;

/// <summary>
/// A person's needs at one game second, derived from their day (live-behaviour spec 4.1): fatigue rises 62 permille an hour awake and falls 125 an hour asleep in a bed, hunger is the hours since the
/// last meal (breakfast when they wake, dinner at noon, supper at half past six, each only if the person is awake then), loneliness grows 25 an hour until a shop or church block. Pure, so the live scorer,
/// the abstract slice and a test all read the same numbers, and there is nothing to save. (The spec's per-person columns come with the economy's hunger writes; until then the day is the truth.)
/// </summary>
public static class NeedModel
{
    private const int Day = ScheduleTuning.Day;

    public static void Read(in PersonAnchors a, long sec, out NeedInputs n, out ChoiceContext ctx)
    {
        long day = sec / Day; int sod = (int)(sec % Day);
        Span<DaySeg> segs = stackalloc DaySeg[Schedule.MaxSegs];
        int cnt = Schedule.Segments(a, day, segs), k = 0;
        while (k < cnt - 1 && sod >= segs[k].End) k++;
        var cur = segs[k];
        n = default; ctx = default;
        // sleep: the contiguous run of Sleep blocks around now, read cyclically
        int sleepSecs = 0; for (int i = 0; i < cnt; i++) if (segs[i].Kind == JobKind.Sleep) sleepSecs += segs[i].End - segs[i].Start;
        // integer (review finding 16): 62 permille an awake hour, truncated as the double version truncated
        int fat0 = (int)Math.Min(1000, 62L * (Day - sleepSecs) / 3600);
        int lastWakeEnd = int.MinValue;          // seconds relative to the start of today; the end of the latest sleep run before now
        if (cur.Kind != JobKind.Sleep)
            for (int step = 1; step <= cnt; step++)
            {
                int i = k - step, idx = ((i % cnt) + cnt) % cnt;
                if (segs[idx].Kind == JobKind.Sleep) { lastWakeEnd = segs[idx].End + (i < 0 ? -Day : 0); break; }
            }
        if (cur.Kind == JobKind.Sleep)
        {
            int runStart = cur.Start; int j = k;                      // walk back over the contiguous run
            while (j > 0 && segs[j - 1].Kind == JobKind.Sleep) { j--; runStart = segs[j].Start; }
            if (j == 0 && cnt > 1 && segs[cnt - 1].Kind == JobKind.Sleep) { int l = cnt - 1; runStart = segs[l].Start - Day; while (l > 0 && segs[l - 1].Kind == JobKind.Sleep && segs[l - 1].End == segs[l].Start) { l--; runStart = segs[l].Start - Day; } }
            n.FatiguePm = (int)Math.Max(0, fat0 - (125L * (sod - runStart) + 3599) / 3600);    // fat0 - 125 h, truncated toward zero: the rest rounds up
        }
        else n.FatiguePm = lastWakeEnd == int.MinValue ? 300 : (int)Math.Min(1000, 62L * (sod - lastWakeEnd) / 3600);
        // meals: breakfast soon after waking, dinner at noon, supper at 18:30; a meal only counts if the person was awake then
        int lastMeal = int.MinValue;
        Span<int> meals = [lastWakeEnd == int.MinValue ? int.MinValue : lastWakeEnd + 900, 12 * 3600, 18 * 3600 + 1800, -Day + 18 * 3600 + 1800];
        foreach (int t in meals)
            if (t != int.MinValue && t <= sod && t > lastMeal && AwakeAt(segs, cnt, ((t % Day) + Day) % Day)) lastMeal = t;
        n.HungerMilliHours = lastMeal == int.MinValue ? 6000 : (int)((sod - lastMeal) * 1000L / 3600);
        // company: hours since the last shop or church block ended
        int lastSoc = int.MinValue;
        for (int i = 0; i < cnt; i++) if (segs[i].Kind is JobKind.Shop or JobKind.Church && segs[i].End <= sod) lastSoc = Math.Max(lastSoc, segs[i].End);
        n.SocialPm = (int)Math.Min(1000, 300 + 25L * (lastSoc == int.MinValue ? sod : sod - lastSoc) / 3600);
        var sched = UtilityBrain.OfKind(cur.Kind);
        n.DutyPm = cur.Kind == JobKind.Work ? UtilityBrain.Default.Profile.DutyPm : 0;
        ctx = new ChoiceContext
        {
            Scheduled = sched, HasBed = true, CanEat = !cur.Walks, WorkOpen = cur.Kind == JobKind.Work, HasRoof = true, CanSocialise = false, CanFlee = false, CanShelter = false,
            Dark = IsDark(a, day, sod), Seed = a.Seed, GameSec = sec, Person = a.Id.Value,
        };
    }

    private static bool AwakeAt(ReadOnlySpan<DaySeg> segs, int cnt, int sod)
    {
        for (int i = 0; i < cnt; i++) if (sod >= segs[i].Start && sod < segs[i].End) return segs[i].Kind != JobKind.Sleep;
        return true;
    }

    public static bool IsDark(in PersonAnchors a, long day, int sod)
    {
        var (sr, ss) = SunCache.Of(a.LatTenths, (int)(day % TimeConst.DaysPerYear) + 1);
        int m = sod / 60;
        return m < sr || m >= ss;
    }
}
