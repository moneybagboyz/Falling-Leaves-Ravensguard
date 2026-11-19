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
        return s;
    }
}
