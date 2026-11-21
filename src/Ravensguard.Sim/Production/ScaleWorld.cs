using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Society;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Production;

/// <summary>
/// The limits of size that act on a season or on the owner's command (P7-ES1, economies-of-scale 5.2, 5.3): the guilds' inspection of oversized shops (fines to the town's treasury, the guild master's opinion, a closing
/// order after three), manufactory licences and their season's rent, and appointed foremen. Every coin is one ledger transfer; the draw is stream 0x721; nothing here runs on the hour (the hourly crews are
/// <see cref="ProductionSystem"/>'s, over <see cref="Crews"/>).
/// </summary>
public sealed partial class ScaleWorld
{
    public ScaleBook Book { get; }
    public ProductionSystem Prod { get; }
    public ContentDatabase Content { get; }
    public ulong Seed { get; }
    /// <summary>The rule's numbers (the charter threshold and fee term); the licence petition judges by them.</summary>
    public Func<RuleSettingsDef> RuleSettings { get; set; } = () => new RuleSettingsDef { Id = "rule" };
    /// <summary>A party's opinion of another (the rule book's, keyed by id), the way to change it, and a person's standing with a realm in permille; defaults to a world with none.</summary>
    public Func<ulong, ulong, int> OpinionOf { get; set; } = (_, _) => 0;
    public Action<ulong, ulong, int>? AddOpinion { get; set; }
    public Func<EntityId, int, int> StandingWithRealm { get; set; } = (_, _) => 0;
    public RuleChron Chron { get; set; } = RuleChron.Silent;

    public ScaleWorld(ScaleBook book, ProductionSystem prod, ContentDatabase content, ulong seed)
    {
        Book = book; Prod = prod; Content = content; Seed = seed;
        prod.Scale = book;
    }

    private ScaleSettingsDef Ss => Prod.Content.Scale;
    private Ledger Led => Prod.Econ.Ledger;

    private bool Treasury(int settlement, out PurseId treasury, out int fief)
    {
        treasury = default; fief = (uint)settlement < (uint)Prod.Pop.Region.Settlements.Count ? Prod.Pop.Region.Settlements[settlement].FiefIndex : -1;
        return fief >= 0 && fief < Prod.Pop.Region.Fiefs.Count && Led.TryFind(PurseKind.Treasury, Prod.Pop.Region.Fiefs[fief].Id, out treasury);
    }

    // ------------------------------------------------------------------ the season (5.3)

    /// <summary>
    /// A season's first day: licences pay their rent (a works that cannot loses the licence), old fines are forgotten, and each oversized, unlicensed shop of a guild is inspected with the guild's draw (50%, stream 0x721,
    /// key the season, the shop and the guild): a fine of 2,000 mites for every worker or station over the caps, to the town's treasury (<c>Fine</c>), the guild master's opinion of the owner down 20, and after three fines
    /// the lord's court closes the shop. Returns the fines laid.
    /// </summary>
    public int Seasonal(int day)
    {
        int season = day / TimeConst.DaysPerSeason, laid = 0;
        Rents(day, season);
        Book.Fines.RemoveAll(f => day - f.Day > Ss.FineKeepDays);
        var rs = Prod.Pop.People;
        foreach (var shop in Limits.Shops(Prod, Content, day))
        {
            var b = shop.Business;
            if (shop.Over.Total == 0 || Book.LicenceOf(b.Id.Value) != null) continue;
            uint draw = DetRng.NextUInt(Seed, season, b.Id.Value, RngStreams.GuildInspection, (uint)shop.Ordinal);
            if (draw >= (ulong)Ss.InspectionPct * 4_294_967_296UL / 100UL) continue;
            long fine = (long)shop.Over.Total * Ss.GuildFinePerOverMites;
            long paid = Pay(b, shop.Settlement, fine, TransferReason.Fine);
            Book.Fines.Add(new GuildFine { Business = b.Id.Value, Season = season, Day = day, Over = shop.Over.Total, Mites = fine, Paid = paid });
            laid++;
            int master = Limits.MasterOf(Prod, shop.Guild, shop.Settlement);
            if (master >= 0 && b.Owner >= 0 && master != b.Owner) AddOpinion?.Invoke(rs.IdOf(master).Value, rs.IdOf(b.Owner).Value, Ss.FineOpinion);
            var place = Prod.Pop.Region.Settlements[shop.Settlement].Id;
            var subj = b.Owner >= 0 ? new[] { rs.IdOf(b.Owner), b.Id } : new[] { b.Id };
            Chron.Chron(GameTime.FromGameHours((long)day * 24 + 9), "guild_fined_oversize", "ch_scale_guild_fine",
                [ChronParam.Person("owner", b.Owner >= 0 ? rs.IdOf(b.Owner) : EntityId.None), ChronParam.Place("place", place), ChronParam.Count("over", shop.Over.Total), ChronParam.Mites("fine", fine)],
                subj, [new Cause(CauseKind.Law, null, NoteKey: "ch_note_guild_cap_exceeded")]);
            if (Book.FinesOf(b.Id.Value) >= Ss.FinesToClose && b.State == BusinessState.Open) { Prod.Close(b); Book.Closings.Add((b.Id.Value, day)); }
        }
        return laid;
    }

