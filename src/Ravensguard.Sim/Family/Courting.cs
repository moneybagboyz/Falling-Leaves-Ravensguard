using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Succession;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Family;

/// <summary>The outcome of one thing the hero asked of a courtship: whether it worked, the message key and its arguments (the desk wraps it in a hero result).</summary>
public sealed record CourtResult(bool Ok, string Key, params object[] Args);

/// <summary>Where the couple would live: an existing dwelling of the hero's household, or a home he holds (settlement, lot) the household would move into.</summary>
public readonly record struct Roof(bool Ok, int Settlement, int Lot, bool MoveIn);

/// <summary>
/// Courting and weddings (family-and-lifespan 4.1, 4.3): who may be courted, a meeting and a gift, the plea and the head of the house's consent, the dwelling gate, the wedding on a holy day with the priest's
/// fee and the dowry through the ledger. The hero is the suitor whichever his sex; the sim keeps one courtship at a time. Every roll keys on the courtship and the day (stream 0x640), never on a tick.
/// </summary>
public static class Courting
{
    // ------------------------------------------------------------------ who may be courted

    /// <summary>Parents, siblings, children and first cousins of a person row: kin within degree 2, who may not marry (the Phase 2 rule).</summary>
    public static HashSet<int> Kin2(FamilyWorld w, int p)
    {
        var kin = w.Pop.Kin; var s = new HashSet<int>();
        foreach (int par in kin.Parents(p))
        {
            s.Add(par);
            foreach (int aunt in kin.Siblings(par)) foreach (int cousin in kin.Children(aunt)) s.Add(cousin);
        }
        foreach (int sib in kin.Siblings(p)) s.Add(sib);
        foreach (int ch in kin.Children(p)) s.Add(ch);
        return s;
    }

    /// <summary>Why a person cannot be courted ("" when they can): a message key.</summary>
    public static string WhyNot(FamilyWorld w, int hero, int p, int day)
    {
        var pt = w.People;
        if (p < 0 || p >= pt.Count || pt.Status[p] != PersonStatus.Alive) return "fm_gone";
        if (pt.Spouse[hero] >= 0) return "fm_hero_wed";
        if (pt.Spouse[p] >= 0) return "fm_they_wed";
        if (pt.Sex[p] == pt.Sex[hero]) return "fm_not_so";
        int age = pt.AgeYears(p, day), min = pt.Sex[p] == Sex.Female ? w.Rates.MarryAgeMinFemale : w.Rates.MarryAgeMinMale;
        if (age < min) return "fm_too_young";
        if (w.St.Captivity?.Find(pt.IdOf(p)) is { Held: true }) return "fm_held";
        int hh = pt.Household[p];
        if (hh < 0 || hh == pt.Household[hero]) return "fm_not_so";
        if (w.Households.Head[hh] == p) return "fm_head";
        if (Kin2(w, hero).Contains(p)) return "fm_kin";
        return "";
    }

    // ------------------------------------------------------------------ a meeting and a gift

