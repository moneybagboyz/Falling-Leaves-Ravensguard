using MessagePack;
using Ravensguard.Content;
using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Save;

[MessagePackObject]
public sealed class AnimalRowSave
{
    [Key(0)] public ulong Id { get; set; }
    [Key(1)] public int Breed { get; set; }
    [Key(2)] public byte Sex { get; set; }
    [Key(3)] public int BirthDay { get; set; }
    [Key(4)] public ulong Owner { get; set; }
    [Key(5)] public int MassKg { get; set; }
    [Key(6)] public int TrainedPm { get; set; }
    [Key(7)] public int Temper { get; set; }
    [Key(8)] public long StaminaU { get; set; }
    [Key(9)] public long FatigueU { get; set; }
    [Key(10)] public int FeedCenti { get; set; }
    [Key(11)] public int WaterPpm { get; set; }
    [Key(12)] public int ThirstHours { get; set; }
    [Key(13)] public ushort[] Shoes { get; set; } = [];
    [Key(14)] public long ShoeWearU { get; set; }
    [Key(15)] public byte Lameness { get; set; }
    [Key(16)] public int LameDays { get; set; }
    [Key(17)] public int Lame3Days { get; set; }
    [Key(18)] public long LameHardMm { get; set; }
    [Key(19)] public int HealthPm { get; set; }
    [Key(20)] public byte State { get; set; }
    [Key(21)] public int Stable { get; set; }
    [Key(22)] public bool Saddled { get; set; }
    [Key(23)] public bool Winded { get; set; }
    [Key(24)] public long XMm { get; set; }
    [Key(25)] public long YMm { get; set; }
    [Key(26)] public long OdometerMm { get; set; }
    [Key(27)] public long ForcedWindow { get; set; }
    [Key(28)] public int HerdSettlement { get; set; }
    [Key(29)] public ulong Vehicle { get; set; }
    [Key(30)] public long LastDay { get; set; }
}

[MessagePackObject]
public sealed class VehicleRowSave
{
    [Key(0)] public ulong Id { get; set; }
    [Key(1)] public int Template { get; set; }
    [Key(2)] public ulong Owner { get; set; }
    [Key(3)] public long XMm { get; set; }
    [Key(4)] public long YMm { get; set; }
    [Key(5)] public byte Facing { get; set; }
    [Key(6)] public ulong[] Team { get; set; } = [];
    [Key(7)] public long OdometerMm { get; set; }
    [Key(8)] public int WheelPm { get; set; }
    [Key(9)] public bool Stuck { get; set; }
    [Key(10)] public long StuckSinceSec { get; set; }
    [Key(11)] public long DugUntilSec { get; set; }
    [Key(12)] public long WearAccMm { get; set; }
    [Key(13)] public bool Driven { get; set; }
    [Key(14)] public ushort[] CargoGood { get; set; } = [];
    [Key(15)] public long[] CargoQty { get; set; } = [];
    // ---- P5-G (vehicle-designer 3.3): additive keys, expanded from the template at load when absent (a save from before part rows)
    /// <summary>The design's id (a content template or <c>player.*</c>); empty in an older save, whose <see cref="Template"/> is an index of <see cref="MountsSave.LegacyTemplates"/>.</summary>
    [Key(16)] public string Design { get; set; } = "";
    [Key(17)] public short[] PartX { get; set; } = [];
    [Key(18)] public short[] PartY { get; set; } = [];
    [Key(19)] public byte[] PartDeck { get; set; } = [];
    [Key(20)] public byte[] PartOrder { get; set; } = [];
    /// <summary>Indices of <see cref="MountsSave.PartTable"/> (the section names its parts, so new content parts never shift a save's rows).</summary>
    [Key(21)] public ushort[] PartIdx { get; set; } = [];
    [Key(22)] public byte[] PartState { get; set; } = [];
    [Key(23)] public byte[] PartTier { get; set; } = [];
    [Key(24)] public int[] PartHp { get; set; } = [];
    [Key(25)] public int[] PartWork { get; set; } = [];
    [Key(26)] public byte Water { get; set; }
    [Key(27)] public int WaterInL { get; set; }
}

