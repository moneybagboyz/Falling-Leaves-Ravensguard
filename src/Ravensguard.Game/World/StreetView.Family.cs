using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Family;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Game.World;

/// <summary>
/// The street view's side of family life (P6-B3, family-and-lifespan 6): the family link on the world's hours (birthdays, the wedding, births, lessons, the kidnappers' season roll), the Family panel
/// opened from the character sheet (a <see cref="FamilyView"/> built at a barrier, refreshed while it is up), the answers to family commands toasted, and the hero's death of old age or in childbed
/// handed to the succession screen. Courting itself is a topic of the talk window (<see cref="CourtOffer"/>). No state here but the views and the link.
/// </summary>
public partial class StreetView
{
    FamilyLink? _familyLink;
    FamilyView? _familyView; Task<FamilyView?>? _familyTask;
    long _familySeen; double _familyAcc; int _familyShot;

    void RefreshFamily()
    {
        if (Runtime is not { } rt || rt.State.Content is not { } content) return;
        if (_familyTask is { IsCompleted: true } t) { if (t.IsCompletedSuccessfully) _familyView = t.Result; _familyTask = null; }
        int day = (int)(HeroNow() / 86400);
        _familyTask ??= Bar(rt, () => FamilyView.Of(rt.State, content, day));
    }

    /// <summary>The offer to court the person being talked to, read at a barrier (null without a world).</summary>
    Task<CourtOffer>? CourtOfferFor(EntityId person)
    {
        if (Runtime is not { } rt || rt.State.Content is not { } content) return null;
        int day = (int)(HeroNow() / 86400);
        return Bar(rt, () => CourtOffer.For(rt.State, content, person, day));
    }

    void OpenFamily(string? shot = null)
    {
        if (_router == null || Runtime == null || _heroLink == null) { _router?.ShowToast(L.T("MAP_NONE")); return; }
        _familyView = null; _familyTask = null;
        RefreshFamily();
        _router.Push(new FamilyPanel(_router, () => _familyView, RefreshFamily,
            (child, course, tutor) => _heroLink?.Issue(FamilyDesk.Teach(child, course, Ravensguard.Sim.People.Skill.None, tutor, HeroNow())),
            who => _heroLink?.Issue(FamilyDesk.Break(who, HeroNow()))) { ShotPath = shot, Arrange = who => _heroLink?.Issue(FamilyDesk.ArrangeBest(who, HeroNow())) });
    }

    /// <summary>Per frame: the family's answers toasted, the panel's view kept fresh while it is up, the dev screenshot.</summary>
    void FamilyTick(double delta)
    {
        _familyAcc += delta;
        if (_familyAcc >= 2.0 && _router?.Top is FamilyPanel) { _familyAcc = 0; RefreshFamily(); }
        if (_heroLink?.View is { } v)
            foreach (var r in v.Results.Where(r => r.Kind == HeroCommandKind.Family && r.Seq > _familySeen)) { _router?.ShowToast(r.Text, 4f); _familySeen = r.Seq; }
        FamilyShots();
    }

    /// <summary>
    /// Dev screenshot <c>--shot-panel family</c>: the hero married with two children and a course under way (made at a barrier by <see cref="FamilyDemo"/>, through the rules play uses), then the panel.
    /// </summary>
    void FamilyShots()
    {
        if (_opt.Panel != "family" || _heroLink == null || Runtime is not { } rt || rt.State.Content is not { } content) return;
        if (_familyShot == 0 && _frames >= 60 && rt.HourDone >= _world.Clock.Now.TotalHours)
        {
            _familyShot = 1;
            int day = (int)(HeroNow() / 86400);
            int spouse = Bar(rt, () => FamilyDemo.MarryWithChildren(rt.State, content, day)).GetAwaiter().GetResult();
            GD.Print($"[family] demo marriage: spouse row {spouse}");
            _familyView = null; _familyTask = null;
            _frames0 = _frames;
        }
        if (_familyShot == 1 && _frames == _frames0 + 20) { _familyShot = 2; OpenFamily(_opt.ShotPath); }
    }

    int _frames0;

    /// <summary>
    /// Dev (the new-game screenshot shell): <c>panel</c> marries the hero with two children and a course under way at a barrier, then opens the Family panel; <c>talk</c> opens the talk window with someone the
    /// hero may court and presses the courting entry; <c>betrothed</c> marries nobody but opens the panel for a hero with no family at all.
    /// </summary>
    public void DebugFamily(string scene, string? shot = null)
    {
        if (Runtime is not { } rt || rt.State.Content is not { } content) return;
        int day = (int)(HeroNow() / 86400);
        switch (scene)
        {
            case "talk":
            {
                var who = rt.AtBarrier(() =>
                {
                    var st = rt.State; var pt = st.Population.People; int home = HeroSettlement();
                    for (int pass = 0; pass < 2; pass++)
                        for (int i = 0; i < pt.Count; i++)
                            if (pt.Status[i] == Ravensguard.Sim.People.PersonStatus.Alive && (pass == 1 || pt.HomeSettlement[i] == home) && CourtOffer.For(st, content, pt.IdOf(i), day).Can) return pt.IdOf(i);
                    return EntityId.None;
                }).GetAwaiter().GetResult();
                GD.Print($"[family] courting talk with person {who.Value}");
                OpenTalk(who, shot, shot != null ? [-1] : null);
                break;
            }
            case "empty": OpenFamily(); break;
            default:
            {
                int spouse = Bar(rt, () => FamilyDemo.MarryWithChildren(rt.State, content, day)).GetAwaiter().GetResult();
                GD.Print($"[family] demo marriage: spouse row {spouse}");
                OpenFamily();
                break;
            }
        }
    }
}
