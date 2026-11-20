using System.Globalization;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Crafting;

/// <summary>A maker the Commission tab lists: who, his trade, his skills (by <see cref="Skill"/>), the stations of his workshop and the work already queued with him.</summary>
public sealed record MakerRow(EntityId Id, string Name, string Trade, byte[] Skills, string[] Stations, long BusySec);

/// <summary>A guild of the place for the screen: which, its master, the fee and dues, whether the hero is a member and how many days he has lived there.</summary>
public sealed record GuildRow(string Id, string Name, string Master, long FeeMites, long DuesMites, int MinSkill, string Skill, bool Member, int ResidentDays, int ResidenceDays);

/// <summary>The Phase 5 crafting the screen shows beyond the crafting state (built at a barrier): the place, its makers, its guilds and the hero's apprenticeship.</summary>
public sealed record CraftPlaceView(int Settlement, IReadOnlyList<MakerRow> Makers, IReadOnlyList<GuildRow> Guilds, string ApprenticeOf, int ApprenticeDaysLeft);

/// <summary>
/// The hero's side of the Phase 5 crafting at the barrier (crafting-full 4.5 to 4.8, P5-E): commission, collect and cancel; join a guild; take an apprenticeship and leave it; teach someone;
/// take a piece apart; feed a fire. The tag says which ("cf:verb|..."), so each is on the replay log, saved with the pending commands and applied in sequence order on the tick worker, as the
/// land desk does. The hourly and daily passes run the commissions, the fires, the apprenticeships and the guild dues.
/// </summary>
public static class CraftDesk
{
    public const string Prefix = "cf:";

    private static string Tag(params object[] parts) => Prefix + string.Join('|', parts.Select(p => Convert.ToString(p, CultureInfo.InvariantCulture)));
    private static HeroCommand Cmd(long sec, string tag, int settlement = -1, long limit = 0, EntityId person = default, EntityId item = default)
        => new() { Kind = HeroCommandKind.Craft, IssuedSec = sec, Tag = tag, Settlement = settlement, Limit = limit, Person = person, Item = item, Origin = CommandOrigin.Human };

    public static HeroCommand Look(int settlement, long sec) => Cmd(sec, Tag("look"), settlement);
    public static HeroCommand Order(CraftRecipe r, EntityId maker, bool clientMaterials, int batch, int[] choice, int settlement, long maxPrice, long sec)
        => Cmd(sec, Tag("order", r.Index, maker.Value, clientMaterials ? 1 : 0, batch, string.Join(',', choice)), settlement, maxPrice, maker);
    public static HeroCommand Collect(EntityId commission, EntityId maker, int settlement, long sec) => Cmd(sec, Tag("collect", commission.Value), settlement, 0, maker);
    public static HeroCommand Cancel(EntityId commission, long sec) => Cmd(sec, Tag("cancel", commission.Value));
    public static HeroCommand Join(string guild, int settlement, long sec) => Cmd(sec, Tag("join", guild), settlement);
    public static HeroCommand Apprentice(EntityId master, Skill trade, int settlement, long sec) => Cmd(sec, Tag("apprentice", master.Value, (int)trade), settlement, 0, master);
    public static HeroCommand Leave(long sec) => Cmd(sec, Tag("leave"));
    public static HeroCommand Teach(EntityId student, CraftRecipe r, int settlement, long sec) => Cmd(sec, Tag("teach", student.Value, r.Index), settlement, 0, student);
    public static HeroCommand TakeApart(EntityId item, int settlement, long sec) => Cmd(sec, Tag("apart", item.Value), settlement, 0, default, item);
    public static HeroCommand Feed(EntityId fire, long grams, long sec) => Cmd(sec, Tag("feed", fire.Value, grams));

    private static HeroResult R(HeroCommand c, HeroResultCode code, string key, params object[] args) => new(c.Seq, c.Kind, code, HeroText.Of(key, args), 0, 0, c.IssuedSec);

    /// <summary>The crafting world of a hero context (made once, kept on the context).</summary>
    public static CraftContext World(HeroContext ctx) => ctx.CraftContext;

