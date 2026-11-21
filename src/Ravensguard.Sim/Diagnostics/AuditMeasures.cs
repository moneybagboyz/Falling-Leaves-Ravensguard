using Ravensguard.Content;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Diagnostics;

/// <summary>
/// The audit's pure measures over the content (P7-EA2): each is a function of the loaded content and the market constants only, so the <c>econ-audit</c> command prints it, the probe tests assert it and the findings file quotes it.
/// </summary>
public static class AuditMeasures
{
    public const int UnskilledWagePerDay = 150;

    /// <summary>X5: every loot row's material value (<c>ItemMath.MaterialMites</c>, as the pack values the item made from it) over its listed value; rows listed at nothing are skipped.</summary>
    public static List<(string Id, int MassG, int Listed, string Material, long MatMites, double Ratio)> LootRatios(ContentDatabase db)
    {
        var list = new List<(string, int, int, string, long, double)>();
        foreach (var it in db.All<SiteItemDef>().OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            string mat = it.Material.Length > 0 ? it.Material : "bone";
            if (it.ValueMites <= 0 || !db.TryGet<MaterialDef>(mat, out var m)) continue;
            long melt = ItemMath.MaterialMites(it.MassG, m.ValuePerKg);
            list.Add((it.Id, it.MassG, it.ValueMites, mat, melt, (double)melt / it.ValueMites));
        }
        return list;
    }

    /// <summary>
    /// X5 for the loot tables' own rows (what the site budget spends): every entry that places a site item, at the entry's mass and the material the pack will make it of (the entry's, else the item's),
    /// as material value over the entry's listed value. A table row that disagrees with the pack is the finding EA_F2 again.
    /// </summary>
    public static List<(string Table, string Item, int MassG, int Listed, string Material, long MatMites, double Ratio)> LootEntryRatios(ContentDatabase db)
    {
        var items = db.All<SiteItemDef>().ToDictionary(x => x.Id, StringComparer.Ordinal);
        var list = new List<(string, string, int, int, string, long, double)>();
        foreach (var tb in db.All<SiteLootTableDef>().OrderBy(x => x.Id, StringComparer.Ordinal))
            foreach (var e in tb.Entries)
            {
                if (!items.TryGetValue(e.Item, out var it) || e.ValueMites <= 0) continue;
                string mat = e.Material.Length > 0 ? e.Material : it.Material.Length > 0 ? it.Material : "bone";
                if (!db.TryGet<MaterialDef>(mat, out var m)) continue;
                long melt = ItemMath.MaterialMites(e.MassG, m.ValuePerKg);
                list.Add((tb.Id, e.Item, e.MassG, e.ValueMites, mat, melt, (double)melt / e.ValueMites));
            }
        return list;
    }

    /// <summary>
    /// S-1 / I4: a worker-day of each bulk raw kind (40 kg or more a day) at the content's base price, over the unskilled day wage. A generated mine's ore is the industries' composite
    /// (hew, haul and dress, 109 kg of dressed ore a worker-day, <c>IndustryRates.Composite</c>), so for the composite good the composite rate replaces the extraction row's own.
    /// </summary>
    public static List<(string Id, string Good, double Kg, double Price, double Value, double Ratio)> RawRatios(ContentDatabase db, GoodCatalog cat)
    {
        var ind = Ravensguard.Sim.Production.Industry.IndustryContent.Load(db, cat);
        string compGood = ind.CompositeGPerWorkerDay > 0 ? cat.StringId(ind.CompositeGood) : "";
        var list = new List<(string, string, double, double, double, double)>();
        foreach (var k in db.All<ExtractionDef>().Where(x => x.RateGPerWorkerDay >= 40_000).OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            double kg = (k.OutputGood == compGood ? ind.CompositeGPerWorkerDay : k.RateGPerWorkerDay) / 1000.0, price = cat.Def(cat.IdOf(k.OutputGood)).BasePriceMitesPerKg, value = kg * price;
            list.Add((k.Id, k.OutputGood, kg, price, value, value / UnskilledWagePerDay));
        }
        return list;
    }

    /// <summary>S-2: silver from a worker-day of silver mining (galena smelted to argentiferous lead, then cupelled: 3 kg of silver from 1,000 kg of galena) at silver's base price.</summary>
    public static (double GalenaKg, double SilverKg, double Mites) SilverPerWorkerDay(ContentDatabase db, GoodCatalog cat)
    {
        var mine = db.Get<ExtractionDef>("mining_silver"); double galena = mine.RateGPerWorkerDay / 1000.0;
        double silver = galena * 0.6 / 600 * 3;
        return (galena, silver, silver * cat.Def(cat.IdOf("silver")).BasePriceMitesPerKg);
    }

    /// <summary>X2: the border desk's export bid (after the seller's toll) over the book's ask, for a non-staple at the target stock and at the glut threshold where the desk acts, and for a staple at the threshold.</summary>
    public static (double AtTarget, double AtGlut, double StapleAtGlut) DeskOverAsk(MoneySettingsDef money)
    {
        const long Ref = 45_000;
        var c = new PriceCurve(Ref, 1_000_000, money.PriceClampMinPermille, money.PriceClampMaxPermille);
        double Ask(long stock) => c.MidMicro(stock) * (1000 + money.SpreadHalfPermille) / 1000.0;
        double Bid(int discount) => Ref * discount / 1000.0 * (1000 - money.MarketTollPermille) / 1000.0;
        long glut = 1_000_000L * ContinentTrade.GlutAbovePermilleOfCover / 1000;
        return (Bid(ContinentTrade.ExportDiscountPermille) / Ask(1_000_000), Bid(ContinentTrade.ExportDiscountPermille) / Ask(glut), Bid(ContinentTrade.StapleExportDiscountPermille) / Ask(glut));
    }

    /// <summary>X3 / X4: how many build pieces with materials there are, the dearest share of materials a demolition returns, and how many return more than 70% (a loss under 30%).</summary>
    public static (int Pieces, int MaxSalvagePm, int Over700) Salvage(ContentDatabase db)
    {
        var p = db.All<BuildPieceDef>().Where(x => x.Build is { Materials.Count: > 0 }).ToList();
        return (p.Count, p.Max(x => x.Build!.SalvagePm), p.Count(x => x.Build!.SalvagePm > 700));
    }
}
