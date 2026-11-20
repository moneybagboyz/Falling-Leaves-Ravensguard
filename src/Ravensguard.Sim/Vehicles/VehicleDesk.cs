using System.Globalization;
using Ravensguard.Content;
using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Crafting;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Carts;

/// <summary>
/// The hero's side of vehicles at the barrier (the hero desk calls it for <see cref="HeroCommandKind.Vehicle"/>, P5-G). The tag names the verb ("vd:verb|..."), so every one is on the replay log,
/// saved with the pending commands and applied in sequence order on the tick worker, under the ride book's lock. The designer and the Q menu issue these. Vehicles are named by their serial.
/// <list type="bullet">
/// <item><c>vd:design|id|text64</c>: a design saved from the designer into the save's designs (and registered, so it can be built).</item>
/// <item><c>vd:build|design|x|y</c>: an unfinished vehicle of the design at region mm (x, y): every part a ghost.</item>
/// <item><c>vd:deposit|vehicle|item</c>: a part's item from the hero's pack onto the site's pile.</item>
/// <item><c>vd:work|vehicle|from|until</c>: the hero's own hands on the site over game seconds [from, until] (his Carpentry, the worker-hour rule).</item>
/// <item><c>vd:crew|vehicle|person|skill</c>, <c>vd:dismiss|vehicle|person</c>, <c>vd:pause|vehicle|1|0</c>.</item>
/// <item><c>vd:commission|design|maker|x|y</c>, <c>vd:collect|vehicle</c> (in person, the hero's position in X, Y).</item>
/// <item><c>vd:repair|vehicle|part</c> (the repair line's materials from the pack), <c>vd:replace|vehicle|part|item</c>, <c>vd:launch|vehicle|people|horse</c>.</item>
/// <item><c>vd:voyage|boat|waterway|from|to</c>: a boat put on a river leg (an NPC crew takes it; the abstract tier moves it).</item>
/// </list>
/// </summary>
public static class VehicleDesk
{
    public const string Prefix = "vd:";

