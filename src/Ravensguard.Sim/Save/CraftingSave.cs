using MessagePack;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Crafting;

namespace Ravensguard.Sim.Save;

/// <summary>One unfinished job as saved: every field, in id order. The components are in the job's store, which the economy section saves.</summary>
[MessagePackObject]
public sealed class CraftJobRow
{
    [Key(0)] public ulong Id { get; set; }
    [Key(1)] public ushort Recipe { get; set; }
    [Key(2)] public ulong Crafter { get; set; }
    [Key(3)] public ulong Owner { get; set; }
    [Key(4)] public long PlaceX { get; set; }
    [Key(5)] public long PlaceY { get; set; }
    [Key(6)] public int Batch { get; set; }
    [Key(7)] public int StepIndex { get; set; }
    [Key(8)] public long StepSeconds { get; set; }
    [Key(9)] public long TotalSeconds { get; set; }
    [Key(10)] public byte State { get; set; }
    [Key(11)] public long StartedSec { get; set; }
    [Key(12)] public long ReadySec { get; set; }
    [Key(13)] public long DeadlineSec { get; set; }
    [Key(14)] public long RuinSec { get; set; }
    [Key(15)] public byte[] Choice { get; set; } = [];
    [Key(16)] public int ProfPm { get; set; }
    [Key(17)] public int ToolPm { get; set; }
    [Key(18)] public int SkillPm { get; set; }
    [Key(19)] public int EffSkill { get; set; }
    [Key(20)] public int ToolBonus { get; set; }
    [Key(21)] public int LightPenalty { get; set; }
    [Key(22)] public int TierDrop { get; set; }
    [Key(23)] public int WastePct { get; set; }
    [Key(24)] public bool Failed { get; set; }
    [Key(25)] public ulong Target { get; set; }
    [Key(26)] public byte AlertMask { get; set; }
    [Key(27)] public bool Rained { get; set; }
    // P5-E (crafting-full): additive keys (no version bump)
    [Key(28)] public uint SkipMask { get; set; }
    [Key(29)] public long FetchLeft { get; set; }
    [Key(30)] public int StationBonus { get; set; }
    [Key(31)] public int MaterialBonus { get; set; }
    [Key(32)] public int MasteryBonus { get; set; }
    [Key(33)] public int KitPenalty { get; set; }
    [Key(34)] public int ProcessBonus { get; set; }
    [Key(35)] public ulong Business { get; set; }
    /// <summary>The business station's index + 1 (0: not station-bound), so an older row reads as not bound.</summary>
    [Key(36)] public int StationSlot { get; set; }
    [Key(37)] public ulong Commission { get; set; }
    [Key(38)] public int FitTo { get; set; }
}

/// <summary>P5-E (crafting-full 3.4): a commission as saved (additive key 25 of the section).</summary>
[MessagePackObject]
public sealed class CommissionRow
{
    [Key(0)] public ulong Id { get; set; }
    [Key(1)] public ulong Client { get; set; }
    [Key(2)] public ulong Maker { get; set; }
    [Key(3)] public ulong Business { get; set; }
    [Key(4)] public ushort Recipe { get; set; }
    [Key(5)] public byte[] Choice { get; set; } = [];
    [Key(6)] public int Batch { get; set; }
    [Key(7)] public bool MaterialsFromClient { get; set; }
    [Key(8)] public long Price { get; set; }
    [Key(9)] public long Escrow { get; set; }
    [Key(10)] public long Labour { get; set; }
    [Key(11)] public long Materials { get; set; }
    [Key(12)] public long ClientMaterials { get; set; }
    [Key(13)] public long QueuedAtHour { get; set; }
    [Key(14)] public long DueHour { get; set; }
    [Key(15)] public long ReadyHour { get; set; }
    [Key(16)] public long WorkSec { get; set; }
    [Key(17)] public long AheadSec { get; set; }
    [Key(18)] public byte State { get; set; }
    [Key(19)] public ulong Job { get; set; }
    [Key(20)] public int FitTo { get; set; }
    [Key(21)] public ulong[] Items { get; set; } = [];
}

