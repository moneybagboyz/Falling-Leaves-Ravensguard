using Ravensguard.Content;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Rumours;

/// <summary>
/// Caravans carry rumours between regions (Phase 3 cleanup B, item 7; dialogue spec 4.6 "a traveller from afar"). A crew member who holds a rumour and arrives in another region tells it where travellers
/// are heard, the innkeepers and merchants of the destination settlement (the same people <c>word_reached</c> rumours use): each of the first few of them takes it as a <see cref="HearHow.Traveller"/>
/// hearing two hops on, keeping the source chain's distortion flags, and the rumour is copied under the same id so a trace in either region reaches the same chronicle entry. Pure in the two worlds and
/// states; the same call on the same records always picks the same people (ascending index), so 1x and 8x agree.
/// </summary>
public static class RumourCarriers
{
    /// <summary>How many rumours one crew member tells per arrival, and how many people of the destination hear each.</summary>
    public const int PerCrew = 2, Listeners = 3;

    /// <summary>
    /// The crew <paramref name="crew"/> (rows in <paramref name="fromW"/>) tell what they hold in settlement <paramref name="destSettlement"/> of the destination. Returns how many hearings were made there.
    /// A rumour the destination already holds is not told again; one that has run out its life is not carried.
    /// </summary>
    public static int Deliver(TalkWorld fromW, TalkState fromTs, TalkWorld toW, TalkState toTs, IReadOnlyList<int> crew, int destSettlement, long hour, TalkSettingsDef set)
        => Deliver(fromW, fromTs, toW, toTs, crew, TravellerHearers(toW, toTs, destSettlement, hour), hour, set);

    /// <summary>The same, with the listeners named: the people who hear (rows in the destination world), at most <see cref="Listeners"/> per rumour, in the order given.</summary>
    public static int Deliver(TalkWorld fromW, TalkState fromTs, TalkWorld toW, TalkState toTs, IReadOnlyList<int> crew, IReadOnlyList<int> hearers, long hour, TalkSettingsDef set)
    {
        int made = 0, day = (int)(hour / 24);
        if (hearers.Count == 0) return 0;
        foreach (int c in crew.OrderBy(i => i))
        {
            var told = new HashSet<EntityId>();
            for (int k = 0; k < PerCrew; k++)
            {
                var (r, h) = RumourSpreader.BestToTell(fromTs.Rumours, c, hour + 1, id => told.Contains(id));
                if (r == null) break;
                told.Add(r.Id);
                if (r.ExpiresDay < day) continue;
                var copy = toTs.Rumours.Get(r.Id);
                if (copy == null)
                {
                    copy = new Rumour
                    {
                        Id = r.Id, Source = r.Source, Kind = r.Kind, TypeId = r.TypeId, Key = r.Key, Settlement = -1, Place = r.Settlement >= 0 && r.Settlement < fromW.SettlementNames.Count ? fromW.SettlementNames[r.Settlement] : r.Place,
                        Subjects = r.Subjects, Severity = r.Severity, BornHour = r.BornHour, ExpiresDay = r.ExpiresDay, LifeDays = r.LifeDays, InterestPm = r.InterestPm, Number = r.Number, Who = r.Who, About = r.About,
                        FactsMask = r.FactsMask, Witnesses = [], FromAfar = true, Authored = r.Authored,
                    };
                    toTs.Rumours.Add(copy);
                }
                int heard = 0;
                foreach (int p in hearers)
                {
                    if (heard >= Listeners) break;
                    if (toTs.Rumours.Holds(copy.Id, p)) { heard++; continue; }
                    var hr = new Hearing { Listener = p, Teller = -1, Hour = (int)hour, Hop = (byte)Math.Min(250, h.Hop + 2), Flags = h.Flags, PlaceAlt = -1, How = HearHow.Traveller };
                    if (toTs.Rumours.AddHearing(copy.Id, hr, set.HearingsPerPersonMax)) { made++; heard++; }
                }
            }
        }
        return made;
    }

    /// <summary>The people a traveller talks to in a settlement: innkeepers and merchants first, then the lowest-indexed residents.</summary>
    public static List<int> TravellerHearers(TalkWorld w, TalkState ts, int settlement, long hour)
    {
        var residents = RumourSeeder.Residents(w, ts, (int)(hour / 24));
        return settlement < 0 || settlement >= residents.Length ? [] : Hearers(w, residents[settlement]);
    }

    private static List<int> Hearers(TalkWorld w, int[] residents)
    {
        var p = w.People; var list = new List<int>();
        foreach (int i in residents) if (p.Occupation[i] is People.Occupation.Innkeeper or People.Occupation.Merchant) list.Add(i);
        foreach (int i in residents) { if (list.Count >= 8) break; if (!list.Contains(i)) list.Add(i); }
        return list;
    }
}
