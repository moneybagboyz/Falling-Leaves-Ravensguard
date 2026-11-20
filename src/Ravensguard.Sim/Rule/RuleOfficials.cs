using System.Globalization;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Law;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Rule;

/// <summary>
/// Officials, honest or corrupt (rule-and-income 4.8): a lord appoints people he knows to the posts of his fief; each person's honesty is rolled once, the first time it matters (stream 0x540, key the person id); an
/// official keeps a share of what he collects when his honesty is low (<c>Skim</c>, a real transfer), a colleague who handles the same chest may see it and the law hears of it, and the lord's audit finds the rest
/// and has him dismissed with the money back. The Ledger shows what the lord could know: collections against what was expected.
/// </summary>
public static class RuleOfficials
{
    // ------------------------------------------------------------------ honesty (0x540)

    /// <summary>A person's honesty (10..100), rolled once the first time it matters and kept (stream 0x540, key the person id, never the day).</summary>
    public static byte HonestyOf(RuleBook b, ulong seed, EntityId person)
    {
        if (b.Honesty.TryGetValue(person.Value, out byte h)) return h;
        var s = b.Settings;
        int draw = DetRng.Range(seed, 0, person.Value, RngStreams.OfficialHonesty, 0, 0, Math.Max(1, s.HonestyMax - s.HonestyMin + 1));
        h = RuleMath.HonestyOf(s, draw);
        b.Honesty[person.Value] = h;
        return h;
    }

    public static int WageOf(RuleBook b, OfficeKind kind)
    {
        string id = kind switch { OfficeKind.CustomsCollector => "collector", _ => kind.ToString().ToLowerInvariant() };
        return b.OfficeDefs.TryGetValue(id, out var d) ? d.WageMitesPerDay : kind switch { OfficeKind.Reeve => 150, OfficeKind.Bailiff => 350, OfficeKind.Steward => 350, OfficeKind.Sheriff => 500, OfficeKind.CustomsCollector => 350, OfficeKind.Gaoler => 150, _ => 350 };
    }

    /// <summary>Skill an office asks for: Law for bailiffs, sheriffs, gaolers and stewards, Trading for collectors and treasurers, none for a reeve.</summary>
    public static Skill SkillOf(OfficeKind kind) => kind switch { OfficeKind.Bailiff or OfficeKind.Sheriff or OfficeKind.Gaoler or OfficeKind.Steward => Skill.Law, OfficeKind.CustomsCollector or OfficeKind.Treasurer => Skill.Trading, _ => Skill.None };

    // ------------------------------------------------------------------ appointment

    /// <summary>
    /// The lord appoints <paramref name="person"/> to an office of a fief (4.8): he must be a living adult; the office takes his honesty (rolled now if never), the wage of its content row. An existing holder of the same
    /// post is dismissed. Returns the office, or null when the person cannot hold it.
    /// </summary>
    public static Office? Appoint(RuleCtx c, OfficeKind kind, int fief, int realm, long person, int day)
    {
        int row = c.RowOf(person);
        if (row < 0 || !c.People.IsAlive(row) || c.People.AgeYears(row, day) < 16) return null;
        var b = c.B;
        foreach (var o in b.Offices) if (o.InOffice && o.Kind == kind && o.Fief == fief && o.Realm == realm) { o.DismissedDay = day; }
        var off = new Office { Serial = b.NextOffice++, Kind = kind, Holder = person, Fief = fief, Realm = realm, WageMitesPerDay = WageOf(b, kind), Honesty = HonestyOf(b, c.Seed, new EntityId((ulong)person)), AppointedDay = day };
        b.Offices.Add(off);
        var ps = new List<ChronParam> { ChronParam.Person("holder", new EntityId((ulong)person)), ChronParam.TextKey("office", "ru_office_" + kind.ToString().ToLowerInvariant()) };
        var place = c.SeatPlace(fief); if (place != EntityId.None) ps.Add(ChronParam.Place("place", place));
        c.Chron.Chron(RuleCtx.At(day), "official_appointed", "ch_rule_official_appointed", ps, [new EntityId((ulong)person)], [new Cause(CauseKind.Office, null, NoteKey: "ch_note_lord_appoints")]);
        return off;
    }

    /// <summary>Dismisses an office holder (a lord's choice, an audit, a death).</summary>
    public static void Dismiss(RuleCtx c, Office o, int day, string noteKey, EntityId? cause = null)
    {
        if (!o.InOffice) return;
        o.DismissedDay = day;
        var ps = new List<ChronParam> { ChronParam.Person("holder", new EntityId((ulong)o.Holder)), ChronParam.TextKey("office", "ru_office_" + o.Kind.ToString().ToLowerInvariant()) };
        c.Chron.Chron(RuleCtx.At(day), "official_dismissed", "ch_rule_official_dismissed", ps, [new EntityId((ulong)o.Holder)], [new Cause(CauseKind.Office, cause, NoteKey: noteKey)]);
    }

