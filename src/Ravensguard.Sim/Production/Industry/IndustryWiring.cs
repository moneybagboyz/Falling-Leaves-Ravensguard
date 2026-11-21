using System.Globalization;
using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Production.Industry;

/// <summary>
/// The industry runtime's place in the abstract tick (P7-IN1, overview 8.4): the works' day in Phase 2's daily business slice (H4, after the production slice), each works at the hour of its index modulo 24. A no-op in a world without an <see cref="IndustryWorld"/>
/// (only the home region has one) and free while no works exists.
/// </summary>
[AbstractSystem]
public sealed class IndustrySystem : AbstractSystem
{
    public IndustrySystem() : base("industry", TickPhase.H4DailySlice, SystemOrder.ProductionDaily + 20) { }

    /// <summary>The hour of the day the founders look at the trade (after the labour market's hour 1).</summary>
    public const int FoundersHour = 7;

    public override void Step(TickContext ctx)
    {
        var iw = ctx.World.Get<IndustryWorld>(); if (iw == null) return;
        bool founders = ctx.HourOfDay == FoundersHour && iw.NpcEnabled;
        if (iw.Book.Works.Count == 0 && !founders) return;
        var prev = iw.Chron;
        iw.Chron = RuleChron.Of(ctx.Outbox);
        try
        {
            if (founders) iw.NpcPass((int)ctx.Time.TotalDays, ctx.Time);                // P7-IN5: the households' weekly look at the trade
            if (iw.Book.Works.Count > 0) iw.Hour(ctx.Time);
        }
        finally { iw.Chron = prev; }
    }
}

/// <summary>Installs the industry runtime on the home region (the P7-IN1 hook in <see cref="WorldRuntime"/>): the world state's book (the "industry" section), the world in the sim's services, the slider from the difficulty.</summary>
public static class IndustryWiring
{
    public static IndustryWorld Attach(RegionWorld home, WorldState state, ContentDatabase content)
    {
        var iw = Make(state, content, home.Economy.Production);
        Hook(iw, home.Sim.Get<ConstructionWorld>(), home.Sim.Get<PowerWorld>(), () => home.Sim.Get<WoundLedger>() ?? state.Wounds, state, content);
        iw.Trade = home.Economy.Trade;                                                   // P7-IN4: the continent's caravans, for the works' own
        iw.IdleHands = (settlement, day) => home.Economy.Labour.IdleCount(settlement, day);   // P7-IN5: the founders look at the labour before they build
        home.Sim.Set(iw);
        return iw;
    }

    /// <summary>The industry world a desk command works on: the home region's when the runtime installed one, else one over the world state's own records (tests, a world without regions).</summary>
    public static IndustryWorld For(WorldState state, ContentDatabase content)
    {
        if (state.HomeRecords?.Sim.Get<IndustryWorld>() is { } iw) return iw;
        return Loose.GetValue(state, s =>
        {
            var w = Make(s, content, s.Production ?? throw new InvalidOperationException("industry needs the world's production"));
            Hook(w, s.HomeRecords?.Sim.Get<ConstructionWorld>(), s.HomeRecords?.Sim.Get<PowerWorld>(), () => s.Wounds, s, content);
            if (s.HomeRecords?.Economy.Trade is { } tr) w.Trade = tr;
            return w;
        });
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<WorldState, IndustryWorld> Loose = new();

    /// <summary>
    /// Wires the premises' side (P7-IN2): the construction book the tiers are recognised from, the power rules' nets and river-high days, and the wound book an accident goes through. Anything the world lacks stays null and the rule is off.
    /// </summary>
    public static void Hook(IndustryWorld iw, ConstructionWorld? cw, PowerWorld? power, Func<WoundLedger?> wounds, WorldState state, ContentDatabase content)
    {
        iw.Build = cw; iw.Power = power; iw.RiverHighDays = power?.RiverHighDays;
        iw.Injure = (person, heightMm, ev, sec) =>
        {
            var ledger = wounds(); if (ledger == null) return false;
            var id = iw.Prod.Pop.People.IdOf(person);
            FallWounds.Apply(content, ledger, id, heightMm, ev, sec, state.Seed);
            return ledger.Find(id.Value)?.Dead == true;
        };
    }

    private static IndustryWorld Make(WorldState state, ContentDatabase content, ProductionSystem prod)
    {
        var book = state.Industry ??= new IndustryBook();
        return new IndustryWorld(book, prod, content, state.Seed)
        {
            SliderPct = () => state.Difficulty.IndustryOutputPct,
            ContractList = () => state.Scale?.Contracts ?? [],                                                            // P7-IN4: a stage routed to a supply contract holds back its deliveries
            CarterOf = (settlement, day) => Ravensguard.Sim.Holdings.HoldingsDesk.CarterOf(state, settlement, day),     // and the gate's carting is paid to a carter of the place
        };
    }
}

/// <summary>
/// The hero's side of industries at the barrier (the hero desk calls it for <see cref="HeroCommandKind.Industry"/>, P7-IN1). The tag names the verb ("in:verb|..."): <c>found|chain|settlement</c> (the capital is the command's limit), <c>crew|stage|n</c>, <c>wage|stage|pm</c>,
/// <c>route|stage|next|gate|store</c>, <c>link|works|to|distance|carts|wagons</c>, <c>cash|works|mites</c>. Stages and works are named by the id of their first business, chains by content id, so every command is on the replay log and survives a content change.
/// </summary>
public static class IndustryDesk
{
    public const string Prefix = "in:";

