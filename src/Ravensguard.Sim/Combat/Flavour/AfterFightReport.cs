using Ravensguard.Content;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Dialogue;

namespace Ravensguard.Sim.Combat.Flavour;

/// <summary>The links of a cause chain (feedback-flavour-log 4.3, plan 3.12): the death, the wound, the blow's author, the fight and what began it.</summary>
public sealed class CauseChain
{
    public int FighterId;
    public string Name = "";
    public int DeathSerial = -1, BlowSerial = -1, AttackerId = -1;
    public string Part = "", Structure = "", Weapon = "", Attacker = "", Trigger = "";
    public string Text = "";
    /// <summary>Death, wound, attacker, fight and trigger all known.</summary>
    public bool Complete => DeathSerial >= 0 && BlowSerial >= 0 && AttackerId >= 0 && Part.Length > 0 && Trigger.Length > 0;
}

public sealed class FallenEntry
{
    public int FighterId;
    public string Name = "";
    public bool Dead;
    public bool Named;
    /// <summary>The line that finished the person (Down or Death), prose.</summary>
    public string FinishingLine = "";
    public CauseChain Chain = new();
}

public sealed class ReportWound
{
    public string Text = "";
    /// <summary>What to do first (a <c>fl_ui_adv_*</c> key) and, for a bleed, the seconds until collapse.</summary>
    public string Advice = "";
    public int Seconds;
    public int Severity;
}

/// <summary>The after-fight report (feedback-flavour-log 4.3), built from the log alone: counts, the hero's own account, his wounds with what to do first, the fallen with their cause chains.</summary>
public sealed class AfterFightReport
{
    /// <summary>won, lost, drawn or fled.</summary>
    public string Result = "drawn";
    public string ResultLine = "";
    public int DurationSec;
    public int Dead, Down, Yielded, Routed;
    public int[] SideSizes = [];
    public int Thrown, Landed, Blocked, Parried, Dodged, Missed, Edge, Point, Blunt, Missile;
    public int LossHighPm, StaminaLowPct;
    public int BestSerial = -1;
    public string BestLine = "";
    /// <summary>Grazes and shallow cuts, counted rather than listed when there is something worse to list.</summary>
    public int MinorWounds;
    public List<ReportWound> Wounds { get; } = [];
    public List<FallenEntry> Fallen { get; } = [];
    public int Casualties => Dead + Down + Yielded + Routed;

    public static AfterFightReport Build(FightLog log, FlavourBook book, ITextSource text)
    {
        var r = new AfterFightReport { DurationSec = Math.Max(0, (log.Finished ? log.EndTick : log.Lines.Count > 0 ? log.Lines[^1].Tick : 0) - log.StartTick) / 20 };
        var teams = new SortedDictionary<int, int>();
        foreach (var f in log.Fighters.Values)
        {
            teams[f.Team] = teams.GetValueOrDefault(f.Team) + 1;
            switch ((FAct)f.End)
            {
                case FAct.Dead: r.Dead++; break;
                case FAct.Down: r.Down++; break;
                case FAct.Surrendered: r.Yielded++; break;
                case FAct.Fled: r.Routed++; break;
            }
        }
        r.SideSizes = [.. teams.Values];
        FighterInfo? hero = log.Hero >= 0 && log.TryInfo(log.Hero, out var h) ? h : null;
        bool heroFled = hero != null && (FAct)hero.End == FAct.Fled;
        r.Result = heroFled ? "fled" : log.Winner >= 0 && log.Winner == log.HeroTeam ? "won" : log.Winner >= 0 ? "lost" : "drawn";
        r.ResultLine = FlavourFormatter.Say(book, text, "report", Ctx(r.Result), new Dictionary<string, string>(), log.Seed, log.StartTick, (ulong)log.FightId);
        if (hero != null)
        {
            r.Thrown = hero.Swings; r.Landed = hero.Landed; r.Blocked = hero.Blocked; r.Parried = hero.Parried; r.Dodged = hero.Dodged; r.Missed = hero.Missed;
            r.Edge = hero.Edge; r.Point = hero.Point; r.Blunt = hero.Blunt; r.Missile = hero.Missile;
            r.LossHighPm = hero.LossHighPm; r.StaminaLowPct = hero.StaminaLow / 100;
            if (hero.BestLine >= 0 && log.IndexOfSerial(hero.BestLine) is var bi and >= 0)
            {
                r.BestSerial = hero.BestLine;
                string best = FlavourFormatter.Format(log, bi, book, text);
                r.BestLine = FlavourFormatter.Say(book, text, "report", Ctx("best"), new Dictionary<string, string> { ["line"] = best }, log.Seed, log.StartTick, (ulong)log.FightId, 2);
                if (r.BestLine.Length == 0) r.BestLine = best;
            }
            var all = HeroWounds(log, hero, text).ToList();
            bool anyWorse = all.Any(w => w.Severity >= 1 || w.Advice != "fl_ui_adv_none" && w.Advice != "fl_ui_adv_bleed");
            foreach (var w in all) { if (anyWorse && w.Severity == 0 && w.Advice is "fl_ui_adv_none" or "fl_ui_adv_bleed") r.MinorWounds++; else r.Wounds.Add(w); }
        }
        foreach (var f in log.Fighters.Values)
        {
            var end = (FAct)f.End;
            if (end is not (FAct.Dead or FAct.Down)) continue;
            var e = new FallenEntry { FighterId = f.Id, Name = FlavourDescribe.NameOf(f), Dead = end == FAct.Dead, Named = f.Named };
            int ser = end == FAct.Dead ? f.DeathSerial : f.DownSerial;
            int ix = ser >= 0 ? log.IndexOfSerial(ser) : -1;
            if (ix >= 0) e.FinishingLine = FlavourFormatter.Format(log, ix, book, text);
            e.Chain = ChainOf(log, f, book, text);
            r.Fallen.Add(e);
        }
        return r;
    }

