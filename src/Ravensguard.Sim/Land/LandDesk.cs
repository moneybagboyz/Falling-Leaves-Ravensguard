using System.Globalization;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Land;

/// <summary>A row of the hero's deeds (the Property list): what, where, on what terms, what it owes and when a lease ends.</summary>
public sealed record LandDeedRow(EntityId Id, HoldingKind Kind, Tenure Tenure, string Where, int AreaM2, long DuesMites, int LeaseEndDay, int ArrearsSeasons, long OwedMites, bool InDebt, int NoticeDay, long AskMites, long ValueMites);
/// <summary>A row of the hero's claims.</summary>
public sealed record LandClaimRow(int Serial, int Tiles, int StakeDay, int DeadlineDay, bool Licensed, bool Squatting, ClaimState State, int Improvements, string Fief, int BannerX, int BannerY);
/// <summary>A row of the hero's founding charters.</summary>
public sealed record LandCharterRow(int Serial, string Grantor, int Settled, int Needed, int SettleByDay, CharterState State, long DuesMitesPerYear, int ServiceMen, int ServiceDays, string Anchor);
/// <summary>Whether the hero could stake a claim where he stands, on whose land, and what the licence would cost.</summary>
public sealed record LandClaimCheck(bool Ok, string Why, int Fief, string FiefName, long LicenceMites, int Tiles);

/// <summary>
/// What the deed and lot screens show, built at a barrier (immutable to the main thread): the settlement's lots for sale, its leasable fields and free stalls, the hero's deeds, claims and charters,
/// and whether a claim could be staked where the hero stands.
/// </summary>
public sealed record LandView(int Day, int Settlement, string Place, IReadOnlyList<Listing> Listings, IReadOnlyList<(int Parcel, int Fields, long RentMites)> Fields, IReadOnlyList<int> FreeStalls,
    IReadOnlyList<LandDeedRow> Deeds, IReadOnlyList<LandClaimRow> Claims, IReadOnlyList<LandCharterRow> Charters, LandClaimCheck Here, long CoinMites, long StallSeasonMites, long StallDayMites);

/// <summary>
/// The hero's side of land at the barrier (the hero desk calls it for <see cref="HeroCommandKind.Land"/>, P5-A2): buy a plot, offer a deed, lease a field, a house or a stall, give a lease up, stake a
/// claim, buy its licence, pull the banner, petition for a founding charter and settle a household on it. The tag says which ("ld:verb|..."), so every one is on the replay log, saved with the pending
/// commands and applied in sequence order on the tick worker.
/// </summary>
public static class LandDesk
{
    public const string Prefix = "ld:";

    private static string Tag(params object[] parts) => Prefix + string.Join('|', parts.Select(p => Convert.ToString(p, CultureInfo.InvariantCulture)));
    private static HeroCommand Cmd(long sec, string tag, long limit = 0, int settlement = -1) => new() { Kind = HeroCommandKind.Land, IssuedSec = sec, Tag = tag, Limit = limit, Settlement = settlement, Origin = CommandOrigin.Human };

    public static HeroCommand Buy(Listing l, long sec) => Cmd(sec, Tag("buy", l.Settlement, l.Lot, l.Rect.X, l.Rect.Y, l.Rect.W, l.Rect.H), l.PriceMites, l.Settlement);
    public static HeroCommand Offer(EntityId deed, long askMites, long sec) => Cmd(sec, Tag("offer", deed.Value, askMites));
    public static HeroCommand LeaseField(int parcel, long sec) => Cmd(sec, Tag("lease", parcel));
    public static HeroCommand LeaseHouse(int settlement, int lot, long sec) => Cmd(sec, Tag("house", settlement, lot), 0, settlement);
    public static HeroCommand GiveUp(EntityId deed, bool renew, long sec) => Cmd(sec, Tag("giveup", deed.Value, renew ? 1 : 0));
    public static HeroCommand Stall(int settlement, int stall, bool fairDay, long sec) => Cmd(sec, Tag("stall", settlement, stall, fairDay ? 1 : 0), 0, settlement);
    public static HeroCommand Stake(int xM, int yM, bool licence, long sec) => Cmd(sec, Tag("claim", xM, yM, licence ? 1 : 0));
    public static HeroCommand Licence(int claim, long sec) => Cmd(sec, Tag("licence", claim));
    public static HeroCommand Pull(int claim, long sec) => Cmd(sec, Tag("pull", claim));
    public static HeroCommand Petition(GrantorKind k, int grantor, int atSettlement, int xM, int yM, long sec) => Cmd(sec, Tag("petition", (int)k, grantor, atSettlement, xM, yM), 0, atSettlement);
    public static HeroCommand Settle(int charter, int household, long sec) => Cmd(sec, Tag("settle", charter, household));

