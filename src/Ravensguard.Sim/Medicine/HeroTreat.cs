using System.Globalization;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Medicine;

/// <summary>
/// The hero's treatment at the barrier (medicine 4.1; <see cref="HeroCommandKind.Treat"/>). The tag says what: <c>tr:do|patient|kind|part</c> queues one verb, <c>tr:all|patient|n</c> queues a
/// "dress all bleeding" of up to n dressings (the planner <see cref="FieldDressing"/>), <c>tr:cancel</c> drops what is left (he moved more than a metre, a fight began, he was hit), and
/// <c>tr:phys|man|patient|kind:part,...</c> pays a physician or barber-surgeon for the lines picked from his quote (<see cref="TreatDesk"/>). A queued verb is performed by
/// <see cref="Advance"/> at the game second its work ends, so the blood lost meanwhile is lost; the pack gives up its bandages, rope and splints only then, so what a cancelled step would have used
/// is still there. The command's <c>Qty</c> bit 0 says the place is lit. Treating someone else trains Surgery by the use rule.
/// </summary>
public static class HeroTreatDesk
{
    public const string Prefix = "tr:";
    public static readonly SkillUse Practice = new("treat", Skill.Surgery, 60);
    /// <summary>The rope a tourniquet takes from the pack, grams (a rope stored as a weight).</summary>
    public const int RopeG = FieldDressing.TourniquetRopeG;

    // ------------------------------------------------------------------ commands

    private static HeroCommand Cmd(long sec, string tag, bool light = true) => new() { Kind = HeroCommandKind.Treat, IssuedSec = sec, Tag = tag, Qty = light ? 1 : 0, Origin = CommandOrigin.Human };
    private static string I(long v) => v.ToString(CultureInfo.InvariantCulture);

    /// <summary>One verb on a part of the patient (0 for the hero himself).</summary>
    public static HeroCommand Do(ulong patient, TreatKind kind, int part, long sec, bool light = true) => Cmd(sec, $"{Prefix}do|{I((long)patient)}|{(int)kind}|{part}", light);
    /// <summary>"Dress all bleeding": up to <paramref name="max"/> dressings, worst bleeder first.</summary>
    public static HeroCommand DressAll(ulong patient, int max, long sec, bool light = true) => Cmd(sec, $"{Prefix}all|{I((long)patient)}|{max}", light);
    public static HeroCommand Cancel(long sec) => Cmd(sec, Prefix + "cancel");
    /// <summary>Pay the physician <paramref name="man"/> to treat <paramref name="patient"/> for the chosen lines of his quote.</summary>
    public static HeroCommand Physician(ulong man, ulong patient, IEnumerable<QuoteLine> lines, long sec) =>
        Cmd(sec, $"{Prefix}phys|{I((long)man)}|{I((long)patient)}|{string.Join(",", lines.Select(l => $"{(int)l.Kind}:{l.Part}"))}");

    /// <summary>Verbs a person cannot do to himself: both hands are needed (a pressure hold and a bandage can be done with one).</summary>
    public static bool TwoHanded(TreatKind k) => k is not (TreatKind.Pressure or TreatKind.Bandage or TreatKind.ReleasePressure);

    // ------------------------------------------------------------------ the barrier

    private static HeroResult R(HeroCommand c, HeroResultCode code, string key, long mites = 0, long qty = 0, params object[] args) => new(c.Seq, c.Kind, code, HeroText.Of(key, args), mites, qty, c.IssuedSec);

    /// <summary>The queue of the world (made on first use), kept with the physicians in the world's medicine book.</summary>
    public static PhysicianBook BookOf(HeroContext ctx) => ctx.State.Medicine ??= new PhysicianBook();

    public static HeroResult Apply(HeroContext ctx, HeroCommand c)
    {
        var l = ctx.State.Wounds;
        if (l == null) return R(c, HeroResultCode.Refused, "tr_nobody");
        lock (l.Sync) return ApplyCore(ctx, l, c);
    }

