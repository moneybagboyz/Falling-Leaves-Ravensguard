using System.Text.RegularExpressions;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Trade;
using Xunit;

namespace Ravensguard.Sim.Tests.Economy;

/// <summary>P7-CN2 (coinage 5.7): CN-1, CN-11 and CN-12, the display of money.</summary>
public class MoneyTextTests
{
    // CN-11
    [Theory]
    [InlineData(1_234, "1 gp 2 sp 3 cp")]
    [InlineData(150, "1 sp 5 cp")]
    [InlineData(250_000, "250 gp")]
    [InlineData(1_000, "1 gp")]
    [InlineData(0, "0 cp")]
    [InlineData(4, "0.4 cp")]
    [InlineData(10, "1 cp")]
    [InlineData(100_049, "100 gp")]
    [InlineData(100_050, "100 gp 1 sp")]          // from 100 gp the copper is dropped (rounded to the silver)
    [InlineData(-150, "-1 sp 5 cp")]
    [InlineData(1_234_567, "1,234 gp 6 sp")]
    public void Amount(long mites, string text) => Assert.Equal(text, MoneyText.Format(mites));

    [Theory]
    [InlineData(1_234, "1 gp 2 sp 3.4 cp")]
    [InlineData(12_345, "12 gp 3 sp 4.5 cp")]      // CN-1
    [InlineData(5, "0.5 cp")]
    [InlineData(100, "1 sp")]
    public void Exact(long mites, string text) => Assert.Equal(text, MoneyText.Format(mites, MoneyStyle.Exact));

    [Fact]
    public void OneUnit()
    {
        Assert.Equal("14.8 sp", MoneyText.Format(1_481, MoneyStyle.OneUnit));
        Assert.Equal("148.1 cp", MoneyText.Format(1_481, MoneyStyle.OneUnitCopper));
        Assert.Equal("-14.8 sp", MoneyText.Format(-1_481, MoneyStyle.OneUnit));
        Assert.Equal("1,234.6 sp", MoneyText.Format(123_456, MoneyStyle.OneUnit));
        Assert.Equal("0.0 sp", MoneyText.Format(-3, MoneyStyle.OneUnit));
        Assert.Equal("67.9 sp", IndustryViewsSilver(6_790));
    }
    private static string IndustryViewsSilver(long m) => Ravensguard.Sim.Production.Industry.IndustryViews.Silver(m);

    [Fact]
    public void PerUnit()
    {
        long Micro(double mitesPerKg) => (long)(mitesPerKg * PriceCurveMicro / 1000);      // micro-mites a gram
        Assert.Equal("2 sp 6 cp per 100 kg", MoneyText.PerUnit(Micro(2.6), false));         // iron_ore
        Assert.Equal("4.5 cp a kg", MoneyText.PerUnit(Micro(45), false));                   // wheat
        Assert.Equal("1 sp 9.4 cp a kg", MoneyText.PerUnit(Micro(194), false));
        Assert.Equal("1 sp 1 cp per 10 kg", MoneyText.PerUnit(Micro(11), false));           // 11 mites a kg: 110 mites for ten
        Assert.Equal("3 sp each", MoneyText.PerUnit(300 * PriceCurveMicro, true));
        Assert.Equal(1_000, MoneyText.Pack(1));                                             // a dust-cheap good is shown per tonne
        Assert.Equal(1, MoneyText.Pack(Micro(45)));
    }
    private const long PriceCurveMicro = Ravensguard.Sim.Economy.Markets.PriceCurve.Micro;

    [Fact]
    public void Pieces()
    {
        // CN-1: 12,345 mites in a purse that tracks no gold
        Assert.Equal("0 gold, 123 silver, 4 copper (160 g)", MoneyText.PiecesOf(12_345));
        Assert.Equal("3 gold, 47 silver, 6 copper (100 g)", MoneyText.PiecesOf(7_760, 3));
    }

