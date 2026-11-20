using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Dialogue;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Rumours;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Society;
using Ravensguard.Sim.Tests.Dialogue;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Tests.Society;

/// <summary>RP-1 to RP-10 (reputation and standing): heard deeds make standing, renown, brands and outlawry, a kin heir inherits half, and the book saves and replays byte for byte.</summary>
public class StandingTests
{
    /// <summary>A map of the hand-made town: settlement 0 and 1 in fief 0 of realm 0, members counted from the person table.</summary>
    private sealed class Map(PersonTable pt, int adults = -1) : IGroupMap
    {
        public GroupRef FiefOf(int s) => s is 0 or 1 ? GroupRef.Fief(0) : GroupRef.Fief(-1);
        public GroupRef RealmOf(int s) => s is 0 or 1 ? GroupRef.Realm(0) : GroupRef.Realm(-1);
        public IReadOnlyList<int> SettlementsOf(GroupRef g) => g.Kind switch { GroupKind.Settlement => [g.Index], GroupKind.Fief or GroupKind.Realm => [0, 1], _ => [] };
        public int Members(GroupRef g)
        {
            int n = 0; foreach (int s in SettlementsOf(g)) for (int i = 0; i < pt.Count; i++) if (pt.HomeSettlement[i] == s && pt.IsAlive(i)) n++;
            return n;
        }
        public int RegionAdults => adults >= 0 ? adults : Members(GroupRef.Realm(0));
        public int HomeOf(EntityId person) { int i = pt.IndexOf(person); return i >= 0 ? pt.HomeSettlement[i] : -1; }
    }

    private static readonly DeedTable Deeds = new(
    [
        new DeedDef { Id = "theft_witnessed", Entry = "theft_witnessed", SettlementPm = -60, FiefPm = -30 },
        new DeedDef { Id = "murder", Entry = "murder", SettlementPm = -300, FiefPm = -150, RealmPm = -50 },
        new DeedDef { Id = "monster_slain", Entry = "monster_slain", SettlementPm = 200, FiefPm = 60 },
        new DeedDef { Id = "alms_given", Entry = "alms_given", SettlementPm = 5, PerKey = "mites", PerUnit = 100, PerMax = 40 },
        new DeedDef { Id = "slave_bought", Entry = "slave_bought", SettlementPm = -40, Match = new() { ["law"] = "none" } },
    ]);

    private static readonly LineBook Book = Rumoured("theft_witnessed", "murder", "monster_slain");

    /// <summary>The shipped talk content with gossip rows for the deed types, so the real seeder and spreader carry them.</summary>
    private static LineBook Rumoured(params string[] types) => new()
    {
        Lines = LineBook.Default.Lines, Barks = LineBook.Default.Barks, Dialogues = LineBook.Default.Dialogues, Settings = LineBook.Default.Settings,
        RumourTypes = [.. LineBook.Default.RumourTypes, .. types.Select(t => new RumourTypeDef { Id = t, Entry = t, Kind = "Crime", Key = "R_RIOT", InterestPm = 800, LifeDays = 10, Witnesses = "town", Severity = 2 })],
    };

    private static EntityId Add(Chronicle c, string type, long day, EntityId doer, params (string K, string V)[] data)
    {
        var id = EntityId.Make(IdKind.Event, (ulong)(c.Count + 1));
        var d = new Dictionary<string, string> { ["settlement"] = "Ashby" }; foreach (var (k, v) in data) d[k] = v;
        c.Add(new ChronicleEntry(id, new GameTime(day * TimeConst.SecondsPerDay + 6 * 3600), type, [doer], [new Cause(CauseKind.Crime, null, "seen")], d));
        return id;
    }

