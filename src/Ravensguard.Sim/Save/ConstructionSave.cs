using MessagePack;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;

namespace Ravensguard.Sim.Save;

/// <summary>
/// The optional "construction" section (P5-B, build-mode-and-construction 3.2, 3.3; overview 7: no format bump, absent while the book is empty so a world that never built keeps its bytes):
/// sites with their crews, ghosts with their progress, the built cells (piece, hit points, skill) with the RoomWorld structure ids and counter, pending groans, stores standing on cells,
/// the engine's clock and the construction's own mass counters. Rooms and facts are rebuilt from the cells at load. The goods of piles and ghosts are economy stores (the "economy"
/// section); the escrow chests are ledger purses.
/// </summary>
[MessagePackObject]
public sealed class ConstructionSave
{
    public const string SectionName = "construction";

    [Key(0)] public ulong NextSerial { get; set; } = 1;
    [Key(1)] public long AdvancedSec { get; set; } = -1;
    [Key(2)] public long HeroWorkedTo { get; set; } = -1;
    [Key(3)] public int NextStructureId { get; set; } = 1;
    [Key(4)] public SiteRow[] Sites { get; set; } = [];
    [Key(5)] public GhostRow[] Ghosts { get; set; } = [];
    [Key(6)] public BuiltRow[] Built { get; set; } = [];
    [Key(7)] public GroanRow[] Groans { get; set; } = [];
    /// <summary>Owner, x, y, z per placed store.</summary>
    [Key(8)] public ulong[] PlacedOwner { get; set; } = [];
    [Key(9)] public int[] PlacedXyz { get; set; } = [];
    [Key(10)] public int[] StructureIds { get; set; } = [];
    [Key(11)] public long[][] StructureCells { get; set; } = [];
    [Key(12)] public byte[] HeroSkill { get; set; } = [];
    [Key(13)] public long[] HeroSkillSec { get; set; } = [];
    [Key(14)] public ushort[] MatGood { get; set; } = [];
    [Key(15)] public long[] MatG { get; set; } = [];
    [Key(16)] public ushort[] DematGood { get; set; } = [];
    [Key(17)] public long[] DematG { get; set; } = [];
    [Key(18)] public long[] Improved { get; set; } = [];

    [MessagePackObject]
    public sealed class SiteRow
    {
        [Key(0)] public ulong Id { get; set; }
        [Key(1)] public int Region { get; set; }
        [Key(2)] public ulong Owner { get; set; }
        [Key(3)] public byte OwnerKind { get; set; }
        [Key(4)] public ulong Payer { get; set; }
        [Key(5)] public byte PayerKind { get; set; }
        [Key(6)] public ulong Deed { get; set; }
        [Key(7)] public int[] Pile { get; set; } = [];
        [Key(8)] public string Blueprint { get; set; } = "";
        [Key(9)] public byte State { get; set; }
        [Key(10)] public byte Pause { get; set; }
        [Key(11)] public byte Priority { get; set; }
        [Key(12)] public long CreatedSec { get; set; }
        [Key(13)] public long EscrowDay { get; set; }
        [Key(14)] public long PaidMites { get; set; }
        [Key(15)] public long RefundedMites { get; set; }
        [Key(16)] public int GhostsPlaced { get; set; }
        [Key(17)] public int PiecesBuilt { get; set; }
        [Key(18)] public CrewRow[] Crew { get; set; } = [];
    }

    [MessagePackObject]
    public sealed class CrewRow
    {
        [Key(0)] public ulong Person { get; set; }
        [Key(1)] public int PersonIndex { get; set; }
        [Key(2)] public byte Class { get; set; }
        [Key(3)] public long Wage { get; set; }
        [Key(4)] public ulong Ghost { get; set; }
        [Key(5)] public ulong Job { get; set; }
        [Key(6)] public long WorkedTodaySec { get; set; }
        [Key(7)] public long SkillSec { get; set; }
        [Key(8)] public int ShortDays { get; set; }
        [Key(9)] public long PaidMites { get; set; }
    }

    [MessagePackObject]
    public sealed class GhostRow
    {
        [Key(0)] public ulong Id { get; set; }
        [Key(1)] public ulong Site { get; set; }
        [Key(2)] public int X { get; set; }
        [Key(3)] public int Y { get; set; }
        [Key(4)] public int Z { get; set; }
        [Key(5)] public string Piece { get; set; } = "";
        /// <summary>Slot, rot, work, state, block, materialised, priority.</summary>
        [Key(6)] public byte[] Flags { get; set; } = [];
        [Key(7)] public long WorkDone { get; set; }
        [Key(8)] public long WorkNeed { get; set; }
        [Key(9)] public ulong Builder { get; set; }
        [Key(10)] public int BuiltSkill { get; set; }
        [Key(11)] public long PlacedSec { get; set; }
    }

