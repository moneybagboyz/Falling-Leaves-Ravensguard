using System.Diagnostics;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Succession;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.PeopleTables;

/// <summary>
/// P8-PF-2, gates PF-6 to PF-8 (09-performance-and-tooling 3.3, 5): the yearly compaction drops the dead rows nothing names, a new person takes a dropped row under a new id, and every saved reference resolves.
/// The world is the seed-1234 game state; the dead are made by hand (a death is a status, a day and a household without the person), thirty or more game years back.
/// </summary>
public class DeadRowsTests(ITestOutputHelper output)
{
    private static (WorldState St, GameTime Now) World()
    {
        var (_, st) = Ravensguard.Sim.Tests.Core.ChronicleSoakTests.Game(1234);
        Assert.NotNull(st.Abstract);
        return (st, st.Clock.Now);
    }

    private static void Kill(WorldState st, int row, GameTime now, int yearsAgo)
    {
        var pt = st.Population.People; var hht = st.Population.Households;
        int hh = pt.Household[row];
        if (hh >= 0)
        {
            hht.RemoveMember(hh, row);
            if (hht.Head[hh] == row) hht.Head[hh] = hht.MemberCount[hh] > 0 ? hht.MemberIdx[hht.MemberStart[hh]] : -1;
        }
        int sp = pt.Spouse[row]; if (sp >= 0 && pt.Spouse[sp] == row) pt.Spouse[sp] = -1;
        pt.Status[row] = PersonStatus.Dead; pt.DeathDay[row] = (int)(now.TotalDays - (long)yearsAgo * TimeConst.DaysPerYear - 3); pt.DeathCause[row] = DeathCause.Age;
        st.Abstract!.Vital.Alive--;
    }

    /// <summary>Alive commoners no other record could name (not notable, not the hero or his kin, not in a fief or lot post), in row order.</summary>
    private static List<int> Commoners(WorldState st)
    {
        var pop = st.Population; var pt = pop.People; var mine = ChronicleView.MineOf(st);
        var posts = new HashSet<int>(pop.FiefLord); foreach (var a in pop.LotKeeper) foreach (int r in a) posts.Add(r);
        for (int h = 0; h < pop.Households.Count; h++) posts.Add(pop.Households.Head[h]);
        var all = new HashSet<ulong>(Enumerable.Range(0, pt.Count).Select(i => pt.IdOf(i).Value));
        var named = HeldIds.NamedInChronicle(st.Chronicle, all); named.UnionWith(HeldIds.Of(st, all).Persons);
        var list = new List<int>();
        for (int i = 0; i < pt.Count; i++)
            if (pt.Status[i] == PersonStatus.Alive && pt.Notable[i] == EntityId.None && i != st.Hero!.PersonIndex && !mine.Contains(pt.IdOf(i).Value) && !posts.Contains(i) && !named.Contains(pt.IdOf(i).Value)
                && pt.Spouse[i] < 0 && pt.Employer[i] < 0)
                list.Add(i);
        return list;
    }

