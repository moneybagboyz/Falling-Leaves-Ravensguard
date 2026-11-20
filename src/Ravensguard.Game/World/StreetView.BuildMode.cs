using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Content;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI;
using Ravensguard.Game.World.Build;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Holdings;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Game.World;

/// <summary>What build mode asks the barrier to check: a brush of cells, or a plan at the cursor (fitted to the lot under it), with the signature that tells two asks apart.</summary>
public sealed record BuildAsk(bool Plan, IReadOnlyList<PlaceCell> Cells, string PlanId, string? Culture, Wealth? Wealth, bool Auto, int Turns, bool Mirror, int X, int Y, (string Id, string Text)? Extra, string Sig);

/// <summary>The barrier's answer: the preview (refusals, cells cleared), the lot under the cursor, and for a plan where it lands (fitted or at the cursor) and its cells.</summary>
public sealed record BuildAnswer(BuildAsk Ask, BuildPreview Preview, FitLot? Lot, PlanFit? Fit, int PlanX, int PlanY, int Index, IReadOnlyList<PlaceCell> Cells, HeroText? Problem = null);

/// <summary>
/// P5-C: build mode (build-mode-and-construction 4.1, 4.2, 4.8, 6; overview 6). B opens it (refused in a fight or in the saddle): the world keeps running (Space pauses, 1-4 the speeds, 8x
/// while building somewhere safe), the World keys other than time, camera, level, overlay, save and the bug report are off (<see cref="ContextRules.BuildKeptWorld"/>). Free placement on
/// the 1 m grid at the level in view: the palette by category and material (<c>[</c> <c>]</c>, <c>,</c> <c>.</c>), LMB places, a drag draws a line for walls, posts and fences or a filled
/// rectangle for floors and roofs (Shift: the outline), Z / X rotate, RMB (or a right drag) takes ghosts off. Every refusal is shown before placing: the barrier runs the placement's checks
/// without placing (<see cref="ConstructionWorld.Preview"/>) and the cells come up red with their reason, with the cost, worker-hours and materials of the brush. P opens the plan library
/// (culture, wealth, purpose, fits this lot), a plan is fitted to the lot under the cursor (<see cref="PlanFitter"/>, Z / X turn and M mirrors it with the fit check) and Enter or LMB commits
/// it as ghosts in one command. R roofs over the room at the cursor, Delete is the take-down brush, Ctrl+Z undoes the last placement, Ctrl+S selects a rectangle to save as a blueprint,
/// J opens the site panel, Esc steps back (tool, then mode), B closes. Nothing here holds game state: every change is a construction command through the hero's desk (recorded for replay).
/// </summary>
public partial class StreetView
{
    enum BTool { Piece, Plan, TakeDown, SaveRect }

    bool _bOn;
    BTool _bTool;
    int _bCatIdx, _bPieceIdx;
    byte _bRot;
    (int X, int Y)? _bFrom;
    bool _bRight, _bShift;
    (int X, int Y) _bCursor;
    string? _bPlanId, _bCulture; Wealth? _bWealth; bool _bAuto = true; int _bTurns; bool _bMirror; (string Id, string Text)? _bPlanText; string _bPlanName = "";
    BuildAnswer? _bAns; Task<BuildAnswer?>? _bTask; string _bSent = "";
    BuildCatalog? _bCatalog;
    BuildPalette? _bPal;
    BuildPreviewLayer? _bLayer;
    long _bSeen;
    double _bDesignsAcc = 99;
    IReadOnlyList<(string Id, string Text)> _bDesigns = [];
    bool _handWork; long _handFrom = -1, _handAt;
    /// <summary>The save-as rectangle waiting for its name (Ctrl+S, a drag, then the dialog).</summary>
    (int X0, int Y0, int X1, int Y1)? _bSaveRect;
    SaveBlueprintDialog? _bSaveDialog;
    /// <summary>Dev and smoke: the cursor was set by a hook and stays until the mouse moves.</summary>
    bool _bPinned;

    BuildCatalog? Catalog => _bCatalog ??= _source?.Content is { } c ? new BuildCatalog(c) : null;
    public bool BuildModeOn => _bOn;

    // ------------------------------------------------------------------ open and close

    /// <summary>B (World): build mode on. Refused in a fight, in the saddle or on a wagon, without a running world.</summary>
    void OpenBuildMode()
    {
        if (_bOn) return;
        if (Runtime == null || _heroLink == null || BuildWorld == null || Catalog == null) { _router?.ShowToast(L.T("BUILD_NO_WORLD")); return; }
        if (_fightView != null) { _router?.ShowToast(L.T("BUILD_NOT_IN_FIGHT"), 3f); return; }
        if (_ride is { IsMounted: true } or { IsDriving: true }) { _router?.ShowToast(L.T("BUILD_NOT_MOUNTED"), 3f); return; }
        _bOn = true; _bTool = BTool.Piece; _bFrom = null; _bAns = null; _bSent = "";
        ActiveContexts.Enter(InputContext.Build);
        if (_band > Band.Street) SetBand(Band.Street);
        _rec.SetInput(new Ravensguard.Sim.Live.MoveInput(0, 0, false));
        if (_bLayer == null) { _bLayer = new BuildPreviewLayer(_art, Catalog.Costs) { Name = "BuildPreview", ZIndex = 46 }; _street.AddChild(_bLayer); }
        if (_bPal == null)
        {
            _bPal = new BuildPalette(Catalog, BuildPaletteAct()) { Name = "BuildPalette" };
            GetNode<Control>("Hud").AddChild(_bPal);
        }
        _bPal.Visible = true; _bLayer.Visible = true;
        if (_hintLabel != null) _hintLabel.Visible = false;
        if (_hintToast != null) { _hintToast.OffsetTop -= BuildHintLift; _hintToast.OffsetBottom -= BuildHintLift; }   // the first hour's hints show above the palette
        _bCulture ??= LocalCulture();
        _bDesignsAcc = 99;
        RefreshPalette();
    }