    /// <summary>A rumour of the entry that exactly <paramref name="heard"/> people of the town hold, made by hand (the first ones by index).</summary>
    private static TalkState Hand(TalkWorld w, EntityId entry, int heard, TalkState? ts = null)
    {
        ts ??= new TalkState(); ts.Rumours.EnsureCounts(p => w.People.HomeSettlement[p]);
        if (ts.Rumours.Get(entry) == null) ts.Rumours.Add(new Rumour { Id = entry, Source = entry, Kind = RumourKind.Crime, TypeId = "x", Settlement = 0, Place = "Ashby", ExpiresDay = 9999, LifeDays = 9999, InterestPm = 500 });
        int have = ts.Rumours.HearingsOf(entry).Count;
        for (int i = have; i < heard; i++) ts.Rumours.AddHearing(entry, new Hearing { Listener = i, Teller = -1, Hour = 0, How = HearHow.Witnessed, PlaceAlt = -1 }, 100);
        return ts;
    }

    private static void Tick(StandingBook?[] book, TalkWorld w, TalkState ts, IGroupMap map, long hour, DeedTable? deeds = null)
    {
        StandingSystem.OnHour(() => book[0] ??= new StandingBook(), book[0], deeds ?? Deeds, map, w, ts, hour);
    }

    private static readonly GroupRef Ashby = GroupRef.Settlement(0), FiefZero = GroupRef.Fief(0);

    // ------------------------------------------------------------------ RP-1, RP-2

    [Fact]
    public void RP1_a_theft_seen_by_two_in_a_town_of_1400_is_nothing_at_first_minus_30_at_700_and_never_past_minus_60()
    {
        var (w, c) = TalkKit.Town(1400); var thief = w.People.IdOf(5); var map = new Map(w.People); var b = new StandingBook?[1];
        var e = Add(c, "theft_witnessed", 0, thief);
        var ts = Hand(w, e, 2); Tick(b, w, ts, map, 1);
        Assert.Equal(0, Standing.Of(b[0], thief, Ashby));                       // -60 x 2/1400 = 0 in integers
        Assert.Equal(2, b[0]!.Row(thief, Ashby)!.Value.Heard);
        Hand(w, e, 700, ts); Tick(b, w, ts, map, 2);
        Assert.Equal(-30, Standing.Of(b[0], thief, Ashby));
        Assert.Equal(-15, Standing.Of(b[0], thief, FiefZero));                  // the fief's -30 over its (same) 1400 people
        Hand(w, e, 1400, ts); Tick(b, w, ts, map, 3);
        Assert.Equal(-60, Standing.Of(b[0], thief, Ashby));
        Hand(w, e, 1400, ts); Tick(b, w, ts, map, 4);
        Assert.True(Standing.Of(b[0], thief, Ashby) >= -60);
    }

    [Fact]
    public void RP2_an_unwitnessed_theft_makes_no_record_no_standing_and_no_rumour()
    {
        var (w, c) = TalkKit.Town(300); var thief = w.People.IdOf(5); var b = new StandingBook?[1];
        // nobody saw and nobody tells: the thief lives nowhere in the region and the place is not a settlement, so the seeder finds no one and there is no rumour to count
        w.People.HomeSettlement[5] = -1;
        var lonely = Add(c, "theft_witnessed", 100, thief, ("settlement", "Nowhere"));
        var ts = new TalkState();
        for (long h = 100 * 24 + 7; h < 100 * 24 + 7 + 3; h++) { TalkSystem.AfterHour(w, ts, Rumoured("theft_witnessed"), h); Tick(b, w, ts, new Map(w.People), h); }
        Assert.Null(ts.Rumours.Get(lonely)); Assert.Equal(0, ts.Rumours.Count);
        Assert.Equal(0, Standing.Of(b[0], thief, Ashby));
        // an entry with no deed row is no deed even when everyone heard it
        var e2 = Add(c, "birth", 100, thief); var ts2 = Hand(w, e2, 100); var b2 = new StandingBook?[1]; Tick(b2, w, ts2, new Map(w.People), 5);
        Assert.Null(b2[0]);
    }

    // ------------------------------------------------------------------ RP-3

