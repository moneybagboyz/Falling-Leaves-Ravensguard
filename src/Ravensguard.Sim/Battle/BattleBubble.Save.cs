using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Morale;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Save;

namespace Ravensguard.Sim.Battle;

public sealed partial class BattleBubble
{
    /// <summary>Rebuilds an open bubble over a restored fight (the <c>fight</c> section restored first: its fighters, wounds and formation book): the box, the sides with reserves and waves, the people entering, the bookkeeping.</summary>
    public static BattleBubble Restore(FightWorld w, BattleRecord r)
    {
        var rules = FormationRules.For(w.Content.Db);
        var b = new BattleBubble
        {
            Id = r.Id, W = w, Rules = rules.Bubble, OriginMm = (r.OriginX, r.OriginY), CapTotal = r.CapTotal, Box = ((int)r.Box[0], (int)r.Box[1], (int)r.Box[2], (int)r.Box[3]),
            CauseKind = (CauseKind)r.CauseKind, CauseEntry = new EntityId(r.CauseEntry), CauseNoteKey = r.CauseNote, Place = new EntityId(r.Place),
        };
        foreach (var sr in r.Sides)
        {
            var st = sr.State;
            b.Sides.Add(new BattleSide
            {
                Team = sr.Team, Realm = sr.Realm, Commander = sr.Commander, HostId = sr.HostId, Kind = (SideKind)sr.Kind,
                LiveCap = (int)st[0], Strength = (int)st[1], WavesSent = (int)st[2], EntryX = (int)st[3], EntryY = (int)st[4], FaceX = (int)st[5], FaceY = (int)st[6], NextForm = (int)st[7], LastWaveTick = (int)st[8], Human = st[9] != 0,
                Reserve = [.. sr.Reserve.Select(BattleRecord.UnitOf)], Pending = [.. sr.Pending.Select(BattleRecord.UnitOf)],
            });
        }
        b.StartTick = (int)r.State[0]; b.ContactSeen = r.State[1] != 0; b.LastEngaged = (int)r.State[2]; b.Hero = (int)r.State[3];
        w.LodCentre = r.State[4] != 0 ? ((int)r.State[5], (int)r.State[6]) : null;
        if (r.State.Length > 7) b.WaveUnderPm = (int)r.State[7];
        b.NamedCaptains.AddRange(r.NamedCaptains);
        b.RecomputePaused();
        return b;
    }
}
