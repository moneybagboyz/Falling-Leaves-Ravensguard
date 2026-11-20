using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Growth;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Land;

/// <summary>What a lord's zoning makes of a piece of ground (holdings-and-works-v2 3.4): a lot (a plot, the square or a street stands on it), a field (a parcel is worked there), the commons round the square (kept open), or waste.</summary>
public enum Zone : byte { Lot, Field, Waste, Common }

/// <summary>A keyed chronicle writer for the land's Phase 6 entries (text key and typed parameters instead of an English sentence).</summary>
public delegate EntityId LandChronicleKeyed(GameTime when, string type, string textKey, IReadOnlyList<ChronParam> parameters, IReadOnlyList<EntityId> subjects, IReadOnlyList<Cause> causes, params (string Key, string Value)[] data);

/// <summary>
/// Zoning and new streets (holdings-and-works-v2 3.4, closes Phase 5's S12). A lord's zoning marks ground as lots, fields, waste or common; when a place has no free plot left and more than
/// <c>StreetWaitingOver</c> women wait for a home, the lord's planner lays a <b>new street</b>: a segment that leaves the end of the nearest existing street (nearest the square, then the lower street
/// and end, straight on before the two side turns) over waste, with 6 to 10 plots along it (3 to 5 a side, <see cref="LandRulesDef.StreetLotsPerSideMin"/> to <c>Max</c>), never across a field, water, steep ground or the
/// commons. The plots are not records: growth and the lord's listings find them with <see cref="RegionGrowth.FindPlot"/> like every other street's, priced by the land rules (an 11 x 28 m town plot at
/// 150 mites a square metre is 462 crowns). The street itself is a <see cref="StreetRecord"/> of the growth log, replayed on load; the visited chunks it crosses get ID-keyed deltas
/// (<see cref="RegionGrowth.Change"/>), the others generate with it. Integer arithmetic and fixed orders only; nothing is drawn.
/// </summary>
public static class Zoning
{
    private const int SampleStepM = 8, MaxSlopePermille = 140;

    /// <summary>The zone of a point (region metres) as the lord of settlement <paramref name="s"/> has it.</summary>
    public static Zone ZoneOf(RegionData r, LandRulesDef rules, int s, int xM, int yM)
    {
        int t = LandRules.TileAt(xM, yM); if (t < 0) return Zone.Lot;
        var plan = r.Settlements[s].Plan;
        var pt = new RectM(xM, yM, 1, 1);
        if (plan.Square.W > 0 && Overlap(pt, plan.Square)) return Zone.Lot;
        foreach (var l in plan.Lots) if (Overlap(pt, l.Rect)) return Zone.Lot;
        foreach (var st in plan.Streets) if (Overlap(pt, StreetRect(st))) return Zone.Lot;
        if (r.ParcelMap[t] != 0) return Zone.Field;
        if (plan.Square.W > 0)
        {
            long dx = Math.Max(0, Math.Max(plan.Square.X - xM, xM - (plan.Square.Right - 1))), dy = Math.Max(0, Math.Max(plan.Square.Y - yM, yM - (plan.Square.Bottom - 1)));
            long ring = rules.CommonRingM; if (dx * dx + dy * dy <= ring * ring) return Zone.Common;
        }
        return Zone.Waste;
    }

    private static bool Overlap(RectM a, RectM b) => a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;
    private static RectM StreetRect(Street st) => new(Math.Min(st.A.X, st.B.X) - st.WidthM / 2, Math.Min(st.A.Y, st.B.Y) - st.WidthM / 2, Math.Abs(st.A.X - st.B.X) + st.WidthM, Math.Abs(st.A.Y - st.B.Y) + st.WidthM);

