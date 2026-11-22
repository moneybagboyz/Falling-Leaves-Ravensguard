using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Crafting;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Siege;

/// <summary>What an engine costs to build (sieges-and-engines 4.2): the goods its part recipes use up (grams by good), the mass of the finished parts, the recipes' attended hours and the install hours in worker-milli-hours at the nominal pace, the goods' value at their base prices.</summary>
public sealed record EngineBill(string Template, int Parts, SortedDictionary<string, long> GoodsG, long ComponentG, long PartsG, long CraftMilli, long InstallMilli, long ValueMites)
{
    public long WorkerHours => (CraftMilli + InstallMilli + 500) / 1000;
}

/// <summary>
/// Building an engine at the camp (sieges-and-engines 4.2): a vehicle site laid from the template, worked by a crew of carpenters in their working hours (08:00 to 18:00) through the same crafting engine and vehicle site as any wagon. Each hour every
/// hand either goes on with the part recipe he has started (the camp's timber, logs, iron bar, rope and hides are drawn from the camp store into the job and used up, the part is made at the recipe's own pace for his skill and goes to the site's
/// pile), or starts the next part nobody is making, or fits what is on the pile at the worker-hour rule. The owner pays each hand's hour through the ledger (the skill's hourly wage); a hand he cannot pay stands that hour. A finished
/// engine is taken into the <see cref="EngineBook"/> and the chronicle says so. Nothing is instant and no coin or goods appear: the bill is exactly what the recipes use.
/// </summary>
public sealed class EngineYard
{
    public RideBook Rides { get; }
    public CraftRunner Runner { get; }
    public CraftSite Site { get; }
    public EconomyState Econ { get; }
    public Ledger? Ledger { get; set; }
    public EngineBook? Book { get; set; }
    public SiegeChronicle? Chronicle { get; set; }

    public EngineYard(RideBook rides, CraftRunner runner, CraftSite site, EconomyState econ) { Rides = rides; Runner = runner; Site = site; Econ = econ; }

    /// <summary>The bill of a template: what its parts' recipes use, what they make, how many worker-hours they take at the nominal pace (VD-4's sum), the goods at their base prices.</summary>
    public static EngineBill Bill(VehicleBlueprint bp, VehicleCatalog cat, RecipeDb db, GoodCatalog goods)
    {
        var by = new SortedDictionary<string, long>(StringComparer.Ordinal); long comp = 0, made = 0, value = 0;
        foreach (var p in bp.Parts)
        {
            var d = cat.PartList[p.Part]; made += (long)d.MassKg * 1000;
            if (!db.TryGet(d.Recipe, out var r)) continue;
            foreach (var slot in r.Slots)
            {
                var a = slot.Alts[0]; long g = goods.MassG(a.Good, a.Qty); string id = goods.StringId(a.Good);
                by[id] = by.GetValueOrDefault(id) + g; comp += g; value += goods.BasePriceMilliMites(a.Good) * g / 1_000_000;
            }
        }
        var (rec, ins) = VehicleSites.BuildMilli(bp, cat, db);
        return new EngineBill(bp.Id, bp.Parts.Count, by, comp, made, rec, ins, value);
    }

    /// <summary>Lays the site of an engine at the camp (the vehicle row with every part a ghost) with its crew of hands and the Carpentry each has (in id order). The ordinary site pass leaves it alone: the yard works it.</summary>
    public (SiteAnswer, VehicleRow?) Lay(string template, EntityId owner, long xMm, long yMm, IEnumerable<(EntityId Person, int Skill)> crew)
    {
        if (Rides.Templates.Find(template) == null) return (SiteAnswer.NoDesign, null);
        if (Book != null && Book.Table.Stat(template) == null) return (SiteAnswer.NoDesign, null);
        var (a, v) = VehicleSites.Order(Rides, template, owner, xMm, yMm);
        if (a != SiteAnswer.Ok || v == null) return (a, v);
        var site = Rides.Works.Sites[v.Id];
        var hands = crew.OrderBy(c => c.Person).ToList();
        site.Crew = [.. hands.Select(h => h.Person)]; site.CrewSkill = [.. hands.Select(h => h.Skill)];
        site.Paused = true;
        return (SiteAnswer.Ok, v);
    }

    private CraftJob? JobOf(EntityId vehicle, EntityId who)
    {
        foreach (var j in Runner.State.Jobs.Values) if (j.Owner == vehicle && j.Crafter == who && j.State == CraftJobState.Active) return j;
        return null;
    }

