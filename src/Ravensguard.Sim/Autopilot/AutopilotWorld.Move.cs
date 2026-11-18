using Ravensguard.Sim.Behaviour;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Jobs;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.Travel;
using Ravensguard.Sim.Weather;

namespace Ravensguard.Sim.Autopilot;

public enum OfferResult : byte { Accepted, Declined, Pending }

/// <summary>A node of authored dialogue as autopilot content validation sees it (spec 4.4, AP-8): every <c>big</c> node needs a decline branch.</summary>
public readonly record struct BigNode(string Id, bool Big, bool HasDecline);

public static class AutopilotContentChecks
{
    /// <summary>Problems of the big-choice rule (empty when valid): a node flagged <c>big</c> without a decline branch fails content load.</summary>
    public static IEnumerable<string> BigChoiceProblems(IEnumerable<BigNode> nodes)
    {
        foreach (var n in nodes) if (n.Big && !n.HasDecline) yield return $"dialogue node '{n.Id}' is a big choice and has no decline branch";
    }
}

public sealed partial class AutopilotWorld
{
    /// <summary>Posts the day's jobs and advances the job table in step with the autopilot windows (a world with no running game around it: tests and the zero-player runner). The game's own barrier does this otherwise.</summary>
    public bool DrivesJobs { get; set; }
    private long _jobsClock = long.MinValue;

    private void StepJobs(long at)
    {
        var jt = Jobs?.Invoke(); if (jt == null) return;
        var (env, table) = jt.Value; long day = at / 86400;
        if (table.LastPostedDay != day)
        {
            table.LastPostedDay = day;
            for (int s = 0; s < State.Population.Region.Settlements.Count; s++) RoutineJobs.PostDay(table, env, State.Population, State.HomeRegion.Id, s, day, State.Seed);
        }
        table.Advance(env, at);
    }

    // ------------------------------------------------------------------ travel, trade routes, escorts

    private void DoMove(AutopilotState st, Directive? d, long at)
    {
        if (d == null) return;
        switch (d.Kind)
        {
            case DirectiveKind.Travel: DoTravel(st, d, at); break;
            case DirectiveKind.TradeRoute: DoTrade(st, d, at); break;
            case DirectiveKind.Escort: DoEscort(st, d, at); break;
        }
    }

    private int[] DangerAlong(long x0, long y0, long x1, long y1, long at)
    {
        long len = PathPlan.Dist(x1 - x0, y1 - y0); int n = (int)Math.Clamp(len / 500_000 + 2, 2, 64);
        var r = new int[n];
        for (int i = 0; i < n; i++) r[i] = DangerAt(x0 + (x1 - x0) * i / (n - 1), y0 + (y1 - y0) * i / (n - 1), at);
        return r;
    }

