using System.Globalization;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.Prospecting;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Trade;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Production;

/// <summary>
/// The hero's side of businesses and prospecting at the barrier (the hero desk calls it for <see cref="HeroCommandKind.Business"/> and <see cref="HeroCommandKind.Prospect"/>, P5-F). The tag says
/// which ("bz:verb|..." or "px:verb|..."), goods by their content id (never an index: a pending command survives a content change), so every one is on the replay log, saved with the pending
/// commands and applied in sequence order on the tick worker.
/// </summary>
public static class BusinessDesk
{
    public const string Prefix = "bz:", ProspectPrefix = "px:";

    private static string Tag(string prefix, params object[] parts) => prefix + string.Join('|', parts.Select(p => Convert.ToString(p, CultureInfo.InvariantCulture)));
    private static HeroCommand Cmd(long sec, string tag, long limit = 0, int settlement = -1) => new() { Kind = tag.StartsWith(ProspectPrefix, StringComparison.Ordinal) ? HeroCommandKind.Prospect : HeroCommandKind.Business, IssuedSec = sec, Tag = tag, Limit = limit, Settlement = settlement, Origin = CommandOrigin.Human };
    private static string B(params object[] p) => Tag(Prefix, p);
    private static string P(params object[] p) => Tag(ProspectPrefix, p);

    // ---- businesses
    public static HeroCommand Found(string kind, int settlement, int lot, int structure, long capital, long sec) => Cmd(sec, B("found", kind, settlement, lot, structure), capital, settlement);
    public static HeroCommand FoundSite(string kind, int x0, int y0, int x1, int y1, long capital, long sec) => Cmd(sec, B("site", kind, x0, y0, x1, y1), capital);
    public static HeroCommand Buy(EntityId biz, long limit, long sec) => Cmd(sec, B("buy", biz.Value), limit);
    public static HeroCommand BuyShare(EntityId biz, int pm, long limit, long sec) => Cmd(sec, B("share", biz.Value, pm), limit);
    public static HeroCommand SellShare(EntityId biz, long sec) => Cmd(sec, B("sellshare", biz.Value));
    public static HeroCommand Cash(EntityId biz, long mites, long sec) => Cmd(sec, B("cash", biz.Value, mites));
    public static HeroCommand Hire(EntityId biz, int person, int wage, long sec) => Cmd(sec, B("hire", biz.Value, person, wage));
    public static HeroCommand Fire(EntityId biz, int person, long sec) => Cmd(sec, B("fire", biz.Value, person));
    public static HeroCommand Steward(EntityId biz, int person, long sec) => Cmd(sec, B("steward", biz.Value, person));
    public static HeroCommand Unsteward(EntityId biz, long sec) => Cmd(sec, B("unsteward", biz.Value));
    public static HeroCommand Orders(EntityId biz, string spec, long sec) => Cmd(sec, B("orders", biz.Value, spec));
    public static HeroCommand Supply(EntityId biz, string good, long qty, long sec) => Cmd(sec, B("supply", biz.Value, good, qty));
    public static HeroCommand Take(EntityId biz, string good, long qty, long sec) => Cmd(sec, B("take", biz.Value, good, qty));
    public static HeroCommand Work(EntityId biz, int station, string recipe, long from, long until) => Cmd(from, B("work", biz.Value, station, recipe, from, until));
    public static HeroCommand Stall(int settlement, int stall, int business, long sec) => Cmd(sec, B("stall", settlement, stall, business), 0, settlement);
    public static HeroCommand Stock(long subject, string good, long qty, long sec) => Cmd(sec, B("stock", subject, good, qty));
    public static HeroCommand Unstock(long subject, string good, long qty, long sec) => Cmd(sec, B("unstock", subject, good, qty));
    public static HeroCommand Mind(long subject, long from, long until) => Cmd(from, B("mind", subject, from, until));
    public static HeroCommand Ask(long subject, int pm, long sec) => Cmd(sec, B("ask", subject, pm));
    public static HeroCommand Minder(long subject, int person, long sec) => Cmd(sec, B("minder", subject, person));
    public static HeroCommand At(int settlement, long sec) => Cmd(sec, B("at", settlement));
    public static HeroCommand Pump(EntityId biz, long sec) => Cmd(sec, B("pump", biz.Value));
    public static HeroCommand FromChest(long mites, long sec) => Cmd(sec, B("chest", mites));
    // ---- economies of scale (P7-ES1): an appointed foreman (the 50-mite day wage, a place in the span of control) and a manufactory licence from the town's lord
    public static HeroCommand Foreman(EntityId biz, int person, long sec) => Cmd(sec, B("foreman", biz.Value, person));
    public static HeroCommand Unforeman(EntityId biz, int person, long sec) => Cmd(sec, B("unforeman", biz.Value, person));
    public static HeroCommand Manufactory(EntityId biz, long sec) => Cmd(sec, B("manufactory", biz.Value));
    // ---- economies of scale, part 2 (P7-ES2): a supply contract between two businesses (one of them the hero's), ended by either party; a station coupled to a machine's cell
    public static HeroCommand Contract(EntityId seller, EntityId buyer, string good, long qty, int everyDays, int seasons, bool carterIsSeller, long sec) => Cmd(sec, B("contract", seller.Value, buyer.Value, good, qty, everyDays, seasons, carterIsSeller ? 1 : 0));
    public static HeroCommand EndContract(int id, long sec) => Cmd(sec, B("endcontract", id));
    public static HeroCommand Couple(EntityId biz, int station, int x, int y, int z, long sec) => Cmd(sec, B("couple", biz.Value, station, x, y, z));
    public static HeroCommand Uncouple(EntityId biz, int station, long sec) => Cmd(sec, B("uncouple", biz.Value, station));
    // ---- prospecting
    public static HeroCommand Walk(int tile, long sec) => Cmd(sec, P("walk", tile));
    public static HeroCommand Start(ProspectMethod m, int tile, long sec) => Cmd(sec, P("start", (int)m, tile));
    public static HeroCommand Cancel(long sec) => Cmd(sec, P("cancel"));
    public static HeroCommand HireProspector(int person, ProspectMethod m, int tile, long sec) => Cmd(sec, P("hire", person, (int)m, tile));
    public static HeroCommand Write(int tile, long sec) => Cmd(sec, P("write", tile));
    public static HeroCommand Read(EntityId item, long sec) => Cmd(sec, P("read", item.Value));
    public static HeroCommand Licence(int tile, long sec) => Cmd(sec, P("licence", tile));

