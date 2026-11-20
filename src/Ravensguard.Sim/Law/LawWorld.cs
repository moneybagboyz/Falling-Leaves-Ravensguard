using Ravensguard.Content;
using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Jobs;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Society;

namespace Ravensguard.Sim.Law;

/// <summary>
/// What the law's systems read and write (crime-and-justice 2), gathered so a test can build a town by hand and the runtime can build it from the world state: the chronicle and its ids, the people and
/// households, the fief of each settlement and its treasury, the jobs board that escrows bounties, and the few questions the law asks of other systems (renown, hunger, names).
/// </summary>
public sealed class LawWorld
{
    public required Chronicle Chronicle { get; init; }
    public required IdAllocator Ids { get; init; }
    public required PersonTable People { get; init; }
    public HouseholdTable? Households { get; init; }
    public required string[] SettlementNames { get; init; }
    /// <summary>The settlements' ids (for a chronicle place parameter); empty entries fall back to no place.</summary>
    public EntityId[] SettlementIds { get; init; } = [];
    /// <summary>Per settlement: the fief it belongs to (an index into <see cref="FiefIds"/>), -1 none.</summary>
    public int[] SettlementFief { get; init; } = [];
    public EntityId[] FiefIds { get; init; } = [];
    /// <summary>Per fief: the seat settlement (where its gaol and court are), -1 none.</summary>
    public int[] FiefSeat { get; init; } = [];
    public ulong Seed { get; init; }
    public LawContent Law { get; init; } = LawContent.Default;
    public int RegionId { get; init; }
    public Ledger? Ledger { get; init; }
    public JobTable? Jobs { get; init; }
    public JobEnv? JobEnv { get; init; }
    /// <summary>The treasury a settlement's lord posts bounties from (null: none to draw on).</summary>
    public Func<int, PurseId?>? TreasuryOf { get; init; }
    /// <summary>A person's renown in permille (the standing book's), 0 when none.</summary>
    public Func<EntityId, int> RenownPm { get; init; } = _ => 0;
    /// <summary>Whether a household is desperate (hungry, missed meals, or in arrears): the source of NPC theft.</summary>
    public Func<int, bool> Desperate { get; init; } = _ => false;
    /// <summary>A person's name for the chronicle's data and the rumour (the hero is not in the person table).</summary>
    public Func<EntityId, string>? NameOf { get; init; }
    /// <summary>P6-B4 (faith 4.3): told of every crime recorded (the doer and the kind), so a sin costs its piety; null: nobody keeps account.</summary>
    public Action<EntityId, string>? Committed { get; init; }

    // ------------------------------------------------------------------ P6-C1: what the gaol, the court and the sentences read and write (every one optional: a hand-made town in a test sets what it uses)

