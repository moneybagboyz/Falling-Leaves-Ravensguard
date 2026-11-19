using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Succession;

/// <summary>Why an heir inherits (morale-capture-succession 4.8, the fixed order).</summary>
public enum SuccessionRule : byte { Child, Spouse, Sibling, YoungChild, Parent, Companion, Follower }

/// <summary>A squad member as succession reads it: who, how loyal (permille), when they joined, recruited (a companion) or hired.</summary>
public readonly record struct SquadMember(EntityId Person, int PersonIndex, int LoyaltyPm, int JoinedDay, bool Recruited, string Name = "");

/// <summary>One possible heir in order: the person, the rule, the age, and for a child of 12-15 the guardian (the most loyal follower, placed with them).</summary>
public sealed record SuccessionCandidate(EntityId Person, int PersonIndex, SuccessionRule Rule, int AgeYears, string Name, EntityId Guardian = default);

/// <summary>What the dead leave where they fell (3.2 <c>Estate</c>): the items on the body, the coin after the funeral, who inherits, and whether it was claimed or looted.</summary>
public sealed class Estate
{
    public int Id;
    public EntityId Dead;
    public long PlaceX, PlaceY;
    public List<EntityId> Items = [];
    public long CoinMites, FuneralMites;
    public EntityId Heir;
    public bool Claimed, Looted;
    public int Day;
}

/// <summary>The record of a succession (3.2 <c>InheritanceRecord</c>): deeds and reputation are carried as pending (Phase 5 and 6 make them real).</summary>
public sealed class InheritanceRecord
{
    public EntityId From, To;
    public SuccessionRule Rule;
    public long Coin;
    public int EstateId;
    public List<string> DeedsPending = [];
    public string ReputationPending = "";
    public int GameDay;
    /// <summary>A quest hint for the heir ("recover {name}'s sword"): the shape of the best weapon left on the body, empty when none.</summary>
    public string RecoverShape = "";
    public EntityId Guardian;
}

/// <summary>The run's successions: estates and inheritance records in order, and whether the run has ended (nobody left: the world persists for a new character).</summary>
public sealed class SuccessionBook
{
    public readonly List<Estate> Estates = [];
    public readonly List<InheritanceRecord> Records = [];
    public bool RunEnded;
    public EntityId CurrentHero;
    /// <summary>The living hero's kin in the person table (P4 close-out: the hero is not a row, so the family is linked here at a new game, and from the heir's own row after a succession).</summary>
    public readonly List<HeroKinLink> Kin = [];

    /// <summary>The <c>EstateHash</c> of the daily trail (6).</summary>
    public ulong Hash()
    {
        ulong h = 14695981039346656037UL;
        void L(long v) => h = Ravensguard.Sim.Combat.Melee2.FightMath.Mix(h, v);
        foreach (var e in Estates)
        {
            L(e.Id); L((long)e.Dead.Value); L(e.PlaceX); L(e.PlaceY); foreach (var i in e.Items) L((long)i.Value); L(e.CoinMites); L(e.FuneralMites); L((long)e.Heir.Value);
            L(e.Claimed ? 1 : 0); L(e.Looted ? 1 : 0); L(e.Day);
        }
        foreach (var r in Records)
        {
            L((long)r.From.Value); L((long)r.To.Value); L((long)r.Rule); L(r.Coin); L(r.EstateId); L(r.DeedsPending.Count); L(r.ReputationPending.Length); L(r.GameDay); L(r.RecoverShape.Length); L((long)r.Guardian.Value);
        }
        L(RunEnded ? 1 : 0); L((long)CurrentHero.Value);
        if (Kin.Count > 0) { L(Kin.Count); foreach (var k in Kin) { L((long)k.Person.Value); L((long)k.Relation); } }   // only a book with kin hashes them (older hashes unchanged)
        return h;
    }
}

/// <summary>
/// Ironman succession by kinship (morale-capture-succession 4.8): <see cref="Order"/> is a pure function of the person table and the squad (no random draw: stream 0x457 is reserved and
/// never used) giving the candidates in the fixed order — children 16 and over eldest first, the spouse, siblings 16 and over eldest first, children of 12 to 15 (with the most loyal follower as
/// guardian), parents, recruited companions by loyalty (then the earliest recruited, then the lowest id), any other follower; ties in a rank by entity id. Eligible: alive, at least 12, not
/// captive and not paralysed from the neck (<paramref name="unavailable"/>). <see cref="Settle"/> moves the estate: the body's coin to the heir less a 5% funeral to the church or lord, the
/// items stay on the body (what was robbed is gone).
/// </summary>
public static class SuccessionOrder
{
    public const int AdultAge = 16, MinAge = 12;
    public const int FuneralPm = 50;
    public const int StayLoyaltyPm = 500;

