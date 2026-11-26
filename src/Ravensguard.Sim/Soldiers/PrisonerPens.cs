using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Rule;

namespace Ravensguard.Sim.Soldiers;

/// <summary>What the world tells the pens (4.7); every hook is optional. With no feeding hook nobody is fed, so a pen the world does not feed empties in two days (the Phase 6 CJ-16 rule).</summary>
public sealed class PenHooks
{
    /// <summary>Feeds a prisoner his kilogram of food from the host's stock (true: fed today). Only goods that exist: the world takes them from a real store.</summary>
    public Func<PrisonerRow, bool>? Feed { get; set; }
    /// <summary>Where the captor's culture allows war captives (Phase 6 Bondage): the buyer's purse and the price in mites for this prisoner, or null when no sale is legal or no buyer can pay.</summary>
    public Func<PrisonerRow, (PurseId Buyer, long Price)?>? SaleOffer { get; set; }
    /// <summary>Told when a sale went through (the bondage book takes him: the row and the buyer's purse).</summary>
    public Action<PrisonerRow, PurseId>? Sold { get; set; }
    /// <summary>A lord who cannot pay cedes a fief to the captor (Phase 6 DA-11): true when the cession was made and the lord is free.</summary>
    public Func<PrisonerRow, bool>? Cede { get; set; }
    /// <summary>Told when a prisoner is killed.</summary>
    public Action<ulong>? Killed { get; set; }
    /// <summary>Told when a parole is broken: the person (the world puts standing -200 on him).</summary>
    public Action<ulong>? ParoleBroken { get; set; }
}

/// <summary>A person taken in a battle, as the sorting sees him: his rank, whether his kin or lord have the means, and the purse asked for his ransom (a ledger index, -1 none).</summary>
public readonly record struct Taken(ulong Person, CaptiveRank Rank, bool Means = false, int PayerPurse = -1, bool PayerIsLord = false);

/// <summary>What the commander is like, for the fate of the common prisoners (4.7).</summary>
public enum Commander : byte { Just, Greedy, Cruel }

public enum PrisonerFate : byte { Parole, Work, Exchange, Sold, Killed }

/// <summary>What <see cref="PrisonerPens.Take"/> did: how many are held, of which ransom class, how many were let go and how many killed.</summary>
public sealed record TakeReport(int Held, int Ransom, int Common, int LetGo, int Killed, ulong Entry);

/// <summary>
/// P7-SC-D (soldiers-and-companies 4.7): prisoners at scale. After a battle the holder keeps what he can guard (a guard to ten); the few worth a ransom are sorted from the many by rank and means; ransom is asked of real purses
/// (halved after 5 days unpaid, quartered after 10, a lord who cannot pay cedes); the many are paroled, put to work, exchanged at the peace or sold where the law has a market, by the commander's traits. Held prisoners
/// eat (a hook feeds them from a real store); unfed two days they go free. Coin only moves purse to purse; nothing here is created.
/// </summary>
public static class PrisonerPens
{
    public const int PerGuard = 10, CruelKillPm = 500, ParoleDays = 90, ParoleBreakPm = 100, UnfedReleaseDays = 2, ChronicleFrom = 20;

    /// <summary>The sorting (pure): ransom class for a man-at-arms or better, or anyone whose kin or lord have the means; the rest are common.</summary>
    public static PrisonerClass Sort(in Taken t) => t.Rank >= CaptiveRank.ManAtArms || t.Means ? PrisonerClass.Ransom : PrisonerClass.Common;

    /// <summary>The price asked at the start: the rank's (a means-only levy is asked the levy's price).</summary>
    public static long ValueOf(AftermathRules r, in Taken t) => Ransom.RankBase(r, t.Rank == CaptiveRank.Wanderer ? CaptiveRank.Levy : t.Rank);

    /// <summary>What a lord of this temper does with the common prisoners (4.7): just paroles, greedy sells where legal (else works), cruel works or kills.</summary>
    public static PrisonerFate FateFor(Commander c, bool saleLegal) => c switch
    {
        Commander.Just => PrisonerFate.Parole,
        Commander.Greedy => saleLegal ? PrisonerFate.Sold : PrisonerFate.Work,
        _ => PrisonerFate.Work,
    };

    private static EntityId CaptorId(long lord, long company) => company >= 0 ? EntityId.Make(IdKind.Faction, 0x4B00_0000UL + (ulong)company) : new EntityId((ulong)Math.Max(0, lord));
    private static GameTime At(int day) => GameTime.FromGameHours((long)day * 24);