    /// <summary>The hero's id (the person 0 of a world; <c>EntityId.None</c> in a test with no hero row).</summary>
    public EntityId HeroId { get; init; } = EntityId.None;
    public bool IsHero(EntityId p) => p == HeroId;
    /// <summary>Everyone held captive (the gaol writes <see cref="CaptorKind.Gaol"/> rows; the bandits' captives are fed from the band's stash).</summary>
    public CaptiveBook? Captives { get; init; }
    public AftermathRules Rules { get; init; } = AftermathRules.Default;
    public WoundLedger? Wounds { get; init; }
    /// <summary>The human body plan the court's wounds are made on; null: no body to cut (the sentence is carried out as a record only).</summary>
    public BodyPlanV2? Human { get; init; }
    /// <summary>The marks of the world's people (brands, outlawry); null: none kept.</summary>
    public Func<MarkBook?> Marks { get; init; } = () => null;
    /// <summary>A person's purse (the hero's own, an NPC's household purse).</summary>
    public Func<EntityId, PurseId?> PurseOf { get; init; } = _ => null;
    /// <summary>The purse that gets restitution, a weregild or a blood price: the victim's household (a person), or the fief or church purse the victim id names.</summary>
    public Func<ulong, PurseId?> VictimPurse { get; init; } = _ => null;
    /// <summary>The purse of the church (penance), of a settlement.</summary>
    public Func<int, PurseId?> ChurchPurse { get; init; } = _ => null;
    /// <summary>Who sells the prisoners' bread (a baker's household), by settlement; null: the coin is sunk.</summary>
    public Func<int, PurseId?> RationPayee { get; init; } = _ => null;
    /// <summary>The punishment key the culture of a settlement names for a crime id (<c>cultures.jsonc</c> "crimes"), null: none.</summary>
    public Func<int, string, string?> CultureKey { get; init; } = (_, _) => null;
    /// <summary>The lord of a fief as a person (<c>EntityId.None</c>: no lord), and whether the hero holds the fief (he then judges its court).</summary>
    public Func<int, EntityId> LordOf { get; init; } = _ => EntityId.None;
    public Func<int, bool> HeroLordOf { get; init; } = _ => false;
    /// <summary>The realm index of a settlement (outlawry is per realm).</summary>
    public Func<int, int> RealmOf { get; init; } = _ => 0;
    /// <summary>A person's standing with a fief (permille, <see cref="Standing.Of"/>).</summary>
    public Func<EntityId, int, int> StandingWithFief { get; init; } = (_, _) => 0;
    /// <summary>A lord's opinion of a person (permille; the talk book's trust): what raises a sentence.</summary>
    public Func<EntityId, EntityId, int> OpinionPm { get; init; } = (_, _) => 0;
    public Func<EntityId, int> LiteracyOf { get; init; } = _ => 0;
    /// <summary>Whether the church tries its own here (benefit of clergy).</summary>
    public Func<int, bool> ChurchCourts { get; init; } = _ => true;
    /// <summary>An official's honesty, 0 to 100 (rule and income 4.8; the C3 part supplies the rolled value): above the setting's honest line he never takes a bribe.</summary>
    public Func<EntityId, int> HonestyOf { get; init; } = _ => 45;
    /// <summary>The distance in metres between two settlements (a witness who lives further than the setting's reach does not come).</summary>
    public Func<int, int, long> DistanceM { get; init; } = (a, b) => a == b ? 0 : 5_000;
    /// <summary>Hangs an NPC (through <c>Demography.Kill</c>, cause Executed), given the entry that explains it; null: the person is only marked dead in the table.</summary>
    public Action<EntityId, long, ulong>? Execute { get; init; }
    /// <summary>Forfeits what an outlaw holds in a realm to its lords (deeds, <c>DeedCause.Forfeited</c>); null: nothing held.</summary>
    public Action<EntityId, int, long>? Forfeit { get; init; }
    /// <summary>Told when a sentence harsher than the culture's key is pronounced on a convicted of a household (unrest: a grievance for the kin) or an acquittal of the plainly guilty (the victim's): (person, +1 harsher, -1 acquitted).</summary>
    public Action<EntityId, int>? Grievance { get; init; }
    /// <summary>P6-D1 (slavery 4.1 rule 3): told when a <c>bond_labour</c> sentence starts: the convict, the fief, the days, the settlement, the second and the sentence's chronicle entry. The bondage book binds him to the fief's works for his term; null: nobody keeps account.</summary>
    public Action<EntityId, int, int, int, long, ulong>? BondLabour { get; init; }
    /// <summary>The captive's skills as the escape rolls read them (stealth, athletics, lockpicking, perception, speech).</summary>
    public Func<EntityId, (int Stealth, int Athletics, int Lockpicking, int Perception, int Speech)> SkillsOf { get; init; } = _ => (0, 0, 0, 0, 0);
    /// <summary>Whether an exiled person has left the realm by the end of his grace (the hero walked out; an NPC was sent); null: nobody has, so exile ends as outlawry.</summary>
    public Func<EntityId, bool>? HasLeftRealm { get; init; }
    /// <summary>The settlement a person lives in (a row's home), -1 when unknown.</summary>
    public int HomeOf(EntityId p) { int i = People.IndexOf(p); return i >= 0 ? People.HomeSettlement[i] : -1; }

    public LawSettingsDef S => Law.S;

    public string Name(EntityId p)
    {
        if (NameOf?.Invoke(p) is { Length: > 0 } n) return n;
        int i = People.IndexOf(p); return i >= 0 ? People.FullName(i) : "";
    }

    public EntityId PlaceId(int settlement) => settlement >= 0 && settlement < SettlementIds.Length ? SettlementIds[settlement] : EntityId.None;
    public string PlaceName(int settlement) => settlement >= 0 && settlement < SettlementNames.Length ? SettlementNames[settlement] : "";
    public int FiefOf(int settlement) => settlement >= 0 && settlement < SettlementFief.Length ? SettlementFief[settlement] : -1;

