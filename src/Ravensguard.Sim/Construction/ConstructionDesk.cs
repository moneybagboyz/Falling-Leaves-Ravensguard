using System.Globalization;
using Ravensguard.Content;
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
/// <item>P5-D contracts: <c>cn:commission|site|builder|crew</c> (hire a builder at his bid), <c>cn:take|contract</c> (take posted work as a job, by hand), <c>cn:drop|contract</c>.</item>
/// <item>P5-C, build mode: <c>cn:plan|...|culture|wealth</c> (a plan of the library at a culture and wealth, the player's designs included), <c>cn:undo</c>, <c>cn:removerect|x0|y0|x1|y1|z</c>,
/// <c>cn:deconrect|x0|y0|x1|y1|z</c>, <c>cn:roof|x|y|z|piece</c>, <c>cn:stairs|x|y|z|rot</c>, <c>cn:design|save|x0|y0|x1|y1|name64</c> (save as blueprint into the save's designs) and
/// <c>cn:design|import|id|text64</c> (a design from the player's mod brought into the save before it is placed; base64 texts, so no separator is ever inside).</item>
/// </list>
/// </summary>
public static class ConstructionDesk
{
    public const string Prefix = "cn:";

    private static string Tag(params object[] parts) => Prefix + string.Join('|', parts.Select(p => Convert.ToString(p, CultureInfo.InvariantCulture)));
    private static HeroCommand Cmd(long sec, string tag, long x = 0, long y = 0) => new() { Kind = HeroCommandKind.Construction, IssuedSec = sec, Tag = tag, X = x, Y = y, Origin = CommandOrigin.Human };