    [MessagePackObject]
    public sealed class BuiltRow
    {
        [Key(0)] public int X { get; set; }
        [Key(1)] public int Y { get; set; }
        [Key(2)] public int Z { get; set; }
        [Key(3)] public byte Slot { get; set; }
        [Key(4)] public string Piece { get; set; } = "";
        [Key(5)] public int Hp { get; set; }
        [Key(6)] public int BuiltSkill { get; set; }
        [Key(7)] public ulong Site { get; set; }
        [Key(8)] public long BuiltSec { get; set; }
    }

    [MessagePackObject]
    public sealed class GroanRow
    {
        [Key(0)] public ulong EventId { get; set; }
        [Key(1)] public long StartSec { get; set; }
        [Key(2)] public long FallSec { get; set; }
        [Key(3)] public long[] Cells { get; set; } = [];
        [Key(4)] public ulong Cause { get; set; }
        [Key(5)] public string Why { get; set; } = "";
    }

    public static ConstructionSave Capture(ConstructionBook b) => new()
    {
        NextSerial = b.NextSerial, AdvancedSec = b.AdvancedSec, HeroWorkedTo = b.HeroWorkedTo, NextStructureId = b.NextStructureId,
        Sites = [.. b.Sites.Values.Select(s => new SiteRow
        {
            Id = s.Id.Value, Region = s.Region, Owner = s.Owner.Value, OwnerKind = (byte)s.OwnerKind, Payer = s.Payer.Value, PayerKind = (byte)s.PayerKind, Deed = s.Deed.Value,
            Pile = [s.PileX, s.PileY, s.PileZ], Blueprint = s.Blueprint, State = (byte)s.State, Pause = (byte)s.Pause, Priority = s.Priority, CreatedSec = s.CreatedSec, EscrowDay = s.EscrowDay,
            PaidMites = s.PaidMites, RefundedMites = s.RefundedMites, GhostsPlaced = s.GhostsPlaced, PiecesBuilt = s.PiecesBuilt,
            Crew = [.. s.Crew.Select(m => new CrewRow
            {
                Person = m.Person.Value, PersonIndex = m.PersonIndex, Class = (byte)m.Class, Wage = m.WageMitesPerDay, Ghost = m.Ghost.Value, Job = m.Job.Value, WorkedTodaySec = m.WorkedTodaySec,
                SkillSec = m.SkillSec, ShortDays = m.ShortDays, PaidMites = m.PaidMites,
            })],
        })],
        Ghosts = [.. b.Ghosts.Values.Select(g => new GhostRow
        {
            Id = g.Id.Value, Site = g.Site.Value, X = g.X, Y = g.Y, Z = g.Z, Piece = g.Piece,
            Flags = [(byte)g.Slot, g.Rot, (byte)g.Work, (byte)g.State, (byte)g.Block, (byte)(g.Materialised ? 1 : 0), g.Priority],
            WorkDone = g.WorkDone, WorkNeed = g.WorkNeed, Builder = g.Builder.Value, BuiltSkill = g.BuiltSkill, PlacedSec = g.PlacedSec,
        })],
        Built = [.. b.Built.Values.Select(c => new BuiltRow { X = c.X, Y = c.Y, Z = c.Z, Slot = (byte)c.Slot, Piece = c.Piece, Hp = c.Hp, BuiltSkill = c.BuiltSkill, Site = c.Site.Value, BuiltSec = c.BuiltSec })],
        Groans = [.. b.Groans.Select(g => new GroanRow { EventId = g.EventId, StartSec = g.StartSec, FallSec = g.FallSec, Cells = g.Cells, Cause = g.Cause.Value, Why = g.Why })],
        PlacedOwner = [.. b.Placed.Values.Select(p => p.Owner.Value)],
        PlacedXyz = [.. b.Placed.Values.SelectMany(p => new[] { p.X, p.Y, p.Z })],
        StructureIds = [.. b.Structures.Select(s => s.Id)],
        StructureCells = [.. b.Structures.Select(s => s.Cells)],
        HeroSkill = [.. b.HeroSkillSec.Keys], HeroSkillSec = [.. b.HeroSkillSec.Values],
        MatGood = [.. b.MaterialisedG.Keys], MatG = [.. b.MaterialisedG.Values],
        DematGood = [.. b.DematerialisedG.Keys], DematG = [.. b.DematerialisedG.Values],
        Improved = [.. b.Improved],
    };