    private static string Tag(params object[] parts) => Prefix + string.Join('|', parts.Select(p => Convert.ToString(p, CultureInfo.InvariantCulture)));
    private static HeroCommand Cmd(long sec, string tag, long x = 0, long y = 0) => new() { Kind = HeroCommandKind.Vehicle, IssuedSec = sec, Tag = tag, X = x, Y = y, Origin = CommandOrigin.Human };
    private static string B64(string s) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s));
    private static string UnB64(string s) { try { return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(s)); } catch (FormatException) { return ""; } }
    private static ulong Serial(EntityId v) => v.Serial - VehicleTable.SerialBase;

    public static HeroCommand SaveDesign(string id, string text, long sec) => Cmd(sec, Tag("design", id, B64(text)));
    public static HeroCommand Build(string design, long xMm, long yMm, long sec) => Cmd(sec, Tag("build", design, xMm, yMm));
    public static HeroCommand Deposit(EntityId vehicle, EntityId item, long sec) => Cmd(sec, Tag("deposit", Serial(vehicle), item.Value));
    public static HeroCommand Work(EntityId vehicle, long fromSec, long untilSec) => Cmd(untilSec, Tag("work", Serial(vehicle), fromSec, untilSec));
    public static HeroCommand Crew(EntityId vehicle, EntityId person, int skill, long sec) => Cmd(sec, Tag("crew", Serial(vehicle), person.Value, skill));
    public static HeroCommand Dismiss(EntityId vehicle, EntityId person, long sec) => Cmd(sec, Tag("dismiss", Serial(vehicle), person.Value));
    public static HeroCommand Pause(EntityId vehicle, bool pause, long sec) => Cmd(sec, Tag("pause", Serial(vehicle), pause ? 1 : 0));
    public static HeroCommand Commission(string design, EntityId maker, long xMm, long yMm, long sec) => Cmd(sec, Tag("commission", design, maker.Value, xMm, yMm));
    public static HeroCommand Collect(EntityId vehicle, long heroXMm, long heroYMm, long sec) => Cmd(sec, Tag("collect", Serial(vehicle)), heroXMm, heroYMm);
    public static HeroCommand Repair(EntityId vehicle, int part, long sec) => Cmd(sec, Tag("repair", Serial(vehicle), part));
    public static HeroCommand Replace(EntityId vehicle, int part, EntityId item, long sec) => Cmd(sec, Tag("replace", Serial(vehicle), part, item.Value));
    public static HeroCommand Launch(EntityId vehicle, int people, bool horse, long sec) => Cmd(sec, Tag("launch", Serial(vehicle), people, horse ? 1 : 0));
    public static HeroCommand Voyage(EntityId boat, int waterway, long fromMm, long toMm, long sec) => Cmd(sec, Tag("voyage", Serial(boat), waterway, fromMm, toMm));

    private static HeroResult R(HeroCommand c, HeroResultCode code, HeroText text, long mites = 0) => new(c.Seq, c.Kind, code, text, mites, 0, c.IssuedSec);
    private static HeroResult Ans(HeroCommand c, SiteAnswer a, string okKey, params object[] args) =>
        a == SiteAnswer.Ok ? R(c, HeroResultCode.Ok, HeroText.Of(okKey, args)) : R(c, a == SiteAnswer.CannotAfford ? HeroResultCode.CannotAfford : HeroResultCode.Refused, HeroText.Of("vd_no_" + a.ToString().ToLowerInvariant()));

    /// <summary>The ride book a desk command works on: the world's (made here when the game has not made it yet).</summary>
    public static RideBook BookOf(WorldState st, ContentDatabase content)
    {
        var b = st.Mounts ??= new RideBook(content, st.Economy ?? throw new InvalidOperationException("vehicles need the world's economy"), st.Seed);
        if (st.Designs != null && b.Templates.All.Count == b.Templates.TemplateCount) b.AttachDesigns(st.Designs);
        return b;
    }

    public static HeroResult Apply(HeroContext ctx, HeroCommand c)
    {
        if (!c.Tag.StartsWith(Prefix, StringComparison.Ordinal)) return R(c, HeroResultCode.Invalid, HeroText.Of("HERO_UNKNOWN_COMMAND"));
        var p = c.Tag[Prefix.Length..].Split('|');
        long N(int i) => i < p.Length && long.TryParse(p[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : long.MinValue;
        string S(int i) => i < p.Length ? p[i] : "";
        var st = ctx.State; var b = BookOf(st, ctx.Content);
        var hero = ctx.Party.Hero; long sec = c.IssuedSec;
        VehicleRow? V(int i) => N(i) < 0 ? null : b.Vehicles.Find(VehicleTable.MakeId((ulong)N(i)));
        lock (b.Gate)
        {
            switch (p[0])
            {
                case "design" when p.Length >= 3:
                {
                    string id = S(1), text = UnB64(S(2));
                    if (!id.StartsWith("player.", StringComparison.Ordinal)) return R(c, HeroResultCode.Invalid, HeroText.Of("vd_no_invalid"));
                    var def = VehicleCatalog.ParseDesign(text, id, out _);
                    if (def == null) return R(c, HeroResultCode.Invalid, HeroText.Of("vd_no_invalid"));
                    var errs = new List<string>();
                    var bp = b.Templates.Register(def, errs);
                    (st.Designs ??= new DesignBook()).Put(id, text, DesignKind.Vehicle);
                    return R(c, HeroResultCode.Ok, HeroText.Of(bp == null ? "vd_design_saved_invalid" : "vd_design_saved", def.Name));
                }
                case "build" when p.Length >= 4:
                {
                    var (a, v) = VehicleSites.Order(b, S(1), hero, N(2), N(3));
                    return Ans(c, a, "vd_site_ordered", v == null ? "" : b.Vehicles.DesignOf(v).Def.Name);
                }
                case "deposit" when p.Length >= 3:
                {
                    var v = V(1); if (v == null) return Ans(c, SiteAnswer.Invalid, "");
                    var a = VehicleSites.Deposit(b, ctx.Econ.Items, v, new EntityId((ulong)N(2)), hero);
                    return Ans(c, a, "vd_part_deposited");
                }
                case "work" when p.Length >= 4:
                {
                    var v = V(1); if (v == null) return Ans(c, SiteAnswer.Invalid, "");
                    if (!MayWork(st, b, v, hero)) return Ans(c, SiteAnswer.NotYours, "");
                    long seconds = Math.Clamp(N(3) - N(2), 0, 86_400);
                    int skill = ctx.Hero?.Skill(Characters.HeroRecord.SkillId(Skill.Carpentry)) ?? 30;
                    long milli = seconds * VehicleSites.MilliPerHour(skill) / 3600;
                    long used = VehicleSites.Work(b, ctx.Econ.Items, v, milli, N(3));
                    return R(c, HeroResultCode.Ok, HeroText.Of(v.Finished ? "vd_site_finished" : used < milli ? "vd_site_waits_part" : "vd_site_worked", (used + 500) / 1000));
                }
                case "crew" when p.Length >= 4:
                {
                    var v = V(1); if (v == null || !b.Works.Sites.TryGetValue(v.Id, out var s)) return Ans(c, SiteAnswer.Invalid, "");
                    if (s.Owner != hero) return Ans(c, SiteAnswer.NotYours, "");
                    var who = new EntityId((ulong)N(2));
                    if (!s.Crew.Contains(who)) { int k = s.Crew.FindIndex(x => x.CompareTo(who) > 0); if (k < 0) k = s.Crew.Count; s.Crew.Insert(k, who); s.CrewSkill.Insert(k, (int)Math.Clamp(N(3), 0, 100)); }
                    return R(c, HeroResultCode.Ok, HeroText.Of("vd_crew_joined"));
                }
                case "dismiss" when p.Length >= 3:
                {
                    var v = V(1); if (v == null || !b.Works.Sites.TryGetValue(v.Id, out var s)) return Ans(c, SiteAnswer.Invalid, "");
                    int k = s.Crew.IndexOf(new EntityId((ulong)N(2))); if (k >= 0) { s.Crew.RemoveAt(k); s.CrewSkill.RemoveAt(k); }
                    return R(c, HeroResultCode.Ok, HeroText.Of("vd_crew_left"));
                }
                case "pause" when p.Length >= 3:
                {
                    var v = V(1); if (v == null || !b.Works.Sites.TryGetValue(v.Id, out var s)) return Ans(c, SiteAnswer.Invalid, "");
                    s.Paused = N(2) != 0; return R(c, HeroResultCode.Ok, HeroText.Of(s.Paused ? "vd_site_paused" : "vd_site_resumed"));
                }
                case "commission" when p.Length >= 5:
                {
                    var (a, com) = VehicleSites.Commission(b, ctx.CraftContext, S(1), hero, new EntityId((ulong)N(2)), N(3), N(4), sec, st.Ids);
                    return Ans(c, a, "vd_commissioned", com == null ? 0 : com.PartsMites + com.InstallMites);
                }
                case "collect" when p.Length >= 2:
                {
                    var v = V(1); if (v == null || !b.Works.Commissions.TryGetValue(v.Id, out var com)) return Ans(c, SiteAnswer.Invalid, "");
                    var a = VehicleSites.Collect(b, ctx.CraftContext, com, hero, c.X, c.Y);
                    return Ans(c, a, "vd_collected", b.Vehicles.DesignOf(v).Def.Name);
                }
                case "repair" when p.Length >= 3:
                {
                    var v = V(1); if (v == null) return Ans(c, SiteAnswer.Invalid, "");
                    var (a, s) = VehicleSites.Repair(b, ctx.Recipes, v, (int)N(2), ctx.Pack);
                    return Ans(c, a, "vd_repaired", (s + 1799) / 3600);
                }
                case "replace" when p.Length >= 4:
                {
                    var v = V(1); if (v == null) return Ans(c, SiteAnswer.Invalid, "");
                    return Ans(c, VehicleSites.Replace(b, ctx.Econ.Items, v, (int)N(2), new EntityId((ulong)N(3)), hero), "vd_part_deposited");
                }
                case "launch" when p.Length >= 4:
                {
                    var v = V(1); if (v == null) return Ans(c, SiteAnswer.Invalid, "");
                    var (a, s) = VehicleSites.Launch(b, v, (int)N(2), N(3) != 0);
                    if (a == SiteAnswer.Ok) b.Works.Sites.Remove(v.Id);
                    return Ans(c, a, "vd_launched", s / 60);
                }
                case "voyage" when p.Length >= 5:
                {
                    var v = V(1); if (v == null || !v.IsBoat) return Ans(c, SiteAnswer.NotABoat, "");
                    var w = st.Population.Region.Waterways.FirstOrDefault(x => x.Id == (int)N(2)); if (w == null) return Ans(c, SiteAnswer.Invalid, "");
                    var y = Voyages.Start(b, v, new WaterPath(w), N(3), N(4), sec);
                    return R(c, HeroResultCode.Ok, HeroText.Of("vd_voyage_started", Voyages.LegSeconds(Math.Abs(y.ToMm - y.AtMm), y.GroundMmps) / 3600));
                }
            }
        }
        return R(c, HeroResultCode.Invalid, HeroText.Of("HERO_MAKES_NO_SENSE"));
    }

    /// <summary>Who may work on a site (vehicle-designer 4.2: anyone allowed): its owner, his household, or anyone when the owner is gone (an abandoned frame on its trestles).</summary>
    public static bool MayWork(WorldState st, RideBook b, VehicleRow v, EntityId who)
    {
        if (v.Owner == who || v.Owner == EntityId.None) return true;
        var people = st.Population.People;
        int o = people.IndexOf(v.Owner), w = people.IndexOf(who);
        if (o < 0) return true;
        if (!people.IsAlive(o)) return true;
        if (w >= 0 && people.Household[o] >= 0 && people.Household[o] == people.Household[w]) return true;
        if (st.Hero != null && w < 0 && people.Household[o] >= 0 && st.Hero.PersonIndex >= 0 && people.Household[st.Hero.PersonIndex] == people.Household[o]) return true;
        return false;
    }
}
