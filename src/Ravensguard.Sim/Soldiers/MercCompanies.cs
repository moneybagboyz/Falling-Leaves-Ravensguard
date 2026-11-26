using Ravensguard.Sim.Contracts;
using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Rule;

namespace Ravensguard.Sim.Soldiers;

/// <summary>What the world tells a company's daily choices (4.6); every hook is optional and defaults to nothing happening.</summary>
public sealed class CompanyHooks
{
    /// <summary>An enemy of a company's employer offering it x1.5 the wage: the rival lord's id and his purse (a ledger index); null when no offer stands.</summary>
    public Func<MercCompany, (long Lord, int Purse)?>? RivalOffer { get; set; }
    /// <summary>Whether the employer's lands are near the company (it may plunder them on the way out).</summary>
    public Func<MercCompany, bool>? EmployerLandsNear { get; set; }
    /// <summary>Settlements where wars are rumoured (idle companies roam toward them).</summary>
    public Func<IReadOnlyList<int>>? WarPlaces { get; set; }
    /// <summary>Told when a company plunders on its way out (the lands' raid, as a warband's) or lives off the land (bandit raids, reputation below 0): the company and the settlement.</summary>
    public Action<MercCompany, int, bool>? Raid { get; set; }
}

public enum HireResult : byte { Hired, NoSuchCompany, Busy, BadTerms, CannotPay, Refused }

/// <summary>
/// P7-SC-C (soldiers-and-companies 4.6): mercenary companies, groups of real people with a captain, a purse, a reputation and a contract (Phase 6 <c>mercenary_service</c>). The employer pays the company's purse a day's wage a
/// day (a ledger transfer; a purse that cannot is arrears); the company pays its men every seventh day, a real transfer to each man's household, his wage less 10% for the captain. Loyalty is the company's daily choice
/// (streams 0x7B0, 0x7B2): grumble, leave, plunder, turn once. Between contracts a company roams to where wars are, or lives off the land. Nothing here mints coin: every payment is a transfer between purses.
/// </summary>
public static class MercCompanies
{
    public const int CaptainCutPct = 10, PayEveryDays = 7, LeaveAtArrearsDays = 6, GrumbleMorale = 5;
    public const int KeptRep = 50, BrokenRep = -200, TurnedRep = -300, LeftStanding = -200, TurnReputationBelow = 300, TurnPm = 250, PlunderPm = 750, RoamPm = 300;
    public const int MinDays = 15, MaxDays = 60, AdvanceDays = 6;
    public const int MinMen = 20, MaxMen = 200;

    public static EntityId IdOf(MercCompany c) => EntityId.Make(IdKind.Faction, 0x4B00_0000UL + (ulong)c.Id);

    /// <summary>One man's mercenary wage a day in mites (the role's x1.5, rounded down; a company that has turned asks x1.5 on that).</summary>
    public static long WageMites(SoldierWorld sw, MercCompany c, ulong person) =>
        sw.Book.Of(person) is { } r ? (long)sw.Roles.WageCp(r.Role, mercenary: true) * 10 * c.WageMultPm / 1000 : 0;

    public static long DailyMites(SoldierWorld sw, MercCompany c) => c.Members.Sum(m => WageMites(sw, c, m));

    /// <summary>The company's view as the contract board takes it (so an idle company with a purse takes guard contracts by the same scorer); null before its purse exists.</summary>
    public static CompanyInfo? InfoOf(MercCompany c) =>
        c.Purse < 0 || c.Hired ? null : new CompanyInfo(IdOf(c), c.Name, Math.Max(0, c.Settlement), c.Members.Count * 12, c.Members.Count, c.Purse, "mercenary");

    // ------------------------------------------------------------------ seeding

    private static readonly string[] Epithets = ["Black", "Grey", "Red", "White", "Free", "Iron", "Wolf", "Raven", "Boar", "Stag", "Hawk", "Bear", "Golden", "Broken", "Lone", "Sworn"];
    private static readonly string[] Nouns = ["Company", "Lances", "Banner", "Brotherhood", "Spears", "Band", "Fellowship", "Blades"];