    private void DoTravel(AutopilotState st, Directive d, long at)
    {
        long tx = d.Params[0], ty = d.Params[1];
        var tr = Travel; var bus = BusOf(st);
        if (d.Aux0 == 0)
        {
            int day = (int)(at / 86400); bool night = IsNight(at);
            var danger = DangerAlong(st.PosX, st.PosY, tx, ty, at);
            var pick = LimitGuard.ChooseRoute(st.Limits, [new LimitGuard.RouteOption("direct", danger)], night);
            if (pick == null)
            {
                // later departure (day), else not at all today
                if (night && LimitGuard.ChooseRoute(st.Limits, [new LimitGuard.RouteOption("direct", danger)], false) != null) { st.CampUntil = at / 86400 * 86400 + Daylight.SunriseMinute(LatTenths, DoY(at + 86400)) * 60L + (at % 86400 < 43200 ? 0 : 86400); if (st.CampUntil <= at) st.CampUntil += 86400; if (st.LimitDay != day) { st.LimitDay = day; Raise(st, InterruptKind.LimitReached, at, "Too dangerous at night: leaving at dawn"); } }
                else { st.CampUntil = (at / 86400 + 1) * 86400; if (st.LimitDay != day) { st.LimitDay = day; Raise(st, InterruptKind.LimitReached, at, "The way is too dangerous for the chosen risk"); } }
                return;
            }
            if (st.Limits.HomeByNightfall)
            {
                double rt = LimitGuard.RoundTripMinutes(PathPlan.Dist(tx - st.Limits.HomeX, ty - st.Limits.HomeY) / 1000);
                if (!LimitGuard.HomeByNightfallOk(LatTenths, DoY(at), MinuteOfDay(at), rt, Settings.HomeByNightfallOffsetMin))
                { st.CampUntil = (at / 86400 + 1) * 86400; if (st.LimitDay != day) { st.LimitDay = day; Raise(st, InterruptKind.LimitReached, at, "Cannot be home by nightfall"); } return; }
            }
            var path = Planner?.Invoke(st.PosX, st.PosY, tx, ty) ?? new RoutePath([st.PosX, tx], [st.PosY, ty]);
            if (tr != null && TravelWorld != null) { tr.Begin(d.Name, path, at); _busSeen[st.Person] = bus.Log.Count; d.Aux0 = 1; }
            else d.Aux0 = 2;
            st.LastProgressSec = at;
        }
        if (d.Aux0 == 1 && tr?.Journey is { } j)
        {
            if (j.State is JourneyState.Arrived or JourneyState.Cancelled) { Ingest(st, bus, at); Arrived(st, d, at); return; }
            if (j.State == JourneyState.Paused && !bus.Paused) tr.Resume();
            if (j.Sec < at) j.Sec = at;                            // the time spent eating or asleep is not travelled
            long before = j.ProgressMm;
            tr.Advance(TravelWorld!, at + WindowSec);
            var (hx, hy) = tr.HeroPosition; Teleport(st, hx, hy);
            if (j.ProgressMm > before) st.LastProgressSec = at;
            Ingest(st, bus, at);
            if (j.State == JourneyState.Arrived) Arrived(st, d, at);
            return;
        }
        // no travel state around: the plain walk
        Walk(st, tx, ty, at);
        if (PathPlan.Dist(st.PosX - tx, st.PosY - ty) <= 3000) { Raise(st, InterruptKind.Arrived, at, "Arrived at " + d.Name); Arrived(st, d, at); }
        else st.LastProgressSec = at;
    }

    private void Arrived(AutopilotState st, Directive d, long at)
    {
        d.State = DirectiveState.Done; st.LastProgressSec = at;
        Book.Log.Add(at, st.Person, LogCode.Directive, $"Directive finished: {d.Name} (arrived)");
        Raise(st, InterruptKind.DirectiveDone, at, d.Name);
    }

    /// <summary>Reads the interrupts travel raised on the person's bus since the last look: the bus applied the policy; the router logs and chronicles and the brain answers.</summary>
    private void Ingest(AutopilotState st, InterruptBus bus, long at)
    {
        int seen = _busSeen.GetValueOrDefault(st.Person); if (seen > bus.Log.Count) seen = 0;
        for (int i = seen; i < bus.Log.Count; i++)
        {
            var rec = bus.Log[i];
            var resp = Book.Router.Observe(st, rec);
            switch (resp)
            {
                case AiResponse.MakeCamp: st.CampUntil = Math.Max(st.CampUntil, at + (rec.Kind == InterruptKind.Dusk ? 10 : 6) * 3600L); break;
                case AiResponse.ReplanOnce when rec.Kind == InterruptKind.BlockedPath && Travel is { } tr && tr.Journey is { } jn && st.Active is { Kind: DirectiveKind.Travel } d && st.ReplanCount++ < 1:
                    tr.Begin(d.Name, new RoutePath([st.PosX, d.Params[0]], [st.PosY, d.Params[1]]), at); break;
                case AiResponse.None: break;
                default: ApplyAi(st, resp, at); break;
            }
        }
        _busSeen[st.Person] = bus.Log.Count;
    }

    private void DoEscort(AutopilotState st, Directive d, long at)
    {
        var target = new EntityId((ulong)d.Target);
        if (PositionHook?.Invoke(target) is { } tp) { if (PathPlan.Dist(st.PosX - tp.X, st.PosY - tp.Y) > 30_000) Walk(st, tp.X, tp.Y, at); st.LastProgressSec = at; return; }
        st.LastProgressSec = at;
    }

    // ---- trade route (spec 4.2): buy at A, sell at B, replan each trip