    /// <summary>The lord's planner: the street a place would get, or null when no end of an existing street has waste to run over with at least <c>2 x StreetLotsPerSideMin</c> plots along it. A pure function of the region.</summary>
    public static StreetRecord? Plan(RegionData r, int s, LandRulesDef ru, GrowthTuningDef t, int day, IReadOnlyList<RectM>? reserved = null)
    {
        var st = r.Settlements[s]; var plan = st.Plan; bool town = st.Class == SettlementClass.Town;
        int w = town ? t.PlotWidthTownM : t.PlotWidthVillageM, d = town ? t.PlotDepthTownM : t.PlotDepthVillageM;
        long cx = plan.Square.W > 0 ? plan.Square.CenterX : st.CenterX, cy = plan.Square.W > 0 ? plan.Square.CenterY : st.CenterY;
        int kMin = Math.Max(1, ru.StreetLotsPerSideMin), kMax = Math.Max(kMin, ru.StreetLotsPerSideMax), width = Math.Max(3, ru.StreetWidthM);
        var cands = new List<(long D2, int Street, int End, int Dir, PointM P, int Dx, int Dy, int OldW)>();
        for (int si = 0; si < plan.Streets.Count; si++)
        {
            var sr = plan.Streets[si];
            if (sr.A == sr.B) continue;
            bool horiz = Math.Abs(sr.B.X - sr.A.X) >= Math.Abs(sr.B.Y - sr.A.Y);
            for (int e = 0; e < 2; e++)
            {
                var p = e == 0 ? sr.A : sr.B; var o = e == 0 ? sr.B : sr.A;
                int ox = horiz ? Math.Sign(p.X - o.X) : 0, oy = horiz ? 0 : Math.Sign(p.Y - o.Y);
                (int, int)[] dirs = [(ox, oy), (oy, ox), (-oy, -ox)];                      // straight on, then the two side turns
                long dx = p.X - cx, dy = p.Y - cy;
                for (int k = 0; k < 3; k++) cands.Add((dx * dx + dy * dy, si, e, k, p, dirs[k].Item1, dirs[k].Item2, sr.WidthM));
            }
        }
        cands.Sort((a, b) => a.D2 != b.D2 ? a.D2.CompareTo(b.D2) : a.Street != b.Street ? a.Street.CompareTo(b.Street) : a.End != b.End ? a.End.CompareTo(b.End) : a.Dir.CompareTo(b.Dir));
        foreach (var c in cands)
        {
            for (int k = kMax; k >= kMin; k--)
            {
                int len = k * (w + 1) + c.OldW + width;
                var b = new PointM(c.P.X + c.Dx * len, c.P.Y + c.Dy * len);
                var street = new Street(c.P, b, width);
                if (!CorridorOk(r, ru, s, c.P, c.Dx, c.Dy, len, c.OldW / 2 + width / 2 + 1, width, reserved)) continue;
                var plots = RegionGrowth.PlotsOnStreet(r, s, street, w, d, 2 * kMax, reserved);
                if (plots.Count < 2 * kMin) continue;
                // trim the street to the far end of the plots it serves
                int reach = 0;
                foreach (var (q, _) in plots)
                {
                    int far = c.Dx != 0 ? (c.Dx > 0 ? q.Right - c.P.X : c.P.X - q.X) : (c.Dy > 0 ? q.Bottom - c.P.Y : c.P.Y - q.Y);
                    reach = Math.Max(reach, far);
                }
                reach = Math.Min(len, Math.Max(reach, c.OldW + width));
                var trimmed = new Street(c.P, new PointM(c.P.X + c.Dx * reach, c.P.Y + c.Dy * reach), width);
                int count = RegionGrowth.PlotsOnStreet(r, s, trimmed, w, d, 100, reserved).Count;
                // the street serves 6 to 10 plots: past ten the far end is cut back a plot's width at a time
                while (count > 2 * kMax && reach > c.OldW + width + w)
                {
                    reach -= w; trimmed = new Street(c.P, new PointM(c.P.X + c.Dx * reach, c.P.Y + c.Dy * reach), width);
                    count = RegionGrowth.PlotsOnStreet(r, s, trimmed, w, d, 100, reserved).Count;
                }
                if (count < 2 * kMin || count > 2 * kMax) continue;
                return new StreetRecord(s, trimmed.A, trimmed.B, width, count, day);
            }
        }
        return null;
    }

