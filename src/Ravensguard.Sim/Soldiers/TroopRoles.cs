using Ravensguard.Content;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Soldiers;

/// <summary>A household's standing for a levy's gear (4.3): what the household sends with the man it gives.</summary>
public enum LevyWealth : byte { Poor, Middling, WellOff }

/// <summary>
/// P7-SC-A (soldiers-and-companies 3.1, 4.3): the troop roles, their wages and requirements, and the loadout templates, read from content (<c>war/troops.jsonc</c>, <c>war/loadouts.jsonc</c>). Pure lookups, ids walked in
/// ordinal order; no state and no draw.
/// </summary>
public sealed class TroopRoles
{
    public const string LevyByWealth = "levy_by_wealth";

    public ContentDatabase Db { get; }
    public SoldierRulesDef Rules { get; }
    private readonly Dictionary<string, TroopRoleDef> _roles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LoadoutTemplateDef> _templates = new(StringComparer.Ordinal);

    public TroopRoles(ContentDatabase db)
    {
        Db = db;
        Rules = db.All<SoldierRulesDef>().OrderBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault() ?? new SoldierRulesDef { Id = "soldier_rules" };
        foreach (var r in db.All<TroopRoleDef>().OrderBy(r => r.Id, StringComparer.Ordinal)) _roles[r.Id] = r;
        foreach (var t in db.All<LoadoutTemplateDef>().OrderBy(t => t.Id, StringComparer.Ordinal)) _templates[t.Id] = t;
    }

    /// <summary>The roles, kitted first first (priority, then id).</summary>
    public IReadOnlyList<TroopRoleDef> Roles => [.. _roles.Values.OrderBy(r => r.Priority).ThenBy(r => r.Id, StringComparer.Ordinal)];
    public TroopRoleDef? Role(string id) => _roles.GetValueOrDefault(id);
    public LoadoutTemplateDef? Template(string id) => _templates.GetValueOrDefault(id);
    public IReadOnlyCollection<LoadoutTemplateDef> Templates => _templates.Values;

    /// <summary>The wage in copper a day: the role's, or a mercenary's (the rules' multiple, rounded down). Zero for an unknown role.</summary>
    public int WageCp(string role, bool mercenary = false)
    {
        var r = Role(role); if (r == null) return 0;
        return mercenary ? (int)((long)r.WageCp * Rules.MercWageMultPm / 1000) : r.WageCp;
    }

    /// <summary>Whether a person with these skill levels meets the role's requirement (all of <c>requires</c>, one of <c>requires_any</c>); a dubbed role (knight) is also refused unless <paramref name="dubbed"/>.</summary>
    public bool Meets(string role, Func<Skill, int> skill, bool dubbed = false)
    {
        var r = Role(role); if (r == null) return false;
        if (r.Dubbed && !dubbed) return false;
        foreach (var (s, lvl) in r.Requires) if (!Enum.TryParse<Skill>(s, out var k) || skill(k) < lvl) return false;
        if (r.RequiresAny.Count > 0 && !r.RequiresAny.Any(kv => Enum.TryParse<Skill>(kv.Key, out var k) && skill(k) >= kv.Value)) return false;
        return true;
    }

    /// <summary>The household wealth class of a levy by the household's wealth in mites (4.3).</summary>
    public LevyWealth WealthOf(long mites) => mites >= Rules.WellOffMites ? LevyWealth.WellOff : mites >= Rules.MiddlingMites ? LevyWealth.Middling : LevyWealth.Poor;

    public static string LevyTemplateOf(LevyWealth w) => w switch { LevyWealth.WellOff => "levy_well_off", LevyWealth.Middling => "levy_middling", _ => "levy_poor" };

    /// <summary>
    /// The template a soldier is kitted from by the lord's armoury: his own override, else his role's loadout; a levy (the household's <c>levy_by_wealth</c>) is filled by the rules' <c>levy_lord_fill</c> (spears and shields
    /// only, 4.3). Null for an unknown role or template.
    /// </summary>
    public LoadoutTemplateDef? TemplateFor(string role, string overrideId = "")
    {
        if (overrideId.Length > 0) return Template(overrideId);
        var r = Role(role); if (r == null) return null;
        return r.Loadout == LevyByWealth ? Template(Rules.LevyLordFill) : Template(r.Loadout);
    }

    /// <summary>The material rank of an item row's main material (its first component); 0 when unknown.</summary>
    public int MaterialRankOf(ItemTable items, int row)
    {
        ref var r = ref items.At(row);
        return r.ComponentCount > 0 && Rules.MaterialRank.TryGetValue(items.Component(r.ComponentStart).Material, out var k) ? k : 0;
    }
}