    [Fact]
    public void RP3_a_murder_everyone_knows_is_minus_300_for_a_year_minus_240_at_three_years_and_never_below_the_floor_of_90()
    {
        var (w, c) = TalkKit.Town(200); var killer = w.People.IdOf(5); var map = new Map(w.People); var b = new StandingBook?[1];
        var e = Add(c, "murder", 0, killer); var ts = Hand(w, e, 200);
        Tick(b, w, ts, map, 0);
        Assert.Equal(-300, Standing.Of(b[0], killer, Ashby));
        foreach (var (day, want) in new[] { (60, -300), (119, -300), (120, -270), (180, -240), (300, -180), (540, -90), (3000, -90) })
        {
            b[0]!.Daily(Deeds, map, day);
            Assert.True(Standing.Of(b[0], killer, Ashby) == want, $"day {day}: {Standing.Of(b[0], killer, Ashby)} != {want}");
        }
    }

    // ------------------------------------------------------------------ RP-4

    [Fact]
    public void RP4_standing_plus_1000_pays_ten_percent_less_for_the_same_bread_and_minus_1000_ten_percent_more()
    {
        Assert.Equal(900, Standing.PriceMites(1000, 1000, buying: true));
        Assert.Equal(1000, Standing.PriceMites(1000, 0, buying: true));
        Assert.Equal(1100, Standing.PriceMites(1000, -1000, buying: true));
        Assert.Equal(1100, Standing.PriceMites(1000, 1000, buying: false));
        Assert.Equal(950, Standing.PriceMites(1000, 500, buying: true));
        Assert.True(Standing.HiringRefused(-301)); Assert.False(Standing.HiringRefused(-300));
        Assert.True(Standing.Watched(-501)); Assert.Equal(1, Standing.MercySteps(300)); Assert.Equal(0, Standing.MercySteps(299));
        Assert.Equal(StandingBand.Hated, Standing.BandOf(-800)); Assert.Equal(StandingBand.Unknown, Standing.BandOf(0)); Assert.Equal(StandingBand.Honoured, Standing.BandOf(900));
    }

    // ------------------------------------------------------------------ RP-5, RP-6

    [Fact]
    public void RP5_a_stranger_meeting_the_branded_hero_starts_at_minus_200_and_the_mark_survives_a_save()
    {
        var hero = EntityId.Make(IdKind.Person, 0); var book = new StandingBook();
        Assert.Equal(0, book.Marks.FirstMeetingTrustPm(hero));
        book.Marks.AddBrand(hero, Brand.Thief);
        Assert.Equal(-200, book.Marks.FirstMeetingTrustPm(hero));
        var party = new HeroParty(); var stranger = EntityId.Make(IdKind.Person, 77);
        Assert.Equal(-200, party.Know(stranger, book.Marks.FirstMeetingTrustPm(hero)).TrustPm);
        Assert.Equal(100, party.Know(EntityId.Make(IdKind.Person, 78), 0).TrustPm);                       // no brand: the ordinary start
        book.Marks.AddBrand(hero, Brand.Murderer);
        Assert.Equal(-400, book.Marks.FirstMeetingTrustPm(hero));
        Assert.True(MarkBook.BrandSeen(3, true)); Assert.False(MarkBook.BrandSeen(4, true)); Assert.False(MarkBook.BrandSeen(2, false));
        var a = SaveCodec.SerializeCompressed(StandingSave.Capture(book));
        var back = SaveCodec.DeserializeCompressed<StandingSave>(a).Restore();
        Assert.True(back.Marks.Has(hero, Brand.Thief | Brand.Murderer));
        Assert.Equal(a, SaveCodec.SerializeCompressed(StandingSave.Capture(back)));
        Assert.Equal(book.Hash(), back.Hash());
    }

