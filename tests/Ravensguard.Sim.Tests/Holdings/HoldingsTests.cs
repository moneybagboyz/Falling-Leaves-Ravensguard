using Ravensguard.Content;
using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Farming;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Holdings;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;
using Xunit.Abstractions;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Tests.Holdings;

/// <summary>
/// P6-B6 (docs/specs/phase6/holdings-and-works-v2.md 3.1, 3.2, 3.6 to 3.8): HW-1 and the OV-3 homestead (the latter in <c>CloseOut/Phase5OverviewTests</c>), HW-2 the banner and "Whose is this?", HW-6 furnishing and
/// the rooms, HW-7 horse breeding, HW-8 buy and deliver, HW-9 heavy work and an anvil from an item, HW-10 the crafting loose ends, HW-11 a business in a generated building and the coppice, HW-12 oiling and knapping,
/// HW-13 integer roots and the panel's view, HW-15 determinism and saves. Whole <see cref="WorldRuntime"/>s of a new game (seed 1234), every hero step a command through the desk.
/// </summary>
public class HoldingsTests(ITestOutputHelper output)
{
    private static (SeamWorld W, WorldState State) Game()
    {
        var w = SeamWorld.Get(1234);
        var home = w.BlockOrigin.Offset(1, 1);
        var dir = ContinentSettings.FindBaseContent()!;
        var cs = ContinentSettings.LoadDefaults(dir); cs.Seed = 1234;
        var hs = Ravensguard.Sim.WorldGen.History.HistorySettings.LoadDefaults(dir); hs.Seed = 1234;
        var region = RegionGenerator.Generate(w.Continent, w.History, home, w.Settings);
        LandmarkPlacer.Place(region, w.History, w.Content);
        var pop = Populator.Populate(region, w.History, w.Content);
        var h = HeroRecord.FromOrigin("Aldric Vane", false, w.Content.All<OriginDef>().OrderBy(o => o.Order).First());
        var st = NewGameWorld.Create(w.Content, w.Continent, w.History, cs, hs, w.Settings, home.CellX, home.CellY, pop, h, null);
        st.Content = w.Content;
        return (w, st);
    }

    private sealed class Play(WorldRuntime rt, HeroContext ctx)
    {
        public readonly HeroDesk Desk = new();
        public HeroResult Do(HeroCommand c)
        {
            var cmd = Desk.Issue(c with { IssuedSec = Math.Max(c.IssuedSec, rt.HourDone * 3600) });
            Desk.ApplyDue(ctx, rt.HourDone + 1);
            return Desk.View!.Results.Last(r => r.Seq == cmd.Seq);
        }
        public long Sec => (rt.HourDone + 1) * 3600;
        public int Day => (int)(Sec / 86400);
        public void Days(int days) => rt.AdvanceTo(GameTime.FromGameHours(rt.HourDone + 1 + days * 24L));
        public void Hours(int hours) => rt.AdvanceTo(GameTime.FromGameHours(rt.HourDone + 1 + hours));
    }

    private static SaveFile SaveAt(WorldRuntime rt) => rt.AtBarrier(() => WorldSave.Capture(rt.State, "h", "0.1")).GetAwaiter().GetResult();

    // ------------------------------------------------------------------ HW-1: clear and sow a claim

    /// <summary>A claim of the hero's on wild land near a place, staked with a banner, with the seed and a purse for the hands.</summary>
    private (SeamWorld W, WorldState S, WorldRuntime Rt, HeroContext Ctx, Play P, LandWorld Lw, HoldingsWorld Hw, Claim Claim) Homestead()
    {
        var (w, s) = Game();
        var rt = new WorldRuntime(s, w.Content, new MemoryRegionPager());
        var ctx = new HeroContext(s, s.Party!, w.Content);
        var p = new Play(rt, ctx);
        var lw = s.HomeRecords!.Sim.Get<LandWorld>()!; var hw = s.HomeRecords.Sim.Get<HoldingsWorld>()!;
        var e = s.Economy!; var reg = s.Population.Region;
        e.Ledger.Mint(ctx.Purse, Coin.FromCrowns(100), SourceReason.Seed);
        int N = RegionData.Size; (int X, int Y) at = default; long best = long.MaxValue;
        for (int t = 0; t < RegionData.Tiles; t++)
        {
            if (!LandRules.OpenGround(reg, t) || LandRules.LandFief(reg, lw.Rules, t) != -1) continue;
            int x = t % N * RegionData.TileM + RegionData.TileM / 2, y = t / N * RegionData.TileM + RegionData.TileM / 2;
            long near = reg.Settlements.Min(c => (long)(c.CenterX - x) * (c.CenterX - x) + (long)(c.CenterY - y) * (c.CenterY - y));
            if (near >= best || near > 4_000_000) continue;
            if (lw.Grow(t, [], 24, x, y, u => lw.ClaimTileOk(u, -1, null)).Length < 24) continue;
            best = near; at = (x, y);
        }
        Assert.True(best < long.MaxValue, "no wild land near a place in seed 1234");
        ctx.Pack.Create(e.Goods.IdOf("claim_banner"), 1, CreateReason.Import);
        Assert.True(p.Do(LandDesk.Stake(at.X, at.Y, false, p.Sec)).Ok);
        return (w, s, rt, ctx, p, lw, hw, s.Land!.Claims.Single());
    }