    /// <summary>The law's view of the running world at a barrier: the home region's people, households, fiefs and treasuries, the jobs board, the standing book's renown and the economy's grievances.</summary>
    public static LawWorld From(WorldState st, ContentDatabase? db)
    {
        var pop = st.Population; var region = pop.Region; var e = st.Economy;
        var names = region.Settlements.Select(s => s.Name).ToArray();
        var ids = region.Settlements.Select(s => s.Id).ToArray();
        var fief = region.Settlements.Select(s => s.FiefIndex >= 0 && s.FiefIndex < region.Fiefs.Count ? s.FiefIndex : -1).ToArray();
        var fiefIds = region.Fiefs.Select(f => f.Id).ToArray();
        JobEnv? env = e == null || db == null ? null : new JobEnv { Ledger = e.Ledger, Econ = e, Pop = pop, Tuning = Ravensguard.Sim.Behaviour.BehaviourContent.From(db).OddJobs };
        var standing = st.Standing;
        var hero = st.Party?.Hero ?? EntityId.None;
        return new LawWorld
        {
            Chronicle = st.Chronicle, Ids = st.Ids, People = pop.People, Households = pop.Households, SettlementNames = names, SettlementIds = ids, SettlementFief = fief, FiefIds = fiefIds,
            Seed = st.Seed, Law = LawContent.From(db), RegionId = st.HomeRegion.Id, Ledger = e?.Ledger, Jobs = e == null ? null : (st.Jobs ??= new JobTable()), JobEnv = env,
            TreasuryOf = s =>
            {
                int f = s >= 0 && s < fief.Length ? fief[s] : -1;
                return e != null && f >= 0 && e.Ledger.TryFind(PurseKind.Treasury, fiefIds[f], out var p) ? p : null;
            },
            RenownPm = p => standing != null && standing.Renowns.TryGetValue(p.Value, out var r) ? r.Pm : 0,
            Desperate = hh => e != null && hh >= 0 && hh < e.HouseholdCount && (e.Grievances[hh] & (Ravensguard.Sim.Economy.Grievance.Hunger | Ravensguard.Sim.Economy.Grievance.MissedMeal | Ravensguard.Sim.Economy.Grievance.Tax)) != 0,
            NameOf = p => p == hero ? st.Hero?.Name ?? "" : "",
            Committed = (doer, kind) => Ravensguard.Sim.Faith.FaithWiring.OnCrime(st, doer, kind),
            BondLabour = db == null ? null : (p, f, days, settlement, sec, entry) => Ravensguard.Sim.Bondage.BondLink.Convict(st, db, p, f, days, settlement, sec, entry),
            // ---- P6-C1: the gaol, the court, the sentences
            HeroId = hero, FiefSeat = region.Fiefs.Select(f => f.SeatSettlement).ToArray(),
            Captives = db != null ? Ravensguard.Sim.Combat.Aftermath.AftermathDesk.BookOf(st, db) : st.Captivity, Rules = st.Captivity?.Rules ?? AftermathRules.Default,
            Wounds = st.Wounds, Human = db != null && st.Wounds != null ? Humans.GetValue(db, d => new Ravensguard.Sim.Combat.CombatTables(d).V2.Plan("human")) : null,
            Marks = () => (st.Standing ??= new StandingBook()).Marks,
            PurseOf = p =>
            {
                if (e == null) return null;
                if (p == hero) return e.Ledger.TryFind(PurseKind.Person, EconomyState.HeroOwner, out var hp) ? hp : null;
                int row = pop.People.IndexOf(p); int hh = row >= 0 ? pop.People.Household[row] : -1;
                return hh >= 0 && hh < e.HouseholdCount ? e.HouseholdPurse[hh] : null;
            },
            VictimPurse = v =>
            {
                if (e == null || v == 0) return null;
                var id = new EntityId(v);
                if (id.Kind == IdKind.Person)
                {
                    int row = pop.People.IndexOf(id); int hh = row >= 0 ? pop.People.Household[row] : -1;
                    return hh >= 0 && hh < e.HouseholdCount ? e.HouseholdPurse[hh] : null;
                }
                if (id.Kind == IdKind.Household) return e.Ledger.TryFind(PurseKind.Household, id, out var hp) ? hp : null;
                return e.Ledger.TryFind(PurseKind.Treasury, id, out var tp) ? tp : null;
            },
            RationPayee = s =>
            {
                if (e == null) return null;
                foreach (var occ in new[] { Occupation.Baker, Occupation.Miller })
                    for (int i = 0; i < pop.People.Count; i++)
                        if (pop.People.IsAlive(i) && pop.People.Occupation[i] == occ && pop.People.HomeSettlement[i] == s && pop.People.Household[i] is var hh and >= 0 && hh < e.HouseholdCount) return e.HouseholdPurse[hh];
                return null;
            },
            CultureKey = (s, kind) => db != null && s >= 0 && s < region.Settlements.Count && Ravensguard.Sim.Faith.ReligionBook.CultureOf(db, region.Settlements[s].Culture) is { } cu ? cu.Crimes.GetValueOrDefault(kind) : null,
            LordOf = f => f >= 0 && f < pop.FiefLord.Length && pop.FiefLord[f] >= 0 && pop.FiefLord[f] < pop.People.Count ? pop.People.IdOf(pop.FiefLord[f]) : EntityId.None,
            HeroLordOf = f => st.Land != null && st.Land.Deeds.Values.Any(d => d.Kind == Ravensguard.Sim.Land.HoldingKind.Fief && d.Subject == f && d.Holder == hero),
            RealmOf = s => s >= 0 && s < region.Settlements.Count ? Math.Max(0, region.Settlements[s].Realm) : 0,
            StandingWithFief = (p, f) => Standing.Of(standing, p, GroupRef.Fief(f)),
            LiteracyOf = p => p == hero ? st.Hero?.Skill(Ravensguard.Sim.Characters.HeroRecord.SkillId(Skill.Literacy)) ?? 0 : pop.People.IndexOf(p) is var r and >= 0 ? pop.People.SkillLevelOf(r, Skill.Literacy) : 0,
            SkillsOf = p =>
            {
                var h = st.Hero;
                if (p != hero || h == null) return (0, 0, 0, 0, 0);
                int S(Skill k) => h.Skill(Ravensguard.Sim.Characters.HeroRecord.SkillId(k));
                return (S(Skill.Stealth), S(Skill.Athletics), S(Skill.Lockpicking), h.Attribute(Ravensguard.Sim.Characters.HeroAttr.Perception), S(Skill.Speech));
            },
            DistanceM = (a, b) => a < 0 || b < 0 || a >= region.Settlements.Count || b >= region.Settlements.Count ? 0 : ISqrt(Sq(region.Settlements[a].CenterX - region.Settlements[b].CenterX) + Sq(region.Settlements[a].CenterY - region.Settlements[b].CenterY)),
            Execute = (p, sec, entry) =>
            {
                int row = pop.People.IndexOf(p);
                if (row < 0 || st.HomeRecords?.Sim is not { } sim) return;
                var ob = new Ravensguard.Sim.Abstract.RegionOutbox(st.Ids);
                Ravensguard.Sim.People.Vital.Demography.Kill(sim, row, DeathCause.Executed, new GameTime(sec), ob, chain: new Cause(CauseKind.Justice, new EntityId(entry)));
                ob.ApplyTo(st.Chronicle, null, new Dictionary<string, long>(StringComparer.Ordinal));
            },
            Forfeit = (p, realm, sec) =>
            {
                if (db == null || e == null) return;
                var lw = Ravensguard.Sim.Land.LandWiring.For(st, db); int day = (int)(sec / 86_400);
                foreach (var d in lw.Registry().HeldBy(p).Where(d => d.Kind == Ravensguard.Sim.Land.HoldingKind.Lot).ToList())
                {
                    var (s, l) = Ravensguard.Sim.Land.LandIds.SplitLot(d.Subject);
                    if (s < 0 || s >= region.Settlements.Count || region.Settlements[s].Realm != realm) continue;
                    var (lord, kind) = Ravensguard.Sim.Land.LandRegistry.LordOf(pop, s);
                    lw.ConveyLot(s, l, lord, kind, day, Ravensguard.Sim.Land.DeedCause.Forfeited);
                }
            },
        };
    }

    /// <summary>The numbers of the law without the rest of the world: what the link needs to know which hours have work (no tables built).</summary>
    public static LawWorld Light(WorldState st) => LightCache.GetValue(st, s => new LawWorld
    {
        Chronicle = s.Chronicle, Ids = s.Ids, People = s.Population.People, SettlementNames = [], Law = LawContent.From(s.Content),
    });
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<WorldState, LawWorld> LightCache = new();

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ContentDatabase, BodyPlanV2> Humans = new();
    private static long Sq(long v) => v * v;
    private static long ISqrt(long n)
    {
        if (n <= 0) return 0;
        long x = n, y = (x + 1) / 2;
        while (y < x) { x = y; y = (x + n / x) / 2; }
        return x;
    }
}
