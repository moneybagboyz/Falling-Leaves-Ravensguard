using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Dialogue;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Rumours;

/// <summary>What the Journal shows of one rumour the hero holds: the rumour, how the hero came to hear it, the chain behind it and the chronicle entry at its root. Immutable; made at a barrier.</summary>
public sealed record JournalCard(EntityId Rumour, RumourKind Kind, string Key, string Place, string Who, string About, int Number, int Settlement, Distortion Flags, short PlaceAlt, HearHow How, int Hop, int Day,
    string TellerName, string Confidence, bool PartlyTrue, int PassedThrough, IReadOnlyList<JournalStep> Steps, JournalSource Source, bool Expired);

/// <summary>One link of the trace: who (a name), the day, how, and what they heard it from (a name, empty for a witness).</summary>
public sealed record JournalStep(string Person, int Day, HearHow How, string From, int Hop);

/// <summary>The chronicle entry at the root of a trace.</summary>
public sealed record JournalSource(EntityId Entry, string TypeId, int Day, string Text, IReadOnlyList<string> Causes, bool Found, string Details = "");

/// <summary>The hero's Journal rumours as the last barrier left them.</summary>
public sealed record JournalView(IReadOnlyList<JournalCard> Cards, long Hour, ulong Hash);

/// <summary>A rumour a person tells the hero when asked for the news.</summary>
public sealed record NewsTold(EntityId Rumour, Hearing Hearing, int Teller);

/// <summary>
/// The talk systems on the world's hour (dialogue spec 4.9): at the hour barrier the new chronicle entries become rumours (H7), rumours spread among the people out together (H4 slice), and at
/// each day's first hour old rumours expire and the hearings of the dead are forgotten (H5 hour 0). The hero's asks and overhearings are applied at barriers too.
/// </summary>
public static class TalkSystem
{
    public static void AfterHour(TalkWorld w, TalkState ts, LineBook book, long hour)
    {
        if (hour <= ts.LastHour) return;
        int day = (int)(hour / 24); var set = book.Settings;
        RumourSeeder.Seed(w, ts, book, hour);
        RumourSpreader.Hour(w, ts, book, hour);
        if (day != ts.LastDay)
        {
            ts.Rumours.Purge(r => r.ExpiresDay < day, id => ts.HeroHolds(id));
            ts.Rumours.Forget(p => p >= w.People.Count || !w.People.IsAlive(p));
            ts.Relations.Decay(day, set);
            EnforceCaps(ts, set);
            ts.LastDay = day;
        }
        ts.LastHour = hour;
    }

    private static void EnforceCaps(TalkState ts, TalkSettingsDef s)
    {
        // the oldest rumours go first (the hero's are kept)
        if (ts.Rumours.HearingCount <= s.HearingsMax && ts.Rumours.Count <= s.RumoursMax) return;
        foreach (var r in ts.Rumours.Rumours.Values.OrderBy(r => r.BornHour).ThenBy(r => r.Id.Value).ToList())
        {
            if (ts.Rumours.HearingCount <= s.HearingsMax && ts.Rumours.Count <= s.RumoursMax) break;
            ts.Rumours.Purge(x => x.Id == r.Id, id => ts.HeroHolds(id));
        }
    }

    /// <summary>The rumour a person would tell the hero when asked the news: the best they hold that the hero does not. Null when they have nothing new.</summary>
    public static NewsTold? WhatTheyTell(TalkState ts, int person, long hour)
    {
        var (r, h) = RumourSpreader.BestToTell(ts.Rumours, person, hour + 1, id => ts.HeroHolds(id));
        return r == null ? null : new NewsTold(r.Id, h, person);
    }

    /// <summary>The hero hears it from <paramref name="teller"/> (told) or overhears it. Returns the new card, or null when the hero already holds it.</summary>
    public static HeroHearing? HeroHears(TalkWorld w, TalkState ts, EntityId rumour, int teller, long hour, HearHow how)
    {
        var r = ts.Rumours.Get(rumour); if (r == null || ts.HeroHolds(rumour)) return null;
        var th = teller >= 0 ? ts.Rumours.Find(rumour, teller) : null;
        var h = new HeroHearing
        {
            Rumour = rumour, Hour = (int)hour, Teller = teller, How = how, Hop = th != null ? th.Value.Hop + 1 : 0, Flags = th?.Flags ?? Distortion.None, PlaceAlt = th?.PlaceAlt ?? -1,
            TellerName = teller >= 0 && teller < w.People.Count ? w.People.FullName(teller) : "",
            Chain = teller >= 0 ? RumourTrace.Walk(ts.Rumours, rumour, teller) : [],
        };
        foreach (var st in h.Chain) h.ChainNames.Add(st.Person >= 0 && st.Person < w.People.Count ? w.People.FullName(st.Person) : "");
        ts.Heard.Add(h);
        return h;
    }