[MessagePackObject]
public sealed class ApprenticeRow
{
    [Key(0)] public ulong Id { get; set; }
    [Key(1)] public ulong Apprentice { get; set; }
    [Key(2)] public ulong Master { get; set; }
    [Key(3)] public byte Trade { get; set; }
    [Key(4)] public int StartDay { get; set; }
    [Key(5)] public int EndDay { get; set; }
    [Key(6)] public int Cap { get; set; }
    [Key(7)] public int DaysWorked { get; set; }
    [Key(8)] public ushort[] Learnt { get; set; } = [];
    [Key(9)] public bool Ended { get; set; }
}

[MessagePackObject]
public sealed class GuildMemberRow
{
    [Key(0)] public string Guild { get; set; } = "";
    [Key(1)] public int Settlement { get; set; }
    [Key(2)] public ulong Person { get; set; }
    [Key(3)] public int JoinedDay { get; set; }
    [Key(4)] public int PaidSeason { get; set; }
    [Key(5)] public int Arrears { get; set; }
}

[MessagePackObject]
public sealed class GuildOffenceRow
{
    [Key(0)] public ulong Id { get; set; }
    [Key(1)] public string Guild { get; set; } = "";
    [Key(2)] public int Settlement { get; set; }
    [Key(3)] public ulong Offender { get; set; }
    [Key(4)] public string Kind { get; set; } = "";
    [Key(5)] public ushort Recipe { get; set; }
    [Key(6)] public long AtSec { get; set; }
    [Key(7)] public ulong[] Witnesses { get; set; } = [];
}

[MessagePackObject]
public sealed class FireRow
{
    [Key(0)] public ulong Id { get; set; }
    [Key(1)] public ulong Owner { get; set; }
    [Key(2)] public long X { get; set; }
    [Key(3)] public long Y { get; set; }
    [Key(4)] public long LitSec { get; set; }
    [Key(5)] public long BurnedToSec { get; set; }
    [Key(6)] public bool Out { get; set; }
}

[MessagePackObject]
public sealed class QueuedCraftRow
{
    [Key(0)] public ulong Id { get; set; }
    [Key(1)] public ulong Business { get; set; }
    [Key(2)] public int Station { get; set; }
    [Key(3)] public ushort Recipe { get; set; }
    [Key(4)] public int Batch { get; set; }
    [Key(5)] public long QueuedAtHour { get; set; }
    [Key(6)] public ulong Commission { get; set; }
}


/// <summary>A crafted item's record: the recipe and what each unit consumed (good, quantity pairs flattened).</summary>
[MessagePackObject]
public sealed class CraftRecordRow
{
    [Key(0)] public ulong Item { get; set; }
    [Key(1)] public ushort Recipe { get; set; }
    [Key(2)] public ushort[] Goods { get; set; } = [];
    [Key(3)] public long[] Qty { get; set; } = [];
}

[MessagePackObject]
public sealed class TeachRow
{
    [Key(0)] public ulong Student { get; set; }
    [Key(1)] public ulong Master { get; set; }
    [Key(2)] public ushort Recipe { get; set; }
    [Key(3)] public long NeededSec { get; set; }
    [Key(4)] public long DoneSec { get; set; }
    [Key(5)] public long Cost { get; set; }
    [Key(6)] public bool ByLabour { get; set; }
    [Key(7)] public bool Paid { get; set; }
}

