using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Soldiers;

namespace Ravensguard.Sim.Armies;

public sealed partial class ArmyWorld
{
    private static readonly string[] NotRetinueRoles = ["levy", "knight", "engineer", "sailor", "marine"];

    // ------------------------------------------------------------------ chronicle

    private EntityId Chron(ArmyEnv e, string type, string key, IReadOnlyList<ChronParam> ps, IReadOnlyList<EntityId> subjects, Cause cause)
        => e.Chronicler.Chron(e.Time, type, key, ps, subjects, [cause]);

    internal EntityId PlaceId(int settlement) => (uint)settlement < (uint)Region.Settlements.Count ? Region.Settlements[settlement].Id : EntityId.None;

    // ------------------------------------------------------------------ who is hired

    /// <summary>The role a retainer holds: the best of the retinue roles he meets by his skills (priority order), else the levy's (the retainer wage).</summary>
    public string RoleFor(ulong person)
    {
        if (Soldiers == null) return "levy";
        foreach (var r in Soldiers.Roles.Roles)
        {
            if (Array.IndexOf(NotRetinueRoles, r.Id) >= 0 || r.Dubbed) continue;
            if (Soldiers.Roles.Meets(r.Id, s => Soldiers.SkillOf(person, s))) return r.Id;
        }
        return "levy";
    }

    private int MeleeOf(int row) { var pt = People; int b = pt.SkillLevelOf(row, Skill.Melee); foreach (var k in new[] { Skill.Blades, Skill.Axes, Skill.Hafted, Skill.Polearms }) b = Math.Max(b, pt.SkillLevelOf(row, k)); return b; }

    /// <summary>The men a fief's lord could hire, best first: his household's guards (already his men), the other grown men of his household, then the idle and the labourers and servants of the fief's households who are not their house's head. A pure listing: no draw.</summary>
    public List<(int Cat, int Row)> Candidates(ArmyEnv e, FiefRow f, ulong lord)
    {
        var res = new List<(int Cat, int Skill, ulong Id, int Row)>();
        var pt = People; var ht = Households; int day = (int)e.Day;
        var inFief = new HashSet<int>(Region.Fiefs[f.Fief].Settlements);
        int lordRow = RowOf(lord), lordHh = lordRow >= 0 ? pt.Household[lordRow] : -1;
        var war = Ew?.War;
        for (int h = 0; h < ht.Count; h++)
        {
            if (ht.MemberCount[h] == 0 || !inFief.Contains(ht.Settlement[h])) continue;
            bool lordsHouse = h == lordHh;
            foreach (int m in ht.Members(h))
            {
                if (pt.Sex[m] != Sex.Male || pt.Status[m] != PersonStatus.Alive || m == lordRow) continue;
                int age = pt.AgeYears(m, day); if (age < Rules.MinAge || age > Rules.MaxAge) continue;
                ulong id = pt.IdOf(m).Value;
                if (IsHero(id) || Book.IsAway(id) || Soldiers?.Book.Rows.ContainsKey(id) == true || war?.IsOnCampaign(m) == true) continue;
                var occ = pt.Occupation[m];
                if (occ is Occupation.Lord or Occupation.Priest or Occupation.Steward or Occupation.Mayor or Occupation.GuildMaster or Occupation.Reeve or Occupation.Physician or Occupation.Monk or Occupation.Bandit) continue;
                int cat;
                if (lordsHouse) cat = occ == Occupation.Guard ? 0 : 1;
                else
                {
                    if (ht.Head[h] == m || IsStaff(m) || pt.Employer[m] >= 0) continue;
                    cat = occ == Occupation.None ? 2 : occ is Occupation.Labourer or Occupation.Servant ? 3 : -1;
                    if (cat < 0) continue;
                }
                res.Add((cat, MeleeOf(m), id, m));
            }
        }
        res.Sort((a, b) => a.Cat != b.Cat ? a.Cat.CompareTo(b.Cat) : a.Skill != b.Skill ? b.Skill.CompareTo(a.Skill) : a.Id.CompareTo(b.Id));
        return [.. res.Select(r => (r.Cat, r.Row))];
    }

