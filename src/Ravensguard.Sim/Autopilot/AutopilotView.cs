using Ravensguard.Sim.Core;
using Ravensguard.Sim.Travel;

namespace Ravensguard.Sim.Autopilot;

/// <summary>What the panel and the HUD chip show: a copy of the person's autopilot row, safe to read off the tick worker (autopilot spec 6).</summary>
public sealed record AutopilotView(
    bool Exists, bool Enabled, bool Paused, bool AlertPaused, string AlertText, string Active, int ActiveIdx, IReadOnlyList<string> Queue, long SpentToday, long CapPerDay, long Coin,
    LimitSet Limits, InterruptPolicy[] Policy, bool[] Quiet, IReadOnlyList<string> LogTail, string Warning)
{
    /// <summary>Game second until which a Notify holds the clock at 4x (the game reads it each frame).</summary>
    public long SlowUntilSec { get; init; }
    /// <summary>The region-millimetre point the autopilot wants the person to walk to now (the game moves the hero), if any.</summary>
    public (long X, long Y)? Wanted { get; init; }
    public string Alert { get; init; } = "";

    public static readonly AutopilotView None = new(false, false, false, false, "", "", -1, [], 0, -1, 0, new LimitSet(), AutopilotState.DefaultPolicies(), new bool[InterruptBus.KindCount], [], "");

    /// <summary>The HUD chip text: <c>Auto: Work at the smithy | Spent 120/300</c> (spec 6).</summary>
    public string Chip(string offText = "Auto: off")
    {
        if (!Enabled) return offText;
        string cap = CapPerDay >= 0 ? $"{SpentToday}/{CapPerDay}" : $"{SpentToday}";
        return $"Auto: {(Active.Length > 0 ? Active : "live like a neighbour")}{(Paused ? " (paused)" : "")} | Spent {cap}";
    }
}

/// <summary>What the player sets in the panel before pressing Start: the queue, the limits and the policy table (a copy; nothing here touches the row until <see cref="AutopilotWorld.Apply"/>).</summary>
public sealed class AutopilotSetup
{
    public List<Directive> Queue = [];
    public LimitSet Limits = new();
    public InterruptPolicy[] Policy = AutopilotState.DefaultPolicies();
    public bool[] Quiet = new bool[InterruptBus.KindCount];

    public static AutopilotSetup Of(AutopilotState st) => new() { Queue = [.. st.Queue.Select(d => d.Clone())], Limits = st.Limits.Clone(), Policy = (InterruptPolicy[])st.Policy.Clone(), Quiet = (bool[])st.Quiet.Clone() };

    /// <summary>The presets of the panel (spec 6): Cautious (a small cap, low risk, home by nightfall), Worker (the defaults) and Watch the world (everything Notify or Ai).</summary>
    public void Preset(string name, AutopilotSettingsDefLike s)
    {
        switch (name)
        {
            case "cautious": Limits.SpendCapMitesPerDay = 100; Limits.MaxRiskPm = s.RiskLowPm; Limits.HomeByNightfall = true; Limits.NoCrimes = true; Policy = AutopilotState.DefaultPolicies(); Quiet = new bool[InterruptBus.KindCount]; break;
            case "worker": Limits.SpendCapMitesPerDay = s.DefaultCap; Limits.MaxRiskPm = s.RiskMediumPm; Limits.HomeByNightfall = false; Limits.NoCrimes = true; Policy = AutopilotState.DefaultPolicies(); Quiet = new bool[InterruptBus.KindCount]; break;
            case "watch":
                Limits.MaxRiskPm = s.RiskMediumPm; Limits.NoCrimes = true;
                for (int i = 0; i < Policy.Length; i++) { var k = (InterruptKind)i; Policy[i] = k is InterruptKind.Attacked or InterruptKind.Injured or InterruptKind.BigChoice ? InterruptPolicy.Pause : InterruptPolicy.Ai; Quiet[i] = false; }
                break;
        }
    }
}

/// <summary>The few numbers of the content a preset needs (so the setup does not hold the content type).</summary>
public readonly record struct AutopilotSettingsDefLike(int RiskLowPm, int RiskMediumPm, int RiskHighPm, int DefaultCap)
{
    public static AutopilotSettingsDefLike Of(Ravensguard.Content.AutopilotSettingsDef d) => new(d.RiskLowPm, d.RiskMediumPm, d.RiskHighPm, d.DefaultSpendCapMitesPerDay);
}

