using MessagePack;
using Ravensguard.Sim.WarSupply;

namespace Ravensguard.Sim.Save;

/// <summary>One host's account in the <c>warsupply</c> section: plain numbers, flattened (the chest purse, baggage store and soldiers' purse are the economy section's own records, found again by owner id).</summary>
[MessagePackObject]
public sealed class HostAccountSave
{
    [Key(0)] public int Id { get; set; }
    [Key(1)] public int Realm { get; set; }
    [Key(2)] public ulong Lord { get; set; }
    /// <summary>The chest's kind and owner, the baggage store's owner, the treasury's kind and owner (0: none).</summary>
    [Key(3)] public ulong[] Owners { get; set; } = [];
    [Key(4)] public int[] Policy { get; set; } = [];
    [Key(5)] public int[] Depots { get; set; } = [];
    [Key(6)] public int[] Men { get; set; } = [];
    /// <summary>Followers, carts, outside men, riding, pack, draught, oxen, mode, place settlement, x, y, pasture, service days, home settlement.</summary>
    [Key(7)] public int[] Fields { get; set; } = [];
    [Key(8)] public long[] Arrears { get; set; } = [];
    [Key(9)] public int[] HungerDays { get; set; } = [];
    [Key(10)] public int[] UnpaidDays { get; set; } = [];
    [Key(11)] public long[] Totals { get; set; } = [];
    /// <summary>The campaign's chronicle entry and the abstract flag (P7-AE1; absent in older sections).</summary>
    [Key(12)] public long[] Extra { get; set; } = [];
}

/// <summary>A convoy on the road (P7-AE1), flattened: caravan, host, depot settlement, carts, baggage owner, chest kind, chest owner, float, day sent.</summary>
[MessagePackObject]
public sealed class ConvoySave
{
    [Key(0)] public long[] Fields { get; set; } = [];
}

/// <summary>The optional <c>warsupply</c> section (army-economy 6; a name of <c>Phase7Seeding.SectionNames</c>), written only once a host has existed, so a world that never had one keeps its bytes. Persons on campaign are listed here by host (no key is added to the people section).</summary>
[MessagePackObject]
public sealed class WarSupplySave
{
    public const string SectionName = "warsupply";

    [Key(0)] public int NextHost { get; set; } = 1;
    [Key(1)] public HostAccountSave[] Hosts { get; set; } = [];
    /// <summary>Pairs: person row, host id.</summary>
    [Key(2)] public int[] OnCampaign { get; set; } = [];
    /// <summary>Pairs: settlement index, besieging host id.</summary>
    [Key(3)] public int[] Blockades { get; set; } = [];
    /// <summary>Triples: settlement index, year, men drawn by that year's muster (P7-EA3d; absent in older sections).</summary>
    [Key(4)] public int[] Levied { get; set; } = [];
    /// <summary>Convoys on the road (P7-AE1; absent in older sections).</summary>
    [Key(5)] public ConvoySave[] Convoys { get; set; } = [];
    /// <summary>Settlements whose <c>siege_starving</c> is written, then those whose depot's <c>depot_emptied</c> is, split at <see cref="EmptiedFrom"/> (P7-AE1).</summary>
    [Key(6)] public int[] Noted { get; set; } = [];
    [Key(7)] public int EmptiedFrom { get; set; }

    /// <summary>P7-AE2 (optional, absent in older sections and while none exists): tallies as flat rows (id, realm, settlement, host, taken day, due day, paid day, household, entry, mites, paid, grams, state), sutlers (host, merchant, day), unnoted scutage (kind, overlord, payers, total), the next tally id and the running stats.</summary>
    [Key(8)] public long[][] Tallies { get; set; } = [];
    [Key(9)] public long[][] Sutlers { get; set; } = [];
    [Key(10)] public long[][] ScutageNotes { get; set; } = [];
    [Key(11)] public long[] Counters { get; set; } = [];

    /// <summary>P7-AE3 (optional, key 12; null while the arms industry has nothing): orders, the refit queue, realm stocks and indices, bans, the caravan log and the stats, flattened.</summary>
    [Key(12)] public ArmsSave? Arms { get; set; }

