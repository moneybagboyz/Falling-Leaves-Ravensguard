using Ravensguard.Sim.Contracts;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.WorldGen.History;

namespace Ravensguard.Sim.Politics;

/// <summary>
/// Realm diplomacy (diplomacy-and-ai-lords 4.1, 4.5, 4.6): the season's pass over every realm that rules: treaties that lapse, tribute that is paid, wars fought by the Phase 6 realm-level resolver (strength decides a season's
/// score; live armies are Phase 7), peace by war score and weariness, envoys that carry what a ruler knows of another and how old it is, and rulers who declare at the war utility's threshold. Every declaration is a chronicle
/// entry naming its largest term; every pact, cession and capture is one too. Integer only; the draws are streams 0x671 (ties, chances) and 0x672 (envoys).
/// </summary>
public static class Diplomacy
{
    // ------------------------------------------------------------------ names, notes and the chronicle

    public static string CbNote(CbKind k) => k switch
    {
        CbKind.Insult => "ch_note_dp_cb_insult", CbKind.UnpaidDues => "ch_note_dp_cb_unpaid", CbKind.BrokenTreaty => "ch_note_dp_cb_treaty", CbKind.SeizedFief => "ch_note_dp_cb_seized",
        CbKind.Raid => "ch_note_dp_cb_raid", CbKind.Excommunication => "ch_note_dp_cb_excomm", _ => "ch_note_dp_cb_claim",
    };

    public static EntityId RulerId(PoliticsCtx c, int realm) => new(c.RulerOf(realm));
    public static ChronParam RulerParam(PoliticsCtx c, string name, int realm) => ChronParam.Person(name, RulerId(c, realm));

    private static EntityId Write(PoliticsCtx c, string type, string key, IReadOnlyList<ChronParam> ps, IReadOnlyList<EntityId> subjects, Cause cause, params (string, string)[] data)
        => c.Chron.Chron(RuleCtx.At(c.Day), type, key, ps, subjects, [cause], data);

    /// <summary>The realm a treasury key names (-1 when it is not a realm's).</summary>
    public static int RealmOfKey(ulong key)
    {
        var id = new EntityId(key);
        return id.Kind == IdKind.Faction && ((id.Counter >> 40) & 1) == 1 ? (int)(id.Counter & 0xFFFFFFFFUL) : -1;
    }

    /// <summary>History's tick (a season) as a game day.</summary>
    public static int DayOfTick(int tick) => tick / 4 * TimeConst.DaysPerYear + tick % 4 * TimeConst.DaysPerSeason;

    // ------------------------------------------------------------------ casus belli

    /// <summary>A casus belli is an event: the chronicle entry is written first and the grievance names it (4.1). Returns the grievance.</summary>
    public static CasusBelli AddCb(PoliticsCtx c, int holder, int against, CbKind kind, EntityId cause = default)
    {
        var ps = new List<ChronParam> { RulerParam(c, "holder", holder), RulerParam(c, "against", against), ChronParam.TextKey("what", "ch_dp_cb_" + kind.ToString().ToLowerInvariant()) };
        var cz = cause.Value != 0 ? new Cause(CauseKind.Diplomacy, cause) : new Cause(CauseKind.Diplomacy, null, NoteKey: CbNote(kind));
        var entry = Write(c, "casus_belli_given", "ch_dp_casus_belli_given", ps, [RulerId(c, holder), RulerId(c, against)], cz);
        var cb = new CasusBelli { Serial = c.B.NextCb++, Holder = holder, Against = against, Kind = kind, Entry = entry.Value, Day = c.Day };
        c.B.Cbs.Add(cb);
        return cb;
    }

    /// <summary>The unused grievance of the greatest weight <paramref name="holder"/> holds against <paramref name="against"/>, the earliest on a tie.</summary>
    public static CasusBelli? BestCb(PoliticsCtx c, int holder, int against)
    {
        CasusBelli? best = null; int bw = 0;
        foreach (var x in c.B.Cbs)
        {
            if (x.Used || x.Holder != holder || x.Against != against) continue;
            int w = WarUtility.CbWeight(c.S, x.Kind); if (w > bw) { bw = w; best = x; }
        }
        return best;
    }

