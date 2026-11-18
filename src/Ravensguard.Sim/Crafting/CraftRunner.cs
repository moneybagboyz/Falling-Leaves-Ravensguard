using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Crafting;

/// <summary>The one who works: an id and the attributes the recipes read (default 50).</summary>
public sealed record CraftActor(EntityId Id, int Strength = 50, int Wits = 50, int Perception = 50, int FatiguePm = 0)
{
    public int Attr(string name) => name switch { "strength" => Strength, "wits" => Wits, "perception" => Perception, _ => 50 };
}

/// <summary>
/// The crafting engine (basic-crafting spec 3 and 4): plans a recipe for a crafter at a site (what is missing, how well it will go), starts it by moving the components into the job's own store, advances it
/// by game seconds, handles unattended steps with their deadline, grace and ruin, rolls failure at the critical step, makes the result and credits skill and proficiency by use. Hero and NPC use the same calls.
/// Goods and coin are only ever moved, created by a faucet of the mass ledger or destroyed as a counted sink, so the audit holds.
/// </summary>
public sealed class CraftRunner
{
    public RecipeDb Db { get; }
    public CraftState State { get; }
    public EconomyState Econ { get; }
    private readonly ulong _seed;
    private readonly IdAllocator _ids;
    private readonly Func<EntityId, Store?> _storeOf;
    private readonly ushort _waste, _rags;

    public CraftRunner(RecipeDb db, EconomyState econ, CraftState state, ulong seed, IdAllocator ids, Func<EntityId, Store?> storeOf)
    {
        Db = db; Econ = econ; State = state; _seed = seed; _ids = ids; _storeOf = storeOf;
        db.Goods.TryIdOf("waste", out _waste); db.Goods.TryIdOf("rags", out _rags);
    }

    private GoodCatalog Cat => Db.Goods;
    private bool Ambient(ushort g) => Cat.HasTag(g, "ambient");
    private int SkillOf(EntityId who, CraftRecipe r) => r.Skill == Skill.None ? 40 : State.Book.Level(who, r.Skill);

    // ------------------------------------------------------------------ planning

