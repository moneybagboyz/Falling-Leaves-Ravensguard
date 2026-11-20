using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Bondage;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Game.World;

/// <summary>
/// The street view's side of slavery (P6-D1, slavery 6): the Q menu's pen, slaves and bond verbs, the bondage panel over the view the barrier built (<see cref="HeroView.Bondage"/>), the town the hero stands in told to the
/// world (a runaway who reaches a free town is free; the law looks at slaves carried into one), and every act sent as a recorded hero command (<see cref="BondDesk"/>). No state here but which answers have been toasted
/// and which town was last told.
/// </summary>
public partial class StreetView
{
    long _slSeen; int _slTold = -2;

    BondageLink BondageLinkFor() => new()
    {
        View = () => _heroLink?.View?.Bondage,
        Buy = p => _heroLink?.Issue(BondDesk.Buy(new EntityId(p), HeroNow())),
        Sell = p => _heroLink?.Issue(BondDesk.Sell(new EntityId(p), HeroSettlement(), HeroNow())),
        Free = p => _heroLink?.Issue(BondDesk.Free(new EntityId(p), HeroNow())),
        Treat = (p, t) => _heroLink?.Issue(BondDesk.Treat(new EntityId(p), t, HeroNow())),
        Chain = (p, on) => _heroLink?.Issue(BondDesk.Chain(new EntityId(p), on, HeroNow())),
        Work = (p, w) => _heroLink?.Issue(BondDesk.Work(new EntityId(p), w, HeroNow())),
        Arm = (p, on) => _heroLink?.Issue(on ? BondDesk.Arm(new EntityId(p), HeroNow()) : BondDesk.Disarm(new EntityId(p), HeroNow())),
        Escape = () => _heroLink?.Issue(BondDesk.Escape(HeroNow())),
        Run = () => _heroLink?.Issue(BondDesk.Run(HeroNow())),
        SelfBuy = () => _heroLink?.Issue(BondDesk.SelfBuy(HeroNow())),
    };

    void OpenBondage(int tab)
    {
        if (_router == null || _heroLink == null || _router.Top is BondageScreen) return;
        _router.Push(new BondageScreen(_router, BondageLinkFor()) { StartTab = tab });
    }

    /// <summary>Per frame: the answers to bondage commands toasted once; the town he stands in told to the world when he has slaves or is a runaway and the town changed.</summary>
    void BondageTick()
    {
        if (_heroLink?.View is not { } v) return;
        foreach (var r in v.Results.Where(r => r.Kind == HeroCommandKind.Bondage && r.Seq > _slSeen)) { _router?.ShowToast(r.Text, 5f); _slSeen = r.Seq; }
        if (v.Bondage is { } b && (b.Owned.Count > 0 || b.Hero != null))
        {
            int s = HeroSettlement();
            if (s != _slTold && s >= 0) { _slTold = s; _heroLink.Issue(BondDesk.Here(s, HeroNow())); }
        }
        else _slTold = -2;
    }

    /// <summary>The Q menu: the pen of the place he stands in, his slaves, and (enslaved) his own bond.</summary>
    void BondageVerbs(List<QMenu.Verb> v)
    {
        if (_heroLink?.View?.Bondage is not { IsEmpty: false } b) return;
        int s = HeroSettlement();
        bool pen = s >= 0 && b.Pens.Any(p => p.Settlement == s);
        if (!pen && b.Owned.Count == 0 && b.Hero == null) return;
        v.Add(new(L.T("sl_q_head"), () => { }, Heading: true));
        if (pen) v.Add(new(L.T("sl_q_pen"), () => OpenBondage(0)));
        if (b.Owned.Count > 0) v.Add(new(L.F("sl_q_slaves", b.Owned.Count), () => OpenBondage(1)));
        if (b.Hero != null) v.Add(new(L.T("sl_q_bond"), () => OpenBondage(2)));
    }

    // ------------------------------------------------------------------ dev (--ng-shot bondage, smoke)

    /// <summary>Dev: the bondage panel over a made-up pen, a hero with two slaves and (tab 2) a hero in chains.</summary>
    public void DebugBondage(string? shot = null, int tab = 0)
    {
        if (_router == null) return;
        var view = BondageScreen.Demo(tab == 2);
        _router.Push(new BondageScreen(_router, new BondageLink
        {
            View = () => view, Buy = _ => { }, Sell = _ => { }, Free = _ => { }, Treat = (_, _) => { }, Chain = (_, _) => { }, Work = (_, _) => { }, Escape = () => { }, Run = () => { }, SelfBuy = () => { },
        }) { ShotPath = shot, StartTab = tab });
    }
}
