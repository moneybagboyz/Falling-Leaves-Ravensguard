using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Clothing;

/// <summary>Why a person died of exposure (the caller turns it into <c>DeathCause.Cold</c> or the weather cause and a chronicle entry).</summary>
public enum ExposureDeath : byte { None, Cold, Heat }

/// <summary>The part of a worn item the item table has no column for (wet, stains, dye, the size it was made for); a side table keyed on the item, saved with the book.</summary>
public struct ItemExtra
{
    /// <summary>Water held, in 1/100 permille (100,000 = fully soaked).</summary>
    public int WetU;
    public byte StainKind;
    /// <summary>Dye as 0xRRGGBB, or 0 for the undyed colour of the material.</summary>
    public uint Dye;
    public int FitCm3;
    public bool Hung;
    public int WetPm => WetU / 100;
}

/// <summary>One person the book tracks: what they wear and their body's state (spec 3.2: the person's core temperature and exposure columns, kept here for tracked people only).</summary>
public sealed class Tracked
{
    public EntityId Person;
    public readonly List<EntityId> Worn = [];
    /// <summary>Core temperature in <see cref="ClothingMath.CoreUnit"/> units (centi-degrees x 60,000).</summary>
    public long CoreU = ClothingMath.CoreUnitsOf(ClothingMath.NormalCore);
    public int ExposureMin;
    public int SevereMin, HeatMin;
    public bool Collapsed;
    public ExposureDeath Died;
    public long LastMinute = long.MinValue;
    public long WearDay = long.MinValue;
    public int ResistPm = 1000;
    public bool Fed = true;
    public int SizeCm3 = 70000;
    /// <summary>Game days a set of this person's clothes lasts (the Phase 2 durables lifetime of their class).</summary>
    public int LifeDays = 1095;
    // the last minute's numbers, for the HUD and the scorer (derived, not saved)
    public ExposureReading Last;
    public BodyState State;
    public int CoreCenti => ClothingMath.CentiOf(CoreU);
}

/// <summary>
/// The clothing and exposure model's state for the people being simulated live and the hero (clothing spec 3.2 and 4): worn items in their four layers as real item rows (shape, material, mass,
/// value, wear), their wetness and dye, and each tracked person's core temperature with the hypothermia and heat stages. Steps once per game minute per person; the weather and place come from
/// the caller as an <see cref="ExposureEnv"/> and an <see cref="Activity"/>, so the book itself holds no clock and no world: a minute's result is a function of the state and that input.
/// <para>
/// Owns its own item table (ids from a private allocator in region 255, which no world region uses), so the live tier can dress people on the main thread without touching the economy's table.
/// All tracked people are kept in id order and every draw is a counter RNG keyed on the person, so a run at any game speed makes the same people with the same clothes.
/// </para>
/// </summary>
public sealed class ClothingBook
{
    /// <summary>The origin region bits of the ids of items this book makes (E2: no world region is 255).</summary>
    public const int WardrobeRegion = 255;
    private const int MinutesPerDay = 1440;
    /// <summary>A catch-up never walks more than this many minutes (a person promoted after a long absence starts from the last 90).</summary>
    public const int MaxCatchUpMinutes = 90;

    public ContentDatabase Db { get; }
    public ExposureTuning Tuning { get; }
    public ItemTable Items { get; private set; } = new();
    public IdAllocator Ids { get; private set; } = IdAllocator.ForRegion(WardrobeRegion);
    public IReadOnlyDictionary<string, Garment> Garments => _garments;
    private readonly Dictionary<string, Garment> _garments = new(StringComparer.Ordinal);
    private readonly IReadOnlyDictionary<string, ResolvedMaterial> _materials;
    private readonly Dictionary<string, WardrobeDef> _wardrobes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _lifetimeDays = new(StringComparer.Ordinal);
    private SortedDictionary<EntityId, Tracked> _people = [];
    private SortedDictionary<EntityId, ItemExtra> _extra = [];

