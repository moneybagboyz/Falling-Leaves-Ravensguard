using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Faith;

/// <summary>
/// Binds the offices of the church to people (faith-and-church 3, the Phase 6 seeding pass): every settlement with a priest's household is a parish whose holder is the priest in it, and every
/// religion and realm that has parishes in the region has a see at its largest parish, its bishop the priest there. A pure function of the world seed, the content and the people's ids (never the day it
/// runs), so a new game and the first load of a save from before Phase 6 derive the same church. The religions themselves are not here: they are derived (<see cref="ReligionBook.Derive"/>).
/// </summary>
public static class ChurchSeeding
{
    /// <summary>The priest of a settlement's parish: the living member of the priest's household who is a priest (lowest row), or -1.</summary>
    public static int PriestRow(Population pop, int settlement)
    {
        if (settlement < 0 || settlement >= pop.PriestHousehold.Length) return -1;
        int hh = pop.PriestHousehold[settlement]; if (hh < 0 || hh >= pop.Households.Count) return -1;
        var pt = pop.People; int best = -1;
        foreach (int p in pop.Households.Members(hh)) if (pt.IsAlive(p) && pt.Occupation[p] == Occupation.Priest && (best < 0 || p < best)) best = p;
        return best;
    }

    /// <summary>The realm a settlement lies in for its see: its fief's, else its own history realm, else -1.</summary>
    public static int RealmOf(WorldGen.Region.Region region, int settlement)
    {
        var s = region.Settlements[settlement];
        if (s.FiefIndex >= 0 && s.FiefIndex < region.Fiefs.Count && region.Fiefs[s.FiefIndex].Realm >= 0) return region.Fiefs[s.FiefIndex].Realm;
        return s.Realm;
    }

    /// <summary>Builds the church of the home region from the people as they stand (offices only; piety is derived and not stored).</summary>
    public static ChurchBook Build(WorldState st, ContentDatabase? db)
    {
        var book = new ChurchBook { Seed = st.Seed, Religions = ReligionBook.Derive(st.Seed, db, st.History) };
        if (db != null) book.Settings = db.All<FaithSettingsDef>().OrderBy(s => s.Id, StringComparer.Ordinal).FirstOrDefault() ?? book.Settings;
        var pop = st.Population; var region = pop.Region; var pt = pop.People;
        for (int s = 0; s < region.Settlements.Count; s++)
        {
            if (s >= pop.PriestHousehold.Length || pop.PriestHousehold[s] < 0) continue;
            int pr = PriestRow(pop, s);
            book.Parishes[s] = new ParishRow { Settlement = s, Priest = pr >= 0 ? (long)pt.IdOf(pr).Value : -1, VacantSince = pr >= 0 ? -1 : 0 };
        }
        BindSees(book, region, pop);
        return book;
    }

    /// <summary>One see per (religion, realm) that has a parish: the seat is the largest parish (ties by index), the bishop its priest. Existing sees are kept as they are.</summary>
    public static void BindSees(ChurchBook book, WorldGen.Region.Region region, Population pop)
    {
        var groups = new SortedDictionary<(int Religion, int Realm), List<int>>();
        foreach (int s in book.Parishes.Keys)
        {
            int r = book.Religions.OfSettlement(region, s); if (r < 0) continue;
            var key = (r, RealmOf(region, s));
            if (!groups.TryGetValue(key, out var l)) groups[key] = l = [];
            l.Add(s);
        }
        int index = book.Sees.Count == 0 ? 0 : book.Sees.Keys.Max() + 1;
        foreach (var ((religion, realm), list) in groups)
        {
            string rid = book.Religions.Get(religion)!.Id;
            if (book.SeeOf(rid, realm) != null) continue;
            int seat = list.OrderByDescending(s => region.Settlements[s].TargetPop).ThenBy(s => s).First();
            var bishop = book.Parishes[seat].Priest;
            book.Sees[index] = new SeeRow { Index = index, Religion = rid, Realm = realm, Seat = seat, Bishop = bishop, VacantSince = bishop >= 0 ? -1 : 0 };
            index++;
        }
    }
}

/// <summary>The offices step of the Phase 6 seeding pass (overview 7): parishes and sees bound to the people who hold them. A bare world with no content or no priests keeps no section.</summary>
public sealed class FaithSeedStep : IPhase6SeedStep
{
    public string Name => "offices";
    public string Section => FaithSave.SectionName;

    public bool Seed(Phase6SeedContext ctx)
    {
        var st = ctx.State;
        if (ctx.Content == null || st.Population.Region.Settlements.Count == 0) return false;
        if (ctx.FromSave) { st.ChurchSeedPending = true; return false; }                   // a loaded save is seeded when the runtime starts (FaithWiring.Attach): read and written back, it keeps its bytes
        var book = ChurchSeeding.Build(st, ctx.Content);
        if (book.Parishes.Count == 0) return false;
        st.Church = book;
        return true;
    }
}
