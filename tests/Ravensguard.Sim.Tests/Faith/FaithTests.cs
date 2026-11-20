using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Behaviour;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Faith;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Society;
using Ravensguard.Sim.Succession;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.Worlds;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Faith;

/// <summary>
/// Faith and church, the core (P6-B4, docs/specs/phase6/faith-and-church.md 7): FC-1 the religions of a seed and the offices of a new game or an older save, FC-2 a holy day, FC-3 the tithe split,
/// FC-4 a vacant see, FC-5 a pilgrimage, FC-10 services refused, FC-12 the slavery doctrine, FC-13 usury, and the section's round trip.
/// </summary>
public class FaithTests(ITestOutputHelper output)
{
    private static int DayOf(int year, int doy) => year * TimeConst.DaysPerYear + doy - 1;

    // ---------------------------------------------------------------- FC-1

    [Fact]
    public void FC1_OneReligionPerBaseCulture_WithFoundersSaintsAndAtMostTwelveHolyDays_DerivedTheSameWhenPureAndOnALoadedOlderSave()
    {
        using var r = new FaithRig(runtime: false);
        var rel = r.Religions;
        Assert.Equal(4, rel.Count);
        var cultures = r.Content.All<CultureDef>().ToDictionary(c => c.Id);
        foreach (var x in rel.All)
        {
            int founders = x.Saints.Count(s => s.Founder);
            Assert.InRange(founders, 3, 5);
            int history = r.W.History.Notables.Count(n => n.Role == Ravensguard.Sim.WorldGen.History.NotableRole.Saint && rel.OfCulture(n.Culture) == x.Index);
            Assert.Equal(history, x.Saints.Count(s => !s.Founder));                     // the history's saints of its cultures are all there
            Assert.InRange(x.HolyDays.Count, 1, 12);
            Assert.Equal(x.HolyDays.Count, x.HolyDays.Select(d => d.DayOfYear).Distinct().Count());
            Assert.All(x.HolyDays, d => Assert.InRange(d.DayOfYear, 1, 60));
            foreach (string cid in x.CultureIds) foreach (var f in cultures[cid].FeastDays.Take(Math.Max(0, 12))) Assert.Contains(x.HolyDays, d => d.Kind == "feast" && d.Id == f.Id);
            Assert.Equal(x.Id, x.Doctrine.Religion);                                    // the doctrines of its row
            Assert.NotEmpty(x.Template.DeityTerms);
            output.WriteLine($"{x.Id}: {x.Saints.Count} saints ({founders} founders), {x.HolyDays.Count} holy days, site {x.HolySiteHistory.Value}");
        }
        // pure: the same seed, content and history derive the same religions, with nothing but the seed to key them
        var again = ReligionBook.Derive(1234, r.Content, r.W.History);
        for (int i = 0; i < rel.Count; i++)
        {
            Assert.Equal(rel.All[i].HolyDays, again.All[i].HolyDays);
            Assert.Equal(rel.All[i].Saints, again.All[i].Saints);
        }
        Assert.NotEqual(ReligionBook.Derive(99, r.Content, r.W.History).All[0].Saints.Where(s => s.Founder).Select(s => s.Full), rel.All[0].Saints.Where(s => s.Founder).Select(s => s.Full));

        // a new game has its offices; a save from before Phase 6 loaded today derives the same church and says so
        Assert.NotEmpty(r.Church.Parishes); Assert.NotEmpty(r.Church.Sees);
        var file = WorldSave.Capture(r.State, "h", "0.1");
        Assert.Contains(FaithSave.SectionName, file.Sections.Keys);
        var older = new SaveFile(file.Header, file.Sections.Where(kv => kv.Key != FaithSave.SectionName).Select(kv => new KeyValuePair<string, byte[]>(kv.Key, kv.Value.ToArray())));
        var loaded = WorldSave.Restore(SaveCodec.Decode(SaveCodec.Encode(older)), r.Content, "h", null, r.W.Continent, r.W.History);
        loaded.Content = r.Content;
        Assert.Null(loaded.Church); Assert.True(loaded.ChurchSeedPending);                                  // read back, it is marked and its bytes are left alone ...
        Assert.Equal(file.Sections.Keys.Where(k => k != FaithSave.SectionName), WorldSave.Capture(loaded, "h", "0.1").Sections.Keys.Where(k => k != FaithSave.SectionName));
        using (new WorldRuntime(loaded, r.Content, new MemoryRegionPager()))                               // ... and the church is derived when the game runs
        {
            Assert.NotNull(loaded.Church);
            Assert.Equal(r.Church.Hash(), loaded.Church!.Hash());
            Assert.Contains(Phase6Seeding.LoadNote, loaded.LoadNotes);
        }
        Assert.Equal(rel.All.Select(x => x.Id), FaithWiring.ReligionsOf(loaded).All.Select(x => x.Id));
        // and a church that has been saved is not derived again
        var again2 = WorldSave.Restore(SaveCodec.Decode(SaveCodec.Encode(file)), r.Content, "h", null, r.W.Continent, r.W.History);
        Assert.Equal(r.Church.Hash(), again2.Church!.Hash());
    }