/// <summary>
/// The "mounts" section (horses-and-carts spec H-17): the animals that are individuals, the vehicles with their cargo stores, the herd head counts the ledger keeps (horses, mules, donkeys), the
/// horses on loan and the hero's ride. Absent in a world nobody has ridden in, so older saves keep their bytes. Rows are in id order and every field is stored as it is, so a save made while mounted
/// or with a loaded cart loads and saves again to the same bytes.
/// </summary>
[MessagePackObject]
public sealed class MountsSave
{
    public const string SectionName = "mounts";
    [Key(0)] public int Version { get; set; } = 1;
    [Key(1)] public ulong NextAnimal { get; set; }
    [Key(2)] public ulong NextVehicle { get; set; }
    [Key(3)] public List<AnimalRowSave> Animals { get; set; } = [];
    [Key(4)] public List<VehicleRowSave> Vehicles { get; set; } = [];
    [Key(5)] public int[] HerdSettlement { get; set; } = [];
    [Key(6)] public string[] HerdSpecies { get; set; } = [];
    [Key(7)] public int[] HerdHead { get; set; } = [];
    [Key(8)] public ulong[] LoanAnimal { get; set; } = [];
    [Key(9)] public ulong[] LoanLender { get; set; } = [];
    [Key(10)] public long[] LoanDue { get; set; } = [];
    [Key(11)] public ulong Mounted { get; set; }
    [Key(12)] public ulong Driving { get; set; }
    [Key(13)] public ulong MountTarget { get; set; }
    [Key(14)] public long MountLeftGs { get; set; }
    [Key(15)] public long DismountLeftGs { get; set; }
    [Key(16)] public byte Want { get; set; }
    [Key(17)] public long HeroXMm { get; set; }
    [Key(18)] public long HeroYMm { get; set; }
    [Key(19)] public int RidingUses { get; set; }
    [Key(20)] public ushort[] HeapGood { get; set; } = [];
    [Key(21)] public long[] HeapQty { get; set; } = [];
    /// <summary>P5-G: the vehicle part ids the rows' <see cref="VehicleRowSave.PartIdx"/> index, in the order they were numbered when saved.</summary>
    [Key(22)] public string[] PartTable { get; set; } = [];
    /// <summary>P5-G: vehicle sites, commissions and voyages (absent while there are none).</summary>
    [Key(23)] public VehicleWorksSave? Works { get; set; }

    /// <summary>The Phase 3 templates in their index order (ordinal id order of the seven): an older save's <see cref="VehicleRowSave.Template"/>.</summary>
    public static readonly string[] LegacyTemplates = ["cart", "covered_wagon", "handcart", "hay_wain", "ox_wagon", "sledge", "wheelbarrow"];

