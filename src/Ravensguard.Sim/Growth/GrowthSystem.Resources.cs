using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Growth;

/// <summary>
/// P8-EA-1 (the economy and growth audit, overview 2.6, decided with the user 28 Nov): the finite resources turn over. An ore body is finite: a mine whose body is worked out closes (its hands
/// are let go, its box and stock go to its owner) and the chronicle says so. Prospectors keep looking: once a game year, for each base mineral the region's books want, a prospector of a
/// settlement with idle hands may find a new body in the hills within <see cref="ProspectRadiusM"/> of it and open a mine there (stream 0x8A0), more surely where the metal is dear or a mine
/// of it has just closed. Where the woods within reach of a settlement are cut below the floor and timber or charcoal is dear there, the chronicle names the cause. Nothing new is saved: a
/// found mine is an ordinary mine business with its works in the production section (its site id carries the tile), and every rule reads the world as it is.
/// </summary>
public sealed partial class GrowthSystem
{
    /// <summary>The minerals prospectors look for (silver and gold are left to the hero and the generated mines: a found silver mine would mint the region's money).</summary>
    public static readonly MineralType[] ProspectMinerals = [MineralType.Iron, MineralType.Copper, MineralType.Tin, MineralType.Coal, MineralType.Salt];
    /// <summary>A find's chance in a year (permille) where the mineral is wanted, and where it is dear (its ore or metal at or above its reference price) or a mine of it closed in the last year.</summary>
    public const int ProspectFindPm = 150, ProspectFindDearPm = 400;
    /// <summary>Open mines of a mineral above which nobody looks (and the higher limit while it is dear).</summary>
    public const int ProspectMinesMax = 1, ProspectMinesDearMax = 2;
    /// <summary>A found body: 300 to 1,500 tonnes (the size of the region's own generated metal veins, 200 to 1,600 t: a mine crewed to its book's demand works one out in decades), two levels, within this
    /// reach of the prospector's settlement, which needs this many idle hands and a book that buys the ore.</summary>
    public const int FoundTonnesMin = 300, FoundTonnesMax = 1500, FoundLevels = 2, ProspectRadiusM = 3000, ProspectMinHands = 6;
    /// <summary>Woods are over-cut when the wood within reach of a settlement holds under this share of its cap (percent); timber or charcoal is dear at this ratio to its reference (permille).</summary>
    public const int OvercutStockPct = 40, OvercutDearPermille = 1300;
    /// <summary>The site ids of found bodies: kind Other, this tag in bits 40 and up, the tile below.</summary>
    public const ulong FoundSiteTag = 0x5DUL << 40;

    public static bool IsFoundMine(MineWorks m) => m.SiteIndex < 0 && (m.SiteId.Value & (0xFFUL << 40)) == FoundSiteTag;
    public static int FoundTile(MineWorks m) => (int)(m.SiteId.Value & 0xFF_FFFF_FFFFUL);

    private static string? ProductOf(MineralType m) => m switch { MineralType.Iron => "iron_bar", MineralType.Copper => "copper", MineralType.Tin => "tin", _ => null };

    /// <summary>The season's resource step (step 6 of the season's first day, after the new businesses): worked-out mines close, then (on the year's first day) prospecting, then the over-cut woods.</summary>
    private void Resources(GameTime now, RegionOutbox ob)
    {
        CloseWorkedOutMines(now, ob);
        if (now.DayOfYear == 1 && Prospecting) Prospect(now, ob);
        OvercutWoods(now, ob);
    }