    void CloseBuildMode()
    {
        if (!_bOn) return;
        _bOn = false; _bFrom = null; _bTool = BTool.Piece;
        _bSaveDialog?.Close();
        ActiveContexts.Exit(InputContext.Build);
        if (_bPal != null) _bPal.Visible = false;
        if (_bLayer != null) { _bLayer.Visible = false; _bLayer.Clear(); }
        if (_hintLabel != null) _hintLabel.Visible = true;
        if (_hintToast != null) { _hintToast.OffsetTop += BuildHintLift; _hintToast.OffsetBottom += BuildHintLift; }
    }

    const int BuildHintLift = 162;

    BuildPalette.Acts BuildPaletteAct() => new()
    {
        Category = d => { _bCatIdx = (_bCatIdx + d + Catalog!.Categories.Count) % Catalog.Categories.Count; _bPieceIdx = 0; _bTool = BTool.Piece; RefreshPalette(); },
        Piece = i => { _bPieceIdx = i; _bTool = BTool.Piece; RefreshPalette(); },
        Library = OpenPlanLibrary,
        Roof = RoofOverCursor,
        TakeDown = () => { _bTool = _bTool == BTool.TakeDown ? BTool.Piece : BTool.TakeDown; RefreshPalette(); },
        SaveAs = () => { _bTool = _bTool == BTool.SaveRect ? BTool.Piece : BTool.SaveRect; RefreshPalette(); },
        Site = () => OpenSitePanel(),
        Undo = () => IssueBuild(ConstructionDesk.Undo(HeroNow())),
        Close = CloseBuildMode,
        Commit = CommitPlan,
    };

    /// <summary>The culture of the place the hero is in (the plan library's default filter), or null.</summary>
    string? LocalCulture()
    {
        if (Runtime is not { } rt || _source?.Content is not { } db) return null;
        int s = HeroSettlement(); if (s < 0) return null;
        var pools = db.All<NamePoolDef>().Select(p => p.Id).OrderBy(i => i, StringComparer.Ordinal).ToList();
        if (pools.Count == 0) return null;
        int ci = rt.State.Population.Region.Settlements[s].Culture;
        string pool = pools[((ci % pools.Count) + pools.Count) % pools.Count];
        return db.All<CultureDef>().FirstOrDefault(c => c.NamePool == pool)?.Id;
    }

    PalettePiece? CurrentPiece()
    {
        var cat = Catalog!.Categories[_bCatIdx];
        var l = Catalog.In(cat);
        if (l.Count == 0) return null;
        _bPieceIdx = Math.Clamp(_bPieceIdx, 0, l.Count - 1);
        return l[_bPieceIdx];
    }

    // ------------------------------------------------------------------ input

    (int X, int Y) RegionCellAt(Vector2 local) { var c = ScreenToCell(local); return (c.X + _world.OriginX, c.Y + _world.OriginY); }

