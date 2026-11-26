using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Armies;

public sealed partial class ArmyWorld
{
    /// <summary>The settlement whose centre is nearest a point (metres of the region frame); ties to the lowest index.</summary>
    public int NearestSettlement(long xM, long yM)
    {
        int best = -1; long bd = long.MaxValue;
        for (int i = 0; i < Region.Settlements.Count; i++)
        {
            long dx = Region.Settlements[i].CenterX - xM, dy = Region.Settlements[i].CenterY - yM, d = dx * dx + dy * dy;
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }

    /// <summary>How far, in metres, a person's household is from a settlement (a person without a household of his own: from his home settlement; unknown: far).</summary>
    public int HomeDistM(ulong person, int settlement)
    {
        int r = RowOf(person); if (r < 0) return int.MaxValue / 2;
        int hh = People.Household[r], home = hh >= 0 ? Households.Settlement[hh] : People.HomeSettlement[r];
        return (uint)home < (uint)Region.Settlements.Count ? DistM(home, settlement) : int.MaxValue / 2;
    }

    // ------------------------------------------------------------------ forming a band

    /// <summary>
    /// A band of bandits forms (3.4) from <paramref name="persons"/>: real people, alive, who leave their lord's service or their trade and live off the roads near <paramref name="place"/> (a settlement). Their occupation becomes
    /// bandit (what they did is kept to give back), their soldier rows go (they took their kit), and any warband they were in loses them. The leader is the best fighter among them, the lowest id on a tie. Their households stay
    /// where they are (abroad or none): nobody is made. Writes <c>bandits_formed</c> with <paramref name="cause"/> (the desertion entry) when there is one. Returns null when fewer than two live people were given.
    /// </summary>
    public BanditBand? FormBand(ArmyEnv e, IEnumerable<ulong> persons, int place, BandOrigin origin, EntityId? cause = null)
    {
        var ids = persons.Distinct().Where(p => RowOf(p) >= 0 && People.IsAlive(RowOf(p)) && !IsHero(p) && Book.BandWith(p) == null).OrderBy(p => p).ToList();
        if (ids.Count < 2 || (uint)place >= (uint)Region.Settlements.Count) return null;
        var band = new BanditBand { Id = Book.NextBand++, Lair = place, Origin = origin, FormedDay = (int)e.Day };
        foreach (ulong p in ids)
        {
            int r = RowOf(p);
            if (Book.WarbandWith(p) is { } wb) { wb.Members.Remove(p); wb.Prev.Remove(p); if (p == wb.Captain) SetCaptain(wb); }
            Soldiers?.Fill.Discharge(Soldiers.Book, p);
            band.Prev[p] = (byte)People.Occupation[r];
            People.Occupation[r] = Occupation.Bandit;
            band.Members.Add(p);
        }
        int best = -1; ulong leader = 0;
        foreach (ulong p in band.Members) { int s = MeleeOf(RowOf(p)); if (s > best) { best = s; leader = p; } }
        band.Leader = leader;
        Book.Bands[band.Id] = band; Book.Touch();
        Chron(e, "bandits_formed", "ch_am_bandits",
            [ChronParam.Person("leader", new EntityId(band.Leader)), ChronParam.Count("men", band.Members.Count), ChronParam.Place("place", PlaceId(place))],
            [new EntityId(band.Leader), PlaceId(place)], new Cause(CauseKind.Desertion, cause is { } c && c != EntityId.None ? c : null, "", "ch_note_am_deserters"));
        return band;
    }

    /// <summary>Men who left a host take to the roads with a band already there (4.8: joins or forms): each is a person of the world, alive, in no band; his trade is kept to give back, his soldier row goes, any warband loses him.</summary>
    public void JoinBand(BanditBand band, IEnumerable<ulong> persons)
    {
        foreach (ulong p in persons.Distinct().OrderBy(x => x))
        {
            int r = RowOf(p); if (r < 0 || !People.IsAlive(r) || IsHero(p) || Book.BandWith(p) != null) continue;
            if (Book.WarbandWith(p) is { } wb) { wb.Members.Remove(p); wb.Prev.Remove(p); if (p == wb.Captain) SetCaptain(wb); }
            Soldiers?.Fill.Discharge(Soldiers.Book, p);
            band.Prev[p] = (byte)People.Occupation[r]; People.Occupation[r] = Occupation.Bandit; band.Members.Add(p);
        }
        band.Members.Sort(); Book.Touch();
    }

    /// <summary>A man leaves his band (he yielded, or drifted home): his trade comes back and he is a man of his household again.</summary>
    public void ReleaseBandit(BanditBand band, ulong person)
    {
        int r = RowOf(person);
        if (r >= 0 && band.Prev.TryGetValue(person, out byte occ) && People.Occupation[r] == Occupation.Bandit) People.Occupation[r] = (Occupation)occ;
        band.Prev.Remove(person); band.Members.Remove(person); Book.Touch();
        if (person == band.Leader) { band.Leader = band.Members.Count > 0 ? band.Members.OrderByDescending(m => MeleeOf(RowOf(m))).ThenBy(m => m).First() : 0; }
    }

    /// <summary>The dead leave a band; a band of fewer than two is no band, and the rest go home.</summary>
    public void PruneBand(ArmyEnv e, BanditBand band)
    {
        foreach (ulong m in band.Members.ToList()) { int r = RowOf(m); if (r < 0 || People.Status[r] != PersonStatus.Alive) { band.Prev.Remove(m); band.Members.Remove(m); } }
        if (band.Leader != 0 && !band.Members.Contains(band.Leader)) band.Leader = band.Members.Count > 0 ? band.Members.OrderByDescending(m => MeleeOf(RowOf(m))).ThenBy(m => m).First() : 0;
        Book.Touch();
        if (band.Members.Count < 2) DissolveBand(band);
    }

    public void DissolveBand(BanditBand band)
    {
        foreach (ulong m in band.Members.ToList()) ReleaseBandit(band, m);
        Book.Bands.Remove(band.Id);
        Book.Reports.RemoveAll(n => n.Band == band.Id);
        Book.Touch();
    }

    // ------------------------------------------------------------------ the band's day

    /// <summary>
    /// The bands' day (hour 18): each band eats a kilogram of grain a man from its own store; with a chance (<c>band_rob_pm</c> a thousand, twice that when it has nothing to eat) it robs the households of a village or
    /// farmstead near its lair of grain (a real move from their stores into the band's), and the lords hear of it as news with the band's strength. A band the dead have thinned below two is no more.
    /// </summary>
    public void BandsDay(ArmyEnv e)
    {
        foreach (var band in Book.Bands.Values.ToList())
        {
            PruneBand(e, band); if (!Book.Bands.ContainsKey(band.Id)) continue;
            var store = StoreOfBand(band.Id); long need = (long)band.Members.Count * 1000, fed = 0;
            foreach (var g in GrainGoods())
            {
                if (fed >= need) break;
                long unit = Math.Max(1, W.Economy.Goods.MassG(g, 1)), qty = Math.Min(store.Qty(g), (need - fed + unit - 1) / unit);
                if (qty > 0 && store.Destroy(g, qty, DestroyReason.Eaten)) fed += qty * unit;
            }
            bool hungry = fed < need;
            int pm = Rules.BandRobPm * (hungry ? 2 : 1);
            if (DetRng.Range(W.Seed, e.Day, (ulong)band.Id, RngStreams.BandRobbery, 0, 0, 1000) >= pm) continue;
            var near = new List<(int D, int S)>();
            for (int s = 0; s < Region.Settlements.Count; s++)
            {
                if (Region.Settlements[s].Class == SettlementClass.Town) continue;
                int d = DistM(band.Lair, s); if (d <= Rules.BandRobReachM) near.Add((d, s));
            }
            if (near.Count == 0) continue;
            near.Sort((a, b) => a.D != b.D ? a.D.CompareTo(b.D) : a.S.CompareTo(b.S));
            int target = near[(int)(DetRng.Next(W.Seed, e.Day, (ulong)band.Id, RngStreams.BandRobbery, 1) % (ulong)Math.Min(3, near.Count))].S;
            long taken = 0; int n = 0; var ht = Households;
            for (int h = 0; h < ht.Count && n < 3; h++)
                if (ht.MemberCount[h] > 0 && ht.Settlement[h] == target) { long got = TakeGrain(h, store, Rules.BandRobKg); if (got > 0) { taken += got; n++; } }
            if (taken <= 0) continue;
            band.LastRobDay = (int)e.Day;
            Book.Hear((int)e.Day, target, band.Id, -1, band.Members.Count);
        }
    }

    /// <summary>The seasonal drift (3.4, stream 0x7A1 keyed band and season): each member has <c>band_drift_home_pm</c> a thousand of going home to his household, a man of his house again.</summary>
    public void BandsSeason(ArmyEnv e)
    {
        foreach (var band in Book.Bands.Values.ToList())
        {
            foreach (ulong m in band.Members.ToList())
                if (DetRng.Range(W.Seed, e.SeasonNo, m, RngStreams.DeserterDrift, (uint)band.Id, 0, 1000) < Rules.BandDriftHomePm) ReleaseBandit(band, m);
            PruneBand(e, band);
        }
    }
}