    /// <summary>A claim on the other's land: history's grudge of a against b, a founded realm's parent against it, or the old overlord.</summary>
    public static bool HasClaim(PoliticsCtx c, int a, int b)
    {
        if (c.B.RealmIfAny(b) is { Founded: true, Parent: var p } && p == a) return true;
        if (c.H == null) return false;
        foreach (var g in c.H.Grudges) if (g.From == a && g.To == b && g.Strength >= 25) return true;
        return false;
    }

    // ------------------------------------------------------------------ knowledge (4.2: last-known, with its age)

    /// <summary>What <paramref name="observer"/> knows of <paramref name="about"/> and how much it weighs (permille): a fact older than two seasons counts at half. A realm that has heard nothing yet is taken to know what everyone knows today.</summary>
    public static (long Strength, int TreasurySeasons, int WeightPm) Known(PoliticsCtx c, int observer, int about)
    {
        if (!c.B.Known.TryGetValue((observer, about), out var k))
        {
            k = new KnownFact { Observer = observer, About = about, Strength = c.Strength(about), TreasurySeasons = c.TreasurySeasons(about), Day = c.Day };
            c.B.Known[(observer, about)] = k;
        }
        return (k.Strength, k.TreasurySeasons, RumourAge.WeightPm(RumourAge.AgeDays(k.Day, c.Day), c.S.StaleSeasons * TimeConst.DaysPerSeason));
    }

    /// <summary>An envoy, a rumour or a spy tells <paramref name="observer"/> the truth as it stands today (the fact's age is 0 again).</summary>
    public static void Learn(PoliticsCtx c, int observer, int about)
    {
        var k = new KnownFact { Observer = observer, About = about, Strength = c.Strength(about), TreasurySeasons = c.TreasurySeasons(about), Day = c.Day };
        c.B.Known[(observer, about)] = k;
    }

    // ------------------------------------------------------------------ the war utility of two realms

    /// <summary>The utility of <paramref name="a"/> against <paramref name="b"/> as <paramref name="a"/> sees it, with the casus belli it would use.</summary>
    public static (int Utility, CasusBelli? Cb, bool Claim, int KnowPm) UtilityOf(PoliticsCtx c, int a, int b)
    {
        var cb = BestCb(c, a, b); bool claim = HasClaim(c, a, b);
        var (strB, _, w) = Known(c, a, b);
        var inp = new WarInputs(claim, cb == null ? 0 : WarUtility.CbWeight(c.S, cb.Kind), c.Strength(a), strB, c.TreasurySeasons(a), c.SeasonNow, WarUtility.HarvestCost(c.S, c.S.HarvestLostPermille),
            c.B.Allied(a, b, c.Day), c.B.TruceHolds(a, b, c.Day), w);
        return (WarUtility.Score(c.S, inp), cb, claim, w);
    }

    // ------------------------------------------------------------------ import of history's live wars and treaties

