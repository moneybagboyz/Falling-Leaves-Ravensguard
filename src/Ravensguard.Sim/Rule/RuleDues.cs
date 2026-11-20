using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Taxes;

namespace Ravensguard.Sim.Rule;

/// <summary>
/// What vassals owe and what tenants owe their lord (rule-and-income 4.1, 4.2): the fief changes hands (relief, wardship, escheat), the overlord calls aids and service, marriage is sold, labour service is accounted, an
/// unpaid due becomes a grievance and costs the vassal the overlord's opinion. Every sum of coin is a transfer between real purses.
/// </summary>
public static class RuleDues
{
    /// <summary>The reasons whose receipts count as a fief's income (a crown grant or an escheat does not): what relief and wardship are measured by.</summary>
    public static readonly TransferReason[] ReceiptReasons = [TransferReason.Hearth, TransferReason.Rent, TransferReason.Toll, TransferReason.MarketToll, TransferReason.Fine, TransferReason.Customs, TransferReason.Subsidy, TransferReason.Tallage, TransferReason.Fee, TransferReason.Royalty];

    /// <summary>A fief's receipts last season, by the treasury's own books.</summary>
    public static long SeasonReceipts(RuleCtx c, int fief)
    {
        var t = c.T; if (t == null || fief < 0 || fief >= t.FiefSlot.Length || t.FiefSlot[fief] < 0) return 0;
        long sum = 0; foreach (var r in ReceiptReasons) sum += t.IncomeOf(t.FiefSlot[fief], r, lastSeason: true);
        return sum;
    }

    /// <summary>Rolls each fief's four-season ring of receipts (run once a season after the treasuries rolled): the newest at index 0.</summary>
    public static void RollReceipts(RuleCtx c)
    {
        foreach (var f in c.B.Fiefs.Values) { f.Receipts[3] = f.Receipts[2]; f.Receipts[2] = f.Receipts[1]; f.Receipts[1] = f.Receipts[0]; f.Receipts[0] = SeasonReceipts(c, f.Fief); }
    }

    /// <summary>One season of a fief's hearth tax at its present level (the aid, RI-6): its households times the level's hearth.</summary>
    public static long HearthPerSeason(RuleCtx c, int fief)
    {
        var tax = c.Tax; if (tax == null) return 0;
        return (long)c.HouseholdsOfFief(fief).Count() * tax.HearthMites(tax.LevelOfFief(fief));
    }

    // ------------------------------------------------------------------ the fief changes hands (relief, wardship, escheat)

    /// <summary>
    /// Compares each fief's recorded holder with the lord the people say he is (the daily pass): a new holder is an heir (or a grantee) who owes relief and, under 16, puts the fief in wardship (4.2); a fief that has no
    /// lord left escheats to its overlord. Returns the number of changes.
    /// </summary>
    public static int Succession(RuleCtx c, int day)
    {
        int n = 0; var pop = c.W.Population;
        foreach (var f in c.B.Fiefs.Values.ToList())
        {
            if (f.Forfeited) continue;
            int lordRow = f.Fief < pop.FiefLord.Length ? pop.FiefLord[f.Fief] : -1;
            long now = lordRow >= 0 && c.People.IsAlive(lordRow) ? (long)c.People.IdOf(lordRow).Value : -1;
            if (now == f.Holder) continue;
            long was = f.Holder;
            if (now < 0)
            {
                f.Holder = -1; n++;
                c.Chron.Chron(RuleCtx.At(day), "fief_escheated", "ch_rule_fief_escheated", [ChronParam.Place("place", c.SeatPlace(f.Fief))],
                    [c.FiefKey(f.Fief)], [new Cause(CauseKind.Law, null, NoteKey: "ch_note_line_died_out")]);
                continue;
            }
            f.Holder = now; f.TookDay = day; n++;
            var heirId = new EntityId((ulong)now);
            var entry = c.Chron.Chron(RuleCtx.At(day), "fief_inherited", "ch_rule_fief_inherited", [ChronParam.Person("heir", heirId), ChronParam.Place("place", c.SeatPlace(f.Fief))], [heirId, c.FiefKey(f.Fief)],
                [new Cause(CauseKind.Family, null, NoteKey: was >= 0 ? "ch_note_lord_died" : "ch_note_fief_granted")]);
            var overlord = c.OverlordOf(f);
            long relief = RuleMath.Relief(f.LastYearReceipts, c.S.ReliefPct);
            if (!overlord.IsNone && relief > 0)
                AddDue(c, DueKind.Relief, c.HouseholdOf(now), overlord, relief, f.Fief, day + c.S.ReliefDueSeasons * TimeConst.DaysPerSeason, entry);
            int row = c.RowOf(now);
            if (row >= 0 && c.People.AgeYears(row, day) < c.S.WardAgeYears)
                BeginWardship(c, f, row, day, entry);
        }
        return n;
    }