    private static string Tag(params object[] parts) => Prefix + string.Join('|', parts.Select(p => Convert.ToString(p, CultureInfo.InvariantCulture)));
    private static HeroCommand Cmd(long sec, string tag, long limit = 0, int settlement = -1) => new() { Kind = HeroCommandKind.Industry, IssuedSec = sec, Tag = tag, Limit = limit, Settlement = settlement, Origin = CommandOrigin.Human };

    public static HeroCommand Found(string chain, int settlement, long capital, long sec) => Cmd(sec, Tag("found", chain, settlement), capital, settlement);
    public static HeroCommand Crew(EntityId stage, int target, long sec) => Cmd(sec, Tag("crew", stage.Value, target));
    public static HeroCommand Wage(EntityId stage, int pm, long sec) => Cmd(sec, Tag("wage", stage.Value, pm));
    public static HeroCommand Route(EntityId stage, string route, long sec) => Cmd(sec, Tag("route", stage.Value, route));
    public static HeroCommand Link(EntityId works, int to, int distanceM, int carts, int wagons, long sec) => Cmd(sec, Tag("link", works.Value, to, distanceM, carts, wagons));
    public static HeroCommand Cash(EntityId works, long mites, long sec) => Cmd(sec, Tag("cash", works.Value, mites));
    /// <summary>P7-IN4: the stage's caravan order: <paramref name="dest"/> "town" (a settlement index) or "gate" (a neighbouring region's id), "cart" or "ox", the wagons, the guards, whether it stands.</summary>
    public static HeroCommand Order(EntityId stage, string dest, int where, string vehicle, int wagons, int guards, bool standing, long sec) => Cmd(sec, Tag("order", stage.Value, dest, where, vehicle, wagons, guards, standing ? 1 : 0));
    /// <summary>P7-IN4: sends the stage's caravan now (the "Send a caravan" button); the quote the hero's notebook gives is the one the result reports.</summary>
    public static HeroCommand Send(EntityId stage, long sec) => Cmd(sec, Tag("send", stage.Value));
    /// <summary>P7-IN4: names the person (an id) who leads the stage's caravans; 0 lets the works hire the best free trader.</summary>
    public static HeroCommand Master(EntityId stage, ulong person, long sec) => Cmd(sec, Tag("master", stage.Value, person));
    /// <summary>P7-IN2: the stage's premises, the rectangle its tier is recognised from (inclusive region metres).</summary>
    public static HeroCommand Site(EntityId stage, int x0, int y0, int x1, int y1, long sec) => Cmd(sec, Tag("site", stage.Value, x0, y0, x1, y1));
    /// <summary>P7-IN2: the next tier's plan placed as ghosts beside the stage's premises, at (x, y) turned <paramref name="turns"/> quarter turns; commission a builder with the construction desk's <c>cn:commission</c> on the site the result names.</summary>
    public static HeroCommand Upgrade(EntityId stage, int x, int y, int turns, long sec) => Cmd(sec, Tag("upgrade", stage.Value, x, y, turns));

    /// <summary>
    /// The Upgrade button (industries 5.3): the next tier's plan (<c>blueprints/industry_tiers.jsonc</c>) is placed as ghosts at the given corner, the stage's site grows to take its footprint, and the tier changes when the last required piece is built (recognition).
    /// The player builds it, sets his people to it or commissions a builder through the construction desk on the site named in the result.
    /// </summary>
    private static OwnResult Upgrade(HeroContext ctx, IndustryWorld iw, ulong stage, int x, int y, int turns, long sec)
    {
        var st = ctx.State;
        var book = st.Industry; var found = book?.StageOf(stage); if (found == null) return OwnResult.No("in_no_such_stage");
        var ws = found.Value.Works.Stages[found.Value.Index];
        if (iw.NextUpgrade(ws) is not { } next) return OwnResult.No("in_top_tier");
        if (next.Plan.Length == 0) return OwnResult.No("in_no_plan");
        var cw = ConstructionWiring.For(st, ctx.Content);
        iw.Build ??= cw;
        cw.Designs = st.Designs ??= new DesignBook();
        var g = cw.Plans().Grid(next.Plan);
        if (g == null) return OwnResult.No("cn_unknown_plan", next.Plan);
        var turned = g.Transform(Math.Clamp(turns, 0, 3), false);
        int hh = ConstructionDesk.HouseholdOf(ctx);
        var pr = cw.PlacePlanGrid(ctx.Party.Hero, PurseKind.Person, hh, turned, next.Plan, x, y, sec);
        if (!pr.Ok) return new OwnResult(false, pr.Key, pr.Args);
        var res = iw.BeginUpgrade(stage, x, y, turned.Width, turned.Height, (long)ConstructionIds.SerialOf(pr.Site));
        return new OwnResult(true, "in_upgrade_placed", [next.Tier, pr.Ghosts.Count], 0, pr.Site);
    }