    /// <summary>The chest an office's money is paid into: its fief's treasury, or its realm's.</summary>
    public static Party ChestOf(Office o) => o.Fief >= 0 ? Party.OfFief(o.Fief) : Party.OfRealm(o.Realm);

    /// <summary>The fief's collector of the hearth tax: its bailiff, else its reeve (4.3).</summary>
    public static Office? CollectorOf(RuleBook b, int fief) => b.OfficeOf(OfficeKind.Bailiff, fief) ?? b.OfficeOf(OfficeKind.Reeve, fief);

    /// <summary>Whether the holder is still alive; a dead holder leaves the post vacant and the next seasonal pass fills it.</summary>
    public static void Reconcile(RuleCtx c, int day)
    {
        foreach (var o in c.B.Offices.ToList()) if (o.InOffice && !c.Alive(o.Holder)) { o.DismissedDay = day; c.Chron.Chron(RuleCtx.At(day), "official_dismissed", "ch_rule_official_dismissed", [ChronParam.Person("holder", new EntityId((ulong)o.Holder)), ChronParam.TextKey("office", "ru_office_" + o.Kind.ToString().ToLowerInvariant())], [new EntityId((ulong)o.Holder)], [new Cause(CauseKind.Office, null, NoteKey: "ch_note_official_died")]); }
    }

    // ------------------------------------------------------------------ collecting and skimming (S4)

    /// <summary>
    /// A settlement's round of the hearth tax was <paramref name="collected"/> by the fief's collector (4.3, 4.8): the book adds it to what he handled against what was expected; and when his honesty is below 60 he keeps
    /// <c>(60 - honesty) / 4</c> percent of it, moved from the fief's treasury to his own household's purse with the reason <c>Skim</c> (RI-10: 20 honesty collecting 30,000 keeps 3,000 and leaves 27,000).
    /// Returns what he kept.
    /// </summary>
    public static long Collected(RuleCtx c, int settlement, long collected, long expected, int day)
    {
        var region = c.Region; if ((uint)settlement >= (uint)region.Settlements.Count) return 0;
        int fief = region.Settlements[settlement].FiefIndex; if (fief < 0) return 0;
        var off = CollectorOf(c.B, fief); if (off == null) return 0;
        return Handle(c, off, Party.OfFief(fief), collected, expected);
    }

    /// <summary>What an official's hands did with a sum he collected into <paramref name="chest"/>: the record, and a skim by his honesty (a real transfer, <c>Skim</c>) up to what the chest holds.</summary>
    public static long Handle(RuleCtx c, Office off, Party chest, long collected, long expected)
    {
        off.Collected += collected; off.Expected += expected;
        long skim = RuleMath.Skim(c.S, collected, off.Honesty);
        if (skim <= 0) return 0;
        long took = c.PayUpTo(chest, c.HouseholdOf(off.Holder), skim, TransferReason.Skim);
        off.Skimmed += took; off.SeasonSkim += took;
        return took;
    }

    // ------------------------------------------------------------------ the season: wages, seen, audits

    /// <summary>The wages of the season: each office holder is paid his fortnight and a day from the fief's treasury (what it holds), reason <c>Wage</c>.</summary>
    public static long PayWages(RuleCtx c)
    {
        long total = 0;
        foreach (var o in c.B.Offices)
        {
            if (!o.InOffice) continue;
            total += c.PayUpTo(ChestOf(o), c.HouseholdOf(o.Holder), (long)o.WageMitesPerDay * TimeConst.DaysPerSeason, TransferReason.Wage);
        }
        return total;
    }

    /// <summary>
    /// A colleague who handles the same chest may see the skimming each season (4.8): for each official with something skimmed this season, every other office holder of the fief draws with P = 20% (stream 0x661, key the
    /// official and the season, draw 1 + colleague); a sighting makes the crime <c>embezzlement</c> a witnessed one, reported by the colleague. Returns the crimes written.
    /// </summary>
    public static int Seen(RuleCtx c, int day)
    {
        int n = 0; int season = day / TimeConst.DaysPerSeason;
        foreach (var o in c.B.Offices.ToList())
        {
            if (!o.InOffice || o.SeasonSkim <= 0 || day - o.AppointedDay < TimeConst.DaysPerSeason) continue;            // a post's first season is nobody's audit
            var colleagues = c.B.Offices.Where(x => x.InOffice && x.Fief == o.Fief && (o.Fief >= 0 || x.Realm == o.Realm) && x.Serial != o.Serial).OrderBy(x => x.Serial).ToList();
            foreach (var col in colleagues)
            {
                if (DetRng.Range(c.Seed, season, (ulong)o.Serial, RngStreams.SkimSeenAndAudits, (uint)(1 + col.Serial), 0, 100) >= c.S.SeenPct) continue;
                if (!c.Alive(col.Holder)) continue;
                Commit(c, o, o.SeasonSkim, [new EntityId((ulong)col.Holder)], new EntityId((ulong)col.Holder), "ch_note_skim_seen", day, report: false);
                n++; break;
            }
        }
        return n;
    }