    /// <summary>Everything the screen and the start need to know about a recipe for this crafter here (spec 4.7). <paramref name="forced"/> picks alternatives by slot (-1 for the default).</summary>
    public CraftPlan Plan(CraftRecipe r, CraftActor who, CraftSite site, Store? src, int batch = 1, int[]? forced = null, bool hasTarget = true)
    {
        var p = new CraftPlan { Recipe = r, Batch = Math.Clamp(batch, 1, r.BatchMax) };
        if (batch > r.BatchMax) p.Missing.Add($"batch at most {r.BatchMax}");
        if (!State.Know.Knows(r, who.Id, State.Book)) { p.Known = false; p.Missing.Add("recipe not known"); }
        p.Skill = SkillOf(who.Id, r);

        // components: the first alternative that can be supplied, or the one asked for
        p.Slots = new SlotPlan[r.Slots.Length]; p.Choice = new byte[r.Slots.Length];
        var reserved = new Dictionary<ushort, long>();
        for (int i = 0; i < r.Slots.Length; i++)
        {
            var slot = r.Slots[i]; var sp = new SlotPlan { Have = new long[slot.Alts.Length], Need = new long[slot.Alts.Length] };
            for (int j = 0; j < slot.Alts.Length; j++)
            {
                var a = slot.Alts[j]; sp.Need[j] = a.Qty * p.Batch;
                long have = src?.Qty(a.Good) ?? 0; have -= reserved.GetValueOrDefault(a.Good);
                if (Ambient(a.Good) && site.Water) have = Math.Max(have, sp.Need[j]);
                sp.Have[j] = Math.Max(0, have);
            }
            int pick = forced != null && i < forced.Length && forced[i] >= 0 && forced[i] < slot.Alts.Length && sp.Have[forced[i]] >= sp.Need[forced[i]] ? forced[i] : -1;
            if (pick < 0) for (int j = 0; j < slot.Alts.Length; j++) if (sp.Have[j] >= sp.Need[j]) { pick = j; break; }
            sp.Chosen = pick; p.Slots[i] = sp; p.Choice[i] = (byte)Math.Max(0, pick);
            if (pick >= 0) reserved[slot.Alts[pick].Good] = reserved.GetValueOrDefault(slot.Alts[pick].Good) + sp.Need[pick];
            else p.Missing.Add("need " + string.Join(" or ", slot.Alts.Select((a, j) => $"{Amount(a.Good, sp.Need[j])} {Cat.Def(a.Good).Name.ToLowerInvariant()}")));
        }
        // fuel of the steps
        foreach (var s in r.Steps)
        {
            if (s.FuelGood == ItemTable.NoGood) continue;
            long need = s.FuelQty * p.Batch, have = (src?.Qty(s.FuelGood) ?? 0) - reserved.GetValueOrDefault(s.FuelGood);
            if (have < need) p.Missing.Add($"need {Amount(s.FuelGood, need)} {Cat.Def(s.FuelGood).Name.ToLowerInvariant()} as fuel");
            else reserved[s.FuelGood] = reserved.GetValueOrDefault(s.FuelGood) + need;
        }
        if (r.IsRepair && !hasTarget) p.Missing.Add("nothing to mend");

        // tools and stations
        int toolBonus = 0, toolPm = 1000; bool improvised = false; int over = 0;
        foreach (var need in Db.AllNeeds(r))
        {
            switch (site.Check(need, out int o))
            {
                case CraftSite.Supply.Real: over = Math.Max(over, o); break;
                case CraftSite.Supply.Improvised: improvised = true; break;
                default: p.Missing.Add($"need {need.Quality} {need.Level}"); break;
            }
        }
        if (r.Workstation != null && !site.Stations.Contains(r.Workstation)) p.Missing.Add($"need a {Db.Stations[r.Workstation].Name.ToLowerInvariant()}");
        if (Db.NeedsWater(r) && !site.Water) p.Missing.Add("need water nearby");
        if (improvised) { toolBonus = -10; toolPm = 1500; } else toolBonus = Math.Min(10, over * 2);
        toolBonus = Math.Clamp(toolBonus + site.StationBonus, -10, 10);
        foreach (var (a, v) in r.MinAttr) if (who.Attr(a) < v) p.Missing.Add($"needs {a} {v}");

        // light
        if (r.MinLight > 0)
        {
            if (site.Light < r.MinLight) { p.Light = LightState.TooDark; p.Missing.Add("too dark"); }
            else if (site.Light < r.MinLight + 40) { p.Light = LightState.Dim; p.LightPenalty = -5; }
        }

        // proficiencies, skill, time
        int eff = p.Skill, profPm = 1000;
        foreach (var u in r.Profs)
        {
            int pm = State.Know.Pm(who.Id, u.Prof);
            eff -= CraftMath.ProfSkillPenalty(u.SkillPenalty, pm);
            profPm = profPm * CraftMath.ProfTimePm(u.TimeMultPm, pm) / 1000;
        }
        p.EffectiveSkill = eff; p.ToolBonus = toolBonus; p.ToolPm = toolPm; p.ProfPm = profPm;
        int skillPm = CraftMath.SkillFactorPm(eff, r.Difficulty);
        p.TimePm = skillPm;
        for (int i = 0; i < r.Steps.Length; i++)
        {
            var s = r.Steps[i];
            if (s.Conditional && !ConditionalOn(r, p.Choice, s.Id)) continue;
            if (s.Attended) p.AttendedSeconds += CraftMath.BatchSeconds(CraftMath.StepSeconds(s.Seconds, profPm, toolPm, skillPm), p.Batch, r.TimeScalePm);
            else p.UnattendedSeconds += s.Seconds;
        }
        p.FailPm = CraftMath.FailPm(eff, r.Difficulty); p.WastePct = CraftMath.WastePct(eff, r.Difficulty);
        p.QualityCounts = CraftMath.QualityCounts(eff, toolBonus, p.LightPenalty);
        p.CanStart = p.Missing.Count == 0;
        return p;
    }

    private static bool ConditionalOn(CraftRecipe r, byte[] choice, string stepId)
    {
        for (int i = 0; i < r.Slots.Length && i < choice.Length; i++) if (r.Slots[i].Alts[choice[i]].NeedsStep == stepId) return true;
        return false;
    }

