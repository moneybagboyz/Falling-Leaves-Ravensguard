using System.Text.Json;
using MessagePack;
using Ravensguard.Content;
using SkillEnum = Ravensguard.Sim.People.Skill;

namespace Ravensguard.Sim.Characters;

/// <summary>One kind of thing the hero carries: an item shape id (weapon_shape or armour_shape), the material it is made of, how many, and its quality (P5-A1; Common for the origin kit).</summary>
public sealed record HeroItem(string Shape, string Material, int Count = 1, Ravensguard.Sim.Goods.ItemQuality Quality = Ravensguard.Sim.Goods.ItemQuality.Common, bool Brittle = false, ulong ItemId = 0);

/// <summary>The hero's five attributes, in their fixed order (plan section 3 item 2).</summary>
public enum HeroAttr { Strength, Toughness, Dexterity, Perception, Wits }

/// <summary>
/// The player character as a record (plan section 3 items 1 and 2, section 5 Phase 1 Quick Start): name, sex, origin, attributes, skills 0..100, inventory,
/// coin and the ambition with the index of the milestone being worked on. Created from the Quick Start draft's <see cref="OriginDef"/> as deltas over a
/// baseline commoner, saved in the world save's "hero" section. Pure data; no engine types.
/// </summary>
public sealed class HeroRecord
{
    public const int BaselineAttribute = 50;
    public const int MaxLevel = 100;

    public required string Name { get; init; }
    public bool Female { get; init; }
    public required string Origin { get; init; }
    /// <summary>Indexed by <see cref="HeroAttr"/>.</summary>
    public int[] Attributes { get; init; } = new int[5];
    /// <summary>Skill id (snake_case of the sim's Skill enum) to level 0..100, every skill present, in a stable order (Skill enum order).</summary>
    public IReadOnlyDictionary<string, int> Skills { get; init; } = new Dictionary<string, int>();
    public IReadOnlyList<HeroItem> Inventory { get; init; } = [];
    /// <summary>Crowns.</summary>
    public int Coin { get; set; }
    public string AmbitionId { get; set; } = "";
    /// <summary>Index of the ambition step being worked on; equals the step count when the ambition is done.</summary>
    public int Milestone { get; set; }
    /// <summary>P5-A4: the hero's row in the home region's person table (<see cref="Ravensguard.Sim.Succession.HeroPerson"/>); -1 none, -2 a v7 save whose row the load inserts.</summary>
    public int PersonIndex { get; set; } = -1;

    public int Attribute(HeroAttr a) => Attributes[(int)a];
    public int Skill(string id) => Skills.TryGetValue(id, out var v) ? v : 0;

    /// <summary>All skill ids in the order the record keeps them.</summary>
    public static IReadOnlyList<string> SkillIds { get; } = Enum.GetValues<SkillEnum>().Where(s => s != SkillEnum.None).Select(s => SkillId(s)).ToList();
    public static string SkillId(SkillEnum s) => JsonNamingPolicy.SnakeCaseLower.ConvertName(s.ToString());
    public static readonly string[] AttrIds = ["strength", "toughness", "dexterity", "perception", "wits"];

    /// <summary>Builds the hero for an origin: the baseline commoner plus the origin's attribute and skill deltas (clamped to 1..100 and 0..100), its kit made of
    /// the usual materials, its coin and its ambition at the first milestone.</summary>
    public static HeroRecord FromOrigin(string name, bool female, OriginDef origin)
    {
        var attrs = new int[5];
        Array.Fill(attrs, BaselineAttribute);
        if (female) { attrs[(int)HeroAttr.Strength] -= 6; attrs[(int)HeroAttr.Dexterity] += 2; } // the same sex adjustment the population rolls use
        for (int i = 0; i < AttrIds.Length; i++)
            if (origin.Attributes.TryGetValue(AttrIds[i], out var d)) attrs[i] += d;
        for (int i = 0; i < attrs.Length; i++) attrs[i] = Math.Clamp(attrs[i], 1, MaxLevel);

        var skills = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in SkillIds) skills[id] = 0;
        foreach (var (id, d) in origin.Skills.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            if (skills.ContainsKey(id)) skills[id] = Math.Clamp(skills[id] + d, 0, MaxLevel);
        DefaultWeaponFamilies(skills, origin.Skills.Keys);

        var inv = new List<HeroItem>();
        foreach (var k in origin.Kit) inv.Add(new HeroItem(k.Item, DefaultMaterial(k.Item), Math.Max(1, k.Count), (Ravensguard.Sim.Goods.ItemQuality)QualityNames.Tier(k.Quality)));
        return new HeroRecord
        {
            Name = name, Female = female, Origin = origin.Id, Attributes = attrs, Skills = skills, Inventory = inv,
            Coin = Math.Max(0, origin.Coin), AmbitionId = origin.Ambition, Milestone = 0,
        };
    }

