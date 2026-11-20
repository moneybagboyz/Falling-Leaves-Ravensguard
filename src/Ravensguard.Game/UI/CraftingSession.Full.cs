using System;
using System.Collections.Generic;
using System.Linq;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Crafting;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Game.UI;

/// <summary>A maker the Commission tab offers for the selected recipe: who, his price, the days until it is ready and whether he would take it.</summary>
public sealed record MakerQuote(EntityId Id, string Name, string Trade, int Skill, long PriceMites, long LabourMites, int Days, bool Knows);

/// <summary>
/// P5-E (crafting-full 4.1, 4.5, 4.7): the crafting screen's view of the stations read from the real room, the item a repair, refit or salvage works on, the makers of the place with their price
/// and days, and the hero's commissions. In a running world the makers come from the barrier's <see cref="CraftPlaceView"/> and orders are hero commands (<see cref="CraftDesk"/>); the dev hamlet
/// keeps a made-up smith of its own so the tab can be seen.
/// </summary>
public sealed partial class CraftingSession
{
    /// <summary>The stations, room and containers the street view read from the map (null in the dev hamlet unless it built one).</summary>
    public ReachScan? Reach { get; }
    /// <summary>The settlement the hero stands in (-1: none); the Commission tab asks there.</summary>
    public int Settlement { get; init; } = -1;
    /// <summary>The dev hamlet's local makers and crafting context (null in a running world).</summary>
    public List<MakerRow> LocalMakers { get; } = [];
    public CraftContext? LocalCtx { get; set; }
    readonly Dictionary<ushort, EntityId> _targets = [];

    // ---- the station panel ----

    /// <summary>"Smithy: forge x2, anvil (fine)": the stations in reach, from the real room.</summary>
    public string StationLine()
    {
        var list = Reach?.Stations.Select(s => (s.Name, s.Count, s.Tier)) ?? Site.Stations.Select(s => (Db.Stations[s].Name, 1, Site.StationTiers.GetValueOrDefault(s, ItemQuality.Common)));
        var parts = list.Select(s => s.Name + (s.Item2 > 1 ? $" x{s.Item2}" : "") + (s.Item3 != ItemQuality.Common ? $" ({L.T("Q_" + s.Item3.ToString().ToUpperInvariant())})" : "")).ToList();
        string room = Reach?.RoomName ?? SiteSpec.Room;
        if (parts.Count == 0) return L.T("cf_ui_no_station");
        return (room.Length > 0 ? room : L.T("cf_ui_in_reach")) + ": " + string.Join(", ", parts);
    }

    // ---- the piece a repair, refit, salvage, reforge or oil works on ----

    /// <summary>The hero's pieces a recipe can work on, worst condition first: (id, name, condition).</summary>
    public List<(EntityId Id, string Name, int Condition)> Targets(CraftRecipe r)
    {
        if (!r.NeedsTarget) return [];
        IEnumerable<(EntityId, string, string, int)> rows = World?.View is { } v
            ? v.Items.Select(i => (i.Id, i.Shape, i.Name, i.ConditionPermille))
            : Econ.Items.LiveRows().Select(i => Econ.Items.At(i)).Where(x => x.Owner == HeroId).Select(x => (x.Id, x.Shape, x.Shape.Replace('_', ' '), (int)x.ConditionPermille));
        string target = r.Repair?.Target ?? r.Refit?.Target ?? (r.Salvage.Length > 0 ? r.SalvageTarget : "any");
        return [.. rows.Where(t => Fits(t.Item2, target)).OrderBy(t => t.Item4).ThenBy(t => t.Item1).Select(t => (t.Item1, t.Item3, t.Item4))];
    }

    static bool Fits(string shape, string target) => target switch
    {
        "mail" => shape.Contains("mail") || shape.Contains("hauberk"),
        "plate" => shape is "breastplate" or "backplate" or "helm" or "great_helm" or "kettle_hat" or "nasal_helm" or "spangenhelm" or "vambraces" or "greaves" or "cuisses" or "pauldrons" or "gauntlets",
        "leather" => shape.Contains("leather") || shape.Contains("boot") || shape.Contains("shoe") || shape == "cuir_bouilli" || shape == "jerkin",
        "blade" => shape is "dagger" or "arming_sword" or "longsword" or "falchion" or "hand_axe" or "axe" or "great_axe" or "billhook" or "spear",
        "garment" => shape is "shirt" or "tunic" or "cloak" or "hood" or "braies" or "hose" or "gloves" or "doublet" or "surcoat" or "tabard" or "gambeson" or "jerkin" or "oiled_cloak",
        "armour" => Fits(shape, "plate") || Fits(shape, "mail") || shape is "brigandine" or "lamellar" or "scale_hauberk",
        _ => true,
    };

