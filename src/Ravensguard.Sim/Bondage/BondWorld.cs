using System.Runtime.CompilerServices;
using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Law;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Bondage;

/// <summary>The numbers of slavery from the content (<c>society/slavery.jsonc</c>), read once per content database; the spec's defaults without content.</summary>
public sealed class BondRules
{
    public SlaveryRulesDef D { get; }
    public BondRules(SlaveryRulesDef d) { D = d; }
    private static readonly ConditionalWeakTable<ContentDatabase, BondRules> Cache = new();
    public static BondRules Default { get; } = new(new SlaveryRulesDef { Id = "slavery" });
    public static BondRules From(ContentDatabase? db) => db == null ? Default : Cache.GetValue(db, d => new BondRules(d.All<SlaveryRulesDef>().OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault() ?? new SlaveryRulesDef { Id = "slavery" }));

    public static SlaveLaw Parse(string? s) => s switch { "debt" => SlaveLaw.Debt, "war_captives" => SlaveLaw.WarCaptives, "chattel" => SlaveLaw.Chattel, _ => SlaveLaw.None };

    /// <summary>
    /// The price of a person (slavery 3): 9,000 mites for an adult of 16 to 50 in health; a trade skill of 40 x1.5, a master of 70 x2 (grown people only); a child under 12 x0.4, 12 to 15 x0.7, over 50 x0.4; maimed x0.5, infirm x0.2.
    /// Each step is an integer permille multiply, in this order.
    /// </summary>
    public long Price(int ageYears, int bestSkill, bool maimed, bool infirm)
    {
        long p = D.BasePriceMites;
        if (ageYears < D.ChildAge) p = p * D.ChildPm / 1000;
        else if (ageYears < D.PrimeMinAge) p = p * D.TeenPm / 1000;
        else if (ageYears > D.PrimeMaxAge) p = p * D.ElderPm / 1000;
        else if (bestSkill >= D.SkillMasterMin) p = p * D.SkillMasterPm / 1000;
        else if (bestSkill >= D.SkillMidMin) p = p * D.SkillMidPm / 1000;
        if (maimed) p = p * D.MaimedPm / 1000;
        if (infirm) p = p * D.InfirmPm / 1000;
        return Math.Max(1, p);
    }
}

/// <summary>
/// What the bondage systems read and write (slavery 2): the law's world (people, households, ledger, chronicle, jobs, captives, marks), the law of each settlement's culture, the numbers, and the few questions slavery asks of other
/// systems. A test builds one by hand; the runtime builds it from the world state (<see cref="BondLink"/>).
/// </summary>
public sealed class BondWorld
{
    public required LawWorld W { get; init; }
    public BondRules Rules { get; init; } = BondRules.Default;
    public SlaveryRulesDef D => Rules.D;
    /// <summary>The law of bondage of a settlement's culture (slavery 3); <see cref="SlaveLaw.None"/> when unknown.</summary>
    public Func<int, SlaveLaw> LawAt { get; init; } = _ => SlaveLaw.None;
    /// <summary>The law of a person's own culture (the person table's culture byte).</summary>
    public Func<int, SlaveLaw> LawOfCulture { get; init; } = _ => SlaveLaw.None;
    /// <summary>The culture index (the person table's byte) of a settlement.</summary>
    public Func<int, int> CultureAt { get; init; } = _ => -1;
    /// <summary>Whether a dead owner's will frees his slaves (the will's book); false: they pass with the estate.</summary>
    public Func<EntityId, bool> WillFrees { get; init; } = _ => false;
    /// <summary>The nearest settlement whose law is none from a place (where a runaway would head), -1 when there is none in the region.</summary>
    public Func<int, int> NearestFreeTown { get; init; } = _ => -1;
    /// <summary>Whether a person has lost a limb (a hand the court took, a stump) and whether he is infirm (very ill or crippled): the price halves or falls to a fifth.</summary>
    public Func<EntityId, bool> Maimed { get; init; } = _ => false;
    public Func<EntityId, bool> Infirm { get; init; } = _ => false;
    /// <summary>A person's leadership skill (the hero's or a row's), for the revolt's leader.</summary>
    public Func<EntityId, int> LeadershipOf { get; init; } = _ => 0;
    /// <summary>The religion index of a person and of a settlement (for the faith's season), -1 none.</summary>
    public Func<EntityId, int> ReligionOf { get; init; } = _ => -1;
    /// <summary>Free armed adults who would put down a revolt at a settlement (its guards).</summary>
    public Func<int, int> GuardsAt { get; init; } = _ => 0;
    /// <summary>Whether a household is hungry today (a missed meal or hunger grievance).</summary>
    public Func<int, bool> HouseholdHungry { get; init; } = _ => false;
    /// <summary>The purse of an owner id (a person's household purse, a household, a fief treasury, a band's own); null when none.</summary>
    public Func<EntityId, PurseId?>? PurseOfOwner { get; init; }
    /// <summary>The settlement a person is at when it matters (the hero's position); null: the home settlement of the row.</summary>
    public Func<EntityId, int>? Here { get; init; }

