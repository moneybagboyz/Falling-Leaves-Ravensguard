using Ravensguard.Content;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Sites;

/// <summary>A collapse on its way: the zone, the centre and radius, the step it falls (6 sim-seconds after the creak).</summary>
public sealed record PendingCaveIn(int Zone, int Cell, int Radius, long AtSec, ulong EventId);

public sealed partial class SiteCrawl
{
    public List<PendingCaveIn> CaveIns { get; } = [];
    /// <summary>A flood event running in this site: its start (game second); the lowest level rises 30 cm an hour for 6 hours (4.5.2).</summary>
    public long FloodSince { get; set; } = -1;

    // ------------------------------------------------------------------ once per sim-second

    private void PerSecond()
    {
        var K = C.K;
        foreach (var m in Members)
        {
            if (m.Dead) continue;
            if (m.BusyS > 0 && Fight == null) { m.BusyS--; if (m.BusyS == 0) FinishBusy(m); }
            if (m.HeldS > 0) { m.HeldS = Math.Max(0, m.HeldS - Math.Max(1, m.Athletics / 30)); if (m.HeldS == 0) Note(CrawlEventKind.Freed, Members.IndexOf(m)); }
            Air(m);
            if (m.Dead) continue;
            Water(m);
            if (m.Dead) continue;
            if (m.Superstitious && (!m.Light.Lit && LightAt(m.Cell) < K.LostLight || Map.Air[m.Cell] < K.AirDim)) m.MoraleX100 = Math.Max(0, m.MoraleX100 - K.SuperstitiousMoraleX100 / 60);
            bool lostNow = !m.HasMap && LightAt(m.Cell) < K.LostLight;
            if (lostNow != m.Lost) { m.Lost = lostNow; if (m.Hero) Note(lostNow ? CrawlEventKind.Lost : CrawlEventKind.Found); }
            if (m.Book.Conscious == false && !m.Unconscious) { m.Unconscious = true; StopPath(m, true); }
            else if (m.Unconscious && m.Book.Conscious && m.FoulS == 0 && !m.Sunk) m.Unconscious = false;
            if (m.Book.Dead && !m.Dead) Die(m, m.Book.Death.ToString());
        }
        DetectTraps();
        CaveInWindow();
        foreach (var c in CaveIns.ToList()) if (c.AtSec <= GameSec) { CaveIns.Remove(c); Collapse(c); }
    }

    private void PerHour()
    {
        if (FloodSince >= 0)
        {
            int hours = (int)Math.Min(C.K.FloodHours, (GameSec - FloodSince) / 3600);
            int want = hours * C.K.FloodRiseCmPerHour;
            if (want > Map.FloodCm[Map.Levels - 1]) { Map.FloodCm[Map.Levels - 1] = want; Note(CrawlEventKind.WaterRising, Map.Levels - 1, want); }
        }
        if (GameSec % 86400 == 0 && Map.Template.StartsWith("mine", StringComparison.Ordinal) | Map.Template.StartsWith("ruin", StringComparison.Ordinal)) Note(CrawlEventKind.RatsAtFood, C.K.RatsPmPerDay);
    }

    private void Die(CrawlMember m, string cause)
    {
        if (m.Dead) return;
        m.Dead = true; m.DeathCause = cause; m.Unconscious = false; m.Light.Lit = false;
        StopPath(m, true);
        Note(CrawlEventKind.Died, Members.IndexOf(m), 0, cause);
        _lightDirty = true;
    }

    // ------------------------------------------------------------------ bad air (4.5.1, EX-7, EX-18)

