using Ravensguard.Sim.Armies;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WarSupply;
using Ravensguard.Sim.Unrest;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Crafting;

namespace Ravensguard.Sim.Siege;

public sealed partial class SiegeWorld
{
    // ------------------------------------------------------------------ the hour (engines and the yard)

    /// <summary>
    /// One abstract hour of every open siege's engines (SiegeSystem, H1 after the armies): the camp's carpenters work the engines under construction, a finished engine is crewed and aimed at the walls' aim point, the finished engines shoot
    /// their hour (SG-B's one step function) and a breach in the walls' box is noticed once (the siege enters Bombardment when the first engine stands and notes the breach day). A live assault pauses nothing here: the bubble owns the wall while it is open.
    /// </summary>
    public void Hour(ArmyEnv e)
    {
        _lastHour = e.Hour; _env = e;
        var book = Peek; if (book == null) return;
        foreach (var s in book.Sieges.Values)
        {
            if (!s.Open || s.Engines.Count == 0) continue;
            var kit = KitOf?.Invoke(s); var walls = WallsOf?.Invoke(s.Place); if (kit == null || walls == null) continue;
            kit.World.Walls = walls.World; kit.World.Cause = new EntityId(s.Entry);
            foreach (ulong vid in s.Engines)
            {
                var v = kit.Yard.Rides.Vehicles.Find(new EntityId(vid)); if (v == null) continue;
                if (!v.Finished) { kit.Yard.Hour(v, e.Hour, kit.Camp); continue; }
                var eng = kit.Book.Find(v.Id);
                if (eng != null && !eng.HasAim && !eng.Disabled && EngineTable.Shoots(eng.Kind)) Crew(s, kit, eng, walls);
            }
            if (s.State is SiegeState.Investing or SiegeState.Blockade && s.Engines.Any(id => kit.Book.Find(new EntityId(id)) is { HasAim: true })) s.State = SiegeState.Bombardment;
        }
        // the shooting: each siege's engines step to the end of the hour in id order (SG-B's one step function), by day only
        foreach (var s in book.Sieges.Values)
        {
            if (!s.Open || s.Engines.Count == 0 || s.State == SiegeState.Assault) continue;
            var kit = KitOf?.Invoke(s); var walls = WallsOf?.Invoke(s.Place); if (kit == null || walls == null) continue;
            kit.World.Walls = walls.World; kit.World.Cause = new EntityId(s.Entry);
            foreach (ulong vid in s.Engines) if (kit.Book.Find(new EntityId(vid)) is { } eng) kit.Book.Advance(eng, (e.Hour + 1) * 3600, kit.World, daylightOnly: true);
        }
        foreach (var s in book.Sieges.Values)
        {
            if (!s.Open || s.BreachDay >= 0) continue;
            var walls = WallsOf?.Invoke(s.Place); if (walls == null) continue;
            if (walls.World.Breaches(walls.X0, walls.Y0, walls.X1, walls.Y1).Count > 0) { s.BreachDay = (int)(e.Hour / 24); s.BreachNoted = true; }
        }
    }

    private void Crew(SiegeRow s, SiegeKit kit, Engine eng, SiegeWalls walls)
    {
        var men = Besiegers(s); var crew = new List<EntityId>();
        int slot = s.Engines.IndexOf(eng.Vehicle.Value);
        for (int i = slot * SiegeConfig.CrewPerEngine; i < men.Count && crew.Count < SiegeConfig.CrewPerEngine; i++) crew.Add(new EntityId(men[i]));
        kit.Book.Assign(eng, crew);
        kit.Book.Aim(eng, walls.AimXMm, walls.AimYMm);
    }

    // ------------------------------------------------------------------ the day (H5)

    /// <summary>The sieges' day (SiegesDaily, H5): each open siege in id order lines the place, feels the winter, tests whether the besieger must leave and whether the defender yields, offers terms, builds engines or storms.</summary>
    public void Day(ArmyEnv e)
    {
        _lastHour = e.Hour; _env = e;
        var book = Peek; if (book == null) return;
        foreach (var s in book.Sieges.Values.ToList()) if (s.Open) DayOf(e, s);
    }