    private static HeroResult R(HeroCommand c, HeroResultCode code, HeroText text, long mites = 0) => new(c.Seq, c.Kind, code, text, mites, 0, c.IssuedSec);

    public static HeroResult Apply(HeroContext ctx, HeroCommand c)
    {
        if (!c.Tag.StartsWith(Prefix, StringComparison.Ordinal)) return R(c, HeroResultCode.Invalid, HeroText.Of("HERO_UNKNOWN_COMMAND"));
        var p = c.Tag[Prefix.Length..].Split('|');
        long N(int i) => i < p.Length && long.TryParse(p[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : long.MinValue;
        int I(int i) { long v = N(i); return v is >= int.MinValue and <= int.MaxValue ? (int)v : int.MinValue; }
        var lw = LandWiring.For(ctx.State, ctx.Content);
        var hero = ctx.Party.Hero; const PurseKind hk = PurseKind.Person;
        int day = (int)(c.IssuedSec / 86400);
        var st = ctx.State;
        EntityId Chron(GameTime when, string type, IReadOnlyList<EntityId> subjects, IReadOnlyList<Cause> causes, params (string Key, string Value)[] data)
        {
            var id = st.Ids.Allocate(IdKind.Event);
            var d = new Dictionary<string, string>(StringComparer.Ordinal); foreach (var (k, v) in data) d[k] = v;
            st.Chronicle.Add(new ChronicleEntry(id, when, type, subjects, causes, d));
            return id;
        }
        LandResult? res = p[0] switch
        {
            "buy" when p.Length >= 7 && I(1) >= 0 => lw.Buy(hero, hk, I(1), I(2), new RectM(I(3), I(4), I(5), I(6)), c.Limit, day, Chron),
            "offer" when p.Length >= 3 && N(2) >= 0 => lw.Offer(hero, new EntityId((ulong)N(1)), N(2), day),
            "lease" when p.Length >= 2 => lw.LeaseParcel(hero, hk, I(1), day, Chron),
            "house" when p.Length >= 3 => lw.LeaseHouse(hero, hk, I(1), I(2), day, Chron),
            "giveup" when p.Length >= 3 => lw.GiveUp(hero, new EntityId((ulong)N(1)), N(2) != 0),
            "stall" when p.Length >= 4 => lw.LeaseStall(hero, hk, I(1), I(2), N(3) != 0, day, Chron),
            "claim" when p.Length >= 4 => HasBanner(ctx, lw) ? lw.Stake(hero, hk, I(1), I(2), N(3) != 0, day, Chron) : LandResult.No("ld_claim_no_banner"),
            "licence" when p.Length >= 2 => lw.BuyLicence(hero, I(1), day, Chron),
            "pull" when p.Length >= 2 => lw.Pull(hero, I(1), day, Chron),
            "petition" when p.Length >= 6 && N(1) is 0 or 1 => lw.Petition(hero, hk, (GrantorKind)N(1), I(2), I(3), I(4), I(5), day, Chron),
            "settle" when p.Length >= 3 => lw.Settle(hero, I(1), I(2), day, Chron),
            _ => null,
        };
        if (res == null) return R(c, HeroResultCode.Invalid, HeroText.Of("HERO_MAKES_NO_SENSE"));
        var code = res.Ok ? HeroResultCode.Ok : res.PriceMoved ? HeroResultCode.PriceMoved : res.CannotAfford ? HeroResultCode.CannotAfford : HeroResultCode.Refused;
        return R(c, code, HeroText.Of(res.Key, res.Args), res.Mites);
    }

    private static bool HasBanner(HeroContext ctx, LandWorld lw)
    {
        string b = lw.Rules.ClaimBannerItem; if (b.Length == 0) return true;
        var e = ctx.Econ;
        foreach (int row in ctx.Party.Carried.Rows(e)) if (e.Items.At(row).Shape == b) return true;
        return e.Goods.TryIdOf(b, out ushort g) && ctx.Pack.Qty(g) > 0;
    }

    // ------------------------------------------------------------------ the view

    /// <summary>The land as the hero sees it from where he stands (region metres; <paramref name="settlement"/> -1 out of any place). Built at a barrier; reads only.</summary>
    public static LandView View(LandWorld lw, EntityId hero, int settlement, int xM, int yM, int day)
    {
        var r = lw.Region; var book = lw.Book;
        bool inPlace = (uint)settlement < (uint)r.Settlements.Count;
        var listings = inPlace ? lw.Listings(settlement) : [];
        var fields = inPlace ? lw.LeasableParcels(settlement).Select(pp => (pp, r.Parcels[pp].FieldCount, LandRules.FieldLeasePerSeason(lw.Rules, r.Parcels[pp].FieldCount))).Take(6).ToList() : [];
        var stalls = new List<int>();
        if (inPlace)
            for (int k = 0; k < lw.StallCount(settlement); k++)
                if (!book.Deeds.TryGetValue(LandIds.Of(HoldingKind.Stall, lw.RegionId, LandIds.StallSubject(settlement, k)), out var sd) || sd.LeaseEndDay <= day) stalls.Add(k);
        var deeds = new List<LandDeedRow>();
        foreach (var d in book.Deeds.Values)
        {
            if (d.Holder != hero) continue;
            string where = d.Kind switch
            {
                HoldingKind.Lot or HoldingKind.Building or HoldingKind.Stall when (uint)d.Settlement < (uint)r.Settlements.Count => r.Settlements[d.Settlement].Name,
                HoldingKind.Parcel when (uint)d.Subject < (uint)r.Parcels.Count => r.Settlements[r.Parcels[(int)d.Subject].Settlement].Name,
                HoldingKind.Claim => d.Lord >= 0 && d.Lord < r.Fiefs.Count ? r.Fiefs[d.Lord].Name : "",
                HoldingKind.FoundingCharter => book.CharterOf((int)d.Subject) is { } ch ? lw.GrantorName(ch.GrantorKind, ch.Grantor) : "",
                _ => "",
            };
            int area = d.Kind switch
            {
                HoldingKind.Parcel when (uint)d.Subject < (uint)r.Parcels.Count => r.Parcels[(int)d.Subject].FieldCount * RegionData.TileM * RegionData.TileM,
                HoldingKind.Claim => (book.ClaimOf((int)d.Subject)?.Tiles.Length ?? 0) * RegionData.TileM * RegionData.TileM,
                HoldingKind.FoundingCharter => (book.CharterOf((int)d.Subject)?.Tiles.Length ?? 0) * RegionData.TileM * RegionData.TileM,
                _ => d.Rect.W * d.Rect.H,
            };
            deeds.Add(new LandDeedRow(d.Id, d.Kind, d.Tenure, where, area, d.DuesMitesPerSeason, d.LeaseEndDay, d.ArrearsSeasons, d.OwedMites, d.InDebt, d.NoticeDay, d.AskMites, lw.ValueOf(d)));
        }
        var claims = book.Claims.Where(c => c.Claimant == hero && c.State is ClaimState.Open or ClaimState.Deeded)
            .Select(c => new LandClaimRow(c.Serial, c.Tiles.Length, c.StakeDay, c.StakeDay + lw.Rules.ClaimImproveWithinDays, c.Licensed, c.Squatting, c.State, c.Improvements,
                c.Fief >= 0 && c.Fief < r.Fiefs.Count ? r.Fiefs[c.Fief].Name : "", c.BannerX, c.BannerY)).ToList();
        var charters = book.Charters.Where(c => c.Grantee == hero)
            .Select(c => new LandCharterRow(c.Serial, lw.GrantorName(c.GrantorKind, c.Grantor), lw.Settled(c), c.MinHouseholds, c.SettleByDay, c.State, c.DuesMitesPerYear, c.ServiceMen, c.ServiceDays,
                c.Anchor >= 0 ? r.Settlements[c.Anchor].Name : "")).ToList();
        var (ok, fief, why) = lw.ClaimSite(xM, yM);
        int tiles = ok ? lw.Grow(LandRules.TileAt(xM, yM), [], lw.Rules.ClaimStartTiles, xM, yM, t => lw.ClaimTileOk(t, fief, null)).Length : 0;
        if (ok && tiles < lw.Rules.ClaimStartTiles) { ok = false; why = "ld_claim_small"; }
        var here = new LandClaimCheck(ok, why, fief, fief >= 0 && fief < r.Fiefs.Count ? r.Fiefs[fief].Name : "", fief >= 0 ? LandRules.Licence(lw.Rules, lw.Rules.ClaimStartTiles) : 0, tiles);
        long coin = lw.Econ.Ledger.TryFind(PurseKind.Person, hero, out var hp) ? lw.Econ.Ledger.Balance(hp).Mites : 0;
        return new LandView(day, settlement, inPlace ? r.Settlements[settlement].Name : "", listings, fields, stalls, deeds, claims, charters, here, coin, lw.Rules.StallRentMitesPerSeason, lw.Rules.StallFairDayMites);
    }
}
