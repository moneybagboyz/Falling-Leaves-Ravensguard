using Ravensguard.Sim.Behaviour;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Live;

public sealed partial class PeopleBridge
{
    private readonly SortedDictionary<EntityId, FollowState> _followers = [];
    /// <summary>The hero's trail, fed once per tick by the street view (<see cref="NoteHero"/>).</summary>
    public HeroTrail Trail { get; } = new();
    /// <summary>The minimum catch-up step per tick in mm (150 on foot at 1x; the caller scales it with the clock speed multiplier).</summary>
    public long FollowMinStepMm { get; set; } = 150;
    public IReadOnlyDictionary<EntityId, FollowState> Followers => _followers;
    public bool IsFollower(EntityId person) => _followers.ContainsKey(person);

    /// <summary>The hero's place this tick, continent millimetres and level.</summary>
    public void NoteHero(long xMm, long yMm, int z) => Trail.Note(xMm, yMm, z);

    /// <summary>
    /// The party members to bring into the bubble: (person, whether they follow or wait), in column order. A member who leaves the list is let go (they fold back as anyone does). Slots are the order given.
    /// A member told to wait keeps their place on the ground where they stand.
    /// </summary>
    public void SetFollowers(IReadOnlyList<(EntityId Person, bool Follow)> party)
    {
        var keep = new HashSet<EntityId>();
        int slot = 0;
        foreach (var (id, follow) in party)
        {
            keep.Add(id);
            if (!_followers.TryGetValue(id, out var fs)) _followers[id] = fs = new FollowState();
            fs.Follow = follow; if (follow) fs.Slot = slot++;
        }
        foreach (var id in _followers.Keys.Where(k => !keep.Contains(k)).ToList()) { _followers.Remove(id); if (_live.TryGetValue(id, out var a)) a.OutsideSince = long.MinValue; }
    }

    private void PlaceFollower(LiveActor a, FollowState fs)
    {
        fs.S = long.MinValue; fs.Step(Trail, FollowMinStepMm);
        if (Trail.Points.Count == 0) return;
        var p = Trail.At(fs.S); a.X = p.X; a.Y = p.Y; a.Z = p.Z; a.HeadX = p.HeadX; a.HeadY = p.HeadY; a.Walking = false; a.Action = JobKind.Idle; a.Intent = ActionKind.Idle;
    }

    private void StepFollower(LiveActor a, FollowState fs)
    {
        a.OutsideSince = -1;
        if (!fs.Follow) { a.Walking = false; a.Action = JobKind.Idle; a.Intent = ActionKind.Idle; return; }          // told to wait: they stay where they stand
        bool moved = fs.Step(Trail, FollowMinStepMm);
        if (Trail.Points.Count == 0) return;
        var p = Trail.At(fs.S); bool walking = moved && (p.X != a.X || p.Y != a.Y || p.Z != a.Z);
        a.X = p.X; a.Y = p.Y; a.Z = p.Z; if (walking) { a.HeadX = p.HeadX; a.HeadY = p.HeadY; } a.Walking = walking;
        a.Action = JobKind.Idle; a.Intent = ActionKind.Idle; a.OutsideSince = -1;
    }
}