    public int Count => _people.Count;
    public IEnumerable<Tracked> People => _people.Values;
    public bool IsTracked(EntityId person) => _people.ContainsKey(person);
    public Tracked? Of(EntityId person) => _people.GetValueOrDefault(person);
    public ItemExtra ExtraOf(EntityId item) => _extra.GetValueOrDefault(item);

    /// <summary>P6-B6 (holdings 3.8, HW-10): the fit size (cm3) of a garment the book keeps, or 0 for a piece it does not know (the refit's read).</summary>
    public int FitOf(EntityId item) => _extra.TryGetValue(item, out var e) ? e.FitCm3 : 0;

    /// <summary>P6-B6: a refit writes the garment's fit size; false when the book does not keep the piece (the crafting state keeps its fit then).</summary>
    public bool SetFit(EntityId item, int fitCm3)
    {
        if (!_extra.TryGetValue(item, out var e)) return false;
        e.FitCm3 = fitCm3; _extra[item] = e;
        return true;
    }

    public ClothingBook(ContentDatabase db)
    {
        Db = db; Tuning = ExposureTuning.From(db); _materials = MaterialResolver.ResolveAll(db);
        foreach (var s in db.All<ArmourShapeDef>().OrderBy(s => s.Id, StringComparer.Ordinal)) _garments[s.Id] = Garment.From(s);
        foreach (var w in db.All<WardrobeDef>().OrderBy(w => w.Id, StringComparer.Ordinal)) _wardrobes[w.Class] = w;
        foreach (var b in db.All<NeedsBasketDef>()) _lifetimeDays[b.Class] = b.ClothesLifetimeDays;
    }

    public Garment GarmentOf(EntityId item) => _garments[Items.Get(item).Shape];

    // ------------------------------------------------------------------ dressing

    /// <summary>Cold seasons for the wardrobe: winter, the second half of autumn and the first week of spring (spec 4.1: everyone dressed for the season).</summary>
    public static bool ColdSeason(long gameSec)
    {
        var t = new GameTime(gameSec); int d = t.DayOfSeason;
        return t.Season == Season.Winter || (t.Season == Season.Autumn && d >= 8) || (t.Season == Season.Spring && d <= 7);
    }

    /// <summary>
    /// Starts tracking a person and dresses them from the wardrobe of their class (real item rows owned by the person). The pieces they own are a stable draw on their id; a cloak, hood or gloves are put on
    /// in the cold seasons. Already tracked: returns the existing record. <paramref name="toughness"/> sets cold resistance (spec 4.3), jittered by 3% on stream 0x331.
    /// </summary>
    public Tracked Track(EntityId person, Sex sex, SocialClass cls, Occupation occ, int toughness, ulong seed, long gameSec, bool dress = true)
    {
        if (_people.TryGetValue(person, out var have)) return have;
        var t = new Tracked { Person = person };
        int jitter = DetRng.Range(seed, 0, person.Value, RngStreams.ColdTolerance, 0, -30, 31);
        t.ResistPm = ClothingMath.ResistPm(toughness, jitter);
        t.SizeCm3 = (sex == Sex.Female ? 62000 : 72000) + DetRng.Range(seed, 0, person.Value, RngStreams.Wardrobe, 98, -4000, 4001);
        t.LifeDays = _lifetimeDays.GetValueOrDefault(cls.ToString(), 1095);
        _people[person] = t;
        if (dress) Dress(t, sex, cls, occ, seed, gameSec);
        t.WearDay = gameSec / TimeConst.SecondsPerDay;
        return t;
    }

    public void Untrack(EntityId person)
    {
        if (!_people.Remove(person, out var t)) return;
        foreach (var it in t.Worn) { _extra.Remove(it); Items.Free(it); }
    }

