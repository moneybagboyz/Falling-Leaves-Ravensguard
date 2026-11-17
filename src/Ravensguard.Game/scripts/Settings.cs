using Godot;

namespace Ravensguard.Game;

/// <summary>Display and accessibility settings, stored in user://settings.cfg.</summary>
public static class Settings
{
    public const string Path = "user://settings.cfg";
    public static int UiScale { get; set; } = 1;
    public static bool ScreenShake { get; set; } = true;
    public static bool Flashes { get; set; } = true;
    public static bool SlowMotion { get; set; } = true;

    public static void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok) return;
        UiScale = Mathf.Clamp((int)cfg.GetValue("display", "ui_scale", 1), 1, 3);
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

    /// <summary>Integer UI scale only (1x, 2x, 3x).</summary>
    public static void ApplyScale(Window window) => window.ContentScaleFactor = Mathf.Clamp(UiScale, 1, 3);
}
