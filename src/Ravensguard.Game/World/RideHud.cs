using System;
using Godot;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Carts;

namespace Ravensguard.Game.World;

/// <summary>
/// The mount bar and the cart bar (horses-and-carts spec 6): a stamina bar that goes green, amber and red, the gait, fodder, shoes and a Lame flag, the speed in m/s and km/h; for a cart, load against
/// capacity, axle load, pull ratio and a stuck warning. Reads <see cref="RideView"/> and nothing else; hidden on foot.
/// </summary>
public sealed partial class RideHud : PanelContainer
{
    readonly Label _title = new() { ThemeTypeVariation = "TitleLabel" }, _line1 = new(), _line2 = new(), _warn = new() { Modulate = new Color(1f, 0.55f, 0.4f) };
    readonly ColorRect _barBack = new() { Color = new Color(0.12f, 0.1f, 0.08f, 0.9f), CustomMinimumSize = new Vector2(220, 10) }, _bar = new() { Color = new Color(0.45f, 0.8f, 0.4f) };

    public RideHud()
    {
        MouseFilter = MouseFilterEnum.Ignore; Visible = false;
        AnchorTop = 1; AnchorBottom = 1; OffsetLeft = 12; OffsetTop = -150; OffsetBottom = -40; GrowVertical = GrowDirection.Begin;
        var v = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        v.AddThemeConstantOverride("separation", 2);
        AddChild(v);
        v.AddChild(_title); v.AddChild(_barBack); _barBack.AddChild(_bar); _bar.SetAnchorsAndOffsetsPreset(LayoutPreset.LeftWide);
        v.AddChild(_line1); v.AddChild(_line2); v.AddChild(_warn);
    }

    static string Gait(Gait g) => L.T(g switch { Ravensguard.Sim.Animals.Gait.Gallop => "RIDE_GALLOP", Ravensguard.Sim.Animals.Gait.Trot => "RIDE_TROT", _ => "RIDE_WALK" });
    static string Speed(int cmps) => L.F("RIDE_SPEED", (cmps / 100.0).ToString("0.0"), (cmps * 0.036).ToString("0"));

    public void Update(RideBook book, long nowSec)
    {
        var v = book.View(nowSec);
        Visible = v.Mounted || v.Driving || v.Mounting;
        if (!Visible) return;
        if (v.Mounting) { _title.Text = L.T("RIDE_MOUNTING"); _barBack.Visible = false; _line1.Text = ""; _line2.Text = ""; _warn.Text = ""; return; }
        _barBack.Visible = v.Mounted;
        if (v.Mounted)
        {
            var a = book.MountedAnimal!; var b = book.Breeds.At(v.BreedIndex);
            _title.Text = b.Def.Name;
            float f = Math.Clamp(v.StaminaPm / 1000f, 0f, 1f);
            _bar.AnchorRight = f; _bar.OffsetRight = 0;
            _bar.Color = f > 0.5f ? new Color(0.45f, 0.8f, 0.4f) : f > 0.15f ? new Color(0.9f, 0.7f, 0.25f) : new Color(0.85f, 0.3f, 0.25f);
            _line1.Text = L.F("RIDE_LINE1", L.T("RIDE_STAMINA"), v.StaminaPm / 10, Gait(v.Gait), Speed(v.SpeedCmps));
            _line2.Text = L.F("RIDE_LINE2", v.FeedCenti >= 100 ? L.T("RIDE_FED_OK") : L.F("RIDE_FED_LOW", v.FeedCenti), v.ShoePm / 10, v.FatiguePm / 10);
            _warn.Text = v.Lameness > 0 ? L.T("RIDE_LAME") : a.Winded ? L.T("RIDE_WINDED") : v.FatiguePm >= RideMath.TiredPm ? L.T("RIDE_TIRED") : "";
        }
        else
        {
            var bp = book.Templates.At(v.TemplateIndex);
            _title.Text = bp.Def.Name;
            _line1.Text = L.F("RIDE_CART1", v.CargoKg, v.CargoCapKg, Speed(v.SpeedCmps));
            _line2.Text = L.F("RIDE_CART2", v.AxlePm / 10, Math.Min(v.RatioPm, 9999) / 10, book.Surface.ToString());
            _warn.Text = v.Stuck ? L.T("RIDE_STUCK") : v.RatioPm > 1000 ? L.T("RIDE_HEAVY") : "";
        }
    }
}
