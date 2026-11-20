using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Society;

namespace Ravensguard.Sim.Faith;

/// <summary>
/// Piety, 0 to 100 per person (faith-and-church 3, 4.3): the default is derived from the id and the trade (40 to 70, a priest or monk 20 more), and only a person whose piety has moved from it is saved.
/// Deeds move it: alms, a service attended, a pilgrimage, and the sins that cost it. No random draw beyond the id's own default.
/// </summary>
public static class Piety
{
    /// <summary>The piety a person has before anything has moved it.</summary>
    public static int Default(ChurchBook b, PersonTable pt, int row)
    {
        var s = b.Settings;
        ulong id = pt.IdOf(row).Value;
        int p = s.PietyBase + (int)(DetRng.Next(b.Seed, 0, id, RngStreams.FaithFoundersAndHolyDays, 77) % (ulong)Math.Max(1, s.PietySpread));
        if (pt.Occupation[row] is Occupation.Priest or Occupation.Monk) p += s.ClergyPietyBonus;
        return Math.Clamp(p, 0, 100);
    }

    /// <summary>A person's piety: the saved value where it has moved, else the derived default (the middle of the default range for a person with no row).</summary>
    public static int Of(ChurchBook b, PersonTable pt, EntityId person)
    {
        if (b.Piety.TryGetValue(person.Value, out byte v)) return v;
        int row = pt.IndexOf(person);
        return row >= 0 ? Default(b, pt, row) : Math.Clamp(b.Settings.PietyBase + b.Settings.PietySpread / 2, 0, 100);
    }

    /// <summary>Moves a person's piety by <paramref name="delta"/> (clamped to 0..100); a value back at the default is not kept. Returns the new piety.</summary>
    public static int Adjust(ChurchBook b, PersonTable pt, EntityId person, int delta)
    {
        int now = Math.Clamp(Of(b, pt, person) + delta, 0, 100);
        int row = pt.IndexOf(person);
        if (row >= 0 && Default(b, pt, row) == now) b.Piety.Remove(person.Value); else b.Piety[person.Value] = (byte)now;
        return now;
    }

    /// <summary>The word a piety wears: lax (under 25), observant (under 50), pious (under 70), devout.</summary>
    public static string BandKey(int piety) => piety switch { < 25 => "fa_band_lax", < 50 => "fa_band_observant", < 70 => "fa_band_pious", _ => "fa_band_devout" };
}

/// <summary>
/// What the church does with a person's standing (faith-and-church 4.3, 4.6; reputation 4.2): a priest marries, buries and witnesses a will only below the floor of -500 with the religion and for a person who is
/// not excommunicated; benefit of clergy needs a standing of 0. Every reader asks <see cref="Standing.Of"/> through here, so nobody keeps a private opinion of the church.
/// </summary>
public static class ChurchServices
{
    /// <summary>The services a priest may refuse.</summary>
    public enum Service : byte { Wedding, Burial, WillWitness, Absolution, Clergy }

    /// <summary>A person's standing with their own church (0 when the faith or the book is unknown).</summary>
    public static int StandingWith(StandingBook? book, EntityId person, int religion) => religion < 0 ? 0 : Standing.Of(book, person, GroupRef.Religion(religion));

    /// <summary>Whether the church grants the service to the person: not excommunicated, and a standing no lower than the floor (benefit of clergy: no lower than 0).</summary>
    public static bool Allows(StandingBook? book, EntityId person, int religion, Service service)
    {
        if (religion < 0) return true;
        if (book != null && book.Marks.IsExcommunicated(person, religion)) return false;
        int st = StandingWith(book, person, religion);
        return service == Service.Clergy ? st >= 0 : !Standing.ChurchRefuses(st);
    }

    /// <summary>The person's religion (their culture's), -1 for none.</summary>
    public static int ReligionOf(ReligionBook religions, PersonTable pt, EntityId person)
    {
        int row = pt.IndexOf(person);
        return row >= 0 ? religions.OfCulture(pt.Culture[row]) : -1;
    }

    /// <summary>The piety a sin costs (faith 4.3): murder 20, theft 5 (a burglary, a pickpocketing too), sacrilege 30, grave robbing a fifth of the faith's barrow taboo, heresy 30; any other crime costs nothing here.</summary>
    public static int SinPiety(Religion? religion, string crimeKind) => crimeKind switch
    {
        "murder" => 20, "theft" or "burglary" or "pickpocketing" => 5, "sacrilege" or "heresy" => 30,
        "grave_robbing" => (religion?.Doctrine.BarrowTaboo ?? 50) / 5, _ => 0,
    };

    // ---------------------------------------------------------------- doctrines in play (FC-12, FC-13)

