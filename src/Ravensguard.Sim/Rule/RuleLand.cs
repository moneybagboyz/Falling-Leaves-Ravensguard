using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Rule;

/// <summary>
/// Charters as politics and the lord's eviction of squatters (rule-and-income 4.7, 4.9). A founding or market charter is granted when the ruler's opinion of the petitioner, his standing with the realm, the fee,
/// whether the land is contested and whether he is of another realm add up to 40; a refusal names the largest negative term. A lapsed charter is revoked at the next season's politics unless its holder pays half the fee
/// for one extension; a charter two seasons in arrears is revoked the same way. A squatter is served notice and then evicted; the guards that enforce it are the justice step's (a hook, <see cref="RuleBook.EvictionOrders"/>).
/// </summary>
public static class RuleLand
{
    // ------------------------------------------------------------------ the petition's judge (wired into LandWorld.CharterJudge)

    /// <summary>
    /// The terms of a petition (4.9, RI-16): returns whether the ruler grants it and, if not, the text key of the largest negative term ("ld_charter_refused_contested", "..._opinion", "..._standing", "..._other_realm",
    /// "..._fee"). The grantor's opinion of the petitioner is the book's; his standing with the realm is the standing book's plus what the land book keeps.
    /// </summary>
    public static (bool Granted, string RefusalKey) Judge(RuleCtx c, EntityId grantee, GrantorKind gk, int grantor, long feeMites, bool contested)
    {
        int realm = gk == GrantorKind.Realm ? grantor : (uint)grantor < (uint)c.Region.Fiefs.Count ? c.Region.Fiefs[grantor].Realm : -1;
        var grantorKey = gk == GrantorKind.Realm ? c.RealmKey(grantor) : c.FiefKey(grantor);
        int opinion = c.B.Opinion(grantorKey.Value, grantee.Value) ;
        int standing = c.StandingWithRealm(grantee, realm) + (c.Land?.Book.StandingWith(gk, grantor) ?? 0);
        bool other = false; int row = c.People.IndexOf(grantee);
        if (row >= 0) { int hs = c.People.HomeSettlement[row]; if (hs >= 0 && hs < c.Region.Settlements.Count) { int mine = RuleCouncil.RealmOfSettlement(c, hs); other = mine >= 0 && realm >= 0 && mine != realm; } }
        var (score, largest) = RuleMath.CharterScore(c.S, opinion, standing, feeMites, contested, other);
        return score >= c.S.CharterGrantThreshold ? (true, "") : (false, "ld_charter_refused_" + largest["ru_term_".Length..]);
    }

    // ------------------------------------------------------------------ revocation and extension (S6)

    private static Party GranteeParty(Charter ch) => ch.GranteePurse == PurseKind.Household ? Party.OfHousehold(ch.Grantee) : Party.OfPerson(ch.Grantee);
    private static Party GrantorParty(Charter ch) => ch.GrantorKind == GrantorKind.Realm ? Party.OfRealm(ch.Grantor) : Party.OfFief(ch.Grantor);

    /// <summary>
    /// A holder pays half the fee for one extension of a lapsed charter's deadline (4.9, RI-17): the fee goes to the grantor's treasury, the charter is granted again with its deadline a year on and cannot be extended again.
    /// Returns the refusal key, or "" when extended.
    /// </summary>
    public static string Extend(RuleCtx c, int serial, int day)
    {
        var land = c.Land; var ch = land?.Book.CharterOf(serial); if (ch == null) return "ld_not_yours";
        if (ch.State != CharterState.Lapsed || c.B.Extended.Contains(serial) || c.B.Revoked.Contains(serial)) return "ru_extend_refused";
        long fee = RuleMath.ExtensionFee(ch.FeeMites, c.S.CharterExtensionFeePct);
        if (!c.Pay(GranteeParty(ch), GrantorParty(ch), fee, TransferReason.Fee)) return "ld_cannot_afford";
        ch.State = CharterState.Granted; ch.LapseDay = -1; ch.SettleByDay = day + c.S.CharterExtensionYears * TimeConst.DaysPerYear;
        c.B.Extended.Add(serial);
        c.Chron.Chron(RuleCtx.At(day), "charter_extended", "ch_rule_charter_extended", [ChronParam.Person("holder", ch.Grantee), ChronParam.Mites("amount", fee)], [ch.Grantee, ch.DeedId], [new Cause(CauseKind.Law, ch.GrantEvent.Value == 0 ? null : ch.GrantEvent, NoteKey: "ch_note_charter_extension_paid")]);
        return "";
    }