    /// <summary>The season's rent of each licence, from the works's cash box to the lord's treasury (<c>Fee</c>); a works closed or too poor to pay loses its licence.</summary>
    private void Rents(int day, int season)
    {
        for (int i = 0; i < Book.Licences.Count; i++)
        {
            var l = Book.Licences[i];
            if (l.PaidSeason >= season) continue;
            var b = Prod.ById(new EntityId(l.Business));
            bool ok = b != null && b.State == BusinessState.Open && Treasury(b.Settlement, out var tr, out _) && Led.Balance(b.Purse).Mites >= Ss.LicenceSeasonMites
                && (Ss.LicenceSeasonMites == 0 || Led.Transfer(b.Purse, tr, new Coin(Ss.LicenceSeasonMites), TransferReason.Fee));
            if (ok) l.PaidSeason = season; else { Book.Licences.RemoveAt(i); i--; }
        }
    }

    /// <summary>Pays what a shop owes from its cash box and then its owner's household purse, to the settlement's treasury; returns what was paid (a poor shop pays less than the fine).</summary>
    private long Pay(Business b, int settlement, long mites, TransferReason why)
    {
        if (mites <= 0 || !Treasury(settlement, out var tr, out _)) return 0;
        long left = mites;
        long fromBox = Math.Min(left, Led.Balance(b.Purse).Mites);
        if (fromBox > 0 && Led.Transfer(b.Purse, tr, new Coin(fromBox), why)) left -= fromBox;
        if (left > 0 && b.OwnerHousehold >= 0 && b.OwnerHousehold < Prod.Econ.HouseholdCount)
        {
            var hp = Prod.Econ.HouseholdPurse[b.OwnerHousehold];
            long fromHome = Math.Min(left, Led.Balance(hp).Mites);
            if (fromHome > 0 && Led.Transfer(hp, tr, new Coin(fromHome), why)) left -= fromHome;
        }
        return mites - left;
    }

    // ------------------------------------------------------------------ the manufactory licence (5.3, ES-13)