    [Fact]
    public void RP6_an_outlaw_is_attacked_on_sight_killing_him_is_no_crime_and_a_pardon_clears_it()
    {
        var (w, c) = TalkKit.Town(100); var man = w.People.IdOf(3); var map = new Map(w.People); var b = new StandingBook?[1];
        var e = Add(c, "monster_slain", 0, man); Tick(b, w, Hand(w, e, 50), map, 1);
        var realm0 = GroupRef.Realm(0);
        Assert.Equal(0, Standing.Of(b[0], man, realm0)); Assert.False(Standing.AttackedOnSight(b[0], man, 0));
        b[0]!.Marks.Outlaw(man, 0);
        Assert.True(Standing.AttackedOnSight(b[0], man, 0)); Assert.False(Standing.AttackedOnSight(b[0], man, 1));
        Assert.True(b[0]!.Marks.KillingIsNoCrime(man, 0));
        Assert.Equal(-1000, Standing.Of(b[0], man, realm0));
        Assert.True(b[0]!.Marks.Pardon(man, 0));
        Assert.False(Standing.AttackedOnSight(b[0], man, 0)); Assert.True(b[0]!.Marks.IsPardoned(man));
        Assert.Equal(0, Standing.Of(b[0], man, realm0)); Assert.False(b[0]!.Marks.Pardon(man, 0));
        Assert.Equal(100, Standing.Of(b[0], man, Ashby));                                                  // 200 x 50/100 people: the town's own row stands through all of it
    }

    // ------------------------------------------------------------------ RP-7

    [Fact]
    public void RP7_a_kin_heir_starts_at_half_of_every_row_and_of_renown_a_companion_at_nothing_and_marks_do_not_pass()
    {
        StandingBook Make(out EntityId hero, out TalkWorld w)
        {
            var (tw, c) = TalkKit.Town(200); w = tw; hero = EntityId.Make(IdKind.Person, 0); var map = new Map(tw.People); var b = new StandingBook?[1];
            var e = Add(c, "murder", 0, hero); var e2 = Add(c, "monster_slain", 0, hero);
            var ts = Hand(tw, e, 200); Hand(tw, e2, 200, ts); Tick(b, tw, ts, map, 1);
            b[0]!.Marks.AddBrand(hero, Brand.Murderer); b[0]!.Marks.Outlaw(hero, 0);
            return b[0]!;
        }
        var kin = Make(out var h1, out var w1); var map1 = new Map(w1.People);
        int before = kin.Value(h1, Ashby), renown = kin.RenownOf(h1);
        Assert.Equal(-100, before); Assert.True(renown > 0);                                              // -300 and +200 at once
        string carried = kin.Inherit(h1, EntityId.Make(IdKind.Person, 901), kin: true);
        Assert.Equal(before / 2, kin.Value(h1, Ashby)); Assert.Equal(renown / 2, kin.RenownOf(h1)); Assert.Contains("S0", carried);
        Assert.Null(kin.Marks.Of(h1));
        kin.Daily(Deeds, map1, 10);                                                                         // the day's recompute keeps the half: the deeds now carry half the weight
        Assert.Equal(before / 2, kin.Value(h1, Ashby));
        var comp = Make(out var h2, out _);
        comp.Inherit(h2, EntityId.Make(IdKind.Person, 902), kin: false);
        Assert.Equal(0, comp.Value(h2, Ashby)); Assert.Equal(0, comp.RenownOf(h2)); Assert.Null(comp.Marks.Of(h2));
        comp.Daily(Deeds, map1, 10); Assert.Equal(0, comp.Value(h2, Ashby));
    }

    // ------------------------------------------------------------------ RP-8

    [Fact]
    public void RP8_a_monster_slain_and_heard_by_60_percent_of_the_adults_is_renown_600_and_wears_5_percent_a_season_after_a_year()
    {
        var (w, c) = TalkKit.Town(1000); var hero = w.People.IdOf(1); var map = new Map(w.People, adults: 1000); var b = new StandingBook?[1];
        var e = Add(c, "monster_slain", 0, hero); var ts = Hand(w, e, 600); Tick(b, w, ts, map, 1);
        Assert.Equal(600, b[0]!.RenownOf(hero));
        foreach (int day in new[] { 15, 30, 45, 60 }) { b[0]!.Daily(Deeds, map, day); Assert.Equal(600, b[0]!.RenownOf(hero)); }     // nothing in the first year
        b[0]!.Daily(Deeds, map, 75); Assert.Equal(570, b[0]!.RenownOf(hero));
        b[0]!.Daily(Deeds, map, 90); Assert.Equal(541, b[0]!.RenownOf(hero));
        // a new deed makes the name fresh again
        var e2 = Add(c, "monster_slain", 91, hero); Hand(w, e2, 600, ts); Tick(b, w, ts, map, 91 * 24 + 1);
        Assert.Equal(600, b[0]!.RenownOf(hero));
    }

