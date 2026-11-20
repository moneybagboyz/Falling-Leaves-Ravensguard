using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Taxes;

namespace Ravensguard.Sim.Faith;

/// <summary>
/// The church on the abstract tick (faith-and-church 4.8): at H5 hour 0 the offices are reconciled with the living (a priest who died leaves the living vacant, a bishop's death opens the see, the chapter
/// elects 1 to 2 seasons later) and a holy day's services are counted; at hour 1 of a season's first day (after the treasuries rolled their season at hour 0) the tithe is split. A no-op in a world
/// that has no <see cref="ChurchBook"/> installed. Integer only; the one draw is the length of a vacancy (stream 0x651).
/// </summary>
[AbstractSystem]
public sealed class FaithSystem : AbstractSystem
{
    public FaithSystem() : base("faith", TickPhase.H5DailyGlobals | TickPhase.H6Seasonal, SystemOrder.S4Taxes + 10) { }

    public override void Step(TickContext ctx)
    {
        var b = ctx.World.Get<ChurchBook>(); if (b == null) return;
        if (ctx.Phase == TickPhase.H5DailyGlobals) { if (ctx.HourOfDay == 0) FaithEngine.Daily(ctx.World, b, (int)ctx.Day, ctx.Time, ctx.Outbox); }
        else if (ctx.Phase == TickPhase.H6Seasonal && ctx.IsSeasonStartDay && ctx.HourOfDay == 1) FaithEngine.Season(ctx.World, ctx.World.Get<EconomyWorld>(), b, (int)ctx.Day, ctx.Time, ctx.Outbox);
    }
}

/// <summary>What a service counted on a holy day in one parish.</summary>
public sealed record ServiceReport(int Settlement, string Feast, int Adults, int Attended);

/// <summary>The rules of the church's day and season, callable by a test with a hand-built world and by the system on the tick.</summary>
public static class FaithEngine
{
    private const int AdultAge = 16;

    /// <summary>The purse of a see's treasury (a treasury purse owned by a faction-kind id, the realm tag being bit 40, the see's bit 41).</summary>
    public static EntityId SeePurseOwner(int see) => EntityId.Make(IdKind.Faction, (2UL << 40) | (ulong)(uint)see);

    private static ulong KeyOf(string id) { ulong h = 0xCBF29CE484222325UL; foreach (char c in id) h = (h ^ c) * 0x100000001B3UL; return h; }

    public static bool IsLivingPriest(PersonTable pt, long person, out int row)
    {
        row = person < 0 ? -1 : pt.IndexOf(new EntityId((ulong)person));
        return row >= 0 && pt.IsAlive(row) && pt.Occupation[row] == Occupation.Priest;
    }

    /// <summary>The ruler's treasury for a see: the realm's treasury of its seat, else the fief's or the town's that the seat pays into.</summary>
    public static PurseId RulerPurse(EconomyWorld? ew, int seat)
    {
        if (ew == null || seat < 0) return PurseId.None;
        var t = ew.Taxes.Treasuries;
        int slot = seat < t.RealmSlotOfSettlement.Length && t.RealmSlotOfSettlement[seat] >= 0 ? t.RealmSlotOfSettlement[seat] : seat < t.PayeeSlot.Length ? t.PayeeSlot[seat] : -1;
        return slot >= 0 ? t[slot].Purse : PurseId.None;
    }

    // ================================================================= the day (H5, hour 0)

    /// <summary>The day's pass: parish holders, bishops and elections, a holy day's services. Runs once for a day number.</summary>
    public static List<ServiceReport> Daily(SimWorld w, ChurchBook b, int day, GameTime now, RegionOutbox? ob)
    {
        var reports = new List<ServiceReport>();
        if (b.LastDay == day) return reports;
        Reconcile(w, b, day, now, ob);
        Comfort(w, b, day);
        reports = Services(w, b, day);
        b.LastDay = day;
        return reports;
    }