    [Fact]
    public void EveryParishHasItsPriestAndEverySeeItsBishop_RealPeople()
    {
        using var r = new FaithRig(runtime: false);
        var pt = r.People;
        foreach (var p in r.Church.Parishes.Values)
        {
            Assert.True(FaithEngine.IsLivingPriest(pt, p.Priest, out int row), $"parish {p.Settlement} has no living priest");
            Assert.Equal(p.Settlement, pt.HomeSettlement[row]);
        }
        foreach (var s in r.Church.Sees.Values)
        {
            Assert.True(FaithEngine.IsLivingPriest(pt, s.Bishop, out int row));
            Assert.Equal(s.Seat, pt.HomeSettlement[row]);                                // the bishop is the priest of the seat
            Assert.Equal(s.Bishop, r.Church.Parishes[s.Seat].Priest);
        }
    }

    // ---------------------------------------------------------------- FC-2

    [Fact]
    public void FC2_OnAHolyDayNobodyWorksTheFields_FoodDemandRises_AndMostAdultsAttend_TheHeroOnceForOnePiety()
    {
        using var r = new FaithRig();
        var (hs, ri) = r.HeroFaith(); var rel = r.Religions.Get(ri)!;
        var feast = rel.HolyDays.First(d => d.Kind == "feast");
        int year = r.State.Clock.Now.Year;                                                 // people are born before today: their ages are real only in the world's own years
        int day = DayOf(year, feast.DayOfYear), plain = DayOf(year, Enumerable.Range(1, 60).First(d => !rel.IsHoly(d) && (d - 1) % 5 != 4 && (d - 1) % 5 != 2));
        // the services: at least 70% of the adults of every parish that keeps the day
        var reports = FaithEngine.Services(r.Sim, r.Church, day);
        Assert.NotEmpty(reports);
        long adults = reports.Sum(x => (long)x.Adults), came = reports.Sum(x => (long)x.Attended);
        output.WriteLine($"{feast.Name}: {came} of {adults} adults attended in {reports.Count} parishes");
        Assert.True(came * 100 >= adults * 70, $"{came}/{adults}");
        Assert.All(reports, x => Assert.True(x.Attended * 100 >= x.Adults * 70 || x.Adults == 0));
        Assert.Empty(FaithEngine.Services(r.Sim, r.Church, plain));                       // not on an ordinary day

        // no field or workshop hours: a farmer's day has no Work on the feast (the culture's), and has on a plain day
        var book = new ScheduleBook(BehaviourContent.From(r.Content)) { FeastMasks = HolyCalendar.FeastMasks(r.Content) };
        var pt = r.People; int farmer = -1;
        for (int i = 0; i < pt.Count && farmer < 0; i++) if (pt.IsAlive(i) && pt.Occupation[i] == Occupation.Farmer && pt.HomeSettlement[i] >= 0 && pt.AgeYears(i, day) >= 18) farmer = i;
        Assert.True(farmer >= 0);
        var a = PersonAnchors.From(r.State.Population, farmer, 1234, 0, day * 86400L, null, null, book);
        Assert.True(a.HasWork); Assert.NotEqual(0UL, a.HolyMask);
        Assert.False(WorksOn(a, day), "the farmer works the feast");
        Assert.True(WorksOn(a, plain), "the farmer does not work an ordinary day");

        // household food demand +50%: the extra relish and ale of the feast are eaten out of the larder
        var e = r.State.Economy!; var hh = r.Sim.Households; int h = -1;
        for (int i = 0; i < hh.Count && h < 0; i++) if (hh.MemberCount[i] > 0 && hh.Class[i] == SocialClass.Peasant) h = i;
        var store = e.HouseholdStore[h];
        long Eaten(int d)
        {
            foreach (var g in e.Food.Staples) store.Create(g, 60_000, Ravensguard.Sim.Goods.CreateReason.Seed);
            foreach (var g in e.Food.Relishes) store.Create(g, 50_000, Ravensguard.Sim.Goods.CreateReason.Seed);
            store.Create(e.Food.Ale, 80_000, Ravensguard.Sim.Goods.CreateReason.Seed);
            long before = e.Food.Relishes.Sum(g => store.Qty(g)) + store.Qty(e.Food.Ale);
            HouseholdNeeds.RunDay(e, r.State.Population, h, d, 12);
            return before - (e.Food.Relishes.Sum(g => store.Qty(g)) + store.Qty(e.Food.Ale));
        }
        r.Sim.Economy.FestivePct = null; long ordinary = Eaten(plain);
        r.Sim.Economy.FestivePct = (_, d) => d == day ? 50 : 0; long feasting = Eaten(day);
        output.WriteLine($"relish and ale eaten: {ordinary} g, on the feast {feasting} g");
        Assert.True(feasting >= ordinary * 140 / 100 && feasting <= ordinary * 160 / 100, $"{feasting} vs {ordinary}");
        // and the church wired the real thing: its hook says 50 on a holy day of the household's faith, 0 on a plain day
        FaithWiring.Attach(r.State.HomeRecords!, r.State, r.Content);
        int hhOfFaith = Enumerable.Range(0, hh.Count).First(i => hh.MemberCount[i] > 0 && r.Religions.OfSettlement(r.Region, hh.Settlement[i]) == ri);
        Assert.Equal(50, e.FestivePct!(hhOfFaith, day)); Assert.Equal(0, e.FestivePct!(hhOfFaith, plain));

        // the hero's attendance: +1 piety, once a holy day
        int parish = r.Church.Parishes.Keys.First(s => r.Religions.OfSettlement(r.Region, s) == ri);
        long sec = day * 86400L + 9 * 3600; int before0 = Piety.Of(r.Church, pt, r.Hero);
        var res = r.Do(FaithDesk.Attend(parish, sec), sec);
        Assert.True(res.Ok, res.Text);
        Assert.Equal(Math.Min(100, before0 + 1), Piety.Of(r.Church, pt, r.Hero));
        Assert.Equal(HeroResultCode.Refused, r.Do(FaithDesk.Attend(parish, sec + 60), sec + 60).Code);
        Assert.Equal(Math.Min(100, before0 + 1), Piety.Of(r.Church, pt, r.Hero));
        Assert.Equal(HeroResultCode.Refused, r.Do(FaithDesk.Attend(parish, plain * 86400L + 9 * 3600), plain * 86400L + 9 * 3600).Code);   // not on an ordinary day
    }

