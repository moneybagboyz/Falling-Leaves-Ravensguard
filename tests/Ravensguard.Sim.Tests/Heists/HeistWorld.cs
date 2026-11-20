using Ravensguard.Content;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Heists;
using Ravensguard.Sim.Jobs;
using Ravensguard.Sim.Law;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Society;
using Ravensguard.Sim.Tests.Dialogue;

namespace Ravensguard.Sim.Tests.Heists;

/// <summary>
/// A town by hand for the heist's world-side tests (the same kit as the courts' tests): eight guards in the person table (person 0 is the gaoler, the keeper who lodges at the gaol), a fief with a treasury of 200,000 mites, a hero (id None) with a
/// purse, a captive book, a ledger and a chronicle. The law's numbers are the content's.
/// </summary>
internal sealed class HeistWorld
{
    public static readonly EntityId Fief = EntityId.Make(IdKind.Faction, 900), Hero = EntityId.None;
    public Chronicle C = null!; public LawWorld W = null!; public CrimeBook Cb = new(); public JusticeBook Jb = new(); public Ledger L = new(); public JobTable Jobs = new();
    public PurseId Treasury, HeroPurse; public CaptiveBook Cap = null!; public WoundLedger Wounds = null!; public MarkBook Marks = new(); public PersonTable P = null!; public IdAllocator Ids = new();
    public Dictionary<int, PurseId> Hh = [];
    public Func<EntityId, int> Honesty = _ => 45;
    public HeistBook Book = new();

    public static HeistWorld Make(ulong seed = 1234, int people = 80)
    {
        var h = new HeistWorld();
        var (tw, chron) = TalkKit.Town(people, seed); h.C = chron; h.P = tw.People;
        for (int i = 0; i < 8; i++) h.P.Occupation[i] = Occupation.Guard;
        h.Treasury = h.L.Open(PurseKind.Treasury, Fief); h.L.Mint(h.Treasury, new Coin(200_000), SourceReason.Seed);
        h.HeroPurse = h.L.Open(PurseKind.Person, EconomyState.HeroOwner); h.L.Mint(h.HeroPurse, new Coin(20_000), SourceReason.Seed);
        h.Cap = new CaptiveBook(seed); h.Wounds = new WoundLedger(seed);
        var db = HeistTests.Db.Value; var plan = new CombatTables(db).V2.Plan("human");
        var content = new LawContent(db.All<CrimeDef>(), new LawSettingsDef { Id = "law", ArrestPct = 0 }, db.All<PunishmentDef>());
        h.W = new LawWorld
        {
            Chronicle = h.C, Ids = h.Ids, People = h.P, SettlementNames = ["Ashby"], SettlementIds = [EntityId.Make(IdKind.Settlement, 1)], SettlementFief = [0], FiefIds = [Fief], FiefSeat = [0], Seed = seed,
            Ledger = h.L, Jobs = h.Jobs, JobEnv = new JobEnv { Ledger = h.L }, TreasuryOf = _ => h.Treasury, Law = content, Captives = h.Cap, Wounds = h.Wounds, Human = plan, Marks = () => h.Marks,
            CultureKey = (_, k) => k == "theft" ? "fine" : null, PurseOf = h.Purse, VictimPurse = v => v == 0 ? null : h.Purse(new EntityId(v)), LordOf = _ => h.P.IdOf(1), HonestyOf = p => h.Honesty(p),
            RationPayee = _ => null, HeroId = Hero, SkillsOf = _ => (70, 40, 100, 50, 40),
        };
        return h;
    }

    public PurseId? Purse(EntityId p)
    {
        if (p == Hero) return HeroPurse;
        int row = P.IndexOf(p); if (row < 0) return null;
        int hh = P.Household[row];
        if (!Hh.TryGetValue(hh, out var pu)) { pu = L.Open(PurseKind.Household, EntityId.Make(IdKind.Household, (ulong)hh + 1)); L.Mint(pu, new Coin(2_000), SourceReason.Seed); Hh[hh] = pu; }
        return pu;
    }

    public EntityId Id(int row) => P.IdOf(row);
    public static long Sec(int day, int hour = 10) => day * 86_400L + hour * 3600L;

    /// <summary>A person of the town jailed for a crime he did and the gaoler saw: the crime, reported, then the arrest (the cell, the captive, the row).</summary>
    public CrimeRecord Jail(EntityId culprit, string kind, int day, long value = 240)
    {
        var rec = CrimeSystem.Commit(W, Cb, new ActSpec(kind, culprit, Id(21), 0, Sec(day), value, 0, "ch_note_crime_greed"), [new WitnessRow(Id(20).Value, WitnessRow.KnewIt)], ActorLook.None);
        CrimeSystem.Report(W, rec, Id(20), Sec(day + 1, 0));
        Gaol.Arrest(W, Cb, Jb, culprit, 0, Sec(day + 2, 10), inAct: false, Id(0));
        return rec;
    }

    public List<ulong> Real() => GuardRoster.RealOf(P, 0);
}
