using MessagePack;
using Ravensguard.Sim.Autopilot;
using Ravensguard.Sim.Behaviour;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Travel;

namespace Ravensguard.Sim.Save;

[MessagePackObject] public sealed class DirectiveRow
{
    [Key(0)] public byte Kind { get; set; } [Key(1)] public byte TargetKind { get; set; } [Key(2)] public long Target { get; set; } [Key(3)] public long[] Params { get; set; } = [];
    [Key(4)] public byte Until { get; set; } [Key(5)] public long UntilValue { get; set; } [Key(6)] public long UntilValue2 { get; set; } [Key(7)] public int StartDay { get; set; } [Key(8)] public byte State { get; set; }
    [Key(9)] public long StartCoin { get; set; } [Key(10)] public long Aux0 { get; set; } [Key(11)] public long Aux1 { get; set; } [Key(12)] public long Aux2 { get; set; } [Key(13)] public long Cost { get; set; } [Key(14)] public long Revenue { get; set; }
    [Key(15)] public string Name { get; set; } = ""; [Key(16)] public long Aux3 { get; set; }
}

[MessagePackObject] public sealed class LimitRow
{
    [Key(0)] public long CapDay { get; set; } [Key(1)] public long CapTotal { get; set; } [Key(2)] public long Reserve { get; set; } [Key(3)] public bool NoCrimes { get; set; } [Key(4)] public int MaxRisk { get; set; }
    [Key(5)] public bool Home { get; set; } [Key(6)] public long HomeX { get; set; } [Key(7)] public long HomeY { get; set; } [Key(8)] public bool NoLethal { get; set; } [Key(9)] public uint Accept { get; set; }
}

[MessagePackObject] public sealed class DayRow
{
    [Key(0)] public int Day { get; set; } [Key(1)] public long[] Sec { get; set; } = []; [Key(2)] public long Earned { get; set; } [Key(3)] public long Spent { get; set; } [Key(4)] public int Jobs { get; set; }
    [Key(5)] public int Interrupts { get; set; } [Key(6)] public int Meals { get; set; } [Key(7)] public long Distance { get; set; } [Key(8)] public long SkillMilli { get; set; } [Key(9)] public long Nutrition { get; set; }
}

[MessagePackObject] public sealed class AutopilotRow
{
    [Key(0)] public ulong Person { get; set; } [Key(1)] public bool Enabled { get; set; } [Key(2)] public bool Paused { get; set; } [Key(3)] public List<DirectiveRow> Queue { get; set; } = []; [Key(4)] public int ActiveIdx { get; set; }
    [Key(5)] public LimitRow Limits { get; set; } = new(); [Key(6)] public byte[] Policy { get; set; } = []; [Key(7)] public bool[] Quiet { get; set; } = []; [Key(8)] public int LastSummaryDay { get; set; }
    [Key(9)] public int SpentDay { get; set; } [Key(10)] public long SpentToday { get; set; } [Key(11)] public long SpentTotal { get; set; } [Key(12)] public long SessionLogStart { get; set; }
    [Key(13)] public long NextWindow { get; set; } [Key(14)] public byte Intent { get; set; } [Key(15)] public long IntentSince { get; set; } [Key(16)] public byte Step { get; set; } [Key(17)] public long StepSince { get; set; }
    [Key(18)] public long StepUntil { get; set; } [Key(19)] public long LastMeal { get; set; } [Key(20)] public long LastProgress { get; set; } [Key(21)] public long LastWorkUse { get; set; } [Key(22)] public long PosX { get; set; }
    [Key(23)] public long PosY { get; set; } [Key(24)] public int Health { get; set; } [Key(25)] public bool Bleeding { get; set; } [Key(26)] public int Threat { get; set; } [Key(27)] public long Nutrition { get; set; }
    [Key(28)] public int NutritionDay { get; set; } [Key(29)] public long CoinAtDayStart { get; set; } [Key(30)] public int Replan { get; set; } [Key(31)] public List<DayRow> Summaries { get; set; } = []; [Key(32)] public DayRow Today { get; set; } = new();
    [Key(33)] public ulong CurrentJob { get; set; } [Key(34)] public long[] LastFired { get; set; } = []; [Key(35)] public uint EdgeBits { get; set; } [Key(36)] public int LastHunger { get; set; } [Key(37)] public long CampUntil { get; set; }
    [Key(38)] public long LastShop { get; set; } [Key(39)] public int LimitDay { get; set; }
    /// <summary>P8-AT-2 (ambitions spec 4), optional keys, null while empty: the planner's running child directive and its means id, the learned rates (ids and pm, parallel) and the jobs done.</summary>
    [Key(40)] public DirectiveRow? Child { get; set; } [Key(41)] public string? ChildMeans { get; set; } [Key(42)] public string[]? RateIds { get; set; } [Key(43)] public int[]? RateValues { get; set; } [Key(44)] public int? JobsDone { get; set; }
}