    private static bool WorksOn(PersonAnchors a, long day)
    {
        Span<DaySeg> buf = stackalloc DaySeg[Schedule.MaxSegs];
        int n = Schedule.Segments(a, day, buf);
        for (int i = 0; i < n; i++) if (buf[i].Kind == JobKind.Work) return true;
        return false;
    }

    // ---------------------------------------------------------------- FC-3

    [Fact]
    public void FC3_ThreeThousandMitesOfTitheIsAThirdTheParishAThirdTheSeeAThirdTheFabric_AndTheCoinIsConserved()
    {
        Assert.Equal((1000L, 1000L, 1000L), FaithEngine.SplitTithe(3000, 1, 1, 1));
        Assert.Equal((1200L, 600L, 1200L), FaithEngine.SplitTithe(3000, 2, 1, 2));
        Assert.Equal(3001, FaithEngine.SplitTithe(3001, 1, 1, 1).Parish + FaithEngine.SplitTithe(3001, 1, 1, 1).See + FaithEngine.SplitTithe(3001, 1, 1, 1).Fabric);   // nothing is lost to the rounding

        using var r = new FaithRig();
        var ew = r.Econ; var led = r.State.Economy!.Ledger; var t = ew.Taxes.Treasuries; var church = r.Church;
        var parish = church.Parishes.Values.First(p => t.ParishSlot[p.Settlement] >= 0 && church.SeeOf(r.Religions.Get(r.Religions.OfSettlement(r.Region, p.Settlement))!.Id, ChurchSeeding.RealmOf(r.Region, p.Settlement)) is { Bishop: >= 0 });
        int ri = r.Religions.OfSettlement(r.Region, parish.Settlement); var rel = r.Religions.Get(ri)!;
        var see = church.SeeOf(rel.Id, ChurchSeeding.RealmOf(r.Region, parish.Settlement))!;
        var chest = led.Open(PurseKind.Chest, r.Region.Settlements[parish.Settlement].Id);
        // 3,000 mites of tithe reach the parish chest this season (from a rich household, so the coin is real)
        var from = Enumerable.Range(0, r.State.Economy!.HouseholdCount).Select(i => r.State.Economy.HouseholdPurse[i]).First(p => led.Balance(p).Mites > 10_000);
        Assert.True(led.Transfer(from, chest, new Coin(3000), TransferReason.Tithe));
        var now = GameTime.FromGameHours(r.Sim.State.HourDone + 1);
        t.RollSeason(100_000, now);                                                          // the season closes: its receipts are last season's
        Assert.True(t.IncomeOf(t.ParishSlot[parish.Settlement], TransferReason.Tithe) >= 3000);
        long received = t.IncomeOf(t.ParishSlot[parish.Settlement], TransferReason.Tithe);
        var seePurse = led.Open(PurseKind.Treasury, FaithEngine.SeePurseOwner(see.Index));
        long coin0 = led.Audit().InPurses.Mites, chest0 = led.Balance(chest).Mites, see0 = led.Balance(seePurse).Mites, fabric0 = parish.FabricMites + parish.PoorMites;
        Assert.Equal(received, Enumerable.Range(0, t.Count).Where(s2 => t[s2].Kind == Ravensguard.Sim.Taxes.TreasuryKind.Parish).Sum(s2 => t.IncomeOf(s2, TransferReason.Tithe)));   // no other parish had any
        var d = rel.Doctrine; var (keep, toSee, fabric) = FaithEngine.SplitTithe(received, d.TitheParishParts, d.TitheSeeParts, d.TitheFabricParts);
        long moved = FaithEngine.Season(r.Sim, ew, church, 100_000, now, null);
        Assert.True(moved >= toSee);
        long stipend = toSee * church.Settings.BishopStipendPct / 100;                       // the bishop draws his living from what the see received
        Assert.Equal(see0 + toSee - stipend, led.Balance(seePurse).Mites);
        Assert.Equal(chest0 - toSee, led.Balance(chest).Mites);                              // the parish keeps its third and the fabric's stays in the chest, earmarked
        Assert.Equal(fabric, parish.FabricMites + parish.PoorMites - fabric0);
        Assert.Equal(coin0, led.Audit().InPurses.Mites);                                     // conserved
        Assert.True(led.Audit().IsConserved);
        output.WriteLine($"{rel.Id}: {received} received: parish {keep}, see {toSee}, fabric and poor {fabric}");
    }

