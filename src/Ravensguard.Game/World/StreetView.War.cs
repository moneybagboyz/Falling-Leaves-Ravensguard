using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ravensguard.Content;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Soldiers;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.WarSupply;

namespace Ravensguard.Game.World;

/// <summary>
/// The street view's side of the Armoury screen (P8-WS-1): the view is built at a barrier from the soldiers' book and the armoury's stock, and every verb (F, G, T, Enter) is one <c>so:</c> command through the hero desk. The Army
/// tab and the Fief screen's war rows come with the Ledger's feed (<c>RefreshBusiness</c>). Nothing here keeps a game state beyond the last view built.
/// </summary>
public partial class StreetView
{
    ArmouryView? _armouryView; long _armouryOf = long.MinValue; Task<ArmouryView?>? _armouryTask; long _armouryAsked = long.MinValue;
    IReadOnlyList<long> _armouryKeys = []; Task<List<long>>? _armouryKeysTask;

    /// <summary>What the helm HUD draws (null: the hero is not at a helm). The ship's helm sets it from the frame each frame; nothing else holds it.</summary>
    Ravensguard.Sim.Naval.HelmView? _helmView;
    public void SetHelm(Ravensguard.Sim.Naval.HelmView? v) => _helmView = v;

    /// <summary>Dev: the helm HUD over a made-up view (no live ship has a helm yet).</summary>
    public void DebugHelm() => _helmView = new Ravensguard.Sim.Naval.HelmView(250, 9, 80, 3600, 60, 1100, Ravensguard.Sim.Naval.HelmView.SeaKeyOf(1100), 140, [Ravensguard.Sim.Naval.CrewOrder.Row, Ravensguard.Sim.Naval.CrewOrder.ReadyGrapples]);

    ArmouryView? ArmouryData(long key)
    {
        if (Runtime is not { } rt) return null;
        if (_armouryTask is { IsCompleted: true } t) { if (t.IsCompletedSuccessfully) { _armouryView = t.Result; _armouryOf = _armouryAsked; } _armouryTask = null; }
        if (_armouryKeysTask is { IsCompleted: true } kt) { if (kt.IsCompletedSuccessfully) _armouryKeys = kt.Result; _armouryKeysTask = null; }
        _armouryKeysTask ??= Bar(rt, () => WarScreens.ArmouryKeys(rt.State));
        if (key >= 0 && _armouryTask == null) { _armouryAsked = key; _armouryTask = Bar(rt, () => rt.State.Content is { } c ? WarScreens.Armoury(rt.State, c, key) : null); }
        return _armouryOf == key ? _armouryView : null;
    }

    /// <summary>End (or Enter on the Army tab's retinue row): the Armoury screen over the hero's soldiers.</summary>
    void OpenArmoury()
    {
        if (_router == null || Runtime == null || _heroLink == null) { _router?.ShowToast(L.T("MAP_NONE")); return; }
        _router.Push(new ArmouryScreen(_router, ArmouryData, () => { ArmouryData(-1); return _armouryKeys; }, ArmouryAct()) { ShotPath = _opt.Panel == "armoury" ? _opt.ShotPath : null });
    }

    ArmouryActions ArmouryAct() => new()
    {
        FillAll = a => IssueBiz(SoldierDesk.Fill(a, HeroNow())),
        FillGaps = a => IssueBiz(SoldierDesk.FillGaps(a, HeroNow())),
        FillOne = (p, a) => IssueBiz(SoldierDesk.FillOne(p, a, HeroNow())),
        SetTemplate = (p, role, tpl) => IssueBiz(SoldierDesk.SetRole(p, role, tpl, HeroNow())),
        Templates = () => Runtime?.State.Content is { } c ? [.. c.All<LoadoutTemplateDef>().Select(t => t.Id).OrderBy(x => x, StringComparer.Ordinal)] : [],
    };
}