    public static HeroCommand Place(string piece, int x0, int y0, int x1, int y1, int z, string shape, int rot, long sec, ulong site = 0) => Cmd(sec, Tag("place", site, piece, x0, y0, x1, y1, z, shape, rot));
    public static HeroCommand Plan(string blueprint, int x, int y, int turns, bool mirror, long sec, int pileX = -1, int pileY = -1, string? culture = null, Wealth? wealth = null) =>
        culture == null && wealth == null ? Cmd(sec, Tag("plan", blueprint, x, y, turns, mirror ? 1 : 0, pileX, pileY))
        : Cmd(sec, Tag("plan", blueprint, x, y, turns, mirror ? 1 : 0, pileX, pileY, culture ?? "", wealth?.ToString().ToLowerInvariant() ?? ""));     // P5-C: the library's culture and wealth
    // ---- P5-C: build mode ----
    public static HeroCommand Undo(long sec) => Cmd(sec, Tag("undo"));
    public static HeroCommand RemoveRect(int x0, int y0, int x1, int y1, int z, long sec) => Cmd(sec, Tag("removerect", x0, y0, x1, y1, z));
    public static HeroCommand DeconstructRect(int x0, int y0, int x1, int y1, int z, long sec) => Cmd(sec, Tag("deconrect", x0, y0, x1, y1, z));
    public static HeroCommand Roof(int x, int y, int z, string piece, long sec) => Cmd(sec, Tag("roof", x, y, z, piece));
    public static HeroCommand Stairs(int x, int y, int z, int rot, long sec) => Cmd(sec, Tag("stairs", x, y, z, rot));
    public static HeroCommand SaveDesign(int x0, int y0, int x1, int y1, string name, long sec) => Cmd(sec, Tag("design", "save", x0, y0, x1, y1, B64(name)));
    public static HeroCommand ImportDesign(string id, string text, long sec) => Cmd(sec, Tag("design", "import", id, B64(text)));
    private static string B64(string s) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s));
    private static string UnB64(string s) { try { return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(s)); } catch (FormatException) { return ""; } }
    /// <summary>P7-LB1: shuts or opens the sluice built at a cell (<c>cn:sluice|x|y|z|shut</c>).</summary>
    public static HeroCommand Sluice(int x, int y, int z, bool shut, long sec) => Cmd(sec, Tag("sluice", x, y, z, shut ? 1 : 0));
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
    public static HeroCommand Commission(EntityId site, EntityId builder, int crew, long sec) => Cmd(sec, Tag("commission", ConstructionIds.SerialOf(site), builder.Value, crew));
    public static HeroCommand TakeContract(EntityId contract, long sec) => Cmd(sec, Tag("take", ConstructionIds.SerialOf(contract)));
    public static HeroCommand DropContract(EntityId contract, long sec) => Cmd(sec, Tag("drop", ConstructionIds.SerialOf(contract)));

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
        EntityId ContractId(int i) => ConstructionIds.Make(cw.RegionId, ConstructionIds.ContractSlot, (ulong)Math.Max(0, N(i)));
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
                // P5-C: through the plan library (the player's designs, the culture and wealth; P8-LM-3: the cellar under the plan is built too); the same cells as P5-B's PlacePlan for a content plan at its own wealth
                Wealth? wl = S(9) switch { "poor" => Wealth.Poor, "middling" => Wealth.Middling, "rich" => Wealth.Rich, _ => null };
                cw.Designs = st.Designs ??= new DesignBook();
                var g = cw.Plans().GridWithCellar(S(1), S(8).Length > 0 ? S(8) : null, wl);
                if (g == null) { res = ConstructionResult.No("cn_unknown_plan", S(1)); break; }
                var turned = g.Transform(I(4), N(5) != 0);
                // P7-LB1 (large-buildings 4.6): a powered plan is placed only where its wheel's water gives what it needs in autumn
                int needW = cw.Plans().Get(S(1))?.Entry.Def.PowerW ?? 0;
                if (needW > 0 && cw.PowerCheck is { } pc && pc(turned, I(2), I(3), needW) is { Ok: false } bad) { res = ConstructionResult.No(bad.Key, bad.HaveW, needW); break; }
                res = Placed(cw.PlacePlanGrid(hero, hk, hh, turned, S(1), I(2), I(3), sec, pile));
                break;
            }
            // ---- P5-C: build mode ----
            case "undo": res = cw.UndoLast(hero, hh, sec); break;
            case "removerect" when p.Length >= 6: res = cw.CancelRect(hero, hh, I(1), I(2), I(3), I(4), I(5), sec); break;
            case "deconrect" when p.Length >= 6: res = cw.DeconstructRect(hero, hk, hh, I(1), I(2), I(3), I(4), I(5), sec); break;
            case "roof" when p.Length >= 5: res = Placed(cw.RoofOver(hero, hk, hh, I(1), I(2), I(3), S(4), sec)); break;
            case "stairs" when p.Length >= 5: res = Placed(cw.PlaceStairs(hero, hk, hh, I(1), I(2), I(3), (byte)Math.Clamp(I(4), 0, 3), sec)); break;
            case "design" when p.Length >= 3:
            {
                var book = cw.Designs = st.Designs ??= new DesignBook();
                if (S(1) == "save" && p.Length >= 7)
                {
                    var d = cw.Capture(I(2), I(3), I(4), I(5), UnB64(S(6)));
                    if (d == null) { res = ConstructionResult.No("cn_design_nothing"); break; }
                    book.Put(d.Id, d.Text);
                    res = ConstructionResult.Yes(d.Warnings.Count > 0 ? "cn_design_saved_warn" : "cn_design_saved", d.Name, d.Id, d.Warnings.Count, d.Warnings.Count > 0 ? d.Warnings[0].Code : "");
                }
                else if (S(1) == "import" && p.Length >= 4)
                {
                    string id = S(2), text = UnB64(S(3));
                    if (!id.StartsWith("player.", StringComparison.Ordinal)) { res = ConstructionResult.No("cn_design_bad", id, "id"); break; }
                    if (book.Text(id) == text) { res = ConstructionResult.Yes("cn_design_imported", id); break; }
                    if (cw.Plans().ParseDesign(text, id, out var problem) == null) { res = ConstructionResult.No("cn_design_bad", id, problem.Length > 80 ? problem[..80] : problem); break; }
                    book.Put(id, text);
                    res = ConstructionResult.Yes("cn_design_imported", id);
                }
                else res = null;
                break;
            }
            case "sluice" when p.Length >= 5:                                   // P7-LB1: the machine panel's switch: a shut sluice stops the wheel
                res = cw.Power is { } pw && pw.SetSluice(I(1), I(2), I(3), N(4) != 0) ? ConstructionResult.Yes(N(4) != 0 ? "lb_sluice_shut_ok" : "lb_sluice_open_ok") : ConstructionResult.No("lb_no_sluice");
                break;
            case "remove" when p.Length >= 5: res = cw.Cancel(hero, hh, I(1), I(2), I(3), (GhostSlot)Math.Clamp(I(4), 0, 1), sec); break;
            case "decon" when p.Length >= 5: res = cw.MarkDeconstruct(hero, hk, hh, I(1), I(2), I(3), (GhostSlot)Math.Clamp(I(4), 0, 1), sec); break;
            case "priority" when p.Length >= 3: res = cw.SetPriority(GhostId(1), (byte)Math.Clamp(I(2), 0, 3), sec); break;
            case "work" when p.Length >= 7:
            {
                var tools = cw.ToolQualities(ctx.PackTools());
                // P6-B6 (holdings 3.8, HW-9): heavy work wears a body out and burns food; he eats at noon from his pack or the site's store, and a hungry or tired man stops
                long workUntil = N(3);
                var hb = st.Holdings;
                if (hb != null)
                {
                    var gh = cw.Book.Ghosts.GetValueOrDefault(GhostId(1));
                    var lim = Ravensguard.Sim.Holdings.HeavyWork.Begin(ctx, hb, Math.Max(N(2), cw.Book.HeroWorkedTo), workUntil, gh != null && cw.Site(gh.Site) is { } gsite ? cw.PileOf(gsite) : null);
                    if (lim.Why != null) { res = ConstructionResult.No(lim.Why); break; }
                    workUntil = lim.UntilSec;
                }
                long workedBefore = Math.Max(N(2), cw.Book.HeroWorkedTo);
                res = cw.HeroWork(hero, hk, hh, GhostId(1), N(2), workUntil, I(4), I(5), I(6), tools, (q, s) => Wear(ctx, q, s));
                if (hb != null && res.Ok) Ravensguard.Sim.Holdings.HeavyWork.End(hb, Math.Max(0, cw.Book.HeroWorkedTo - workedBefore));
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
            case "commission" when p.Length >= 4: res = cw.Commission(hero, hk, SiteId(1), new EntityId((ulong)N(2)), Math.Clamp(I(3), 1, 8), sec); break;       // P5-D
            case "take" when p.Length >= 2: res = cw.Take(hero, hk, ContractId(1), sec); break;
            case "drop" when p.Length >= 2: res = cw.Drop(hero, ContractId(1), sec); break;
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

    /// <summary>P5-C: the same for build mode's preview at a barrier (no desk context there).</summary>
    public static int HouseholdOf(Ravensguard.Sim.Save.WorldState st)
    {
        if (st.Hero is not { } h || st.Party is not { } party) return -1;
        var pt = st.Population.People;
        int row = Ravensguard.Sim.Succession.HeroPerson.RowOf(pt, h, party.Hero);
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
