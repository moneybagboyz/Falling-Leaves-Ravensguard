using System.Linq;
using Godot;
using Ravensguard.Content;
using Ravensguard.Game.UI;
using Ravensguard.Game.World.Combat;
using Ravensguard.Sim.Combat.Flavour;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Travel;

namespace Ravensguard.Game.World;

/// <summary>
/// The street view's side of the combat log and the after-fight report (feedback-flavour-log 4.2, 4.3, 7): slow motion and hit-stop scale how much real time a fight step is given, a fight that
/// ends closes its log, writes the chronicle entries of its named dead and opens the report (or a toast with a link), F7 and Shift+F7 reopen the log and the last report, and the log of the
/// fight in progress or the last one is saved in the optional "fightlog" section. Nothing here is read by the sim.
/// </summary>
public partial class StreetView
{
    FightLog? _lastFightLog;
    FlavourBook? _lastBook;
    AfterFightPanel? _reportScreen;

    /// <summary>Real seconds to a fight step: 1, slowed in a slow-motion moment, 0 in a hit-stop (the fight's step stays 20 Hz: the number of steps per real second changes).</summary>
    double FightTimeScale() => _fightView?.Feedback is { } f ? f.TimeScale : 1.0;

    /// <summary>Names what began the fight (the last link of a cause chain) and keeps the log for the save.</summary>
    void BeginFightLog(EncounterDef def, EncounterEvent ev)
    {
        if (_fightView == null) return;
        var log = _fightView.Log;
        log.FightId = (ulong)ev.Sec;
        if (_hero != null && _fightView.Hero >= 0) { var me = log.Info(_fightView.Hero); me.Name = _hero.Name; me.Female = _hero.Female; }       // the log says who you are
        if (def.Kind == "Ambush") { log.TriggerKey = "fl_trigger_ambush"; log.TriggerArg = L.T("fl_trigger_strangers"); }
        else { log.TriggerKey = "fl_trigger_encounter"; log.TriggerArg = def.Name.ToLowerInvariant(); }
        _lastFightLog = log; _lastBook = _fightView.Book;
        if (Runtime is { } rt) rt.State.FightLogSource = () =>
        {
            if (_lastFightLog is not { } l || _lastBook is not { } b) return null;
            // the save copies on the tick worker while the live step appends lines under the ledger's lock (review finding 1)
            if (rt.State.Wounds is { } wl) lock (wl.Sync) return FightLogSave.Capture(l, b);
            return FightLogSave.Capture(l, b);
        };
    }

    /// <summary>The road fight is over: the log is closed, the named dead are written to the chronicle, and the report opens when you were hurt or three fell (else a toast says where to find it).</summary>
    void FightEnded(FightWorld f)
    {
        if (_fightView == null) return;
        _fightView.FinishLog();
        var log = _fightView.Log; var book = _fightView.Book;
        _lastFightLog = log; _lastBook = book;
        if (Runtime is { } rt)
        {
            // composed here, numbered and added at the barrier: the world's chronicle and id allocator belong to the tick worker (review, game finding 1)
            var made = FightChronicle.Write(log, new Ravensguard.Sim.Core.Chronicle(), new Ravensguard.Sim.Core.IdAllocator(), book, GameText.Instance, f.Watch is { EverOn: true } ws ? ws.Stance.Id : null);   // P4-F: "(watched)"
            if (made.Count > 0) _ = Bar(rt, () => { foreach (var e in made) rt.State.Chronicle.Add(e with { Id = rt.State.Ids.Allocate(Ravensguard.Sim.Core.IdKind.Event) }); return made.Count; });
        }
        var hero = _fightView.HeroF;
        var rep = AfterFightReport.Build(log, book, GameText.Instance);
        bool hurt = hero != null && hero.Book.Records.Count > 0;
        if (hurt || rep.Casualties >= 3) OpenReportFor(rep, f.Ground.Count, f.SpentAmmo.Values.Sum() / 2);
        else _router?.ShowToast(L.T("fl_ui_toast_report"), 5f);
    }

    void OpenReportFor(AfterFightReport rep, int ground, int arrows)
    {
        if (_router == null || _reportScreen != null && IsInstanceValid(_reportScreen)) return;
        var panel = new AfterFightPanel(rep, ground, arrows) { Bleeding = HeroBleedingWounds(), Treat = () => Callable.From(OpenTreatmentSelf).CallDeferred(), Why = () => Callable.From(OpenChronicleLatest).CallDeferred() };    // P6-B1: the call to action opens the treatment screen
        panel.Closed = () => { _router.Pop(); _reportScreen = null; };
        _reportScreen = panel;
        _router.Push(panel);
    }

    /// <summary>The last log: this session's, or the one the loaded save carried.</summary>
    bool LastLog(out FightLog log, out FlavourBook book)
    {
        log = null!; book = null!;
        var db = _source?.Content;
        if (_lastFightLog == null && Runtime?.State.FightLog is { } saved && db != null) { _lastFightLog = saved.Restore(); _lastBook = new FlavourBook(db); }
        if (_lastFightLog == null || _lastBook == null) return false;
        log = _lastFightLog; book = _lastBook;
        return true;
    }

    /// <summary>Shift+F7 outside a fight: the report of the last fight, or a toast that there is none.</summary>
    void OpenLastReport()
    {
        if (!LastLog(out var log, out var book)) { _router?.ShowToast(L.T("fl_ui_report_none")); return; }
        OpenReportFor(AfterFightReport.Build(log, book, GameText.Instance), 0, 0);
    }

    /// <summary>F7 outside a fight: the last fight's log as a screen.</summary>
    void OpenCombatLog()
    {
        if (_router == null) return;
        if (!LastLog(out var log, out var book)) { _router.ShowToast(L.T("fl_ui_report_none")); return; }
        FightLogPanel? panel = null;
        panel = new FightLogPanel(() => log, book, asScreen: true) { Closed = () => _router.Pop() };
        _router.Push(panel);
    }
}
