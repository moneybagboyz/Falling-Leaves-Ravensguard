using System.Globalization;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Succession;

/// <summary>Someone the will panel offers as a beneficiary: who, how known (kin, companion, follower, acquaintance), age and where.</summary>
public sealed record WillPerson(EntityId Person, string Name, string How, int Age, string Place);
/// <summary>A thing a will can name: a deed or a business of the hero's, with its value.</summary>
public sealed record WillThing(EntityId Id, BequestWhat What, string Name, long ValueMites);
/// <summary>
/// What the will panel shows (inheritance 6): the people the hero knows, his deeds and businesses, the will he has made (if any), the fee and his coin, and the estate as it stands (what each heir
/// would get today). Built at a barrier; reads only.
/// </summary>
public sealed record WillView(IReadOnlyList<WillPerson> People, IReadOnlyList<WillThing> Things, Will? Current, long FeeMites, long CoinMites, EstateSummary Estate);

/// <summary>
/// The hero's will at the barrier (inheritance 3, 6; dialogue <c>make_will</c>): made in person before a priest or the reeve ("wl:make|witness|person:what:thing:pm|..."), the 2-crown fee to the
/// witness's household, recorded for replay like every hero command. A newer will replaces the old.
/// </summary>
public static class WillDesk
{
    public const string Prefix = "wl:";
    /// <summary>Who may witness a will: a priest (the church) or the reeve (the lord's officer).</summary>
    public static readonly Occupation[] Witnesses = [Occupation.Priest, Occupation.Reeve];

    public static HeroCommand Make(EntityId witness, IReadOnlyList<Bequest> bequests, long sec) => new()
    {
        Kind = HeroCommandKind.Will, IssuedSec = sec, Origin = CommandOrigin.Human,
        Tag = Prefix + "make|" + witness.Value.ToString(CultureInfo.InvariantCulture) + string.Concat(bequests.Select(b =>
            string.Create(CultureInfo.InvariantCulture, $"|{b.Person.Value}:{(int)b.What}:{b.Thing.Value}:{b.CoinPm}"))),
    };

    private static HeroResult R(HeroCommand c, HeroResultCode code, HeroText t) => new(c.Seq, c.Kind, code, t, 0, 0, c.IssuedSec);

