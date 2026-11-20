using System.Security.Cryptography;
using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Economy;

/// <summary>
/// Household mood flags the economy sets for the unrest and population specs (households spec 2.1, 3.2). <see cref="Hunger"/> is a low store: under one day of
/// food left after the day's meals (the unrest weight). <see cref="MissedMeal"/> is a household that could not eat its whole day's food that day (review finding 17:
/// the soak's hunger measure); a missed meal always leaves a low store too, so the set of households with any grievance is unchanged by it.
/// </summary>
[Flags] public enum Grievance : ushort { None = 0, Hunger = 1, Tax = 2, Crowding = 4, Crime = 8, Lord = 16, Cold = 32, MissedMeal = 64 }

/// <summary>Where a household's gathered fuel comes from (review finding 4): the woodland of its settlement, with its members' hours. Set by the economy world; not saved (the stock is the resource layer's).</summary>
public interface IFuelGatherer
{
    /// <summary>Gathers up to <paramref name="wantG"/> grams of firewood into the household's store; returns the grams gathered.</summary>
    long Gather(int hh, long wantG);
}

/// <summary>Household tier chosen at the daily slice (households spec 3.5): survive first, then buffer, then profit.</summary>
public enum HouseholdTier : byte { Profit, Buffer, Survive }

/// <summary>
/// Everything economic that is saved state rather than generation (money spec, items spec, households spec): the coin ledger, the goods mass ledger, one store
/// and one purse per household, other stores (granaries, chests), item instances and the per-household and per-person needs columns. Arrays are indexed by
/// the household and person indices of the <see cref="Population"/> it was created for. Created by <see cref="Create"/> at world creation; the live economy
/// mutates it only through <see cref="Ledger"/>, <see cref="Store"/> and <see cref="HouseholdNeeds"/>.
/// </summary>
public sealed class EconomyState
{
    public const short FullNutritionCd = 3000;
    /// <summary>Owner of the hero's purse until the hero is a table person (counter 0 is never allocated).</summary>
    public static readonly EntityId HeroOwner = EntityId.Make(IdKind.Person, 0);

    public GoodCatalog Goods { get; }
    public Ledger Ledger { get; }
    public MassLedger Mass { get; }
    public ItemTable Items { get; set; } = new();
    public MoneySettingsDef Money { get; }
    /// <summary>The economy's tuning numbers (review finding 12, <c>content/base/economy/tuning.jsonc</c>); the code's old constants when the content has none.</summary>
    public EconomyTuningDef Tuning { get; set; } = new() { Id = "economy" };
    /// <summary>The woodland households gather fuel from (review finding 4); null in bare economies (unit tests), where gathering creates the wood as before.</summary>
    public IFuelGatherer? FuelGatherer { get; set; }
    /// <summary>P6-B4 (faith 4.1): the percent of extra relish and ale a household eats on a holy day of its faith (household, game day); null or 0 for an ordinary day. Not saved: the church binds it when the world runs.</summary>
    public Func<int, int, int>? FestivePct { get; set; }
    private readonly NeedsBasketDef[] _baskets;
    /// <summary>Which goods are staples, relishes, ale, salt and fuel (derived from good tags and classes).</summary>
    public FoodPlan Food { get; }

    /// <summary>Per household index: the household purse, and its house store.</summary>
    public PurseId[] HouseholdPurse { get; private set; }
    public Store[] HouseholdStore { get; private set; }
    /// <summary>Stores that are not a household's (granaries, barns, chests), by owner id ascending.</summary>
    public SortedDictionary<EntityId, Store> OtherStores { get; } = [];

    /// <summary>Per person: nutrition reserve in centidays 0..3000 (households spec 3.2 step 6).</summary>
    public short[] NutritionCd { get; private set; }
    /// <summary>Per household columns (households spec 2.1).</summary>
    public int[] SeedReserveG { get; private set; }
    public short[] FoodCoverDaysX10 { get; private set; }
    public short[] FuelCoverDaysX10 { get; private set; }
    public Grievance[] Grievances { get; private set; }
    public HouseholdTier[] Tiers { get; private set; }
    /// <summary>Set for the day when relish was replaced by grain (feeds disease and unrest in the population spec).</summary>
    public bool[] DietPoor { get; private set; }
    /// <summary>Per household, three counts (shoes, clothes, tools) of items that wore out and are waiting to be bought (integration decision I10).</summary>
    public byte[] DueDurables { get; private set; }
    public const int Durables = 3;
    /// <summary>Grams of seed corn households ate in the seed emergency (not saved: a running tally for the soak).</summary>
    public long SeedEaten;

