using Godot;

namespace Ravensguard.Game;

/// <summary>Display and accessibility settings, stored in user://settings.cfg.</summary>
public static class Settings
{
    public const string Path = "user://settings.cfg";
    /// <summary>0 = Auto (floor(window height / 540)), otherwise 1..3.</summary>
    public static int UiScale { get; set; } = 0;
    public static bool ScreenShake { get; set; } = true;
    public static bool Flashes { get; set; } = true;
    public static bool SlowMotion { get; set; } = true;

    public static void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok) return;
        UiScale = Mathf.Clamp((int)cfg.GetValue("display", "ui_scale", 0), 0, 3);
        ScreenShake = (bool)cfg.GetValue("accessibility", "screen_shake", true);
        Flashes = (bool)cfg.GetValue("accessibility", "flashes", true);
        SlowMotion = (bool)cfg.GetValue("accessibility", "slow_motion", true);
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

    /// <summary>Integer UI scale only: Auto = max(1, floor(height / 540)), or a fixed 1x/2x/3x.</summary>
    public static int EffectiveScale(Window window) =>
        UiScale > 0 ? UiScale : Mathf.Max(1, window.Size.Y / 540);

    public static void ApplyScale(Window window) => window.ContentScaleFactor = EffectiveScale(window);
}