    /// <summary>The hero within earshot of a group: any pass of the last hour in this settlement whose teller is among <paramref name="nearby"/> is overheard (the first the hero does not hold).</summary>
    public static HeroHearing? Overhear(TalkWorld w, TalkState ts, int settlement, IReadOnlyCollection<int> nearby, long hour)
    {
        foreach (var p in ts.Passes)
        {
            if (p.Settlement != settlement || p.Hour < hour - 1 || !nearby.Contains(p.Teller) || ts.HeroHolds(p.Rumour)) continue;
            return HeroHears(w, ts, p.Rumour, p.Teller, hour, HearHow.Overheard);
        }
        return null;
    }

    /// <summary>
    /// An authored dialogue's <c>add_rumour</c>: the most recent rumour of the kind that the hero does not hold (so it still traces to the chronicle); with none, a rumour made up by the dialogue,
    /// which says so in the Journal.
    /// </summary>
    public static HeroHearing? AddAuthored(TalkWorld w, TalkState ts, string kind, string place, int teller, long hour)
    {
        var k = kind switch { "road_bandits" => RumourKind.RoadBandits, _ => Enum.TryParse<RumourKind>(kind, true, out var kk) ? kk : RumourKind.Omen };
        var found = ts.Rumours.Rumours.Values.Where(r => r.Kind == k && !ts.HeroHolds(r.Id) && !r.Authored).OrderByDescending(r => r.BornHour).FirstOrDefault();
        if (found != null) return HeroHears(w, ts, found.Id, teller, hour, HearHow.Asked);
        var id = EntityId.Make(IdKind.Other, 0x5000_0000UL + ts.NextAuthored++);
        var a = new Rumour { Id = id, Source = EntityId.None, Kind = k, TypeId = "authored", Key = "R_AUTHORED_" + k.ToString().ToUpperInvariant(), Place = place, BornHour = (int)hour, ExpiresDay = int.MaxValue, LifeDays = 9999, InterestPm = 300, Authored = true, FactsMask = 0 };
        ts.Rumours.Add(a);
        return HeroHears(w, ts, id, teller, hour, HearHow.Asked);
    }

    /// <summary>The Journal's cards, newest first, each with its trace resolved to names and the root chronicle entry.</summary>
    public static JournalView Journal(TalkWorld w, TalkState ts, long hour)
    {
        var cards = new List<JournalCard>();
        foreach (var h in ts.Heard)
        {
            var r = ts.Rumours.Get(h.Rumour); if (r == null) continue;
            var steps = new List<JournalStep>();
            for (int i = 0; i < h.Chain.Count; i++)
            {
                var st = h.Chain[i]; string from = i + 1 < h.Chain.Count ? h.ChainNames[i + 1] : "";
                steps.Add(new JournalStep(h.ChainNames[i], st.Hour / 24, st.How, from, st.Hop));
            }
            var e = r.Source != EntityId.None ? w.Chronicle.Get(r.Source) : null;
            var src = e == null ? new JournalSource(r.Source, r.TypeId, r.BornDay, "", [], false)
                : new JournalSource(e.Id, e.TypeId, (int)e.When.TotalDays, e.Data.TryGetValue("text", out var tx) ? tx : e.Data.TryGetValue("title", out var ti) ? ti : "",
                    [.. e.Causes.Select(c => c.Note).Where(n => n.Length > 0)], true, string.Join(", ", e.Data.Where(kv => kv.Key is not ("text" or "title")).OrderBy(kv => kv.Key, StringComparer.Ordinal).Take(5).Select(kv => $"{kv.Key}: {kv.Value}")));
            cards.Add(new JournalCard(r.Id, r.Kind, r.Key, r.Place, r.Who, r.About, r.Number, r.Settlement, h.Flags, h.PlaceAlt, h.How, h.Hop, h.Day, h.TellerName, RumourText.Confidence(h),
                RumourText.PartlyTrue(h.Flags), h.Chain.Count, steps, src, r.ExpiresDay < (int)(hour / 24)));
        }
        cards.Reverse();
        return new JournalView(cards, hour, ts.Hash());
    }
}