    // ---------------------------------------------------------------- FC-4

    [Fact]
    public void FC4_WhenTheBishopDies_TheRulerTakesTheSeesReceiptsUntilTheChapterElects_WithChronicleEntriesAndCauses()
    {
        using var r = new FaithRig();
        var church = r.Church; var pt = r.People; var led = r.State.Economy!.Ledger; var ew = r.Econ;
        var see = church.Sees.Values.OrderByDescending(s => church.Parishes.Count(p => ChurchSeeding.RealmOf(r.Region, p.Key) == s.Realm)).First();
        var seePurse = led.Open(PurseKind.Treasury, FaithEngine.SeePurseOwner(see.Index));
        var from = Enumerable.Range(0, r.State.Economy!.HouseholdCount).Select(i => r.State.Economy.HouseholdPurse[i]).First(p => led.Balance(p).Mites > 10_000);
        Assert.True(led.Transfer(from, seePurse, new Coin(5000), TransferReason.Tithe));
        var ruler = FaithEngine.RulerPurse(ew, see.Seat); Assert.False(ruler.IsNone);
        long ruler0 = led.Balance(ruler).Mites, coin0 = led.Audit().InPurses.Mites;
        // the bishop dies
        var dead = new EntityId((ulong)see.Bishop); int row = pt.IndexOf(dead);
        pt.Status[row] = PersonStatus.Dead; pt.DeathDay[row] = 700;
        var ob = new RegionOutbox(r.State.Ids); int day = 701; var now = new GameTime(day * 86400L);
        FaithEngine.Daily(r.Sim, church, day, now, ob);
        Assert.True(see.Bishop < 0); Assert.Equal(day, see.VacantSince);
        Assert.InRange(see.ElectionDay - day, 15, 30);                                    // 1 to 2 seasons
        Assert.Equal(ruler0 + 5000, led.Balance(ruler).Mites); Assert.Equal(0, led.Balance(seePurse).Mites);
        Assert.Equal(5000, see.TakenMites);
        var vacant = ob.Entries.Single(e => e.TypeId == "see_vacant");
        Assert.Equal("ch_faith_see_vacant", vacant.TextKey); Assert.Equal(CauseKind.Faith, vacant.Causes[0].Kind); Assert.Equal("ch_note_see_bishop_died", vacant.Causes[0].NoteKey);
        var seized = ob.Entries.Single(e => e.TypeId == "see_revenue_seized");
        Assert.Equal(vacant.Id, seized.Causes[0].EventId); Assert.Contains(seized.Params!, p => p.Name == "amount" && p.Value == 5000);
        // while it is vacant, a parish's tithe for the see goes to the ruler too
        var parish = church.Parishes.Values.First(p => ChurchSeeding.RealmOf(r.Region, p.Settlement) == see.Realm && r.Econ.Taxes.Treasuries.ParishSlot[p.Settlement] >= 0 && p.Priest >= 0 && p.Priest != see.Bishop);
        var chest = led.Open(PurseKind.Chest, r.Region.Settlements[parish.Settlement].Id);
        Assert.True(led.Transfer(from, chest, new Coin(3000), TransferReason.Tithe));
        ew.Taxes.Treasuries.RollSeason(200_000, now);
        long r1 = led.Balance(ruler).Mites;
        long moved = FaithEngine.Season(r.Sim, ew, church, day, now, null);
        Assert.True(moved > 0); Assert.True(led.Balance(ruler).Mites > r1); Assert.Equal(0, led.Balance(seePurse).Mites);
        // the chapter elects the most pious senior priest of the faith and realm
        int election = see.ElectionDay; var ob2 = new RegionOutbox(r.State.Ids);
        FaithEngine.Daily(r.Sim, church, election - 1, new GameTime((election - 1) * 86400L), ob2);
        Assert.True(see.Bishop < 0);                                                          // not before its day
        FaithEngine.Daily(r.Sim, church, election, new GameTime(election * 86400L), ob2);
        Assert.True(see.Bishop >= 0); Assert.Equal(-1, see.VacantSince);
        Assert.True(FaithEngine.IsLivingPriest(pt, see.Bishop, out int nrow));
        var rel = r.Religions.All.First(x => x.Id == see.Religion);
        int best = church.Parishes.Values.Where(p => p.Priest >= 0 && r.Religions.Get(r.Religions.OfSettlement(r.Region, p.Settlement))!.Id == see.Religion && ChurchSeeding.RealmOf(r.Region, p.Settlement) == see.Realm
            && FaithEngine.IsLivingPriest(pt, p.Priest, out _)).Max(p => Piety.Of(church, pt, new EntityId((ulong)p.Priest)));
        Assert.Equal(best, Piety.Of(church, pt, pt.IdOf(nrow)));
        var elected = ob2.Entries.Single(e => e.TypeId == "bishop_elected");
        Assert.Equal(CauseKind.Faith, elected.Causes[0].Kind); Assert.Equal("ch_note_chapter_election", elected.Causes[0].NoteKey);
        Assert.True(led.Audit().IsConserved); Assert.Equal(coin0, led.Audit().InPurses.Mites);
        output.WriteLine($"{rel.Id}: vacant {day}, elected {election}, taken {see.TakenMites}");
    }

