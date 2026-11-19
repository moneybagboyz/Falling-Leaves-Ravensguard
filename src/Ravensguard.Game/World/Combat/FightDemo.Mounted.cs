using System.Linq;
using Ravensguard.Content;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Mounted;

namespace Ravensguard.Game.World.Combat;

/// <summary>
/// The mounted demo (<c>-- --fight-demo mounted</c>, mounted-combat): you on a courser with a lance, a heater and mail, 40 m from a swordsman, a braced spearman and an axeman, with a
/// bandit on a palfrey on the far side; Shift gallops, LMB couches the lance at a target. Screenshots (<c>--shot-what charge|impact|brace|unhorsed</c>, p4d-mounted-*.png) stage the moment.
/// </summary>
public partial class FightDemo
{
    bool _mounted;

    FightWorld BuildMounted(FightContent c)
    {
        var w = new FightWorld(c, _seed, 1, 8 * 3600);
        w.AddMounted(new FighterKit
        {
            Name = "You", Team = 0, Weapon = new WeaponInstance("lance", "steel"), Shield = "heater", Skill = 55, X = -30_000, FaceX = 1024,
            Worn = [new("shirt", "linen"), new("gambeson", "linen"), new("mail_hauberk", "iron"), new("helm", "iron")],
        }, new MountKit { Riding = 55, Barding = [new("horse_blanket", "linen")] }, player: true);
        w.Add(new FighterKit { Name = "Bandit", Team = 1, Weapon = new WeaponInstance("arming_sword", "iron"), Shield = "round_shield", Skill = 40, X = 0, Y = -1800, FaceX = -1024,
            Worn = [new("shirt", "linen"), new("tunic", "wool"), new("jack", "linen")] });
        w.Add(new FighterKit { Name = "Spearman", Team = 1, Weapon = new WeaponInstance("spear", "iron"), Skill = 45, X = 1500, Y = 2500, FaceX = -1024,
            Worn = [new("shirt", "linen"), new("gambeson", "linen"), new("cap", "leather")] });
        w.Add(new FighterKit { Name = "Axeman", Team = 1, Weapon = new WeaponInstance("axe", "iron"), Skill = 40, X = 3000, Y = -400, FaceX = -1024, Stance = "aggressive",
            Worn = [new("shirt", "linen"), new("tunic", "wool")] });
        w.AddMounted(new FighterKit { Name = "Rider", Team = 1, Weapon = new WeaponInstance("arming_sword", "iron"), Skill = 40, X = 24_000, Y = 6000, FaceX = -1024,
            Worn = [new("shirt", "linen"), new("jack", "linen")] }, new MountKit { Breed = "horse_palfrey", Riding = 40 });
        return w;
    }

    /// <summary>Two lancers riding at each other (the unhorsing screenshot): you and a knight on coursers, 50 m apart.</summary>
    FightWorld BuildJoust(FightContent c, ulong seed)
    {
        var w = new FightWorld(c, seed, 1, 8 * 3600);
        WornItem[] mail = [new("shirt", "linen"), new("gambeson", "linen"), new("mail_hauberk", "iron"), new("helm", "iron")];
        w.AddMounted(new FighterKit { Name = "You", Team = 0, Weapon = new WeaponInstance("lance", "steel"), Shield = "heater", Skill = 55, X = -25_000, FaceX = 1024, Worn = mail },
            new MountKit { Riding = 55 }, player: true);
        w.AddMounted(new FighterKit { Name = "Knight", Team = 1, Weapon = new WeaponInstance("lance", "steel"), Shield = "heater", Skill = 50, X = 25_000, Y = 1200, FaceX = -1024, Worn = mail },
            new MountKit { Riding = 45, Barding = [new("horse_mail", "iron")] });
        return w;
    }

    /// <summary>Steps the brain-driven demo to the moment a mounted screenshot shows.</summary>
    void StageMounted(string what, Fighter hero)
    {
        if (what == "unhorsed")
        {
            for (ulong s = _seed; s < _seed + 20; s++)
            {
                _w = BuildJoust(Content(), s); _view.Replace(_w);
                var me = _w.Fighters[0]; me.Player = false;
                for (int i = 0; i < 20 * 60; i++)
                {
                    int ev = _w.Events.Count;
                    _view.Advance(1);
                    for (int k = ev; k < _w.Events.Count; k++) if (_w.Events[k].Kind == FightEventKind.Unhorsed) { _view.Advance(14); me.Player = true; _view.Zoom = 3.2f; return; }
                }
            }
            return;
        }
        bool Seen(FightEventKind k, int from) { for (int i = from; i < _w.Events.Count; i++) if (_w.Events[i].Kind == k) return true; return false; }
        for (int i = 0; i < 20 * 60; i++)
        {
            int ev = _w.Events.Count;
            _view.Advance(1);
            var m = _w.Riding(hero);
            if (what == "charge" && m != null && m.Speed >= 9000 && _w.Fighters.Any(f => f.Team != hero.Team && f.Active && !_w.IsHorse(f) && _w.DistMm(hero, f) < 9000)) break;
            if (what == "impact" && Seen(FightEventKind.Charge, ev)) { _view.Advance(3); break; }
            if (what == "brace" && Seen(FightEventKind.BraceStrike, ev)) { _view.Advance(1); break; }
            if (what == "unhorsed" && Seen(FightEventKind.Unhorsed, ev)) { _view.Advance(12); break; }
        }
        hero.Player = true;
        _view.Zoom = what == "charge" ? 2.6f : 3.2f;
    }
}
