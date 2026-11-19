using Ravensguard.Content;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Dialogue;

namespace Ravensguard.Sim.Combat.Flavour;

/// <summary>The words of one line: the match context, the up to two wounds that carry a consequence, and the names the templates use. Built from the log alone (no content tables).</summary>
public sealed class LineDesc
{
    public FlavourCtx Ctx;
    public string Slot = "";
    public bool Blow;
    /// <summary>Consequence contexts (one per chosen wound flag), at most two.</summary>
    public readonly List<(FlavourCtx Ctx, WoundSnap Wound, WoundFlags Flag)> Cons = [];
    public readonly Dictionary<string, string> P = new(StringComparer.Ordinal);
    public bool Armoured;
}

/// <summary>Turns a log line into the match context and the parameters of its templates (feedback-flavour-log 3.2, 4.1).</summary>
public static class FlavourDescribe
{
    static readonly WoundFlags[] Priority =
    [
        WoundFlags.Severed, WoundFlags.Vital, WoundFlags.OrganHit, WoundFlags.SpineHit, WoundFlags.ArteryHit, WoundFlags.Compound, WoundFlags.Displaced, WoundFlags.BoneBroken,
        WoundFlags.MotorNerveHit, WoundFlags.SensoryNerveHit, WoundFlags.NerveHit, WoundFlags.TendonHit, WoundFlags.LigamentHit, WoundFlags.InsetHit, WoundFlags.VeinHit,
        WoundFlags.Concussion, WoundFlags.RibSlip, WoundFlags.Burn,
    ];

    static readonly HashSet<string> Metals = new(StringComparer.Ordinal) { "steel", "iron", "bronze", "copper", "brass", "silver", "gold", "tin", "lead" };
    static readonly HashSet<string> BoneLike = new(StringComparer.Ordinal) { "bone", "antler", "horn", "tooth", "ivory" };
    static readonly HashSet<string> StoneLike = new(StringComparer.Ordinal) { "flint", "stone", "granite", "obsidian", "marble", "limestone", "sandstone", "slate", "basalt", "chert" };

    public static string CategoryOf(string material) => Metals.Contains(material) ? "metal" : BoneLike.Contains(material) ? "bone" : StoneLike.Contains(material) ? "stone" : "wood";

    /// <summary>Armour family of a worn piece (templates: cloth, padded, leather, mail, plate, scale, helm).</summary>
    public static string ArmourFamilyOf(string shape, string material) => shape switch
    {
        "mail_hauberk" or "mail_coif" or "mail_chausses" => "mail",
        "gambeson" or "jack" or "padded_coif" or "doublet" => "padded",
        "breastplate" or "backplate" or "vambraces" or "pauldrons" or "cuisses" or "greaves" or "gauntlets" => "plate",
        "helm" or "great_helm" or "kettle_hat" or "nasal_helm" or "spangenhelm" or "cap" => material is "leather" or "linen" or "wool" ? "leather" : "helm",
        "brigandine" or "lamellar" or "scale_hauberk" => "scale",
        "cuir_bouilli" or "leather_bracers" => "leather",
        _ => material switch { "leather" or "hide" => "leather", "iron" or "steel" or "bronze" => "plate", _ => "cloth" },
    };

    public static string PartGroupOf(string baseId) => baseId switch
    {
        "head" or "skull" or "brain" => "head",
        "eye" or "ear" or "nose" or "jaw" or "teeth" => "face",
        "neck" => "neck",
        "upper_body" or "lower_body" or "ribs" or "pelvis" or "collarbone" or "torso" or "lungs" or "heart" => "torso",
        "upper_arm" or "lower_arm" => "arm",
        "hand" or "thumb" or "fingers" => "hand",
        _ when baseId.StartsWith("fore_leg", StringComparison.Ordinal) || baseId.StartsWith("hind_leg", StringComparison.Ordinal) => "leg",
        "upper_leg" or "lower_leg" or "kneecap" or "fore_leg" or "hind_leg" or "leg" => "leg",
        "foot" => "foot",
        _ => "body",
    };

