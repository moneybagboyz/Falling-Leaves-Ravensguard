using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Crafting;

/// <summary>
/// P5-E (crafting-full 4.1, 4.3 to 4.5): what a finished job does beyond making goods and items: a refit changes a piece's size, salvage turns a piece into its stuff with the mass exact, reforging
/// makes a new piece from an old one, oil keeps rust off for 30 days, a kindled fire is a world object that burns its fuel; and the station-bound jobs of the workshop bridge.
/// </summary>
public sealed partial class CraftRunner
{
    /// <summary>A live business batch finished (the bridge completes its station).</summary>
    public Action<CraftJob>? StationDone { get; set; }
    /// <summary>An item a job made (commissions keep their items).</summary>
    public Action<CraftJob, EntityId>? Made { get; set; }
    /// <summary>The size a worn piece was made for (cm3) when the world keeps it (the clothing book); null reads the crafting state's own record, else 70,000.</summary>
    public Func<EntityId, int>? FitOf { get; set; }
    public Action<EntityId, int>? SetFit { get; set; }
    /// <summary>What a material is (metal, leather, wood, cloth) for salvage by category; null treats every component as of the line's category.</summary>
    public Func<string, string?>? MaterialCategory { get; set; }

    public const int DefaultBodyCm3 = 70_000, OilDays = 30;

    /// <summary>
    /// Set for the runner of the abstract tick (<see cref="CraftSystem"/>): an emptied store is not taken out of the economy in the middle of an hour (the other systems of the hour keep their
    /// order of stores) but listed in <see cref="CraftState.Dead"/> and taken out at the next barrier (<see cref="SweepDeadStores"/>).
    /// </summary>
    public bool InTick { get; set; }

    /// <summary>Takes an emptied store out of the economy now, or at the next barrier when working inside the tick.</summary>
    public void DropStore(EntityId id)
    {
        if (InTick) State.Dead.Add(id);
        else Econ.OtherStores.Remove(id);
    }

    /// <summary>The barrier's sweep: the stores emptied inside the tick go, if they are still empty.</summary>
    public void SweepDeadStores()
    {
        foreach (var id in State.Dead) if (Econ.OtherStores.TryGetValue(id, out var s) && s.RowCount == 0) Econ.OtherStores.Remove(id);
        State.Dead.Clear();
    }

    /// <summary>An item a job made: a commission keeps it for its client to collect; the hook is told.</summary>
    private void Kept(CraftJob j, EntityId item)
    {
        if (j.Commission != EntityId.None && State.Commissions.TryGetValue(j.Commission, out var cm)) cm.Items.Add(item);
        Made?.Invoke(j, item);
    }

    /// <summary>Seconds of work still in a job at its own speed: the fetching left, the rest of this step and every attended step after it.</summary>
    public long LeftSec(CraftJob j)
    {
        var r = Db[j.Recipe]; long sec = j.FetchLeft;
        for (int i = j.StepIndex; i < r.Steps.Length; i++)
        {
            if (j.Skipped(i) || r.Steps[i].Conditional && !ConditionalOn(r, j.Choice, r.Steps[i].Id) || !r.Steps[i].Attended) continue;
            sec += StepNeed(j, i) - (i == j.StepIndex ? j.StepSeconds : 0);
        }
        return Math.Max(0, sec);
    }

    // ---- the forge's heat (crafting-full 4.3) ----

    /// <summary>Whether the forge at a place is at working heat now (a smithing job there worked within the hour).</summary>
    public bool ForgeIsHot(long x, long y, long nowSec) => State.ForgeHot.TryGetValue((x, y), out var until) && nowSec <= until;

    private void ForgeHotUntil(CraftJob j, long now)
    {
        if (j.StationBound) return;
        State.ForgeHot[(j.PlaceX, j.PlaceY)] = now + Smithing.HotAfterSec;
    }

    // ---- the workshop bridge (crafting-full 4.4) ----

    private void FinishAtStation(CraftJob j, CraftRecipe r, long now, List<CraftEvent> ev)
    {
        j.State = CraftJobState.Done;
        StationDone?.Invoke(j);
        ev.Add(new CraftEvent(CraftEventKind.Finished, j.Id, j.Crafter, now, r.Id, 0));
    }

    // ---- refit (crafting-full 4.5) ----

    public int FitSize(EntityId item) => FitOf?.Invoke(item) is int f and > 0 ? f : State.Fits.TryGetValue(item, out var s) ? s : DefaultBodyCm3;

