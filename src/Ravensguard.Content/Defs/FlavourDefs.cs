namespace Ravensguard.Content;

/// <summary>
/// When a flavour template applies (feedback-flavour-log 3.2). Every list that is present must contain the event's value; an absent list means any. <see cref="Flags"/> matches when
/// the blow has at least one of the named wound flags. All names are lower case words (the enum names of the resolver, snake case).
/// </summary>
public sealed record FlavourWhen
{
    /// <summary>edge, point, blunt, missile, thrown, fall, bite, claw.</summary>
    public List<string> Attack { get; init; } = [];
    /// <summary>The weapon shape's family: blade, dagger, axe, hafted, polearm, ranged, unarmed, natural, practice, improvised, ammo.</summary>
    public List<string> Family { get; init; } = [];
    /// <summary>The weapon head's material id or its category (steel, iron, bronze, metal, wood, bone, stone).</summary>
    public List<string> Material { get; init; } = [];
    /// <summary>The outermost armour layer's outcome: cut, crushed, stopped, deflected, not_covered, bypassed, absorbed, none.</summary>
    public List<string> ArmourOutcome { get; init; } = [];
    /// <summary>The armour piece struck: cloth, padded, leather, mail, plate, scale, helm, shield.</summary>
    public List<string> ArmourFamily { get; init; } = [];
    /// <summary>The deepest tissue reached: skin, fat, muscle, bone, organ, artery, vein, tendon, nerve, ligament, spine, none.</summary>
    public List<string> Tissue { get; init; } = [];
    /// <summary>The grade of the deepest wound: sound, bruised, torn, mangled (muscle) or cracked, broken, displaced, shattered (bone).</summary>
    public List<string> Grade { get; init; } = [];
    public List<int> Severity { get; init; } = [];
    /// <summary>The struck part's base id (upper_arm, eye, jaw, skull, ribs, ...): for the pieces written for one place.</summary>
    public List<string> Part { get; init; } = [];
    /// <summary>head, face, neck, torso, arm, hand, leg, foot, body.</summary>
    public List<string> PartGroup { get; init; } = [];
    /// <summary>front, flank, rear.</summary>
    public List<string> Facing { get; init; } = [];
    /// <summary>Wound flag names (severed, artery_hit, vein_hit, nerve_hit, tendon_hit, bone_broken, organ_hit, vital, ligament_hit, motor_nerve_hit, sensory_nerve_hit, spine_hit, inset_hit, displaced, compound, burn, concussion, rib_slip).</summary>
    public List<string> Flags { get; init; } = [];
    /// <summary>human, horse, draugr, beast.</summary>
    public List<string> Target { get; init; } = [];
    /// <summary>normal, quick, heavy.</summary>
    public List<string> Style { get; init; } = [];
    /// <summary>The event kind (the fight's <c>FightEventKind</c> names, snake case) for the event slots.</summary>
    public List<string> Event { get; init; } = [];
    /// <summary>For falling and dying: blood, pain, heart, concussion, breath, organ, brain, infection.</summary>
    public List<string> Cause { get; init; } = [];
    /// <summary>A called shot (true) or an unaimed one (false).</summary>
    public bool? Called { get; init; }
    /// <summary>True: only after an armour clause (the piece reads "and ..."); false: only when there is none.</summary>
    public bool? Chained { get; init; }
}

/// <summary>
/// One piece of flavour prose (feedback-flavour-log 3.2). <see cref="Slot"/> names where it goes in a line: open, armour, tissue, blow (a whole first sentence), consequence, after,
/// defence, shot, morale, report. <see cref="Text"/> is a <c>tr()</c> key (lower case, prefix fl_); the English lives in the game's en.csv. <see cref="Gore"/> is "high" for a piece the
/// reduced-blood setting leaves out.
/// </summary>
[ContentType("flavour")]
public sealed record FlavourDef : ContentDef
{
    public required string Slot { get; init; }
    public FlavourWhen When { get; init; } = new();
    public int Weight { get; init; } = 10;
    /// <summary>The key; when left out it is <c>fl_</c> and the id with dots as underscores (the generated rows leave it out).</summary>
    public string Text { get; init; } = "";
    [System.Text.Json.Serialization.JsonIgnore] public string TextKey => Text.Length > 0 ? Text : "fl_" + Id.Replace('.', '_');
    /// <summary>any or high.</summary>
    public string Gore { get; init; } = "any";
}

/// <summary>
/// A placeholder sound hook (feedback-flavour-log 4.6): which fight events and severities play which named sound, the caption shown when captions are on, and the file (none ship in
/// Phase 4; real audio is Phase 8). A row with <see cref="Silent"/> says an event is deliberately silent. Of several matching rows the highest <see cref="Priority"/> wins.
/// </summary>
[ContentType("audio_event")]
public sealed record AudioEventDef : ContentDef
{
    /// <summary>The fight event kinds (snake case) this row answers.</summary>
    public List<string> Events { get; init; } = [];
    public int MinSeverity { get; init; }
    /// <summary>The armour family struck (none, cloth, padded, leather, mail, plate, scale, helm, shield); empty: any.</summary>
    public List<string> Armour { get; init; } = [];
    public List<string> Attack { get; init; } = [];
    /// <summary>Wound flag names (snake case) the blow must carry (any of).</summary>
    public List<string> Flags { get; init; } = [];
    public int Priority { get; init; } = 10;
    /// <summary>The <c>tr()</c> key of the caption ("[sword clash]"); empty: no caption.</summary>
    public string Caption { get; init; } = "";
    /// <summary>A file under <c>assets/audio/placeholder</c>; empty while there is none.</summary>
    public string File { get; init; } = "";
    public bool Silent { get; init; }
}
