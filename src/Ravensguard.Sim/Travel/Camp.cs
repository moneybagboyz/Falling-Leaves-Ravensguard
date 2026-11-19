using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Weather;

namespace Ravensguard.Sim.Travel;

public enum ShelterKind : byte { None, Bedroll, LeanTo, Tent }

/// <summary>A companion as the camp sees them: the numbers the night changes. The game copies them from and back to the squad (loyalty) and the acquaintance book (familiarity).</summary>
public sealed class CampMember
{
    public EntityId Person;
    public string Name = "";
    public int Loyalty, Familiarity, Mood, Perception = 40, FatiguePm, Cooking, SocialPm;
    public int AmbitionProgress;
    public bool BackstoryTold;
    public int LastTalkNight = -1, LastMealDay = -1;
    public bool AmbitionKnown => AmbitionProgress >= 3;
}

/// <summary>A night camp (spec 4.3). Stock is a store of the camp's id; the fire burns fuel that has already left the stock.</summary>
public sealed class Camp
{
    public EntityId Id;
    public long XMm, YMm, MadeAtSec;
    public bool FireLit;
    public long FuelG, FireSinceSec, FireLastSec, BurnAccGs;
    public ShelterKind Shelter;
    public readonly List<EntityId> Members = [];
    public readonly List<EntityId> Sentries = [];
    public bool Broken;
    public long BrokenAtSec;
    public bool Hung;
}

public enum TalkKind : byte { Ambition, Backstory, Opinion, Banter, None }
public readonly record struct TalkResult(EntityId Person, TalkKind Kind, bool Gained);
public readonly record struct CampEventResult(string Id, string Effect, string Name, long GramsLost);
public readonly record struct MealResult(int Fed, long GramsEaten, long MeasuresPpm);

/// <summary>What the hero is doing at camp for a while (game time, saved): the view only shows its label and acts when it ends.</summary>
/// <summary>What a camp job is doing; Search and Dig (P4 close-out) are the surface sites' verbs, whose find is rolled at the barrier when the job comes due.</summary>
public enum CampJobKind : byte { Talk = 1, Sleep = 2, Break = 3, Gather = 4, Forage = 5, Hunt = 6, Search = 7, Dig = 8 }
public sealed record CampJobState(CampJobKind Kind, long EndSec);

public sealed partial class TravelState
{
    public Camp? Camp { get; set; }
    /// <summary>The camp job in progress (review finding 6: it was a closure in the view node, so a save mid-job lost it), or null.</summary>
    public CampJobState? CampJob { get; set; }

    /// <summary>Starts a camp job of at least a game minute, ending <paramref name="secs"/> from <paramref name="nowSec"/>.</summary>
    public CampJobState StartCampJob(CampJobKind kind, long secs, long nowSec) => CampJob = new CampJobState(kind, nowSec + Math.Max(60, secs));

    /// <summary>The camp job when it is due at <paramref name="nowSec"/> (and clears it), else null. Game seconds only: the same at 1x and 8x.</summary>
    public CampJobState? TakeDueCampJob(long nowSec)
    {
        if (CampJob is not { } j || nowSec < j.EndSec) return null;
        CampJob = null; return j;
    }
    /// <summary>Old camps: their fire pits stay as landmarks, the rest is gone after <c>CampDecayDays</c>.</summary>
    public readonly List<Camp> OldCamps = [];
    public readonly List<CampMember> Companions = [];
    public ulong NextCamp;
    /// <summary>The id of the nth camp: in the reserved serial space (review finding 2), never a record's id. Saves from before keep their old ids.</summary>
    public static EntityId CampId(ulong n) => EntityId.Reserved(IdKind.Other, 253, EntityId.ReservedSpace.Camps, n);

    public CampMember? Member(EntityId id) => Companions.FirstOrDefault(m => m.Person == id);

    // ---------------------------------------------------------------- making and breaking camp

    /// <summary>Q on open ground (spec 4.3): not near a settlement, flat, not on water or deep mud, the party close. Returns the reason when not.</summary>
    public (bool Ok, string Reason) CanMakeCamp(in LegSample s, int partyWithinM)
    {
        if (s.NearestSettlementM < Cfg.CampMinFromSettlementM) return (false, "Too near a settlement; use an inn");
        if (s.SlopePm >= Cfg.CampMaxSlopePm) return (false, "The ground is too steep");
        if ((s.Terrain & (TerrainFlags.Marsh | TerrainFlags.Mud)) != 0 || s.Ground is "ford" or "marsh") return (false, "The ground is too wet");
        if (partyWithinM > 20) return (false, "The party is not together");
        return (true, "");
    }

