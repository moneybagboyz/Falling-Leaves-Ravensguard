using MessagePack;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Carts;

/// <summary>
/// An unfinished vehicle (vehicle-designer 4.2): a vehicle row whose parts are ghosts, at a yard or a slipway, and the people working on it. Its parts' items wait in its pile (item rows owned by the
/// vehicle's id); each ghost in stack order takes its item and then <c>install_h</c> worker-hours at the one worker-hour rule. Anyone allowed may resume it; it moves only when every part it moves on is built.
/// </summary>
public sealed class VehicleSite
{
    public EntityId Vehicle, Owner;
    /// <summary>The people who work on it in their working hours (08:00-18:00), each with the Carpentry skill the worker-hour rule reads, in id order.</summary>
    public List<EntityId> Crew = [];
    public List<int> CrewSkill = [];
    public bool Paused;
    /// <summary>Worker-milli-hours put in so far (all parts), and the game second the last part was fitted (0 while unfinished).</summary>
    public long WorkedMilli;
    public long DoneSec;
    /// <summary>The commission that ordered it (0: the owner's own site).</summary>
    public EntityId Commission;
}

public enum VehicleCommissionState : byte { Making, Ready, Collected, Cancelled }

/// <summary>
/// A vehicle commissioned from a cartwright or shipwright (vehicle-designer 4.2, crafting-full 4.7 at the vehicle scale): one P5-E commission per part recipe at his shop (the vehicle site is their client,
/// with a purse the client funded), the parts collected onto the site as they are made, and his install hours at his yard. The client collects the finished vehicle in person and pays the install labour.
/// Never instant: every part is made and fitted by him in his working hours.
/// </summary>
public sealed class VehicleCommission
{
    public EntityId Vehicle, Client, Maker;
    public int Settlement;
    /// <summary>The parts' price (the P5-E commissions' sum, funded into the site's purse at ordering) and the install labour (paid on collection).</summary>
    public long PartsMites, InstallMites;
    public List<EntityId> Parts = [];
    public VehicleCommissionState State;
    public long OrderedSec, ReadySec;
}

/// <summary>
/// A boat under way on a river (vehicle-designer 4.5, 4.6): which waterway, where along it (mm from its upstream end), where it is going, the speed of this leg and when it started. Abstract legs run
/// hourly on the tick worker (<see cref="VehicleSystem"/>); the live tier steps the same numbers each game second, so the two agree (VD-19).
/// </summary>
public sealed class Voyage
{
    public EntityId Boat;
    public int Waterway;
    public long AtMm, ToMm;
    public int GroundMmps;
    public long LegStartSec, LegDelaySec;
    public int Leg;
    public long ArrivedSec;
    /// <summary>A barge on a regular run: at each end it turns and goes back (a new leg, its own delay draw).</summary>
    public bool Shuttle;
    public bool Downstream => ToMm >= AtMm;
}

/// <summary>The abstract side of vehicles (vehicle-designer 4.6): sites, commissions and voyages, saved in the "mounts" section (<see cref="VehicleWorksSave"/>).</summary>
public sealed class VehicleWorks
{
    public SortedDictionary<EntityId, VehicleSite> Sites { get; } = [];
    public SortedDictionary<EntityId, VehicleCommission> Commissions { get; } = [];
    public SortedDictionary<EntityId, Voyage> Voyages { get; } = [];
    public bool IsEmpty => Sites.Count == 0 && Commissions.Count == 0 && Voyages.Count == 0;
}

[MessagePackObject]
public sealed class VehicleWorksSave
{
    [Key(0)] public ulong[] SiteVehicle { get; set; } = [];
    [Key(1)] public ulong[] SiteOwner { get; set; } = [];
    [Key(2)] public ulong[][] SiteCrew { get; set; } = [];
    [Key(3)] public int[][] SiteSkill { get; set; } = [];
    [Key(4)] public bool[] SitePaused { get; set; } = [];
    [Key(5)] public long[] SiteWorked { get; set; } = [];
    [Key(6)] public long[] SiteDone { get; set; } = [];
    [Key(7)] public ulong[] SiteCommission { get; set; } = [];
    [Key(8)] public ulong[] ComVehicle { get; set; } = [];
    [Key(9)] public ulong[] ComClient { get; set; } = [];
    [Key(10)] public ulong[] ComMaker { get; set; } = [];
    [Key(11)] public int[] ComSettlement { get; set; } = [];
    [Key(12)] public long[] ComParts { get; set; } = [];
    [Key(13)] public long[] ComInstall { get; set; } = [];
    [Key(14)] public ulong[][] ComPartIds { get; set; } = [];
    [Key(15)] public byte[] ComState { get; set; } = [];
    [Key(16)] public long[] ComOrdered { get; set; } = [];
    [Key(17)] public long[] ComReady { get; set; } = [];
    [Key(18)] public ulong[] VoyBoat { get; set; } = [];
    [Key(19)] public int[] VoyWaterway { get; set; } = [];
    [Key(20)] public long[] VoyAt { get; set; } = [];
    [Key(21)] public long[] VoyTo { get; set; } = [];
    [Key(22)] public int[] VoySpeed { get; set; } = [];
    [Key(23)] public long[] VoyStart { get; set; } = [];
    [Key(24)] public long[] VoyDelay { get; set; } = [];
    [Key(25)] public int[] VoyLeg { get; set; } = [];
    [Key(26)] public long[] VoyArrived { get; set; } = [];
    [Key(27)] public bool[] VoyShuttle { get; set; } = [];