    /// <summary>The most a lord's wage roll can bear a season, in men who are on no payroll but his: the share of what he takes in a season (the last year's receipts over four, else a fourth of the purse he holds), over what one man costs a season.</summary>
    public int PaidMenBudget(ArmyEnv e, FiefRow f, ulong lord)
    {
        var c = RuleCtx(e); if (c == null) return 0;
        long season = f.LastYearReceipts / 4;
        if (season <= 0) season = c.Balance(c.HouseholdOf((long)lord)) / 4;
        long budget = season * Rules.WageRollMaxPct / 100;
        return (int)Math.Min(100_000, budget / Math.Max(1, (long)Rules.RetainerWageMitesDay * TimeConst.DaysPerSeason));
    }

    // ------------------------------------------------------------------ raising a warband

    private static void AddSorted(List<ulong> list, ulong id) { int i = list.BinarySearch(id); if (i < 0) list.Insert(~i, id); }

    /// <summary>Takes the men on: the occupation of a hired man becomes soldier (what he did is kept to give back), his row is made, and he is listed.</summary>
    private void Enlist(ArmyEnv e, Warband wb, int row, int cat)
    {
        ulong id = People.IdOf(row).Value;
        if (cat != 0) { wb.Prev[id] = (byte)People.Occupation[row]; People.Occupation[row] = Occupation.Soldier; }
        AddSorted(wb.Members, id);
        if (Soldiers != null) Soldiers.Book.Rows[id] = new SoldierRow { Person = id, Role = RoleFor(id), Lord = (long)wb.Lord, PromotedDay = (int)e.Day };
        Book.Touch();
    }

    private void SetCaptain(Warband wb)
    {
        ulong best = 0; int bs = -1;
        foreach (ulong m in wb.Members) { int r = RowOf(m); if (r < 0) continue; int s = MeleeOf(r); if (s > bs || (s == bs && m < best)) { bs = s; best = m; } }
        wb.Captain = best;
    }

    /// <summary>
    /// Raises a lord's warband (4.1, the seeding of overview 7): the men are real people of his household and fief, hired by the order of <see cref="Candidates"/> up to the holding's size and what the wage roll bears;
    /// nobody is made. A lord with no man to hire has none (asked again next season). Returns the warband, null when none was raised.
    /// </summary>
    public Warband? Raise(ArmyEnv e, FiefRow f)
    {
        if (f.Holder < 0 || f.Forfeited || (uint)f.Fief >= (uint)Region.Fiefs.Count) return null;
        ulong lord = (ulong)f.Holder;
        if (IsHero(lord) || RowOf(lord) < 0 || !People.IsAlive(RowOf(lord)) || Book.WarbandOfFief(f.Fief) != null) return null;
        int seat = Region.Fiefs[f.Fief].SeatSettlement; if ((uint)seat >= (uint)Region.Settlements.Count) return null;
        var wb = new Warband { Id = Book.NextWarband, Lord = lord, Fief = f.Fief, Realm = f.Realm, Seat = seat, Here = seat, RaisedDay = (int)e.Day, ThoughtDay = (int)e.Day - 1, Mounted = true };
        int want = TargetSize(f.Fief), paidRoom = PaidMenBudget(e, f, lord), paid = 0;
        foreach (var (cat, row) in Candidates(e, f, lord))
        {
            if (wb.Members.Count >= want) break;
            bool free = cat == 0 && IsStaff(row);
            if (!free) { if (paid >= paidRoom) { if (cat == 0) continue; break; } paid++; }
            Enlist(e, wb, row, cat);
        }
        if (wb.Members.Count == 0) return null;
        Book.NextWarband++; Book.Warbands[wb.Id] = wb; SetCaptain(wb);
        Equip(e, wb);
        Chron(e, "warband_raised", "ch_am_raised",
            [ChronParam.Person("lord", new EntityId(lord)), ChronParam.Place("place", PlaceId(seat)), ChronParam.Count("men", wb.Members.Count)],
            [new EntityId(lord), PlaceId(seat)], new Cause(CauseKind.Campaign, null, "", "ch_note_am_retinue"));
        return wb;
    }

