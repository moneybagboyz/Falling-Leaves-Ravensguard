using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Sites;

public static partial class SiteGen
{
    /// <summary>The reserved id space of the people and beasts that live in sites (bits 40-46 under <see cref="EntityId.ReservedSerialBit"/>); far from the spaces in use.</summary>
    public const byte SiteFolkSpace = 9;

    /// <summary>The person id of a site's inhabitant <paramref name="serial"/>: a reserved person id, unique per site and serial, never a record's.</summary>
    public static ulong FolkId(ulong siteId, int serial) => EntityId.Reserved(IdKind.Person, 0, (EntityId.ReservedSpace)SiteFolkSpace, ((siteId & 0xFF_FFFF) << 14) | (uint)(serial & 0x3FFF)).Value;

    public sealed partial class Job
    {
        private RngStream LootRng(int room) => new(_seed, room, _h.SiteId, RngStreams.SiteLoot);
        private RngStream FolkRng(int serial) => new(_seed, serial, _h.SiteId, RngStreams.SiteInhabitants);
        private RngStream HazardRng(int key) => new(_seed, key, _h.SiteId, RngStreams.HazardEvent);

        private void Fill()
        {
            var m = _m!;
            MakeContainers();
            PlaceLoot();
            PlaceHazards();
            PlaceTraps();
            PlacePeople();
        }

        private void MakeContainers()
        {
            var m = _m!;
            var namesRng = FolkRng(9000);
            int dead = 0;
            foreach (var (l, cell, ch, room) in _anchors)
            {
                if (m.Tiles[cell] != SiteTile.Container) continue;
                var pr = m.Rooms[room];
                bool burial = pr.Purposes.Contains("burial") || pr.Purposes.Contains("lair");
                var c = new SiteContainer
                {
                    Id = m.Containers.Count, Cell = cell, Room = room, Kind = ch == 'n' ? ContainerKind.Niche : ch == 'b' ? ContainerKind.Bier : _t.Kind == "Cave" ? ContainerKind.Stash : ContainerKind.Chest,
                    Grave = _t.Taboo && burial,
                };
                if (ch is 'n' or 'b' && dead < 12)
                {
                    // the named dead of the place: its lords and clergy (ruins), the war dead or the barrow's lord
                    c.Dead = SiteNames.Person(_c, _h.Culture, (dead & 3) == 3, ref namesRng) + (_h.Kind == SiteKind.Ruin ? SiteNames.Title(ref namesRng) : "");
                    dead++;
                }
                m.Containers.Add(c);
            }
        }

