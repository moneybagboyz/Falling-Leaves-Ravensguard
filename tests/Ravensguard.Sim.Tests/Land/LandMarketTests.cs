using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Trade;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Land;

/// <summary>The lot market's other half and the view the screens read: resale to a household that needs room, the desk's tags, the hero's deeds in the view.</summary>
public class LandMarketTests(ITestOutputHelper output)
{
    [Fact]
    public void AnOfferedPlot_IsBoughtAtTheAsk_ByAHouseholdOfThePlaceThatNeedsRoom_AndOnlyAtOrBelow90PercentOfItsValue()
    {
        using var rig = LandRig.Make(crowns: 2000);
        var hht = rig.W.Pop.Households; var pt = rig.W.Pop.People;
        bool Wants(int h) => hht.MemberCount[h] * 100 > hht.Beds[h] * 130 || hht.Members(h).Any(m => pt.Status[m] == PersonStatus.Alive && pt.Sex[m] == Sex.Female && pt.Spouse[m] < 0 && pt.MarriageWait[m] > 0);
        // a household of a village with a plot for sale, whose daughter waits to marry (nobody is crowded or waiting in a freshly generated world: the marriage market makes them)
        int v0 = rig.Village();
        int hh = Enumerable.Range(0, hht.Count).First(h => hht.Settlement[h] == v0 && hht.MemberCount[h] > 0 && hht.Class[h] != SocialClass.Gentry
            && hht.Members(h).Any(m => pt.Status[m] == PersonStatus.Alive && pt.Sex[m] == Sex.Female && pt.Spouse[m] < 0 && pt.AgeYears(m, rig.Day) is >= 16 and < 30));
        int girl = hht.Members(hh).First(m => pt.Status[m] == PersonStatus.Alive && pt.Sex[m] == Sex.Female && pt.Spouse[m] < 0 && pt.AgeYears(m, rig.Day) is >= 16 and < 30);
        pt.MarriageWait[girl] = 1;
        Assert.True(Wants(hh));
        int v = hht.Settlement[hh]; var l = rig.L.Listings(v).First(x => x.Lot < 0);
        var r = rig.L.Buy(rig.Hero, PurseKind.Person, v, -1, l.Rect, 0, rig.Day, rig.C);
        var d = rig.L.Book.Deeds[r.Deed];
        long value = rig.L.ValueOf(d);
        rig.Led.Transfer(rig.HeroPurse, rig.W.Econ.HouseholdPurse[hh], new Coin(value * 2), TransferReason.Gift);   // a buyer that can pay
        Assert.True(rig.L.Offer(rig.Hero, d.Id, value, rig.Day).Ok);                                 // at full value: too dear, nobody buys
        rig.L.Seasonal(LandWorld.NextSeasonStart(rig.Day), rig.C);
        Assert.Equal(rig.Hero, d.Holder);
        long ask = value * 900 / 1000;
        rig.L.Offer(rig.Hero, d.Id, ask, rig.Day);
        long h0 = rig.Coin;
        rig.L.Seasonal(LandWorld.NextSeasonStart(rig.Day) + 15, rig.C);
        Assert.NotEqual(rig.Hero, d.Holder);
        Assert.Equal(PurseKind.Household, d.HolderPurse);
        Assert.Equal(h0 - d.DuesMitesPerSeason + ask, rig.Coin);                                     // the season's dues out, then the ask in
        Assert.Equal(0, d.AskMites);
        Assert.Contains(rig.Chron, e => e.TypeId == "land_sold");
        Assert.True(rig.Led.Audit().IsConserved);
        output.WriteLine($"plot worth {value} sold for {ask} to {hht.Name[hht.IndexOf(d.Holder)]}");
    }

    [Fact]
    public void EveryLandTextKey_IsInEnCsv_InOneBlockBeforeTheFlavourRows()
    {
        var csv = Ravensguard.Sim.Tests.Dialogue.TalkKit.EnCsv();
        string root = Ravensguard.Sim.Tests.Dialogue.TalkKit.Root();
        var files = Directory.GetFiles(Path.Combine(root, "src", "Ravensguard.Sim", "Land"), "*.cs")
            .Concat([Path.Combine(root, "src", "Ravensguard.Game", "UI", "LandScreen.cs"), Path.Combine(root, "src", "Ravensguard.Game", "World", "StreetView.Land.cs")]);
        var used = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var f in files)
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(f), "\"(ld_[a-z_]+)\""))
                if (!m.Groups[1].Value.EndsWith('_')) used.Add(m.Groups[1].Value);
        foreach (var t in Enum.GetNames<Tenure>()) used.Add("ld_tenure_" + t.ToLowerInvariant());
        foreach (var k in Enum.GetNames<HoldingKind>()) used.Add("ld_kind_" + k.ToLowerInvariant());
        foreach (var c in Enum.GetNames<CharterState>()) used.Add("ld_cstate_" + c.ToLowerInvariant());
        var missing = used.Where(k => !csv.ContainsKey(k)).ToList();
        Assert.True(missing.Count == 0, "missing from en.csv: " + string.Join(", ", missing));
        Assert.True(used.Count > 100);
        // one contiguous block, before the flavour rows (fl_ stays last)
        var keys = File.ReadAllLines(Path.Combine(root, "src", "Ravensguard.Game", "localization", "en.csv")).Select(l => l.Split(',')[0]).ToList();
        int first = keys.FindIndex(k => k.StartsWith("ld_", StringComparison.Ordinal)), last = keys.FindLastIndex(k => k.StartsWith("ld_", StringComparison.Ordinal));
        Assert.True(keys.Skip(first).Take(last - first + 1).All(k => k.StartsWith("ld_", StringComparison.Ordinal)));
        Assert.True(last < keys.FindIndex(k => k.StartsWith("fl_", StringComparison.Ordinal)));
    }

    [Fact]
    public void TheDeskTags_AreLandCommands_AndTheView_ShowsListingsStallsTheClaimHereAndTheHerosDeeds()
    {
        using var rig = LandRig.Make(crowns: 2000);
        int town = rig.Town(); var l = rig.L.Listings(town)[0];
        var buy = LandDesk.Buy(l, 1000);
        Assert.Equal(HeroCommandKind.Land, buy.Kind); Assert.StartsWith("ld:buy|", buy.Tag); Assert.Equal(l.PriceMites, buy.Limit);
        Assert.True(rig.L.Buy(rig.Hero, PurseKind.Person, town, l.Lot, l.Rect, l.PriceMites, rig.Day, rig.C).Ok);
        Assert.True(rig.L.LeaseStall(rig.Hero, PurseKind.Person, town, 0, false, rig.Day, rig.C).Ok);
        var (x, y) = rig.ClaimPoint(wild: true, room: 8);
        var view = LandDesk.View(rig.L, rig.Hero, town, x, y, rig.Day);
        Assert.Equal(rig.Region.Settlements[town].Name, view.Place);
        Assert.Equal(2, view.Deeds.Count);
        Assert.Contains(view.Deeds, d => d.Kind == HoldingKind.Lot && d.Tenure == Tenure.Burgage);
        Assert.DoesNotContain(0, view.FreeStalls);
        Assert.True(view.Here.Ok); Assert.Equal(-1, view.Here.Fief); Assert.Equal(8, view.Here.Tiles);
        Assert.Equal(rig.Coin, view.CoinMites);
        Assert.True(view.Listings.Count > 0);
        output.WriteLine($"{view.Place}: {view.Listings.Count} lots, {view.Fields.Count} fields, {view.FreeStalls.Count} free stalls; first lot {view.Listings[0].PriceMites} mites");
    }
}