    /// <summary>
    /// A petition to the lord of the town for a manufactory licence (5.3): the shop must be one of a guild's trades in its town. The charter rule's sum with the guild master's opinion of the petitioner and -20 for every licence
    /// already granted in the trade there decides; the fee (5 gp) goes from <paramref name="payer"/> to the lord's treasury (<c>Fee</c>), the licence then costs 500 mites a season from the works. Returns the refusal key, or "".
    /// </summary>
    public OwnResult Petition(Business b, EntityId petitioner, PurseId payer, int day)
    {
        var pt = Prod.Pop.People;
        var shop = Limits.Shops(Prod, Content, day).FirstOrDefault(s => s.Business == b);
        if (shop == null) return OwnResult.No("sc_no_guild");
        if (Book.LicenceOf(b.Id.Value) != null) return OwnResult.No("sc_already_licensed");
        if (!Treasury(shop.Settlement, out var tr, out int fief)) return OwnResult.No("sc_no_lord");
        long fee = Ss.LicenceFeeMites;
        var fiefId = Prod.Pop.Region.Fiefs[fief].Id; int realm = Prod.Pop.Region.Fiefs[fief].Realm;
        int master = Limits.MasterOf(Prod, shop.Guild, shop.Settlement);
        int masterOpinion = master >= 0 ? OpinionOf(pt.IdOf(master).Value, petitioner.Value) : 0;
        int granted = Book.Licences.Count(l => l.Guild == shop.Guild.Id && Prod.ById(new EntityId(l.Business)) is { } ob && ob.Settlement == shop.Settlement);
        var (_, ok, key) = Limits.LicenceTerms(RuleSettings(), Ss, OpinionOf(fiefId.Value, petitioner.Value), StandingWithRealm(petitioner, realm), fee, masterOpinion, granted);
        if (!ok) return OwnResult.No(key);
        if (Led.Balance(payer).Mites < fee || !Led.Transfer(payer, tr, new Coin(fee), TransferReason.Fee)) return new OwnResult(false, "ld_cannot_afford", [fee], fee, CannotAfford: true);
        Book.Licences.Add(new ManufactoryLicence { Business = b.Id.Value, Fief = fief, Guild = shop.Guild.Id, GrantedDay = day, FeeMites = (int)fee, PaidSeason = day / TimeConst.DaysPerSeason });
        var place = Prod.Pop.Region.Settlements[shop.Settlement].Id;
        Chron.Chron(GameTime.FromGameHours((long)day * 24 + 9), "manufactory_licensed", "ch_scale_licensed",
            [ChronParam.Person("holder", petitioner), ChronParam.Place("place", place), ChronParam.Mites("fee", fee)], [petitioner, b.Id, fiefId], [new Cause(CauseKind.Law, null, NoteKey: "ch_note_manufactory_petition")]);
        return new OwnResult(true, "sc_licensed", [fee], fee);
    }

    // ------------------------------------------------------------------ foremen (5.2)

    /// <summary>Appoints a staff member foreman: a journeyman or hand (not a master) with Leadership at least 20; he oversees <c>6 + Leadership / 10</c> workers and earns 50 mites a day more.</summary>
    public OwnResult Appoint(Business b, int person)
    {
        if (!b.Staff.Contains(person)) return OwnResult.No("sc_not_staff");
        if (Book.IsForeman(b.Id.Value, person)) return OwnResult.No("sc_already_foreman");
        var pt = Prod.Pop.People;
        if (pt.SkillLevelOf(person, Skill.Leadership) < Ss.ForemanMinLeadership) return OwnResult.No("sc_foreman_leadership", Ss.ForemanMinLeadership);
        foreach (var r in Prod.KindOf(b).Recipes) if (r.Skill != Skill.None && pt.SkillLevelOf(person, r.Skill) >= Ss.MasterSkill) return OwnResult.No("sc_foreman_is_master");
        if (!Book.Foremen.TryGetValue(b.Id.Value, out var set)) Book.Foremen[b.Id.Value] = set = [];
        set.Add(person);
        return OwnResult.Yes("sc_foreman_appointed", pt.FullName(person));
    }

    public OwnResult Dismiss(Business b, int person)
    {
        if (!Book.IsForeman(b.Id.Value, person)) return OwnResult.No("sc_not_foreman");
        Book.Forget(b.Id.Value, person);
        return OwnResult.Yes("sc_foreman_dismissed", Prod.Pop.People.FullName(person));
    }
}

/// <summary>The scale step's place in the abstract tick: the day's supply contracts and, on a season's first day, the guilds' inspection, at hour 9 (the ownership world's slot), after the hour's production. A no-op in a world without a <see cref="ScaleWorld"/> (only the home region has one).</summary>
[AbstractSystem]
public sealed class ScaleSystem : AbstractSystem
{
    public ScaleSystem() : base("scale", TickPhase.H7Outbox, SystemOrder.Outbox + 121) { }