    /// <summary>The ground a street runs over: dry, level enough, not a field, not the commons, inside the region's margin (sampled every <see cref="SampleStepM"/> metres past the junction).</summary>
    private static bool CorridorOk(RegionData r, LandRulesDef ru, int s, PointM p, int dx, int dy, int len, int skip, int width, IReadOnlyList<RectM>? reserved)
    {
        for (int i = skip; i <= len; i += SampleStepM)
        {
            int x = p.X + dx * i, y = p.Y + dy * i;
            if (x < 96 || y < 96 || x >= RegionData.Meters - 96 || y >= RegionData.Meters - 96) return false;
            int t = LandRules.TileAt(x, y); if (t < 0 || r.Water[t] != 0 || r.SlopePermille[t] > MaxSlopePermille) return false;
            if (ZoneOf(r, ru, s, x, y) != Zone.Waste) return false;
            if (reserved != null) { var q = new RectM(x - width / 2, y - width / 2, width, width); foreach (var rv in reserved) if (Overlap(q, rv)) return false; }       // a plot a household is building on is not a street's
        }
        return true;
    }

    /// <summary>Women waiting for a home in a place (alive, unmarried, with a marriage wait).</summary>
    public static int Waiting(Population pop, int settlement)
    {
        var pt = pop.People; var hht = pop.Households; int n = 0;
        for (int p = 0; p < pt.Count; p++)
        {
            if (pt.Status[p] != PersonStatus.Alive || pt.Sex[p] != Sex.Female || pt.Spouse[p] >= 0 || pt.MarriageWait[p] == 0) continue;
            int hh = pt.Household[p]; if (hh >= 0 && hh < hht.Count && hht.Settlement[hh] == settlement) n++;
        }
        return n;
    }

    /// <summary>
    /// The season's planning (H6 S6): for each town and village in index order whose lord allows it (<see cref="LandWorld.StreetsAllowed"/>), with no free lot and more than <c>StreetWaitingOver</c> women waiting,
    /// and no street laid there within <c>StreetCooldownDays</c>, a street is planned and laid. Returns the streets laid.
    /// </summary>
    public static List<StreetRecord> Seasonal(LandWorld lw, int day, LandChronicleKeyed? chron)
    {
        var laid = new List<StreetRecord>();
        var r = lw.Region; var gs = lw.GrowthLog;
        for (int s = 0; s < r.Settlements.Count; s++)
        {
            var st = r.Settlements[s];
            if (st.Class == SettlementClass.Farmstead) continue;
            if (lw.StreetsAllowed != null && !lw.StreetsAllowed(st.FiefIndex)) continue;
            if (gs.Streets.Any(x => x.Settlement == s && day - x.Day < lw.Rules.StreetCooldownDays)) continue;
            if (lw.FreeLots(s) > 0 || Waiting(lw.Pop, s) <= lw.Rules.StreetWaitingOver) continue;
            var rec = Plan(r, s, lw.Rules, lw.GrowthTuning, day, gs.Projects.Where(p => p.Settlement == s && p.Kind != ProjectKind.Assart && p.Lot < 0).Select(p => p.Rect).ToList());
            if (rec == null) continue;
            Lay(lw, rec, chron);
            laid.Add(rec);
        }
        return laid;
    }

    /// <summary>Lays a planned street: the region changes under the growth lock with the visited chunks' deltas, the record joins the log.</summary>
    public static void Lay(LandWorld lw, StreetRecord rec, LandChronicleKeyed? chron)
    {
        var r = lw.Region;
        int pad = rec.WidthM / 2 + RegionGrowth.StreetPadM + 6;
        var touched = new RectM(Math.Min(rec.A.X, rec.B.X) - pad, Math.Min(rec.A.Y, rec.B.Y) - pad, Math.Abs(rec.A.X - rec.B.X) + 2 * pad, Math.Abs(rec.A.Y - rec.B.Y) + 2 * pad);
        RegionGrowth.Change(r, lw.GrowthSink, [touched], () => { RegionGrowth.ApplyStreet(r, rec); return 0; });
        lw.GrowthLog.Streets.Add(rec);
        if (chron == null) return;
        var st = r.Settlements[rec.Settlement];
        var lord = LandRegistry.LordOf(lw.Pop, rec.Settlement).Holder;
        chron(new GameTime(rec.Day * 86_400L + 9 * 3600), "street_laid", "ch_street_laid", [ChronParam.Place("place", st.Id), ChronParam.Count("plots", rec.Plots)], lord == EntityId.None ? [st.Id] : [lord, st.Id],
            [new Cause(CauseKind.Migration, null, NoteKey: "ch_note_street_no_plots")], ("settlement", st.Name), ("plots", rec.Plots.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }
}