    /// <summary>Whether a person of a faith may offer a loan at interest: any loan at no interest, an interest loan unless the faith forbids usury.</summary>
    public static bool CanOfferLoan(ReligionBook religions, int lenderReligion, bool atInterest) => !atInterest || MayLendAtInterest(religions, lenderReligion);

    /// <summary>The lenders of a realm that may lend at interest (FC-13): a believer under <c>forbid</c> drops out, so loans there come from merchants of another faith or none (religion index -1 is no faith).</summary>
    public static List<T> InterestLenders<T>(ReligionBook religions, IEnumerable<(T Who, int Religion)> candidates) => [.. candidates.Where(c => MayLendAtInterest(religions, c.Religion)).Select(c => c.Who)];

    /// <summary>Whether a believer may lend at interest (usury, FC-13): not under <c>forbid</c>. Loans in such a realm then come from merchants of another faith or none (rule-and-income RI-14).</summary>
    public static bool MayLendAtInterest(ReligionBook religions, int lenderReligion)
        => religions.Get(lenderReligion) is not { } r || !string.Equals(r.Doctrine.Usury, "forbid", StringComparison.Ordinal);

    /// <summary>
    /// Owning a slave against the doctrine (FC-12, faith 4.3, slavery SL-6): true when the owner's religion forbids it outright, or forbids it for coreligionists and the slave is one. The owner then loses
    /// <see cref="Content.FaithSettingsDef.SlaveSeasonPiety"/> piety a season (<see cref="OwnsSlaveAgainstDoctrine"/>) and the deed's standing with the religion.
    /// </summary>
    public static bool SlaveHoldingForbidden(ReligionBook religions, int ownerReligion, int slaveReligion)
        => religions.Get(ownerReligion) is { } r && r.Doctrine.SlaveryStance switch { "forbid" => true, "coreligionists_forbidden" => slaveReligion == ownerReligion, _ => false };

    /// <summary>
    /// The season's piety cost of owning <paramref name="slaves"/> slaves against the doctrine, applied to the owner (a person's piety is a column of the church book). Returns the piety lost (0 when the
    /// doctrine allows it or none is owned against it). The standing side is the <c>slave_held</c> deed the caller writes with the chronicle entry (the slavery step, D1).
    /// </summary>
    public static int OwnsSlaveAgainstDoctrine(ChurchBook book, PersonTable pt, EntityId owner, int ownerReligion, int slaves, Func<int, int> slaveReligionOf)
    {
        if (slaves <= 0) return 0;
        int against = 0;
        for (int i = 0; i < slaves; i++) if (SlaveHoldingForbidden(book.Religions, ownerReligion, slaveReligionOf(i))) against++;
        if (against == 0) return 0;
        int before = Piety.Of(book, pt, owner);
        int after = Piety.Adjust(book, pt, owner, -book.Settings.SlaveSeasonPiety);
        return before - after;
    }

    /// <summary>
    /// The season's sin of keeping slaves against the doctrine (FC-12, faith 4.3; the slavery step calls it each season for an owner): the piety is lost (<see cref="OwnsSlaveAgainstDoctrine"/>), the chronicle
    /// says so with its cause, and the <c>slave_held</c> deed moves the owner's standing with the faith and the place. Returns the piety lost.
    /// </summary>
    public static int SlaveHeldSeason(Save.WorldState st, Content.ContentDatabase content, EntityId owner, int ownerReligion, int slaves, Func<int, int> slaveReligionOf, int settlement, GameTime when)
    {
        var book = FaithWiring.Bind(st, content); if (book == null) return 0;
        int lost = OwnsSlaveAgainstDoctrine(book, st.Population.People, owner, ownerReligion, slaves, slaveReligionOf);
        if (lost <= 0) return 0;
        var region = st.Population.Region; var ps = new List<ChronParam> { ChronParam.Person("doer", owner) };
        if (settlement >= 0 && settlement < region.Settlements.Count) ps.Add(ChronParam.Place("place", region.Settlements[settlement].Id));
        var e = new ChronicleEntry(st.Ids.Allocate(IdKind.Event), when, "slave_held", [owner], [new Cause(CauseKind.Faith, null, NoteKey: "ch_note_slave_against_doctrine")],
            new Dictionary<string, string> { ["slaves"] = slaves.ToString(System.Globalization.CultureInfo.InvariantCulture) }, TextKey: "ch_faith_slave_held", Params: ps);
        st.Chronicle.Add(e);
        FaithStanding.Record(st, content, owner, "slave_held", e.Id, settlement, 1, -1, false, (int)when.TotalDays);
        return lost;
    }
}
