using System.Globalization;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Law;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Heists;

/// <summary>
/// The hero's heist at the barrier (heists-and-jailbreaks 4, 6), recorded and replayed like every hero command (tag "hj:"): slip into a site ("enter"), act in it ("do": a step, a walk, a pick, a key, a break, a search, a bribe, a douse, a throw, a
/// fire, freeing a prisoner, the chest, the drain, the way out; the stance toggles) and give it up ("end"). Each action plays its seconds through <see cref="HeistRun"/> and then <see cref="HeistOutcome"/> turns what happened into coin, crimes and
/// entries. The screen reads <see cref="HeistView"/> and sends the next.
/// </summary>
public static class HeistDesk
{
    public const string Prefix = "hj:";

    private static string N(long v) => v.ToString(CultureInfo.InvariantCulture);
    private static HeroCommand C(string tag, long sec, int settlement = -1) => new() { Kind = HeroCommandKind.Heist, IssuedSec = sec, Origin = CommandOrigin.Human, Settlement = settlement, Tag = Prefix + tag };

    /// <summary>Slip into the site of a kind at a place; <paramref name="weather"/> 0 clear, 1 light rain, 2 heavy rain, 3 storm.</summary>
    public static HeroCommand Enter(int settlement, HeistKind kind, int weather, long sec) => C("enter|" + N(settlement) + "|" + (int)kind + "|" + N(weather), sec, settlement);
    /// <summary>One action of the run: "mv|dx|dy", "go|x|y", "wait|n", "pick|door", "pickout", "key|door", "brk|door", "pry|door", "srch|guard", "sub|guard", "brb|guard|offer", "dse|sconce", "thr|x|y", "fire|x|y", "free|slot", "loot", "portal", "leave", "sneak|0/1", "run|0/1", "torch|0/1", "liv|0/1".</summary>
    public static HeroCommand Do(string action, long sec) => C("do|" + action, sec);
    /// <summary>Gives the heist up where he stands (the run ends as if he had walked out; what he did stays done).</summary>
    public static HeroCommand End(long sec) => C("end", sec);

    private static HeroResult R(HeroCommand c, HeroResultCode code, string key, long mites = 0, long qty = 0, params object[] args) => new(c.Seq, c.Kind, code, HeroText.Of(key, args), mites, qty, c.IssuedSec);

    private static bool I(string s, out int v) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

    public static HeroResult Apply(HeroContext ctx, HeroCommand c)
    {
        if (!c.Tag.StartsWith(Prefix, StringComparison.Ordinal)) return R(c, HeroResultCode.Invalid, "HERO_UNKNOWN_COMMAND");
        var f = c.Tag[Prefix.Length..].Split('|');
        var st = ctx.State; var hb = st.Heists ??= new HeistBook();
        try
        {
            var rules = HeistRules.From(ctx.Content);
            var w = LawWorld.From(st, ctx.Content); var hero = ctx.Party.Hero;
            var cb = st.Law ??= new CrimeBook(); var jb = st.Justice ??= new JusticeBook();
            if (f[0] == "enter" && f.Length >= 4 && I(f[1], out int town) && I(f[2], out int kindI) && I(f[3], out int weather))
            {
                if (hb.Run is { Active: true }) return R(c, HeroResultCode.Refused, "hj_already_in");
                var kind = (HeistKind)kindI; var region = st.Population.Region;
                if (town < 0 || town >= region.Settlements.Count || !Enum.IsDefined(kind)) return R(c, HeroResultCode.Invalid, "HERO_MAKES_NO_SENSE");
                if (!HeistSites.Plan(region, st.Population.People).Contains((town, kind))) return R(c, HeroResultCode.Refused, "hj_no_such_site");
                bool seat = region.Fiefs.Any(fi => fi.SeatSettlement == town);
                int tier = HeistSites.TierOf(region.Settlements[town], seat);
                var real = GuardRoster.RealOf(st.Population.People, town);
                int heroHome = w.HomeOf(hero);
                var held = jb.Gaol.Values.Where(r => r.Settlement == town).OrderBy(r => r.Person).Select(r => r.Person).ToList();
                var run = HeistSites.Start(hb, rules, ctx.Seed, town, kind, tier, c.IssuedSec, Math.Clamp(weather, 0, 3), real, held, p => w.HonestyOf(new EntityId(p)), p => heroHome == town ? 1 : 0);
                if (run == null) return R(c, HeroResultCode.Refused, "hj_no_such_site");
                var (stealth, _, lockp, _, _) = w.SkillsOf(hero); run.Stealth = stealth; run.Lockpicking = lockp;
                return R(c, HeroResultCode.Ok, "hj_entered");
            }
            if (hb.Run is not { } r) return R(c, HeroResultCode.Refused, "hj_not_in");
            if (f[0] == "end")
            {
                if (r.Active) { r.Outcome = RunOutcome.Left; r.Events.Add(new HeistEvent(HeistEventKind.Left, r.Sec, (ulong)r.Seen.Count, 0, 0)); }
                var rep = HeistOutcome.Apply(w, cb, jb, hb, r, c.IssuedSec);
                return R(c, HeroResultCode.Ok, rep.Key.Length > 0 ? rep.Key : "hj_left_nothing", rep.Mites, 0, rep.Args);
            }
            if (f[0] != "do" || f.Length < 2) return R(c, HeroResultCode.Invalid, "HERO_MAKES_NO_SENSE");
            long before = r.Sec;
            var code = Dispatch(ctx, w, r, f, out string key, out long offer);
            if (code == HeroResultCode.CannotAfford) return R(c, code, "hj_cannot_afford");
            // the world's second is the run's: the barrier writes at the second the run has reached
            long now = Math.Max(c.IssuedSec, r.Sec);
            var report = HeistOutcome.Apply(w, cb, jb, hb, r, now);
            if (report.Key.Length > 0) return R(c, HeroResultCode.Ok, report.Key, report.Mites, 0, report.Args);
            _ = before; _ = offer;
            return R(c, code, key);
        }
        finally
        {
            if (st.Heists is { IsEmpty: true }) st.Heists = null;
            if (st.Justice is { IsEmpty: true }) st.Justice = null;
        }
    }