    public override void Step(TickContext ctx)
    {
        if (ctx.HourOfDay != 9) return;
        var sw = ctx.World.Get<ScaleWorld>(); if (sw == null) return;
        sw.Chron = RuleChron.Of(ctx.Outbox);
        if (ctx.Day % TimeConst.DaysPerSeason == 0) sw.Seasonal((int)ctx.Day);
        sw.Daily((int)ctx.Day);                                                                  // P7-ES2: the supply contracts falling due
    }
}

/// <summary>Installs economies of scale on the home region (the P7-ES1 hook in <see cref="WorldRuntime"/>): the world state's book (the "scale" section), the world in the sim's services, its hooks to the rule book and the standing book.</summary>
public static class ScaleWiring
{
    public static ScaleWorld Attach(RegionWorld home, WorldState state, ContentDatabase content)
    {
        var sw = Make(state, content, home.Economy.Production);
        home.Sim.Set(sw);
        return sw;
    }

    /// <summary>The scale world a desk command works on: the home region's when the runtime installed one, else one over the world state's own records (tests, a world without regions).</summary>
    public static ScaleWorld For(WorldState state, ContentDatabase content)
    {
        if (state.HomeRecords?.Sim.Get<ScaleWorld>() is { } sw) return sw;
        return Loose.GetValue(state, s => Make(s, content, s.Production ?? throw new InvalidOperationException("scale needs the world's production")));
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<WorldState, ScaleWorld> Loose = new();

    /// <summary>The machines' labour hook (4.5): a station coupled to a machine's cell takes the labour the premises' power net gives it; no coupling, or no powered machine there: null, and the kind's own <c>labour_pm</c> stands.</summary>
    public static Func<Business, int, int?> MachineLabour(Func<ScaleBook?> book, Func<Ravensguard.Sim.Building.PowerWorld?> power) => (b, si) =>
    {
        if (book() is not { Couplings.Count: > 0 } sb || sb.CouplingOf(b.Id.Value, si) is not { } k) return null;
        return power()?.LabourPmAt(k.X, k.Y, k.Z);
    };

    private static ScaleWorld Make(WorldState state, ContentDatabase content, ProductionSystem prod)
    {
        var book = state.Scale ??= new ScaleBook();
        var sw = new ScaleWorld(book, prod, content, state.Seed);
        sw.RuleSettings = () => state.Rule?.Settings ?? content.All<RuleSettingsDef>().OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault() ?? new RuleSettingsDef { Id = "rule" };
        sw.OpinionOf = (who, of) => state.Rule?.Opinion(who, of) ?? 0;
        sw.AddOpinion = (who, of, d) => state.Rule?.AddOpinion(who, of, d);
        sw.StandingWithRealm = (p, realm) => realm < 0 ? 0 : Standing.Of(state.Standing, p, GroupRef.Realm(realm));
        sw.CarterOf = (settlement, day) => Ravensguard.Sim.Holdings.HoldingsDesk.CarterOf(state, settlement, day);
        // P7-ES2 (4.5): a station coupled to a machine takes its labour from the premises' power net; no coupling, or no net there: the kind's own labour_pm
        prod.StationLabourPm = MachineLabour(() => state.Scale, () => state.HomeRecords?.Sim.Get<Ravensguard.Sim.Building.PowerWorld>());
        prod.StationMachineWhy = (b, si) =>
        {
            if (state.Scale?.CouplingOf(b.Id.Value, si) is not { } k || state.HomeRecords?.Sim.Get<Ravensguard.Sim.Building.PowerWorld>() is not { } pw) return "sc_machine_idle";
            foreach (var n in pw.Nets) foreach (var m in n.Machines) if (m.Station is { } sp && sp.X == k.X && sp.Y == k.Y && sp.Z == k.Z) return n.Why.Length > 0 ? n.Why : "sc_machine_idle";
            return "sc_machine_none";
        };
        return sw;
    }
}
