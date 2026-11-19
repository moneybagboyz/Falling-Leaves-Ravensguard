using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;

namespace Ravensguard.Sim.Combat.Aftermath;

/// <summary>
/// The abstract tier's hourly captivity step (morale-capture-succession 5): the shared <see cref="CaptiveBook"/> (set in the world services next to the wound ledger) runs its hour after
/// everything else (H7): care, ransoms falling due and paid from the payer's purse, renegotiation, transfers and their night windows, abstract rescues and the hold limits. An army's
/// returned stash goes back to the captive's ownership here. Found by <see cref="AbstractSystemAttribute"/>.
/// </summary>
[AbstractSystem]
public sealed class CaptivitySystem : AbstractSystem
{
    public const int OrderSlot = 7_050;
    public CaptivitySystem() : base("captivity", TickPhase.H7Outbox, OrderSlot) { }

    public override void Step(TickContext ctx)
    {
        var b = ctx.World.Get<CaptiveBook>();
        if (b == null || b.Captives.Count == 0) return;
        var e = ctx.World.Economy;
        var wounds = ctx.World.Get<WoundLedger>();
        // the medic's care writes the captives' books, which the live tier steps on the main thread under the ledger's lock (review finding 1): the same lock here
        if (wounds != null) lock (wounds.Sync) b.StepHour(b.Rules, e.Ledger, ctx.Hour, k => wounds.Find(k), (c, items) => ReturnStash(b, e, c, items));
        else b.StepHour(b.Rules, e.Ledger, ctx.Hour, _ => null, (c, items) => ReturnStash(b, e, c, items));
    }

    /// <summary>An army hands the stash back on release or ransom, less its 10% keep of the non-weapon valuables (4.6).</summary>
    public static void ReturnStash(CaptiveBook b, EconomyState e, Captive c, IReadOnlyList<EntityId> items)
    {
        var rows = new List<(EntityId Id, long Value, bool Weapon)>();
        foreach (var id in items)
        {
            int row = e.Items.IndexOf(id);
            if (row < 0 || e.Items.At(row).Owner != c.Stash) continue;
            rows.Add((id, b.ValueOf?.Invoke(row) ?? e.Items.MassG(row, e.Goods), b.IsWeapon?.Invoke(e.Items.At(row).Shape) ?? false));
        }
        var back = CaptiveBook.Returned(b.Rules, rows);
        Robbery.Return(e, c.Stash, c.Person, back);
        c.StashItems.RemoveAll(back.Contains);
    }
}