    private void Dress(Tracked t, Sex sex, SocialClass cls, Occupation occ, ulong seed, long gameSec)
    {
        if (!_wardrobes.TryGetValue(cls.ToString(), out var wd)) return;
        bool cold = ColdSeason(gameSec);
        var chosen = new List<(WardrobePieceDef P, Garment G, int I)>();
        for (int i = 0; i < wd.Pieces.Count; i++)
        {
            var p = wd.Pieces[i];
            if (p.Season == "cold" && !cold) continue;
            if (p.Season == "warm" && cold) continue;
            if (p.Occupations.Count > 0 && !p.Occupations.Contains(occ.ToString())) continue;
            if (p.ChancePm < 1000 && DetRng.Range(seed, 0, t.Person.Value, RngStreams.Wardrobe, (uint)i, 0, 1000) >= p.ChancePm) continue;
            var g = _garments[p.Shape];
            if (!g.Wearable || chosen.Any(c => !ClothingMath.CanStack(g, c.G))) continue;
            chosen.Add((p, g, i));
        }
        foreach (var (p, g, i) in chosen)
        {
            int cond = Math.Max(0, 1000 - p.WornPm);
            // the outer wool pieces are dyed, each its own colour from the class's dyes (a stable draw on the person and the piece); linen next to the skin, hose and leather keep the colour of the material
            uint d = 0;
            if (wd.Dyes.Count > 0 && p.Material == "wool" && g.Id != "hose" && g.Layer is ArmourLayer.Over or ArmourLayer.Cover)
                d = Convert.ToUInt32(wd.Dyes[DetRng.Range(seed, 0, t.Person.Value, RngStreams.Wardrobe, (uint)(100 + i), 0, wd.Dyes.Count)], 16);
            Wear(t, Create(g, p.Material, t.Person, cond, d, t.SizeCm3));
        }
        t.Collapsed = false;
    }

    /// <summary>Makes a garment as a real item row: one component of the material, its mass scaled from the shape's default material by density, labour baked into its value.</summary>
    public EntityId Create(Garment g, string material, EntityId owner, int conditionPermille = 1000, uint dye = 0, int fitCm3 = 70000, ItemQuality quality = ItemQuality.Common)
    {
        var m = _materials[material];
        double refDensity = g.DefaultMaterial != null && _materials.TryGetValue(g.DefaultMaterial, out var dm) ? dm.DensityKgM3 : m.DensityKgM3;
        int massG = (int)Math.Max(1, (long)g.MassG * (long)Math.Round(m.DensityKgM3) / Math.Max(1, (long)Math.Round(refDensity)));
        var id = Items.Add(Ids, g.Id, quality, owner, [new ItemComponent(material, massG, (short)conditionPermille)], ItemTable.NoGood, labourHours: Math.Max(1, g.MassG / 150), skill: 40, conditionPermille: conditionPermille);
        _extra[id] = new ItemExtra { Dye = dye, FitCm3 = fitCm3 };
        return id;
    }

    // ------------------------------------------------------------------ wearing

    /// <summary>Puts a piece on (sorted into layer, order and id). The piece must exist; the caller checked fit and stacking.</summary>
    public void Wear(Tracked t, EntityId item)
    {
        if (t.Worn.Contains(item)) return;
        t.Worn.Add(item);
        t.Worn.Sort(CompareWorn);
    }

    private int CompareWorn(EntityId a, EntityId b)
    {
        var ga = GarmentOf(a); var gb = GarmentOf(b);
        int c = ((int)ga.Layer).CompareTo((int)gb.Layer);
        if (c != 0) return c;
        c = ga.Order.CompareTo(gb.Order);
        return c != 0 ? c : a.CompareTo(b);
    }

    /// <summary>Why a person cannot put the piece on, or null when they can: not wearable, too big or small (spec 4.1, C-9), or nothing stacks over what is already worn.</summary>
    public string? CannotWear(Tracked t, EntityId item)
    {
        var g = GarmentOf(item); var ex = _extra[item];
        if (!g.Wearable) return "it is not worn";
        if (!ClothingMath.Fits(ex.FitCm3, t.SizeCm3)) return t.SizeCm3 > ex.FitCm3 ? "too small; refit at a tailor or armourer" : "too big; refit at a tailor or armourer";
        foreach (var w in t.Worn) if (!ClothingMath.CanStack(g, GarmentOf(w))) return "something is already worn there";
        return null;
    }

    public bool Remove(Tracked t, EntityId item) => t.Worn.Remove(item);