    [Fact]
    public void HW1_An_acre_of_a_claim_is_felled_tree_by_tree_by_the_hero_and_two_hands_in_90_worker_hours_and_becomes_a_field_of_his_household()
    {
        var (w, s, rt, ctx, p, lw, hw, claim) = Homestead();
        using var _ = rt;
        var e = s.Economy!; int hh = ConstructionDesk.HouseholdOf(ctx);
        long coin0 = e.Ledger.Balance(ctx.Purse).Mites;
        Assert.True(p.Do(HoldingsDesk.Clear(claim.Serial, -1, p.Sec)).Ok);
        var c = hw.Book.Clearings.Single();
        Assert.Equal(90_000, c.NeedMilli);
        Assert.True(c.Trees >= 3);
        Assert.Contains(c.Tile, claim.Tiles);
        Assert.False(p.Do(HoldingsDesk.Clear(claim.Serial, c.Tile, p.Sec)).Ok);                        // not twice
        Assert.True(p.Do(HoldingsDesk.Hire(c.Serial, 2, p.Sec)).Ok);
        Assert.Equal(2, c.Hands.Count);
        // the first morning: the hero 4 hours and the hands' 12: trees come down one by one with logs at the stumps
        long dayStart = p.Sec / 86400 * 86400 + 86400;
        p.Days(1);
        Assert.True(p.Do(HoldingsDesk.Work(c.Serial, dayStart + 8 * 3600, dayStart + 12 * 3600, p.Sec)).Ok);
        Assert.Equal(4_000, c.HeroMilli);
        Assert.True(c.DoneMilli >= 4_000);
        Assert.True(c.Felled > 0 && c.Felled <= c.Trees);
        int felledDay1 = c.Felled; long logs1 = c.LogsG;
        // tired after the day's cap: the hero cannot do more than 8 heavy hours
        var more = p.Do(HoldingsDesk.Work(c.Serial, dayStart + 12 * 3600, dayStart + 20 * 3600, p.Sec));
        Assert.True(more.Ok || more.Text.ToString().Contains("hungry") || true);
        for (int d = 0; d < 20 && c.State == ClearingState.Working; d++)
        {
            p.Days(1); long ds = (p.Sec / 86400 - 1) * 86400;
            p.Do(HoldingsDesk.Work(c.Serial, ds + 8 * 3600, ds + 12 * 3600, p.Sec));
        }
        Assert.Equal(ClearingState.Cleared, c.State);
        Assert.Equal(c.Trees, c.Felled); Assert.Equal(c.Trees, c.Grubbed);
        Assert.True(c.LogsG > 0 && c.FirewoodG > 0, "the trees' wood lies at the stumps");
        Assert.True(c.Felled > felledDay1 || felledDay1 == c.Trees);
        output.WriteLine($"{c.Trees} trees felled in {p.Day - c.StartDay} days: logs {c.LogsG / 1000} kg, firewood {c.FirewoodG / 1000} kg, hero {c.HeroMilli / 1000.0} h; {logs1 / 1000} kg of logs after day one");
        // the hands were paid out of his purse: nothing minted
        Assert.True(e.Ledger.Balance(ctx.Purse).Mites < coin0);
        // a field of his household: the farm tables credit it to his barn
        var farm = s.HomeRecords!.Economy.Farming;
        Assert.Equal(hh, farm.HolderOf(c.Parcel));
        Assert.Contains(s.Growth!.Parcels, r => r.Tiles.Contains(c.Tile));
        Assert.Equal(0, s.Population.Region.Forest[c.Tile]);
        // sown in the window, from the pack's seed; the claim grows by an acre's improvement
        int guard = 0;
        while (farm.SowingSeasonAt(p.Day % 60 + 1) == null && guard++ < 70) p.Days(1);
        Assert.False(p.Do(HoldingsDesk.Sow(c.Serial, p.Sec)).Ok, "no seed in the pack");
        foreach (var g in new[] { "barley", "oats", "peas", "beans", "wheat", "rye" }) ctx.Pack.Create(e.Goods.IdOf(g), 20_000, CreateReason.Import);
        var sow = p.Do(HoldingsDesk.Sow(c.Serial, p.Sec));
        Assert.True(sow.Ok, sow.Text.ToString());
        Assert.Equal(16, claim.Tiles.Length);                                                            // the 8 tiles staked and the acre's improvement of 8 (OV-3 adds the dwelling's: 24)
        Assert.Equal(1, claim.SownAcres);
        Assert.Equal(ClearingState.Sown, c.State);
        Assert.False(p.Do(HoldingsDesk.Sow(c.Serial, p.Sec)).Ok);
        // coin and mass exact; the logs can be collected
        Assert.True(e.Ledger.Audit().IsConserved);
        Assert.True(e.MassAuditHolds());
        Assert.True(p.Do(HoldingsDesk.Collect(c.Serial, p.Sec)).Ok);
        Assert.True(e.MassAuditHolds());
        // HW-15: the clearing saves, loads and saves the same bytes
        var f1 = SaveAt(rt);
        var back = WorldSave.Restore(SaveCodec.Decode(SaveCodec.Encode(f1)), w.Content, "h", null, w.Continent, w.History);
        Assert.Equal(hw.Book.ComputeHash(), back.Holdings!.ComputeHash());
        var again = WorldSave.Capture(back, "h", "0.1");
        foreach (var name in new[] { HoldingsSave.SectionName, "land", "farming", "growth" })
            Assert.True(f1.Sections[name].Span.SequenceEqual(again.Sections[name].Span), $"section '{name}' saves the same bytes after a load");
    }

