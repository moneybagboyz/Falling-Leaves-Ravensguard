using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Autopilot;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Travel;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Game.World;

/// <summary>
/// The Q menu and the verbs it holds (Phase 3 cleanup B, items 2 and 10): hunting game (key <c>;</c>), setting and collecting snares (<c>'</c>), making camp, foraging, the job board, and the autopilot's directive verbs
/// (work for pay, guard, rest, stop). Each verb is also a key where the spec asks for one. The sim does the work (<see cref="TravelState.Hunt"/>, <see cref="TravelState.SetSnare"/>); this file picks the tile, runs the
/// timed job on the camp clock and shows the result.
/// </summary>
public partial class StreetView
{
    readonly Dictionary<int, Sprite2D> _snareSprites = new();

    int HeroTile()
    {
        var (wx, wy) = _world.WorldCell;
        return RegionData.Index(Math.Clamp(wx / RegionData.TileM, 0, RegionData.Size - 1), Math.Clamp(wy / RegionData.TileM, 0, RegionData.Size - 1));
    }

    bool HeroArmed() => _heroLink?.View?.Items.Any(i => i.Slot is Ravensguard.Sim.Inventory.EquipSlot.MainHand or Ravensguard.Sim.Inventory.EquipSlot.OffHand && IsWeapon(i.Shape)) == true;

    Snare? SnareNear(double metres = 4)
    {
        var t = _travel; if (t == null) return null; var (hx, hy) = _world.WorldMm;
        return t.Snares.Where(s => DistM(SnareXY(s).X, SnareXY(s).Y, hx, hy) <= metres).OrderBy(s => DistM(SnareXY(s).X, SnareXY(s).Y, hx, hy)).ThenBy(s => s.Id).FirstOrDefault();
    }

    static (long X, long Y) SnareXY(Snare s) => s.XMm != 0 || s.YMm != 0 ? (s.XMm, s.YMm) : ((s.Tile % RegionData.Size) * RegionData.TileM * 1000L + RegionData.TileM * 500L, (s.Tile / RegionData.Size) * RegionData.TileM * 1000L + RegionData.TileM * 500L);
    static double DistM(long ax, long ay, long bx, long by) { double dx = (ax - bx) / 1000.0, dy = (ay - by) / 1000.0; return Math.Sqrt(dx * dx + dy * dy); }

    /// <summary>The hunt key: an hour of stalking this tile; a kill is butchered into the pack, a miss costs the hour.</summary>
    void HuntGame()
    {
        var t = _travel; var layer = Runtime?.State.Production?.Resources;
        if (t == null || layer == null || t.PackStore?.Invoke() is not { } pack || _source?.Region is not { } rg) { _router?.ShowToast(L.T("HUNT_NONE")); return; }
        int tile = HeroTile();
        if (t.AnimalsNear(layer, tile) <= 0) { _router?.ShowToast(L.T("HUNT_NO_GAME")); return; }
        _rec.Action(Ravensguard.Sim.Diagnostics.HeroAction.Hunt, tile, _world.Clock.Now.Seconds);
        StartCampJob(t, CampJobKind.Hunt, 3600);
    }

    /// <summary>The hour of stalking is over (the sim's camp job came due): a kill is butchered into the pack, a miss costs the hour.</summary>
    void HuntDone(TravelState t)
    {
        var layer = Runtime?.State.Production?.Resources;
        if (layer == null || t.PackStore?.Invoke() is not { } pack || _source?.Region is not { } rg) return;
        int tile = HeroTile();
        {
            long now = _world.Clock.Now.Seconds; var (hx, hy) = _world.WorldMm;
            var (car, why) = t.Hunt(layer, tile, rg.Biomes[tile], _world.Clock.Now.Season, now, hx, hy, 20, HeroArmed());
            if (car == null) { _router?.ShowToast(L.T(why == "missed" ? "HUNT_MISSED" : "HUNT_NO_GAME"), 3f); _rec.SetWantedSpeed(SimSpeed.X1); return; }
            var r = t.Butcher(car, pack, 50, now);
            string made = r == null ? "" : string.Join(", ", r.Made.Select(m => $"{m.Grams / 1000.0:0.#} kg {m.Good.Replace('_', ' ')}"));
            _router?.ShowToast(L.F("HUNT_GOT", car.Species.Replace('_', ' '), made), 4f); _rec.SetWantedSpeed(SimSpeed.X1);
        }
    }