    // ---------------------------------------------------------------- FC-5

    [Fact]
    public void FC5_APilgrimWhoWalksToTheHolySite_GainsPietyStandingABadge_AndTheSickEaseButAreNotCured()
    {
        using var r = new FaithRig();
        var (hs, ri) = r.HeroFaith(); var church = r.Church; var pt = r.People; var hero = r.Hero;
        var shrines = r.Religions.ShrinesIn(r.Region, ri); Assert.NotEmpty(shrines);
        int site = r.Religions.HolySiteIn(r.Region, ri); Assert.Equal(shrines[0], site);
        long sec = DayOf(5, 20) * 86400L + 8 * 3600;
        // sick: some pain from a wound the hero bears
        var plan = Ravensguard.Sim.Medicine.MedicineWiring.PlanOf(r.Content, "human")!;
        var wl = r.State.Wounds ??= new WoundLedger(r.State.Seed, sec); var book = wl.For(hero.Value, plan, sec); book.PainX100 = 5000;
        int piety0 = Piety.Of(church, pt, hero);
        Assert.Equal(0, Standing.Of(r.State.Standing, hero, GroupRef.Religion(ri)));
        // a vow to a place that is no shrine is refused; to the holy site it is taken
        int notShrine = Enumerable.Range(0, r.Region.Settlements.Count).First(s => !shrines.Contains(s));
        Assert.Equal(HeroResultCode.Refused, r.Do(FaithDesk.Vow(hs, notShrine, sec), sec).Code);
        Assert.True(r.Do(FaithDesk.Vow(hs, site, sec), sec).Ok);
        Assert.NotNull(church.Pilgrim); Assert.Equal(HeroResultCode.Refused, r.Do(FaithDesk.Vow(hs, site, sec + 1), sec + 1).Code);
        Assert.Equal(HeroResultCode.Invalid, r.Do(FaithDesk.Arrive(notShrine, sec + 2), sec + 2).Code);       // arriving elsewhere is no arrival
        long arrive = sec + 3 * 86400;
        var res = r.Do(FaithDesk.Arrive(site, arrive), arrive);
        Assert.True(res.Ok, res.Text);
        Assert.Null(church.Pilgrim); Assert.Equal(1, church.Pilgrimages);
        Assert.Equal(Math.Min(100, piety0 + 10), Piety.Of(church, pt, hero));                                 // +10
        Assert.Equal(80, Standing.Of(r.State.Standing, hero, GroupRef.Religion(ri)));                         // +80 with the faith
        var items = r.State.Economy!.Items;
        Assert.Contains(items.LiveRows(), i => items.At(i).Shape == "pilgrim_badge" && items.At(i).Owner == hero);   // the badge
        var chronicle = r.State.Chronicle.All.Where(e => e.TypeId is "pilgrimage_started" or "pilgrimage_done").ToList();
        Assert.Equal(2, chronicle.Count);
        Assert.Equal(chronicle[0].Id, chronicle[1].Causes[0].EventId);                                        // the arrival names the vow as its cause
        // the sick feel better and nothing more: the pain is 10 lower, the wounds as they were
        Assert.Equal(4000, book.PainX100); Assert.Empty(book.Records);
        FaithEngine.Daily(r.Sim, church, (int)(arrive / 86400) + 1, new GameTime(arrive + 86400), null);
        Assert.Equal(3000, book.PainX100);                                                                    // five days of it, no cure
        FaithEngine.Daily(r.Sim, church, (int)(arrive / 86400) + 7, new GameTime(arrive + 7 * 86400), null);
        Assert.Equal(3000, book.PainX100);                                                                    // the comfort is over
        Assert.True(r.State.Economy!.Ledger.Audit().IsConserved);
    }

    // ---------------------------------------------------------------- FC-10