    public EconomyState(GoodCatalog goods, MoneySettingsDef money, IReadOnlyList<NeedsBasketDef> baskets, int householdCount, int personCount, Ledger? ledger = null, MassLedger? mass = null)
    {
        Goods = goods; Food = new FoodPlan(goods); Money = money; Ledger = ledger ?? new Ledger(); Mass = mass ?? new MassLedger(goods.Count);
        _baskets = new NeedsBasketDef[Enum.GetValues<SocialClass>().Length];
        foreach (var b in baskets)
        {
            if (!Enum.TryParse<SocialClass>(b.Class, out var c)) throw new InvalidDataException($"needs basket '{b.Id}': unknown class '{b.Class}'");
            _baskets[(int)c] = b;
        }
        foreach (var c in Enum.GetValues<SocialClass>()) if (_baskets[(int)c] == null) throw new InvalidDataException($"no needs basket for class {c}");
        HouseholdPurse = new PurseId[householdCount]; HouseholdStore = new Store[householdCount];
        _householdCount = householdCount; _personCount = personCount;
        NutritionCd = new short[personCount]; Array.Fill(NutritionCd, FullNutritionCd);
        SeedReserveG = new int[householdCount]; FoodCoverDaysX10 = new short[householdCount]; FuelCoverDaysX10 = new short[householdCount];
        Grievances = new Grievance[householdCount]; Tiers = new HouseholdTier[householdCount]; DietPoor = new bool[householdCount]; DueDurables = new byte[householdCount * Durables];
    }

    public NeedsBasketDef Basket(SocialClass c) => _baskets[(int)c];
    public IReadOnlyList<NeedsBasketDef> Baskets => _baskets;
    // The per-household and per-person arrays are exact-length from creation and grow geometrically once births and weddings add rows (population spec 2);
    // the counts below, not Length, say how many rows are real.
    private int _householdCount, _personCount;
    public int HouseholdCount => _householdCount;
    public int PersonCount => _personCount;

    /// <summary>Opens the purse and house store of household <paramref name="index"/> of <paramref name="h"/>, which must be the next unseen row (a new household from a wedding).</summary>
    public int AddHousehold(HouseholdTable h, int index)
    {
        if (index != _householdCount) throw new ArgumentException($"household {index} is not the next row ({_householdCount})", nameof(index));
        if (index == HouseholdPurse.Length)
        {
            int n = Math.Max(16, index * 2);
            HouseholdPurse = Grow(HouseholdPurse, n); HouseholdStore = Grow(HouseholdStore, n); SeedReserveG = Grow(SeedReserveG, n);
            FoodCoverDaysX10 = Grow(FoodCoverDaysX10, n); FuelCoverDaysX10 = Grow(FuelCoverDaysX10, n); Grievances = Grow(Grievances, n); Tiers = Grow(Tiers, n); DietPoor = Grow(DietPoor, n); DueDurables = Grow(DueDurables, n * Durables);
        }
        _householdCount++;
        OpenHousehold(h, index);
        return index;
    }

    /// <summary>Adds the economic columns of one new person (a birth): a full nutrition reserve. Returns the person's index, which must be the next row.</summary>
    public int AddPerson(int index)
    {
        if (index != _personCount) throw new ArgumentException($"person {index} is not the next row ({_personCount})", nameof(index));
        if (index == NutritionCd.Length) NutritionCd = Grow(NutritionCd, Math.Max(16, index * 2));
        NutritionCd[index] = FullNutritionCd;
        _personCount++;
        return index;
    }

    private static T[] Grow<T>(T[] a, int n) { var r = new T[n]; Array.Copy(a, r, a.Length); return r; }

    public Coin HouseholdCoin(int hh) => Ledger.Balance(HouseholdPurse[hh]);

    /// <summary>Content loaded from a database: goods, money settings and needs baskets.</summary>
    public static EconomyState FromContent(ContentDatabase db, int householdCount, int personCount)
    {
        var money = db.All<MoneySettingsDef>().OrderBy(m => m.Id, StringComparer.Ordinal).FirstOrDefault() ?? new MoneySettingsDef { Id = "money" };
        return new EconomyState(new GoodCatalog(db), money, db.All<NeedsBasketDef>().OrderBy(b => b.Id, StringComparer.Ordinal).ToList(), householdCount, personCount)
        {
            Tuning = db.All<EconomyTuningDef>().OrderBy(t => t.Id, StringComparer.Ordinal).FirstOrDefault() ?? new EconomyTuningDef { Id = "economy" },
        };
    }

