namespace Ravensguard.Sim.Production;

/// <summary>A manufactory licence (economies-of-scale 3, 5.3): the lord's leave for a works larger than the guild's caps inside its liberty. The fee was paid once; the rent is paid each season.</summary>
public sealed class ManufactoryLicence
{
    public ulong Business;
    /// <summary>Index of the fief whose lord granted it (and whose treasury takes the fee and the rent).</summary>
    public int Fief;
    /// <summary>The guild's template id (the trade the licence is for).</summary>
    public string Guild = "";
    public int GrantedDay, FeeMites;
    /// <summary>The last season index whose rent was paid.</summary>
    public int PaidSeason;
}

/// <summary>A guild's fine on an oversized shop (5.3): pruned after two years.</summary>
public sealed class GuildFine
{
    public ulong Business;
    public int Season, Day, Over;
    public long Mites, Paid;
}

public enum SupplyState : byte { Open, Done, Broken }

/// <summary>
/// A standing supply contract (P7-ES2, economies-of-scale 3, 4.3): <see cref="QtyPerDelivery"/> of a good (grams, or pieces) every <see cref="EveryDays"/> days from seller to buyer at a price fixed at signing,
/// payment on delivery with no spread and no market toll. The carter is the seller by default and bears the carting. <see cref="NextDue"/> is the day the next delivery falls due.
/// </summary>
public sealed class SupplyContract
{
    public int Id;
    public ulong Seller, Buyer;
    public string Good = "";
    public long QtyPerDelivery;
    public int EveryDays, StartDay, TermSeasons;
    /// <summary>Micro-mites per gram (or per piece).</summary>
    public long PriceMicro;
    public bool CarterIsSeller = true;
    public int Delivered, Missed, SeasonOfMiss = -1, SeasonMisses, NextDue, EndDay;
    public SupplyState State;
    /// <summary>The last day a delivery was due that is still being waited for (a delivery is late from the day after).</summary>
    public int EndsOn => StartDay + TermSeasons * Ravensguard.Sim.Core.TimeConst.DaysPerSeason;
}

/// <summary>The days a business finished runs of one standard pattern (4.6): the practice rule reads the last few.</summary>
public sealed class PatternRun
{
    public ulong Business;
    public string Recipe = "";
    public List<int> Days { get; } = [];
}

/// <summary>A station of a business coupled to a machine's station piece at a cell (4.5): the machines' share of the labour comes from the power net there. The hook the business-to-premises link will replace.</summary>
public sealed class MachineCoupling
{
    public ulong Business;
    public int Station, X, Y, Z;
}

/// <summary>
/// The saved state of economies of scale, part 1 (P7-ES1; the optional "scale" section, absent while empty): appointed foremen, manufactory licences, guild fines, and the shops a lord's court has closed. Everything
/// else (roles, grade hours, effective crews, spans, crowding) is derived each hour and never saved. ES2 adds supply contracts and pattern runs to the same section.
/// </summary>
public sealed class ScaleBook
{
    /// <summary>Per business id, the person indices of the staff appointed foremen (ascending).</summary>
    public SortedDictionary<ulong, SortedSet<int>> Foremen { get; } = [];
    public List<ManufactoryLicence> Licences { get; } = [];
    public List<GuildFine> Fines { get; } = [];
    /// <summary>Closing orders carried out: the business id and the day.</summary>
    public List<(ulong Business, int Day)> Closings { get; } = [];

    // ---- P7-ES2
    public List<SupplyContract> Contracts { get; } = [];
    public int NextContractId { get; set; } = 1;
    public List<PatternRun> Runs { get; } = [];
    public List<MachineCoupling> Couplings { get; } = [];

    public bool IsEmpty => Foremen.Count == 0 && Licences.Count == 0 && Fines.Count == 0 && Closings.Count == 0 && Contracts.Count == 0 && Runs.Count == 0 && Couplings.Count == 0;