    /// <summary>Build mode's keys and clicks (the World keys it suspends never reach here: <see cref="ActiveContexts"/>). True when the event was build mode's.</summary>
    bool BuildInput(InputEvent e)
    {
        if (!_bOn) return false;
        if (_bSaveDialog != null) return e is InputEventKey or InputEventMouseButton;   // the name dialog is modal: no build key or click under it
        if (e is InputEventMouseMotion mm) { _bPinned = false; _bCursor = RegionCellAt(mm.Position - GlobalPosition); return false; }
        if (e is InputEventMouseButton mb && mb.ButtonIndex is MouseButton.Left or MouseButton.Right)
        {
            _bCursor = RegionCellAt(mb.Position - GlobalPosition);
            if (mb.Pressed && Actions.Pressed(e, "build.place")) { _bShift = mb.ShiftPressed; BuildPress(false); return true; }
            if (mb.Pressed && Actions.Pressed(e, "build.remove_ghost")) { BuildPress(true); return true; }
            if (!mb.Pressed && _bFrom != null && (mb.ButtonIndex == MouseButton.Left) != _bRight) { _bShift = mb.ShiftPressed; BuildRelease(); return true; }
            return false;
        }
        if (e is not InputEventKey { Pressed: true }) return false;
        if (Actions.Pressed(e, "build.close")) CloseBuildMode();
        else if (Actions.Pressed(e, "build.cancel")) BuildBack();
        else if (Actions.Pressed(e, "build.undo")) IssueBuild(ConstructionDesk.Undo(HeroNow()));
        else if (Actions.Pressed(e, "build.save_blueprint")) { _bTool = _bTool == BTool.SaveRect ? BTool.Piece : BTool.SaveRect; _bFrom = null; RefreshPalette(); }
        else if (Actions.Pressed(e, "build.rotate_ccw")) Turn(-1);
        else if (Actions.Pressed(e, "build.rotate_cw")) Turn(1);
        else if (Actions.Pressed(e, "build.mirror")) { if (_bTool == BTool.Plan) { _bMirror = !_bMirror; _bAuto = false; RefreshPalette(); } }
        else if (Actions.Pressed(e, "build.piece_prev")) { _bPieceIdx = Math.Max(0, _bPieceIdx - 1); _bTool = BTool.Piece; RefreshPalette(); }
        else if (Actions.Pressed(e, "build.piece_next")) { _bPieceIdx++; _bTool = BTool.Piece; CurrentPiece(); RefreshPalette(); }
        else if (Actions.Pressed(e, "build.category_prev")) BuildPaletteAct().Category!(-1);
        else if (Actions.Pressed(e, "build.category_next")) BuildPaletteAct().Category!(1);
        else if (Actions.Pressed(e, "build.plan_library")) OpenPlanLibrary();
        else if (Actions.Pressed(e, "build.roof_over")) RoofOverCursor();
        else if (Actions.Pressed(e, "build.deconstruct")) { _bTool = _bTool == BTool.TakeDown ? BTool.Piece : BTool.TakeDown; _bFrom = null; RefreshPalette(); }
        else if (Actions.Pressed(e, "build.commit")) CommitPlan();
        else if (Actions.Pressed(e, "build.site_panel")) OpenSitePanel();
        else return false;
        return true;
    }

    /// <summary>Esc steps back: a drag, then the tool, then build mode itself.</summary>
    void BuildBack()
    {
        if (_bFrom != null) { _bFrom = null; return; }
        if (_bTool != BTool.Piece) { _bTool = BTool.Piece; RefreshPalette(); return; }
        CloseBuildMode();
    }

    void Turn(int d)
    {
        if (_bTool == BTool.Plan)
        {
            if (_bAuto && _bAns?.Fit is { Ok: true } f) { _bTurns = f.Turns; _bMirror = f.Mirror; }
            _bTurns = (_bTurns + d + 4) & 3; _bAuto = false;
        }
        else _bRot = (byte)((_bRot + d + 4) & 3);
        RefreshPalette();
    }

    void BuildPress(bool right)
    {
        if (_bTool == BTool.Plan && !right) { CommitPlan(); return; }
        _bFrom = _bCursor; _bRight = right;
    }

    void BuildRelease()
    {
        if (_bFrom is not { } from) return;
        _bFrom = null;
        var (x1, y1) = _bCursor; long now = HeroNow(); int z = _viewZ;
        if (_bRight) { IssueBuild(ConstructionDesk.RemoveRect(from.X, from.Y, x1, y1, z, now)); return; }
        switch (_bTool)
        {
            case BTool.TakeDown: IssueBuild(ConstructionDesk.DeconstructRect(from.X, from.Y, x1, y1, z, now)); break;
            case BTool.SaveRect: _bSaveRect = (from.X, from.Y, x1, y1); OpenSaveBlueprint(); break;
            case BTool.Piece when CurrentPiece() is { } p:
                if (p.Kind == PieceKind.StairsUp) { IssueBuild(ConstructionDesk.Stairs(x1, y1, z, _bRot, now)); break; }
                var (shape, ax, ay) = Shape(p, from, (x1, y1));
                IssueBuild(ConstructionDesk.Place(p.Id, ax, ay, x1, y1, z, shape, _bRot, now));
                break;
        }
    }

    /// <summary>The brush of a piece (spec 4.1): a drag draws a line for walls, posts and fences, a filled rectangle for floors and roofs (Shift: its outline); furniture one cell.</summary>
    (string Shape, int X0, int Y0) Shape(PalettePiece p, (int X, int Y) from, (int X, int Y) to)
    {
        bool area = p.Kind is PieceKind.Floor or PieceKind.Roof;
        bool line = p.Kind is PieceKind.Wall or PieceKind.Post or PieceKind.Beam || p.Category == BuildCategory.Fences;
        if (from == to || !(area || line)) return ("one", to.X, to.Y);
        if (_bShift) return ("outline", from.X, from.Y);
        return (area ? "rect" : "line", from.X, from.Y);
    }

    /// <summary>The cells the brush covers now (the drag, or the cursor): what the preview checks and draws.</summary>
    List<PlaceCell> BrushCells()
    {
        if (CurrentPiece() is not { } p) return [];
        int z = _viewZ;
        if (p.Kind == PieceKind.StairsUp) return [new(_bCursor.X, _bCursor.Y, z, "stairs_up", _bRot), new(_bCursor.X, _bCursor.Y, z + 1, "stairs_down", _bRot)];
        if (_bFrom is { } f && !_bRight && _bTool == BTool.Piece)
        {
            var (shape, ax, ay) = Shape(p, f, _bCursor);
            return [.. ConstructionDesk.Brush(ax, ay, _bCursor.X, _bCursor.Y, shape).Select(c => new PlaceCell(c.X, c.Y, z, p.Id, _bRot))];
        }
        return [new(_bCursor.X, _bCursor.Y, z, p.Id, _bRot)];
    }