    private void Air(CrawlMember m)
    {
        var K = C.K;
        int a = Map.Air[m.Cell];
        var e = SiteLight.Air(a, K);
        if (e.Warning && !m.WarnedAir) { m.WarnedAir = true; if (m.Hero) { Note(CrawlEventKind.AirWarning, a); StopPath(m, true); StopRest(); } }
        else if (!e.Warning && m.WarnedAir) { m.WarnedAir = false; if (m.Hero) Note(CrawlEventKind.AirClear, a); }
        if (e.FlameOut && m.Light.Lit && m.Light.Kind != LightKind.Lantern) { m.Light.Lit = false; Note(CrawlEventKind.FlameDied, Members.IndexOf(m)); _lightDirty = true; }
        if (e.Foul)
        {
            m.FoulS++; m.ClearS = 0;
            if (m.FoulS >= K.AirUnconsciousS && !m.Unconscious) { m.Unconscious = true; StopPath(m, true); Note(CrawlEventKind.Unconscious, Members.IndexOf(m)); }
            if (m.FoulS >= K.AirDeathS) Die(m, "Suffocation");
        }
        else if (m.FoulS > 0)
        {
            m.ClearS++;
            if (a >= K.AirDazed && m.ClearS >= 10) { m.FoulS = 0; if (m.Unconscious && m.Book.Conscious) { m.Unconscious = false; Note(CrawlEventKind.Woke, Members.IndexOf(m)); } }
        }
    }

    /// <summary>The air effect at a member's cell (accuracy, regeneration, speed), for the fight and the HUD.</summary>
    public AirEffect AirOf(CrawlMember m) => SiteLight.Air(Map.Air[m.Cell], C.K);

    // ------------------------------------------------------------------ water and swimming (4.5.2, EX-8)

    private void Water(CrawlMember m)
    {
        var K = C.K;
        int depth = Map.Water(m.Cell);
        if (depth <= K.SwimCm)
        {
            if (m.Swimming) { m.Swimming = false; m.Sunk = false; m.BreathS = 0; m.SwimChecks = 0; }
            return;
        }
        if (!m.Swimming) { m.Swimming = true; m.BreathS = 0; Note(CrawlEventKind.Swimming, Members.IndexOf(m)); if (m.Light.Lit && m.Light.Kind != LightKind.Lantern) { m.Light.Lit = false; _lightDirty = true; } }
        int sinceIn = (int)(m.BreathS);
        // the sink check: in more than 15 kg of gear an Athletics check every 5 sim-seconds, the first as one goes in; armour over 10 kg doubles the odds of sinking
        int gearKg = (m.GearG + m.LootG) / 1000;
        if (gearKg > K.SinkGearKg && !m.Sunk && m.SwimChecks % K.SinkCheckS == 0)
        {
            int failPm = 1000 - Math.Clamp(K.SinkCheckPm + m.Athletics * 10, 0, 1000);
            if (ArmourKg(m) > 10) failPm = Math.Min(1000, failPm * 2);
            int roll = DetRng.Range(Seed, SimSecond, m.Person, RngStreams.HazardEvent, 0x5100, 0, 1000);
            if (roll < failPm) { m.Sunk = true; Note(CrawlEventKind.Sinking, Members.IndexOf(m)); }
        }
        m.SwimChecks++;
        if (m.Sunk)
        {
            m.BreathS++;
            if (m.BreathS >= K.HoldBreathS + m.Athletics / 2) { Die(m, "Drowned"); Note(CrawlEventKind.Drowned, Members.IndexOf(m)); }
        }
        _ = sinceIn;
    }

    private static int ArmourKg(CrawlMember m) { int g = 0; foreach (var w in m.Worn) g += w.Stats.Shape.MassG; return g / 1000; }

    // ------------------------------------------------------------------ traps (4.6, EX-10)

    private void DetectTraps()
    {
        var K = C.K;
        foreach (var m in Members)
        {
            if (!m.CanAct && m.BusyS <= 0 || m.Dead || m.Unconscious) continue;
            foreach (var t in Map.Traps)
            {
                if (!t.Armed || t.Detected) continue;
                var (tl, tx, ty) = Map.At(t.Cell);
                if (tl != m.Level) continue;
                int dx = tx * 1000 + 500 - m.X, dy = ty * 1000 + 500 - m.Y;
                if ((long)dx * dx + (long)dy * dy > (long)K.TrapNearMm * K.TrapNearMm) continue;
                if (!SiteTraps.Facing(m.FX, m.FY, dx, dy)) continue;
                if (!C.Traps.TryGetValue(t.Def, out var d)) continue;
                int pm = SiteTraps.DetectPm(m.Perception, d.Detect, LightAt(t.Cell), m.Sneak);
                if (DetRng.Range(Seed, SimSecond, m.Person, RngStreams.TrapDetect, (uint)t.Id, 0, 1000) < pm) Detect(t, m);
            }
        }
    }

