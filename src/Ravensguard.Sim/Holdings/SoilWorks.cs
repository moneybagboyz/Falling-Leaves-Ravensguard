using System.Globalization;
using Ravensguard.Content;
using Ravensguard.Sim.Contracts;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Farming;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Holdings;

/// <summary>What is put on a field: marl (+15 for twenty game years, then 1 a year fades) or dung (+8 for two seasons). Append only.</summary>
public enum SoilKind : byte { Marl, Manure }

/// <summary>Where a piece of soil work stands. Append only.</summary>
public enum SoilState : byte { Open, Working, Done, Failed }

/// <summary>
/// One job of improving a field (an acre: holdings-and-works-v2 3.5): what and where, who posted it (the contract with its escrow, or none for the hero's own field), the worker-hours it takes and how many are
/// done, and the day it was finished. The worker is the contract's taker, so the book needs no second copy of him.
/// </summary>
public sealed class SoilWork
{
    public int Serial;
    public int Field, Parcel, Settlement;
    public SoilKind Kind;
    public SoilState State;
    /// <summary>The contract's id (0: the hero's own work on his own field, nobody pays him).</summary>
    public int Contract;
    public EntityId Issuer;
    public long NeedMilli, DoneMilli;
    public int PostedDay, DoneDay = -1;
    /// <summary>P8-BL-1 (D13): the chronicle entry of the need a household's own job answers (its root, the cause of the job's finish); 0 for a posted job, whose posting is the cause. Saved as an optional last element of the row.</summary>
    public ulong NeedEntry;
}

/// <summary>A improvement standing on a field: what it added (<see cref="Amount"/> points of fertility, what the clamp left of the full amount), when it ends (<see cref="EndDay"/> for dung) or starts to fade (marl).</summary>
public sealed class SoilEffect
{
    public int Field;
    public SoilKind Kind;
    public int Amount;
    public int StartDay, EndDay, LastFade;
}

/// <summary>The soil works and the improvements standing (the optional "landworks" save section; empty books are not written, so older saves keep their bytes).</summary>
public sealed class SoilBook
{
    public int NextSerial = 1;
    public List<SoilWork> Works { get; } = [];
    public List<SoilEffect> Effects { get; } = [];
    public bool IsEmpty => NextSerial == 1 && Works.Count == 0 && Effects.Count == 0;

    public SoilWork? Get(int serial) { foreach (var w in Works) if (w.Serial == serial) return w; return null; }
    public SoilWork? ByContract(int contract) { if (contract <= 0) return null; foreach (var w in Works) if (w.Contract == contract) return w; return null; }
    public bool Live(int field) { foreach (var w in Works) if (w.Field == field && w.State is SoilState.Open or SoilState.Working) return true; foreach (var e in Effects) if (e.Field == field) return true; return false; }