    /// <summary>Every open mine whose body is worked out closes (in business order), with its entry.</summary>
    private void CloseWorkedOutMines(GameTime now, RegionOutbox ob)
    {
        var prod = _ew.Production;
        foreach (var b in prod.Businesses)
        {
            if (b.State != BusinessState.Open || b.Mine < 0 || b.Mine >= prod.Mines.Count) continue;
            var m = prod.Mines[b.Mine]; if (m.RemainingG > 0 || m.InitialG <= 0) continue;
            if (prod.Steering is { } st && st.Steered(b)) continue;                                  // the hero's mine is his to close
            prod.Close(b);
            var place = Region.Settlements[b.Settlement].Id;
            ob.Chronicle(now, "mine_worked_out", "ch_p8_mine_worked_out", [ChronParam.Place("place", place), ChronParam.Good("ore", OreId(m.Mineral))], [b.Id],
                [Why.Of(CauseKind.Industry, "ch_why_ore_gone", ChronParam.Place("place", place))], ("tonnes", (m.InitialG / 1_000_000).ToString()), ("mineral", m.Mineral.ToString()));
            ob.Count("mines_worked_out");
        }
    }

    private int OreId(MineralType m) => MineWorks.OreGood(m) is { } g && _cat.TryIdOf(g, out var id) ? id : 0;

    /// <summary>The ore's demand a book must show for a mine there to have a buyer (grams a day: a smelter's or a household's real use, not the book's floor of one).</summary>
    public const long OreBuyerMinGPerDay = 1000;

    /// <summary>Whether some book buys the mineral's ore (its demand at least <see cref="OreBuyerMinGPerDay"/>) and whether it is dear (the ore, or its metal, at or above its reference price in such a book).</summary>
    private (bool Wanted, bool Dear) Want(MineralType m)
    {
        bool wanted = false, dear = false;
        if (MineWorks.OreGood(m) is not { } ore || !_cat.TryIdOf(ore, out var oid)) return (false, false);
        ushort metal = ProductOf(m) is { } pg && _cat.TryIdOf(pg, out var mid) ? mid : (ushort)0;
        foreach (var bk in _ew.Markets.AllBooks())
        {
            if (bk.Goods[oid].Demand < OreBuyerMinGPerDay) continue;
            wanted = true;
            if (bk.MidRatioPermille(oid) >= 1000 || (metal != 0 && bk.Goods[metal].Demand > 0 && bk.MidRatioPermille(metal) >= 1000)) dear = true;
        }
        return (wanted, dear);
    }

    /// <summary>
    /// The year's prospecting: for each mineral of <see cref="ProspectMinerals"/> the books want, while fewer mines of it are open than the limit, one roll (stream 0x8A0, key the year and the
    /// mineral); on a find the settlement with idle hands is drawn, then the tile (the highest dry, unbuilt, unfarmed tile of twelve drawn within reach, off every vein and mine), the tonnage, and the
    /// owner (the growth's founder rule for a mine); the mine is opened with half the owner's coin as its capital. At most one find a year.
    /// </summary>
    private void Prospect(GameTime now, RegionOutbox ob)
    {
        var prod = _ew.Production; var mineKind = prod.Content.Kinds.FirstOrDefault(k => k.Id == "mine"); if (mineKind == null) return;
        int day = (int)now.TotalDays; ulong seed = W.Seed; int year = now.Year;
        // a mine of the mineral closed in the last game year (read from the chronicle: nothing new is kept)
        var lost = new HashSet<string>(StringComparer.Ordinal); long since = now.TotalDays - TimeConst.DaysPerYear;
        foreach (var en in W.Chronicle.ByType("mine_worked_out")) if (en.When.TotalDays >= since && en.Data.TryGetValue("mineral", out var mv)) lost.Add(mv);
        foreach (var m in ProspectMinerals)
        {
            var (wanted, dear) = Want(m); if (!wanted) continue;
            bool lostHere = lost.Contains(m.ToString());
            int open = 0; foreach (var b in prod.Businesses) if (b.State == BusinessState.Open && b.Mine >= 0 && b.Mine < prod.Mines.Count && prod.Mines[b.Mine].Mineral == m && prod.Mines[b.Mine].RemainingG > 0) open++;
            bool keen = dear || lostHere;
            if (open >= (keen ? ProspectMinesDearMax : ProspectMinesMax)) continue;
            int pm = keen ? ProspectFindDearPm : ProspectFindPm;
            if (DetRng.Range(seed, year, (ulong)m, RngStreams.ProspectFind, 0, 0, 1000) >= pm) continue;
            var places = new List<int>(); var oid = _cat.IdOf(MineWorks.OreGood(m)!);
            for (int s = 0; s < Region.Settlements.Count; s++) if (HandsOf(s, day).Count >= ProspectMinHands && _ew.Markets.BookForSettlement(s).Goods[oid].Demand >= OreBuyerMinGPerDay) places.Add(s);     // where the ore has a buyer
            if (places.Count == 0) continue;
            int at = places[DetRng.Range(seed, year, (ulong)m, RngStreams.ProspectFind, 1, 0, places.Count)];
            int tile = FindTile(at, seed, year, m); if (tile < 0) continue;
            int owner = ChooseOwner(at, mineKind, day); if (owner < 0) continue;
            int tonnes = DetRng.Range(seed, year, (ulong)m, RngStreams.ProspectFind, 2, FoundTonnesMin, FoundTonnesMax + 1);
            Open(now, ob, mineKind, at, tile, m, tonnes, owner, lostHere, dear);
            return;
        }
    }