    private static string Amount(ushort g, long qty) => qty.ToString();

    // ------------------------------------------------------------------ starting

    /// <summary>Starts the work: the components (and the fuel of the steps) are moved from <paramref name="src"/> into the job's own store. Null when the plan cannot start or a move fails (nothing is changed).</summary>
    public CraftJob? Start(CraftPlan plan, CraftActor who, EntityId owner, Store src, long nowSec, long placeX = 0, long placeY = 0, EntityId target = default)
    {
        if (!plan.CanStart) return null;
        var r = plan.Recipe;
        if (r.IsRepair)
        {
            if (!Econ.Items.IsLive(target)) return null;
            if (Econ.Items.Get(target).ConditionPermille >= r.Repair!.Max) return null;
        }
        var id = _ids.Allocate(IdKind.Other);
        var store = Econ.OpenStore(StoreKind.House, id, 0, 0, false);
        var moved = new List<(ushort Good, long Qty, bool Made)>();
        bool ok = true;
        void Take(ushort g, long qty)
        {
            if (!ok || qty <= 0) return;
            long fromSrc = Ambient(g) ? Math.Min(qty, src.Qty(g)) : qty;
            if (!Store.Move(src, store, g, fromSrc)) { ok = false; return; }
            moved.Add((g, fromSrc, false));
            if (qty > fromSrc) { if (store.Create(g, qty - fromSrc, CreateReason.Other)) moved.Add((g, qty - fromSrc, true)); else ok = false; }
        }
        for (int i = 0; i < r.Slots.Length; i++) { var a = r.Slots[i].Alts[plan.Choice[i]]; Take(a.Good, a.Qty * plan.Batch); }
        foreach (var s in r.Steps) if (s.FuelGood != ItemTable.NoGood) Take(s.FuelGood, s.FuelQty * plan.Batch);
        if (!ok)
        {
            foreach (var (g, q, made) in moved) { if (made) store.Destroy(g, q, DestroyReason.Other); else Store.Move(store, src, g, q); }
            Econ.OtherStores.Remove(id);
            return null;
        }
        var job = new CraftJob
        {
            Id = id, Recipe = r.Index, Crafter = who.Id, Owner = owner, PlaceX = placeX, PlaceY = placeY, Batch = plan.Batch, StartedSec = nowSec, Choice = (byte[])plan.Choice.Clone(),
            ProfPm = plan.ProfPm, ToolPm = plan.ToolPm, SkillPm = plan.TimePm, EffSkill = plan.EffectiveSkill, ToolBonus = plan.ToolBonus, LightPenalty = plan.LightPenalty, Target = target,
        };
        job.StepIndex = FirstStep(r, job, 0);
        State.Add(job);
        return job;
    }

    private static int FirstStep(CraftRecipe r, CraftJob j, int from)
    {
        int i = from;
        while (i < r.Steps.Length && r.Steps[i].Conditional && !ConditionalOn(r, j.Choice, r.Steps[i].Id)) i++;
        return i;
    }

    /// <summary>Seconds the attended step takes for this job (spec 4.2).</summary>
    public long StepNeed(CraftJob j, int index)
    {
        var r = Db[j.Recipe]; var s = r.Steps[index];
        if (!s.Attended) return s.Seconds;
        return CraftMath.BatchSeconds(CraftMath.StepSeconds(s.Seconds, j.ProfPm, j.ToolPm, j.SkillPm), j.Batch, r.TimeScalePm);
    }

    // ------------------------------------------------------------------ working

    /// <summary>
    /// The crafter works for <paramref name="seconds"/> game seconds from <paramref name="nowSec"/>: progress inside the step, and at each step's end the skill and proficiency credit, the fuel burned, the
    /// failure roll of the critical step, then the next step, an unattended wait or the finished result. Returns what happened, in order.
    /// </summary>
    public List<CraftEvent> Advance(CraftJob j, long seconds, long nowSec)
    {
        var ev = new List<CraftEvent>(); var r = Db[j.Recipe];
        long t = nowSec;
        while (seconds > 0 && j.State == CraftJobState.Active && j.StepIndex < r.Steps.Length)
        {
            long need = StepNeed(j, j.StepIndex), take = Math.Min(seconds, need - j.StepSeconds);
            j.StepSeconds += take; j.TotalSeconds += take; seconds -= take; t += take;
            if (j.StepSeconds < need) break;
            CompleteStep(j, r, t, ev);
        }
        return ev;
    }

