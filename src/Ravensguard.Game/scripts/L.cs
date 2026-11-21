using Godot;
using Ravensguard.Sim.Core;

namespace Ravensguard.Game;

/// <summary>Localisation helpers. All UI text goes through these (tr keys live in localization/en.csv).</summary>
public static class L
{
    public static string T(string key) => TranslationServer.Translate(key);
    /// <summary>P7-CN2: rows may hold <c>{n:money}</c>, <c>{n:money_exact}</c> and <c>{n:money_unit}</c> (an amount in mites; coinage 5.7).</summary>
    static readonly MoneyFormat Fmt = new(T);
    public static string Format(string template, params object[] args) => string.Format(Fmt, template, args);
    public static string F(string key, params object[] args) => string.Format(Fmt, T(key), args);
}