    /// <summary>An heir under 16: the overlord takes the fief's receipts until the heir comes of age, paying the guardian a fifth for the keep (RI-5).</summary>
    public static void BeginWardship(RuleCtx c, FiefRow f, int heirRow, int day, EntityId cause)
    {
        f.WardUntilDay = RuleMath.WardEndsDay(c.People.BirthDay[heirRow], c.S.WardAgeYears);
        int hh = c.People.Household[heirRow]; long guardian = -1;
        if (hh >= 0) foreach (int m in c.Households.Members(hh)) if (m != heirRow && c.People.IsAlive(m) && c.People.AgeYears(m, day) >= 18 && (guardian < 0 || (long)c.People.IdOf(m).Value < guardian)) guardian = (long)c.People.IdOf(m).Value;
        f.Guardian = guardian;
        var heirId = c.People.IdOf(heirRow);
        c.Chron.Chron(RuleCtx.At(day), "wardship_begun", "ch_rule_wardship_begun", [ChronParam.Person("heir", heirId), ChronParam.Place("place", c.SeatPlace(f.Fief)), ChronParam.Days("until", f.WardUntilDay)],
            [heirId, c.FiefKey(f.Fief)], [new Cause(CauseKind.Family, cause == EntityId.None ? null : cause, NoteKey: "ch_note_heir_under_age")]);
    }

    /// <summary>
    /// The season's wardship (4.2, RI-5): for each fief in ward the overlord takes the season's receipts from its treasury (reason <c>Fee</c>) and pays the guardian a
    /// fifth for the keep (<c>Stipend</c>); at the heir's age the ward ends. Returns the coin taken.
    /// </summary>
    public static long Wardships(RuleCtx c, int day)
    {
        long taken = 0;
        foreach (var f in c.B.Fiefs.Values.ToList())
        {
            if (f.WardUntilDay < 0) continue;
            if (day >= f.WardUntilDay) { f.WardUntilDay = -1; f.Guardian = -1; c.Chron.Chron(RuleCtx.At(day), "wardship_ended", "ch_rule_wardship_ended", [ChronParam.Place("place", c.SeatPlace(f.Fief))], [c.FiefKey(f.Fief)], [new Cause(CauseKind.Family, null, NoteKey: "ch_note_heir_of_age")]); continue; }
            var overlord = c.OverlordOf(f); if (overlord.IsNone) continue;
            long receipts = f.Receipts[0]; if (receipts <= 0) continue;
            long got = c.PayUpTo(Party.OfFief(f.Fief), overlord, receipts, TransferReason.Fee);
            taken += got;
            long keep = RuleMath.GuardianKeep(got, c.S.WardGuardianPct);
            if (keep > 0 && f.Guardian >= 0) c.PayUpTo(overlord, c.HouseholdOf(f.Guardian), keep, TransferReason.Stipend);
        }
        return taken;
    }

    // ------------------------------------------------------------------ calling and settling dues

