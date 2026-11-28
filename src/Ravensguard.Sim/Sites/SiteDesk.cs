using System.Globalization;
using System.Text;
using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Sites;

/// <summary>
/// The hero's side of the sites at the barrier (the hero desk calls it for <see cref="HeroCommandKind.Site"/>; P4 close-out): what a crawl brought out goes into the pack as item rows and
/// the torches it burnt come out of the pack's <c>torch</c> goods ("ex:take"); a search of an old battlefield ("ex:search") and a dig at a hoard ("ex:dig") are rolled here, under the
/// site book's lock, on the record's visit counter, and the find goes into the pack. The main thread only sends the command (after the search's or the dig's clock time has passed), so
/// every one of them is on the replay log, saved with the pending commands and applied in sequence order on the tick worker. Nothing is minted: a looted thing is the mass that left the
/// site's container (one item row of that mass), and coin found in a stash stays a thing of silver until it is sold.
/// </summary>
public static class SiteDesk
{
    public const string TakePrefix = "ex:take|", SearchPrefix = "ex:search|", DigPrefix = "ex:dig|", OpenMinePrefix = "ex:openmine|";
    /// <summary>Clock time of a search of one cell (60 sim-seconds, a game minute's twentieth each) and of a dig (120 sim-seconds), in game seconds.</summary>
    public const long SearchSec = 1200, DigSec = 2400;

    /// <summary>The crawl's haul and the lights it burnt, encoded in the tag so the command alone says what moves (saved and replayed as it was sent).</summary>
    public static HeroCommand TakeCommand(ulong site, IReadOnlyList<LootItem> loot, int torchesBurnt, long sec)
    {
        var sb = new StringBuilder(TakePrefix);
        sb.Append(CultureInfo.InvariantCulture, $"{site}|{torchesBurnt}");
        foreach (var it in loot)
            sb.Append(CultureInfo.InvariantCulture, $"|{it.Item},{it.Material},{it.ValueMites},{it.MassG},{it.ConditionPm},{(it.Dirty ? 1 : 0) | (it.Grave ? 2 : 0)},{Uri.EscapeDataString(it.Named)}");
        return new HeroCommand { Kind = HeroCommandKind.Site, IssuedSec = sec, Tag = sb.ToString(), Origin = CommandOrigin.Human };
    }

    /// <summary>P8-LM-3: the hero opens a prospected deposit as a walkable mine (a level for each 10 m of depth): the tile of the region, the mineral, its tonnage, the depth in metres and a name.</summary>
    public static HeroCommand OpenMineCommand(int tileX, int tileY, int mineral, int tonnage, int depthM, string name, long sec)
        => new() { Kind = HeroCommandKind.Site, IssuedSec = sec, Tag = string.Create(CultureInfo.InvariantCulture, $"{OpenMinePrefix}{tileX}|{tileY}|{mineral}|{tonnage}|{depthM}|{Uri.EscapeDataString(name)}"), Origin = CommandOrigin.Human };

    public static HeroCommand SearchCommand(ulong site, long sec) => new() { Kind = HeroCommandKind.Site, IssuedSec = sec, Tag = SearchPrefix + site.ToString(CultureInfo.InvariantCulture), Origin = CommandOrigin.Human };
    public static HeroCommand DigCommand(ulong site, long sec) => new() { Kind = HeroCommandKind.Site, IssuedSec = sec, Tag = DigPrefix + site.ToString(CultureInfo.InvariantCulture), Origin = CommandOrigin.Human };