    private static HeroResult R(HeroCommand c, HeroResultCode code, HeroText text, long mites = 0) => new(c.Seq, c.Kind, code, text, mites, 0, c.IssuedSec);

    public static HeroResult Apply(HeroContext ctx, HeroCommand c)
    {
        bool px = c.Tag.StartsWith(ProspectPrefix, StringComparison.Ordinal);
        if (!px && !c.Tag.StartsWith(Prefix, StringComparison.Ordinal)) return R(c, HeroResultCode.Invalid, HeroText.Of("HERO_UNKNOWN_COMMAND"));
        var p = c.Tag[(px ? ProspectPrefix.Length : Prefix.Length)..].Split('|');
        long N(int i) => i < p.Length && long.TryParse(p[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : long.MinValue;
        int I(int i) { long v = N(i); return v is >= int.MinValue and <= int.MaxValue ? (int)v : int.MinValue; }
        EntityId Id(int i) => i < p.Length && ulong.TryParse(p[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong v) ? new EntityId(v) : EntityId.None;
        string S(int i) => i < p.Length ? p[i] : "";
        var ow = OwnershipWiring.For(ctx.State, ctx.Content);
        var e = ctx.Econ; int day = (int)(c.IssuedSec / 86400);
        ow.Today = Math.Max(ow.Today, day);
        var st = ctx.State;
        ow.Chron ??= (when, type, subjects, causes, data) =>
        {
            var id = st.Ids.Allocate(IdKind.Event);
            var d = new Dictionary<string, string>(StringComparer.Ordinal); foreach (var (k, v) in data) d[k] = v;
            st.Chronicle.Add(new ChronicleEntry(id, when, type, subjects, causes, d));
            return id;
        };
        bool Good(int i, out ushort g) => e.Goods.TryIdOf(S(i), out g);
        OwnResult? res;
        if (px)
        {
            res = p[0] switch
            {
                "walk" when p.Length >= 2 => ow.Walk(I(1), c.IssuedSec) >= 0 ? OwnResult.Yes("") : null,
                "start" when p.Length >= 3 && N(1) is >= 0 and < 8 => ow.StartProspect((ProspectMethod)N(1), I(2), ctx.PackTools(), c.IssuedSec),
                "cancel" => ow.CancelProspect(),
                "hire" when p.Length >= 4 && N(2) is >= 0 and < 8 => ow.HireProspector(I(1), (ProspectMethod)N(2), I(3), c.IssuedSec),
                "write" when p.Length >= 2 => ow.WriteReport(I(1), c.IssuedSec),
                "read" when p.Length >= 2 => ReadOwn(ctx, ow, Id(1)),
                "licence" when p.Length >= 2 => ow.PetitionLicence(I(1), day),
                _ => null,
            };
        }
        else
        {
            res = p[0] switch
            {
                "found" when p.Length >= 5 && N(2) >= 0 => ow.Found(S(1), I(2), I(3), I(4), c.Limit, day, MayUseBuilding(ctx, ow)),
                "site" when p.Length >= 6 => ow.FoundExtraction(S(1), I(2), I(3), I(4), I(5), c.Limit, day, MayUse(ctx, ow)),
                "buy" when p.Length >= 2 => ow.Buy(Id(1), c.Limit, day),
                "share" when p.Length >= 3 => ow.BuyShare(Id(1), I(2), c.Limit, day),
                "sellshare" when p.Length >= 2 => ow.SellShare(Id(1), day),
                "cash" when p.Length >= 3 && N(2) != long.MinValue && N(2) != 0 => ow.Cash(Id(1), N(2)),
                "hire" when p.Length >= 4 => ow.Hire(Id(1), I(2), I(3), day),
                "fire" when p.Length >= 3 => ow.Fire(Id(1), I(2)),
                "steward" when p.Length >= 3 => ow.HireSteward(Id(1), I(2), day),
                "unsteward" when p.Length >= 2 => ow.DismissSteward(Id(1)),
                "orders" when p.Length >= 3 => ow.Orders(Id(1), S(2)),
                "supply" when p.Length >= 4 && N(3) > 0 && Good(2, out var g1) => ow.Supply(Id(1), ctx.Pack, g1, N(3)),
                "take" when p.Length >= 4 && N(3) > 0 && Good(2, out var g2) => ow.Take(Id(1), ctx.Pack, g2, N(3)),
                "work" when p.Length >= 6 && N(4) >= 0 && N(5) > N(4) => ow.Work(Id(1), I(2), S(3), N(4), N(5)),
                "stall" when p.Length >= 4 => ow.OpenStall(I(1), I(2), I(3)),
                "stock" when p.Length >= 4 && N(3) > 0 && Good(2, out var g3) => ow.StallStock(N(1), ctx.Pack, g3, N(3)),
                "unstock" when p.Length >= 4 && N(3) > 0 && Good(2, out var g4) => ow.StallTake(N(1), ctx.Pack, g4, N(3)),
                "mind" when p.Length >= 4 && N(3) > N(2) => ow.Mind(N(1), N(2), N(3)),
                "ask" when p.Length >= 3 => ow.StallAsk(N(1), I(2)),
                "minder" when p.Length >= 3 => ow.StallMinder(N(1), I(2)),
                "at" when p.Length >= 2 && N(1) >= -2 => At(ow, I(1)),
                "pump" when p.Length >= 2 => ow.Pump(Id(1)),
                "chest" when p.Length >= 2 && N(1) > 0 => ow.FromChest(N(1)),
                "foreman" when p.Length >= 3 => ScaleOf(ctx, ow, Id(1), (sw, b) => sw.Appoint(b, I(2))),
                "unforeman" when p.Length >= 3 => ScaleOf(ctx, ow, Id(1), (sw, b) => sw.Dismiss(b, I(2))),
                "manufactory" when p.Length >= 2 => ScaleOf(ctx, ow, Id(1), (sw, b) => { sw.Chron = RuleChron.Of(st.Chronicle, st.Ids); return sw.Petition(b, ctx.Party.Hero, ow.HeroPurse, day); }),
                "contract" when p.Length >= 8 && N(4) > 0 && Good(3, out var g5) => ContractOf(ctx, ow, Id(1), Id(2), (sw, sell, buy) => sw.Offer(sell, buy, g5, N(4), I(5), I(6), N(7) != 0, day)),
                "endcontract" when p.Length >= 2 => ContractEnd(ctx, ow, I(1), day),
                "couple" when p.Length >= 6 => ScaleOf(ctx, ow, Id(1), (sw, b) => sw.Couple(b, I(2), I(3), I(4), I(5))),
                "uncouple" when p.Length >= 3 => ScaleOf(ctx, ow, Id(1), (sw, b) => sw.Uncouple(b, I(2))),
                _ => null,
            };
        }
        if (res == null) return R(c, HeroResultCode.Invalid, HeroText.Of("HERO_MAKES_NO_SENSE"));
        if (res.Key.Length == 0) return R(c, HeroResultCode.Ok, HeroText.Empty);
        var code = res.Ok ? HeroResultCode.Ok : res.PriceMoved ? HeroResultCode.PriceMoved : res.CannotAfford ? HeroResultCode.CannotAfford : HeroResultCode.Refused;
        return R(c, code, HeroText.Of(res.Key, res.Args), res.Mites);
    }

    /// <summary>A scale command on one of the hero's own businesses (P7-ES1).</summary>
    private static OwnResult ScaleOf(HeroContext ctx, OwnershipWorld ow, EntityId biz, Func<ScaleWorld, Business, OwnResult> act)
    {
        if (ow.BusinessOf(biz) is not { } b || !ow.IsHeroOwner(b)) return OwnResult.No("bz_not_yours");
        return act(ScaleWiring.For(ctx.State, ctx.Content), b);
    }

    /// <summary>A supply contract between two open businesses, one of them the hero's (P7-ES2).</summary>
    private static OwnResult ContractOf(HeroContext ctx, OwnershipWorld ow, EntityId seller, EntityId buyer, Func<ScaleWorld, Business, Business, OwnResult> act)
    {
        if (ow.BusinessOf(seller) is not { } s || ow.BusinessOf(buyer) is not { } b) return OwnResult.No("sc_contract_unknown");
        if (!ow.IsHeroOwner(s) && !ow.IsHeroOwner(b)) return OwnResult.No("sc_contract_not_party");
        return act(ScaleWiring.For(ctx.State, ctx.Content), s, b);
    }

    private static OwnResult ContractEnd(HeroContext ctx, OwnershipWorld ow, int id, int day)
    {
        var sw = ScaleWiring.For(ctx.State, ctx.Content);
        if (sw.Book.Contract(id) is not { } c) return OwnResult.No("sc_contract_unknown");
        if ((ow.BusinessOf(new EntityId(c.Seller)) is not { } s || !ow.IsHeroOwner(s)) && (ow.BusinessOf(new EntityId(c.Buyer)) is not { } b || !ow.IsHeroOwner(b))) return OwnResult.No("sc_contract_not_party");
        return sw.End(id, day);
    }

    private static OwnResult At(OwnershipWorld ow, int settlement) { ow.Book.HeroAt = settlement; return OwnResult.Yes(""); }

    /// <summary>A report the hero holds (carried or in his pack) is read; one he does not hold cannot be.</summary>
    private static OwnResult ReadOwn(HeroContext ctx, OwnershipWorld ow, EntityId item)
    {
        if (!ctx.Econ.Items.IsLive(item) || ctx.Econ.Items.Get(item).Owner != ctx.Party.Hero) return OwnResult.No("px_not_a_report");
        return ow.ReadReport(item, ctx.Party.Hero);
    }

    /// <summary>The surface right at a point (land 4.1): the hero may build there (his deed, a lease that allows it, a claim).</summary>
    private static Func<int, int, bool>? MayUse(HeroContext ctx, OwnershipWorld ow)
    {
        if (ow.Land == null) return null;
        var reg = ow.Land.Registry(); int hh = ow.HeroHousehold;
        return (x, y) => reg.May(ctx.Party.Hero, hh, LandAction.Build, x, y).Allowed;
    }

    /// <summary>A building the hero may use (land 4.1): he may build there or store goods there (his deed, a lease of the house, a claim).</summary>
    private static Func<int, int, bool>? MayUseBuilding(HeroContext ctx, OwnershipWorld ow)
    {
        if (ow.Land == null) return null;
        var reg = ow.Land.Registry(); int hh = ow.HeroHousehold;
        return (x, y) => reg.May(ctx.Party.Hero, hh, LandAction.Build, x, y).Allowed || reg.May(ctx.Party.Hero, hh, LandAction.Store, x, y).Allowed;
    }

    public static int TileAt(long xM, long yM) => (int)Math.Clamp(yM / RegionData.TileM, 0, RegionData.Size - 1) * RegionData.Size + (int)Math.Clamp(xM / RegionData.TileM, 0, RegionData.Size - 1);
}