        /// <summary>The loot from history (4.2): the budget is spent on the tables' items, one container at a time (stream 0x4B1, key site and room); a band's stash is its robberies.</summary>
        private void PlaceLoot()
        {
            var m = _m!;
            int budget = SiteHistoryInfo.LootBudget(_t.Loot, _h);
            if (_h.Kind == SiteKind.Battlefield) budget = (int)((long)budget * SiteHistoryInfo.BattlefieldRemainingPm(_h.YearsSince, _c.K.BattlefieldDecayPmPerYear) / 1000);
            m.LootBudgetMites = budget;
            var entries = new List<SiteLootEntryDef>();
            foreach (var t in _t.Loot.Tables) if (_c.Loot.TryGetValue(t, out var tb)) entries.AddRange(tb.Entries);
            var targets = m.Containers.Where(c => c.Kind != ContainerKind.Niche || true).ToList();
            if (targets.Count == 0) return;
            // a hideout's stash: exactly the band's recorded robberies, in the hall's first chest
            if (_h.Stash.Count > 0 || _h.StashCoinMites > 0)
            {
                var stash = targets.FirstOrDefault(c => m.Rooms[c.Room].Template == "hideout_hall") ?? targets[0];
                stash.Kind = ContainerKind.Stash;
                stash.Items.AddRange(_h.Stash);
                if (_h.StashCoinMites > 0) stash.Items.Add(new LootItem("coin", "silver", (int)Math.Min(int.MaxValue, _h.StashCoinMites), (int)Math.Max(1, _h.StashCoinMites / 10), 1000));
                targets.Remove(stash);
                if (targets.Count == 0) { m.LootPlacedMites = m.LootLeftMites; return; }
            }
            int left = budget, placed = 0, k = 0;
            bool namedDone = false;
            // grave goods go to the biers and the burial chambers first
            targets.Sort((a, b) => (b.Grave ? 1 : 0).CompareTo(a.Grave ? 1 : 0) != 0 ? (b.Grave ? 1 : 0).CompareTo(a.Grave ? 1 : 0) : a.Id.CompareTo(b.Id));
            // the event's own piece first (the reliquary of the abbey sacked in 287): the first named entry the budget can pay for
            if (entries.FirstOrDefault(e => e.Named && e.ValueMites <= left) is { } own)
            {
                var c0 = targets.FirstOrDefault(c => m.Rooms[c.Room].Template is "reliquary_vault" or "barrow_burial_chamber" or "tomb_chamber") ?? targets[0];
                c0.Items.Add(new LootItem(own.Item, own.Material, own.ValueMites, own.MassG, own.Condition.Count >= 2 ? own.Condition[1] : 1000, NameOfPiece(own.Item), own.Dirty, c0.Grave));
                left -= own.ValueMites; placed += own.ValueMites; namedDone = true;
            }
            for (int guard = 0; guard < 400 && left > 0 && entries.Count > 0; guard++)
            {
                var c = targets[k % targets.Count]; k++;
                var r = LootRng(c.Room * 64 + c.Id % 64);
                for (int skip = 0; skip < guard / targets.Count; skip++) r.NextUInt();
                int wsum = 0; foreach (var e in entries) if (e.ValueMites <= left && (!e.Named || !namedDone)) wsum += e.Weight;
                if (wsum <= 0) break;
                int roll = r.Range(0, wsum), acc = 0; SiteLootEntryDef? pick = null;
                foreach (var e in entries) { if (e.ValueMites > left || e.Named && namedDone) continue; acc += e.Weight; if (roll < acc) { pick = e; break; } }
                if (pick == null) break;
                int cond = pick.Condition.Count >= 2 ? pick.Condition[0] + r.Range(0, Math.Max(1, pick.Condition[1] - pick.Condition[0] + 1)) : 1000;
                string named = "";
                if (pick.Named) { namedDone = true; named = NameOfPiece(pick.Item); }
                c.Items.Add(new LootItem(pick.Item, pick.Material, pick.ValueMites, pick.MassG, cond, named, pick.Dirty, c.Grave));
                left -= pick.ValueMites; placed += pick.ValueMites;
            }
            m.LootPlacedMites = placed + (m.Containers.FirstOrDefault(x => x.Kind == ContainerKind.Stash && _h.Stash.Count > 0) is { } st ? st.Items.Sum(i => i.ValueMites) : 0);
        }

        /// <summary>The name a piece of history carries: "the reliquary of Aldford, sacked in 287", "the torc of the barrow's lord".</summary>
        private string NameOfPiece(string item)
        {
            string what = _c.Items.TryGetValue(item, out var si) ? si.Name.ToLowerInvariant() : item.Replace('_', ' ');
            if (_h.Kind == SiteKind.Ruin && _h.SettlementName.Length > 0) return $"the {what} of {_h.SettlementName}, {(_h.Sub is "sacked" or "razed" or "burned" ? _h.Sub : "lost")} in {_h.EventYear}";
            if (_h.ArtefactName.Length > 0) return _h.ArtefactName;
            var r = FolkRng(9100);
            return $"the {what} of {Legend(ref r)}";
        }

        private string? _legend;
        private string Legend(ref RngStream r) => _legend ??= _h.LegendName.Length > 0 ? _h.LegendName : SiteNames.Person(_c, _h.Culture, false, ref r) + " " + SiteNames.Byname(_c, _h.Culture, ref r);

