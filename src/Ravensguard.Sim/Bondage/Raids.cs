using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Bondage;

/// <summary>
/// Slaver bands, their raids and the pens they fill (slavery 4.1 rule 1, 3). A region has a raid in a season with P = 15% x the bandit slider (stream 0x630, key region and season); a raid takes 2 to 8 people from a
/// village whose lord keeps fewer than three guards. The captives are Phase 4 captives of <see cref="CaptorKind.Slavers"/>: the hold of ten days is Phase 4's, and the end of the hold is a sale, never a release with a
/// rumour. <see cref="Sweep"/> stands each sold captive in the pen of the nearest market of a law that has one, with the price table's asking price; <see cref="Market"/> lets households buy from the pens (stream 0x631).
/// </summary>
public static class Raids
{
    /// <summary>Whether a region is raided in a season (4.1): permille <c>RaidPm x bandit% / 100</c>, drawn on 0x630 with the region and the season as the key.</summary>
    public static bool Happens(ulong seed, int region, int season, int banditPct, BondRules rules) =>
        DetRng.Range(seed, season, (ulong)(uint)region, RngStreams.SlaveRaids, 0, 0, 1000) < rules.D.RaidPm * Math.Max(0, banditPct) / 100;

    /// <summary>The slaver band of a region (made when first needed): an id from the region, a camp, six to fifteen people.</summary>
    public static SlaverBand BandOf(BondWorld bw, BondBook book, int region, int camp)
    {
        var id = Slavery.BandId(region, 0);
        if (book.Bands.TryGetValue(id.Value, out var b)) return b;
        b = new SlaverBand { Id = id.Value, Camp = camp, Size = 6 + DetRng.Range(bw.Seed, 0, (ulong)(uint)region, RngStreams.SlaveRaids, 3, 0, 10) };
        book.Bands[id.Value] = b;
        Slavery.BandPurse(bw, id);
        return b;
    }

    /// <summary>
    /// The raid of a season in a region (the caller asked <see cref="Happens"/>): a village with few guards, two to eight adults of it taken into the band's hold (in id order, by draws on 0x630), one chronicle entry. Returns the people
    /// taken (empty when no village is weak enough or none has adults to take).
    /// </summary>
    public static List<EntityId> Raid(BondWorld bw, BondBook book, CaptiveBook captives, AftermathRules rules, int region, int season, int day, long sec, Func<EntityId, bool>? protectedPerson = null)
    {
        var taken = new List<EntityId>();
        var pt = bw.People; var w = bw.W;
        // the weak villages, in settlement order: fewer guards than the line and at least two adults to take
        var weak = new List<(int S, List<int> Adults)>();
        var byTown = new SortedDictionary<int, List<int>>();
        for (int i = 0; i < pt.Count; i++)
        {
            if (!pt.IsAlive(i) || pt.HomeSettlement[i] < 0) continue;
            int age = pt.AgeYears(i, day); if (age < 16 || age > 50) continue;
            var id = pt.IdOf(i);
            if (id == w.HeroId || pt.Occupation[i] == Occupation.Guard || book.Has(id) || captives.Find(id) is { Held: true } || protectedPerson?.Invoke(id) == true) continue;
            if (!byTown.TryGetValue(pt.HomeSettlement[i], out var l)) byTown[pt.HomeSettlement[i]] = l = [];
            l.Add(i);
        }
        foreach (var (s, adults) in byTown) if (bw.GuardsAt(s) < bw.D.RaidGuardsBelow && adults.Count >= 2) weak.Add((s, adults));
        if (weak.Count == 0) return taken;
        ulong rkey = (ulong)(uint)region;
        var (town, pool) = weak[DetRng.Range(bw.Seed, season, rkey, RngStreams.SlaveRaids, 1, 0, weak.Count)];
        int count = Math.Min(pool.Count, bw.D.RaidMin + DetRng.Range(bw.Seed, season, rkey, RngStreams.SlaveRaids, 2, 0, Math.Max(1, bw.D.RaidMax - bw.D.RaidMin + 1)));
        var band = BandOf(bw, book, region, town);
        for (int k = 0; k < count && pool.Count > 0; k++)
        {
            int at = DetRng.Range(bw.Seed, season, rkey, RngStreams.SlaveRaids, (uint)(10 + k), 0, pool.Count);
            int row = pool[at]; pool.RemoveAt(at);
            var who = pt.IdOf(row);
            captives.Capture(rules, CaptorKind.Slavers, who, false, new EntityId(band.Id), new PurseRef(PurseKind.Household, new EntityId(band.Id)), sec, CaptiveRank.Levy, 0, PayerKind.None, [], [], (0, 0, 0, 0, 0), 0);
            taken.Add(who);
        }
        band.Raids++; band.LastRaidSeason = season;
        if (taken.Count > 0)
        {
            var ps = new List<ChronParam> { ChronParam.Count("count", taken.Count) };
            ps.AddRange(bw.PlaceParams(town));
            var e = Slavery.Write(bw, sec, "slave_raid", "ch_slavery_raid", taken.Take(4).ToList(), Slavery.Note("ch_note_slave_weak_guard"), ps, ("count", Slavery.N(taken.Count)));
            band.Raids += 0; _ = e;
        }
        return taken;
    }

