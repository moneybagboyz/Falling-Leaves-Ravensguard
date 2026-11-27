using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Law;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.WorldGen.History;

namespace Ravensguard.Sim.Faith;

/// <summary>A relic as the church or the street sees it: its key (the history artefact's id, or a fake's made one), its name, the faith it belongs to, where it lies and whether it is a pardoner's fake.</summary>
public sealed record RelicInfo(ulong Key, string Name, string Religion, int Settlement, bool Fake, int Fame, byte Holder);

/// <summary>
/// Relics (faith-and-church 4.4, P6-E2): the reliquaries of the history's saints rest on the altars of the shrines (derived, never saved); only a relic that has moved is saved (<see cref="RelicRow"/>). Taking one from
/// an altar while seen is <c>sacrilege</c> (piety -30, standing -300 with the faith); a church buys one for coin; venerating one gives +2 piety once a season. Pardoners sell fakes (a Wits of 60 or a Theology of 30 spots one,
/// stream 0x656); a church that bought a fake loses piety in its priest when it is exposed.
/// </summary>
public static class Relics
{
    private const ulong FakeBit = 1UL << 63;

    /// <summary>The pardoner's purse of a settlement (a treasury purse of a faction-kind id with bit 42 set): what a fake's buyer pays.</summary>
    public static EntityId PardonerOwner(int settlement) => EntityId.Make(IdKind.Faction, (4UL << 40) | (ulong)(uint)settlement);

    private static IEnumerable<Artefact> Reliquaries(WorldState st) =>
        st.History == null ? [] : st.History.Artefacts.Where(a => a.Kind == "reliquary" && a.Status == ArtefactStatus.Held).OrderBy(a => a.Id.Value);

    /// <summary>The relics lying on the altar of a settlement's church: the derived reliquaries held by its history settlement (unless moved) and any that a church bought back or was given.</summary>
    public static List<RelicInfo> OnAltar(FaithOps.Pass p, int settlement)
    {
        var list = new List<RelicInfo>(); if (settlement < 0 || settlement >= p.Region.Settlements.Count) return list;
        var hid = p.Region.Settlements[settlement].HistoryId; int ri = p.Religions.OfSettlement(p.Region, settlement);
        string rid = p.Religions.Get(ri)?.Id ?? "";
        if (hid != EntityId.None)
            foreach (var a in Reliquaries(p.St))
            {
                if (a.Holder != hid) continue;
                if (p.Pol.Relics.TryGetValue(a.Id.Value, out var moved) && !(moved.Holder == 0 && moved.At == settlement)) continue;
                var row = p.Pol.Relics.GetValueOrDefault(a.Id.Value);
                list.Add(new RelicInfo(a.Id.Value, a.Name, rid, settlement, false, row?.Fame ?? 0, 0));
            }
        foreach (var r in p.Pol.Relics.Values)
            if (r.Holder == 0 && r.At == settlement && !list.Any(x => x.Key == r.Key))
                list.Add(new RelicInfo(r.Key, r.Name, r.Religion, settlement, r.Fake, r.Fame, 0));
        return list;
    }

    private static Artefact? st_derived(FaithOps.Pass p, ulong key) => Reliquaries(p.St).FirstOrDefault(a => a.Id.Value == key);

    /// <summary>A relic by key wherever it lies: derived on its altar, or a saved row.</summary>
    public static RelicInfo? Find(FaithOps.Pass p, ulong key)
    {
        if (p.Pol.Relics.TryGetValue(key, out var r)) return new RelicInfo(r.Key, r.Name, r.Religion, r.At, r.Fake, r.Fame, r.Holder);
        var a = st_derived(p, key); if (a == null) return null;
        for (int s = 0; s < p.Region.Settlements.Count; s++)
            if (p.Region.Settlements[s].HistoryId == a.Holder && a.Holder != EntityId.None) return new RelicInfo(key, a.Name, p.Religions.Get(p.Religions.OfSettlement(p.Region, s))?.Id ?? "", s, false, 0, 0);
        return null;
    }

    /// <summary>The relics a person carries.</summary>
    public static List<RelicInfo> CarriedBy(FaithOps.Pass p, EntityId person)
        => [.. p.Pol.Relics.Values.Where(r => r.Holder == 1 && r.Person == person.Value).Select(r => new RelicInfo(r.Key, r.Name, r.Religion, -1, r.Fake, r.Fame, 1))];

    private static RelicRow Own(FaithOps.Pass p, RelicInfo i)
    {
        if (!p.Pol.Relics.TryGetValue(i.Key, out var r)) p.Pol.Relics[i.Key] = r = new RelicRow { Key = i.Key, Name = i.Name, Religion = i.Religion, At = i.Settlement, Fake = i.Fake, Fame = i.Fame };
        return r;
    }

    // ============================================================ taking, selling, venerating