    public static VehicleWorksSave Capture(VehicleWorks w)
    {
        var s = w.Sites.Values.ToList(); var c = w.Commissions.Values.ToList(); var v = w.Voyages.Values.ToList();
        return new VehicleWorksSave
        {
            SiteVehicle = [.. s.Select(x => x.Vehicle.Value)], SiteOwner = [.. s.Select(x => x.Owner.Value)], SiteCrew = [.. s.Select(x => x.Crew.Select(p => p.Value).ToArray())], SiteSkill = [.. s.Select(x => x.CrewSkill.ToArray())],
            SitePaused = [.. s.Select(x => x.Paused)], SiteWorked = [.. s.Select(x => x.WorkedMilli)], SiteDone = [.. s.Select(x => x.DoneSec)], SiteCommission = [.. s.Select(x => x.Commission.Value)],
            ComVehicle = [.. c.Select(x => x.Vehicle.Value)], ComClient = [.. c.Select(x => x.Client.Value)], ComMaker = [.. c.Select(x => x.Maker.Value)], ComSettlement = [.. c.Select(x => x.Settlement)],
            ComParts = [.. c.Select(x => x.PartsMites)], ComInstall = [.. c.Select(x => x.InstallMites)], ComPartIds = [.. c.Select(x => x.Parts.Select(p => p.Value).ToArray())], ComState = [.. c.Select(x => (byte)x.State)],
            ComOrdered = [.. c.Select(x => x.OrderedSec)], ComReady = [.. c.Select(x => x.ReadySec)],
            VoyBoat = [.. v.Select(x => x.Boat.Value)], VoyWaterway = [.. v.Select(x => x.Waterway)], VoyAt = [.. v.Select(x => x.AtMm)], VoyTo = [.. v.Select(x => x.ToMm)], VoySpeed = [.. v.Select(x => x.GroundMmps)],
            VoyStart = [.. v.Select(x => x.LegStartSec)], VoyDelay = [.. v.Select(x => x.LegDelaySec)], VoyLeg = [.. v.Select(x => x.Leg)], VoyArrived = [.. v.Select(x => x.ArrivedSec)], VoyShuttle = [.. v.Select(x => x.Shuttle)],
        };
    }

    public void Restore(VehicleWorks w)
    {
        w.Sites.Clear(); w.Commissions.Clear(); w.Voyages.Clear();
        for (int i = 0; i < SiteVehicle.Length; i++)
            w.Sites[new EntityId(SiteVehicle[i])] = new VehicleSite
            {
                Vehicle = new EntityId(SiteVehicle[i]), Owner = new EntityId(SiteOwner[i]), Crew = [.. SiteCrew[i].Select(p => new EntityId(p))], CrewSkill = [.. SiteSkill[i]], Paused = SitePaused[i], WorkedMilli = SiteWorked[i],
                DoneSec = SiteDone[i], Commission = new EntityId(SiteCommission[i]),
            };
        for (int i = 0; i < ComVehicle.Length; i++)
            w.Commissions[new EntityId(ComVehicle[i])] = new VehicleCommission
            {
                Vehicle = new EntityId(ComVehicle[i]), Client = new EntityId(ComClient[i]), Maker = new EntityId(ComMaker[i]), Settlement = ComSettlement[i], PartsMites = ComParts[i], InstallMites = ComInstall[i],
                Parts = [.. ComPartIds[i].Select(p => new EntityId(p))], State = (VehicleCommissionState)ComState[i], OrderedSec = ComOrdered[i], ReadySec = ComReady[i],
            };
        for (int i = 0; i < VoyBoat.Length; i++)
            w.Voyages[new EntityId(VoyBoat[i])] = new Voyage
            {
                Boat = new EntityId(VoyBoat[i]), Waterway = VoyWaterway[i], AtMm = VoyAt[i], ToMm = VoyTo[i], GroundMmps = VoySpeed[i], LegStartSec = VoyStart[i], LegDelaySec = VoyDelay[i], Leg = VoyLeg[i], ArrivedSec = VoyArrived[i], Shuttle = i < VoyShuttle.Length && VoyShuttle[i],
            };
    }
}