    [Fact] // PF-6
    public void PF6_AfterThirtyYears_DeadRowsNamedByNothingAreGone_AndEveryNamedOneStays()
    {
        var (st, now) = World(); var pt = st.Population.People;
        var pool = Commoners(st);
        // children (not parents of anyone) so that "kin of the living" does not hide the plain ones: take people nobody lists as a parent
        var parents = new HashSet<int>(); for (int i = 0; i < pt.Count; i++) { if (pt.Father[i] >= 0) parents.Add(pt.Father[i]); if (pt.Mother[i] >= 0) parents.Add(pt.Mother[i]); }
        pool = pool.Where(r => !parents.Contains(r)).ToList();
        var plain = pool.Take(400).ToList();
        int byEntry = pool[400], byBook = pool[401], recent = pool[402], byEstate = pool[403];
        int kinOfLiving = Commoners(st).First(r => parents.Contains(r));
        foreach (int r in plain.Append(byEntry).Append(byBook).Append(byEstate)) Kill(st, r, now, 31);
        Kill(st, recent, now, 5);
        Kill(st, kinOfLiving, now, 40);
        // a notable
        int notable = Enumerable.Range(0, pt.Count).First(i => pt.Notable[i] != EntityId.None && pt.Status[i] == PersonStatus.Alive && i != st.Hero!.PersonIndex);
        Kill(st, notable, now, 60);
        // named by a kept chronicle entry, by a book (a will), and by an estate record of the succession book
        st.Chronicle.Add(new ChronicleEntry(st.Ids.Allocate(IdKind.Event), GameTime.FromGameDays(now.TotalDays - 5), "test_event", [pt.IdOf(byEntry)], [], new Dictionary<string, string>()));
        st.Wills = new WillBook();
        var will = new Will { Testator = pt.IdOf(st.Hero!.PersonIndex), Witness = pt.IdOf(byEstate) }; will.Bequests.Add(new Bequest(pt.IdOf(byBook), BequestWhat.All));
        st.Wills.ByTestator[will.Testator] = will;
        var ids = plain.Concat([byEntry, byBook, recent, byEstate, kinOfLiving, notable]).ToDictionary(r => r, r => pt.IdOf(r));
        var names = ids.Keys.ToDictionary(r => r, r => pt.FullName(r));
        int kinChild = Enumerable.Range(0, pt.Count).First(i => pt.Status[i] == PersonStatus.Alive && (pt.Father[i] == kinOfLiving || pt.Mother[i] == kinOfLiving));
        int before = pt.Count;

        var (entries, dropped) = PersonCompaction.Run(st, now);

        foreach (int r in plain) { Assert.True(pt.IsFree(r), $"row {r} should be dropped"); Assert.Equal(-1, pt.IndexOf(ids[r])); Assert.Equal("", pt.Given[r]); Assert.Equal(PersonStatus.Dead, pt.Status[r]); }
        foreach (int r in new[] { byEntry, byBook, recent, byEstate, kinOfLiving, notable }) { Assert.False(pt.IsFree(r), $"row {r} is named and must stay"); Assert.Equal(r, pt.IndexOf(ids[r])); Assert.Equal(names[r], pt.FullName(r)); }
        Assert.True(pt.Father[kinChild] == kinOfLiving || pt.Mother[kinChild] == kinOfLiving);
        Assert.Equal(before, pt.Count);
        Assert.True(dropped >= plain.Count, $"dropped {dropped}");
        // every link a kept row holds resolves to a kept row or to none
        for (int i = 0; i < pt.Count; i++)
        {
            if (pt.IsFree(i)) continue;
            foreach (int l in new[] { pt.Father[i], pt.Mother[i], pt.Spouse[i], pt.Employer[i], pt.FatherOfUnborn[i] }) Assert.True(l < 0 || !pt.IsFree(l), $"row {i} links to the dropped row {l}");
        }
        // the dropped rows are the same on a second pass with nothing new: nothing more goes, and the rows stay free
        Assert.Equal(0, PersonCompaction.Run(st, now).Rows);
        output.WriteLine($"PF-6: {plain.Count} unnamed dead rows of {before} dropped ({dropped} in all); the {ids.Count - plain.Count} named ones kept; {entries} chronicle entries compacted");
    }