    /// <summary>The (garment, condition, wet) triples of what a person wears, inner to outer.</summary>
    public (Garment G, int Cond, int Wet)[] WornOf(Tracked t)
    {
        var r = new (Garment, int, int)[t.Worn.Count];
        for (int i = 0; i < r.Length; i++) { ref var row = ref Items.Get(t.Worn[i]); r[i] = (_garments[row.Shape], row.ConditionPermille, _extra[t.Worn[i]].WetPm); }
        return r;
    }

    public int EncumbranceOf(Tracked t) { int e = 0; foreach (var w in t.Worn) e += GarmentOf(w).EncPm; return e; }

    /// <summary>Grams a person's clothes weigh now, wet included (spec 4.5).</summary>
    public long WornMassG(Tracked t, bool wet = true)
    {
        long m = 0;
        foreach (var w in t.Worn) { long dry = Items.MassG(Items.IndexOf(w), null!); m += wet ? ClothingMath.WetMassG(dry, GarmentOf(w).AbsorbPct, _extra[w].WetPm) : dry; }
        return m;
    }

    /// <summary>Soaks every piece (a fall into a river): <paramref name="fractionPm"/> of the way to 1000 from the waist down is the caller's choice; 1000 sets every piece to full wet.</summary>
    public void Immerse(Tracked t, int wetPm = 1000)
    {
        foreach (var w in t.Worn) { var e = _extra[w]; e.WetU = Math.Max(e.WetU, wetPm * 100); _extra[w] = e; SetWetFlag(w, e.WetPm); }
    }

    private void SetWetFlag(EntityId item, int wetPm)
    {
        ref var row = ref Items.Get(item);
        row.Flags = wetPm > 200 ? row.Flags | ItemFlags.Wet : row.Flags & ~ItemFlags.Wet;
    }

    /// <summary>Stains a piece with <paramref name="amount"/> of a kind (the 0..255 column) and sets the kind flag.</summary>
    public void Stain(EntityId item, byte kind, int amount)
    {
        ref var row = ref Items.Get(item); var e = _extra[item];
        row.Stains = (byte)Math.Min(255, row.Stains + amount); row.Flags |= ItemFlags.Stained; e.StainKind |= kind; _extra[item] = e;
    }

    /// <summary>Washes a piece (spec 4.7): mud and dirt go entirely, blood and grease by 70% unless set; lye soap takes everything. The piece comes out soaked.</summary>
    public void Wash(EntityId item, bool soap)
    {
        ref var row = ref Items.Get(item); var e = _extra[item];
        bool set = (e.StainKind & ClothingMath.Set) != 0;
        if (soap) { row.Stains = 0; e.StainKind = 0; }
        else if (!set) { row.Stains = (byte)(row.Stains * 30 / 100); if (row.Stains == 0) e.StainKind = 0; }
        if (row.Stains == 0) row.Flags &= ~ItemFlags.Stained;
        e.WetU = 100_000; _extra[item] = e; SetWetFlag(item, 1000);
    }

    /// <summary>Value of a worn or carried piece in mites with its stain loss applied (the item table's flat per-stain discount is replaced by the spec's per-kind rule).</summary>
    public long ValueMites(EntityId item)
    {
        int row = Items.IndexOf(item); ref var r = ref Items.At(row); var e = _extra[item];
        byte stains = r.Stains; r.Stains = 0;
        long v = Items.ValueMites(row, mat => _materials[mat].ValuePerKg); r.Stains = stains;
        byte kind = e.StainKind == 0 ? (byte)0 : (byte)(e.StainKind & 31);
        int worst = 0; for (byte k = 1; k <= 16; k <<= 1) if ((kind & k) != 0) worst = Math.Max(worst, ClothingMath.StainLossPm(k, stains));
        return v * (1000 - worst) / 1000;
    }

    /// <summary>Wring out a piece: 150 permille less wet.</summary>
    public void WringOut(EntityId item) { var e = _extra[item]; e.WetU = Math.Max(0, e.WetU - 15000); _extra[item] = e; SetWetFlag(item, e.WetPm); }

    // ------------------------------------------------------------------ the minute