    public static WarSupplySave Capture(WarSupplyBook b) => new()
    {
        NextHost = b.NextHost,
        Hosts = [.. b.Hosts.Values.Select(a => new HostAccountSave
        {
            Id = a.Id, Realm = a.Realm, Lord = a.Lord,
            Owners = [a.ChestKind, a.ChestOwner, a.BaggageOwner, a.TreasuryKind, a.TreasuryOwner],
            Policy = [a.Purveyance ? 1 : 0, a.Plunder ? 1 : 0, (int)a.Forage, a.BuyRadiusM],
            Depots = [.. a.Depots], Men = [.. a.Men],
            Fields = [a.Followers, a.Carts, a.Outside, a.Riding, a.Pack, a.Draught, a.Oxen, (int)a.Mode, a.PlaceSettlement, a.PlaceXM, a.PlaceYM, a.Pasture ? 1 : 0, a.ServiceDays, a.HomeSettlement],
            Arrears = [.. a.Arrears], HungerDays = [.. a.HungerDays], UnpaidDays = [.. a.UnpaidDays], Totals = [.. a.Totals],
            Extra = a.Retainers != 0 || a.ForageCutPm != 0 ? [(long)a.Campaign, a.Abstract ? 1 : 0, a.Retainers, a.ForageCutPm] : a.Campaign != 0 || a.Abstract ? [(long)a.Campaign, a.Abstract ? 1 : 0] : [],
        })],
        OnCampaign = [.. b.OnCampaign.SelectMany(kv => new[] { kv.Key, kv.Value })],
        Blockades = [.. b.Blockades.SelectMany(kv => new[] { kv.Key, kv.Value })],
        Levied = [.. b.Levied.SelectMany(kv => new[] { kv.Key, kv.Value.Year, kv.Value.Men })],
        Convoys = [.. b.Convoys.Values.Select(c => new ConvoySave { Fields = [(long)c.Caravan, c.Host, c.Depot, c.Carts, (long)c.BaggageOwner, c.ChestKind, (long)c.ChestOwner, c.FloatMites, c.SentDay] })],
        Noted = [.. b.Starving, .. b.Emptied],
        EmptiedFrom = b.Starving.Count,
        Tallies = [.. b.Tallies.Values.Select(t => new long[] { t.Id, t.Realm, t.Settlement, t.Host, t.TakenDay, t.DueDay, t.PaidDay, (long)t.Household, (long)t.Entry, t.Mites, t.PaidMites, t.Grams, (long)t.State })],
        Sutlers = [.. b.Sutlers.Select(x => new long[] { x.Host, (long)x.Merchant, x.JoinedDay })],
        ScutageNotes = [.. b.ScutageNotes.Select(n => new long[] { n.OverlordKind, n.Overlord, n.Payers, n.Total })],
        Arms = b.Arms.IsEmpty ? null : ArmsSave.Capture(b.Arms),
        Counters = b.Tallies.Count == 0 && b.Sutlers.Count == 0 && b.ScutageNotes.Count == 0 && b.NextTally == 1 && b.Stats.All(x => x == 0) ? [] : [b.NextTally, .. b.Stats],
    };

    public WarSupplyBook Restore()
    {
        var b = new WarSupplyBook { NextHost = NextHost };
        foreach (var s in Hosts)
        {
            if (s.Owners.Length != 5 || s.Policy.Length != 4 || s.Fields.Length != 14) throw new InvalidDataException("corrupt warsupply host row");
            var a = new HostAccount
            {
                Id = s.Id, Realm = s.Realm, Lord = s.Lord,
                ChestKind = (byte)s.Owners[0], ChestOwner = s.Owners[1], BaggageOwner = s.Owners[2], TreasuryKind = (byte)s.Owners[3], TreasuryOwner = s.Owners[4],
                Purveyance = s.Policy[0] != 0, Plunder = s.Policy[1] != 0, Forage = (ForagePolicy)s.Policy[2], BuyRadiusM = s.Policy[3], Depots = [.. s.Depots],
                Followers = s.Fields[0], Carts = s.Fields[1], Outside = s.Fields[2], Riding = s.Fields[3], Pack = s.Fields[4], Draught = s.Fields[5], Oxen = s.Fields[6],
                Mode = (HostMode)s.Fields[7], PlaceSettlement = s.Fields[8], PlaceXM = s.Fields[9], PlaceYM = s.Fields[10], Pasture = s.Fields[11] != 0, ServiceDays = s.Fields[12], HomeSettlement = s.Fields[13],
            };
            // a section written by a build with more or fewer roles pads or trims (roles are append-only)
            Array.Copy(s.Men, a.Men, Math.Min(s.Men.Length, a.Men.Length)); Array.Copy(s.Arrears, a.Arrears, Math.Min(s.Arrears.Length, a.Arrears.Length));
            Array.Copy(s.HungerDays, a.HungerDays, Math.Min(s.HungerDays.Length, a.HungerDays.Length)); Array.Copy(s.UnpaidDays, a.UnpaidDays, Math.Min(s.UnpaidDays.Length, a.UnpaidDays.Length));
            Array.Copy(s.Totals, a.Totals, Math.Min(s.Totals.Length, a.Totals.Length));
            if (s.Extra.Length >= 2) { a.Campaign = (ulong)s.Extra[0]; a.Abstract = s.Extra[1] != 0; }
            if (s.Extra.Length >= 4) { a.Retainers = (int)s.Extra[2]; a.ForageCutPm = (int)s.Extra[3]; }
            b.Hosts[a.Id] = a;
        }
        for (int i = 0; i + 1 < OnCampaign.Length; i += 2) b.OnCampaign[OnCampaign[i]] = OnCampaign[i + 1];
        for (int i = 0; i + 1 < Blockades.Length; i += 2) b.Blockades[Blockades[i]] = Blockades[i + 1];
        for (int i = 0; i + 2 < Levied.Length; i += 3) b.Levied[Levied[i]] = (Levied[i + 1], Levied[i + 2]);
        foreach (var c in Convoys)
        {
            var f = c.Fields; if (f.Length != 9) throw new InvalidDataException("corrupt warsupply convoy row");
            b.Convoys[(ulong)f[0]] = new ConvoyRec((ulong)f[0], (int)f[1], (int)f[2], (int)f[3], (ulong)f[4], (byte)f[5], (ulong)f[6], f[7], (int)f[8]);
        }
        foreach (var f in Tallies)
        {
            if (f.Length != 13) throw new InvalidDataException("corrupt warsupply tally row");
            b.Tallies[(int)f[0]] = new PurveyanceTally { Id = (int)f[0], Realm = (int)f[1], Settlement = (int)f[2], Host = (int)f[3], TakenDay = (int)f[4], DueDay = (int)f[5], PaidDay = (int)f[6], Household = (ulong)f[7], Entry = (ulong)f[8], Mites = f[9], PaidMites = f[10], Grams = f[11], State = (TallyState)f[12] };
        }
        foreach (var f in Sutlers) { if (f.Length != 3) throw new InvalidDataException("corrupt warsupply sutler row"); b.Sutlers.Add(new SutlerRec((int)f[0], (ulong)f[1], (int)f[2])); }
        foreach (var f in ScutageNotes) { if (f.Length != 4) throw new InvalidDataException("corrupt warsupply scutage row"); b.ScutageNotes.Add(new ScutageNote((byte)f[0], f[1], (int)f[2], f[3])); }
        if (Counters.Length >= 1) { b.NextTally = (int)Counters[0]; Array.Copy(Counters, 1, b.Stats, 0, Math.Min(Counters.Length - 1, b.Stats.Length)); }
        Arms?.Restore(b.Arms);
        for (int i = 0; i < Noted.Length; i++) (i < EmptiedFrom ? b.Starving : b.Emptied).Add(Noted[i]);
        return b;
    }
}

