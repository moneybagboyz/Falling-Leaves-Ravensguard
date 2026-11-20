using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Sim.Core;

namespace Ravensguard.Game.UI.NewGame;

/// <summary>
/// The honest named sliders as rows of the Quick Start's Advanced panel (difficulty-sliders.md 6): one cycling button per slider with a one-line description of exactly what it changes, then the
/// read-only line for the world's ageing pace when the world comes from the library. Nothing adjusts itself; the default is the game as calibrated.
/// </summary>
public sealed class DifficultyRows
{
    readonly Func<Difficulty> _get;
    readonly Action<Difficulty> _set;
    readonly List<(Button Button, Label Help, Func<string> Text, Func<string> HelpText)> _rows = [];
    Label _paceFixed = null!;
    Button _paceButton = null!;

    public DifficultyRows(VBoxContainer into, Func<Difficulty> get, Action<Difficulty> set)
    {
        _get = get; _set = set;
        into.AddChild(UiKit.Hint(L.T("DIFF_HELP"), true));
        Row(into, "DIFF_WOUNDS", d => L.T("DIFF_WOUNDS_" + d.WoundLethalityPct), d => L.F("DIFF_WOUNDS_HELP", d.WoundLethalityPct),
            d => d with { WoundLethalityPct = Next(Difficulty.WoundChoices, d.WoundLethalityPct) });
        Row(into, "DIFF_BANDITS", d => L.F("UI_PCT", d.BanditPct), d => L.F("DIFF_BANDITS_HELP", d.BanditPct),
            d => d with { BanditPct = Next(Difficulty.BanditChoices, d.BanditPct) });
        Row(into, "DIFF_ECONOMY", d => L.T("DIFF_ECONOMY_" + d.Economy.ToString().ToUpperInvariant()), d => L.F("DIFF_ECONOMY_HELP", d.OriginCoinPct, d.HarvestVariancePct, d.SpoilagePct),
            d => d with { Economy = (EconomyHarshness)(((int)d.Economy + 1) % 3) });
        Row(into, "DIFF_PLAGUE", d => L.T("DIFF_PLAGUE_" + d.Plague.ToString().ToUpperInvariant()), d => L.F("DIFF_PLAGUE_HELP", PlagueStartText(d), d.PlagueFatalityPct(100)),
            d => d with { Plague = (PlagueSeverity)(((int)d.Plague + 1) % 4) });
        _paceButton = Row(into, "DIFF_PACE", d => L.T("DIFF_PACE_" + d.AgingPace), d => L.F("DIFF_PACE_HELP", d.AgingPace),
            d => d with { AgingPace = Next(Difficulty.PaceChoices, d.AgingPace) });
        _paceFixed = UiKit.Hint("", true);
        into.AddChild(_paceFixed);
        Refresh(false, 1);
    }

    static string PlagueStartText(Difficulty d) => d.Plague switch { PlagueSeverity.Off => "0%", PlagueSeverity.Rare => "0.5%", PlagueSeverity.Frequent => "2.5%", _ => "1%" };

    static int Next(int[] choices, int cur) => choices[(Array.IndexOf(choices, cur) + 1) % choices.Length];

    Button Row(VBoxContainer into, string labelKey, Func<Difficulty, string> value, Func<Difficulty, string> help, Func<Difficulty, Difficulty> next)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        row.AddChild(new Label { Text = L.T(labelKey), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var b = new Button { CustomMinimumSize = new Vector2(170, 28) };
        row.AddChild(b);
        into.AddChild(row);
        var h = UiKit.Hint("", true);
        into.AddChild(h);
        b.Pressed += () => { _set(next(_get())); RefreshText(); };
        _rows.Add((b, h, () => value(_get()), () => help(_get())));
        return b;
    }

    void RefreshText() { foreach (var r in _rows) { r.Button.Text = r.Text(); r.Help.Text = r.HelpText(); } }

    /// <summary>The ageing pace belongs to the world: a library world shows its own, read-only; a new world lets the player pick.</summary>
    public void Refresh(bool libraryWorld, int worldPace)
    {
        _paceButton.Disabled = libraryWorld;
        _paceFixed.Visible = libraryWorld;
        _paceFixed.Text = libraryWorld ? L.F("DIFF_PACE_FIXED", worldPace) : "";
        RefreshText();
    }

    /// <summary>The non-default values of a run's record as one short line ("" when every slider is at its default), for the world library card and the run list.</summary>
    public static string NonDefault(int[]? values)
    {
        var d = Difficulty.FromArray(values);
        if ((d with { Ironman = false }).IsDefault) return "";
        var parts = new List<string>();
        if (d.WoundLethalityPct != 100) parts.Add(L.F("DIFF_SHORT_WOUNDS", d.WoundLethalityPct));
        if (d.BanditPct != 100) parts.Add(L.F("DIFF_SHORT_BANDITS", d.BanditPct));
        if (d.Economy != EconomyHarshness.Normal) parts.Add(L.F("DIFF_SHORT_ECONOMY", L.T("DIFF_ECONOMY_" + d.Economy.ToString().ToUpperInvariant())));
        if (d.Plague != PlagueSeverity.Normal) parts.Add(L.F("DIFF_SHORT_PLAGUE", L.T("DIFF_PLAGUE_" + d.Plague.ToString().ToUpperInvariant())));
        if (d.AgingPace != 1) parts.Add(L.F("DIFF_SHORT_PACE", d.AgingPace));
        return string.Join(", ", parts);
    }
}