    private void CompleteStep(CraftJob j, CraftRecipe r, long now, List<CraftEvent> ev)
    {
        var s = r.Steps[j.StepIndex]; long worked = j.StepSeconds;
        j.StepSeconds = 0;
        // credit: one meaningful use of the skill and the proficiency hours
        bool capped = false;
        if (r.Skill != Skill.None)
        {
            int dx = r.IsPractice ? r.PracticeDx100 : CraftMath.UseWeight(r.Difficulty);
            bool master = TeachingNow(j.Crafter, r.Index);
            long gain = State.Book.Use(j.Crafter, r.Skill, dx, r.Difficulty, now, 0, master, r.IsPractice ? r.PracticeCap : 100);
            if (r.IsPractice && State.Book.Level(j.Crafter, r.Skill) >= r.PracticeCap && gain == 0) capped = true;
            if (gain > 0) ev.Add(new CraftEvent(CraftEventKind.SkillGain, j.Id, j.Crafter, now, r.Skill.ToString(), (int)gain));
        }
        if (!capped) foreach (var u in r.Profs) State.Know.Practise(Db, j.Crafter, u.Prof, worked, TeachingNow(j.Crafter, r.Index) ? Db.Prof(u.Prof).TeachMultPm : 1000);
        var store = Econ.OtherStores[j.Id];
        if (s.FuelGood != ItemTable.NoGood) store.Destroy(s.FuelGood, Math.Min(store.Qty(s.FuelGood), s.FuelQty * j.Batch), DestroyReason.Burned);
        if (s.Critical && !j.Failed)
        {
            int fail = CraftMath.FailPm(j.EffSkill, r.Difficulty);
            if (fail > 0 && DetRng.Range(_seed, (long)j.Id.Counter, j.Crafter.Value, RngStreams.CraftFailure, (uint)j.StepIndex, 0, 1000) < fail)
            {
                j.Failed = true; j.WastePct = CraftMath.WastePct(j.EffSkill, r.Difficulty); Waste(j, store, j.WastePct);
                ev.Add(new CraftEvent(CraftEventKind.Failed, j.Id, j.Crafter, now, r.Id, j.WastePct));
            }
        }
        ev.Add(new CraftEvent(CraftEventKind.StepDone, j.Id, j.Crafter, now, s.Id));
        j.StepIndex = FirstStep(r, j, j.StepIndex + 1);
        EnterStep(j, r, now, ev);
    }

    private void EnterStep(CraftJob j, CraftRecipe r, long now, List<CraftEvent> ev)
    {
        if (j.StepIndex >= r.Steps.Length) { Finish(j, r, now, ev); return; }
        var s = r.Steps[j.StepIndex];
        if (s.Attended) return;
        j.State = CraftJobState.Waiting; j.AlertMask = 0;
        j.ReadySec = now + s.Seconds;
        long dl = s.DeadlineSec; if (s.Outdoor && j.Rained) dl /= 2;
        j.DeadlineSec = j.ReadySec + dl; j.RuinSec = j.DeadlineSec + s.GraceSec;
        ev.Add(new CraftEvent(CraftEventKind.Waiting, j.Id, j.Crafter, now, s.Id));
    }

    /// <summary>
    /// A failure wastes <paramref name="pct"/> percent of every component in the job's store: the mass becomes the <c>waste</c> good, so mass is conserved. The share is exact on average:
    /// <c>q x pct / 100</c> whole units, plus one more with probability of the remainder (stream 0x363 keyed by the job and the good), so a stack of 3 pieces at 10 percent loses one
    /// three times in ten rather than never (review finding 8).
    /// </summary>
    private void Waste(CraftJob j, Store store, int pct)
    {
        long massLost = 0;
        foreach (var (g, q) in store.Rows())
        {
            long lose = WasteOf(_seed, j.Id, g, q, pct);
            if (lose <= 0) continue;
            store.Destroy(g, lose, DestroyReason.Other); massLost += Cat.MassG(g, lose);
        }
        if (massLost > 0) store.Create(_waste, massLost, CreateReason.Other);
    }