    static int ArmourRank(LayerOutcome o) => o switch
    {
        LayerOutcome.Stopped => 6, LayerOutcome.Deflected => 6, LayerOutcome.Bypassed => 5, LayerOutcome.Crushed => 4, LayerOutcome.Cut => 3, LayerOutcome.Absorbed => 2, LayerOutcome.NotCovered => 1, _ => 0,
    };

    static string OutcomeName(LayerOutcome o) => o switch
    {
        LayerOutcome.Cut => "cut", LayerOutcome.Crushed => "crushed", LayerOutcome.Stopped => "stopped", LayerOutcome.Deflected => "deflected", LayerOutcome.NotCovered => "not_covered",
        LayerOutcome.Bypassed => "bypassed", LayerOutcome.Absorbed => "absorbed", LayerOutcome.Blocked => "blocked", LayerOutcome.Parried => "parried", LayerOutcome.Dodged => "dodged", _ => "none",
    };

    /// <summary>The wound's rank for "the deepest": what the line is about.</summary>
    static int Depth(FightLog log, in WoundSnap w)
    {
        if ((w.Flags & WoundFlags.Vital) != 0) return 105;
        if ((w.Flags & WoundFlags.Severed) != 0) return 110;
        if ((w.Flags & WoundFlags.SpineHit) != 0) return 100;
        if ((w.Flags & WoundFlags.OrganHit) != 0) return 95;
        if ((w.Flags & WoundFlags.ArteryHit) != 0) return 90;
        string t = log.Str(w.Tissue);
        if (t.StartsWith("bone", StringComparison.Ordinal)) return 70 + w.Grade;
        if ((w.Flags & (WoundFlags.NerveHit | WoundFlags.MotorNerveHit | WoundFlags.SensoryNerveHit)) != 0) return 60;
        if ((w.Flags & (WoundFlags.TendonHit | WoundFlags.LigamentHit)) != 0) return 55;
        if ((w.Flags & WoundFlags.VeinHit) != 0) return 52;
        if (t == "muscle") return 40 + w.Grade;
        if (t == "fat") return 20;
        return 10;
    }

    public static string TissueOf(FightLog log, in WoundSnap w)
    {
        var f = w.Flags;
        if (w.Organ != 0 || (f & (WoundFlags.OrganHit | WoundFlags.Vital)) != 0) return "organ";
        if ((f & WoundFlags.SpineHit) != 0) return "spine";
        if ((f & WoundFlags.ArteryHit) != 0) return "artery";
        if ((f & (WoundFlags.NerveHit | WoundFlags.MotorNerveHit | WoundFlags.SensoryNerveHit)) != 0) return "nerve";
        if ((f & WoundFlags.TendonHit) != 0) return "tendon";
        if ((f & WoundFlags.LigamentHit) != 0) return "ligament";
        if ((f & WoundFlags.VeinHit) != 0) return "vein";
        string t = log.Str(w.Tissue);
        if (t.StartsWith("bone", StringComparison.Ordinal)) return "bone";
        return t switch { "muscle" => "muscle", "fat" => "fat", "" => "none", _ => "skin" };
    }

    public static string GradeOf(FightLog log, in WoundSnap w)
    {
        string t = TissueOf(log, w);
        return t switch
        {
            "muscle" => w.Grade switch { 1 => "bruised", 2 => "torn", 3 => "mangled", _ => "sound" },
            "bone" => w.Grade switch { 1 => "cracked", 2 => "broken", 3 => "displaced", 4 => "shattered", _ => "bruised" },
            _ => "",
        };
    }

    // ------------------------------------------------------------------ vocabulary

    public static string Pretty(string id) => id.Replace('.', ' ').Replace('_', ' ');

    public static string V(ITextSource text, string kind, string id)
    {
        if (id.Length == 0) return "";
        string key = "fl_v_" + kind + "_" + id.Replace('.', '_');
        return text.Has(key) ? text.T(key) : Pretty(id);
    }