    /// <summary>One meeting (at most one a day) and, if <paramref name="giftMites"/> is more than 0, a gift from the hero's purse to the courted's household; starts the courtship.</summary>
    public static CourtResult Court(FamilyWorld w, ulong who, long giftMites, int day)
    {
        var pt = w.People; int hero = w.HeroRow, p = pt.IndexOf(new EntityId(who));
        if (hero < 0) return new(false, "fm_no_hero");
        string why = WhyNot(w, hero, p, day); if (why.Length > 0) return new(false, why);
        var book = w.Book; var s = w.S;
        var c = book.CourtshipOf(who);
        if (c == null)
        {
            if (book.Courtships.Values.Any(x => x.State is CourtState.Courting or CourtState.Betrothed)) return new(false, "fm_already_courting");
            c = new Courtship { Id = book.NextCourtship++, Suitor = w.HeroId.Value, Courted = who, StartDay = day };
            book.Courtships[c.Id] = c;
            w.Chronicle.Add(new ChronicleEntry(w.Ids.Allocate(IdKind.Event), new GameTime(day * 86400L), "courtship_begun", [w.HeroId, new EntityId(who)],
                [new Cause(CauseKind.Family, null, NoteKey: "ch_note_courting")], new Dictionary<string, string> { ["courted"] = pt.FullName(p) },
                TextKey: "ch_family_courting", Params: [ChronParam.Person("suitor", w.HeroId), ChronParam.Person("courted", new EntityId(who)), ChronParam.Place("place", w.PlaceId(pt.HomeSettlement[p]))]));
        }
        if (c.State == CourtState.Betrothed) return new(false, "fm_already_betrothed");
        var k = w.St.Party?.Know(new EntityId(who));
        if (k != null && c.LastMeetingDay != day)
        {
            c.LastMeetingDay = day; c.Meetings++;
            k.TrustPm = Math.Min(1000, k.TrustPm + s.MeetingTrustPm); k.Familiarity = Math.Min(100, k.Familiarity + 3);
        }
        if (giftMites > 0)
        {
            int hh = pt.Household[p]; var from = w.HeroPurse; var to = w.HouseholdPurse(hh);
            if (w.Ledger.Balance(from).Mites < giftMites) return new(false, "fm_cannot_afford");
            long theirs = Math.Max(1, w.Ledger.Balance(to).Mites);
            if (!w.Ledger.Transfer(from, to, Coin.FromMites(giftMites), TransferReason.Gift)) return new(false, "fm_cannot_afford");
            c.Gifts += giftMites;
            if (k != null) k.TrustPm = Math.Min(1000, k.TrustPm + (int)Math.Min(s.GiftTrustCapPm, giftMites * 100 / theirs));
            return new(true, "fm_gift_given", pt.FullName(p), giftMites);
        }
        return new(true, c.Meetings <= 1 ? "fm_met_first" : "fm_met", pt.FullName(p));
    }

    // ------------------------------------------------------------------ the plea

    /// <summary>The acceptance score of a person or a house toward the hero: <see cref="FamilyRules.Acceptance"/> with the live trust, class gap, wealth and standing.</summary>
    public static int Score(FamilyWorld w, int hero, int answerer, int hh, int courtship, int day, uint draw)
    {
        var pt = w.People; var s = w.S;
        int gap = Math.Abs((int)pt.Class[hero] - (int)w.Households.Class[hh]);
        long mine = w.Ledger.Balance(w.HeroPurse).Mites + w.Ledger.Balance(w.HouseholdPurse(pt.Household[hero])).Mites, theirs = w.Ledger.Balance(w.HouseholdPurse(hh)).Mites;
        int trust = answerer >= 0 ? w.TrustPm(pt.IdOf(answerer)) : 0;
        return FamilyRules.Acceptance(s, trust, gap, mine.CompareTo(theirs), w.StandingPm(w.Households.Settlement[hh]), FamilyRules.Jitter(s, w.Seed, courtship, day, draw));
    }

    /// <summary>
    /// The hero asks for her hand (4.1): the courted answers first (score at least 60), then the head of her household with the same score from his own trust and the house's standing; a yes is a betrothal
    /// and, when the couple has a roof, a wedding on the next holy day at least 5 days away. A plea is answered once a day.
    /// </summary>
    public static CourtResult Ask(FamilyWorld w, ulong who, int day)
    {
        var pt = w.People; int hero = w.HeroRow, p = pt.IndexOf(new EntityId(who));
        if (hero < 0) return new(false, "fm_no_hero");
        string why = WhyNot(w, hero, p, day); if (why.Length > 0) return new(false, why);
        var c = w.Book.CourtshipOf(who);
        if (c == null || c.Meetings < 1) return new(false, "fm_not_courted");
        if (c.State == CourtState.Betrothed) return new(false, "fm_already_betrothed");
        if (c.AskedDay == day) return new(false, "fm_asked_today");
        c.AskedDay = day;
        string name = pt.FullName(p); int hh = pt.Household[p];
        c.Score = Score(w, hero, p, hh, c.Id, day, 0);
        if (c.Score < w.S.YesAt) { c.Consent = ConsentState.Pending; return new(false, "fm_says_no", name, c.Score); }
        int head = w.Households.Head[hh];
        int headScore = head >= 0 && head != p ? Score(w, hero, head, hh, c.Id, day, 1) : int.MaxValue;
        if (headScore < w.S.YesAt) { c.Consent = ConsentState.No; return new(false, "fm_house_no", name, pt.FullName(head)); }
        if (!ChurchAllows(w, w.HeroId) || !ChurchAllows(w, pt.IdOf(p))) return new(false, "fm_church_refuses");
        c.Consent = ConsentState.Yes; c.State = CourtState.Betrothed;
        var roof = RoofFor(w, hero);
        if (roof.Ok) c.WeddingDay = FamilyRules.NextHolyDay(w.S, day);
        w.Chronicle.Add(new ChronicleEntry(w.Ids.Allocate(IdKind.Event), new GameTime(day * 86400L), "betrothal", [w.HeroId, new EntityId(who)],
            [new Cause(CauseKind.Family, null, NoteKey: "ch_note_betrothal")], new Dictionary<string, string> { ["courted"] = name },
            TextKey: "ch_family_betrothal", Params: [ChronParam.Person("suitor", w.HeroId), ChronParam.Person("courted", new EntityId(who)), ChronParam.Place("place", w.PlaceId(pt.HomeSettlement[p]))]));
        return roof.Ok ? new(true, "fm_betrothed", name, c.WeddingDay) : new(true, "fm_betrothed_waits", name);
    }