    /// <summary>
    /// Takes a relic from the altar of <paramref name="settlement"/> (4.4, FC-6). Seen (<paramref name="seen"/>: the priest or a worshipper saw it), it is <c>sacrilege</c>: the crime record with the priest for witness (piety -30
    /// through the law's hook), the church's standing -300 at once (a proclaimed deed). Unseen, the relic is only missing: the chronicle says the altar is bare and not by whom. Returns "" or a refusal key.
    /// </summary>
    public static string Steal(WorldState st, ContentDatabase db, EntityId thief, int settlement, ulong key, bool seen, int day, out RelicInfo? taken)
    {
        taken = null;
        var p = FaithOps.Of(st, db); if (p == null) return "fa_no_church";
        var info = OnAltar(p, settlement).FirstOrDefault(x => x.Key == key); if (info == null) return "fa_no_relic";
        var row = Own(p, info); row.Holder = 1; row.Person = thief.Value; row.At = -1; row.SinceDay = day; taken = info with { Holder = 1, Settlement = -1 };
        var place = FaithOps.PlaceOf(st, settlement); long sec = FaithOps.SecOf(day);
        if (!seen)
        {
            FaithOps.Chron(st, sec, "relic_taken", "ch_faith_relic_taken", [ChronParam.Place("place", place)], [place], null, "ch_note_altar_bare", null, ("relic", info.Name));
            return "";
        }
        var priest = p.Book.ParishAt(settlement)?.Priest ?? -1;
        var witnesses = priest >= 0 ? new List<WitnessRow> { new((ulong)priest, WitnessRow.KnewIt) } : [];
        var law = LawWorld.From(st, db); var book = st.Law ??= new CrimeBook();
        var rec = CrimeSystem.Commit(law, book, new ActSpec("sacrilege", thief, EntityId.None, settlement, sec, 0, 0, "ch_note_crime_hero"), witnesses, ActorLook.None);
        FaithStanding.Record(st, db, thief, "sacrilege", new EntityId(rec.Entry), settlement, 1, -1, true, day);
        return "";
    }

    /// <summary>Venerating a relic (4.3): piety +2, once a season (the hero's).</summary>
    public static string Venerate(WorldState st, ContentDatabase db, EntityId who, int settlement, ulong key, int day, out int piety)
    {
        piety = 0;
        var p = FaithOps.Of(st, db); if (p == null) return "fa_no_church";
        if (OnAltar(p, settlement).All(x => x.Key != key)) return "fa_no_relic";
        int season = day / TimeConst.DaysPerSeason;
        if (p.Pol.VenerateSeason == season) return "fa_already_venerated";
        p.Pol.VenerateSeason = season;
        piety = Piety.Adjust(p.Book, p.People, who, p.S.VeneratePiety);
        return "";
    }

    /// <summary>A church buys a relic the seller carries (4.4): the parish chest pays the price to him (a transfer, <see cref="TransferReason.RelicSale"/>), the relic lies on the altar again. A fake is bought as willingly as a true one.</summary>
    public static string Sell(WorldState st, ContentDatabase db, EntityId seller, int settlement, ulong key, int day, out long paid)
    {
        paid = 0;
        var p = FaithOps.Of(st, db); if (p == null) return "fa_no_church";
        if (p.Book.ParishAt(settlement) == null) return "fa_no_church";
        if (!p.Pol.Relics.TryGetValue(key, out var r) || r.Holder != 1 || r.Person != seller.Value) return "fa_no_relic";
        int ri = p.Religions.OfSettlement(p.Region, settlement); if (p.Religions.Get(ri)?.Id != r.Religion) return "fa_other_faith";
        var led = st.Economy!.Ledger;
        if (!led.TryFind(PurseKind.Chest, p.Region.Settlements[settlement].Id, out var chest)) return "fa_no_church";
        long price = Math.Min(p.S.RelicPriceMites, led.Balance(chest).Mites);
        var to = FaithOps.PurseOf(st, seller);
        if (price > 0 && !to.IsNone && led.Transfer(chest, to, new Coin(price), TransferReason.RelicSale)) paid = price;
        r.Holder = 0; r.At = settlement; r.Person = 0; r.SinceDay = day;
        return "";
    }

    // ============================================================ pardoners and fakes (cut item 1: built last)

    /// <summary>Whether a person sees a fake relic for what it is (4.4): Wits 60 or Theology 30.</summary>
    public static bool SpotsFake(FaithSettingsDef s, PersonTable pt, int row) => pt.Attribute(row, PersonTable.WitsIdx) >= s.FakeWits || pt.SkillLevelOf(row, Skill.Theology) >= s.FakeTheology;