public sealed partial class AutopilotWorld
{
    /// <summary>A copy of the person's autopilot row for the panel and the HUD chip (a barrier job in the game, so it is read on the worker).</summary>
    public AutopilotView View(EntityId person, long now)
    {
        var st = Book.Get(person); if (st == null) return AutopilotView.None;
        int day = (int)(now / 86400); var bus = BusOf(st);
        var q = st.Queue.Select(d => d.State == DirectiveState.Done ? d.Name + " (done)" : d.State == DirectiveState.Blocked ? d.Name + " (blocked)" : d.Name).ToList();
        var tail = Book.Log.Lines.Where(l => l.Person == person).TakeLast(8).Select(l => $"{l.Sec % 86400 / 3600:00}:{l.Sec % 3600 / 60:00} {l.Text}").ToList();
        long coin = CoinOf(st);
        return new AutopilotView(true, st.Enabled, st.Paused, bus.Paused, bus.PausedBy?.Text ?? "", st.Active?.Name ?? "", st.ActiveIdx, q, LimitGuard.SpentOn(st, day), st.Limits.SpendCapMitesPerDay, coin,
            st.Limits.Clone(), (InterruptPolicy[])st.Policy.Clone(), (bool[])st.Quiet.Clone(), tail, Warning(st)) { SlowUntilSec = bus.NotifySlowUntilSec, Wanted = Wanted.TryGetValue(person, out var w) ? w : null };
    }

    /// <summary>"It cannot buy food above 3 crowns a day" (spec 6): what the AI will not do given the limits, one line.</summary>
    public string Warning(AutopilotState st)
    {
        var l = st.Limits; var parts = new List<string>();
        if (l.SpendCapMitesPerDay >= 0) parts.Add($"spend more than {Ravensguard.Sim.Trade.Quotes.Money(l.SpendCapMitesPerDay)} a day");
        if (l.PurseReserveMites > 0) parts.Add($"go below {Ravensguard.Sim.Trade.Quotes.Money(l.PurseReserveMites)} in the purse");
        if (l.NoCrimes) parts.Add("commit a crime");
        parts.Add(l.MaxRiskPm <= Settings.RiskLowPm ? "go anywhere dangerous" : l.MaxRiskPm <= Settings.RiskMediumPm ? "take a risky road" : "take the worst roads without an escort");
        if (l.HomeByNightfall) parts.Add("be away after dark");
        parts.Add("accept a marriage, an oath or a bribe");
        return "It will not " + string.Join(", ", parts) + ".";
    }

    /// <summary>Sets the person's queue, limits and policy table from the panel and turns autopilot on (a changed total budget starts again from zero spent).</summary>
    public AutopilotState Apply(EntityId person, long sec, AutopilotSetup s, bool enable = true)
    {
        var st = Book.GetOrAdd(person, Cfg);
        bool capChanged = st.Limits.SpendCapTotalMites != s.Limits.SpendCapTotalMites;
        st.Limits = s.Limits.Clone(); st.Policy = (InterruptPolicy[])s.Policy.Clone(); st.Quiet = (bool[])s.Quiet.Clone();
        if (capChanged) st.SpentTotal = 0;
        st.Queue = [.. s.Queue.Select(d => d.Clone())]; st.ActiveIdx = Math.Max(0, st.Queue.FindIndex(d => d.State is DirectiveState.Pending or DirectiveState.Active));
        if (st.Queue.Count > 0 && st.Queue.All(d => d.State is DirectiveState.Done or DirectiveState.Blocked or DirectiveState.Failed)) foreach (var d in st.Queue) { d.State = DirectiveState.Pending; d.StartDay = -1; st.ActiveIdx = 0; }
        if (enable) { bool was = st.Enabled; st.Enabled = true; st.Paused = false; st.NextWindow = sec / WindowSec; if (!was) { Book.Log.Add(sec, person, LogCode.Start, "Autopilot on" + (st.Active != null ? ": " + st.Active.Name : "")); WriteChronicle(person, "autopilot_start", sec, new Dictionary<string, string> { ["autopilot"] = "1", ["directive"] = st.Active?.Name ?? "live like a neighbour" }); } }
        return st;
    }
}