/// <summary>The arms industry's part of the <c>warsupply</c> section (P7-AE3), flattened.</summary>
[MessagePackObject]
public sealed class ArmsSave
{
    [Key(0)] public string[] Shapes { get; set; } = [];
    [Key(1)] public long[] Orders { get; set; } = [];
    [Key(2)] public long[] Refit { get; set; } = [];
    [Key(3)] public long[] Stock { get; set; } = [];
    [Key(4)] public long[] Index { get; set; } = [];
    [Key(5)] public long[] Bans { get; set; } = [];
    [Key(6)] public long[] Log { get; set; } = [];
    [Key(7)] public long[] Stats { get; set; } = [];

    public static ArmsSave Capture(ArmsBook a)
    {
        var shapes = a.Orders.Select(o => o.Shape).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        return new ArmsSave
        {
            Shapes = shapes,
            Orders = [.. a.Orders.SelectMany(o => new long[] { (long)o.Lord, o.Day, o.Count, o.Mites, Array.IndexOf(shapes, o.Shape) })],
            Refit = [.. a.Refit.SelectMany(kv => new long[] { (long)kv.Key, kv.Value })],
            Stock = [.. a.Stock.SelectMany(kv => new long[] { kv.Key, kv.Value })],
            Index = [.. a.Index.SelectMany(kv => new long[] { kv.Key, kv.Value })],
            Bans = [.. a.Bans],
            Log = [.. a.Log.SelectMany(s => new long[] { s.From, s.To, s.Grams, s.Mites, s.Day, s.Outcome })],
            Stats = [.. a.Stats],
        };
    }

    public void Restore(ArmsBook a)
    {
        for (int i = 0; i + 4 < Orders.Length; i += 5) a.Orders.Add(new ArmsOrderRow((ulong)Orders[i], (int)Orders[i + 1], Shapes[(int)Orders[i + 4]], (int)Orders[i + 2], Orders[i + 3]));
        for (int i = 0; i + 1 < Refit.Length; i += 2) a.Refit[(ulong)Refit[i]] = Refit[i + 1];
        for (int i = 0; i + 1 < Stock.Length; i += 2) a.Stock[(int)Stock[i]] = Stock[i + 1];
        for (int i = 0; i + 1 < Index.Length; i += 2) a.Index[(int)Index[i]] = (int)Index[i + 1];
        foreach (long k in Bans) a.Bans.Add(k);
        for (int i = 0; i + 5 < Log.Length; i += 6) a.Log.Add(new ArmsShipRow((int)Log[i], (int)Log[i + 1], Log[i + 2], Log[i + 3], (int)Log[i + 4], (byte)Log[i + 5]));
        Array.Copy(Stats, a.Stats, Math.Min(Stats.Length, a.Stats.Length));
    }
}
