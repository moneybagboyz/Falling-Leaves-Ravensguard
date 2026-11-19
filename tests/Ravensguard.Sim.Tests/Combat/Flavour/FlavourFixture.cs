using Ravensguard.Content;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Flavour;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Dialogue;
using Ravensguard.Sim.Tests.Combat.Melee2;
using Ravensguard.Sim.Tests.Dialogue;

namespace Ravensguard.Sim.Tests.Combat.Flavour;

/// <summary>The fight content, the flavour book of the shipped templates and the game's English text (en.csv), loaded once.</summary>
public sealed class FlavourFixture
{
    public FightFixture F { get; } = new();
    public FlavourBook Book { get; }
    public ITextSource Text { get; }
    public IReadOnlyDictionary<string, string> En { get; }
    public IReadOnlyList<AudioEventDef> Audio { get; }

    public FlavourFixture()
    {
        Book = new FlavourBook(F.Db);
        En = TalkKit.EnCsv();
        Text = new DictText(En);
        Audio = [.. F.Db.All<AudioEventDef>().OrderBy(a => a.Id, StringComparer.Ordinal)];
    }

    public FightContent C => F.C;

    /// <summary>A recorded duel: hero (player flag false so the AI drives both) against a bandit, until it is over or <paramref name="maxTicks"/>.</summary>
    public (FightWorld W, FlavourRecorder R) Duel(ulong seed, string weaponA = "arming_sword", string matA = "steel", string weaponB = "axe", string matB = "iron", string[]? wornA = null,
        string[]? wornB = null, int maxTicks = 20 * 120, bool heroPlayer = false)
    {
        var w = new FightWorld(C, seed, 1, 8 * 3600) { RecordEvents = true };
        IReadOnlyList<WornItem> Worn(string[]? ids, params string[] dflt) => [.. (ids ?? dflt).Select(s => { var p = s.Split(':'); return new WornItem(p[0], p[1]); })];
        w.Add(new FighterKit
        {
            Name = "Aldric", Team = 0, Weapon = new WeaponInstance(weaponA, matA), Shield = "heater", Skill = 55, X = -1800, FaceX = 1024,
            Worn = Worn(wornA, "gambeson:linen", "mail_hauberk:iron", "helm:iron"),
        }, player: heroPlayer);
        w.Add(new FighterKit
        {
            Name = "Bram", Team = 1, Weapon = new WeaponInstance(weaponB, matB), Skill = 50, X = 1800, FaceX = -1024, Stance = "aggressive",
            Worn = Worn(wornB, "shirt:linen", "tunic:wool", "jack:linen"),
        });
        var r = FlavourRecorder.Attach(w, null, Book);
        r.Log.Hero = 0; r.Log.HeroTeam = 0;
        while (!w.IsOver && w.Tick < maxTicks) w.StepWithAi();
        r.Finish();
        return (w, r);
    }

    public string Line(FightLog log, int i) => FlavourFormatter.Format(log, i, Book, Text);
}
