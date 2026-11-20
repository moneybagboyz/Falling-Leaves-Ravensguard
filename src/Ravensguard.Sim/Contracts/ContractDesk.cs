using System.Globalization;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Contracts;

/// <summary>
/// The hero's acts on the contract board, at the barrier (contracts-and-rumours-v2 4.2 to 4.5), recorded and replayed like every hero command: the tag "ct:" says which. Take, put down, report a carried thing,
/// hand over goods to a delivery's store, pin a contract to the map (a soft marker the player chose), enter the tournament, bid on a building contract with his own crew, speak ill of a person.
/// </summary>
public static class ContractDesk
{
    public const string Prefix = "ct:";
    private static string N(long v) => v.ToString(CultureInfo.InvariantCulture);
    private static HeroCommand C(string tag, long sec, int settlement = -1, ushort good = ItemTable.NoGood, long qty = 0) => new() { Kind = HeroCommandKind.Contract, IssuedSec = sec, Origin = CommandOrigin.Human, Settlement = settlement, Tag = Prefix + tag, Good = good, Qty = qty };

    public static HeroCommand Take(int id, long sec) => C("take|" + N(id), sec);
    public static HeroCommand Abandon(int id, long sec) => C("abandon|" + N(id), sec);
    public static HeroCommand Report(int id, long sec) => C("report|" + N(id), sec);
    /// <summary>Hands over up to <paramref name="grams"/> of the delivery's good from the pack to the store the contract names.</summary>
    public static HeroCommand Deliver(int id, long grams, long sec) => C("deliver|" + N(id), sec, qty: grams);
    public static HeroCommand Pin(int id, bool on, long sec) => C("pin|" + N(id) + "|" + (on ? "1" : "0"), sec);
    public static HeroCommand Enter(int tournament, long sec) => C("enter|" + N(tournament), sec);
    /// <summary>The hero bids on a building contract with his own hands: the materials he would bring (mites) and the hours the work is.</summary>
    public static HeroCommand Bid(int id, long materialsMites, long hours, long sec) => C("bid|" + N(id) + "|" + N(materialsMites) + "|" + N(hours), sec);
    public static HeroCommand SpeakIll(EntityId victim, EntityId listener, string kind, long sec) => C("slander|" + N((long)victim.Value) + "|" + N((long)listener.Value) + "|" + kind, sec);
    /// <summary>The hero answers a lie told about him in court (defamation).</summary>
    public static HeroCommand Answer(EntityId rumour, long sec) => C("answer|" + N((long)rumour.Value), sec);

    private static HeroResult R(HeroCommand c, HeroResultCode code, string key, params object[] args) => new(c.Seq, c.Kind, code, HeroText.Of(key, args), 0, 0, c.IssuedSec);

