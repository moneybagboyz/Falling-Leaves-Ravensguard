using System.Globalization;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Holdings;

/// <summary>
/// The hero's side of holdings at the barrier (the hero desk calls it for <see cref="HeroCommandKind.Holdings"/>, P6-B6): clear an acre of a claim (Q > Clear an acre), work it by hand, hire hands, sow it,
/// take the logs, and ask whose a door, field or lot is (Q > Whose is this?). The tag says which ("hw:verb|..."), so every one is on the replay log, saved with the pending commands and applied in
/// sequence order on the tick worker; the works verbs (buy and deliver, post haul jobs, install a workstation) are in <c>HoldingsDesk.Works</c>.
/// </summary>
public static partial class HoldingsDesk
{
    public const string Prefix = "hw:";

    private static string Tag(params object[] parts) => Prefix + string.Join('|', parts.Select(p => Convert.ToString(p, CultureInfo.InvariantCulture)));
    private static HeroCommand Cmd(long sec, string tag, int settlement = -1) => new() { Kind = HeroCommandKind.Holdings, IssuedSec = sec, Tag = tag, Settlement = settlement, Origin = CommandOrigin.Human };

    /// <summary>Q > Clear an acre: a tile of the claim (-1: the nearest to the banner).</summary>
    public static HeroCommand Clear(int claim, int tile, long sec) => Cmd(sec, Tag("clear", claim, tile));
    /// <summary>The hero fells and grubs by hand from one game second to another.</summary>
    public static HeroCommand Work(int clearing, long fromSec, long toSec, long sec) => Cmd(sec, Tag("work", clearing, fromSec, toSec));
    public static HeroCommand Hire(int clearing, int count, long sec) => Cmd(sec, Tag("hire", clearing, count));
    public static HeroCommand Dismiss(int clearing, long sec) => Cmd(sec, Tag("dismiss", clearing));
    /// <summary>Q > Sow: ploughs and sows the cleared acre from the hero's own seed, in the window.</summary>
    public static HeroCommand Sow(int clearing, long sec) => Cmd(sec, Tag("sow", clearing));
    public static HeroCommand Collect(int clearing, long sec) => Cmd(sec, Tag("collect", clearing));
    /// <summary>Q > Whose is this? at a point in region metres.</summary>
    public static HeroCommand Whose(int xM, int yM, long sec) => Cmd(sec, Tag("whose", xM, yM));

    private static HeroResult R(HeroCommand c, HeroResultCode code, HeroText text, long mites = 0) => new(c.Seq, c.Kind, code, text, mites, 0, c.IssuedSec);