    public static List<SuccessionCandidate> Order(PersonTable pt, int hero, int nowDay, IReadOnlyList<SquadMember> squad, Func<int, EntityId, bool>? unavailable = null)
    {
        var list = new List<SuccessionCandidate>();
        var seen = new HashSet<ulong>();
        bool Ok(int i) => i >= 0 && i < pt.Count && i != hero && pt.Status[i] == PersonStatus.Alive && pt.AgeYears(i, nowDay) >= MinAge && !(unavailable?.Invoke(i, pt.IdOf(i)) ?? false);
        void Add(int i, SuccessionRule rule, EntityId guardian = default)
        {
            var id = pt.IdOf(i);
            if (!seen.Add(id.Value)) return;
            list.Add(new SuccessionCandidate(id, i, rule, pt.AgeYears(i, nowDay), pt.FullName(i), guardian));
        }
        IEnumerable<int> Eldest(IEnumerable<int> rows) => rows.OrderBy(i => pt.BirthDay[i]).ThenBy(i => pt.IdOf(i).Value);
        var children = new List<int>(); var siblings = new List<int>();
        if (hero >= 0 && hero < pt.Count)
        {
            int f = pt.Father[hero], m = pt.Mother[hero];
            for (int i = 0; i < pt.Count; i++)
            {
                if (i == hero) continue;
                if (pt.Father[i] == hero || pt.Mother[i] == hero) children.Add(i);
                else if (f >= 0 && pt.Father[i] == f || m >= 0 && pt.Mother[i] == m) siblings.Add(i);
            }
            foreach (int i in Eldest(children)) if (Ok(i) && pt.AgeYears(i, nowDay) >= AdultAge) Add(i, SuccessionRule.Child);
            int sp = pt.Spouse[hero];
            if (Ok(sp)) Add(sp, SuccessionRule.Spouse);
            foreach (int i in Eldest(siblings)) if (Ok(i) && pt.AgeYears(i, nowDay) >= AdultAge) Add(i, SuccessionRule.Sibling);
            var guardian = MostLoyal(squad, pt, nowDay, unavailable);
            foreach (int i in Eldest(children)) if (Ok(i) && pt.AgeYears(i, nowDay) < AdultAge) Add(i, SuccessionRule.YoungChild, guardian);
            foreach (int p in new[] { f, m }.Where(p => p >= 0).OrderBy(p => pt.IdOf(p).Value)) if (Ok(p) && pt.AgeYears(p, nowDay) >= AdultAge) Add(p, SuccessionRule.Parent);
        }
        foreach (var s in Ranked(squad.Where(s => s.Recruited))) if (s.PersonIndex < 0 || Ok(s.PersonIndex)) AddMember(s, SuccessionRule.Companion);
        foreach (var s in Ranked(squad.Where(s => !s.Recruited))) if (s.PersonIndex < 0 || Ok(s.PersonIndex)) AddMember(s, SuccessionRule.Follower);
        return list;

        void AddMember(SquadMember s, SuccessionRule rule)
        {
            if (!seen.Add(s.Person.Value)) return;
            list.Add(new SuccessionCandidate(s.Person, s.PersonIndex, rule, s.PersonIndex >= 0 ? pt.AgeYears(s.PersonIndex, nowDay) : AdultAge, s.PersonIndex >= 0 ? pt.FullName(s.PersonIndex) : s.Name));
        }
    }

    private static IEnumerable<SquadMember> Ranked(IEnumerable<SquadMember> m) => m.OrderByDescending(s => s.LoyaltyPm).ThenBy(s => s.JoinedDay).ThenBy(s => s.Person.Value);

    private static EntityId MostLoyal(IReadOnlyList<SquadMember> squad, PersonTable pt, int nowDay, Func<int, EntityId, bool>? unavailable) =>
        Ranked(squad.Where(s => s.PersonIndex < 0 || pt.Status[s.PersonIndex] == PersonStatus.Alive && !(unavailable?.Invoke(s.PersonIndex, s.Person) ?? false))).Select(s => s.Person).FirstOrDefault();