    /// <summary>Breaks a courtship off (the hero's word).</summary>
    public static CourtResult Break(FamilyWorld w, ulong who)
    {
        var c = w.Book.CourtshipOf(who);
        if (c == null) return new(false, "fm_not_courted");
        c.State = CourtState.Broken;
        return new(true, "fm_broken_off");
    }

    // ------------------------------------------------------------------ the church (the one call site of the wedding's refusal, B4's hook)

    /// <summary>
    /// Whether the church will marry a person (faith-and-church 4.3): <see cref="Faith.ChurchServices.Allows"/> for the wedding, with their own religion (their culture's): not excommunicated, and a standing with the
    /// religion above the floor of -500. A world without a faith book answers as the old rule did (an excommunicated person is refused). The one place the family asks, for courtships and arranged matches alike.
    /// </summary>
    public static bool ChurchAllows(FamilyWorld w, EntityId person)
    {
        var st = w.St;
        if (st.Standing?.Marks is { } marks && marks.Of(person)?.Excommunicated.Count > 0) return false;
        if (w.Content == null) return true;
        int row = w.RowOf(person); if (row < 0) return true;
        int religion = Faith.FaithWiring.ReligionsOf(st, w.Content).OfCulture(w.People.Culture[row]);
        return Faith.ChurchServices.Allows(st.Standing, person, religion, Faith.ChurchServices.Service.Wedding);
    }

    // ------------------------------------------------------------------ a roof

    private static int BedsOfLot(FamilyWorld w, int s, int lot)
    {
        int beds = s < w.Pop.LotBeds.Length && lot < w.Pop.LotBeds[s].Length ? w.Pop.LotBeds[s][lot] : 0;
        if (beds > 0) return beds;
        var lots = w.Pop.Region.Settlements[s].Plan.Lots;
        return lot < lots.Count ? lots[lot].Dwelling switch { DwellingType.Cottage => 3, DwellingType.Longhouse => 5, DwellingType.Burgage => 4, _ => 0 } : 0;
    }

    /// <summary>The dwelling gate (4.3): the hero's household has a dwelling with a free bed, or he holds a vacant home to move into; otherwise the wedding waits.</summary>
    public static Roof RoofFor(FamilyWorld w, int hero)
    {
        var hht = w.Households; int hh = w.People.Household[hero];
        if (hh < 0) return default;
        if (hht.Lot[hh] >= 0 && hht.Dwelling[hh] != DwellingType.None && hht.MemberCount[hh] < hht.Beds[hh]) return new(true, hht.Settlement[hh], hht.Lot[hh], false);
        if (hht.Lot[hh] >= 0 && hht.Dwelling[hh] != DwellingType.None) return default;
        foreach (var (s, lot) in w.HeldHomes())
        {
            if ((uint)s >= (uint)w.Pop.Region.Settlements.Count || (uint)lot >= (uint)w.Pop.Region.Settlements[s].Plan.Lots.Count) continue;
            if (w.Pop.LotHousehold[s][lot] >= 0 || BedsOfLot(w, s, lot) < 2) continue;
            return new(true, s, lot, true);
        }
        return default;
    }

    // ------------------------------------------------------------------ the wedding

