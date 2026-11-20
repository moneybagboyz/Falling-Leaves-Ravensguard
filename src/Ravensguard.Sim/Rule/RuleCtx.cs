using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Taxes;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Rule;

/// <summary>Where the rule's chronicle entries go: the tick's outbox (published at the barrier) or the chronicle directly (a hero's command at the barrier, a test). Every entry carries a text key and typed parameters.</summary>
public sealed class RuleChron
{
    private readonly Func<GameTime, string, string, IReadOnlyList<ChronParam>, IReadOnlyList<EntityId>, IReadOnlyList<Cause>, (string, string)[], EntityId> _write;
    private RuleChron(Func<GameTime, string, string, IReadOnlyList<ChronParam>, IReadOnlyList<EntityId>, IReadOnlyList<Cause>, (string, string)[], EntityId> w) { _write = w; }

    public static RuleChron Of(RegionOutbox ob) => new((when, type, key, ps, subj, causes, data) => ob.Chronicle(when, type, key, ps, subj, causes, data));
    public static RuleChron Of(Chronicle chronicle, IdAllocator ids) => new((when, type, key, ps, subj, causes, data) =>
    {
        var id = ids.Allocate(IdKind.Event);
        chronicle.Add(new ChronicleEntry(id, when, type, subj, causes, data.ToDictionary(d => d.Item1, d => d.Item2), TextKey: key, Params: ps));
        return id;
    });
    /// <summary>Writes nothing and returns no id (a world that keeps no chronicle).</summary>
    public static readonly RuleChron Silent = new((_, _, _, _, _, _, _) => EntityId.None);

    public EntityId Chron(GameTime when, string type, string textKey, IReadOnlyList<ChronParam> ps, IReadOnlyList<EntityId> subjects, IReadOnlyList<Cause> causes, params (string, string)[] data) => _write(when, type, textKey, ps, subjects, causes, data);
}

/// <summary>An embezzlement the rule has to give to the law (rule-and-income 4.8): who, of whom, where, what was taken and who saw it.</summary>
public sealed record SkimCrime(EntityId Doer, EntityId Victim, int Settlement, long Sec, long Value, IReadOnlyList<EntityId> Witnesses, string NoteKey, bool Report, EntityId Reporter);

/// <summary>
/// What the rule's steps read and write, gathered so a test can build the pieces by hand and the tick can pass its own: the world, the book, the economy and its treasuries, where the chronicle goes, and the few hooks into
/// the law and the land. Every purse a party names is found here, so every payment is one ledger transfer between real purses.
/// </summary>
public sealed class RuleCtx
{
    public required SimWorld W { get; init; }
    public required RuleBook B { get; init; }
    public EconomyWorld? Ew { get; init; }
    public RuleChron Chron { get; init; } = RuleChron.Silent;
    /// <summary>Hands an embezzlement to the law (B2's crime core): null when no law is installed (a bare world).</summary>
    public Func<SkimCrime, EntityId>? Crime { get; init; }
    /// <summary>The land world (charters, claims) when there is one.</summary>
    public LandWorld? Land { get; init; }
    /// <summary>The justice step's guards take an eviction order over (claim serial, fief, day): true when they will finish it with <see cref="RuleLand.Complete"/>; null or false carries the order out at once.</summary>
    public Func<int, int, int, bool>? GuardsEnforce { get; init; }
    /// <summary>The hero's person id, for his own deeds (none: empty).</summary>
    public EntityId Hero { get; init; }
    /// <summary>Whether <see cref="Hero"/> is a real hero (the hero's id can be the zero id, so it cannot be told from none by value).</summary>
    public bool HasHero { get; init; }
    /// <summary>A person's standing with a realm in permille (the standing book's), 0 when none.</summary>
    public Func<EntityId, int, int> StandingWithRealm { get; init; } = (_, _) => 0;
    /// <summary>Whether a ruler's realm is at war and why (for the council's NPC rule): "" none, "defence" or "casus_belli".</summary>
    public Func<int, string> WarCause { get; init; } = _ => "";

    public Ledger Led => W.Economy.Ledger;
    public RegionData Region => W.Population.Region;
    public PersonTable People => W.People;
    public HouseholdTable Households => W.Households;
    public Treasuries? T => Ew?.Taxes.Treasuries;
    public TaxSystem? Tax => Ew?.Taxes;
    public ulong Seed => W.Seed;
    public Ravensguard.Content.RuleSettingsDef S => B.Settings;

    // ------------------------------------------------------------------ names and keys

    public EntityId RealmKey(int realm) => LandWorld.RealmTreasuryId(realm);
    public EntityId FiefKey(int fief) => (uint)fief < (uint)Region.Fiefs.Count ? Region.Fiefs[fief].Id : EntityId.None;
    public string FiefName(int fief) => (uint)fief < (uint)Region.Fiefs.Count ? Region.Fiefs[fief].Name : "";
    public string RealmName(int realm) => Region.Realms.FirstOrDefault(r => r.HistoryIndex == realm)?.Name ?? (realm >= 0 ? $"realm {realm}" : "");
    public EntityId SeatPlace(int fief) { int s = (uint)fief < (uint)Region.Fiefs.Count ? Region.Fiefs[fief].SeatSettlement : -1; return s >= 0 && s < Region.Settlements.Count ? Region.Settlements[s].Id : EntityId.None; }