    public static DueRow AddDue(RuleCtx c, DueKind kind, Party payer, Party payee, long mites, int units, int dueDay, EntityId entry)
    {
        var d = new DueRow { Serial = c.B.NextDue++, Kind = kind, Payer = payer, Payee = payee, AmountMites = mites, Units = units, DueDay = dueDay, State = DueState.Due, Entry = entry.Value };
        c.B.Dues.Add(d);
        return d;
    }

    /// <summary>One of the overlord's three occasions (his ransom, his eldest son's knighting, his eldest daughter's marriage): each vassal fief of the realm owes a season of its hearth tax (RI-6).</summary>
    public static int CallAid(RuleCtx c, int realm, string occasionKey, int day)
    {
        int n = 0;
        foreach (var f in c.B.Fiefs.Values)
        {
            if (f.Realm != realm || f.Forfeited || f.Holder < 0) continue;
            long amount = RuleMath.Aid(HearthPerSeason(c, f.Fief), c.S.AidSeasons);
            if (amount <= 0) continue;
            var entry = c.Chron.Chron(RuleCtx.At(day), "aid_called", "ch_rule_aid_called", [ChronParam.Mites("amount", amount), ChronParam.Place("place", c.SeatPlace(f.Fief)), ChronParam.TextKey("occasion", occasionKey)], [c.FiefKey(f.Fief), c.RealmKey(realm)],
                [new Cause(CauseKind.Law, null, NoteKey: "ch_note_feudal_aid")]);
            AddDue(c, DueKind.Aid, Party.OfFief(f.Fief), Party.OfRealm(realm), amount, 0, day + c.S.AidDueSeasons * TimeConst.DaysPerSeason, entry); n++;
        }
        return n;
    }

    /// <summary>The overlord calls his men: each vassal owes <c>men x days</c> (a fief one man in fifty households for a week) to report to his seat; kept by the fief's holder being alive and his fief able to spare the men.</summary>
    public static int CallService(RuleCtx c, int realm, int day)
    {
        int n = 0;
        foreach (var f in c.B.Fiefs.Values)
        {
            if (f.Realm != realm || f.Forfeited || f.ServiceMen <= 0) continue;
            AddDue(c, DueKind.Service, Party.OfFief(f.Fief), Party.OfRealm(realm), 0, f.ServiceMen * 1000 + f.ServiceDays, day + 15, EntityId.None); n++;
        }
        return n;
    }

    /// <summary>The overlord sells his consent to the marriage of a fief's heir or widow: one season of receipts (4.2); returns the due.</summary>
    public static DueRow? SellMarriage(RuleCtx c, int fief, int day)
    {
        var f = c.B.FiefOf(fief); if (f == null || f.Holder < 0) return null;
        var overlord = c.OverlordOf(f); if (overlord.IsNone) return null;
        long fee = RuleMath.MarriageFee(f.Receipts[0] > 0 ? f.Receipts[0] : SeasonReceipts(c, fief), c.S.MarriageFeeSeasons);
        var entry = c.Chron.Chron(RuleCtx.At(day), "marriage_consent_sold", "ch_rule_marriage_sold", [ChronParam.Mites("amount", fee), ChronParam.Place("place", c.SeatPlace(fief))], [c.FiefKey(fief)], [new Cause(CauseKind.Family, null, NoteKey: "ch_note_overlord_consent")]);
        return AddDue(c, DueKind.MarriageFee, c.HouseholdOf(f.Holder), overlord, fee, fief, day + 15, entry);
    }