    /// <summary>Takes history's live wars and treaties in once, as the first season step finds the book: they are the state the world starts in (a pure function of the history).</summary>
    public static void Import(PoliticsCtx c)
    {
        var b = c.B; if (b.Imported || c.H == null) return;
        b.Imported = true;
        var h = c.H; int endTick = h.Years * 4;
        foreach (var w in h.Wars)
        {
            if (!w.Alive || !c.Alive(w.Attacker) || !c.Alive(w.Defender)) continue;
            var kind = w.Cb switch { CasusBelliKind.Insult => CbKind.Insult, CasusBelliKind.Raid => CbKind.Raid, _ => CbKind.Claim };
            b.Wars.Add(new War { Serial = b.NextWar++, Attacker = w.Attacker, Defender = w.Defender, StartDay = DayOfTick(w.StartTick), Cb = kind, CbEntry = w.CbEvent.Value, DeclEntry = w.DeclEvent.Value,
                Score = Math.Clamp(w.Score, -100, 100), Seasons = Math.Max(0, endTick - w.StartTick) });
        }
        foreach (var t in h.Treaties)
        {
            if (t.ExpiryTick <= endTick || !c.Alive(t.A) || !c.Alive(t.B)) continue;
            var p = new Pact { Serial = b.NextPact++, A = t.A, B = t.B, StartDay = DayOfTick(t.StartTick), ExpiryDay = DayOfTick(t.ExpiryTick), Cause = t.Event.Value };
            switch (t.Kind)
            {
                case Ravensguard.Sim.WorldGen.History.TreatyKind.Truce: p.Kind = PactKind.Truce; break;
                case Ravensguard.Sim.WorldGen.History.TreatyKind.MarriageAlliance: p.Kind = PactKind.MarriagePact; break;
                case Ravensguard.Sim.WorldGen.History.TreatyKind.Vassalage: p.Kind = PactKind.Vassalage; p.ExpiryDay = -1; break;
                default: p.Kind = PactKind.Truce; p.TributeFrom = t.B; p.TributeUntilDay = p.ExpiryDay; p.TributeMites = c.YearReceipts(t.B) * c.S.TributePct / 100 / TimeConst.SeasonsPerYear; break;
            }
            b.Pacts.Add(p);
        }
    }

    // ------------------------------------------------------------------ treaties

    public static Pact Sign(PoliticsCtx c, PactKind kind, int a, int b, int seasons, ulong cause, long tribute = 0, int tributeFrom = -1, int tributeSeasons = 0, string noteKey = "")
    {
        var p = new Pact { Serial = c.B.NextPact++, Kind = kind, A = a, B = b, StartDay = c.Day, ExpiryDay = seasons > 0 ? c.Day + seasons * TimeConst.DaysPerSeason : -1, Cause = cause,
            TributeMites = tribute, TributeFrom = tributeFrom, TributeUntilDay = tributeSeasons > 0 ? c.Day + tributeSeasons * TimeConst.DaysPerSeason : -1 };
        c.B.Pacts.Add(p);
        var cz = cause != 0 ? new Cause(CauseKind.Diplomacy, new EntityId(cause)) : new Cause(CauseKind.Diplomacy, null, NoteKey: noteKey.Length > 0 ? noteKey : "ch_note_dp_pact_terms");
        var ps = new List<ChronParam> { RulerParam(c, "a", a), RulerParam(c, "b", b), ChronParam.TextKey("kind", "ch_dp_pact_" + kind.ToString().ToLowerInvariant()) };
        if (seasons > 0) ps.Add(ChronParam.Days("days", seasons * TimeConst.DaysPerSeason)); else ps.Add(ChronParam.Days("days", 0));
        Write(c, "pact_signed", "ch_dp_pact_signed", ps, [RulerId(c, a), RulerId(c, b)], cz);
        return p;
    }

    /// <summary>Ends treaties whose day has come (truces lapse, alliances run out) and pays the season's tribute; a broken truce gives the wronged realm a casus belli (the war step calls <see cref="BreakTruce"/>).</summary>
    public static void TreatyPass(PoliticsCtx c)
    {
        foreach (var p in c.B.Pacts)
        {
            if (!p.Ended && p.ExpiryDay >= 0 && c.Day >= p.ExpiryDay)
            {
                p.Ended = true;
                Write(c, "pact_ended", "ch_dp_pact_ended", [RulerParam(c, "a", p.A), RulerParam(c, "b", p.B), ChronParam.TextKey("kind", "ch_dp_pact_" + p.Kind.ToString().ToLowerInvariant())],
                    [RulerId(c, p.A), RulerId(c, p.B)], new Cause(CauseKind.Diplomacy, p.Cause != 0 ? new EntityId(p.Cause) : null, NoteKey: p.Cause != 0 ? "" : "ch_note_dp_pact_ran_out"));
            }
            if (p.TributeFrom >= 0 && p.TributeMites > 0 && c.Day < p.TributeUntilDay) PayTribute(c, p);          // the tribute of the terms outlasts the truce itself
        }
    }