    /// <summary>The margin of a trip in permille: what B pays (less the toll) over what A asks. Uses the hero's notebook, never the books themselves.</summary>
    public static int MarginPm(long askMicroA, long bidMicroB, int tollPm = 40) => askMicroA <= 0 ? -1000 : (int)(((bidMicroB * (1000 - tollPm) / 1000) - askMicroA) * 1000 / askMicroA);

    private (long X, long Y) Centre(int settlement)
    {
        var s = State.Population.Region.Settlements[Math.Clamp(settlement, 0, State.Population.Region.Settlements.Count - 1)];
        return (s.CenterX * 1000L, s.CenterY * 1000L);
    }

    private void DoTrade(AutopilotState st, Directive d, long at)
    {
        int A = (int)d.Target, B = (int)d.Params[0]; ushort good = (ushort)d.Params[1]; long grams = d.Params[2]; int minPm = (int)d.Params[3];
        int hour = (int)(at % 86400 / 3600), day = (int)(at / 86400);
        var party = Ctx.Party;
        bool Open(int s) => Ctx.Book(s) is { } b && b.IsOpen(hour);
        bool At(int s) { var c = Centre(s); return PathPlan.Dist(st.PosX - c.X, st.PosY - c.Y) <= 5000; }
        void Go(int s) { var c = Centre(s); Walk(st, c.X, c.Y, at); st.LastProgressSec = at; }
        switch (d.Aux0)
        {
            case 0:     // be at A, read the prices, decide
                if (!At(A)) { Go(A); return; }
                if (!Open(A)) return;
                if (st.StepUntil != -1 - day) { st.StepUntil = -1 - day; Desk.Issue(new HeroCommand { Kind = HeroCommandKind.VisitMarket, Settlement = A, IssuedSec = at, Origin = CommandOrigin.Autopilot }); return; }
                if (!party.Notebook.TryGet(A, good, out var na) || !party.Notebook.TryGet(B, good, out var nb))
                { if (at - st.StepSince > 3 * 3600 && st.StepSince != 0) { Block(st, d, at, "No prices known for the route"); } if (st.StepSince == 0) st.StepSince = at; return; }
                int margin = MarginPm(na.AskMicro, nb.BidMicro);
                if (margin < minPm)
                {
                    d.Aux1++;
                    Book.Log.Add(at, st.Person, LogCode.Action, $"Route {A}->{B}: margin {margin / 10.0:0.0}% is below {minPm / 10.0:0.0}%");
                    if (d.Aux1 >= 2) { Block(st, d, at, "The margin stayed below the minimum twice"); return; }
                    st.CampUntil = (at / 86400 + 1) * 86400; return;
                }
                long allowance = LimitGuard.AllowanceLeft(st.Limits, st, day);
                long afford = Math.Min(Ctx.Coin - st.Limits.PurseReserveMites, allowance == long.MaxValue ? long.MaxValue : allowance);
                long qty = Math.Min(grams, afford <= 0 ? 0 : afford * 1_000_000 / Math.Max(1, na.AskMicro) * 9 / 10);
                if (qty <= 0) { Raise(st, InterruptKind.NeedMoney, at, "The route needs more coin than the cap allows"); st.CampUntil = (at / 86400 + 1) * 86400; return; }
                var book = Ctx.Book(A); long cost = book == null ? 0 : Quotes.Buy(book, good, qty).TotalMites;
                var v = LimitGuard.CheckSpend(st.Limits, st, day, Ctx.Coin, cost);
                if (cost <= 0 || v != LimitVerdict.Allowed) { Raise(st, InterruptKind.LimitReached, at, $"Buying for the route: {v}"); st.CampUntil = (at / 86400 + 1) * 86400; return; }
                d.Cost = 0; d.Revenue = 0;
                var cmd = Desk.Issue(new HeroCommand { Kind = HeroCommandKind.Buy, Settlement = A, Good = good, Qty = qty, Limit = cost * 105 / 100, IssuedSec = at, Origin = CommandOrigin.Autopilot });
                LimitGuard.Commit(st, day, cost); _reserved[cmd.Seq] = (st.Person, day, cost);
                d.Aux0 = 1; st.StepSince = at; st.StepUntil = at; break;
            case 1:     // waiting for the goods to be in the pack (the barrier applies the buy at the end of the hour)
                if (Ctx.Pack.Qty(good) > 0 || at - st.StepSince > 3 * 3600) { d.Aux0 = Ctx.Pack.Qty(good) > 0 ? 2 : 0; st.StepUntil = 0; st.StepSince = 0; }
                break;
            case 2:     // to B
                if (!At(B)) { Go(B); return; }
                d.Aux0 = 3; st.StepSince = at; break;
            case 3:     // sell
                if (!Open(B)) return;
                Desk.Issue(new HeroCommand { Kind = HeroCommandKind.VisitMarket, Settlement = B, IssuedSec = at, Origin = CommandOrigin.Autopilot });
                Desk.Issue(new HeroCommand { Kind = HeroCommandKind.Sell, Settlement = B, Good = good, Qty = Ctx.Pack.Qty(good), IssuedSec = at, Origin = CommandOrigin.Autopilot });
                d.Aux0 = 4; st.StepSince = at; break;
            case 4:     // the sale is in at the end of the hour: judge the trip
                if (Ctx.Pack.Qty(good) > 0 && at - st.StepSince < 3 * 3600) return;
                int tripPm = d.Cost > 0 ? (int)((d.Revenue - d.Cost) * 1000 / d.Cost) : -1000;
                d.Aux2++; st.LastProgressSec = at;
                Book.Log.Add(at, st.Person, LogCode.Action, $"Trade trip {d.Aux2}: paid {Quotes.Money(d.Cost)}, got {Quotes.Money(d.Revenue)} ({tripPm / 10.0:0.0}%)");
                if (tripPm < minPm) { d.Aux1++; if (d.Aux1 >= 2) { Block(st, d, at, "The margin stayed below the minimum twice"); return; } } else d.Aux1 = 0;
                d.Aux0 = 5; break;
            case 5:     // back to A
                if (!At(A)) { Go(A); return; }
                d.Aux0 = 0; st.StepUntil = 0; st.StepSince = 0; break;
        }
    }