    [Fact]
    public void FC10_BelowMinusFiveHundredTheChurchRefusesAWeddingABurialAndAWillsWitness_AboveItDoesNot()
    {
        using var r = new FaithRig();
        var (hs, ri) = r.HeroFaith(); var hero = r.Hero;
        var book = r.State.Standing ??= new StandingBook();
        var key = (hero.Value, GroupRef.Religion(ri).Packed);
        book.Rows[key] = new StandingRow { ValuePm = -600, Heard = 100, LastDay = 1 };
        foreach (var s in new[] { ChurchServices.Service.Wedding, ChurchServices.Service.Burial, ChurchServices.Service.WillWitness, ChurchServices.Service.Absolution, ChurchServices.Service.Clergy })
            Assert.False(ChurchServices.Allows(book, hero, ri, s), s.ToString());
        // the priest of the hero's parish will not witness his will
        int pr = ChurchSeeding.PriestRow(r.State.Population, hs); Assert.True(pr >= 0);
        long sec = 40 * 86400L + 9 * 3600;
        var refused = r.Do(WillDesk.Make(r.People.IdOf(pr), [], sec), sec);
        Assert.Equal(HeroResultCode.Refused, refused.Code); Assert.Equal("fa_services_refused", refused.Message.Key);
        // no lower than the floor: a wedding and a burial and a witness; benefit of clergy wants 0 or more
        book.Rows[key] = new StandingRow { ValuePm = -400, Heard = 100, LastDay = 1 };
        Assert.True(ChurchServices.Allows(book, hero, ri, ChurchServices.Service.Wedding)); Assert.True(ChurchServices.Allows(book, hero, ri, ChurchServices.Service.Burial));
        Assert.False(ChurchServices.Allows(book, hero, ri, ChurchServices.Service.Clergy));
        book.Rows[key] = new StandingRow { ValuePm = 0, Heard = 0, LastDay = 1 };
        Assert.True(ChurchServices.Allows(book, hero, ri, ChurchServices.Service.Clergy));
        Assert.NotEqual("fa_services_refused", r.Do(WillDesk.Make(r.People.IdOf(pr), [], sec + 60), sec + 60).Message.Key);      // the church no longer stands in the way
        // an excommunicated man is refused whatever his standing
        book.Marks.Excommunicate(hero, ri);
        Assert.False(ChurchServices.Allows(book, hero, ri, ChurchServices.Service.Wedding));
        Assert.True(ChurchServices.Allows(null, hero, -1, ChurchServices.Service.Wedding));                  // no faith, no refusal
    }

    // ---------------------------------------------------------------- FC-12

    [Fact]
    public void FC12_OwningASlaveAgainstTheDoctrineCostsPietyAndStanding_OnlyWhereTheFaithForbidsIt()
    {
        using var r = new FaithRig();
        var rel = r.Religions; var church = r.Church; var pt = r.People; var hero = r.Hero;
        int hill = rel.All.First(x => x.Id == "hill_saints").Index, north = rel.All.First(x => x.Id == "old_gods").Index, vale = rel.All.First(x => x.Id == "church_of_the_vale").Index;
        Assert.True(ChurchServices.SlaveHoldingForbidden(rel, hill, north));                // forbid: any slave
        Assert.False(ChurchServices.SlaveHoldingForbidden(rel, north, hill));               // accept
        Assert.True(ChurchServices.SlaveHoldingForbidden(rel, vale, vale));                 // coreligionists_forbidden: a believer's slave...
        Assert.False(ChurchServices.SlaveHoldingForbidden(rel, vale, north));               // ...but not a heathen's
        var (hs, ri) = r.HeroFaith(); Assert.Equal(hill, ri);
        int piety0 = Piety.Of(church, pt, hero);
        var when = new GameTime(DayOf(5, 30) * 86400L);
        int lost = ChurchServices.SlaveHeldSeason(r.State, r.Content, hero, ri, 2, _ => north, hs, when);
        Assert.Equal(church.Settings.SlaveSeasonPiety, lost); Assert.Equal(Math.Max(0, piety0 - 10), Piety.Of(church, pt, hero));
        Assert.Contains(r.State.Chronicle.All, e => e.TypeId == "slave_held" && e.Subjects.Contains(hero));
        Assert.Equal(-20, Standing.Of(r.State.Standing, hero, GroupRef.Settlement(hs)));                    // the town thinks the worse of him
        Assert.True(Standing.Of(r.State.Standing, hero, GroupRef.Religion(ri)) <= 0);
        // an owner whose faith accepts it loses nothing
        var other = r.People.IdOf(Enumerable.Range(0, pt.Count).First(i => pt.IsAlive(i) && rel.OfCulture(pt.Culture[i]) == hill));
        Assert.Equal(0, ChurchServices.SlaveHeldSeason(r.State, r.Content, other, north, 3, _ => hill, hs, when));
    }