    public static MountsSave Capture(RideBook b)
    {
        var o = new MountsSave { NextAnimal = b.Animals.NextSerial, NextVehicle = b.Vehicles.NextSerial, Mounted = b.Mounted.Value, Driving = b.Driving.Value, MountTarget = b.MountTarget.Value, MountLeftGs = b.MountLeftGs, DismountLeftGs = b.DismountLeftGs, Want = (byte)b.Want, HeroXMm = b.HeroXMm, HeroYMm = b.HeroYMm, RidingUses = b.RidingUses };
        foreach (var a in b.Animals.Rows)
            o.Animals.Add(new AnimalRowSave
            {
                Id = a.Id.Value, Breed = a.Breed, Sex = a.Sex, BirthDay = a.BirthDay, Owner = a.Owner.Value, MassKg = a.MassKg, TrainedPm = a.TrainedPm, Temper = a.Temper, StaminaU = a.StaminaU, FatigueU = a.FatigueU, FeedCenti = a.FeedCenti,
                WaterPpm = a.WaterPpm, ThirstHours = a.ThirstHours, Shoes = (ushort[])a.Shoes.Clone(), ShoeWearU = a.ShoeWearU, Lameness = a.Lameness, LameDays = a.LameDays, Lame3Days = a.Lame3Days, LameHardMm = a.LameHardMm, HealthPm = a.HealthPm,
                State = (byte)a.State, Stable = a.Stable, Saddled = a.Saddled, Winded = a.Winded, XMm = a.XMm, YMm = a.YMm, OdometerMm = a.OdometerMm, ForcedWindow = a.ForcedWindow, HerdSettlement = a.HerdSettlement, Vehicle = a.Vehicle.Value, LastDay = a.LastDay,
            });
        var cat = b.Templates;
        if (b.Vehicles.Rows.Count > 0) o.PartTable = [.. cat.PartList.Select(p => p.Id)];
        foreach (var v in b.Vehicles.Rows)
        {
            var rows = b.Hosted ? [] : v.Cargo.Rows();       // a hosted bed is saved with the economy's other stores
            var ps = v.Parts;
            o.Vehicles.Add(new VehicleRowSave
            {
                Id = v.Id.Value, Template = v.Template, Owner = v.Owner.Value, XMm = v.XMm, YMm = v.YMm, Facing = v.Facing, Team = [.. v.Team.Select(t => t.Value)], OdometerMm = v.OdometerMm, WheelPm = v.WheelPm, Stuck = v.Stuck,
                StuckSinceSec = v.StuckSinceSec, DugUntilSec = v.DugUntilSec, WearAccMm = v.WearAccMm, Driven = v.Driven, CargoGood = [.. rows.Select(r => r.Good)], CargoQty = [.. rows.Select(r => r.Qty)],
                Design = v.Design, PartX = [.. ps.Select(p => p.X)], PartY = [.. ps.Select(p => p.Y)], PartDeck = [.. ps.Select(p => p.Deck)], PartOrder = [.. ps.Select(p => p.Order)], PartIdx = [.. ps.Select(p => p.Part)],
                PartState = [.. ps.Select(p => (byte)p.State)], PartTier = [.. ps.Select(p => (byte)p.Tier)], PartHp = [.. ps.Select(p => p.HpMilli)], PartWork = [.. ps.Select(p => p.WorkMilli)], Water = (byte)v.Water, WaterInL = v.WaterInL,
            });
        }
        o.Works = b.Works.IsEmpty ? null : VehicleWorksSave.Capture(b.Works);
        var hs = b.Herds.Rows().ToList();
        o.HerdSettlement = [.. hs.Select(h => h.Settlement)]; o.HerdSpecies = [.. hs.Select(h => h.Species)]; o.HerdHead = [.. hs.Select(h => h.Head)];
        var hp = b.Hosted ? [] : b.Heap.Rows(); o.HeapGood = [.. hp.Select(r => r.Good)]; o.HeapQty = [.. hp.Select(r => r.Qty)];
        o.LoanAnimal = [.. b.Loans.Select(l => l.Animal.Value)]; o.LoanLender = [.. b.Loans.Select(l => l.Lender.Value)]; o.LoanDue = [.. b.Loans.Select(l => l.DueSec)];
        return o;
    }

    /// <summary>Rebuilds the book on the given content, goods and ledger (the economy section has restored the ledger's counts by now); the player's vehicle designs come from <paramref name="designs"/>.</summary>
    public RideBook Restore(ContentDatabase content, GoodCatalog goods, MassLedger ledger, ulong seed, Construction.DesignBook? designs = null) => Restore(new RideBook(content, goods, ledger, seed), designs);

    /// <summary>Rebuilds the book on the economy (its beds and heap are the economy's restored stores).</summary>
    public RideBook Restore(ContentDatabase content, Economy.EconomyState econ, ulong seed, Construction.DesignBook? designs = null) => Restore(new RideBook(content, econ, seed), designs);

