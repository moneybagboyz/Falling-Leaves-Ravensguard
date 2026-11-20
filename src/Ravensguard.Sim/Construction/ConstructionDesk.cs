using System.Globalization;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Construction;

/// <summary>
/// The hero's side of construction at the barrier (the hero desk calls it for <see cref="HeroCommandKind.Construction"/>, P5-B). The tag names the verb ("cn:verb|..."), so every one is on the
/// replay log, saved with the pending commands and applied in sequence order on the tick worker. Build mode (P5-C) and the dev console issue these; the sim API is
/// <see cref="ConstructionWorld"/>. Positions are region metres; a site or ghost is named by its serial (the low bits of its id).
/// <list type="bullet">
/// <item><c>cn:place|site|piece|x0|y0|x1|y1|z|shape|rot</c>: free placement, shape <c>one</c>, <c>line</c>, <c>rect</c> (filled) or <c>outline</c>; site 0 = the owner's near site or a new one.</item>
/// <item><c>cn:plan|blueprint|x|y|turns|mirror|pileX|pileY</c>: a plan from the library committed in one command (pile -1: the default).</item>
/// <item><c>cn:remove|x|y|z|slot</c>, <c>cn:decon|x|y|z|slot</c>, <c>cn:priority|ghost|p</c>.</item>
/// <item><c>cn:work|ghost|from|until|x|y|z</c>: the hero's own hands on a ghost over game seconds [from, until] (tools from his pack).</item>
/// <item><c>cn:deliver|site|good|qty|x|y</c>: unload from the pack into the pile within 3 m.</item>
/// <item><c>cn:haul|site|srcOwner|sx|sy|good|qty</c>: post haul jobs from a store the hero owns elsewhere.</item>
/// <item><c>cn:hire|site|count|skilled</c>, <c>cn:crew|site|person</c> (a follower), <c>cn:dismiss|site|person</c>, <c>cn:pause|site|1|0</c>.</item>
/// </list>
/// </summary>
public static class ConstructionDesk
{
    public const string Prefix = "cn:";

    private static string Tag(params object[] parts) => Prefix + string.Join('|', parts.Select(p => Convert.ToString(p, CultureInfo.InvariantCulture)));
    private static HeroCommand Cmd(long sec, string tag, long x = 0, long y = 0) => new() { Kind = HeroCommandKind.Construction, IssuedSec = sec, Tag = tag, X = x, Y = y, Origin = CommandOrigin.Human };

    public static HeroCommand Place(string piece, int x0, int y0, int x1, int y1, int z, string shape, int rot, long sec, ulong site = 0) => Cmd(sec, Tag("place", site, piece, x0, y0, x1, y1, z, shape, rot));
    public static HeroCommand Plan(string blueprint, int x, int y, int turns, bool mirror, long sec, int pileX = -1, int pileY = -1) => Cmd(sec, Tag("plan", blueprint, x, y, turns, mirror ? 1 : 0, pileX, pileY));
    public static HeroCommand Remove(int x, int y, int z, GhostSlot slot, long sec) => Cmd(sec, Tag("remove", x, y, z, (int)slot));
    public static HeroCommand Deconstruct(int x, int y, int z, GhostSlot slot, long sec) => Cmd(sec, Tag("decon", x, y, z, (int)slot));
    public static HeroCommand Priority(EntityId ghost, int p, long sec) => Cmd(sec, Tag("priority", ConstructionIds.SerialOf(ghost), p));
    public static HeroCommand Work(EntityId ghost, long fromSec, long untilSec, int x, int y, int z) => Cmd(untilSec, Tag("work", ConstructionIds.SerialOf(ghost), fromSec, untilSec, x, y, z));
    public static HeroCommand Deliver(EntityId site, string good, long qty, int x, int y, long sec) => Cmd(sec, Tag("deliver", ConstructionIds.SerialOf(site), good, qty, x, y));
    public static HeroCommand Haul(EntityId site, EntityId srcOwner, int sx, int sy, string good, long qty, long sec) => Cmd(sec, Tag("haul", ConstructionIds.SerialOf(site), srcOwner.Value, sx, sy, good, qty));
    public static HeroCommand Hire(EntityId site, int count, bool skilled, long sec) => Cmd(sec, Tag("hire", ConstructionIds.SerialOf(site), count, skilled ? 1 : 0));
    public static HeroCommand Crew(EntityId site, EntityId person, long sec) => Cmd(sec, Tag("crew", ConstructionIds.SerialOf(site), person.Value));
    public static HeroCommand Dismiss(EntityId site, EntityId person, long sec) => Cmd(sec, Tag("dismiss", ConstructionIds.SerialOf(site), person.Value));
    public static HeroCommand Pause(EntityId site, bool pause, long sec) => Cmd(sec, Tag("pause", ConstructionIds.SerialOf(site), pause ? 1 : 0));