    /// <summary>
    /// Steps a person from the last minute they were stepped to <paramref name="minute"/> (game minutes since year 0), one minute at a time with the same <paramref name="env"/> and
    /// <paramref name="act"/> (a catch-up of more than <see cref="MaxCatchUpMinutes"/> starts from the last 90). Returns the first death the steps caused.
    /// </summary>
    public ExposureDeath StepTo(Tracked t, long minute, in ExposureEnv env, Activity act)
    {
        if (t.Died != ExposureDeath.None) return t.Died;
        long from = t.LastMinute == long.MinValue ? minute : Math.Max(t.LastMinute + 1, minute - MaxCatchUpMinutes + 1);
        for (long m = from; m <= minute; m++) { StepMinute(t, m, env, act); if (t.Died != ExposureDeath.None) break; }
        t.LastMinute = minute;
        return t.Died;
    }

    /// <summary>One game minute for one person: wetting and drying, the effective insulation, the stress, the core, the stages and daily wear.</summary>
    public void StepMinute(Tracked t, long minute, in ExposureEnv env, Activity act)
    {
        if (t.Died != ExposureDeath.None) return;
        var tu = Tuning;
        // wear: once per game day of wearing, the clothes lose 1000 / lifetime of their condition (the Phase 2 durables lifetime), kept as a remainder in the item's Wear column
        long day = minute / MinutesPerDay;
        if (t.WearDay == long.MinValue) t.WearDay = day;
        for (; t.WearDay < day; t.WearDay++) WearOneDay(t);
        // wetness
        StepWet(t, env);
        var worn = WornOf(t);
        int iEff = ClothingMath.IEff(tu, worn);
        bool sheltered = ClothingMath.Sheltered(env);
        int wp = ClothingMath.WindproofPm(worn);
        int windchill = sheltered ? 0 : ClothingMath.WindChillCenti(tu, env.WindCmps, wp);
        int tAir = ClothingMath.TAir(tu, env);
        int tEff = tAir - windchill;
        int cold = ClothingMath.ColdStressPm(tu, ClothingMath.IReq(tu, tEff, act), iEff + (env.InBed ? tu.Def.BeddingCloX100 : 0));
        int ret = 0; int enc = 0; long wetSum = 0;
        foreach (var (g, _, w) in worn) { ret += g.HeatRetentionPm; enc += g.EncPm; wetSum += w; }
        int heatT = env.Shade ? tAir - 400 : tAir;
        int heat = ClothingMath.HeatStressPm(tu, heatT, act, iEff, ret);
        int reqNow = ClothingMath.IReq(tu, tEff, act);
        t.Last = new ExposureReading(iEff, reqNow, cold, heat, windchill, tEff, wp, ret, enc, worn.Length == 0 ? 0 : (int)(wetSum / worn.Length));
        long rate = ClothingMath.CoreRatePerHour(tu, cold, heat, t.CoreCenti, env.Fire, t.ResistPm, t.Fed);
        t.CoreU += rate;                                            // milli-cC an hour == one minute of core units
        if (t.CoreU < 0) t.CoreU = 0;
        int core = t.CoreCenti;
        t.State = ClothingMath.StateOf(tu, core);
        t.ExposureMin = cold > tu.Def.ComfortStressPm || heat > 0 ? Math.Min(60000, t.ExposureMin + 1) : 0;
        // collapse, then death: severe cold for half an hour drops a person unconscious; only an unconscious person at lethal cold dies (open question 1, default)
        if (core < tu.Def.SevereCc) { t.SevereMin++; if (t.SevereMin >= tu.Def.CollapseMinutes) t.Collapsed = true; }
        else { t.SevereMin = 0; if (t.State < BodyState.Warm && core >= tu.Def.ModerateCc) t.Collapsed = false; }
        if (core > tu.Def.StrokeCc) { t.Collapsed = true; t.HeatMin++; }
        else if (core <= tu.Def.StrokeCoolCc) { t.HeatMin = 0; if (core < tu.Def.WarmCc && core >= tu.Def.ModerateCc) t.Collapsed = false; }
        if (t.State == BodyState.Lethal && t.Collapsed) t.Died = ExposureDeath.Cold;
        else if (t.HeatMin >= tu.Def.HeatstrokeDeathMinutes) t.Died = ExposureDeath.Heat;
    }