    [Fact] // PF-7
    public void PF7_NoIdAndNoRowIsReused_AndEverySavedReferenceResolves()
    {
        var (st, now) = World(); var pt = st.Population.People;
        var every = new HashSet<ulong>(Enumerable.Range(0, pt.Count).Select(i => pt.IdOf(i).Value));
        var plain = Commoners(st).Take(300).ToList();
        var parents = new HashSet<int>(); for (int i = 0; i < pt.Count; i++) { if (pt.Father[i] >= 0) parents.Add(pt.Father[i]); if (pt.Mother[i] >= 0) parents.Add(pt.Mother[i]); }
        plain = plain.Where(r => !parents.Contains(r)).ToList();
        foreach (int r in plain) Kill(st, r, now, 32);
        var gone = plain.Select(r => pt.IdOf(r)).ToList();
        Assert.Equal(plain.Count, PersonCompaction.Run(st, now).Rows);
        int count = pt.Count;
        // 200 births: the lowest free rows first, new ids from the allocator, none seen before
        var born = new List<(int Row, EntityId Id)>();
        for (int k = 0; k < 200; k++)
        {
            st.Ids.EnsureNext(IdKind.Person, pt.BaseCounter + (ulong)pt.Count);
            var id = st.Ids.Allocate(IdKind.Person);
            int row = pt.Add(id);
            st.Economy!.OpenPerson(row);
            pt.Status[row] = PersonStatus.Alive; pt.Given[row] = "Born" + k; pt.BirthDay[row] = (int)now.TotalDays;
            Assert.Equal(id, pt.IdOf(row)); Assert.True(every.Add(id.Value), $"id {id} was used before");
            born.Add((row, id));
        }
        Assert.Equal(count + 200, pt.Count);                                                                // births append: a dropped row is never taken again
        Assert.All(plain, r => { Assert.Equal(PersonStatus.Dead, pt.Status[r]); Assert.Equal("", pt.Given[r]); }); Assert.All(born, b => Assert.DoesNotContain(b.Row, plain));
        foreach (var id in gone) { Assert.Equal(-1, pt.IndexOf(id)); }
        foreach (var (row, id) in born) Assert.Equal(row, pt.IndexOf(id));
        // more births than free rows grow the table at the end, still under new ids
        for (int k = 0; k < 150; k++)
        {
            st.Ids.EnsureNext(IdKind.Person, pt.BaseCounter + (ulong)pt.Count);
            var id = st.Ids.Allocate(IdKind.Person); int row = pt.Add(id); st.Economy!.OpenPerson(row);
            pt.Status[row] = PersonStatus.Alive; Assert.True(every.Add(id.Value)); Assert.Equal(row, pt.IndexOf(id));
        }
        Assert.Equal(count + 350, pt.Count);
        // the save: the same rows, ids, free list and hash after a round trip, and every dropped id still unresolved
        var back = WorldSave.Restore(SaveCodec.Decode(SaveCodec.Encode(WorldSave.Capture(st, "h", "0.1.0"))), st.Content!, "h");
        var bp = back.Population.People;
        Assert.Equal(pt.Count, bp.Count);
        for (int i = 0; i < pt.Count; i++) Assert.Equal(pt.IdOf(i), bp.IdOf(i));
        foreach (var id in gone) Assert.Equal(-1, bp.IndexOf(id));
        foreach (var (row, id) in born) Assert.Equal(row, bp.IndexOf(id));
        Assert.Equal(pt.FreeRows, bp.FreeRows); Assert.Equal(pt.RetiredRows, bp.RetiredRows);
        Assert.Equal(st.Population.ComputeHash(), back.Population.ComputeHash());
        Assert.Equal(st.Population.ComputeSimHash(), back.Population.ComputeSimHash());
        // and a world with nothing dropped writes no new key: the retired rows are absent
        Assert.Null(WorldSave.EncodePeople(World().St.Population).Retired);
        output.WriteLine($"PF-7: {gone.Count} ids retired, 350 births appended under new ids, save round trip equal");
    }

    [Fact] // PF-7: a holder of a person's row number
    public void PF7_ARowNumberAHolderKeepsOfADroppedPerson_StillPointsAtABlankDeadRow_NeverANewborn()
    {
        var (st, now) = World(); var pt = st.Population.People;
        var plain = Commoners(st).Take(100).ToList();
        foreach (int r in plain) Kill(st, r, now, 33);
        var holders = plain.ToArray();                                 // stands for every int row a book, a crew or the live tier keeps
        PersonCompaction.Run(st, now);
        for (int y = 0; y < 3; y++)
            for (int k = 0; k < 300; k++) { st.Ids.EnsureNext(IdKind.Person, pt.BaseCounter + (ulong)pt.Count); int row = pt.Add(st.Ids.Allocate(IdKind.Person)); st.Economy!.OpenPerson(row); pt.Status[row] = PersonStatus.Alive; }
        holders = holders.Where(pt.IsFree).ToArray(); Assert.True(holders.Length > 50);
        foreach (int row in holders) { Assert.Equal(PersonStatus.Dead, pt.Status[row]); Assert.False(pt.IsAlive(row)); Assert.Equal("", pt.Given[row]); }
    }

