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
    /// <summary>Appended (Phase 3 cleanup B): the hero pays a toll or bribe on the road (<c>CoinGive</c> mites to the <c>Person</c>'s household when one is named and real, else to the lord's tithe purse as a toll).</summary>
    EncounterPay,
    /// <summary>Appended (Phase 3 review finding 6): the hero takes the job <c>Job</c> (his position in <c>X</c>, <c>Y</c>, region millimetres), or walks away from it (<c>JobAbandon</c>, <c>Tag</c> "away" when the distance rule asked).</summary>
    JobClaim, JobAbandon,
    /// <summary>Appended (Phase 4 P4-D): a lost fight's defeat (the request in <c>Tag</c>, "mc:defeat|...") or a captive's attempt ("mc:act|verb"), applied by <see cref="Ravensguard.Sim.Combat.Aftermath.AftermathDesk"/>.</summary>
    Aftermath,
    /// <summary>Appended (Phase 4 P4-F): a watch-mode setting (the stance of a person, keep watching, take control on input, the speed cap; the tag "wm:...") applied by <see cref="Ravensguard.Sim.Combat.Watch.WatchDesk"/>.</summary>
    Watch,
    /// <summary>Appended (P4 close-out): the sites at the barrier, applied by <see cref="Ravensguard.Sim.Sites.SiteDesk"/>: a crawl's haul into the pack and its burnt torches out ("ex:take|..."), a battlefield searched ("ex:search|site"), a hoard dug ("ex:dig|site").</summary>
    Site,
    /// <summary>Appended (P4 close-out): the hero died; the heir takes over and the estate is settled ("su:heir|person|x|y"), or the line ends ("su:end|x|y"): <see cref="Ravensguard.Sim.Succession.SuccessionDesk"/>.</summary>
    Succession,
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
    /// <summary>The region whose market a Visit/Buy/Sell is for (<c>RegionCoord.Id</c>); -1 is the hero's own region. Another resident region's market is dealt with through <see cref="AbroadDealing"/>.</summary>
    public int Region { get; init; } = -1;
    /// <summary>A short key naming what the command is for (<c>EncounterPay</c>: the encounter id).</summary>
    public string Tag { get; init; } = "";
}

public enum HeroResultCode : byte { Ok, PriceMoved, Closed, NoStock, CannotAfford, TheyCannotAfford, Refused, NoMarket, Invalid, NoRoom, NotKnown }

public sealed record HeroResult(long Seq, HeroCommandKind Kind, HeroResultCode Code, HeroText Message, long Mites = 0, long Qty = 0, long AtSec = 0)
{
    public bool Ok => Code == HeroResultCode.Ok;
    /// <summary>The message as the game's language has it (<see cref="HeroText.Resolver"/>); the sim itself only holds the key and its parameters.</summary>
    public string Text => Message.Render();
}

/// <summary>The local book frozen at a barrier: every good's price line, the merchants' coin and the items merchants have for sale.</summary>
public sealed record MarketView(int Settlement, string Name, bool Open, int Day, IReadOnlyList<BookLine> Lines, long MerchantCash, IReadOnlyList<CarriedItem> MerchantItems, int Region = -1);

/// <summary>A person the hero is dealing with: their household's goods and coin, what they would ask to be hired, and whether they would join.</summary>
public sealed record PersonView(EntityId Id, string Name, int Age, string Trade, int MaxSkill, int Familiarity, int TrustPm, bool ReasonKnown, IReadOnlyList<(ushort Good, long Qty)> Goods,
    long CoinMites, long ReservationWage, long FairOffer, RecruitAnswer RecruitCheck, bool Survive, bool InSquad);

