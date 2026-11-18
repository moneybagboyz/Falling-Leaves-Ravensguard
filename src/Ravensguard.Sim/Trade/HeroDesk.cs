using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Crafting;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Inventory;
using Ravensguard.Sim.Jobs;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;

namespace Ravensguard.Sim.Trade;

public enum HeroCommandKind : byte
{
    VisitMarket, Buy, Sell, BuyItem, SellItem, Barter, Equip, Unequip, PutItem, PutGood, Inspect, Talk, Hire, Recruit, Dismiss, Order, ViewPerson,
    CraftStart, CraftWork, CraftPause, CraftResume, CraftCollect,
}

/// <summary>Where the hero crafts (built on the main thread from what is in reach, copied into the command).</summary>
public sealed record CraftSiteSpec(int Light, bool Water, string[] Tools, string[] Stations, string[] Improvised)
{
    public CraftSite Build(RecipeDb db)
    {
        var s = new CraftSite { Light = Light, Water = Water };
        foreach (var t in Tools) if (db.Tools.ContainsKey(t)) s.AddTool(db, t);
        foreach (var st in Stations) if (db.Stations.ContainsKey(st)) s.AddStation(db, st);
        foreach (var i in Improvised) s.WithImprovised(i);
        return s;
    }
}

/// <summary>
/// One thing the player asks of the world (spec 4.7: <c>Buy/Sell/PayCoin/HireWorker/Recruit/Dismiss</c> at the barrier). Stamped on the main thread with the game second it was asked at and a
/// sequence number; applied by the tick worker on the world as it stood after the game hour it was asked in, in sequence order, whatever the clock speed.
/// </summary>
public sealed record HeroCommand
{
    public long Seq { get; init; }
    public long IssuedSec { get; init; }
    public HeroCommandKind Kind { get; init; }
    public int Settlement { get; init; } = -1;
    public ushort Good { get; init; } = ItemTable.NoGood;
    public long Qty { get; init; }
    /// <summary>Buy: the most it may cost (mites); sell: the least it must bring. 0: no guard.</summary>
    public long Limit { get; init; }
    public EntityId Item { get; init; }
    public EntityId Container { get; init; }
    public EntityId Person { get; init; }
    public EquipSlot Slot { get; init; }
    public long Wage { get; init; }
    public int Days { get; init; }
    public HireTerms Terms { get; init; }
    public HireDanger Danger { get; init; }
    public SquadOrder Order { get; init; }
    public long X { get; init; }
    public long Y { get; init; }
    public EntityId Job { get; init; }
    public ushort Recipe { get; init; }
    public int Batch { get; init; } = 1;
    public int[]? Choice { get; init; }
    public CraftSiteSpec? Site { get; init; }
    public long UntilSec { get; init; }
    /// <summary>Who issued it (autopilot spec 2, 4.1): the player, the autopilot layer, or an NPC. Part of the command log; the barrier's <see cref="HeroDesk.Gate"/> checks the autopilot's.</summary>
    public CommandOrigin Origin { get; init; }
    /// <summary>Barter: what the hero gives and takes (counted goods), items both ways and coin both ways.</summary>
    public (ushort Good, long Qty)[] Give { get; init; } = [];
    public (ushort Good, long Qty)[] Take { get; init; } = [];
    public EntityId[] GiveItems { get; init; } = [];
    public long CoinGive { get; init; }
    public long CoinTake { get; init; }
    public long Hour => IssuedSec / 3600;
}

public enum HeroResultCode : byte { Ok, PriceMoved, Closed, NoStock, CannotAfford, TheyCannotAfford, Refused, NoMarket, Invalid, NoRoom, NotKnown }

public sealed record HeroResult(long Seq, HeroCommandKind Kind, HeroResultCode Code, string Text, long Mites = 0, long Qty = 0, long AtSec = 0)
{
    public bool Ok => Code == HeroResultCode.Ok;
}

/// <summary>The local book frozen at a barrier: every good's price line, the merchants' coin and the items merchants have for sale.</summary>
public sealed record MarketView(int Settlement, string Name, bool Open, int Day, IReadOnlyList<BookLine> Lines, long MerchantCash, IReadOnlyList<CarriedItem> MerchantItems);

/// <summary>A person the hero is dealing with: their household's goods and coin, what they would ask to be hired, and whether they would join.</summary>
public sealed record PersonView(EntityId Id, string Name, int Age, string Trade, int MaxSkill, int Familiarity, int TrustPm, bool ReasonKnown, IReadOnlyList<(ushort Good, long Qty)> Goods,
    long CoinMites, long ReservationWage, long FairOffer, RecruitAnswer RecruitCheck, bool Survive, bool InSquad);

/// <summary>A tracked skill as the character sheet shows it: the level in micro-levels and the second it was last used.</summary>
public readonly record struct SkillRow(Skill Skill, long Micro, long LastGainSec);

/// <summary>
/// The hero's view of the world, built by the tick worker at a barrier and swapped in whole (immutable to the main thread, like <c>RuntimeSnapshot</c>): coin, everything carried,
/// encumbrance, skills and attributes, the price notebook, the local market, the person being dealt with, the party, the hero's craft jobs (a copy of the crafting state) and the latest results.
/// </summary>
public sealed record HeroView
{
    public long HourDone { get; init; }
    public long AppliedSeq { get; init; }
    public long CoinMites { get; init; }
    public IReadOnlyList<CarriedItem> Items { get; init; } = [];
    public IReadOnlyList<CarriedGood> Goods { get; init; } = [];
    public long MassG { get; init; }
    public long CapacityG { get; init; }
    public int RatioPm { get; init; }
    public int SpeedPm => Encumbrance.SpeedPm(RatioPm);
    public int StaminaPm => Encumbrance.StaminaPm(RatioPm);
    public long WornVolumeMl { get; init; }
    public long WornCapacityMl { get; init; }
    public IReadOnlyList<SkillRow> Skills { get; init; } = [];
    public int[] Attributes { get; init; } = new int[5];
    public int[] AttrMilli { get; init; } = new int[5];
    public IReadOnlyList<PriceNote> Notes { get; init; } = [];
    public IReadOnlyDictionary<int, string> BookNames { get; init; } = new Dictionary<int, string>();
    public IReadOnlyList<ushort> Traded { get; init; } = [];
    public MarketView? Market { get; init; }
    public PersonView? Person { get; init; }
    public IReadOnlyList<Follower> Squad { get; init; } = [];
    public int SquadCap { get; init; } = 2;
    public CraftState? Crafting { get; init; }
    public IReadOnlyList<HeroResult> Results { get; init; } = [];
    public int Day => (int)(HourDone / 24);
}

