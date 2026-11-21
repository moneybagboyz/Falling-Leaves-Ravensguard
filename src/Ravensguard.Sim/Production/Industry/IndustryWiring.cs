using System.Globalization;
using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
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

    public override void Step(TickContext ctx)
    {
        var iw = ctx.World.Get<IndustryWorld>(); if (iw == null || iw.Book.Works.Count == 0) return;
        var prev = iw.Chron;
        iw.Chron = RuleChron.Of(ctx.Outbox);
        try { iw.Hour(ctx.Time); }
        finally { iw.Chron = prev; }
    }
}

/// <summary>Installs the industry runtime on the home region (the P7-IN1 hook in <see cref="WorldRuntime"/>): the world state's book (the "industry" section), the world in the sim's services, the slider from the difficulty.</summary>
public static class IndustryWiring
{
    public static IndustryWorld Attach(RegionWorld home, WorldState state, ContentDatabase content)
    {
        var iw = Make(state, content, home.Economy.Production);
        home.Sim.Set(iw);
        return iw;
    }

    /// <summary>The industry world a desk command works on: the home region's when the runtime installed one, else one over the world state's own records (tests, a world without regions).</summary>
    public static IndustryWorld For(WorldState state, ContentDatabase content)
    {
        if (state.HomeRecords?.Sim.Get<IndustryWorld>() is { } iw) return iw;
        return Loose.GetValue(state, s => Make(s, content, s.Production ?? throw new InvalidOperationException("industry needs the world's production")));
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<WorldState, IndustryWorld> Loose = new();

    private static IndustryWorld Make(WorldState state, ContentDatabase content, ProductionSystem prod)
    {
        var book = state.Industry ??= new IndustryBook();
        return new IndustryWorld(book, prod, content, state.Seed) { SliderPct = () => state.Difficulty.IndustryOutputPct };
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
            _ => null,
        };
        if (res == null) return R(c, HeroResultCode.Invalid, HeroText.Of("HERO_MAKES_NO_SENSE"));
        var code = res.Ok ? HeroResultCode.Ok : res.CannotAfford ? HeroResultCode.CannotAfford : HeroResultCode.Refused;
        return R(c, code, HeroText.Of(res.Key, res.Args), res.Mites);
    }
}