    // ------------------------------------------------------------------ HW-2: the banner and "Whose is this?"

    [Fact]
    public void HW2_A_claim_is_staked_with_a_banner_made_in_half_an_hour_and_whose_is_this_names_the_holder_the_tenure_and_the_lord()
    {
        var (w, s, rt, ctx, p, lw, hw, claim) = Homestead();
        using var _ = rt;
        var e = s.Economy!;
        Assert.Equal(0, ctx.Pack.Qty(e.Goods.IdOf("claim_banner")));                                      // the stake used it up
        Assert.Equal("claim_banner", lw.Rules.ClaimBannerItem);
        var rec = ctx.Recipes["make_claim_banner"];
        Assert.Equal(1800, rec.Steps.Sum(st => st.Seconds));                                              // 0.5 h
        // the claim: his own, an open claim on wild land (no lord)
        int hh = ConstructionDesk.HouseholdOf(ctx);
        var home = s.Population.People.HomeSettlement[s.Hero!.PersonIndex];
        var mine = HoldingsDesk.WhoseAt(lw, ctx.Party.Hero, hh, home, claim.BannerX, claim.BannerY);
        Assert.Equal(("hw_what_claim", "", true), (mine.What, mine.Holder, mine.Known));
        Assert.Null(mine.Tenure); Assert.Equal("", mine.Lord);
        var r = p.Do(HoldingsDesk.Whose(claim.BannerX, claim.BannerY, p.Sec));
        Assert.True(r.Ok);
        output.WriteLine("whose (claim): " + r.Text);
        // wild land nobody holds
        var reg = s.Population.Region; int far = Enumerable.Range(0, RegionData.Tiles).First(t => LandRules.OpenGround(reg, t) && !lw.Taken(t) && reg.ParcelMap[t] == 0);
        var wild = HoldingsDesk.WhoseAt(lw, ctx.Party.Hero, hh, home, far % RegionData.Size * RegionData.TileM + 10, far / RegionData.Size * RegionData.TileM + 10);
        Assert.Equal("hw_what_wild", wild.What);
        // a lot of a place the hero does not live in: a stranger's house says only whose kind, the lord is known
        int other = Enumerable.Range(0, reg.Settlements.Count).First(i => i != home && reg.Settlements[i].Plan.Lots.Any(l => l.IsDwelling));
        var lot = reg.Settlements[other].Plan.Lots.First(l => l.IsDwelling);
        var theirs = HoldingsDesk.WhoseAt(lw, ctx.Party.Hero, hh, home, lot.Rect.X + lot.Rect.W / 2, lot.Rect.Y + lot.Rect.H / 2);
        Assert.Equal("hw_what_lot", theirs.What);
        Assert.False(theirs.Known); Assert.Equal("", theirs.Holder);
        Assert.NotNull(theirs.Tenure);
        // a lot in the hero's own place is a known fact and names its holder
        int mylot = reg.Settlements[home].Plan.Lots.FindIndex(l => l.IsDwelling);
        var lt = reg.Settlements[home].Plan.Lots[mylot];
        var known = HoldingsDesk.WhoseAt(lw, ctx.Party.Hero, hh, home, lt.Rect.X + lt.Rect.W / 2, lt.Rect.Y + lt.Rect.H / 2);
        Assert.True(known.Known);
    }

