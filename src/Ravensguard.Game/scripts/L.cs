using Godot;

namespace Ravensguard.Game;

/// <summary>Localisation helpers. All UI text goes through these (tr keys live in localization/en.csv).</summary>
public static class L
{
    public static string T(string key) => TranslationServer.Translate(key);
    public static string F(string key, params object[] args) => string.Format(T(key), args);
}