    [Fact]
    public void ASinCostsItsPiety_MurderTwentyTheftFiveSacrilegeThirtyAndGraveRobbingAFifthOfTheBarrowTaboo()
    {
        using var r = new FaithRig();
        var church = r.Church; var pt = r.People; var hero = r.Hero; var hill = r.Religions.Get(r.HeroFaith().Religion)!;
        Assert.Equal(90 / 5, ChurchServices.SinPiety(hill, "grave_robbing")); Assert.Equal(0, ChurchServices.SinPiety(hill, "poaching"));
        Piety.Adjust(church, pt, hero, 100);                                              // start from the top so every sin shows
        int p0 = Piety.Of(church, pt, hero); Assert.Equal(100, p0);
        FaithWiring.OnCrime(r.State, hero, "theft"); Assert.Equal(95, Piety.Of(church, pt, hero));
        FaithWiring.OnCrime(r.State, hero, "murder"); Assert.Equal(75, Piety.Of(church, pt, hero));
        FaithWiring.OnCrime(r.State, hero, "sacrilege"); Assert.Equal(45, Piety.Of(church, pt, hero));
        FaithWiring.OnCrime(r.State, hero, "grave_robbing"); Assert.Equal(27, Piety.Of(church, pt, hero));
        // a recorded crime tells the church through the law's own commit
        int town = r.HeroFaith().Settlement; var law = Ravensguard.Sim.Law.LawWorld.From(r.State, r.Content);
        var witnesses = Enumerable.Range(0, pt.Count).Where(i => pt.IsAlive(i) && pt.HomeSettlement[i] == town).Take(2).Select(i => new Ravensguard.Sim.Law.WitnessRow(pt.IdOf(i).Value, 0)).ToList();
        Ravensguard.Sim.Law.CrimeSystem.Commit(law, new Ravensguard.Sim.Law.CrimeBook(), new Ravensguard.Sim.Law.ActSpec("murder", hero, EntityId.None, town, 5 * 86400L, 0, 0, "ch_note_crime_hero"), witnesses, new Ravensguard.Sim.Law.ActorLook([], 1));
        Assert.Equal(7, Piety.Of(church, pt, hero));
    }

    // ---------------------------------------------------------------- FC-13

    [Fact]
    public void FC13_ABelieverWhoseFaithForbidsUsuryCannotOfferALoanAtInterest_SoTheLendersAreOfOtherFaithsOrNone()
    {
        using var r = new FaithRig(runtime: false);
        var rel = r.Religions;
        var vale = rel.All.First(x => x.Id == "church_of_the_vale").Index; var coast = rel.All.First(x => x.Id == "drowned_saints").Index;
        var north = rel.All.First(x => x.Id == "old_gods").Index; var hill = rel.All.First(x => x.Id == "hill_saints").Index;
        Assert.False(ChurchServices.MayLendAtInterest(rel, vale)); Assert.False(ChurchServices.MayLendAtInterest(rel, coast));
        Assert.True(ChurchServices.MayLendAtInterest(rel, north)); Assert.True(ChurchServices.MayLendAtInterest(rel, hill));
        Assert.True(ChurchServices.MayLendAtInterest(rel, -1));                                          // no faith: no scruple
        Assert.True(ChurchServices.CanOfferLoan(rel, vale, atInterest: false));                          // a loan at no interest is charity
        Assert.False(ChurchServices.CanOfferLoan(rel, vale, atInterest: true));
        var merchants = new (string Who, int Religion)[] { ("Aldous", vale), ("Brand", north), ("Cadoc", hill), ("Dwyn", coast), ("Eilidh", -1) };
        Assert.Equal(["Brand", "Cadoc", "Eilidh"], ChurchServices.InterestLenders(rel, merchants));
    }

    // ---------------------------------------------------------------- the section and the groups

    [Fact]
    public void TheFaithSectionRoundTrips_AWholeWorldSavesLoadsAndSavesByteIdentical_WithAVacancyAPilgrimageAndPiety()
    {
        using var r = new FaithRig();
        var church = r.Church; var see = church.Sees.Values.First();
        see.Bishop = -1; see.VacantSince = 77; see.ElectionDay = 95; see.TakenMites = 1234;
        church.Pilgrim = new PilgrimRow { Religion = "hill_saints", Target = 3, StartDay = 70, Entry = 99 };
        church.LastServiceDay = 69; church.ComfortUntilDay = 80; church.Pilgrimages = 2; church.HeroId = (long)r.Hero.Value; church.LastDay = 76;
        Piety.Adjust(church, r.People, r.Hero, 17);
        church.Parishes.Values.First().FabricMites = 410; church.Parishes.Values.First().ServiceDay = 60; church.Parishes.Values.First().Adults = 55; church.Parishes.Values.First().Attended = 47;
        var b1 = SaveCodec.SerializeCompressed(FaithSave.Capture(church));
        var back = SaveCodec.DeserializeCompressed<FaithSave>(b1).Restore();
        Assert.Equal(church.Hash(), back.Hash());
        Assert.Equal(b1, SaveCodec.SerializeCompressed(FaithSave.Capture(back)));

        var bytes = SaveCodec.Encode(WorldSave.Capture(r.State, "h", "0.1"));
        var st = WorldSave.Restore(SaveCodec.Decode(bytes), r.Content, "h", null, r.W.Continent, r.W.History); st.Content = r.Content;
        Assert.Equal(church.Hash(), st.Church!.Hash());
        Assert.Equal(bytes, SaveCodec.Encode(WorldSave.Capture(st, "h", "0.1")));
    }