    /// <summary>The shrine's comfort (4.4): for the days after a pilgrim's arrival his pain is eased a little each day (and nothing is cured).</summary>
    private static void Comfort(SimWorld w, ChurchBook b, int day)
    {
        if (b.HeroId < 0 || b.ComfortUntilDay < day) return;
        if (w.Get<Ravensguard.Sim.Combat.Wounds.WoundLedger>() is { } wl && wl.Books.TryGetValue((ulong)b.HeroId, out var wb)) wb.Relieve(b.Settings.ShrinePainRelief);
    }

    private static void Reconcile(SimWorld w, ChurchBook b, int day, GameTime now, RegionOutbox? ob)
    {
        var pt = w.People; var pop = w.Population; var region = pop.Region;
        foreach (var p in b.Parishes.Values)
        {
            if (IsLivingPriest(pt, p.Priest, out _)) continue;
            int pr = ChurchSeeding.PriestRow(pop, p.Settlement);
            if (pr >= 0) { p.Priest = (long)pt.IdOf(pr).Value; p.VacantSince = -1; }
            else { p.Priest = -1; if (p.VacantSince < 0) p.VacantSince = day; }
        }
        foreach (var see in b.Sees.Values)
        {
            if (see.Bishop >= 0 && !IsLivingPriest(pt, see.Bishop, out _))
            {
                long dead = see.Bishop; see.Bishop = -1; see.VacantSince = day;
                see.ElectionDay = day + ElectionLength(w, b, see, day);
                Windfall(w, b, see, dead, now, ob);
            }
            else if (see.Bishop < 0)
            {
                if (see.VacantSince < 0) see.VacantSince = day;
                if (see.ElectionDay < 0) see.ElectionDay = day + ElectionLength(w, b, see, day);
                else if (day >= see.ElectionDay) Elect(w, b, see, day, now, ob);
            }
        }
    }

    private static int ElectionLength(SimWorld w, ChurchBook b, SeeRow see, int day)
    {
        var s = b.Settings;
        return DetRng.Range(w.Seed, day, KeyOf(see.Religion) ^ (ulong)(uint)see.Index, RngStreams.FaithFoundersAndHolyDays, 900, s.ElectionMinDays, s.ElectionMaxDays + 1);
    }

    private static EntityId PlaceOf(SimWorld w, int settlement) => settlement >= 0 && settlement < w.Population.Region.Settlements.Count ? w.Population.Region.Settlements[settlement].Id : EntityId.None;

