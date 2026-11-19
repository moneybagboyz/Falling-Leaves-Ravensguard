using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WorldGen.Region;
using Xunit.Abstractions;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Tests.Land;

/// <summary>Founding charters (land spec 4a, decided with the user): LD-C1 to LD-C4.</summary>
public class CharterTests(ITestOutputHelper output)
{
    /// <summary>A fief whose lord can grant a site, and the centre of a site he could grant (the first such fief and tile, ascending).</summary>
    private static (int Fief, int X, int Y) Site(LandRig rig)
    {
        int N = RegionData.Size; var r = rig.Region;
        for (int t = 1000; t < RegionData.Tiles; t += 13)
        {
            if (!LandRules.OpenGround(r, t) || LandRules.LandFief(r, rig.L.Rules, t) != -1) continue;
            int f = LandRules.FiefOfTile(r, t); if (f < 0) continue;
            int x = t % N * 64 + 32, y = t / N * 64 + 32;
            if (rig.L.CharterSite(GrantorKind.Fief, f, x, y).Length >= rig.L.Rules.CharterSiteTiles && r.NearestActiveWarM(x, y) >= rig.L.Rules.CharterWarM) return (f, x, y);
        }
        throw new InvalidOperationException("no site");
    }

    private static List<int> Settlers(LandRig rig, int n) => [.. Enumerable.Range(0, rig.W.Pop.Households.Count).Where(rig.L.CanSettle).Take(n)];

    [Fact]
    public void LDC1_AGrantedCharter_MakesTheDeed_AndMovesTheFeeToTheGrantorsTreasury_CoinConserved()
    {
        using var rig = LandRig.Make(crowns: 200);
        var (f, x, y) = Site(rig);
        int seat = rig.L.SeatOf(GrantorKind.Fief, f);
        var fiefId = rig.Region.Fiefs[f].Id;
        long t0 = rig.Bal(fiefId, PurseKind.Treasury), h0 = rig.Coin; var audit0 = rig.Led.Audit();
        // not at the seat: refused, nothing moves
        var away = rig.L.Petition(rig.Hero, PurseKind.Person, GrantorKind.Fief, f, seat == 0 ? 1 : 0, x, y, rig.Day, rig.C);
        Assert.False(away.Ok); Assert.Equal("ld_charter_at_seat", away.Key);
        // standing below the rule: refused
        rig.L.Book.Standing[LandBook.StandingKey(GrantorKind.Fief, f)] = -1;
        Assert.Equal("ld_charter_standing", rig.L.Petition(rig.Hero, PurseKind.Person, GrantorKind.Fief, f, seat, x, y, rig.Day, rig.C).Key);
        rig.L.Book.Standing.Clear();
        var r = rig.L.Petition(rig.Hero, PurseKind.Person, GrantorKind.Fief, f, seat, x, y, rig.Day, rig.C);
        Assert.True(r.Ok, r.Key);
        var c = rig.L.Book.Charters.Single();
        var d = rig.L.Book.Deeds[r.Deed];
        output.WriteLine($"charter of {rig.Region.Fiefs[f].Name} at {rig.Region.Settlements[seat].Name}: {c.Tiles.Length} tiles, {c.MinHouseholds} households by day {c.SettleByDay}, dues {c.DuesMitesPerYear} a year, fee {c.FeeMites}");
        Assert.Equal(HoldingKind.FoundingCharter, d.Kind); Assert.Equal(rig.Hero, d.Holder);
        Assert.Equal(5, c.MinHouseholds); Assert.Equal(c.GrantDay + 2 * TimeConst.DaysPerYear, c.SettleByDay);
        Assert.Equal(rig.L.Rules.CharterFeeMites, c.FeeMites);
        Assert.Equal(t0 + c.FeeMites, rig.Bal(fiefId, PurseKind.Treasury)); Assert.Equal(h0 - c.FeeMites, rig.Coin);
        Assert.True(rig.Led.Audit().IsConserved); Assert.Equal(audit0.InPurses, rig.Led.Audit().InPurses);
        // the dues go to the grantor's treasury by the season
        long t1 = rig.Bal(fiefId, PurseKind.Treasury);
        rig.L.Seasonal(LandWorld.NextSeasonStart(rig.Day), rig.C);
        Assert.Equal(t1 + c.DuesMitesPerYear / 4, rig.Bal(fiefId, PurseKind.Treasury));
        // the grantee may build on the site, nobody else
        var reg = rig.L.Registry(); int t = c.Tiles[0];
        Assert.True(reg.May(rig.Hero, -1, LandAction.Build, t % RegionData.Size * 64 + 32, t / RegionData.Size * 64 + 32).Allowed);
        Assert.Equal("ld_may_charter", reg.May(EntityId.Make(IdKind.Person, 77), -1, LandAction.Build, t % RegionData.Size * 64 + 32, t / RegionData.Size * 64 + 32).Why);
    }

