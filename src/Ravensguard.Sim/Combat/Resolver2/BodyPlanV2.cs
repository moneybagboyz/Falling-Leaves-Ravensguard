using Ravensguard.Content;

namespace Ravensguard.Sim.Combat.Resolver2;

public enum TissueRole : byte { Soft, Muscle, Bone, Other }

/// <summary>One tissue layer of a part: the material, its thickness in micrometres and its role (soft, muscle, bone).</summary>
public sealed class LayerV2
{
    public required MaterialV2 Tissue { get; init; }
    public required long ThicknessUm { get; init; }
    public required TissueRole Role { get; init; }
    public bool IsSoft => Role == TissueRole.Soft;
    public bool IsMuscle => Role == TissueRole.Muscle;
    public bool IsBone => Role == TissueRole.Bone;
}

/// <summary>A structure inside the muscle (vessel, tendon, nerve, ligament, cord) in integer form.</summary>
public sealed class StructureV2
{
    public required string Id { get; init; }
    public required StructureKind Kind { get; init; }
    public required MaterialV2 Tissue { get; init; }
    public required int AreaPpm { get; init; }
    public string? Joint { get; init; }
    /// <summary>Area multiplier by facing (front, flank, rear), permille.</summary>
    public required int[] FacingPm { get; init; }
    /// <summary>Area multiplier per plan gap index (0: none), permille.</summary>
    public required int[] GapBoostPm { get; init; }
    /// <summary>Parts that lose function when this nerve is cut.</summary>
    public required int[] Distal { get; init; }
}

public sealed class OrganV2
{
    public required string Id { get; init; }
    public required MaterialV2 Tissue { get; init; }
    public required int AreaPpm { get; init; }
    public bool Vital { get; init; }
    public int BleedX10 { get; init; }
    public bool BeforeBone { get; init; }
}

/// <summary>A concrete part instance (sided parts expanded to id_l / id_r) with its hosted parts.</summary>
public sealed class PartV2
{
    public required string Id { get; init; }
    public required string BaseId { get; init; }
    public required int Index { get; init; }
    public required int BaseIndex { get; init; }
    public required string Name { get; init; }
    /// <summary>0 none, 1 left, 2 right.</summary>
    public required int Side { get; init; }
    public required int RelPpm { get; init; }
    public required long DepthUm { get; init; }
    public required bool Severable { get; init; }
    public int Parent { get; internal set; } = -1;
    public List<int> Children { get; } = [];
    /// <summary>Hosted parts: the host part index (or -1).</summary>
    public int Host { get; internal set; } = -1;
    public List<int> Hosted { get; } = [];
    public required bool Shell { get; init; }
    public int ExposureGap { get; internal set; } = -1;
    public int AreaPpm { get; init; }
    public int AimFactorPm { get; init; }
    public int PainPm { get; init; } = 1000;
    public string? Limb { get; init; }
    public bool RibSlip { get; init; }
    public required IReadOnlyDictionary<string, int> Function { get; init; }
    public required LayerV2[] Layers { get; init; }
    public required StructureV2[] Structures { get; init; }
    public required OrganV2[] Organs { get; init; }
    /// <summary>Shell parts that stand for this part's bone layer (head: skull; upper body: ribs left and right; lower body: pelvis).</summary>
    public List<int> Shells { get; } = [];
    public bool IsHosted => Host >= 0;
    public long TotalLayerUm { get { long t = 0; foreach (var l in Layers) t += l.ThicknessUm; return t; } }
    public int FunctionPm(string channel) => Function.TryGetValue(channel, out var v) ? v : 0;
}

/// <summary>A named gap: the base parts it opens onto (resolver-v2 4.2).</summary>
public sealed class GapV2
{
    public required string Name { get; init; }
    public required int Index { get; init; }
    /// <summary>Bits over base part indices.</summary>
    public required ulong BaseMask { get; init; }
    public bool OnBase(int baseIndex) => (BaseMask & (1UL << baseIndex)) != 0;
}

/// <summary>
/// Body plan v2 (anatomy-and-body-plans.md 3): parts with hosted parts and shells, structures with kinds, facing and gap boosts, organs, gaps,
/// the plan flags (bleeds, feels pain, animating core) and the id migration table. Built once at load; parts in content order, sided parts
/// left then right, then hosted parts; stable for hashing.
/// </summary>
public sealed class BodyPlanV2
{
    public required string Id { get; init; }
    public required int Index { get; init; }
    public required int SizeCm3 { get; init; }
    public required int BloodMl { get; init; }
    public required PartV2[] Parts { get; init; }
    public required IReadOnlyList<string> BaseIds { get; init; }
    public required GapV2[] Gaps { get; init; }
    /// <summary>The largest targetable (non-hosted) part, ppm: the hit-chance zone factor's denominator.</summary>
    public required int LargestPpm { get; init; }
    public bool Bleeds { get; init; } = true;
    public bool FeelsPain { get; init; } = true;
    public int AnimatingCore { get; init; } = -1;
    public int BleedScalePm { get; init; } = 1000;
    public int UntargetablePm { get; init; }
    public int TargetBonusPoints { get; init; }
    public int FleeBelowBloodPm { get; init; }
    public required IReadOnlyDictionary<string, string[]> Migrate { get; init; }
    private readonly Dictionary<string, int> _byId = new(StringComparer.Ordinal);