    /// <summary>The direction a refit would move a piece for a wearer of <paramref name="bodyCm3"/>: +1 grow, -1 shrink, 0 when it already fits or the recipe cannot move it that way (plate cannot grow).</summary>
    public int RefitStep(CraftRecipe r, EntityId item, int bodyCm3)
    {
        if (r.Refit == null || !Econ.Items.IsLive(item)) return 0;
        int body = bodyCm3 > 0 ? bodyCm3 : DefaultBodyCm3, cur = FitSize(item);
        if (cur == body) return 0;
        if (cur > body) return r.Refit.Shrink ? -1 : 0;
        return r.Refit.Grow ? 1 : 0;
    }

    /// <summary>One refit: a seventh of the wearer's body toward him (never past it). Growing mail weaves the wire it used into the piece; shrinking it takes rings out (the recipe's grow share of its mass).</summary>
    private void ApplyRefit(CraftJob j, CraftRecipe r)
    {
        int dir = RefitStep(r, j.Target, j.FitTo);
        if (dir == 0) return;
        int body = j.FitTo > 0 ? j.FitTo : DefaultBodyCm3, cur = FitSize(j.Target), step = Math.Max(1, body / 7);
        int next = dir > 0 ? Math.Min(body, cur + step) : Math.Max(body, cur - step);
        if (SetFit != null) SetFit(j.Target, next); else State.Fits[j.Target] = next;
        if (r.Refit!.GrowMassPm <= 0) return;
        int row = Econ.Items.IndexOf(j.Target); var ir = Econ.Items.At(row);
        if (ir.ComponentCount == 0) return;
        var c = Econ.Items.Component(ir.ComponentStart);
        var store = Econ.OtherStores.GetValueOrDefault(j.Id);
        if (dir > 0 && store != null && r.Slots.Length > 0)
        {
            // the wire goes into the mail: its mass leaves the job's store as consumed and joins the piece
            var a = r.Slots[0].Alts[j.Choice[0]]; long g = Math.Min(store.Qty(a.Good), a.Qty * j.Batch);
            long massG = Cat.MassG(a.Good, g);
            if (g > 0 && store.Destroy(a.Good, g, DestroyReason.Consumed)) Econ.Items.SetComponent(ir.ComponentStart, c with { MassG = (int)(c.MassG + massG) });
        }
        else if (dir < 0 && Cat.TryIdOf("mail_rings", out var rings))
        {
            long out1 = (long)c.MassG * r.Refit.GrowMassPm / 1000;
            if (out1 > 0 && _storeOf(j.Owner) is { } dest)
            {
                Econ.Items.SetComponent(ir.ComponentStart, c with { MassG = (int)(c.MassG - out1) });
                dest.Create(rings, out1, CreateReason.Other);
            }
        }
    }

    // ---- salvage (crafting-full 4.5) ----

    /// <summary>
    /// Salvage: the target's mass in grams, line by line (each line a permille of the components of its material category, or of the whole piece), floor; the rest of the mass goes to the rest good, so
    /// what comes out weighs exactly what went in (CF-16: 9 kg of mail -> 6.3 kg rings, 2.7 kg scrap). The piece is gone.
    /// </summary>
    public static List<(ushort Good, long Grams)> SalvageOf(CraftRecipe r, IReadOnlyList<ItemComponent> parts, Func<string, string?>? categoryOf)
    {
        var res = new List<(ushort, long)>();
        long total = 0; foreach (var p in parts) total += p.MassG;
        long given = 0;
        foreach (var l in r.Salvage)
        {
            long basis = 0;
            foreach (var p in parts) if (l.Category == null || categoryOf == null || categoryOf(p.Material) == l.Category) basis += p.MassG;
            long g = basis * l.Pm / 1000;
            if (g > 0) { res.Add((l.Good, g)); given += g; }
        }
        if (total > given) res.Add((r.SalvageRest, total - given));
        return res;
    }

    private void ApplySalvage(CraftJob j, CraftRecipe r, Store dest)
    {
        if (!Econ.Items.IsLive(j.Target)) return;
        int row = Econ.Items.IndexOf(j.Target);
        var parts = Econ.Items.ComponentsOf(row).ToList();
        foreach (var (g, q) in SalvageOf(r, parts, MaterialCategory)) dest.Create(g, q, CreateReason.Other);
        Econ.Items.Free(j.Target); State.Records.Remove(j.Target); State.Fits.Remove(j.Target); State.OiledUntil.Remove(j.Target);
    }