    public static HeroResult Apply(HeroContext ctx, HeroCommand c)
    {
        if (!c.Tag.StartsWith(Prefix, StringComparison.Ordinal)) return R(c, HeroResultCode.Invalid, "HERO_UNKNOWN_COMMAND");
        var p = c.Tag[Prefix.Length..].Split('|');
        long N(int i) => i < p.Length && long.TryParse(p[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : long.MinValue;
        var w = ctx.CraftContext; var st = ctx.Craft.State; var hero = ctx.Party.Hero;
        int day = (int)(c.IssuedSec / 86400);
        if (c.Settlement >= 0) { ctx.CraftSettlement = c.Settlement; Guilds.SeeAt(st, hero, c.Settlement, day); }
        switch (p[0])
        {
            case "look": return R(c, HeroResultCode.Ok, "cf_looked");
            case "order" when p.Length >= 6 && N(1) >= 0 && N(1) < ctx.Recipes.Recipes.Count:
            {
                var r = ctx.Recipes[(ushort)N(1)];
                var maker = new EntityId((ulong)N(2));
                if (c.Settlement < 0 || w.SettlementOf(maker) != c.Settlement) return R(c, HeroResultCode.Refused, "cf_not_here");
                int[] choice = [.. p[5].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : -1)];
                var quote = Commissions.Quote(w, r, hero, maker, N(3) == 1, c.IssuedSec, (int)Math.Clamp(N(4), 1, 99), choice);
                if (quote == null) return R(c, HeroResultCode.Refused, "cf_maker_cannot");
                if (c.Limit > 0 && quote.PriceMites > c.Limit) return R(c, HeroResultCode.PriceMoved, "cf_price_moved", Quotes.Money(quote.PriceMites));
                var (ans, cm) = Commissions.Order(w, r, hero, maker, N(3) == 1, Sources(ctx), c.IssuedSec, ctx.State.Ids, (int)Math.Clamp(N(4), 1, 99), choice, CraftRunner.DefaultBodyCm3);
                return ans switch
                {
                    CommissionAnswer.Ok => new HeroResult(c.Seq, c.Kind, HeroResultCode.Ok, HeroText.Of("cf_ordered", r.Name, Quotes.Money(cm!.PriceMites), (cm.DueHour * 3600 - c.IssuedSec + 86399) / 86400), cm.Escrow, 0, c.IssuedSec),
                    CommissionAnswer.CannotAfford => R(c, HeroResultCode.CannotAfford, "cf_cannot_afford"),
                    CommissionAnswer.NoMaterials => R(c, HeroResultCode.NoStock, "cf_no_materials"),
                    _ => R(c, HeroResultCode.Refused, "cf_maker_cannot"),
                };
            }
            case "collect" when p.Length >= 2:
            {
                if (!st.Commissions.TryGetValue(new EntityId((ulong)N(1)), out var cm)) return R(c, HeroResultCode.Invalid, "HERO_MAKES_NO_SENSE");
                if (c.Person != cm.Maker) return R(c, HeroResultCode.Refused, "cf_collect_in_person");
                var a = Commissions.Collect(w, cm, hero, c.Settlement, ctx.Pack);
                if (a == CommissionAnswer.Ok) foreach (var (g, _) in ctx.Pack.Rows().ToList()) ctx.Party.Carried.Stow(ctx.Econ, ctx.Rules, g);
                return a switch
                {
                    CommissionAnswer.Ok => R(c, HeroResultCode.Ok, "cf_collected", ctx.Recipes[cm.Recipe].Name),
                    CommissionAnswer.NotReady => R(c, HeroResultCode.Refused, "cf_not_ready"),
                    CommissionAnswer.CannotAfford => R(c, HeroResultCode.CannotAfford, "cf_cannot_afford"),
                    CommissionAnswer.NotHere => R(c, HeroResultCode.Refused, "cf_collect_in_person"),
                    _ => R(c, HeroResultCode.Refused, "cf_gone"),
                };
            }
            case "cancel" when p.Length >= 2:
            {
                if (!st.Commissions.TryGetValue(new EntityId((ulong)N(1)), out var cm)) return R(c, HeroResultCode.Invalid, "HERO_MAKES_NO_SENSE");
                return Commissions.Cancel(w, cm, hero, ctx.Pack) == CommissionAnswer.Ok ? R(c, HeroResultCode.Ok, "cf_cancelled") : R(c, HeroResultCode.Refused, "cf_gone");
            }
            case "join" when p.Length >= 2:
            {
                var a = Guilds.Join(w, hero, p[1], c.Settlement, day);
                return a == GuildAnswer.Ok ? R(c, HeroResultCode.Ok, "cf_joined", ctx.Recipes.Guilds[p[1]].Name) : R(c, a == GuildAnswer.CannotAfford ? HeroResultCode.CannotAfford : HeroResultCode.Refused, "cf_join_" + a.ToString().ToLowerInvariant());
            }
            case "apprentice" when p.Length >= 3:
            {
                var master = new EntityId((ulong)N(1));
                if (c.Settlement < 0 || w.SettlementOf(master) != c.Settlement) return R(c, HeroResultCode.Refused, "cf_not_here");
                var (a, _) = Apprenticeships.Start(w, hero, master, (Skill)N(2), day, ctx.State.Ids);
                return a == ApprenticeAnswer.Ok ? R(c, HeroResultCode.Ok, "cf_apprenticed", ctx.Recipes.Settings.ApprenticeTermDays) : R(c, HeroResultCode.Refused, "cf_apprentice_" + a.ToString().ToLowerInvariant());
            }
            case "leave":
            {
                var a = Apprenticeships.As(st, hero); if (a == null) return R(c, HeroResultCode.Invalid, "HERO_MAKES_NO_SENSE");
                Apprenticeships.Leave(st, a); return R(c, HeroResultCode.Ok, "cf_left_apprenticeship", a.Learnt.Count);
            }
            case "teach" when p.Length >= 3 && N(2) >= 0 && N(2) < ctx.Recipes.Recipes.Count:
            {
                var r = ctx.Recipes[(ushort)N(2)]; var student = new EntityId((ulong)N(1));
                if (!Guilds.MayTeach(w, r, hero, student, hero, out bool offence)) return R(c, HeroResultCode.Refused, "cf_guild_only");
                var s = ctx.Craft.StartTeaching(w.ActorOf(student), ctx.Actor(), r, byLabour: true);
                if (s == null) return R(c, HeroResultCode.Refused, "cf_cannot_teach");
                if (offence) Guilds.Record(w, r.Guild!, c.Settlement, hero, "taught_outsider", r.Index, c.IssuedSec, ctx.State.Ids, ctx.State.Chronicle);
                return R(c, HeroResultCode.Ok, "cf_teaching", r.Name);
            }
            case "apart" when p.Length >= 2:
            {
                var item = new EntityId((ulong)N(1));
                if (!ctx.Econ.Items.IsLive(item) || ctx.Econ.Items.Get(item).Owner != hero || !st.Records.TryGetValue(item, out var rec)) return R(c, HeroResultCode.Refused, "cf_cannot_take_apart");
                var r = ctx.Recipes[rec.Recipe];
                if (!ctx.Craft.Disassemble(item, ctx.Actor(), ctx.Pack)) return R(c, HeroResultCode.Refused, "cf_cannot_take_apart");
                if (r.Guild != null && !Guilds.IsMember(st, r.Guild, hero) && w.WitnessesOf(c.Settlement, hero).Count > 0)
                    Guilds.Record(w, r.Guild, c.Settlement, hero, "took_apart", r.Index, c.IssuedSec, ctx.State.Ids, ctx.State.Chronicle);
                foreach (var (g, _) in ctx.Pack.Rows().ToList()) ctx.Party.Carried.Stow(ctx.Econ, ctx.Rules, g);
                return R(c, HeroResultCode.Ok, "cf_took_apart", r.Name);
            }
            case "feed" when p.Length >= 3 && N(2) > 0:
            {
                if (!ctx.Econ.Goods.TryIdOf("firewood", out var fw)) return R(c, HeroResultCode.Invalid, "HERO_MAKES_NO_SENSE");
                ctx.Craft.BurnFires(c.IssuedSec, 0);
                return st.Fires.Feed(ctx.Econ, new EntityId((ulong)N(1)), ctx.Pack, fw, N(2)) ? R(c, HeroResultCode.Ok, "cf_fed") : R(c, HeroResultCode.Refused, "cf_fire_out");
            }
        }
        return R(c, HeroResultCode.Invalid, "HERO_MAKES_NO_SENSE");
    }

    /// <summary>The hero's sources for a commission's materials: his pack.</summary>
    private static CraftSources Sources(HeroContext ctx) => CraftSources.Of(ctx.Pack);

    /// <summary>
    /// The end of a game hour (the hero desk's after-hour pass): the commissions worked by their makers, the fires burned; at a day's start the apprenticeships' day, the guild dues and the fires
    /// that are out swept. Returns the commissions of the hero that changed (for his messages).
    /// </summary>
    public static List<HeroText> Hourly(HeroContext ctx, long hour)
    {
        var w = ctx.CraftContext; var st = ctx.Craft.State; var msgs = new List<HeroText>(); var hero = ctx.Party.Hero;
        if (st.Commissions.Count > 0)
            foreach (var cm in Commissions.Hourly(w, hour))
                if (cm.Client == hero)
                    msgs.Add(cm.State switch
                    {
                        CommissionState.Working => HeroText.Of("cf_commission_started", ctx.Recipes[cm.Recipe].Name),
                        CommissionState.Ready => HeroText.Of("cf_commission_ready", ctx.Recipes[cm.Recipe].Name),
                        CommissionState.Sold => HeroText.Of("cf_commission_sold", ctx.Recipes[cm.Recipe].Name),
                        _ => HeroText.Empty,
                    });
        if (st.Fires.Count > 0) { ctx.Craft.BurnFires(hour * 3600, ctx.RainTenthsMm?.Invoke() ?? 0); }
        if (hour % 24 == 0)
        {
            int day = (int)(hour / 24);
            if (ctx.CraftSettlement >= 0) Guilds.SeeAt(st, hero, ctx.CraftSettlement, day);
            if (st.Apprenticeships.Count > 0)
                foreach (var (a, r) in Apprenticeships.Day(w, day, a => a.Apprentice != hero || st.Residence.TryGetValue(hero.Value, out var res) && res.Settlement == w.SettlementOf(a.Master)))
                    if (a.Apprentice == hero) msgs.Add(HeroText.Of("cf_learnt", r.Name));
            if (st.Members.Count > 0)
                foreach (var m in Guilds.Dues(w, day)) if (m.Person == hero) msgs.Add(HeroText.Of("cf_expelled", ctx.Recipes.Guilds.TryGetValue(m.Guild, out var g) ? g.Name : m.Guild));
            if (st.Fires.Count > 0) st.Fires.Sweep(ctx.Econ, o => o == hero ? ctx.Pack : null);
        }
        return [.. msgs.Where(m => !m.IsEmpty)];
    }

    /// <summary>The place view for the screen: its makers (people with a workshop), its guilds and the hero's apprenticeship. Built at a barrier.</summary>
    public static CraftPlaceView View(HeroContext ctx, int settlement, int day)
    {
        var w = ctx.CraftContext; var st = ctx.Craft.State; var hero = ctx.Party.Hero;
        var makers = new List<MakerRow>();
        if (settlement >= 0 && ctx.State.Production is { } prod)
        {
            var people = ctx.State.Population.People;
            var skills = Enum.GetValues<Skill>();
            foreach (var p in w.PeopleOf(settlement))
            {
                int i = people.IndexOf(p); if (i < 0) continue;
                Production.Business? b = null;
                foreach (var x in prod.Businesses) if (x.Owner == i || x.Staff.Contains(i)) { b = x; break; }
                if (b == null) continue;
                var kind = prod.Content.Kinds[b.Kind]; if (kind.Stations.Length == 0) continue;
                var sk = new byte[skills.Length]; foreach (var s in skills) sk[(int)s] = (byte)Math.Clamp(st.Book.Level(p, s), 0, 100);
                makers.Add(new MakerRow(p, people.FullName(i), people.Occupation[i].ToString(), sk,
                    [.. kind.Stations.Select(k => RecipeUnifier.StationOfKind.GetValueOrDefault(k.Kind, k.Kind)).Distinct()], Commissions.BusySec(st, p, j => ctx.Craft.LeftSec(j))));
                if (makers.Count >= 24) break;
            }
        }
        var guilds = Guilds.Of(w, settlement).Select(g =>
        {
            int mi = ctx.State.Population.People.IndexOf(g.Master);
            return new GuildRow(g.Def.Id, g.Def.Name, mi >= 0 ? ctx.State.Population.People.FullName(mi) : "", g.Def.EntryFeeMites, g.Def.DuesMitesPerSeason, g.Def.MinSkill, g.Def.Skill,
                Guilds.IsMember(st, g.Def.Id, hero), Guilds.ResidentDays(st, hero, settlement, day), g.Def.ResidenceDays);
        }).ToList();
        var ap = Apprenticeships.As(st, hero);
        string apOf = ""; int left = 0;
        if (ap != null) { int mi = ctx.State.Population.People.IndexOf(ap.Master); apOf = mi >= 0 ? ctx.State.Population.People.FullName(mi) : ""; left = Math.Max(0, ap.EndDay - day); }
        return new CraftPlaceView(settlement, makers, guilds, apOf, left);
    }
}