/// <summary>Everything the desk needs to apply a command, made once per world on the tick worker.</summary>
public sealed class HeroContext
{
    public WorldState State { get; }
    public HeroParty Party { get; }
    public ContentDatabase Content { get; }
    public EconomyState Econ => State.Economy!;
    public MarketSystem? Markets => State.Markets;
    public HeroRecord? Hero => State.Hero;
    public ContainerRules Rules { get; }
    public ItemFactory Factory { get; }
    public TradeSettingsDef Trade { get; }
    public long WageU { get; }
    public RecipeDb Recipes { get; }
    public CraftRunner Craft { get; }
    public PurseId Purse { get; }
    public ulong Seed => State.Seed;
    /// <summary>The job table and its environment, for Work orders (null outside a running game).</summary>
    public Func<(JobEnv Env, JobTable Table)?>? Jobs { get; init; }

    public HeroContext(WorldState state, HeroParty party, ContentDatabase content)
    {
        State = state; Party = party; Content = content;
        var e = state.Economy ?? throw new InvalidOperationException("the hero's desk needs the world's economy");
        Rules = new ContainerRules(content, e.Goods);
        Factory = new ItemFactory(content, e.Goods);
        Trade = content.All<TradeSettingsDef>().OrderBy(t => t.Id, StringComparer.Ordinal).FirstOrDefault() ?? new TradeSettingsDef { Id = "default" };
        WageU = Ravensguard.Sim.Behaviour.BehaviourContent.From(content).OddJobs.UnskilledWageMitesPerDay;
        Recipes = RecipeDb.Build(content, e.Goods);
        var cs = state.Crafting ??= new CraftState();
        var hero = state.Hero; var people = state.Population.People;
        cs.Book.Base = (p, s) =>
        {
            if (p == party.Hero) return hero?.Skill(HeroRecord.SkillId(s)) ?? 0;
            int i = people.IndexOf(p); return i >= 0 ? people.SkillLevelOf(i, s) : 0;
        };
        Craft = new CraftRunner(Recipes, e, cs, state.Seed, state.Ids, o => o == party.Hero ? party.Carried.Pack(e) : e.OtherStores.GetValueOrDefault(o));
        Purse = e.Ledger.Open(PurseKind.Person, party.Hero);
    }

    public Store Pack => Party.Carried.Pack(Econ);
    public MarketParty HeroMarketParty => new(Party.Hero, Purse, Pack, PartyKind.Household, int.MaxValue);
    public long Coin => Econ.Ledger.Balance(Purse).Mites;
    public MarketBook? Book(int settlement)
    {
        var m = Markets; if (m == null || settlement < 0 || settlement >= m.BookOf.Length) return null;
        return m.BookForSettlement(settlement);
    }
    public CraftActor Actor()
    {
        var h = Hero;
        int A(HeroAttr a) => h == null ? 50 : Progress.Effective(h.Attribute(a), Party.AttrMilli[(int)a]);
        return new CraftActor(Party.Hero, A(HeroAttr.Strength), A(HeroAttr.Wits), A(HeroAttr.Perception));
    }
    public int Strength => Hero == null ? 50 : Progress.Effective(Hero.Attribute(HeroAttr.Strength), Party.AttrMilli[(int)HeroAttr.Strength]);
}

/// <summary>
/// The hero's command desk (spec 4.7): the main thread issues <see cref="HeroCommand"/>s; the tick worker applies them at a barrier, each on the world as it stood after the game hour the
/// command was asked in (<see cref="ApplyDue"/> with the last finished hour), in sequence order, so a run at 1x and at 8x apply the same commands to the same records. After every barrier the
/// desk swaps in a new <see cref="HeroView"/>. Coin moves only through the ledger, goods through <see cref="Store.Move"/> and item rows change owner; nothing is minted or destroyed.
/// </summary>
public sealed class HeroDesk
{
    private readonly object _gate = new();
    private readonly List<HeroCommand> _pending = [];
    private readonly List<HeroResult> _results = [];
    private long _seq;
    private volatile HeroView? _view;
    private int _market = -1;
    private EntityId _person;

    public HeroView? View => _view;
    /// <summary>
    /// The barrier check of commands the autopilot issued (autopilot spec 4.3): called for every command with <c>Origin = Autopilot</c> before it is applied; a non-null result rejects it (nothing changes) and is the answer
    /// the command gets. Null for a desk without autopilot.
    /// </summary>
    public Func<HeroContext, HeroCommand, HeroResult?>? Gate { get; set; }
    /// <summary>Called after every command is applied, with its result (the autopilot settles what it reserved).</summary>
    public Action<HeroContext, HeroCommand, HeroResult>? Settled { get; set; }
    public int Pending { get { lock (_gate) return _pending.Count; } }

    public HeroDesk(long lastSeq = 0) { _seq = lastSeq; }

    /// <summary>Queues a command (any thread); returns it with its sequence number.</summary>
    public HeroCommand Issue(HeroCommand c)
    {
        lock (_gate) { c = c with { Seq = ++_seq }; _pending.Add(c); }
        return c;
    }

    /// <summary>Applies, in sequence order, every queued command asked in a game hour up to <paramref name="hourDone"/> (the world's last finished hour). Tick worker only.</summary>
    public int ApplyDue(HeroContext ctx, long hourDone, bool publish = true)
    {
        List<HeroCommand> due;
        lock (_gate)
        {
            due = [.. _pending.Where(c => c.Hour <= hourDone).OrderBy(c => c.Seq)];
            if (due.Count == 0) { if (publish && _view == null) Publish(ctx, hourDone); return 0; }
            _pending.RemoveAll(c => c.Hour <= hourDone);
        }
        foreach (var c in due)
        {
            var r = (c.Origin == CommandOrigin.Autopilot ? Gate?.Invoke(ctx, c) : null) ?? Apply(ctx, c);
            Settled?.Invoke(ctx, c, r);
            ctx.Party.LastSeq = Math.Max(ctx.Party.LastSeq, c.Seq);
            _results.Add(r);
        }
        if (_results.Count > 40) _results.RemoveRange(0, _results.Count - 40);
        if (publish) Publish(ctx, hourDone);
        return due.Count;
    }

    /// <summary>
    /// The end of a game hour (called by the runtime right after it stepped hour <paramref name="h"/>): the hour's own passes for the hero (the wage day at hour 0, the crafting hourly pass for
    /// waiting work), then the commands asked in that hour that arrived while it was being stepped. The order is fixed: passes of hour h, then commands of hour h.
    /// </summary>
    public void AfterHour(HeroContext ctx, long h)
    {
        if (h % 24 == 0) PayDay(ctx, (int)(h / 24));
        foreach (var ev in ctx.Craft.Hourly(h * 3600))
            if (ctx.Craft.State.Get(ev.Job) is { } j && j.Owner == ctx.Party.Hero) _results.Add(new HeroResult(0, HeroCommandKind.CraftWork, HeroResultCode.Ok, CraftText(ctx, ev), AtSec: ev.AtSec));
        ctx.Party.Carried.Tidy(ctx.Econ);
        ApplyDue(ctx, h, publish: false);
        Publish(ctx, h);
    }