/// <summary>
/// The "crafting" section (basic-crafting spec 3.3): the unfinished jobs, the skills by use (micro-levels and the hour's use counters), what each person knows (recipes, proficiencies, watching), the record of
/// what crafted items were made of, and the teaching sessions. Absent in a world nobody has crafted in, so older saves keep their bytes. Nothing here is a function of anything but these rows and the clock:
/// the unattended deadlines are stored as times, the due queue is rebuilt, so a save made while the dough rises loads and saves again to the same bytes.
/// </summary>
[MessagePackObject]
public sealed class CraftingSave
{
    [Key(0)] public int Version { get; set; } = 1;
    [Key(1)] public List<CraftJobRow> Jobs { get; set; } = [];
    [Key(2)] public List<CraftRecordRow> Records { get; set; } = [];
    [Key(3)] public ulong[] SkillPerson { get; set; } = [];
    [Key(4)] public byte[] SkillId { get; set; } = [];
    [Key(5)] public long[] SkillMicro { get; set; } = [];
    [Key(6)] public ulong[] UsePerson { get; set; } = [];
    [Key(7)] public byte[] UseSkill { get; set; } = [];
    [Key(8)] public long[] UseHour { get; set; } = [];
    [Key(9)] public int[] UseCount { get; set; } = [];
    [Key(10)] public ulong[] RecipePerson { get; set; } = [];
    [Key(11)] public ushort[] RecipeId { get; set; } = [];
    [Key(12)] public ulong[] ProfPerson { get; set; } = [];
    [Key(13)] public ushort[] ProfId { get; set; } = [];
    [Key(14)] public long[] ProfRaw { get; set; } = [];
    [Key(15)] public ulong[] WatchPerson { get; set; } = [];
    [Key(16)] public ushort[] WatchRecipe { get; set; } = [];
    [Key(17)] public long[] WatchSec { get; set; } = [];
    [Key(18)] public int[] WatchTries { get; set; } = [];
    [Key(19)] public List<TeachRow> Sessions { get; set; } = [];
    // P5-A1 (save v8): the named artefacts, by item id ascending
    [Key(20)] public ulong[] ArtefactItem { get; set; } = [];
    [Key(21)] public string[] ArtefactName { get; set; } = [];
    [Key(22)] public ulong[] ArtefactMaker { get; set; } = [];
    [Key(23)] public string[] ArtefactMakerName { get; set; } = [];
    [Key(24)] public long[] ArtefactSec { get; set; } = [];
    // P5-E (crafting-full 3.4, 4.1 to 4.8): additive keys, in id order
    [Key(25)] public List<CommissionRow> Commissions { get; set; } = [];
    [Key(26)] public List<ApprenticeRow> Apprenticeships { get; set; } = [];
    [Key(27)] public List<GuildMemberRow> Members { get; set; } = [];
    [Key(28)] public List<GuildOffenceRow> Offences { get; set; } = [];
    [Key(29)] public ulong[] ResidencePerson { get; set; } = [];
    [Key(30)] public int[] ResidenceSettlement { get; set; } = [];
    [Key(31)] public int[] ResidenceSince { get; set; } = [];
    [Key(32)] public List<FireRow> Fires { get; set; } = [];
    [Key(33)] public long[] ForgeX { get; set; } = [];
    [Key(34)] public long[] ForgeY { get; set; } = [];
    [Key(35)] public long[] ForgeUntil { get; set; } = [];
    [Key(36)] public ulong[] FitItem { get; set; } = [];
    [Key(37)] public int[] FitCm3 { get; set; } = [];
    [Key(38)] public ulong[] OilItem { get; set; } = [];
    [Key(39)] public int[] OilUntil { get; set; } = [];
    [Key(40)] public List<QueuedCraftRow> Queue { get; set; } = [];
    [Key(41)] public ulong[] Dead { get; set; } = [];