    public ConstructionBook Restore()
    {
        var b = new ConstructionBook { NextSerial = NextSerial, AdvancedSec = AdvancedSec, HeroWorkedTo = HeroWorkedTo, NextStructureId = NextStructureId };
        foreach (var r in Sites)
        {
            var s = new ConstructionSite
            {
                Id = new EntityId(r.Id), Region = r.Region, Owner = new EntityId(r.Owner), OwnerKind = (PurseKind)r.OwnerKind, Payer = new EntityId(r.Payer), PayerKind = (PurseKind)r.PayerKind,
                Deed = new EntityId(r.Deed), PileX = r.Pile.Length > 0 ? r.Pile[0] : 0, PileY = r.Pile.Length > 1 ? r.Pile[1] : 0, PileZ = r.Pile.Length > 2 ? r.Pile[2] : 0, Blueprint = r.Blueprint,
                State = (SiteState)r.State, Pause = (PauseReason)r.Pause, Priority = r.Priority, CreatedSec = r.CreatedSec, EscrowDay = r.EscrowDay, PaidMites = r.PaidMites,
                RefundedMites = r.RefundedMites, GhostsPlaced = r.GhostsPlaced, PiecesBuilt = r.PiecesBuilt,
            };
            foreach (var c in r.Crew)
                s.Crew.Add(new CrewMember
                {
                    Person = new EntityId(c.Person), PersonIndex = c.PersonIndex, Class = (CrewClass)c.Class, WageMitesPerDay = c.Wage, Ghost = new EntityId(c.Ghost), Job = new EntityId(c.Job),
                    WorkedTodaySec = c.WorkedTodaySec, SkillSec = c.SkillSec, ShortDays = c.ShortDays, PaidMites = c.PaidMites,
                });
            b.Sites[s.Id] = s;
        }
        foreach (var r in Ghosts)
        {
            byte F(int i) => i < r.Flags.Length ? r.Flags[i] : (byte)0;
            var g = new Ghost
            {
                Id = new EntityId(r.Id), Site = new EntityId(r.Site), X = r.X, Y = r.Y, Z = r.Z, Piece = r.Piece, Slot = (GhostSlot)F(0), Rot = F(1), Work = (GhostWork)F(2), State = (GhostState)F(3),
                Block = (GhostBlock)F(4), Materialised = F(5) != 0, Priority = F(6), WorkDone = r.WorkDone, WorkNeed = r.WorkNeed, Builder = new EntityId(r.Builder), BuiltSkill = r.BuiltSkill, PlacedSec = r.PlacedSec,
            };
            b.Ghosts[g.Id] = g;
        }
        foreach (var r in Built)
        {
            var c = new BuiltCell { X = r.X, Y = r.Y, Z = r.Z, Slot = (GhostSlot)r.Slot, Piece = r.Piece, Hp = r.Hp, BuiltSkill = r.BuiltSkill, Site = new EntityId(r.Site), BuiltSec = r.BuiltSec };
            b.Built[c.SlotKey] = c;
        }
        foreach (var r in Groans) b.Groans.Add(new Groan { EventId = r.EventId, StartSec = r.StartSec, FallSec = r.FallSec, Cells = r.Cells, Cause = new EntityId(r.Cause), Why = r.Why });
        for (int i = 0; i < PlacedOwner.Length && i * 3 + 2 < PlacedXyz.Length; i++)
            b.Placed[new EntityId(PlacedOwner[i])] = new PlacedStore(new EntityId(PlacedOwner[i]), PlacedXyz[i * 3], PlacedXyz[i * 3 + 1], PlacedXyz[i * 3 + 2]);
        b.Structures = [.. StructureIds.Select((id, i) => (id, i < StructureCells.Length ? StructureCells[i] : []))];
        for (int i = 0; i < HeroSkill.Length && i < HeroSkillSec.Length; i++) b.HeroSkillSec[HeroSkill[i]] = HeroSkillSec[i];
        for (int i = 0; i < MatGood.Length && i < MatG.Length; i++) b.MaterialisedG[MatGood[i]] = MatG[i];
        for (int i = 0; i < DematGood.Length && i < DematG.Length; i++) b.DematerialisedG[DematGood[i]] = DematG[i];
        foreach (var k in Improved) b.Improved.Add(k);
        return b;
    }
}
