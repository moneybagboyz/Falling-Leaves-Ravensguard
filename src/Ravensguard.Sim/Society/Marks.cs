using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Society;

/// <summary>The brands a sentence burns on a person (reputation 3): what for. A brand never fades.</summary>
[Flags] public enum Brand : byte { None = 0, Thief = 1, Murderer = 2, Runaway = 4 }

/// <summary>One person's marks (reputation 3, 4.3): brands, the realms that have outlawed them, the religions that have cast them out, bondage, and a pardon once given.</summary>
public sealed class MarkSet
{
    public Brand Brands;
    public readonly SortedSet<int> OutlawRealms = [];
    public readonly SortedSet<int> Excommunicated = [];
    public bool Enslaved, Pardoned;
    public bool IsEmpty => Brands == Brand.None && OutlawRealms.Count == 0 && Excommunicated.Count == 0 && !Enslaved && !Pardoned;
}

/// <summary>
/// The marks of the world's people by person id (reputation 3, 4.3). A brand is applied by a sentence and never fades; outlawry is per realm and sets the standing there to -1000 for as long as it
/// lasts (<see cref="Standing.Of"/>); a pardon clears <c>Outlaw</c> and adds <c>Pardoned</c>. A stranger meeting a branded person starts at a lower trust (<see cref="FirstMeetingTrustPm"/>).
/// Marks never pass to an heir (4.4). No random draw.
/// </summary>
public sealed class MarkBook
{
    /// <summary>A thief's brand starts a stranger's trust at this many permille, a murderer's at <see cref="MurdererBrandTrustPm"/>, a runaway's at <see cref="RunawayBrandTrustPm"/> (4.3).</summary>
    public const int ThiefBrandTrustPm = -200, MurdererBrandTrustPm = -400, RunawayBrandTrustPm = -100;
    /// <summary>The brand is seen at a first meeting within this many metres in light (3).</summary>
    public const int SeenWithinM = 3;

    private readonly SortedDictionary<ulong, MarkSet> _by = [];
    public IReadOnlyDictionary<ulong, MarkSet> All => _by;
    public int Count => _by.Count;
    public bool IsEmpty => _by.Count == 0;

    public MarkSet? Of(EntityId person) => _by.GetValueOrDefault(person.Value);
    private MarkSet Get(EntityId person) => _by.TryGetValue(person.Value, out var m) ? m : _by[person.Value] = new MarkSet();
    private void Tidy(EntityId person) { if (_by.TryGetValue(person.Value, out var m) && m.IsEmpty) _by.Remove(person.Value); }

    public void AddBrand(EntityId person, Brand b) => Get(person).Brands |= b;
    public bool Has(EntityId person, Brand b) => Of(person) is { } m && (m.Brands & b) != 0;
    public bool IsOutlaw(EntityId person, int realm) => Of(person) is { } m && m.OutlawRealms.Contains(realm);
    public bool IsOutlawAnywhere(EntityId person) => Of(person) is { } m && m.OutlawRealms.Count > 0;
    public bool IsExcommunicated(EntityId person, int religion) => Of(person) is { } m && m.Excommunicated.Contains(religion);
    public bool IsEnslaved(EntityId person) => Of(person)?.Enslaved == true;
    public bool IsPardoned(EntityId person) => Of(person)?.Pardoned == true;

    public void Outlaw(EntityId person, int realm) => Get(person).OutlawRealms.Add(realm);
    public void Excommunicate(EntityId person, int religion) => Get(person).Excommunicated.Add(religion);
    public void Absolve(EntityId person, int religion) { Of(person)?.Excommunicated.Remove(religion); Tidy(person); }
    public void SetEnslaved(EntityId person, bool on) { if (on) Get(person).Enslaved = true; else if (Of(person) is { } m) { m.Enslaved = false; Tidy(person); } }

    /// <summary>A pardon (bought, granted or a ruler's act) clears the outlawry in that realm (every realm when <paramref name="realm"/> is negative) and adds <c>Pardoned</c>. False when there was nothing to pardon.</summary>
    public bool Pardon(EntityId person, int realm = -1)
    {
        var m = Of(person); if (m == null || m.OutlawRealms.Count == 0) return false;
        if (realm < 0) m.OutlawRealms.Clear(); else if (!m.OutlawRealms.Remove(realm)) return false;
        m.Pardoned = true; return true;
    }

    /// <summary>Anyone in the realm may kill an outlaw without it being a crime (4.3): the crime step reads this before it makes a murder.</summary>
    public bool KillingIsNoCrime(EntityId victim, int realm) => IsOutlaw(victim, realm);

    /// <summary>The trust a stranger starts at on meeting a person with a visible brand (the worst brand counts), 0 when none (4.3). The caller adds it to the base start.</summary>
    public int FirstMeetingTrustPm(EntityId person)
    {
        if (Of(person) is not { } m) return 0;
        int t = 0;
        if ((m.Brands & Brand.Murderer) != 0) t = Math.Min(t, MurdererBrandTrustPm);
        if ((m.Brands & Brand.Thief) != 0) t = Math.Min(t, ThiefBrandTrustPm);
        if ((m.Brands & Brand.Runaway) != 0) t = Math.Min(t, RunawayBrandTrustPm);
        return t;
    }

    /// <summary>A brand is seen at a first meeting within 3 m in light (3).</summary>
    public static bool BrandSeen(int distanceM, bool inLight) => inLight && distanceM <= SeenWithinM;

    /// <summary>Marks never pass to an heir (4.4): the dead's marks go with them, the heir (who takes up the same id) starts clean.</summary>
    public void Clear(EntityId person) => _by.Remove(person.Value);

    public void Restore(ulong person, MarkSet m) => _by[person] = m;
}
