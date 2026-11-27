using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WarSupply;

namespace Ravensguard.Sim.Armies;

public sealed partial class ArmyWorld
{
    // ------------------------------------------------------------------ hosts from outside (4.4, plan 1B)

    /// <summary>
    /// A host that comes from outside the region enters it at a gate (<paramref name="gate"/> is <c>-1 - gate index</c>, as in a route; the places of the persons' birth are the settlement nearest the gate): every man of its roster
    /// who is not yet a person is made one, by the persons stream 0x7A3 (key the host and the man's index: the same host in the same world gets the same names), a grown man of a soldier's trade with no household, and the
    /// ones who left the region with the host before (Departed rows the march keeps) come back as themselves. The roster does not change: a man who was a count is now a person who is that count. Returns the persons in the region.
    /// </summary>
    public int EnterRegion(ArmyEnv e, HostId host, int gate)
    {
        var hs = Supply; if (hs == null || !hs.Exists(host)) return 0;
        var a = hs.Account(host);
        if (!Book.Marches.TryGetValue(host.Value, out var m)) { m = new HostMarch { Host = host.Value, StartDay = (int)e.Day, State = MarchState.Arrived }; Book.Marches[m.Host] = m; }      // standing in the region with no road to walk
        var pt = People; var w = W;
        var near = gate < 0 ? GateSettlement(-1 - gate) : Math.Max(0, gate);
        int levyPersons = hs.Book.OnCampaign.Count(kv => kv.Value == host.Value);
        // the departed come back first (by id), then the missing are made, role by role
        foreach (var (id, role) in m.Outsiders)
        {
            int r = RowOf(id); if (r < 0 || pt.Status[r] != PersonStatus.Departed) continue;
            pt.Status[r] = PersonStatus.Alive; w.Vital.Alive++; w.Vital.Immigrants++;
        }
        int index = m.Outsiders.Count;
        for (int role = 0; role < Roster.Roles; role++)
        {
            int want = a.Men[role] - (role == (int)TroopRole.Levy ? levyPersons : 0);
            int have = m.Outsiders.Values.Count(x => x == role);
            for (int k = have; k < want; k++) { ulong id = MakeOutsider(e, host, index++, role, near); m.Outsiders[id] = (byte)role; }
        }
        m.InRegion = true; a.Outside = Math.Max(a.Outside, m.Outsiders.Count);
        hs.ToDetailed(host, hs.PlaceAt(Math.Clamp(near, 0, Region.Settlements.Count - 1)), HostMode.March);
        return m.Outsiders.Count;
    }

    private int GateSettlement(int gate)
    {
        if ((uint)gate >= (uint)Region.Gates.Count) return 0;
        int outer = Region.Settlements.Count + 2 * gate; if (outer >= Region.RoadNodes.Count) return 0;
        var n = Region.RoadNodes[outer]; long bd = long.MaxValue; int best = 0;
        for (int s = 0; s < Region.Settlements.Count; s++)
        {
            long dx = Region.Settlements[s].CenterX - n.X, dy = Region.Settlements[s].CenterY - n.Y, d = dx * dx + dy * dy;
            if (d < bd) { bd = d; best = s; }
        }
        return best;
    }

    /// <summary>One man made for a host from outside: a grown man (18 to 39), a soldier by trade, with no household and no home in the region (a visitor who is on the books while he is here). Everything of him is keyed on stream 0x7A3, the host and his index.</summary>
    private ulong MakeOutsider(ArmyEnv e, HostId host, int index, int role, int near)
    {
        var w = W; var pt = People; long day = e.Day;
        ulong key = ((ulong)(uint)host.Value << 32) | (uint)index;
        w.Ids.EnsureNext(IdKind.Person, pt.BaseCounter + (ulong)pt.Count);
        var id = w.Ids.Allocate(IdKind.Person);
        int p = pt.Add();
        if (pt.IdOf(p) != id) throw new InvalidOperationException("person id out of step with the table");
        int culture = Region.Settlements[Math.Clamp(near, 0, Region.Settlements.Count - 1)].Culture;
        var pool = w.Names?.For(culture);
        var rg = new Rnd(w.Seed, key, RngStreams.HostPersonsFromOutside); var rf = new Rnd(w.Seed, key ^ (1UL << 63), RngStreams.HostPersonsFromOutside);
        int age = 18 + new Rnd(w.Seed, key ^ (1UL << 62), RngStreams.HostPersonsFromOutside).Range(0, 22);
        pt.Sex[p] = Sex.Male; pt.BirthDay[p] = PersonTable.BirthDayFor(day, age, new Rnd(w.Seed, key ^ (3UL << 61), RngStreams.HostPersonsFromOutside).Range(0, 60), pt.AgingPace);
        pt.Culture[p] = (byte)culture; pt.Class[p] = (TroopRole)role is TroopRole.ManAtArms or TroopRole.Knight or TroopRole.Captain ? SocialClass.Gentry : SocialClass.Peasant;
        pt.Family[p] = pool != null ? pool.FamilyName(rf) : "Outlander"; pt.Given[p] = pool != null ? pool.Given(false, rg) : "Soldier";
        pt.Status[p] = PersonStatus.Alive; pt.Occupation[p] = Occupation.Soldier; pt.Household[p] = -1;
        var rs = new RngStream(w.Seed, 0, key, RngStreams.HostPersonsFromOutside, 8);
        int Roll() => 30 + rs.Range(0, 21) + rs.Range(0, 21) + rs.Range(0, 21);
        int b = p * PersonTable.AttrCount;
        byte K(int v) => (byte)Math.Clamp(v, 1, 100);
        pt.Attr[b] = K(Roll()); pt.Attr[b + 1] = K(Roll()); pt.Attr[b + 2] = K(Roll()); pt.Attr[b + 3] = K(Roll()); pt.Attr[b + 4] = K(Roll());
        w.Economy.AddPerson(p);
        w.Vital.Alive++; w.Vital.Immigrants++;
        w.NoteBirth(p);
        return id.Value;
    }

    /// <summary>
    /// The host leaves the region (it marches out at a gate, or the bubble gives it back): the living persons made for its men from outside become <c>Departed</c> rows again (they are counts of the roster, the roster as it
    /// stands after the dead were taken off), the dead stay dead with the causes the fight gave them. Returns how many went. Nothing is made or lost: the men who are gone are the men the roster no longer counts.
    /// </summary>
    public int LeaveRegion(ArmyEnv e, HostId host)
    {
        var hs = Supply; if (hs == null || !Book.Marches.TryGetValue(host.Value, out var m)) return 0;
        SyncDead(hs, m);
        var pt = People; int n = 0;
        foreach (var id in m.Outsiders.Keys)
        {
            int r = RowOf(id); if (r < 0 || pt.Status[r] != PersonStatus.Alive) continue;
            DepartRow(e, r); n++;
        }
        m.InRegion = false;
        return n;
    }

    /// <summary>P7R-6: a man who leaves the region leaves through <see cref="Ravensguard.Sim.People.Vital.Demography.Depart"/> (the year counter, the outbox count, the continent link's emigrants, his offices); only a world with no outbox falls back to the bare row change.</summary>
    internal void DepartRow(ArmyEnv e, int r)
    {
        if (e.Ob != null) { Ravensguard.Sim.People.Vital.Demography.Depart(W, r, new GameTime(e.Hour * 3600), e.Ob); return; }
        People.Status[r] = PersonStatus.Departed; W.Vital.Alive--; W.Vital.Emigrants++;
    }
}
