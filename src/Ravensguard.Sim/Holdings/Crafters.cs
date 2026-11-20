using Ravensguard.Sim.Clothing;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Crafting;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;

namespace Ravensguard.Sim.Holdings;

/// <summary>
/// The crafting loose ends of holdings 3.8 (HW-10): NPC crafters' tools wear by Phase 5's rule at their stations, the containers near a station count as the business's store, a master takes an idle
/// youth for a season from the labour market, and a refit writes the garment's fit into the clothing book.
/// </summary>
public static class Crafters
{
    /// <summary>The tool quality a station kind is worked with (what wears at it); null for a station nobody swings a tool at.</summary>
    public static string? QualityOf(string stationKind) => stationKind switch
    {
        "forge" or "anvil" or "bloomery" => "hammering",
        "saw_pit" or "wheelwright_bay" or "work_bench" => "sawing",
        "cobbler_bench" or "tailor_bench" => "sewing",
        "mason_yard" => "chiselling",
        "barrel_bench" or "saddle_tree" => "cutting",
        _ => null,
    };

    /// <summary>
    /// A finished batch wears the tool its station is worked with (items 4.3, Phase 5's rule, now for NPC businesses): the business's own tool row of the best condition that wears for the quality takes the
    /// recipe's worker-hours of attended use; a tool that breaks leaves its scrap in the business's store. Returns true when a tool wore.
    /// </summary>
    public static bool WearTool(ToolWear wear, ItemTable items, EntityId business, Store store, string stationKind, long workerHours, Func<string, string?> materialCategory)
    {
        if (QualityOf(stationKind) is not { } quality || workerHours <= 0) return false;
        int best = -1;
        foreach (int row in items.LiveRows())
        {
            ref var r = ref items.At(row);
            if (r.Owner != business || wear.MilliPerHour(r.Shape, quality) <= 0) continue;
            if (best < 0 || r.ConditionPermille > items.At(best).ConditionPermille) best = row;
        }
        if (best < 0) return false;
        wear.Use(items, best, quality, workerHours * 3600, store, materialCategory);
        return true;
    }

    /// <summary>
    /// The season's intake of NPC apprentices (labour market, crafting-full 4.6 used by the world): in each place each master of a craft who has the skill (40) and fewer than two apprentices takes one
    /// idle youth of 12 to 16, the youngest-id first, for the season's term. Masters in id order, youths in id order. Returns the apprenticeships begun.
    /// </summary>
    public static int TakeApprentices(CraftService svc, Population pop, IdAllocator ids, int day, Func<EntityId, bool> isHero)
    {
        var pt = pop.People; var w = svc.Context; var st = svc.Runner.State; int began = 0;
        int minSkill = svc.Runner.Db.Settings.ApprenticeMasterMinSkill;
        for (int s = 0; s < pop.Region.Settlements.Count; s++)
        {
            var masters = new List<(int Row, Skill Trade)>(); var youths = new List<int>();
            foreach (var id in w.PeopleOf(s))
            {
                int i = pt.IndexOf(id); if (i < 0 || !pt.IsAlive(i)) continue;
                int age = pt.AgeYears(i, day);
                if (age is >= 12 and <= 16 && pt.Employer[i] < 0 && pt.Occupation[i] == Occupation.None && Apprenticeships.As(st, id) == null) youths.Add(i);
                var trade = OccupationInfo.Skills(pt.Occupation[i]).A;
                if (age >= 20 && trade != Skill.None && IsCraft(pt.Occupation[i]) && pt.SkillLevelOf(i, trade) >= minSkill && !isHero(id)) masters.Add((i, trade));
            }
            if (youths.Count == 0) continue;
            youths.Sort(); masters.Sort((a, b) => a.Row.CompareTo(b.Row));
            int next = 0;
            foreach (var (row, trade) in masters)
            {
                if (next >= youths.Count) break;
                var (answer, ap) = Apprenticeships.Start(w, pt.IdOf(youths[next]), pt.IdOf(row), trade, day, ids);
                if (answer == ApprenticeAnswer.Ok && ap != null) { next++; began++; }
            }
        }
        return began;
    }

    private static bool IsCraft(Occupation o) => o is Occupation.Smith or Occupation.Carpenter or Occupation.Mason or Occupation.Weaver or Occupation.Tanner or Occupation.Cobbler or Occupation.Baker or Occupation.Brewer or Occupation.Butcher or Occupation.Miller;

    /// <summary>
    /// Ties the crafting runner's refit to the clothing book's fit (HW-10): a refit writes the garment's fit size into the book, and the book reads the fit a refit has made for pieces it does not
    /// itself know. Call once with the world's runner and the live clothing book.
    /// </summary>
    public static void LinkFit(CraftRunner craft, ClothingBook book)
    {
        craft.FitOf = item => book.FitOf(item);
        craft.SetFit = (item, fit) => { if (!book.SetFit(item, fit)) craft.State.Fits[item] = fit; };
    }
}
