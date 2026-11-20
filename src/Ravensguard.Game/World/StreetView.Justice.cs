using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Law;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Game.World;

/// <summary>
/// The street view's side of the courts (P6-C1, crime-and-justice 4.5, 4.7, 6): the cell's view (charges, the sentence to expect, the ways out) for the captivity screen, built at a barrier while the hero is held;
/// the hero's pleas and offers as recorded commands (<see cref="JusticeDesk"/>); and, for a lord, the Court panel over the docket the barrier built. Q beside a guard: give yourself up; Q in a lord's seat with
/// cases waiting: hold court. No state here but the last views and which screen is up.
/// </summary>
public partial class StreetView
{
    CellView? _cell; Task<CellView?>? _cellTask; double _cellAcc;
    CourtView? _court; Task<CourtView?>? _courtTask; double _courtAcc; long _justiceSeen; int _docketToast = -1;

    CellView? BuildCell(Ravensguard.Sim.Save.WorldState st, EntityId hero, int day)
    {
        if (st.Law == null || st.Justice == null || st.Content == null || !st.Justice.Gaol.ContainsKey(hero.Value)) return null;
        return JusticeView.Cell(LawWorld.From(st, st.Content), st.Law, st.Justice, hero, day);
    }

    CourtView? BuildCourt(Ravensguard.Sim.Save.WorldState st, int day)
    {
        if (st.Law == null || st.Justice == null || st.Content == null || st.Justice.Docket.Count == 0) return null;
        return JusticeView.Docket(LawWorld.From(st, st.Content), st.Law, st.Justice, day);
    }

    void RefreshCell()
    {
        if (Runtime is not { } rt || rt.State.Party is not { } party) return;
        if (_cellTask is { IsCompleted: true } t) { if (t.IsCompletedSuccessfully) _cell = t.Result; _cellTask = null; }
        int day = (int)(HeroNow() / 86400); var hero = party.Hero;
        _cellTask ??= Bar(rt, () => BuildCell(rt.State, hero, day));
    }

    void RefreshCourt()
    {
        if (Runtime is not { } rt) return;
        if (_courtTask is { IsCompleted: true } t) { if (t.IsCompletedSuccessfully) _court = t.Result; _courtTask = null; }
        int day = (int)(HeroNow() / 86400);
        _courtTask ??= Bar(rt, () => BuildCourt(rt.State, day));
    }

    /// <summary>The hero's plea or offer from the cell: guilty or clergy for every charge, restitution on each, an offer of the gaoler's least price.</summary>
    void PleaAct(string what)
    {
        if (_heroLink == null || _cell is not { } c) return;
        long now = HeroNow();
        switch (what)
        {
            case "guilty": foreach (var ch in c.Charges) _heroLink.Issue(JusticeDesk.Plead(ch.Crime, false, now)); break;
            case "clergy": foreach (var ch in c.Charges) _heroLink.Issue(JusticeDesk.Plead(ch.Crime, true, now)); break;
            case "restitute": foreach (var ch in c.Charges) _heroLink.Issue(JusticeDesk.Restitute(ch.Crime, now)); break;
            case "bribe": if (c.WaysOut.FirstOrDefault(w => w.Key == "jl_way_bribe") is { Available: true } w) _heroLink.Issue(JusticeDesk.Bribe(w.Mites, now)); break;
        }
        _cellAcc = 99;                                                                  // look again as soon as the barrier has answered
        _rec.SetWantedSpeed(SimSpeed.X8); _rec.SetSpeed(SimSpeed.X8);
    }

    void OpenCourt()
    {
        if (_router == null || _heroLink == null) return;
        if (_router.Top is CourtScreen) return;
        RefreshCourt();
        _router.Push(new CourtScreen(_router, () => _court,
            (crime, key) => { _heroLink.Issue(JusticeDesk.Judge(crime, key, HeroNow())); _courtAcc = 99; },
            crime => { _heroLink.Issue(JusticeDesk.Acquit(crime, HeroNow())); _courtAcc = 99; },
            () => _heroLink.Issue(JusticeDesk.Adjourn(HeroNow()))));
    }

    /// <summary>Per frame: the answers to justice commands toasted, the cell's view kept fresh while the captivity screen is up, the docket noticed (and kept fresh while the Court panel is up).</summary>
    void JusticeTick(double delta)
    {
        if (Runtime == null) return;
        _cellAcc += delta; _courtAcc += delta;
        if (_heroLink?.View is { } v)
            foreach (var r in v.Results.Where(r => r.Kind == HeroCommandKind.Justice && r.Seq > _justiceSeen)) { _router?.ShowToast(r.Text, 5f); _justiceSeen = r.Seq; _cellAcc = 99; _courtAcc = 99; }
        if (_captivityView != null && _cellAcc >= 1.5) { _cellAcc = 0; RefreshCell(); }
        else if (_captivityView == null && _cell != null && _cellAcc >= 3) { _cell = null; _cellTask = null; }
        bool courtUp = _router?.Top is CourtScreen;
        if (Runtime.State.Justice is { Docket.Count: > 0 } jd)
        {
            if (_courtAcc >= (courtUp ? 1.0 : 5.0)) { _courtAcc = 0; RefreshCourt(); }
            if (_docketToast != jd.DocketDay && jd.DocketDay >= 0) { _docketToast = jd.DocketDay; _router?.ShowToast(L.F("jl_docket_toast", jd.Docket.Count), 6f); }
        }
        else if (_court != null && _courtAcc >= 5) { _court = null; _courtTask = null; }
    }

    /// <summary>The Q menu's justice verbs: beside a guard, give yourself up (when charged); with cases on the docket, hold court.</summary>
    void JusticeVerbs(List<QMenu.Verb> v)
    {
        if (Runtime?.State is not { } st || _heroLink == null || st.Party is not { } party) return;
        bool charged = st.Law?.Open(party.Hero).Any(c => c.State is CrimeState.Seen or CrimeState.Reported or CrimeState.Accused or CrimeState.Fled) == true;
        bool docket = st.Justice is { Docket.Count: > 0 };
        if (!charged && !docket) return;
        v.Add(new(L.T("jl_q_head"), () => { }, Heading: true));
        if (charged)
        {
            bool guard = PeopleNear(10).Any(p => p.Occupation == Occupation.Guard);
            v.Add(new(L.T("jl_q_surrender"), () => { _heroLink.Issue(JusticeDesk.Surrender(HeroSettlement(), HeroNow())); _rec.SetWantedSpeed(SimSpeed.X8); _rec.SetSpeed(SimSpeed.X8); }, guard ? null : L.T("jl_find_guard")));
        }
        if (docket) v.Add(new(L.T("jl_q_court"), OpenCourt));
    }

    // ------------------------------------------------------------------ dev (--ng-shot court, smoke)

    /// <summary>Dev: the Court panel over a made-up docket (a lord with three cases), or the gaol cell of a held hero.</summary>
    public void DebugCourt(string? shot = null)
    {
        if (_router == null) return;
        var view = CourtScreen.Demo();
        _router.Push(new CourtScreen(_router, () => view, (_, _) => { }, _ => { }, () => { }) { ShotPath = shot });
    }
}