    private void StepWet(Tracked t, in ExposureEnv env)
    {
        var tu = Tuning; int n = t.Worn.Count;
        if (n == 0) return;
        bool rain = env.PrecipTenthsMmH > 0 && !env.Roofed;
        int dry = ClothingMath.DryPerMinute(tu, env);
        Span<int> gain = stackalloc int[n];
        if (rain)
        {
            // an item is exposed when no piece of a higher layer covers its main region; the piece next inside an exposed, soaked one gets a leak (spec 4.5)
            for (int i = 0; i < n; i++)
            {
                var g = GarmentOf(t.Worn[i]); var pr = g.Principal(tu.RegionsPm);
                bool covered = false;
                for (int j = i + 1; j < n && !covered; j++) { var h = GarmentOf(t.Worn[j]); covered = h.Layer > g.Layer && h.Covers(pr); }
                if (!covered) gain[i] = ClothingMath.WetGainPerMinute(tu, env.PrecipTenthsMmH, env.Snow, g.WaterproofPm);
            }
            for (int i = 0; i < n; i++)
            {
                if (gain[i] == 0) continue;
                var g = GarmentOf(t.Worn[i]); var pr = g.Principal(tu.RegionsPm);
                if (_extra[t.Worn[i]].WetPm <= 700) continue;
                for (int j = i - 1; j >= 0; j--) { var h = GarmentOf(t.Worn[j]); if (h.Layer < g.Layer && h.Covers(pr)) { gain[j] = Math.Max(gain[j], gain[i] * tu.Def.WetLeakInnerPct / 100); break; } }
            }
        }
        for (int i = 0; i < n; i++)
        {
            var id = t.Worn[i]; var e = _extra[id];
            int before = e.WetPm;
            e.WetU = Math.Clamp(e.WetU + gain[i] - (gain[i] > 0 ? 0 : dry), 0, 100_000);
            if (e.WetPm != before) SetWetFlag(id, e.WetPm);
            _extra[id] = e;
        }
    }

    private void WearOneDay(Tracked t)
    {
        foreach (var w in t.Worn)
        {
            ref var row = ref Items.Get(w);
            int add = 100_000 / Math.Max(1, t.LifeDays);                  // 1/100 permille a day
            int acc = Goods.QualityTiers.WearFraction(row.Wear) + add; int lose = acc / 100; row.Wear = Goods.QualityTiers.WithWearFraction(row.Wear, acc % 100);   // the low bits; the top four are the repair count (P5-A1)
            if (lose > 0) row.ConditionPermille = (short)Math.Max(0, row.ConditionPermille - lose);
        }
    }

    // ------------------------------------------------------------------ queries

    /// <summary>Hours until the core reaches <paramref name="targetCenti"/> at the current rate, or -1 when it is not falling or rising towards it (the HUD's "chilled in 1.3 h").</summary>
    public int MinutesTo(Tracked t, int targetCenti, bool fire = false)
    {
        long rate = ClothingMath.CoreRatePerHour(Tuning, t.Last.ColdStressPm, t.Last.HeatStressPm, t.CoreCenti, fire, t.ResistPm, t.Fed);
        long gap = ClothingMath.CoreUnitsOf(targetCenti) - t.CoreU;
        if (rate == 0 || (gap > 0) != (rate > 0) || gap == 0) return -1;
        return (int)Math.Min(int.MaxValue, Math.Abs(gap) / Math.Abs(rate));
    }

    /// <summary>Total mass of every item the book holds, grams (dry): what the conservation test sums.</summary>
    public long TotalMassG() { long m = 0; foreach (int i in Items.LiveRows()) m += Items.MassG(i, null!); return m; }