    private static HeroResultCode Dispatch(HeroContext ctx, LawWorld w, HeistRun r, string[] f, out string key, out long offer)
    {
        offer = 0; key = "hj_done";
        Act a; string op = f[1];
        bool P(int i, out int v) { v = 0; return f.Length > i && I(f[i], out v); }
        switch (op)
        {
            case "mv" when P(2, out int dx) && P(3, out int dy): a = r.Move(dx, dy); break;
            case "go" when P(2, out int gx) && P(3, out int gy): a = r.GoTo(gx, gy); break;
            case "wait" when P(2, out int n): a = r.Wait(Math.Clamp(n, 0, 600)); break;
            case "pick" when P(2, out int d): a = r.Pick(d); break;
            case "pickout": a = r.PickOuter(); break;
            case "key" when P(2, out int kd): a = r.UseKey(kd); break;
            case "brk" when P(2, out int bd): a = r.Break(bd); break;
            case "pry" when P(2, out int pd): a = r.Pry(pd); break;
            case "srch" when P(2, out int sg): a = r.Search(sg); break;
            case "sub" when P(2, out int ug): a = r.Subdue(ug); break;
            case "brb" when P(2, out int bg) && f.Length > 3 && long.TryParse(f[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long off):
            {
                offer = off;
                var from = w.PurseOf(w.HeroId);
                if (off <= 0 || w.Ledger == null || from == null || w.Ledger.Balance(from.Value).Mites < off) return HeroResultCode.CannotAfford;
                var (act, res) = r.Bribe(bg, off);
                key = act != Act.Ok ? "hj_act_" + act.ToString().ToLowerInvariant() : res == BribeResult.Taken ? "hj_bribe_taken" : "hj_bribe_refused";
                return act == Act.Ok ? HeroResultCode.Ok : HeroResultCode.Refused;
            }
            case "dse" when P(2, out int sc): a = r.Douse(sc); break;
            case "thr" when P(2, out int tx) && P(3, out int ty): a = r.Throw(tx, ty); break;
            case "fire" when P(2, out int fx) && P(3, out int fy): a = r.SetFire(fx, fy); break;
            case "free" when P(2, out int fs): a = r.Free(fs); break;
            case "loot": a = r.Loot(); break;
            case "portal": a = r.UsePortal(); break;
            case "leave": a = r.Leave(); break;
            case "sneak" when P(2, out int sn): r.SetSneak(sn != 0); a = Act.Ok; break;
            case "run" when P(2, out int rn): r.SetRun(rn != 0); a = Act.Ok; break;
            case "torch" when P(2, out int tn): r.SetTorch(tn != 0); a = Act.Ok; break;
            case "liv" when P(2, out int ln): r.SetLivery(ln != 0, ln != 0 ? LordOfHere(w, r) : 0); a = Act.Ok; break;
            default: key = "HERO_MAKES_NO_SENSE"; return HeroResultCode.Invalid;
        }
        key = a == Act.Ok ? "hj_done" : "hj_act_" + a.ToString().ToLowerInvariant();
        return a == Act.Ok ? HeroResultCode.Ok : HeroResultCode.Refused;
    }

    /// <summary>The lord whose colours the livery shows: the lord of the fief the site stands in.</summary>
    private static ulong LordOfHere(LawWorld w, HeistRun r)
    {
        int st = r.Sites.TryGetValue(r.Cur, out var s) ? s.Settlement : 0;
        return w.LordOf(w.FiefOf(st)).Value;
    }
}