    [Fact]
    public void ReligionGroupsCount_MembersAndSettlementsComeFromEachPersonsCulture_AndAPlacesFaithHearsOfADeed()
    {
        using var r = new FaithRig(runtime: false);
        var rel = r.Religions; var map = new RegionGroups(r.Region, r.People, 100, rel);
        int ri = r.HeroFaith().Religion;
        Assert.True(map.Members(GroupRef.Religion(ri)) > 0);
        Assert.NotEmpty(map.SettlementsOf(GroupRef.Religion(ri)));
        Assert.Equal(ri, map.ReligionOf(0).Index);
        int own = Enumerable.Range(0, r.People.Count).First(i => r.People.IsAlive(i) && r.People.HomeSettlement[i] == 0);
        Assert.Equal(rel.OfCulture(r.People.Culture[own]), map.ReligionOfPerson(r.People.IdOf(own)).Index);
        // a person's murder is heard in his town: the religion row moves by its share (-40 at full knowledge)
        var book = new StandingBook(); var deeds = DeedTable.From(r.Content);
        var doer = r.People.IdOf(1); int home = r.People.HomeSettlement[1];
        var rec = new DeedRecord { Entry = new EntityId(1), Doer = doer, DeedId = "murder", Day = 90, Place = home };
        rec.Heard[home] = map.Members(GroupRef.Settlement(home));
        book.AddDeed(rec); book.Recompute(doer, deeds, map, 100);
        int v = book.Value(doer, GroupRef.Religion(map.ReligionOf(home).Index));
        Assert.InRange(v, -40, 0);                                                                       // a share of the faith heard it
        Assert.Equal(-300, book.Value(doer, GroupRef.Settlement(home)));
        rec.Proclaimed = true; book.Recompute(doer, deeds, map, 100);                                      // the church proclaims it: the whole faith has heard
        Assert.Equal(-40, book.Value(doer, GroupRef.Religion(map.ReligionOf(home).Index)));
    }

    [Fact]
    public void ASaveInTheMiddleOfTenDaysWithAHolyDayInThem_ResumesToTheSameChurchAndTheSameBytes()
    {
        using var straight = new FaithRig();
        long h0 = straight.Runtime.HourDone + (24 - straight.Runtime.HourDone % 24) % 24;
        straight.Runtime.AdvanceTo(GameTime.FromGameHours(h0 + 24 * 5));
        var (hs, ri) = straight.HeroFaith(); long sec = (h0 + 24 * 5) * 3600 + 600;
        straight.Church.Pilgrim = new PilgrimRow { Religion = straight.Religions.Get(ri)!.Id, Target = hs, StartDay = (int)(sec / 86400), Entry = 5 };
        Piety.Adjust(straight.Church, straight.People, straight.Hero, 9);
        var bytes = SaveCodec.Encode(WorldSave.Capture(straight.State, "h", "0.1"));
        straight.Runtime.AdvanceTo(GameTime.FromGameHours(h0 + 24 * 10));                  // a feast of the hill faith falls on the tenth day
        var want = SaveCodec.SerializeCompressed(FaithSave.Capture(straight.Church));

        var st = WorldSave.Restore(SaveCodec.Decode(bytes), straight.Content, "h", null, straight.W.Continent, straight.W.History); st.Content = straight.Content;
        using var resumed = new WorldRuntime(st, straight.Content, new MemoryRegionPager());
        resumed.AdvanceTo(GameTime.FromGameHours(h0 + 24 * 10));
        Assert.Equal(want, SaveCodec.SerializeCompressed(FaithSave.Capture(st.Church!)));
        Assert.Equal(straight.Church.Hash(), st.Church!.Hash());
        Assert.True(straight.Church.LastDay >= (int)(h0 / 24) + 9);
        Assert.True(st.Economy!.Ledger.Audit().IsConserved);
    }

    [Fact]
    public void AWorldRunsASeasonWithTheChurchOn_CoinConserved_AndTheSameTwice()
    {
        string Run()
        {
            using var r = new FaithRig();
            long h0 = r.Runtime.HourDone + (24 - r.Runtime.HourDone % 24) % 24;
            r.Runtime.AdvanceTo(GameTime.FromGameHours(h0 + 24 * 16));                       // past the next season's first day (the world starts on the spring's first)
            Assert.True(r.State.Economy!.Ledger.Audit().IsConserved);
            Assert.NotEqual(-1, r.Church.LastDay);
            return r.Church.Hash().ToString("X") + "/" + r.State.Chronicle.Hash();
        }
        string a = Run(), b = Run();
        output.WriteLine(a);
        Assert.Equal(a, b);
    }
}