    /// <summary>A pardoner comes to a town (stream 0x656 key settlement and season): a fake relic of one of the faith's saints, for sale at the standard price. At most one a settlement at a time. Returns the offers made.</summary>
    public static int PardonersCome(WorldState st, ContentDatabase db, int day)
    {
        var p = FaithOps.Of(st, db); if (p == null) return 0;
        int season = day / TimeConst.DaysPerSeason, n = 0;
        foreach (var par in p.Book.Parishes.Values)
        {
            int s = par.Settlement; ulong key = FakeBit | ((ulong)(uint)s << 20) | (uint)season;
            if (p.Pol.Relics.Values.Any(r => r.Holder == 2 && r.At == s)) continue;
            if (DetRng.Range(st.Seed, season, (ulong)(uint)s, RngStreams.FakeRelics, 0, 0, 1000) >= 30) continue;      // 3% a season a settlement
            var rel = p.Religions.Get(p.Religions.OfSettlement(p.Region, s)); if (rel == null || rel.Saints.Count == 0) continue;
            var saint = rel.Saints[DetRng.Range(st.Seed, season, (ulong)(uint)s, RngStreams.FakeRelics, 1, 0, rel.Saints.Count)];
            p.Pol.Relics[key] = new RelicRow { Key = key, Name = "Bone of " + saint.Full, Religion = rel.Id, Holder = 2, At = s, Fake = true, SinceDay = day };
            FaithOps.Chron(st, FaithOps.SecOf(day), "pardoner_came", "ch_faith_pardoner_came", [ChronParam.Place("place", FaithOps.PlaceOf(st, s))], [FaithOps.PlaceOf(st, s)], null, "ch_note_pardoner_trade", null, ("relic", "Bone of " + saint.Full));
            n++;
        }
        return n;
    }

    /// <summary>The hero looks at a relic for sale: a Wits of 60 or a Theology of 30 tells a fake (the answer is the truth only when he has the eye; else he cannot say).</summary>
    public static (bool Knows, bool Fake) Inspect(WorldState st, ContentDatabase db, EntityId who, ulong key)
    {
        var p = FaithOps.Of(st, db); if (p == null || !p.Pol.Relics.TryGetValue(key, out var r)) return (false, false);
        int row = FaithOps.RowOf(st, who); if (row < 0) return (false, false);
        return SpotsFake(p.S, p.People, row) ? (true, r.Fake) : (false, false);
    }

    /// <summary>Buys the relic a pardoner offers: the buyer's coin goes to the pardoner's purse (a transfer, <see cref="TransferReason.RelicSale"/>) and the relic is his.</summary>
    public static string Buy(WorldState st, ContentDatabase db, EntityId buyer, int settlement, ulong key, int day)
    {
        var p = FaithOps.Of(st, db); if (p == null) return "fa_no_church";
        if (!p.Pol.Relics.TryGetValue(key, out var r) || r.Holder != 2 || r.At != settlement) return "fa_no_relic";
        var led = st.Economy!.Ledger; var from = FaithOps.PurseOf(st, buyer); if (from.IsNone) return "fa_cannot_afford";
        var to = led.Open(PurseKind.Treasury, PardonerOwner(settlement));
        if (!led.Transfer(from, to, new Coin(p.S.RelicPriceMites), TransferReason.RelicSale)) return "fa_cannot_afford";
        r.Holder = 1; r.Person = buyer.Value; r.At = -1; r.SinceDay = day;
        return "";
    }

    /// <summary>
    /// A fake a church bought is exposed (stream 0x656 key relic and season, FakeExposePct a season): the chronicle says so, the priest loses piety, the relic is gone from the altar. Returns the exposures.
    /// </summary>
    public static int ExposeFakes(WorldState st, ContentDatabase db, int day)
    {
        var p = FaithOps.Of(st, db); if (p == null) return 0;
        int season = day / TimeConst.DaysPerSeason, n = 0;
        foreach (var r in p.Pol.Relics.Values.Where(x => x.Fake && x.Holder == 0).ToList())
        {
            if (DetRng.Range(st.Seed, season, r.Key, RngStreams.FakeRelics, 2, 0, 100) >= p.S.FakeExposePct) continue;
            int s = r.At; r.Holder = 3; r.At = -1; n++;
            var priest = p.Book.ParishAt(s)?.Priest ?? -1; var ps = new List<ChronParam> { ChronParam.Place("place", FaithOps.PlaceOf(st, s)) };
            if (priest >= 0) { ps.Add(ChronParam.Person("priest", new EntityId((ulong)priest))); Piety.Adjust(p.Book, p.People, new EntityId((ulong)priest), -p.S.FakeExposedPiety); }
            FaithOps.Chron(st, FaithOps.SecOf(day), "fake_relic_exposed", "ch_faith_fake_exposed", ps, [FaithOps.PlaceOf(st, s)], null, "ch_note_fake_exposed", null, ("relic", r.Name));
        }
        return n;
    }
}
