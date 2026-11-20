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
            Crime = links?.Crime, StandingWithRealm = links?.StandingWithRealm ?? ((_, _) => 0), Hero = links?.Hero() ?? EntityId.None, HasHero = links?.HasHero ?? false, Deed = links?.Deed, GuardsEnforce = links?.GuardsEnforce, WarCause = links?.WarCause ?? (_ => ""),
        };
    }

    /// <summary>The rule book of a world state with its content bound, or null when the world has none.</summary>
    public static RuleBook? Bind(WorldState st, ContentDatabase? content = null)
    {
        var b = st.Rule; if (b == null) return null;
        return RuleSeeding.Bind(b, content ?? st.Content, st.Seed);
    }

    /// <summary>
    /// Puts a book into a sim world's services with its links, hands the hearth tax's rounds to the officials and has the land judge charters as politics: the part of the wiring a headless world (tools, tests) shares with the
    /// running game. Not saved.
    /// </summary>
    public static RuleLinks Install(SimWorld sim, EconomyWorld ew, RuleBook book, Ravensguard.Sim.WorldGen.Region.Region region)
    {
        sim.Set(book);
        var links = sim.Get<RuleLinks>() ?? new RuleLinks(); sim.Set(links);
        RuleLand.ApplyMarketCharters(book, region);
        // the hearth tax's round of a settlement is the reeve's or bailiff's: what he handled, against what was expected (4.3, 4.8)
        ew.Taxes.Collected = (s, hearthIn, now, ob) =>
        {
            var c = Context(sim, book, RuleChron.Of(ob));
            var tax = ew.Taxes; int fief = region.Settlements[s].FiefIndex; if (fief < 0) return;
            long expected = (long)c.HouseholdsInSettlement(s) * tax.HearthMites(tax.LevelOfFief(fief));
            RuleOfficials.Collected(c, s, hearthIn, expected, (int)now.TotalDays);
        };
        if (sim.Get<LandWorld>() is { } lw)
            lw.CharterJudge = (grantee, gk, grantor, fee, contested) => RuleLand.Judge(Context(sim, book, RuleChron.Silent), grantee, gk, grantor, fee, contested);
        return links;
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
        Install(home.Sim, home.Economy, book, state.Population.Region);
        var links = home.Sim.Get<RuleLinks>()!;
        links.Hero = () => state.Party?.Hero ?? EntityId.None; links.HasHero = true;
        links.Deed = (doer, deedId, entry, place, day) => { if (state.Content != null) Ravensguard.Sim.Faith.FaithStanding.Record(state, state.Content, doer, deedId, entry, place, 1, -1, false, day); };
        links.StandingWithRealm = (p, realm) => realm < 0 ? 0 : Standing.Of(state.Standing, p, GroupRef.Realm(realm));
        links.Crime = sc =>
        {
            var law = LawWorld.From(state, state.Content);
            var crimes = state.Law ??= new CrimeBook();
            var rows = sc.Witnesses.Select(w => new WitnessRow(w.Value, WitnessRow.KnewIt)).ToList();
            var rec = CrimeSystem.Commit(law, crimes, new ActSpec("embezzlement", sc.Doer, sc.Victim, sc.Settlement, sc.Sec, sc.Value, 0, sc.NoteKey), rows, ActorLook.None);
            if (sc.Report && sc.Reporter != EntityId.None) CrimeSystem.Report(law, rec, sc.Reporter, sc.Sec);
            return new EntityId(rec.Entry);
        };
        return book;
    }
}
