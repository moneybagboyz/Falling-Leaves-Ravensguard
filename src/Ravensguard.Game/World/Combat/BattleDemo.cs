using System;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Content;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Abstract;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Morale;

namespace Ravensguard.Game.World.Combat;

/// <summary>
/// The report viewer on its own (P4-G; <c>-- --battle-demo [a|b|c|d]</c>, <c>--shot-out file.png</c>): an abstract battle of an equivalence scenario on the shipped content and
/// wound tables, shown as the chronicle tells it. The sim decides everything; this only draws.
/// </summary>
public partial class BattleDemo : Control
{
    string _what = "b";
    string? _shotOut;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        string Arg(string n, string d) { int i = Array.IndexOf(args, n); return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[i + 1] : d; }
        _what = Arg("--battle-demo", "b");
        _shotOut = Arg("--shot-out", ""); if (_shotOut.Length == 0) _shotOut = null;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Theme = PixelTheme.Build();
        var bg = new ColorRect { Color = new Color(0.12f, 0.11f, 0.10f) };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);
        var fc = FightDemo.Content();
        var tables = WoundTables.For(fc.Db);
        if (tables == null) { GD.PrintErr("BattleDemo: no wound tables in content"); GetTree().Quit(1); return; }
        var s = _what switch { "a" => AbstractScenarios.Scenario.A, "c" => AbstractScenarios.Scenario.C, "d" => AbstractScenarios.Scenario.D, _ => AbstractScenarios.Scenario.B };
        var input = AbstractEquivalence.InputOf(s, 20_017) with
        {
            Place = L.T("ab_demo_place"), SideKinds = new System.Collections.Generic.Dictionary<int, SideKind> { [0] = SideKind.Army, [1] = SideKind.Bandits }, FirstAid = true,
        };
        var b = new AbstractBattle(fc, tables, input);
        var rep = b.Run();
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);
        center.AddChild(new BattleReportView(rep, L.T("ab_demo_town"), L.T("ab_demo_side_a"), L.T("ab_demo_side_b")) { Name = "BattleReportView" });
        GD.Print($"BattleDemo ready: {s}, winner {rep.Winner}, {rep.DurationS} s, hash {rep.Hash:X16}");
        if (_shotOut != null) _ = Shot();
    }

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