    private static HeroResult R(HeroCommand c, HeroResultCode code, HeroText text, long mites = 0) => new(c.Seq, c.Kind, code, text, mites, 0, c.IssuedSec);

    /// <summary>The cells a placement brush covers (spec 4.1): one cell, a line (the longer axis), a filled rectangle or its outline; at most 4,096.</summary>
    public static List<(int X, int Y)> Brush(int x0, int y0, int x1, int y1, string shape)
    {
        var cells = new List<(int, int)>();
        int ax = Math.Min(x0, x1), bx = Math.Max(x0, x1), ay = Math.Min(y0, y1), by = Math.Max(y0, y1);
        if ((long)(bx - ax + 1) * (by - ay + 1) > 4096) return cells;
        switch (shape)
        {
            case "line":
                if (bx - ax >= by - ay) for (int x = ax; x <= bx; x++) cells.Add((x, y0));
                else for (int y = ay; y <= by; y++) cells.Add((x0, y));
                break;
            case "rect":
                for (int y = ay; y <= by; y++) for (int x = ax; x <= bx; x++) cells.Add((x, y));
                break;
            case "outline":
                for (int y = ay; y <= by; y++) for (int x = ax; x <= bx; x++) if (x == ax || x == bx || y == ay || y == by) cells.Add((x, y));
                break;
            default: cells.Add((x0, y0)); break;
        }
        return cells;
    }

