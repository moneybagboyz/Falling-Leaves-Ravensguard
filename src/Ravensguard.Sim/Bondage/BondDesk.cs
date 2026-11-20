using System.Globalization;
using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Law;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Bondage;

/// <summary>
/// The hero's slaves and his own chains at the barrier (slavery 4.2, 4.4, 6), recorded and replayed like every hero command (tag "sl:"): <c>buy</c> a person standing in a pen, <c>sell</c> a bond to a buyer of the town he stands in
/// (<c>Settlement</c>), <c>free</c> (manumission, lawful anywhere), <c>treat|n</c> (0 Harsh, 1 Fair, 2 Kind), <c>chain|0/1</c>, <c>work|n</c> (a <see cref="BondWork"/>), <c>ransom</c> (buy another's slave free), and for an enslaved
/// hero <c>escape</c> (pick the chains, at night, per the lock-pick stream 0x455), <c>run</c> (walk away unchained) and <c>selfbuy</c> (his peculium reaches his last price). <c>here</c> tells the world which town he stands in: a
/// runaway who reaches a town whose law is none is free there; slaves he carries into such a town are the law's business (the daily pass).
/// </summary>
public static class BondDesk
{
    public const string Prefix = "sl:";

    private static HeroCommand C(string tag, long sec, EntityId person = default, int settlement = -1) => new() { Kind = HeroCommandKind.Bondage, IssuedSec = sec, Origin = CommandOrigin.Human, Person = person, Settlement = settlement, Tag = Prefix + tag };
    public static HeroCommand Buy(EntityId person, long sec) => C("buy", sec, person);
    public static HeroCommand Sell(EntityId person, int settlement, long sec) => C("sell", sec, person, settlement);
    public static HeroCommand Free(EntityId person, long sec) => C("free", sec, person);
    public static HeroCommand Treat(EntityId person, Treatment t, long sec) => C("treat|" + ((int)t).ToString(CultureInfo.InvariantCulture), sec, person);
    public static HeroCommand Chain(EntityId person, bool on, long sec) => C("chain|" + (on ? 1 : 0), sec, person);
    public static HeroCommand Work(EntityId person, BondWork w, long sec) => C("work|" + ((int)w).ToString(CultureInfo.InvariantCulture), sec, person);
    public static HeroCommand Ransom(EntityId person, long sec) => C("ransom", sec, person);
    public static HeroCommand Here(int settlement, long sec) => C("here", sec, default, settlement);
    public static HeroCommand Escape(long sec) => C("escape", sec);
    public static HeroCommand Run(long sec) => C("run", sec);
    public static HeroCommand SelfBuy(long sec) => C("selfbuy", sec);
    /// <summary>P6-E3: hand a spare weapon to a slave of the party (<paramref name="item"/> none: the first spare one), take it back, or a slave of the party who bolts (<c>flee|0</c>: before the fight, under the flee morale; <c>flee|1</c>: routed in it).</summary>
    public static HeroCommand Arm(EntityId person, long sec, EntityId item = default) => C("arm|" + item.Value.ToString(CultureInfo.InvariantCulture), sec, person);
    public static HeroCommand Disarm(EntityId person, long sec) => C("disarm", sec, person);
    /// <summary>A road fight ended: the party's slaves are steadied by a held field and shaken by a lost one.</summary>
    public static HeroCommand Fought(bool won, long sec) => C("fought|" + (won ? 1 : 0), sec);
    public static HeroCommand Flee(EntityId person, bool inFight, long sec) => C("flee|" + (inFight ? 1 : 0), sec, person);

    private static HeroResult R(HeroCommand c, HeroResultCode code, string key, long mites = 0, params object[] args) => new(c.Seq, c.Kind, code, HeroText.Of(key, args), mites, 0, c.IssuedSec);
    private static HeroResult Of(HeroCommand c, SlaveResult r) => R(c, r.Ok ? HeroResultCode.Ok : r.Key is "sl_cannot_afford" ? HeroResultCode.CannotAfford : HeroResultCode.Refused, r.Key, r.Mites);

    public static HeroResult Apply(HeroContext ctx, HeroCommand c)
    {
        if (!c.Tag.StartsWith(Prefix, StringComparison.Ordinal)) return R(c, HeroResultCode.Invalid, "HERO_UNKNOWN_COMMAND");
        var f = c.Tag[Prefix.Length..].Split('|');
        var st = ctx.State; var hero = ctx.Party.Hero; int day = (int)(c.IssuedSec / 86_400);
        var book = st.Bonds ??= new BondBook();
        try
        {
            var res = Dispatch(ctx, c, f, st, hero, day, book);
            if (res.Code == HeroResultCode.Ok) SlaveParty.Sync(book, ctx.Party.Squad, st.Population.People, st.Population.Households, hero, day);        // P6-E3: Follow work is a place in the squad
            return res;
        }
        finally { if (st.Bonds is { IsEmpty: true }) st.Bonds = null; }
    }

