using MessagePack;
using Ravensguard.Content;
using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Siege;

namespace Ravensguard.Sim.Save;

/// <summary>One engine of the book (sieges-and-engines 7): the vehicle it is, its crew and pushers, aim, clock, serials, the hides' clock and what burns.</summary>
[MessagePackObject]
public sealed class EngineRowSave
{
    [Key(0)] public ulong Vehicle { get; set; }
    [Key(1)] public string Template { get; set; } = "";
    [Key(2)] public ulong[] Crew { get; set; } = [];
    [Key(3)] public ulong[] Pushers { get; set; } = [];
    [Key(4)] public bool HasAim { get; set; }
    [Key(5)] public long AimX { get; set; }
    [Key(6)] public long AimY { get; set; }
    [Key(7)] public int AimZ { get; set; }
    [Key(8)] public long ClockSec { get; set; }
    [Key(9)] public long ReadyAtSec { get; set; }
    [Key(10)] public uint Serial { get; set; }
    [Key(11)] public uint FireSerial { get; set; }
    [Key(12)] public uint CoverSerial { get; set; }
    [Key(13)] public long WetUntilSec { get; set; }
    [Key(14)] public int[] Burning { get; set; } = [];
    [Key(15)] public long BurnCarry { get; set; }
    [Key(16)] public bool Toppled { get; set; }
    [Key(17)] public bool Disabled { get; set; }
    [Key(18)] public long Fired { get; set; }
}

/// <summary>
/// The optional <c>sieges</c> section (a name of <c>Phase7Seeding.SectionNames</c>; no version bump): written only while the book holds an engine, so a world that never raised one keeps its bytes. P7-SG-B puts the engines here;
/// the siege book of SG-D appends its own keys. Wall damage is not here: it lives in the chunks' deltas (SG-A), parts and their hit points in the vehicle rows of the <c>mounts</c> section, the jobs of a camp's building in <c>crafting</c>.
/// </summary>
[MessagePackObject]
public sealed class SiegeSave
{
    public const string SectionName = "sieges";

    [Key(0)] public EngineRowSave[] Engines { get; set; } = [];
    /// <summary>P7-SG-C: the assault in progress over a battle, if one is (additive: an older save has none).</summary>
    [Key(1)] public AssaultRecord? Assault { get; set; }

    /// <summary>P7-SG-D: the sieges (keys 2 and 3) and the next id; absent while none was ever laid.</summary>
    [Key(2)] public SiegeRow[] Sieges { get; set; } = [];
    [Key(3)] public int NextSiege { get; set; } = 1;

    public SiegeBook RestoreSieges() { var b = new SiegeBook { Next = NextSiege }; foreach (var s in Sieges) b.Sieges[s.Id] = s; return b; }

    /// <summary>The section a world writes: while the book holds an engine, an assault is open or a siege was laid, else none (a world that never besieged keeps its bytes).</summary>
    public static SiegeSave? CaptureOrNull(EngineBook? b, AssaultRecord? a, SiegeBook? sb = null)
        => b is { IsEmpty: false } || a != null || sb is { IsEmpty: false }
            ? new SiegeSave { Engines = b is { IsEmpty: false } ? Capture(b).Engines : [], Assault = a, Sieges = sb is { IsEmpty: false } ? [.. sb.Sieges.Values] : [], NextSiege = sb?.Next ?? 1 } : null;

    public static SiegeSave Capture(EngineBook b) => new()
    {
        Engines = [.. b.Engines.Values.Select(e => new EngineRowSave
        {
            Vehicle = e.Vehicle.Value, Template = e.Template, Crew = [.. e.Crew.Select(c => c.Value)], Pushers = [.. e.Pushers.Select(c => c.Value)], HasAim = e.HasAim, AimX = e.AimX, AimY = e.AimY, AimZ = e.AimZ,
            ClockSec = e.ClockSec, ReadyAtSec = e.ReadyAtSec, Serial = e.Serial, FireSerial = e.FireSerial, CoverSerial = e.CoverSerial, WetUntilSec = e.WetUntilSec, Burning = [.. e.Burning], BurnCarry = e.BurnCarry,
            Toppled = e.Toppled, Disabled = e.Disabled, Fired = e.Fired,
        })],
    };

    /// <summary>Rebuilds the book over the loaded ride book; an engine whose template is no longer in the content is dropped (the load report names missing content elsewhere).</summary>
    public EngineBook Restore(ContentDatabase content, RideBook rides, ulong seed)
    {
        var b = new EngineBook(content, rides, seed);
        foreach (var r in Engines)
        {
            if (b.Table.Stat(r.Template) == null) continue;
            b.Engines[new EntityId(r.Vehicle)] = new Engine
            {
                Vehicle = new EntityId(r.Vehicle), Template = r.Template, Kind = b.Table.Kind(r.Template), Crew = [.. r.Crew.Select(c => new EntityId(c))], Pushers = [.. r.Pushers.Select(c => new EntityId(c))],
                HasAim = r.HasAim, AimX = r.AimX, AimY = r.AimY, AimZ = r.AimZ, ClockSec = r.ClockSec, ReadyAtSec = r.ReadyAtSec, Serial = r.Serial, FireSerial = r.FireSerial, CoverSerial = r.CoverSerial,
                WetUntilSec = r.WetUntilSec, Burning = [.. r.Burning], BurnCarry = r.BurnCarry, Toppled = r.Toppled, Disabled = r.Disabled, Fired = r.Fired,
            };
        }
        return b;
    }
}