    // ---- reforge (crafting-full 4.5) ----

    /// <summary>A new piece of the same shape and material from the old one's components (plus the recipe's extra metal, worked in), at a new quality roll; the old piece is gone.</summary>
    private bool ApplyReforge(CraftJob j, CraftRecipe r, Store store, long now, List<CraftEvent> ev)
    {
        if (!Econ.Items.IsLive(j.Target)) return false;
        int row = Econ.Items.IndexOf(j.Target); var old = Econ.Items.At(row);
        var parts = Econ.Items.ComponentsOf(row).Select(c => c with { ConditionPermille = 1000 }).ToList();
        // the extra metal joins the main component (the first)
        if (parts.Count > 0 && r.Slots.Length > 0)
        {
            var a = r.Slots[0].Alts[j.Choice[0]]; long g = Math.Min(store.Qty(a.Good), a.Qty * j.Batch);
            if (g > 0 && store.Destroy(a.Good, g, DestroyReason.Consumed)) parts[0] = parts[0] with { MassG = (int)(parts[0].MassG + Cat.MassG(a.Good, g)) };
        }
        uint unit = (uint)(j.Id.Counter & 0xFFFFF) * 64 + 1;
        var q = CraftMath.Drop(ItemQualityRules.Crafted(_seed, j.StartedSec, j.Crafter, unit, ScoreOf(j, unit)), j.TierDrop + (j.Failed ? 1 : 0));
        string shape = old.Shape; var owner = old.Owner;
        Econ.Items.Free(j.Target); State.Records.Remove(j.Target);
        var id = Econ.Items.Add(_ids, shape, q, owner, parts, ItemTable.NoGood, (int)Math.Max(1, (j.TotalSeconds + 1799) / 3600), Math.Clamp(j.EffSkill, 0, 100));
        if (Smithing.TemperSkipped(r, j)) Econ.Items.Get(id).Flags |= ItemFlags.Brittle;
        if (State.Fits.Remove(j.Target, out var fit)) State.Fits[id] = fit;
        ev.Add(new CraftEvent(CraftEventKind.Finished, j.Id, j.Crafter, now, shape, (int)q));
        Kept(j, id);
        return true;
    }

    // ---- oil and fire (crafting-full 4.1; items-quality-and-wear 4.4) ----

    private void ApplyOil(CraftJob j, long now)
    {
        if (!Econ.Items.IsLive(j.Target)) return;
        Econ.Items.Get(j.Target).Flags |= ItemFlags.Oiled;
        State.OiledUntil[j.Target] = (int)(now / 86400) + OilDays;
    }

    /// <summary>The oil's last day for an item (the rust rule's "oiled until"), or -1.</summary>
    public int OiledUntilDay(EntityId item) => State.OiledUntil.TryGetValue(item, out var d) ? d : -1;

    /// <summary>A kindled fire becomes a world object (crafting-full 4.1): the recipe's firewood is its fuel, burned at 0.5 kg an hour from now; the tinder burns at once.</summary>
    private void LightFire(CraftJob j, Store store, long now)
    {
        if (!Cat.TryIdOf("firewood", out var fw)) return;
        var fid = _ids.Allocate(IdKind.Other);
        State.Fires.Light(fid, j.Owner, j.PlaceX, j.PlaceY, now);
        long wood = store.Qty(fw);
        if (wood <= 0) return;
        var fuel = Econ.OpenStore(StoreKind.House, fid, 0, 0, false);
        Store.Move(store, fuel, fw, wood);
    }

    /// <summary>Burns the fires to now (the hour pass, and when the screen asks); rain over 5 mm an hour puts them out.</summary>
    public List<Fire> BurnFires(long nowSec, int rainTenthsMmPerHour)
        => Cat.TryIdOf("firewood", out var fw) ? State.Fires.Advance(Econ, fw, nowSec, rainTenthsMmPerHour) : [];

    /// <summary>Removes the fires that are out; their leftover wood goes to <paramref name="give"/>, their emptied stores as <see cref="DropStore"/> says.</summary>
    public void SweepFires(Func<EntityId, Store?> give) => State.Fires.Sweep(Econ, give, DropStore);

    // ---- learning: an apprentice gains at double rate on his master's trade (crafting-full 4.6) ----

    private bool ApprenticeNow(EntityId who, CraftRecipe r)
    {
        foreach (var a in State.Apprenticeships.Values) if (!a.Ended && a.Apprentice == who && a.Trade == r.Skill) return true;
        return false;
    }
}