    /// <summary>
    /// Makes one company out of <paramref name="candidates"/> (adult men with no soldier row, any order: sorted here): its size is drawn 20 to 200 (stream 0x7B1, key the company), the men are the lowest draws, the roles
    /// a historical mix (a sixth archers, a tenth crossbowmen, an eighth men-at-arms, the rest spearmen; the captain a sergeant), the captain the man of best Leadership and Tactics (ties by id). Returns null when
    /// fewer than 20 men are free. No coin and no purse yet.
    /// </summary>
    public static MercCompany? Seed(SoldierWorld sw, ulong seed, long id, int settlement, IReadOnlyList<ulong> candidates, int maxMen = MaxMen)
    {
        var free = candidates.Where(p => sw.Book.Of(p) == null).Distinct().OrderBy(p => p).ToList();
        int size = Math.Min(Math.Min(maxMen, MaxMen), DetRng.Range(seed, 0, (ulong)id, RngStreams.CompanySeeding, 0, MinMen, MaxMen + 1));
        size = Math.Min(size, free.Count);
        if (size < MinMen) return null;
        var picked = free.OrderBy(p => DetRng.NextUInt(seed, 0, (ulong)id, RngStreams.CompanySeeding, (uint)(1 + (p & 0x3FFFFFFF)))).ThenBy(p => p).Take(size).OrderBy(p => p).ToList();
        ulong cap = picked.OrderByDescending(p => sw.SkillOf(p, Skill.Leadership) + sw.SkillOf(p, Skill.Tactics)).ThenBy(p => p).First();
        var c = new MercCompany
        {
            Id = id, Captain = cap, Settlement = settlement, Reputation = 0,
            Name = "The " + Epithets[DetRng.Range(seed, 0, (ulong)id, RngStreams.CompanySeeding, 1_000_000, 0, Epithets.Length)] + " " + Nouns[DetRng.Range(seed, 0, (ulong)id, RngStreams.CompanySeeding, 1_000_001, 0, Nouns.Length)],
        };
        int i = 0;
        foreach (var p in picked)
        {
            string role = p == cap ? "sergeant" : (i++ % 24) switch { < 4 => "archer", < 6 => "crossbowman", < 9 => "man_at_arms", _ => "spearman" };
            sw.Book.Rows[p] = new SoldierRow { Person = p, Role = role, Lord = -1 };
            c.Members.Add(p);
        }
        sw.Book.Companies[id] = c;
        return c;
    }

    // ------------------------------------------------------------------ hiring

    /// <summary>
    /// Hires a company for <paramref name="days"/> (15 to 60) as a <c>mercenary_service</c> contract: the advance, half of the first six days' wages, leaves the employer's purse into the contract's escrow at once and is
    /// the company's when the service is done; the wage a day runs through <see cref="Day"/>. The company's purse is opened on its first hire. Armoury gaps are filled when <paramref name="armoury"/> is given and the terms say so.
    /// </summary>
    public static HireResult Hire(ContractWorld w, ContractBook book, SoldierWorld sw, long companyId, EntityId employer, long employerLord, PurseId employerPurse, int settlement, int days, bool fillGaps, int day, long armoury = -1,
        int plunderSharePm = 333, int ransomSharePm = 333)
    {
        if (!sw.Book.Companies.TryGetValue(companyId, out var c)) return HireResult.NoSuchCompany;
        if (c.Hired || book.Contracts.Values.Any(k => k.State == WorkState.Taken && k.ByCompany && k.Taker == IdOf(c))) return HireResult.Busy;          // a company on a guard job is not for hire
        if (plunderSharePm < 0 || plunderSharePm > 1000 || ransomSharePm < 0 || ransomSharePm > 1000) return HireResult.BadTerms;
        if (days < MinDays || days > MaxDays || employerLord < 0 || employerPurse.IsNone) return HireResult.BadTerms;
        long advance = DailyMites(sw, c) * AdvanceDays / 2;
        if (advance <= 0 || w.Ledger.Balance(employerPurse).Mites < advance) return HireResult.CannotPay;
        var cause = new ChronicleEntry(w.Ids.Allocate(IdKind.Event), new GameTime(day * 86_400L), "company_for_hire", [employer], [new Cause(CauseKind.Campaign, null, NoteKey: "ch_note_so_company")],
            new Dictionary<string, string> { ["company"] = c.Name }, TextKey: "ch_so_company_need", Params: [ChronParam.Count("men", c.Members.Count), ChronParam.Count("days", days)]);
        w.Chronicle.Add(cause);
        if (c.Purse < 0) c.Purse = w.Ledger.Open(PurseKind.Chest, IdOf(c)).Index;
        var k = ContractSystem.Post(w, book, "mercenary_service", employer, employerPurse, settlement, advance, day, cause, x => { x.Param = days; x.DeadlineDay = day + days; });
        if (k == null) return HireResult.Refused;
        if (ContractSystem.Take(w, book, k.Id, IdOf(c), new PurseId(c.Purse), day, byCompany: true) != TakeResult.Taken) { ContractSystem.Cancel(w, book, k.Id, day); return HireResult.Refused; }
        c.ContractId = k.Id; c.Employer = employerLord; c.EmployerPurse = employerPurse.Index; c.HiredDay = day; c.EndDay = day + days;
        c.ArrearsDays = 0; c.OwedMites = 0; c.Morale = 0; c.FillGaps = fillGaps; c.PlunderSharePm = plunderSharePm; c.RansomSharePm = ransomSharePm;
        if (fillGaps && armoury >= 0) sw.FillFrom(armoury, sw.SoldiersOf(-1, r => c.Members.Contains(r.Person)), day, employerLord);
        return HireResult.Hired;
    }

