using MessagePack;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Prospecting;

namespace Ravensguard.Sim.Save;

/// <summary>
/// The optional "ownership" section (P5-F; overview 7: no format bump, absent while the book is empty so a world that never used it keeps its bytes): the hero's holdings in businesses (shares,
/// steward, directives, wages, premises, season history, the steward's price memory), his stalls, the prospect book (knowledge rows, hints, pan findings, survey reports, jobs), mining licences.
/// Businesses are named by their id, goods by index of the goods table (mapped by name at a load like every other section: <see cref="ContentRemap"/> rewrites it).
/// </summary>
[MessagePackObject]
public sealed class OwnershipSave
{
    public const string SectionName = "ownership";

    [Key(0)] public int NextSerial { get; set; } = 1;
    [Key(1)] public int HeroAt { get; set; } = -1;
    [Key(2)] public int ToastDay { get; set; } = -1;
    [Key(3)] public ushort Toasted { get; set; }
    [Key(4)] public HoldingRow[] Holdings { get; set; } = [];
    [Key(5)] public StallRow[] Stalls { get; set; } = [];
    [Key(6)] public ProspectRowSave[] Prospects { get; set; } = [];
    [Key(7)] public HintRow[] Hints { get; set; } = [];
    [Key(8)] public PanRow[] Pans { get; set; } = [];
    [Key(9)] public ulong[] ReportItem { get; set; } = [];
    [Key(10)] public ProspectRowSave[][] ReportRows { get; set; } = [];
    [Key(11)] public JobRow[] Jobs { get; set; } = [];
    [Key(12)] public int NextJob { get; set; } = 1;
    [Key(13)] public LicenceRow[] Licences { get; set; } = [];
    /// <summary>The goods hash the price memory and directives' goods were saved against (rewritten by the load remap).</summary>
    [Key(14)] public string GoodsHash { get; set; } = "";

    [MessagePackObject]
    public sealed class HoldingRow
    {
        [Key(0)] public ulong Business { get; set; }
        [Key(1)] public ulong[] ShareHolder { get; set; } = [];
        [Key(2)] public int[] SharePm { get; set; } = [];
        [Key(3)] public int Steward { get; set; } = -1;
        [Key(4)] public int StewardWage { get; set; }
        [Key(5)] public long StewardOwed { get; set; }
        /// <summary>KeepGood, FloorGood, SendEveryDays, HireUpTo, Recipe, PerDay, close (0/1).</summary>
        [Key(6)] public int[] OrderInts { get; set; } = [];
        /// <summary>KeepQty, FloorMites, SeasonBudget, KeepMites.</summary>
        [Key(7)] public long[] OrderLongs { get; set; } = [];
        [Key(8)] public int[] Premises { get; set; } = [];
        [Key(9)] public bool HandsOn { get; set; }
        [Key(10)] public int[] WagePerson { get; set; } = [];
        [Key(11)] public int[] WageMites { get; set; } = [];
        [Key(12)] public int FoundedDay { get; set; }
        /// <summary>RevenueSeason, SentSeason, SentToPurse, SentToChest, RoyaltyPaid, RoyaltyOwed, LastMinedG, StewardPaidSeason, HandsToSec.</summary>
        [Key(13)] public long[] Books { get; set; } = [];
        /// <summary>Season, Revenue, Wages, Inputs, Profit, Paid per row.</summary>
        [Key(14)] public long[] History { get; set; } = [];
        /// <summary>LastSentDay, Alerts, Vein, Licence, Unlicensed (0/1), StewardShortDays, MadeToday, MadeDay.</summary>
        [Key(15)] public int[] Ints { get; set; } = [];
        [Key(16)] public ushort[] PriceGoods { get; set; } = [];
        [Key(17)] public long[] PriceDays { get; set; } = [];
        [Key(18)] public int[] Tiles { get; set; } = [];
    }

    [MessagePackObject]
    public sealed class StallRow
    {
        [Key(0)] public long Subject { get; set; }
        [Key(1)] public int Settlement { get; set; }
        [Key(2)] public int Business { get; set; }
        [Key(3)] public ulong StoreId { get; set; }
        [Key(4)] public int AskPm { get; set; }
        [Key(5)] public int Minder { get; set; }
        [Key(6)] public long MindedToSec { get; set; }
        [Key(7)] public int Day { get; set; }
        /// <summary>SoldToday, TakingsToday, TollToday, TakingsSeason, SoldSeason.</summary>
        [Key(8)] public long[] Books { get; set; } = [];
    }