    /// <summary>The key an opinion or a chronicle subject uses for a party.</summary>
    public EntityId KeyOf(Party p) => p.Kind switch
    {
        PartyKind.Fief => FiefKey((int)p.Id),
        PartyKind.Realm => RealmKey((int)p.Id),
        _ => new EntityId((ulong)p.Id),
    };

    /// <summary>The purse a party pays from or is paid into: a fief's or realm's treasury, a household's purse, a person's own (the hero's) or his household's.</summary>
    public PurseId PurseOf(Party p)
    {
        switch (p.Kind)
        {
            case PartyKind.Fief:
            {
                var t = T; if (t == null || p.Id < 0 || p.Id >= t.FiefSlot.Length || t.FiefSlot[p.Id] < 0) return PurseId.None;
                return t[t.FiefSlot[p.Id]].Purse;
            }
            case PartyKind.Realm:
            {
                var t = T; if (t == null) return PurseId.None;
                var id = RealmKey((int)p.Id);
                return Led.TryFind(PurseKind.Treasury, id, out var pr) ? pr : PurseId.None;
            }
            case PartyKind.Household:
                return Led.TryFind(PurseKind.Household, new EntityId((ulong)p.Id), out var hp) ? hp : PurseId.None;
            default:
            {
                var id = new EntityId((ulong)p.Id);
                if (Led.TryFind(PurseKind.Person, id, out var pp) && Led.Balance(pp).Mites > 0) return pp;
                int row = People.IndexOf(id);
                if (row >= 0) { int hh = People.Household[row]; if (hh >= 0 && hh < W.Economy.HouseholdCount) return W.Economy.HouseholdPurse[hh]; }
                return Led.TryFind(PurseKind.Person, id, out pp) ? pp : PurseId.None;
            }
        }
    }

    public long Balance(Party p) { var pr = PurseOf(p); return pr.IsNone ? 0 : Led.Balance(pr).Mites; }

    /// <summary>Moves coin from one party to another by a ledger transfer; true only when the whole sum moved.</summary>
    public bool Pay(Party from, Party to, long mites, TransferReason why)
    {
        if (mites <= 0) return true;
        var a = PurseOf(from); var b = PurseOf(to);
        return !a.IsNone && !b.IsNone && a != b && Led.Balance(a).Mites >= mites && Led.Transfer(a, b, new Coin(mites), why);
    }

    /// <summary>Moves what the payer holds up to <paramref name="mites"/> and returns what moved.</summary>
    public long PayUpTo(Party from, Party to, long mites, TransferReason why)
    {
        var a = PurseOf(from); var b = PurseOf(to);
        if (a.IsNone || b.IsNone || a == b) return 0;
        long pay = Math.Min(mites, Led.Balance(a).Mites);
        return pay > 0 && Led.Transfer(a, b, new Coin(pay), why) ? pay : 0;
    }

    // ------------------------------------------------------------------ the people

    public int RowOf(long person) => person < 0 ? -1 : People.IndexOf(new EntityId((ulong)person));
    public bool Alive(long person) { int r = RowOf(person); return r >= 0 && People.IsAlive(r); }
    public string NameOf(long person) { int r = RowOf(person); return r >= 0 ? People.FullName(r) : ""; }
    public int Law(long person) { int r = RowOf(person); return r >= 0 ? People.SkillLevelOf(r, Skill.Law) : 0; }

    /// <summary>The household purse party of a person (the lord's own coin), or the person himself when he has no household here (the hero).</summary>
    public Party HouseholdOf(long person)
    {
        int r = RowOf(person);
        if (r >= 0) { int hh = People.Household[r]; if (hh >= 0 && hh < Households.Count) return Party.OfHousehold(Households.IdOf(hh)); }
        return Party.OfPerson(new EntityId((ulong)person));
    }

    /// <summary>The households of a fief (members alive), in ascending row.</summary>
    public IEnumerable<int> HouseholdsOfFief(int fief)
    {
        var hht = Households;
        for (int h = 0; h < hht.Count; h++)
        {
            if (hht.MemberCount[h] == 0) continue;
            int s = hht.Settlement[h]; if ((uint)s >= (uint)Region.Settlements.Count) continue;
            if (Region.Settlements[s].FiefIndex == fief) yield return h;
        }
    }

    /// <summary>Households with members living in a settlement.</summary>
    public int HouseholdsInSettlement(int settlement) { int n = 0; var h = Households; for (int i = 0; i < h.Count; i++) if (h.MemberCount[i] > 0 && h.Settlement[i] == settlement) n++; return n; }

    /// <summary>The realm treasury party above a fief, none when it has no realm.</summary>
    public Party OverlordOf(FiefRow f) => f.OverlordFief >= 0 ? Party.OfFief(f.OverlordFief) : f.Realm >= 0 ? Party.OfRealm(f.Realm) : Party.None;

    public static GameTime At(int day) => GameTime.FromGameHours((long)day * 24);
}