    private void PayDay(HeroContext ctx, int day)
    {
        var e = ctx.Econ; var people = ctx.State.Population.People;
        PurseId? PurseOf(Follower f)
        {
            int i = people.IndexOf(f.Person);
            if (i >= 0 && people.Household[i] >= 0 && people.Household[i] < e.HouseholdCount) return e.HouseholdPurse[people.Household[i]];
            return e.Ledger.TryFind(PurseKind.Person, f.Person, out var p) ? p : null;
        }
        foreach (var ev in ctx.Party.Squad.PayDay(ctx.Trade, e.Ledger, ctx.Purse, PurseOf, day))
        {
            if (ev.Kind is "unpaid" or "quit" or "ended")
                _results.Add(new HeroResult(0, HeroCommandKind.Hire, ev.Kind == "unpaid" ? HeroResultCode.CannotAfford : HeroResultCode.Ok,
                    ev.Kind switch { "unpaid" => $"{ev.Name} was not paid today ({Quotes.Money(ev.Mites)}).", "quit" => $"{ev.Name} has left your service: three days unpaid.", _ => $"{ev.Name}'s term is over." }, ev.Mites, AtSec: (long)day * 86400));
            if (ev.Kind == "quit")
                ctx.State.Chronicle.Add(new ChronicleEntry(ctx.State.Ids.Allocate(IdKind.Event), new GameTime((long)day * 86400), "squad_member_quit", [ev.Person, ctx.Party.Hero], [],
                    new Dictionary<string, string> { ["name"] = ev.Name, ["reason"] = "unpaid" }));
        }
    }

    public void Publish(HeroContext ctx, long hourDone) => _view = BuildView(ctx, hourDone);

    // ------------------------------------------------------------------ applying

    private static HeroResult R(HeroCommand c, HeroResultCode code, string text, long mites = 0, long qty = 0) => new(c.Seq, c.Kind, code, text, mites, qty, c.IssuedSec);

    private HeroResult Apply(HeroContext ctx, HeroCommand c)
    {
        try
        {
            return c.Kind switch
            {
                HeroCommandKind.VisitMarket => Visit(ctx, c),
                HeroCommandKind.Buy => Buy(ctx, c),
                HeroCommandKind.Sell => Sell(ctx, c),
                HeroCommandKind.BuyItem => BuyItem(ctx, c),
                HeroCommandKind.SellItem => SellItem(ctx, c),
                HeroCommandKind.Barter => DoBarter(ctx, c),
                HeroCommandKind.Equip => Equip(ctx, c),
                HeroCommandKind.Unequip => ctx.Party.Carried.Unequip(c.Item) ? R(c, HeroResultCode.Ok, "Taken off.") : R(c, HeroResultCode.Invalid, "Not worn."),
                HeroCommandKind.PutItem => Put(c, ctx.Party.Carried.PutItem(ctx.Econ, ctx.Rules, c.Item, c.Container)),
                HeroCommandKind.PutGood => Put(c, ctx.Party.Carried.PutGood(ctx.Econ, ctx.Rules, c.Good, c.Container)),
                HeroCommandKind.Inspect => Inspect(ctx, c),
                HeroCommandKind.Talk => Talk(ctx, c),
                HeroCommandKind.ViewPerson => ViewPerson(c),
                HeroCommandKind.Hire => Hire(ctx, c),
                HeroCommandKind.Recruit => Recruit(ctx, c),
                HeroCommandKind.Dismiss => Dismiss(ctx, c),
                HeroCommandKind.Order => Order(ctx, c),
                HeroCommandKind.CraftStart => CraftStart(ctx, c),
                HeroCommandKind.CraftWork => CraftWork(ctx, c),
                HeroCommandKind.CraftPause => CraftPause(ctx, c),
                HeroCommandKind.CraftResume => CraftResume(ctx, c),
                HeroCommandKind.CraftCollect => CraftCollect(ctx, c),
                _ => R(c, HeroResultCode.Invalid, "Unknown command."),
            };
        }
        finally { ctx.Party.Carried.Tidy(ctx.Econ); }
    }

    private static HeroResult Put(HeroCommand c, PutRefusal why) => why == PutRefusal.None ? R(c, HeroResultCode.Ok, "Stowed.") : R(c, why == PutRefusal.Volume ? HeroResultCode.NoRoom : HeroResultCode.Invalid, why switch
    {
        PutRefusal.Volume => "It does not fit.", PutRefusal.TooLong => "It is too long for that.", PutRefusal.TooDeep => "Containers nest at most three deep.", PutRefusal.Liquid => "Liquids need a flask or a skin.",
        PutRefusal.NotAContainer => "That holds nothing.", _ => "You cannot do that.",
    });

    private string GoodName(HeroContext ctx, ushort g) => ctx.Econ.Goods.Def(g).Name.ToLowerInvariant();
    private string Amount(HeroContext ctx, ushort g, long q) => ctx.Econ.Goods.IsPiece(g) ? $"{q}" : $"{q / 1000.0:0.###} kg";

    private HeroResult Visit(HeroContext ctx, HeroCommand c)
    {
        var b = ctx.Book(c.Settlement); if (b == null) return R(c, HeroResultCode.NoMarket, "There is no market here.");
        _market = c.Settlement;
        int day = (int)(c.IssuedSec / 86400);
        ctx.Party.Notebook.Visit(b, day);
        HearAround(ctx, b, day);
        return R(c, HeroResultCode.Ok, $"The market of {b.Name}.");
    }

    /// <summary>Price talk at a market (spec 4.2): the merchants tell what prices were in up to six other markets of the region when they last heard, dated by the road (a day per 25 km, at least one).</summary>
    private static void HearAround(HeroContext ctx, MarketBook here, int day)
    {
        var m = ctx.Markets; if (m == null) return;
        var setts = ctx.State.Population.Region.Settlements;
        var hs = here.SettlementIndex >= 0 && here.SettlementIndex < setts.Count ? setts[here.SettlementIndex] : null;
        var others = m.AllBooks().Where(b => b != here && b.SettlementIndex >= 0 && b.SettlementIndex < setts.Count)
            .Select(b => (B: b, D: hs == null ? 0L : (long)Math.Sqrt((double)(setts[b.SettlementIndex].CenterX - hs.CenterX) * (setts[b.SettlementIndex].CenterX - hs.CenterX) + (double)(setts[b.SettlementIndex].CenterY - hs.CenterY) * (setts[b.SettlementIndex].CenterY - hs.CenterY))))
            .OrderBy(t => t.D).ThenBy(t => t.B.SettlementIndex).Take(6);
        foreach (var (b, d) in others)
        {
            int heard = day - Math.Max(1, (int)(d / 25_000)) - (int)((ulong)b.SettlementIndex % 5);
            for (ushort g = 0; g < b.Goods.Length; g++)
            {
                var gb = b.Goods[g]; if (gb.StockUnits == 0 && gb.LastTradedDay < 0) continue;
                if (ctx.Party.Notebook.TryGet(b.SettlementIndex, g, out var old) && old.HeardDay >= heard) continue;
                ctx.Party.Notebook.Hear(new PriceNote(b.SettlementIndex, g, b.AskMicro(g), b.BidMicro(g), gb.StockUnits, heard));
            }
        }
    }