    /// <summary>One season's tribute: a real transfer between the realms' treasuries when both keep purses here, else a move of the abstract coin (nothing is made).</summary>
    public static long PayTribute(PoliticsCtx c, Pact p)
    {
        int from = p.TributeFrom, to = p.A == from ? p.B : p.A;
        var a = Party.OfRealm(from); var b = Party.OfRealm(to);
        long paid;
        if (!c.R.PurseOf(a).IsNone && !c.R.PurseOf(b).IsNone) paid = c.R.PayUpTo(a, b, p.TributeMites, TransferReason.Tribute);
        else
        {
            paid = p.TributeMites;
            c.B.RealmOf(from).TreasuryDelta -= paid; c.B.RealmOf(to).TreasuryDelta += paid;
        }
        return paid;
    }

    // ------------------------------------------------------------------ war

    public static War Declare(PoliticsCtx c, int a, int b, CasusBelli? cb, bool claim, int utility)
    {
        var kind = cb?.Kind ?? CbKind.Claim;
        int cbw = cb == null ? 0 : WarUtility.CbWeight(c.S, cb.Kind);
        bool byCb = cb != null && cbw >= (claim ? c.S.ClaimPts : 0);
        var cause = byCb && cb!.Entry != 0 ? new Cause(CauseKind.Diplomacy, new EntityId(cb.Entry)) : new Cause(CauseKind.Diplomacy, null, NoteKey: byCb ? CbNote(kind) : "ch_note_dp_cb_claim");
        var ps = new List<ChronParam> { RulerParam(c, "a", a), RulerParam(c, "b", b), ChronParam.Count("utility", utility) };
        var entry = Write(c, "realm_war_declared", "ch_dp_war_declared", ps, [RulerId(c, a), RulerId(c, b)], cause, ("a", c.NameOf(a)), ("b", c.NameOf(b)));
        if (cb != null) cb.Used = true;
        var w = new War { Serial = c.B.NextWar++, Attacker = a, Defender = b, StartDay = c.Day, Cb = byCb ? kind : CbKind.Claim, CbEntry = cb?.Entry ?? 0, DeclEntry = entry.Value };
        c.B.Wars.Add(w);
        // the truce that held is broken by the declaration (the wronged realm gets its own grievance, 4.1)
        if (c.B.PactOf(PactKind.Truce, a, b, c.Day) is { } t) { t.Ended = true; AddCb(c, b, a, CbKind.BrokenTreaty, entry); }
        Opinions.Add(c.B, c.RB, c.KeyOf(a), c.KeyOf(b), "war", c.Day, entry.Value);
        Opinions.Add(c.B, c.RB, c.KeyOf(b), c.KeyOf(a), "war", c.Day, entry.Value);
        return w;
    }

    /// <summary>One season of one war: strength decides the score (with a small draw), both hosts bleed, a decisive blow may take a lord, and a side weary enough sues (4.1, 4.5).</summary>
    public static void StepWar(PoliticsCtx c, War w)
    {
        int a = w.Attacker, d = w.Defender;
        if (!c.Alive(a) || !c.Alive(d)) { w.EndDay = c.Day; return; }
        var s = c.S;
        long sa = c.Strength(a), sd = c.Strength(d);
        int lead = (int)((sa * 1000 / Math.Max(1, sd) - 1000) / 100);
        int jitter = DetRng.Range(c.Seed, c.Day, (ulong)w.Serial, RngStreams.DiplomacyTies, 1, -3, 4);
        int delta = Math.Clamp(lead * s.ScorePerLeadStep, -s.ScoreMaxStep, s.ScoreMaxStep) + jitter;
        w.Score = Math.Clamp(w.Score + delta, -100, 100); w.Seasons++; w.Battles++;
        if (delta > 0) w.GainA += delta; else w.GainD -= delta;
        int winLoss = s.LossPermille, loseLoss = s.LossLoserPermille;
        int lossA = delta >= 0 ? winLoss : loseLoss, lossD = delta >= 0 ? loseLoss : winLoss;
        c.B.RealmOf(a).LossPm = Math.Min(900, c.B.RealmOf(a).LossPm + lossA); c.B.RealmOf(d).LossPm = Math.Min(900, c.B.RealmOf(d).LossPm + lossD);
        w.LossA += lossA; w.LossD += lossD;
        if (Math.Abs(delta) >= s.DecisiveStep && DetRng.Range(c.Seed, c.Day, (ulong)w.Serial, RngStreams.DiplomacyTies, 2, 0, 100) < s.CapturePct)
            CaptureLord(c, w, delta > 0 ? d : a, delta > 0 ? a : d);
        // peace: the loser sues when weariness less his own gains reaches the gap
        int loser = w.Score > 0 ? d : w.Score < 0 ? a : -1;
        if (loser < 0) return;
        int dead = (loser == a ? w.LossA : w.LossD) / 10;
        int wear = WarUtility.Weariness(s, w.Seasons, dead, c.TreasurySeasons(loser));
        if (WarUtility.Sues(s, wear, loser == a ? w.GainA : w.GainD)) MakePeace(c, w, loser, wear);
    }

