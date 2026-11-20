using System.Globalization;
using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Family;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Succession;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Disease;

/// <summary>
/// Installs disease on the home region (P6-C4 hook in <see cref="WorldRuntime"/>): the world state's <see cref="DiseaseBook"/> (the "disease" section) goes into the sim's services for
/// <see cref="DiseaseSystem"/>; the continent's plague is the book's (so the seasonal tick and the save see one object); the scarcity terms are handed back to the economy that reads them; the tracked
/// persons are the hero, his household and his party; the hero's death is the family's pending death.
/// </summary>
public static class DiseaseWiring
{
    public static DiseaseBook Attach(RegionWorld home, WorldState state, ContentDatabase content)
    {
        DiseaseRules.Install(content);
        var b = state.Disease ??= new DiseaseBook();
        b.Severity = state.Difficulty.Plague;
        if (state.ContinentState is { } cs) cs.Plague = b.Continent;
        b.Tracked = () => TrackedOf(state);
        b.PurseOf = person => IsHero(state, person) && state.Economy != null && state.Economy.Ledger.TryFind(PurseKind.Person, state.Party!.Hero, out var p) ? p : null;
        b.HeroDies = (id, when, cause, ob) => HeroDied(state, id, when, cause, ob);
        home.Sim.Set(b);
        PlagueEconomy.Apply(home.Sim, b);
        return b;
    }

    private static bool IsHero(WorldState st, ulong person) => st.Party != null && st.Party.Hero.Value == person;

    /// <summary>The hero first, then his household and his active party, in ascending id (a person once).</summary>
    public static IReadOnlyList<TrackedPerson> TrackedOf(WorldState st)
    {
        var party = st.Party; if (party == null || st.Population == null) return [];
        var pt = st.Population.People; var hht = st.Population.Households;
        var ids = new SortedSet<ulong>();
        int hr = HeroPerson.RowOf(pt, st.Hero, party.Hero);
        if (hr >= 0)
        {
            ids.Add(pt.IdOf(hr).Value);
            int hh = pt.Household[hr];
            if (hh >= 0) foreach (int m in hht.Members(hh)) ids.Add(pt.IdOf(m).Value);
        }
        foreach (var f in party.Squad.Active) ids.Add(f.Person.Value);
        var list = new List<TrackedPerson>(ids.Count);
        foreach (var id in ids) list.Add(new TrackedPerson(id, hr >= 0 && id == party.Hero.Value));
        return list;
    }

    /// <summary>The hero died of a sickness: the family's pending death (the succession screen takes it from there) and the chronicle entry.</summary>
    private static void HeroDied(WorldState st, ulong id, GameTime when, Cause cause, RegionOutbox ob)
    {
        var fam = st.Family ??= new FamilyBook();
        if (fam.Death != null || st.Population == null) return;
        var pt = st.Population.People; int row = HeroPerson.RowOf(pt, st.Hero, new EntityId(id)); if (row < 0) return;
        int day = (int)when.TotalDays;
        fam.Death = new PendingDeath { Day = day, Row = row, Age = pt.AgeYears(row, day), Cause = DeathCause.Disease };
        var name = cause.NoteParams is { Count: > 0 } ps ? new ChronParam("illness", ChronParamKind.TextKey, 0, ps[0].Key) : ChronParam.TextKey("illness", "ill_name_cough");
        ob.Chronicle(when, "hero_died_illness", "ch_ill_hero_died", [ChronParam.Person("hero", new EntityId(id)), name], [new EntityId(id)], [cause]);
    }
}

/// <summary>
/// The hero's side of sickness at the barrier (disease-and-plague 4.1, 4.3, 4.4; <see cref="HeroCommandKind.Disease"/>). The tag says what: <c>dz:nurse|person</c> (a party member spends two hours a day at the
/// bedside), <c>dz:phys|person</c> (a physician is asked to call every day; the fee is paid at each pass), <c>dz:burn</c> (the household burns the dead's bedding), <c>dz:quarantine|settlement|days</c>
/// (the lord closes a town's gates).
/// </summary>
public static class DiseaseDesk
{
    public const string Prefix = "dz:";