    private static int HourOf(long sec) => (int)(sec % 86400 / 3600);

    private HeroResult Buy(HeroContext ctx, HeroCommand c)
    {
        var b = ctx.Book(c.Settlement); if (b == null) return R(c, HeroResultCode.NoMarket, "There is no market here.");
        if (!b.IsOpen(HourOf(c.IssuedSec))) return R(c, HeroResultCode.Closed, "The stalls are shut (06:00 to 18:00).");
        ushort g = c.Good; if (g >= b.Goods.Length || c.Qty <= 0) return R(c, HeroResultCode.Invalid, "Nothing to buy.");
        b.Sync(g);
        if (b.Goods[g].StockUnits < c.Qty) return R(c, HeroResultCode.NoStock, $"They have only {Amount(ctx, g, b.Goods[g].StockUnits)} of {GoodName(ctx, g)}.");
        var q = Quotes.Buy(b, g, c.Qty);
        if (c.Limit > 0 && q.TotalMites > c.Limit) return R(c, HeroResultCode.PriceMoved, $"Prices moved: {Amount(ctx, g, c.Qty)} of {GoodName(ctx, g)} now costs {Quotes.Money(q.TotalMites)}. Nothing was spent.", q.TotalMites);
        if (ctx.Coin < q.TotalMites) return R(c, HeroResultCode.CannotAfford, $"You cannot afford it: {Quotes.Money(q.TotalMites)}, you have {Quotes.Money(ctx.Coin)}.", q.TotalMites);
        var r = b.Buy(ctx.HeroMarketParty, g, c.Qty, b.Money.PriceClampMaxPermille, (int)(c.IssuedSec / 86400), c.Limit > 0 ? c.Limit : q.TotalMites);
        if (r.Qty <= 0) return R(c, HeroResultCode.NoStock, "No one will sell at that price.");
        ctx.Party.Carried.Stow(ctx.Econ, ctx.Rules, g);
        ctx.Party.Traded.Add(g);
        Deal(ctx, c);
        ctx.Party.Notebook.Visit(b, (int)(c.IssuedSec / 86400));
        return R(c, HeroResultCode.Ok, $"Bought {Amount(ctx, g, r.Qty)} of {GoodName(ctx, g)} for {Quotes.Money(r.PaidMites)}.", r.PaidMites, r.Qty);
    }

    private HeroResult Sell(HeroContext ctx, HeroCommand c)
    {
        var b = ctx.Book(c.Settlement); if (b == null) return R(c, HeroResultCode.NoMarket, "There is no market here.");
        if (!b.IsOpen(HourOf(c.IssuedSec))) return R(c, HeroResultCode.Closed, "The stalls are shut (06:00 to 18:00).");
        ushort g = c.Good;
        if (c.Qty <= 0 || ctx.Pack.Qty(g) < c.Qty) return R(c, HeroResultCode.Invalid, "You do not have that much.");
        if (b.Merchants.Count == 0) return R(c, HeroResultCode.TheyCannotAfford, "No merchant buys here.");
        if (ctx.Econ.Goods.SpoilPpm(g, StoreKind.Granary) >= MarketBook.PerishablePpmPerDay) return R(c, HeroResultCode.Refused, "No merchant buys fresh food to hold.");
        b.Sync(g);
        var q = Quotes.Sell(b, g, c.Qty);
        if (c.Limit > 0 && q.NetMites < c.Limit) return R(c, HeroResultCode.PriceMoved, $"Prices moved: they now pay {Quotes.Money(q.NetMites)}. Nothing was sold.", q.NetMites);
        long before = ctx.Coin;
        var r = b.SellToMerchants(ctx.HeroMarketParty, g, c.Qty, (int)(c.IssuedSec / 86400));
        if (r.Qty <= 0) return R(c, HeroResultCode.TheyCannotAfford, $"The merchants have no coin for it (they hold {Quotes.Money(MerchantCash(b))}).");
        ctx.Party.Carried.Stow(ctx.Econ, ctx.Rules, g);
        ctx.Party.Traded.Add(g);
        Deal(ctx, c);
        ctx.Party.Notebook.Visit(b, (int)(c.IssuedSec / 86400));
        long net = ctx.Coin - before;
        return R(c, HeroResultCode.Ok, $"Sold {Amount(ctx, g, r.Qty)} of {GoodName(ctx, g)} for {Quotes.Money(r.PaidMites)} (toll {Quotes.Money(r.TollMites)}).", net, r.Qty);
    }

    private static long MerchantCash(MarketBook b) => b.Merchants.Sum(m => Math.Max(0, b.Econ.Ledger.Balance(m.Purse).Mites - (m.CashReserve?.Invoke() ?? 0)));

    private void Deal(HeroContext ctx, HeroCommand c)
    {
        var (o, _) = Progress.Use(ctx.Craft.State.Book, ctx.Party.Hero, Progress.TradeDeal, 40, c.IssuedSec);
        _ = o;
    }

    private long ItemValue(HeroContext ctx, int row) => ctx.Econ.Items.ValueMites(row, m => { try { return ctx.Factory.ValueCrownsPerKg(m); } catch (KeyNotFoundException) { return 1.0; } });

    private HeroResult SellItem(HeroContext ctx, HeroCommand c)
    {
        var b = ctx.Book(c.Settlement); if (b == null) return R(c, HeroResultCode.NoMarket, "There is no market here.");
        var e = ctx.Econ; int row = e.Items.IndexOf(c.Item);
        if (row < 0 || e.Items.At(row).Owner != ctx.Party.Hero) return R(c, HeroResultCode.Invalid, "You do not have that.");
        if ((e.Items.At(row).Flags & ItemFlags.Stolen) != 0) return R(c, HeroResultCode.Refused, "\"I don't buy stolen goods.\"");
        long bid = ItemPricing.Bid(ItemValue(ctx, row), b.Money.SpreadHalfPermille, ctx.Party.Inspected.Contains(c.Item), ctx.Trade.SellUnknownConditionPm);
        if (bid <= 0) return R(c, HeroResultCode.Refused, "It is worth nothing to them.");
        if (c.Limit > 0 && bid < c.Limit) return R(c, HeroResultCode.PriceMoved, $"They now offer {Quotes.Money(bid)}.", bid);
        foreach (var m in b.Merchants)
        {
            if (e.Ledger.Balance(m.Purse).Mites < bid) continue;
            if (!e.Ledger.Transfer(m.Purse, ctx.Purse, Coin.FromMites(bid), TransferReason.Trade)) continue;
            foreach (int k in ctx.Party.Carried.Rows(e).ToArray()) if (e.Items.At(k).Parent == row) e.Items.At(k).Parent = -1; // its contents stay with the hero
            ref var it = ref e.Items.At(row); it.Owner = m.Id; it.Parent = -1;
            Deal(ctx, c);
            return R(c, HeroResultCode.Ok, $"Sold the {ctx.Rules.Name(it.Shape).ToLowerInvariant()} for {Quotes.Money(bid)}.", bid, 1);
        }
        return R(c, HeroResultCode.TheyCannotAfford, $"No merchant has {Quotes.Money(bid)} for it.");
    }