    public Camp MakeCamp(long xMm, long yMm, long sec, IEnumerable<EntityId> members)
    {
        if (Camp is { Broken: false } old) BreakCamp(sec);
        var c = new Camp { Id = CampId(++NextCamp), XMm = xMm, YMm = yMm, MadeAtSec = sec, FireLastSec = sec };
        c.Members.AddRange(members);
        Camp = c; StoreOf(c.Id);
        return c;
    }

    /// <summary>Break camp (10 game minutes): the stock goes back to the pack, the fire is doused, the camp becomes an old one whose fire pit stays.</summary>
    public long BreakCamp(long sec)
    {
        if (Camp is not { } c) return 0;
        long moved = 0;
        var pack = PackStore?.Invoke();
        if (pack != null && TryStore(c.Id, out var stock))
            foreach (var (g, q) in stock.Rows()) moved += Store.MoveUpTo(stock, pack, g, q);
        c.FireLit = false; c.Broken = true; c.BrokenAtSec = sec + Cfg.BreakCampMinutes * TimeConst.SecondsPerMinute;
        OldCamps.Add(c); Camp = null;
        return moved;
    }

    /// <summary>Old camps decay after three game days (objects removed; the fire pit stays as a landmark); returns how many were forgotten.</summary>
    public int DecayCamps(long sec)
    {
        int n = 0;
        foreach (var c in OldCamps.Where(c => sec - c.MadeAtSec > Cfg.CampDecayDays * TimeConst.SecondsPerDay).ToList()) { OldCamps.Remove(c); n++; }
        return n;
    }

    // ---------------------------------------------------------------- the fire

    /// <summary>Success chance of lighting a fire in permille: 700 + 3 x skill, -300 in rain or snow, +200 with dry tinder; at least 50, at most 950.</summary>
    public int FireChancePm(int skill, WeatherState wx, bool dryTinder)
    {
        int p = Cfg.FireBasePm + Cfg.FireSkillPm * skill;
        if (wx is WeatherState.LightRain or WeatherState.HeavyRain or WeatherState.Thunderstorm or WeatherState.Snow or WeatherState.Blizzard) p += Cfg.FireRainPm;
        if (dryTinder) p += Cfg.FireDryTinderPm;
        return Math.Clamp(p, 50, 950);
    }

    /// <summary>Tries to light the fire (needs fuel in the camp stock or already loaded and tinder with steel, which the caller checks). Stream 0x347 keyed on the person and the game minute.</summary>
    public bool TryLightFire(Camp c, EntityId person, int skill, WeatherState wx, bool dryTinder, long sec, int attempt = 0)
    {
        if (c.FuelG <= 0) return false;
        int p = FireChancePm(skill, wx, dryTinder);
        bool ok = DetRng.Range(Seed, sec / 60, person.Value, RngStreams.FireMaking, (uint)attempt, 0, 1000) < p;
        if (ok) { c.FireLit = true; c.FireSinceSec = sec; c.FireLastSec = sec; }
        return ok;
    }

    /// <summary>Moves firewood from the camp stock onto the fire (the grams leave the stock as burned: they are already accounted). Returns grams added.</summary>
    public long AddFuel(Camp c, long grams)
    {
        if (Goods == null) return 0;
        var stock = StoreOf(c.Id); ushort wood = Goods.IdOf("firewood");
        long take = Math.Min(grams, stock.Qty(wood));
        if (take <= 0 || !stock.Destroy(wood, take, DestroyReason.Burned)) return 0;
        c.FuelG += take; return take;
    }

    /// <summary>Burns the fire up to <paramref name="toSec"/> at 5 kg an hour; the remainder is carried, so any chunking gives the same grams. The fire goes out when the fuel is gone.</summary>
    public void StepFire(Camp c, long toSec)
    {
        long dt = toSec - c.FireLastSec; if (dt <= 0) return;
        if (c.FireLit)
        {
            c.BurnAccGs += (long)Cfg.FireBurnGPerHour * dt;
            long g = c.BurnAccGs / TimeConst.SecondsPerHour; c.BurnAccGs %= TimeConst.SecondsPerHour;
            if (g >= c.FuelG) { c.FuelG = 0; c.FireLit = false; c.BurnAccGs = 0; } else c.FuelG -= g;
        }
        c.FireLastSec = toSec;
    }

    /// <summary>The warmth offset a fire gives at <paramref name="distM"/> metres (clothing spec: +15 C within 4 m).</summary>
    public int FireWarmthC(Camp c, int distM) => c.FireLit && distM <= Cfg.FireWarmthM ? Cfg.FireWarmthC : 0;
    public int LightRadiusM(Camp c) => c.FireLit ? Cfg.FireLightRadiusM : 0;