    private static HeroResult R(HeroCommand c, HeroResultCode code, HeroText text, long mites = 0) => new(c.Seq, c.Kind, code, text, mites, 0, c.IssuedSec);

    public static HeroResult Apply(HeroContext ctx, HeroCommand c)
    {
        if (!c.Tag.StartsWith(Prefix, StringComparison.Ordinal)) return R(c, HeroResultCode.Invalid, HeroText.Of("HERO_UNKNOWN_COMMAND"));
        var p = c.Tag[Prefix.Length..].Split('|');
        long N(int i) => i < p.Length && long.TryParse(p[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : long.MinValue;
        int I(int i) { long v = N(i); return v is >= int.MinValue and <= int.MaxValue ? (int)v : int.MinValue; }
        string S(int i) => i < p.Length ? p[i] : "";
        var st = ctx.State; var ow = OwnershipWiring.For(st, ctx.Content); var iw = IndustryWiring.For(st, ctx.Content);
        int day = (int)(c.IssuedSec / 86400);
        iw.Chron = RuleChron.Of(st.Chronicle, st.Ids);
        int hero = ow.HeroRow(); var purse = ow.HeroPurse;
        bool Mine(ulong business) => st.Industry?.Of(business) is { } w && w.Owner == hero;
        OwnResult? res = p[0] switch
        {
            "found" when p.Length >= 3 && c.Limit >= 0 && hero >= 0 => iw.Found(S(1), hero, I(2), purse, c.Limit, day),
            "crew" when p.Length >= 3 => Mine((ulong)N(1)) ? iw.SetCrew((ulong)N(1), I(2)) : OwnResult.No("in_not_yours"),
            "wage" when p.Length >= 3 => Mine((ulong)N(1)) ? iw.SetWageOffer((ulong)N(1), I(2)) : OwnResult.No("in_not_yours"),
            "route" when p.Length >= 3 && Enum.TryParse<Routing>(S(2), true, out var rt) => Mine((ulong)N(1)) ? iw.SetRoute((ulong)N(1), rt) : OwnResult.No("in_not_yours"),
            "link" when p.Length >= 6 => Mine((ulong)N(1)) ? iw.SetLink((ulong)N(1), I(2), I(3), I(4), I(5)) : OwnResult.No("in_not_yours"),
            "cash" when p.Length >= 3 && N(2) != 0 && N(2) != long.MinValue => Mine((ulong)N(1)) ? iw.Cash((ulong)N(1), purse, N(2)) : OwnResult.No("in_not_yours"),
            "site" when p.Length >= 6 => Mine((ulong)N(1)) ? iw.SetSite((ulong)N(1), I(2), I(3), I(4), I(5)) : OwnResult.No("in_not_yours"),
            "order" when p.Length >= 8 && (S(2) is "town" or "gate") && (S(4) is "cart" or "ox") => Mine((ulong)N(1))
                ? iw.SetCaravanOrder((ulong)N(1), S(2) == "town" ? DestKind.Town : DestKind.Neighbour, I(3), S(4) == "cart" ? (byte)1 : (byte)2, I(5), I(6), N(7) != 0) : OwnResult.No("in_not_yours"),
            "send" when p.Length >= 2 => Mine((ulong)N(1)) ? iw.Caravans.Send((ulong)N(1), day, GameTime.FromGameSeconds(c.IssuedSec), ctx.Party.Notebook) : OwnResult.No("in_not_yours"),
            "master" when p.Length >= 3 => Mine((ulong)N(1)) ? iw.SetCaravanMaster((ulong)N(1), N(2) <= 0 ? -1 : st.Population.People.IndexOf(new EntityId((ulong)N(2)))) : OwnResult.No("in_not_yours"),
            "upgrade" when p.Length >= 5 => Mine((ulong)N(1)) ? Upgrade(ctx, iw, (ulong)N(1), I(2), I(3), I(4), c.IssuedSec) : OwnResult.No("in_not_yours"),
            _ => null,
        };
        if (res == null) return R(c, HeroResultCode.Invalid, HeroText.Of("HERO_MAKES_NO_SENSE"));
        var code = res.Ok ? HeroResultCode.Ok : res.CannotAfford ? HeroResultCode.CannotAfford : HeroResultCode.Refused;
        return R(c, code, HeroText.Of(res.Key, res.Args), res.Mites);
    }
}
