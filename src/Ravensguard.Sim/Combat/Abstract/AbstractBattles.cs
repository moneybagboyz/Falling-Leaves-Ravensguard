using System.Runtime.CompilerServices;
using Ravensguard.Content;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Morale;
using Ravensguard.Sim.Combat.Watch;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Combat.Abstract;

/// <summary>
/// The abstract tier's fight resolver (overview 3, watch-mode Notes; abstract-battles-v1 4.1): behind <see cref="IAbstractFights"/>, a fight outside every bubble is an
/// <see cref="AbstractBattle"/> on the setup's kits, with the wound tables of its content. With no tables in the content (a test fixture without the generated rows) it is the
/// step-5 stub. The battle fights on copies of the people's wound books (<see cref="BookOf"/>); its <see cref="BattleCommand"/>s carry the new books and fates, applied at the barrier.
/// </summary>
public sealed class AbstractBattles : IAbstractFights
{
    public static readonly AbstractBattles Default = new();

    /// <summary>The people's existing wound books (a lookup into the world's <see cref="WoundLedger"/>), copied by the battle; null: everyone starts unhurt.</summary>
    public Func<ulong, WoundBook?>? BookOf { get; init; }
    /// <summary>The place's name for the report ("the road from Oakford").</summary>
    public string Place { get; init; } = "";

    public AbstractFightReport Resolve(FightSetup setup, FightContent content)
    {
        var tables = WoundTables.For(content.Db);
        if (tables == null) return AbstractFightStub.Instance.Resolve(setup, content);
        var b = new AbstractBattle(content, tables, InputOf(setup, BookOf, Place));
        var rep = b.Run();
        var outBy = new Dictionary<int, int>();
        foreach (var s in b.Soldiers) { int t = s.Kit.Team; outBy[t] = outBy.GetValueOrDefault(t) + (s.Status == SoldierStatus.Active && !s.Routing ? 0 : 1); }
        int odds = Math.Max(0, RiskEstimate.OddsOf(setup.Build(content, null), rep.Winner >= 0 ? b.Team[rep.Winner] : b.Team[0]));
        return new AbstractFightReport(setup.FightId, setup.Kind, rep.Winner >= 0 ? b.Team[rep.Winner] : -1, odds, outBy, Stub: false, setup.Cause) { Battle = rep, Commands = b.Commands };
    }

    /// <summary>The battle's input from a fight setup (its kits, seed, id, start, cause, sides, ground and weather; a rider is a kit on a horse of the setup, none in v1's setups).</summary>
    public static BattleInput InputOf(FightSetup s, Func<ulong, WoundBook?>? bookOf = null, string place = "") => new(s.Kits, s.Seed, s.FightId, s.GameSec, s.Cause)
    {
        Terrain = s.Terrain, Weather = s.Weather, SideKinds = s.SideKinds, Place = place, BookOf = bookOf,
        Night = s.GameSec / 3600 % 24 is >= 21 or < 5,
    };
}

/// <summary>
/// The world's service for battles nobody watches (abstract-battles-v1 5): the fight content and wound tables of the world's content database, built once, and the people's wound
/// books. Set on the home region's world (<c>WorldRuntime</c>); a system that finds none keeps its older rule (a caravan's ambush odds), so a world without it plays as before.
/// </summary>
public sealed class AbstractBattleService
{
    private static readonly ConditionalWeakTable<ContentDatabase, FightContent> Fights = new();
    public ContentDatabase Db { get; }
    private FightContent? _fight;

    public AbstractBattleService(ContentDatabase db) { Db = db; }

    /// <summary>The fight content (tables built on first use, shared by every service of the database).</summary>
    public FightContent Fight => _fight ??= Fights.GetValue(Db, d => new FightContent(new CombatTables(d), d));
    public WoundTables? Tables => WoundTables.For(Db);
    public bool Ready => Db.All<WoundTableDef>().Any();
}