    /// <summary>The rest a sleeper gets: fatigue lost per hour in permille (bare ground 75, bedroll 100, a tent by a fire 125).</summary>
    public int RestPerHour(Camp c) => c.Shelter == ShelterKind.Tent && c.FireLit ? 125 : c.Shelter is ShelterKind.Tent or ShelterKind.Bedroll or ShelterKind.LeanTo ? 100 : 75;
    /// <summary>Shelter's offset to the effective temperature in degrees C (lean-to +3, tent +5).</summary>
    public int ShelterOffsetC(Camp c) => c.Shelter switch { ShelterKind.LeanTo => 3, ShelterKind.Tent => 5, _ => 0 };

    // ---------------------------------------------------------------- meals

    /// <summary>
    /// Feeds <paramref name="persons"/> one person-day each from a store: foods in good-id order (fresh meat first), taking <c>ceil(1e9 / measures-ppm-per-kg)</c> grams per measure. Eaten goods leave the
    /// store as <see cref="DestroyReason.Eaten"/>. Returns grams eaten.
    /// </summary>
    public long FeedFromStore(Store store, int persons, long sec = 0, ulong tag = 0)
    {
        if (Goods == null || persons <= 0) return 0;
        long needPpm = (long)Cfg.MealMeasuresPerDay * 1000 * persons, ate = 0;      // measure-equivalents x 1e6
        var rows = store.Rows().Where(r => Goods.FoodMeasuresPpm(r.Good) > 0).OrderBy(r => Goods.StringId(r.Good) == "meat_fresh" ? 0 : 1).ThenBy(r => r.Good).ToList();
        foreach (var (g, qty) in rows)
        {
            if (needPpm <= 0) break;
            long ppmPerKg = Goods.FoodMeasuresPpm(g); long grams = ((needPpm * 1000) + ppmPerKg - 1) / ppmPerKg;      // measures x1e6 / (ppm per kg / 1000 per g)
            if (Goods.IsPiece(g)) continue;
            long take = Math.Min(grams, qty);
            if (take <= 0 || !store.Destroy(g, take, DestroyReason.Eaten)) continue;
            ate += take; needPpm -= take * ppmPerKg / 1000;
        }
        return ate;
    }

    /// <summary>The camp meal (spec 4.3, V-10): each present companion eats one person-day from the camp stock, sets their last meal, and once a day gets Loyalty +20 and Familiarity +2; a cook with Cooking 40+ adds Mood +5.</summary>
    public MealResult CampMeal(Camp c, IReadOnlyList<CampMember> eaters, long sec, int cookSkill = 0)
    {
        var stock = StoreOf(c.Id);
        long gramsAll = 0; int fed = 0;
        int day = (int)(sec / TimeConst.SecondsPerDay);
        foreach (var m in eaters)
        {
            long g = FeedFromStore(stock, 1, sec);
            if (g <= 0) continue;
            gramsAll += g; fed++;
            if (m.LastMealDay != day) { m.LastMealDay = day; m.Loyalty += Cfg.MealLoyalty; m.Familiarity += Cfg.MealFamiliarity; if (cookSkill >= 40) m.Mood += 5; }
        }
        if (fed > 0) LastMealSec = sec;
        return new MealResult(fed, gramsAll, (long)Cfg.MealMeasuresPerDay * 1000 * fed);
    }

    // ---------------------------------------------------------------- sentries

    public bool SetSentries(Camp c, IEnumerable<EntityId> ids)
    {
        var l = ids.Distinct().ToList();
        if (l.Count > Cfg.SentryMax) return false;
        c.Sentries.Clear(); c.Sentries.AddRange(l); return true;
    }

    /// <summary>notice_pm = 400 + 6 x perception, minus 150 when fatigued above 600.</summary>
    public int NoticePm(int perception, int fatiguePm) => Math.Clamp(Cfg.NoticeBasePm + Cfg.NoticePerceptionPm * perception - (fatiguePm > 600 ? Cfg.NoticeFatiguePenaltyPm : 0), 0, 1000);

    /// <summary>The distance a sentry sees a hostile: 120 m by day, 30 m moonlit, 18 m by the fire, 8 m in the dark (spec 4.3).</summary>
    public int VisionM(bool daylight, bool moonlit, bool fireLit)
    {
        if (daylight) return Cfg.VisionDayM;
        int v = Cfg.VisionDarkM;
        if (moonlit) v = Math.Max(v, Cfg.VisionMoonM);
        if (fireLit) v = Math.Max(v, Cfg.VisionFireM);
        return v;
    }