    private int FindTile(int s, ulong seed, int year, MineralType m)
    {
        var set = Region.Settlements[s]; int r = ProspectRadiusM / RegionData.TileM, cx = set.CenterX / RegionData.TileM, cy = set.CenterY / RegionData.TileM;
        var taken = new HashSet<int>(); foreach (var mw in _ew.Production.Mines) if (IsFoundMine(mw)) taken.Add(FoundTile(mw));
        int best = -1, bestH = int.MinValue;
        for (uint k = 0; k < 12; k++)
        {
            int x = cx + DetRng.Range(seed, year, (ulong)m, RngStreams.ProspectFind, 8 + 2 * k, -r, r + 1), y = cy + DetRng.Range(seed, year, (ulong)m, RngStreams.ProspectFind, 9 + 2 * k, -r, r + 1);
            if ((uint)x >= RegionData.Size || (uint)y >= RegionData.Size || (long)(x - cx) * (x - cx) + (long)(y - cy) * (y - cy) > (long)r * r) continue;
            int t = RegionData.Index(x, y);
            if (Region.Built[t] != 0 || Region.ParcelMap[t] != 0 || Region.Mineral[t] != 0 || taken.Contains(t)) continue;
            if ((Region.Water[t] & (WaterFlags.Lake | WaterFlags.River | WaterFlags.Sea)) != 0) continue;
            if (Region.HeightCm[t] > bestH) { bestH = Region.HeightCm[t]; best = t; }
        }
        return best;
    }

    private void Open(GameTime now, RegionOutbox ob, BusinessKind kind, int s, int tile, MineralType m, int tonnes, int owner, bool afterLoss, bool dear)
    {
        var prod = _ew.Production; var pt = W.People; var e = W.Economy; int ohh = pt.Household[owner];
        var place = Region.Settlements[s].Id; var who = pt.IdOf(owner);
        var why = afterLoss ? Why.Of(CauseKind.Industry, "ch_why_prospect_closed", ChronParam.Good("ore", OreId(m)))
            : dear ? Why.Of(CauseKind.Industry, "ch_why_prospect_dear", ChronParam.Good("ore", OreId(m))) : Why.Of(CauseKind.Industry, "ch_why_prospect_look", ChronParam.Place("place", place));
        var found = ob.Chronicle(now, "vein_found", "ch_p8_vein_found", [ChronParam.Person("person", who), ChronParam.Place("place", place), ChronParam.Good("ore", OreId(m))], [who], [why],
            ("tile", tile.ToString()), ("tonnes", tonnes.ToString()));
        var b = prod.NewBusiness(kind, s, -1, owner);
        prod.SetOwner(b, owner); b.OwnerWorks = false;
        long g = (long)tonnes * 1_000_000;
        b.Mine = prod.AddMine(new MineWorks { SiteIndex = -1, SiteId = EntityId.Make(IdKind.Other, FoundSiteTag | (uint)tile), Mineral = m, RemainingG = g, InitialG = g, Levels = FoundLevels, State = MineStateKind.Active });
        _ew.Labour.EnsureSkill(owner, Skill.Mining, LabourMarket.StartSkill);
        long capital = Balance(ohh) / 2;
        if (capital > 0) e.Ledger.Transfer(e.HouseholdPurse[ohh], b.Purse, new Coin(capital), TransferReason.Gift);
        b.CashStart = e.Ledger.Balance(b.Purse).Mites; b.Capital = Math.Max(b.CashStart, 1000);
        ob.Chronicle(now, "mine_opened", "ch_p8_mine_opened", [ChronParam.Person("person", who), ChronParam.Place("place", place), ChronParam.Good("ore", OreId(m))], [who, b.Id],
            [Why.After(CauseKind.Industry, found, "ch_why_vein_found", ChronParam.Place("place", place))], ("tile", tile.ToString()), ("tonnes", tonnes.ToString()));
        ob.Count("mines_opened");
        State.Year(now.Year).BusinessesOpened++;
        _ew.Labour.Rebuild();
    }