    /// <summary>How much of a stack of <paramref name="q"/> a failure at <paramref name="pct"/> percent wastes (see <see cref="Waste"/>): integer, a pure function of (seed, job, good).</summary>
    public static long WasteOf(ulong seed, EntityId job, ushort good, long q, int pct)
    {
        if (q <= 0 || pct <= 0) return 0;
        if (pct >= 100) return q;
        long whole = (long)((Int128)q * pct / 100), rem = (long)((Int128)q * pct % 100);
        if (rem > 0 && DetRng.Range(seed, (long)job.Counter, job.Value, RngStreams.CraftWaste, good, 0, 100) < rem) whole++;
        return Math.Min(q, whole);
    }

    // ------------------------------------------------------------------ unattended work

    /// <summary>
    /// Takes the work out of its unattended step (collect the risen dough). Too early: false. Past the ruin time it is ruined. In the grace window the result loses a tier or two. The finisher is credited
    /// from here on, and the step after it is entered.
    /// </summary>
    public (bool Ok, List<CraftEvent> Events) Collect(CraftJob j, CraftActor who, long nowSec)
    {
        var ev = new List<CraftEvent>(); var r = Db[j.Recipe];
        if (j.State != CraftJobState.Waiting || nowSec < j.ReadySec) return (false, ev);
        int late = CraftMath.LateTiers(nowSec, j.DeadlineSec, j.RuinSec);
        if (late < 0) { Ruin(j, r, nowSec, ev); return (true, ev); }
        j.TierDrop = Math.Max(j.TierDrop, late);
        TakeOver(j, r, who);
        j.State = CraftJobState.Active; j.StepSeconds = 0;
        ev.Add(new CraftEvent(CraftEventKind.StepDone, j.Id, who.Id, nowSec, r.Steps[j.StepIndex].Id, late));
        j.StepIndex = FirstStep(r, j, j.StepIndex + 1);
        EnterStep(j, r, nowSec, ev);
        return (true, ev);
    }

    /// <summary>The hourly pass (spec 4.8, H5): alerts at ready, at the deadline and an hour before ruin, and the ruin itself. No per-tick work for waiting jobs.</summary>
    public List<CraftEvent> Hourly(long nowSec)
    {
        var ev = new List<CraftEvent>();
        foreach (var (at, id, kind) in State.DueQueue().ToList())
        {
            if (at > nowSec) break;
            var j = State.Get(id); if (j == null || j.State != CraftJobState.Waiting) continue;
            switch (kind)
            {
                case CraftEventKind.Ready: j.AlertMask |= 1; ev.Add(new CraftEvent(kind, id, j.Owner, at, Db[j.Recipe].Name)); break;
                case CraftEventKind.PastDeadline: j.AlertMask |= 2; ev.Add(new CraftEvent(kind, id, j.Owner, at, Db[j.Recipe].Name)); break;
                case CraftEventKind.Ruining: j.AlertMask |= 4; ev.Add(new CraftEvent(kind, id, j.Owner, at, Db[j.Recipe].Name)); break;
                case CraftEventKind.Ruined: Ruin(j, Db[j.Recipe], at, ev); break;
            }
        }
        return ev;
    }

    /// <summary>Rain on an uncovered outdoor step halves what is left of its deadline (spec 4.5); the ruin time follows.</summary>
    public void Rain(CraftJob j)
    {
        var r = Db[j.Recipe];
        if (j.State != CraftJobState.Waiting || j.Rained || !r.Steps[j.StepIndex].Outdoor) return;
        j.Rained = true; var s = r.Steps[j.StepIndex];
        j.DeadlineSec = j.ReadySec + s.DeadlineSec / 2; j.RuinSec = j.DeadlineSec + s.GraceSec;
    }