    // ------------------------------------------------------------------ shares of the contract (P7-SC-D)

    /// <summary>
    /// A ransom the company's captive paid, shared by the contract: the payer's coin goes to the employer's purse (the company's own when it is between contracts), and the company's share (<see cref="MercCompany.RansomSharePm"/>)
    /// moves on from there, never more than that purse holds. Returns what the payer paid; 0 when it could not (nothing moves).
    /// </summary>
    public static long ShareRansom(Ledger l, MercCompany c, PurseId payer, long askMites)
    {
        if (askMites <= 0 || c.Purse < 0 || payer.IsNone) return 0;
        var co = new PurseId(c.Purse); var to = c.Hired && c.EmployerPurse >= 0 ? new PurseId(c.EmployerPurse) : co;
        if (payer == to || l.Balance(payer).Mites < askMites || !l.Transfer(payer, to, new Coin(askMites), AftermathCoin.Ransom)) return 0;
        if (to != co) l.Transfer(to, co, new Coin(Math.Min(l.Balance(to).Mites, askMites * c.RansomSharePm / 1000)), TransferReason.Plunder);
        return askMites;
    }

    /// <summary>
    /// Loot the company took, shared by the contract: its share of the coin (<see cref="MercCompany.PlunderSharePm"/>) from the purse that holds it, never more than the purse holds, and its share of the pieces (whole
    /// pieces, the lowest ids first) from the owner that holds them, only pieces that exist and are that owner's. Returns the (mites, pieces) that moved.
    /// </summary>
    public static (long Mites, int Pieces) SharePlunder(Ledger l, ItemTable items, MercCompany c, PurseId lootPurse, long lootMites, EntityId lootOwner, IReadOnlyList<EntityId> lootItems)
    {
        if (c.Purse < 0) return (0, 0);
        long coin = 0; var co = new PurseId(c.Purse);
        if (lootMites > 0 && !lootPurse.IsNone && lootPurse != co)
        {
            long share = Math.Min(l.Balance(lootPurse).Mites, lootMites * c.PlunderSharePm / 1000);
            if (share > 0 && l.Transfer(lootPurse, co, new Coin(share), TransferReason.Plunder)) coin = share;
        }
        int pieces = 0, want = (int)((long)lootItems.Count * c.PlunderSharePm / 1000);
        foreach (var id in lootItems.OrderBy(i => i.Value))
        {
            if (pieces >= want) break;
            int row = items.IndexOf(id); if (row < 0 || items.At(row).Owner != lootOwner) continue;
            items.At(row).Owner = IdOf(c); pieces++;
        }
        return (coin, pieces);
    }

    // ------------------------------------------------------------------ the day

