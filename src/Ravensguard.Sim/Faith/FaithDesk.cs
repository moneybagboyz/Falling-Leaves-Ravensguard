using System.Globalization;
using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Succession;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Sites;
using Ravensguard.Sim.Society;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Faith;

/// <summary>
/// Puts a deed of the church into the standing book without waiting for gossip (faith-and-church 4.3, 4.4): the deed's row of <c>society/deeds.jsonc</c> reads the chronicle entry, the hearings are the
/// parish's (the priest and the neighbours) or, for a pilgrimage the church proclaims, the whole faith, and the doer's standing is recomputed at once.
/// </summary>
public static class FaithStanding
{
    /// <summary>Counts <paramref name="deedId"/> for <paramref name="doer"/> now. <paramref name="heardHere"/> is how many of <paramref name="place"/>'s people heard (negative: all of them); <paramref name="proclaimed"/> has the faith hear it in full.</summary>
    public static void Record(WorldState st, ContentDatabase content, EntityId doer, string deedId, EntityId entry, int place, int amount, int heardHere, bool proclaimed, int day)
    {
        var deeds = DeedTable.From(content);
        if (deeds.Get(deedId) == null) return;
        var book = st.Standing ??= new StandingBook();
        var map = new RegionGroups(st.Population.Region, st.Population.People, day, FaithWiring.ReligionsOf(st));
        if (heardHere < 0) heardHere = map.Members(GroupRef.Settlement(place));          // everyone in the place knows (a household's slaves)
        var rec = new DeedRecord { Entry = entry, Doer = doer, DeedId = deedId, Day = day, Place = place, Amount = Math.Max(1, amount), Proclaimed = proclaimed };
        if (heardHere > 0 && place >= 0) rec.Heard[place] = heardHere;
        book.AddDeed(rec);
        book.Recompute(doer, deeds, map, day);
    }
}

/// <summary>The hero's side of the church, as the Faith panel shows it (faith-and-church 6): built at a barrier, read only.</summary>
public sealed record FaithView(string ReligionName, string ReligionId, string Deity, int PietyNow, string PietyBandKey, int StandingPm, StandingBand Band, bool Excommunicated,
    string PriestName, string BishopName, string ParishName, bool ParishVacant, string SeeName, bool SeeVacant,
    IReadOnlyList<(string Name, int InDays, bool Today, bool Saint)> HolyDays, IReadOnlyList<string> Doctrines, string HolySiteName, int HolySiteSettlement, bool OnPilgrimage, string PilgrimTarget, int PilgrimagesDone,
    bool ServiceToday, string FeastToday, bool AttendedToday, int LastServiceDay, int ServiceAdults, int ServiceAttended, int Day,
    IReadOnlyList<(string Name, int Settlement, bool Site)> Shrines, int HomeSettlement, int Saints, int PilgrimSettlement = -1)
{
    public static readonly FaithView Empty = new("", "", "", 0, "fa_band_observant", 0, StandingBand.Unknown, false, "", "", "", false, "", false, [], [], "", -1, false, "", 0, false, "", false, -1, 0, 0, 0, [], -1, 0);

    /// <summary>The hero's faith as it stands: his religion, piety and standing with the church, the priest of his parish and the bishop of his see, the holy days ahead, the shrines to walk to.</summary>
    public static FaithView Of(WorldState st, EntityId hero, int homeSettlement, int day)
    {
        var book = FaithWiring.Bind(st); var religions = FaithWiring.ReligionsOf(st);
        if (book == null || religions.Count == 0) return Empty with { Day = day };
        var pop = st.Population; var pt = pop.People; var region = pop.Region;
        int row = HeroPerson.RowOf(pt, st.Hero, hero);
        int home = homeSettlement >= 0 ? homeSettlement : row >= 0 ? pt.HomeSettlement[row] : -1;
        int ri = row >= 0 ? religions.OfCulture(pt.Culture[row]) : home >= 0 ? religions.OfSettlement(region, home) : -1;
        var rel = religions.Get(ri); if (rel == null) return Empty with { Day = day };
        int piety = Piety.Of(book, pt, hero);
        int standing = Standing.Of(st.Standing, hero, GroupRef.Religion(ri));
        bool excom = st.Standing?.Marks.IsExcommunicated(hero, ri) ?? false;
        int doy = day % TimeConst.DaysPerYear + 1;
        string Name(int s) => s >= 0 && s < region.Settlements.Count ? region.Settlements[s].Name : "";
        string PersonName(long id) { if (id < 0) return ""; int r = pt.IndexOf(new EntityId((ulong)id)); return r >= 0 ? pt.FullName(r) : ""; }
        var parish = book.ParishAt(home);
        int realm = home >= 0 ? ChurchSeeding.RealmOf(region, home) : -1;
        var see = book.SeeOf(rel.Id, realm);
        var days = rel.Upcoming(doy, 6).Select(u => (u.Day.Name, u.InDays, u.InDays == 0, u.Day.Kind == "saint")).ToList();
        var docs = new List<string> { "slavery_" + rel.Doctrine.SlaveryStance, "usury_" + rel.Doctrine.Usury, rel.Doctrine.Fasting ? "fasting" : "no_fasting", rel.Doctrine.ChurchCourts ? "church_courts" : "no_church_courts", rel.Doctrine.ClergyMarry ? "clergy_marry" : "clergy_celibate" };
        var shrines = religions.ShrinesIn(region, ri); int site = shrines.Count > 0 ? shrines[0] : -1;
        var today = rel.HolyDayOn(doy);
        var pg = book.Pilgrim;
        return new FaithView(rel.Name, rel.Id, rel.DeityTerm(0), piety, Piety.BandKey(piety), standing, Standing.BandOf(standing), excom,
            parish == null ? "" : PersonName(parish.Priest), see == null ? "" : PersonName(see.Bishop), Name(home), parish is { Priest: < 0 }, see == null ? "" : Name(see.Seat), see is { Bishop: < 0 },
            days, docs, Name(site), site, pg != null, pg == null ? "" : Name(pg.Target), book.Pilgrimages,
            today != null, today?.Name ?? "", book.LastServiceDay == day, parish?.ServiceDay ?? -1, parish?.Adults ?? 0, parish?.Attended ?? 0, day,
            [.. shrines.Select((s, i) => (Name(s), s, i == 0))], home, rel.Saints.Count, pg?.Target ?? -1);
    }
}

