using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Save;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;
using Xunit.Abstractions;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Tests.Land;

/// <summary>LD-2 and the save rules (overview 7): the optional "land" section, save, load, save byte for byte; save, load and go on equals the run that never stopped; a world without land keeps its bytes.</summary>
public class LandSaveTests(ITestOutputHelper output)
{
    private static (SeamWorld W, WorldState State) Game()
    {
        var w = SeamWorld.Get(1234);
        var home = w.BlockOrigin.Offset(1, 1);
        var dir = Ravensguard.Sim.WorldGen.ContinentSettings.FindBaseContent()!;
        var cs = Ravensguard.Sim.WorldGen.ContinentSettings.LoadDefaults(dir); cs.Seed = 1234;
        var hs = Ravensguard.Sim.WorldGen.History.HistorySettings.LoadDefaults(dir); hs.Seed = 1234;
        var region = RegionGenerator.Generate(w.Continent, w.History, home, w.Settings);
        LandmarkPlacer.Place(region, w.History, w.Content);
        var pop = Populator.Populate(region, w.History, w.Content);
        var origin = w.Content.All<OriginDef>().OrderBy(o => o.Order).First();
        var hero = HeroRecord.FromOrigin("Aldric Vane", false, origin);
        return (w, NewGameWorld.Create(w.Content, w.Continent, w.History, cs, hs, w.Settings, home.CellX, home.CellY, pop, hero, null));
    }

    /// <summary>The hero buys a plot, leases a field and a stall, stakes a claim (through the hero's desk, as the game sends them), so every part of the book is in use.</summary>
    private static void Play(WorldState s, ContentDatabase content, WorldRuntime rt)
    {
        var lw = s.HomeRecords!.Sim.Get<LandWorld>()!;
        var ctx = new HeroContext(s, s.Party!, content);
        s.Economy!.Ledger.Mint(ctx.Purse, Coin.FromCrowns(2000), SourceReason.Seed);              // the test's purse (before the save, the same in both runs)
        var desk = new HeroDesk();
        long sec = rt.HourDone * 3600;
        var reg = s.Population.Region;
        int town = reg.Settlements.FindIndex(x => x.Class == SettlementClass.Town && lw.Listings(reg.Settlements.IndexOf(x)).Count > 0);
        var lot = lw.Listings(town)[0];
        int parcel = Enumerable.Range(0, reg.Parcels.Count).FirstOrDefault(lw.Leasable, -1);
        if (parcel < 0)
        {
            parcel = Enumerable.Range(0, reg.Parcels.Count).First(p => reg.Parcels[p].Owner.Kind == OwnerKind.Tenant && LandRegistry.LordHousehold(s.Population, reg.Parcels[p].Settlement) >= 0);
            int lord = LandRegistry.LordHousehold(s.Population, reg.Parcels[parcel].Settlement);
            int old = s.Population.ParcelHolder[parcel]; if (old >= 0) s.Population.Households.ParcelCount[old]--;
            s.Population.ParcelHolder[parcel] = lord; s.Population.Households.ParcelCount[lord]++;
            s.HomeRecords.Economy.Farming.State.Parcels.Holder[parcel] = lord;
        }
        int N = RegionData.Size; (int X, int Y) wild = default;
        for (int t = 1000; t < RegionData.Tiles; t += 7)
        {
            int x = t % N * 64 + 32, y = t / N * 64 + 32;
            if (LandRules.OpenGround(reg, t) && LandRules.LandFief(reg, lw.Rules, t) == -1 && lw.Grow(t, [], 8, x, y, u => lw.ClaimTileOk(u, -1, null)).Length >= 8) { wild = (x, y); break; }
        }
        desk.Issue(LandDesk.Buy(lot, sec));
        desk.Issue(LandDesk.LeaseField(parcel, sec));
        desk.Issue(LandDesk.Stall(town, 0, false, sec));
        desk.Issue(LandDesk.Stake(wild.X, wild.Y, false, sec));
        desk.ApplyDue(ctx, rt.HourDone);
        Assert.All(desk.View!.Results, r => Assert.True(r.Ok, r.Text));
        Assert.Equal(4, desk.View.Results.Count);
    }