    public static CraftingSave Capture(CraftState s)
    {
        var o = new CraftingSave();
        foreach (var j in s.Jobs.Values)
            o.Jobs.Add(new CraftJobRow
            {
                Id = j.Id.Value, Recipe = j.Recipe, Crafter = j.Crafter.Value, Owner = j.Owner.Value, PlaceX = j.PlaceX, PlaceY = j.PlaceY, Batch = j.Batch, StepIndex = j.StepIndex, StepSeconds = j.StepSeconds,
                TotalSeconds = j.TotalSeconds, State = (byte)j.State, StartedSec = j.StartedSec, ReadySec = j.ReadySec, DeadlineSec = j.DeadlineSec, RuinSec = j.RuinSec, Choice = (byte[])j.Choice.Clone(),
                ProfPm = j.ProfPm, ToolPm = j.ToolPm, SkillPm = j.SkillPm, EffSkill = j.EffSkill, ToolBonus = j.ToolBonus, LightPenalty = j.LightPenalty, TierDrop = j.TierDrop, WastePct = j.WastePct,
                Failed = j.Failed, Target = j.Target.Value, AlertMask = j.AlertMask, Rained = j.Rained,
                SkipMask = j.SkipMask, FetchLeft = j.FetchLeft, StationBonus = j.StationBonus, MaterialBonus = j.MaterialBonus, MasteryBonus = j.MasteryBonus, KitPenalty = j.KitPenalty,
                ProcessBonus = j.ProcessBonus, Business = j.Business.Value, StationSlot = j.StationIndex + 1, Commission = j.Commission.Value, FitTo = j.FitTo,
            });
        foreach (var (id, r) in s.Records)
            o.Records.Add(new CraftRecordRow { Item = id.Value, Recipe = r.Recipe, Goods = [.. r.Parts.Select(p => p.Good)], Qty = [.. r.Parts.Select(p => p.Qty)] });
        var mr = s.Book.MicroRows(); o.SkillPerson = [.. mr.Select(x => x.Person)]; o.SkillId = [.. mr.Select(x => x.Skill)]; o.SkillMicro = [.. mr.Select(x => x.Micro)];
        var ur = s.Book.UseRows(); o.UsePerson = [.. ur.Select(x => x.Person)]; o.UseSkill = [.. ur.Select(x => x.Skill)]; o.UseHour = [.. ur.Select(x => x.Hour)]; o.UseCount = [.. ur.Select(x => x.Uses)];
        var rr = s.Know.RecipeRows(); o.RecipePerson = [.. rr.Select(x => x.Person)]; o.RecipeId = [.. rr.Select(x => x.Recipe)];
        var pr = s.Know.ProfRows(); o.ProfPerson = [.. pr.Select(x => x.Person)]; o.ProfId = [.. pr.Select(x => x.Prof)]; o.ProfRaw = [.. pr.Select(x => x.Raw)];
        var wr = s.Know.WatchRows(); o.WatchPerson = [.. wr.Select(x => x.Person)]; o.WatchRecipe = [.. wr.Select(x => x.Recipe)]; o.WatchSec = [.. wr.Select(x => x.Sec)]; o.WatchTries = [.. wr.Select(x => x.Tries)];
        foreach (var t in s.Sessions)
            o.Sessions.Add(new TeachRow { Student = t.Student, Master = t.Master, Recipe = t.Recipe, NeededSec = t.NeededSec, DoneSec = t.DoneSec, Cost = t.CostMites, ByLabour = t.ByLabour, Paid = t.Paid });
        var named = s.Artefacts.Named;
        o.ArtefactItem = [.. named.Keys.Select(k => k.Value)]; o.ArtefactName = [.. named.Values.Select(a => a.Name)]; o.ArtefactMaker = [.. named.Values.Select(a => a.Maker.Value)];
        o.ArtefactMakerName = [.. named.Values.Select(a => a.MakerName)]; o.ArtefactSec = [.. named.Values.Select(a => a.MadeSec)];
        foreach (var c in s.Commissions.Values)
            o.Commissions.Add(new CommissionRow
            {
                Id = c.Id.Value, Client = c.Client.Value, Maker = c.Maker.Value, Business = c.Business.Value, Recipe = c.Recipe, Choice = (byte[])c.Choice.Clone(), Batch = c.Batch, MaterialsFromClient = c.MaterialsFromClient,
                Price = c.PriceMites, Escrow = c.Escrow, Labour = c.LabourMites, Materials = c.MaterialsMites, ClientMaterials = c.ClientMaterialsMites, QueuedAtHour = c.QueuedAtHour, DueHour = c.DueHour,
                ReadyHour = c.ReadyHour, WorkSec = c.WorkSec, AheadSec = c.AheadSec, State = (byte)c.State, Job = c.Job.Value, FitTo = c.FitTo, Items = [.. c.Items.Select(i => i.Value)],
            });
        foreach (var a in s.Apprenticeships.Values)
            o.Apprenticeships.Add(new ApprenticeRow { Id = a.Id.Value, Apprentice = a.Apprentice.Value, Master = a.Master.Value, Trade = (byte)a.Trade, StartDay = a.StartDay, EndDay = a.EndDay, Cap = a.Cap,
                DaysWorked = a.DaysWorked, Learnt = [.. a.Learnt], Ended = a.Ended });
        foreach (var m in s.Members.Values)
            o.Members.Add(new GuildMemberRow { Guild = m.Guild, Settlement = m.Settlement, Person = m.Person.Value, JoinedDay = m.JoinedDay, PaidSeason = m.PaidSeason, Arrears = m.Arrears });
        foreach (var f in s.Offences)
            o.Offences.Add(new GuildOffenceRow { Id = f.Id.Value, Guild = f.Guild, Settlement = f.Settlement, Offender = f.Offender.Value, Kind = f.Kind, Recipe = f.Recipe, AtSec = f.AtSec, Witnesses = [.. f.Witnesses.Select(w => w.Value)] });
        o.ResidencePerson = [.. s.Residence.Keys]; o.ResidenceSettlement = [.. s.Residence.Values.Select(v => v.Settlement)]; o.ResidenceSince = [.. s.Residence.Values.Select(v => v.SinceDay)];
        foreach (var f in s.Fires.All.Values) o.Fires.Add(new FireRow { Id = f.Id.Value, Owner = f.Owner.Value, X = f.X, Y = f.Y, LitSec = f.LitSec, BurnedToSec = f.BurnedToSec, Out = f.Out });
        o.ForgeX = [.. s.ForgeHot.Keys.Select(k => k.X)]; o.ForgeY = [.. s.ForgeHot.Keys.Select(k => k.Y)]; o.ForgeUntil = [.. s.ForgeHot.Values];
        o.FitItem = [.. s.Fits.Keys.Select(k => k.Value)]; o.FitCm3 = [.. s.Fits.Values];
        o.OilItem = [.. s.OiledUntil.Keys.Select(k => k.Value)]; o.OilUntil = [.. s.OiledUntil.Values];
        o.Dead = [.. s.Dead.Select(d => d.Value)];
        foreach (var q in s.Queue) o.Queue.Add(new QueuedCraftRow { Id = q.Id.Value, Business = q.Business.Value, Station = q.StationIndex, Recipe = q.Recipe, Batch = q.Batch, QueuedAtHour = q.QueuedAtHour, Commission = q.Commission.Value });
        return o;
    }