    /// <summary>
    /// The over-cut woods: a settlement whose woods within reach hold under <see cref="OvercutStockPct"/> percent of their cap, where logs or charcoal stand at <see cref="OvercutDearPermille"/> or more
    /// of their reference, gets an entry naming the cause (once in two game years a place). The price rise itself is the market's: the cutters' yield falls with the stock (resources spec 3.1), so less
    /// comes to the book.
    /// </summary>
    private void OvercutWoods(GameTime now, RegionOutbox ob)
    {
        if (!_cat.TryIdOf("logs", out var logs)) return;
        _cat.TryIdOf("charcoal", out var coal);
        var recent = new HashSet<long>(); long since = now.TotalDays - 2L * TimeConst.DaysPerYear;
        foreach (var en in W.Chronicle.ByType("woods_overcut")) if (en.When.TotalDays >= since && en.Data.TryGetValue("settlement", out var sv) && long.TryParse(sv, out var si)) recent.Add(si);
        foreach (var bk in _ew.Markets.AllBooks())
        {
            int s = bk.SettlementIndex; if ((uint)s >= (uint)Region.Settlements.Count || recent.Contains(s)) continue;
            int lr = bk.Goods[logs].Demand > 0 ? bk.MidRatioPermille(logs) : 0, cr = coal != 0 && bk.Goods[coal].Demand > 0 ? bk.MidRatioPermille(coal) : 0;
            // logs are the woods' own good; charcoal counts only where the logs are not cheap as well (charcoal dear beside cheap logs wants burners, not woods: EA_F19's crews)
            bool logsDear = lr >= OvercutDearPermille, coalDear = cr >= OvercutDearPermille && lr >= 1000;
            if (!logsDear && !coalDear) continue;
            var rows = _ew.Woodland.RowsOf(s); if (rows.Count == 0) continue;
            long stock = 0, cap = 0; foreach (int row in rows) { stock += _ew.Resources.StockG(row); cap += _ew.Resources.CapG(row); }
            if (cap <= 0 || stock * 100 >= cap * OvercutStockPct) continue;
            var place = Region.Settlements[s].Id; ushort dearGood = logsDear ? logs : coal;
            ob.Chronicle(now, "woods_overcut", "ch_p8_woods_overcut", [ChronParam.Place("place", place), ChronParam.Good("good", dearGood)], [place],
                [Why.Of(CauseKind.Trade, "ch_why_overcut", ChronParam.Place("place", place), ChronParam.Count("pct", stock * 100 / cap))],
                ("settlement", s.ToString()), ("stock_pct", (stock * 100 / cap).ToString()), ("price_pm", (logsDear ? lr : cr).ToString()));
            ob.Count("woods_overcut");
        }
    }
}
