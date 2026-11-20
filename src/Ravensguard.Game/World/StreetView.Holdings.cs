using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Holdings;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Game.World;

/// <summary>
/// The street view's side of holdings (P6-B6, holdings-and-works-v2 6): the Q menu's Clear an acre, fell and grub by hand (every half game minute the hero's attended time goes to the acre, as build mode's
/// hands-on work does), hire two hands, Sow, Take the logs and Whose is this?, and the answers toasted. The menu reads a <see cref="HoldingsView"/> built at a barrier; every action is a holdings command
/// through the hero's desk (recorded for replay).
/// </summary>
public partial class StreetView
{
    HoldingsView? _hwView;
    Task<HoldingsView?>? _hwTask;
    double _hwAcc;
    long _hwSeen;
    bool _fell; int _fellSerial; long _fellFrom, _fellAt;

    static HoldingsWorld? HoldingsOf(WorldRuntime rt) => rt.State.HomeRecords?.Sim.Get<HoldingsWorld>();

    /// <summary>Asks the barrier for a fresh view (one at a time); the last one stays used meanwhile.</summary>
    void RefreshHoldings()
    {
        if (Runtime is not { } rt || _heroLink == null) return;
        if (_hwTask is { IsCompleted: true } t) { if (t.IsCompletedSuccessfully) _hwView = t.Result; _hwTask = null; }
        if (_hwTask != null) return;
        var (x, y) = HeroRegionMm(); int day = (int)(HeroNow() / 86400); var hero = HeroId;
        _hwTask = Bar(rt, () => HoldingsOf(rt) is { } hw && LandOf(rt) is { } lw ? HoldingsDesk.View(hw, lw, hero, (int)(x / 1000), (int)(y / 1000), day) : null);
    }

    /// <summary>Dev: opens the Q menu (the homestead shot).</summary>
    public void DebugQMenu() => OpenQMenu();

    /// <summary>Per frame: the view is kept fresh, the hero's hands-on felling is credited, and the answers to holdings commands are toasted.</summary>
    void HoldingsTick(double delta)
    {
        _hwAcc += delta;
        if (_hwAcc >= 2.0 || _hwTask is { IsCompleted: true }) { _hwAcc = 0; RefreshHoldings(); }
        if (_heroLink?.View is not { } v) return;
        foreach (var r in v.Results.Where(r => r.Kind == HeroCommandKind.Holdings && r.Seq > _hwSeen))
        {
            _router?.ShowToast(r.Text, 4f); _hwSeen = r.Seq;
            if (!r.Ok) _fell = false;                                                     // tired, hungry, too far: the hands-on work stops with its reason
        }
        FellTick();
    }

    /// <summary>While "fell and grub" is on, every half game minute the hero's attended time goes to the acre (a recorded <c>hw:work</c> command: the sim checks the cap, the meal and the acre).</summary>
    void FellTick()
    {
        if (!_fell || _heroLink == null) return;
        long now = HeroNow();
        if (now - _fellAt < 30) return;
        var row = _hwView?.Clearings.FirstOrDefault(c => c.Serial == _fellSerial);
        var (x, y) = HeroRegionMm();
        if (row == null || row.State != ClearingState.Working) { _fell = false; return; }
        long dx = x / 1000 - row.XM, dy = y / 1000 - row.YM;
        if (dx * dx + dy * dy > 200L * 200) { _fell = false; _router?.ShowToast(L.T("hw_q_not_here"), 3f); return; }
        _heroLink.Issue(HoldingsDesk.Work(_fellSerial, Math.Max(_fellFrom, now - 600), now, now));
        _fellFrom = now; _fellAt = now;
    }

    void IssueHoldings(HeroCommand c)
    {
        if (_heroLink == null) return;
        _heroLink.Issue(c);
        _hwAcc = 2.0;
    }