    private CraftJob? StartNext(VehicleRow v, EntityId who, Store camp, long nowSec)
    {
        var cat = Rides.Templates; var db = Runner.Db;
        var wanted = VehicleSites.Wanted(v, cat);
        if (wanted.Count == 0) return null;
        var have = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in VehicleSites.Pile(Econ.Items, v.Id)) { string s = Econ.Items.Get(id).Shape; have[s] = have.GetValueOrDefault(s) + 1; }
        foreach (var j in Runner.State.Jobs.Values)
            if (j.Owner == v.Id && j.State == CraftJobState.Active) { string? s = db[j.Recipe].Results.Length > 0 ? db[j.Recipe].Results[0].Shape : null; if (s != null) have[s] = have.GetValueOrDefault(s) + 1; }
        var actor = new CraftActor(who);
        foreach (string shape in wanted.Distinct())
        {
            if (have.GetValueOrDefault(shape) >= wanted.Count(x => x == shape)) continue;
            if (!db.TryGet(cat.Parts[shape].Recipe, out var r)) continue;
            var plan = Runner.Plan(r, actor, Site, camp);
            if (!plan.CanStart) continue;
            var job = Runner.Start(plan, actor, v.Id, camp, nowSec);
            if (job != null) return job;
        }
        return null;
    }

    /// <summary>
    /// One game hour of the camp's work on an engine site (<paramref name="hour"/> counts from the world's start): every hand, in id order, works his hour if it is a working hour and the owner can pay him. Returns the worker-milli-hours put in.
    /// A part waiting for materials the camp store does not hold stalls its hand (the next part is tried; none makes his hour idle).
    /// </summary>
    public long Hour(VehicleRow v, long hour, Store camp)
    {
        if (!Rides.Works.Sites.TryGetValue(v.Id, out var site) || site.DoneSec > 0) return 0;
        int hod = (int)(hour % 24); if (hod < 8 || hod >= 18) return 0;
        long now = hour * 3600, total = 0;
        for (int k = 0; k < site.Crew.Count; k++)
        {
            var who = site.Crew[k]; int skill = site.CrewSkill[k];
            if (Ledger != null && who != site.Owner)
            {
                long wage = Commissions.WagePerHour(skill);
                if (!Ledger.TryFind(PurseKind.Person, site.Owner, out var from) || !Ledger.TryFind(PurseKind.Person, who, out var to)) continue;
                if (Ledger.Balance(from).Mites < wage) continue;
                Ledger.Transfer(from, to, new Coin(wage), TransferReason.Wage);
            }
            long budget = 3600, worked = 0;
            while (budget > 0)
            {
                var job = JobOf(v.Id, who) ?? StartNext(v, who, camp, now + (3600 - budget));
                if (job != null)
                {
                    long before = job.TotalSeconds;
                    Runner.Advance(job, budget, now + (3600 - budget));
                    long used = job.TotalSeconds - before;
                    if (job.State == CraftJobState.Done) Runner.State.Remove(job.Id);
                    if (used <= 0) break;
                    budget -= used; worked += used; site.WorkedMilli += used * 1000 / 3600;
                    continue;
                }
                long milli = VehicleSites.MilliPerHour(skill) * budget / 3600;
                long done = milli <= 0 ? 0 : VehicleSites.Work(Rides, Econ.Items, v, milli, now + 3600);
                if (done <= 0) break;
                long spent = budget * done / milli; budget -= spent; worked += spent;
            }
            total += worked * 1000 / 3600;
            if (v.Finished) break;
        }
        if (v.Finished && site.DoneSec > 0 && Book != null && Book.Raise(v, site.DoneSec) is { } e) Announce(e, v, site);
        return total;
    }

    private void Announce(Engine e, VehicleRow v, VehicleSite site)
    {
        if (Chronicle == null) return;
        int cx = EngineBook.CellOf(v.XMm), cy = EngineBook.CellOf(v.YMm);
        Chronicle(new GameTime(site.DoneSec), "engine_built", "ch_sg_engine_built", [ChronParam.TextKey("engine", "ch_sg_kind_" + e.Template), ChronParam.Count("x", cx), ChronParam.Count("y", cy), ChronParam.Count("hours", (site.WorkedMilli + 500) / 1000)], [],
            [new Cause(CauseKind.Siege, null, "", "ch_note_sg_carpenters")], [("x", cx.ToString()), ("y", cy.ToString()), ("engine", e.Template)]);
    }
}
