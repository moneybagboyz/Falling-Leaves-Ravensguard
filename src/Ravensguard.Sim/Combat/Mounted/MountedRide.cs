using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Combat.Mounted;

/// <summary>
/// The hero's Phase 3 ride in a live fight: a hero who is riding when a fight starts fights from the saddle on that horse (its breed, training, temper and stamina, the hero's Riding
/// skill and weight, the ground under it); when the fight ends the horse's stamina, lameness and blood go back to the animal, a horse killed in the fight dies, and a hero who came off
/// it is on foot. Both ends run inside the sim (the fight's start record carries the mount, and <c>LiveWorld.EndFight</c> settles it), so a replay repeats them.
/// </summary>
public static class MountedRide
{
    public static MountKit KitOf(RideBook book, Animal a) => new()
    {
        Breed = book.Breeds.At(a.Breed).Id, Animal = a.Id.Value, Name = book.Breeds.At(a.Breed).Def.Name, TrainedPm = a.TrainedPm, Temper = a.Temper,
        Riding = book.RidingSkill, RiderKg = book.RiderKg, StaminaSp = a.StaminaSp,
    };

    /// <summary>Puts the hero (fighter <paramref name="hero"/>) on the horse he is riding; nothing when he is on foot or driving.</summary>
    public static MountedFighter? MountHero(FightWorld w, int hero, RideBook book)
    {
        if (hero < 0 || hero >= w.Fighters.Count || !book.IsMounted || book.MountedAnimal is not { } a || !a.Alive) return null;
        var m = w.Mount(w.Fighters[hero], KitOf(book, a));
        w.Mounts!.Terrain.Base = FightTerrain.Of(book.Surface);
        return m;
    }

    /// <summary>The fight is over: what happened to the hero's horse goes back to the ride.</summary>
    public static void Settle(FightWorld w, RideBook book)
    {
        if (w.Mounts == null) return;
        foreach (var m in w.Mounts.Pairs)
        {
            if (m.Animal == 0 || book.Animals.Find(new EntityId(m.Animal)) is not { } a) continue;
            var h = w.Fighters[m.Horse];
            a.StaminaU = Math.Clamp(m.StaminaU, 0, book.Breeds.At(a.Breed).StaminaSp * 1000L);
            var bs = h.Book.Function(h.Plan);
            int lame = Math.Max(bs.Lameness, bs.WalkSpeedPm <= 0 ? 3 : bs.WalkSpeedPm < 500 ? 2 : bs.WalkSpeedPm < 1000 ? 1 : 0);     // a leg that cannot bear is lame 3
            a.Lameness = (byte)Math.Max(a.Lameness, Math.Clamp(lame, 0, 3));
            a.HealthPm = Math.Max(0, a.HealthPm - h.Book.LossPm * 2);
            bool ridden = book.Mounted == a.Id;
            if (h.Book.Dead || h.Act == FAct.Dead)
            {
                if (ridden) book.Dismount();
                a.State = AnimalState.Dead;
            }
            else if (!m.Mounted && ridden) book.Dismount();
        }
    }
}