    /// <summary>One sentry's roll against one approacher in the 20-game-second window of <paramref name="sec"/> (stream 0x344, key = window, sentry, approacher index). Same window, same answer.</summary>
    public bool SentryNotices(EntityId sentry, int approacherIdx, int distM, int visionM, int noticePm, long sec)
    {
        if (distM > visionM) return false;
        return DetRng.Range(Seed, sec / 20, sentry.Value, RngStreams.SentryNotice, (uint)approacherIdx, 0, 1000) < noticePm;
    }

    // ---------------------------------------------------------------- companion talk

    /// <summary>The night a game second belongs to: it turns over at 18:00, so supper, the fire and the small hours are one night.</summary>
    public static int NightOf(long sec) => (int)((sec + 6 * TimeConst.SecondsPerHour) / TimeConst.SecondsPerDay);

    /// <summary>
    /// Talk around the fire (20 game minutes): every present companion offers one topic: the ambition (the first three nights), their backstory, the day's news, banter. Familiarity +6 and Loyalty +50
    /// once per night each, Mood +5 for all at the fire; the hero's Social need eases by 150. Skipped (Esc): no gain, no penalty.
    /// </summary>
    public List<TalkResult> TalkAroundFire(Camp c, IReadOnlyList<CampMember> present, long sec, bool recentNews, bool skip = false)
    {
        var res = new List<TalkResult>(); int night = NightOf(sec);
        if (skip) return res;
        foreach (var m in present)
        {
            if (m.LastTalkNight == night) { res.Add(new(m.Person, TalkKind.None, false)); continue; }
            TalkKind k = !m.AmbitionKnown ? TalkKind.Ambition : !m.BackstoryTold ? TalkKind.Backstory : recentNews ? TalkKind.Opinion : TalkKind.Banter;
            if (k == TalkKind.Ambition) m.AmbitionProgress++; else if (k == TalkKind.Backstory) m.BackstoryTold = true;
            m.LastTalkNight = night; m.Familiarity += Cfg.TalkFamiliarity; m.Loyalty += Cfg.TalkLoyalty; m.Mood += Cfg.TalkMood; m.SocialPm += Cfg.TalkSocialPm;
            res.Add(new(m.Person, k, true));
        }
        return res;
    }

    /// <summary>
    /// The companion who sits down to talk with the hero after the fire talk (Phase 3 cleanup B, item 8): the first who has not yet told their backstory, else the night's turn among those present
    /// (night number modulo their count), never the hero. Pure; null when no one else is at the fire. The game opens the talk window (Sim/Dialogue) with this person.
    /// </summary>
    public CampMember? FireSpeaker(IReadOnlyList<CampMember> present, EntityId hero, long sec)
    {
        var others = present.Where(m => m.Person != hero).OrderBy(m => m.Person.Value).ToList();
        if (others.Count == 0) return null;
        return others.FirstOrDefault(m => !m.BackstoryTold) ?? others[NightOf(sec) % others.Count];
    }

    // ---------------------------------------------------------------- night events

    /// <summary>The hour's night event (stream 0x346 keyed on the camp and the game hour): one draw over the eligible rows, "a quiet night" included.</summary>
    public CampEventResult RollNightEvent(Camp c, long sec, int dangerPm, bool watched, Season season)
    {
        long hour = sec / TimeConst.SecondsPerHour;
        var elig = Content.All<CampEventDef>().OrderBy(e => e.Id, StringComparer.Ordinal)
            .Where(e => (!e.Unwatched || !watched) && (e.Seasons.Count == 0 || e.Seasons.Contains(season.ToString())) && dangerPm >= e.DangerAtLeastPm).ToList();
        if (elig.Count == 0) return new("", "none", "", 0);
        int total = elig.Sum(e => e.WeightPm);
        int pick = DetRng.Range(Seed, hour, c.Id.Value, RngStreams.CampEvent, 0, 0, total);
        CampEventDef ev = elig[^1];
        foreach (var e in elig) { if (pick < e.WeightPm) { ev = e; break; } pick -= e.WeightPm; }
        long lost = 0;
        switch (ev.Effect)
        {
            case "steal_food":
            {
                var stock = StoreOf(c.Id);
                var food = stock.Rows().Where(r => Goods != null && Goods.FoodMeasuresPpm(r.Good) > 0 && !Goods.IsPiece(r.Good)).OrderByDescending(r => r.Qty).ThenBy(r => r.Good).FirstOrDefault();
                if (food.Qty > 0) { lost = Math.Min(food.Qty, Math.Max(0, ev.StealG)); stock.Destroy(food.Good, lost, DestroyReason.Lost); }
                break;
            }
            case "rain": if (c.Shelter is ShelterKind.None or ShelterKind.Bedroll) { c.FireLit = false; } break;
        }
        return new(ev.Id, ev.Effect, ev.Name, lost);
    }
}