        /// <summary>Hazard zones at generation (4.5, stream 0x4B3 key site and roll): bad air cores and their gradient, unstable spans, flooded levels, a blocked entrance.</summary>
        private void PlaceHazards()
        {
            var m = _m!; var K = _c.K;
            for (int hi = 0; hi < _t.Hazards.Count; hi++)
            {
                var hz = _t.Hazards[hi];
                var r = HazardRng(hi);
                if (r.Range(0, 1000) >= hz.ChancePm) continue;
                int vlo = hz.Value.Count > 0 ? hz.Value[0] : 0, vhi = hz.Value.Count > 1 ? hz.Value[1] : vlo;
                int V() => vlo + r.Range(0, Math.Max(1, vhi - vlo + 1));
                switch (hz.Id)
                {
                    case "stale_air":
                    case "foul_air":
                    {
                        bool deep = hz.Id == "foul_air";
                        int level = deep ? m.Levels - 1 : r.Range(0, m.Levels);
                        var rooms = m.Rooms.Where(p => p.Level == level && p.Template != "stairs_down").ToList();
                        if (rooms.Count == 0) break;
                        var dist = Distances(level, m.Arrival[level]);
                        PlacedRoom core = deep ? rooms.OrderByDescending(p => RoomDist(p, dist)).ThenBy(p => p.Index).First() : rooms[r.Range(0, rooms.Count)];
                        AirCore(core, V());
                        break;
                    }
                    case "collapse":
                    {
                        int n = hz.Zones.Count > 0 ? hz.Zones[0] + r.Range(0, Math.Max(1, (hz.Zones.Count > 1 ? hz.Zones[1] : hz.Zones[0]) - hz.Zones[0] + 1)) : 1;
                        var rooms = m.Rooms.Where(p => p.Template != "stairs_down" && m.Unstable.All(z => !(z.Level == p.Level && z.X0 == p.X + 1 && z.Y0 == p.Y + 1))).ToList();
                        for (int i = 0; i < n && rooms.Count > 0; i++)
                        {
                            var p = rooms[r.Range(0, rooms.Count)]; rooms.Remove(p);
                            m.Unstable.Add(new UnstableZone { Id = m.Unstable.Count, Level = p.Level, X0 = p.X + 1, Y0 = p.Y + 1, X1 = p.X + p.W - 2, Y1 = p.Y + p.H - 2, InstabilityPm = V() });
                        }
                        break;
                    }
                    case "flood":
                    {
                        int level = m.Levels - 1, depth = Math.Max(120, V());
                        for (int i = 0; i < m.Plane; i++)
                        {
                            int c = level * m.Plane + i;
                            if (m.Tiles[c] is SiteTile.Floor or SiteTile.Water) { m.Tiles[c] = SiteTile.Water; m.WaterCm[c] = (ushort)depth; }
                        }
                        break;
                    }
                    case "entrance_blocked":
                    {
                        // a collapse from history: the tunnel out of the entrance is rubble (dig in with a pick: one tile per 20 sim-seconds)
                        var arrival = m.Rooms[0];
                        var dist = Distances(0, m.Mouth);
                        int done = 0;
                        for (int want = arrival.H; want < arrival.H + 6 && done < 3; want++)
                            for (int i = 0; i < m.Plane && done < 3; i++)
                                if (dist[i] == want && m.Room[i] < 0 && m.Tiles[i] == SiteTile.Floor) { m.Tiles[i] = SiteTile.Rubble; done++; break; }
                        break;
                    }
                }
            }
            // natural spans (old caves, rotten or fresh props): rooms whose roof reaches more than 3 tiles from a support
            if (_t.Instability.Count >= 2 && _t.Instability[1] > 0)
            {
                var r = HazardRng(500);
                foreach (var p in m.Rooms)
                {
                    if (p.Template == "stairs_down" || m.Unstable.Any(z => z.Level == p.Level && z.X0 == p.X + 1 && z.Y0 == p.Y + 1)) continue;
                    if (!Spans(p)) { r.NextUInt(); continue; }
                    int v = _t.Instability[0] + r.Range(0, Math.Max(1, _t.Instability[1] - _t.Instability[0] + 1));
                    m.Unstable.Add(new UnstableZone { Id = m.Unstable.Count, Level = p.Level, X0 = p.X + 1, Y0 = p.Y + 1, X1 = p.X + p.W - 2, Y1 = p.Y + p.H - 2, InstabilityPm = v });
                }
            }
        }