    public CraftState Restore()
    {
        var s = new CraftState();
        foreach (var r in Jobs)
            s.Add(new CraftJob
            {
                Id = new EntityId(r.Id), Recipe = r.Recipe, Crafter = new EntityId(r.Crafter), Owner = new EntityId(r.Owner), PlaceX = r.PlaceX, PlaceY = r.PlaceY, Batch = r.Batch, StepIndex = r.StepIndex,
                StepSeconds = r.StepSeconds, TotalSeconds = r.TotalSeconds, State = (CraftJobState)r.State, StartedSec = r.StartedSec, ReadySec = r.ReadySec, DeadlineSec = r.DeadlineSec, RuinSec = r.RuinSec,
                Choice = (byte[])r.Choice.Clone(), ProfPm = r.ProfPm, ToolPm = r.ToolPm, SkillPm = r.SkillPm, EffSkill = r.EffSkill, ToolBonus = r.ToolBonus, LightPenalty = r.LightPenalty, TierDrop = r.TierDrop,
                WastePct = r.WastePct, Failed = r.Failed, Target = new EntityId(r.Target), AlertMask = r.AlertMask, Rained = r.Rained,
                SkipMask = r.SkipMask, FetchLeft = r.FetchLeft, StationBonus = r.StationBonus, MaterialBonus = r.MaterialBonus, MasteryBonus = r.MasteryBonus, KitPenalty = r.KitPenalty,
                ProcessBonus = r.ProcessBonus, Business = new EntityId(r.Business), StationIndex = r.StationSlot - 1, Commission = new EntityId(r.Commission), FitTo = r.FitTo,
            });
        foreach (var r in Records) s.Records[new EntityId(r.Item)] = new CraftedRecord(r.Recipe, [.. r.Goods.Select((g, i) => (g, r.Qty[i]))]);
        for (int i = 0; i < SkillMicro.Length; i++) s.Book.RestoreMicro(SkillPerson[i], SkillId[i], SkillMicro[i]);
        for (int i = 0; i < UseHour.Length; i++) s.Book.RestoreUse(UsePerson[i], UseSkill[i], UseHour[i], UseCount[i]);
        for (int i = 0; i < RecipeId.Length; i++) s.Know.RestoreRecipe(RecipePerson[i], RecipeId[i]);
        for (int i = 0; i < ProfRaw.Length; i++) s.Know.RestoreProf(ProfPerson[i], ProfId[i], ProfRaw[i]);
        for (int i = 0; i < WatchRecipe.Length; i++) s.Know.RestoreWatch(WatchPerson[i], WatchRecipe[i], WatchSec[i], WatchTries[i]);
        foreach (var t in Sessions) s.Sessions.Add(new TeachSession { Student = t.Student, Master = t.Master, Recipe = t.Recipe, NeededSec = t.NeededSec, DoneSec = t.DoneSec, CostMites = t.Cost, ByLabour = t.ByLabour, Paid = t.Paid });
        for (int i = 0; i < ArtefactItem.Length && i < ArtefactName.Length && i < ArtefactMaker.Length && i < ArtefactMakerName.Length && i < ArtefactSec.Length; i++)
            s.Artefacts.Named[new EntityId(ArtefactItem[i])] = new Ravensguard.Sim.Goods.Artefact(ArtefactName[i], new EntityId(ArtefactMaker[i]), ArtefactMakerName[i], ArtefactSec[i]);
        foreach (var c in Commissions ?? [])
            s.Commissions[new EntityId(c.Id)] = new Commission
            {
                Id = new EntityId(c.Id), Client = new EntityId(c.Client), Maker = new EntityId(c.Maker), Business = new EntityId(c.Business), Recipe = c.Recipe, Choice = (byte[])c.Choice.Clone(), Batch = c.Batch,
                MaterialsFromClient = c.MaterialsFromClient, PriceMites = c.Price, Escrow = c.Escrow, LabourMites = c.Labour, MaterialsMites = c.Materials, ClientMaterialsMites = c.ClientMaterials,
                QueuedAtHour = c.QueuedAtHour, DueHour = c.DueHour, ReadyHour = c.ReadyHour, WorkSec = c.WorkSec, AheadSec = c.AheadSec, State = (CommissionState)c.State, Job = new EntityId(c.Job), FitTo = c.FitTo,
                Items = [.. c.Items.Select(i => new EntityId(i))],
            };
        foreach (var a in Apprenticeships ?? [])
            s.Apprenticeships[new EntityId(a.Id)] = new Apprenticeship { Id = new EntityId(a.Id), Apprentice = new EntityId(a.Apprentice), Master = new EntityId(a.Master), Trade = (Ravensguard.Sim.People.Skill)a.Trade,
                StartDay = a.StartDay, EndDay = a.EndDay, Cap = a.Cap, DaysWorked = a.DaysWorked, Learnt = [.. a.Learnt], Ended = a.Ended };
        foreach (var m in Members ?? [])
            s.Members[(m.Guild, m.Settlement, m.Person)] = new GuildMember { Guild = m.Guild, Settlement = m.Settlement, Person = new EntityId(m.Person), JoinedDay = m.JoinedDay, PaidSeason = m.PaidSeason, Arrears = m.Arrears };
        foreach (var f in Offences ?? [])
            s.Offences.Add(new GuildOffence(new EntityId(f.Id), f.Guild, f.Settlement, new EntityId(f.Offender), f.Kind, f.Recipe, f.AtSec, [.. f.Witnesses.Select(w => new EntityId(w))]));
        for (int i = 0; i < ResidencePerson.Length && i < ResidenceSettlement.Length && i < ResidenceSince.Length; i++) s.Residence[ResidencePerson[i]] = (ResidenceSettlement[i], ResidenceSince[i]);
        foreach (var f in Fires ?? []) s.Fires.Restore(new Fire { Id = new EntityId(f.Id), Owner = new EntityId(f.Owner), X = f.X, Y = f.Y, LitSec = f.LitSec, BurnedToSec = f.BurnedToSec, Out = f.Out });
        for (int i = 0; i < ForgeX.Length && i < ForgeY.Length && i < ForgeUntil.Length; i++) s.ForgeHot[(ForgeX[i], ForgeY[i])] = ForgeUntil[i];
        for (int i = 0; i < FitItem.Length && i < FitCm3.Length; i++) s.Fits[new EntityId(FitItem[i])] = FitCm3[i];
        for (int i = 0; i < OilItem.Length && i < OilUntil.Length; i++) s.OiledUntil[new EntityId(OilItem[i])] = OilUntil[i];
        foreach (var d in Dead ?? []) s.Dead.Add(new EntityId(d));
        foreach (var q in Queue ?? [])
            s.Queue.Add(new QueuedCraft { Id = new EntityId(q.Id), Business = new EntityId(q.Business), StationIndex = q.Station, Recipe = q.Recipe, Batch = q.Batch, QueuedAtHour = q.QueuedAtHour, Commission = new EntityId(q.Commission) });
        return s;
    }
}
