using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.Law;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Society;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Rule;

/// <summary>
/// Installs the rule and income on the home region (P6-C3 hook in <see cref="WorldRuntime"/>): the world state's <see cref="RuleBook"/> (the "fiefs" section) goes into the sim's services for <see cref="RuleSystem"/>,
/// derived at once for an older save (or a bare state that has content only now), its numbers bound from the content; the hearth tax hands each settlement's round to the official who collected it; the land's petitions are
/// judged as politics; the founded towns of saved market charters are towns again; and the crime core is told of an embezzlement.
/// </summary>
public static class RuleWiring
{
    /// <summary>The rule's context for a tick or a barrier: the book, the world, the economy and the links the wiring installed.</summary>
    public static RuleCtx Context(SimWorld w, RuleBook book, RuleChron chron)
    {
        var links = w.Get<RuleLinks>();
        return new RuleCtx
        {
            W = w, B = book, Ew = w.Get<EconomyWorld>(), Chron = chron, Land = w.Get<LandWorld>(),
            Crime = links?.Crime, StandingWithRealm = links?.StandingWithRealm ?? ((_, _) => 0), Hero = links?.Hero() ?? EntityId.None, GuardsEnforce = links?.GuardsEnforce, WarCause = links?.WarCause ?? (_ => ""),
        };
    }

    /// <summary>The rule book of a world state with its content bound, or null when the world has none.</summary>
    public static RuleBook? Bind(WorldState st, ContentDatabase? content = null)
    {
        var b = st.Rule; if (b == null) return null;
        return RuleSeeding.Bind(b, content ?? st.Content, st.Seed);
    }

    public static RuleBook? Attach(RegionWorld home, WorldState state, ContentDatabase content)
    {
        var book = state.Rule;
        if (book == null)
        {
            if (state.Population.Region.Fiefs.Count == 0) return null;
            var made = RuleSeeding.Build(state, content);                                    // an older save (the pass only marked it) or a bare state with content only now: derived from the seed and the people's ids
            if (made.Fiefs.Count == 0) return null;
            book = state.Rule = made;
            if (state.RuleSeedPending && !state.LoadNotes.Contains(Phase6Seeding.LoadNote)) state.LoadNotes = [.. state.LoadNotes, Phase6Seeding.LoadNote];
        }
        state.RuleSeedPending = false;
        Bind(state, content);
        home.Sim.Set(book);
        var links = new RuleLinks
        {
            Hero = () => state.Party?.Hero ?? EntityId.None,
            StandingWithRealm = (p, realm) => realm < 0 ? 0 : Standing.Of(state.Standing, p, GroupRef.Realm(realm)),
            Crime = sc =>
            {
                var law = LawWorld.From(state, state.Content);
                var crimes = state.Law ??= new CrimeBook();
                var rows = sc.Witnesses.Select(w => new WitnessRow(w.Value, WitnessRow.KnewIt)).ToList();
                var rec = CrimeSystem.Commit(law, crimes, new ActSpec("embezzlement", sc.Doer, sc.Victim, sc.Settlement, sc.Sec, sc.Value, 0, sc.NoteKey), rows, ActorLook.None);
                if (sc.Report && sc.Reporter != EntityId.None) CrimeSystem.Report(law, rec, sc.Reporter, sc.Sec);
                return new EntityId(rec.Entry);
            },
        };
        home.Sim.Set(links);
        RuleLand.ApplyMarketCharters(book, state.Population.Region);
        var ew = home.Economy;
        // the hearth tax's round of a settlement is the reeve's or bailiff's: what he handled, against what was expected (4.3, 4.8)
        ew.Taxes.Collected = (s, hearthIn, now, ob) =>
        {
            var c = Context(home.Sim, book, RuleChron.Of(ob));
            var tax = ew.Taxes; var region = state.Population.Region; int fief = region.Settlements[s].FiefIndex; if (fief < 0) return;
            long expected = (long)c.HouseholdsInSettlement(s) * tax.HearthMites(tax.LevelOfFief(fief));
            RuleOfficials.Collected(c, s, hearthIn, expected, (int)now.TotalDays);
        };
        if (home.Sim.Get<LandWorld>() is { } lw)
            lw.CharterJudge = (grantee, gk, grantor, fee, contested) => RuleLand.Judge(Context(home.Sim, book, RuleChron.Silent), grantee, gk, grantor, fee, contested);
        return book;
    }
}