    /// <summary>
    /// The lord (or his treasurer) audits each official once a season (4.8): found with P = <c>min(90, 30 + auditor's Law - official's Law / 2)</c> percent (stream 0x661, key the official and the season, draw 0). One
    /// who skimmed and is found repays what he took that season (to the fief's treasury, <c>Restitution</c>), is dismissed, and the law has the crime record. Returns the officials found.
    /// </summary>
    public static int Audit(RuleCtx c, int day)
    {
        int found = 0; int season = day / TimeConst.DaysPerSeason;
        foreach (var o in c.B.Offices.ToList())
        {
            if (!o.InOffice || o.Fief < 0 || day - o.AppointedDay < TimeConst.DaysPerSeason) continue;                 // the first season of a post is not audited (what he took stays on his record for the next)
            var fr = c.B.FiefOf(o.Fief);
            long auditor = fr?.Holder ?? -1;
            var treasurer = c.B.OfficeOf(OfficeKind.Treasurer, o.Fief); if (treasurer != null && treasurer.Serial != o.Serial) auditor = treasurer.Holder;
            if (auditor < 0 || auditor == o.Holder || (c.RowOf(auditor) >= 0 && !c.Alive(auditor))) { o.SeasonSkim = 0; continue; }          // nobody to audit him this season (a lord who is the hero has no person row: his books are audited all the same)
            int p = RuleMath.AuditPct(c.S, c.Law(auditor), c.Law(o.Holder));
            bool hit = DetRng.Range(c.Seed, season, (ulong)o.Serial, RngStreams.SkimSeenAndAudits, 0, 0, 100) < p;
            if (hit && o.SeasonSkim > 0)
            {
                found++; long back = o.SeasonSkim; o.Found += back;
                long repaid = c.PayUpTo(c.HouseholdOf(o.Holder), ChestOf(o), back, TransferReason.Restitution);
                var rec = Commit(c, o, back, [new EntityId((ulong)auditor)], new EntityId((ulong)auditor), "ch_note_skim_audit", day, report: true);
                var ps = new List<ChronParam> { ChronParam.Person("holder", new EntityId((ulong)o.Holder)), ChronParam.Mites("amount", back), ChronParam.Mites("repaid", repaid) };
                var entry = c.Chron.Chron(RuleCtx.At(day), "official_found_skimming", "ch_rule_official_found", ps, [new EntityId((ulong)o.Holder), new EntityId((ulong)auditor)], [new Cause(CauseKind.Office, rec == EntityId.None ? null : rec, NoteKey: "ch_note_audit_found")]);
                Dismiss(c, o, day, "ch_note_dismissed_for_skimming", entry == EntityId.None ? null : entry);
                c.Deed?.Invoke(new EntityId((ulong)o.Holder), "official_found_skimming", entry, (uint)o.Fief < (uint)c.Region.Fiefs.Count ? c.Region.Fiefs[o.Fief].SeatSettlement : -1, day);        // a thief's name travels (reputation 4.1)
            }
            o.SeasonSkim = 0;
        }
        return found;
    }

    private static EntityId Commit(RuleCtx c, Office o, long value, IReadOnlyList<EntityId> witnesses, EntityId reporter, string noteKey, int day, bool report)
    {
        if (c.Crime == null) return EntityId.None;
        int settlement = (uint)o.Fief < (uint)c.Region.Fiefs.Count ? c.Region.Fiefs[o.Fief].SeatSettlement : -1;
        return c.Crime(new SkimCrime(new EntityId((ulong)o.Holder), c.FiefKey(o.Fief), settlement, day * 86_400L + 12 * 3600L, value, witnesses, noteKey, report, reporter));
    }

    /// <summary>What the lord could know (4.8, open question 2): collections against what was expected, in percent (an honest bailiff's shortfall from arrears looks the same at first).</summary>
    public static int ShortfallPct(Office o) => o.Expected <= 0 ? 0 : (int)Math.Clamp((o.Expected - o.Collected) * 100 / o.Expected, 0, 100);

    public static string KeyOf(long v) => v.ToString(CultureInfo.InvariantCulture);
}