    private HeroResult BuyItem(HeroContext ctx, HeroCommand c)
    {
        var b = ctx.Book(c.Settlement); if (b == null) return R(c, HeroResultCode.NoMarket, "There is no market here.");
        var e = ctx.Econ; int row = e.Items.IndexOf(c.Item);
        var seller = row < 0 ? null : b.Merchants.FirstOrDefault(m => m.Id == e.Items.At(row).Owner);
        if (seller == null) return R(c, HeroResultCode.NoStock, "That is no longer for sale.");
        long ask = ItemPricing.Ask(ItemValue(ctx, row), b.Money.SpreadHalfPermille);
        if (c.Limit > 0 && ask > c.Limit) return R(c, HeroResultCode.PriceMoved, $"It now costs {Quotes.Money(ask)}.", ask);
        if (!e.Ledger.Transfer(ctx.Purse, seller.Purse, Coin.FromMites(ask), TransferReason.Trade)) return R(c, HeroResultCode.CannotAfford, $"You cannot afford {Quotes.Money(ask)}.", ask);
        ref var it = ref e.Items.At(row); it.Owner = ctx.Party.Hero; it.Parent = -1;
        ctx.Party.Inspected.Add(c.Item);
        Deal(ctx, c);
        return R(c, HeroResultCode.Ok, $"Bought the {ctx.Rules.Name(it.Shape).ToLowerInvariant()} for {Quotes.Money(ask)}.", ask, 1);
    }

    private HeroResult Equip(HeroContext ctx, HeroCommand c)
    {
        var e = ctx.Econ; var item = c.Item;
        if (item == EntityId.None && c.Good != ItemTable.NoGood)
        {
            // a counted piece with a shape (a knife bought at market) becomes an item of its own when it is taken in hand (Materialise: a counted sink, the row is the thing)
            var made = ctx.Factory.Materialise(ctx.Pack, e.Items, ctx.State.Ids, c.Good, ctx.Party.Hero);
            if (made == null) return R(c, HeroResultCode.Invalid, "That cannot be worn or held.");
            item = made.Value;
        }
        return ctx.Party.Carried.EquipItem(e, ctx.Rules, item, c.Slot) ? R(c, HeroResultCode.Ok, "Done.") : R(c, HeroResultCode.Invalid, "That cannot be worn or held.");
    }

    private HeroResult Inspect(HeroContext ctx, HeroCommand c)
    {
        int row = ctx.Econ.Items.IndexOf(c.Item); if (row < 0) return R(c, HeroResultCode.Invalid, "Nothing there.");
        ctx.Party.Inspected.Add(c.Item);
        var it = ctx.Econ.Items.At(row);
        return R(c, HeroResultCode.Ok, $"{ctx.Rules.Name(it.Shape)}: {it.Quality}, condition {it.ConditionPermille / 10}%, worth about {Quotes.Money(ItemValue(ctx, row))}.", ItemValue(ctx, row));
    }

    // ------------------------------------------------------------------ people

    private (int Index, int Household) PersonOf(HeroContext ctx, EntityId id)
    {
        var p = ctx.State.Population.People; int i = p.IndexOf(id);
        return i < 0 ? (-1, -1) : (i, p.Household[i]);
    }

    private HeroCandidateInfo Candidate(HeroContext ctx, int i, EntityId id, int day)
    {
        var p = ctx.State.Population.People; var e = ctx.Econ;
        int hh = p.Household[i];
        int maxSkill = 0; string trade = p.Occupation[i].ToString();
        for (int s = 0; s < PersonTable.SkillSlots; s++) maxSkill = Math.Max(maxSkill, p.SkillAt(i, s).Level);
        bool survive = hh >= 0 && hh < e.HouseholdCount && e.Tiers[hh] == HouseholdTier.Survive;
        var hht = ctx.State.Population.Households;
        bool head = hh >= 0 && hh < hht.Count && hht.MemberCount[hh] > 1 && p.Employer[i] >= 0 && p.AgeYears(i, day) >= 25 && p.Spouse[i] >= 0;
        var k = ctx.Party.Known.GetValueOrDefault(id) ?? new Acquaintance();
        return new HeroCandidateInfo(new HireCandidate(id, p.AgeYears(i, day), maxSkill, k.TrustPm, survive, head, p.IsAlive(i), false), k, trade, hh);
    }

    private sealed record HeroCandidateInfo(HireCandidate C, Acquaintance Known, string Trade, int Household);

    private HeroResult ViewPerson(HeroCommand c) { _person = c.Person; return R(c, HeroResultCode.Ok, ""); }

    private HeroResult Talk(HeroContext ctx, HeroCommand c)
    {
        var (i, _) = PersonOf(ctx, c.Person); if (i < 0) return R(c, HeroResultCode.Invalid, "They are gone.");
        _person = c.Person;
        var k = ctx.Party.Know(c.Person);
        if (k.LastTalkHour != c.Hour) { k.Familiarity = Math.Min(100, k.Familiarity + 10); k.TrustPm = Math.Min(1000, k.TrustPm + 25); k.LastTalkHour = c.Hour; }
        if (k.Familiarity >= ctx.Trade.RecruitFamiliarityMin) k.ReasonKnown = true;
        Progress.Use(ctx.Craft.State.Book, ctx.Party.Hero, Progress.Talk, 40, c.IssuedSec);
        string name = ctx.State.Population.People.FullName(i);
        return R(c, HeroResultCode.Ok, k.ReasonKnown ? $"You talk with {name}. You know what would make them go with you: a fair wage." : $"You talk with {name} a while.");
    }

    private HeroResult Hire(HeroContext ctx, HeroCommand c)
    {
        var (i, hh) = PersonOf(ctx, c.Person); if (i < 0) return R(c, HeroResultCode.Invalid, "They are gone.");
        if (ctx.Party.Squad.Find(c.Person) != null) return R(c, HeroResultCode.Invalid, "They already work for you.");
        int day = (int)(c.IssuedSec / 86400); var info = Candidate(ctx, i, c.Person, day);
        var (ans, score, res) = Hiring.Ask(ctx.Trade, ctx.Seed, day, ctx.WageU, info.C, c.Wage, c.Danger, c.Terms);
        string name = ctx.State.Population.People.FullName(i);
        if (ans != HireAnswer.Accept)
            return R(c, HeroResultCode.Refused, ans switch { HireAnswer.TooYoung => $"{name} is too young to hire.", HireAnswer.Unavailable => $"{name} cannot be hired.", _ => $"{name} will not work for {Quotes.Money(c.Wage)} a day (they ask about {Quotes.Money(Hiring.FairOffer(ctx.Trade, res, info.C))})." }, res);
        ctx.Party.Squad.Members.Add(new Follower
        {
            Person = c.Person, PersonIndex = i, Name = name, Origin = FollowerOrigin.Hired, Loyalty = Recruiting.LoyaltyStart(ctx.Trade, info.C.TrustPm), JoinedDay = day, EndDay = day + Math.Clamp(c.Days, 1, 30),
            WageMitesPerDay = c.Wage, Terms = c.Terms, PaidThroughDay = day, Order = SquadOrder.Follow, MaxSkill = info.C.MaxSkill, Trade = info.Trade,
            Household = hh >= 0 ? ctx.State.Population.Households.IdOf(hh) : EntityId.None,
        });
        var k = ctx.Party.Know(c.Person); k.Familiarity = Math.Min(100, k.Familiarity + 5);
        return R(c, HeroResultCode.Ok, $"{name} agrees: {Math.Clamp(c.Days, 1, 30)} days at {Quotes.Money(c.Wage)} a day.", c.Wage, score);
    }