    private void Ruin(CraftJob j, CraftRecipe r, long now, List<CraftEvent> ev)
    {
        var store = Econ.OtherStores[j.Id]; var s = r.Steps[j.StepIndex];
        long mass = 0;
        foreach (var (g, q) in store.Rows()) { mass += Cat.MassG(g, q); store.Destroy(g, q, DestroyReason.Other); }
        var into = s.RuinedInto != ItemTable.NoGood ? s.RuinedInto : _waste;
        if (mass > 0) store.Create(into, mass, CreateReason.Other);
        var dest = _storeOf(j.Owner);
        if (dest != null) Store.MoveUpTo(store, dest, into, store.Qty(into));
        if (store.RowCount == 0) Econ.OtherStores.Remove(j.Id);
        j.State = CraftJobState.Ruined;
        ev.Add(new CraftEvent(CraftEventKind.Ruined, j.Id, j.Owner, now, Cat.Def(into).Name, (int)mass));
    }

    // ------------------------------------------------------------------ interrupting and resuming

    public void Pause(CraftJob j) { if (j.State == CraftJobState.Active) j.State = CraftJobState.Paused; }

    /// <summary>
    /// Anyone who knows the recipe can pick up an unfinished job (spec 3.3): progress, the components and the time factors stay; the new crafter's skill counts for the quality of what they finish.
    /// </summary>
    public bool Resume(CraftJob j, CraftActor who)
    {
        var r = Db[j.Recipe];
        if (j.Closed || j.State == CraftJobState.Waiting) return false;
        if (!State.Know.Knows(r, who.Id, State.Book)) return false;
        TakeOver(j, r, who);
        j.State = CraftJobState.Active;
        return true;
    }

    private void TakeOver(CraftJob j, CraftRecipe r, CraftActor who)
    {
        if (j.Crafter == who.Id) return;
        j.Crafter = who.Id;
        int eff = SkillOf(who.Id, r);
        foreach (var u in r.Profs) eff -= CraftMath.ProfSkillPenalty(u.SkillPenalty, State.Know.Pm(who.Id, u.Prof));
        j.EffSkill = eff;
    }

    // ------------------------------------------------------------------ the result

    private void Finish(CraftJob j, CraftRecipe r, long now, List<CraftEvent> ev)
    {
        var store = Econ.OtherStores[j.Id]; var dest = _storeOf(j.Owner) ?? store;
        var quality = CraftMath.Drop(ItemQualityRules.FromScore(ItemQualityRules.Score(_seed, j.StartedSec, j.Crafter, (uint)(j.Id.Counter & 0xFFFFF) * 64, j.EffSkill, j.ToolBonus) + j.LightPenalty),
            j.TierDrop + (j.Failed ? 1 : 0));
        // components that are kept (moulds) go back
        for (int i = 0; i < r.Slots.Length; i++)
        {
            var a = r.Slots[i].Alts[j.Choice[i]];
            if (a.Keep) Store.MoveUpTo(store, dest, a.Good, a.Qty * j.Batch);
        }
        long labourH = Math.Max(1, (j.TotalSeconds + 1799) / 3600);
        int made = 0;
        if (!r.IsPractice)
            for (int k = 0; k < r.Results.Length; k++)
            {
                var x = r.Results[k];
                if (x.IsItem)
                {
                    for (int u = 0; u < j.Batch; u++)
                    {
                        var q = CraftMath.Drop(ItemQualityRules.FromScore(ItemQualityRules.Score(_seed, j.StartedSec, j.Crafter, (uint)(j.Id.Counter & 0xFFFFF) * 64 + (uint)(k * 8 + u + 1), j.EffSkill, j.ToolBonus) + j.LightPenalty),
                            j.TierDrop + (j.Failed ? 1 : 0));
                        var id = Econ.Items.Add(_ids, x.Shape!, q, j.Owner, [new ItemComponent(x.Material!, (int)x.MassG, 1000)], ItemTable.NoGood, (int)labourH, Math.Clamp(j.EffSkill, 0, 100));
                        State.Records[id] = new CraftedRecord(r.Index, [.. r.Slots.Select((s, i) => (s.Alts[j.Choice[i]].Good, s.Alts[j.Choice[i]].Qty))]);
                        made++;
                        ev.Add(new CraftEvent(CraftEventKind.Finished, j.Id, j.Crafter, now, x.Shape!, (int)q));
                    }
                }
                else
                {
                    long qty = x.Qty * j.Batch * (100 - j.WastePct) / 100;
                    if (!Cat.IsPiece(x.Good)) qty = qty * CraftMath.YieldPm(quality) / 1000;
                    if (qty > 0) dest.CreateUpTo(x.Good, qty, CreateReason.Crafted);
                    made++;
                    ev.Add(new CraftEvent(CraftEventKind.Finished, j.Id, j.Crafter, now, Cat.Def(x.Good).Id, (int)Math.Min(int.MaxValue, qty)));
                }
            }
        for (int b = 0; b < r.Byproducts.Length; b++)
        {
            var by = r.Byproducts[b];
            if (by.ChancePm < 1000 && DetRng.Range(_seed, (long)j.Id.Counter, j.Id.Value, RngStreams.CraftByproduct, (uint)b, 0, 1000) >= by.ChancePm) continue;
            dest.CreateUpTo(by.Good, by.Qty * j.Batch, CreateReason.Crafted);
        }
        if (r.IsRepair && Econ.Items.IsLive(j.Target))
        {
            ref var row = ref Econ.Items.Get(j.Target); var rp = r.Repair!;
            row.ConditionPermille = (short)Math.Min(rp.Max, row.ConditionPermille + rp.Add);
            if (rp.Flag == "repaired") row.Flags |= ItemFlags.Repaired;
        }
        if (made == 0) ev.Add(new CraftEvent(CraftEventKind.Finished, j.Id, j.Crafter, now, r.Effect.Length > 0 ? r.Effect : r.Id, (int)quality));
        // what is left in the job's store was consumed (burned for a fire)
        var why = r.Effect == "lit_fire" ? DestroyReason.Burned : DestroyReason.Consumed;
        foreach (var (g, q) in store.Rows()) store.Destroy(g, q, why);
        if (!ReferenceEquals(store, dest)) Econ.OtherStores.Remove(j.Id);
        j.State = CraftJobState.Done;
    }

