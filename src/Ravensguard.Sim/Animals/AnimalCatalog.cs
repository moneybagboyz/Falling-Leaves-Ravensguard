using Ravensguard.Content;

namespace Ravensguard.Sim.Animals;

public enum Gait : byte { Walk, Trot, Gallop }

/// <summary>A breed's numbers, resolved from <see cref="SpeciesDef"/>: speeds in millimetres per game second, stamina costs per metre, the daily ration in grams.</summary>
public sealed class Breed
{
    public required SpeciesDef Def { get; init; }
    public int Index { get; init; }
    public string Id => Def.Id;
    public string Species => Def.Species;
    public int MassKg => Def.MassKg;
    public int CarryKg => Def.CarryKg;
    public int PullN => Def.PullN;
    public int StaminaSp => Def.StaminaSp;
    public int WalkCmps { get; init; }
    public int TrotCmps { get; init; }
    public int GallopCmps { get; init; }
    public int TrotCost { get; init; }
    public int GallopCost { get; init; }
    public int RegenWalk { get; init; }
    public int RegenRest { get; init; }
    public bool CanRide => Def.Ridden && TrotCmps > 0;

    public int Cmps(Gait g) => g switch { Gait.Gallop => GallopCmps, Gait.Trot => TrotCmps, _ => WalkCmps };
    public int CostPerM(Gait g) => g switch { Gait.Gallop => GallopCost, Gait.Trot => TrotCost, _ => 0 };
    /// <summary>The speed a team of this breed can keep up for a working day: the trot when it has one, else the walk (the cart's gait cap, spec 4.5).</summary>
    public int WorkCmps => TrotCmps > 0 ? TrotCmps : WalkCmps;
    /// <summary>The ration, hay-equivalent grams a day: idle, and working (hay plus oats at 1.8).</summary>
    public long IdleRationG => Def.IdleHayKgDay * 1000L;
    public long WorkRationG => Def.WorkHayKgDay * 1000L + Def.WorkOatsHgDay * 100L * 18 / 10;
}

/// <summary>Every breed of a content database in id order (the index is what saves store).</summary>
public sealed class AnimalCatalog
{
    private readonly List<Breed> _list = [];
    private readonly Dictionary<string, Breed> _byId = new(StringComparer.Ordinal);
    public IReadOnlyList<Breed> All => _list;

    public static AnimalCatalog From(ContentDatabase db)
    {
        var c = new AnimalCatalog();
        foreach (var d in db.All<SpeciesDef>().OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            int G(string k) => d.GaitCmps.TryGetValue(k, out var v) ? v : 0;
            int C(string k) => d.StaminaCostSpPerM.TryGetValue(k, out var v) ? v : 0;
            int R(string k) => d.RegenSpPerGs.TryGetValue(k, out var v) ? v : 0;
            var b = new Breed { Def = d, Index = c._list.Count, WalkCmps = G("walk"), TrotCmps = G("trot"), GallopCmps = G("gallop"), TrotCost = C("trot"), GallopCost = C("gallop"), RegenWalk = R("walk"), RegenRest = R("rest") };
            c._list.Add(b); c._byId[d.Id] = b;
        }
        return c;
    }

    public Breed? Find(string id) => _byId.TryGetValue(id, out var b) ? b : null;
    public Breed Get(string id) => _byId[id];
    public Breed At(int index) => _list[index];
}