    public static string PartName(ITextSource text, string partId, out string side, out string baseName)
    {
        side = ""; string b = partId;
        if (partId.EndsWith("_l", StringComparison.Ordinal)) { side = text.Has("fl_v_side_left") ? text.T("fl_v_side_left") : "left"; b = partId[..^2]; }
        else if (partId.EndsWith("_r", StringComparison.Ordinal)) { side = text.Has("fl_v_side_right") ? text.T("fl_v_side_right") : "right"; b = partId[..^2]; }
        baseName = V(text, "part", b);
        return side.Length > 0 ? side + " " + baseName : baseName;
    }

    public static string NameOf(FighterInfo? f) => f == null ? "" : f.Kind == "human" ? f.Name : "the " + f.Name.ToLowerInvariant();

    static void Pronouns(Dictionary<string, string> p, string prefix, FighterInfo? f)
    {
        bool it = f != null && f.Kind != "human";
        bool she = f != null && f.Female && !it;
        string he = it ? "it" : she ? "she" : "he", his = it ? "its" : she ? "her" : "his", him = it ? "it" : she ? "her" : "him";
        p[prefix + "he"] = he; p[prefix + "his"] = his; p[prefix + "him"] = him;
        p[prefix + "He"] = char.ToUpperInvariant(he[0]) + he[1..]; p[prefix + "His"] = char.ToUpperInvariant(his[0]) + his[1..];
    }

    // ------------------------------------------------------------------ describe

    /// <summary>The slot a kind of event is told in.</summary>
    public static string SlotOf(FightEventKind k) => k switch
    {
        FightEventKind.Hit or FightEventKind.Stopped => "blow",
        FightEventKind.Blocked or FightEventKind.Parried or FightEventKind.Dodged or FightEventKind.Whiff or FightEventKind.Missed or FightEventKind.Overpowered or FightEventKind.Feint
            or FightEventKind.ProjectileMiss or FightEventKind.OutOfAmmo => "defence",
        FightEventKind.Shot or FightEventKind.Throw => "shot",
        FightEventKind.Down or FightEventKind.Death or FightEventKind.Wake or FightEventKind.Broke or FightEventKind.Disarmed or FightEventKind.Interrupted => "after",
        FightEventKind.Rout or FightEventKind.Rally or FightEventKind.Surrender or FightEventKind.Fled => "morale",
        FightEventKind.Charge or FightEventKind.Unhorsed or FightEventKind.Fell or FightEventKind.HorseFell or FightEventKind.Shattered or FightEventKind.BraceStrike or FightEventKind.Trampled => "after",
        FightEventKind.Reared or FightEventKind.Bolted or FightEventKind.Balked => "morale",
        _ => "",
    };

    public static string EventName(FightEventKind k) => FlavourBook.Snake(k.ToString());

    /// <summary>The cause word of an armour-damage line (close-out item 4: <see cref="Resolver2.ArmourHarm"/>).</summary>
    public static string HarmCause(int harm) => harm switch { 1 => "dented", 2 => "buckled", 3 => "burst", 4 => "strap", _ => "dented" };

    static string DownCause(int bits) => (bits & 4) != 0 ? "heart" : (bits & 16) != 0 ? "breath" : (bits & 8) != 0 ? "concussion" : (bits & 1) != 0 ? "blood" : (bits & 2) != 0 ? "pain" : "blood";
    static string DeathCause(int kind) => kind switch { 1 => "blood", 2 => "organ", 3 => "brain", 4 => "breath", 5 => "infection", 6 => "infection", _ => "blood" };

