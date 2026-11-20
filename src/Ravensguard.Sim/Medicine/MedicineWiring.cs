using System.Runtime.CompilerServices;
using Ravensguard.Content;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Medicine;

/// <summary>
/// Installs medicine on the home region (P6-B1 hook in <see cref="WorldRuntime"/>): the world state's <see cref="PhysicianBook"/> (the "medicine" section) goes into the sim's services for
/// <see cref="AbstractPhysic"/>, gets the combat content's body-plan lookup, and, for a loaded save that had no physicians yet, is seeded now that the records exist.
/// </summary>
public static class MedicineWiring
{
    private static readonly ConditionalWeakTable<ContentDatabase, CombatTablesV2> Tables = new();

    /// <summary>The body plan of a wound book (the combat content's), or null for an unknown plan or no content.</summary>
    public static BodyPlanV2? PlanOf(ContentDatabase? content, string id)
    {
        if (content == null) return null;
        var t = Tables.GetValue(content, c => new CombatTables(c).V2);
        try { return t.Plan(id); } catch (KeyNotFoundException) { return null; }
    }

    public static PhysicianBook Attach(RegionWorld home, WorldState state, ContentDatabase content)
    {
        var book = state.Medicine ??= new PhysicianBook();
        book.Plans = id => PlanOf(content, id);
        home.Sim.Set(book);
        if (state.MedicineSeedPending)
        {
            state.MedicineSeedPending = false;
            if (!PhysicianSeeding.Run(home.Sim).IsEmpty && !state.LoadNotes.Contains(Phase6Seeding.LoadNote)) state.LoadNotes = [.. state.LoadNotes, Phase6Seeding.LoadNote];   // the load report says physicians were seated (overview 7)
        }
        return book;
    }
}

/// <summary>The physicians' step of the Phase 6 seeding pass (overview 7): at a new game the records exist and the men are chosen now; at a loaded save the records are made later, so the pass only says so.</summary>
public sealed class PhysicianSeedStep : IPhase6SeedStep
{
    public string Name => "physicians";
    public string Section => MedicineSave.SectionName;

    public bool Seed(Phase6SeedContext ctx)
    {
        var st = ctx.State;
        if (st.Economy == null || st.Production == null) return false;                       // a bare world (a tool, a test fixture): no people to seat
        if (st.HomeRecords is { } home)
        {
            var book = st.Medicine ??= new PhysicianBook();
            home.Sim.Set(book);
            if (ctx.Content != null) book.Plans = id => MedicineWiring.PlanOf(ctx.Content, id);
            PhysicianSeeding.Run(home.Sim);
            return !book.IsEmpty;
        }
        st.MedicineSeedPending = true;                                                       // MedicineWiring.Attach seeds when the home region is built (and adds the load note then)
        return false;
    }
}