    [Fact]
    public void LD2_BuyLeaseClaim_SaveLoadSave_IsByteIdentical_TheRegistryReplays_AndTheRunGoesOnTheSame()
    {
        var (w, a) = Game();
        using var ra = new WorldRuntime(a, w.Content, new MemoryRegionPager());
        Assert.NotNull(a.HomeRecords!.Sim.Get<LandWorld>());
        Play(a, w.Content, ra);
        Assert.False(a.Land!.IsEmpty);
        ra.AdvanceTo(GameTime.FromGameHours(ra.HourDone + 30));
        var bytes1 = SaveCodec.Encode(WorldSave.Capture(a, "h", "0.1"));
        var file1 = SaveCodec.Decode(bytes1);
        Assert.Contains(LandSave.SectionName, file1.Sections.Keys);
        output.WriteLine($"land section {file1.Sections[LandSave.SectionName].Length} bytes, {a.Land.Deeds.Count} deeds, {a.Land.Changes.Count} changes");
        Assert.True(file1.Sections[LandSave.SectionName].Length < 100_000);
        var b = WorldSave.Restore(file1, w.Content, "h", null, w.Continent, w.History);
        b.Content = w.Content;                                                                   // as the game's load does: the next save diffs the people again
        Assert.Equal(a.Land.ComputeHash(), b.Land!.ComputeHash());
        var bytes2 = SaveCodec.Encode(WorldSave.Capture(b, "h", "0.1"));
        var f2 = SaveCodec.Decode(bytes2);
        var diff = file1.Sections.Keys.Union(f2.Sections.Keys).Where(k => !file1.Sections.TryGetValue(k, out var x) || !f2.Sections.TryGetValue(k, out var y) || !x.Span.SequenceEqual(y.Span)).ToList();
        Assert.True(diff.Count == 0, "sections differ after save-load-save: " + string.Join(", ", diff));
        Assert.Equal(bytes1, bytes2);                                                            // save, load, save: byte for byte
        // the registry rebuilt over the loaded world is the same
        var rules = LandRules.From(w.Content);
        Assert.Equal(LandRegistry.Build(a.Population.Region, a.Population, a.Land, rules).ComputeHash(), LandRegistry.Build(b.Population.Region, b.Population, b.Land, rules).ComputeHash());
        // go on: the loaded world and the one that never stopped pass a season's dues the same way
        using var rb = new WorldRuntime(b, w.Content, new MemoryRegionPager());
        long target = (ra.HourDone / 24 / TimeConst.DaysPerSeason + 1) * TimeConst.DaysPerSeason * 24 + 30;          // past the next season's dues
        ra.AdvanceTo(GameTime.FromGameHours(target)); rb.AdvanceTo(GameTime.FromGameHours(target));
        Assert.Contains(a.Land.Deeds.Values, d => d.Kind == HoldingKind.Parcel && d.ArrearsSeasons == 0 && d.DuesMitesPerSeason > 0);
        Assert.Equal(a.Land.ComputeHash(), b.Land.ComputeHash());
        var fa = WorldSave.Capture(a, "h", "0.1"); var fb = WorldSave.Capture(b, "h", "0.1");
        Assert.True(fa.Sections[LandSave.SectionName].Span.SequenceEqual(fb.Sections[LandSave.SectionName].Span));
        // (the whole runtime world is compared hour for hour at the region level below: a runtime reloaded mid-game differs from the one that ran on in its economy even
        // without land, so the exact go-on check is the region section's, as growth's is)
    }

    /// <summary>The home region's records with the land installed (as <see cref="LandWiring.Attach"/> does), over a given book.</summary>
    private static LandWorld Install(RegionWorld rw, LandBook book, ContentDatabase content)
    {
        var lw = new LandWorld(book, rw.Population, rw.Sim.Economy, content, () => rw.Economy.Growth.State, null, rw.Economy.Markets);
        rw.Sim.Set(lw); rw.Economy.Growth.LandHolds = lw.HoldsLot; rw.Economy.Farming.LeasedOut = lw.IsLeased;
        return lw;
    }

