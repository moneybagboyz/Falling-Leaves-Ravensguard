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
        })],
        OnCampaign = [.. b.OnCampaign.SelectMany(kv => new[] { kv.Key, kv.Value })],
        Blockades = [.. b.Blockades.SelectMany(kv => new[] { kv.Key, kv.Value })],
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
            b.Hosts[a.Id] = a;
        }
        for (int i = 0; i + 1 < OnCampaign.Length; i += 2) b.OnCampaign[OnCampaign[i]] = OnCampaign[i + 1];
        for (int i = 0; i + 1 < Blockades.Length; i += 2) b.Blockades[Blockades[i]] = Blockades[i + 1];
        return b;
    }
}