    public PersonTable People => W.People;
    public Ledger Ledger => W.Ledger!;
    public Chronicle Chronicle => W.Chronicle;
    public ulong Seed => W.Seed;

    /// <summary>The hero's row in the person table when his id does not name it (null: by id).</summary>
    public Func<int>? HeroRow { get; init; }
    /// <summary>P6-E1: told when a holding's bonded have risen (place, leader, the rebels, D1's entry, day): unrest turns it into a revolt of the bonded the guards answer.</summary>
    public Action<int, ulong, IReadOnlyList<ulong>, ulong, int>? Rose { get; init; }
    public int RowOf(EntityId p) { int r = People.IndexOf(p); return r < 0 && p == W.HeroId && HeroRow != null ? HeroRow() : r; }
    public int AgeOf(EntityId p, int day) { int r = RowOf(p); return r < 0 ? 30 : People.AgeYears(r, day); }

    /// <summary>The best of a person's skills (the price counts the trade skill).</summary>
    public int BestSkill(EntityId p)
    {
        int r = RowOf(p); if (r < 0) return 0;
        int best = 0;
        for (int k = 0; k < PersonTable.SkillSlots; k++)
        {
            var s = People.SkillAt(r, k);
            if (s.Skill is Skill.None or Skill.Leadership or Skill.Melee or Skill.Blades or Skill.Axes or Skill.Hafted or Skill.Polearms or Skill.Shields or Skill.Throwing or Skill.Archery) continue;
            best = Math.Max(best, s.Level);
        }
        return best;
    }

    /// <summary>The price table's answer for a person on a day (3).</summary>
    public long PriceOf(EntityId p, int day) => Rules.Price(AgeOf(p, day), BestSkill(p), Maimed(p), Infirm(p));

    /// <summary>The household row an owner id stands for (a person's own household, a household id), -1 for a fief, a business or a band.</summary>
    public int HouseholdOf(EntityId owner)
    {
        if (owner.Kind == IdKind.Household) return W.Households?.IndexOf(owner) ?? -1;
        if (owner.Kind == IdKind.Person) { int r = RowOf(owner); return r >= 0 ? People.Household[r] : -1; }
        return -1;
    }

    public PurseId? PurseOf(EntityId owner)
    {
        if (PurseOfOwner?.Invoke(owner) is { } own) return own;
        if (owner.Kind == IdKind.Person) return W.PurseOf(owner);
        if (owner.Kind == IdKind.Household) return W.VictimPurse(owner.Value);
        foreach (var k in new[] { PurseKind.Treasury, PurseKind.Household, PurseKind.Business })
            if (Ledger.TryFind(k, owner, out var p)) return p;
        return null;
    }

    /// <summary>The slave's own purse (the peculium): the hero's, or a person purse under the slave's id.</summary>
    public PurseId PeculiumPurse(EntityId person) => person == W.HeroId && W.PurseOf(person) is { } hp ? hp : Ledger.Open(PurseKind.Person, person);
    public long Peculium(EntityId person) => (person == W.HeroId && W.PurseOf(person) is { } hp ? Ledger.Balance(hp) : Ledger.TryFind(PurseKind.Person, person, out var p) ? Ledger.Balance(p) : new Coin(0)).Mites;

    /// <summary>The person who speaks for an owner id: himself, or the head of the household.</summary>
    public EntityId OwnerPerson(EntityId owner)
    {
        if (owner.Kind == IdKind.Person) return owner;
        int hh = HouseholdOf(owner);
        if (hh >= 0 && W.Households is { } hht && hht.Head[hh] is var h and >= 0 && h < People.Count) return People.IdOf(h);
        return Slavery.Nobody;
    }

    /// <summary>The settlement an owner is in, for the law of the place (a household's own, a person's here), -1 unknown.</summary>
    public int PlaceOfOwner(EntityId owner)
    {
        if (owner.Kind == IdKind.Person && Here?.Invoke(owner) is { } h and >= 0) return h;
        int hh = HouseholdOf(owner);
        return hh >= 0 && W.Households is { } hht ? hht.Settlement[hh] : owner.Kind == IdKind.Person ? W.HomeOf(owner) : -1;
    }

    /// <summary>The settlement's own name and id for a chronicle place.</summary>
    public ChronParam[] PlaceParams(int settlement) => W.PlaceId(settlement) is { } id && id != EntityId.None ? [ChronParam.Place("place", id)] : [];

    /// <summary>Moves a person to another household (member lists, home bindings); false when there is no table or the row is unknown.</summary>
    public bool Move(EntityId person, int toHousehold)
    {
        var hht = W.Households; int r = RowOf(person);
        if (hht == null || r < 0 || toHousehold < 0 || toHousehold >= hht.Count) return false;
        int from = People.Household[r];
        if (from == toHousehold) return true;
        if (from >= 0) hht.RemoveMember(from, r);
        hht.AddMemberLater(toHousehold, r);
        People.Household[r] = toHousehold; People.HomeSettlement[r] = hht.Settlement[toHousehold]; People.HomeLot[r] = hht.Lot[toHousehold]; People.Bed[r] = -1;
        if (People.Homeless[r] != HomelessReason.None) People.Homeless[r] = HomelessReason.None;
        return true;
    }
}