    [Fact]
    public void LDC1_ARealmGrant_PaysTheRealmsTreasury()
    {
        using var rig = LandRig.Make(crowns: 200);
        int N = RegionData.Size; var reg = rig.Region;
        int realm = reg.Fiefs[0].Realm; int seat = rig.L.SeatOf(GrantorKind.Realm, realm);
        Assert.True(seat >= 0);
        (int X, int Y)? at = null;
        for (int t = 1000; t < RegionData.Tiles && at == null; t += 13)
        {
            int x = t % N * 64 + 32, y = t / N * 64 + 32;
            if (LandRules.OpenGround(reg, t) && rig.L.CharterSite(GrantorKind.Realm, realm, x, y).Length >= rig.L.Rules.CharterSiteTiles && reg.NearestActiveWarM(x, y) >= rig.L.Rules.CharterWarM) at = (x, y);
        }
        Assert.NotNull(at);
        var tid = LandWorld.RealmTreasuryId(realm);
        Assert.True(rig.Led.TryFind(PurseKind.Treasury, tid, out _), "the realm's treasury purse is the taxes' one");
        long t0 = rig.Bal(tid, PurseKind.Treasury);
        var r = rig.L.Petition(rig.Hero, PurseKind.Person, GrantorKind.Realm, realm, seat, at.Value.X, at.Value.Y, rig.Day, rig.C);
        Assert.True(r.Ok, r.Key);
        Assert.Equal(t0 + rig.L.Rules.CharterFeeMites, rig.Bal(tid, PurseKind.Treasury));
        Assert.True(rig.Led.Audit().IsConserved);
    }

    [Fact]
    public void LDC2_FiveHouseholdsSettledBeforeTheDeadline_FulfilTheCharter_AndFourDoNot()
    {
        // five: fulfilled the day the fifth comes
        using (var rig = LandRig.Make(crowns: 200))
        {
            var (f, x, y) = Site(rig); int seat = rig.L.SeatOf(GrantorKind.Fief, f);
            rig.L.Petition(rig.Hero, PurseKind.Person, GrantorKind.Fief, f, seat, x, y, rig.Day, rig.C);
            var c = rig.L.Book.Charters.Single();
            var hhs = Settlers(rig, 5); Assert.Equal(5, hhs.Count);
            var hht = rig.W.Pop.Households;
            long coin0 = rig.Led.Balance(rig.W.Econ.HouseholdPurse[hhs[0]]).Mites;
            for (int i = 0; i < 5; i++)
            {
                var r = rig.L.Settle(rig.Hero, c.Serial, hhs[i], rig.Day + 10 + i, rig.C);
                Assert.True(r.Ok, r.Key);
                Assert.Equal(i < 4 ? CharterState.Granted : CharterState.Fulfilled, c.State);
            }
            Assert.Equal(rig.Day + 14, c.FulfilDay);
            // they came with their own: coin and goods stay theirs; they camp on the site, counted at the anchor
            Assert.Equal(coin0, rig.Led.Balance(rig.W.Econ.HouseholdPurse[hhs[0]]).Mites);
            foreach (int h in hhs) { Assert.Equal(c.Anchor, hht.Settlement[h]); Assert.Equal(-1, hht.Lot[h]); }
            Assert.Equal(5, rig.L.Settled(c));
            Assert.False(rig.L.CanSettle(hhs[0]));                                                    // once only
            rig.L.Daily(c.SettleByDay + 1, rig.C);
            Assert.Equal(CharterState.Fulfilled, c.State);                                           // stays fulfilled past the deadline
            output.WriteLine($"fulfilled on day {c.FulfilDay} (deadline {c.SettleByDay}) with {rig.L.Settled(c)} households at {rig.Region.Settlements[c.Anchor].Name}");
        }
        // four: lapses at the deadline (recorded only)
        using (var rig = LandRig.Make(crowns: 200))
        {
            var (f, x, y) = Site(rig); int seat = rig.L.SeatOf(GrantorKind.Fief, f);
            rig.L.Petition(rig.Hero, PurseKind.Person, GrantorKind.Fief, f, seat, x, y, rig.Day, rig.C);
            var c = rig.L.Book.Charters.Single();
            foreach (int h in Settlers(rig, 4)) Assert.True(rig.L.Settle(rig.Hero, c.Serial, h, rig.Day + 5, rig.C).Ok);
            rig.L.Daily(c.SettleByDay, rig.C);
            Assert.Equal(CharterState.Granted, c.State);
            rig.L.Daily(c.SettleByDay + 1, rig.C);
            Assert.Equal(CharterState.Lapsed, c.State);
            Assert.True(rig.L.Book.Deeds.ContainsKey(c.DeedId), "a lapsed charter is only recorded: the deed is not revoked in Phase 5");
            Assert.Contains(rig.Chron, e => e.TypeId == "charter_lapsed");
            // a fifth household after the deadline does not fulfil it
            Assert.False(rig.L.Settle(rig.Hero, c.Serial, Settlers(rig, 1)[0], c.SettleByDay + 2, rig.C).Ok);
        }
    }

