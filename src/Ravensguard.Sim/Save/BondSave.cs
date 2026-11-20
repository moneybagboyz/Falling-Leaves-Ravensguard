using MessagePack;
using Ravensguard.Sim.Bondage;
using Ravensguard.Sim.Combat.Aftermath;

namespace Ravensguard.Sim.Save;

/// <summary>
/// The optional "bondage" section (P6-D1, slavery 3, overview 7; no format bump, absent while nobody is bonded, no band has raided and nobody was swept to a pen, so a world that never had slavery keeps its bytes): the bonds with
/// their owners, terms, prices, treatment and grievances, the slaver bands, the rebel bands of revolts and the captives already taken to a pen. Content ids and strings are not here (laws are derived from the cultures).
/// </summary>
[MessagePackObject]
public sealed class BondSave
{
    public const string SectionName = "bondage";

    /// <summary>Per bond: person, owner, kind, origin, since, term end, price, treatment, chain, work, place, home household, ask, seller kind, seller owner, morale, hungry days, flog day, separated day, flog entry, runaway day, runaway crime, bounty job, entry.</summary>
    [Key(0)] public long[][] Bonds { get; set; } = [];
    /// <summary>Per band: id, camp, size, last raid season, raids.</summary>
    [Key(1)] public long[][] Bands { get; set; } = [];
    /// <summary>Per rebel band: leader, place, day, members, entry.</summary>
    [Key(2)] public long[][] Rebels { get; set; } = [];
    /// <summary>The captives taken to a pen already (person ids, ascending).</summary>
    [Key(3)] public long[] Swept { get; set; } = [];
    /// <summary>Freed total, last daily pass, last season pass.</summary>
    [Key(4)] public long[] Counters { get; set; } = [];

    public static BondSave Capture(BondBook b) => new()
    {
        Bonds = [.. b.Bonds.Values.Select(x => new long[]
        {
            (long)x.Person, (long)x.Owner, (long)x.Kind, (long)x.Origin, x.Since, x.TermEndDay, x.PriceMites, (long)x.Treatment, (long)x.Chain, (long)x.Work, x.Place, x.HomeHousehold, x.Ask, x.SellerKind, (long)x.SellerOwner,
            x.Morale, x.HungryDays, x.FlogDay, x.SeparatedDay, (long)x.FlogEntry, x.RunawayDay, x.RunawayCrime, (long)x.BountyJob, (long)x.Entry,
        })],
        Bands = [.. b.Bands.Values.Select(x => new long[] { (long)x.Id, x.Camp, x.Size, x.LastRaidSeason, x.Raids })],
        Rebels = [.. b.Rebels.Select(x => new long[] { (long)x.Leader, x.Place, x.Day, x.Members, (long)x.Entry })],
        Swept = [.. b.Swept.Select(x => (long)x)],
        Counters = [b.FreedTotal, b.LastDay, b.LastSeason],
    };

    public BondBook Restore()
    {
        if (Counters.Length < 3) throw new SaveException("Corrupt 'bondage' section in save.");
        var b = new BondBook { FreedTotal = (int)Counters[0], LastDay = (int)Counters[1], LastSeason = (int)Counters[2] };
        foreach (var v in Bonds)
        {
            if (v.Length < 24) throw new SaveException("Corrupt bond in save.");
            b.Bonds[(ulong)v[0]] = new Bond
            {
                Person = (ulong)v[0], Owner = (ulong)v[1], Kind = (BondKind)v[2], Origin = (BondOrigin)v[3], Since = (int)v[4], TermEndDay = (int)v[5], PriceMites = v[6], Treatment = (Treatment)v[7], Chain = (Restraint)v[8],
                Work = (BondWork)v[9], Place = (int)v[10], HomeHousehold = (int)v[11], Ask = v[12], SellerKind = (byte)v[13], SellerOwner = (ulong)v[14], Morale = (int)v[15], HungryDays = (int)v[16], FlogDay = (int)v[17],
                SeparatedDay = (int)v[18], FlogEntry = (ulong)v[19], RunawayDay = (int)v[20], RunawayCrime = (int)v[21], BountyJob = (ulong)v[22], Entry = (ulong)v[23],
            };
        }
        foreach (var v in Bands)
        {
            if (v.Length < 5) throw new SaveException("Corrupt slaver band in save.");
            b.Bands[(ulong)v[0]] = new SlaverBand { Id = (ulong)v[0], Camp = (int)v[1], Size = (int)v[2], LastRaidSeason = (int)v[3], Raids = (int)v[4] };
        }
        foreach (var v in Rebels)
        {
            if (v.Length < 5) throw new SaveException("Corrupt rebel band in save.");
            b.Rebels.Add(new RebelBand { Leader = (ulong)v[0], Place = (int)v[1], Day = (int)v[2], Members = (int)v[3], Entry = (ulong)v[4] });
        }
        foreach (long s in Swept) b.Swept.Add((ulong)s);
        return b;
    }
}
