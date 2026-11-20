namespace Ravensguard.Sim.Core;

/// <summary>What <see cref="ChronicleChains.Check"/> found: entries walked, chains' roots, causes naming no entry, causes naming a later entry, and roots a type does not allow.</summary>
public sealed record ChainReport(int Entries, int Roots, int Unresolved, int OutOfOrder, int IllegalRoots, IReadOnlyList<string> Problems)
{
    public bool Sound => Unresolved == 0 && OutOfOrder == 0 && IllegalRoots == 0;
}

/// <summary>
/// The chain walker of chronicle-full 4.3 (CH-6): every cause's <see cref="Cause.EventId"/> must name an earlier entry (earlier in append order and not later in time), and an entry
/// none of whose causes names an entry is a root, allowed only for the kinds that start things: a type whose row says <c>root</c> (weather, a person's decision, the history's seeding)
/// or <c>chance</c> (an accident's dice), or a legacy type (Phase 2-5 and history v0 writers, which move to specific causes with their own steps).
/// </summary>
public static class ChronicleChains
{
    public static ChainReport Check(Chronicle c, ChronicleTypes? types = null, int maxProblems = 20, Chronicle? older = null)
    {
        var pos = new Dictionary<EntityId, int>(c.Count);
        for (int i = 0; i < c.Count; i++) pos[c.All[i].Id] = i;
        int roots = 0, unresolved = 0, outOfOrder = 0, illegal = 0;
        var problems = new List<string>();
        void Note(string s) { if (problems.Count < maxProblems) problems.Add(s); }
        for (int i = 0; i < c.Count; i++)
        {
            var e = c.All[i];
            bool anyEvent = false;
            foreach (var cs in e.Causes)
            {
                if (cs.EventId is not { } id) continue;
                anyEvent = true;
                if (!pos.TryGetValue(id, out int j))
                {
                    if (older?.Get(id) is { } oe && oe.When.Seconds <= e.When.Seconds) continue;                  // an entry of the history this region continues: earlier by construction
                    unresolved++; Note($"{e.TypeId} {e.Id}: cause names {id}, which is not in the chronicle"); }
                else if (j >= i || c.All[j].When.Seconds > e.When.Seconds) { outOfOrder++; Note($"{e.TypeId} {e.Id}: cause {id} ({c.All[j].TypeId}) is not earlier"); }
            }
            if (anyEvent) continue;
            roots++;
            var row = types?.Get(e.TypeId);
            if (types != null && (row == null || !(row.Def.Root || row.Def.Chance || row.Def.Legacy))) { illegal++; Note($"{e.TypeId} {e.Id}: a root, but its type is not a root or chance type"); }
        }
        return new ChainReport(c.Count, roots, unresolved, outOfOrder, illegal, problems);
    }
}
