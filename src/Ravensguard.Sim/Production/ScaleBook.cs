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

    public bool IsEmpty => Foremen.Count == 0 && Licences.Count == 0 && Fines.Count == 0 && Closings.Count == 0;

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
        return h;
    }
}