/// <summary>
/// The hero's acts of faith at the barrier (faith-and-church 4.1, 4.3, 4.4; recorded for replay like every hero command, the tag <c>fa:</c>): attend a holy day's service (piety +1, once a day), give alms
/// to the parish chest (piety by the gift, the deed for standing), take a pilgrim's vow (Q beside a priest), and arrive at the shrine (piety +10, a badge, the shrine's comfort, standing +80 with the faith).
/// </summary>
public static class FaithDesk
{
    public const string Prefix = "fa:";

    private static string N(long v) => v.ToString(CultureInfo.InvariantCulture);

    private static HeroCommand Make(string tag, int settlement, long sec) => new() { Kind = HeroCommandKind.Faith, IssuedSec = sec, Origin = CommandOrigin.Human, Settlement = settlement, Tag = Prefix + tag };

    /// <summary>Attend the service at the church of <paramref name="settlement"/> (only on one of its faith's holy days).</summary>
    public static HeroCommand Attend(int settlement, long sec) => Make("attend|" + N(settlement), settlement, sec);
    /// <summary>Give <paramref name="mites"/> in alms to the parish chest of <paramref name="settlement"/>.</summary>
    public static HeroCommand Alms(int settlement, long mites, long sec) => Make("alms|" + N(settlement) + "|" + N(mites), settlement, sec);
    /// <summary>Take a pilgrim's vow to walk to the shrine at <paramref name="target"/>.</summary>
    public static HeroCommand Vow(int from, int target, long sec) => Make("vow|" + N(target), from, sec);
    /// <summary>Arrive at <paramref name="settlement"/>: completes the pilgrimage when it is the shrine the vow named.</summary>
    public static HeroCommand Arrive(int settlement, long sec) => Make("arrive|" + N(settlement), settlement, sec);
    /// <summary>Give up the pilgrimage.</summary>
    public static HeroCommand Abandon(long sec) => Make("abandon", -1, sec);
    // ---- P6-E2: excommunication, relics, superstition (faith 4.4, 4.6, 4.7)
    /// <summary>Ask the bishop to excommunicate <paramref name="target"/> (a person id) on the chronicle entry <paramref name="cause"/>, at the gift of 100 crowns.</summary>
    public static HeroCommand AskBan(long target, long cause, long sec) => Make("ban|" + N(target) + "|" + N(cause), -1, sec);
    /// <summary>Do penance: a tenth of the hero's treasury (or coin) to his see, once a pilgrimage has been made since the ban.</summary>
    public static HeroCommand Penance(long sec) => Make("penance", -1, sec);
    /// <summary>Take the relic <paramref name="key"/> from the altar of <paramref name="settlement"/>; <paramref name="seen"/> says whether the priest or a worshipper saw it.</summary>
    public static HeroCommand Steal(int settlement, ulong key, bool seen, long sec) => Make("steal|" + N(settlement) + "|" + N((long)key) + "|" + (seen ? "1" : "0"), settlement, sec);
    public static HeroCommand Venerate(int settlement, ulong key, long sec) => Make("venerate|" + N(settlement) + "|" + N((long)key), settlement, sec);
    /// <summary>Sell a relic the hero carries to the church of <paramref name="settlement"/>.</summary>
    public static HeroCommand SellRelic(int settlement, ulong key, long sec) => Make("sellrelic|" + N(settlement) + "|" + N((long)key), settlement, sec);
    /// <summary>Buy the relic a pardoner offers at <paramref name="settlement"/>.</summary>
    public static HeroCommand BuyRelic(int settlement, ulong key, long sec) => Make("buyrelic|" + N(settlement) + "|" + N((long)key), settlement, sec);
    /// <summary>Show the head of a slain monster at <paramref name="settlement"/>.</summary>
    public static HeroCommand ShowHead(int settlement, long sec) => Make("head|" + N(settlement), settlement, sec);
    /// <summary>Buy a charm from the priest of <paramref name="settlement"/>.</summary>
    public static HeroCommand Charm(int settlement, long sec) => Make("charm|" + N(settlement), settlement, sec);