    void RoofOverCursor()
    {
        string roof = CurrentPiece() is { Kind: PieceKind.Roof } p ? p.Id : "roof_thatch";
        IssueBuild(ConstructionDesk.Roof(_bCursor.X, _bCursor.Y, _viewZ, roof, HeroNow()));
    }

    void IssueBuild(HeroCommand c)
    {
        if (_heroLink == null) return;
        _heroLink.Issue(c);
        _bSent = "";                                          // ask the barrier again once the command is in
    }

    // ------------------------------------------------------------------ plans

    void CommitPlan()
    {
        if (_bTool != BTool.Plan || _bPlanId == null) return;
        if (_bAns is not { Ask.Plan: true } a || a.Ask.PlanId != _bPlanId) { _router?.ShowToast(L.T("BUILD_WAIT"), 2f); return; }
        if (a.Problem != null) { _router?.ShowToast(a.Problem.Render(), 3f); return; }
        if (a.Preview.Refused.Count > 0) { _router?.ShowToast(L.F("BUILD_PLAN_REFUSED", Reason(a.Preview.Refused[0]), a.Preview.Refused.Count), 4f); return; }
        long now = HeroNow();
        if (_bPlanText is { } t) IssueBuild(ConstructionDesk.ImportDesign(t.Id, t.Text, now));
        var (turns, mirror) = PlanFitter.Transform(a.Index);
        IssueBuild(ConstructionDesk.Plan(_bPlanId, a.PlanX, a.PlanY, turns, mirror, now, -1, -1, _bCulture, _bWealth));
        _bTool = BTool.Piece; _bPlanId = null;
        RefreshPalette();
    }

    /// <summary>A plan picked in the library: build mode follows the cursor with it, fitted to the lot under it.</summary>
    void PlanChosen(string id, string name, string? culture, Wealth? wealth, (string Id, string Text)? text)
    {
        _bPlanId = id; _bPlanName = name; _bCulture = culture; _bWealth = wealth; _bPlanText = text; _bAuto = true; _bTurns = 0; _bMirror = false;
        _bTool = BTool.Plan; _bAns = null; _bSent = "";
        RefreshPalette();
    }

    void OpenPlanLibrary()
    {
        if (_router == null || Catalog == null || _source?.Content is not { } db) return;
        var disk = PlayerDesigns.ReadDisk();
        var lot = _bAns?.Lot;
        _router.Push(new PlanLibraryPanel(_router, Catalog, db, () => _bDesigns, disk, lot, _bCulture, PlanChosen) { Name = "PlanLibrary" });
    }

    // ------------------------------------------------------------------ save as blueprint

    /// <summary>The name dialog over the street (the rectangle stays in view); build mode takes no key while it is open.</summary>
    void OpenSaveBlueprint()
    {
        if (_bSaveRect is not { } r || _bSaveDialog != null) return;
        int w = Math.Abs(r.X1 - r.X0) + 1, h = Math.Abs(r.Y1 - r.Y0) + 1;
        _bSaveDialog = new SaveBlueprintDialog(w, h, name =>
        {
            IssueBuild(ConstructionDesk.SaveDesign(r.X0, r.Y0, r.X1, r.Y1, name, HeroNow()));
            _bTool = BTool.Piece; RefreshPalette();
        }, () => { _bSaveDialog?.QueueFree(); _bSaveDialog = null; _bSaveRect = null; }) { Name = "SaveBlueprint" };
        GetNode<Control>("Hud").AddChild(_bSaveDialog);
    }