    private HeroResult Recruit(HeroContext ctx, HeroCommand c)
    {
        var (i, hh) = PersonOf(ctx, c.Person); if (i < 0) return R(c, HeroResultCode.Invalid, "They are gone.");
        if (ctx.Party.Squad.Find(c.Person) is { Origin: FollowerOrigin.Recruited }) return R(c, HeroResultCode.Invalid, "They are already with you.");
        int day = (int)(c.IssuedSec / 86400); var info = Candidate(ctx, i, c.Person, day);
        int leadership = ctx.Craft.State.Book.Level(ctx.Party.Hero, Skill.Leadership);
        long wage = c.Wage > 0 ? c.Wage : Hiring.FairOffer(ctx.Trade, Hiring.ReservationWage(ctx.Trade, ctx.WageU, info.C.MaxSkill, info.C.TrustPm), info.C);
        int size = ctx.Party.Squad.Members.Count(m => m.Active && m.Person != c.Person);
        var ans = Recruiting.Check(ctx.Trade, info.Known.Familiarity, info.Known.TrustPm, info.Known.ReasonKnown, size, leadership, ctx.Coin, wage, info.C.Alive && info.C.Age >= 14);
        string name = ctx.State.Population.People.FullName(i);
        if (ans != RecruitAnswer.Accept)
            return R(c, HeroResultCode.Refused, ans switch
            {
                RecruitAnswer.NotFamiliar => $"{name} hardly knows you.", RecruitAnswer.NoTrust => $"{name} does not trust you yet.", RecruitAnswer.NoReason => $"Talk with {name} first: you do not know what would make them come.",
                RecruitAnswer.SquadFull => "Your party is as large as you can lead.", RecruitAnswer.CannotPay => $"You cannot pay the signing bonus of {Quotes.Money(Recruiting.SigningBonus(ctx.Trade, wage))}.", _ => $"{name} cannot join you.",
            });
        long bonus = Recruiting.SigningBonus(ctx.Trade, wage);
        var e = ctx.Econ;
        if (bonus > 0)
        {
            var to = hh >= 0 && hh < e.HouseholdCount ? e.HouseholdPurse[hh] : e.Ledger.Open(PurseKind.Person, c.Person);
            if (!e.Ledger.Transfer(ctx.Purse, to, Coin.FromMites(bonus), TransferReason.Wage)) return R(c, HeroResultCode.CannotAfford, "You cannot pay the signing bonus.");
        }
        ctx.Party.Squad.Members.RemoveAll(m => m.Person == c.Person);   // a hired hand who signs on for good
        ctx.Party.Squad.Members.Add(new Follower
        {
            Person = c.Person, PersonIndex = i, Name = name, Origin = FollowerOrigin.Recruited, Loyalty = Recruiting.LoyaltyStart(ctx.Trade, info.Known.TrustPm), JoinedDay = day,
            WageMitesPerDay = wage, PaidThroughDay = day, Order = SquadOrder.Follow, MaxSkill = info.C.MaxSkill, Trade = info.Trade, Household = hh >= 0 ? ctx.State.Population.Households.IdOf(hh) : EntityId.None,
        });
        var hht = ctx.State.Population.Households;
        string warn = hh >= 0 && hh < hht.Count && hht.MemberCount[hh] > 1 ? $" {name}'s household will lose an earner." : "";
        return R(c, HeroResultCode.Ok, $"{name} joins you for {Quotes.Money(wage)} a day; signing bonus {Quotes.Money(bonus)}.{warn}", bonus);
    }

    private HeroResult Dismiss(HeroContext ctx, HeroCommand c)
    {
        var f = ctx.Party.Squad.Find(c.Person); if (f == null) return R(c, HeroResultCode.Invalid, "They are not with you.");
        long sev = Recruiting.Severance(ctx.Trade, f.WageMitesPerDay); var e = ctx.Econ;
        var (_, hh) = PersonOf(ctx, c.Person);
        if (sev > 0 && hh >= 0 && hh < e.HouseholdCount) e.Ledger.Transfer(ctx.Purse, e.HouseholdPurse[hh], Coin.FromMites(Math.Min(sev, ctx.Coin)), TransferReason.Wage);
        f.State = HireState.Dismissed;
        ctx.Party.Squad.Members.Remove(f);
        return R(c, HeroResultCode.Ok, $"{f.Name} goes home with {Quotes.Money(sev)} severance.", sev);
    }

    private HeroResult Order(HeroContext ctx, HeroCommand c)
    {
        var f = ctx.Party.Squad.Find(c.Person); if (f == null) return R(c, HeroResultCode.Invalid, "They are not with you.");
        f.Order = c.Order; f.OrderX = c.X; f.OrderY = c.Y; f.OrderJob = EntityId.None;
        if (c.Order == SquadOrder.Work)
        {
            if (ctx.Jobs?.Invoke() is not { } jt) return R(c, HeroResultCode.Invalid, "There is no work board here.");
            var job = c.Job != EntityId.None ? c.Job : jt.Table.Jobs.Values.Where(j => j.State == JobState.Open).OrderBy(j => Math.Abs(j.P0 - c.X) + Math.Abs(j.P1 - c.Y)).ThenBy(j => j.Id).Select(j => j.Id).FirstOrDefault();
            if (job == EntityId.None) { f.Order = SquadOrder.Follow; return R(c, HeroResultCode.NoStock, "There is no open work nearby."); }
            var p = ctx.State.Population.People; int day = (int)(c.IssuedSec / 86400);
            var res = jt.Table.ClaimAll(jt.Env, [new ClaimRequest(job, f.Person, f.PersonIndex, c.IssuedSec, (int)c.Seq, c.X, c.Y, f.MaxSkill, p.AgeYears(f.PersonIndex, day))], c.IssuedSec, ctx.Seed);
            if (res.Count == 0 || res[0].Result != ClaimResult.Taken) { f.Order = SquadOrder.Follow; return R(c, HeroResultCode.Refused, $"{f.Name} could not take that work ({(res.Count > 0 ? res[0].Result : ClaimResult.NoSuchJob)})."); }
            f.OrderJob = job;
            return R(c, HeroResultCode.Ok, $"{f.Name} goes to work: {jt.Table.Jobs[job].Title}.");
        }
        return R(c, HeroResultCode.Ok, $"{f.Name}: {c.Order.ToString().ToLowerInvariant()}.");
    }