    /// <summary>
    /// The politics of charters at the season's S6 (4.9, RI-17): a lapsed charter is revoked unless its holder (an NPC, here) can pay the extension, which he does at once; a charter two seasons in arrears of its dues is
    /// revoked the same way. A revoked charter's deed returns to its grantor and the chronicle says why; the settlers stay as the grantor's tenants. Returns the charters revoked.
    /// </summary>
    public static int Charters(RuleCtx c, int day)
    {
        var land = c.Land; if (land == null) return 0; int n = 0;
        foreach (var ch in land.Book.Charters.ToList())
        {
            if (c.B.Revoked.Contains(ch.Serial)) continue;
            string why = "";
            if (ch.State == CharterState.Lapsed)
            {
                bool heroHolds = c.HasHero && ch.Grantee == c.Hero;
                if (!heroHolds && !c.B.Extended.Contains(ch.Serial) && Extend(c, ch.Serial, day) == "") continue;       // an NPC holder pays the extension if he can
                why = "ch_note_charter_lapsed";
            }
            else if (land.Book.Deeds.TryGetValue(ch.DeedId, out var d) && d.ArrearsSeasons >= c.S.CharterArrearsSeasons) why = "ch_note_charter_arrears";
            if (why.Length == 0) continue;
            if (!land.RevokeCharter(ch, day, (when, type, subj, causes, data) => EntityId.None)) continue;
            c.B.Revoked.Add(ch.Serial); n++;
            c.Chron.Chron(RuleCtx.At(day), "charter_revoked", "ch_rule_charter_revoked", [ChronParam.Person("holder", ch.Grantee)], [ch.Grantee, ch.DeedId], [new Cause(CauseKind.Law, ch.GrantEvent.Value == 0 ? null : ch.GrantEvent, NoteKey: why)]);
        }
        return n;
    }

    // ------------------------------------------------------------------ market charters (4.4, RI-18)

    /// <summary>Whether a settlement may petition for a market charter: not a town yet, at least 200 people and an open square.</summary>
    public static bool MayPetitionMarket(RuleCtx c, int settlement)
    {
        if ((uint)settlement >= (uint)c.Region.Settlements.Count) return false;
        var st = c.Region.Settlements[settlement];
        if (st.Class == SettlementClass.Town || c.B.MarketCharters.ContainsKey(settlement)) return false;
        return People(c, settlement) >= c.S.MarketCharterPeople && st.Plan.Square.W > 0 && st.Plan.Square.H > 0;
    }

    public static int People(RuleCtx c, int settlement)
    {
        int n = 0; var pt = c.People;
        for (int i = 0; i < pt.Count; i++) if (pt.IsAlive(i) && pt.HomeSettlement[i] == settlement) n++;
        return n;
    }

    /// <summary>
    /// A petition for a market charter (4.4, RI-18): a settlement of at least 200 people with an open square asks its realm's ruler; the fee (50 crowns) goes to the grantor's treasury and the same terms decide. Granted, the
    /// settlement becomes a town: market days, stalls (the land rules count stalls by class), market tolls to the charter's holder, and 5 crowns a year in dues. Returns (granted, the refusal key).
    /// </summary>
    public static (bool Granted, string Key) PetitionMarket(RuleCtx c, EntityId petitioner, int settlement, int day)
    {
        if (!MayPetitionMarket(c, settlement)) return (false, "ru_market_not_eligible");
        int realm = RuleCouncil.RealmOfSettlement(c, settlement);
        var grantor = realm >= 0 ? Party.OfRealm(realm) : Party.None; if (grantor.IsNone) return (false, "ld_charter_no_grantor");
        var (ok, key) = Judge(c, petitioner, GrantorKind.Realm, realm, c.S.MarketCharterFeeMites, contested: false);
        if (!ok) return (false, key);
        var from = c.People.IndexOf(petitioner) >= 0 ? c.HouseholdOf((long)petitioner.Value) : Party.OfPerson(petitioner);
        if (!c.Pay(from, grantor, c.S.MarketCharterFeeMites, TransferReason.Fee)) return (false, "ld_cannot_afford");
        c.B.MarketCharters[settlement] = (day, (long)petitioner.Value);
        c.Region.Settlements[settlement].Class = SettlementClass.Town;
        var place = c.Region.Settlements[settlement].Id;
        c.Chron.Chron(RuleCtx.At(day), "market_charter_granted", "ch_rule_market_charter", [ChronParam.Person("holder", petitioner), ChronParam.Place("place", place), ChronParam.Mites("fee", c.S.MarketCharterFeeMites)], [petitioner, place], [new Cause(CauseKind.Law, null, NoteKey: "ch_note_market_petition")]);
        return (true, "");
    }