    /// <summary>The snare key: a snare within four metres is taken up (and its catch butchered into the pack); else one is set here.</summary>
    void SnareVerb()
    {
        var t = _travel; var layer = Runtime?.State.Production?.Resources;
        if (t == null || layer == null) { _router?.ShowToast(L.T("HUNT_NONE")); return; }
        long now = _world.Clock.Now.Seconds; var (hx, hy) = _world.WorldMm;
        if (SnareNear() is { } s)
        {
            bool caught = s.Caught; _rec.Action(Ravensguard.Sim.Diagnostics.HeroAction.SnareCollect, hx, hy); var car = t.CollectSnare(s, now);
            if (car != null && t.PackStore?.Invoke() is { } pack) { var r = t.Butcher(car, pack, 50, now); _router?.ShowToast(L.F("SNARE_GOT", car.Species.Replace('_', ' '), r == null ? "" : string.Join(", ", r.Made.Select(m => $"{m.Grams / 1000.0:0.#} kg {m.Good.Replace('_', ' ')}"))), 4f); }
            else _router?.ShowToast(L.T(caught ? "SNARE_SPOILED" : "SNARE_EMPTY"), 3f);
            return;
        }
        if (t.AnimalsNear(layer, HeroTile()) <= 0) { _router?.ShowToast(L.T("HUNT_NO_GAME")); return; }
        _rec.Action(Ravensguard.Sim.Diagnostics.HeroAction.SnareSet, hx, hy);
        _router?.ShowToast(t.SetSnare(HeroTile(), now, hx, hy) != null ? L.T("SNARE_SET") : L.T("SNARE_MAX"), 3f);
    }