    [MessagePackObject]
    public sealed class ProspectRowSave
    {
        [Key(0)] public ulong Person { get; set; }
        [Key(1)] public int Tile { get; set; }
        [Key(2)] public int Resource { get; set; }
        [Key(3)] public long EstG { get; set; }
        [Key(4)] public int GradePm { get; set; }
        [Key(5)] public int ConfidencePm { get; set; }
        [Key(6)] public int Day { get; set; }
        [Key(7)] public byte Method { get; set; }
        public static ProspectRowSave Of(ProspectRow r) => new() { Person = r.Person.Value, Tile = r.Tile, Resource = r.Resource, EstG = r.EstG, GradePm = r.GradePm, ConfidencePm = r.ConfidencePm, Day = r.Day, Method = (byte)r.Method };
        public ProspectRow Row() => new(new EntityId(Person), Tile, Resource, EstG, GradePm, ConfidencePm, Day, (ProspectMethod)Method);
    }

    [MessagePackObject]
    public sealed class HintRow
    {
        [Key(0)] public ulong Person { get; set; }
        [Key(1)] public int Tile { get; set; }
        [Key(2)] public int Mineral { get; set; }
        [Key(3)] public int Direction { get; set; }
        [Key(4)] public int Day { get; set; }
    }

    [MessagePackObject]
    public sealed class PanRow
    {
        [Key(0)] public ulong Person { get; set; }
        [Key(1)] public int Tile { get; set; }
        [Key(2)] public int[] Minerals { get; set; } = [];
        [Key(3)] public int Day { get; set; }
    }

    [MessagePackObject]
    public sealed class JobRow
    {
        [Key(0)] public int Serial { get; set; }
        [Key(1)] public ulong Person { get; set; }
        [Key(2)] public byte Method { get; set; }
        [Key(3)] public int Tile { get; set; }
        [Key(4)] public int Skill { get; set; }
        [Key(5)] public long StartSec { get; set; }
        [Key(6)] public long EndSec { get; set; }
        [Key(7)] public ulong Client { get; set; }
        [Key(8)] public long FeeMites { get; set; }
        [Key(9)] public bool Done { get; set; }
    }

    [MessagePackObject]
    public sealed class LicenceRow
    {
        [Key(0)] public int Serial { get; set; }
        [Key(1)] public ulong Holder { get; set; }
        [Key(2)] public int Vein { get; set; }
        [Key(3)] public int Tile { get; set; }
        [Key(4)] public byte Mineral { get; set; }
        [Key(5)] public byte Grantor { get; set; }
        [Key(6)] public int GrantorIndex { get; set; }
        [Key(7)] public long FeeMites { get; set; }
        [Key(8)] public int RoyaltyPm { get; set; }
        [Key(9)] public int Day { get; set; }
        [Key(10)] public bool Revoked { get; set; }
    }