    private void Block(AutopilotState st, Directive d, long at, string why)
    {
        d.State = DirectiveState.Blocked;
        Book.Log.Add(at, st.Person, LogCode.Directive, $"Directive blocked: {d.Name}: {why}");
        Raise(st, InterruptKind.NeedMoney, at, why);
    }

    // ------------------------------------------------------------------ offers (contracts, big choices)

    /// <summary>
    /// A person offers the autopilot person a job, a toll or a ferry (spec 3.2 <c>Contract</c>). The interrupt is raised; with policy <c>Ai</c> the offer is accepted only if its kind is in <see cref="LimitSet.AcceptContractKinds"/>
    /// and within the limits, else declined politely; with Pause or Notify it is left to the player (<see cref="OfferResult.Pending"/>).
    /// </summary>
    public OfferResult OfferContract(EntityId person, OddJobKind kind, long wageMites, string text, long at)
    {
        var st = Book.Get(person); if (st == null || !st.Enabled) return OfferResult.Pending;
        var r = Raise(st, InterruptKind.Contract, at, text);
        if (r.Policy != InterruptPolicy.Ai) return OfferResult.Pending;
        bool ok = st.Limits.Accepts(kind);
        if (ok) Book.ContractsAccepted++; else Book.ContractsDeclined++;
        Book.Log.Add(at, person, LogCode.Action, ok ? $"Accepted the offer: {text}" : $"Declined politely: {text}");
        return ok ? OfferResult.Accepted : OfferResult.Declined;
    }

    /// <summary>
    /// An authored offer flagged <c>big</c> (marriage, fealty, a bribe, an oath, the sale of property): irreversible choices are never accepted by the AI (hard rule). With policy <c>Ai</c> the decline branch is taken and recorded;
    /// otherwise the game pauses for the player (a big choice cannot be set to Notify).
    /// </summary>
    public OfferResult OfferBigChoice(EntityId person, string text, long at)
    {
        var st = Book.Get(person); if (st == null || !st.Enabled) return OfferResult.Pending;
        var r = Raise(st, InterruptKind.BigChoice, at, text);
        if (r.Policy != InterruptPolicy.Ai) return OfferResult.Pending;
        Book.BigChoicesDeclined++;
        Book.Log.Add(at, person, LogCode.Action, $"Declined a big choice and noted it: {text}");
        return OfferResult.Declined;
    }
}