    private static void Script(RegionWorld rw, LandWorld lw)
    {
        var e = rw.Sim.Economy; var hero = EconomyState.HeroOwner; e.SeedHero(2000);
        int day = (int)(rw.HourDone / 24);
        EntityId C(GameTime when, string type, IReadOnlyList<EntityId> subj, IReadOnlyList<Cause> causes, params (string Key, string Value)[] data) => EntityId.None;
        var reg = rw.Population.Region;
        int town = reg.Settlements.FindIndex(x => x.Class == SettlementClass.Town && lw.Listings(reg.Settlements.IndexOf(x)).Count > 0);
        var l = lw.Listings(town)[0];
        Assert.True(lw.Buy(hero, PurseKind.Person, town, l.Lot, l.Rect, 0, day, C).Ok);
        Assert.True(lw.LeaseStall(hero, PurseKind.Person, town, 0, false, day, C).Ok);
        int N = RegionData.Size;
        for (int t = 1000; t < RegionData.Tiles; t += 7)
        {
            int x = t % N * 64 + 32, y = t / N * 64 + 32;
            if (LandRules.OpenGround(reg, t) && LandRules.LandFief(reg, lw.Rules, t) == -1 && lw.Grow(t, [], 8, x, y, u => lw.ClaimTileOk(u, -1, null)).Length >= 8) { Assert.True(lw.Stake(hero, PurseKind.Person, x, y, false, day, C).Ok); break; }
        }
        var (vs, vl) = (-1, -1);
        for (int s2 = 0; s2 < reg.Settlements.Count && vs < 0; s2++) for (int k = 0; k < reg.Settlements[s2].Plan.Lots.Count; k++) if (reg.Settlements[s2].Plan.Lots[k].Vacant && reg.Settlements[s2].Plan.Lots[k].Dwelling != DwellingType.None && !lw.HoldsLot(s2, k) && LandRegistry.LordHousehold(rw.Population, s2) >= 0) { vs = s2; vl = k; break; }
        Assert.True(vs >= 0, "no vacant house");
        var r = lw.LeaseHouse(hero, PurseKind.Person, vs, vl, day, C);
        Assert.True(r.Ok, r.Key);
    }

    [Fact]
    public void LD2_SaveLoadAndGoOn_EqualsTheRunThatNeverStopped_RegionAndLandHourForHour()
    {
        void Step(RegionWorld rw, long to) { while (rw.HourDone < to) rw.AdvanceTo(Math.Min(rw.HourDone + 24, to)); }
        var content = SeamWorld.Get(1234).Content;
        var rigA = new Ravensguard.Sim.Tests.RegionsB.RegionRig(); var a = rigA.Set.TryGet(rigA.Home)!; long h0 = a.HourDone;
        var la = Install(a, new LandBook(), content); Script(a, la);
        long end = h0 + 24 * 40;                                                          // across two seasons' dues and the claim's lapse window
        Step(a, end);
        var rigB = new Ravensguard.Sim.Tests.RegionsB.RegionRig(); var b = rigB.Set.TryGet(rigB.Home)!;
        var lb = Install(b, new LandBook(), content); Script(b, lb);
        Step(b, h0 + 24 * 17);                                                            // past the first season's dues
        var bytes = b.CaptureBytes(); var land = SaveCodec.SerializeCompressed(LandSave.Capture(lb.Book));
        var d = SaveCodec.DeserializeCompressed<RegionSectionData>(bytes);
        var back = RegionWorld.Restore(rigB.Sv, rigB.Home, b.Population.Region, d);
        var lback = Install(back, SaveCodec.DeserializeCompressed<LandSave>(land).Restore(), content);
        Assert.Equal(bytes, back.CaptureBytes());
        Assert.Equal(land, SaveCodec.SerializeCompressed(LandSave.Capture(lback.Book)));          // save, load, save: byte for byte
        Assert.Equal(lb.Registry().ComputeHash(), lback.Registry().ComputeHash());
        Step(back, end);
        Assert.Equal(la.Book.ComputeHash(), lback.Book.ComputeHash());
        Assert.Equal(a.RecordsHash(), back.RecordsHash());
        Assert.Equal(a.CaptureBytes(), back.CaptureBytes());
        Assert.Contains(la.Book.Deeds.Values, x => x.Kind == HoldingKind.Building && x.ArrearsSeasons == 0);
        output.WriteLine($"land book {la.Book.Deeds.Count} deeds, {la.Book.Changes.Count} changes, {la.Book.Claims.Count} claims; land section {land.Length} bytes");
    }

    [Fact]
    public void AWorldThatNeverTouchedLand_HasNoLandSection()
    {
        var (w, a) = Game();
        using var ra = new WorldRuntime(a, w.Content, new MemoryRegionPager());
        ra.AdvanceTo(GameTime.FromGameHours(ra.HourDone + 24 * 16));                                 // across a season's first day
        Assert.True(a.Land!.IsEmpty);
        var file = WorldSave.Capture(a, "h", "0.1");
        Assert.DoesNotContain(LandSave.SectionName, file.Sections.Keys);
        var back = WorldSave.Restore(file, w.Content, "h", null, w.Continent, w.History);
        Assert.Null(back.Land);
    }
}