    public static OwnershipSave Capture(OwnershipBook b, string goodsHash)
    {
        var s = new OwnershipSave { NextSerial = b.NextSerial, HeroAt = b.HeroAt, ToastDay = b.ToastDay, Toasted = (ushort)b.Toasted, GoodsHash = goodsHash, NextJob = b.Prospects.NextJob };
        s.Holdings = [.. b.Holdings.Values.Select(h => new HoldingRow
        {
            Business = h.Business.Value, ShareHolder = [.. h.Shares.Select(x => x.Holder.Value)], SharePm = [.. h.Shares.Select(x => x.Pm)], Steward = h.Steward, StewardWage = h.StewardWage, StewardOwed = h.StewardOwed,
            OrderInts = [h.Orders.KeepGood, h.Orders.FloorGood, h.Orders.SendEveryDays, h.Orders.HireUpTo, h.Orders.Recipe, h.Orders.PerDay, h.Orders.CloseAfterTwoLosses ? 1 : 0],
            OrderLongs = [h.Orders.KeepQty, h.Orders.FloorMites, h.Orders.SeasonBudget, h.Orders.KeepMites],
            Premises = [.. h.Premises], HandsOn = h.HandsOn, WagePerson = [.. h.Wages.Keys], WageMites = [.. h.Wages.Values], FoundedDay = h.FoundedDay,
            Books = [h.RevenueSeason, h.SentSeason, h.SentToPurse, h.SentToChest, h.RoyaltyPaid, h.RoyaltyOwed, h.LastMinedG, h.StewardPaidSeason, h.HandsToSec],
            History = [.. h.History.SelectMany(r => new long[] { r.Season, r.Revenue, r.Wages, r.Inputs, r.Profit, r.Paid })],
            Ints = h.InheritedDay >= 0 ? [h.LastSentDay, (int)h.Alerts, h.Vein, h.Licence, h.Unlicensed ? 1 : 0, h.StewardShortDays, h.MadeToday, h.MadeDay, h.InheritedDay]   // P5-H: a ninth int only once inherited
                : [h.LastSentDay, (int)h.Alerts, h.Vein, h.Licence, h.Unlicensed ? 1 : 0, h.StewardShortDays, h.MadeToday, h.MadeDay],
            PriceGoods = [.. h.PriceGoods], PriceDays = [.. h.PriceDays.SelectMany(d => d)], Tiles = [.. h.Tiles],
        })];
        s.Stalls = [.. b.Stalls.Values.Select(x => new StallRow { Subject = x.Subject, Settlement = x.Settlement, Business = x.Business, StoreId = x.StoreId.Value, AskPm = x.AskPm, Minder = x.Minder, MindedToSec = x.MindedToSec, Day = x.Day, Books = [x.SoldToday, x.TakingsToday, x.TollToday, x.TakingsSeason, x.SoldSeason] })];
        var p = b.Prospects;
        s.Prospects = [.. p.Rows.Values.Select(ProspectRowSave.Of)];
        s.Hints = [.. p.Hints.Select(h => new HintRow { Person = h.Person.Value, Tile = h.Tile, Mineral = h.Mineral, Direction = h.Direction, Day = h.Day })];
        s.Pans = [.. p.Pans.Select(x => new PanRow { Person = x.Person.Value, Tile = x.Tile, Minerals = [.. x.Minerals], Day = x.Day })];
        s.ReportItem = [.. p.Reports.Keys.Select(k => k.Value)];
        s.ReportRows = [.. p.Reports.Values.Select(rows => rows.Select(ProspectRowSave.Of).ToArray())];
        s.Jobs = [.. p.Jobs.Select(j => new JobRow { Serial = j.Serial, Person = j.Person.Value, Method = (byte)j.Method, Tile = j.Tile, Skill = j.Skill, StartSec = j.StartSec, EndSec = j.EndSec, Client = j.Client.Value, FeeMites = j.FeeMites, Done = j.Done })];
        s.Licences = [.. b.Licences.Select(l => new LicenceRow { Serial = l.Serial, Holder = l.Holder.Value, Vein = l.Vein, Tile = l.Tile, Mineral = (byte)l.Mineral, Grantor = (byte)l.Grantor, GrantorIndex = l.GrantorIndex, FeeMites = l.FeeMites, RoyaltyPm = l.RoyaltyPm, Day = l.Day, Revoked = l.Revoked })];
        return s;
    }