    /// <summary>
    /// World creation (money spec 3.8): opens a purse and a store for every household and seeds coin and starting stock, deterministically for the seed.
    /// Seed money is the only non-mint creation; it runs once, before day 0. Starting stock (about a month of food, fuel and salt) is created as Seed goods.
    /// </summary>
    public static EconomyState Create(Population pop, ContentDatabase db, ulong seed, bool seedGoods = true)
    {
        var e = FromContent(db, pop.Households.Count, pop.People.Count);
        e.OpenHouseholds(pop);
        MoneySeeder.Seed(e, pop, seed);
        if (seedGoods) e.SeedStartingStock(pop);
        return e;
    }

    /// <summary>Opens the household purses and house stores (empty). Stowage capacity per dwelling follows the lot table (cottage 3,000 L ... manor 20,000 L) but is not enforced in Phase 2.</summary>
    public void OpenHouseholds(Population pop) => OpenHouseholds(pop.Households);

    /// <summary>Opens the household purses and house stores for a household table.</summary>
    public void OpenHouseholds(HouseholdTable h)
    {
        for (int i = 0; i < h.Count; i++) OpenHousehold(h, i);
    }

    private void OpenHousehold(HouseholdTable h, int i)
    {
        var id = h.IdOf(i);
        HouseholdPurse[i] = Ledger.Open(PurseKind.Household, id);
        long litres = h.Dwelling[i] switch { Ravensguard.Sim.WorldGen.Region.DwellingType.Longhouse => 6000, Ravensguard.Sim.WorldGen.Region.DwellingType.Burgage => 8000, Ravensguard.Sim.WorldGen.Region.DwellingType.Manor => 20000, _ => 3000 };
        HouseholdStore[i] = new Store(Goods, Mass, StoreKind.House, id, 0, litres * 1000, enforce: false);
    }

    /// <summary>A store that is not a household's (granary, barn, chest); capacity 0 means unlimited. Idempotent per owner.</summary>
    public Store OpenStore(StoreKind kind, EntityId owner, long capacityMassG = 0, long capacityVolumeMl = 0, bool? enforce = null)
    {
        if (OtherStores.TryGetValue(owner, out var s)) return s;
        return OtherStores[owner] = new Store(Goods, Mass, kind, owner, capacityMassG, capacityVolumeMl, enforce);
    }

    /// <summary>Starting stock per household: 30 days of staple and relish as wheat, 10 days of peas, 20 days of firewood, 30 of salt, 3 of ale.</summary>
    public void SeedStartingStock(Population pop) => SeedStartingStock(pop.Households);

    /// <summary>Starting stock for a household table.</summary>
    public void SeedStartingStock(HouseholdTable h) => SeedStartingStock(h, 0, h.Count);

    /// <summary>Starting stock for the households <paramref name="from"/> to <paramref name="to"/> - 1 (P5-A4: the hero's own household with children, made at the new game).</summary>
    public void SeedStartingStock(HouseholdTable h, int from, int to = -1)
    {
        ushort wheat = Goods.IdOf("wheat"), peas = Goods.IdOf("peas"), wood = Goods.IdOf("firewood"), salt = Goods.IdOf("salt"), ale = Goods.IdOf("ale");
        if (to < 0) to = from + 1;
        for (int i = from; i < to && i < h.Count; i++)
        {
            int n = h.MemberCount[i]; if (n == 0) continue;
            var b = Basket(h.Class[i]); var s = HouseholdStore[i];
            long measuresX100 = (long)n * (b.StapleMeasuresPerPersonDayX100 + b.RelishMeasuresPerPersonDayX100);
            s.Create(wheat, measuresX100 * HouseholdNeeds.GramsPerMeasure / 100 * 30, CreateReason.Seed);
            s.Create(peas, (long)n * b.RelishMeasuresPerPersonDayX100 * HouseholdNeeds.GramsPerMeasure / 100 * 10, CreateReason.Seed);
            s.Create(wood, (long)n * b.FirewoodGPerPersonDay * 20, CreateReason.Seed);
            s.Create(salt, (long)n * b.SaltGPerPersonDay * 30, CreateReason.Seed);
            s.Create(ale, (long)n * b.AleMlPerPersonDay * Goods.Def(ale).LitreMassG / 1000 * 3, CreateReason.Seed);
        }
    }

    /// <summary>Opens the hero's person purse and seeds it with the hero's starting crowns (a one-off Seed creation). Returns the purse.</summary>
    public PurseId SeedHero(int crowns)
    {
        var p = Ledger.Open(PurseKind.Person, HeroOwner);
        if (crowns > 0) Ledger.Mint(p, Coin.FromCrowns(crowns), SourceReason.Seed);
        return p;
    }