    private void Detect(SiteTrap t, CrawlMember by)
    {
        t.Detected = true;
        var (l, x, y) = Map.At(t.Cell);
        Grid.SetOpen(x, y, SiteMap.Z(l), false);
        Known.Mark(t.Cell);
        Note(CrawlEventKind.TrapDetected, t.Id, Members.IndexOf(by), t.Def);
        foreach (var m in Members) StopPath(m, true);
        StopRest();
    }

    private void StepOnTrap(CrawlMember m, int cell)
    {
        var t = Map.TrapAt(cell);
        if (t == null || !t.Armed) return;
        if (m.Sneak && m.CarriedKg + 75 < C.K.SneakSkipMaxKg + 75 && m.CarriedKg < C.K.SneakSkipMaxKg
            && DetRng.Range(Seed, SimSecond, ((ulong)t.Id << 20) + (ulong)(uint)Members.IndexOf(m), RngStreams.TrapTrigger, 0, 0, 1000) < C.K.SneakSkipPm) return;
        Trigger(t, m);
    }

    /// <summary>A trap goes off on <paramref name="m"/> (4.6): its blow, fall, collapse, alarm or held foot through the resolver.</summary>
    public void Trigger(SiteTrap t, CrawlMember m)
    {
        if (!C.Traps.TryGetValue(t.Def, out var d)) return;
        t.Triggered++; t.Armed = false; t.Detected = true;
        int who = Members.IndexOf(m);
        Note(CrawlEventKind.TrapTriggered, t.Id, who, t.Def);
        foreach (var x in Members) StopPath(x, true);
        StopRest();
        var key = new BlowKey(Seed, SimSecond, 0x7000_0000UL | (ulong)t.Id, (uint)t.Triggered);
        int Part(uint draw)
        {
            int roll = DetRng.Range(Seed, SimSecond, ((ulong)t.Id << 20) + (ulong)(uint)who, RngStreams.TrapTrigger, draw, 0, 1000), acc = 0;
            bool right = DetRng.Range(Seed, SimSecond, ((ulong)t.Id << 20) + (ulong)(uint)who, RngStreams.TrapTrigger, draw + 1, 0, 2) == 0;
            foreach (var p in d.Parts) { acc += p.Pm; if (roll < acc) return SiteBlows.Part(m.Plan, p.Part, right); }
            return SiteBlows.Part(m.Plan, d.Parts.Count > 0 ? d.Parts[0].Part : "upper_body", right);
        }
        switch (d.Effect)
        {
            case "blow":
                Blows.Blow(m.Book, m.Plan, m.Worn, m.Kit.SizeCm3, d.Attack, d.MomentumMilli, d.AreaMm2, d.PenMm, Part(10), key, GameSec, 0x7000_0000UL | (ulong)t.Id);
                break;
            case "fall":
                Blows.Fall(m.Book, m.Plan, m.Worn, m.Kit.SizeCm3, 75 + m.CarriedKg, d.FallMm, Part(10), key, GameSec, 0x7000_0000UL | (ulong)t.Id);
                if (d.SpikeMilli > 0 && DetRng.Range(Seed, SimSecond, (ulong)t.Id, RngStreams.TrapTrigger, 20, 0, 2) == 0)
                {
                    int n = 1 + DetRng.Range(Seed, SimSecond, (ulong)t.Id, RngStreams.TrapTrigger, 21, 0, 3);
                    for (int i = 0; i < n; i++) Blows.Blow(m.Book, m.Plan, m.Worn, m.Kit.SizeCm3, "point", d.SpikeMilli, d.AreaMm2, d.PenMm, Part(30u + (uint)i * 2), key with { SwingSerial = key.SwingSerial + 100u + (uint)i }, GameSec, 0x7000_0000UL | (ulong)t.Id);
                }
                Note(CrawlEventKind.Fell, who, d.FallMm);
                m.BusyS = Math.Max(m.BusyS, 10); m.BusyTarget = -1;
                break;
            case "floor":
                CollapseFloor(t, d);
                break;
            case "alarm":
                Alarm(t.Cell, d.AlarmM);
                break;
            case "hold":
                if (d.MomentumMilli > 0) Blows.Blow(m.Book, m.Plan, m.Worn, m.Kit.SizeCm3, d.Attack, d.MomentumMilli, d.AreaMm2, d.PenMm, Part(10), key, GameSec, 0x7000_0000UL | (ulong)t.Id);
                m.HeldS = d.HoldS;
                Note(CrawlEventKind.Held, who, d.HoldS);
                break;
        }
    }