    /// <summary>Per frame: a snare sprite at each set snare (the art pipeline's <c>object.snare</c>), the full one lifted a little.</summary>
    void SyncSnareSprites()
    {
        var t = _travel; if (t == null) return;
        var want = t.Snares.Select(s => s.Id).ToHashSet();
        foreach (var id in _snareSprites.Keys.Where(k => !want.Contains(k)).ToList()) { _snareSprites[id].QueueFree(); _snareSprites.Remove(id); }
        foreach (var s in t.Snares)
        {
            if (!_snareSprites.TryGetValue(s.Id, out var sp))
            {
                var tex = _art.Texture("object.snare");
                sp = new Sprite2D { Texture = tex, Centered = false, Offset = new Vector2(-tex.GetWidth() / 2f, -tex.GetHeight() + 4), TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
                _ysort.AddChild(sp); _snareSprites[s.Id] = sp;
            }
            var (x, y) = SnareXY(s);
            sp.Position = new Vector2((float)Math.Round(x / 1000.0 * Tile), (float)Math.Round(y / 1000.0 * Tile));
            sp.Modulate = s.Caught ? new Color(1f, 0.85f, 0.7f) : Colors.White;
        }
    }

    // ------------------------------------------------------------------ the Q menu

    /// <summary>Q (or the right mouse button): the verbs for where the hero stands.</summary>
    void OpenQMenu()
    {
        if (_router == null) { OpenWork(); return; }
        var t = _travel; var layer = Runtime?.State.Production?.Resources;
        bool game = t != null && layer != null && t.AnimalsNear(layer, HeroTile()) > 0;
        var v = new List<QMenu.Verb>();
        v.Add(new(L.T("Q_HEAD_HERE"), () => { }, Heading: true));
        v.Add(new(L.T("Q_JOBS"), OpenWork));
        v.Add(new(L.T("Q_CAMP"), () => ToggleCamp(), t == null ? L.T("MAP_NONE") : null));
        v.Add(new(L.T("Q_FORAGE"), Forage, t == null ? L.T("MAP_NONE") : null));
        v.Add(new(L.T("Q_HUNT"), HuntGame, t == null ? L.T("MAP_NONE") : !game ? L.T("HUNT_NO_GAME") : null));
        v.Add(new(SnareNear() != null ? L.T("Q_SNARE_TAKE") : L.T("Q_SNARE_SET"), SnareVerb, t == null ? L.T("MAP_NONE") : SnareNear() == null && !game ? L.T("HUNT_NO_GAME") : null));
        v.Add(new(L.T("Q_HEAD_AUTO"), () => { }, Heading: true));
        string noAp = _ap == null ? L.T("MAP_NONE") : null!;
        v.Add(new(L.T("AP_D_WORK"), () => ApQuick(Directive.Work(0, L.T("AP_D_WORK"))), noAp));
        v.Add(new(L.T("AP_D_GUARD"), () => ApQuick(Named(Directive.Guard(HeroSettlement() >= 0 ? HeroSettlement() : 0), L.T("AP_D_GUARD"))), noAp));
        v.Add(new(L.T("AP_D_REST"), () => ApQuick(Named(Directive.Rest(), L.T("AP_D_REST"))), noAp));
        v.Add(new(L.T("Q_AUTO_STOP"), () => { if (_ap != null) ApHost().Stop(); }, _ap == null || !_apView.Enabled ? L.T("Q_AUTO_OFF") : null));
        v.Add(new(L.T("Q_AUTO_PANEL"), OpenAutopilot, noAp));
        SurfaceSiteVerbs(v);                                // P4-H: search an old battlefield, dig a hoard (StreetView.Crawl.cs)
        LandVerbs(v);                                       // P5-A2: the lot market, the deeds, staking a claim (StreetView.Land.cs)
        HoldingsVerbs(v);                                   // P6-B6: whose is this, clear an acre of the claim, felling by hand, sowing, the logs (StreetView.Holdings.cs)
        WillVerbs(v);                                       // P5-H: make a will before a priest or the reeve (StreetView.Will.cs)
        TreatVerbs(v);                                      // P6-B1: treat the wounded, a physician's Treat me (StreetView.Treatment.cs)
        FaithVerbs(v);                                      // P6-B4: attend the service, give alms, a pilgrim's vow, beside a priest (StreetView.Faith.cs)
        JusticeVerbs(v);                                    // P6-C1: give yourself up beside a guard, hold court with cases on the docket (StreetView.Justice.cs)
        ContractVerbs(v);                                   // P5-D: the contract board (StreetView.Contracts.cs)
        VehicleVerbs(v);                                    // P5-G: the designer, a vehicle's parts, work, repair, launch (StreetView.Vehicles.cs)
        BusinessVerbs(v);                                   // P5-F: the Ledger, open a business, hire, steward, a share, prospecting (StreetView.Business.cs)
        _router.Push(new QMenu(_router, L.T("Q_TITLE"), v));
    }

    static Directive Named(Directive d, string name) { d.Name = name; return d; }

    /// <summary>A directive verb from the Q menu: the autopilot starts with that one directive on the current limits, as the Y panel's Start would (the same barrier path).</summary>
    void ApQuick(Directive d)
    {
        if (_ap == null) return;
        var host = ApHost(); var s = host.Setup();
        var setup = new AutopilotSetup { Queue = [d], Limits = s.Limits.Clone(), Policy = (InterruptPolicy[])s.Policy.Clone(), Quiet = (bool[])s.Quiet.Clone() };
        host.Start(setup);
        _router?.ShowToast(L.T("AP_STARTED"), 3f);
    }

    /// <summary>A directive for a party member, from the squad screen's picker (autopilot spec 2: the same decision function runs for the hero, the party and NPCs): <c>work</c>, <c>guard</c>, <c>rest</c> or <c>stop</c>.</summary>
    void ApPartyDirective(EntityId person, string kind)
    {
        if (_ap == null || Runtime is not { } rt) return;
        long now = _world.Clock.Now.Seconds; var ap = _ap; var cfg = ap.Cfg.Settings;
        _rec.Action(Ravensguard.Sim.Diagnostics.HeroAction.PartyDirective, (long)person.Value, kind switch { "guard" => 1, "rest" => 2, "stop" => 3, _ => 0 });
        if (kind == "stop") { _ = Bar(rt, () => { ap.Disable(person, now); return 0; }); return; }
        Directive d = kind switch { "guard" => Named(Directive.Guard(Math.Max(0, HeroSettlement())), L.T("AP_D_GUARD")), "rest" => Named(Directive.Rest(), L.T("AP_D_REST")), _ => Directive.Work(0, L.T("AP_D_WORK")) };
        var setup = new AutopilotSetup { Queue = [d] };
        setup.Limits.SpendCapMitesPerDay = cfg.DefaultSpendCapMitesPerDay; setup.Limits.MaxRiskPm = cfg.DefaultMaxRiskPm; setup.Limits.PurseReserveMites = cfg.DefaultPurseReserveMites;
        _ = Bar(rt, () => { ap.Apply(person, now, setup); return 0; });
    }
}