    public EntityId TargetFor(CraftRecipe r)
    {
        var list = Targets(r);
        if (list.Count == 0) return EntityId.None;
        return _targets.TryGetValue(r.Index, out var t) && list.Any(x => x.Id == t) ? t : list[0].Id;
    }

    public void NextTarget(CraftRecipe r)
    {
        var list = Targets(r); if (list.Count < 2) return;
        int i = list.FindIndex(x => x.Id == TargetFor(r));
        _targets[r.Index] = list[(i + 1) % list.Count].Id;
    }

    // ---- hints: what is missing and where it could come from ----

    /// <summary>For each tool quality the site lacks, what gives it: the field kits, the tools and the stations (the screen's hint line).</summary>
    public List<string> ToolHints(CraftPlan p)
    {
        var res = new List<string>();
        foreach (var n in p.MissingTools)
        {
            var kits = Db.Tools.Values.Where(t => t.FieldKit && t.Qualities.GetValueOrDefault(n.Quality) >= n.Level).Select(t => t.Name).ToList();
            var tools = Db.Tools.Values.Where(t => !t.FieldKit && t.Qualities.GetValueOrDefault(n.Quality) >= n.Level).Select(t => t.Name).Take(2).ToList();
            var stations = Db.Stations.Values.Where(s => s.Provides.GetValueOrDefault(n.Quality) >= n.Level).Select(s => s.Name).Take(2).ToList();
            var from = tools.Concat(stations).Concat(kits.Select(k => k + " (" + L.T("cf_ui_kit") + ")")).ToList();
            res.Add(L.F("cf_ui_hint_tool", n.Quality, n.Level, from.Count > 0 ? string.Join(", ", from) : "-"));
        }
        foreach (var (where, mass, trips) in p.Fetches) res.Add(L.F("cf_ui_hint_fetch", where, Ravensguard.Sim.Trade.Quotes.Weight(mass), trips, Math.Max(1, (p.FetchSeconds + 59) / 60)));
        return res;
    }

    // ---- commissions ----

    /// <summary>The makers of the place who could take the recipe, with his price and days (computed as the sim will at ordering: labour x wage x 1,450 pm, his materials at his ask).</summary>
    public List<MakerQuote> Quotes(CraftRecipe r, bool clientMaterials)
    {
        var makers = World?.View?.CraftPlace?.Makers ?? (IReadOnlyList<MakerRow>)LocalMakers;
        var res = new List<MakerQuote>();
        if (!r.Commissionable) return res;
        foreach (var m in makers)
        {
            int skill = r.Skill == Skill.None ? 40 : m.Skills[(int)r.Skill];
            bool knows = skill >= r.Difficulty && (r.Workstation == null || m.Stations.Contains(r.Workstation)) && r.Guild == null;
            long hours = Commissions.LabourHours(r, 1);
            long labour = Commissions.LabourMites(hours, Commissions.WagePerHour(skill), Db.Settings.CommissionMarginPm);
            long mats = 0;
            if (!clientMaterials)
            {
                for (int i = 0; i < r.Slots.Length; i++) { var a = r.Slots[i].Alts[0]; if (!Db.Goods.HasTag(a.Good, "ambient")) mats += Ask(a.Good, a.Qty); }
                foreach (var s in r.Steps) if (s.FuelGood != ItemTable.NoGood) mats += Ask(s.FuelGood, s.FuelQty);
            }
            long workSec = 0; int pm = CraftMath.SkillFactorPm(skill, r.Difficulty);
            foreach (var s in r.Steps) if (s.Attended && !s.Conditional) workSec += CraftMath.StepSeconds(s.Seconds, 1000, 1000, pm);
            long now = Now / 3600;
            long due = Commissions.DueHour(now, m.BusySec + workSec, Db.Settings.MakerDayStartHour, Db.Settings.MakerHoursPerDay);
            res.Add(new MakerQuote(m.Id, m.Name, m.Trade, skill, labour + mats, labour, (int)Math.Max(1, (due - now + 23) / 24), knows));
        }
        return [.. res.OrderByDescending(q => q.Knows).ThenBy(q => q.PriceMites).ThenBy(q => q.Id)];
    }