    [Fact]
    public void FormatProviderKinds()
    {
        Assert.Equal("Paid 1 gp 2 sp 3 cp, 5 pieces", string.Format(MoneyFormat.Default, "Paid {0:money}, {1} pieces", 1_234L, 5));
        Assert.Equal("1 gp 2 sp 3.4 cp", string.Format(MoneyFormat.Default, "{0:money_exact}", 1_234));
        Assert.Equal("14.8 sp", string.Format(MoneyFormat.Default, "{0:money_unit}", 1_481L));
        Assert.Equal("007", string.Format(MoneyFormat.Default, "{0:000}", 7));
        // the words come from the key table when one is given, and fall back to the symbols for a key it lacks
        Assert.Equal("1 or 5 pe", MoneyText.Format(1_050, MoneyStyle.Amount, k => k == "mt_gp" ? "or" : k == "mt_cp" ? "pe" : k));
        Assert.Equal("1 gp", MoneyText.Format(1_000, MoneyStyle.Amount, MoneyText.Words(k => k)));
    }

    [Fact]
    public void QuotesAndChronicleSpeakCoin()
    {
        Assert.Equal("3 gp 8 sp 9 cp", Quotes.Money(3_888));
        Assert.Equal("1 sp 9.4 cp a kg", Quotes.PerUnit(194_000, false));
        var text = ChronicleTypes.FormatNote(new Cause(CauseKind.Trade, null, NoteKey: "k", NoteParams: [ChronParam.Mites("amount", 12_345)]), k => k == "k" ? "paid {amount}" : k, (_, _) => "");
        Assert.Equal("paid 12 gp 3 sp 5 cp", text);
    }

    // CN-12: no English money in the sim or the game's strings
    static string Root()
    {
        for (var d = new DirectoryInfo(Directory.GetCurrentDirectory()); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "src", "Ravensguard.Game", "localization", "en.csv"))) return d.FullName;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "src", "Ravensguard.Game", "localization", "en.csv"))) return d.FullName;
        throw new FileNotFoundException("repository root");
    }

    [Fact]
    public void NoOldMoneyWordsInTheRows()
    {
        var rows = EnCsv.Load(Path.Combine(Root(), "src", "Ravensguard.Game", "localization", "en.csv"));
        var old = new Regex(@"\b(mites|crowns?|cr)\b", RegexOptions.IgnoreCase);
        var bad = rows.Where(r => old.IsMatch(r.Value) && r.Key != "ch_dp_succession").Select(r => r.Key + ": " + r.Value).ToList();
        Assert.True(bad.Count == 0, "rows that still say mites, crowns or cr: " + string.Join(" | ", bad));
        // every money-bearing row uses the money kind: the 22 rows that were rewritten
        foreach (var k in new[] { "AP_CROWNS_DAY", "AP_CROWNS", "ONB_DEBT", "vd_commissioned", "fm_gift_given", "fm_wed", "fam_courting", "fam_talk_state", "fam_talk_gift", "fa_btn_charm", "fm_dowry_range", "fam_match_betrothed", "fam_match_alliance", "fam_arrange_line", "fam_arrange_line_alliance", "fam_arrange_line_consent", "co_sold_metal", "co_changed" })
            Assert.Contains(":money}", rows[k]);
    }

    [Fact]
    public void NoOldMoneyFormattersInTheGame()
    {
        var game = Path.Combine(Root(), "src", "Ravensguard.Game");
        var bad = new List<string>();
        foreach (var f in Directory.EnumerateFiles(game, "*.cs", SearchOption.AllDirectories))
            foreach (var (line, i) in File.ReadLines(f).Select((l, i) => (l, i + 1)))
            {
                string code = line.TrimStart();
                if (code.StartsWith("//") || code.StartsWith("///")) continue;
                if (code.Contains("Quotes.Money(") || code.Contains("Quotes.PerUnit(") || Regex.IsMatch(code, "\"[^\"]*(\\bcr\\b|\\bcrowns?\\b|\\bmites\\b)[^\"]*\"")) bad.Add(Path.GetFileName(f) + ":" + i);
            }
        Assert.True(bad.Count == 0, "old money text in the game: " + string.Join(", ", bad.Take(20)));
    }
}