/// <summary>One of the hero's kin for the character screen: name, how related, age (or age at death), alive or not, and where they live.</summary>
public sealed record KinView(string Name, Ravensguard.Sim.Succession.KinRelation Relation, int Age, bool Alive, string Place);

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
    /// <summary>P5-A4: the hero's kin as the person table has them (the character screen's family: inheritance 6).</summary>
    public IReadOnlyList<KinView> Family { get; init; } = [];
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
        Craft = new CraftRunner(Recipes, e, cs, state.Seed, state.Ids, o => o == party.Hero ? party.Carried.Pack(e) : e.OtherStores.GetValueOrDefault(o))
        {
            // P5-A1: a masterwork may be a named artefact, with its chronicle line and its maker's name
            Content = content, Chronicle = state.Chronicle, Tiers = QualityTiers.From(content),
            NameOf = p => { if (p == party.Hero) return state.Hero?.Name ?? ""; int i = people.IndexOf(p); return i >= 0 ? people.FullName(i) : ""; },
        };
        Purse = e.Ledger.Open(PurseKind.Person, party.Hero);
    }

    public Store Pack => Party.Carried.Pack(Econ);
    /// <summary>P5-A1: tool wear by the content's rates and the quality scale.</summary>
    public ToolWear ToolWear => _toolWear ??= new ToolWear(Content);
    private ToolWear? _toolWear;
    /// <summary>A material's category (metal, wood, cloth, leather...), for scrap.</summary>
    public string? MaterialCategory(string material) => Content.TryGet<MaterialDef>(material, out var m) ? m.Category : null;
    /// <summary>P5-A4: the craft tools the hero's pack really holds (item rows and goods whose shape is a <c>craft_tool</c>; a dagger cuts as a knife).</summary>
    public List<string> PackTools()
    {
        var e = Econ;
        var shapes = Party.Carried.Rows(e).Select(i => e.Items.At(i).Shape).Concat(Pack.Rows().Where(x => x.Qty > 0).Select(x => e.Goods.StringId(x.Good)));
        return HeroPack.Tools(shapes, Recipes.Tools.ContainsKey);
    }
    public MarketParty HeroMarketParty => new(Party.Hero, Purse, Pack, PartyKind.Household, int.MaxValue);
    public long Coin => Econ.Ledger.Balance(Purse).Mites;
    /// <summary>True when <paramref name="region"/> names a resident region other than the hero's own.</summary>
    public bool IsAbroad(int region) => region >= 0 && (BookAbroad != null || State.Regions != null) && region != (State.Regions?.Home.Id ?? 0);
    /// <summary>Finds a market book of another region by (region id, settlement): the resident regions' markets by default; tests set it to a second fixture economy.</summary>
    public Func<int, int, MarketBook?>? BookAbroad { get; init; }
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
    private int _market = -1, _marketRegion = -1;
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

    /// <summary>The commands still waiting, in sequence order (any thread; a save writes them, review finding 6).</summary>
    public IReadOnlyList<HeroCommand> PendingCommands() { lock (_gate) return [.. _pending.OrderBy(c => c.Seq)]; }

    /// <summary>Takes up the commands a save left waiting, with their own numbers and hours; the next command issued is numbered after all of them.</summary>
    public void RestorePending(IEnumerable<HeroCommand> cmds)
    {
        lock (_gate)
            foreach (var c in cmds)
            {
                if (_pending.Any(p => p.Seq == c.Seq)) continue;
                _pending.Add(c); _seq = Math.Max(_seq, c.Seq);
            }
    }

    /// <summary>Commands whose application threw (review finding 3): answered <see cref="HeroResultCode.Invalid"/>, counted and reported here; the runtime never faults for one.</summary>
    public long Faults { get; private set; }
    public string LastFault { get; private set; } = "";
    /// <summary>Called on the tick worker with each command that threw (the game logs it).</summary>
    public Action<HeroCommand, Exception>? Faulted { get; set; }

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
            HeroResult r;
            try { r = (c.Origin == CommandOrigin.Autopilot ? Gate?.Invoke(ctx, c) : null) ?? Apply(ctx, c); }
            catch (Exception ex)
            {
                // one bad command is answered and logged; it must not fault the tick (a faulted runtime refuses every save)
                Faults++; LastFault = $"{c.Kind} #{c.Seq}: {ex.GetType().Name}: {ex.Message}";
                Faulted?.Invoke(c, ex);
                r = R(c, HeroResultCode.Invalid, T("HERO_COULD_NOT_BE_DONE"));
            }
            Settled?.Invoke(ctx, c, r);
            ctx.Party.LastSeq = Math.Max(ctx.Party.LastSeq, c.Seq);
            _results.Add(r);
        }
        if (_results.Count > 40) _results.RemoveRange(0, _results.Count - 40);
        _craftDirty = true;                                  // any command may touch the crafting state (a skill use, a job)
        MirrorHero(ctx);
        if (publish) Publish(ctx, hourDone);
        return due.Count;
    }

    /// <summary>P5-A4: the hero's person row mirrors the record at the barrier (one direction, hero to row: attributes and the four best skills).</summary>
    private static void MirrorHero(HeroContext ctx)
    {
        if (ctx.Hero is not { } h) return;
        var pt = ctx.State.Population.People;
        int row = Ravensguard.Sim.Succession.HeroPerson.RowOf(pt, h, ctx.Party.Hero);
        if (row >= 0) Ravensguard.Sim.Succession.HeroPerson.Mirror(pt, row, h);
    }

    /// <summary>
    /// The end of a game hour (called by the runtime right after it stepped hour <paramref name="h"/>): the hour's own passes for the hero (the wage day at hour 0, the crafting hourly pass for
    /// waiting work), then the commands asked in that hour that arrived while it was being stepped. The order is fixed: passes of hour h, then commands of hour h.
    /// </summary>
    public void AfterHour(HeroContext ctx, long h)
    {
        if (h % 24 == 0) PayDay(ctx, (int)(h / 24));
        foreach (var ev in ctx.Craft.Hourly(h * 3600))
            if (ctx.Craft.State.Get(ev.Job) is { } j && j.Owner == ctx.Party.Hero)
            {
                _craftDirty = true;
                _results.Add(new HeroResult(0, HeroCommandKind.CraftWork, HeroResultCode.Ok, CraftText(ctx, ev), AtSec: ev.AtSec));
            }
        ctx.Party.Carried.Tidy(ctx.Econ);
        ApplyDue(ctx, h, publish: false);
        MirrorHero(ctx);
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
                    ev.Kind switch { "unpaid" => T("HERO_WAS_NOT_PAID_TODAY", ev.Name, Quotes.Money(ev.Mites)), "quit" => T("HERO_HAS_LEFT_YOUR_SERVICE", ev.Name), _ => T("HERO_S_TERM_OVER", ev.Name) }, ev.Mites, AtSec: (long)day * 86400));
            if (ev.Kind == "quit")
                ctx.State.Chronicle.Add(new ChronicleEntry(ctx.State.Ids.Allocate(IdKind.Event), new GameTime((long)day * 86400), "squad_member_quit", [ev.Person, ctx.Party.Hero], [],
                    new Dictionary<string, string> { ["name"] = ev.Name, ["reason"] = "unpaid" }));
        }
    }

    public void Publish(HeroContext ctx, long hourDone) => _view = BuildView(ctx, hourDone);

    // ------------------------------------------------------------------ applying

    private static HeroResult R(HeroCommand c, HeroResultCode code, HeroText text, long mites = 0, long qty = 0) => new(c.Seq, c.Kind, code, text, mites, qty, c.IssuedSec);
    /// <summary>A message key of en.csv and its parameters (review finding 14: the desk returns keys, the game resolves them).</summary>
    internal static HeroText T(string key, params object[] args) => HeroText.Of(key, args);

    /// <summary>
    /// The numbers every command must have before anything is looked up (review finding 3): no negative quantity, limit, wage, coin or days, no barter line of zero or less or of a good that
    /// does not exist. A command that fails is answered <see cref="HeroResultCode.Invalid"/> and changes nothing.
    /// </summary>
    public static bool Valid(HeroCommand c, int goodCount)
    {
        if (c.Qty < 0 || c.Limit < 0 || c.Wage < 0 || c.Days < 0 || c.CoinGive < 0 || c.CoinTake < 0 || c.Batch < 0) return false;
        foreach (var (g, q) in c.Give) if (q <= 0 || g >= goodCount) return false;
        foreach (var (g, q) in c.Take) if (q <= 0 || g >= goodCount) return false;
        foreach (var id in c.GiveItems) if (id == EntityId.None) return false;
        return true;
    }

    private HeroResult Apply(HeroContext ctx, HeroCommand c)
    {
        if (!Valid(c, ctx.Econ.Goods.Count)) return R(c, HeroResultCode.Invalid, T("HERO_MAKES_NO_SENSE"));
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
                HeroCommandKind.Unequip => ctx.Party.Carried.Unequip(c.Item) ? R(c, HeroResultCode.Ok, T("HERO_TAKEN_OFF")) : R(c, HeroResultCode.Invalid, T("HERO_NOT_WORN")),
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
                HeroCommandKind.EncounterPay => EncounterPay(ctx, c),
                HeroCommandKind.JobClaim => JobClaim(ctx, c),
                HeroCommandKind.JobAbandon => JobAbandon(ctx, c),
                HeroCommandKind.Aftermath => Ravensguard.Sim.Combat.Aftermath.AftermathDesk.Apply(ctx, c),     // P4-D hook
                HeroCommandKind.Watch => Ravensguard.Sim.Combat.Watch.WatchDesk.Apply(ctx, c),                // P4-F hook
                HeroCommandKind.Site => Ravensguard.Sim.Sites.SiteDesk.Apply(ctx, c),                          // P4 close-out: loot to the pack, search and dig
                HeroCommandKind.Succession => Ravensguard.Sim.Succession.SuccessionDesk.Apply(ctx, c),         // P4 close-out: the heir takes over, the estate is settled
                _ => R(c, HeroResultCode.Invalid, T("HERO_UNKNOWN_COMMAND")),
            };
        }
        finally { ctx.Party.Carried.Tidy(ctx.Econ); }
    }

    private static HeroResult Put(HeroCommand c, PutRefusal why) => why == PutRefusal.None ? R(c, HeroResultCode.Ok, T("HERO_STOWED")) : R(c, why == PutRefusal.Volume ? HeroResultCode.NoRoom : HeroResultCode.Invalid, why switch
    {
        PutRefusal.Volume => T("HERO_DOES_NOT_FIT"), PutRefusal.TooLong => T("HERO_TOO_LONG"), PutRefusal.TooDeep => T("HERO_CONTAINERS_NEST_MOST_THREE"), PutRefusal.Liquid => T("HERO_LIQUIDS_NEED_FLASK_OR"),
        PutRefusal.NotAContainer => T("HERO_HOLDS_NOTHING"), _ => T("HERO_CANNOT_DO"),
    });

    private string GoodName(HeroContext ctx, ushort g) => ctx.Econ.Goods.Def(g).Name.ToLowerInvariant();
    private string Amount(HeroContext ctx, ushort g, long q) => ctx.Econ.Goods.IsPiece(g) ? $"{q}" : Quotes.Weight(q);

    private HeroResult Visit(HeroContext ctx, HeroCommand c)
    {
        if (c.Settlement < 0) { _market = -1; _marketRegion = -1; return R(c, HeroResultCode.Ok, HeroText.Empty); }       // on the road: only a person to barter with
        if (ctx.IsAbroad(c.Region)) return AbroadDealing.Visit(ctx, c, out _market, out _marketRegion);
        var b = ctx.Book(c.Settlement); if (b == null) return R(c, HeroResultCode.NoMarket, T("HERO_THERE_NO_MARKET_HERE"));
        _market = c.Settlement; _marketRegion = -1;
        int day = (int)(c.IssuedSec / 86400);
        ctx.Party.Notebook.Visit(b, day);
        HearAround(ctx, b, day);
        return R(c, HeroResultCode.Ok, T("HERO_MARKET", b.Name));
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
        if (ctx.IsAbroad(c.Region)) return AbroadDealing.Buy(ctx, c);
        var b = ctx.Book(c.Settlement); if (b == null) return R(c, HeroResultCode.NoMarket, T("HERO_THERE_NO_MARKET_HERE"));
        if (!b.IsOpen(HourOf(c.IssuedSec))) return R(c, HeroResultCode.Closed, T("HERO_STALLS_ARE_SHUT"));
        ushort g = c.Good; if (g >= b.Goods.Length || c.Qty <= 0) return R(c, HeroResultCode.Invalid, T("HERO_NOTHING_BUY"));
        b.Sync(g);
        if (b.Goods[g].StockUnits < c.Qty) return R(c, HeroResultCode.NoStock, T("HERO_THEY_HAVE_ONLY", Amount(ctx, g, b.Goods[g].StockUnits), GoodName(ctx, g)));
        var q = Quotes.Buy(b, g, c.Qty);
        if (c.Limit > 0 && q.TotalMites > c.Limit) return R(c, HeroResultCode.PriceMoved, T("HERO_PRICES_MOVED_NOW_COSTS", Amount(ctx, g, c.Qty), GoodName(ctx, g), Quotes.Money(q.TotalMites)), q.TotalMites);
        if (ctx.Coin < q.TotalMites) return R(c, HeroResultCode.CannotAfford, T("HERO_CANNOT_AFFORD_HAVE", Quotes.Money(q.TotalMites), Quotes.Money(ctx.Coin)), q.TotalMites);
        var r = b.Buy(ctx.HeroMarketParty, g, c.Qty, b.Money.PriceClampMaxPermille, (int)(c.IssuedSec / 86400), c.Limit > 0 ? c.Limit : q.TotalMites);
        if (r.Qty <= 0) return R(c, HeroResultCode.NoStock, T("HERO_NO_ONE_WILL_SELL"));
        ctx.Party.Carried.Stow(ctx.Econ, ctx.Rules, g);
        ctx.Party.Traded.Add(g);
        Deal(ctx, c);
        ctx.Party.Notebook.Visit(b, (int)(c.IssuedSec / 86400));
        return R(c, HeroResultCode.Ok, T("HERO_BOUGHT", Amount(ctx, g, r.Qty), GoodName(ctx, g), Quotes.Money(r.PaidMites)), r.PaidMites, r.Qty);
    }

    private HeroResult Sell(HeroContext ctx, HeroCommand c)
    {
        if (ctx.IsAbroad(c.Region)) return AbroadDealing.Sell(ctx, c);
        var b = ctx.Book(c.Settlement); if (b == null) return R(c, HeroResultCode.NoMarket, T("HERO_THERE_NO_MARKET_HERE"));
        if (!b.IsOpen(HourOf(c.IssuedSec))) return R(c, HeroResultCode.Closed, T("HERO_STALLS_ARE_SHUT"));
        ushort g = c.Good;
        if (c.Qty <= 0 || ctx.Pack.Qty(g) < c.Qty) return R(c, HeroResultCode.Invalid, T("HERO_DO_NOT_HAVE_MUCH"));
        if (b.Merchants.Count == 0) return R(c, HeroResultCode.TheyCannotAfford, T("HERO_NO_MERCHANT_BUYS_HERE"));
        if (ctx.Econ.Goods.SpoilPpm(g, StoreKind.Granary) >= MarketBook.PerishablePpmPerDay) return R(c, HeroResultCode.Refused, T("HERO_NO_MERCHANT_BUYS_FRESH"));
        b.Sync(g);
        var q = Quotes.Sell(b, g, c.Qty);
        if (c.Limit > 0 && q.NetMites < c.Limit) return R(c, HeroResultCode.PriceMoved, T("HERO_PRICES_MOVED_THEY_NOW", Quotes.Money(q.NetMites)), q.NetMites);
        long before = ctx.Coin;
        var r = b.SellToMerchants(ctx.HeroMarketParty, g, c.Qty, (int)(c.IssuedSec / 86400));
        if (r.Qty <= 0) return R(c, HeroResultCode.TheyCannotAfford, T("HERO_MERCHANTS_HAVE_NO_COIN", Quotes.Money(MerchantCash(b))));
        ctx.Party.Carried.Stow(ctx.Econ, ctx.Rules, g);
        ctx.Party.Traded.Add(g);
        Deal(ctx, c);
        ctx.Party.Notebook.Visit(b, (int)(c.IssuedSec / 86400));
        long net = ctx.Coin - before;
        return R(c, HeroResultCode.Ok, T("HERO_SOLD_TOLL", Amount(ctx, g, r.Qty), GoodName(ctx, g), Quotes.Money(r.PaidMites), Quotes.Money(r.TollMites)), net, r.Qty);
    }

    internal static long MerchantCash(MarketBook b) => b.Merchants.Sum(m => Math.Max(0, b.Econ.Ledger.Balance(m.Purse).Mites - (m.CashReserve?.Invoke() ?? 0)));

    private void Deal(HeroContext ctx, HeroCommand c)
    {
        var (o, _) = Progress.Use(ctx.Craft.State.Book, ctx.Party.Hero, Progress.TradeDeal, 40, c.IssuedSec);
        _ = o;
    }

    private long ItemValue(HeroContext ctx, int row) => ctx.Econ.Items.ValueMites(row, m => { try { return ctx.Factory.ValueCrownsPerKg(m); } catch (KeyNotFoundException) { return 1.0; } });

    private HeroResult SellItem(HeroContext ctx, HeroCommand c)
    {
        var b = ctx.Book(c.Settlement); if (b == null) return R(c, HeroResultCode.NoMarket, T("HERO_THERE_NO_MARKET_HERE"));
        var e = ctx.Econ; int row = e.Items.IndexOf(c.Item);
        if (row < 0 || e.Items.At(row).Owner != ctx.Party.Hero) return R(c, HeroResultCode.Invalid, T("HERO_DO_NOT_HAVE"));
        if ((e.Items.At(row).Flags & ItemFlags.Stolen) != 0) return R(c, HeroResultCode.Refused, T("HERO_I_DON_T_BUY"));
        long bid = ItemPricing.Bid(ItemValue(ctx, row), b.Money.SpreadHalfPermille, ctx.Party.Inspected.Contains(c.Item), ctx.Trade.SellUnknownConditionPm);
        if (bid <= 0) return R(c, HeroResultCode.Refused, T("HERO_WORTH_NOTHING_THEM"));
        if (c.Limit > 0 && bid < c.Limit) return R(c, HeroResultCode.PriceMoved, T("HERO_THEY_NOW_OFFER", Quotes.Money(bid)), bid);
        foreach (var m in b.Merchants)
        {
            if (e.Ledger.Balance(m.Purse).Mites < bid) continue;
            if (!e.Ledger.Transfer(m.Purse, ctx.Purse, Coin.FromMites(bid), TransferReason.Trade)) continue;
            foreach (int k in ctx.Party.Carried.Rows(e).ToArray()) if (e.Items.At(k).Parent == row) e.Items.At(k).Parent = -1; // its contents stay with the hero
            ref var it = ref e.Items.At(row); it.Owner = m.Id; it.Parent = -1;
            Deal(ctx, c);
            return R(c, HeroResultCode.Ok, T("HERO_SOLD", ctx.Rules.Name(it.Shape).ToLowerInvariant(), Quotes.Money(bid)), bid, 1);
        }
        return R(c, HeroResultCode.TheyCannotAfford, T("HERO_NO_MERCHANT_HAS", Quotes.Money(bid)));
    }

    private HeroResult BuyItem(HeroContext ctx, HeroCommand c)
    {
        var b = ctx.Book(c.Settlement); if (b == null) return R(c, HeroResultCode.NoMarket, T("HERO_THERE_NO_MARKET_HERE"));
        var e = ctx.Econ; int row = e.Items.IndexOf(c.Item);
        var seller = row < 0 ? null : b.Merchants.FirstOrDefault(m => m.Id == e.Items.At(row).Owner);
        if (seller == null) return R(c, HeroResultCode.NoStock, T("HERO_NO_LONGER_SALE"));
        long ask = ItemPricing.Ask(ItemValue(ctx, row), b.Money.SpreadHalfPermille);
        if (c.Limit > 0 && ask > c.Limit) return R(c, HeroResultCode.PriceMoved, T("HERO_NOW_COSTS", Quotes.Money(ask)), ask);
        if (!e.Ledger.Transfer(ctx.Purse, seller.Purse, Coin.FromMites(ask), TransferReason.Trade)) return R(c, HeroResultCode.CannotAfford, T("HERO_CANNOT_AFFORD", Quotes.Money(ask)), ask);
        ref var it = ref e.Items.At(row); it.Owner = ctx.Party.Hero; it.Parent = -1;
        ctx.Party.Inspected.Add(c.Item);
        Deal(ctx, c);
        return R(c, HeroResultCode.Ok, T("HERO_BOUGHT_2", ctx.Rules.Name(it.Shape).ToLowerInvariant(), Quotes.Money(ask)), ask, 1);
    }

    private HeroResult Equip(HeroContext ctx, HeroCommand c)
    {
        var e = ctx.Econ; var item = c.Item;
        if (item == EntityId.None && c.Good != ItemTable.NoGood)
        {
            // a counted piece with a shape (a knife bought at market) becomes an item of its own when it is taken in hand (Materialise: a counted sink, the row is the thing)
            var made = ctx.Factory.Materialise(ctx.Pack, e.Items, ctx.State.Ids, c.Good, ctx.Party.Hero);
            if (made == null) return R(c, HeroResultCode.Invalid, T("HERO_CANNOT_BE_WORN_OR"));
            item = made.Value;
        }
        return ctx.Party.Carried.EquipItem(e, ctx.Rules, item, c.Slot) ? R(c, HeroResultCode.Ok, T("HERO_DONE")) : R(c, HeroResultCode.Invalid, T("HERO_CANNOT_BE_WORN_OR"));
    }

    private HeroResult Inspect(HeroContext ctx, HeroCommand c)
    {
        int row = ctx.Econ.Items.IndexOf(c.Item); if (row < 0) return R(c, HeroResultCode.Invalid, T("HERO_NOTHING_THERE"));
        ctx.Party.Inspected.Add(c.Item);
        var it = ctx.Econ.Items.At(row);
        return R(c, HeroResultCode.Ok, T("HERO_CONDITION_WORTH_ABOUT", ctx.Rules.Name(it.Shape), it.Quality, it.ConditionPermille / 10, Quotes.Money(ItemValue(ctx, row))), ItemValue(ctx, row));
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

    private HeroResult ViewPerson(HeroCommand c) { _person = c.Person; return R(c, HeroResultCode.Ok, HeroText.Empty); }

    /// <summary>
    /// A toll or bribe on the road (Phase 3 cleanup B, item 1): <c>CoinGive</c> mites move from the hero's purse through the ledger to the household of <c>Person</c> (a real record, so the coin stays in the
    /// world), or, when no one real is named, to the first town's toll purse as <c>Toll</c>. Never more than the hero holds; nothing is minted or destroyed.
    /// </summary>
    private HeroResult EncounterPay(HeroContext ctx, HeroCommand c)
    {
        long want = Math.Max(0, c.CoinGive);
        if (want == 0) return R(c, HeroResultCode.Invalid, T("HERO_NOTHING_PAY"));
        if (ctx.Coin < want) return R(c, HeroResultCode.CannotAfford, T("HERO_CANNOT_PAY_HAVE", Quotes.Money(want), Quotes.Money(ctx.Coin)), want);
        var e = ctx.Econ; PurseId? to = null;
        if (c.Person != EntityId.None) { var (i, hh) = PersonOf(ctx, c.Person); if (i >= 0 && hh >= 0 && hh < e.HouseholdCount) to = e.HouseholdPurse[hh]; }
        if (to == null)
        {
            var b = ctx.Markets is { } ms ? ms.AllBooks().FirstOrDefault() : null;
            to = b?.TollPurse;
        }
        if (to == null) return R(c, HeroResultCode.Invalid, T("HERO_THERE_NO_ONE_PAY"));
        if (!e.Ledger.Transfer(ctx.Purse, to.Value, Coin.FromMites(want), TransferReason.Toll)) return R(c, HeroResultCode.CannotAfford, T("HERO_CANNOT_PAY"), want);
        return R(c, HeroResultCode.Ok, T("HERO_PAY", Quotes.Money(want)), want);
    }

    /// <summary>The hero takes a job (review finding 6: it used to reach the job table through the view). Claimed at the second it was asked, in sequence order.</summary>
    private HeroResult JobClaim(HeroContext ctx, HeroCommand c)
    {
        if (ctx.Jobs?.Invoke() is not { } jt) return R(c, HeroResultCode.Invalid, T("HERO_THERE_NO_WORK_BOARD"));
        var res = jt.Table.ClaimAll(jt.Env, [new ClaimRequest(c.Job, ctx.Party.Hero, -1, c.IssuedSec, (int)c.Seq, c.X, c.Y, 20, 25)], c.IssuedSec, ctx.Seed);
        return res.Count > 0 && res[0].Result == ClaimResult.Taken ? R(c, HeroResultCode.Ok, HeroText.Empty) : R(c, HeroResultCode.Refused, HeroText.Empty);
    }

    /// <summary>The hero walks away from his own job: it fails, pays the hours worked and returns the rest of the escrow.</summary>
    private HeroResult JobAbandon(HeroContext ctx, HeroCommand c)
    {
        if (ctx.Jobs?.Invoke() is not { } jt) return R(c, HeroResultCode.Invalid, T("HERO_THERE_NO_WORK_BOARD"));
        if (jt.Table.Get(c.Job) is not { Live: true } j || j.Worker != ctx.Party.Hero) return R(c, HeroResultCode.Invalid, HeroText.Empty);
        return jt.Table.Abandon(jt.Env, c.Job, c.IssuedSec) ? R(c, HeroResultCode.Ok, HeroText.Empty) : R(c, HeroResultCode.Invalid, HeroText.Empty);
    }

    /// <summary>The real caravan a person belongs to (master or crew of an active caravan of the hero's region), with its cargo chest and account purse.</summary>
    private static (Ravensguard.Sim.Caravans.Caravan Caravan, Store Store, PurseId Purse)? CaravanTrader(HeroContext ctx, int row)
    {
        var cont = ctx.State.Regions?.Services.AbstractContinent; if (cont == null || ctx.State.Regions == null) return null;
        int home = ctx.State.Regions.Home.Id;
        foreach (var car in cont.Caravans.All)
            if (car.Home == home && car.Active && car.Account != PurseId.None && (car.Master == row || Array.IndexOf(car.Crew, row) >= 0))
                return (car, ctx.Econ.OpenStore(StoreKind.Chest, car.Id, 0, 0, enforce: false), car.Account);
        return null;
    }

    private HeroResult Talk(HeroContext ctx, HeroCommand c)
    {
        var (i, _) = PersonOf(ctx, c.Person); if (i < 0) return R(c, HeroResultCode.Invalid, T("HERO_THEY_ARE_GONE"));
        _person = c.Person;
        var k = ctx.Party.Know(c.Person);
        if (k.LastTalkHour != c.Hour) { k.Familiarity = Math.Min(100, k.Familiarity + 10); k.TrustPm = Math.Min(1000, k.TrustPm + 25); k.LastTalkHour = c.Hour; }
        if (k.Familiarity >= ctx.Trade.RecruitFamiliarityMin) k.ReasonKnown = true;
        Progress.Use(ctx.Craft.State.Book, ctx.Party.Hero, Progress.Talk, 40, c.IssuedSec);
        string name = ctx.State.Population.People.FullName(i);
        return R(c, HeroResultCode.Ok, k.ReasonKnown ? T("HERO_TALK_KNOW_WHAT_WOULD", name) : T("HERO_TALK_WHILE", name));
    }

    private HeroResult Hire(HeroContext ctx, HeroCommand c)
    {
        var (i, hh) = PersonOf(ctx, c.Person); if (i < 0) return R(c, HeroResultCode.Invalid, T("HERO_THEY_ARE_GONE"));
        if (ctx.Party.Squad.Find(c.Person) != null) return R(c, HeroResultCode.Invalid, T("HERO_THEY_ALREADY_WORK"));
        int day = (int)(c.IssuedSec / 86400); var info = Candidate(ctx, i, c.Person, day);
        var (ans, score, res) = Hiring.Ask(ctx.Trade, ctx.Seed, day, ctx.WageU, info.C, c.Wage, c.Danger, c.Terms);
        string name = ctx.State.Population.People.FullName(i);
        if (ans != HireAnswer.Accept)
            return R(c, HeroResultCode.Refused, ans switch { HireAnswer.TooYoung => T("HERO_TOO_YOUNG_HIRE", name), HireAnswer.Unavailable => T("HERO_CANNOT_BE_HIRED", name), _ => T("HERO_WILL_NOT_WORK_DAY", name, Quotes.Money(c.Wage), Quotes.Money(Hiring.FairOffer(ctx.Trade, res, info.C))) }, res);
        ctx.Party.Squad.Members.Add(new Follower
        {
            Person = c.Person, PersonIndex = i, Name = name, Origin = FollowerOrigin.Hired, Loyalty = Recruiting.LoyaltyStart(ctx.Trade, info.C.TrustPm), JoinedDay = day, EndDay = day + Math.Clamp(c.Days, 1, 30),
            WageMitesPerDay = c.Wage, Terms = c.Terms, PaidThroughDay = day, Order = SquadOrder.Follow, MaxSkill = info.C.MaxSkill, Trade = info.Trade,
            Household = hh >= 0 ? ctx.State.Population.Households.IdOf(hh) : EntityId.None,
        });
        var k = ctx.Party.Know(c.Person); k.Familiarity = Math.Min(100, k.Familiarity + 5);
        return R(c, HeroResultCode.Ok, T("HERO_AGREES_DAYS_DAY", name, Math.Clamp(c.Days, 1, 30), Quotes.Money(c.Wage)), c.Wage, score);
    }

    private HeroResult Recruit(HeroContext ctx, HeroCommand c)
    {
        var (i, hh) = PersonOf(ctx, c.Person); if (i < 0) return R(c, HeroResultCode.Invalid, T("HERO_THEY_ARE_GONE"));
        if (ctx.Party.Squad.Find(c.Person) is { Origin: FollowerOrigin.Recruited }) return R(c, HeroResultCode.Invalid, T("HERO_THEY_ARE_ALREADY"));
        int day = (int)(c.IssuedSec / 86400); var info = Candidate(ctx, i, c.Person, day);
        int leadership = ctx.Craft.State.Book.Level(ctx.Party.Hero, Skill.Leadership);
        long wage = c.Wage > 0 ? c.Wage : Hiring.FairOffer(ctx.Trade, Hiring.ReservationWage(ctx.Trade, ctx.WageU, info.C.MaxSkill, info.C.TrustPm), info.C);
        int size = ctx.Party.Squad.Members.Count(m => m.Active && m.Person != c.Person);
        var ans = Recruiting.Check(ctx.Trade, info.Known.Familiarity, info.Known.TrustPm, info.Known.ReasonKnown, size, leadership, ctx.Coin, wage, info.C.Alive && info.C.Age >= 14);
        string name = ctx.State.Population.People.FullName(i);
        if (ans != RecruitAnswer.Accept)
            return R(c, HeroResultCode.Refused, ans switch
            {
                RecruitAnswer.NotFamiliar => T("HERO_HARDLY_KNOWS", name), RecruitAnswer.NoTrust => T("HERO_DOES_NOT_TRUST_YET", name), RecruitAnswer.NoReason => T("HERO_TALK_FIRST_DO_NOT", name),
                RecruitAnswer.SquadFull => T("HERO_YOUR_PARTY_AS_LARGE"), RecruitAnswer.CannotPay => T("HERO_CANNOT_PAY_SIGNING_BONUS", Quotes.Money(Recruiting.SigningBonus(ctx.Trade, wage))), _ => T("HERO_CANNOT_JOIN", name),
            });
        long bonus = Recruiting.SigningBonus(ctx.Trade, wage);
        var e = ctx.Econ;
        if (bonus > 0)
        {
            var to = hh >= 0 && hh < e.HouseholdCount ? e.HouseholdPurse[hh] : e.Ledger.Open(PurseKind.Person, c.Person);
            if (!e.Ledger.Transfer(ctx.Purse, to, Coin.FromMites(bonus), TransferReason.Wage)) return R(c, HeroResultCode.CannotAfford, T("HERO_CANNOT_PAY_SIGNING_BONUS_2"));
        }
        ctx.Party.Squad.Members.RemoveAll(m => m.Person == c.Person);   // a hired hand who signs on for good
        ctx.Party.Squad.Members.Add(new Follower
        {
            Person = c.Person, PersonIndex = i, Name = name, Origin = FollowerOrigin.Recruited, Loyalty = Recruiting.LoyaltyStart(ctx.Trade, info.Known.TrustPm), JoinedDay = day,
            WageMitesPerDay = wage, PaidThroughDay = day, Order = SquadOrder.Follow, MaxSkill = info.C.MaxSkill, Trade = info.Trade, Household = hh >= 0 ? ctx.State.Population.Households.IdOf(hh) : EntityId.None,
        });
        var hht = ctx.State.Population.Households;
        bool earner = hh >= 0 && hh < hht.Count && hht.MemberCount[hh] > 1;
        return R(c, HeroResultCode.Ok, earner ? T("HERO_JOINS_LOSES_EARNER", name, Quotes.Money(wage), Quotes.Money(bonus)) : T("HERO_JOINS_DAY_SIGNING_BONUS", name, Quotes.Money(wage), Quotes.Money(bonus)), bonus);
    }

    private HeroResult Dismiss(HeroContext ctx, HeroCommand c)
    {
        var f = ctx.Party.Squad.Find(c.Person); if (f == null) return R(c, HeroResultCode.Invalid, T("HERO_THEY_ARE_NOT"));
        long sev = Recruiting.Severance(ctx.Trade, f.WageMitesPerDay); var e = ctx.Econ;
        var (_, hh) = PersonOf(ctx, c.Person);
        // what was actually paid (review finding 9): what the hero could afford, and nothing when the transfer did not go through or there is no household to pay
        long paid = 0, give = Math.Min(sev, ctx.Coin);
        if (give > 0 && hh >= 0 && hh < e.HouseholdCount && e.Ledger.Transfer(ctx.Purse, e.HouseholdPurse[hh], Coin.FromMites(give), TransferReason.Wage)) paid = give;
        f.State = HireState.Dismissed;
        ctx.Party.Squad.Members.Remove(f);
        HeroText text = sev <= 0 ? T("HERO_GOES_HOME", f.Name) : paid >= sev ? T("HERO_GOES_HOME_SEVERANCE", f.Name, Quotes.Money(paid))
            : paid > 0 ? T("HERO_GOES_HOME_SEVERANCE_OWED", f.Name, Quotes.Money(paid), Quotes.Money(sev)) : T("HERO_GOES_HOME_WITHOUT_SEVERANCE", f.Name, Quotes.Money(sev));
        return R(c, HeroResultCode.Ok, text, paid);
    }

    private HeroResult Order(HeroContext ctx, HeroCommand c)
    {
        var f = ctx.Party.Squad.Find(c.Person); if (f == null) return R(c, HeroResultCode.Invalid, T("HERO_THEY_ARE_NOT"));
        f.Order = c.Order; f.OrderX = c.X; f.OrderY = c.Y; f.OrderJob = EntityId.None;
        if (c.Order == SquadOrder.Work)
        {
            if (ctx.Jobs?.Invoke() is not { } jt) return R(c, HeroResultCode.Invalid, T("HERO_THERE_NO_WORK_BOARD"));
            var job = c.Job != EntityId.None ? c.Job : jt.Table.Jobs.Values.Where(j => j.State == JobState.Open).OrderBy(j => Math.Abs(j.P0 - c.X) + Math.Abs(j.P1 - c.Y)).ThenBy(j => j.Id).Select(j => j.Id).FirstOrDefault();
            if (job == EntityId.None) { f.Order = SquadOrder.Follow; return R(c, HeroResultCode.NoStock, T("HERO_THERE_NO_OPEN_WORK")); }
            var p = ctx.State.Population.People; int day = (int)(c.IssuedSec / 86400);
            var res = jt.Table.ClaimAll(jt.Env, [new ClaimRequest(job, f.Person, f.PersonIndex, c.IssuedSec, (int)c.Seq, c.X, c.Y, f.MaxSkill, p.AgeYears(f.PersonIndex, day))], c.IssuedSec, ctx.Seed);
            if (res.Count == 0 || res[0].Result != ClaimResult.Taken) { f.Order = SquadOrder.Follow; return R(c, HeroResultCode.Refused, T("HERO_COULD_NOT_TAKE_WORK", f.Name, (res.Count > 0 ? res[0].Result : ClaimResult.NoSuchJob))); }
            f.OrderJob = job;
            return R(c, HeroResultCode.Ok, T("HERO_GOES_WORK", f.Name, jt.Table.Jobs[job].Title));
        }
        return R(c, HeroResultCode.Ok, T("HERO_ORDER", f.Name, T("HERO_ORDER_" + c.Order.ToString().ToUpperInvariant())));
    }

    /// <summary>Barter lines summed by good, ascending (review finding 4: a good named twice was checked against the whole pack twice and credited twice). False on a sum that overflows.</summary>
    internal static bool Coalesce((ushort Good, long Qty)[] lines, out List<(ushort Good, long Qty)> res)
    {
        var sum = new SortedDictionary<ushort, long>();
        res = [];
        try { foreach (var (g, q) in lines) sum[g] = checked(sum.GetValueOrDefault(g) + q); }
        catch (OverflowException) { return false; }
        foreach (var (g, q) in sum) res.Add((g, q));
        return true;
    }

    private HeroResult DoBarter(HeroContext ctx, HeroCommand c)
    {
        var (i, hh) = PersonOf(ctx, c.Person); var e = ctx.Econ;
        if (i < 0 || hh < 0 || hh >= e.HouseholdCount) return R(c, HeroResultCode.Invalid, T("HERO_THEY_HAVE_NO_HOUSEHOLD"));
        if (!Coalesce(c.Give, out var give) || !Coalesce(c.Take, out var take)) return R(c, HeroResultCode.Invalid, T("HERO_MAKES_NO_SENSE"));
        var items = c.GiveItems.Distinct().OrderBy(x => x).ToArray();
        var book = ctx.Book(c.Settlement >= 0 ? c.Settlement : ctx.State.Population.People.HomeSettlement[i]);
        var store = e.HouseholdStore[hh]; var purse = e.HouseholdPurse[hh];
        var van = CaravanTrader(ctx, i); if (van is { } v0) { store = v0.Store; purse = v0.Purse; }       // a caravan's people trade the caravan's own cargo and float (Phase 3 cleanup B, item 1)
        bool survive = van == null && e.Tiers[hh] == HouseholdTier.Survive;
        long Val(ushort g, long q) => book == null ? (long)((Int128)e.Goods.BasePriceMilliMites(g) * q / (e.Goods.IsPiece(g) ? 1000 : 1_000_000)) : (long)((Int128)book.MidMicro(g) * q / PriceCurve.Micro);
        int Want(ushort g) => survive && e.Goods.FoodMeasuresPpm(g) > 0 ? 1 : 0;
        var pack = ctx.Pack;
        long received = c.CoinGive, given = c.CoinTake;
        foreach (var (g, q) in give) { if (pack.Qty(g) < q) return R(c, HeroResultCode.Invalid, T("HERO_DO_NOT_HAVE_MUCH")); received += Barter.Weighted(Val(g, q), Want(g), ctx.Trade.WantPm); }
        foreach (var id in items) { int row = e.Items.IndexOf(id); if (row < 0 || e.Items.At(row).Owner != ctx.Party.Hero) return R(c, HeroResultCode.Invalid, T("HERO_DO_NOT_HAVE")); received += ItemValue(ctx, row); }
        foreach (var (g, q) in take) { if (store.Qty(g) < q) return R(c, HeroResultCode.NoStock, T("HERO_THEY_DO_NOT_HAVE")); given += Barter.Weighted(Val(g, q), Want(g), ctx.Trade.WantPm); }
        if (c.CoinTake > 0 && e.Ledger.Balance(purse).Mites < c.CoinTake) return R(c, HeroResultCode.TheyCannotAfford, T("HERO_THEY_HAVE_ONLY_2", Quotes.Money(e.Ledger.Balance(purse).Mites)));
        if (c.CoinGive > 0 && ctx.Coin < c.CoinGive) return R(c, HeroResultCode.CannotAfford, T("HERO_DO_NOT_HAVE_COIN"));
        string name = ctx.State.Population.People.FullName(i);
        if (!Barter.Accepts(received, given)) return R(c, HeroResultCode.Refused, T("HERO_SHAKES_THEIR_HEAD_WORTH", name, Quotes.Money(received), Quotes.Money(given)), received);
        // every move and transfer is checked; the first that fails undoes the ones before it, so the exchange happens whole or not at all (review finding 4)
        var moved = new List<(Store From, Store To, ushort Good, long Qty)>(); var paid = new List<(PurseId From, PurseId To, long Mites)>();
        bool ok = true;
        foreach (var (g, q) in give) { if (!(ok = Store.Move(pack, store, g, q))) break; moved.Add((pack, store, g, q)); }
        if (ok) foreach (var (g, q) in take) { if (!(ok = Store.Move(store, pack, g, q))) break; moved.Add((store, pack, g, q)); }
        if (ok && c.CoinGive > 0) { if (ok = e.Ledger.Transfer(ctx.Purse, purse, Coin.FromMites(c.CoinGive), TransferReason.Trade)) paid.Add((ctx.Purse, purse, c.CoinGive)); }
        if (ok && c.CoinTake > 0) { if (ok = e.Ledger.Transfer(purse, ctx.Purse, Coin.FromMites(c.CoinTake), TransferReason.Trade)) paid.Add((purse, ctx.Purse, c.CoinTake)); }
        if (!ok)
        {
            for (int k = paid.Count - 1; k >= 0; k--) e.Ledger.Transfer(paid[k].To, paid[k].From, Coin.FromMites(paid[k].Mites), TransferReason.Trade);
            for (int k = moved.Count - 1; k >= 0; k--) Store.Move(moved[k].To, moved[k].From, moved[k].Good, moved[k].Qty);
            return R(c, HeroResultCode.Invalid, T("HERO_EXCHANGE_FELL_THROUGH_NOTHING"));
        }
        foreach (var id in items) { ref var it = ref e.Items.Get(id); it.Owner = ctx.State.Population.People.IdOf(i); it.Parent = -1; }
        foreach (var (g, _) in take) { ctx.Party.Carried.Stow(e, ctx.Rules, g); ctx.Party.Traded.Add(g); }
        if (van is { } v1) foreach (var (g, _) in take) foreach (var line in v1.Caravan.Cargo) if (line.Good == g) line.Units = Math.Min(line.Units, store.Qty(g));
        Deal(ctx, c);
        var k2 = ctx.Party.Know(c.Person); k2.Familiarity = Math.Min(100, k2.Familiarity + 2);
        return R(c, HeroResultCode.Ok, T("HERO_AGREES_EXCHANGE", name), received - given);
    }

    // ------------------------------------------------------------------ crafting (the real world's CraftState, on the world clock)

    private static HeroText CraftText(HeroContext ctx, CraftEvent e) => e.Kind switch
    {
        CraftEventKind.Finished => T("HERO_FINISHED", e.Text.Replace('_', ' ')), CraftEventKind.Ready => T("HERO_YOUR_WORK_READY_COLLECT"), CraftEventKind.PastDeadline => T("HERO_YOUR_WORK_PAST_ITS"),
        CraftEventKind.Ruining => T("HERO_YOUR_WORK_ABOUT_SPOIL"), CraftEventKind.Ruined => T("HERO_YOUR_WORK_WAS_SPOILED"), CraftEventKind.Failed => T("HERO_WORK_WENT_WRONG"), CraftEventKind.Waiting => T("HERO_LEFT_REST_COME_BACK"),
        CraftEventKind.SkillGain => HeroText.Empty, CraftEventKind.StepDone => HeroText.Empty, _ => T("HERO_RAW", e.Text),
    };

    private HeroResult CraftEvents(HeroContext ctx, HeroCommand c, List<CraftEvent> ev, HeroText fallback)
    {
        foreach (var x in ev) if (x.Kind == CraftEventKind.Finished) foreach (var (g, _) in ctx.Pack.Rows()) ctx.Party.Carried.Stow(ctx.Econ, ctx.Rules, g);
        var texts = ev.Select(x => CraftText(ctx, x)).Where(t => !t.IsEmpty).ToList();
        return R(c, HeroResultCode.Ok, texts.Count > 0 ? HeroText.Join(texts) : fallback);
    }

    private HeroResult CraftStart(HeroContext ctx, HeroCommand c)
    {
        if (c.Recipe >= ctx.Recipes.Recipes.Count) return R(c, HeroResultCode.Invalid, T("HERO_NO_SUCH_RECIPE"));
        var r = ctx.Recipes[c.Recipe];
        if (!ctx.Craft.State.Know.Knows(r, ctx.Party.Hero, ctx.Craft.State.Book)) return R(c, HeroResultCode.NotKnown, T("HERO_DO_NOT_KNOW_HOW"));
        var site = (c.Site ?? new CraftSiteSpec(255, true, [], [], [])).Build(ctx.Recipes);
        foreach (var t in ctx.PackTools()) site.AddTool(ctx.Recipes, t);                      // P5-A4: the tools really in the pack count, whatever the screen sent
        var plan = ctx.Craft.Plan(r, ctx.Actor(), site, ctx.Pack, Math.Max(1, c.Batch), c.Choice);
        if (!plan.CanStart) return R(c, HeroResultCode.NoStock, T("HERO_LACK_WHAT_NEEDS"));
        var job = ctx.Craft.Start(plan, ctx.Actor(), ctx.Party.Hero, ctx.Pack, c.IssuedSec, c.X, c.Y);
        if (job == null) return R(c, HeroResultCode.NoStock, T("HERO_LACK_WHAT_NEEDS"));
        ctx.Party.WorkedTo[job.Id] = c.IssuedSec;
        return R(c, HeroResultCode.Ok, T("HERO_START", r.Name), 0, (long)job.Id.Value);
    }

    private HeroResult CraftWork(HeroContext ctx, HeroCommand c)
    {
        var j = ctx.Craft.State.Get(c.Job);
        if (j == null || j.Closed || j.Owner != ctx.Party.Hero) return R(c, HeroResultCode.Invalid, HeroText.Empty);
        long from = ctx.Party.WorkedTo.GetValueOrDefault(j.Id, c.UntilSec);
        if (j.State != CraftJobState.Active || c.UntilSec <= from) { ctx.Party.WorkedTo[j.Id] = Math.Max(from, c.UntilSec); return R(c, HeroResultCode.Ok, HeroText.Empty); }
        long worked = c.UntilSec - from;
        var ev = ctx.Craft.Advance(j, worked, from);
        WearTools(ctx, ctx.Recipes[j.Recipe], worked);
        ctx.Party.WorkedTo[j.Id] = c.UntilSec;
        if (j.Closed) ctx.Party.WorkedTo.Remove(j.Id);
        return CraftEvents(ctx, c, ev, HeroText.Empty);
    }

    /// <summary>
    /// P5-A1 (items-quality-and-wear 4.3): the attended seconds wear the pack's tool rows the recipe used (for each tool quality, the first row in pack order whose shape is a craft tool with
    /// a wear rate for it); a tool worn to 0 breaks into scrap in the pack. Tools at a station and in a business are P5-E's (crafting-full 4.1).
    /// </summary>
    private static void WearTools(HeroContext ctx, Ravensguard.Sim.Crafting.CraftRecipe r, long seconds)
    {
        if (seconds <= 0 || r.Tools.Length == 0) return;
        var e = ctx.Econ; var rows = ctx.Party.Carried.Rows(e).ToList();
        foreach (var need in r.Tools)
            foreach (int row in rows)
            {
                if (e.Items.At(row).Id == EntityId.None || ctx.ToolWear.MilliPerHour(e.Items.At(row).Shape, need.Quality) <= 0) continue;
                if (ctx.ToolWear.Use(e.Items, row, need.Quality, seconds, ctx.Pack, ctx.MaterialCategory))
                {
                    var gone = ctx.Party.Carried.Equip.Keys.Where(k => !e.Items.IsLive(k)).ToList();
                    foreach (var k in gone) ctx.Party.Carried.Equip.Remove(k);
                }
                break;
            }
    }

    private HeroResult CraftPause(HeroContext ctx, HeroCommand c)
    {
        var j = ctx.Craft.State.Get(c.Job); if (j == null || j.Owner != ctx.Party.Hero) return R(c, HeroResultCode.Invalid, HeroText.Empty);
        ctx.Craft.Pause(j); return R(c, HeroResultCode.Ok, T("HERO_PUT_DOWN_WORK_KEEPS"));
    }

    private HeroResult CraftResume(HeroContext ctx, HeroCommand c)
    {
        var j = ctx.Craft.State.Get(c.Job); if (j == null) return R(c, HeroResultCode.Invalid, HeroText.Empty);
        if (!ctx.Craft.Resume(j, ctx.Actor())) return R(c, HeroResultCode.Refused, T("HERO_CANNOT_TAKE_UP"));
        ctx.Party.WorkedTo[j.Id] = c.IssuedSec;
        return R(c, HeroResultCode.Ok, T("HERO_BACK_WORK"));
    }

    private HeroResult CraftCollect(HeroContext ctx, HeroCommand c)
    {
        var j = ctx.Craft.State.Get(c.Job); if (j == null) return R(c, HeroResultCode.Invalid, HeroText.Empty);
        var (ok, ev) = ctx.Craft.Collect(j, ctx.Actor(), c.IssuedSec);
        if (!ok) return R(c, HeroResultCode.Refused, T("HERO_NOT_READY_YET"));
        ctx.Party.WorkedTo[j.Id] = c.IssuedSec;
        return CraftEvents(ctx, c, ev, T("HERO_COLLECTED"));
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
        if (_market >= 0 && _marketRegion >= 0 && AbroadDealing.BookOf(ctx, _marketRegion, _market) is { } ab)
        {
            var lines = new List<BookLine>();
            for (ushort g = 0; g < ab.Goods.Length; g++) lines.Add(BookLine.Of(ab, g));
            mv = new MarketView(_market, ab.Name, ab.IsOpen((int)(hourDone % 24)), (int)(hourDone / 24), lines, MerchantCash(ab), [], _marketRegion);
        }
        else if (_market >= 0 && _marketRegion < 0 && ctx.Book(_market) is { } mb)
        {
            var lines = new List<BookLine>();
            for (ushort g = 0; g < mb.Goods.Length; g++) lines.Add(BookLine.Of(mb, g));
            mv = new MarketView(_market, mb.Name, mb.IsOpen((int)(hourDone % 24)), (int)(hourDone / 24), lines, MerchantCash(mb), MerchantItems(ctx, mb, hourDone));
        }
        if (mv == null && _market < 0 && _person != EntityId.None) mv = new MarketView(-1, "", true, (int)(hourDone / 24), [], 0, []);     // the road: barter with a person, no stalls
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
            Crafting = CraftCopy(ctx, hourDone), Results = [.. _results], Family = FamilyOf(ctx, (int)(hourDone / 24)),
        };
    }

    /// <summary>The hero's kin rows, in the order the succession book keeps them.</summary>
    private static List<KinView> FamilyOf(HeroContext ctx, int day)
    {
        var list = new List<KinView>();
        var pt = ctx.State.Population.People; var region = ctx.State.Population.Region;
        foreach (var k in ctx.State.Succession?.Kin ?? [])
        {
            int i = pt.IndexOf(k.Person); if (i < 0) continue;
            bool alive = pt.Status[i] == PersonStatus.Alive;
            int age = alive ? pt.AgeYears(i, day) : pt.DeathDay[i] >= 0 ? pt.AgeYears(i, pt.DeathDay[i]) : pt.AgeYears(i, day);
            int s = pt.HomeSettlement[i];
            list.Add(new KinView(pt.FullName(i), k.Relation, age, alive, s >= 0 && s < region.Settlements.Count ? region.Settlements[s].Name : ""));
        }
        return list;
    }

    // ------------------------------------------------------------------ the view's heavy parts, kept between barriers (review finding 15)

    private (int Market, long Day, long Seq, int Items) _merchKey = (-1, -1, -1, -1);
    private IReadOnlyList<CarriedItem> _merchItems = [];
    private CraftState? _craftCopy;
    private long _craftDay = -1;
    private bool _craftDirty = true;
    /// <summary>Full scans of the item rows for merchants' stock, and deep copies of the crafting state (tests count them).</summary>
    public long MerchantScans { get; private set; }
    public long CraftCopies { get; private set; }
    /// <summary>
    /// Whether a screen that shows the crafting state is open (the game sets it; null: assume it is). While it is not and nothing of the hero's crafting has changed, the view keeps the copy
    /// it had, so a barrier does not deep-copy the whole crafting state for nobody.
    /// </summary>
    public Func<bool>? CraftViewWanted { get; set; }

    /// <summary>
    /// What the merchants of the book have for sale: one pass over the item rows, indexed by owner, made again only when the market, the day, the hero's last command or the number of
    /// rows changed (a sale through the desk is a command; the world's own item trades show by the next day).
    /// </summary>
    private IReadOnlyList<CarriedItem> MerchantItems(HeroContext ctx, MarketBook mb, long hourDone)
    {
        var e = ctx.Econ;
        var key = (mb.SettlementIndex, hourDone / 24, ctx.Party.LastSeq, e.Items.Count);
        if (key == _merchKey) return _merchItems;
        MerchantScans++;
        var byOwner = new Dictionary<EntityId, List<int>>();
        foreach (var m in mb.Merchants) byOwner.TryAdd(m.Id, []);
        foreach (int r in e.Items.LiveRows()) if (byOwner.TryGetValue(e.Items.At(r).Owner, out var l)) l.Add(r);
        var res = new List<CarriedItem>();
        foreach (int r in byOwner.Values.SelectMany(l => l).Order())
        {
            var row = e.Items.At(r);
            res.Add(new CarriedItem(row.Id, row.Shape, ctx.Rules.Name(row.Shape), row.Quality, row.ConditionPermille, e.Items.MassG(r, e.Goods), ctx.Rules.VolumeMl(e.Items, r), EntityId.None, EquipSlot.None,
                ItemValue(ctx, r), ctx.Rules.IsContainer(row.Shape), 0, 0, row.Flags, 1));
        }
        _merchKey = key; _merchItems = res;
        return res;
    }

    /// <summary>The crafting state for the view: copied again when a command was applied, the hourly pass moved a job of the hero's, the day turned, or there is none yet; else the last copy.</summary>
    private CraftState CraftCopy(HeroContext ctx, long hourDone)
    {
        bool wanted = CraftViewWanted?.Invoke() ?? true;
        bool stale = _craftCopy == null || _craftDirty || hourDone / 24 != _craftDay || wanted && HasOpenWork(ctx);
        if (!stale) return _craftCopy!;
        CraftCopies++;
        _craftCopy = CraftingSave.Capture(ctx.Craft.State).Restore(); _craftDay = hourDone / 24; _craftDirty = false;
        return _craftCopy;
    }

    private static bool HasOpenWork(HeroContext ctx)
    {
        foreach (var j in ctx.Craft.State.Jobs.Values) if (!j.Closed && j.Owner == ctx.Party.Hero) return true;
        return false;
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
        if (CaravanTrader(ctx, i) is { } van) { goods = van.Store.Rows().Select(r => (r.Good, r.Qty)).ToList(); coin = e.Ledger.Balance(van.Purse).Mites; }
        int leadership = ctx.Craft.State.Book.Level(ctx.Party.Hero, Skill.Leadership);
        long fair = Hiring.FairOffer(ctx.Trade, res, info.C);
        var rc = Recruiting.Check(ctx.Trade, info.Known.Familiarity, info.Known.TrustPm, info.Known.ReasonKnown, ctx.Party.Squad.ActiveCount, leadership, ctx.Coin, fair, info.C.Alive && info.C.Age >= 14);
        return new PersonView(id, p.FullName(i), info.C.Age, info.Trade, info.C.MaxSkill, info.Known.Familiarity, info.Known.TrustPm, info.Known.ReasonKnown, goods, coin, res, fair, rc, info.C.SurviveTier,
            ctx.Party.Squad.Find(id) != null);
    }
}
