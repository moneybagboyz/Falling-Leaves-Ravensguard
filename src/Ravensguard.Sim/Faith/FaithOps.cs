using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Succession;

namespace Ravensguard.Sim.Faith;

/// <summary>What the faith's politics share (P6-E2): the world state's church pieces bound for a pass, people and purses by id, and the chronicle writer.</summary>
public static class FaithOps
{
    /// <summary>The church, its politics book and the settings, bound for a pass; null when the world has no church.</summary>
    public sealed record Pass(WorldState St, ContentDatabase Db, ChurchBook Book, ChurchPolitics Pol, FaithSettingsDef S)
    {
        public ReligionBook Religions => Book.Religions;
        public PersonTable People => St.Population.People;
        public WorldGen.Region.Region Region => St.Population.Region;
    }

    public static Pass? Of(WorldState st, ContentDatabase? db = null)
    {
        db ??= st.Content; if (db == null) return null;
        var book = FaithWiring.Bind(st, db); if (book == null || book.Religions.Count == 0) return null;
        var pol = st.FaithPol ??= new ChurchPolitics();
        return new Pass(st, db, book, pol, book.Settings);
    }

    /// <summary>A person's row: by id, or the hero's own row.</summary>
    public static int RowOf(WorldState st, EntityId id)
    {
        var pt = st.Population.People;
        if (st.Party != null && id == st.Party.Hero) { int h = HeroPerson.RowOf(pt, st.Hero, id); if (h >= 0) return h; }
        return pt.IndexOf(id);
    }

    /// <summary>A person's religion index (their culture's), -1 for none.</summary>
    public static int ReligionOf(Pass p, EntityId id)
    {
        int row = RowOf(p.St, id); return row < 0 ? -1 : p.Religions.OfCulture(p.People.Culture[row]);
    }

    /// <summary>The realm of a person's home settlement (-1 when none).</summary>
    public static int RealmOfPerson(Pass p, EntityId id)
    {
        int row = RowOf(p.St, id); if (row < 0) return -1;
        return ChurchSeeding.RealmOf(p.Region, p.People.HomeSettlement[row]);
    }

    /// <summary>The purse a person's coin is in: the hero's own, else his household's.</summary>
    public static PurseId PurseOf(WorldState st, EntityId id)
    {
        var e = st.Economy; if (e == null) return PurseId.None;
        if (st.Party != null && id == st.Party.Hero) return e.Ledger.TryFind(PurseKind.Person, EconomyState.HeroOwner, out var hp) ? hp : PurseId.None;
        int row = st.Population.People.IndexOf(id); int hh = row >= 0 ? st.Population.People.Household[row] : -1;
        return hh >= 0 && hh < e.HouseholdCount ? e.HouseholdPurse[hh] : PurseId.None;
    }

    public static EntityId PlaceOf(WorldState st, int settlement) => settlement >= 0 && settlement < st.Population.Region.Settlements.Count ? st.Population.Region.Settlements[settlement].Id : EntityId.None;

    /// <summary>Writes a chronicle entry of the faith and returns its id (a text key with typed params, one cause with a note key, never a sentence).</summary>
    public static EntityId Chron(WorldState st, long sec, string type, string textKey, IReadOnlyList<ChronParam> ps, IReadOnlyList<EntityId> subjects, EntityId? cause, string noteKey, IReadOnlyList<ChronParam>? noteParams = null, params (string, string)[] data)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal); foreach (var (k, v) in data) d[k] = v;
        var e = new ChronicleEntry(st.Ids.Allocate(IdKind.Event), new GameTime(sec), type, subjects, [new Cause(CauseKind.Faith, cause is { } c && c != EntityId.None ? c : null, NoteKey: noteKey, NoteParams: noteParams)], d, TextKey: textKey, Params: ps);
        st.Chronicle.Add(e);
        return e.Id;
    }

    public static long SecOf(int day) => day * 86_400L + 3600;
}