    /// <summary>
    /// The companies' day, in id order: the employer pays (or the pay falls into arrears), every seventh day the company pays its men, a finished service is counted and the loyalty rule runs; an idle company
    /// roams. Chronicle entries for what a company does carry the hire's posting as their cause.
    /// </summary>
    public static void Day(ContractWorld w, ContractBook book, SoldierWorld sw, RuleChron chron, int day, CompanyHooks? hooks = null)
    {
        hooks ??= sw.CompanyHooks;
        foreach (var c in sw.Book.Companies.Values.ToList())
        {
            if (!c.Hired) { Roam(w, sw, chron, c, day, hooks); continue; }
            if (c.ContractId >= 0 && (book.Get(c.ContractId) is not { } k || k.Closed))
            {
                if (book.Get(c.ContractId)?.State == WorkState.Done) { c.Reputation = Math.Min(1000, c.Reputation + KeptRep); c.Kept++; }
                Release(sw, c); continue;
            }
            if (c.ContractId < 0 && day >= c.EndDay) { Release(sw, c); continue; }       // a service begun after a turn ends by its day
            PayCompany(w, sw, c);
            if ((day - c.HiredDay) % PayEveryDays == PayEveryDays - 1) PayMen(w, sw, c);
            Loyalty(w, book, sw, chron, c, day, hooks);
        }
    }

    /// <summary>
    /// A service ends (done, failed, unpaid or run out): the kit the employer's armoury issued goes back to it, with the wear it has (a piece lost on campaign is just gone), and the company is idle again. Returns the
    /// pieces that went back.
    /// </summary>
    public static int ReturnKit(SoldierWorld sw, MercCompany c)
    {
        int back = 0;
        foreach (var m in c.Members)
        {
            if (sw.Book.Of(m) is not { } row || row.Issued.Count == 0) continue;
            foreach (var grp in row.Issued.GroupBy(i => i.Armoury).OrderBy(g => g.Key)) back += sw.Armouries.Return(grp.Key, [.. grp.OrderBy(i => i.Item).Select(i => new EntityId(i.Item))]);
            var gone = row.Issued.Select(i => i.Item).ToHashSet();
            foreach (var slot in row.Worn.Where(w => gone.Contains(w.Value)).Select(w => w.Key).ToList()) row.Worn.Remove(slot);
            row.Issued.Clear();
        }
        return back;
    }

    private static void Release(SoldierWorld sw, MercCompany c) { ReturnKit(sw, c); Release(c); }

    private static void Release(MercCompany c) { c.Employer = -1; c.EmployerPurse = -1; c.ContractId = -1; c.ArrearsDays = 0; c.OwedMites = 0; c.Morale = 0; c.HiredDay = -1; c.EndDay = -1; c.FillGaps = false; }

    private static void PayCompany(ContractWorld w, SoldierWorld sw, MercCompany c)
    {
        long owed = c.OwedMites + DailyMites(sw, c);
        var from = new PurseId(c.EmployerPurse);
        if (c.Purse >= 0 && w.Ledger.Balance(from).Mites >= owed && w.Ledger.Transfer(from, new PurseId(c.Purse), new Coin(owed), TransferReason.Wage)) { c.OwedMites = 0; c.ArrearsDays = 0; }
        else { c.OwedMites = owed; c.ArrearsDays++; }
    }

    /// <summary>
    /// The company pays its men a week's wage each, a real transfer to the man's household purse: his wage less 10%, the cuts to the captain on top of his own. What the purse does not hold is shared pro rata; a man with no
    /// household purse is left out. Returns the mites paid.
    /// </summary>
    public static long PayMen(ContractWorld w, SoldierWorld sw, MercCompany c, int days = PayEveryDays)
    {
        if (c.Purse < 0) return 0;
        var from = new PurseId(c.Purse);
        var due = new List<(ulong P, PurseId To, long Net)>(); long cuts = 0;
        foreach (var m in c.Members)
        {
            var to = sw.PurseOf(m); if (to.IsNone) continue;
            long weekly = WageMites(sw, c, m) * days;
            if (m == c.Captain) { due.Add((m, to, weekly)); continue; }
            long cut = weekly * CaptainCutPct / 100; cuts += cut; due.Add((m, to, weekly - cut));
        }
        int ci = due.FindIndex(d => d.P == c.Captain);
        if (ci >= 0) due[ci] = due[ci] with { Net = due[ci].Net + cuts };
        long total = due.Sum(d => d.Net), bal = w.Ledger.Balance(from).Mites; if (total <= 0 || bal <= 0) return 0;
        long paid = 0;
        foreach (var d in due)
        {
            long amt = bal >= total ? d.Net : d.Net * bal / total;
            if (amt > 0 && w.Ledger.Transfer(from, d.To, new Coin(amt), TransferReason.Wage)) paid += amt;
        }
        return paid;
    }