    /// <summary>A bishop has died: the see's treasury goes to the ruler until the election, and the chronicle says why (4.2).</summary>
    private static void Windfall(SimWorld w, ChurchBook b, SeeRow see, long dead, GameTime now, RegionOutbox? ob)
    {
        var place = PlaceOf(w, see.Seat);
        var deadId = new EntityId((ulong)dead);
        EntityId vacantEntry = EntityId.None;
        if (ob != null)
        {
            var ps = new List<ChronParam> { ChronParam.Person("bishop", deadId) };
            if (place != EntityId.None) ps.Add(ChronParam.Place("place", place));
            vacantEntry = ob.Chronicle(now, "see_vacant", "ch_faith_see_vacant", ps, [deadId], [new Cause(CauseKind.Faith, null, NoteKey: "ch_note_see_bishop_died")], ("see", see.Index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        var ew = w.Get<EconomyWorld>(); var led = w.Economy.Ledger;
        if (!led.TryFind(PurseKind.Treasury, SeePurseOwner(see.Index), out var seePurse)) return;
        long bal = led.Balance(seePurse).Mites; var ruler = RulerPurse(ew, see.Seat);
        if (bal <= 0 || ruler.IsNone || !led.Transfer(seePurse, ruler, new Coin(bal), TransferReason.Escheat)) return;
        see.TakenMites += bal;
        if (ob != null)
        {
            var ps = new List<ChronParam> { ChronParam.Mites("amount", bal) };
            if (place != EntityId.None) ps.Add(ChronParam.Place("place", place));
            ob.Chronicle(now, "see_revenue_seized", "ch_faith_see_seized", ps, [deadId], [new Cause(CauseKind.Faith, vacantEntry == EntityId.None ? (EntityId?)null : vacantEntry, NoteKey: "ch_note_see_vacant_receipts")]);
        }
    }

    /// <summary>The chapter chooses the most pious senior priest of the see's faith and realm (highest piety, then lowest id); with none to choose a fortnight is added.</summary>
    private static void Elect(SimWorld w, ChurchBook b, SeeRow see, int day, GameTime now, RegionOutbox? ob)
    {
        var pt = w.People; var region = w.Population.Region;
        long best = -1; int bestPiety = -1;
        foreach (var p in b.Parishes.Values)
        {
            int r = b.Religions.OfSettlement(region, p.Settlement);
            if (r < 0 || b.Religions.Get(r)!.Id != see.Religion || ChurchSeeding.RealmOf(region, p.Settlement) != see.Realm) continue;
            if (!IsLivingPriest(pt, p.Priest, out int row)) continue;
            int piety = Piety.Of(b, pt, pt.IdOf(row));
            if (piety > bestPiety || (piety == bestPiety && p.Priest < best)) { best = p.Priest; bestPiety = piety; }
        }
        if (best < 0) { see.ElectionDay = day + 15; return; }
        see.Bishop = best; see.VacantSince = -1; see.ElectionDay = -1;
        if (ob != null)
        {
            var who = new EntityId((ulong)best); var place = PlaceOf(w, see.Seat);
            var ps = new List<ChronParam> { ChronParam.Person("bishop", who) };
            if (place != EntityId.None) ps.Add(ChronParam.Place("place", place));
            ob.Chronicle(now, "bishop_elected", "ch_faith_bishop_elected", ps, [who], [new Cause(CauseKind.Faith, null, NoteKey: "ch_note_chapter_election")], ("see", see.Index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
    }

    /// <summary>
    /// A holy day's services (4.1): in each parish whose faith keeps the day, the adults who come are counted by their piety (a person comes with P = 600 + 5 x piety permille, so a devout parish fills the
    /// church and a lax one most of it), as an expectation with no dice. The hero's own attendance is a command (<see cref="FaithDesk"/>).
    /// </summary>
    public static List<ServiceReport> Services(SimWorld w, ChurchBook b, int day)
    {
        var list = new List<ServiceReport>();
        var region = w.Population.Region; int doy = day % TimeConst.DaysPerYear + 1;
        var holy = new HolyDay?[region.Settlements.Count]; bool any = false;
        foreach (var p in b.Parishes.Values)
        {
            var r = b.Religions.Get(b.Religions.OfSettlement(region, p.Settlement));
            if (r?.HolyDayOn(doy) is { } hd) { holy[p.Settlement] = hd; any = true; }
        }
        if (!any) return list;
        var pt = w.People; var adults = new int[holy.Length]; var pm = new long[holy.Length]; var s = b.Settings;
        for (int i = 0; i < pt.Count; i++)
        {
            if (!pt.IsAlive(i)) continue;
            int hs = pt.HomeSettlement[i]; if (hs < 0 || hs >= holy.Length || holy[hs] == null) continue;
            if (pt.AgeYears(i, day) < AdultAge) continue;
            int piety = b.Piety.TryGetValue(pt.IdOf(i).Value, out byte v) ? v : Piety.Default(b, pt, i);
            adults[hs]++; pm[hs] += Math.Clamp(s.AttendBasePm + s.AttendPerPietyPm * piety, 0, 1000);
        }
        foreach (var p in b.Parishes.Values)
        {
            if (holy[p.Settlement] is not { } hd) continue;
            p.ServiceDay = day; p.Adults = adults[p.Settlement]; p.Attended = (int)(pm[p.Settlement] / 1000);
            if (w.Get<ChurchPolitics>()?.Heresies.TryGetValue(p.Settlement, out var hr) == true && hr.Interdict) p.Attended = 0;      // an interdict: no services (P6-E2, 4.5)
            list.Add(new ServiceReport(p.Settlement, hd.Name, p.Adults, p.Attended));
        }
        return list;
    }

    // ================================================================= the season (H6, hour 1 of its first day)

    /// <summary>The tithe a parish received, split by the doctrine's parts (4.2): the see's and the fabric's shares by integer division, the parish keeps the rest, so nothing is lost to the rounding (3,000 mites in thirds: 1,000 each).</summary>
    public static (long Parish, long See, long Fabric) SplitTithe(long received, int parishParts, int seeParts, int fabricParts)
    {
        long total = Math.Max(1, (long)parishParts + seeParts + fabricParts);
        long see = received * seeParts / total, fabric = received * fabricParts / total;
        return (received - see - fabric, see, fabric);
    }

    /// <summary>
    /// The tithe split (4.2): what each parish chest received as tithe last season is divided by the doctrine's parts (a third each by default): the see's part goes to the bishop's treasury (to the ruler's while the see
    /// is vacant), the fabric-and-poor part stays in the chest earmarked, the parish keeps the rest. Every coin moves by a ledger transfer; the bishop draws his living from what the see took. Returns the coin
    /// that went to the sees (or the rulers).
    /// </summary>
    public static long Season(SimWorld w, EconomyWorld? ew, ChurchBook b, int day, GameTime now, RegionOutbox? ob)
    {
        if (ew == null) return 0;
        var t = ew.Taxes.Treasuries; var led = w.Economy.Ledger; var region = w.Population.Region; var pt = w.People;
        var taken = new Dictionary<int, long>(); long total = 0;
        foreach (var p in b.Parishes.Values)
        {
            int slot = p.Settlement < t.ParishSlot.Length ? t.ParishSlot[p.Settlement] : -1; if (slot < 0) continue;
            long received = t.IncomeOf(slot, TransferReason.Tithe, lastSeason: true); if (received <= 0) continue;
            var r = b.Religions.Get(b.Religions.OfSettlement(region, p.Settlement)); var d = r?.Doctrine;
            int ps = d?.TitheParishParts ?? 1, ss = d?.TitheSeeParts ?? 1, fs = d?.TitheFabricParts ?? 1;
            var (keep, seeShare, fabric) = SplitTithe(received, ps, ss, fs);
            var see = r == null ? null : b.SeeOf(r.Id, ChurchSeeding.RealmOf(region, p.Settlement));
            var chest = led.TryFind(PurseKind.Chest, region.Settlements[p.Settlement].Id, out var c) ? c : PurseId.None;
            if (chest.IsNone) continue;
            long give = Math.Min(seeShare, led.Balance(chest).Mites);
            if (see != null && give > 0)
            {
                bool vacant = see.Bishop < 0;
                PurseId to = vacant ? RulerPurse(ew, see.Seat) : led.Open(PurseKind.Treasury, SeePurseOwner(see.Index));
                if (!to.IsNone && led.Transfer(chest, to, new Coin(give), vacant ? TransferReason.Escheat : TransferReason.Tithe))
                {
                    total += give;
                    if (vacant) see.TakenMites += give; else taken[see.Index] = taken.GetValueOrDefault(see.Index) + give;
                }
            }
            p.FabricMites += fabric / 2; p.PoorMites += fabric - fabric / 2;
        }
        // the bishop's living: a share of what his see received this season, from the see's treasury to his household
        foreach (var (idx, got) in taken)
        {
            var see = b.Sees[idx]; if (see.Bishop < 0 || !IsLivingPriest(pt, see.Bishop, out int brow)) continue;
            int hh = pt.Household[brow]; if (hh < 0 || hh >= w.Economy.HouseholdCount) continue;
            long pay = got * b.Settings.BishopStipendPct / 100;
            if (pay > 0 && led.TryFind(PurseKind.Treasury, SeePurseOwner(idx), out var sp) && led.Balance(sp).Mites >= pay) led.Transfer(sp, w.Economy.HouseholdPurse[hh], new Coin(pay), TransferReason.Stipend);
        }
        return total;
    }
}