[MessagePackObject] public sealed class LogLineRow { [Key(0)] public long Sec { get; set; } [Key(1)] public ulong Person { get; set; } [Key(2)] public byte Code { get; set; } [Key(3)] public byte Arg { get; set; } [Key(4)] public string Text { get; set; } = ""; }

/// <summary>
/// The "autopilot" section (autopilot-v1 spec 3.1, 9): the row of each person that has had autopilot (queue, limits, policy table, spent so far, the step they were on), the ring of log lines, the daily summaries and the counters.
/// Absent for a world nobody turned autopilot on in, so older saves keep their bytes. Rows and lines are in a fixed order, so save, load, save is byte-identical. At most 100 KB.
/// </summary>
[MessagePackObject]
public sealed class AutopilotSave
{
    public const string SectionName = "autopilot";
    [Key(0)] public int Version { get; set; } = 1;
    [Key(1)] public int StandIn { get; set; }
    [Key(2)] public long Summaries { get; set; } [Key(3)] public long BigDeclined { get; set; } [Key(4)] public long ContractsAccepted { get; set; } [Key(5)] public long ContractsDeclined { get; set; }
    [Key(6)] public long Violations { get; set; } [Key(7)] public long PlanViolations { get; set; } [Key(8)] public long Crimes { get; set; } [Key(9)] public long Added { get; set; }
    [Key(10)] public List<AutopilotRow> States { get; set; } = [];
    [Key(11)] public List<LogLineRow> Log { get; set; } = [];
    /// <summary>The spends booked against commands still waiting at the barrier (review finding 6); absent in older saves.</summary>
    [Key(12)] public long[] ReservedSeq { get; set; } = []; [Key(13)] public ulong[] ReservedPerson { get; set; } = []; [Key(14)] public int[] ReservedDay { get; set; } = []; [Key(15)] public long[] ReservedMites { get; set; } = [];

    private static DirectiveRow Row(Directive d) => new() { Kind = (byte)d.Kind, TargetKind = d.TargetKind, Target = d.Target, Params = [.. d.Params], Until = (byte)d.Until, UntilValue = d.UntilValue, UntilValue2 = d.UntilValue2, StartDay = d.StartDay, State = (byte)d.State, StartCoin = d.StartCoin, Aux0 = d.Aux0, Aux1 = d.Aux1, Aux2 = d.Aux2, Cost = d.Cost, Revenue = d.Revenue, Name = d.Name, Aux3 = d.Aux3 };
    private static Directive Of(DirectiveRow d) => new() { Kind = (DirectiveKind)d.Kind, TargetKind = d.TargetKind, Target = d.Target, Params = d.Params.Length == 4 ? d.Params : d.Params.Concat(new long[4]).Take(4).ToArray(), Until = (UntilKind)d.Until, UntilValue = d.UntilValue, UntilValue2 = d.UntilValue2, StartDay = d.StartDay, State = (DirectiveState)d.State, StartCoin = d.StartCoin, Aux0 = d.Aux0, Aux1 = d.Aux1, Aux2 = d.Aux2, Cost = d.Cost, Revenue = d.Revenue, Name = d.Name, Aux3 = d.Aux3 };

    private static DayRow Day(DaySummary d) => new() { Day = d.Day, Sec = [.. d.SecByAction], Earned = d.Earned, Spent = d.Spent, Jobs = d.JobsDone, Interrupts = d.Interrupts, Meals = d.Meals, Distance = d.DistanceM, SkillMilli = d.SkillGainMilli, Nutrition = d.NutritionCd };
    private static DaySummary Day(DayRow r) { var d = new DaySummary { Day = r.Day, Earned = r.Earned, Spent = r.Spent, JobsDone = r.Jobs, Interrupts = r.Interrupts, Meals = r.Meals, DistanceM = r.Distance, SkillGainMilli = r.SkillMilli, NutritionCd = r.Nutrition }; for (int i = 0; i < d.SecByAction.Length && i < r.Sec.Length; i++) d.SecByAction[i] = r.Sec[i]; return d; }