    private static HeroResult R(HeroCommand c, HeroResultCode code, string key, params object[] args) => new(c.Seq, c.Kind, code, HeroText.Of(key, args), 0, 0, c.IssuedSec);

    public static HeroResult Apply(HeroContext ctx, HeroCommand c)
    {
        if (!c.Tag.StartsWith(Prefix, StringComparison.Ordinal)) return R(c, HeroResultCode.Invalid, "HERO_UNKNOWN_COMMAND");
        var f = c.Tag[Prefix.Length..].Split('|');
        var st = ctx.State;
        var book = FaithWiring.Bind(st);
        if (book == null || book.Religions.Count == 0) return R(c, HeroResultCode.Refused, "fa_no_church");
        var pt = st.Population.People; var region = st.Population.Region;
        var hero = ctx.Party.Hero; int day = (int)(c.IssuedSec / 86400);
        int row = HeroPerson.RowOf(pt, st.Hero, hero);
        int ri = row >= 0 ? book.Religions.OfCulture(pt.Culture[row]) : -1;
        if (ri < 0) return R(c, HeroResultCode.Refused, "fa_no_faith");
        var rel = book.Religions.Get(ri)!;
        int S(int i) { return f.Length > i && int.TryParse(f[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : -1; }
        long L(int i) { return f.Length > i && long.TryParse(f[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : -1; }
        switch (f[0])
        {
            case "attend":
            {
                int s = S(1); var p = book.ParishAt(s);
                if (p == null) return R(c, HeroResultCode.Invalid, "fa_no_church");
                if (book.Religions.OfSettlement(region, s) != ri) return R(c, HeroResultCode.Refused, "fa_other_faith");
                if (!ChurchServices.Allows(st.Standing, hero, ri, ChurchServices.Service.Absolution)) return R(c, HeroResultCode.Refused, "fa_refused");
                if (st.FaithPol?.Heresies.TryGetValue(s, out var inter) == true && inter.Interdict) return R(c, HeroResultCode.Refused, "fa_interdict");
                if (rel.HolyDayOn(day % TimeConst.DaysPerYear + 1) is not { } hd) return R(c, HeroResultCode.Refused, "fa_no_service_today");
                if (book.LastServiceDay == day) return R(c, HeroResultCode.Refused, "fa_already_attended");
                book.LastServiceDay = day; book.HeroId = (long)hero.Value;
                int now = Piety.Adjust(book, pt, hero, book.Settings.ServicePiety);
                return new HeroResult(c.Seq, c.Kind, HeroResultCode.Ok, HeroText.Of("fa_attended", hd.Name, now), 0, now, c.IssuedSec);
            }
            case "ban":
            {
                long target = L(1), cause = L(2);
                string key = Excommunication.Request(st, ctx.Content, hero, new EntityId((ulong)target), new EntityId((ulong)cause), day, out _);
                if (key.Length > 0) return R(c, key == "fa_cannot_afford" ? HeroResultCode.CannotAfford : HeroResultCode.Refused, key, Quotes.Money(book.Settings.ExcommunicationGiftMites));
                return new HeroResult(c.Seq, c.Kind, HeroResultCode.Ok, HeroText.Of("fa_ban_laid", pt.IndexOf(new EntityId((ulong)target)) is var tr and >= 0 ? pt.FullName(tr) : ""), book.Settings.ExcommunicationGiftMites, 0, c.IssuedSec);
            }
            case "penance":
            {
                var x = st.FaithPol?.ActiveFor(hero.Value);
                string key = Excommunication.Penance(st, ctx.Content, hero, x != null && book.Pilgrimages > x.PilgrimsBefore, day);
                return key.Length > 0 ? R(c, HeroResultCode.Refused, key) : R(c, HeroResultCode.Ok, "fa_penance_done");
            }
            case "steal":
            {
                int s = S(1); ulong k = (ulong)L(2); bool seen = S(3) == 1;
                string key = Relics.Steal(st, ctx.Content, hero, s, k, seen, day, out var info);
                return key.Length > 0 ? R(c, HeroResultCode.Refused, key) : R(c, HeroResultCode.Ok, seen ? "fa_relic_taken_seen" : "fa_relic_taken", info?.Name ?? "");
            }
            case "venerate":
            {
                string key = Relics.Venerate(st, ctx.Content, hero, S(1), (ulong)L(2), day, out int piety);
                return key.Length > 0 ? R(c, HeroResultCode.Refused, key) : new HeroResult(c.Seq, c.Kind, HeroResultCode.Ok, HeroText.Of("fa_venerated", piety), 0, piety, c.IssuedSec);
            }
            case "sellrelic":
            {
                string key = Relics.Sell(st, ctx.Content, hero, S(1), (ulong)L(2), day, out long paid);
                return key.Length > 0 ? R(c, HeroResultCode.Refused, key) : new HeroResult(c.Seq, c.Kind, HeroResultCode.Ok, HeroText.Of("fa_relic_sold", Quotes.Money(paid)), paid, 0, c.IssuedSec);
            }
            case "buyrelic":
            {
                string key = Relics.Buy(st, ctx.Content, hero, S(1), (ulong)L(2), day);
                return key.Length > 0 ? R(c, key == "fa_cannot_afford" ? HeroResultCode.CannotAfford : HeroResultCode.Refused, key, Quotes.Money(book.Settings.RelicPriceMites)) : R(c, HeroResultCode.Ok, "fa_relic_bought");
            }
            case "head":
            {
                string key = Superstition.ShowHead(st, ctx.Content, hero, S(1), day, out int level);
                return key.Length > 0 ? R(c, HeroResultCode.Refused, key) : new HeroResult(c.Seq, c.Kind, HeroResultCode.Ok, HeroText.Of("fa_head_shown", level), 0, level, c.IssuedSec);
            }
            case "charm":
            {
                int s = S(1); var p = book.ParishAt(s);
                if (p == null || p.Priest < 0) return R(c, HeroResultCode.Refused, "fa_no_church");
                var to = FaithOps.PurseOf(st, new EntityId((ulong)p.Priest));
                if (to.IsNone || !ctx.Econ.Ledger.Transfer(ctx.Purse, to, new Coin(book.Settings.CharmMites), TransferReason.Trade)) return R(c, HeroResultCode.CannotAfford, "fa_cannot_afford", Quotes.Money(book.Settings.CharmMites));
                SiteDesk.Make(ctx, new LootItem("charm", "tin", 20, 20, 1000));
                return R(c, HeroResultCode.Ok, "fa_charm_bought");
            }
            case "alms":
            {
                int s = S(1); long mites = f.Length > 2 && long.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long m) ? m : 0;
                var p = book.ParishAt(s);
                if (p == null || mites <= 0) return R(c, HeroResultCode.Invalid, "fa_no_church");
                var led = ctx.Econ.Ledger;
                if (!led.TryFind(PurseKind.Chest, region.Settlements[s].Id, out var chest)) return R(c, HeroResultCode.Invalid, "fa_no_church");
                if (!led.Transfer(ctx.Purse, chest, new Coin(mites), TransferReason.Alms)) return R(c, HeroResultCode.CannotAfford, "fa_cannot_afford", Quotes.Money(mites));
                int piety = Math.Min(book.Settings.AlmsPietyMax, (int)(mites / 100) * book.Settings.AlmsPietyPer100);
                if (piety > 0) Piety.Adjust(book, pt, hero, piety);
                var place = region.Settlements[s].Id;
                var e = new ChronicleEntry(st.Ids.Allocate(IdKind.Event), new GameTime(c.IssuedSec), "alms_given", [hero], [new Cause(CauseKind.Faith, null, NoteKey: "ch_note_alms_charity")],
                    new Dictionary<string, string> { ["mites"] = N(mites), ["settlement"] = region.Settlements[s].Name }, TextKey: "ch_faith_alms",
                    Params: [ChronParam.Person("doer", hero), ChronParam.Mites("amount", mites), ChronParam.Place("place", place)]);
                st.Chronicle.Add(e);
                FaithStanding.Record(st, ctx.Content, hero, "alms_given", e.Id, s, (int)Math.Min(int.MaxValue, mites), 6, false, day);
                return new HeroResult(c.Seq, c.Kind, HeroResultCode.Ok, HeroText.Of("fa_alms_given", Quotes.Money(mites), region.Settlements[s].Name), mites, piety, c.IssuedSec);
            }
            case "vow":
            {
                int target = S(1);
                if (book.Pilgrim != null) return R(c, HeroResultCode.Refused, "fa_already_pilgrim");
                if (!book.Religions.ShrinesIn(region, ri).Contains(target)) return R(c, HeroResultCode.Refused, "fa_no_shrine");
                var place = region.Settlements[target].Id;
                var e = new ChronicleEntry(st.Ids.Allocate(IdKind.Event), new GameTime(c.IssuedSec), "pilgrimage_started", [hero], [new Cause(CauseKind.Faith, null, NoteKey: "ch_note_pilgrim_vow")],
                    new Dictionary<string, string> { ["settlement"] = region.Settlements[target].Name }, TextKey: "ch_faith_pilgrim_start", Params: [ChronParam.Person("doer", hero), ChronParam.Place("place", place)]);
                st.Chronicle.Add(e);
                book.Pilgrim = new PilgrimRow { Religion = rel.Id, Target = target, StartDay = day, Entry = e.Id.Value }; book.HeroId = (long)hero.Value;
                return R(c, HeroResultCode.Ok, "fa_vow_taken", region.Settlements[target].Name);
            }
            case "abandon":
            {
                if (book.Pilgrim == null) return R(c, HeroResultCode.Invalid, "fa_no_pilgrimage");
                book.Pilgrim = null;
                return R(c, HeroResultCode.Ok, "fa_vow_given_up");
            }
            case "arrive":
            {
                int s = S(1); var pg = book.Pilgrim;
                if (pg == null || pg.Target != s) return R(c, HeroResultCode.Invalid, "fa_no_pilgrimage");
                book.Pilgrim = null; book.Pilgrimages++; book.HeroId = (long)hero.Value; book.ComfortUntilDay = day + book.Settings.ShrinePainDays - 1;
                int piety = Piety.Adjust(book, pt, hero, book.Settings.PilgrimagePiety);
                var place = region.Settlements[s].Id;
                var e = new ChronicleEntry(st.Ids.Allocate(IdKind.Event), new GameTime(c.IssuedSec), "pilgrimage_done", [hero], [new Cause(CauseKind.Faith, pg.Entry == 0 ? null : new EntityId(pg.Entry), NoteKey: "ch_note_pilgrim_vow")],
                    new Dictionary<string, string> { ["settlement"] = region.Settlements[s].Name }, TextKey: "ch_faith_pilgrim_done", Params: [ChronParam.Person("doer", hero), ChronParam.Place("place", place)]);
                st.Chronicle.Add(e);
                FaithStanding.Record(st, ctx.Content, hero, "pilgrimage_done", e.Id, s, 1, 0, true, day);
                // the badge: an item of the shrine's tin, in the pack
                SiteDesk.Make(ctx, new LootItem(book.Settings.BadgeItem, "tin", 8, 30, 1000));
                // a sick pilgrim feels better at once (5 days of it follow, the daily pass): pain lower, no cure; a hurt one is watched for a natural recovery (believed miracles, 10a)
                Miracles.WatchPilgrim(st, ctx.Content, hero, s, e.Id, day);
                if (st.Wounds is { } wl && wl.Books.TryGetValue(hero.Value, out var wb)) wb.Relieve(book.Settings.ShrinePainRelief);
                return new HeroResult(c.Seq, c.Kind, HeroResultCode.Ok, HeroText.Of("fa_pilgrim_arrived", region.Settlements[s].Name, piety), 0, piety, c.IssuedSec);
            }
        }
        return R(c, HeroResultCode.Invalid, "HERO_MAKES_NO_SENSE");
    }
}
