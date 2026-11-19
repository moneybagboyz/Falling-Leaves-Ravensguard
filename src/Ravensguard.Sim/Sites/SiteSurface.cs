using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Sites;

/// <summary>
/// The surface sites (exploration-and-sites 4.2): an old battlefield's scatter, searched a 10 m x 10 m cell at a time (60 sim-seconds, the yield by Perception, stream 0x4B9 keyed on
/// the search's serial and the person), from a finite budget that history left (<c>1000 - 8 x years</c> permille, floor 100) and that every find spends; the rare named weapon of
/// a legend once. A hoard is a dig marker: 120 sim-seconds with a shovel give its artefact, once. What a site still holds is the record's <see cref="SiteRecord.LootPm"/> share.
/// </summary>
public static class SiteSurface
{
    /// <summary>The scatter's value left on a field (mites): the template's budget x the remaining permille of the years x what searches and scavengers left.</summary>
    public static int Budget(SiteContent c, SiteHistoryInfo h, int lootPm)
    {
        var t = c.TemplateFor(h.Kind.ToString(), h.Sub);
        if (t == null) return 0;
        long b = (long)SiteHistoryInfo.LootBudget(t.Loot, h) * SiteHistoryInfo.BattlefieldRemainingPm(h.YearsSince, c.K.BattlefieldDecayPmPerYear) / 1000;
        return (int)(b * lootPm / 1000);
    }

    /// <summary>
    /// One search of a cell (60 sim-seconds): a find at <c>20 + Perception/2</c> percent, an item of the scatter table no dearer than what is left, the field's share spent by it
    /// (<paramref name="lootPm"/> goes down). The legend's named weapon turns up once, at 3% of finds, and only in the second half of the field (it lies deep, under the scatter). Returns null when nothing was found.
    /// </summary>
    public static LootItem? Search(SiteContent c, SiteHistoryInfo h, ref int lootPm, ref bool namedFound, int perception, ulong person, int serial, ulong seed)
    {
        int left = Budget(c, h, lootPm);
        if (left <= 0) return null;
        var r = new RngStream(seed, serial, person, RngStreams.Scavenge);
        if (r.Range(0, 100) >= 20 + Math.Clamp(perception, 0, 100) / 2) return null;
        var t = c.TemplateFor(h.Kind.ToString(), h.Sub)!;
        var entries = t.Loot.Tables.Where(c.Loot.ContainsKey).SelectMany(id => c.Loot[id].Entries).Where(e => e.ValueMites <= left).ToList();
        if (entries.Count == 0) return null;
        LootItem item;
        if (!namedFound && h.ArtefactName.Length > 0 && lootPm <= 500 && r.Range(0, 100) < 3)
        {
            namedFound = true;
            item = new LootItem("arming_sword", "steel", Math.Min(left, 1200), 1100, 700, h.ArtefactName);
        }
        else
        {
            int w = entries.Sum(e => e.Weight), roll = r.Range(0, Math.Max(1, w)), acc = 0;
            var pick = entries[^1];
            foreach (var e in entries) { acc += e.Weight; if (roll < acc) { pick = e; break; } }
            int cond = pick.Condition.Count >= 2 ? pick.Condition[0] + r.Range(0, Math.Max(1, pick.Condition[1] - pick.Condition[0] + 1)) : 1000;
            item = new LootItem(pick.Item, pick.Material, pick.ValueMites, pick.MassG, cond, "", pick.Dirty);
        }
        int total = Budget(c, h, 1000);
        if (total > 0) lootPm = Math.Max(0, lootPm - Math.Max(1, (int)((long)item.ValueMites * 1000 / total)));   // even a skull spends a little of the field
        return item;
    }

    /// <summary>A hoard dug up (120 sim-seconds with a shovel): the artefact of the history, once; null when it was already taken or there is none.</summary>
    public static LootItem? DigHoard(SiteHistoryInfo h, ref int lootPm)
    {
        if (lootPm <= 0 || h.ArtefactName.Length == 0) return null;
        lootPm = 0;
        return new LootItem("gold_torc", "", 2000, 300, 1000, h.ArtefactName, false, true);
    }
}