    private static HeroCommand Cmd(long sec, string tag, int settlement = -1, EntityId person = default) => new() { Kind = HeroCommandKind.Disease, IssuedSec = sec, Tag = Prefix + tag, Settlement = settlement, Person = person, Origin = CommandOrigin.Human };
    public static HeroCommand Nurse(long sec, EntityId person) => Cmd(sec, "nurse|" + person.Value.ToString(CultureInfo.InvariantCulture), person: person);
    public static HeroCommand CallPhysician(long sec, EntityId person) => Cmd(sec, "phys|" + person.Value.ToString(CultureInfo.InvariantCulture), person: person);
    public static HeroCommand BurnBedding(long sec) => Cmd(sec, "burn");
    public static HeroCommand Quarantine(long sec, int settlement, int days) => Cmd(sec, "quarantine|" + settlement.ToString(CultureInfo.InvariantCulture) + "|" + days.ToString(CultureInfo.InvariantCulture), settlement);

    private static HeroResult R(HeroCommand c, HeroResultCode code, string key, params object[] args) => new(c.Seq, c.Kind, code, HeroText.Of(key, args), 0, 0, c.IssuedSec);

    public static HeroResult Apply(HeroContext ctx, HeroCommand c)
    {
        if (!c.Tag.StartsWith(Prefix, StringComparison.Ordinal)) return R(c, HeroResultCode.Invalid, "HERO_UNKNOWN_COMMAND");
        var f = c.Tag[Prefix.Length..].Split('|');
        var st = ctx.State; var b = st.Disease ?? st.HomeRecords?.Sim.Get<DiseaseBook>();
        if (b == null) return R(c, HeroResultCode.Refused, "dz_nothing_ill");
        int day = (int)(c.IssuedSec / 86400);
        switch (f[0])
        {
            case "nurse":
            case "phys":
            {
                if (f.Length < 2 || !ulong.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong id)) return R(c, HeroResultCode.Invalid, "dz_nothing_ill");
                bool ok = f[0] == "nurse" ? DiseaseEngine.Nurse(b, id, day) : DiseaseEngine.CallPhysician(b, id, day);
                if (!ok) return R(c, HeroResultCode.Refused, "dz_nothing_ill");
                if (f[0] == "phys")
                {
                    int row = st.Population.People.IndexOf(new EntityId(id)); int s = row >= 0 ? st.Population.People.HomeSettlement[row] : -1;
                    if (st.HomeRecords == null || DiseaseEngine.PhysicianIn(st.HomeRecords.Sim, s) == null) { if (b.Illnesses.TryGetValue(id, out var i)) i.PhysicianUntil = -1; if (b.Plague?.Cases.TryGetValue(id, out var pc) == true) pc.PhysicianUntil = -1; return R(c, HeroResultCode.Refused, "dz_no_physician"); }
                    bool plague = b.Plague?.Cases.ContainsKey(id) == true;
                    return R(c, HeroResultCode.Ok, "dz_physician_called", Quotes.Money(DiseaseEngine.VisitFee(plague)));
                }
                return R(c, HeroResultCode.Ok, "dz_nursing");
            }
            case "burn":
            {
                var pt = st.Population.People; int row = HeroPerson.RowOf(pt, st.Hero, ctx.Party.Hero); int hh = row >= 0 ? pt.Household[row] : -1;
                if (hh < 0 || !DiseaseEngine.BurnBedding(b, hh, day + 30)) return R(c, HeroResultCode.Refused, "dz_no_plague");
                return R(c, HeroResultCode.Ok, "dz_bedding_burned");
            }
            case "quarantine":
            {
                if (f.Length < 3 || !int.TryParse(f[1], out int s) || !int.TryParse(f[2], out int days) || b.Plague is not { Active: true } gp || st.HomeRecords == null) return R(c, HeroResultCode.Refused, "dz_no_plague");
                if ((uint)s >= (uint)st.Population.Region.Settlements.Count || days <= 0) return R(c, HeroResultCode.Invalid, "dz_no_plague");
                var ob = new RegionOutbox(st.Ids);
                DiseaseEngine.Quarantine(st.HomeRecords.Sim, b, gp, s, day, Math.Min(days, 90), new GameTime(c.IssuedSec), ob, new EntityId(gp.BeginEntry));
                foreach (var e in ob.Entries) st.Chronicle.Add(new ChronicleEntry(e.Id, e.When, e.TypeId, e.Subjects, e.Causes, e.Data, e.TextKey, e.Params));
                return R(c, HeroResultCode.Ok, "dz_quarantined", st.Population.Region.Settlements[s].Name, days);
            }
            default: return R(c, HeroResultCode.Invalid, "HERO_UNKNOWN_COMMAND");
        }
    }
}
