using Ravensguard.Sim.Armies;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.WarSupply;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Unrest;
using Ravensguard.Sim.Economy;

namespace Ravensguard.Sim.Siege;

/// <summary>The walls of a besieged place as the siege sees them: the wall store, where the besiegers lay their engines and what they aim at (millimetres in the walls' frame), and the box the breach is looked for in (cells).</summary>
public sealed class SiegeWalls
{
    public required WallWorld World;
    public long LayXMm, LayYMm, AimXMm, AimYMm;
    public int X0, Y0, X1, Y1;
    /// <summary>Mostly timber (a palisade): the campaign brain counts it as one.</summary>
    public bool Timber;
}

/// <summary>What the besiegers build and shoot with: the engine book, the yard that builds at the camp, the shooting world and the camp's store of hauled materials.</summary>
public sealed class SiegeKit
{
    public required EngineBook Book;
    public required EngineYard Yard;
    public required EngineWorld World;
    public required Store Camp;
}

/// <summary>The siege rules' numbers (sieges-and-engines 4.3, 4.6, 4.7), in one place.</summary>
public static class SiegeConfig
{
    public const int LinesDays = 2, StarveDays = 5, ReliefDays = 3, OutnumberedRatio = 4, TermsEveryDays = 5, AssaultCooldown = 4, LiveFallbackHours = 24;
    public const int TrainReachM = 30_000, RansomPct = 25, SackPlunderCoinPct = 40, SackPlunderGoodsPct = 40, BurnOneIn = 5, BurnLossPct = 50, SackKillMinPermille = 10, SackKillSpanPermille = 40;
    public const int WinterDesertPm = 10, ExposurePm = 4, BuildersPerEngine = 80, CrewPerEngine = 8, LiftShortDays = 12;
    public static readonly string[] Engines = ["trebuchet", "trebuchet"];
    /// <summary>Besiegers' power per man and the defenders' per man behind an unbroken wall, behind a breach, in an unwalled place.</summary>
    public const int AttackPower = 100, DefenceWalled = 300, DefenceBreached = 150, DefenceOpen = 120;
}

/// <summary>
/// P7-SG-D: sieges (sieges-and-engines 4.3 to 4.7): a host invests a place, the blockade cuts it off, the besieger chooses to starve it, batter it with engines or storm it, the defender surrenders on terms, and the siege ends by surrender, storm and sack,
/// lifting or abandonment. Every step is a chronicle entry on the chain of the <c>siege_begun</c> entry. The men are the host's and the garrison's real persons; the food is the town's real stock (the blockade is <see cref="HostSupply.SiegeBlockade"/>); coin and
/// goods of a ransom or a sack are ledger transfers and store moves. Built over the engines, walls and assault of SG-A to SG-C through ports (<see cref="WallsOf"/>, <see cref="KitOf"/>, <see cref="OpenAssault"/>), so a world without walls or engines still lays
/// siege by blockade and storm.
/// </summary>
public sealed partial class SiegeWorld
{
    public ArmyWorld Armies { get; }
    public SimWorld W { get; }
    private readonly Func<SiegeBook?> _peek;
    private readonly Func<SiegeBook> _make;

    /// <summary>The walls of a place (null: unwalled), the engines' kit for a siege (null: no engines can be built) and the live assault's opener (true: the bubble has the assault and will call <see cref="EndAssault"/>). Ports, not saved.</summary>
    public Func<int, SiegeWalls?>? WallsOf { get; set; }
    public Func<SiegeRow, SiegeKit?>? KitOf { get; set; }
    public Func<SiegeRow, bool>? OpenAssault { get; set; }
    /// <summary>The live assaults the hero's bubble holds (the <see cref="OpenAssault"/> port's owner); null in a world with none.</summary>
    public SiegeAssaults? Assaults { get; set; }
    internal long LastHour => _lastHour;
    internal List<ulong> MenOf(SiegeRow s) => Besiegers(s);
    internal EntityId PlaceOf(int place) => PlaceId(place);
    /// <summary>The besieger's policy of starving a place out and never storming it (the campaign brain's choice, AM-E; a test's switch). Not saved.</summary>
    public bool NeverStorm { get; set; }

