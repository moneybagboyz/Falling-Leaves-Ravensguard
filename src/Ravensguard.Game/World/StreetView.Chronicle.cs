using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Core;

namespace Ravensguard.Game.World;

/// <summary>
/// The street view's side of the Chronicle screen (P6-E4, chronicle-full 4.4): H opens it; its <see cref="ChronicleView"/> is built at a barrier (the region's entries so far, the history they continue, the names of
/// the things they mention, and who counts as mine, here and the realm's) and rebuilt only when the chronicle has grown. The rumour card's History link, the contract board and a defeat's "why" open it at an entry.
/// </summary>
public partial class StreetView
{
    ChronicleView? _chView; Task<ChronicleView?>? _chTask; ChronicleTypes? _chTypes;

    void RefreshChronicle()
    {
        if (Runtime is not { } rt) return;
        if (_chTask is { IsCompleted: true } t) { if (t.IsCompletedSuccessfully && t.Result != null) _chView = t.Result; _chTask = null; }
        int at = HeroSettlement(); var seen = _chView;
        _chTask ??= Bar(rt, () => (ChronicleView?)(seen != null && seen.Entries.Count == rt.State.Chronicle.Count ? seen : ChronicleView.Of(rt.State, at)));
    }

    /// <summary>H: the Chronicle, at the hero's own life (or at <paramref name="focus"/>, an entry: a rumour's source, a contract's posting, the cause of a defeat).</summary>
    public void OpenChronicle(EntityId? focus = null, ChronicleScope scope = ChronicleScope.MyLife, string? shot = null, int follow = 0, bool latestMine = false, bool deepest = false)
    {
        if (_router == null || Runtime is not { } rt || rt.State.Content is not { } content) { _router?.ShowToast(L.T("MAP_NONE")); return; }
        if (_router.Top is ChronicleScreen) return;
        _chTypes ??= ChronicleTypes.From(content);
        _chView = null; _chTask = null;
        RefreshChronicle();
        _router.Push(new ChronicleScreen(_router, () => { RefreshChronicle(); return _chView; }, RefreshChronicle, _chTypes)
        {
            Focus = focus, StartScope = scope, ShotPath = shot, StartFollow = follow, FocusLatestMine = latestMine, FocusDeepest = deepest,
            OpenPlace = place => { _router.Pop(); OpenMap(); },
        });
    }

    /// <summary>The Chronicle at the latest entry that names the hero (a defeat's "why": the fight, then the chain behind it).</summary>
    void OpenChronicleLatest() => OpenChronicle(latestMine: true);

    /// <summary>Dev (smoke, shots): the Chronicle opened on the entry with the longest chain of causes, in the view that holds it.</summary>
    public void DebugChronicle(string? scope = null) => OpenChronicle(scope: scope?.ToLowerInvariant() switch { "mylife" => ChronicleScope.MyLife, "here" => ChronicleScope.Here, "legends" => ChronicleScope.Legends, _ => ChronicleScope.Realm }, deepest: true);
}