    /// <summary>Hires more men up to the holding's size and the wage roll (a season's step); returns how many came.</summary>
    public int Grow(ArmyEnv e, Warband wb, FiefRow f)
    {
        int want = TargetSize(f.Fief) - wb.Members.Count; if (want <= 0) return 0;
        int paid = 0; foreach (ulong m in wb.Members) { int r = RowOf(m); if (r >= 0 && !IsStaff(r)) paid++; }
        int room = PaidMenBudget(e, f, wb.Lord) - paid, n = 0;
        foreach (var (cat, row) in Candidates(e, f, wb.Lord))
        {
            if (n >= want) break;
            bool free = cat == 0 && IsStaff(row);
            if (!free) { if (room <= 0) { if (cat == 0) continue; break; } room--; }
            Enlist(e, wb, row, cat); n++;
        }
        if (n > 0) { SetCaptain(wb); Equip(e, wb); }
        return n;
    }

    /// <summary>Lets one man go: his occupation comes back, his row goes (issued kit back to its armoury), and he is off the lists.</summary>
    public void Release(Warband wb, ulong person)
    {
        int r = RowOf(person);
        if (r >= 0 && wb.Prev.TryGetValue(person, out byte occ) && People.Occupation[r] == Occupation.Soldier) People.Occupation[r] = (Occupation)occ;
        wb.Prev.Remove(person); wb.Members.Remove(person);
        Soldiers?.Fill.Discharge(Soldiers.Book, person);
        Book.Touch();
        if (person == wb.Captain) SetCaptain(wb);
    }

    /// <summary>Sends the whole warband home (the lord could not pay them, or has gone): every man released, the errand it escorted left to go alone, the loot put down. Writes the chronicle with its cause.</summary>
    public void Dismiss(ArmyEnv e, Warband wb, string noteKey, int fiefSeasonsBarred = 2)
    {
        int men = wb.Members.Count;
        foreach (ulong m in wb.Members.ToList()) Release(wb, m);
        if (wb.Escorting != 0 && Book.Errands.TryGetValue(wb.Escorting, out var er)) er.Escort = 0;
        Unload(e, wb, force: true);
        Book.Warbands.Remove(wb.Id); Book.Barren[wb.Fief] = e.SeasonNo + fiefSeasonsBarred; Book.Touch();
        Chron(e, "warband_dismissed", "ch_am_dismissed",
            [ChronParam.Person("lord", new EntityId(wb.Lord)), ChronParam.Place("place", PlaceId(wb.Seat)), ChronParam.Count("men", men)],
            [new EntityId(wb.Lord), PlaceId(wb.Seat)], new Cause(CauseKind.Campaign, null, "", noteKey));
    }

    /// <summary>Takes the dead out of the lists (a death comes by any road: sickness, a fight, old age): they stay dead, and their rows go.</summary>
    public void PruneDead(Warband wb)
    {
        for (int i = wb.Members.Count - 1; i >= 0; i--)
        {
            ulong m = wb.Members[i]; int r = RowOf(m);
            if (r >= 0 && People.Status[r] == PersonStatus.Alive) continue;
            wb.Members.RemoveAt(i); wb.Prev.Remove(m); Soldiers?.Book.Rows.Remove(m); Book.Touch();
            if (m == wb.Captain) SetCaptain(wb);
        }
    }

    // ------------------------------------------------------------------ kit from the lord's armoury

    private long? ArmouryOf(int seat)
    {
        if (ArmouryAt != null) return ArmouryAt(seat);
        var ww = W.Get<Ravensguard.Sim.Production.WorksWorld>(); if (ww == null || (uint)seat >= (uint)Region.Settlements.Count) return null;
        var s = Region.Settlements[seat]; long reach = (long)(s.Plan.RadiusM + 50) * (s.Plan.RadiusM + 50);
        foreach (var (key, room) in ww.RoomsOf(Ravensguard.Sim.Production.LargeRooms.Armoury))
        {
            long dx = room.Origin.X - s.CenterX, dy = room.Origin.Y - s.CenterY;
            if (dx * dx + dy * dy <= reach) return key;
        }
        return null;
    }