    /// <summary>One day of spoilage over every store, households first then the others, ascending owner id (items spec 4: the H4 slice).</summary>
    /// <summary>Difficulty (economy harshness): spoilage rates and harvest-variance amplitude in percent of the calibrated value; 100 is the game as calibrated.</summary>
    public int SpoilagePct = 100, HarvestVariancePct = 100;

    public long ApplyDailySpoilage(ulong seed, long hourKey, int tempC)
    {
        long lost = 0;
        for (int i = 0; i < _householdCount; i++) lost += HouseholdStore[i].ApplySpoilage(seed, hourKey, tempC, 1, SpoilagePct);
        foreach (var s in OtherStores.Values) lost += s.ApplySpoilage(seed, hourKey, tempC, 1, SpoilagePct);
        return lost;
    }

    /// <summary>Goods audit (soak 9.1): per good, <c>created - destroyed</c> equals the sum over every store, and no row is negative.</summary>
    public bool MassAuditHolds()
    {
        var sums = new long[Goods.Count];
        void Add(Store s) { foreach (var (g, q) in s.Rows()) { if (q < 0) return; sums[g] += q; } }
        for (int i = 0; i < _householdCount; i++) Add(HouseholdStore[i]);
        foreach (var s in OtherStores.Values) Add(s);
        // an instance that came out of stock was destroyed as Materialised (and is created again as Dematerialised), so the ledger's net is exactly the stores
        for (ushort g = 0; g < Goods.Count; g++)
            if (Mass.Net(g) != sums[g]) return false;
        return true;
    }

    /// <summary>Deterministic digest of the whole economy (ledger, stores, columns, items) for determinism and save round-trip tests.</summary>
    public string ComputeHash()
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var b8 = new byte[8];
        void L(long v) { System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(b8, v); h.AppendData(b8); }
        void S(string t) { L(t.Length); h.AppendData(System.Text.Encoding.UTF8.GetBytes(t)); }
        var st = Ledger.Export();
        L(st.Owner.Length); for (int i = 0; i < st.Owner.Length; i++) { L((long)st.Owner[i]); L(st.Kind[i]); L(st.Balance[i]); }
        foreach (var v in st.Sourced) L(v); foreach (var v in st.Sunk) L(v); foreach (var v in st.FlowCoin) L(v); foreach (var v in st.FlowCount) L(v); L(st.Seq);
        void Store(Store s) { L((long)s.Owner.Value); L((long)s.Kind); L(s.CapacityMassG); L(s.CapacityVolumeMl); foreach (var (g, q) in s.Rows()) { L(g); L(q); } L(-1); }
        for (int i = 0; i < _householdCount; i++) Store(HouseholdStore[i]);
        foreach (var (k, s) in OtherStores) { L((long)k.Value); Store(s); }
        foreach (var v in Mass.CreatedState()) L(v); foreach (var v in Mass.DestroyedState()) L(v);
        for (int i = 0; i < _personCount; i++) L(NutritionCd[i]);
        for (int i = 0; i < _householdCount; i++) L(SeedReserveG[i]);
        for (int i = 0; i < _householdCount; i++) L(FoodCoverDaysX10[i]);
        for (int i = 0; i < _householdCount; i++) L(FuelCoverDaysX10[i]);
        for (int i = 0; i < _householdCount; i++) L((long)Grievances[i]);
        for (int i = 0; i < _householdCount; i++) L((long)Tiers[i]);
        for (int i = 0; i < _householdCount; i++) L(DietPoor[i] ? 1 : 0);
        for (int i = 0; i < _householdCount * Durables; i++) if (DueDurables[i] != 0) { L(i); L(DueDurables[i]); }
        var snap = Items.Compact();
        L(snap.Rows.Length);
        foreach (var r in snap.Rows) { L((long)r.Id.Value); S(r.Shape); L(r.Good); L((long)r.Quality); L(r.ConditionPermille); L(r.Wear); L((long)r.Flags); L(r.Stains); L(r.LabourHours); L(r.Skill); L((long)r.Owner.Value); L(r.Parent); L(r.ComponentStart); L(r.ComponentCount); L(r.ContentsGood); L(r.ContentsQty); }
        foreach (var c in snap.Components) { S(c.Material); L(c.MassG); L(c.ConditionPermille); }
        return Convert.ToHexString(h.GetHashAndReset());
    }
}