    /// <summary>Everything a line's text needs. <paramref name="text"/> supplies the vocabulary (a missing word falls back to the id).</summary>
    public static LineDesc Describe(FightLog log, in FightLine line, ITextSource text)
    {
        var d = new LineDesc { Slot = SlotOf(line.Kind) };
        var c = FlavourCtx.Blank();
        c.Event = EventName(line.Kind); c.Severity = Math.Max(0, (int)line.Severity);
        var flags = line.Flags;
        c.Style = (flags & LineFlags.Overpowered) != 0 ? "overpowered" : (flags & LineFlags.Heavy) != 0 ? "heavy" : (flags & LineFlags.Quick) != 0 ? "quick" : "normal";
        c.Facing = (flags & LineFlags.Rear) != 0 ? "rear" : (flags & LineFlags.Flank) != 0 ? "flank" : "front";
        c.Called = (flags & LineFlags.Called) != 0;
        c.Attack = line.AKind < AttackKinds.Names.Length ? AttackKinds.Names[line.AKind] : "blunt";
        c.Family = log.Str(line.Family);
        c.Material = log.Str(line.WeaponMaterial); c.MaterialCat = CategoryOf(c.Material);
        log.TryInfo(line.Actor, out var actor); log.TryInfo(line.Target, out var other);

        // roles: for a defence the event's actor is the defender; for a wound it is the attacker
        FighterInfo? attacker, victim;
        switch (line.Kind)
        {
            case FightEventKind.Blocked: case FightEventKind.Parried: case FightEventKind.Dodged: case FightEventKind.Overpowered: case FightEventKind.Riposte:
                attacker = other; victim = actor; break;
            case FightEventKind.Down: case FightEventKind.Death: case FightEventKind.Wake: case FightEventKind.Broke: case FightEventKind.Disarmed: case FightEventKind.Interrupted:
            case FightEventKind.Rout: case FightEventKind.Rally: case FightEventKind.Fled: case FightEventKind.Surrender:
            case FightEventKind.Unhorsed: case FightEventKind.Fell: case FightEventKind.HorseFell: case FightEventKind.Reared: case FightEventKind.Bolted: case FightEventKind.Balked:
            case FightEventKind.Shattered:          // mounted-combat: the one it happens to is the event's actor
                attacker = other; victim = actor; break;
            default: attacker = actor; victim = other; break;
        }
        c.Target = victim?.Kind ?? "human";
        d.P["attacker"] = NameOf(attacker); d.P["target"] = NameOf(victim);
        d.P["other"] = NameOf(other);
        Pronouns(d.P, "", attacker); Pronouns(d.P, "tgt_", victim);
        string weapon = V(text, "weapon", log.Str(line.WeaponShape)), mat = V(text, "mat", c.Material);
        d.P["weapon"] = weapon; d.P["weapon_full"] = mat.Length > 0 && c.Family is not ("natural" or "unarmed") ? mat + " " + weapon : weapon;
        d.P["weapon_a"] = (weapon.Length > 0 && "aeiou".IndexOf(char.ToLowerInvariant(weapon[0])) >= 0 ? "an " : "a ") + weapon;
        d.P["verb"] = log.Str(line.Verb).Length > 0 ? log.Str(line.Verb) : "strikes";
        d.P["part"] = ""; d.P["part_base"] = ""; d.P["side"] = ""; d.P["tissue"] = ""; d.P["structure"] = ""; d.P["organ"] = ""; d.P["bone"] = ""; d.P["grade"] = "";
        d.P["armour_piece"] = ""; d.P["armour_material"] = ""; d.P["armour"] = "";
        d.P["count"] = ""; d.P["n"] = ""; d.P["place"] = log.Place; d.P["trigger"] = ""; d.P["cause"] = "";
        string partId = line.Part >= 0 ? "" : "";

        if (line.Kind is FightEventKind.Hit or FightEventKind.Stopped or FightEventKind.Blocked or FightEventKind.Missed)
        {
            d.Blow = true;
            var b = log.BlowOf(line);
            if (b != null) DescribeBlow(log, b, line, text, d, ref c);
            if (line.Kind == FightEventKind.Blocked)
            {
                c.ArmourFamily = (flags & LineFlags.Shield) != 0 ? "shield" : "guard";
                if ((flags & LineFlags.Shield) != 0) { d.P["armour_piece"] = V(text, "armour", "shield"); }
            }
        }
        else if (line.Kind == FightEventKind.Down) { c.Cause = DownCause(line.A); d.P["cause"] = V(text, "cause", c.Cause); }
        else if (line.Kind == FightEventKind.Death) { c.Cause = DeathCause(line.A); d.P["cause"] = V(text, "cause", c.Cause); }
        if (line.Kind is FightEventKind.Down or FightEventKind.Death && victim != null)
        {
            // who and what did it: the wound that bled most, else the last blow
            int ser = victim.WorstHitSerial >= 0 ? victim.WorstHitSerial : victim.LastHitSerial;
            int ix = ser >= 0 ? log.IndexOfSerial(ser) : -1;
            if (ix >= 0)
            {
                var hl = log.Lines[ix];
                log.TryInfo(hl.Actor, out var hitter);
                d.P["attacker"] = NameOf(hitter); d.P["other"] = NameOf(hitter);
                Pronouns(d.P, "", hitter);
                string hw = V(text, "weapon", log.Str(hl.WeaponShape)); d.P["weapon"] = hw; d.P["weapon_full"] = V(text, "mat", log.Str(hl.WeaponMaterial)) + " " + hw;
                var hb = log.BlowOf(hl);
                if (hb != null && hb.Wounds.Length > 0)
                {
                    var w = hb.Wounds[0]; int best = -1;
                    foreach (var x in hb.Wounds) { int dp = Depth(log, x); if (dp > best) { best = dp; w = x; } }
                    d.P["part"] = PartName(text, log.Str(w.Part), out var sd, out var bn); d.P["side"] = sd; d.P["part_base"] = bn;
                    d.P["structure"] = V(text, "struct", log.Str(w.Structure)); d.P["organ"] = V(text, "organ", log.Str(w.Organ));
                    c.PartGroup = PartGroupOf(Severity.BaseOf(log.Str(w.Part)));
                }
            }
        }
        if (line.Kind is FightEventKind.Broke) { c.Cause = line.A == 1 ? "shield" : line.A == 3 ? HarmCause(line.B) : "improvised"; }
        if (line.Kind is FightEventKind.Broke && line.A == 3)
        {
            string shape = log.Str(line.WeaponShape), am = log.Str(line.WeaponMaterial);
            c.ArmourFamily = ArmourFamilyOf(shape, am);
            d.P["armour_piece"] = V(text, "armour", shape); d.P["armour_material"] = V(text, "mat", am); d.P["armour"] = d.P["armour_material"] + " " + d.P["armour_piece"];
        }
        if (line.Kind == FightEventKind.Disarmed) c.Cause = line.A == 1 ? "shield" : "weapon";
        _ = partId;
        d.Ctx = c;
        return d;
    }