    // ------------------------------------------------------------------ RP-9

    [Fact]
    public void RP9_a_household_that_moves_takes_its_hearings_with_it()
    {
        var (w, c) = TalkKit.Town(100, settlements: 2); var man = w.People.IdOf(0); var map = new Map(w.People); var b = new StandingBook?[1];
        var e = Add(c, "monster_slain", 0, man); var ts = Hand(w, e, 5); Tick(b, w, ts, map, 1);
        var rec = b[0]!.Deed(e)!;
        Assert.Equal(5, rec.Heard[0]); Assert.False(rec.Heard.ContainsKey(1));
        // person 2 (who heard it) moves with its household to settlement 1
        w.People.HomeSettlement[2] = 1; StandingSystem.Moved(ts, 2, 0, 1); Tick(b, w, ts, map, 2);
        Assert.Equal(4, rec.Heard[0]); Assert.Equal(1, rec.Heard[1]);
        // a person who heard nothing moving changes nothing
        w.People.HomeSettlement[50] = 1; StandingSystem.Moved(ts, 50, 0, 1); Tick(b, w, ts, map, 3);
        Assert.Equal(4, rec.Heard[0]); Assert.Equal(1, rec.Heard[1]);
        // the counts are the same when rebuilt from the stored hearings
        var fresh = new RumourTable(); fresh.Restore(ts.Rumours.Rumours.Values, ts.Rumours.HearingsOf(e).Select(h => (e, h)));
        fresh.EnsureCounts(p => w.People.HomeSettlement[p]);
        Assert.Equal(4, fresh.HeardBySettlement(e)[0]); Assert.Equal(1, fresh.HeardBySettlement(e)[1]);
    }

    // ------------------------------------------------------------------ RP-10

    private static (ulong Hash, byte[] Save, int Rows) Soak(int days, int stopAndReloadAt = -1)
    {
        var (w, c) = TalkKit.Town(300, seed: 11); var map = new Map(w.People); var ts = new TalkState(); var hero = w.People.IdOf(7);
        StandingBook? book = null; var ser = Book;
        ts.AfterSpread = (tw, h) => StandingSystem.OnHour(() => book ??= new StandingBook(), book, Deeds, map, tw, ts, h);
        for (long h = 100 * 24; h < (100 + days) * 24L; h++)
        {
            int day = (int)(h / 24);
            if (h % 24 == 7 && (day - 100) % 4 == 0) Add(c, (day - 100) % 8 == 0 ? "theft_witnessed" : "monster_slain", day, hero);     // the bot's doing
            if (h % 24 == 9 && (day - 100) == 11) Add(c, "murder", day, w.People.IdOf(20));
            TalkSystem.AfterHour(w, ts, ser, h);
            if (stopAndReloadAt >= 0 && h == (100 + stopAndReloadAt) * 24L + 12 && book != null)
            {
                var snap = SaveCodec.SerializeCompressed(StandingSave.Capture(book)); book = SaveCodec.DeserializeCompressed<StandingSave>(snap).Restore();    // save -> load in the middle of the run
            }
        }
        return (book?.Hash() ?? 0, book == null ? [] : SaveCodec.SerializeCompressed(StandingSave.Capture(book)), book?.RowCount ?? 0);
    }