        /// <summary>How far a room lies from the level's arrival: the farthest of its cells (walked).</summary>
        private int RoomDist(PlacedRoom p, int[] dist)
        {
            int best = -1, w = _m!.W;
            for (int y = p.Y; y < p.Y + p.H; y++) for (int x = p.X; x < p.X + p.W; x++) best = Math.Max(best, dist[y * w + x]);
            return best;
        }

        /// <summary>The structure rule (plan 2.2a): some floor cell of the room is more than 3 tiles from every wall, pillar or prop.</summary>
        private bool Spans(PlacedRoom p)
        {
            var m = _m!;
            for (int y = p.Y + 1; y < p.Y + p.H - 1; y++)
                for (int x = p.X + 1; x < p.X + p.W - 1; x++)
                {
                    bool near = false;
                    for (int dy = -3; dy <= 3 && !near; dy++)
                        for (int dx = -3; dx <= 3 && !near; dx++)
                            if (SiteTiles.Support(m.Tile(p.Level, x + dx, y + dy))) near = true;
                    if (!near) return true;
                }
            return false;
        }

        /// <summary>A bad-air core (4.5.1): the room's cells hold <paramref name="core"/> and the air rises by the slope per cell walked away, so the candle gutters about 30 m before the foul air (EX-18).
        /// An open shaft within 6 tiles ventilates the core (+200).</summary>
        private void AirCore(PlacedRoom room, int core)
        {
            var m = _m!; var K = _c.K; int l = room.Level;
            for (int i = 0; i < m.Plane; i++)
            {
                var t = m.Tiles[l * m.Plane + i];
                if (t is not (SiteTile.ShaftDown or SiteTile.ShaftUp)) continue;
                int x = i % m.W, y = i / m.W;
                if (x >= room.X - K.AirVentTiles && x < room.X + room.W + K.AirVentTiles && y >= room.Y - K.AirVentTiles && y < room.Y + room.H + K.AirVentTiles) { core += K.AirVentBonus; break; }
            }
            var d = new int[m.Plane]; Array.Fill(d, -1); var q = new Queue<int>();
            for (int y = room.Y; y < room.Y + room.H; y++)
                for (int x = room.X; x < room.X + room.W; x++)
                {
                    int i = y * m.W + x;
                    if (SiteTiles.Walkable(m.Tiles[l * m.Plane + i]) || m.Tiles[l * m.Plane + i] == SiteTile.Container) { d[i] = 0; q.Enqueue(i); }
                }
            while (q.Count > 0)
            {
                int c = q.Dequeue(); int x = c % m.W, y = c / m.W;
                foreach (var (dx, dy) in Four)
                {
                    int nx = x + dx, ny = y + dy; if ((uint)nx >= m.W || (uint)ny >= m.H) continue;
                    int j = ny * m.W + nx; if (d[j] >= 0 || !SiteTiles.Walkable(m.Tiles[l * m.Plane + j]) && m.Tiles[l * m.Plane + j] != SiteTile.Container) continue;
                    d[j] = d[c] + 1; q.Enqueue(j);
                }
            }
            for (int i = 0; i < m.Plane; i++)
            {
                if (d[i] < 0) continue;
                int a = Math.Min(1000, core + K.AirSlopePerTile * d[i]);
                int c = l * m.Plane + i;
                if (a < m.Air[c]) m.Air[c] = (ushort)a;
            }
        }