    public SiegeWorld(SimWorld w, ArmyWorld armies, Func<SiegeBook?> peek, Func<SiegeBook> make)
    {
        W = w; Armies = armies; _peek = peek; _make = make;
        armies.ExtraDesertPm = host => _peek() is { } b && b.OfHost(HostOfSupply(host)) is { } s && s.Open && InWinter() ? SiegeConfig.WinterDesertPm : 0;
    }

    public SiegeBook Book => _make();
    public SiegeBook? Peek => _peek();
    private HostSupply? Hs => Armies.Supply;
    private bool InWinter() => GameTime.FromGameHours(_lastHour).Season == Season.Winter;
    private long _lastHour;
    private ArmyEnv? _env;

    /// <summary>The chronicle writer for the walls', engines' and yard's own entries (<c>wall_breached</c>, <c>engine_built</c>): it writes into the tick that is stepping the siege (the outbox), so a shot of the hour lands in that hour's barrier. Set it on <see cref="WallWorld.Chronicle"/>, <see cref="EngineWorld.Chronicle"/> and <see cref="EngineYard.Chronicle"/>.</summary>
    public SiegeChronicle Writer => (when, type, key, ps, subjects, causes, data) => _env is { } env ? env.Chronicler.Chron(when, type, key, ps, subjects, causes, data) : EntityId.None;

    private int HostOfSupply(int supply) { foreach (var h in Armies.Book.Hosts.Values) if (h.Supply == supply && supply != 0) return h.Id; return -1; }
    private EntityId PlaceId(int s) => (uint)s < (uint)Armies.Region.Settlements.Count ? Armies.Region.Settlements[s].Id : EntityId.None;

    private EntityId Chron(ArmyEnv e, string type, string key, IReadOnlyList<ChronParam> ps, IReadOnlyList<EntityId> subjects, Cause cause)
        => e.Chronicler.Chron(e.Time, type, key, ps, subjects, [cause]);
    private static Cause OnSiege(SiegeRow s, string note) => new(CauseKind.Siege, s.Entry == 0 ? null : new EntityId(s.Entry), "", note);

    // ------------------------------------------------------------------ investing

    /// <summary>
    /// A host that has arrived at a place with a siege objective invests it (4.3): camps (the host's mode becomes SiegeRow), its state Besieging, and <c>siege_begun</c> is written on the host's muster (or the war's declaration through it). The lines go up within
    /// <see cref="SiegeConfig.LinesDays"/> days and the place is blockaded then. Returns null when the host or place is unknown, the place already besieged or the host already besieging.
    /// </summary>
    public SiegeRow? Invest(ArmyEnv e, int hostId, int place)
    {
        var hs = Hs; if (hs == null || !Armies.Book.Hosts.TryGetValue(hostId, out var host) || host.Supply == 0 || !hs.Exists(new HostId(host.Supply))) return null;
        if ((uint)place >= (uint)Armies.Region.Settlements.Count || Peek?.OfPlace(place) != null || Peek?.OfHost(hostId) != null) return null;
        var hid = new HostId(host.Supply);
        Armies.EndMarch(hid);
        hs.Update(hid, hs.PlaceAt(place), HostMode.Siege);
        host.State = HostState.Besieging;
        var men = Armies.MenIdsOf(hs, hid, host); var garrison = Garrison(place);
        var book = Book;
        var s = new SiegeRow { Id = book.Next++, Place = place, Host = hostId, Supply = host.Supply, Realm = host.Realm, StartDay = (int)e.Day, State = SiegeState.Investing, MenAtStart = men.Count, GarrisonAtStart = garrison.Count };
        var walls = WallsOf?.Invoke(place);
        if (walls != null) { s.CampXMm = walls.LayXMm; s.CampYMm = walls.LayYMm; }
        var cause = host.Entry != 0 ? new Cause(CauseKind.Campaign, new EntityId(host.Entry), "", "ch_note_sg_marched") : new Cause(CauseKind.Campaign, null, "", "ch_note_sg_marched");
        var subj = new List<EntityId> { PlaceId(place) }; if (host.Commander != 0) subj.Add(new EntityId(host.Commander));
        s.Entry = Chron(e, "siege_begun", "ch_sg_begun", [ChronParam.Person("besieger", new EntityId(host.Commander)), ChronParam.Place("place", PlaceId(place)), ChronParam.Count("men", men.Count), ChronParam.Count("garrison", garrison.Count)], subj, cause).Value;
        book.Sieges[s.Id] = s;
        return s;
    }