    /// <summary>
    /// Takes the prisoners of a battle: the ransom class first (dearest first), then the common by person id, up to a guard to ten (<paramref name="guards"/>); the rest are let go, or killed when the commander is cruel
    /// (stream 0x7B3, key battle and person: 500 permille each). Each held man gets a row in the pen; a ransom is asked from <paramref name="day"/>. Pure in the inputs and the seed.
    /// </summary>
    public static TakeReport Take(SoldierWorld sw, AftermathRules r, RuleChron chron, ulong seed, ulong battle, int day, long lord, long company, int pen, PurseId captorPurse, int guards, Commander commander, IReadOnlyList<Taken> captives)
    {
        int cap = Math.Max(0, guards) * PerGuard, held = 0, ransom = 0, common = 0, letGo = 0, killed = 0;
        var ordered = captives.Where(t => !sw.Book.Prisoners.ContainsKey(t.Person)).OrderByDescending(t => Sort(t) == PrisonerClass.Ransom).ThenByDescending(t => ValueOf(r, t)).ThenBy(t => t.Person).ToList();
        var who = CaptorId(lord, company);
        var entry = chron.Chron(At(day), "prisoners_taken", "ch_so_prisoners", [ChronParam.Count("count", ordered.Count)], [who], [new Cause(CauseKind.Campaign, null, NoteKey: "ch_note_so_prisoners")],
            ("battle", battle.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        foreach (var t in ordered)
        {
            if (held >= cap)
            {
                if (commander == Commander.Cruel && DetRng.Range(seed, (long)battle, t.Person, RngStreams.PrisonersKilled, 0, 0, 1000) < CruelKillPm) { killed++; sw.PenHooks.Killed?.Invoke(t.Person); }
                else letGo++;
                continue;
            }
            var cls = Sort(t);
            sw.Book.Prisoners[t.Person] = new PrisonerRow
            {
                Person = t.Person, Captor = lord, Company = company, Pen = pen, Since = day, Class = cls, ValueMites = ValueOf(r, t), DueDay = day, PayerPurse = cls == PrisonerClass.Ransom ? t.PayerPurse : -1,
                CaptorPurse = captorPurse.IsNone ? -1 : captorPurse.Index, PayerIsLord = t.PayerIsLord, Entry = entry.Value,
            };
            held++; if (cls == PrisonerClass.Ransom) ransom++; else common++;
        }
        return new TakeReport(held, ransom, common, letGo, killed, entry.Value);
    }

    /// <summary>
    /// The commander's choice for the common prisoners the pen holds under this captor (4.7): all of them go to the fate the temper picks (a cruel commander kills each by a draw of 0x7B3 and puts the rest to work; a sale
    /// that finds no buyer becomes work). A chronicle entry follows for 20 or more, caused by the capture. Returns how many were given a fate.
    /// </summary>
    public static int Settle(SoldierWorld sw, RuleChron chron, ulong seed, int day, long lord, long company, Commander commander, bool saleLegal, int exchangeWar = -1)
    {
        var fate = FateFor(commander, saleLegal); int n = 0; ulong entry = 0;
        foreach (var row in sw.Book.Prisoners.Values.Where(p => p.Class == PrisonerClass.Common && p.Captor == lord && p.Company == company && p.ParoleUntil < 0 && p.Work == "" && p.ExchangeWar < 0).ToList())
        {
            var f = fate;
            if (commander == Commander.Cruel && DetRng.Range(seed, day, row.Person, RngStreams.PrisonersKilled, 1, 0, 1000) < CruelKillPm) f = PrisonerFate.Killed;
            if (exchangeWar >= 0 && f == PrisonerFate.Work) f = PrisonerFate.Exchange;
            entry = row.Entry; Apply(sw, row, f, day, exchangeWar); n++;
        }
        if (n >= ChronicleFrom)
            chron.Chron(At(day), "prisoners_fate", "ch_so_fate_" + fate.ToString().ToLowerInvariant(), [ChronParam.Count("count", n)], [CaptorId(lord, company)], [new Cause(CauseKind.Campaign, new EntityId(entry))]);
        return n;
    }

    /// <summary>The hero (or any captor) puts one prisoner to a fate. Selling needs a legal buyer who can pay from the hook; otherwise the man is put to work instead. Returns the fate he got.</summary>
    public static PrisonerFate Apply(SoldierWorld sw, PrisonerRow row, PrisonerFate fate, int day, int exchangeWar = -1)
    {
        switch (fate)
        {
            case PrisonerFate.Parole: row.ParoleUntil = day + ParoleDays; row.Work = ""; row.UnfedDays = 0; return fate;
            case PrisonerFate.Exchange: row.ExchangeWar = Math.Max(0, exchangeWar); return fate;
            case PrisonerFate.Killed: sw.Book.Prisoners.Remove(row.Person); sw.PenHooks.Killed?.Invoke(row.Person); return fate;
            case PrisonerFate.Sold:
                if (sw.PenHooks.SaleOffer?.Invoke(row) is { } o && o.Price > 0 && row.CaptorPurse >= 0 && sw.Econ.Ledger.Balance(o.Buyer).Mites >= o.Price
                    && sw.Econ.Ledger.Transfer(o.Buyer, new PurseId(row.CaptorPurse), new Coin(o.Price), TransferReason.SlaveSale))
                {
                    sw.Book.Prisoners.Remove(row.Person); sw.PenHooks.Sold?.Invoke(row, o.Buyer); return fate;
                }
                row.Work = "camp"; return PrisonerFate.Work;
            default: row.Work = "camp"; return PrisonerFate.Work;
        }
    }

    /// <summary>The peace: every prisoner of the war's lists, on both sides, is exchanged and goes home. Returns how many.</summary>
    public static int Exchange(SoldierWorld sw, int war)
    {
        var go = sw.Book.Prisoners.Values.Where(p => p.ExchangeWar == war).Select(p => p.Person).ToList();
        foreach (var p in go) sw.Book.Prisoners.Remove(p);
        return go.Count;
    }

    /// <summary>What the captors ask of this prisoner today: the full ransom, half after 5 days past the due day, a quarter after 10.</summary>
    public static long AskToday(AftermathRules r, PrisonerRow row, int day) => Ransom.Current(r, row.ValueMites, row.DueDay, day);

    /// <summary>
    /// H5 hour 0: parole runs out (broken by a draw of 0x7B4 on its last day: a crime, the world puts standing -200 on the man), the held are fed (a hook; unfed two days in a row they are released), and the ransoms ask
    /// their price of the payer's real purse, paid whole or not at all (a company's captive's ransom is shared by its contract). A lord who cannot pay the quartered ransom cedes (the hook).
    /// </summary>
    public static void Day(SoldierWorld sw, AftermathRules r, RuleChron chron, ulong seed, int day)
    {
        var l = sw.Econ.Ledger;
        foreach (var row in sw.Book.Prisoners.Values.ToList())
        {
            if (row.ParoleUntil >= 0)
            {
                if (day < row.ParoleUntil - 1) continue;
                sw.Book.Prisoners.Remove(row.Person);
                if (DetRng.Range(seed, row.ParoleUntil / ParoleDays, row.Person, RngStreams.ParoleBroken, 0, 0, 1000) < ParoleBreakPm)
                {
                    sw.PenHooks.ParoleBroken?.Invoke(row.Person);
                    chron.Chron(At(day), "parole_broken", "ch_so_parole_broken", [ChronParam.Person("person", new EntityId(row.Person))], [new EntityId(row.Person)], [new Cause(CauseKind.Campaign, new EntityId(row.Entry))]);
                }
                continue;
            }
            if (sw.PenHooks.Feed?.Invoke(row) == true) row.UnfedDays = 0; else row.UnfedDays++;
            if (row.UnfedDays >= UnfedReleaseDays) { sw.Book.Prisoners.Remove(row.Person); continue; }
            if (row.Class != PrisonerClass.Ransom || row.PayerPurse < 0) continue;
            long ask = AskToday(r, row, day); var payer = new PurseId(row.PayerPurse);
            if (ask > 0 && PayRansom(sw, row, payer, ask))
            {
                sw.Book.Prisoners.Remove(row.Person);
                chron.Chron(At(day), "ransom_paid", "ch_so_ransom", [ChronParam.Person("captive", new EntityId(row.Person)), ChronParam.Mites("amount", ask)], [new EntityId(row.Person), CaptorId(row.Captor, row.Company)],
                    [new Cause(CauseKind.Campaign, new EntityId(row.Entry))]);
            }
            else if (row.PayerIsLord && day - row.DueDay >= r.D.QuarterAfterDays && l.Balance(payer).Mites < ask && sw.PenHooks.Cede?.Invoke(row) == true)
            {
                sw.Book.Prisoners.Remove(row.Person);
                chron.Chron(At(day), "ransom_ceded", "ch_so_ransom_ceded", [ChronParam.Person("captive", new EntityId(row.Person))], [new EntityId(row.Person), CaptorId(row.Captor, row.Company)],
                    [new Cause(CauseKind.Campaign, new EntityId(row.Entry))]);
            }
        }
    }

    private static bool PayRansom(SoldierWorld sw, PrisonerRow row, PurseId payer, long ask)
    {
        var l = sw.Econ.Ledger;
        if (l.Balance(payer).Mites < ask) return false;
        if (row.Company >= 0 && sw.Book.Companies.TryGetValue(row.Company, out var c)) return MercCompanies.ShareRansom(l, c, payer, ask) > 0;
        return row.CaptorPurse >= 0 && new PurseId(row.CaptorPurse) != payer && l.Transfer(payer, new PurseId(row.CaptorPurse), new Coin(ask), AftermathCoin.Ransom);
    }
}