    private static HeroResult ApplyCore(HeroContext ctx, WoundLedger l, HeroCommand c)
    {
        var q = BookOf(ctx).Queue;
        string[] p = c.Tag.Split('|');
        string verb = p[0].Length > Prefix.Length ? p[0][Prefix.Length..] : "";
        bool light = (c.Qty & 1) != 0;
        ulong hero = ctx.Party.Hero.Value;
        ulong U(int i) => i < p.Length && ulong.TryParse(p[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : ulong.MaxValue;
        int N(int i) => i < p.Length && int.TryParse(p[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : int.MinValue;
        switch (verb)
        {
            case "cancel": { int n = q.Cancel(); return R(c, HeroResultCode.Ok, n > 0 ? "tr_cancelled" : "tr_nothing_queued", 0, n); }
            case "do":
            case "all":
                {
                    ulong patient = U(1); bool all = verb == "all";
                    if (patient == ulong.MaxValue) return R(c, HeroResultCode.Invalid, "tr_bad");
                    if (l.Find(patient) is not { Dead: false } book) return R(c, HeroResultCode.Refused, "tr_no_wounds");
                    int kindN = all ? TreatQueue.DressAllKind : N(2), part = all ? -1 : N(3), max = all ? Math.Clamp(N(2), 1, FieldDressingMax) : 1;
                    if (!all && (kindN < 0 || kindN > (int)TreatKind.ReleasePressure || part < -1 || part > 255)) return R(c, HeroResultCode.Invalid, "tr_bad");
                    var kind = all ? TreatKind.Bandage : (TreatKind)kindN;
                    if (patient == hero && TwoHanded(all ? TreatKind.Bandage : kind)) return R(c, HeroResultCode.Refused, "tr_two_hands");
                    if (!all && SkillOf(ctx) < Treatment.MinSkill(kind)) return R(c, HeroResultCode.Refused, "tr_skill", 0, 0, Treatment.MinSkill(kind));
                    long start = Math.Max(c.IssuedSec, q.EndSec);
                    var step = new TreatStep { Patient = patient, Kind = (byte)kindN, Part = part, Remaining = all ? max : 1, StartSec = start, Actor = hero, Light = light, Chain = all };
                    if (!all) step.FinishSec = start + (long)Treatment.SimSeconds(kind) * MedicineTables.TicksPerSimS;
                    if (!q.Add(step)) return R(c, HeroResultCode.Refused, "tr_queue_full");
                    return R(c, HeroResultCode.Ok, "tr_queued", 0, q.Count);
                }
            case "phys": return Phys(ctx, l, c, p, U, light);
        }
        return R(c, HeroResultCode.Invalid, "tr_bad");
    }

    private const int FieldDressingMax = FieldDressing.MaxSteps;

    private static HeroResult Phys(HeroContext ctx, WoundLedger l, HeroCommand c, string[] p, Func<int, ulong> U, bool light)
    {
        var book = BookOf(ctx);
        ulong man = U(1), patient = U(2);
        if (!book.TryGet(man, out var row)) return R(c, HeroResultCode.Refused, "tr_no_physician");
        if (l.Find(patient) is not { Dead: false } wb) return R(c, HeroResultCode.Refused, "tr_no_wounds");
        var pt = ctx.State.Population.People; int mrow = pt.IndexOf(new EntityId(man));
        if (mrow < 0 || pt.Status[mrow] != PersonStatus.Alive) return R(c, HeroResultCode.Refused, "tr_no_physician");
        int skill = pt.SkillLevelOf(mrow, Skill.Surgery);
        var plan = MedicineWiring.PlanOf(ctx.Content, wb.PlanId);
        var quote = TreatDesk.Quote(wb, plan, row.Kind, skill, c.IssuedSec);
        var want = new HashSet<(int, int)>();
        if (p.Length > 3) foreach (var s in p[3].Split(',', StringSplitOptions.RemoveEmptyEntries)) { var kv = s.Split(':'); if (kv.Length == 2 && int.TryParse(kv[0], out int k) && int.TryParse(kv[1], out int pa)) want.Add((k, pa)); }
        var lines = quote.Where(q => want.Contains(((int)q.Kind, q.Part))).ToList();
        if (lines.Count == 0) return R(c, HeroResultCode.Refused, "tr_nothing_to_do");
        var ew = ctx.State.HomeRecords?.Sim.Get<EconomyWorld>();
        PurseId payee = row.Business >= 0 && ew != null && row.Business < ew.Production.Businesses.Count ? ew.Production.Businesses[row.Business].Purse
            : ctx.Econ.HouseholdPurse[pt.Household[mrow]];
        var outcomes = TreatDesk.Perform(ctx.Econ.Ledger, wb, plan, lines, row.Kind, skill, man, ctx.Purse, payee, c.IssuedSec, light: true);
        int done = outcomes.Count(o => o.Done); long paid = outcomes.Sum(o => (long)o.PaidMites);
        if (done == 0) return R(c, HeroResultCode.CannotAfford, outcomes.Count > 0 && outcomes[0].Why == "cannot pay" ? "tr_cannot_pay" : "tr_could_not", 0, 0);
        return R(c, HeroResultCode.Ok, "tr_physician_done", paid, done, done, Quotes.Money(paid));
    }

    // ------------------------------------------------------------------ the second the work ends

    /// <summary>What the hero knows of surgery (the skill book), 0 to 100.</summary>
    public static int SkillOf(HeroContext ctx) => ctx.Craft.State.Book.Level(ctx.Party.Hero, Skill.Surgery);

    /// <summary>
    /// Performs every step of the queue whose work has ended by game second <paramref name="nowSec"/>, in order, each at its own finish second; a "dress all" picks its next dressing when its turn
    /// comes. A step that cannot be done (the pack has run dry, the patient is gone) ends the queue. Returns how many were done. Called at a barrier by the game each frame while the queue is
    /// not empty, and by the hour's end.
    /// </summary>
    public static int Advance(HeroContext ctx, long nowSec)
    {
        var l = ctx.State.Wounds;
        var q = ctx.State.Medicine?.Queue;
        if (l == null || q == null || q.IsEmpty) return 0;
        int done = 0;
        lock (l.Sync)
        {
            ulong hero = ctx.Party.Hero.Value;
            var items = new PackItems(ctx);
            while (q.Head is { } h)
            {
                if (l.Find(h.Patient) is not { Dead: false } book) { q.Cancel(); break; }
                var plan = MedicineWiring.PlanOf(ctx.Content, book.PlanId);
                if (!h.Planned)
                {
                    if (plan == null) { q.Cancel(); break; }
                    if (FieldDressing.PlanStep(book, plan, items, h.StartSec) is not var (pk, pp)) { q.PopHead(); continue; }       // nothing left to dress, or nothing to dress with: the chain ends
                    h.Kind = (byte)pk; h.Part = pp; h.FinishSec = h.StartSec + (long)Treatment.SimSeconds(pk) * MedicineTables.TicksPerSimS;
                }
                if (h.FinishSec > nowSec) break;
                var kind = (TreatKind)h.Kind;
                var res = Treatment.Perform(book, plan, new TreatRequest(kind, h.Part, h.FinishSec, SkillOf(ctx), h.Actor, h.Light), items);
                q.PopHead();
                q.Note(new TreatOutcome(h.FinishSec, h.Patient, kind, h.Part, res.Ok, res.Success, res.Reason));
                if (!res.Ok) { q.Cancel(); break; }
                done++;
                if (h.Patient != hero) Progress.Use(ctx.Craft.State.Book, ctx.Party.Hero, Practice, Math.Max(10, Treatment.MinSkill(kind)), h.FinishSec / 20 * 20);
                if (h.Chain && h.Remaining > 1)
                    q.PushHead(new TreatStep { Patient = h.Patient, Kind = TreatQueue.DressAllKind, Part = -1, Remaining = h.Remaining - 1, StartSec = h.FinishSec, Actor = h.Actor, Light = h.Light, Chain = true });
            }
        }
        return done;
    }

    /// <summary>The pack as a treatment's items: goods by id (rope by weight), and the sewing needle as any carried tool that gives the <c>sewing</c> quality (kept, not used up).</summary>
    private sealed class PackItems(HeroContext ctx) : ITreatItems
    {
        private bool? _sewing;
        private bool Sewing() => _sewing ??= ctx.PackTools().Any(t => ctx.Recipes.Tools.TryGetValue(t, out var d) && d.Qualities.GetValueOrDefault("sewing") >= 1);
        private long Need(ushort g, string item, int count) => item == "rope" && !ctx.Econ.Goods.IsPiece(g) ? (long)count * RopeG : count;
        public bool Has(string item, int count)
        {
            if (item == "needle_and_thread") return Sewing();
            return ctx.Econ.Goods.TryIdOf(item, out var g) && ctx.Pack.Qty(g) >= Need(g, item, count);
        }
        public void Take(string item, int count)
        {
            if (item == "needle_and_thread" || !ctx.Econ.Goods.TryIdOf(item, out var g)) return;
            ctx.Pack.DestroyUpTo(g, Need(g, item, count), Ravensguard.Sim.Goods.DestroyReason.Consumed);
        }
    }

    /// <summary>The hero's pack as the screen counts it (bandages, rope, splints...), for the "uses" column.</summary>
    public static ITreatItems ItemsOf(HeroContext ctx) => new PackItems(ctx);
}