    /// <summary>Fills the men's templates from the lord's armoury at his seat (the filling rule of SC-A: best piece first, gaps shown, nothing made); nothing happens when the seat has no armoury or the men's own kit is all there is.</summary>
    public FillResult? Equip(ArmyEnv e, Warband wb)
    {
        if (Soldiers == null) return null;
        long? key = ArmouryOf(wb.Seat); if (key == null) return null;
        var men = Soldiers.SoldiersOf((long)wb.Lord);
        if (men.Count == 0) return null;
        return Soldiers.FillFrom(key.Value, men, (int)e.Day, (long)wb.Lord);
    }

    // ------------------------------------------------------------------ pay

    /// <summary>
    /// The day's wages (4.1: "wages are ledger transfers from the lord's purse"): each man not on a manor's payroll is paid his wage from his lord's household purse into his own household's; what the purse cannot pay is arrears and
    /// is paid down when there is coin again. Morale follows. A warband whose arrears pass <c>dismiss_arrears_days</c> of its roll goes home. Returns the coin paid.
    /// </summary>
    public long PayDay(ArmyEnv e)
    {
        var c = RuleCtx(e); if (c == null) return 0;
        var led = W.Economy.Ledger; long total = 0; int day = (int)e.Day;
        foreach (var wb in Book.Warbands.Values.ToList())
        {
            PruneDead(wb);
            if (wb.Members.Count == 0) { Dismiss(e, wb, "ch_note_am_none_left"); continue; }
            Rebind(wb, c);
            var lordPurse = RowOf(wb.Lord) >= 0 ? c.PurseOf(c.HouseholdOf((long)wb.Lord)) : PurseId.None;
            int lordHh = HouseholdOf(wb.Lord); long roll = 0, short_ = 0;
            foreach (ulong m in wb.Members)
            {
                int row = RowOf(m); if (row < 0 || IsStaff(row)) continue;
                int hh = People.Household[row]; if (hh < 0 || hh == lordHh) continue;
                long wage = WageOf(m); roll += wage;
                var to = W.Economy.HouseholdPurse[hh];
                if (!lordPurse.IsNone && led.Balance(lordPurse).Mites >= wage && led.Transfer(lordPurse, to, new Coin(wage), TransferReason.Wage)) total += wage; else short_ += wage;
            }
            wb.ArrearsMites += short_;
            if (wb.ArrearsMites > 0 && short_ == 0 && !lordPurse.IsNone)                                   // what is left of the day's purse pays the arrears, a day's wage a man at most
            {
                foreach (ulong m in wb.Members)
                {
                    if (wb.ArrearsMites <= 0) break;
                    int row = RowOf(m); if (row < 0 || IsStaff(row)) continue;
                    int hh = People.Household[row]; if (hh < 0 || hh == lordHh) continue;
                    long pay = Math.Min(Math.Min(WageOf(m), wb.ArrearsMites), led.Balance(lordPurse).Mites); if (pay <= 0) break;
                    if (led.Transfer(lordPurse, W.Economy.HouseholdPurse[hh], new Coin(pay), TransferReason.Wage)) { wb.ArrearsMites -= pay; total += pay; }
                }
            }
            wb.PaidDay = short_ == 0 ? day : wb.PaidDay;
            wb.Morale = wb.ArrearsMites > 0 ? Math.Max(0, wb.Morale - Rules.MoraleLoss) : Math.Min(100, wb.Morale + Rules.MoraleGain);
            if (roll > 0 && wb.ArrearsMites > roll * Rules.DismissArrearsDays) Dismiss(e, wb, "ch_note_am_unpaid");
        }
        return total;
    }

    /// <summary>The fief's holder changed (a death, a grant): the men serve the new lord and are his rows' lord now.</summary>
    private void Rebind(Warband wb, RuleCtx c)
    {
        var rb = c.B; if (!rb.Fiefs.TryGetValue(wb.Fief, out var f)) return;
        if (f.Holder < 0 || f.Forfeited || (ulong)f.Holder == wb.Lord || !c.Alive(f.Holder)) return;
        wb.Lord = (ulong)f.Holder; wb.Realm = f.Realm;
        if (Soldiers != null) foreach (ulong m in wb.Members) if (Soldiers.Book.Rows.TryGetValue(m, out var row)) row.Lord = (long)wb.Lord;
    }
}