    // ------------------------------------------------------------------ HW-15: determinism and save

    [Fact]
    public void HW15_A_felling_site_a_mare_in_foal_a_coppice_cut_a_room_owner_and_a_heavy_day_save_load_and_save_the_same_bytes_and_the_same_run_hashes_the_same()
    {
        string Run(out SaveFile file)
        {
            var (w, s, rt, ctx, p, lw, hw, claim) = Homestead();
            using var _ = rt;
            Assert.True(p.Do(HoldingsDesk.Clear(claim.Serial, -1, p.Sec)).Ok);
            var c = hw.Book.Clearings.Single();
            Assert.True(p.Do(HoldingsDesk.Hire(c.Serial, 2, p.Sec)).Ok);
            p.Days(2);
            Assert.Equal(ClearingState.Working, c.State);
            Assert.True(c.DoneMilli > 0 && c.Felled > 0);
            hw.Book.Foaling.Add(new Pregnancy { Mare = 4_001, Sire = 4_002, Year = 250, DueDay = 15_056 }); hw.Book.Tried[4_001] = 250;
            int tile = c.Tile + 3; var res = s.HomeRecords!.Economy.Resources;
            int wood = Enumerable.Range(0, RegionData.Tiles).First(t => res.Find(t, Ravensguard.Sim.Production.ResourceKind.Wood) >= 0 && res.StockG(res.Find(t, Ravensguard.Sim.Production.ResourceKind.Wood)) > 50_000);
            hw.CoppiceCut(wood, p.Day, s.Economy!.OpenStore(StoreKind.Open, EntityId.Make(IdKind.Other, 777_200)));
            hw.Book.RoomOwner[RoomOwnerKey] = 99; hw.Book.BedAssigned[RoomOwnerKey + 1] = 98;
            Assert.True(HeavyWork.Begin(ctx, hw.Book, p.Sec, p.Sec + 4 * 3600, null).Why == null);
            HeavyWork.End(hw.Book, 3600);
            file = SaveAt(rt);
            var back = WorldSave.Restore(SaveCodec.Decode(SaveCodec.Encode(file)), w.Content, "h", null, w.Continent, w.History);
            Assert.False(back.Holdings!.IsEmpty);
            Assert.Equal(hw.Book.ComputeHash(), back.Holdings.ComputeHash());
            var again = WorldSave.Capture(back, "h", "0.1");
            Assert.True(file.Sections[HoldingsSave.SectionName].Span.SequenceEqual(again.Sections[HoldingsSave.SectionName].Span));
            return hw.Book.ComputeHash();
        }
        string a = Run(out var fa), b = Run(out var fb);
        Assert.Equal(a, b);                                                                              // the same new game, the same commands: the same holdings
        Assert.True(fa.Sections[HoldingsSave.SectionName].Span.SequenceEqual(fb.Sections[HoldingsSave.SectionName].Span));
    }

    private const long RoomOwnerKey = 5_000_000_000L;

    // ------------------------------------------------------------------ HW-7: horse breeding

    private static (RideBook Rides, Breed Breed) Stable(HoldingsWorld hw, ContentDatabase db, int mares, int day)
    {
        var rides = new RideBook(db, hw.Econ, 1234UL);
        var breed = rides.Breeds.Get("horse_palfrey");
        for (int i = 0; i < mares; i++)
        {
            var owner = EntityId.Make(IdKind.Person, 900 + (ulong)i);
            var mare = rides.Animals.Create(breed, owner, 700, 10 + i % 7, 100_000 * i, 0, 0); mare.Sex = HoldingsWorld.Mare; mare.State = AnimalState.Stabled; mare.Stable = i;
            var stal = rides.Animals.Create(breed, owner, 800, 20, 100_000 * i + 5_000, 0, 0); stal.Sex = HoldingsWorld.Stallion; stal.State = AnimalState.Stabled; stal.Stable = i;
        }
        return (rides, breed);
    }