    private void DayOf(ArmyEnv e, SiegeRow s)
    {
        var hs = Hs; if (hs == null) return;
        int day = (int)e.Day; var hid = new HostId(s.Supply);
        if (!Armies.Book.Hosts.TryGetValue(s.Host, out var host) || host.State == HostState.Disbanding || !hs.Exists(hid)) { Lift(e, s, "ch_note_sg_supply"); return; }
        if (s.State == SiegeState.Assault) { TickLive(e, s); if (s.State == SiegeState.Assault) return; }
        var men = Besiegers(s);
        if (s.State == SiegeState.Investing && day - s.StartDay >= SiegeConfig.LinesDays) { s.State = SiegeState.Blockade; hs.SiegeBlockade(s.Place, true, hid); }
        if (e.Winter) Winter(e, s, men, hs, hid);
        // the besieger leaves
        var st = Armies.RationsOf(s.Supply);
        if (men.Count * 8 < s.MenAtStart) { Lift(e, s, "ch_note_sg_supply"); return; }
        if (st.ShortDays >= SiegeConfig.LiftShortDays) { Lift(e, s, e.Winter ? "ch_note_sg_winter" : "ch_note_sg_supply"); return; }
        var garrison = Garrison(s.Place); bool relief = ReliefKnown(s);
        if (relief && ReliefStronger(s, men.Count)) { Lift(e, s, "ch_note_sg_relief"); return; }
        // the defender yields
        int food = hs.IsBlockaded(s.Place) ? hs.DaysOfFood(s.Place) : int.MaxValue;
        bool breach = s.BreachDay >= 0 && IsBreachOpen(s);
        string? why = null;
        if (food < SiegeConfig.StarveDays && !relief) why = "ch_note_sg_starved";
        else if (breach && garrison.Count * SiegeConfig.OutnumberedRatio < men.Count) why = "ch_note_sg_breach";
        else if (W.Get<UnrestBook>() is { } ubk && ubk.Riots.Any(r => r.Settlement == s.Place && r.ActedHour >= 0 && r.EndedHour < 0)) why = "ch_note_sg_town_rose";
        if (why != null) { Surrender(e, s, why); return; }
        // terms: at the start and after each breach
        bool offerDue = s.TermsDay < 0 ? day - s.StartDay >= SiegeConfig.LinesDays : (breach && s.TermsDay < s.BreachDay) || day - s.TermsDay >= SiegeConfig.TermsEveryDays * 4;
        if (offerDue && !Hero(host.Commander))
        {
            s.TermsDay = day; s.TermsCount++;
            Chron(e, "siege_terms", "ch_sg_terms", [ChronParam.Place("place", PlaceId(s.Place)), ChronParam.Count("offers", s.TermsCount)], [PlaceId(s.Place)], OnSiege(s, s.TermsCount == 1 ? "ch_note_sg_terms" : "ch_note_sg_terms_breach"));
            // the defender accepts when it is outmatched two to one behind a breach; else it holds and the answer is the besieger's next choice
            if (breach && garrison.Count * 2 < men.Count) { Surrender(e, s, "ch_note_sg_breach"); return; }
            s.RefusedDay = day;
        }
        if (Hero(host.Commander)) return;                                                   // the hero chooses for his own host
        Choose(e, s, host, men, garrison, breach, food);
    }

    /// <summary>The besieger's daily choice (4.3): storm a weak or breached place after the terms were refused, else build engines against walls, else starve the place out.</summary>
    private void Choose(ArmyEnv e, SiegeRow s, ArmyHost host, List<ulong> men, List<ulong> garrison, bool breach, int food)
    {
        int day = (int)e.Day; var walls = WallsOf?.Invoke(s.Place);
        if (NeverStorm) return;
        bool cooled = s.AssaultDay < 0 || day - s.AssaultDay >= SiegeConfig.AssaultCooldown;
        if (walls != null) { PlanEngines(e, s, host, walls); Refill(s, men); }               // the carpenters start with the lines; men who left the host are replaced at the benches
        if (!cooled || s.State == SiegeState.Investing) return;
        bool strong = men.Count >= Math.Max(1, garrison.Count) * 4;
        if (walls == null) { if (strong) Assault(e, s, host, men, garrison, breach: false, walled: false); return; }          // an unwalled place is stormed when the besieger is strong
        if (breach && s.RefusedDay >= s.TermsDay && men.Count >= garrison.Count) Assault(e, s, host, men, garrison, breach: true, walled: true);
    }

    /// <summary>Lays the engines the besieger can pay for at the camp: the bill's goods are hauled from the host's baggage to the camp's store (moved, never made); an engine the baggage cannot cover is not laid (the besieger starves the place instead).</summary>
    private void PlanEngines(ArmyEnv e, SiegeRow s, ArmyHost host, SiegeWalls walls)
    {
        var kit = KitOf?.Invoke(s); var hs = Hs; if (kit == null || hs == null) return;
        var rides = kit.Yard.Rides; var bag = hs.BaggageOf(new HostId(s.Supply)); var goods = kit.Yard.Econ.Goods;
        while (s.Engines.Count < SiegeConfig.Engines.Length)
        {
            string template = SiegeConfig.Engines[s.Engines.Count];
            var bill = EngineYard.Bill(rides.Templates.Get(template), rides.Templates, kit.Yard.Runner.Db, goods);
            foreach (var (good, g) in bill.GoodsG) { ushort id = goods.IdOf(good); long unit = Math.Max(1, goods.MassG(id, 1)); long need = ((g + unit - 1) / unit) * 5 / 4 + 2; if (bag.Qty(id) < need) return; }
            foreach (var (good, g) in bill.GoodsG) { ushort id = goods.IdOf(good); long unit = Math.Max(1, goods.MassG(id, 1)); long need = ((g + unit - 1) / unit) * 5 / 4 + 2; Store.Move(bag, kit.Camp, id, need); }
            var men = Besiegers(s); var crew = new List<(EntityId, int)>();
            foreach (ulong m in men.Skip(s.Engines.Count * SiegeConfig.BuildersPerEngine).Take(SiegeConfig.BuildersPerEngine)) { int r = Armies.RowOf(m); crew.Add((new EntityId(m), Math.Max(40, r >= 0 ? Armies.People.SkillLevelOf(r, Skill.Carpentry) : 0))); }
            var (ans, v) = kit.Yard.Lay(template, new EntityId(host.Commander), walls.LayXMm, walls.LayYMm + s.Engines.Count * 8000, crew);
            if (ans != SiteAnswer.Ok || v == null) return;
            kit.Yard.Book = kit.Book; s.Engines.Add(v.Id.Value);
            rides.Works.Sites[v.Id].Paused = false;
        }
    }