    /// <summary>The Q menu's holdings verbs (P6-B6): whose is this, clear an acre of the claim, the acre's work, sowing, the logs, coppice.</summary>
    void HoldingsVerbs(List<QMenu.Verb> v)
    {
        if (Runtime == null || _heroLink == null) return;
        var hv = _hwView;
        v.Add(new(L.T("hw_q_head"), () => { }, Heading: true));
        var (x, y) = HeroRegionMm(); int xm = (int)(x / 1000), ym = (int)(y / 1000);
        v.Add(new(L.T("hw_q_whose"), () => IssueHoldings(HoldingsDesk.Whose(xm, ym, HeroNow()))));
        string? clearWhy = hv == null ? L.T("ld_waiting") : hv.ClaimHere < 0 ? L.T("hw_q_no_claim") : hv.ClearableTile < 0 ? L.T("hw_q_not_here") : null;
        v.Add(new(L.T("hw_q_clear"), () => { if (hv != null) IssueHoldings(HoldingsDesk.Clear(hv.ClaimHere, hv.ClearableTile, HeroNow())); }, clearWhy));
        if (hv == null) return;
        foreach (var c in hv.Clearings)
        {
            if (c.State == ClearingState.Working)
            {
                int serial = c.Serial;
                v.Add(new(L.F("hw_q_clear_status", c.Percent, c.Felled, c.Trees), () => { }, Heading: true));
                v.Add(new(_fell && _fellSerial == serial ? L.T("hw_q_work_stop") : L.T("hw_q_work"), () => { _fell = !(_fell && _fellSerial == serial); _fellSerial = serial; _fellFrom = HeroNow(); _fellAt = _fellFrom - 30; }));
                v.Add(new(L.T("hw_q_hire"), () => IssueHoldings(HoldingsDesk.Hire(serial, 2, HeroNow())), c.Hands >= HoldingsWorld.MaxHands ? L.F("hw_crew_full", HoldingsWorld.MaxHands) : null));
            }
            else if (c.State == ClearingState.Cleared)
            {
                int serial = c.Serial;
                v.Add(new(L.T("hw_q_sow"), () => IssueHoldings(HoldingsDesk.Sow(serial, HeroNow())), hv.SowingDay ? null : L.T("hw_q_sow_wait")));
            }
            if (c.LogsG + c.FirewoodG > 0) { int serial = c.Serial; v.Add(new(L.T("hw_q_collect"), () => IssueHoldings(HoldingsDesk.Collect(serial, HeroNow())))); }
        }
    }

    // ------------------------------------------------------------------ dev: --ng-shot homestead

    /// <summary>
    /// Dev (the screenshot driver only): a claim staked beside the hero (a banner from the dev faucet), an acre cleared and sown through the sim's own calls, and the hero set down at its edge: the acre's
    /// field stands where the trees were. Returns what happened, for the log.
    /// </summary>
    public string DebugHomestead()
    {
        if (Runtime is not { } rt) return "no runtime";
        var (hx, hy) = HeroRegionMm();
        var res = rt.AtBarrier(() =>
        {
            var hw = HoldingsOf(rt); var lw = LandOf(rt); var st = rt.State;
            if (hw == null || lw == null || st.Party == null) return ((int, int)?)null;
            int N = Ravensguard.Sim.WorldGen.Region.Region.Size, T = Ravensguard.Sim.WorldGen.Region.Region.TileM;
            int cx = (int)(hx / 1000) / T, cy = (int)(hy / 1000) / T;
            for (int r = 3; r < 60; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx += (Math.Abs(dy) == r ? 1 : 2 * r))
                    {
                        int tx = cx + dx, ty = cy + dy; if ((uint)tx >= (uint)N || (uint)ty >= (uint)N) continue;
                        int xm = tx * T + T / 2, ym = ty * T + T / 2;
                        var (ok, fief, _) = lw.ClaimSite(xm, ym);
                        if (!ok || lw.Grow(ty * N + tx, [], lw.Rules.ClaimStartTiles, xm, ym, u => lw.ClaimTileOk(u, fief, null)).Length < lw.Rules.ClaimStartTiles) continue;
                        long sec = (rt.HourDone + 1) * 3600; int day = (int)(sec / 86400);
                        Ravensguard.Sim.Core.EntityId Chron(Ravensguard.Sim.Core.GameTime when, string type, IReadOnlyList<Ravensguard.Sim.Core.EntityId> s, IReadOnlyList<Ravensguard.Sim.Core.Cause> c, params (string Key, string Value)[] data)
                        { var id = st.Ids.Allocate(Ravensguard.Sim.Core.IdKind.Event); var d = new Dictionary<string, string>(StringComparer.Ordinal); foreach (var (k, v) in data) d[k] = v; st.Chronicle.Add(new Ravensguard.Sim.Core.ChronicleEntry(id, when, type, s, c, d)); return id; }
                        var staked = lw.Stake(st.Party.Hero, Ravensguard.Sim.Economy.PurseKind.Person, xm, ym, false, day, Chron);
                        if (!staked.Ok) continue;
                        var claim = lw.Book.Claims.Last();
                        var cl = hw.Clear(st.Party.Hero, claim.Serial, -1, day);
                        if (!cl.Ok) continue;
                        var c0 = hw.Book.Clearings.Last();
                        hw.Hire(st.Party.Hero, c0.Serial, 2, day);
                        for (int i = 0; i < 14 && c0.State == ClearingState.Working; i++) hw.HeroWork(st.Party.Hero, c0.Serial, 0, 8 * 3600, day + i, Chron);          // the hero's days in a heap: the dev shot's acre is cleared
                        return (c0.Tile % N * T + T / 2, c0.Tile / N * T + T / 2);
                    }
            return ((int, int)?)null;
        }).GetAwaiter().GetResult();
        if (res is { } at) { DebugPlaceHero(at.Item1 - 45, at.Item2); _pan = new Vector2(24 * Tile, 0); return $"homestead acre at {at.Item1},{at.Item2}"; }
        return "no wild ground near";
    }
}