    public OwnershipBook Restore()
    {
        try
        {
            var b = new OwnershipBook { NextSerial = NextSerial, HeroAt = HeroAt, ToastDay = ToastDay, Toasted = (OwnerAlert)Toasted };
            foreach (var r in Holdings)
            {
                var h = new BusinessHolding { Business = new EntityId(r.Business), Steward = r.Steward, StewardWage = r.StewardWage, StewardOwed = r.StewardOwed, HandsOn = r.HandsOn, FoundedDay = r.FoundedDay };
                for (int i = 0; i < r.ShareHolder.Length; i++) h.Shares.Add(new ShareRow(new EntityId(r.ShareHolder[i]), r.SharePm[i]));
                h.Orders = new Directives
                {
                    KeepGood = r.OrderInts[0], FloorGood = r.OrderInts[1], SendEveryDays = r.OrderInts[2], HireUpTo = r.OrderInts[3], Recipe = r.OrderInts[4], PerDay = r.OrderInts[5], CloseAfterTwoLosses = r.OrderInts[6] != 0,
                    KeepQty = r.OrderLongs[0], FloorMites = r.OrderLongs[1], SeasonBudget = r.OrderLongs[2], KeepMites = r.OrderLongs[3],
                };
                h.Premises.AddRange(r.Premises);
                for (int i = 0; i < r.WagePerson.Length; i++) h.Wages[r.WagePerson[i]] = r.WageMites[i];
                h.RevenueSeason = r.Books[0]; h.SentSeason = r.Books[1]; h.SentToPurse = r.Books[2]; h.SentToChest = r.Books[3]; h.RoyaltyPaid = r.Books[4]; h.RoyaltyOwed = r.Books[5]; h.LastMinedG = r.Books[6]; h.StewardPaidSeason = r.Books[7]; h.HandsToSec = r.Books[8];
                for (int i = 0; i + 5 < r.History.Length; i += 6) h.History.Add(new SeasonRow((int)r.History[i], r.History[i + 1], r.History[i + 2], r.History[i + 3], r.History[i + 4], r.History[i + 5]));
                h.LastSentDay = r.Ints[0]; h.Alerts = (OwnerAlert)r.Ints[1]; h.Vein = r.Ints[2]; h.Licence = r.Ints[3]; h.Unlicensed = r.Ints[4] != 0; h.StewardShortDays = r.Ints[5]; h.MadeToday = r.Ints[6]; h.MadeDay = r.Ints[7];
                if (r.Ints.Length > 8) h.InheritedDay = r.Ints[8];
                h.PriceGoods = [.. r.PriceGoods];
                int w = h.PriceGoods.Length;
                if (w > 0) for (int i = 0; i + w <= r.PriceDays.Length; i += w) h.PriceDays.Add(r.PriceDays[i..(i + w)]);
                h.Tiles = [.. r.Tiles];
                b.Holdings[h.Business] = h;
            }
            foreach (var r in Stalls)
                b.Stalls[r.Subject] = new StallHolding
                {
                    Subject = r.Subject, Settlement = r.Settlement, Business = r.Business, StoreId = new EntityId(r.StoreId), AskPm = r.AskPm, Minder = r.Minder, MindedToSec = r.MindedToSec, Day = r.Day,
                    SoldToday = r.Books[0], TakingsToday = r.Books[1], TollToday = r.Books[2], TakingsSeason = r.Books[3], SoldSeason = r.Books[4],
                };
            var p = b.Prospects; p.NextJob = NextJob;
            foreach (var r in Prospects) { var row = r.Row(); p.Rows[(row.Person.Value, row.Tile, row.Resource)] = row; }
            foreach (var h in Hints) p.Hints.Add(new ProspectHint(new EntityId(h.Person), h.Tile, h.Mineral, h.Direction, h.Day));
            foreach (var x in Pans) p.Pans.Add(new PanFinding(new EntityId(x.Person), x.Tile, [.. x.Minerals], x.Day));
            for (int i = 0; i < ReportItem.Length; i++) p.Reports[new EntityId(ReportItem[i])] = [.. ReportRows[i].Select(r => r.Row())];
            foreach (var j in Jobs) p.Jobs.Add(new ProspectJob { Serial = j.Serial, Person = new EntityId(j.Person), Method = (ProspectMethod)j.Method, Tile = j.Tile, Skill = j.Skill, StartSec = j.StartSec, EndSec = j.EndSec, Client = new EntityId(j.Client), FeeMites = j.FeeMites, Done = j.Done });
            foreach (var l in Licences) b.Licences.Add(new MiningLicence { Serial = l.Serial, Holder = new EntityId(l.Holder), Vein = l.Vein, Tile = l.Tile, Mineral = (Ravensguard.Sim.WorldGen.MineralType)l.Mineral, Grantor = (Ravensguard.Sim.Land.GrantorKind)l.Grantor, GrantorIndex = l.GrantorIndex, FeeMites = l.FeeMites, RoyaltyPm = l.RoyaltyPm, Day = l.Day, Revoked = l.Revoked });
            return b;
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException)
        {
            throw new SaveException("Corrupt 'ownership' section in save.", ex);
        }
    }
}