    public int IndexOf(string partId) => _byId.TryGetValue(partId, out var i) ? i : -1;
    public PartV2 this[string partId] => Parts[_byId[partId]];
    public int GapIndex(string name) { foreach (var g in Gaps) if (g.Name == name) return g.Index; return -1; }
    public int BaseIndexOf(string baseId) { for (int i = 0; i < BaseIds.Count; i++) if (BaseIds[i] == baseId) return i; return -1; }

    /// <summary>What a v1 structure id loads as (anatomy 3.1 migration): <c>arm_nerve</c> becomes the motor and the sensory nerve; unknown ids load as themselves.</summary>
    public string[] MigrateStructure(string id) => Migrate.TryGetValue(id, out var to) ? to : [id];

    /// <summary>Neighbours in the stable order used by a redirected miss: parent, then children (hosted parts excluded).</summary>
    public int NeighbourCount(int part) { var p = Parts[part]; return (p.Parent >= 0 ? 1 : 0) + p.Children.Count; }
    public int Neighbour(int part, int i) { var p = Parts[part]; if (p.Parent >= 0) { if (i == 0) return p.Parent; i--; } return p.Children[i]; }

    public static BodyPlanV2 Build(BodyPlanDef def, int planIndex, Func<string, MaterialV2> material)
    {
        if (def.Parts.Count == 0) throw new ContentException($"body_plan '{def.Id}': no parts");
        var baseIds = def.Parts.Select(p => p.Id).ToList();
        if (baseIds.Count > 64) throw new ContentException($"body_plan '{def.Id}': more than 64 base parts");
        var byBase = def.Parts.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var gaps = new GapV2[def.NamedGaps.Count];
        for (int g = 0; g < gaps.Length; g++)
        {
            var gd = def.NamedGaps[g];
            ulong mask = 0;
            foreach (var b in gd.Also.Prepend(gd.Part))
            {
                int bi = baseIds.IndexOf(b);
                if (bi < 0) throw new ContentException($"body_plan '{def.Id}': gap '{gd.Name}' on unknown part '{b}'");
                mask |= 1UL << bi;
            }
            gaps[g] = new GapV2 { Name = gd.Name, Index = g, BaseMask = mask };
        }
        int GapIdx(string n) { for (int i = 0; i < gaps.Length; i++) if (gaps[i].Name == n) return i; return -1; }
        MaterialV2 Mat(BodyPartDef d, string t)
        {
            try { return material(t); }
            catch (KeyNotFoundException) { throw new ContentException($"body_plan '{def.Id}': part '{d.Id}' uses unknown material '{t}'"); }
        }
        static int Ppm(double v) => (int)Math.Round(v * 1_000_000, MidpointRounding.AwayFromZero);
        static long Um(double mm) => (long)Math.Round(mm * 1000, MidpointRounding.AwayFromZero);
        static TissueRole Role(string t) => t switch
        {
            "skin" or "fat" or "hair" or "hide" or "hide_thick" => TissueRole.Soft,
            "muscle" => TissueRole.Muscle,
            _ when t.StartsWith("bone", StringComparison.Ordinal) => TissueRole.Bone,
            _ => TissueRole.Other,
        };

        // instances: non-hosted parts first (so trunk and limbs keep the v1 order), then hosted parts
        var order = def.Parts.Where(p => p.Host == null && !p.Shell).Concat(def.Parts.Where(p => p.Host != null || p.Shell)).ToList();
        var parts = new List<PartV2>();
        var pending = new List<(PartV2 Part, BodyPartDef Def, string Side)>();
        foreach (var d in order)
        {
            foreach (var side in d.Sided ? new[] { "_l", "_r" } : [""])
            {
                var layers = d.Layers.Select(l => new LayerV2 { Tissue = Mat(d, l.Tissue), ThicknessUm = Um(l.ThicknessMm), Role = Role(l.Role ?? l.Tissue) }).ToArray();
                var structures = d.Structures.Select(s => new StructureV2
                {
                    Id = s.Id, Kind = s.Kind, Tissue = Mat(d, s.Tissue), AreaPpm = Ppm(s.AreaFraction), Joint = s.Joint,
                    FacingPm = [s.FacingPm.GetValueOrDefault("front", 1000), s.FacingPm.GetValueOrDefault("flank", 1000), s.FacingPm.GetValueOrDefault("rear", 1000)],
                    GapBoostPm = [.. gaps.Select(g => s.GapBoost.TryGetValue(g.Name, out var b) ? (int)Math.Round(b * 1000, MidpointRounding.AwayFromZero) : 0)],
                    Distal = [],
                }).ToArray();
                foreach (var s in d.Structures) foreach (var gb in s.GapBoost.Keys)
                        if (GapIdx(gb) < 0) throw new ContentException($"body_plan '{def.Id}': structure '{s.Id}' boosts unknown gap '{gb}'");
                var organs = d.Organs.Select(o => new OrganV2
                {
                    Id = o.Id, Tissue = Mat(d, o.Tissue), AreaPpm = Ppm(o.AreaFraction), Vital = o.Vital,
                    BleedX10 = (int)Math.Round(o.BleedMlPerS * 10, MidpointRounding.AwayFromZero), BeforeBone = o.BeforeBone,
                }).ToArray();
                var p = new PartV2
                {
                    Id = d.Id + side, BaseId = d.Id, Index = parts.Count, BaseIndex = baseIds.IndexOf(d.Id),
                    Name = d.Name + (side == "_l" ? " (left)" : side == "_r" ? " (right)" : ""), Side = side == "_l" ? 1 : side == "_r" ? 2 : 0,
                    RelPpm = Ppm(d.RelativeSize), DepthUm = Um(d.DepthMm), Severable = d.Severable, Shell = d.Shell,
                    AreaPpm = Ppm(d.AreaFraction), AimFactorPm = d.AimFactorPm, PainPm = d.PainPm, Limb = d.Limb, RibSlip = d.RibSlip,
                    Function = d.Function, Layers = layers, Structures = structures, Organs = organs,
                };
                if (d.ExposureGap != null)
                {
                    p.ExposureGap = GapIdx(d.ExposureGap);
                    if (p.ExposureGap < 0) throw new ContentException($"body_plan '{def.Id}': part '{d.Id}' exposure gap '{d.ExposureGap}' is not a named gap");
                }
                parts.Add(p); pending.Add((p, d, side));
            }
        }
        var plan = new BodyPlanV2
        {
            Id = def.Id, Index = planIndex, SizeCm3 = def.SizeCm3, BloodMl = (int)Math.Round(def.BloodMl, MidpointRounding.AwayFromZero), Parts = [.. parts],
            BaseIds = baseIds, Gaps = gaps,
            LargestPpm = parts.Where(p => p.Host < 0 && !p.Shell && pending[p.Index].Def.Host == null).Max(p => p.RelPpm),
            Bleeds = def.Bleeds, FeelsPain = def.FeelsPain, BleedScalePm = def.BleedScalePm, UntargetablePm = def.UntargetablePm,
            TargetBonusPoints = def.TargetBonusPoints, FleeBelowBloodPm = def.FleeBelowBloodPm,
            Migrate = def.Migrate.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray(), StringComparer.Ordinal),
            AnimatingCore = def.AnimatingCore == null ? -1 : parts.FindIndex(p => p.Id == def.AnimatingCore),
        };
        foreach (var p in parts) plan._byId[p.Id] = p.Index;
        if (def.AnimatingCore != null && plan.AnimatingCore < 0) throw new ContentException($"body_plan '{def.Id}': animating core '{def.AnimatingCore}' is not a part");