    /// <summary>A match made without the overlord's consent: a fine of two seasons of receipts and his opinion lowered (4.2).</summary>
    public static DueRow? MarryWithoutConsent(RuleCtx c, int fief, int day)
    {
        var f = c.B.FiefOf(fief); if (f == null || f.Holder < 0) return null;
        var overlord = c.OverlordOf(f); if (overlord.IsNone) return null;
        long fine = RuleMath.MarriageFee(f.Receipts[0] > 0 ? f.Receipts[0] : SeasonReceipts(c, fief), c.S.MarriageFineSeasons);
        c.B.AddOpinion(c.KeyOf(overlord).Value, (ulong)f.Holder, c.S.MarriageFineOpinion);
        var entry = c.Chron.Chron(RuleCtx.At(day), "marriage_fined", "ch_rule_marriage_fined", [ChronParam.Mites("amount", fine), ChronParam.Place("place", c.SeatPlace(fief))], [c.FiefKey(fief)], [new Cause(CauseKind.Family, null, NoteKey: "ch_note_married_without_consent")]);
        return AddDue(c, DueKind.MarriageFee, c.HouseholdOf(f.Holder), overlord, fine, fief, day + 15, entry);
    }

    /// <summary>
    /// The seasonal settling of the dues (S5): each due whose day has come is paid up to what the payer holds; an unpaid one becomes arrears and costs the payer the payee's opinion (-20), a second season unpaid is
    /// a grievance; an unpaid relief four seasons after the heir took the fief is forfeit by the overlord's court (RI-4). Paid rows older than a year are pruned. Returns the coin that moved.
    /// </summary>
    public static long Settle(RuleCtx c, int day)
    {
        long moved = 0;
        foreach (var d in c.B.Dues.ToList())
        {
            if (d.State is DueState.Paid or DueState.Waived) continue;
            if (day < d.DueDay) continue;
            if (d.Kind == DueKind.Service) { SettleService(c, d, day); continue; }
            long pay = c.PayUpTo(d.Payer, d.Payee, d.Owing, TransferReasonOf(d.Kind));
            d.PaidMites += pay; moved += pay;
            if (d.Owing == 0) { d.State = DueState.Paid; continue; }
            if (d.State == DueState.Due)
            {
                d.State = DueState.Arrears;
                c.B.AddOpinion(c.KeyOf(d.Payee).Value, c.KeyOf(d.Payer).Value, c.S.GrievanceOpinion);
                var ps = new List<ChronParam> { ChronParam.Mites("amount", d.Owing), ChronParam.TextKey("due", "ru_due_" + d.Kind.ToString().ToLowerInvariant()) };
                d.Entry = c.Chron.Chron(RuleCtx.At(day), "due_arrears", "ch_rule_due_arrears", ps, [c.KeyOf(d.Payer), c.KeyOf(d.Payee)], [new Cause(CauseKind.Law, d.Entry == 0 ? null : new EntityId(d.Entry), NoteKey: "ch_note_due_unpaid")]).Value;
            }
            else if (d.State == DueState.Arrears && day >= d.DueDay + TimeConst.DaysPerSeason)
            {
                d.State = DueState.Grievance;
                c.B.AddOpinion(c.KeyOf(d.Payee).Value, c.KeyOf(d.Payer).Value, c.S.GrievanceOpinion);
            }
            if (d.Kind == DueKind.Relief && day >= d.DueDay - c.S.ReliefDueSeasons * TimeConst.DaysPerSeason + c.S.ReliefForfeitSeasons * TimeConst.DaysPerSeason && d.Owing > 0) Forfeit(c, d, day);
        }
        c.B.Dues.RemoveAll(d => d.State is DueState.Paid or DueState.Waived && day - d.DueDay > TimeConst.DaysPerYear);
        return moved;
    }

    private static TransferReason TransferReasonOf(DueKind k) => k switch
    {
        DueKind.Aid => TransferReason.Aid, DueKind.Relief => TransferReason.Relief, DueKind.Subsidy => TransferReason.Subsidy, DueKind.Tallage => TransferReason.Tallage,
        DueKind.MarriageFee => TransferReason.Fee, DueKind.Wardship => TransferReason.Fee, DueKind.Rent => TransferReason.Rent, _ => TransferReason.Rent,
    };