    /// <summary>The loser sues; the terms follow the winner's score: a truce, tribute, or a fief ceded (4.1, DA-3).</summary>
    public static void MakePeace(PoliticsCtx c, War w, int loser, int weariness)
    {
        int winner = loser == w.Attacker ? w.Defender : w.Attacker;
        int score = Math.Abs(w.Score);
        var terms = WarUtility.TermsFor(c.S, score);
        var cause = new Cause(CauseKind.Diplomacy, w.DeclEntry != 0 ? new EntityId(w.DeclEntry) : null, NoteKey: w.DeclEntry != 0 ? "" : "ch_note_dp_peace_weary");
        var ps = new List<ChronParam> { RulerParam(c, "winner", winner), RulerParam(c, "loser", loser), ChronParam.Count("score", score), ChronParam.Count("weariness", weariness),
            ChronParam.TextKey("terms", terms == WarUtility.Terms.CedeFief ? "ch_dp_terms_cede" : terms == WarUtility.Terms.Tribute ? "ch_dp_terms_tribute" : "ch_dp_terms_truce") };
        var peace = Write(c, "realm_peace", "ch_dp_peace", ps, [RulerId(c, winner), RulerId(c, loser)], cause, ("winner", c.NameOf(winner)), ("loser", c.NameOf(loser)));
        w.EndDay = c.Day;
        long tribute = 0; int years = 0;
        if (terms == WarUtility.Terms.Tribute) { tribute = c.YearReceipts(loser) * c.S.TributePct / 100 / TimeConst.SeasonsPerYear; years = c.S.TributeSeasons; }
        Sign(c, PactKind.Truce, winner, loser, c.S.TruceSeasons, peace.Value, tribute, tribute > 0 ? loser : -1, years);
        if (terms == WarUtility.Terms.CedeFief) CedeFief(c, loser, winner, peace);
    }

    /// <summary>The loser's fief with the thinnest receipts passes to the winner's realm (or, with none here, a march of the loser's land named by its capital); a chronicle entry follows the peace's.</summary>
    public static int CedeFief(PoliticsCtx c, int loser, int winner, EntityId peace)
    {
        FiefRow? pick = null;
        foreach (var f in c.RB.Fiefs.Values) if (f.Realm == loser && !f.Forfeited && (pick == null || f.LastYearReceipts < pick.LastYearReceipts)) pick = f;
        int fief = pick?.Fief ?? -1;
        var place = pick != null ? c.R.SeatPlace(pick.Fief) : new EntityId(c.CapitalPlace(loser));
        var ps = new List<ChronParam> { RulerParam(c, "winner", winner), RulerParam(c, "loser", loser), ChronParam.Place("place", place) };
        Write(c, "fief_ceded", "ch_dp_fief_ceded", ps, [RulerId(c, winner), RulerId(c, loser), place], new Cause(CauseKind.Diplomacy, peace));
        if (pick == null) return -1;
        pick.Realm = winner; pick.OverlordFief = -1;
        foreach (var m in c.B.Manors) if (m.OverlordFief == fief) m.Realm = winner;
        return fief;
    }