    private HeroResult DoBarter(HeroContext ctx, HeroCommand c)
    {
        var (i, hh) = PersonOf(ctx, c.Person); var e = ctx.Econ;
        if (i < 0 || hh < 0 || hh >= e.HouseholdCount) return R(c, HeroResultCode.Invalid, "They have no household to trade from.");
        var book = ctx.Book(c.Settlement >= 0 ? c.Settlement : ctx.State.Population.People.HomeSettlement[i]);
        var store = e.HouseholdStore[hh]; var purse = e.HouseholdPurse[hh];
        bool survive = e.Tiers[hh] == HouseholdTier.Survive;
        long Val(ushort g, long q) => book == null ? (long)(e.Goods.BasePriceMilliMites(g) * q / (e.Goods.IsPiece(g) ? 1000 : 1_000_000)) : (long)((Int128)book.MidMicro(g) * q / PriceCurve.Micro);
        int Want(ushort g) => survive && e.Goods.FoodMeasuresPpm(g) > 0 ? 1 : 0;
        long received = c.CoinGive, given = c.CoinTake;
        foreach (var (g, q) in c.Give) { if (ctx.Pack.Qty(g) < q) return R(c, HeroResultCode.Invalid, "You do not have that much."); received += Barter.Weighted(Val(g, q), Want(g), ctx.Trade.WantPm); }
        foreach (var id in c.GiveItems) { int row = e.Items.IndexOf(id); if (row < 0 || e.Items.At(row).Owner != ctx.Party.Hero) return R(c, HeroResultCode.Invalid, "You do not have that."); received += ItemValue(ctx, row); }
        foreach (var (g, q) in c.Take) { if (store.Qty(g) < q) return R(c, HeroResultCode.NoStock, "They do not have that much."); given += Barter.Weighted(Val(g, q), Want(g), ctx.Trade.WantPm); }
        if (c.CoinTake > 0 && e.Ledger.Balance(purse).Mites < c.CoinTake) return R(c, HeroResultCode.TheyCannotAfford, $"They have only {Quotes.Money(e.Ledger.Balance(purse).Mites)}.");
        if (c.CoinGive > 0 && ctx.Coin < c.CoinGive) return R(c, HeroResultCode.CannotAfford, "You do not have the coin.");
        string name = ctx.State.Population.People.FullName(i);
        if (!Barter.Accepts(received, given)) return R(c, HeroResultCode.Refused, $"{name} shakes their head: that is worth {Quotes.Money(received)} to them, they would give {Quotes.Money(given)}.", received);
        // all checks passed: every move below succeeds (the same stores, no capacity on a house store or the pack)
        foreach (var (g, q) in c.Give) Store.Move(ctx.Pack, store, g, q);
        foreach (var (g, q) in c.Take) { Store.Move(store, ctx.Pack, g, q); ctx.Party.Carried.Stow(e, ctx.Rules, g); ctx.Party.Traded.Add(g); }
        foreach (var id in c.GiveItems) { ref var it = ref e.Items.Get(id); it.Owner = ctx.State.Population.People.IdOf(i); it.Parent = -1; }
        if (c.CoinGive > 0) e.Ledger.Transfer(ctx.Purse, purse, Coin.FromMites(c.CoinGive), TransferReason.Trade);
        if (c.CoinTake > 0) e.Ledger.Transfer(purse, ctx.Purse, Coin.FromMites(c.CoinTake), TransferReason.Trade);
        Deal(ctx, c);
        var k = ctx.Party.Know(c.Person); k.Familiarity = Math.Min(100, k.Familiarity + 2);
        return R(c, HeroResultCode.Ok, $"{name} agrees to the exchange.", received - given);
    }

    // ------------------------------------------------------------------ crafting (the real world's CraftState, on the world clock)

    private static string CraftText(HeroContext ctx, CraftEvent e) => e.Kind switch
    {
        CraftEventKind.Finished => $"Finished: {e.Text.Replace('_', ' ')}.", CraftEventKind.Ready => "Your work is ready to collect.", CraftEventKind.PastDeadline => "Your work is past its time: collect it soon.",
        CraftEventKind.Ruining => "Your work is about to spoil.", CraftEventKind.Ruined => "Your work was spoiled.", CraftEventKind.Failed => "The work went wrong.", CraftEventKind.Waiting => "Left to rest; come back later.",
        CraftEventKind.SkillGain => "", CraftEventKind.StepDone => "", _ => e.Text,
    };

    private HeroResult CraftEvents(HeroContext ctx, HeroCommand c, List<CraftEvent> ev, string fallback)
    {
        foreach (var x in ev) if (x.Kind == CraftEventKind.Finished) foreach (var (g, _) in ctx.Pack.Rows()) ctx.Party.Carried.Stow(ctx.Econ, ctx.Rules, g);
        var texts = ev.Select(x => CraftText(ctx, x)).Where(t => t.Length > 0).Distinct().ToList();
        return R(c, HeroResultCode.Ok, texts.Count > 0 ? string.Join(" ", texts) : fallback);
    }

    private HeroResult CraftStart(HeroContext ctx, HeroCommand c)
    {
        if (c.Recipe >= ctx.Recipes.Recipes.Count) return R(c, HeroResultCode.Invalid, "No such recipe.");
        var r = ctx.Recipes[c.Recipe];
        if (!ctx.Craft.State.Know.Knows(r, ctx.Party.Hero, ctx.Craft.State.Book)) return R(c, HeroResultCode.NotKnown, "You do not know how to make that.");
        var site = (c.Site ?? new CraftSiteSpec(255, true, [], [], [])).Build(ctx.Recipes);
        var plan = ctx.Craft.Plan(r, ctx.Actor(), site, ctx.Pack, Math.Max(1, c.Batch), c.Choice);
        if (!plan.CanStart) return R(c, HeroResultCode.NoStock, "You lack what it needs.");
        var job = ctx.Craft.Start(plan, ctx.Actor(), ctx.Party.Hero, ctx.Pack, c.IssuedSec, c.X, c.Y);
        if (job == null) return R(c, HeroResultCode.NoStock, "You lack what it needs.");
        ctx.Party.WorkedTo[job.Id] = c.IssuedSec;
        return R(c, HeroResultCode.Ok, $"You start: {r.Name}.", 0, (long)job.Id.Value);
    }

    private HeroResult CraftWork(HeroContext ctx, HeroCommand c)
    {
        var j = ctx.Craft.State.Get(c.Job);
        if (j == null || j.Closed || j.Owner != ctx.Party.Hero) return R(c, HeroResultCode.Invalid, "");
        long from = ctx.Party.WorkedTo.GetValueOrDefault(j.Id, c.UntilSec);
        if (j.State != CraftJobState.Active || c.UntilSec <= from) { ctx.Party.WorkedTo[j.Id] = Math.Max(from, c.UntilSec); return R(c, HeroResultCode.Ok, ""); }
        var ev = ctx.Craft.Advance(j, c.UntilSec - from, from);
        ctx.Party.WorkedTo[j.Id] = c.UntilSec;
        if (j.Closed) ctx.Party.WorkedTo.Remove(j.Id);
        return CraftEvents(ctx, c, ev, "");
    }

