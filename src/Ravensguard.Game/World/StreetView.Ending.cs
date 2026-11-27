using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Game.UI;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Endings;
using Ravensguard.Sim.Live;

namespace Ravensguard.Game.World;

/// <summary>
/// [endings] The street view's side of an ending (P8-EN-1, endings spec 3.1, 3.2). A great ambition's done offers "End here" or "Carry on": the sim raises <c>EndingOffered</c> (always a pause, on autopilot too) and
/// leaves <see cref="Ravensguard.Sim.Autopilot.AutopilotWorld.PendingEnding"/> for this view to ask. "Carry on" keeps the offer in the Journal; "End here" takes the ending at a barrier (the epilogue, the hero handed to
/// the NPC AI), writes the run's last save (marked ended in the library) and shows the epilogue over the main menu. No state here but which question is up and which job is running.
/// </summary>
public partial class StreetView
{
    string[] _endingOffers = [];
    string? _endingAsked;
    bool _endingTaking;
    Task<EndingDone?>? _endingTake;
    Task<string>? _endingSave;
    EndingDone? _endingDone;

    sealed record EndingDone(string EndingId, int Day, ChronicleView View);

    /// <summary>The offers still open (the Journal lists them, each with "End here").</summary>
    IReadOnlyList<string> EndingOffersNow() => _endingOffers;

    void EndingTick()
    {
        if (_router == null || Runtime is not { } rt) return;
        if (_endingTake is { IsCompleted: true } take)
        {
            _endingTake = null;
            if (take.IsCompletedSuccessfully && take.Result is { } done && Run is { } run)
            {
                _endingDone = done;
                _endingSave = run.SnapshotAsync(_world).ContinueWith(c => run.WriteAsync(c.GetAwaiter().GetResult(), SaveKind.Manual), TaskScheduler.Default).Unwrap();
            }
            else { _endingTaking = false; _router.ShowToast(L.T("ep_ended_readonly")); }
            return;
        }
        if (_endingSave is { IsCompleted: true } save && _endingDone is { } d)
        {
            _endingSave = null; _endingDone = null;
            if (save.IsFaulted || save.IsCanceled) { GD.PushWarning("ending save failed: " + save.Exception?.GetBaseException().Message); _router.ShowToast(L.F("PAUSE_SAVE_FAILED", save.Exception?.GetBaseException().Message ?? "")); }
            else Run?.MarkEnded(d.EndingId, d.Day);
            var content = rt.State.Content;
            _leaving = true;
            _router.ReturnToMainMenu();
            if (content != null) EpilogueScreen.Open(_router, rt.State, content, d.View);
            return;
        }
        if (_endingTaking || _leaving || _ap?.PendingEnding is not { } id || _endingAsked == id || (_router.HasModal)) return;
        _endingAsked = id;
        _rec.SetSpeed(SimSpeed.Paused);
        _router.ShowModal(L.T("ep_offer_title"), L.F("ep_offer_body", L.T("ep_name_" + id)), CarryOn,
            (L.T("ep_offer_end"), () => TakeEnding(id)), (L.T("ep_offer_carry"), null));
    }

    /// <summary>"Carry on" (or Esc): the question is closed, the offer stays in the Journal.</summary>
    void CarryOn()
    {
        if (_ap is not { } ap || Runtime is not { } rt) return;
        _ = Bar(rt, () => { ap.CarryOn(); return 0; });
    }

    /// <summary>"End here": takes the ending at the next barrier; the last save and the epilogue follow when it is done.</summary>
    void TakeEnding(string id)
    {
        if (_endingTaking || Run == null || Runtime is not { } rt) return;
        _endingTaking = true; _rec.SetSpeed(SimSpeed.Paused);
        long now = _world.Clock.Now.Seconds;
        _endingTake = Bar(rt, () =>
        {
            var book = EndingDesk.Take(rt.State, id, now);
            return book == null ? null : (EndingDone?)new EndingDone(id, book.Day, ChronicleView.Of(rt.State, -1));
        });
    }
}
