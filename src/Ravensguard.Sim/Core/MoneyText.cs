using System.Globalization;
using Ravensguard.Sim.Economy;

namespace Ravensguard.Sim.Core;

/// <summary>How a sum is shown (coinage 5.7). Sorting always uses mites.</summary>
public enum MoneyStyle : byte
{
    /// <summary>Money of account, greedy gp, sp, cp, zero parts dropped; a sum under one copper shows its tenths; from 100 gp the copper is dropped.</summary>
    Amount,
    /// <summary>As Amount with the mites as a tenth of a copper ("1 gp 2 sp 3.4 cp").</summary>
    Exact,
    /// <summary>The Industry tab's one unit (Q14): silver with one decimal ("14.8 sp").</summary>
    OneUnit,
    /// <summary>The one unit switched to copper ("148.1 cp").</summary>
    OneUnitCopper,
}

/// <summary>
/// P7-CN2 (coinage 5.7): the one function every money string goes through. The sim keeps mites; this turns an amount into text from a key table
/// (<c>mt_*</c> rows of the localisation, passed as <c>tr</c>), so the words are the game's. With no translator it falls back to the unit symbols
/// ("gp", "sp", "cp"), which read the same in every language. Pure: no state, integers only.
/// </summary>
public static class MoneyText
{
    public const string KeyGold = "mt_gp", KeySilver = "mt_sp", KeyCopper = "mt_cp", KeyPerKg = "mt_per_kg", KeyPerPack = "mt_per_pack", KeyEach = "mt_each", KeyPieces = "mt_pieces";
    /// <summary>The smallest pack (1, 10, 100, 1,000 kg) whose price reaches this many mites (3 cp) is the one shown.</summary>
    public const long PackFloorMites = 30;

    private static string Tr(Func<string, string>? tr, string key) => tr == null ? Default(key) : tr(key);
    private static string Default(string key) => key switch
    {
        KeyGold => "gp", KeySilver => "sp", KeyCopper => "cp", KeyPerKg => "a kg", KeyPerPack => "per {0} kg", KeyEach => "each",
        KeyPieces => "{0} gold, {1} silver, {2} copper ({3} g)", _ => key,
    };

    /// <summary>A key-to-text function that falls back to the built-in unit words for a key the table does not hold (a translator that hands the key back).</summary>
    public static Func<string, string> Words(Func<string, string>? tr) => tr == null ? Default : k => { var t = tr(k); return t.Length == 0 || t == k ? Default(k) : t; };

    public static string Format(long mites, MoneyStyle style = MoneyStyle.Amount, Func<string, string>? tr = null)
    {
        if (mites == long.MinValue) mites = long.MinValue + 1;
        bool neg = mites < 0; long a = neg ? -mites : mites;
        string body; bool zero;
        switch (style)
        {
            case MoneyStyle.OneUnit: { long t = (a + 5) / 10; body = Tenths(t) + " " + Tr(tr, KeySilver); zero = t == 0; break; }
            case MoneyStyle.OneUnitCopper: body = Tenths(a) + " " + Tr(tr, KeyCopper); zero = a == 0; break;
            default: body = Parts(a, style == MoneyStyle.Exact, tr); zero = a == 0; break;
        }
        return neg && !zero ? "-" + body : body;
    }

    /// <summary>1,481 tenths -> "148.1".</summary>
    private static string Tenths(long tenths) => (tenths / 10).ToString("N0", CultureInfo.InvariantCulture) + "." + (tenths % 10).ToString(CultureInfo.InvariantCulture);

    private static string Parts(long a, bool exact, Func<string, string>? tr)
    {
        if (!exact)
        {
            if (a >= 100_000) a = (a + 50) / 100 * 100;            // from 100 gp the copper is dropped
            else if (a >= 10) a = (a + 5) / 10 * 10;               // whole coppers
        }
        long g = a / Coin.MitesPerGold, s = a % Coin.MitesPerGold / Coin.MitesPerSilver, c = a % Coin.MitesPerSilver / Coin.MitesPerCopper, m = a % Coin.MitesPerCopper;
        var sb = new System.Text.StringBuilder(24);
        void Add(string n, string unit) { if (sb.Length > 0) sb.Append(' '); sb.Append(n).Append(' ').Append(unit); }
        if (g > 0) Add(g.ToString("N0", CultureInfo.InvariantCulture), Tr(tr, KeyGold));
        if (s > 0) Add(s.ToString(CultureInfo.InvariantCulture), Tr(tr, KeySilver));
        if (c > 0 || m > 0 || sb.Length == 0)
            Add(m == 0 ? c.ToString(CultureInfo.InvariantCulture) : c.ToString(CultureInfo.InvariantCulture) + "." + m.ToString(CultureInfo.InvariantCulture), Tr(tr, KeyCopper));
        return sb.ToString();
    }