    /// <summary>Betrothals waiting for a roof get their day; betrothals whose day has come are wed; a courtship whose wedding has waited too long is called off (family 4.1).</summary>
    public static void Daily(FamilyWorld w, int day)
    {
        if (w.St.Family is not { } book || book.Courtships.Count == 0) return;
        int hero = w.HeroRow; var pt = w.People;
        foreach (var c in book.Courtships.Values.Where(x => x.State == CourtState.Betrothed).OrderBy(x => x.Id).ToList())
        {
            int p = pt.IndexOf(new EntityId(c.Courted));
            if (hero < 0 || p < 0 || pt.Status[p] != PersonStatus.Alive || pt.Spouse[p] >= 0 || pt.Spouse[hero] >= 0) { CallOff(w, c, p, day, "ch_note_wedding_lost"); continue; }
            if (c.WeddingDay < 0)
            {
                if (RoofFor(w, hero).Ok) c.WeddingDay = FamilyRules.NextHolyDay(w.S, day);
                else if (day - c.StartDay > w.S.WeddingGiveUpDays * 4) CallOff(w, c, p, day, "ch_note_wedding_no_roof");
                continue;
            }
            if (day < c.WeddingDay) continue;
            var res = Wed(w, c, hero, p, day);
            if (!res.Ok && day - c.WeddingDay > w.S.WeddingGiveUpDays) CallOff(w, c, p, day, res.Key == "fm_church_refuses" ? "ch_note_wedding_church" : res.Key == "fm_no_roof" ? "ch_note_wedding_no_roof" : "ch_note_wedding_fee");
        }
    }

    private static void CallOff(FamilyWorld w, Courtship c, int p, int day, string noteKey)
    {
        c.State = CourtState.Broken;
        w.Chronicle.Add(new ChronicleEntry(w.Ids.Allocate(IdKind.Event), new GameTime(day * 86400L), "wedding_called_off", [w.HeroId, new EntityId(c.Courted)],
            [new Cause(CauseKind.Family, null, NoteKey: noteKey)], new Dictionary<string, string>(),
            TextKey: "ch_family_called_off", Params: [ChronParam.Person("suitor", w.HeroId), ChronParam.Person("courted", new EntityId(c.Courted))]));
    }

    /// <summary>The priest of the bride's or groom's settlement (the lowest person id of that trade), if any; the fee goes to his household's purse, else the fief's treasury.</summary>
    public static PurseId? FeePurse(FamilyWorld w, int settlement)
    {
        var pt = w.People;
        for (int i = 0; i < pt.Count; i++)
            if (pt.Status[i] == PersonStatus.Alive && pt.Occupation[i] == Occupation.Priest && pt.HomeSettlement[i] == settlement && pt.Household[i] >= 0) return w.HouseholdPurse(pt.Household[i]);
        int fief = settlement >= 0 && settlement < w.Pop.Region.Settlements.Count ? w.Pop.Region.Settlements[settlement].FiefIndex : -1;
        return fief >= 0 && fief < w.Pop.Region.Fiefs.Count && w.Ledger.TryFind(PurseKind.Treasury, w.Pop.Region.Fiefs[fief].Id, out var tp) ? tp : null;
    }

