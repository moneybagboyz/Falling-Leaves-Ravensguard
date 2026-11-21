using Ravensguard.Game.UI.NewGame;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Game.World;

/// <summary>
/// The street view's side of land (P5-A2, land spec 6): the lot market at the lord's officer (a reeve, a mayor, a steward or the lord himself within ten metres), the hero's deeds (also from the
/// character sheet), and the claim verb in the Q menu. The screen reads a <see cref="LandView"/> built at a barrier; every action is a land command through the hero's desk (recorded for replay),
/// and its answer is toasted when the barrier has applied it.
/// </summary>
public partial class StreetView
{
    LandView? _landView;
    Task<LandView?>? _landTask;
    double _landAcc;
    long _landSeen;

    static LandWorld? LandOf(WorldRuntime rt) => rt.State.HomeRecords?.Sim.Get<LandWorld>();

    /// <summary>Asks the barrier for a fresh view (one at a time); the last one stays shown meanwhile.</summary>
    void RefreshLand()
    {
        if (Runtime is not { } rt || _heroLink == null) return;
        if (_landTask is { IsCompleted: true } t) { if (t.IsCompletedSuccessfully) _landView = t.Result; _landTask = null; }
        if (_landTask != null) return;
        int s = HeroSettlement(); var (x, y) = HeroRegionMm(); int day = (int)(HeroNow() / 86400); var hero = HeroId;
        _landTask = Bar(rt, () => LandOf(rt) is { } lw ? LandDesk.View(lw, hero, s, (int)(x / 1000), (int)(y / 1000), day) : null);
    }

    /// <summary>Per frame: the view is kept fresh every two seconds while the street or the land screen is up, and the answers to land commands are toasted.</summary>
    void LandTick(double delta)
    {
        _landAcc += delta;
        if (_landAcc >= 2.0 || _landTask is { IsCompleted: true }) { _landAcc = 0; RefreshLand(); }
        if (_heroLink?.View is not { } v) return;
        foreach (var r in v.Results.Where(r => r.Kind == HeroCommandKind.Land && r.Seq > _landSeen)) { _router?.ShowToast(r.Text, 4f); _landSeen = r.Seq; }
        LandShots();
    }

    static readonly Occupation[] Officers = [Occupation.Reeve, Occupation.Mayor, Occupation.Steward, Occupation.Lord];

    /// <summary>The lord's officer within ten metres (the lot market is bought in person: a deed needs a witness), or null.</summary>
    string? OfficerNear() => PeopleNear(10).Where(p => Officers.Contains(p.Occupation)).OrderBy(p => p.Id.Value).Select(p => p.Name).FirstOrDefault();

    LandActions LandAct() => new()
    {
        Buy = l => IssueLand(LandDesk.Buy(l, HeroNow())),
        LeaseField = p => IssueLand(LandDesk.LeaseField(p, HeroNow())),
        RentStall = (k, day) => IssueLand(LandDesk.Stall(HeroSettlement(), k, day, HeroNow())),
        Renew = (d, renew) => IssueLand(LandDesk.GiveUp(d, renew, HeroNow())),
        Offer = (d, ask) => IssueLand(LandDesk.Offer(d, ask, HeroNow())),
        Pull = c => IssueLand(LandDesk.Pull(c, HeroNow())),
    };

    void IssueLand(HeroCommand c)
    {
        if (_heroLink == null) return;
        _heroLink.Issue(c);
        _landAcc = 2.0;                                          // a fresh view once the barrier has applied it
    }

    /// <summary>The lot market (from the Q menu beside the lord's officer), or the hero's deeds.</summary>
    void OpenLand(bool deeds, bool force = false)
    {
        if (_router == null || Runtime == null || _heroLink == null) { _router?.ShowToast(L.T("MAP_NONE")); return; }
        if (!deeds && !force && OfficerNear() == null) { _router.ShowToast(L.T("ld_find_officer"), 3f); return; }
        RefreshLand();
        _router.Push(new LandScreen(_router, () => { RefreshLand(); return _landView; }, LandAct(), deeds) { ShotPath = _opt.Panel is "land" or "deeds" ? _opt.ShotPath : null });
    }

    /// <summary>Stake a claim where the hero stands: on wild land it is free (and nobody protects it); on a lord's waste the licence protects it, and without one it is squatting.</summary>
    void StakeClaimVerb()
    {
        if (_router == null || _landView is not { } v) return;
        var here = v.Here; var (x, y) = HeroRegionMm(); int xm = (int)(x / 1000), ym = (int)(y / 1000); long now = HeroNow();
        if (!here.Ok) { _router.ShowToast(L.T(here.Why.Length > 0 ? here.Why : "ld_claim_not_open"), 3f); return; }
        if (here.Fief < 0)
            _router.ShowModal(L.T("ld_stake_title"), L.F("ld_stake_wild", here.Tiles), (L.T("ld_btn_stake"), () => IssueLand(LandDesk.Stake(xm, ym, false, now))), (L.T("UI_CANCEL"), null));
        else
            _router.ShowModal(L.T("ld_stake_title"), L.F("ld_stake_waste", here.FiefName, here.Tiles, UiKit.Money(here.LicenceMites)),
                (L.T("ld_btn_licence"), () => IssueLand(LandDesk.Stake(xm, ym, true, now))), (L.T("ld_btn_squat"), () => IssueLand(LandDesk.Stake(xm, ym, false, now))), (L.T("UI_CANCEL"), null));
    }