    // ------------------------------------------------------------------ taking things apart (K-19)

    /// <summary>
    /// Takes a crafted item apart: from <c>difficulty - 10</c> of skill it is allowed and teaches the recipe (when the recipe says so); 70 percent by mass of each recorded component comes back (floor), and the
    /// rest of the mass is returned as <c>rags</c>, so the total is exact. False when the item was not crafted, may not be taken apart or the skill is too low.
    /// </summary>
    public bool Disassemble(EntityId item, CraftActor who, Store dest)
    {
        if (!Econ.Items.IsLive(item) || !State.Records.TryGetValue(item, out var rec)) return false;
        var r = Db[rec.Recipe];
        if (!r.Decomp || !CraftMath.CanDisassemble(SkillOf(who.Id, r), r.Difficulty)) return false;
        long total = 0, back = 0;
        foreach (var (g, q) in rec.Parts)
        {
            long ret = CraftMath.ReturnG(q);
            total += Cat.MassG(g, q);
            if (ret > 0) { dest.Create(g, ret, CreateReason.Other); back += Cat.MassG(g, ret); }
        }
        if (total > back) dest.Create(_rags, total - back, CreateReason.Other);
        Econ.Items.Free(item); State.Records.Remove(item);
        State.Know.Learn(who.Id, r.Index);
        return true;
    }

    // ------------------------------------------------------------------ learning (spec 4.6)

    private bool TeachingNow(EntityId student, ushort recipe) => State.Sessions.Any(s => s.Student == student.Value && s.Recipe == recipe && !s.Done);

    /// <summary>Starts a session of a master teaching a recipe. Paid by coin (student to master) now, or by labour. Null when the master cannot teach it or the student cannot pay.</summary>
    public TeachSession? StartTeaching(CraftActor student, CraftActor master, CraftRecipe r, bool byLabour, Ledger? ledger = null, PurseId? from = null, PurseId? to = null)
    {
        if (!r.MasterTeaches || !State.Know.Knows(r, master.Id, State.Book) || !CraftMath.CanTeach(SkillOf(master.Id, r), r.Difficulty)) return null;
        if (State.Know.Knows(r, student.Id, State.Book)) return null;
        var s = new TeachSession { Student = student.Id.Value, Master = master.Id.Value, Recipe = r.Index, NeededSec = CraftMath.LearnHoursX10(r.Difficulty) * 360L, CostMites = CraftMath.TeachCostMites(r.Difficulty), ByLabour = byLabour };
        if (!byLabour)
        {
            if (ledger == null || from == null || to == null || !ledger.Transfer(from.Value, to.Value, new Coin(s.CostMites), TransferReason.Wage)) return null;
            s.Paid = true;
        }
        State.Sessions.Add(s);
        return s;
    }