    [Fact]
    public void RP10_thirty_days_of_gossip_give_the_same_standing_hash_twice_and_after_save_load_save()
    {
        var a = Soak(30); var b = Soak(30);
        Assert.NotEqual(0UL, a.Hash); Assert.True(a.Rows > 0);
        Assert.Equal(a.Hash, b.Hash); Assert.Equal(a.Save, b.Save);
        var mid = Soak(30, stopAndReloadAt: 14);                                                            // a save and a load part-way through changes nothing
        Assert.Equal(a.Hash, mid.Hash); Assert.Equal(a.Save, mid.Save);
        var back = SaveCodec.DeserializeCompressed<StandingSave>(a.Save).Restore();
        Assert.Equal(a.Hash, back.Hash());
        Assert.Equal(a.Save, SaveCodec.SerializeCompressed(StandingSave.Capture(back)));
    }

    // ------------------------------------------------------------------ the content and the section

    [Fact]
    public void The_shipped_deed_rows_load_through_the_mod_loader_and_match_by_data()
    {
        var db = TalkKit.LoadBase(); var t = DeedTable.From(db);
        Assert.True(t.Count >= 20);
        Assert.Equal(-300, t.Get("murder")!.SettlementPm); Assert.Equal(-60, t.Get("theft_witnessed")!.SettlementPm);
        var doer = EntityId.Make(IdKind.Person, 4); var none = new Dictionary<string, string> { ["law"] = "none" }; var chattel = new Dictionary<string, string> { ["law"] = "chattel" };
        ChronicleEntry E(string type, Dictionary<string, string> d) => new(EntityId.Make(IdKind.Event, 1), new GameTime(0), type, [doer], [], d);
        Assert.Equal("slave_bought", t.RowFor(E("slave_bought", none))!.Id); Assert.Null(t.RowFor(E("slave_bought", chattel)));
        Assert.Null(t.RowFor(E("birth", [])));
        Assert.Equal(doer, DeedTable.DoerOf(E("murder", [])));
        Assert.Null(DeedTable.DoerOf(new ChronicleEntry(EntityId.Make(IdKind.Event, 2), new GameTime(0), "murder", [], [], new Dictionary<string, string>())));
        // alms scale by the mites given, capped
        var alms = t.Get("alms_given")!;
        Assert.Equal(300, DeedTable.AmountOf(alms, E("alms_given", new() { ["mites"] = "300" })));
        Assert.Equal(4000, DeedTable.AmountOf(alms, E("alms_given", new() { ["mites"] = "90000" })));
    }

    [Fact]
    public void Alms_move_standing_by_the_hundred_mites_and_an_empty_book_has_no_section()
    {
        var (w, c) = TalkKit.Town(100); var giver = w.People.IdOf(2); var map = new Map(w.People); var b = new StandingBook?[1];
        var e = Add(c, "alms_given", 0, giver, ("mites", "2000")); Tick(b, w, Hand(w, e, 100), map, 1);
        Assert.Equal(100, Standing.Of(b[0], giver, Ashby));                                                  // +5 per 100 mites x 20, everyone knows
        Assert.True(new StandingBook().IsEmpty); Assert.False(b[0]!.IsEmpty);
        Assert.Equal(0, Standing.Of(null, giver, Ashby));
    }

    [Fact]
    public void Why_lists_the_deeds_behind_a_standing_largest_first()
    {
        var (w, c) = TalkKit.Town(100); var man = w.People.IdOf(2); var map = new Map(w.People); var b = new StandingBook?[1];
        var t = Add(c, "theft_witnessed", 0, man); var m = Add(c, "murder", 0, man); var a = Add(c, "monster_slain", 0, man);
        var ts = Hand(w, t, 100); Hand(w, m, 100, ts); Hand(w, a, 100, ts); Tick(b, w, ts, map, 1);
        var why = b[0]!.Why(man, Ashby, Deeds, map, 1);
        Assert.Equal(["murder", "monster_slain", "theft_witnessed"], why.Select(x => x.DeedId).ToArray());
        Assert.Equal([m, a, t], why.Select(x => x.Entry).ToArray());
        Assert.Equal(-160, Standing.Of(b[0], man, Ashby));
    }
}
