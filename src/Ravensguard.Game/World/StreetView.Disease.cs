using System.Linq;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Disease;
using Ravensguard.Sim.Trade;


namespace Ravensguard.Game.World;

/// <summary>
/// The street view's side of sickness (P6-C4, disease-and-plague 6): the answers to the hero's nursing and physician commands toasted, and the care callback the character screen's illness rows use. The
/// rows themselves are a <see cref="HeroView.Ill"/> built at a barrier; every act is a <see cref="HeroCommandKind.Disease"/> command through the hero's desk (recorded for replay).
/// </summary>
public partial class StreetView
{
    long _dzSeen;

    /// <summary>The character screen's Nurse and Physician buttons: a party member sits with the sick one, or a physician is asked to call every day.</summary>
    void CareFor(ulong person, bool physician)
    {
        if (_heroLink == null) return;
        var id = new EntityId(person);
        _heroLink.Issue(physician ? DiseaseDesk.CallPhysician(HeroNow(), id) : DiseaseDesk.Nurse(HeroNow(), id));
    }

    /// <summary>Per frame: the answers to sickness commands, toasted once.</summary>
    void DiseaseTick()
    {
        if (_heroLink?.View is not { } v) return;
        foreach (var r in v.Results.Where(r => r.Kind == HeroCommandKind.Disease && r.Seq > _dzSeen)) { _router?.ShowToast(r.Text, 5f); _dzSeen = r.Seq; }
    }

    // ------------------------------------------------------------------ dev shot (--ng-shot disease)

    /// <summary>Dev: the hero falls ill of the ague (a party member sits with him), the plague is in the region and the wage term is up; the caller opens the Character screen once a barrier has built the hero's view.</summary>
    public void DebugDisease()
    {
        if (Runtime is not { } rt || _heroLink == null || rt.State.Party is not { } party || rt.State.Disease is not { } book || rt.State.HomeRecords == null) { _router?.ShowToast(L.T("MAP_NONE")); return; }
        long now = HeroNow();
        rt.AtBarrier(() =>
        {
            var sim = rt.State.HomeRecords!.Sim; var pt = rt.State.Population.People;
            int row = Ravensguard.Sim.Succession.HeroPerson.RowOf(pt, rt.State.Hero, party.Hero); if (row < 0) return 0;
            int day = (int)(now / 86400); ulong id = pt.IdOf(row).Value;
            DiseaseEngine.Begin(sim, book, id, row, IllnessKind.Ague, "ch_note_ill_marsh", day - 2, new GameTime(now), new Ravensguard.Sim.Abstract.RegionOutbox(rt.State.Ids));
            DiseaseEngine.Nurse(book, id, day);
            book.Plague = new GreatPlague { Id = 1, NameIndex = 0, StartDay = day - 20, Dead = 212 };
            book.Scarcity.WageMultPm = 1350; book.Scarcity.LandMultPm = 700;
            return 0;
        }).GetAwaiter().GetResult();
        _ = _heroLink.Kick();                                           // a fresh hero view at the barrier (the clock is paused)
    }
}
