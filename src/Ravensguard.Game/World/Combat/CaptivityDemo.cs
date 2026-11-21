using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Game.UI;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Succession;

namespace Ravensguard.Game.World.Combat;

/// <summary>
/// The aftermath screens on their own (<c>-- --captivity-demo defeat|captive|army|killed</c>, P4-D): the defeat screen after a robbery, the captivity screen in a bandit hideout or an army
/// cage, a death with the succession. Built from sim records made here (the screens read nothing else). Screenshots: <c>--shot-out file.png</c>.
/// </summary>
public partial class CaptivityDemo : Control
{
    string _what = "defeat";
    string? _shotOut;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        string Arg(string n, string d) { int i = Array.IndexOf(args, n); return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[i + 1] : d; }
        _what = Arg("--captivity-demo", "defeat");
        _shotOut = Arg("--shot-out", ""); if (_shotOut.Length == 0) _shotOut = null;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Theme = PixelTheme.Build();
        var road = new ColorRect { Color = new Color(0.22f, 0.27f, 0.18f) };
        road.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(road);
        var lane = new ColorRect { Color = new Color(0.42f, 0.36f, 0.26f), AnchorLeft = 0, AnchorRight = 1, AnchorTop = 0.42f, AnchorBottom = 0.58f };
        AddChild(lane);
        AddChild(Build());
        GD.Print($"CaptivityDemo ready: {_what}");
        if (_shotOut != null) _ = Shot();
    }

    Control Build()
    {
        long now = 300L * 86400 + 22 * 3600;
        switch (_what)
        {
            case "captive":
            case "army":
            {
                bool army = _what == "army";
                var view = new CaptiveView(EconomyStateHero, army ? CaptorKind.Army : CaptorKind.Bandits, army ? Restraint.Caged : Restraint.Bound, CaptiveState.Held, army ? 2 : 3, army ? 2 : 1, true,
                    army ? 1500 : 1500, army ? PayerKind.Household : PayerKind.Friends, army ? 800 : 300, 303, 300, army ? 340 : 310, army, army, 500, army ? 400 : 0, army ? 125 : 108, !army, army ? 0 : 1, 2, "",
                    new CaptiveResult(army ? CaptiveVerb.Search : CaptiveVerb.WorkBonds, army, false, army ? "MC_FOUND_NAIL" : "MC_BONDS_HOLD", now), 0, 0);
                return new CaptivityView(() => view, _ => { }, () => false) { Name = "CaptivityView" };
            }
            case "rescue":                                                      // P4 close-out: the rescue control switch on the captivity screen
            {
                var view = new CaptiveView(EconomyStateHero, CaptorKind.Bandits, Restraint.Bound, CaptiveState.Held, 3, 1, true, 1500, PayerKind.Friends, 300, 303, 300, 310, false, false, 500, 0, 108, false, 1, 0, "",
                    null, 0, 0);
                return new CaptivityView(() => view, _ => { }, () => false)
                {
                    Name = "CaptivityView", Rescuers = () => [(EntityId.Make(IdKind.Person, 9), "Wat Hobbe")], TakeControl = _ => { },
                };
            }
            case "rescue_hud":                                                  // P4 close-out: the street view's line while the player is the companion
            {
                var box = new PanelContainer { Name = "RescueHud", Position = new Vector2(12, 12), CustomMinimumSize = new Vector2(560, 0) };
                box.AddChild(UiKit.Body(L.F("rs_hud", "Wat Hobbe", "Aldric Vane")));
                var toast = new PanelContainer { Position = new Vector2(180, 440), CustomMinimumSize = new Vector2(600, 0) };
                toast.AddChild(UiKit.Body(L.F("rs_now_you_are", "Wat Hobbe", "Aldric Vane")));
                var root = new Control(); root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); root.AddChild(box); root.AddChild(toast);
                return root;
            }
            case "succession":
            case "succession_ironman":                                         // P4 close-out: the heir takes over
            {
                bool iron = _what == "succession_ironman";
                var heirs = new List<SuccessionCandidate>
                {
                    new(EntityId.Make(IdKind.Person, 7), 1, SuccessionRule.Sibling, 22, "Isolde Vane"),
                    new(EntityId.Make(IdKind.Person, 8), 2, SuccessionRule.Sibling, 19, "Hugh Vane"),
                    new(EntityId.Make(IdKind.Person, 9), 3, SuccessionRule.Parent, 47, "Maud Vane"),
                    new(EntityId.Make(IdKind.Person, 10), -1, SuccessionRule.Companion, 30, "Wat Hobbe"),
                };
                return new SuccessionView("Aldric Vane", L.T("su_cause_fight"), iron ? heirs.GetRange(0, 1) : heirs, heirs.Count, iron, _ => { }, iron ? null : () => { }, () => { }) { Name = "SuccessionView" };
            }
            case "loot":                                                        // P4 close-out: what the crawl brought out, in the pack
            {
                var box = new PanelContainer { Position = new Vector2(180, 90), CustomMinimumSize = new Vector2(600, 0) };
                var v = new VBoxContainer(); box.AddChild(v);
                v.AddChild(UiKit.Title(L.T("INV_TITLE")));
                foreach (var line in new[] { "Silver chalice  0.45 kg", "Reliquary  2.4 kg", "Arming sword (rusted, dirty)  1.1 kg", "Torch x1  0.5 kg" }) v.AddChild(UiKit.Body("  " + line));
                var toast = new PanelContainer { Position = new Vector2(180, 440), CustomMinimumSize = new Vector2(600, 0) };
                toast.AddChild(UiKit.Body(L.F("ex_ui_left_loot", 3, UiKit.Money(1480))));
                var root = new Control(); root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); root.AddChild(box); root.AddChild(toast);
                return root;
            }
            case "killed":
            {
                var rep = new DefeatReport(DefeatKind.Killed, WinnerKind.Draugr, "", 0, 0, [], [], false, 4, true, true, 0, now);
                var heirs = new[]
                {
                    new SuccessionCandidate(EntityId.Make(IdKind.Person, 7), 1, SuccessionRule.Child, 17, "Isolde Vane"),
                    new SuccessionCandidate(EntityId.Make(IdKind.Person, 8), 2, SuccessionRule.Spouse, 38, "Maud Vane"),
                    new SuccessionCandidate(EntityId.Make(IdKind.Person, 9), -1, SuccessionRule.Companion, 30, "Brand"),
                };
                return new DefeatView(rep, () => { }, heirs, ironman: false) { Name = "DefeatView" };
            }
            default:
            {
                var rep = new DefeatReport(DefeatKind.Robbed, WinnerKind.Bandits, "robber", 340, 20, ["arming_sword", "mail_hauberk", "cloth_wool", "javelin"],
                    ["shirt", "boots", "dagger", "bread"], true, 2, true, true, 0, now);
                return new DefeatView(rep, () => { }) { Name = "DefeatView" };
            }
        }
    }

    static EntityId EconomyStateHero => Ravensguard.Sim.Economy.EconomyState.HeroOwner;

    async Task Shot()
    {
        for (int i = 0; i < 8; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var img = GetViewport().GetTexture().GetImage();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_shotOut!) ?? ".");
        img.SavePng(_shotOut!);
        GD.Print("shot: " + _shotOut);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetTree().Quit(0);
    }
}