    /// <summary>Service is performed when the vassal's fief can spare the men (it has a holder and the households); a vassal without one has not reported: a grievance, his overlord's opinion of him -20.</summary>
    private static void SettleService(RuleCtx c, DueRow d, int day)
    {
        var f = c.B.FiefOf((int)d.Payer.Id);
        int men = d.Units / 1000;
        bool kept = f != null && f.Holder >= 0 && !f.Forfeited && c.HouseholdsOfFief(f.Fief).Count() >= men;
        if (kept) { d.State = DueState.Paid; d.PaidMites = d.AmountMites; return; }
        d.State = DueState.Grievance;
        c.B.AddOpinion(c.KeyOf(d.Payee).Value, c.KeyOf(d.Payer).Value, c.S.GrievanceOpinion);
        c.Chron.Chron(RuleCtx.At(day), "due_arrears", "ch_rule_due_arrears", [ChronParam.Mites("amount", 0), ChronParam.TextKey("due", "ru_due_service")], [c.KeyOf(d.Payer), c.KeyOf(d.Payee)], [new Cause(CauseKind.Law, null, NoteKey: "ch_note_service_unpaid")]);
    }

    /// <summary>The overlord's court takes the deed back (an unpaid relief after four seasons): the fief has no holder and the deed returns to the overlord.</summary>
    public static void Forfeit(RuleCtx c, DueRow d, int day)
    {
        var f = c.B.FiefOf(d.Units);                                                                       // a relief's units name its fief
        if (f == null || f.Forfeited) return;
        long was = f.Holder; f.Forfeited = true; f.Holder = -1; f.Rights = FiefRights.None;
        d.State = DueState.Waived;
        c.Chron.Chron(RuleCtx.At(day), "fief_forfeited", "ch_rule_fief_forfeited", [ChronParam.Person("holder", new EntityId((ulong)Math.Max(0, was))), ChronParam.Place("place", c.SeatPlace(f.Fief)), ChronParam.Mites("owed", d.Owing)],
            [c.FiefKey(f.Fief)], [new Cause(CauseKind.Law, d.Entry == 0 ? null : new EntityId(d.Entry), NoteKey: "ch_note_relief_unpaid")]);
    }

    // ------------------------------------------------------------------ labour service (4.1)

    /// <summary>
    /// Settles a window of labour service (4.1, RI-3): each tenant household of the fief owes one day in five of the window's days, credited as hours on the demesne when it worked them (8 an adult-day);
    /// a household that did not (no working adult, or hungry) owes 150 mites a day, collected from what it holds. Returns (households that owed, days credited, coin collected).
    /// </summary>
    public static (int Households, int DaysCredited, long Coin) SettleLabour(RuleCtx c, int fief, int windowDays, int day, Func<int, bool>? worked = null)
    {
        int days = RuleMath.LabourDaysOwed(windowDays, c.S.LabourDayEvery); int hhCount = 0, credited = 0; long coin = 0;
        var hht = c.Households; var e = c.W.Economy;
        foreach (int h in c.HouseholdsOfFief(fief))
        {
            if (hht.Deed[h] != DeedKind.Tenant) continue;
            hhCount++;
            bool did = worked?.Invoke(h) ?? (WorkingAdult(c, h, day) && (e.Grievances[h] & Grievance.Hunger) == 0);
            int daysWorked = did ? days : 0;
            credited += daysWorked;
            long miss = RuleMath.LabourMissed(days, daysWorked, c.S.LabourMissedMitesPerDay);
            if (miss > 0) coin += c.PayUpTo(Party.OfHousehold(hht.IdOf(h)), Party.OfFief(fief), miss, TransferReason.Rent);
        }
        c.B.DemesneHours[fief] = c.B.DemesneHours.GetValueOrDefault(fief) + (long)credited * 8;
        if (coin > 0) c.B.LabourCoin[fief] = c.B.LabourCoin.GetValueOrDefault(fief) + coin;
        return (hhCount, credited, coin);
    }