    private static HeroResult Dispatch(HeroContext ctx, HeroCommand c, string[] f, Save.WorldState st, EntityId hero, int day, BondBook book)
    {
        try
        {
            var bw = BondWiring.World(st, ctx.Content);
            switch (f[0])
            {
                case "buy": return Of(c, Slavery.Buy(bw, book, hero, c.Person, day, c.IssuedSec));
                case "sell": return Of(c, Slavery.Sell(bw, book, hero, c.Person, c.Settlement, day, c.IssuedSec));
                case "free": return Of(c, Slavery.Manumit(bw, book, hero, c.Person, day, c.IssuedSec, st.Law));
                case "ransom": return Of(c, Slavery.BuyFree(bw, book, hero, c.Person, day, c.IssuedSec, st.Law));
                case "treat" when f.Length > 1 && int.TryParse(f[1], out int t) && t is >= 0 and <= 2: return Of(c, Slavery.SetTreatment(book, hero, c.Person, (Treatment)t));
                case "chain" when f.Length > 1: return Of(c, Slavery.SetChain(book, hero, c.Person, f[1] == "1"));
                case "work" when f.Length > 1 && int.TryParse(f[1], out int w) && Enum.IsDefined((BondWork)w): return Of(c, Slavery.SetWork(book, hero, c.Person, (BondWork)w));
                case "arm": return Of(c, SlaveParty.Arm(ctx.Econ, ctx.Content, ctx.Party, book, hero, c.Person, f.Length > 1 && ulong.TryParse(f[1], out var iv) ? new EntityId(iv) : EntityId.None));
                case "disarm": return Of(c, SlaveParty.Disarm(ctx.Econ, ctx.Content, book, hero, c.Person));
                case "flee" when f.Length > 1:
                {
                    var crimes = st.Law ?? new CrimeBook();
                    bool ran = SlaveParty.Flee(bw, book, ctx.Party.Squad, crimes, hero, c.Person, f[1] == "1", day, c.IssuedSec);
                    if (!crimes.IsEmpty) st.Law = crimes;
                    return ran ? R(c, HeroResultCode.Ok, "sl_party_ran") : R(c, HeroResultCode.Refused, "sl_not_in_party");
                }
                case "fought" when f.Length > 1: SlaveParty.AfterFight(book, ctx.Party.Squad, f[1] == "1"); return R(c, HeroResultCode.Ok, "sl_here");
                case "here": return Here(bw, book, hero, c, day, st.Law);
                case "selfbuy": return Of(c, Slavery.SelfPurchase(bw, book, hero, day, c.IssuedSec, st.Law));
                case "run": return Run(bw, st, book, hero, c, day);
                case "escape": return Escape(ctx, bw, st, book, hero, c, day);
                default: return R(c, HeroResultCode.Invalid, "HERO_MAKES_NO_SENSE");
            }
        }
        finally { if (st.Bonds is { IsEmpty: true }) st.Bonds = null; }
    }

    /// <summary>He stands in a town: his slaves are there (the law looks at them), and a runaway of his own who has reached a town whose law is none is free.</summary>
    private static HeroResult Here(BondWorld bw, BondBook book, EntityId hero, HeroCommand c, int day, CrimeBook? crimes)
    {
        if (c.Settlement < 0) return R(c, HeroResultCode.Invalid, "HERO_MAKES_NO_SENSE");
        foreach (var b in book.Bonds.Values.Where(b => b.Owner == hero.Value && !b.InPen)) b.Place = c.Settlement;
        if (book.Of(hero) is { OnRun: true } && BondDaily.Arrive(bw, book, hero, c.Settlement, day, c.IssuedSec, crimes)) return R(c, HeroResultCode.Ok, "sl_free_here");
        return R(c, HeroResultCode.Ok, "sl_here");
    }

    private static HeroResult Run(BondWorld bw, Save.WorldState st, BondBook book, EntityId hero, HeroCommand c, int day)
    {
        if (book.Of(hero) is not { InPen: false, OnRun: false } b) return R(c, HeroResultCode.Refused, "sl_not_bonded");
        if (b.Chain != Restraint.None) return R(c, HeroResultCode.Refused, "sl_chained_fast");
        var crimes = st.Law ?? new CrimeBook();
        BondDaily.Flee(bw, book, crimes, b, day, c.IssuedSec);
        if (!crimes.IsEmpty) st.Law = crimes;
        return R(c, HeroResultCode.Ok, "sl_ran");
    }

    /// <summary>The enslaved hero picks his chains (4.4): <see cref="Slavery.Escape"/> with the hero's stealth and lock-picking.</summary>
    private static HeroResult Escape(HeroContext ctx, BondWorld bw, Save.WorldState st, BondBook book, EntityId hero, HeroCommand c, int day)
    {
        var (stealth, _, lockp, _, _) = bw.W.SkillsOf(hero);
        var crimes = st.Law ?? new CrimeBook();
        var res = Slavery.Escape(bw, book, crimes, st.Captivity?.Rules ?? AftermathRules.Default, hero, c.IssuedSec, stealth, lockp);
        if (!crimes.IsEmpty) st.Law = crimes;
        _ = ctx; _ = day;
        return Of(c, res);
    }
}