    public static AutopilotSave Capture(AutopilotBook b)
    {
        var o = new AutopilotSave { StandIn = b.StandIn, Summaries = b.SummariesMade, BigDeclined = b.BigChoicesDeclined, ContractsAccepted = b.ContractsAccepted, ContractsDeclined = b.ContractsDeclined, Violations = b.Log.Violations, PlanViolations = b.Log.PlanViolations, Crimes = b.Log.Crimes, Added = b.Log.Added };
        foreach (var (id, s) in b.States)
        {
            var l = s.Limits;
            o.States.Add(new AutopilotRow
            {
                Person = id.Value, Enabled = s.Enabled, Paused = s.Paused, ActiveIdx = s.ActiveIdx, LastSummaryDay = s.LastSummaryDay, SpentDay = s.SpentDay, SpentToday = s.SpentToday, SpentTotal = s.SpentTotal, SessionLogStart = s.SessionLogStart,
                NextWindow = s.NextWindow, Intent = (byte)s.Intent, IntentSince = s.IntentSince, Step = (byte)s.Step, StepSince = s.StepSince, StepUntil = s.StepUntil, LastMeal = s.LastMealSec, LastProgress = s.LastProgressSec, LastWorkUse = s.LastWorkUseSec,
                PosX = s.PosX, PosY = s.PosY, Health = s.HealthPm, Bleeding = s.Bleeding, Threat = s.ThreatPm, Nutrition = s.NutritionCd, NutritionDay = s.NutritionDay, CoinAtDayStart = s.CoinAtDayStart, Replan = s.ReplanCount,
                CurrentJob = s.CurrentJob.Value, LastFired = [.. s.LastFiredSec], EdgeBits = s.EdgeBits, LastHunger = s.LastHungerMilli, CampUntil = s.CampUntil, LastShop = s.LastShopSec, LimitDay = s.LimitDay,
                Policy = [.. s.Policy.Select(p => (byte)p)], Quiet = [.. s.Quiet],
                Limits = new LimitRow { CapDay = l.SpendCapMitesPerDay, CapTotal = l.SpendCapTotalMites, Reserve = l.PurseReserveMites, NoCrimes = l.NoCrimes, MaxRisk = l.MaxRiskPm, Home = l.HomeByNightfall, HomeX = l.HomeX, HomeY = l.HomeY, NoLethal = l.NoLethalForce, Accept = l.AcceptContractKinds },
                Queue = [.. s.Queue.Select(d => new DirectiveRow { Kind = (byte)d.Kind, TargetKind = d.TargetKind, Target = d.Target, Params = [.. d.Params], Until = (byte)d.Until, UntilValue = d.UntilValue, UntilValue2 = d.UntilValue2, StartDay = d.StartDay, State = (byte)d.State, StartCoin = d.StartCoin, Aux0 = d.Aux0, Aux1 = d.Aux1, Aux2 = d.Aux2, Cost = d.Cost, Revenue = d.Revenue, Name = d.Name, Aux3 = d.Aux3 })],
                Summaries = [.. s.Summaries.Select(Day)], Today = Day(s.Today),
                Child = s.Child == null ? null : Row(s.Child), ChildMeans = s.ChildMeans.Length == 0 ? null : s.ChildMeans,
                RateIds = s.Rates.Count == 0 ? null : [.. s.Rates.Keys], RateValues = s.Rates.Count == 0 ? null : [.. s.Rates.Values], JobsDone = s.JobsDoneTotal == 0 ? null : s.JobsDoneTotal,
            });
        }
        o.Log = [.. b.Log.Lines.Select(x => new LogLineRow { Sec = x.Sec, Person = x.Person.Value, Code = (byte)x.Code, Arg = x.Arg, Text = x.Text })];
        o.ReservedSeq = [.. b.Reserved.Keys]; o.ReservedPerson = [.. b.Reserved.Values.Select(v => v.Person.Value)]; o.ReservedDay = [.. b.Reserved.Values.Select(v => v.Day)]; o.ReservedMites = [.. b.Reserved.Values.Select(v => v.Mites)];
        return o;
    }