    public string ComputeHash()
    {
        ulong h = 0x5011B00C0DE00001UL;
        void M(long v) => h = DetRng.Mix(h ^ ((ulong)v + 0x9E3779B97F4A7C15UL));
        M(NextSerial);
        foreach (var w in Works) { M(w.Serial); M(w.Field); M(w.Parcel); M(w.Settlement); M((long)w.Kind); M((long)w.State); M(w.Contract); M((long)w.Issuer.Value); M(w.NeedMilli); M(w.DoneMilli); M(w.PostedDay); M(w.DoneDay); }
        foreach (var e in Effects) { M(e.Field); M((long)e.Kind); M(e.Amount); M(e.StartDay); M(e.EndDay); M(e.LastFade); }
        return h.ToString("X16", CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// Soil improvement as contracts (holdings-and-works-v2 3.5, HW-5). Each season the holders of fields post the work for those whose fertility has fallen below 50: the lord's planner on his demesne
/// (paid from the fief's treasury), a household on its own land (from its purse), as a <c>soil_improvement</c> contract with its pay in escrow (an unpayable job is not posted). Marl (250 worker-hours and
/// 40 tonnes an acre from a marl pit, where a clay pit lies within 3 km) adds 15 points of fertility for twenty game years, then 1 a year fades; dung (1 tonne an acre from the byres and the sheep folded
/// on the fallow, while the place's herds have it to spare that year) adds 8 for two seasons. The hero takes the contract and works it by hand (<c>hw:soilwork</c>, eight heavy hours a day, as felling), or
/// improves a field of his own household's without a contract (<c>hw:soilown</c>); a job nobody takes in a week is taken by the farmhands of the place, who finish it by the contract rules (the companies' odds).
/// Marl and dung are not goods: the pit and the byre are sources of the place, the cost is the labour (a deviation from a goods model, noted in the spec). Integer arithmetic; nothing is drawn here.
/// </summary>
public sealed class SoilWorld
{
    public SoilBook Book { get; }
    public LandWorld Land { get; }
    public FarmingSystem Farming { get; }
    public WorldState St { get; }
    public LandRulesDef Rules => Land.Rules;
    /// <summary>Whether a marl pit lies near a settlement (a clay pit within 3 km); the wiring reads the resource layer. Null: none does.</summary>
    public Func<int, bool>? HasMarlPit { get; set; }
    /// <summary>The day of the last hour the world was stepped (a company's finish carries no day of its own).</summary>
    public int LastDay { get; private set; }
    public const int CrewIdBase = 0x4B00_0000;

    public SoilWorld(SoilBook book, LandWorld land, FarmingSystem farming, WorldState st)
    {
        Book = book; Land = land; Farming = farming; St = st;
        farming.AfterLabour = OwnHours;                      // P8-BL-1 (D13): the households' own hands, in the idle hours the day's farm work left
    }

    public static SoilWorld? For(WorldState st) => st.HomeRecords?.Sim.Get<SoilWorld>();
    private FieldTable F => Farming.State.Fields;
    private RegionData Region => Land.Region;
    public static EntityId CrewId(int settlement) => EntityId.Make(IdKind.Faction, (ulong)CrewIdBase + (ulong)settlement);

    public int HoursOf(SoilKind k) => k == SoilKind.Marl ? Rules.MarlHours : Rules.ManureHours;
    public int AmountOf(SoilKind k) => k == SoilKind.Marl ? Rules.MarlFertility : Rules.ManureFertility;
    public long PayOf(SoilKind k) => HoursOf(k) * (long)RecipeBook.WagePerHour(WageClass.Unskilled) * Rules.SoilPayPm / 1000;

    // ------------------------------------------------------------------ what the place has

    /// <summary>Dung left in a place this game year, kilograms: the herds' year's worth less what jobs of this year have taken.</summary>
    public long DungLeftKg(int settlement, int day)
    {
        var lv = Farming.State.Livestock; if ((uint)settlement >= (uint)lv.Sheep.Length) return 0;
        long year = (long)lv.Sheep[settlement] * Rules.DungKgPerSheepYear + ((long)lv.Oxen[settlement] + lv.Cows[settlement]) * Rules.DungKgPerBeastYear;
        long used = 0; int y = day / TimeConst.DaysPerYear;
        foreach (var w in Book.Works) if (w.Kind == SoilKind.Manure && w.Settlement == settlement && w.State != SoilState.Failed && w.PostedDay / TimeConst.DaysPerYear == y) used += Rules.ManureKg;
        return year - used;
    }

    /// <summary>The better of what a place can give a field: marl when a pit is near, else dung while the herds have it; null when neither.</summary>
    public SoilKind? KindFor(int settlement, int day)
    {
        if (HasMarlPit?.Invoke(settlement) == true) return SoilKind.Marl;
        return DungLeftKg(settlement, day) >= Rules.ManureKg ? SoilKind.Manure : null;
    }

    // ------------------------------------------------------------------ posting (a season's first day)

    /// <summary>The season's posting: for each parcel in index order, each of its fields in order under the fertility bar with no job or improvement on it. Returns the contracts posted.</summary>
    public List<WorkContract> Season(ContractWorld w, ContractBook cb, int day)
    {
        var posted = new List<WorkContract>(); var perPlace = new Dictionary<int, int>();
        var hero = St.Party?.Hero ?? EntityId.None; int heroHh = HeroHousehold();
        var pop = Land.Pop; var hht = pop.Households; var r = Region; var F = this.F;
        if (w.Content.Kind("soil_improvement") == null) return posted;
        var crews = new HashSet<EntityId>(w.Companies().Select(x => x.Id));
        for (int p = 0; p < r.Parcels.Count; p++)
        {
            var pc = r.Parcels[p]; int s = pc.Settlement;
            if (pc.Owner.Kind == OwnerKind.Glebe || perPlace.GetValueOrDefault(s) >= Rules.SoilPostsPerSeason) continue;
            // no crew in the place, no job: a posting nobody can take only fills the board (the soak of P6-G2 had 500 open soil jobs on 36 boards by year 25 and no delivery ever posted again, 39,000 need entries a century)
            // P8-BL-1 (D13): but a household's own land is worked by its own hands first, so it needs no crew to begin
            bool crewHere = crews.Contains(CrewId(s));
            if (!crewHere && (pc.Owner.Kind == OwnerKind.Demesne || !OwnHandsFirst)) continue;
            EntityId issuer; PurseId? purse;
            if (pc.Owner.Kind == OwnerKind.Demesne)
            {
                int f = r.Settlements[s].FiefIndex; if (f < 0 || f >= w.FiefIds.Length) continue;
                issuer = w.FiefIds[f]; purse = w.TreasuryOf(s);
            }
            else
            {
                int hh = p < pop.ParcelHolder.Length ? pop.ParcelHolder[p] : -1;
                if (hh < 0 || hh >= hht.Count || hht.MemberCount[hh] == 0 || hh == heroHh) continue;
                issuer = hht.IdOf(hh); purse = w.PurseOfOwner(issuer);
                if (OwnHandsFirst && purse is { } own && !own.IsNone)
                {
                    // the holder's own household takes the field in hand: no contract, no escrow, no one is paid; the hours come from the adults' idle time (OwnHours)
                    for (int k = 0; k < pc.FieldCount && perPlace.GetValueOrDefault(s) < Rules.SoilPostsPerSeason; k++)
                    {
                        int f = pc.FieldStart + k;
                        if (f >= F.Count || F.Fertility[f] >= Rules.SoilPostBelowFertility || Book.Live(f)) continue;
                        if (KindFor(s, day) is not { } kind1) break;
                        var need1 = ContractSystem.NeedEntry(w, "soil", s, day * 86_400L + 600, [issuer]);                  // the poor field is the need: the root the finish hangs on
                        Book.Works.Add(new SoilWork { Serial = Book.NextSerial++, Field = f, Parcel = p, Settlement = s, Kind = kind1, State = SoilState.Open, Contract = 0, Issuer = issuer, NeedMilli = HoursOf(kind1) * 1000L, PostedDay = day, NeedEntry = need1.Id.Value });
                        perPlace[s] = perPlace.GetValueOrDefault(s) + 1;
                    }
                    continue;
                }
            }
            if (purse is not { } pr) continue;
            for (int k = 0; k < pc.FieldCount && perPlace.GetValueOrDefault(s) < Rules.SoilPostsPerSeason; k++)
            {
                int f = pc.FieldStart + k;
                if (f >= F.Count || F.Fertility[f] >= Rules.SoilPostBelowFertility || Book.Live(f)) continue;
                if (KindFor(s, day) is not { } kind) break;
                long pay = PayOf(kind);
                if (w.Ledger.Balance(pr).Mites < pay) break;
                var need = ContractSystem.NeedEntry(w, "soil", s, day * 86_400L + 600, [issuer]);
                int serial = Book.NextSerial;
                var posted1 = ContractSystem.Post(w, cb, "soil_improvement", issuer, pr, s, pay, day, need, x => x.Param = HoursOf(kind));
                if (posted1 == null) break;
                Book.NextSerial++;
                Book.Works.Add(new SoilWork { Serial = serial, Field = f, Parcel = p, Settlement = s, Kind = kind, State = SoilState.Open, Contract = posted1.Id, Issuer = issuer, NeedMilli = HoursOf(kind) * 1000L, PostedDay = day });
                posted.Add(posted1); perPlace[s] = perPlace.GetValueOrDefault(s) + 1;
            }
        }
        return posted;
    }

    // ------------------------------------------------------------------ the day: crews take jobs, improvements age

    /// <summary>
    /// The households' own hands (P8-BL-1, D13): after each day's farm work the adults of a household that has taken its own field in hand spend the hours the farm left over on it, jobs in serial order. The last hour finishes it. A household
    /// with no members left drops the job.
    /// </summary>
    private void OwnHours(int day)
    {
        var idle = Farming.IdleHours; if (idle.Length == 0 || Book.Works.Count == 0) return;
        var hht = Land.Pop.Households;
        for (int i = 0; i < Book.Works.Count; i++)
        {
            var sw = Book.Works[i];
            if (sw.Contract != 0 || sw.State is not (SoilState.Open or SoilState.Working) || sw.Issuer.Kind != IdKind.Household) continue;
            int hh = hht.IndexOf(sw.Issuer);
            if (hh < 0 || hh >= hht.Count || hht.MemberCount[hh] == 0) { sw.State = SoilState.Failed; continue; }
            if (hh >= idle.Length || idle[hh] <= 0) continue;
            long needH = (sw.NeedMilli - sw.DoneMilli + 999) / 1000, take = Math.Min(idle[hh], needH);
            if (take <= 0) continue;
            idle[hh] -= (int)take; sw.DoneMilli += take * 1000; sw.State = SoilState.Working; OwnHoursDone += take;
            if (sw.DoneMilli >= sw.NeedMilli) { Finish(sw, day); OwnJobsDone++; }
        }
    }

    /// <summary>D13: a household works its own field before it posts it (the default). A test that exercises the posted contract itself (the hero takes it, the farmhands finish it) turns it off.</summary>
    public bool OwnHandsFirst { get; set; } = true;

    /// <summary>Counters of the household hands' soil work (diagnostics, never saved): hours worked and jobs finished.</summary>
    public long OwnHoursDone, OwnJobsDone, PoolJobsPosted;

    /// <summary>What a household's own hands have not done in <see cref="LandRulesDef.SoilOwnDays"/> days is posted for the day-labour pool: the rest of the hours at the posted wage, the pay in escrow (a household that cannot pay keeps working it itself).</summary>
    private void Escalate(ContractWorld w, ContractBook cb, int day)
    {
        if (w.Content.Kind("soil_improvement") == null) return;
        HashSet<EntityId>? crews = null;
        var hht = Land.Pop.Households;
        foreach (var sw in Book.Works.Where(x => x.Contract == 0 && x.State is SoilState.Open or SoilState.Working && x.Issuer.Kind == IdKind.Household && day >= x.PostedDay + Rules.SoilOwnDays).ToList())
        {
            crews ??= new HashSet<EntityId>(w.Companies().Select(x => x.Id));
            if (!crews.Contains(CrewId(sw.Settlement))) continue;
            if (w.PurseOfOwner(sw.Issuer) is not { } purse || purse.IsNone) continue;
            long left = sw.NeedMilli - sw.DoneMilli; if (left <= 0) continue;
            long pay = Math.Max(1, PayOf(sw.Kind) * left / Math.Max(1, sw.NeedMilli));
            if (w.Ledger.Balance(purse).Mites < pay) continue;
            var need = ContractSystem.NeedEntry(w, "soil", sw.Settlement, day * 86_400L + 600, [sw.Issuer]);
            var c = ContractSystem.Post(w, cb, "soil_improvement", sw.Issuer, purse, sw.Settlement, pay, day, need, x => x.Param = (int)((left + 999) / 1000));
            if (c == null) continue;
            sw.Contract = c.Id; sw.State = SoilState.Open; PoolJobsPosted++;
        }
    }

    public void Daily(ContractWorld w, ContractBook cb, int day)
    {
        LastDay = day;
        Escalate(w, cb, day);
        foreach (var sw in Book.Works.Where(x => x.State is SoilState.Open or SoilState.Working && x.Contract > 0).ToList())
        {
            var c = cb.Get(sw.Contract);
            if (c == null || c.State is WorkState.Expired or WorkState.Cancelled or WorkState.Failed) { sw.State = SoilState.Failed; continue; }
            if (c.State == WorkState.Taken) { sw.State = SoilState.Working; continue; }
            // a job nobody took in a week is taken by the farmhands of the place
            if (c.State != WorkState.Open || day < sw.PostedDay + Rules.SoilCrewWaitDays) continue;
            var crew = CrewId(sw.Settlement);
            if (w.Companies().Where(x => x.Id == crew).Select(x => (CompanyInfo?)x).FirstOrDefault() is not { } co) continue;
            if (ContractSystem.Take(w, cb, c.Id, crew, new PurseId(co.Purse), day, byCompany: true) == TakeResult.Taken) sw.State = SoilState.Working;
        }
        Age(day);
    }

    /// <summary>The improvements age: dung leaves after its seasons, marl fades a point a year after its twenty.</summary>
    public void Age(int day)
    {
        var F = this.F;
        for (int i = 0; i < Book.Effects.Count; i++)
        {
            var e = Book.Effects[i];
            if (e.Field >= F.Count) { Book.Effects.RemoveAt(i--); continue; }
            if (e.Kind == SoilKind.Manure)
            {
                if (day < e.EndDay) continue;
                F.Fertility[e.Field] = (byte)Math.Max(0, F.Fertility[e.Field] - e.Amount); Book.Effects.RemoveAt(i--);
                continue;
            }
            while (e.Amount > 0 && day >= e.LastFade + TimeConst.DaysPerYear)
            {
                e.LastFade += TimeConst.DaysPerYear; e.Amount--;
                F.Fertility[e.Field] = (byte)Math.Max(0, F.Fertility[e.Field] - 1);
            }
            if (e.Amount <= 0) Book.Effects.RemoveAt(i--);
        }
    }

    // ------------------------------------------------------------------ finishing

    /// <summary>The job is done: the field gains its points (what the cap of 100 leaves of them) and the improvement stands from today.</summary>
    public void Finish(SoilWork w, int day)
    {
        if (w.State == SoilState.Done) return;
        var F = this.F; if (w.Field >= F.Count) { w.State = SoilState.Failed; return; }
        int want = AmountOf(w.Kind), have = F.Fertility[w.Field], add = Math.Max(0, Math.Min(want, 100 - have));
        F.Fertility[w.Field] = (byte)(have + add);
        w.State = SoilState.Done; w.DoneDay = day; w.DoneMilli = Math.Max(w.DoneMilli, w.NeedMilli);
        if (add > 0)
            Book.Effects.Add(new SoilEffect { Field = w.Field, Kind = w.Kind, Amount = add, StartDay = day,
                EndDay = w.Kind == SoilKind.Manure ? day + Rules.ManureSeasons * TimeConst.DaysPerSeason : 0,
                LastFade = w.Kind == SoilKind.Marl ? day + Rules.MarlLastYears * TimeConst.DaysPerYear : 0 });
        var st = Region.Settlements[w.Settlement];
        var e = new ChronicleEntry(St.Ids.Allocate(IdKind.Event), new GameTime(day * 86_400L + 12 * 3600), "soil_improved", w.Issuer == EntityId.None ? [st.Id] : [w.Issuer, st.Id],
            [new Cause(CauseKind.Trade, PostedEntry(w), NoteKey: "ch_note_soil_poor")], new Dictionary<string, string> { ["kind"] = w.Kind.ToString(), ["added"] = add.ToString(CultureInfo.InvariantCulture) },
            TextKey: "ch_soil_improved", Params: [ChronParam.Place("place", st.Id), ChronParam.TextKey("what", w.Kind == SoilKind.Marl ? "ct_soil_marl" : "ct_soil_manure"), ChronParam.Count("points", add)]);
        St.Chronicle.Add(e);
    }

    private EntityId? PostedEntry(SoilWork w) => St.Contracts?.Get(w.Contract) is { PostedEntry: > 0 } c ? new EntityId(c.PostedEntry) : w.NeedEntry != 0 ? new EntityId(w.NeedEntry) : null;

    /// <summary>A company's job ended in success (the contract is about to be paid): the field is improved. Told by the contracts' company hook.</summary>
    public void CompanyDone(WorkContract c)
    {
        if (Book.ByContract(c.Id) is { } w) Finish(w, LastDay);
    }

    // ------------------------------------------------------------------ the hero

    public int HeroHousehold()
    {
        var pt = St.Population.People; int row = Ravensguard.Sim.Succession.HeroPerson.RowOf(pt, St.Hero, St.Party?.Hero ?? EntityId.None);
        return row < 0 ? -1 : pt.Household[row];
    }

    /// <summary>Q > Marl this field / Manure this field: the hero improves a field of his own household's with his own hands (no contract, nobody pays him).</summary>
    public HoldingsResult StartOwn(EntityId hero, int field, SoilKind kind, int day)
    {
        var F = this.F;
        if (field < 0 || field >= F.Count) return HoldingsResult.No("hw_soil_no_field");
        int p = F.Parcel[field]; var pop = Land.Pop; int hh = HeroHousehold();
        if (hh < 0 || p < 0 || p >= pop.ParcelHolder.Length || pop.ParcelHolder[p] != hh) return HoldingsResult.No("hw_soil_not_yours");
        if (Book.Live(field)) return HoldingsResult.No("hw_soil_busy");
        int s = Region.Parcels[p].Settlement;
        if (kind == SoilKind.Marl ? HasMarlPit?.Invoke(s) != true : DungLeftKg(s, day) < Rules.ManureKg) return HoldingsResult.No(kind == SoilKind.Marl ? "hw_soil_no_marl" : "hw_soil_no_dung");
        var w = new SoilWork { Serial = Book.NextSerial++, Field = field, Parcel = p, Settlement = s, Kind = kind, State = SoilState.Working, Issuer = hero, NeedMilli = HoursOf(kind) * 1000L, PostedDay = day };
        Book.Works.Add(w);
        return HoldingsResult.Yes("hw_soil_started", w.Serial, HoursOf(kind));
    }

    /// <summary>Hours by hand on a job (milli-hours, already limited by the heavy-work day): the hero's own, or a contract he took. The last hour finishes it (and pays the contract's escrow).</summary>
    public HoldingsResult HeroWork(EntityId hero, int serial, long milli, int day, out long workedMilli)
    {
        workedMilli = 0;
        var w = Book.Get(serial); if (w == null || w.State is SoilState.Done or SoilState.Failed) return HoldingsResult.No("hw_soil_no_job");
        WorkContract? c = null; var cb = St.Contracts;
        if (w.Contract > 0)
        {
            c = cb?.Get(w.Contract);
            if (c == null || c.State != WorkState.Taken || c.Taker != hero) return HoldingsResult.No("hw_soil_not_taken");
        }
        else if (w.Issuer != hero) return HoldingsResult.No("hw_soil_not_yours");
        long add = Math.Min(milli, w.NeedMilli - w.DoneMilli); if (add <= 0) return HoldingsResult.No("hw_soil_no_job");
        w.DoneMilli += add; workedMilli = add; w.State = SoilState.Working;
        if (w.DoneMilli < w.NeedMilli) return HoldingsResult.Yes("hw_soil_worked", (w.NeedMilli - w.DoneMilli) / 1000);
        Finish(w, day);
        if (c != null) ContractSystem.Complete(ContractWiring.From(St, St.Content), cb!, c, day);
        return HoldingsResult.Yes("hw_soil_done", AmountOf(w.Kind));
    }

    // ------------------------------------------------------------------ the hour (the contracts' hook)

    /// <summary>The hour hook (called by the contracts' hour): the day's work at hour 9, the posting on a season's first day.</summary>
    public void OnHour(ContractWorld w, ContractBook cb, int day, int hod)
    {
        if (hod != 9) return;
        Daily(w, cb, day);
        if (day % TimeConst.DaysPerSeason == 0) Season(w, cb, day);
    }
}
