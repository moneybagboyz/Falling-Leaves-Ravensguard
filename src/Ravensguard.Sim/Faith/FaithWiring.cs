using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Faith;

/// <summary>
/// Installs the church on the home region (P6-B4 hook in <see cref="WorldRuntime"/>): the world state's <see cref="ChurchBook"/> (the "faith" section) goes into the sim's services for <see cref="FaithSystem"/>,
/// the religions are derived once the content is known, and the households' festive appetite is bound to the holy days of their faith. Also the one place the derived religions of a world are found.
/// </summary>
public static class FaithWiring
{
    /// <summary>The religions of the world (derived on first use from the content and the history, never saved). Empty until the state has its content.</summary>
    public static ReligionBook ReligionsOf(WorldState st, ContentDatabase? content = null)
    {
        if (st.Religions != null) return st.Religions;
        var db = content ?? st.Content; if (db == null) return ReligionBook.Empty;
        return st.Religions = ReligionBook.Derive(st.Seed, db, st.History);
    }

    /// <summary>The church with its derived parts bound (religions, settings, seed), or null when the world has none.</summary>
    public static ChurchBook? Bind(WorldState st, ContentDatabase? content = null)
    {
        var b = st.Church; if (b == null) return null;
        var db = content ?? st.Content;
        b.Seed = st.Seed; b.Religions = ReligionsOf(st, db);
        if (db != null) b.Settings = db.All<FaithSettingsDef>().OrderBy(s => s.Id, StringComparer.Ordinal).FirstOrDefault() ?? b.Settings;
        return b;
    }

    /// <summary>A crime was recorded (faith 4.3): the doer loses the piety of the sin (murder 20, theft 5, sacrilege 30, grave robbing a fifth of the barrow taboo, heresy 30).</summary>
    public static void OnCrime(WorldState st, EntityId doer, string kind)
    {
        var b = Bind(st); if (b == null) return;
        int row = st.Population.People.IndexOf(doer);
        int ri = row >= 0 ? b.Religions.OfCulture(st.Population.People.Culture[row]) : -1;
        int cost = ChurchServices.SinPiety(b.Religions.Get(ri), kind);
        if (cost > 0) Piety.Adjust(b, st.Population.People, doer, -cost);
    }

    public static ChurchBook? Attach(RegionWorld home, WorldState state, ContentDatabase content)
    {
        var book = state.Church;
        if (book == null)
        {
            if (state.Population.Region.Settlements.Count == 0) return null;
            var made = ChurchSeeding.Build(state, content);                       // an older save (the pass only marked it) or a bare state with content only now: derived from the seed and the people's ids
            if (made.Parishes.Count == 0) return null;
            book = state.Church = made;
            if (state.ChurchSeedPending && !state.LoadNotes.Contains(Phase6Seeding.LoadNote)) state.LoadNotes = [.. state.LoadNotes, Phase6Seeding.LoadNote];   // the load report says so (overview 7)
        }
        state.ChurchSeedPending = false;
        Bind(state, content);
        home.Sim.Set(book);
        var region = state.Population.Region;
        // a holy day's feast: the household of a parish whose faith keeps the day eats and drinks more (the extra comes out of what the larder holds, never into the need)
        home.Sim.Economy.FestivePct = (hh, day) =>
        {
            var hht = home.Sim.Households; if (hh < 0 || hh >= hht.Count) return 0;
            int s = hht.Settlement[hh];
            return book.Religions.Get(book.Religions.OfSettlement(region, s))?.IsHoly(day % TimeConst.DaysPerYear + 1) == true ? book.Settings.HolyFoodPct : 0;
        };
        return book;
    }
}