    /// <summary>The heir: the first candidate, or none (the run ends; the world persists).</summary>
    public static SuccessionCandidate? Heir(PersonTable pt, int hero, int nowDay, IReadOnlyList<SquadMember> squad, Func<int, EntityId, bool>? unavailable = null) =>
        Order(pt, hero, nowDay, squad, unavailable).FirstOrDefault();

    /// <summary>
    /// The estate at the death (4.8): the dead's coin goes to the heir less a 5% funeral (to <paramref name="funeral"/>, the local church or lord), through the ledger; the items stay on the body
    /// unless robbed (<paramref name="robbed"/> are gone and are not listed); the record carries the deeds and reputation as pending. With no heir the coin stays in the dead's purse (the
    /// estate waits) and the run ends.
    /// </summary>
    public static (Estate Estate, InheritanceRecord? Record) Settle(SuccessionBook book, Ledger ledger, EntityId dead, PurseId deadPurse, SuccessionCandidate? heir, PurseId heirPurse,
        PurseId funeral, IReadOnlyList<EntityId> itemsOnBody, IReadOnlyCollection<EntityId> robbed, int day, long placeX = 0, long placeY = 0, string recoverShape = "")
    {
        long coin = ledger.Balance(deadPurse).Mites;
        long fee = heir == null ? 0 : coin * FuneralPm / 1000;
        if (fee > 0 && funeral != deadPurse) ledger.Transfer(deadPurse, funeral, Coin.FromMites(fee), Ravensguard.Sim.Combat.Aftermath.AftermathCoin.Funeral);
        long rest = ledger.Balance(deadPurse).Mites;
        if (heir != null && rest > 0 && heirPurse != deadPurse) ledger.Transfer(deadPurse, heirPurse, Coin.FromMites(rest), Ravensguard.Sim.Combat.Aftermath.AftermathCoin.Inherit);
        var est = new Estate
        {
            Id = book.Estates.Count + 1, Dead = dead, PlaceX = placeX, PlaceY = placeY, Items = [.. itemsOnBody.Where(i => !robbed.Contains(i))], CoinMites = heir == null ? rest : rest,
            FuneralMites = fee, Heir = heir?.Person ?? EntityId.None, Claimed = false, Looted = robbed.Count > 0, Day = day,
        };
        book.Estates.Add(est);
        InheritanceRecord? rec = null;
        if (heir != null)
        {
            rec = new InheritanceRecord { From = dead, To = heir.Person, Rule = heir.Rule, Coin = rest, EstateId = est.Id, GameDay = day, RecoverShape = recoverShape, Guardian = heir.Guardian };
            book.Records.Add(rec);
            book.CurrentHero = heir.Person;
        }
        else book.RunEnded = true;
        return (est, rec);
    }

    /// <summary>Who stays with the heir (4.8): kin always, companions with loyalty 500 or more; the rest disperse.</summary>
    public static List<EntityId> Stay(IReadOnlyList<SquadMember> squad, IReadOnlySet<EntityId> kin, EntityId heir) =>
        [.. squad.Where(s => s.Person != heir && (kin.Contains(s.Person) || s.LoyaltyPm >= StayLoyaltyPm)).Select(s => s.Person)];

    /// <summary>The non-Ironman death screen's choices: the order as it is (SU-4); Ironman takes the first without asking.</summary>
    public static IReadOnlyList<SuccessionCandidate> Options(IReadOnlyList<SuccessionCandidate> order, bool ironman) => ironman ? (order.Count > 0 ? [order[0]] : []) : order;

    /// <summary>The heir as the new hero (4.8: attributes and skills from the person row; origin "heir").</summary>
    public static HeroRecord HeroFrom(PersonTable pt, int row, string name)
    {
        var attrs = new int[5];
        for (int i = 0; i < 5; i++) attrs[i] = Math.Clamp(pt.Attribute(row, i), 1, HeroRecord.MaxLevel);
        var skills = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in HeroRecord.SkillIds) skills[id] = 0;
        for (int k = 0; k < PersonTable.SkillSlots; k++) { var s = pt.SkillAt(row, k); if (s.Skill != Skill.None) skills[HeroRecord.SkillId(s.Skill)] = Math.Clamp(s.Level, 0, HeroRecord.MaxLevel); }
        HeroRecord.DefaultWeaponFamilies(skills, skills.Where(kv => kv.Value > 0).Select(kv => kv.Key));
        return new HeroRecord { Name = name, Female = pt.Sex[row] == Sex.Female, Origin = "heir", Attributes = attrs, Skills = skills, Inventory = [], Coin = 0, AmbitionId = "", Milestone = 0 };
    }
}