    public static HeroResult Apply(HeroContext ctx, HeroCommand c)
    {
        if (!c.Tag.StartsWith(Prefix + "make|", StringComparison.Ordinal)) return R(c, HeroResultCode.Invalid, HeroText.Of("HERO_UNKNOWN_COMMAND"));
        var parts = c.Tag[(Prefix.Length + 5)..].Split('|');
        if (!ulong.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong w)) return R(c, HeroResultCode.Invalid, HeroText.Of("HERO_MAKES_NO_SENSE"));
        var st = ctx.State; var pt = st.Population.People; var e = ctx.Econ;
        var witness = new EntityId(w);
        int wr = pt.IndexOf(witness);
        if (wr < 0 || pt.Status[wr] != PersonStatus.Alive || Array.IndexOf(Witnesses, pt.Occupation[wr]) < 0) return R(c, HeroResultCode.Refused, HeroText.Of("in_wl_no_witness"));
        int wh = pt.Household[wr];
        var witnessPurse = wh >= 0 && wh < e.HouseholdCount ? e.HouseholdPurse[wh] : e.Ledger.Open(PurseKind.Person, witness);
        var bequests = new List<Bequest>();
        foreach (var q in parts.Skip(1))
        {
            var f = q.Split(':');
            if (f.Length != 4 || !ulong.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong person) || !int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int what)
                || !ulong.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong thing) || !int.TryParse(f[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pm) || what is < 0 or > 3)
                return R(c, HeroResultCode.Invalid, HeroText.Of("HERO_MAKES_NO_SENSE"));
            bequests.Add(new Bequest(new EntityId(person), (BequestWhat)what, new EntityId(thing), pm));
        }
        var book = st.Wills ??= new WillBook();
        var (ok, key) = book.Make(e.Ledger, ctx.Party.Hero, ctx.Purse, witnessPurse, witness, bequests, (int)(c.IssuedSec / 86400));
        if (book.IsEmpty) st.Wills = null;
        if (!ok) return R(c, key == "in_wl_cannot_afford" ? HeroResultCode.CannotAfford : HeroResultCode.Refused, HeroText.Of(key, Quotes.Money(WillBook.FeeMites)));
        st.Chronicle.Add(new ChronicleEntry(st.Ids.Allocate(IdKind.Event), new GameTime(c.IssuedSec), "will_made", [ctx.Party.Hero, witness], [],
            new Dictionary<string, string> { ["witness"] = pt.FullName(wr), ["lines"] = bequests.Count.ToString(CultureInfo.InvariantCulture) }));
        return new HeroResult(c.Seq, c.Kind, HeroResultCode.Ok, HeroText.Of(key, pt.FullName(wr), Quotes.Money(WillBook.FeeMites)), WillBook.FeeMites, bequests.Count, c.IssuedSec);
    }

    /// <summary>The will panel's view (at a barrier): the people the hero knows (kin first, then the squad, then acquaintances), his deeds and businesses, the current will, the estate as it stands.</summary>
    public static WillView View(Save.WorldState st, HeroParty party, Content.ContentDatabase content, int day)
    {
        var pt = st.Population.People; var region = st.Population.Region;
        var people = new List<WillPerson>(); var seen = new HashSet<EntityId>();
        string Place(int i) => i >= 0 && pt.HomeSettlement[i] is int s && s >= 0 && s < region.Settlements.Count ? region.Settlements[s].Name : "";
        void Add(EntityId id, string how)
        {
            if (id == EntityId.None || id == party.Hero || !seen.Add(id)) return;
            int i = pt.IndexOf(id); if (i < 0 || pt.Status[i] != PersonStatus.Alive) return;
            people.Add(new WillPerson(id, pt.FullName(i), how, pt.AgeYears(i, day), Place(i)));
        }
        int row = HeroPerson.RowOf(pt, st.Hero, party.Hero);
        if (row >= 0) for (int i = 0; i < pt.Count; i++) if (pt.Father[i] == row || pt.Mother[i] == row) Add(pt.IdOf(i), "child");
        foreach (var k in st.Succession?.Kin ?? []) Add(k.Person, k.Relation.ToString().ToLowerInvariant());
        foreach (var m in party.Squad.Active) Add(m.Person, m.Origin == FollowerOrigin.Recruited ? "companion" : "follower");
        foreach (var k in party.Known.Keys) Add(k, "known");
        var things = new List<WillThing>();
        if (st.Land is { } land && st.Economy != null)
        {
            var lw = Land.LandWiring.For(st, content);
            foreach (var d in land.Deeds.Values.Where(d => d.Holder == party.Hero && !d.IsLease && d.Kind != Land.HoldingKind.Stall))
                things.Add(new WillThing(d.Id, BequestWhat.Deed, d.Kind.ToString() + (d.Settlement >= 0 && d.Settlement < region.Settlements.Count ? ", " + region.Settlements[d.Settlement].Name : ""), lw.ValueOf(d)));
        }
        if (st.Production is { } prod && row >= 0)
        {
            var ow = OwnershipWiring.For(st, content);
            foreach (var b in prod.Businesses.Where(b => b.Owner == row && b.State == BusinessState.Open)) things.Add(new WillThing(b.Id, BequestWhat.Business, ow.NameOf(b), ow.ValueOf(b)));
        }
        long coin = st.Economy?.Ledger.TryFind(PurseKind.Person, party.Hero, out var hp) == true ? st.Economy.Ledger.Balance(hp).Mites : 0;
        var heirs = SuccessionDesk.Candidates(st, party, day);
        var first = heirs.FirstOrDefault();
        bool kin = first != null && first.Rule is SuccessionRule.Child or SuccessionRule.Spouse or SuccessionRule.Sibling or SuccessionRule.YoungChild or SuccessionRule.Parent;
        var est = st.Economy == null ? EstateSummary.Empty : EstateWiring.For(st, content).Summary(party.Hero, row, true,
            first == null ? null : new Recipient(first.PersonIndex, party.Hero, party.Hero, PurseKind.Person, true, false, first.Name), kin,
            st.Economy.Ledger.TryFind(PurseKind.Person, party.Hero, out var pp) ? pp : null, day);
        return new WillView(people, things, st.Wills?.Of(party.Hero), WillBook.FeeMites, coin, est);
    }
}