    // ------------------------------------------------------------------ the march ends in a pen

    /// <summary>The nearest settlement of a law with a market from a place: a chattel town first, then a war-captive town, ties to the lower settlement index. -1 when the region has none.</summary>
    public static int NearestMarket(BondWorld bw, int from, int count)
    {
        int best = -1; long bestD = long.MaxValue;
        foreach (var law in new[] { SlaveLaw.Chattel, SlaveLaw.WarCaptives })
        {
            for (int s = 0; s < count; s++)
            {
                if (bw.LawAt(s) != law) continue;
                long d = bw.W.DistanceM(from, s);
                if (d < bestD) { bestD = d; best = s; }
            }
            if (best >= 0) return best;
        }
        return best;
    }

    /// <summary>
    /// The captives whose hold ended in a sale (Phase 4's "sold" fate, now meant): each is stood in the pen of the nearest market, once (the book remembers who was listed), asking the table's price, the captor's purse the seller.
    /// With no market in the region the person is simply not listed: he was taken beyond it. Returns the people listed.
    /// </summary>
    public static List<EntityId> Sweep(BondWorld bw, BondBook book, CaptiveBook captives, int settlements, int day, long sec)
    {
        var listed = new List<EntityId>();
        foreach (var c in captives.Captives.Values.Where(c => c.State == CaptiveState.Released && c.Note == "sold" && !book.Swept.Contains(c.Person.Value)).OrderBy(c => c.Person.Value).ToList())
        {
            book.Swept.Add(c.Person.Value);
            if (book.Has(c.Person)) continue;
            int from = bw.W.HomeOf(c.Person); if (from < 0 && c.Person == bw.W.HeroId) from = 0;
            int market = NearestMarket(bw, from, settlements);
            if (market < 0) continue;
            var seller = c.CaptorPurse.Owner != EntityId.None ? c.CaptorPurse.Owner : c.CaptorId;
            Slavery.List(bw, book, c.Person, seller, c.CaptorPurse.Kind, market, day, sec, 0);
            listed.Add(c.Person);
        }
        return listed;
    }

    /// <summary>
    /// The season's buying at the pens (stream 0x631, key market and day): each lot is bought by the best household of its town with a draw of <c>BuyerPm</c>; a lot that has stood in the pen less than a day is left. In
    /// market order, then person order. Returns the lots bought.
    /// </summary>
    public static int Market(BondWorld bw, BondBook book, int day, long sec)
    {
        int bought = 0;
        foreach (var g in book.Bonds.Values.Where(b => b.InPen && b.Place >= 0).GroupBy(b => b.Place).OrderBy(g => g.Key).ToList())
        {
            uint lot = 0;
            foreach (var b in g.OrderBy(b => b.Person).ToList())
            {
                lot++;
                if (day - b.Since < 1) continue;
                ulong key = bw.W.PlaceId(g.Key).Value != 0 ? bw.W.PlaceId(g.Key).Value : (ulong)(uint)g.Key;
                if (DetRng.Range(bw.Seed, day, key, RngStreams.SlaveMarketTies, 1000 + lot, 0, 1000) >= bw.D.BuyerPm) continue;
                if (Slavery.Buyer(bw, g.Key, b.Ask, day, lot) is { } buyer && Slavery.Buy(bw, book, buyer, new EntityId(b.Person), day, sec).Ok) bought++;
            }
        }
        return bought;
    }
}