    long Ask(ushort good, long qty)
    {
        long milli = (long)Math.Round(Db.Goods.Def(good).BasePriceMitesPerKg * 1000);
        return Db.Goods.IsPiece(good) ? (milli * qty + 999) / 1000 : (milli * qty + 999_999) / 1_000_000;
    }

    /// <summary>Orders it: a hero command in a running world (the price is the most it may cost), a local order in the dev hamlet.</summary>
    public bool Order(CraftRecipe r, MakerQuote q, bool clientMaterials)
    {
        if (World != null)
        {
            World.Issue(CraftDesk.Order(r, q.Id, clientMaterials, 1, new int[r.Slots.Length], Settlement, q.PriceMites, Now));
            return true;
        }
        if (LocalCtx == null) return false;
        return Commissions.Order(LocalCtx, r, HeroId, q.Id, clientMaterials, CraftSources.Of(Pack), NowSec, _ids).Answer == CommissionAnswer.Ok;
    }

    /// <summary>The hero's commissions, newest first.</summary>
    public IEnumerable<Commission> MyCommissions() => State.Commissions.Values.Where(c => c.Client == HeroId).OrderByDescending(c => c.Id);

    public string MakerName(EntityId id) => (World?.View?.CraftPlace?.Makers ?? (IReadOnlyList<MakerRow>)LocalMakers).FirstOrDefault(m => m.Id == id)?.Name ?? L.T("cf_ui_a_craftsman");

    public void CollectCommission(Commission c)
    {
        if (World != null) { World.Issue(CraftDesk.Collect(c.Id, c.Maker, Settlement, Now)); return; }
        if (LocalCtx != null) Commissions.Collect(LocalCtx, c, HeroId, 0, Pack);
    }

    public void CancelCommission(Commission c)
    {
        if (World != null) { World.Issue(CraftDesk.Cancel(c.Id, Now)); return; }
        if (LocalCtx != null) Commissions.Cancel(LocalCtx, c, HeroId, Pack);
    }

    /// <summary>Asks the barrier for the place's makers and guilds (sent when the tab opens).</summary>
    public void LookAround() { if (World != null && Settlement >= 0) World.Issue(CraftDesk.Look(Settlement, Now)); }

    /// <summary>The dev hamlet's smith: a made-up maker with a forge and an anvil, his purse and store in the local economy, so the Commission tab can be tried and seen.</summary>
    public void AddLocalSmith(string name, int skill, long coin)
    {
        var id = EntityId.Make(IdKind.Person, 9_000_001);
        var skills = new byte[Enum.GetValues<Skill>().Length]; skills[(int)Skill.Smithing] = (byte)skill; skills[(int)Skill.Carpentry] = (byte)(skill / 2);
        LocalMakers.Add(new MakerRow(id, name, "Smith", skills, ["forge", "anvil", "workbench"], 0));
        State.Book.Set(id, Skill.Smithing, skill * CraftMath.MicroPerLevel);
        var hero = Econ.Ledger.Open(PurseKind.Person, HeroId);
        if (coin > 0) Econ.Ledger.Mint(hero, new Coin(coin), SourceReason.Seed);
        LocalCtx = new CraftContext
        {
            Econ = Econ, Runner = Runner,
            PurseOf = p => Econ.Ledger.Open(PurseKind.Person, p),
            StoreOf = p => p == HeroId ? Pack : Econ.OpenStore(StoreKind.House, p, 0, 0, false),
            SiteOf = (p, r) => { var s = new CraftSite { Light = 255, Water = true }; foreach (var st in new[] { "forge", "anvil", "workbench" }) s.AddStation(Db, st); foreach (var (t, d) in Db.Tools) if (!d.FieldKit) s.AddTool(Db, t); return r.Workstation == null || s.Stations.Contains(r.Workstation) ? s : null; },
            SettlementOf = _ => 0,
        };
    }

    /// <summary>The hero's coin (the view's in a world, the local purse in the hamlet).</summary>
    public long Coin => World?.View?.CoinMites ?? (Econ.Ledger.TryFind(PurseKind.Person, HeroId, out var p) ? Econ.Ledger.Balance(p).Mites : 0);
}