    public static HeroResult Apply(HeroContext ctx, HeroCommand c)
    {
        if (!c.Tag.StartsWith(Prefix, StringComparison.Ordinal)) return R(c, HeroResultCode.Invalid, HeroText.Of("HERO_UNKNOWN_COMMAND"));
        var p = c.Tag[Prefix.Length..].Split('|');
        long N(int i) => i < p.Length && long.TryParse(p[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : long.MinValue;
        int I(int i) { long v = N(i); return v is >= int.MinValue and <= int.MaxValue ? (int)v : int.MinValue; }
        var st = ctx.State;
        if (p[0] == "whose" && p.Length >= 3) return Whose(ctx, c, I(1), I(2));
        var hw = HoldingsWiring.For(st);
        if (hw == null) return R(c, HeroResultCode.Invalid, HeroText.Of("HERO_MAKES_NO_SENSE"));
        var hero = ctx.Party.Hero;
        int day = (int)(c.IssuedSec / 86400);
        EntityId Chron(GameTime when, string type, IReadOnlyList<EntityId> subjects, IReadOnlyList<Cause> causes, params (string Key, string Value)[] data)
        {
            var id = st.Ids.Allocate(IdKind.Event);
            var d = new Dictionary<string, string>(StringComparer.Ordinal); foreach (var (k, v) in data) d[k] = v;
            st.Chronicle.Add(new ChronicleEntry(id, when, type, subjects, causes, d));
            return id;
        }
        HoldingsResult? res = p[0] switch
        {
            "clear" when p.Length >= 3 => hw.Clear(hero, I(1), I(2), day),
            "work" when p.Length >= 4 => FellByHand(ctx, hw, I(1), N(2), N(3), day, Chron),
            "hire" when p.Length >= 3 => hw.Hire(hero, I(1), Math.Clamp(I(2), 1, HoldingsWorld.MaxHands), day),
            "dismiss" when p.Length >= 2 => hw.Dismiss(hero, I(1)),
            "sow" when p.Length >= 2 => hw.Sow(hero, I(1), day, ctx.Pack, Chron),
            "collect" when p.Length >= 2 => hw.Collect(hero, I(1), ctx.Pack),
            _ => Works(ctx, c, p, hw, day),
        };
        if (res == null) return R(c, HeroResultCode.Invalid, HeroText.Of("HERO_MAKES_NO_SENSE"));
        var code = res.Ok ? HeroResultCode.Ok : res.CannotAfford ? HeroResultCode.CannotAfford : HeroResultCode.Refused;
        return R(c, code, HeroText.Of(res.Key, res.Args), res.Mites);
    }

    // ------------------------------------------------------------------ whose is this? (holdings 3.2)

    /// <summary>What the registry says of a point: the kind of holding, the holder's name as the hero may know it, the tenure and the lord (HW-2).</summary>
    public sealed record WhoseAnswer(string What, string Holder, Tenure? Tenure, string Lord, bool Known, EntityId Deed);

    public static WhoseAnswer WhoseAt(LandWorld lw, EntityId hero, int heroHousehold, int heroSettlement, int xM, int yM)
    {
        var reg = lw.Registry(); var r = lw.Region;
        var may = reg.May(hero, heroHousehold, LandAction.Farm, xM, yM);
        var deed = may.Deed != EntityId.None ? reg.Get(may.Deed) : null;
        int t = LandRules.TileAt(xM, yM);
        if (deed == null && t >= 0)
        {
            foreach (var cl in lw.Book.Claims)
                if (cl.State is ClaimState.Open or ClaimState.Deeded && Array.BinarySearch(cl.Tiles, t) >= 0)
                {
                    bool mine = cl.Claimant == hero;
                    return new WhoseAnswer("hw_what_claim", mine ? "" : reg.HolderName(cl.Claimant, cl.ClaimantPurse), cl.State == ClaimState.Deeded ? (cl.Licensed ? Tenure.Customary : Tenure.Freehold) : null,
                        cl.Fief >= 0 && cl.Fief < r.Fiefs.Count ? r.Fiefs[cl.Fief].Name : "", true, cl.DeedId);
                }
        }
        if (deed == null) return new WhoseAnswer("hw_what_wild", "", null, "", true, EntityId.None);
        string what = deed.Kind switch { HoldingKind.Parcel => "hw_what_field", HoldingKind.Claim => "hw_what_claim", HoldingKind.Stall => "hw_what_stall", _ => "hw_what_lot" };
        string name = deed.Holder == hero ? "" : reg.HolderName(deed.Holder, deed.HolderPurse);
        // a known fact in the hero's own place and for the lord's and the town's holdings; a stranger's house elsewhere is only "a burgher's" or "a villager's"
        bool known = deed.Holder == hero || deed.Settlement == heroSettlement || deed.HolderPurse is PurseKind.Treasury or PurseKind.Settlement or PurseKind.Chest;
        string lord = deed.Lord >= 0 && deed.Lord < r.Fiefs.Count ? r.Fiefs[deed.Lord].Name : "";
        return new WhoseAnswer(what, known ? name : "", deed.Tenure, lord, known, deed.Id);
    }

    private static HeroResult Whose(HeroContext ctx, HeroCommand c, int x, int y)
    {
        var lw = LandWiring.For(ctx.State, ctx.Content);
        int hh = Ravensguard.Sim.Construction.ConstructionDesk.HouseholdOf(ctx);
        var pt = ctx.State.Population.People; int row = Ravensguard.Sim.Succession.HeroPerson.RowOf(pt, ctx.Hero, ctx.Party.Hero);
        int home = row >= 0 ? pt.HomeSettlement[row] : -1;
        var a = WhoseAt(lw, ctx.Party.Hero, hh, home, x, y);
        var lord = a.Lord.Length > 0 ? a.Lord : "-";
        var tenure = a.Tenure is { } tn ? HeroText.Of("hw_tenure_" + tn.ToString().ToLowerInvariant()) : HeroText.Of("hw_tenure_none");
        var holder = a.Holder.Length > 0 ? (object)a.Holder : HeroText.Of(a.Deed == EntityId.None && a.What == "hw_what_wild" ? "hw_holder_nobody" : a.Known ? "hw_holder_you" : "hw_holder_stranger");
        string key = a.What == "hw_what_wild" ? "hw_whose_wild" : "hw_whose";
        return R(c, HeroResultCode.Ok, HeroText.Of(key, HeroText.Of(a.What), holder, tenure, lord));
    }

    /// <summary>The hero fells and grubs by hand: heavy work, so the noon meal and the day's cap rule it (<see cref="HeavyWork"/>), then the hours go to the acre.</summary>
    private static HoldingsResult FellByHand(HeroContext ctx, HoldingsWorld hw, int serial, long fromSec, long toSec, int day, Ravensguard.Sim.Land.LandChronicle chron)
    {
        var lim = HeavyWork.Begin(ctx, hw.Book, fromSec, toSec, null);
        if (lim.Why != null) return HoldingsResult.No(lim.Why);
        long before = hw.Book.Get(serial)?.HeroMilli ?? 0;
        var r = hw.HeroWork(ctx.Party.Hero, serial, fromSec, lim.UntilSec, day, chron);
        if (r.Ok) HeavyWork.End(hw.Book, ((hw.Book.Get(serial)?.HeroMilli ?? 0) - before) * 3600 / 1000);
        return r;
    }

    /// <summary>The household and settlement of the hero for the desk's helpers.</summary>
    internal static (int Household, int Home) HeroPlace(HeroContext ctx)
    {
        var pt = ctx.State.Population.People; int row = Ravensguard.Sim.Succession.HeroPerson.RowOf(pt, ctx.Hero, ctx.Party.Hero);
        return row < 0 ? (-1, -1) : (pt.Household[row], pt.HomeSettlement[row]);
    }
}