    /// <summary>The haul and the burnt torches of a take command (null when the tag is not one).</summary>
    public static (ulong Site, int Torches, List<LootItem> Loot)? DecodeTake(string tag)
    {
        if (!tag.StartsWith(TakePrefix, StringComparison.Ordinal)) return null;
        var p = tag[TakePrefix.Length..].Split('|');
        if (p.Length < 2 || !ulong.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var site) || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var torches)) return null;
        var loot = new List<LootItem>();
        for (int k = 2; k < p.Length; k++)
        {
            var f = p[k].Split(',');
            if (f.Length < 7) return null;
            int I(int i) => int.TryParse(f[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
            int flags = I(5);
            loot.Add(new LootItem(f[0], f[1], I(2), Math.Max(0, I(3)), Math.Clamp(I(4), 0, 1000), Uri.UnescapeDataString(f[6]), (flags & 1) != 0, (flags & 2) != 0));
        }
        return (site, Math.Max(0, torches), loot);
    }

    private static HeroResult R(HeroCommand c, HeroResultCode code, HeroText text, long mites = 0, long qty = 0) => new(c.Seq, c.Kind, code, text, mites, qty, c.IssuedSec);
    private static HeroText T(string key, params object[] args) => HeroText.Of(key, args);

    public static HeroResult Apply(HeroContext ctx, HeroCommand c)
    {
        if (c.Tag.StartsWith(TakePrefix, StringComparison.Ordinal)) return Take(ctx, c);
        if (c.Tag.StartsWith(OpenMinePrefix, StringComparison.Ordinal)) return OpenMine(ctx, c);
        bool search = c.Tag.StartsWith(SearchPrefix, StringComparison.Ordinal), dig = c.Tag.StartsWith(DigPrefix, StringComparison.Ordinal);
        if (!search && !dig) return R(c, HeroResultCode.Invalid, T("HERO_UNKNOWN_COMMAND"));
        string idText = c.Tag[(search ? SearchPrefix : DigPrefix).Length..];
        if (!ulong.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) return R(c, HeroResultCode.Invalid, T("HERO_MAKES_NO_SENSE"));
        var site = ctx.State.Population.Region.Sites.FirstOrDefault(s => s.Id.Value == id);
        if (site == null || (search ? site.Kind != SiteKind.Battlefield : site.Kind != SiteKind.Hoard)) return R(c, HeroResultCode.Invalid, T("HERO_NOTHING_THERE"));
        var book = ctx.State.Sites ??= new SiteBook();
        var info = SiteHistoryInfo.Of(site, ctx.State.History, new GameTime(c.IssuedSec).Year, 0, book);
        LootItem? it;
        lock (book.Sync)
        {
            var rec = book.Record(site.Id.Value);
            int pm = rec.LootPm;
            if (search)
            {
                var sc = SiteWiring.ContentOf(ctx.Content);
                int perception = ctx.Hero?.Attribute(Characters.HeroAttr.Perception) ?? 50;
                bool named = rec.Curse;
                it = SiteSurface.Search(sc, info, ref pm, ref named, perception, ctx.Party.Hero.Value, rec.Visits++, ctx.Seed);
                rec.Curse = named;
            }
            else it = SiteSurface.DigHoard(info, ref pm);
            rec.LootPm = pm;
        }
        if (it == null) return R(c, HeroResultCode.NoStock, T(search ? "ex_ui_search_none" : "ex_ui_dig_none"));
        var made = Make(ctx, it);
        return R(c, HeroResultCode.Ok, T("ex_ui_search_found", it.Named.Length > 0 ? it.Named : ctx.Rules.Name(it.Item)), it.ValueMites, made == EntityId.None ? 0 : 1);
    }

    /// <summary>The hero opens a prospected deposit as a mine he can walk (P8-LM-3): a site of the region with a level for each 10 m of depth, kept in the site book so a load puts it back.</summary>
    private static HeroResult OpenMine(HeroContext ctx, HeroCommand c)
    {
        var p = c.Tag[OpenMinePrefix.Length..].Split('|');
        if (p.Length < 6 || !int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int tx) || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int ty)
            || !int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int mineral) || !int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int tonnage)
            || !int.TryParse(p[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int depth) || mineral < 0 || mineral > (int)Ravensguard.Sim.WorldGen.MineralType.Stone) return R(c, HeroResultCode.Invalid, T("HERO_MAKES_NO_SENSE"));
        var book = ctx.State.Sites ??= new SiteBook();
        var s = MineSites.Open(book, ctx.State.Population.Region, tx, ty, (Ravensguard.Sim.WorldGen.MineralType)mineral, Math.Max(0, tonnage), depth, Uri.UnescapeDataString(p[5]), new GameTime(c.IssuedSec).Year);
        return s == null ? R(c, HeroResultCode.Invalid, T("HERO_NOTHING_THERE")) : R(c, HeroResultCode.Ok, T("ex_ui_mine_opened", s.Name, s.Levels), 0, s.Levels);
    }

    /// <summary>What the crawl brought out: each thing an item row in the pack (its mass the mass that left the site), and the burnt torches out of the pack's goods.</summary>
    private static HeroResult Take(HeroContext ctx, HeroCommand c)
    {
        if (DecodeTake(c.Tag) is not { } t) return R(c, HeroResultCode.Invalid, T("HERO_MAKES_NO_SENSE"));
        var e = ctx.Econ; var pack = ctx.Pack;
        if (t.Torches > 0 && e.Goods.TryIdOf("torch", out var torch)) pack.DestroyUpTo(torch, t.Torches, DestroyReason.Burned);
        long value = 0; int n = 0;
        foreach (var it in t.Loot) { if (Make(ctx, it) != EntityId.None) { n++; value += it.ValueMites; } }
        if (n == 0) return R(c, HeroResultCode.Ok, T("ex_ui_left"));
        return R(c, HeroResultCode.Ok, T("ex_ui_left_loot", n, Quotes.Money(value)), value, n);
    }

    /// <summary>
    /// One site thing as an item row the hero owns, of exactly the mass it had below: a weapon shape gets its head and haft (scaled to that mass), anything else one part of its material
    /// (the loot entry's, else the site item's, else bone). Condition and the dirt of a rusted blade carry over. None for a thing of no mass.
    /// </summary>
    public static EntityId Make(HeroContext ctx, LootItem it)
    {
        if (it.MassG <= 0) return EntityId.None;
        var db = ctx.Content;
        string mat = it.Material.Length > 0 ? it.Material : SiteWiring.ContentOf(db).Items.GetValueOrDefault(it.Item)?.Material is { Length: > 0 } m ? m : "bone";
        ItemComponent[] parts;
        if (db.TryGet<WeaponShapeDef>(it.Item, out var w) && db.TryGet<MaterialDef>(mat, out _))
        {
            parts = ctx.Factory.WeaponParts(w, mat);
            long total = parts.Sum(p => (long)p.MassG); int left = it.MassG;
            for (int k = parts.Length - 1; k >= 0; k--)
            {
                int share = k == 0 ? left : total <= 0 ? 0 : (int)((long)parts[k].MassG * it.MassG / total);
                share = Math.Clamp(share, 0, left); left -= share;
                parts[k] = parts[k] with { MassG = share, ConditionPermille = (short)it.ConditionPm };
            }
        }
        else parts = [new ItemComponent(mat, it.MassG, (short)it.ConditionPm)];
        var id = ctx.Econ.Items.Add(ctx.State.Ids, it.Item, ItemQuality.Common, ctx.Party.Hero, parts, conditionPermille: it.ConditionPm);
        if (it.Dirty) ctx.Econ.Items.Get(id).Stains = 1;
        return id;
    }
}