    /// <summary>The Q menu's land verbs (P5-A2): the lot market beside an officer, the hero's deeds, staking a claim here.</summary>
    void LandVerbs(List<QMenu.Verb> v)
    {
        if (Runtime == null || _heroLink == null) return;
        v.Add(new(L.T("ld_q_head"), () => { }, Heading: true));
        string? officer = OfficerNear();
        v.Add(new(officer != null ? L.F("ld_q_market_with", officer) : L.T("ld_q_market"), () => OpenLand(false), officer == null ? L.T("ld_find_officer") : null));
        v.Add(new(L.T("ld_q_deeds"), () => OpenLand(true)));
        var here = _landView?.Here;
        string? why = here == null ? L.T("ld_waiting") : here.Ok ? null : L.T(here.Why.Length > 0 ? here.Why : "ld_claim_not_open");
        v.Add(new(here is { Ok: true, Fief: >= 0 } ? L.F("ld_q_stake_waste", here.FiefName) : L.T("ld_q_stake"), StakeClaimVerb, why));
    }

    // ------------------------------------------------------------------ dev screenshots: --shot-panel land | deeds | claim

    int _landShot, _landShotAt;

    /// <summary>
    /// <c>land</c>: the lot market in the hero's place; <c>deeds</c>: a plot bought, a stall rented and a claim staked first (through the desk, as play sends them), then the deeds;
    /// <c>claim</c>: the Q menu with the claim verb and the staking dialog.
    /// </summary>
    void LandShots()
    {
        if (_opt.Panel is not ("land" or "deeds" or "claim") || _heroLink == null || Runtime is not { } rt) return;
        if (_landShot == 0 && _frames >= 60 && rt.HourDone >= _world.Clock.Now.TotalHours && _landView != null) { _landShot = 1; _landShotAt = _frames; }
        if (_landShot == 1)
        {
            _landShot = 2;
            if (_opt.Panel == "deeds" && _landView is { } v)
            {
                if (v.Listings.Count > 0) IssueLand(LandDesk.Buy(v.Listings[0], HeroNow()));
                if (v.FreeStalls.Count > 0) IssueLand(LandDesk.Stall(v.Settlement, v.FreeStalls[0], false, HeroNow()));
                // a claim on the nearest open ground a lord's waste offers: found at the barrier, staked without a licence (squatting shows in the list)
                var (x, y) = HeroRegionMm();
                var at = rt.AtBarrier(() =>
                {
                    if (LandOf(rt) is not { } lw) return ((int, int)?)null;
                    int N = Ravensguard.Sim.WorldGen.Region.Region.Size, cx = (int)(x / 1000) / 64, cy = (int)(y / 1000) / 64;
                    for (int r = 1; r < 40; r++)
                        for (int dy = -r; dy <= r; dy++)
                            for (int dx = -r; dx <= r; dx += (Math.Abs(dy) == r ? 1 : 2 * r))
                            {
                                int tx = cx + dx, ty = cy + dy; if ((uint)tx >= (uint)N || (uint)ty >= (uint)N) continue;
                                var (ok, fief, _) = lw.ClaimSite(tx * 64 + 32, ty * 64 + 32);
                                if (ok && lw.Grow(ty * N + tx, [], lw.Rules.ClaimStartTiles, tx * 64 + 32, ty * 64 + 32, u => lw.ClaimTileOk(u, fief, null)).Length >= lw.Rules.ClaimStartTiles) return (tx * 64 + 32, ty * 64 + 32);
                            }
                    return null;
                }).GetAwaiter().GetResult();
                if (at is { } p) { GD.Print($"[land] staking at region metres {p.Item1},{p.Item2}"); IssueLand(LandDesk.Stake(p.Item1, p.Item2, false, HeroNow())); }
            }
        }
        if (_landShot != 2 || _frames != _landShotAt + 120) return;
        _landShot = 3;
        switch (_opt.Panel)
        {
            case "land": OpenLand(false, force: true); break;
            case "deeds": OpenLand(true); break;
            case "claim": OpenQMenu(); break;
        }
        if (_opt.Panel == "claim") _ = ClaimShot();
    }

    async Task ClaimShot()
    {
        for (int i = 0; i < 10; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (_router?.Top is QMenu qm) foreach (var n in qm.FindChildren("*", "ScrollContainer", true, false)) ((ScrollContainer)n).ScrollVertical = 100_000;   // the land verbs are last
        for (int i = 0; i < 10; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var img = GetViewport().GetTexture().GetImage();
        string q = _opt.ShotPath ?? "/tmp/land-claim.png";
        GD.Print($"[land] q menu screenshot {q}: {img.SavePng(q)}");
        _router?.Pop();
        if (_landView is { Here: { } h }) GD.Print($"[land] here: ok {h.Ok} why {h.Why} fief {h.FiefName} tiles {h.Tiles}");
        StakeClaimVerb();                                      // the staking dialog (or, where no claim can stand, the toast saying why)
        for (int i = 0; i < 12; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        img = GetViewport().GetTexture().GetImage();
        string d = q.Replace(".png", "-dialog.png", StringComparison.Ordinal);
        GD.Print($"[land] dialog screenshot {d}: {img.SavePng(d)}");
        GetTree().Quit();
    }
}