    public static HeroResult Apply(HeroContext ctx, HeroCommand c)
    {
        if (!c.Tag.StartsWith(Prefix, StringComparison.Ordinal)) return R(c, HeroResultCode.Invalid, HeroText.Of("HERO_UNKNOWN_COMMAND"));
        var p = c.Tag[Prefix.Length..].Split('|');
        long N(int i) => i < p.Length && long.TryParse(p[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : long.MinValue;
        int I(int i) { long v = N(i); return v is >= int.MinValue and <= int.MaxValue ? (int)v : int.MinValue; }
        string S(int i) => i < p.Length ? p[i] : "";
        var cw = ConstructionWiring.For(ctx.State, ctx.Content);
        var st = ctx.State;
        cw.Chronicle = (when, type, subjects, causes, data) =>
        {
            var id = st.Ids.Allocate(IdKind.Event);
            var d = new Dictionary<string, string>(StringComparer.Ordinal); foreach (var (k, v) in data) d[k] = v;
            st.Chronicle.Add(new ChronicleEntry(id, when, type, subjects, causes, d));
            return id;
        };
        var hero = ctx.Party.Hero; const PurseKind hk = PurseKind.Person;
        int hh = HouseholdOf(ctx);
        long sec = c.IssuedSec;
        EntityId SiteId(int i) => ConstructionIds.Make(cw.RegionId, ConstructionIds.SiteSlot, (ulong)Math.Max(0, N(i)));
        EntityId GhostId(int i) => ConstructionIds.Make(cw.RegionId, ConstructionIds.GhostSlotId, (ulong)Math.Max(0, N(i)));
        ConstructionResult? res;
        switch (p[0])
        {
            case "place" when p.Length >= 10:
            {
                var cells = Brush(I(3), I(4), I(5), I(6), S(8)).Select(xy => new PlaceCell(xy.X, xy.Y, I(7), S(2), (byte)Math.Clamp(I(9), 0, 3))).ToList();
                var pr = cw.Place(hero, hk, hh, cells, sec, N(1) > 0 ? SiteId(1) : default);
                res = Placed(pr);
                break;
            }
            case "plan" when p.Length >= 6:
            {
                (int, int)? pile = p.Length >= 8 && I(6) >= 0 ? (I(6), I(7)) : null;
                res = Placed(cw.PlacePlan(hero, hk, hh, S(1), I(2), I(3), I(4), N(5) != 0, sec, pile));
                break;
            }
            case "remove" when p.Length >= 5: res = cw.Cancel(hero, hh, I(1), I(2), I(3), (GhostSlot)Math.Clamp(I(4), 0, 1), sec); break;
            case "decon" when p.Length >= 5: res = cw.MarkDeconstruct(hero, hk, hh, I(1), I(2), I(3), (GhostSlot)Math.Clamp(I(4), 0, 1), sec); break;
            case "priority" when p.Length >= 3: res = cw.SetPriority(GhostId(1), (byte)Math.Clamp(I(2), 0, 3), sec); break;
            case "work" when p.Length >= 7:
            {
                var tools = cw.ToolQualities(ctx.PackTools());
                res = cw.HeroWork(hero, hk, hh, GhostId(1), N(2), N(3), I(4), I(5), I(6), tools, (q, s) => Wear(ctx, q, s));
                break;
            }
            case "deliver" when p.Length >= 6:
                res = ctx.Econ.Goods.TryIdOf(S(2), out var dg) ? cw.Deliver(ctx.Pack, SiteId(1), dg, Math.Max(0, N(3)), I(4), I(5), sec) : ConstructionResult.No("cn_unknown_good", S(2));
                break;
            case "haul" when p.Length >= 7:
            {
                var src = new EntityId((ulong)N(2));
                if (!OwnedBy(ctx, src)) { res = ConstructionResult.No("cn_not_your_store"); break; }
                res = ctx.Econ.Goods.TryIdOf(S(5), out var hg) ? cw.PostHaul(SiteId(1), src, I(3), I(4), hg, Math.Max(0, N(6)), sec) : ConstructionResult.No("cn_unknown_good", S(5));
                break;
            }
            case "hire" when p.Length >= 4: res = cw.Hire(hero, hh, SiteId(1), Math.Clamp(I(2), 1, 8), N(3) != 0, sec); break;
            case "crew" when p.Length >= 3:
            {
                var who = new EntityId((ulong)N(2));
                var f = ctx.Party.Squad.Members.FirstOrDefault(m => m.Person == who);
                res = f == null ? ConstructionResult.No("cn_not_a_follower") : cw.AddWorker(SiteId(1), who, f.PersonIndex, CrewClass.Follower, 0, sec);
                break;
            }
            case "dismiss" when p.Length >= 3: res = cw.Dismiss(hero, SiteId(1), new EntityId((ulong)N(2)), sec); break;
            case "pause" when p.Length >= 3: res = cw.SetPaused(hero, hk, hh, SiteId(1), N(2) != 0, sec); break;
            default: res = null; break;
        }
        if (res == null) return R(c, HeroResultCode.Invalid, HeroText.Of("HERO_MAKES_NO_SENSE"));
        return R(c, res.Ok ? HeroResultCode.Ok : HeroResultCode.Refused, HeroText.Of(res.Key, res.Args), res.Mites);
    }

    private static ConstructionResult Placed(PlaceResult pr) => new(pr.Ok, pr.Key, pr.Args, 0, pr.Site);

    /// <summary>The hero's household (his person row's), for the land rule; -1 when he has none.</summary>
    public static int HouseholdOf(HeroContext ctx)
    {
        if (ctx.Hero is not { } h) return -1;
        var pt = ctx.State.Population.People;
        int row = Ravensguard.Sim.Succession.HeroPerson.RowOf(pt, h, ctx.Party.Hero);
        return row >= 0 ? pt.Household[row] : -1;
    }

    /// <summary>Stores the hero may send goods from: his pack, his household's store, a site pile of his.</summary>
    private static bool OwnedBy(HeroContext ctx, EntityId store)
    {
        if (store == ctx.Party.Hero) return true;
        int hh = HouseholdOf(ctx);
        if (hh >= 0 && ctx.State.Population.Households.IdOf(hh) == store) return true;
        return ctx.State.Construction?.Sites.Values.Any(s => s.Pile == store && s.Owner == ctx.Party.Hero) == true;
    }

    /// <summary>The hero's tool for a quality wears by the attended seconds (items 4.3): the first pack row whose shape wears for that quality.</summary>
    private static void Wear(HeroContext ctx, string quality, long seconds)
    {
        var e = ctx.Econ;
        foreach (int row in ctx.Party.Carried.Rows(e).ToList())
        {
            if (e.Items.At(row).Id == EntityId.None || ctx.ToolWear.MilliPerHour(e.Items.At(row).Shape, quality) <= 0) continue;
            if (ctx.ToolWear.Use(e.Items, row, quality, seconds, ctx.Pack, ctx.MaterialCategory))
                foreach (var k in ctx.Party.Carried.Equip.Keys.Where(k => !e.Items.IsLive(k)).ToList()) ctx.Party.Carried.Equip.Remove(k);
            break;
        }
    }
}
