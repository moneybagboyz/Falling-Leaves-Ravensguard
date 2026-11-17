using Godot;

namespace Ravensguard.Game;

/// <summary>Display and accessibility settings, stored in user://settings.cfg.</summary>
public static class Settings
{
    public const string Path = "user://settings.cfg";
    /// <summary>0 = Auto (the largest whole scale at which a 960x540 layout still fits the window), otherwise 1..3.</summary>
    public static int UiScale { get; set; } = 0;
    // The three accessibility toggles below have no readers yet. Effects code MUST go through the
    // Accessibility facade (never read these or ignore them) so the toggles work from day one.
    public static bool ScreenShake { get; set; } = true;
    public static bool Flashes { get; set; } = true;
    public static bool SlowMotion { get; set; } = true;

    public static void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok) return;
        UiScale = Mathf.Clamp(ReadInt(cfg, "display", "ui_scale", 0), 0, 3);
        ScreenShake = ReadBool(cfg, "accessibility", "screen_shake", true);
        Flashes = ReadBool(cfg, "accessibility", "flashes", true);
        SlowMotion = ReadBool(cfg, "accessibility", "slow_motion", true);
    }

    // Type-checked reads: a hand-edited or corrupt file falls back to the default instead of throwing InvalidCastException.
    static bool ReadBool(ConfigFile cfg, string section, string key, bool def)
    {
        var v = cfg.GetValue(section, key, def);
        return v.VariantType == Variant.Type.Bool ? v.AsBool() : def;
    }

    static int ReadInt(ConfigFile cfg, string section, string key, int def)
    {
        var v = cfg.GetValue(section, key, def);
        return v.VariantType == Variant.Type.Int ? (int)v.AsInt64() : def;
    }

    public static void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue("display", "ui_scale", UiScale);
        cfg.SetValue("accessibility", "screen_shake", ScreenShake);
        cfg.SetValue("accessibility", "flashes", Flashes);
        cfg.SetValue("accessibility", "slow_motion", SlowMotion);
        var err = cfg.Save(Path);
        if (err != Error.Ok) GD.PushWarning($"Could not save {Path}: {err}");
    }

    /// <summary>Integer UI scale only: Auto = clamp(min(height / 540, width / 960), 1, 3), or a fixed 1x/2x/3x.</summary>
    public static int EffectiveScale(Window window) =>
        UiScale > 0 ? UiScale : AutoScale(window.Size.X, window.Size.Y);

    public static int AutoScale(int width, int height) => System.Math.Clamp(System.Math.Min(height / 540, width / 960), 1, 3);

    public static void ApplyScale(Window window) => window.ContentScaleFactor = EffectiveScale(window);
}

/// <summary>
/// Read-side of the accessibility toggles. Screen shake, flashes (white flashes, strobing, hit-flash) and slow motion
/// (hit-stop, time dilation) code must ask here before doing anything: Accessibility.ShakeScale(1f) etc.
/// </summary>
public static class Accessibility
{
    public static bool ScreenShakeEnabled => Settings.ScreenShake;
    public static bool FlashesEnabled => Settings.Flashes;
    public static bool SlowMotionEnabled => Settings.SlowMotion;

    /// <summary>Scales a shake amplitude: 0 when screen shake is off.</summary>
    public static float ShakeScale(float amplitude) => ScreenShakeEnabled ? amplitude : 0f;
    /// <summary>Returns a flash intensity of 0 when flashes are off.</summary>
    public static float FlashScale(float intensity) => FlashesEnabled ? intensity : 0f;
    /// <summary>Time scale to use for dramatic slow motion: 1 (normal speed) when slow motion is off.</summary>
    public static float TimeScale(float slowMotionScale) => SlowMotionEnabled ? slowMotionScale : 1f;
}
