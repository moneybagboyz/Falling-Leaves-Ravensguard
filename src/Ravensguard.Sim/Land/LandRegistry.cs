using System.Security.Cryptography;
using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Land;

/// <summary>
/// Who holds every lot, parcel, stall, claim and charter of a region (land spec 3.2): the deeds generation implies, derived from the plan, the people and the growth log, with the
/// deeds of the <see cref="LandBook"/> laid over them (the book wins), plus the lease rows the book holds on top of a lot or a parcel. Not stored: built at load or when asked
/// (about 6,000 lots and 2,500 parcels, within the 40 ms budget). Also answers <see cref="May"/>.
/// </summary>
public sealed class LandRegistry
{
    private readonly RegionData _r;
    private readonly Population _pop;
    private readonly LandBook _book;
    private readonly List<Deed> _deeds = [];
    private readonly Dictionary<long, int> _lot = [];
    private readonly int[] _parcel;
    private readonly Dictionary<long, Deed> _lotLease = [];
    private readonly Dictionary<int, Deed> _parcelLease = [];
    private readonly Dictionary<int, List<long>> _lotsByTile = [];
    private readonly Func<EntityId, string>? _names;

    public int RegionId { get; }
    public IReadOnlyList<Deed> Deeds => _deeds;

    private LandRegistry(RegionData r, Population pop, LandBook book, Func<EntityId, string>? names)
    {
        _r = r; _pop = pop; _book = book; _names = names; RegionId = RegionCoord.OfCell(r.CellX, r.CellY).Id;
        _parcel = new int[r.Parcels.Count]; Array.Fill(_parcel, -1);
    }

    // ------------------------------------------------------------------ who the lord is

    /// <summary>The lord's household of a settlement (-1 none: no fief, or the lord's house is empty).</summary>
    public static int LordHousehold(Population pop, int settlement)
    {
        var r = pop.Region; if ((uint)settlement >= (uint)r.Settlements.Count) return -1;
        int f = r.Settlements[settlement].FiefIndex;
        int hh = f >= 0 && f < pop.FiefLordHousehold.Length ? pop.FiefLordHousehold[f] : -1;
        return hh >= 0 && hh < pop.Households.Count && pop.Households.MemberCount[hh] > 0 ? hh : -1;
    }

    /// <summary>The purse that sells the lord's land and takes his dues in a settlement: his household's, else his fief's treasury, else the town's chest.</summary>
    public static (EntityId Holder, PurseKind Kind) LordOf(Population pop, int settlement)
    {
        int hh = LordHousehold(pop, settlement);
        if (hh >= 0) return (pop.Households.IdOf(hh), PurseKind.Household);
        return TownOf(pop.Region, settlement);
    }

    /// <summary>The fief's treasury of a settlement, else the town's own chest (the purse a market stall's rent is paid into).</summary>
    public static (EntityId Holder, PurseKind Kind) TownOf(RegionData r, int settlement)
    {
        var st = r.Settlements[settlement]; int f = st.FiefIndex;
        if (f >= 0 && f < r.Fiefs.Count) return (r.Fiefs[f].Id, PurseKind.Treasury);
        return (st.Id, PurseKind.Settlement);
    }

    // ------------------------------------------------------------------ building

