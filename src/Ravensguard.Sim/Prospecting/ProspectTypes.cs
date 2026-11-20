using Ravensguard.Sim.Core;
using Ravensguard.Sim.WorldGen;

namespace Ravensguard.Sim.Prospecting;

/// <summary>How a piece of knowledge was had (prospecting spec 4). Append only: saved as a byte.</summary>
public enum ProspectMethod : byte { Walk, ReadLand, Pan, TestPit, TrialShaft, Report, Hired }

/// <summary>
/// What is known of a resource: the surface kinds of the resource layer (<c>ResourceKind</c>, 0..15) and the ore bodies' minerals (<see cref="OreBase"/> + <see cref="MineralType"/>). One code
/// space so a knowledge row names either.
/// </summary>
public static class ResourceCode
{
    public const int OreBase = 100;
    public static int Ore(MineralType m) => OreBase + (int)m;
    public static bool IsOre(int code) => code >= OreBase;
    public static MineralType Mineral(int code) => (MineralType)(code - OreBase);
    public static Ravensguard.Sim.Production.ResourceKind Surface(int code) => (Ravensguard.Sim.Production.ResourceKind)code;
    /// <summary>The en.csv key of a code's name.</summary>
    public static string Key(int code) => IsOre(code) ? "px_res_" + Mineral(code).ToString().ToLowerInvariant() : "px_res_" + Surface(code).ToString().ToLowerInvariant();
}

/// <summary>
/// One row of a person's knowledge (prospecting spec 3): who knows, which acre tile and resource, the estimated amount (grams), the estimated grade (permille; 0 for surface kinds), the
/// confidence (permille) and the day it was surveyed, and how. Estimates are the truth plus an error whose spread falls with skill and method; they go stale as the stock is worked.
/// </summary>
public readonly record struct ProspectRow(EntityId Person, int Tile, int Resource, long EstG, int GradePm, int ConfidencePm, int Day, ProspectMethod Method);

/// <summary>A hint from reading the land (spec 4): a hidden ore body of a mineral within the read radius, in a direction (0 = north, clockwise in eighths), not where.</summary>
public readonly record struct ProspectHint(EntityId Person, int Tile, int Mineral, int Direction, int Day);

/// <summary>What panning a stream found (spec 4): the minerals of the catchment upstream (which, not where).</summary>
public readonly record struct PanFinding(EntityId Person, int Tile, int[] Minerals, int Day);

/// <summary>A prospecting action in progress (spec 7: it runs while the prospector does it): who, what, where, from when to when; finished at the hour after <see cref="EndSec"/>.</summary>
public sealed class ProspectJob
{
    public int Serial;
    public EntityId Person;
    public ProspectMethod Method;
    public int Tile;
    public int Skill;
    public long StartSec, EndSec;
    /// <summary>A hired prospector's job: the client who gets the written survey (the hero), the fee paid.</summary>
    public EntityId Client;
    public long FeeMites;
    public bool Done;
}

/// <summary>A mining licence (spec 5): the holder, the vein (or tile), the mineral, who granted it (the lord of a fief or the crown of a realm), the fee and the royalty.</summary>
public sealed class MiningLicence
{
    public int Serial;
    public EntityId Holder;
    public int Vein = -1, Tile;
    public MineralType Mineral;
    public Ravensguard.Sim.Land.GrantorKind Grantor;
    public int GrantorIndex;
    public long FeeMites;
    public int RoyaltyPm;
    public int Day;
    public bool Revoked;
}

/// <summary>
/// The knowledge records of prospecting (spec 3, PX-2): people's rows (one per person x tile x resource, the newest kept), hints, pan findings, survey reports (an item id and the rows it
/// carries: knowledge is property) and the jobs in progress. The truth is never here: it stays in the resource layer and the veins.
/// </summary>
public sealed class ProspectBook
{
    public SortedDictionary<(ulong Person, int Tile, int Resource), ProspectRow> Rows { get; } = [];
    public List<ProspectHint> Hints { get; } = [];
    public List<PanFinding> Pans { get; } = [];
    /// <summary>Survey report items (the item's id) and the knowledge each carries.</summary>
    public SortedDictionary<EntityId, ProspectRow[]> Reports { get; } = [];
    public List<ProspectJob> Jobs { get; } = [];
    public int NextJob = 1;

    public bool IsEmpty => Rows.Count == 0 && Hints.Count == 0 && Pans.Count == 0 && Reports.Count == 0 && Jobs.Count == 0 && NextJob == 1;

    /// <summary>Keeps the row unless the person already knows the same tile and resource more confidently from a later day.</summary>
    public void Learn(ProspectRow r)
    {
        var k = (r.Person.Value, r.Tile, r.Resource);
        if (Rows.TryGetValue(k, out var old) && (old.ConfidencePm > r.ConfidencePm || (old.ConfidencePm == r.ConfidencePm && old.Day > r.Day))) return;
        Rows[k] = r;
    }

    public IEnumerable<ProspectRow> Of(EntityId person) { foreach (var r in Rows.Values) if (r.Person == person) yield return r; }
    public IEnumerable<ProspectRow> At(EntityId person, int tile) { foreach (var r in Rows.Values) if (r.Person == person && r.Tile == tile) yield return r; }

    public void Hash(Action<long> L)
    {
        L(NextJob); L(Rows.Count);
        foreach (var r in Rows.Values) Row(r, L);
        L(Hints.Count); foreach (var h in Hints) { L((long)h.Person.Value); L(h.Tile); L(h.Mineral); L(h.Direction); L(h.Day); }
        L(Pans.Count); foreach (var p in Pans) { L((long)p.Person.Value); L(p.Tile); L(p.Day); L(p.Minerals.Length); foreach (int m in p.Minerals) L(m); }
        L(Reports.Count); foreach (var (id, rows) in Reports) { L((long)id.Value); L(rows.Length); foreach (var r in rows) Row(r, L); }
        L(Jobs.Count); foreach (var j in Jobs) { L(j.Serial); L((long)j.Person.Value); L((long)j.Method); L(j.Tile); L(j.Skill); L(j.StartSec); L(j.EndSec); L((long)j.Client.Value); L(j.FeeMites); L(j.Done ? 1 : 0); }
    }

    private static void Row(ProspectRow r, Action<long> L) { L((long)r.Person.Value); L(r.Tile); L(r.Resource); L(r.EstG); L(r.GradePm); L(r.ConfidencePm); L(r.Day); L((long)r.Method); }
}
