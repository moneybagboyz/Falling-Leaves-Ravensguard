using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Dialogue;

namespace Ravensguard.Sim.Combat.Flavour;

/// <summary>
/// The chronicle entries a fight asks for (feedback-flavour-log 4.3): the death of a named person, a permanent injury (a severed limb, a broken spine), the slaying of a monster by a
/// named person, a capture, and a battle of 20 or more casualties. Each carries its cause chain; nameless soldiers get nothing. Written through the normal chronicle API, once, when the fight ends.
/// </summary>
public static class FightChronicle
{
    public const int BattleCasualties = 20;

    /// <param name="watchedStance">P4-F (watch-mode 4.6): the stance the hero was watched under, when the fight was handed to the AI: the chronicle notes "(watched)" and the stance.</param>
    public static List<ChronicleEntry> Write(FightLog log, Chronicle chronicle, IdAllocator ids, FlavourBook book, ITextSource text, string? watchedStance = null)
    {
        var made = new List<ChronicleEntry>();
        var when = new GameTime(log.GameSec0 + (log.Finished ? log.EndTick : log.Lines.Count > 0 ? log.Lines[^1].Tick : 0));
        string fightId = log.FightId.ToString();
        ChronicleEntry Add(string type, EntityId[] subjects, string note, params (string K, string V)[] data)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal) { ["fight"] = fightId };
            foreach (var (k, v) in data) d[k] = v;
            var e = new ChronicleEntry(ids.Allocate(IdKind.Event), when, type, subjects, [new Cause(CauseKind.Wound, null, note)], d);
            chronicle.Add(e); made.Add(e);
            return e;
        }

        foreach (var f in log.Fighters.Values)
        {
            if (!f.Named) continue;
            var subj = new[] { new EntityId(f.Person) };
            if ((FAct)f.End == FAct.Dead)
            {
                var chain = AfterFightReport.ChainOf(log, f, book, text);
                Add("fight_death", subj, chain.Text.Length > 0 ? chain.Text : f.Name + " died in a fight.", ("text_key", "fl_report_chron_death"), ("name", f.Name), ("attacker", chain.Attacker), ("weapon", chain.Weapon));
                // a named person who slew a monster
                if (f.Kind != "human" && chain.AttackerId >= 0 && log.TryInfo(chain.AttackerId, out var killer) && killer.Named)
                    Add("monster_slain", [new EntityId(killer.Person)], killer.Name + " slew " + f.Name + ".", ("text_key", "fl_report_chron_monster"), ("name", f.Name), ("attacker", killer.Name));
            }
            else if ((FAct)f.End == FAct.Surrendered)
            {
                string captor = "";
                for (int i = log.Lines.Count - 1; i >= 0; i--)
                    if (log.Lines[i] is { Kind: FightEventKind.Surrender } l && l.Actor == f.Id) { captor = log.TryInfo(l.Target, out var c) ? c.Name : ""; break; }
                Add("fight_capture", subj, f.Name + " yielded and was taken.", ("text_key", "fl_report_chron_capture"), ("name", f.Name), ("other", captor));
            }
        }
        // permanent injuries: a named person who lost a limb or a spine
        var maimed = new HashSet<(int, string)>();
        for (int i = 0; i < log.Lines.Count; i++)
        {
            var l = log.Lines[i];
            if (l.Kind != FightEventKind.Hit || !log.TryInfo(l.Target, out var t) || !t.Named) continue;
            var b = log.BlowOf(l);
            if (b == null) continue;
            foreach (var w in b.Wounds)
            {
                if ((w.Flags & (WoundFlags.Severed | WoundFlags.SpineHit)) == 0) continue;
                string part = FlavourDescribe.PartName(text, log.Str(w.Part), out _, out var bn);
                if (!maimed.Add((t.Id, part)) || (FAct)t.End == FAct.Dead && (w.Flags & WoundFlags.Severed) == 0) continue;
                log.TryInfo(l.Actor, out var a);
                Add("fight_maiming", [new EntityId(t.Person)], t.Name + " was maimed: " + part + ".", ("text_key", "fl_report_chron_maim"), ("name", t.Name), ("part", part), ("attacker", a?.Name ?? ""), ("weapon", FlavourDescribe.V(text, "weapon", log.Str(l.WeaponShape))));
            }
        }
        if (watchedStance != null && log.Hero >= 0 && log.TryInfo(log.Hero, out var hero))
        {
            int foes = log.Fighters.Values.Count(x => x.Team != hero.Team);
            Add("fight_watched", hero.Person != 0 ? [new EntityId(hero.Person)] : [], $"{hero.Name} fought {foes} (watched, {watchedStance}).", ("text_key", "wm_chron_watched"), ("name", hero.Name), ("n", foes.ToString()), ("stance", watchedStance));
        }
        var rep = AfterFightReport.Build(log, book, text);
        if (rep.Casualties >= BattleCasualties) Add("battle_fought", [], rep.Casualties + " fell in a battle.", ("text_key", "fl_report_chron_battle"), ("n", rep.Casualties.ToString()));
        return made;
    }
}