    /// <summary>The builders of an unfinished engine are the host's men who are still there: a man who left (desertion, hunger, the cold) is replaced by the next man of the roll, so the work does not stop with the people who began it.</summary>
    private void Refill(SiegeRow s, List<ulong> men)
    {
        var kit = KitOf?.Invoke(s); if (kit == null) return; var present = new HashSet<ulong>(men); var pt = Armies.People;
        for (int slot = 0; slot < s.Engines.Count; slot++)
        {
            if (!kit.Yard.Rides.Works.Sites.TryGetValue(new EntityId(s.Engines[slot]), out var site) || site.DoneSec > 0) continue;
            if (site.Crew.All(c => present.Contains(c.Value)) && site.Crew.Count >= Math.Min(SiegeConfig.BuildersPerEngine, men.Count / 2)) continue;
            var crew = new List<EntityId>(); var skill = new List<int>();
            foreach (ulong m in men.Skip(slot * SiegeConfig.BuildersPerEngine).Concat(men).Distinct().Take(SiegeConfig.BuildersPerEngine))
            { int r = Armies.RowOf(m); crew.Add(new EntityId(m)); skill.Add(Math.Max(40, r >= 0 ? pt.SkillLevelOf(r, Skill.Carpentry) : 0)); }
            site.Crew = crew; site.CrewSkill = skill;
            // a part its maker left half made passes to a man who has no job: the work is continued, never lost
            var busy = new HashSet<EntityId>(); var vid = new EntityId(s.Engines[slot]);
            foreach (var j in kit.Yard.Runner.State.Jobs.Values) if (j.Owner == vid && j.State == CraftJobState.Active && crew.Contains(j.Crafter)) busy.Add(j.Crafter);
            foreach (var j in kit.Yard.Runner.State.Jobs.Values.ToList())
                if (j.Owner == vid && j.State == CraftJobState.Active && !crew.Contains(j.Crafter))
                { var free = crew.FirstOrDefault(c => !busy.Contains(c)); if (free == default) break; j.Crafter = free; busy.Add(free); }
        }
    }

    private bool IsBreachOpen(SiegeRow s)
    {
        var walls = WallsOf?.Invoke(s.Place); if (walls == null) return false;
        return walls.World.Breaches(walls.X0, walls.Y0, walls.X1, walls.Y1).Count > 0;
    }

    // ------------------------------------------------------------------ relief and winter

    private bool ReliefKnown(SiegeRow s)
    {
        int defRealm = Hs?.RealmOf(s.Place) ?? -1; if (defRealm < 0) return false;
        foreach (var h in Armies.Book.Hosts.Values)
            if (h.Id != s.Host && h.Realm == defRealm && h.State is HostState.Marching or HostState.Camped && Armies.WalkDays(h.Point, s.Place) <= SiegeConfig.ReliefDays) return true;
        return false;
    }

    private bool ReliefStronger(SiegeRow s, int besiegers)
    {
        int defRealm = Hs?.RealmOf(s.Place) ?? -1; int best = 0;
        foreach (var h in Armies.Book.Hosts.Values) if (h.Id != s.Host && h.Realm == defRealm) best = Math.Max(best, h.Levies + h.Warbands.Sum(w => w.Men));
        return best > besiegers * 3 / 2;
    }

    /// <summary>
    /// A winter siege tests the camp (4.7): a host that the supply book could not give its day's firewood is exposed, each man rolling the cold's permille on the siege stream and dying of cold with the host's entry as cause; every winter day adds the siege's
    /// 10 permille to the desertion rule (<see cref="ArmyWorld.ExtraDesertPm"/>).
    /// </summary>
    private void Winter(ArmyEnv e, SiegeRow s, List<ulong> men, HostSupply hs, HostId hid)
    {
        var res = hs.Last(hid); if (res == null || res.ShortG[(int)SupplyNeed.Firewood] <= 0) return;
        var pt = Armies.People; var ob = e.Ob ?? new RegionOutbox(W.Ids); int dead = 0;
        foreach (ulong m in men)
        {
            int r = Armies.RowOf(m); if (r < 0 || pt.Status[r] != PersonStatus.Alive || Hero(m)) continue;
            if (DetRng.Range(W.Seed, e.Day, m, RngStreams.SiegeAiTies, (uint)s.Id, 0, 1000) >= SiegeConfig.ExposurePm) continue;
            Demography.Kill(W, r, DeathCause.Cold, e.Time, ob, chain: OnSiege(s, "ch_note_sg_cold")); dead++;
        }
        s.LostAttackers += dead;
    }
}