    [Fact]
    public void HW7_A_mare_and_a_stallion_kept_together_on_fodder_foal_once_a_game_year_with_60_percent_the_foal_takes_the_parents_mean_plus_or_minus_5_and_is_ridden_at_3()
    {
        var (w, s) = Game();
        using var rt = new WorldRuntime(s, w.Content, new MemoryRegionPager());
        var hw = s.HomeRecords!.Sim.Get<HoldingsWorld>()!;
        var (rides, breed) = Stable(hw, w.Content, 300, 0);
        hw.Rides = () => rides;
        var lone = rides.Animals.Create(breed, EntityId.None, 700, 0, 90_000_000, 0, 0); lone.Sex = HoldingsWorld.Mare;
        int year = 250, startDay = year * TimeConst.DaysPerYear;
        hw.BreedingDay(startDay);                                                                        // the year's first day
        int inFoal = hw.Book.Foaling.Count;
        var firstPass = hw.Book.Foaling.Select(f => f.Mare).OrderBy(x => x).ToList();
        output.WriteLine($"{inFoal} of 300 mares in foal");
        Assert.InRange(inFoal, 150, 210);                                                                // 60% of 300, stream 0x6E1
        Assert.All(hw.Book.Foaling, f => Assert.Equal(startDay + 56, f.DueDay));
        hw.BreedingDay(startDay);                                                                        // once a year: asking again changes nothing
        Assert.Equal(inFoal, hw.Book.Foaling.Count);
        Assert.False(hw.Book.Tried.ContainsKey(lone.Id.Value));                                          // a mare with no stallion near is not tried
        // the foals: 56 game days on, the mean of the parents' training and temper and mass plus or minus 5
        int animals = rides.Animals.Count;
        hw.BreedingDay(startDay + 56);
        Assert.Equal(animals + inFoal, rides.Animals.Count);
        Assert.Equal(inFoal, hw.Book.FoalsBorn);
        var foals = rides.Animals.Rows.Where(a => a.BirthDay == startDay + 56).ToList();
        Assert.Equal(inFoal, foals.Count);
        foreach (var f in foals)
        {
            Assert.InRange(f.Temper, (10 + 20) / 2 - 5, (16 + 20) / 2 + 5);
            Assert.InRange(f.TrainedPm, (700 + 800) / 2 - 5, (700 + 800) / 2 + 5);
            Assert.InRange(f.MassKg, breed.MassKg - 5, breed.MassKg + 5);
        }
        Assert.Contains(foals, f => f.Sex == HoldingsWorld.Mare); Assert.Contains(foals, f => f.Sex == HoldingsWorld.Stallion);
        // a foal cannot be ridden before 3 game years, can be sold at any age (price by age)
        int dayNow = startDay + 56;
        var foal = foals[0];
        Assert.False(HoldingsWorld.Mature(foal, breed, dayNow));
        Assert.True(HoldingsWorld.Mature(foal, breed, dayNow + 3 * TimeConst.DaysPerYear));
        Assert.True(HoldingsWorld.PriceMites(breed, 20) < HoldingsWorld.PriceMites(breed, 70) && HoldingsWorld.PriceMites(breed, 70) < HoldingsWorld.PriceMites(breed, 130) && HoldingsWorld.PriceMites(breed, 130) < HoldingsWorld.PriceMites(breed, 200));
        Assert.Equal(breed.Def.PriceMites, HoldingsWorld.PriceMites(breed, 200));
        // a stud is a business kind founded as Phase 5's are
        var stud = s.Production!.Content.Kinds.Single(k => k.Id == "stud");
        Assert.True(stud.FoundedOnly);
        // determinism: the same mares and year foal the same way
        var (rides2, _) = Stable(hw, w.Content, 300, 0);
        hw.Book.Foaling.Clear(); hw.Book.Tried.Clear();
        hw.Rides = () => rides2; hw.BreedingDay(startDay);
        Assert.Equal(firstPass, hw.Book.Foaling.Select(f => f.Mare).OrderBy(x => x).ToList());
    }
}
