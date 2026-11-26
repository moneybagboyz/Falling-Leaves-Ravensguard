namespace Ravensguard.Sim.Soldiers;

/// <summary>The answer to taking one more soldier into a lord's retinue.</summary>
public readonly record struct RetinueCheck(bool Ok, string Holding, int Cap, int Have);

/// <summary>
/// P7-SC-B (soldiers-and-companies 4.5): a lord's retinue is the paid professionals beyond his squad of companions, capped by his holding (<c>soldier_rules.retinue_caps</c>: manor 6, knight's fee 12, baron 40, count 120,
/// ruler 300). Companions stay under the squad cap and are not counted here. A lord whose holding is unknown has no cap.
/// </summary>
public static class Retinues
{
    public static int CapOf(TroopRoles roles, string holding) => roles.Rules.RetinueCaps.TryGetValue(holding, out int c) ? c : int.MaxValue;

    /// <summary>Soldiers of the lord that count against the cap: every row of his except his companions.</summary>
    public static int Count(SoldierWorld sw, long lord)
    {
        var comp = sw.CompanionsOf(lord);
        return sw.Book.Rows.Values.Count(r => r.Lord == lord && !comp.Contains(r.Person));
    }

    /// <summary>Whether the lord may take <paramref name="person"/> on as a soldier of his retinue (a companion always may: the squad cap rules him).</summary>
    public static RetinueCheck CanEnlist(SoldierWorld sw, long lord, ulong person)
    {
        string h = sw.HoldingOf(lord); int cap = CapOf(sw.Roles, h), have = Count(sw, lord);
        if (sw.CompanionsOf(lord).Contains(person)) return new(true, h, cap, have);
        return new(have < cap, h, cap, have);
    }
}