    private HeroResult CraftPause(HeroContext ctx, HeroCommand c)
    {
        var j = ctx.Craft.State.Get(c.Job); if (j == null || j.Owner != ctx.Party.Hero) return R(c, HeroResultCode.Invalid, "");
        ctx.Craft.Pause(j); return R(c, HeroResultCode.Ok, "Put down; the work keeps.");
    }

    private HeroResult CraftResume(HeroContext ctx, HeroCommand c)
    {
        var j = ctx.Craft.State.Get(c.Job); if (j == null) return R(c, HeroResultCode.Invalid, "");
        if (!ctx.Craft.Resume(j, ctx.Actor())) return R(c, HeroResultCode.Refused, "You cannot take that up.");
        ctx.Party.WorkedTo[j.Id] = c.IssuedSec;
        return R(c, HeroResultCode.Ok, "Back to work.");
    }

    private HeroResult CraftCollect(HeroContext ctx, HeroCommand c)
    {
        var j = ctx.Craft.State.Get(c.Job); if (j == null) return R(c, HeroResultCode.Invalid, "");
        var (ok, ev) = ctx.Craft.Collect(j, ctx.Actor(), c.IssuedSec);
        if (!ok) return R(c, HeroResultCode.Refused, "It is not ready yet.");
        ctx.Party.WorkedTo[j.Id] = c.IssuedSec;
        return CraftEvents(ctx, c, ev, "Collected.");
    }

    // ------------------------------------------------------------------ the view

    public HeroView BuildView(HeroContext ctx, long hourDone)
    {
        var e = ctx.Econ; var party = ctx.Party; var c = party.Carried;
        var items = c.ListItems(e, ctx.Rules, ctx.Factory.ValueCrownsPerKg);
        var goods = c.ListGoods(e);
        long cap = Encumbrance.CapacityG(ctx.Strength);
        long mass = c.MassG(e, ctx.Purse);
        var book = ctx.Craft.State.Book;
        var skills = Enum.GetValues<Skill>().Where(s => s != Skill.None).Select(s => new SkillRow(s, book.Micro(party.Hero, s), LastUse(book, party.Hero, s))).ToList();
        var attrs = new int[5];
        for (int a = 0; a < 5; a++) attrs[a] = ctx.Hero == null ? 50 : Progress.Effective(ctx.Hero.Attributes[a], party.AttrMilli[a]);
        var names = new Dictionary<int, string>();
        if (ctx.Markets is { } ms) foreach (var b in ms.AllBooks()) names[b.SettlementIndex] = b.Name;
        MarketView? mv = null;
        if (_market >= 0 && ctx.Book(_market) is { } mb)
        {
            var lines = new List<BookLine>();
            for (ushort g = 0; g < mb.Goods.Length; g++) lines.Add(BookLine.Of(mb, g));
            var merchIds = mb.Merchants.Select(m => m.Id).ToHashSet();
            var merchItems = new List<CarriedItem>();
            foreach (int r in e.Items.LiveRows())
            {
                var row = e.Items.At(r); if (!merchIds.Contains(row.Owner)) continue;
                merchItems.Add(new CarriedItem(row.Id, row.Shape, ctx.Rules.Name(row.Shape), row.Quality, row.ConditionPermille, e.Items.MassG(r, e.Goods), ctx.Rules.VolumeMl(e.Items, r), EntityId.None, EquipSlot.None,
                    ItemValue(ctx, r), ctx.Rules.IsContainer(row.Shape), 0, 0, row.Flags, 1));
            }
            mv = new MarketView(_market, mb.Name, mb.IsOpen((int)(hourDone % 24)), (int)(hourDone / 24), lines, MerchantCash(mb), merchItems);
        }
        PersonView? pv = null;
        if (_person != EntityId.None) { var (i, hh) = PersonOf(ctx, _person); if (i >= 0) pv = MakePerson(ctx, i, hh, (int)(hourDone / 24)); }
        int leadership = book.Level(party.Hero, Skill.Leadership);
        // walk the worn containers for the volume bar
        long wornCap = 0, wornUsed = 0;
        foreach (var it in items) if (it.IsContainer && it.Slot != EquipSlot.None) { wornCap += it.CapacityMl; wornUsed += it.UsedMl; }
        return new HeroView
        {
            HourDone = hourDone, AppliedSeq = party.LastSeq, CoinMites = ctx.Coin, Items = items, Goods = goods, MassG = mass, CapacityG = cap, RatioPm = Encumbrance.RatioPm(mass, cap),
            WornCapacityMl = wornCap, WornVolumeMl = wornUsed,
            Skills = skills, Attributes = attrs, AttrMilli = (int[])party.AttrMilli.Clone(), Notes = [.. party.Notebook.Notes], BookNames = names, Traded = [.. party.Traded], Market = mv, Person = pv,
            Squad = [.. party.Squad.Members.Select(FollowerRow.Of).Select(r => r.Restore())], SquadCap = Recruiting.SquadCap(ctx.Trade, leadership),
            Crafting = CraftingSave.Capture(ctx.Craft.State).Restore(), Results = [.. _results],
        };
    }

    private static long LastUse(SkillBook b, EntityId p, Skill s)
    {
        foreach (var (person, skill, hour, _) in b.UseRows()) if (person == p.Value && skill == (byte)s) return hour * 3600;
        return long.MinValue;
    }

    private PersonView MakePerson(HeroContext ctx, int i, int hh, int day)
    {
        var e = ctx.Econ; var p = ctx.State.Population.People; var id = p.IdOf(i);
        var info = Candidate(ctx, i, id, day);
        long res = Hiring.ReservationWage(ctx.Trade, ctx.WageU, info.C.MaxSkill, info.C.TrustPm);
        var goods = hh >= 0 && hh < e.HouseholdCount ? e.HouseholdStore[hh].Rows().Select(r => (r.Good, r.Qty)).ToList() : [];
        long coin = hh >= 0 && hh < e.HouseholdCount ? e.Ledger.Balance(e.HouseholdPurse[hh]).Mites : 0;
        int leadership = ctx.Craft.State.Book.Level(ctx.Party.Hero, Skill.Leadership);
        long fair = Hiring.FairOffer(ctx.Trade, res, info.C);
        var rc = Recruiting.Check(ctx.Trade, info.Known.Familiarity, info.Known.TrustPm, info.Known.ReasonKnown, ctx.Party.Squad.ActiveCount, leadership, ctx.Coin, fair, info.C.Alive && info.C.Age >= 14);
        return new PersonView(id, p.FullName(i), info.C.Age, info.Trade, info.C.MaxSkill, info.Known.Familiarity, info.Known.TrustPm, info.Known.ReasonKnown, goods, coin, res, fair, rc, info.C.SurviveTier,
            ctx.Party.Squad.Find(id) != null);
    }
}