    private RideBook Restore(RideBook b, Construction.DesignBook? designs)
    {
        if (designs != null) b.AttachDesigns(designs);
        b.Animals.Restore(Animals.Select(r => new Animal
        {
            Id = new EntityId(r.Id), Breed = r.Breed, Sex = r.Sex, BirthDay = r.BirthDay, Owner = new EntityId(r.Owner), MassKg = r.MassKg, TrainedPm = r.TrainedPm, Temper = r.Temper, StaminaU = r.StaminaU, FatigueU = r.FatigueU,
            FeedCenti = r.FeedCenti, WaterPpm = r.WaterPpm, ThirstHours = r.ThirstHours, Shoes = (ushort[])r.Shoes.Clone(), ShoeWearU = r.ShoeWearU, Lameness = r.Lameness, LameDays = r.LameDays, Lame3Days = r.Lame3Days, LameHardMm = r.LameHardMm,
            HealthPm = r.HealthPm, State = (AnimalState)r.State, Stable = r.Stable, Saddled = r.Saddled, Winded = r.Winded, XMm = r.XMm, YMm = r.YMm, OdometerMm = r.OdometerMm, ForcedWindow = r.ForcedWindow, HerdSettlement = r.HerdSettlement,
            Vehicle = new EntityId(r.Vehicle), LastDay = r.LastDay,
        }), NextAnimal);
        b.Vehicles.ClearForRestore(NextVehicle);
        var cat = b.Templates;
        // the section's part table onto the content's (by id): a part the content no longer has refuses the load, as a removed recipe in use does
        var partMap = new ushort[PartTable.Length];
        for (int i = 0; i < PartTable.Length; i++)
        {
            int pi = cat.PartIndex(PartTable[i]);
            if (pi < 0) throw new SaveException($"This save has a vehicle part '{PartTable[i]}' the game content no longer has.");
            partMap[i] = (ushort)pi;
        }
        foreach (var r in Vehicles)
        {
            string design = r.Design.Length > 0 ? r.Design : r.Template >= 0 && r.Template < LegacyTemplates.Length ? LegacyTemplates[r.Template] : "";
            var bp = cat.Find(design) ?? throw new SaveException($"This save has a vehicle of the design '{design}', which neither the content nor the save's designs have.");
            var v = new VehicleRow
            {
                Id = new EntityId(r.Id), Template = bp.Index, Design = bp.Id, Owner = new EntityId(r.Owner), XMm = r.XMm, YMm = r.YMm, Facing = r.Facing, Team = [.. r.Team.Select(t => new EntityId(t))], OdometerMm = r.OdometerMm,
                Stuck = r.Stuck, StuckSinceSec = r.StuckSinceSec, DugUntilSec = r.DugUntilSec, WearAccMm = r.WearAccMm, Driven = r.Driven, Water = (WaterState)r.Water, WaterInL = r.WaterInL,
            };
            if (r.PartState.Length == 0)
            {
                // a save from before part rows (v7 or v8 before P5-G): the row expands from its template, every part built, each wheel at the old WheelPm (vehicle-designer 3.3)
                v.Parts = [.. bp.Parts];
                b.Vehicles.Restore(v);
                v.WheelPm = r.WheelPm;
                if (bp.Stats.Boat) v.Water = WaterState.Afloat;
            }
            else
            {
                for (int i = 0; i < r.PartState.Length; i++)
                    v.Parts.Add(new PartRow { X = r.PartX[i], Y = r.PartY[i], Deck = r.PartDeck[i], Order = r.PartOrder[i], Part = partMap[r.PartIdx[i]], State = (PartState)r.PartState[i], Tier = (ItemQuality)r.PartTier[i], HpMilli = r.PartHp[i], WorkMilli = r.PartWork[i] });
                b.Vehicles.Restore(v);
            }
            v.Cargo = b.Vehicles.NewStore(v.Id, bp.Stats);
            if (!b.Hosted) v.Cargo.LoadRows(r.CargoGood, r.CargoQty);
        }
        if (Works != null) Works.Restore(b.Works);
        if (!b.Hosted) b.Heap.LoadRows(HeapGood, HeapQty);
        for (int i = 0; i < HerdHead.Length; i++) b.Herds.Set(HerdSettlement[i], HerdSpecies[i], HerdHead[i]);
        for (int i = 0; i < LoanAnimal.Length; i++) b.Loans.Add(new HorseLoan(new EntityId(LoanAnimal[i]), new EntityId(LoanLender[i]), LoanDue[i]));
        b.Mounted = new EntityId(Mounted); b.Driving = new EntityId(Driving); b.MountTarget = new EntityId(MountTarget); b.MountLeftGs = MountLeftGs; b.DismountLeftGs = DismountLeftGs;
        b.Want = (Gait)Want; b.HeroXMm = HeroXMm; b.HeroYMm = HeroYMm; b.RidingUses = RidingUses;
        return b;
    }
}