    /// <summary>A design saved through the desk goes to disk too (user://blueprints/player, the player's mod), so other games can build it.</summary>
    void WriteDesignToDisk(string id)
    {
        if (Runtime is not { } rt) return;
        _ = Bar(rt, () => rt.State.Designs?.Text(id)).ContinueWith(t =>
        {
            if (!t.IsCompletedSuccessfully || t.Result is not { } text) return;
            try { PlayerDesigns.Write(id, text); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { GD.PushWarning("[build] blueprint not written to disk: " + ex.Message); }
        });
    }

    // ------------------------------------------------------------------ the site panel

    /// <summary>The open site nearest the cursor (in build mode) or the hero, of the hero's own; null when none.</summary>
    SiteView? NearSite()
    {
        if (Runtime?.State.Construction?.View is not { } v) return null;
        var s = _world.Snapshot;
        var (x, y) = _bOn ? _bCursor : (s.OriginX + s.Hero.CellX, s.OriginY + s.Hero.CellY);
        return v.Sites.Where(t => t.State is SiteState.Active or SiteState.Paused or SiteState.Abandoned).OrderBy(t => Math.Abs(t.PileX - x) + Math.Abs(t.PileY - y)).ThenBy(t => t.Id).FirstOrDefault();
    }

    bool OpenSitePanel()
    {
        if (_router == null || Runtime is not { } rt) return false;
        var site = NearSite();
        if (site == null) { _router.ShowToast(L.T("BUILD_NO_SITE"), 3f); return false; }
        var id = site.Id;
        Task<List<PlannedRoom>>? rooms = BuildWorld is { } cw ? Bar(rt, () => cw.PlannedRooms(id)) : null;
        _router.Push(new SitePanel(_router, () => rt.State.Construction?.View, id, () => rooms is { IsCompletedSuccessfully: true } r ? r.Result : null, SiteAct(id), () => _handWork)
            { Name = "SitePanel", PlanName = id => Catalog?.Costs.Library.Blueprints.FirstOrDefault(b => b.Def.Id == id)?.Def.Name });
        return true;
    }

    SitePanel.Acts SiteAct(EntityId site) => new()
    {
        Pause = p => IssueBuild(ConstructionDesk.Pause(site, p, HeroNow())),
        Hire = skilled => IssueBuild(ConstructionDesk.Hire(site, 1, skilled, HeroNow())),
        Unload = () => UnloadPack(site),
        BuyDeliver = () => BuyAndDeliverMissing(site),
        PostHaul = () => IssueBuild(HoldingsDesk.PostHaul(site, HeroNow())),
        HandWork = on => { _handWork = on; _handFrom = HeroNow(); _handAt = HeroNow(); _router?.ShowToast(L.T(on ? "BUILD_HAND_ON" : "BUILD_HAND_OFF"), 3f); },
    };

    /// <summary>P6-B6 (holdings 3.8): buys what the pile still lacks at the hero's market and has it carted to the pile, one order a good (the sim prices each at the book and adds the carting).</summary>
    void BuyAndDeliverMissing(EntityId site)
    {
        if (Runtime?.State.Construction?.View is not { } cv || cv.Sites.FirstOrDefault(s => s.Id == site) is not { } sv) return;
        int place = HeroSettlement();
        if (place < 0) { _router?.ShowToast(L.T("HERO_THERE_NO_MARKET_HERE"), 3f); return; }
        int n = 0;
        foreach (var m in sv.Materials.Where(m => m.InPileG < m.NeedG)) { _heroLink?.Issue(HoldingsDesk.BuyDeliver(site, m.Good, m.NeedG - m.InPileG, place, HeroNow())); n++; }
        if (n == 0) _router?.ShowToast(L.T("BUILD_SITE_NOTHING_MISSING"), 3f);
    }

    /// <summary>Unload what the site needs from the hero's pack into its pile (he must stand within 3 m of it: the sim says so otherwise).</summary>
    void UnloadPack(EntityId site)
    {
        if (_heroLink?.View is not { } hv || Runtime?.State.Construction?.View is not { } v || v.Sites.FirstOrDefault(s => s.Id == site) is not { } sv) return;
        var s = _world.Snapshot; int hx = s.OriginX + s.Hero.CellX, hy = s.OriginY + s.Hero.CellY;
        int n = 0;
        foreach (var m in sv.Materials.Where(m => m.NeedG > m.InPileG))
            foreach (var g in hv.Goods.Where(g => g.Id == m.Good && g.Container == default))
            { IssueBuild(ConstructionDesk.Deliver(site, m.Good, Math.Min(g.Qty, m.NeedG - m.InPileG), hx, hy, HeroNow())); n++; }
        if (n == 0) _router?.ShowToast(L.T("BUILD_NOTHING_TO_UNLOAD"), 3f);
    }

    // ------------------------------------------------------------------ the frame

    /// <summary>Per frame (from <see cref="BuildTick"/>): the answers to construction commands, the hero's own hands, and while build mode is open its pan, preview and palette.</summary>
    void BuildModeTick(double delta)
    {
        if (Runtime is not { } rt) return;
        // the answers of construction commands: toasted (the quiet "worked n minutes" of the hero's hands excepted), a saved design written to disk
        if (_heroLink?.View is { } hv)
            foreach (var r in hv.Results.Where(r => r.Kind == HeroCommandKind.Construction && r.Seq > _bSeen).ToList())
            {
                _bSeen = r.Seq;
                if (r.Message.Key is "cn_design_saved" or "cn_design_saved_warn" && r.Message.Args.Count > 1) WriteDesignToDisk(r.Message.Args[1]?.ToString() ?? "");
                if (r.Ok && r.Message.Key is "cn_worked" or "cn_built_pieces" or "cn_design_imported" or "cn_unloaded") continue;
                if (!r.Ok && _handWork && r.Message.Key is "cn_too_far" or "cn_too_dark" or "cn_rain" or "cn_no_materials" or "cn_no_support_yet" or "cn_no_ghost") _handWork = false;
                if (r.Text.Length > 0) _router?.ShowToast(r.Text, 3.5f);
            }
        HandWorkTick();
        if (!_bOn) return;
        if (_fightView != null || _ride is { IsMounted: true } or { IsDriving: true }) { CloseBuildMode(); return; }
        // WASD pan (the World's move keys are off)
        var pan = new Vector2((Actions.HeldLoose("build.pan_right") ? 1 : 0) - (Actions.HeldLoose("build.pan_left") ? 1 : 0), (Actions.HeldLoose("build.pan_down") ? 1 : 0) - (Actions.HeldLoose("build.pan_up") ? 1 : 0));
        if (pan != Vector2.Zero) { _pan += pan * (float)(delta * 10 * Tile); ClampPan(); }
        if (!_bPinned) _bCursor = RegionCellAt(GetLocalMousePosition());
        // the designs of the save (for the library), every few seconds
        _bDesignsAcc += delta;
        if (_bDesignsAcc > 3)
        {
            _bDesignsAcc = 0;
            _ = Bar(rt, () => rt.State.Designs?.Designs.Select(kv => (kv.Key, kv.Value.Text)).ToList() ?? []).ContinueWith(t => { if (t.IsCompletedSuccessfully) _bDesigns = t.Result; });
        }
        // the preview: one ask at a time, the newest
        if (_bTask is { IsCompleted: true } done)
        {
            if (done.IsCompletedSuccessfully && done.Result is { } ans) { _bAns = ans; RefreshPalette(); }
            _bTask = null;
        }
        var ask = CurrentAsk();
        if (ask != null && _bTask == null && ask.Sig != _bSent && BuildWorld is { } cw)
        {
            _bSent = ask.Sig;
            var st = rt.State;
            _bTask = Bar(rt, () => Answer(cw, st, ask));
        }
        _bLayer?.Show(ask, _bAns, _viewZ, _bCursor, _bFrom, _bTool == BTool.TakeDown || _bRight, _bTool == BTool.SaveRect);
    }

    /// <summary>What the preview should check now: the brush, or the plan at the cursor; null for the tools that draw only a rectangle.</summary>
    BuildAsk? CurrentAsk()
    {
        if (_bTool is BTool.TakeDown or BTool.SaveRect || _bRight && _bFrom != null) return null;
        if (_bTool == BTool.Plan && _bPlanId != null)
        {
            string sig = $"p|{_bPlanId}|{_bCulture}|{_bWealth}|{_bAuto}|{_bTurns}|{_bMirror}|{_bCursor.X}|{_bCursor.Y}|{_viewZ}";
            return new BuildAsk(true, [], _bPlanId, _bCulture, _bWealth, _bAuto, _bTurns, _bMirror, _bCursor.X, _bCursor.Y, _bPlanText, sig);
        }
        var cells = BrushCells();
        if (cells.Count == 0) return null;
        string s = "c|" + string.Join(";", cells.Take(3).Select(c => $"{c.X},{c.Y},{c.Z},{c.Piece},{c.Rot}")) + "|" + cells.Count + "|" + cells[^1].X + "," + cells[^1].Y;
        return new BuildAsk(false, cells, "", null, null, false, 0, false, _bCursor.X, _bCursor.Y, null, s);
    }

    /// <summary>At the barrier: the preview of an ask (nothing changes; the placement's own checks).</summary>
    static BuildAnswer? Answer(ConstructionWorld cw, Ravensguard.Sim.Save.WorldState st, BuildAsk a)
    {
        var hero = st.Party?.Hero ?? EconomyState.HeroOwner; int hh = ConstructionDesk.HouseholdOf(st);
        var lot = cw.LotAt?.Invoke(a.X, a.Y);
        if (!a.Plan) return new BuildAnswer(a, cw.Preview(hero, PurseKind.Person, hh, a.Cells), lot, null, 0, 0, 0, a.Cells);
        var lib = cw.Plans(a.Extra is { } e ? [e] : null);
        var g = lib.Grid(a.PlanId, a.Culture, a.Wealth);
        if (g == null) return new BuildAnswer(a, BuildPreview.Empty, lot, null, 0, 0, 0, [], HeroText.Of("cn_unknown_plan", a.PlanId));
        int idx = PlanFitter.IndexOf(a.Turns, a.Mirror);
        var tg = g.Transform(a.Turns, a.Mirror);
        int ox = a.X - tg.Width / 2, oy = a.Y - tg.Height / 2;
        PlanFit? fit = null;
        if (lot != null)
        {
            fit = cw.FitPlan(g, a.X, a.Y, a.Auto ? -1 : idx).Fit;
            if (fit.Ok) { ox = fit.X; oy = fit.Y; tg = fit.Grid!; idx = fit.Index; }
        }
        var cells = cw.PlanPlaceCells(tg, ox, oy);
        return new BuildAnswer(a, cw.Preview(hero, PurseKind.Person, hh, cells, allOrNothing: true, lot, fit), lot, fit, ox, oy, idx, cells);
    }

    void RefreshPalette()
    {
        if (_bPal == null || Catalog == null) return;
        var cat = Catalog.Categories[_bCatIdx];
        var piece = CurrentPiece();
        var a = _bAns;
        string tool = _bTool switch
        {
            BTool.Plan => L.F("BUILD_TOOL_PLAN", _bPlanName),
            BTool.TakeDown => L.T("BUILD_TOOL_TAKEDOWN"),
            BTool.SaveRect => L.T("BUILD_TOOL_SAVERECT"),
            _ => piece != null ? L.F("BUILD_TOOL_PIECE", piece.Name, piece.MaterialName) : L.T("BUILD_TOOL_NONE"),
        };
        var lines = new List<(string Text, int Tone)>();
        if (_bTool is BTool.Piece or BTool.Plan && a != null && (a.Ask.Plan == (_bTool == BTool.Plan)))
        {
            var cost = Catalog.Cost(a.Cells.Select(c => c.Piece));
            if (a.Problem != null) lines.Add((a.Problem.Render(), 2));
            if (_bTool == BTool.Plan)
            {
                var (turns, mirror) = PlanFitter.Transform(a.Index);
                lines.Add((L.F("BUILD_PLAN_TRANSFORM", turns * 90, L.T(mirror ? "BUILD_MIRRORED" : "BUILD_NOT_MIRRORED")), 0));
                if (a.Lot is { } lot) lines.Add((a.Fit is { Ok: true } f ? L.F(f.DoorToStreet ? "BUILD_FIT_STREET" : "BUILD_FIT_OK", lot.W, lot.H) : L.F("BUILD_FIT_NO", Ravensguard.Sim.Trade.HeroText.Of(a.Fit?.Key ?? "cn_fit_none", a.Fit?.Args ?? []).Render()), a.Fit is { Ok: true } ? 1 : 2));
                else lines.Add((L.T("cn_fit_no_lot"), 0));
            }
            lines.Add((BuildCatalog.Describe(cost), 0));
            if (a.Preview.Refused.Count > 0)
                foreach (var g in a.Preview.Refused.GroupBy(r => Reason(r)).Take(3)) lines.Add((L.F("BUILD_REFUSED_N", g.Count(), g.Key), 2));
            else if (a.Cells.Count > 0) lines.Add((L.T("BUILD_ALL_OK"), 1));
            if (a.Preview.Clears.Count > 0) lines.Add((L.F("BUILD_CLEARS", a.Preview.Clears.Count), 3));
        }
        _bPal.Show(cat, _bPieceIdx, tool, _viewZ, _bRot, lines, _bTool == BTool.Plan, _handWork);
    }

    static string Reason(PlaceRefusal r) => Ravensguard.Sim.Trade.HeroText.Of(r.Key, r.Args).Render();

    // ------------------------------------------------------------------ the hero's own hands (spec 4.4, 6: hands-on, Kenshi style)

    /// <summary>
    /// While "work by hand" is on, every half game minute the hero's attended time goes to the nearest ready ghost within reach (a recorded <c>cn:work</c> command: the sim checks reach, light,
    /// rain and materials and stops it with its reason). Moving away or nothing left to do turns it off.
    /// </summary>
    void HandWorkTick()
    {
        if (!_handWork || _heroLink == null || Runtime?.State.Construction?.View is not { } v) return;
        long now = HeroNow();
        if (now - _handAt < 30) return;
        var s = _world.Snapshot; int hx = s.OriginX + s.Hero.CellX, hy = s.OriginY + s.Hero.CellY, hz = s.Hero.Z;
        var g = v.Ghosts.Where(x => x.State is GhostState.Ready or GhostState.InProgress && Math.Abs(x.Z - hz) <= 1 && Math.Max(Math.Abs(x.X - hx), Math.Abs(x.Y - hy)) <= 3)
            .OrderBy(x => x.Z).ThenBy(x => Math.Max(Math.Abs(x.X - hx), Math.Abs(x.Y - hy))).ThenBy(x => x.Id).FirstOrDefault();
        if (g == null) { _handWork = false; _router?.ShowToast(L.T("BUILD_HAND_NONE"), 3f); RefreshPalette(); return; }
        _heroLink.Issue(ConstructionDesk.Work(g.Id, Math.Max(_handFrom, now - 600), now, hx, hy, hz));
        _handFrom = now; _handAt = now;
    }

    // ------------------------------------------------------------------ dev and smoke hooks

    /// <summary>Smoke: open build mode as B does (true when it opened).</summary>
    public bool DebugBuildMode(bool on) { if (on) OpenBuildMode(); else CloseBuildMode(); return _bOn == on; }
    public Control? DebugBuildPalette => _bPal;
    public bool DebugOpenPlanLibrary() { OpenPlanLibrary(); return _router?.Top is PlanLibraryPanel; }
    public bool DebugOpenSitePanel() => OpenSitePanel();
    public SaveBlueprintDialog? DebugSaveRect(int x0, int y0, int x1, int y1) { _bTool = BTool.SaveRect; _bFrom = (x0, y0); _bCursor = (x1, y1); _bPinned = true; _bSaveRect = (x0, y0, x1, y1); OpenSaveBlueprint(); return _bSaveDialog; }

    /// <summary>Screenshot hook: the cursor on a region cell (the mouse is not moved; build mode reads this cell until the next motion).</summary>
    public void DebugBuildCursor(int x, int y, (int X, int Y)? dragFrom = null) { _bCursor = (x, y); _bFrom = dragFrom; _bRight = false; _bSent = ""; _bPinned = true; }
    public void DebugBuildPiece(string piece)
    {
        if (Catalog == null) return;
        for (int c = 0; c < Catalog.Categories.Count; c++)
        {
            int i = Catalog.In(Catalog.Categories[c]).ToList().FindIndex(p => p.Id == piece);
            if (i >= 0) { _bCatIdx = c; _bPieceIdx = i; _bTool = BTool.Piece; RefreshPalette(); return; }
        }
    }
    public void DebugPlan(string id, int index = -1)
    {
        PlanChosen(id, Catalog?.Costs.Library.Blueprints.FirstOrDefault(b => b.Def.Id == id)?.Def.Name ?? id, _bCulture, null, null);
        if (index >= 0) { (_bTurns, _bMirror) = PlanFitter.Transform(index); _bAuto = false; }
    }
    public void DebugPlanPlace() => CommitPlan();
    /// <summary>Screenshot hook: the dev console's <c>build</c> verbs (stock, finish, crew) on the site nearest the hero.</summary>
    public string DebugDevBuild(params string[] args) => DevBuild(args);
    public BuildAnswer? DebugBuildAnswer => _bAns;
    public (int X, int Y) DebugHeroCell { get { var s = _world.Snapshot; return (s.OriginX + s.Hero.CellX, s.OriginY + s.Hero.CellY); } }
    public void DebugPanTo(int x, int y) { var s = _world.Snapshot; _pan = new Vector2((x - s.OriginX - s.Hero.CellX) * Tile, (y - s.OriginY - s.Hero.CellY) * Tile); ClampPan(); }

    /// <summary>
    /// Screenshot hook (dev only, a faucet): the empty plot for sale nearest the hero in his place (a new plot, no vacant house on it), the coin for it minted into his purse, and the purchase sent through his desk as play
    /// sends it. Returns the plot (region metres, its street side), or null when the place sells none.
    /// </summary>
    public async Task<FitLot?> DebugBuyLot()
    {
        if (Runtime is not { } rt || _heroLink == null || _source?.Content is not { } db) return null;
        int s = HeroSettlement(); var (hx, hy) = DebugHeroCell;
        var pick = await Bar(rt, () =>
        {
            if (LandOf(rt) is not { } lw || rt.State.Party is not { } party) return null;
            var l = lw.Listings(s).Where(x => x.Lot < 0).OrderBy(x => Math.Abs(x.Rect.CenterX - hx) + Math.Abs(x.Rect.CenterY - hy)).ThenBy(x => x.Lot).FirstOrDefault();
            if (l == null) return null;
            var ctx = new HeroContext(rt.State, party, db);
            rt.State.Economy!.Ledger.Mint(ctx.Purse, Coin.FromMites(l.PriceMites + 50_000), SourceReason.Seed);
            return l;
        });
        if (pick == null) return null;
        _heroLink.Issue(Ravensguard.Sim.Land.LandDesk.Buy(pick, HeroNow()));
        return new FitLot(pick.Rect.X, pick.Rect.Y, pick.Rect.W, pick.Rect.H, pick.Facing & 3);
    }

    /// <summary>Screenshot hook: whether the land rule lets the hero build at a point now.</summary>
    public async Task<bool> DebugMayBuild(int x, int y)
    {
        if (Runtime is not { } rt || BuildWorld is not { } cw) return false;
        return await Bar(rt, () => cw.May == null || cw.May(rt.State.Party?.Hero ?? EconomyState.HeroOwner, ConstructionDesk.HouseholdOf(rt.State), Ravensguard.Sim.Land.LandAction.Build, x, y).Allowed);
    }

    /// <summary>Screenshot hook (dev only): the hero set down on a region cell (as auto-travel places him; the window follows).</summary>
    public void DebugPlaceHero(int x, int y)
    {
        _rec.TravelPlace(x * 1000L + 500, y * 1000L + 500, Ravensguard.Sim.Live.Facing.South);
        _rec.SetInput(new Ravensguard.Sim.Live.MoveInput(0, 0, false));
        _pan = Vector2.Zero;
    }

    /// <summary>Smoke and shots: how many ghosts the construction view holds.</summary>
    public int DebugGhostCount => Runtime?.State.Construction?.View?.Ghosts.Count ?? 0;
}

/// <summary>The player's own blueprints on disk (build-mode-and-construction 4.8): <c>user://blueprints/player/&lt;id&gt;.jsonc</c>, a mod folder (<c>mod.json</c>, id <c>player</c>) read through the mod loader.</summary>
public static class PlayerDesigns
{
    public static string Dir => Path.GetFullPath(ProjectSettings.GlobalizePath("user://blueprints/player"));

    public static void Write(string id, string text)
    {
        Directory.CreateDirectory(Dir);
        string manifest = Path.Combine(Dir, "mod.json");
        if (!File.Exists(manifest)) File.WriteAllText(manifest, "{ \"id\": \"player\", \"name\": \"The player's blueprints\", \"version\": \"0.1.0\" }\n");
        File.WriteAllText(Path.Combine(Dir, id + ".jsonc"), text);
        GD.Print($"[build] blueprint {id} written to {Path.Combine(Dir, id + ".jsonc")}");
    }

    /// <summary>Every design file on disk as (id, text), by id; the id is the file's name.</summary>
    public static List<(string Id, string Text)> ReadDisk()
    {
        var l = new List<(string, string)>();
        try
        {
            if (!Directory.Exists(Dir)) return l;
            foreach (var f in Directory.GetFiles(Dir, "player.*.jsonc").OrderBy(f => f, StringComparer.Ordinal)) l.Add((Path.GetFileNameWithoutExtension(f), File.ReadAllText(f)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { GD.PushWarning("[build] blueprints on disk not read: " + ex.Message); }
        return l;
    }
}
