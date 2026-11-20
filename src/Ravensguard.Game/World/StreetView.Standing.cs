using System;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Society;

namespace Ravensguard.Game.World;

/// <summary>
/// The street view's side of standing (P6-A3, reputation 6): the character screen's Standing tab shows a <see cref="StandingView"/> built at a barrier from the standing book (what each group thinks
/// of the hero, renown, marks) and refreshed once a second while the screen is up.
/// </summary>
public partial class StreetView
{
    StandingView? _standingView; Task<StandingView?>? _standingTask;

    void RefreshStanding()
    {
        if (Runtime is not { } rt || rt.State.Party is not { } party || rt.State.Content is not { } content) return;
        if (_standingTask is { IsCompleted: true } t) { if (t.IsCompletedSuccessfully) _standingView = t.Result; _standingTask = null; }
        int day = (int)(HeroNow() / 86400); var hero = party.Hero;
        _standingTask ??= Bar(rt, () => (StandingView?)StandingView.Of(rt.State, DeedTable.From(content), hero, day));
    }

    void OpenStanding()
    {
        if (_router == null || Runtime == null) { _router?.ShowToast(L.T("MAP_NONE")); return; }
        _standingView = null; _standingTask = null;
        _router.Push(new StandingScreen(_router, () => _standingView, RefreshStanding));
    }
}
