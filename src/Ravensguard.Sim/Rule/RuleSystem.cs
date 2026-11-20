using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Rule;

/// <summary>What the rule's tick reads that lives outside the sim world: the law, the land, the standing book and the hero (installed by <see cref="RuleWiring"/>, not saved).</summary>
public sealed class RuleLinks
{
    public Func<SkimCrime, EntityId>? Crime { get; set; }
    public Func<EntityId, int, int> StandingWithRealm { get; set; } = (_, _) => 0;
    public Func<EntityId> Hero { get; set; } = () => EntityId.None;
    public Func<int, int, int, bool>? GuardsEnforce { get; set; }
    public Func<int, string> WarCause { get; set; } = _ => "";
}

/// <summary>
/// The rule's place in the abstract tick (rule-and-income 4.10): every day at hour 0 the fiefs' holders are compared with the people (relief, wardship, escheat), grievances fall due on the households and a finished plough or
/// harvest window's labour service is settled; on a season's first day, spread over its hours, the receipts are rolled (hour 1), wardships, dues and loans settled (5), councils, charters and squatters judged (6), the officials
/// paid (12) and, after the collection rounds are done, the skimming seen, the officials audited and the levies taken (23). The hearth tax's own hand-off to an official is the <see cref="Ravensguard.Sim.Taxes.TaxSystem.Collected"/>
/// hook. A no-op in a world without a <see cref="RuleBook"/>. Integer only; the draws are streams 0x540, 0x661, 0x662 and 0x663.
/// </summary>
[AbstractSystem]
public sealed class RuleSystem : AbstractSystem
{
    public RuleSystem() : base("rule", TickPhase.H5DailyGlobals | TickPhase.H6Seasonal, SystemOrder.S4Taxes + 20) { }

    public override void Step(TickContext ctx)
    {
        var book = ctx.World.Get<RuleBook>(); if (book == null) return;
        var c = RuleWiring.Context(ctx.World, book, RuleChron.Of(ctx.Outbox));
        int day = (int)ctx.Day;
        if (ctx.Phase == TickPhase.H5DailyGlobals) { if (ctx.HourOfDay == 0) RuleEngine.Daily(c, day); }
        else if (ctx.Phase == TickPhase.H6Seasonal && ctx.IsSeasonStartDay) RuleEngine.SeasonStep(c, day, ctx.HourOfDay);
    }
}

/// <summary>The rule's passes, callable by a test with a hand-built world and by <see cref="RuleSystem"/> on the tick.</summary>
public static class RuleEngine
{
    /// <summary>The day's pass (hour 0): succession and its dues, grievances on the households, a window's labour service. Runs once for a day number.</summary>
    public static void Daily(RuleCtx c, int day)
    {
        var b = c.B; if (b.LastDay == day) return;
        RuleOfficials.Reconcile(c, day);
        RuleDues.Succession(c, day);
        RuleDues.ApplyGrievances(c, day);
        if (day % TimeConst.DaysPerSeason == 0 && day > 0 && b.LastLabourDay != day)
        {
            var finished = GameTime.FromGameHours((long)(day - 1) * 24).Season;                          // the season that just ended
            if (finished is Core.Season.Spring or Core.Season.Autumn) foreach (var f in b.Fiefs.Values) RuleDues.SettleLabour(c, f.Fief, TimeConst.DaysPerSeason, day - 1);
            b.LastLabourDay = day;
        }
        b.LastDay = day;
    }

    /// <summary>The season's passes by hour of its first day (see <see cref="RuleSystem"/>).</summary>
    public static void SeasonStep(RuleCtx c, int day, int hour)
    {
        switch (hour)
        {
            case 1: RuleDues.RollReceipts(c); RuleSeeding.FillVacancies(c, day); break;
            case 5: RuleDues.Wardships(c, day); RuleDues.Settle(c, day); RuleLoans.Season(c, day); break;
            case 6: RuleCouncil.NpcCouncils(c, day); RuleLand.Charters(c, day); RuleLand.Squatters(c, day); break;
            case 12: RuleOfficials.PayWages(c); break;
            case 23: RuleOfficials.Seen(c, day); RuleOfficials.Audit(c, day); RuleCouncil.CollectLevies(c, day); c.B.LastSeasonDay = day; break;
        }
    }
}