    [Fact]
    public void LDC3_ASiteThatOverlapsAnExistingClaim_IsRefused()
    {
        using var rig = LandRig.Make(crowns: 200);
        var (f, x, y) = Site(rig); int seat = rig.L.SeatOf(GrantorKind.Fief, f);
        var site = rig.L.CharterSite(GrantorKind.Fief, f, x, y);
        // someone's claim inside the site (staked by another person, on wild land)
        var other = EntityId.Make(IdKind.Person, 4321);
        var st = rig.L.Stake(other, PurseKind.Person, site[^1] % RegionData.Size * 64 + 32, site[^1] / RegionData.Size * 64 + 32, false, rig.Day, rig.C);
        Assert.True(st.Ok, st.Key);
        long h0 = rig.Coin;
        var r = rig.L.Petition(rig.Hero, PurseKind.Person, GrantorKind.Fief, f, seat, x, y, rig.Day, rig.C);
        Assert.False(r.Ok); Assert.Equal("ld_charter_contested", r.Key);
        Assert.Equal(h0, rig.Coin); Assert.Empty(rig.L.Book.Charters);
        // and a claim cannot be staked inside a granted site
        rig.L.Pull(other, rig.L.Book.Claims.Single().Serial, rig.Day, rig.C);
        Assert.True(rig.L.Petition(rig.Hero, PurseKind.Person, GrantorKind.Fief, f, seat, x, y, rig.Day, rig.C).Ok);
        Assert.Equal("ld_claim_taken", rig.L.Stake(other, PurseKind.Person, x, y, false, rig.Day, rig.C).Key);
    }

    [Fact]
    public void LDC4_GrantAndFulfilment_AreChronicleEventsWithCauses()
    {
        using var rig = LandRig.Make(crowns: 200);
        var (f, x, y) = Site(rig); int seat = rig.L.SeatOf(GrantorKind.Fief, f);
        rig.L.Petition(rig.Hero, PurseKind.Person, GrantorKind.Fief, f, seat, x, y, rig.Day, rig.C);
        var c = rig.L.Book.Charters.Single();
        foreach (int h in Settlers(rig, 5)) rig.L.Settle(rig.Hero, c.Serial, h, rig.Day + 1, rig.C);
        var grant = rig.Chron.Single(e => e.TypeId == "charter_granted");
        var done = rig.Chron.Single(e => e.TypeId == "charter_fulfilled");
        Assert.Equal(c.GrantEvent, grant.Id);
        Assert.Contains(rig.Hero, grant.Subjects); Assert.Contains(rig.Region.Fiefs[f].Id, grant.Subjects);
        Assert.Contains(grant.Causes, k => k.Kind == CauseKind.Law);
        Assert.Contains(done.Causes, k => k.Kind == CauseKind.Law && k.EventId == grant.Id);        // fulfilment is caused by the grant
        Assert.Equal(5, rig.Chron.Count(e => e.TypeId == "charter_settled" && e.Causes.Any(k => k.EventId == grant.Id)));
        output.WriteLine($"{grant.TypeId}: {string.Join(", ", grant.Data.Select(kv => kv.Key + "=" + kv.Value))}");
    }
}
