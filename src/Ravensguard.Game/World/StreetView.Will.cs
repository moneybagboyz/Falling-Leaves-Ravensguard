using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Succession;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Game.World;

/// <summary>
/// The street view's side of wills and estates (P5-H, inheritance 6): Q beside a priest or the reeve (within ten metres: a will needs a witness) > Make a will opens the will panel over a
/// <see cref="WillView"/> built at a barrier; the will is one recorded command through the hero's desk (the 2-crown fee to the witness), its answer toasted when the barrier applied it.
/// Dev shots (<c>--ng-shot inheritance</c>): the will panel, the succession screen with the estate, the Ledger with the inherited rows.
/// </summary>
public partial class StreetView
{
    WillView? _willView; Task<WillView?>? _willTask; double _willAcc; long _willSeen;

    /// <summary>A priest or the reeve within ten metres (who may witness a will), or null.</summary>
    (EntityId Id, string Name)? WitnessNear()
    {
        foreach (var p in PeopleNear(10).Where(p => WillDesk.Witnesses.Contains(p.Occupation)).OrderBy(p => p.Id.Value)) return (p.Id, p.Name);
        return null;
    }

    void RefreshWill()
    {
        if (Runtime is not { } rt || _heroLink == null || rt.State.Party is not { } party || rt.State.Content is not { } content) return;
        if (_willTask is { IsCompleted: true } t) { if (t.IsCompletedSuccessfully) _willView = t.Result; _willTask = null; }
        int day = (int)(HeroNow() / 86400);
        _willTask ??= Bar(rt, () => (WillView?)WillDesk.View(rt.State, party, content, day));
    }

    /// <summary>Per frame: the will answers toasted; the view kept fresh while the panel is up.</summary>
    void WillTick(double delta)
    {
        _willAcc += delta;
        if (_willAcc >= 2.0 && _router?.Top is WillPanel) { _willAcc = 0; RefreshWill(); }
        if (_heroLink?.View is not { } v) return;
        foreach (var r in v.Results.Where(r => r.Kind == HeroCommandKind.Will && r.Seq > _willSeen)) { _router?.ShowToast(r.Text, 5f); _willSeen = r.Seq; }
    }

    void OpenWill(EntityId witness, string name, Dictionary<EntityId, int>? preset = null, string? shot = null)
    {
        if (_router == null || Runtime == null || _heroLink == null) { _router?.ShowToast(L.T("MAP_NONE")); return; }
        _willView = null; _willTask = null;
        RefreshWill();
        _router.Push(new WillPanel(_router, () => { RefreshWill(); return _willView; }, lines => _heroLink?.Issue(WillDesk.Make(witness, lines, HeroNow())), name) { ShotPath = shot, Preset = preset });
    }

    /// <summary>The Q menu's will verb: beside a priest or the reeve, Make a will; elsewhere the line says who is needed.</summary>
    void WillVerbs(List<QMenu.Verb> v)
    {
        if (Runtime == null || _heroLink == null) return;
        v.Add(new(L.T("in_q_head"), () => { }, Heading: true));
        var w = WitnessNear();
        v.Add(new(w is { } ww ? L.F("in_q_will_with", ww.Name) : L.T("in_q_will"), () => { if (WitnessNear() is { } x) OpenWill(x.Id, x.Name); }, w == null ? L.T("in_find_witness") : null));
    }

    // ------------------------------------------------------------------ dev shots (--ng-shot inheritance --ng-scene will | succession | ledger)

    /// <summary>
    /// Dev: <c>will</c> opens the will panel witnessed by the place's priest (found at the barrier, not walked to), with the first person the hero knows down for everything; <c>succession</c> shows
    /// the succession screen with the estate summary the barrier computes for the first heir; <c>ledger</c> opens the Ledger on the Property tab (the inherited lease and tannery marked).
    /// </summary>
    public void DebugInheritance(string scene, string? shot = null)
    {
        if (Runtime is not { } rt || rt.State.Party is not { } party || rt.State.Content is not { } content) return;
        int day = (int)(HeroNow() / 86400); int s = HeroSettlement();
        switch (scene)
        {
            case "will":
            {
                var (id, name, first) = rt.AtBarrier(() =>
                {
                    var pt = rt.State.Population.People;
                    int best = -1;
                    for (int i = 0; i < pt.Count; i++)
                        if (pt.Status[i] == PersonStatus.Alive && WillDesk.Witnesses.Contains(pt.Occupation[i]) && (best < 0 || (pt.HomeSettlement[i] == s && pt.HomeSettlement[best] != s))) best = i;
                    var view = WillDesk.View(rt.State, party, content, day);
                    return (best >= 0 ? pt.IdOf(best) : EntityId.None, best >= 0 ? pt.FullName(best) : "", view.People.FirstOrDefault()?.Person ?? EntityId.None);
                }).GetAwaiter().GetResult();
                GD.Print($"[will] witness {name}, first beneficiary {first.Value}");
                OpenWill(id, name, first != EntityId.None ? new Dictionary<EntityId, int> { [first] = 1 } : null, shot);
                break;
            }
            case "succession":
            {
                var (heirs, est) = rt.AtBarrier(() =>
                {
                    var h = SuccessionDesk.Candidates(rt.State, party, day);
                    return (h, SuccessionDesk.Estate(rt.State, party, content, h.FirstOrDefault(), day));
                }).GetAwaiter().GetResult();
                GD.Print($"[succession] {heirs.Count} heirs; estate: {est.Lines.Count} lines, coin {est.CoinMites}, debts {est.DebtsMites}");
                DebugShowSuccession(heirs, Run is { Ironman: true }, est);
                break;
            }
            default:
                OpenLedger(Ravensguard.Sim.Production.LedgerTab.Property);
                break;
        }
    }
}