    /// <summary>The registry of a region: every derived deed, then the book's deeds over them (spec 3.2). Pure: the same records give the same registry.</summary>
    public static LandRegistry Build(RegionData r, Population pop, LandBook book, LandRulesDef rules, Func<EntityId, string>? names = null)
    {
        var g = new LandRegistry(r, pop, book, names);
        var hht = pop.Households; int reg = g.RegionId;
        for (int s = 0; s < r.Settlements.Count; s++)
        {
            var st = r.Settlements[s]; bool town = st.Class == SettlementClass.Town;
            var (lordId, lordKind) = LordOf(pop, s);
            int fief = st.FiefIndex;
            var lots = st.Plan.Lots;
            for (int l = 0; l < lots.Count; l++)
            {
                var lot = lots[l];
                var d = new Deed { Id = LandIds.Of(HoldingKind.Lot, reg, LandIds.LotSubject(s, l)), Kind = HoldingKind.Lot, Subject = LandIds.LotSubject(s, l), Settlement = s, Lord = fief, Rect = lot.Rect,
                    Holder = lordId, HolderPurse = lordKind, Tenure = Tenure.Freehold, Cause = DeedCause.Generated };
                int hh = l < pop.LotHousehold[s].Length ? pop.LotHousehold[s][l] : -1;
                if (hh >= 0 && hh < hht.Count && lot.Dwelling != DwellingType.None && hht.Deed[hh] != DeedKind.None)
                {
                    d.Holder = hht.IdOf(hh); d.HolderPurse = PurseKind.Household;
                    d.Tenure = hht.Deed[hh] switch
                    {
                        DeedKind.Owned => town && hht.Class[hh] != SocialClass.Gentry ? Tenure.Burgage : Tenure.Freehold,
                        DeedKind.Rented => Tenure.Leasehold,
                        _ => Tenure.Tenancy,
                    };
                    if (d.IsLease) { d.Lessor = lordId; d.LessorPurse = lordKind; }
                }
                else if (lot.Use is LotUse.Church or LotUse.Chapel)
                {
                    d.Holder = st.Id; d.HolderPurse = PurseKind.Chest;               // the parish's chest (taxes 2.1)
                }
                else if (lot.Use is LotUse.Smithy or LotUse.Tavern or LotUse.Mill && l < pop.LotKeeper[s].Length && pop.LotKeeper[s][l] is int keeper && keeper >= 0
                         && pop.People.Household[keeper] is int kh && kh >= 0 && kh < hht.Count && hht.MemberCount[kh] > 0 && kh != LordHousehold(pop, s))
                {
                    d.Holder = hht.IdOf(kh); d.HolderPurse = PurseKind.Household; d.Tenure = town ? Tenure.Burgage : Tenure.Customary;
                }
                g.Add(d);
                g.IndexTiles(LandIds.LotSubject(s, l), lot.Rect);
            }
            int stalls = st.Class switch { SettlementClass.Town => rules.StallsTown, SettlementClass.Village => rules.StallsVillage, _ => 0 };
            var (townId, townKind) = TownOf(r, s);
            for (int k = 0; k < stalls; k++)
                g.Add(new Deed { Id = LandIds.Of(HoldingKind.Stall, reg, LandIds.StallSubject(s, k)), Kind = HoldingKind.Stall, Subject = LandIds.StallSubject(s, k), Settlement = s, Lord = fief,
                    Holder = townId, HolderPurse = townKind, Tenure = Tenure.Freehold, Cause = DeedCause.Generated });
        }
        for (int p = 0; p < r.Parcels.Count; p++)
        {
            var pc = r.Parcels[p]; int s = pc.Settlement;
            var (lordId, lordKind) = LordOf(pop, s);
            var d = new Deed { Id = LandIds.Of(HoldingKind.Parcel, reg, p), Kind = HoldingKind.Parcel, Subject = p, Settlement = s, Lord = r.Settlements[s].FiefIndex,
                Holder = lordId, HolderPurse = lordKind, Tenure = Tenure.Freehold, Cause = DeedCause.Generated };
            int holder = p < pop.ParcelHolder.Length ? pop.ParcelHolder[p] : -1;
            bool real = holder >= 0 && holder < hht.Count && hht.MemberCount[holder] > 0;
            switch (pc.Owner.Kind)
            {
                case OwnerKind.Glebe: d.Holder = r.Settlements[s].Id; d.HolderPurse = PurseKind.Chest; break;
                case OwnerKind.Demesne: break;
                case OwnerKind.Tenant:
                    if (real && hht.Class[holder] != SocialClass.Gentry) { d.Holder = hht.IdOf(holder); d.HolderPurse = PurseKind.Household; d.Tenure = Tenure.Tenancy; d.Lessor = lordId; d.LessorPurse = lordKind; }
                    break;
                default:
                    if (real) { d.Holder = hht.IdOf(holder); d.HolderPurse = PurseKind.Household; }
                    break;
            }
            g._parcel[p] = g._deeds.Count; g.Add(d);
        }
        // the book's deeds: a changed generated deed replaces its derived row; a lease row sits on top of the lot or parcel it lets; claims and charters are added
        foreach (var bd in book.Deeds.Values)
        {
            if (g.TryIndex(bd.Id, out int i)) { g._deeds[i] = bd; continue; }
            if (bd.IsLease && bd.Kind == HoldingKind.Parcel && bd.Subject >= 0 && bd.Subject < r.Parcels.Count) g._parcelLease[(int)bd.Subject] = bd;
            else if (bd.IsLease && bd.Kind == HoldingKind.Building) g._lotLease[bd.Subject] = bd;
            g.Add(bd);
        }
        g._deeds.Sort((a, b) => a.Id.CompareTo(b.Id));
        g._byId.Clear(); g._lot.Clear(); Array.Fill(g._parcel, -1);
        for (int i = 0; i < g._deeds.Count; i++)
        {
            var d = g._deeds[i]; g._byId[d.Id] = i;
            if (d.Kind == HoldingKind.Lot && d.Id == LandIds.Of(HoldingKind.Lot, reg, d.Subject)) g._lot[d.Subject] = i;
            else if (d.Kind == HoldingKind.Parcel && d.Id == LandIds.Of(HoldingKind.Parcel, reg, d.Subject) && d.Subject < g._parcel.Length) g._parcel[(int)d.Subject] = i;
        }
        return g;
    }