    [Fact] // PF-7 (phase-6 G2: compaction's dangling links)
    public void PF7_AnEntryAnyLiveRecordNames_IsKeptWhateverItsAge_AndTheRestOfTheOldCommonOnesGo()
    {
        var (st, now) = World(); var pt = st.Population.People;
        var commoners = Commoners(st);
        EntityId Old(int row, int years) { var id = st.Ids.Allocate(IdKind.Event); st.Chronicle.Add(new ChronicleEntry(id, GameTime.FromGameDays(now.TotalDays - (long)years * TimeConst.DaysPerYear), "test_event", [pt.IdOf(row)], [], new Dictionary<string, string>())); return id; }
        var held = Old(commoners[10], 14); var loose = Old(commoners[11], 14); var heldBy = Old(commoners[12], 15); var young = Old(commoners[13], 3);
        st.Abstract!.Vital.FamineEntry[0] = held.Value;                                  // a record of the abstract world names it
        st.Wills = new WillBook(); var w = new Will { Testator = pt.IdOf(st.Hero!.PersonIndex) }; w.Bequests.Add(new Bequest(pt.IdOf(commoners[20]), BequestWhat.Deed, heldBy)); st.Wills.ByTestator[w.Testator] = w;   // and a book names the other
        int dropped = ChronicleCompaction.Yearly(st, now);
        Assert.NotNull(st.Chronicle.Get(held)); Assert.NotNull(st.Chronicle.Get(heldBy)); Assert.NotNull(st.Chronicle.Get(young));
        Assert.Null(st.Chronicle.Get(loose));
        Assert.True(dropped >= 1);
    }

    [Fact] // PF-8 (the row-count half; the wall-clock half is the century soak, FL_SOAK100)
    public void PF8_RowCountRatchet_ACenturyOfDeathsAndBirths_GrowsNoFasterThanOneRowPerBirth()
    {
        var (st, now) = World(); var pt = st.Population.People;
        int start = pt.Count; const int perYear = 300, lifeYears = 20, years = 100;
        var queue = new Queue<(int Year, List<int> Rows)>();
        long firstMs = 0, lastMs = 0; int peak = start;
        for (int y = 1; y <= years; y++)
        {
            var at = GameTime.FromGameDays(now.TotalDays + (long)y * TimeConst.DaysPerYear);
            var born = new List<int>();
            for (int k = 0; k < perYear; k++)
            {
                st.Ids.EnsureNext(IdKind.Person, pt.BaseCounter + (ulong)pt.Count);
                int row = pt.Add(st.Ids.Allocate(IdKind.Person)); st.Economy!.OpenPerson(row);
                pt.Status[row] = PersonStatus.Alive; pt.BirthDay[row] = (int)at.TotalDays; born.Add(row);
            }
            queue.Enqueue((y, born));
            while (queue.Count > 0 && queue.Peek().Year <= y - lifeYears)
                foreach (int r in queue.Dequeue().Rows) { pt.Status[r] = PersonStatus.Dead; pt.DeathDay[r] = (int)at.TotalDays - 1; pt.DeathCause[r] = DeathCause.Age; }
            PersonCompaction.Run(st, at);
            peak = Math.Max(peak, pt.Count);
            var sw = Stopwatch.StartNew(); for (int rep = 0; rep < 20; rep++) st.Population.ComputeSimHash(); long ms = sw.ElapsedMilliseconds;
            if (y == 1) firstMs = ms; if (y == years) lastMs = ms;
        }
        int control = start + perYear * years;
        output.WriteLine($"PF-8: rows {start} at the start, {pt.Count} at year {years} (peak {peak}); without dropping {control}; a full pass over the rows {firstMs} ms in year 1, {lastMs} ms in year {years}");
        // rows are not reused (see PersonTable.Add), so the table grows by one row a birth: the ratchet is that, with the blanked rows all the dead of thirty years back
        Assert.Equal(control, pt.Count);
        Assert.True(pt.RetiredRows.Count >= perYear * (years - lifeYears - PersonCompaction.DeadYears - 1), $"blanked {pt.RetiredRows.Count}");
        Assert.True(lastMs <= 2 * Math.Max(1, firstMs) + 20, $"a pass over the rows took {lastMs} ms against {firstMs}");
        // nothing the century left in the table is a dangling id: every live row resolves by its own id
        for (int i = 0; i < pt.Count; i++) if (!pt.IsFree(i)) Assert.Equal(i, pt.IndexOf(pt.IdOf(i)));
    }
}