    /// <summary>The weapon families (Phase 4) not given by the origin or the save start at 80% of Melee (melee-and-ranged M1; the v6 save migration).</summary>
    public static void DefaultWeaponFamilies(Dictionary<string, int> skills, IEnumerable<string> given)
    {
        var set = new HashSet<string>(given, StringComparer.Ordinal);
        int melee = skills.TryGetValue(SkillId(SkillEnum.Melee), out var m) ? m : 0;
        foreach (var f in Ravensguard.Sim.Combat.Melee2.CombatSkills.Families)
        {
            string id = SkillId(f);
            if (!set.Contains(id)) skills[id] = Math.Clamp(melee * Ravensguard.Sim.Combat.Melee2.CombatSkills.MigratePm / 1000, 0, MaxLevel);
        }
    }

    /// <summary>The material a commoner's item of this shape is usually made of (the head of a weapon; the cloth, hide or metal of an armour piece).</summary>
    public static string DefaultMaterial(string shape) => shape switch
    {
        "arming_sword" or "longsword" or "dagger" or "rapier" or "sabre" => "steel",
        "axe" or "spear" or "mace" or "maul" or "hammer" or "pike" or "halberd" or "poleaxe" or "helm" or "mail_hauberk" or "gauntlets" => "iron",
        "breastplate" => "steel",
        "club" or "staff" or "bow" or "shortbow" or "longbow" or "quarterstaff" => "oak",
        "shirt" or "gambeson" => "linen",
        "tunic" or "cloak" or "hose" or "hood" => "wool",
        "boots" or "gloves" or "jerkin" => "leather",
        _ => "iron",
    };

    /// <summary>The ambition's current step, or null when the hero has none or has finished them all.</summary>
    public AmbitionStepDef? CurrentStep(AmbitionDef? a) => a != null && Milestone >= 0 && Milestone < a.Steps.Count ? a.Steps[Milestone] : null;

    /// <summary>Marks the current milestone done; false when there is none left.</summary>
    public bool CompleteMilestone(AmbitionDef a)
    {
        if (Milestone >= a.Steps.Count) return false;
        Milestone++;
        return true;
    }

    // ------------------------------------------------------------------ P8-AT-1: the ambition history

    /// <summary>Ambitions finished, with the game day (done ambitions stay done).</summary>
    public List<(string Id, int Day)> AmbitionsDone { get; } = [];
    /// <summary>The offer made when the last ambition was done and not yet taken; it stays in the Journal.</summary>
    public List<string> Offered { get; } = [];
    /// <summary>Ambitions dropped for another (their steps are not kept; they may be offered again from step 0).</summary>
    public List<string> Dropped { get; } = [];

    public bool IsDone(string id) => AmbitionsDone.Exists(d => d.Id == id);

    // ------------------------------------------------------------------ P8-EN-1: endings

    /// <summary>The game day the run was ended at (an ending was taken), or null while the hero plays on. An ended hero's person row is an ordinary person run by the NPC AI.</summary>
    public int? EndedAt { get; set; }
    /// <summary>Endings on offer (the ending id, and the day the great ambition's conditions first stopped holding, -1 while they hold). "Carry on" keeps an offer; it ends after <see cref="Ravensguard.Sim.Endings.EndingRules.LapseDays"/> days without the condition.</summary>
    public List<(string Id, int LapseDay)> EndingOffers { get; } = [];

    /// <summary>Records the current ambition as done on <paramref name="day"/> and keeps <paramref name="offer"/> as the open offer.</summary>
    public void FinishAmbition(int day, IEnumerable<string> offer)
    {
        if (AmbitionId.Length > 0 && !IsDone(AmbitionId)) AmbitionsDone.Add((AmbitionId, day));
        Offered.Clear(); Offered.AddRange(offer);
    }

    /// <summary>Takes an ambition (an offered one, or a switch): a current unfinished ambition is dropped, the steps start from 0 and the offer is cleared.</summary>
    public void ChooseAmbition(string id)
    {
        if (AmbitionId.Length > 0 && AmbitionId != id && !IsDone(AmbitionId) && !Dropped.Contains(AmbitionId)) Dropped.Add(AmbitionId);
        Dropped.Remove(id);
        AmbitionId = id; Milestone = 0; Offered.Clear();
    }

    // ------------------------------------------------------------------ saving

    /// <summary>
    /// The skills a save writes: every skill, except that a Phase 7 skill (<c>Seamanship</c>, <c>Engineering</c>, the last of the enum) the hero has never trained is left out, so a hero that never used them saves the bytes
    /// it saved before they were appended; a load leaves an absent skill at 0.
    /// </summary>
    private IEnumerable<string> SavedSkillIds() => SkillIds.Where(id => Skill(id) != 0 || !PhaseSevenSkillIds.Contains(id));
    private static readonly HashSet<string> PhaseSevenSkillIds = [SkillId(SkillEnum.Seamanship), SkillId(SkillEnum.Engineering)];