    static void DescribeBlow(FightLog log, BlowSnap b, in FightLine line, ITextSource text, LineDesc d, ref FlavourCtx c)
    {
        // armour: the step that decided the outcome (terminal outcomes first, then the deepest passage), naming the piece
        int best = -1, bestStep = -1;
        for (int i = 0; i < b.Steps.Length; i++)
        {
            if (!b.Steps[i].IsArmour) continue;
            int r = ArmourRank(b.Steps[i].Outcome);
            if (r > best) { best = r; bestStep = i; }
        }
        if (bestStep >= 0)
        {
            var st = b.Steps[bestStep];
            c.ArmourOutcome = OutcomeName(st.Outcome);
            // a blow that made a wound was not stopped: the armour turned the edge and the force went on (the resolver's Stopped is "became blunt")
            if (b.Wounds.Length > 0 && c.ArmourOutcome is "stopped" or "deflected") c.ArmourOutcome = "crushed";
            if (st.Piece >= 0 && st.Piece < b.PieceShapes.Length)
            {
                string shape = log.Str(b.PieceShapes[st.Piece]), mat = log.Str(b.PieceMaterials[st.Piece]);
                c.ArmourFamily = ArmourFamilyOf(shape, mat);
                d.P["armour_piece"] = V(text, "armour", shape); d.P["armour_material"] = V(text, "mat", mat);
                d.P["armour"] = d.P["armour_material"] + " " + d.P["armour_piece"];
                d.Armoured = true;
            }
            else c.ArmourOutcome = "none";
        }
        // the wounds: the deepest names the tissue and the part; the flags of all of them are the blow's
        WoundFlags all = WoundFlags.None; int deepest = -1, dv = -1;
        for (int i = 0; i < b.Wounds.Length; i++) { all |= b.Wounds[i].Flags; int dp = Depth(log, b.Wounds[i]); if (dp > dv) { dv = dp; deepest = i; } }
        c.Flags = all;
        string partId = b.Wounds.Length > 0 ? log.Str(b.Wounds[deepest].Part) : log.Str(b.Part);
        if (partId.Length > 0)
        {
            d.P["part"] = PartName(text, partId, out var side, out var baseName); d.P["side"] = side; d.P["part_base"] = baseName;
            c.Part = Severity.BaseOf(partId); c.PartGroup = PartGroupOf(c.Part);
        }
        if (deepest >= 0)
        {
            var w = b.Wounds[deepest];
            c.Tissue = TissueOf(log, w); c.Grade = GradeOf(log, w);
            d.P["tissue"] = V(text, "tissue", c.Tissue); d.P["structure"] = V(text, "struct", log.Str(w.Structure)); d.P["organ"] = V(text, "organ", log.Str(w.Organ));
            d.P["grade"] = c.Grade.Length > 0 ? V(text, "grade", c.Grade) : ""; d.P["bone"] = V(text, "bone", c.Part);
        }
        else c.Tissue = "none";
        c.Chained = d.Armoured && c.ArmourOutcome is "cut" or "crushed" or "bypassed" or "not_covered" or "absorbed";
        // consequences: up to two distinct flags by priority, each told about the wound that carries it
        var chosen = new List<WoundFlags>();
        foreach (var f in Priority)
        {
            if ((all & f) == 0) continue;
            if (f == WoundFlags.OrganHit && (all & WoundFlags.Vital) != 0) continue;
            if (f == WoundFlags.BoneBroken && (all & (WoundFlags.Compound | WoundFlags.Displaced)) != 0) continue;
            if (f == WoundFlags.Displaced && (all & WoundFlags.Compound) != 0) continue;
            if (f == WoundFlags.NerveHit && (all & (WoundFlags.MotorNerveHit | WoundFlags.SensoryNerveHit)) != 0) continue;
            chosen.Add(f);
            if (chosen.Count == 2) break;
        }
        foreach (var f in chosen)
        {
            WoundSnap carrier = b.Wounds[deepest];
            foreach (var w in b.Wounds) if ((w.Flags & f) != 0) { carrier = w; break; }
            var cc = c;
            cc.Flags = f;
            string pid = log.Str(carrier.Part);
            cc.Part = Severity.BaseOf(pid); cc.PartGroup = PartGroupOf(cc.Part); cc.Tissue = TissueOf(log, carrier); cc.Grade = GradeOf(log, carrier);
            d.Cons.Add((cc, carrier, f));
        }
    }

    /// <summary>Fills the per-consequence parameters (the part, structure and organ of the wound that carries the flag).</summary>
    public static Dictionary<string, string> ConsParams(FightLog log, LineDesc d, in WoundSnap w, ITextSource text)
    {
        var p = new Dictionary<string, string>(d.P, StringComparer.Ordinal);
        string pid = log.Str(w.Part);
        p["part"] = PartName(text, pid, out var side, out var bn); p["side"] = side; p["part_base"] = bn;
        p["structure"] = V(text, "struct", log.Str(w.Structure)); p["organ"] = V(text, "organ", log.Str(w.Organ));
        p["tissue"] = V(text, "tissue", TissueOf(log, w)); p["bone"] = V(text, "bone", Severity.BaseOf(pid));
        string g = GradeOf(log, w); p["grade"] = g.Length > 0 ? V(text, "grade", g) : "";
        p["count"] = w.Count > 0 ? w.Count.ToString() : "";
        return p;
    }
}