    private void CollapseFloor(SiteTrap t, SiteTrapDef d)
    {
        var (l, x, y) = Map.At(t.Cell);
        var falling = Members.Where(m => !m.Dead && m.Level == l && Math.Abs(m.X / 1000 - x) <= 1 && Math.Abs(m.Y / 1000 - y) <= 1).ToList();
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (!Map.In(l, x + dx, y + dy)) continue;
                int c = Map.Index(l, x + dx, y + dy);
                if (Map.Tiles[c] is SiteTile.Floor && Map.PortalAt(c) == null && c != Map.Mouth) { Map.Tiles[c] = SiteTile.Hole; Grid.SetOpen(x + dx, y + dy, SiteMap.Z(l), false); }
            }
        foreach (var m in falling) FallThrough(m, m.Cell);
        _lightDirty = _mapDirty = true;
    }

    /// <summary>A fall through a hole (a collapsing floor): one level down onto the nearest open cell, 3 m, through the resolver; on the deepest level it is a pit.</summary>
    private void FallThrough(CrawlMember m, int cell)
    {
        var (l, x, y) = Map.At(cell);
        if (l + 1 < Map.Levels)
        {
            int to = NearestOpen(l + 1, x, y);
            if (to >= 0)
            {
                var (tl, tx, ty) = Map.At(to);
                m.Level = tl; m.X = tx * 1000 + 500; m.Y = ty * 1000 + 500; m.Cell = to; m.Crumbs.Add(to);
                if (m.Hero) Note(CrawlEventKind.LevelChanged, tl);
            }
        }
        FallBlow(m, 3000, 0x4F00_0000u + (uint)(cell & 0xFFFFFF));
        StopPath(m, true);
        _lightDirty = _mapDirty = true;
    }

    private void FallBlow(CrawlMember m, int heightMm, uint serial)
    {
        int who = Members.IndexOf(m);
        int g = DetRng.Range(Seed, SimSecond, m.Person, RngStreams.TrapTrigger, serial, 0, 100);
        bool right = (g & 1) == 0;
        string part = g < 45 ? "lower_leg" : g < 70 ? "upper_leg" : g < 85 ? "lower_arm" : "upper_body";
        Blows.Fall(m.Book, m.Plan, m.Worn, m.Kit.SizeCm3, 75 + m.CarriedKg, heightMm, SiteBlows.Part(m.Plan, part, right), new BlowKey(Seed, SimSecond, 0x4600_0000UL | m.Person, serial), GameSec, 0x4600_0000UL);
        Note(CrawlEventKind.Fell, who, heightMm);
    }

    private int NearestOpen(int level, int x, int y)
    {
        for (int r = 0; r < 12; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r || !Map.In(level, x + dx, y + dy)) continue;
                    int c = Map.Index(level, x + dx, y + dy);
                    if (Map.Tiles[c] is SiteTile.Floor or SiteTile.Water or SiteTile.DoorOpen) return c;
                }
        return -1;
    }

    private void Disarm(CrawlMember m, SiteTrap t)
    {
        if (!C.Traps.TryGetValue(t.Def, out var d) || !t.Armed) return;
        if (d.Disarm <= 0) { Note(CrawlEventKind.TrapCannotDisarm, t.Id); return; }
        int roll = DetRng.Range(Seed, SimSecond, m.Person, RngStreams.TrapTrigger, 0x0D00u + (uint)t.Id, 0, 1000);
        if (roll < SiteTraps.DisarmPm(m.Lockpicking, d.Disarm))
        {
            t.Armed = false;
            var (l, x, y) = Map.At(t.Cell);
            if (Map.Tiles[t.Cell] != SiteTile.Hole) Grid.SetOpen(x, y, SiteMap.Z(l), true);
            Note(CrawlEventKind.TrapDisarmed, t.Id);
            return;
        }
        Note(CrawlEventKind.TrapDisarmFailed, t.Id);
        if (DetRng.Range(Seed, SimSecond, m.Person, RngStreams.TrapTrigger, 0x0E00u + (uint)t.Id, 0, 1000) < C.K.DisarmFailTriggerPm) Trigger(t, m);
    }

    /// <summary>A tripwire: the band within range wakes and comes (4.6).</summary>
    public void Alarm(int cell, int radiusM)
    {
        var (l, x, y) = Map.At(cell);
        foreach (var n in Map.Inhabitants)
        {
            if (n.State is InhabitantState.Dead or InhabitantState.Fled or InhabitantState.Away || n.Monster.Length > 0 || n.Level != l) continue;
            long dx = n.X - (x * 1000 + 500), dy = n.Y - (y * 1000 + 500);
            if (dx * dx + dy * dy <= (long)radiusM * radiusM * 1_000_000) { n.State = InhabitantState.Hunt; n.TargetX = x * 1000 + 500; n.TargetY = y * 1000 + 500; n.TargetLevel = l; }
        }
    }

    // ------------------------------------------------------------------ cave-ins (4.5.3, EX-9)

    /// <summary>The once-a-minute roll of an unstable zone with someone in it: <c>P = instability / 20,000</c>, x3 after a loud fight in the window, x2 in heavy rain above (stream 0x4B3).</summary>
    public static bool CaveInRolls(ulong seed, long window, ulong siteId, int zoneId, int instabilityPm, int boostPm)
    {
        long p = (long)instabilityPm * boostPm / 1000;                           // permille of 20,000: p / 20,000 per minute
        return DetRng.Range(seed, window, siteId, RngStreams.HazardEvent, 0x3000u + (uint)zoneId, 0, 20_000) < p;
    }

    private void CaveInWindow()
    {
        var K = C.K;
        int windowS = K.CaveInWindowS * 20;
        if (GameSec % windowS != 0) return;
        long window = GameSec / windowS;
        foreach (var z in Map.Unstable)
        {
            if (z.Collapsed || CaveIns.Any(c => c.Zone == z.Id)) continue;
            CrawlMember? inside = Members.FirstOrDefault(m => !m.Dead && z.Contains(m.Level, m.X / 1000, m.Y / 1000));
            if (inside == null) continue;
            int boost = 1000 * (_loudWindow == window || _loudWindow == window - 1 ? 3 : 1) * (HeavyRainAbove ? 2 : 1);
            if (!CaveInRolls(Seed, window, Map.SiteId, z.Id, z.InstabilityPm, boost)) continue;
            ulong ev = (ulong)window << 16 | (uint)z.Id;
            int radius = K.CaveInRadiusMin + DetRng.Range(Seed, (long)ev, Map.SiteId, RngStreams.CaveInDebris, 0, 0, K.CaveInRadiusMax - K.CaveInRadiusMin + 1);
            CaveIns.Add(new PendingCaveIn(z.Id, inside.Cell, radius, GameSec + K.CaveInWarnS * 20, ev));
            // the creak: a perception check at 50% (the hero hears it, or a companion calls it)
            bool heard = Members.Any(m => !m.Dead && !m.Unconscious && DetRng.Range(Seed, (long)ev, m.Person, RngStreams.CaveInDebris, 1, 0, 100) < 50);
            if (heard) { Note(CrawlEventKind.CaveInCreak, z.Id, radius); foreach (var m in Members) StopPath(m, true); StopRest(); }
        }
    }

    /// <summary>The roof comes down: rubble in the radius (the delta persists), a rubble blow on each one there who does not dodge (80% if free).</summary>
    private void Collapse(PendingCaveIn c)
    {
        var K = C.K;
        var z = Map.Unstable[c.Zone]; z.Collapsed = true;
        var (l, cx, cy) = Map.At(c.Cell);
        int i = 0;
        foreach (var m in Members)
        {
            i++;
            if (m.Dead || m.Level != l) continue;
            int dx = m.X / 1000 - cx, dy = m.Y / 1000 - cy;
            if (dx * dx + dy * dy > c.Radius * c.Radius) continue;
            bool free = m.CanAct && !m.Sneak;
            int roll = DetRng.Range(Seed, (long)c.EventId, m.Person, RngStreams.CaveInDebris, 2, 0, 1000);
            if (free && roll < K.CaveInDodgePm) continue;
            int g = DetRng.Range(Seed, (long)c.EventId, m.Person, RngStreams.CaveInDebris, 3, 0, 100);
            string part = g < 35 ? "upper_body" : g < 55 ? "head" : g < 75 ? "upper_arm" : "lower_leg";
            Blows.Blow(m.Book, m.Plan, m.Worn, m.Kit.SizeCm3, "blunt", K.CaveInBlowMilli, 120, 0, SiteBlows.Part(m.Plan, part, (g & 1) == 0),
                new BlowKey(Seed, (long)c.EventId, 0x4300_0000UL | (ulong)i, 0), GameSec, 0x4300_0000UL);
        }
        var keep = new HashSet<int>(Members.Where(m => !m.Dead).Select(m => m.Cell));
        for (int dy = -c.Radius; dy <= c.Radius; dy++)
            for (int dx = -c.Radius; dx <= c.Radius; dx++)
            {
                if (dx * dx + dy * dy > c.Radius * c.Radius || !Map.In(l, cx + dx, cy + dy)) continue;
                int cell = Map.Index(l, cx + dx, cy + dy);
                if (keep.Contains(cell) || Map.PortalAt(cell) != null || cell == Map.Mouth) continue;
                if (Map.Tiles[cell] is SiteTile.Floor or SiteTile.DoorOpen or SiteTile.Door or SiteTile.Water)
                {
                    Map.Tiles[cell] = SiteTile.Rubble;
                    Grid.SetOpen(cx + dx, cy + dy, SiteMap.Z(l), false);
                    Lights.SetOpaque(cx + dx, cy + dy, SiteMap.Z(l), true);
                }
            }
        Note(CrawlEventKind.CaveIn, c.Zone, c.Radius);
        _lightDirty = _mapDirty = true;
    }

    /// <summary>A loud event (a fight, an axe on a prop) triples the cave-in odds for the window (4.5.3).</summary>
    public void Loud() => _loudWindow = (int)(GameSec / (C.K.CaveInWindowS * 20));

    // ------------------------------------------------------------------ loot and the taboo (4.2, 4.10)

    private void Loot(CrawlMember m, SiteContainer box)
    {
        box.Opened = true;
        int value = 0;
        foreach (var it in box.Items) { m.Loot.Add(it); m.LootG += it.MassG; value += it.ValueMites; }
        bool grave = box.Grave && box.Items.Count > 0;
        box.Items.Clear();
        Note(CrawlEventKind.Looted, box.Id, value);
        // the site's people answer a robbed home (a hermit, miners)
        foreach (var n in Map.Inhabitants) if (!n.Hostile && n.State != InhabitantState.Dead && Map.Room[n.Home] == box.Room) { n.Hostile = true; n.State = InhabitantState.Hunt; }
        if (grave)
        {
            // the barrow taboo (4.2, E7): a TabooRobbed event, rumours of a curse, a curse flag; and a draugr's grave good wakes it (4.9 a)
            if (Map.Taboo && !TabooBroken)
            {
                TabooBroken = true;
                Note(CrawlEventKind.TabooRobbed, box.Id);
                Chronicle.Add(("taboo_robbed", new Dictionary<string, string>(StringComparer.Ordinal) { ["site"] = Map.SiteId.ToString(), ["dead"] = box.Dead, ["text_key"] = "ex_chron_taboo" }));
            }
            foreach (var n in Map.Inhabitants) if (n.Monster == "draugr" && n.State == InhabitantState.Dormant && Map.Room[n.Home < 0 ? 0 : n.Home] == box.Room) n.TriggerS = Math.Max(n.TriggerS, 1);
            GraveTaken = true;
        }
    }

    /// <summary>A grave good was taken from a lair chamber (the draugr's first wake trigger).</summary>
    public bool GraveTaken { get; internal set; }
}