    public HeroSave ToSave() => new()
    {
        Name = Name, Female = Female, Origin = Origin, Attributes = (int[])Attributes.Clone(),
        SkillIds = SavedSkillIds().ToArray(), SkillLevels = SavedSkillIds().Select(Skill).ToArray(),
        ItemShapes = Inventory.Select(i => i.Shape).ToArray(), ItemMaterials = Inventory.Select(i => i.Material).ToArray(), ItemCounts = Inventory.Select(i => i.Count).ToArray(),
        Coin = Coin, AmbitionId = AmbitionId, Milestone = Milestone, PersonIndex = PersonIndex,
        DoneIds = AmbitionsDone.Count == 0 ? null : AmbitionsDone.Select(d => d.Id).ToArray(), DoneDays = AmbitionsDone.Count == 0 ? null : AmbitionsDone.Select(d => d.Day).ToArray(),
        OfferedIds = Offered.Count == 0 ? null : [.. Offered], DroppedIds = Dropped.Count == 0 ? null : [.. Dropped],
        EndedAt = EndedAt, EndingOffers = EndingOffers.Count == 0 ? null : [.. EndingOffers.Select(o => o.Id + ":" + o.LapseDay.ToString(System.Globalization.CultureInfo.InvariantCulture))],
    };

    public static HeroRecord FromSave(HeroSave s)
    {
        if (s.Attributes.Length != 5 || s.SkillIds.Length != s.SkillLevels.Length || s.ItemShapes.Length != s.ItemMaterials.Length || s.ItemShapes.Length != s.ItemCounts.Length)
            throw new Save.SaveException("Corrupt 'hero' section in save.");
        var skills = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in SkillIds) skills[id] = 0;
        for (int i = 0; i < s.SkillIds.Length; i++) if (skills.ContainsKey(s.SkillIds[i])) skills[s.SkillIds[i]] = Math.Clamp(s.SkillLevels[i], 0, MaxLevel);
        var inv = new List<HeroItem>();
        for (int i = 0; i < s.ItemShapes.Length; i++) inv.Add(new HeroItem(s.ItemShapes[i], s.ItemMaterials[i], s.ItemCounts[i]));
        var h = new HeroRecord
        {
            Name = s.Name, Female = s.Female, Origin = s.Origin, Attributes = (int[])s.Attributes.Clone(), Skills = skills, Inventory = inv,
            Coin = s.Coin, AmbitionId = s.AmbitionId, Milestone = s.Milestone, PersonIndex = s.PersonIndex,
        };
        if (s.DoneIds != null)
        {
            if (s.DoneDays == null || s.DoneDays.Length != s.DoneIds.Length) throw new Save.SaveException("Corrupt 'hero' ambition history in save.");
            for (int i = 0; i < s.DoneIds.Length; i++) h.AmbitionsDone.Add((s.DoneIds[i], s.DoneDays[i]));
        }
        if (s.OfferedIds != null) h.Offered.AddRange(s.OfferedIds);
        if (s.DroppedIds != null) h.Dropped.AddRange(s.DroppedIds);
        h.EndedAt = s.EndedAt;
        if (s.EndingOffers != null)
            foreach (var o in s.EndingOffers) { int c = o.LastIndexOf(':'); if (c <= 0 || !int.TryParse(o.AsSpan(c + 1), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var d)) throw new Save.SaveException("Corrupt 'hero' ending offer in save."); h.EndingOffers.Add((o[..c], d)); }
        return h;
    }
}

/// <summary>Save form of <see cref="HeroRecord"/> (explicit key order, parallel arrays in the record's stable order).</summary>
[MessagePackObject]
public sealed class HeroSave
{
    [Key(0)] public string Name { get; set; } = "";
    [Key(1)] public bool Female { get; set; }
    [Key(2)] public string Origin { get; set; } = "";
    [Key(3)] public int[] Attributes { get; set; } = [];
    [Key(4)] public string[] SkillIds { get; set; } = [];
    [Key(5)] public int[] SkillLevels { get; set; } = [];
    [Key(6)] public string[] ItemShapes { get; set; } = [];
    [Key(7)] public string[] ItemMaterials { get; set; } = [];
    [Key(8)] public int[] ItemCounts { get; set; } = [];
    [Key(9)] public int Coin { get; set; }
    [Key(10)] public string AmbitionId { get; set; } = "";
    [Key(11)] public int Milestone { get; set; }
    /// <summary>P5-A4 (save v8): the hero's person row; -1 none; a v7 save migrates to -2 (inserted at load).</summary>
    [Key(12)] public int PersonIndex { get; set; } = -1;
    /// <summary>P8-AT-1 (optional keys, null while empty): the done ambitions' ids and days, the open offer and the dropped ambitions.</summary>
    [Key(13)] public string[]? DoneIds { get; set; }
    [Key(14)] public int[]? DoneDays { get; set; }
    [Key(15)] public string[]? OfferedIds { get; set; }
    [Key(16)] public string[]? DroppedIds { get; set; }
    /// <summary>P8-EN-1 (additive keys): the day an ending was taken (absent while the run goes on) and the open endings as "id:lapseDay".</summary>
    [Key(17)] public int? EndedAt { get; set; }
    [Key(18)] public string[]? EndingOffers { get; set; }
}

/// <summary>Save form of the Codex progress.</summary>
[MessagePackObject]
public sealed class CodexSave
{
    [Key(0)] public string[] Unlocked { get; set; } = [];
}