    /// <summary>
    /// A decisive blow takes the loser's lord (4.5): ransomed at the Baron rank price from his own household's purse into the winner's treasury, else, with the price beyond him, the fief he holds is ceded to the captor
    /// (the captor's option). Returns the lord's person id (0 when none was in reach).
    /// </summary>
    public static ulong CaptureLord(PoliticsCtx c, War w, int loserRealm, int captorRealm)
    {
        var lords = new List<FiefRow>();
        foreach (var f in c.RB.Fiefs.Values) if (f.Realm == loserRealm && !f.Forfeited && f.Holder >= 0 && c.R.Alive(f.Holder) && !c.Links.IsHero((ulong)f.Holder)) lords.Add(f);
        if (lords.Count == 0) return 0;
        var lord = lords[DetRng.Range(c.Seed, c.Day, (ulong)w.Serial, RngStreams.DiplomacyTies, 3, 0, lords.Count)];
        return Ransom(c, lord, loserRealm, captorRealm, w.DeclEntry);
    }

    public static ulong Ransom(PoliticsCtx c, FiefRow lord, int loserRealm, int captorRealm, ulong cause)
    {
        long price = c.S.LordRansomMites; ulong person = (ulong)lord.Holder;
        var payer = c.R.HouseholdOf(lord.Holder); var captor = Party.OfRealm(captorRealm);
        if (c.R.PurseOf(captor).IsNone) c.R.Led.Open(PurseKind.Treasury, c.R.RealmKey(captorRealm));
        bool paid = c.R.Balance(payer) >= price && c.R.Pay(payer, captor, price, TransferReason.LordRansom);
        var cz = cause != 0 ? new Cause(CauseKind.War, new EntityId(cause)) : new Cause(CauseKind.Diplomacy, null, NoteKey: "ch_note_dp_cb_claim");
        var place = c.R.SeatPlace(lord.Fief);
        var ps = new List<ChronParam> { ChronParam.Person("lord", new EntityId(person)), RulerParam(c, "captor", captorRealm), ChronParam.Mites("amount", paid ? price : 0), ChronParam.Place("place", place),
            ChronParam.TextKey("how", paid ? "ch_dp_ransom_paid" : "ch_dp_ransom_ceded") };
        Write(c, "lord_ransomed", "ch_dp_lord_captured", ps, [new EntityId(person), place], cz);
        if (!paid) { lord.Realm = captorRealm; lord.OverlordFief = -1; }
        Opinions.Add(c.B, c.RB, person, c.KeyOf(captorRealm), "war", c.Day, 0);
        return person;
    }

    // ------------------------------------------------------------------ the season

    /// <summary>The realms' season at the first day's S10 hour: import (once), treaties and tribute, wars, envoys, declarations, the host's recovery. Run once for a day number.</summary>
    public static void Season(PoliticsCtx c)
    {
        var b = c.B; if (b.LastSeasonDay == c.Day || c.H == null) return;
        Import(c);
        TreatyPass(c);
        foreach (var w in b.Wars.ToList()) if (w.Alive) StepWar(c, w);
        var realms = c.AliveRealms().ToList();
        Envoys(c, realms);
        Declarations(c, realms);
        Grants(c, realms);
        Recover(c, realms);
        FoundedRealmsLive(c);
        b.LastSeasonDay = c.Day;
    }

    /// <summary>Each ruler sends an envoy to each realm he touches: it finds its road with 0x672 (a fresh fact) and now and again an insult gives the sender a casus belli.</summary>
    public static void Envoys(PoliticsCtx c, List<int> realms)
    {
        foreach (int a in realms)
        {
            if (c.HeroRules(a)) continue;
            foreach (int bb in realms)
            {
                if (a == bb || !c.InContact(a, bb)) continue;
                ulong envoy = (ulong)a * 256 + (ulong)bb;
                int roll = DetRng.Range(c.Seed, c.Day, envoy, RngStreams.EnvoyEvents, 0, 0, 100);
                if (roll < c.S.EnvoyArrivePct) Learn(c, a, bb);
                int ins = DetRng.Range(c.Seed, c.Day, envoy, RngStreams.EnvoyEvents, 1, 0, 100);
                if (ins < 2 && c.B.WarOf(a, bb) == null)
                {
                    var cb = AddCb(c, a, bb, CbKind.Insult);
                    Opinions.Add(c.B, c.RB, c.KeyOf(a), c.KeyOf(bb), "insult", c.Day, cb.Entry);
                }
            }
        }
    }