    public static HeroResult Apply(HeroContext ctx, HeroCommand c)
    {
        if (!c.Tag.StartsWith(Prefix, StringComparison.Ordinal)) return R(c, HeroResultCode.Invalid, "HERO_UNKNOWN_COMMAND");
        var f = c.Tag[Prefix.Length..].Split('|');
        var st = ctx.State; var hero = ctx.Party.Hero;
        if (st.Economy == null) return R(c, HeroResultCode.Invalid, "HERO_MAKES_NO_SENSE");
        var w = ContractWiring.From(st, ctx.Content); var book = st.Contracts ??= new ContractBook();
        int day = (int)(c.IssuedSec / 86_400);
        try
        {
            static bool Num(string s, out long v) => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
            switch (f[0])
            {
                case "take" when f.Length >= 2 && Num(f[1], out long id):
                {
                    var res = ContractSystem.Take(w, book, (int)id, hero, ctx.Purse, day);
                    return res switch
                    {
                        TakeResult.Taken => R(c, HeroResultCode.Ok, "ct_taken", w.Content.Kind(book.Get((int)id)!.Kind) is { } k ? k.Id : ""),
                        TakeResult.CannotBond => R(c, HeroResultCode.CannotAfford, "ct_cannot_bond"),
                        TakeResult.Busy => R(c, HeroResultCode.Refused, "ct_busy"),
                        TakeResult.Hidden => R(c, HeroResultCode.Refused, "ct_hidden"),
                        _ => R(c, HeroResultCode.Refused, "ct_not_open"),
                    };
                }
                case "abandon" when f.Length >= 2 && Num(f[1], out long aid):
                    return ContractSystem.Abandon(w, book, (int)aid, hero, day) ? R(c, HeroResultCode.Ok, "ct_abandoned") : R(c, HeroResultCode.Refused, "ct_not_yours");
                case "report" when f.Length >= 2 && Num(f[1], out long rid):
                    return ContractSystem.Report(w, book, (int)rid, hero, day) ? R(c, HeroResultCode.Ok, "ct_reported") : R(c, HeroResultCode.Refused, "ct_cannot_report");
                case "deliver" when f.Length >= 2 && Num(f[1], out long did):
                {
                    var k = book.Get((int)did);
                    if (k == null || k.State != WorkState.Taken || k.Taker != hero || k.Good < 0 || k.TargetOwner == EntityId.None) return R(c, HeroResultCode.Refused, "ct_not_yours");
                    var dst = DestinationStore(st, k.TargetOwner);
                    if (dst == null) return R(c, HeroResultCode.Refused, "ct_no_store");
                    long moved = Ravensguard.Sim.Goods.Store.MoveUpTo(ctx.Pack, dst, (ushort)k.Good, Math.Min(c.Qty, k.Grams - k.Delivered));
                    if (moved <= 0) return R(c, HeroResultCode.NoStock, "ct_nothing_to_hand");
                    ContractSystem.GoodsReceived(w, book, k.TargetOwner, (ushort)k.Good, moved, day);
                    return new HeroResult(c.Seq, c.Kind, HeroResultCode.Ok, HeroText.Of("ct_handed", moved), 0, moved, c.IssuedSec);
                }
                case "pin" when f.Length >= 3 && Num(f[1], out long pid):
                    if (book.Get((int)pid) is not { } pc) return R(c, HeroResultCode.Refused, "ct_not_open");
                    pc.Pinned = f[2] == "1"; return R(c, HeroResultCode.Ok, pc.Pinned ? "ct_pinned" : "ct_unpinned");
                case "enter" when f.Length >= 2 && Num(f[1], out long tid):
                    return Tournaments.Enter(w, book, (int)tid, hero, day) ? R(c, HeroResultCode.Ok, "ct_entered") : R(c, HeroResultCode.Refused, "ct_cannot_enter");
                case "bid" when f.Length >= 4 && Num(f[1], out long bid) && Num(f[2], out long mats) && Num(f[3], out long hours):
                {
                    var k = book.Get((int)bid);
                    if (k == null || !k.Open || k.Kind != "build_cottage") return R(c, HeroResultCode.Refused, "ct_not_open");
                    var me = new Bidder(hero, "hero", [ctx.Hero?.Skill(Ravensguard.Sim.Characters.HeroRecord.SkillId(Skill.Carpentry)) ?? 0], mats, hours, w.DailyWageMites / 10, true, ctx.Purse.Index);
                    var others = w.Bidders?.Invoke(k) ?? [];
                    var (_, win) = ConstructionMarket.Award(w, book, k, [.. others, me], day);
                    return win == null ? R(c, HeroResultCode.Refused, "ct_bid_lost") : win.Who.IsHero ? R(c, HeroResultCode.Ok, "ct_bid_won") : R(c, HeroResultCode.Refused, "ct_bid_lost");
                }
                case "slander" when f.Length >= 4 && Num(f[1], out long victim) && Num(f[2], out long listener):
                {
                    int s = w.PlaceOf(new EntityId((ulong)listener));
                    return Slander.SpeakIll(w, book, new EntityId((ulong)victim), new EntityId((ulong)listener), f[3], s, c.IssuedSec) ? R(c, HeroResultCode.Ok, "ct_slandered") : R(c, HeroResultCode.Refused, "ct_not_believed");
                }
                case "answer" when f.Length >= 2 && Num(f[1], out long ru):
                    return st.Talk != null && Slander.Answer(w, book, st.Talk, new EntityId((ulong)ru), true, st.Standing, null, day) ? R(c, HeroResultCode.Ok, "ct_answered") : R(c, HeroResultCode.Refused, "ct_nothing_to_answer");
            }
        }
        finally { if (st.Contracts is { IsEmpty: true }) st.Contracts = null; }
        return R(c, HeroResultCode.Invalid, "HERO_MAKES_NO_SENSE");
    }

    private static Ravensguard.Sim.Goods.Store? DestinationStore(Ravensguard.Sim.Save.WorldState st, EntityId owner)
    {
        var e = st.Economy!;
        if (st.Production?.ById(owner) is { } b) return b.Store;
        if (e.OtherStores.TryGetValue(owner, out var s)) return s;
        int h = st.Population.Households.IndexOf(owner);
        return h >= 0 && h < e.HouseholdCount ? e.HouseholdStore[h] : null;
    }
}