    private readonly Dictionary<EntityId, int> _byId = [];
    private void Add(Deed d) { _byId[d.Id] = _deeds.Count; _deeds.Add(d); }
    private bool TryIndex(EntityId id, out int i) => _byId.TryGetValue(id, out i);

    private void IndexTiles(long lotSubject, RectM q)
    {
        int x0 = Math.Clamp(q.X / RegionData.TileM, 0, RegionData.Size - 1), x1 = Math.Clamp((q.Right - 1) / RegionData.TileM, 0, RegionData.Size - 1);
        int y0 = Math.Clamp(q.Y / RegionData.TileM, 0, RegionData.Size - 1), y1 = Math.Clamp((q.Bottom - 1) / RegionData.TileM, 0, RegionData.Size - 1);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int t = RegionData.Index(x, y);
                if (!_lotsByTile.TryGetValue(t, out var l)) _lotsByTile[t] = l = [];
                l.Add(lotSubject);
            }
    }

    // ------------------------------------------------------------------ lookups

    public Deed? Get(EntityId id) => _byId.TryGetValue(id, out int i) ? _deeds[i] : null;
    public Deed? LotDeed(int settlement, int lot) => _lot.TryGetValue(LandIds.LotSubject(settlement, lot), out int i) ? _deeds[i] : null;
    public Deed? ParcelDeed(int parcel) => (uint)parcel < (uint)_parcel.Length && _parcel[parcel] >= 0 ? _deeds[_parcel[parcel]] : null;
    public Deed? ParcelLease(int parcel) => _parcelLease.GetValueOrDefault(parcel);
    public Deed? HouseLease(int settlement, int lot) => _lotLease.GetValueOrDefault(LandIds.LotSubject(settlement, lot));

    /// <summary>The dwelling deed of a household: the deed of the lot it lives on when it is the holder (null for a household without a dwelling).</summary>
    public Deed? DwellingOf(int household)
    {
        var hht = _pop.Households; if ((uint)household >= (uint)hht.Count) return null;
        int s = hht.Settlement[household], l = hht.Lot[household];
        if (s < 0 || l < 0 || s >= _pop.LotHousehold.Length || l >= _pop.LotHousehold[s].Length || _pop.LotHousehold[s][l] != household) return null;
        var d = LotDeed(s, l);
        return d != null && d.Holder == hht.IdOf(household) && d.HolderPurse == PurseKind.Household ? d : null;
    }

    /// <summary><see cref="HouseholdTable.Deed"/> derived from the registry (spec 3.1): Freehold and Burgage are Owned, Leasehold is Rented, Tenancy and Customary are Tenant.</summary>
    public DeedKind DerivedDeedKind(int household) => DwellingOf(household) is not { } d ? DeedKind.None : d.Tenure switch
    {
        Tenure.Freehold or Tenure.Burgage => DeedKind.Owned, Tenure.Leasehold => DeedKind.Rented, _ => DeedKind.Tenant,
    };

    /// <summary>Every deed held by <paramref name="holder"/> (ascending id).</summary>
    public IEnumerable<Deed> HeldBy(EntityId holder) { foreach (var d in _deeds) if (d.Holder == holder) yield return d; }

    /// <summary>The lot whose ground holds a point (region metres), or (-1, -1).</summary>
    public (int Settlement, int Lot) LotAt(int xM, int yM)
    {
        int t = LandRules.TileAt(xM, yM);
        if (t < 0 || !_lotsByTile.TryGetValue(t, out var subs)) return (-1, -1);
        foreach (long sub in subs)
        {
            var (s, l) = LandIds.SplitLot(sub); var q = _r.Settlements[s].Plan.Lots[l].Rect;
            if (xM >= q.X && xM < q.Right && yM >= q.Y && yM < q.Bottom) return (s, l);
        }
        return (-1, -1);
    }

    // ------------------------------------------------------------------ names

    /// <summary>The name a refusal gives the holder: a household's head, the lord's fief, the parish, the town; the hero and other persons through the caller's names.</summary>
    public string HolderName(EntityId holder, PurseKind kind)
    {
        var hht = _pop.Households;
        if (kind == PurseKind.Household && hht.IndexOf(holder) is int h && h >= 0)
        {
            int head = hht.Head[h]; var p = _pop.People;
            if (head >= 0 && head < p.Count) return (p.Given[head] + " " + p.Family[head]).Trim();
            return hht.Name[h];
        }
        if (kind == PurseKind.Treasury) foreach (var f in _r.Fiefs) if (f.Id == holder) return f.Name;
        if (kind is PurseKind.Settlement or PurseKind.Chest) foreach (var s in _r.Settlements) if (s.Id == holder) return s.Name;
        return _names?.Invoke(holder) ?? "";
    }

    // ------------------------------------------------------------------ may (spec 4.1)

    /// <summary>
    /// May <paramref name="person"/> (of <paramref name="household"/>, -1 none) do <paramref name="action"/> at a point (region metres)? On his own lot, claim or charter site: anything (a lessee or a
    /// tenant never demolishes); on a parcel he leases: farm, store and farm structures only; elsewhere: refused, naming the holder (LD-6).
    /// </summary>
    public MayResult May(EntityId person, int household, LandAction action, int xM, int yM)
    {
        var hhId = household >= 0 ? _pop.Households.IdOf(household) : EntityId.None;
        bool Mine(Deed d) => d.Holder == person || (household >= 0 && d.Holder == hhId && d.HolderPurse == PurseKind.Household);
        int t = LandRules.TileAt(xM, yM);
        foreach (var c in _book.Claims)
        {
            if (c.State is not (ClaimState.Open or ClaimState.Deeded) || Array.BinarySearch(c.Tiles, t) < 0) continue;
            if (c.Claimant == person) return MayResult.Yes(c.DeedId);
            return new MayResult(false, HolderName(c.Claimant, c.ClaimantPurse), c.DeedId, "ld_may_claimed");
        }
        foreach (var c in _book.Charters)
        {
            if (c.State == CharterState.Lapsed || Array.BinarySearch(c.Tiles, t) < 0) continue;
            if (c.Grantee == person) return MayResult.Yes(c.DeedId);
            return new MayResult(false, HolderName(c.Grantee, c.GranteePurse), c.DeedId, "ld_may_charter");
        }
        var (s, l) = LotAt(xM, yM);
        if (s >= 0)
        {
            if (HouseLease(s, l) is { } lease && Mine(lease))
                return action is LandAction.Store or LandAction.Farm ? MayResult.Yes(lease.Id) : new MayResult(false, HolderName(lease.Lessor, lease.LessorPurse), lease.Id, "ld_may_lessee");
            var d = LotDeed(s, l)!;
            if (Mine(d)) return d.IsLease && action is LandAction.Demolish or LandAction.Build ? new MayResult(false, HolderName(d.Lessor, d.LessorPurse), d.Id, "ld_may_lessee") : MayResult.Yes(d.Id);
            return new MayResult(false, HolderName(d.Holder, d.HolderPurse), d.Id, "ld_may_theirs");
        }
        int p = t >= 0 ? _r.ParcelMap[t] - 1 : -1;
        if (p >= 0)
        {
            if (ParcelLease(p) is { } lease && Mine(lease))
                return action is LandAction.Farm or LandAction.BuildFarm or LandAction.Store ? MayResult.Yes(lease.Id) : new MayResult(false, HolderName(lease.Lessor, lease.LessorPurse), lease.Id, "ld_may_farm_only");
            var d = ParcelDeed(p);
            if (d != null && Mine(d)) return d.IsLease && action is LandAction.Build or LandAction.Demolish ? new MayResult(false, HolderName(d.Lessor, d.LessorPurse), d.Id, "ld_may_farm_only") : MayResult.Yes(d.Id);
            if (d != null) return new MayResult(false, HolderName(d.Holder, d.HolderPurse), d.Id, "ld_may_theirs");
        }
        return new MayResult(false, "", EntityId.None, "ld_may_wild");
    }

    // ------------------------------------------------------------------ hash

    /// <summary>Hash of every deed in id order (LD-2: a replayed registry hashes the same).</summary>
    public string ComputeHash()
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var b8 = new byte[8];
        void L(long v) { System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(b8, v); h.AppendData(b8); }
        L(_deeds.Count);
        foreach (var d in _deeds) LandBook.HashDeed(d, L);
        return Convert.ToHexString(h.GetHashAndReset());
    }
}