    /// <summary>A short hash of every tracked person and item (for the determinism tests).</summary>
    public string StateHash()
    {
        var h = System.Security.Cryptography.SHA256.Create();
        var sb = new System.Text.StringBuilder();
        foreach (var t in _people.Values)
        {
            sb.Append(t.Person.Value).Append(':').Append(t.CoreU).Append(',').Append(t.ExposureMin).Append(',').Append(t.SevereMin).Append(',').Append(t.HeatMin).Append(',').Append(t.Collapsed ? 1 : 0).Append(',').Append((int)t.Died);
            foreach (var w in t.Worn) { var e = _extra[w]; ref var r = ref Items.Get(w); sb.Append('|').Append(w.Value).Append(r.Shape).Append(r.ConditionPermille).Append('.').Append(r.Wear).Append('.').Append(e.WetU).Append('.').Append(e.Dye); }
            sb.Append('\n');
        }
        return Convert.ToHexString(h.ComputeHash(System.Text.Encoding.UTF8.GetBytes(sb.ToString())));
    }

    // ------------------------------------------------------------------ save

    /// <summary>The whole book as MessagePack bytes (items, side columns and the tracked people, all in id order): equal state gives equal bytes.</summary>
    public byte[] ToBytes() => Save.SaveCodec.Serialize(ClothingSave.Capture(this));

    /// <summary>The body a save keeps for one tracked person (the hero): core temperature, exposure counters, the minute it was last stepped, and the wetness of each worn piece by its shape, in wearing order.</summary>
    public HeroBody CaptureBody(Tracked t)
    {
        var shapes = new string[t.Worn.Count]; var wet = new int[t.Worn.Count];
        for (int i = 0; i < t.Worn.Count; i++) { shapes[i] = GarmentOf(t.Worn[i]).Id; wet[i] = ExtraOf(t.Worn[i]).WetU; }
        return new HeroBody(t.CoreU, t.ExposureMin, t.SevereMin, t.HeatMin, t.Collapsed, t.Died, t.LastMinute, t.WearDay, shapes, wet);
    }

    /// <summary>
    /// Puts a saved body back on a tracked person who is already dressed: the core and counters, and each saved piece's wetness on the first worn piece of the same shape not yet matched (the
    /// hero is dressed again from the inventory on load, so the item ids are new but the shapes and their order are the same). Pieces with no match are left dry.
    /// </summary>
    public void ApplyBody(Tracked t, HeroBody b)
    {
        t.CoreU = b.CoreU; t.ExposureMin = b.ExposureMin; t.SevereMin = b.SevereMin; t.HeatMin = b.HeatMin; t.Collapsed = b.Collapsed; t.Died = b.Died; t.LastMinute = b.LastMinute; t.WearDay = b.WearDay;
        var used = new bool[t.Worn.Count];
        for (int k = 0; k < b.WornShapes.Length; k++)
            for (int i = 0; i < t.Worn.Count; i++)
            {
                if (used[i] || GarmentOf(t.Worn[i]).Id != b.WornShapes[k]) continue;
                used[i] = true; var e = _extra[t.Worn[i]]; e.WetU = Math.Max(0, b.WornWetU[k]); _extra[t.Worn[i]] = e; SetWetFlag(t.Worn[i], e.WetPm);
                break;
            }
    }

    public static ClothingBook FromBytes(ContentDatabase db, ReadOnlyMemory<byte> bytes)
    {
        var b = new ClothingBook(db);
        Save.SaveCodec.Deserialize<ClothingSave>(bytes).Restore(b);
        return b;
    }

    internal SortedDictionary<EntityId, Tracked> PeopleMap => _people;
    internal SortedDictionary<EntityId, ItemExtra> ExtraMap => _extra;
    internal void Replace(ItemTable items, IdAllocator ids, SortedDictionary<EntityId, Tracked> people, SortedDictionary<EntityId, ItemExtra> extra) { Items = items; Ids = ids; _people = people; _extra = extra; }
}

/// <summary>A tracked person's body as a save keeps it (<see cref="ClothingBook.CaptureBody"/>, <see cref="ClothingBook.ApplyBody"/>): the hero's core temperature and the wetness of what they wear.</summary>
public sealed record HeroBody(long CoreU, int ExposureMin, int SevereMin, int HeatMin, bool Collapsed, ExposureDeath Died, long LastMinute, long WearDay, string[] WornShapes, int[] WornWetU);