        int Resolve(string baseId, string side, string what, string of)
        {
            if (!byBase.TryGetValue(baseId, out var bd)) throw new ContentException($"body_plan '{def.Id}': part '{of}' has unknown {what} '{baseId}'");
            string id = baseId + (bd.Sided ? (side.Length > 0 ? side : throw new ContentException($"body_plan '{def.Id}': unsided part '{of}' cannot have sided {what} '{baseId}'")) : "");
            return plan._byId[id];
        }
        foreach (var (p, d, side) in pending)
        {
            if (d.Parent != null) { p.Parent = Resolve(d.Parent, side, "parent", d.Id); if (d.Host == null && !d.Shell) parts[p.Parent].Children.Add(p.Index); }
            if (d.Host != null)
            {
                p.Host = Resolve(d.Host, side, "host", d.Id);
                if (d.Shell) parts[p.Host].Shells.Add(p.Index); else parts[p.Host].Hosted.Add(p.Index);
            }
            else if (d.Shell) throw new ContentException($"body_plan '{def.Id}': shell part '{d.Id}' needs a host");
        }
        // nerve distal sets: the part and everything below it in the parent chain, with their hosted parts
        foreach (var (p, d, _) in pending)
        {
            for (int si = 0; si < p.Structures.Length; si++)
            {
                var s = p.Structures[si];
                if (s.Kind is not (StructureKind.MotorNerve or StructureKind.SensoryNerve or StructureKind.Nerve)) continue;
                var set = new List<int>();
                var sd = d.Structures[si];
                if (sd.Distal.Count > 0) { foreach (var b in sd.Distal) set.Add(Resolve(b, p.Side == 1 ? "_l" : p.Side == 2 ? "_r" : "", "distal part", d.Id)); }
                else Collect(parts, p.Index, set);
                p.Structures[si] = new StructureV2
                {
                    Id = s.Id, Kind = s.Kind, Tissue = s.Tissue, AreaPpm = s.AreaPpm, Joint = s.Joint, FacingPm = s.FacingPm, GapBoostPm = s.GapBoostPm, Distal = [.. set],
                };
            }
        }
        return plan;
    }

    private static void Collect(List<PartV2> parts, int i, List<int> into)
    {
        into.Add(i);
        foreach (var h in parts[i].Hosted) into.Add(h);
        foreach (var c in parts[i].Children) Collect(parts, c, into);
    }
}