        /// <summary>Traps (4.6): per template density at the rooms' trap anchors first, then on room floor; a hideout's tripwire across the way in.</summary>
        private void PlaceTraps()
        {
            var m = _m!;
            int rooms = m.Rooms.Count;
            var anchors = _anchors.Where(a => a.C == '^').Select(a => a.Cell).ToList();
            var r = new RngStream(_seed, 7000, _h.SiteId, RngStreams.SiteLayout);
            for (int i = anchors.Count - 1; i > 0; i--) { int j = r.Range(0, i + 1); (anchors[i], anchors[j]) = (anchors[j], anchors[i]); }
            bool Free(int cell)
            {
                if (m.Tiles[cell] != SiteTile.Floor || m.TrapAt(cell) != null || m.Arrival.Contains(cell)) return false;
                if (m.Room[cell] == 0 && m.At(cell).Level == 0) return false;
                return true;
            }
            // a floor that gives way leaves a hole: only where one can still walk round it (3 x 3 of open floor), so it never cuts the way out
            bool Open3(int cell) { var (l, x, y) = m.At(cell); for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) if (m.Tile(l, x + dx, y + dy) != SiteTile.Floor) return false; return true; }
            foreach (var tr in _t.Traps)
            {
                bool hole = _c.Traps.TryGetValue(tr.Id, out var td) && td.Effect == "floor";
                bool Free2(int cell) => Free(cell) && (!hole || Open3(cell));
                int n = tr.PerEntrance ? 1 : Math.Max(1, (rooms + tr.PerRooms - 1) / Math.Max(1, tr.PerRooms));
                for (int k = 0; k < n; k++)
                {
                    int cell = -1;
                    if (tr.PerEntrance)
                    {
                        var dist = Distances(0, m.Mouth);
                        for (int i = 0; i < m.Plane && cell < 0; i++) if (dist[i] == m.Rooms[0].H && m.Room[i] < 0 && Free(i)) cell = i;
                    }
                    for (int ai = 0; cell < 0 && ai < anchors.Count; ai++) { int a = anchors[ai]; if (Free2(a)) { cell = a; anchors.RemoveAt(ai); } }
                    for (int tries = 0; tries < 80 && cell < 0; tries++)
                    {
                        var p = m.Rooms[1 + r.Range(0, Math.Max(1, rooms - 1)) % Math.Max(1, rooms)];
                        int c = m.Index(p.Level, p.X + 1 + r.Range(0, Math.Max(1, p.W - 2)), p.Y + 1 + r.Range(0, Math.Max(1, p.H - 2)));
                        if (Free2(c)) cell = c;
                    }
                    if (cell >= 0) m.Traps.Add(new SiteTrap { Id = m.Traps.Count, Def = tr.Id, Cell = cell });
                }
            }
        }

        /// <summary>The people and beasts of the site (4.3, 4.9; stream 0x4B2 key site and serial): by template chances, named from the culture's names; a lair's monsters on their biers or in the den.</summary>
        private void PlacePeople()
        {
            var m = _m!;
            int serial = 0;
            var role = FolkRng(0);
            foreach (var ih in _t.Inhabitants)
            {
                if (role.Range(0, 1000) >= ih.ChancePm) continue;
                int lo = ih.Count.Count > 0 ? ih.Count[0] : 1, hi = ih.Count.Count > 1 ? ih.Count[1] : lo;
                int n = lo + role.Range(0, Math.Max(1, hi - lo + 1));
                var home = HomeRoom(ih.Role);
                for (int i = 0; i < n; i++) AddFolk(ih.Role, SiteContent.KitOf(ih.Role), "", home, serial++, false);
            }
            if (_h.MonsterLair && _c.MonsterFor(_h.LairKind) is { } mon)
            {
                var lair = m.Rooms.LastOrDefault(p => p.Purposes.Contains("lair")) ?? m.Rooms.Last(p => p.Level == m.Levels - 1);
                var r = FolkRng(5000);
                int n = mon.Dormant ? 1 + r.Range(0, 3) : r.Range(0, 100) < 25 ? 2 : 1;
                var beds = _anchors.Where(a => a.Room == lair.Index && a.C == 'b').Select(a => a.Cell).ToList();
                for (int i = 0; i < n; i++)
                {
                    var f = AddFolk(mon.Id, mon.Kit, mon.Id, lair, serial++, true);
                    if (i < beds.Count) { f.Bed = beds[i]; var spot = BesideCell(beds[i]); if (spot >= 0) SetPos(f, spot); }
                    if (f.Bed >= 0 && BesideCell(f.Bed) is var hb and >= 0) f.Home = hb;
                    f.State = mon.Dormant ? InhabitantState.Dormant : InhabitantState.Asleep;
                    var lr = FolkRng(6000 + i);
                    f.Name = i == 0 ? Legend(ref lr) : SiteNames.Person(_c, _h.Culture, false, ref lr) + " " + SiteNames.Byname(_c, _h.Culture, ref lr);
                }
            }
        }

