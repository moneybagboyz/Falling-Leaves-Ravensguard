using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Jobs;
using Ravensguard.Sim.Law;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.Worlds;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Phase6Overview;

/// <summary>
/// OV-1 (overview 4, "theft in front of a witness raises a bounty with an explained cause"): the CJ-1 script on a whole running game of seed 1234 (no unit rig): the hero's act goes through the desk as the
/// street view sends it (five witnesses it saw, Wynn the nearest and one who knew him), the hero is not taken that day, at the next day's first hour a witness reports and the lord posts a bounty from the real fief
/// treasury, and the Journal's Wanted list (<see cref="Bounties.OnPerson"/>, which the card reads) and the Chronicle's Why (the entry's chain, worded by the Chronicle's own reader from en.csv) name the theft, the
/// witness and the bounty. The save in the middle resumes byte for byte.
/// </summary>
public class Ov1TheftTests(ITestOutputHelper output)
{
    [Fact]
    public void OV1_ATheftSeenByFiveRaisesABountyAtTheNextDaysFirstHour_TheWantedCardAndTheChroniclesWhyBothNameTheTheftAndTheWitness()
    {
        var (w, s) = P6Game.Game();
        using var rt = new WorldRuntime(s, w.Content, new MemoryRegionPager());
        P6Game.AttachGameLinks(rt, w.Content);
        var ctx = new HeroContext(s, s.Party!, w.Content);
        var p = new P6Play(rt, ctx);
        var pt = s.Population.People; int town = pt.HomeSettlement[s.Hero!.PersonIndex];
        long h0 = rt.HourDone + (24 - rt.HourDone % 24) % 24;
        rt.AdvanceTo(GameTime.FromGameHours(h0 + 10));                                              // ten in the morning, the market's hours

        // five adults of his town saw it: Wynn first (he knew the hero, he is the one robbed), a second who knew him, three who did not
        int day = (int)(rt.HourDone / 24);
        var five = Enumerable.Range(0, pt.Count).Where(i => pt.IsAlive(i) && pt.HomeSettlement[i] == town && pt.Status[i] == PersonStatus.Alive && pt.AgeYears(i, day) >= 18 && i != s.Hero.PersonIndex).Take(5).ToList();
        Assert.Equal(5, five.Count);
        var wynn = pt.IdOf(five[0]); string wynnName = pt.FullName(five[0]);
        var rows = five.Select((r, k) => new WitnessRow(pt.IdOf(r).Value, k < 2 ? WitnessRow.KnewIt : (byte)0)).ToList();
        long seeded = s.Economy!.Ledger.Audit().Seeded.Mites;

        var act = p.Do(CrimeDesk.Act("theft", wynn, town, 240, 0, "ch_note_crime_hero", new ActorLook([0xA03030u], 2), rows, new CryInfo(wynn, 4, 2), rt.HourDone * 3600 + 600));
        Assert.True(act.Ok, act.Text.ToString());
        var rec = s.Law!.Crimes.Values.Single(c => c.Kind == "theft");
        Assert.Equal(5, rec.Witnesses.Count);
        Assert.Equal((CrimeState.Seen, true, true), (rec.State, rec.Known, rec.Cried));
        Assert.True(rec.Witnesses.First(x => x.Person == wynn.Value).Recognised);

        // the hero is away before the guards come: nobody holds him, nothing is posted on the day (the live tier's guard pursuit is a gap, see phase-6-open.md)
        rt.AdvanceTo(GameTime.FromGameHours(h0 + 23));
        Assert.Null(s.Captivity?.Hero); Assert.Equal(CrimeState.Seen, rec.State); Assert.Equal(0UL, rec.BountyJob);

        // at the next day's first hour (H5) a witness reports and the lord posts the bounty from the fief treasury
        rt.AdvanceTo(GameTime.FromGameHours(h0 + 25));
        Assert.Equal(CrimeState.Reported, rec.State);
        var job = s.Jobs!.Get(new EntityId(rec.BountyJob))!;
        Assert.Equal((OddJobKind.Bounty, 580), (job.Kind, (int)job.WageMites));
        Assert.Equal(580, s.Economy.Ledger.Balance(new PurseId(job.Escrow)).Mites);
        Assert.True(s.Economy.Ledger.Audit().IsConserved); Assert.Equal(seeded, s.Economy.Ledger.Audit().Seeded.Mites);

        // the Journal's Wanted card reads the bounty jobs open on the hero's person
        var wanted = Bounties.OnPerson(s.Jobs, s.Party!.Hero).ToList();
        Assert.Single(wanted); Assert.Equal(580, wanted[0].WageMites);

        // the Chronicle's Why: bounty <- report <- witnessing <- theft, each in the words of en.csv, naming Wynn
        var posted = s.Chronicle.Get(new EntityId(rec.BountyEntry))!;
        var view = ChronicleView.Of(s, town);
        string Line(ChronicleEntry e) => ChronicleReader.Line(e, ChronicleTypes.From(w.Content), view, P6Game.Tr, g => g);
        var chain = new List<ChronicleEntry>(); var cur = posted;
        while (cur != null) { chain.Add(cur); cur = cur.Causes.Count > 0 && cur.Causes[0].EventId is { } id ? s.Chronicle.Get(id) : null; }
        Assert.Equal(["bounty_posted", "crime_reported", "crime_witnessed", "theft"], chain.Select(e => e.TypeId));
        foreach (var e in chain) output.WriteLine($"{e.TypeId}: {Line(e)}");
        Assert.Contains(wynnName, Line(chain[1]));                                                   // "<witness> reported the crime ..."
        Assert.Contains(wynnName, Line(chain[2]));                                                   // "<witness> saw <doer> commit the crime ... (seen by 5)"
        Assert.Contains("(seen by 5)", Line(chain[2]));
        string heroName = view.NameOf(0);                                                           // the hero is person 0: his deeds read with his name, not "someone"
        Assert.False(string.IsNullOrEmpty(heroName)); Assert.Contains(heroName, Line(chain[3]));
        Assert.Contains("A bounty of", Line(chain[0])); Assert.Contains(wynnName, Line(chain[0]));    // "...posted at <place> on the word of <witness>"
        Assert.Contains("stole goods worth", Line(chain[3]));
        // the Why of the bounty is the report, whose Why is the witnessing
        Assert.Equal(chain[1].Id, posted.Causes[0].EventId!.Value); Assert.Equal(chain[2].Id, chain[1].Causes[0].EventId!.Value);

        // the save in the middle (Seen, reported, the bounty open) resumes the same
        var bytes = SaveCodec.Encode(P6Game.SaveAt(rt));
        rt.AdvanceTo(GameTime.FromGameHours(h0 + 25 + 72));
        var want = P6Game.Sections(rt);
        var st2 = WorldSave.Restore(SaveCodec.Decode(bytes), w.Content, P6Game.Hash, null, w.Continent, w.History);
        st2.Content = w.Content;
        using var back = new WorldRuntime(st2, w.Content, new MemoryRegionPager());
        P6Game.AttachGameLinks(back, w.Content);
        back.AdvanceTo(GameTime.FromGameHours(h0 + 25 + 72));
        var got = P6Game.Sections(back);
        Assert.Equal(want.Keys, got.Keys);
        foreach (var k in want.Keys) Assert.True(want[k].AsSpan().SequenceEqual(got[k]), $"section {k} differs after the resume");
        Assert.Equal(s.Chronicle.Hash(), back.State.Chronicle.Hash());
    }
}