    /// <summary>The towns a saved market charter made: the generated region still says village, so the charter's settlements are set to towns again whenever the book is attached.</summary>
    public static void ApplyMarketCharters(RuleBook b, RegionData region)
    {
        foreach (var s in b.MarketCharters.Keys) if ((uint)s < (uint)region.Settlements.Count) region.Settlements[s].Class = SettlementClass.Town;
    }

    // ------------------------------------------------------------------ squatters (4.7, RI-15)

    /// <summary>
    /// The lord's bailiff serves notice on each unlicensed claim in his fief he has heard of (4.7; the season's S6): pay the licence (an acre costs <c>ClaimLicenceMitesPerAcre</c>) within 15 days or leave. When the notice runs
    /// out an unlicensed claim is ordered cleared: if the justice step's guards are installed (<see cref="RuleCtx.GuardsEnforce"/>) they walk there and finish it with <see cref="Complete"/>; without them the order is carried
    /// out at once. Returns (notices served, claims evicted).
    /// </summary>
    public static (int Served, int Evicted) Squatters(RuleCtx c, int day)
    {
        var land = c.Land; if (land == null) return (0, 0); int served = 0, evicted = 0;
        foreach (var cl in land.Book.Claims.ToList())
        {
            if (cl.State != ClaimState.Open || cl.Fief < 0) continue;
            if (!cl.Squatting) { c.B.EvictionNotices.Remove(cl.Serial); continue; }                      // he bought the licence in time
            if (!c.B.EvictionNotices.TryGetValue(cl.Serial, out int until))
            {
                c.B.EvictionNotices[cl.Serial] = day + c.S.EvictNoticeDays; served++;
                long fee = LandRules.Licence(land.Rules, cl.Tiles.Length);
                c.Chron.Chron(RuleCtx.At(day), "eviction_notice", "ch_rule_eviction_notice", [ChronParam.Person("squatter", cl.Claimant), ChronParam.Mites("licence", fee), ChronParam.Days("days", c.S.EvictNoticeDays), ChronParam.Place("place", c.SeatPlace(cl.Fief))],
                    [cl.Claimant, c.FiefKey(cl.Fief)], [new Cause(CauseKind.Law, null, NoteKey: "ch_note_squatting_on_waste")]);
                continue;
            }
            if (day < until || c.B.EvictionOrders.ContainsKey(cl.Serial)) continue;
            c.B.EvictionOrders[cl.Serial] = day;
            if (c.GuardsEnforce?.Invoke(cl.Serial, cl.Fief, day) == true) continue;                       // C1's guards take it from here
            if (Complete(c, cl.Serial, day)) evicted++;
        }
        return (served, evicted);
    }

    private const string EvictedKey = "ch_rule_claim_evicted";

    /// <summary>The order is carried out (by the guards, or at once when there are none): the claim is pulled, its improvements are the lord's, and the squatter who held the banner is named in the chronicle.</summary>
    public static bool Complete(RuleCtx c, int serial, int day)
    {
        var land = c.Land; var cl = land?.Book.ClaimOf(serial); if (cl == null) return false;
        c.B.EvictionNotices.Remove(serial); c.B.EvictionOrders.Remove(serial);
        List<ChronParam> ps = [ChronParam.Person("squatter", cl.Claimant), ChronParam.Count("tiles", cl.Tiles.Length)];
        return land!.EvictClaim(cl, day, (when, type, subj, causes, data) => c.Chron.Chron(when, type, EvictedKey, ps, subj, causes, data));
    }
}