    /// <summary>The wedding itself (4.1 to 4.3): the church's consent, the roof, the priest's fee, then the bride's house gives the dowry and a tenth of its goods, the spouse joins the hero's household and becomes kin.</summary>
    public static CourtResult Wed(FamilyWorld w, Courtship c, int hero, int p, int day)
    {
        var pt = w.People; var hht = w.Households; var s = w.S; var ledger = w.Ledger;
        if (!ChurchAllows(w, w.HeroId) || !ChurchAllows(w, pt.IdOf(p))) return new(false, "fm_church_refuses");
        var roof = RoofFor(w, hero);
        if (!roof.Ok) return new(false, "fm_no_roof");
        int heroHh = pt.Household[hero], fromHh = pt.Household[p], settlement = roof.Settlement;
        if (fromHh < 0 || heroHh < 0) return new(false, "fm_gone");
        var fee = FeePurse(w, settlement);
        if (fee != null && s.WeddingFeeMites > 0 && ledger.Balance(w.HeroPurse).Mites < s.WeddingFeeMites) return new(false, "fm_cannot_afford_fee");
        if (fee is { } feePurse && s.WeddingFeeMites > 0) ledger.Transfer(w.HeroPurse, feePurse, Coin.FromMites(s.WeddingFeeMites), TransferReason.Fee);
        // a roof the hero holds becomes the household's dwelling
        if (roof.MoveIn)
        {
            var lot = w.Pop.Region.Settlements[settlement].Plan.Lots[roof.Lot];
            hht.Settlement[heroHh] = settlement; hht.Lot[heroHh] = (short)roof.Lot; hht.Dwelling[heroHh] = lot.Dwelling == DwellingType.None ? DwellingType.Cottage : lot.Dwelling; hht.Beds[heroHh] = (short)BedsOfLot(w, settlement, roof.Lot);
            hht.Deed[heroHh] = DeedKind.Rented; w.Pop.LotHousehold[settlement][roof.Lot] = heroHh;
            pt.HomeSettlement[hero] = settlement; pt.HomeLot[hero] = (short)roof.Lot;
            foreach (int m in hht.Members(heroHh)) { pt.HomeSettlement[m] = settlement; pt.HomeLot[m] = (short)roof.Lot; }
        }
        // the dowry: the bride's household gives its class share of its coin to the couple's household (none when the bride is the hero: it is her own house)
        long dowry = 0; var brideHh = pt.Sex[p] == Sex.Female ? fromHh : heroHh;
        if (brideHh == fromHh && fromHh != heroHh)
        {
            int pct = w.F.DowryPct(hht.Class[fromHh]);
            long movables = ledger.Balance(w.HouseholdPurse(fromHh)).Mites;
            dowry = movables * pct / 100;
            if (dowry > 0) ledger.Transfer(w.HouseholdPurse(fromHh), w.HouseholdPurse(heroHh), Coin.FromMites(dowry), TransferReason.Dowry);
            // a tenth of each good to start the hearth (the Phase 2 rule), conserved by the store move
            var src = w.Econ.HouseholdStore[fromHh]; var dst = w.Econ.HouseholdStore[heroHh]; int goodsPct = w.Rates.SettleInGoodsPct;
            foreach (var (g, q) in src.Rows().ToList()) { long give = q * goodsPct / 100; if (give > 0) Store.Move(src, dst, g, give); }
        }
        // the spouse joins the hero's household and is kin
        hht.RemoveMember(fromHh, p); hht.AddMemberLater(heroHh, p);
        pt.Household[p] = heroHh; pt.HomeSettlement[p] = hht.Settlement[heroHh]; pt.HomeLot[p] = hht.Lot[heroHh]; pt.Bed[p] = -1; pt.Class[p] = hht.Class[heroHh];
        if (pt.Homeless[p] != HomelessReason.None) pt.Homeless[p] = HomelessReason.None;
        pt.Spouse[hero] = p; pt.Spouse[p] = hero; pt.MarriageWait[p] = 0;
        w.Pop.ResetKin();
        var sb = w.St.Succession ??= new SuccessionBook();
        if (!sb.Kin.Any(k => k.Person == pt.IdOf(p))) sb.Kin.Add(new HeroKinLink(pt.IdOf(p), KinRelation.Spouse));
        c.State = CourtState.Wed;
        string heroName = w.St.Hero?.Name ?? pt.FullName(hero), spouseName = pt.FullName(p);
        var bride = pt.Sex[p] == Sex.Female ? pt.IdOf(p) : w.HeroId; var groom = pt.Sex[p] == Sex.Female ? w.HeroId : pt.IdOf(p);
        var ps = new List<ChronParam> { ChronParam.Person("bride", bride), ChronParam.Person("groom", groom), ChronParam.Place("place", w.PlaceId(settlement)) };
        if (dowry > 0) ps.Add(ChronParam.Mites("dowry", dowry));
        w.Chronicle.Add(new ChronicleEntry(w.Ids.Allocate(IdKind.Event), new GameTime(day * 86400L + 12 * 3600), "hero_wedding", [w.HeroId, pt.IdOf(p)],
            [new Cause(CauseKind.Family, null, NoteKey: "ch_note_courted_and_wed")],
            new Dictionary<string, string> { ["bride"] = pt.Sex[p] == Sex.Female ? spouseName : heroName, ["groom"] = pt.Sex[p] == Sex.Female ? heroName : spouseName, ["dowry"] = dowry.ToString(System.Globalization.CultureInfo.InvariantCulture), ["settlement"] = w.PlaceName(settlement) },
            TextKey: dowry > 0 ? "ch_family_wedding_dowry" : "ch_family_wedding", Params: ps));
        return new(true, "fm_wed", spouseName, dowry);
    }
}