    /// <summary>Rulers who are at peace weigh each realm they touch and declare at the threshold (4.1), at the pace the settings give.</summary>
    public static void Declarations(PoliticsCtx c, List<int> realms)
    {
        foreach (int a in realms)
        {
            if (c.B.WarOf(a) != null || c.HeroRules(a) || (c.RulerOf(a) == 0 && !c.RB.Rulers.ContainsKey(a))) continue;
            int bestB = -1, bestU = int.MinValue; CasusBelli? bestCb = null; bool bestClaim = false;
            foreach (int bb in realms)
            {
                if (bb == a || c.B.WarOf(bb) != null || !c.InContact(a, bb)) continue;
                var (u, cb, claim, _) = UtilityOf(c, a, bb);
                if (cb == null && !claim) continue;                                  // a war needs a grievance or a claim
                if (u > bestU) { bestU = u; bestB = bb; bestCb = cb; bestClaim = claim; }
            }
            if (bestB < 0) continue;
            var traits = c.TraitsOf(a);
            if (!WarUtility.Declares(c.S, bestU, traits)) continue;
            if (DetRng.Range(c.Seed, c.Day, (ulong)a, RngStreams.DiplomacyTies, 4, 0, 100) >= c.S.DeclareChancePct) continue;
            Declare(c, a, bestB, bestCb, bestClaim, bestU);
        }
    }

    /// <summary>
    /// A ruler grants a vacant fief of his realm to the best of those who petitioned for one (4.2): <c>opinion + standing / 10 + service done</c>, at least the petition floor. Only a vacant fief moves (the Phase 1 bug fixed by construction).
    /// </summary>
    public static int Grants(PoliticsCtx c, List<int> realms)
    {
        int n = 0;
        foreach (int a in realms)
        {
            if (c.HeroRules(a)) continue;
            foreach (var f in c.RB.Fiefs.Values.Where(x => x.Realm == a && Road.Vacant(x)).OrderBy(x => x.Fief).ToList())
            {
                ulong best = 0; int bestScore = int.MinValue;
                foreach (var p in c.B.Petitions.OrderBy(x => x.Person))
                {
                    if (!c.R.Alive((long)p.Person) || c.Links.IsHero(p.Person)) continue;
                    int sc = Road.CandidateScore(c, new EntityId(p.Person), a);
                    if (sc > bestScore) { bestScore = sc; best = p.Person; }
                }
                if (best != 0 && bestScore >= c.S.PetitionOpinion && Road.GrantFief(c, f.Fief, new EntityId(best), "ch_note_dp_petition")) n++;
            }
        }
        return n;
    }

    /// <summary>Hosts at peace recover a share of what they lost.</summary>
    public static void Recover(PoliticsCtx c, List<int> realms)
    {
        foreach (var (k, row) in c.B.Realms)
            if (row.LossPm > 0 && c.B.WarOf(k) == null) row.LossPm = Math.Max(0, row.LossPm - c.S.LossRecoverPermille);
    }

    /// <summary>A founded realm with no fief left is gone (its ruler's line goes on as a lord elsewhere).</summary>
    public static void FoundedRealmsLive(PoliticsCtx c)
    {
        foreach (var (k, row) in c.B.Realms)
        {
            if (!row.Founded || row.Dead) continue;
            bool any = false; foreach (var f in c.RB.Fiefs.Values) if (f.Realm == k) { any = true; break; }
            if (!any) { row.Dead = true; foreach (var w in c.B.Wars) if (w.Alive && w.Involves(k)) w.EndDay = c.Day; }
        }
    }
}
