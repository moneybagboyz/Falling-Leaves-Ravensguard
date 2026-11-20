using Ravensguard.Content;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Law;

/// <summary>Who answered a shout (crime-and-justice 4.2): the witness who raised it, everyone who heard, the guards who now pursue and the townsfolk who joined.</summary>
public sealed record CryResult(Observer Raiser, IReadOnlyList<Observer> Hearers, IReadOnlyList<Observer> Pursuers, IReadOnlyList<Observer> Joiners)
{
    public CryInfo Info => new(Raiser.Person, Hearers.Count, Pursuers.Count);
}

/// <summary>The cry as the chronicle and the barrier keep it: who raised it and how many heard and how many guards turned out.</summary>
public readonly record struct CryInfo(EntityId Raiser, int Hearers, int Guards);

/// <summary>
/// The hue and cry (crime-and-justice 4.2). A witness raises it when he does not fear the actor (renown below 600 permille, or he stands with two others) and is not the actor's kin or friend (trust
/// below 500 permille). Everyone awake within 30 m hears (walls halve the reach), guards awake within 80 m switch to pursuit, and up to three townsfolk with melee 30 or more and no fear join.
/// </summary>
public static class HueAndCry
{
    /// <summary>Whether this witness fears the actor: a renowned one is feared unless the witness stands with two others within 6 m.</summary>
    public static bool Fears(LawSettingsDef s, WitnessActor actor, Sighting w, IReadOnlyList<Sighting> witnesses)
    {
        if (actor.RenownPm < s.FearRenownPm) return false;
        int company = 0; foreach (var o in witnesses) if (o.Who != w.Who && Witnessing.DistCm(o.Who.At, w.Who.At) <= 600) company++;
        return company < 2;
    }

    public static bool IsFriend(LawSettingsDef s, Observer o) => o.Kin || o.TrustPm >= s.FriendTrustPm;

    /// <summary>The cry, or null when nobody raises it (every witness fears, is kin or a friend, or there are none). <paramref name="everyone"/> is everybody near the act, guards included.</summary>
    public static CryResult? Raise(LawSettingsDef s, IWitnessScene scene, WitnessActor actor, IReadOnlyList<Sighting> witnesses, IReadOnlyList<Observer> everyone)
    {
        Observer? raiser = null;
        foreach (var w in witnesses.OrderBy(w => w.Who.Person))
            if (!IsFriend(s, w.Who) && !Fears(s, actor, w, witnesses)) { raiser = w.Who; break; }
        if (raiser == null) return null;
        var hearers = new List<Observer>(); var pursuers = new List<Observer>(); var joiners = new List<Observer>();
        long hear = s.CryHearM * 100L, guard = s.CryGuardM * 100L;
        foreach (var o in everyone.OrderBy(o => o.Person))
        {
            if (!o.Awake || o == raiser || o.Person == actor.Person) continue;
            long d = Witnessing.DistCm(o.At, raiser.At);
            long reach = scene.Los(o.At, raiser.At) ? hear : hear * s.CryWallPct / 100;
            if (d <= reach) hearers.Add(o);
            if (o.Guard && d <= guard) pursuers.Add(o);
        }
        bool feared = actor.RenownPm >= s.FearRenownPm;
        if (!feared)
            foreach (var o in hearers.Where(o => !o.Guard && o.Melee >= s.CryJoinMelee && !IsFriend(s, o)).OrderBy(o => Witnessing.DistCm(o.At, raiser.At)).ThenBy(o => o.Person).Take(s.CryJoinMax)) joiners.Add(o);
        return new CryResult(raiser, hearers, pursuers, joiners);
    }
}