    static FlavourCtx Ctx(string ev) { var c = FlavourCtx.Blank(); c.Event = ev; return c; }

    /// <summary>The wounds of the hero as the plain list with what to do first (4.3 section 3).</summary>
    static IEnumerable<ReportWound> HeroWounds(FightLog log, FighterInfo hero, ITextSource text)
    {
        for (int i = 0; i < log.Lines.Count; i++)
        {
            var l = log.Lines[i];
            if (l.Target != hero.Id || l.Kind != FightEventKind.Hit) continue;
            var b = log.BlowOf(l);
            if (b == null) continue;
            foreach (var w in b.Wounds)
            {
                var rw = new ReportWound { Text = FlavourFormatter.WoundText(log, w, text), Severity = l.Severity };
                var f = w.Flags;
                if ((f & WoundFlags.Severed) != 0) rw.Advice = "fl_ui_adv_sever";
                else if ((f & WoundFlags.SpineHit) != 0) rw.Advice = "fl_ui_adv_spine";
                else if ((f & (WoundFlags.OrganHit | WoundFlags.Vital)) != 0) rw.Advice = "fl_ui_adv_organ";
                else if ((f & WoundFlags.ArteryHit) != 0) { rw.Advice = "fl_ui_adv_artery"; rw.Seconds = w.BleedX10 > 0 ? Math.Max(1, 15_000 / w.BleedX10) : 0; }
                else if ((f & WoundFlags.BoneBroken) != 0) rw.Advice = "fl_ui_adv_bone";
                else if ((f & (WoundFlags.TendonHit | WoundFlags.LigamentHit)) != 0) rw.Advice = "fl_ui_adv_tendon";
                else if (w.BleedX10 > 5) rw.Advice = "fl_ui_adv_bleed";
                else rw.Advice = "fl_ui_adv_none";
                yield return rw;
            }
        }
    }

    /// <summary>"Bram died of blood loss from a cut femoral artery dealt by Aldric's axe in the fight at the ford, which began when the bandits ambushed the cart" (plan 3.12).</summary>
    public static CauseChain ChainOf(FightLog log, FighterInfo f, FlavourBook book, ITextSource text)
    {
        var ch = new CauseChain { FighterId = f.Id, Name = FlavourDescribe.NameOf(f) };
        bool dead = (FAct)f.End == FAct.Dead;
        ch.DeathSerial = dead ? f.DeathSerial : f.DownSerial;
        int ser = f.WorstHitSerial >= 0 ? f.WorstHitSerial : f.LastHitSerial;
        int ix = ser >= 0 ? log.IndexOfSerial(ser) : -1;
        string trigger = text.Has(log.TriggerKey) ? text.T(log.TriggerKey).Replace("{0}", log.TriggerArg) : log.TriggerKey;
        ch.Trigger = trigger;
        var p = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["target"] = ch.Name, ["place"] = log.Place, ["trigger"] = trigger, ["attacker"] = "", ["weapon"] = "", ["structure"] = "", ["organ"] = "", ["part_base"] = "", ["tgt_his"] = f.Kind == "human" ? "his" : "its",
        };
        var c = Ctx("chain_death");
        if (ix >= 0)
        {
            var hl = log.Lines[ix];
            ch.BlowSerial = hl.Serial; ch.AttackerId = hl.Actor;
            log.TryInfo(hl.Actor, out var hitter);
            ch.Attacker = FlavourDescribe.NameOf(hitter); ch.Weapon = FlavourDescribe.V(text, "weapon", log.Str(hl.WeaponShape));
            p["attacker"] = ch.Attacker; p["weapon"] = ch.Weapon;
            var b = log.BlowOf(hl);
            if (b != null && b.Wounds.Length > 0)
            {
                var w = b.Wounds[0]; int best = -1;
                foreach (var x in b.Wounds) { int bl = x.BleedX10; if (bl > best) { best = bl; w = x; } }
                ch.Part = FlavourDescribe.PartName(text, log.Str(w.Part), out _, out var bn);
                ch.Structure = FlavourDescribe.V(text, "struct", log.Str(w.Structure));
                p["structure"] = ch.Structure.Length > 0 ? ch.Structure : FlavourDescribe.V(text, "tissue", FlavourDescribe.TissueOf(log, w));
                p["organ"] = FlavourDescribe.V(text, "organ", log.Str(w.Organ)); p["part_base"] = bn;
                var fl = w.Flags;
                c.Cause = (fl & (WoundFlags.OrganHit | WoundFlags.Vital)) != 0 ? ((fl & WoundFlags.Vital) != 0 && Severity.BaseOf(log.Str(w.Part)) is "head" or "skull" ? "brain" : "organ") : "blood";
            }
        }
        ch.Text = FlavourFormatter.Say(book, text, "report", c, p, log.Seed, f.Id, (ulong)(f.Id + 1), 1);
        return ch;
    }
}