    public SupplyContract? Contract(int id) => Contracts.FirstOrDefault(c => c.Id == id);

    /// <summary>Records a finished run of a pattern recipe (4.6); only the last few days are kept.</summary>
    public void RecordRun(ulong business, string recipe, int day)
    {
        var r = Runs.FirstOrDefault(x => x.Business == business && x.Recipe == recipe);
        if (r == null)
        {
            r = new PatternRun { Business = business, Recipe = recipe };
            int at = Runs.FindIndex(x => x.Business > business || (x.Business == business && string.CompareOrdinal(x.Recipe, recipe) > 0));
            if (at < 0) Runs.Add(r); else Runs.Insert(at, r);
        }
        r.Days.Add(day); if (r.Days.Count > 8) r.Days.RemoveAt(0);
    }

    /// <summary>Whether the business has practice at a pattern (4.6): at least <paramref name="runs"/> finished runs in the last <paramref name="days"/> days.</summary>
    public bool Practised(ulong business, string recipe, int day, int runs, int days)
    {
        foreach (var r in Runs) if (r.Business == business && r.Recipe == recipe) { int n = 0; foreach (int d in r.Days) if (day - d <= days && d <= day) n++; return n >= runs; }
        return false;
    }

    public MachineCoupling? CouplingOf(ulong business, int station) => Couplings.FirstOrDefault(c => c.Business == business && c.Station == station);

    public bool IsForeman(ulong business, int person) => Foremen.TryGetValue(business, out var s) && s.Contains(person);
    public ManufactoryLicence? LicenceOf(ulong business) => Licences.FirstOrDefault(l => l.Business == business);
    public int FinesOf(ulong business) => Fines.Count(f => f.Business == business);

    /// <summary>A staff member who left (or died) stops being a foreman.</summary>
    public void Forget(ulong business, int person)
    {
        if (!Foremen.TryGetValue(business, out var s)) return;
        s.Remove(person); if (s.Count == 0) Foremen.Remove(business);
    }

    /// <summary>The section's hash (<c>ScaleHash</c>, overview 7): fixed order, integers only. Zero while empty.</summary>
    public ulong Hash()
    {
        if (IsEmpty) return 0;
        ulong h = 14695981039346656037UL;
        void M(long v) { unchecked { h = (h ^ (ulong)v) * 1099511628211UL; } }
        foreach (var (b, set) in Foremen) { M((long)b); M(set.Count); foreach (int p in set) M(p); }
        M(-1);
        foreach (var l in Licences) { M((long)l.Business); M(l.Fief); foreach (char c in l.Guild) M(c); M(l.GrantedDay); M(l.FeeMites); M(l.PaidSeason); }
        M(-2);
        foreach (var f in Fines) { M((long)f.Business); M(f.Season); M(f.Day); M(f.Over); M(f.Mites); M(f.Paid); }
        M(-3);
        foreach (var (b, d) in Closings) { M((long)b); M(d); }
        if (Contracts.Count > 0 || Runs.Count > 0 || Couplings.Count > 0)
        {
            M(-4); M(NextContractId);
            foreach (var c in Contracts)
            {
                M(c.Id); M((long)c.Seller); M((long)c.Buyer); foreach (char ch in c.Good) M(ch); M(c.QtyPerDelivery); M(c.EveryDays); M(c.StartDay); M(c.TermSeasons); M(c.PriceMicro); M(c.CarterIsSeller ? 1 : 0);
                M(c.Delivered); M(c.Missed); M(c.SeasonOfMiss); M(c.SeasonMisses); M(c.NextDue); M(c.EndDay); M((int)c.State);
            }
            M(-5);
            foreach (var r in Runs) { M((long)r.Business); foreach (char ch in r.Recipe) M(ch); foreach (int d in r.Days) M(d); M(-6); }
            M(-7);
            foreach (var c in Couplings) { M((long)c.Business); M(c.Station); M(c.X); M(c.Y); M(c.Z); }
        }
        return h;
    }
}