    /// <summary>Works the teaching sessions on by game seconds; at the end the recipe is known. The student gains skill at double rate while it lasts.</summary>
    public List<CraftEvent> AdvanceTeaching(long seconds, long nowSec)
    {
        var ev = new List<CraftEvent>();
        foreach (var s in State.Sessions.ToList())
        {
            long before = s.DoneSec; s.DoneSec = Math.Min(s.NeededSec, s.DoneSec + seconds);
            var r = Db[s.Recipe]; var student = new EntityId(s.Student);
            for (long h = before / 3600 + 1; h <= s.DoneSec / 3600; h++)
                State.Book.Use(student, r.Skill, CraftMath.UseWeight(r.Difficulty), r.Difficulty, nowSec - (s.DoneSec - h * 3600), 0, true);
            foreach (var u in r.Profs) State.Know.Practise(Db, student, u.Prof, s.DoneSec - before, Db.Prof(u.Prof).TeachMultPm);
            if (!s.Done) continue;
            State.Know.Learn(student, r.Index); State.Sessions.Remove(s);
            ev.Add(new CraftEvent(CraftEventKind.Learned, default, student, nowSec, r.Id));
        }
        return ev;
    }

    /// <summary>Time spent within 6 m of a crafter at this recipe counts (spec 4.6); returns the hours needed so the screen can show "watched 3 of 8.5 hours".</summary>
    public void Watch(CraftActor student, CraftRecipe r, long seconds) { if (!State.Know.Knows(r, student.Id, State.Book)) State.Know.AddWatch(student.Id, r.Index, seconds); }

    /// <summary>"Try it": once enough has been watched, the recipe is learnt with probability <c>400 + 5 x Wits + 3 x skill</c> permille (cap 950; stream 0x362). A failure sets the count back to half.</summary>
    public bool TryWatchLearn(CraftActor student, CraftRecipe r, long nowSec, List<CraftEvent>? ev = null)
    {
        long need = r.WatchHoursX10 * 360L;
        if (State.Know.Knows(r, student.Id, State.Book) || State.Know.WatchSeconds(student.Id, r.Index) < need) return false;
        int p = CraftMath.WatchProbPm(student.Wits, SkillOf(student.Id, r));
        int tries = State.Know.Tries(student.Id, r.Index); State.Know.AddTry(student.Id, r.Index);
        bool ok = DetRng.Range(_seed, 0, student.Id.Value, RngStreams.CraftWatch, (uint)r.Index * 16 + (uint)Math.Min(15, tries), 0, 1000) < p;
        if (ok) { State.Know.Learn(student.Id, r.Index); State.Know.SetWatch(student.Id, r.Index, 0); ev?.Add(new CraftEvent(CraftEventKind.Learned, default, student.Id, nowSec, r.Id)); }
        else State.Know.SetWatch(student.Id, r.Index, need / 2);
        return ok;
    }

    /// <summary>Reading a manuscript (spec 4.6): needs Literacy 20; teaches every recipe it lists and returns the game seconds the reading takes.</summary>
    public long ReadManuscript(CraftActor reader, string manuscriptId)
    {
        var m = Db.Manuscript(manuscriptId);
        if (m == null || State.Book.Level(reader.Id, Skill.Literacy) < 20) return -1;
        long sec = 0;
        foreach (var rid in m.Recipes)
        {
            var r = Db[rid]; long s = CraftMath.ManuscriptHoursX10(r.Difficulty) * 360L; sec += s;
            State.Know.Learn(reader.Id, r.Index);
            foreach (var u in r.Profs) State.Know.Practise(Db, reader.Id, u.Prof, s, Db.Prof(u.Prof).ManuscriptMultPm);
        }
        return sec;
    }
}