        private PlacedRoom HomeRoom(string role)
        {
            var m = _m!;
            string[] want = role switch
            {
                "wolves" or "bear" or "vermin" => ["den", "rest", "passage"], "bandits" or "smugglers" => ["guard", "rest", "dock"], "hermit" => ["worship", "rest"],
                "miners" => ["work"], _ => ["storage", "rest", "burial"],
            };
            foreach (var w in want) { var p = m.Rooms.FirstOrDefault(x => x.Purposes.Contains(w) && x.Template != "stairs_down"); if (p != null) return p; }
            return m.Rooms.Count > 1 ? m.Rooms[1] : m.Rooms[0];
        }

        private Inhabitant AddFolk(string role, string kit, string monster, PlacedRoom home, int serial, bool legend)
        {
            var m = _m!; var r = FolkRng(serial + 1);
            int cell = -1;
            for (int k = 0; k < 40 && cell < 0; k++)
            {
                int c = m.Index(home.Level, home.X + 1 + r.Range(0, Math.Max(1, home.W - 2)), home.Y + 1 + r.Range(0, Math.Max(1, home.H - 2)));
                if (m.Tiles[c] is SiteTile.Floor && m.TrapAt(c) == null) cell = c;
            }
            if (cell < 0) cell = m.Arrival[home.Level];
            int skill = 30;
            if (_c.Db.TryGet<FightKitDef>(kit, out var kd)) skill = kd.SkillMin + r.Range(0, Math.Max(1, kd.SkillMax - kd.SkillMin + 1));
            bool person = role is "bandits" or "smugglers" or "scavengers" or "hermit" or "miners";
            var f = new Inhabitant
            {
                Id = FolkId(_h.SiteId, serial), Role = role, Kit = kit, Monster = monster, Home = cell, Skill = skill, Legend = legend,
                Name = person ? SiteNames.Person(_c, _h.Culture, r.Range(0, 5) == 0, ref r) : "", State = role is "wolves" or "bear" ? InhabitantState.Asleep : InhabitantState.Home,
                Hostile = role is not ("hermit" or "miners"),
            };
            SetPos(f, cell);
            m.Inhabitants.Add(f);
            return f;
        }

        private void SetPos(Inhabitant f, int cell) { var (l, x, y) = _m!.At(cell); f.Level = l; f.X = x * 1000 + 500; f.Y = y * 1000 + 500; }

        private int BesideCell(int cell)
        {
            var m = _m!; var (l, x, y) = m.At(cell);
            foreach (var (dx, dy) in Four) if (m.Tile(l, x + dx, y + dy) == SiteTile.Floor) return m.Index(l, x + dx, y + dy);
            return -1;
        }
    }
}

/// <summary>Names of the people of sites from the culture name pools (plan 4A.4), drawn on the site's own stream.</summary>
public static class SiteNames
{
    public static string Person(SiteContent c, int culture, bool female, ref RngStream r)
    {
        if (c.Names.Count == 0) return female ? "Hild" : "Ketil";
        var p = c.Names[Math.Abs(culture) % c.Names.Count];
        string s = p.Onsets[r.Range(0, p.Onsets.Count)] + p.Mids[r.Range(0, p.Mids.Count)] + (female ? p.FemaleEnds[r.Range(0, p.FemaleEnds.Count)] : p.MaleEnds[r.Range(0, p.MaleEnds.Count)]);
        return s;
    }

    public static string Byname(SiteContent c, int culture, ref RngStream r)
    {
        if (c.Names.Count == 0) return "Ravenfoot";
        var p = c.Names[Math.Abs(culture) % c.Names.Count];
        return p.Bynames.Count > 0 ? p.Bynames[r.Range(0, p.Bynames.Count)] : p.FamilyStems[r.Range(0, p.FamilyStems.Count)] + p.FamilyEnds[r.Range(0, p.FamilyEnds.Count)];
    }

    private static readonly string[] Titles = [", abbot", ", prior", ", brother of the house", ", lord of the manor", ", lady of the manor", ", reeve", ", priest"];
    public static string Title(ref RngStream r) => Titles[r.Range(0, Titles.Length)];
}