    private static void Loyalty(ContractWorld w, ContractBook book, SoldierWorld sw, RuleChron chron, MercCompany c, int day, CompanyHooks hooks)
    {
        var posted = c.ContractId >= 0 && book.Get(c.ContractId) is { } k ? new EntityId(k.PostedEntry) : (EntityId?)null;
        // an enemy's better offer: once, with reputation below 300 (stream 0x7B2, key company and day)
        if (!c.Turned && c.Reputation < TurnReputationBelow && hooks.RivalOffer?.Invoke(c) is { } offer && offer.Lord >= 0 && offer.Lord != c.Employer
            && DetRng.Range(w.Seed, day, (ulong)c.Id, RngStreams.ChangingSides, 0, 0, 1000) < TurnPm)
        {
            long old = c.Employer; int oldContract = c.ContractId;
            if (oldContract >= 0 && book.Get(oldContract) is { Live: true } kc) ContractSystem.Fail(w, book, kc, day, "company_turned");
            c.Turned = true; c.WageMultPm = 1500; c.Reputation = Math.Max(-1000, c.Reputation + TurnedRep);
            c.Employer = offer.Lord; c.EmployerPurse = offer.Purse; c.ContractId = -1; c.ArrearsDays = 0; c.OwedMites = 0; c.HiredDay = day; c.EndDay = Math.Max(day + MinDays, c.EndDay);
            Note(chron, day, "company_turns", "ch_so_company_turns", c, old, posted);
            return;
        }
        if (c.ArrearsDays == 0) return;
        if (c.ArrearsDays < LeaveAtArrearsDays) { c.Morale -= GrumbleMorale; return; }
        // six days short: the company leaves, plundering the employer's lands when they are near and its name is bad (stream 0x7B0)
        bool plunder = c.Reputation < 0 && hooks.EmployerLandsNear?.Invoke(c) == true && DetRng.Range(w.Seed, day, (ulong)c.Id, RngStreams.CompanyLoyalty, 0, 0, 1000) < PlunderPm;
        long employer = c.Employer;
        if (c.ContractId >= 0 && book.Get(c.ContractId) is { Live: true } kl) ContractSystem.Fail(w, book, kl, day, "company_unpaid");
        sw.Book.Standing[employer] = Math.Max(-1000, sw.Book.Standing.GetValueOrDefault(employer) + LeftStanding);
        Note(chron, day, plunder ? "company_plunders" : "company_leaves", plunder ? "ch_so_company_plunders" : "ch_so_company_leaves", c, employer, posted);
        if (plunder) hooks.Raid?.Invoke(c, c.Settlement, true);
        Release(sw, c);
    }

    private static void Roam(ContractWorld w, SoldierWorld sw, RuleChron chron, MercCompany c, int day, CompanyHooks hooks)
    {
        if (c.Purse >= 0 && (day % PayEveryDays) == 0) PayMen(w, sw, c);
        if (DetRng.Range(w.Seed, day, (ulong)c.Id, RngStreams.CompanyLoyalty, 1, 0, 1000) >= RoamPm) return;
        var wars = hooks.WarPlaces?.Invoke();
        if (wars is { Count: > 0 })
        {
            int to = wars[DetRng.Range(w.Seed, day, (ulong)c.Id, RngStreams.CompanyLoyalty, 2, 0, wars.Count)];
            if (to != c.Settlement) c.Settlement = to;
        }
        else if (c.Reputation < 0) hooks.Raid?.Invoke(c, c.Settlement, false);          // lives off the land
    }

    private static void Note(RuleChron chron, int day, string type, string key, MercCompany c, long employer, EntityId? posted) =>
        chron.Chron(GameTime.FromGameHours((long)day * 24), type, key, [ChronParam.Count("men", c.Members.Count), ChronParam.Person("captain", new EntityId(c.Captain))], [new EntityId(c.Captain)],
            posted is { } p ? [new Cause(CauseKind.Campaign, p)] : [new Cause(CauseKind.Campaign, null, NoteKey: "ch_note_so_company")], ("company", c.Name), ("employer", employer.ToString(System.Globalization.CultureInfo.InvariantCulture)));
}