    private static bool WorkingAdult(RuleCtx c, int h, int day)
    {
        foreach (int m in c.Households.Members(h)) if (c.People.IsAlive(m)) { int age = c.People.AgeYears(m, day); if (age >= 16 && age < 60) return true; }
        return false;
    }

    // ------------------------------------------------------------------ rents in kind and in coin (4.1)

    /// <summary>
    /// The lord's one policy switch a year (4.1, RI-2): his tenants' leases are commuted from a share of the harvest in kind to coin (the farming default coin rent a field a year), or put back to a share. Returns the
    /// leases changed, or -1 when he changed it less than a year ago.
    /// </summary>
    public static int Commute(RuleCtx c, int fief, bool toCoin, int day)
    {
        var f = c.B.FiefOf(fief); var ew = c.Ew; if (f == null || ew == null) return 0;
        if (day - f.CommuteDay < TimeConst.DaysPerYear) return -1;
        var P = ew.Farming.State.Parcels; var d = ew.Farming.Settings.Def; int lordHh = fief < c.W.Population.FiefLordHousehold.Length ? c.W.Population.FiefLordHousehold[fief] : -1; int n = 0;
        for (int p = 0; p < P.Count; p++)
        {
            if (lordHh < 0 || P.Landlord[p] != lordHh) continue;
            if (toCoin && P.Lease[p] == Ravensguard.Sim.Farming.LeaseKind.Share) { P.Lease[p] = Ravensguard.Sim.Farming.LeaseKind.Coin; P.CoinMitesPerFieldYear[p] = d.RentCoinMitesPerFieldYear; n++; }
            else if (!toCoin && P.Lease[p] == Ravensguard.Sim.Farming.LeaseKind.Coin) { P.Lease[p] = Ravensguard.Sim.Farming.LeaseKind.Share; P.ShareBp[p] = (short)d.RentShareBpDefault; n++; }
        }
        f.RentInCoin = toCoin; f.CommuteDay = day;
        c.Chron.Chron(RuleCtx.At(day), "rents_commuted", "ch_rule_rents_commuted", [ChronParam.Place("place", c.SeatPlace(fief)), ChronParam.Count("leases", n)], [c.FiefKey(fief)], [new Cause(CauseKind.Law, null, NoteKey: toCoin ? "ch_note_lord_takes_coin" : "ch_note_lord_takes_grain")]);
        return n;
    }

    // ------------------------------------------------------------------ grievances (4.3)

    /// <summary>A household's grievance against the government (a subsidy paid, a tallage taken): points for a season; the unrest ladder reads them through <c>Grievance.Lord</c>.</summary>
    public static void AddGrievance(RuleCtx c, int household, int points, int seasons, int day, EntityId entry)
    {
        var id = c.Households.IdOf(household).Value;
        if (!c.B.Grievances.TryGetValue(id, out var g)) c.B.Grievances[id] = g = new GrievanceRow();
        g.Points += points; g.UntilDay = Math.Max(g.UntilDay, day + seasons * TimeConst.DaysPerSeason); g.Entry = entry.Value;
    }

    /// <summary>The day's pass: households with a live grievance carry the <c>Lord</c> flag for the unrest ladder; lapsed rows are cleared.</summary>
    public static void ApplyGrievances(RuleCtx c, int day)
    {
        var e = c.W.Economy; var hht = c.Households;
        foreach (var (id, g) in c.B.Grievances.ToList())
        {
            int h = hht.IndexOf(new EntityId(id));
            bool live = g.UntilDay > day;
            if (h >= 0 && h < e.HouseholdCount) e.Grievances[h] = live ? e.Grievances[h] | Grievance.Lord : e.Grievances[h] & ~Grievance.Lord;
            if (!live) c.B.Grievances.Remove(id);
        }
    }
}