    public AutopilotBook Restore()
    {
        var b = new AutopilotBook { StandIn = StandIn, SummariesMade = Summaries, BigChoicesDeclined = BigDeclined, ContractsAccepted = ContractsAccepted, ContractsDeclined = ContractsDeclined };
        foreach (var r in States)
        {
            var s = new AutopilotState
            {
                Person = new EntityId(r.Person), Enabled = r.Enabled, Paused = r.Paused, ActiveIdx = r.ActiveIdx, LastSummaryDay = r.LastSummaryDay, SpentDay = r.SpentDay, SpentToday = r.SpentToday, SpentTotal = r.SpentTotal, SessionLogStart = r.SessionLogStart,
                NextWindow = r.NextWindow, Intent = (ActionKind)r.Intent, IntentSince = r.IntentSince, Step = (StepKind)r.Step, StepSince = r.StepSince, StepUntil = r.StepUntil, LastMealSec = r.LastMeal, LastProgressSec = r.LastProgress, LastWorkUseSec = r.LastWorkUse,
                PosX = r.PosX, PosY = r.PosY, HealthPm = r.Health, Bleeding = r.Bleeding, ThreatPm = r.Threat, NutritionCd = r.Nutrition, NutritionDay = r.NutritionDay, CoinAtDayStart = r.CoinAtDayStart, ReplanCount = r.Replan,
                CurrentJob = new EntityId(r.CurrentJob), EdgeBits = r.EdgeBits, LastHungerMilli = r.LastHunger, CampUntil = r.CampUntil, LastShopSec = r.LastShop, LimitDay = r.LimitDay,
            };
            for (int i = 0; i < s.Policy.Length && i < r.Policy.Length; i++) s.Policy[i] = (InterruptPolicy)r.Policy[i];
            for (int i = 0; i < s.Quiet.Length && i < r.Quiet.Length; i++) s.Quiet[i] = r.Quiet[i];
            for (int i = 0; i < s.LastFiredSec.Length && i < r.LastFired.Length; i++) s.LastFiredSec[i] = r.LastFired[i];
            var l = r.Limits;
            s.Limits = new LimitSet { SpendCapMitesPerDay = l.CapDay, SpendCapTotalMites = l.CapTotal, PurseReserveMites = l.Reserve, NoCrimes = l.NoCrimes, MaxRiskPm = l.MaxRisk, HomeByNightfall = l.Home, HomeX = l.HomeX, HomeY = l.HomeY, NoLethalForce = l.NoLethal, AcceptContractKinds = l.Accept };
            s.Queue = [.. r.Queue.Select(d => new Directive { Kind = (DirectiveKind)d.Kind, TargetKind = d.TargetKind, Target = d.Target, Params = d.Params.Length == 4 ? d.Params : d.Params.Concat(new long[4]).Take(4).ToArray(), Until = (UntilKind)d.Until, UntilValue = d.UntilValue, UntilValue2 = d.UntilValue2, StartDay = d.StartDay, State = (DirectiveState)d.State, StartCoin = d.StartCoin, Aux0 = d.Aux0, Aux1 = d.Aux1, Aux2 = d.Aux2, Cost = d.Cost, Revenue = d.Revenue, Name = d.Name, Aux3 = d.Aux3 })];
            s.Summaries = [.. r.Summaries.Select(Day)]; s.Today = Day(r.Today);
            if (r.Child != null) s.Child = Of(r.Child);
            s.ChildMeans = r.ChildMeans ?? ""; s.JobsDoneTotal = r.JobsDone ?? 0;
            if (r.RateIds != null && r.RateValues != null) for (int i = 0; i < r.RateIds.Length && i < r.RateValues.Length; i++) s.Rates[r.RateIds[i]] = r.RateValues[i];
            b.States[s.Person] = s;
        }
        b.Log.Restore(Log.Select(x => new LogLine(x.Sec, new EntityId(x.Person), (LogCode)x.Code, x.Arg, x.Text)), Violations, PlanViolations, Crimes, Added);
        for (int i = 0; i < ReservedSeq.Length && i < ReservedPerson.Length && i < ReservedDay.Length && i < ReservedMites.Length; i++) b.Reserved[ReservedSeq[i]] = (new EntityId(ReservedPerson[i]), ReservedDay[i], ReservedMites[i]);
        return b;
    }
}