    private static long PackMites(long microPerGram, int kg) => (microPerGram * 1000L * kg + 500_000) / 1_000_000;

    /// <summary>The pack size (1, 10, 100 or 1,000 kg) shown for a price in micro-mites a gram: the smallest whose price is at least <see cref="PackFloorMites"/>, else 1,000.</summary>
    public static int Pack(long microPerGram)
    {
        if (PackMites(microPerGram, 1) >= PackFloorMites) return 1;
        if (PackMites(microPerGram, 10) >= PackFloorMites) return 10;
        if (PackMites(microPerGram, 100) >= PackFloorMites) return 100;
        return 1000;
    }

    /// <summary>A bulk price as text (<paramref name="micro"/> micro-mites a gram, or a piece, as the books hold it): "4.5 cp a kg", "2 sp 6 cp per 100 kg", a piece good "3 sp each".</summary>
    public static string PerUnit(long micro, bool piece, Func<string, string>? tr = null)
    {
        if (piece) return Format((micro + 500_000) / 1_000_000, MoneyStyle.Exact, tr) + " " + Tr(tr, KeyEach);
        int pack = Pack(micro);
        string price = Format(PackMites(micro, pack), MoneyStyle.Exact, tr);
        return price + " " + (pack == 1 ? Tr(tr, KeyPerKg) : string.Format(CultureInfo.InvariantCulture, Tr(tr, KeyPerPack), pack.ToString("N0", CultureInfo.InvariantCulture)));
    }

    /// <summary>The pieces a purse holds ("3 gold, 47 silver, 6 copper (26 g)").</summary>
    public static string Pieces(long gold, long silver, long copper, long massGrams, Func<string, string>? tr = null)
        => string.Format(CultureInfo.InvariantCulture, Tr(tr, KeyPieces), gold, silver, copper, massGrams);

    /// <summary>
    /// The pieces of a purse worth <paramref name="mites"/> that holds <paramref name="goldPieces"/> gold pieces (0 for a purse that tracks no gold): the gold counted, silver and copper from the rest, and the coins' mass.
    /// 12,345 mites with no gold: "123 silver, 4 copper (160 g)" (the mites under a copper are not a piece).
    /// </summary>
    public static string PiecesOf(long mites, long goldPieces = 0, Func<string, string>? tr = null)
    {
        long m = Math.Max(0, mites), g = Math.Min(Math.Max(0, goldPieces), m / Coin.MitesPerGold), rest = m - g * Coin.MitesPerGold;
        return Pieces(g, rest / Coin.MitesPerSilver, rest % Coin.MitesPerSilver / Coin.MitesPerCopper, Inventory.Encumbrance.CoinMassG(m), tr);
    }
}

/// <summary>
/// The <c>{n:money}</c>, <c>{n:money_exact}</c> and <c>{n:money_unit}</c> parameter kinds of the localisation rows (coinage 5.7): pass it to <c>string.Format</c> as the provider and the argument (an amount in mites,
/// any integer type) is shown by <see cref="MoneyText"/>. Every other format string and argument is left to the ordinary formatting.
/// </summary>
public sealed class MoneyFormat : IFormatProvider, ICustomFormatter
{
    private readonly Func<string, string>? _words;
    public MoneyFormat(Func<string, string>? tr) { _words = MoneyText.Words(tr); }
    public static readonly MoneyFormat Default = new(null);

    public object? GetFormat(Type? t) => t == typeof(ICustomFormatter) ? this : null;

    public string Format(string? fmt, object? arg, IFormatProvider? provider)
    {
        MoneyStyle? style = fmt switch { "money" => MoneyStyle.Amount, "money_exact" => MoneyStyle.Exact, "money_unit" => MoneyStyle.OneUnit, _ => null };
        if (style is { } st && arg is long or int or short or byte or uint or ushort) return MoneyText.Format(Convert.ToInt64(arg, CultureInfo.InvariantCulture), st, _words);
        if (arg is IFormattable f) return f.ToString(fmt, CultureInfo.InvariantCulture);
        return arg?.ToString() ?? "";
    }
}
