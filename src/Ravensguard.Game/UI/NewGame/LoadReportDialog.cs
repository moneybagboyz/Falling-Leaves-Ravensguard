using Godot;
using Ravensguard.Sim.Save;

namespace Ravensguard.Game.UI.NewGame;

/// <summary>The pre-load report of a save or a library world made with other content (plan 2.11): what changes, then Continue or Cancel. Widgets only; the report is the sim's.</summary>
public static class LoadReportDialog
{
    /// <summary>Asks the player. Exactly one of <paramref name="onContinue"/> and <paramref name="onCancel"/> runs (Esc cancels).</summary>
    public static void Ask(ScreenRouter router, LoadReport report, bool world, System.Action onContinue, System.Action onCancel)
    {
        bool chosen = false;
        void Once(System.Action a) { if (chosen) return; chosen = true; a(); }
        string body = L.T("LOADREPORT_INTRO") + "\n\n" + report.ToText();
        router.ShowReportModal(L.T(world ? "LOADREPORT_WORLD_TITLE" : "LOADREPORT_TITLE"), body,
            // Esc or any other close: the pressed button's own callback runs right after this one, so the answer is taken one frame later
            () => Callable.From(() => Once(onCancel)).CallDeferred(),
            (L.T("LOADREPORT_CONTINUE"), () => Once(onContinue)), (L.T("LOADREPORT_CANCEL"), () => Once(onCancel)));
    }
}
