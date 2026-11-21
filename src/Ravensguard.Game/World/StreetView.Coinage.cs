using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Game.World;

/// <summary>
/// The street view's side of coinage (P7-CN2, coinage 9): Q in a town offers "Sell metal at the mint" and "Change money", each opening <see cref="CoinPanel"/> over a <see cref="CoinView"/> built at a barrier.
/// Every button is one <c>cn:</c> command through the hero's desk (<see cref="CoinDesk"/>: the sim checks the mint's hours, the master in office, the changer's gold and the fee); its answer is toasted once.
/// Dev shots (<c>--ng-scene mint | changer</c>): the panels over a made-up view, the widest case they have.
/// </summary>
public partial class StreetView
{
    CoinView? _coinView; Task<CoinView?>? _coinTask; double _coinAcc; long _coinSeen; int _coinTown = -1;

    void RefreshCoin()
    {
        if (Runtime is not { } rt || _heroLink == null || rt.State.Party is not { } party || rt.State.Content is not { } content || _coinTown < 0) return;
        if (_coinTask is { IsCompleted: true } t) { if (t.IsCompletedSuccessfully) _coinView = t.Result; _coinTask = null; }
        int town = _coinTown; int hour = (int)(HeroNow() / 3600 % 24);
        _coinTask ??= Bar(rt, () => (CoinView?)CoinView.Build(rt.State, content, party, town, hour));
    }

    /// <summary>Per frame: the coin answers toasted; the view kept fresh while a coin panel is up.</summary>
    void CoinTick(double delta)
    {
        _coinAcc += delta;
        if (_coinAcc >= 1.0 && _router?.Top is CoinPanel) { _coinAcc = 0; RefreshCoin(); }
        if (_heroLink?.View is not { } v) return;
        foreach (var r in v.Results.Where(r => r.Kind == HeroCommandKind.Coin && r.Seq > _coinSeen)) { _router?.ShowToast(r.Text, 5f); _coinSeen = r.Seq; }
    }

    void OpenCoin(CoinCounter kind)
    {
        int town = HeroSettlement();
        if (_router == null || Runtime == null || _heroLink == null || town < 0) { _router?.ShowToast(L.T("MAP_NONE")); return; }
        _coinTown = town; _coinView = null; _coinTask = null; RefreshCoin();
        _router.Push(new CoinPanel(_router, kind, () => { RefreshCoin(); return _coinView; },
            (metal, grams) => { _heroLink?.Issue(CoinDesk.Sell(metal, grams, town, HeroNow())); _coinAcc = 1.0; },
            (toGold, n) => { _heroLink?.Issue(toGold ? CoinDesk.ChangeToGold(n, town, HeroNow()) : CoinDesk.ChangeToSilver(n, town, HeroNow())); _coinAcc = 1.0; }));
    }

    /// <summary>The Q menu's coin verbs: in a town, sell metal at the mint and change money (the panel says when the town has neither).</summary>
    void CoinVerbs(List<QMenu.Verb> v)
    {
        if (Runtime == null || _heroLink == null || HeroSettlement() < 0) return;
        v.Add(new(L.T("co_q_head"), () => { }, Heading: true));
        v.Add(new(L.T("co_q_mint"), () => OpenCoin(CoinCounter.Mint)));
        v.Add(new(L.T("co_q_changer"), () => OpenCoin(CoinCounter.Changer)));
    }

    /// <summary>Dev: opens a coin panel over a made-up view with every row live (for the smoke's fit check and the shots).</summary>
    public void DebugCoin(string which)
    {
        if (_router == null) return;
        var view = new CoinView(0, 10, true, true, true, true, 20, 123_456, 7, 1_100, 2_600, 5_000, 5_238_000, 711_500, 1_200, 40_000, 55);
        _router.Push(new CoinPanel(_router, which == "changer" ? CoinCounter.Changer : CoinCounter.Mint, () => view, (_, _) => { }, (_, _) => { }));
    }
}