    // ------------------------------------------------------------------ the two sides

    /// <summary>The defenders of a place: the men of the warbands standing in it and the guards who live there, the living, by id.</summary>
    public List<ulong> Garrison(int place)
    {
        var pt = Armies.People; var set = new SortedSet<ulong>();
        foreach (var wb in Armies.Book.Warbands.Values) if (wb.Here == place) foreach (ulong m in wb.Members) { int r = Armies.RowOf(m); if (r >= 0 && pt.Status[r] == PersonStatus.Alive) set.Add(m); }
        for (int r = 0; r < pt.Count; r++) if (pt.Status[r] == PersonStatus.Alive && pt.HomeSettlement[r] == place && pt.Occupation[r] is Occupation.Guard or Occupation.Soldier && !Armies.Book.IsAway(pt.IdOf(r).Value)) set.Add(pt.IdOf(r).Value);
        return [.. set];
    }

    private List<ulong> Besiegers(SiegeRow s)
    {
        var hs = Hs; if (hs == null || !hs.Exists(new HostId(s.Supply))) return [];
        Armies.Book.Hosts.TryGetValue(s.Host, out var host);
        return Armies.MenIdsOf(hs, new HostId(s.Supply), host);
    }

    private bool Hero(ulong person) => Armies.IsHero(person);

    // ------------------------------------------------------------------ ends

    private void Close(SiegeRow s, SiegeState state, string why, EntityId end, ArmyEnv e)
    {
        s.State = state; s.Why = why; s.EndDay = (int)e.Day; s.EndEntry = end.Value; s.LiveSinceHour = -1;
        var hs = Hs;
        if (hs != null && hs.IsBlockaded(s.Place)) hs.SiegeBlockade(s.Place, false, new HostId(s.Supply));
        if (Armies.Book.Hosts.TryGetValue(s.Host, out var host) && host.State == HostState.Besieging) host.State = HostState.Camped;
        if (hs != null && hs.Exists(new HostId(s.Supply))) hs.Update(new HostId(s.Supply), hs.PlaceAt(s.Place), HostMode.Camp);
    }

    /// <summary>The besieger leaves (supply gone, winter, a relief host near and stronger, the host dissolved): the place is free, <c>siege_abandoned</c> (the spec's lifted) on the chain.</summary>
    public void Lift(ArmyEnv e, SiegeRow s, string why)
    {
        var end = Chron(e, "siege_abandoned", "ch_sg_lifted", [ChronParam.Place("place", PlaceId(s.Place)), ChronParam.Days("days", e.Day - s.StartDay)], [PlaceId(s.Place)], OnSiege(s, why));
        Close(s, SiegeState.Lifted, why, end, e);
    }

    /// <summary>The place yields on terms (4.3): the garrison goes out with its arms and the lord pays a ransom of a quarter of his purse to the besieger's chest (a ledger transfer); the siege ends.</summary>
    public void Surrender(ArmyEnv e, SiegeRow s, string why)
    {
        long ransom = PayRansom(s);
        var end = Chron(e, "siege_surrendered", "ch_sg_surrendered", [ChronParam.Place("place", PlaceId(s.Place)), ChronParam.TextKey("terms", "ch_sg_terms_arms"), ChronParam.Mites("ransom", ransom), ChronParam.Count("men", Garrison(s.Place).Count)], [PlaceId(s.Place)], OnSiege(s, why));
        s.PlunderMites += ransom;
        Close(s, SiegeState.Surrendered, why, end, e);
    }

    private long PayRansom(SiegeRow s)
    {
        var hs = Hs; var ew = Armies.Ew; if (hs == null || ew == null) return 0;
        var fi = Armies.Region.Settlements[s.Place].FiefIndex; var rb = Armies.Rule;
        if (rb == null || fi < 0 || !rb.Fiefs.TryGetValue(fi, out var f) || f.Holder < 0) return 0;
        int hh = Armies.HouseholdOf((ulong)f.Holder); if (hh < 0) return 0;
        var led = W.Economy.Ledger; var purse = W.Economy.HouseholdPurse[hh];
        long pay = led.Balance(purse).Mites * SiegeConfig.RansomPct / 100; if (pay <= 0) return 0;
        return led.Transfer(purse, hs.ChestOf(new HostId(s.Supply)), new Coin(pay), TransferReason.Tribute) ? pay : 0;
    }
}
