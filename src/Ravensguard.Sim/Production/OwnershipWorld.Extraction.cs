using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Prospecting;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.WorldGen;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Production;

public sealed partial class OwnershipWorld
{
    // ------------------------------------------------------------------ prospecting (prospecting spec 4)

    /// <summary>The hero's prospecting skill: what use has trained, from a start of his Perception / 5 (spec 4: "starts from Perception").</summary>
    public Func<int>? HeroPerception { get; set; }
    public int HeroProspecting() => Math.Max(HeroSkill?.Invoke(Skill.Prospecting) ?? 0, (HeroPerception?.Invoke() ?? 50) / 5);

    /// <summary>A walk past a tile (free): its visible surface resources at a rough estimate (confidence 200). Recorded once a tile a day.</summary>
    public int Walk(int tile, long sec)
    {
        if ((uint)tile >= (uint)RegionData.Tiles) return 0;
        int day = (int)(sec / 86400);
        int n = 0;
        foreach (var r in Prospector.Walk(Truth, PRules, Seed, Hero, tile, HeroProspecting(), day))
        {
            if (Book.Prospects.Rows.TryGetValue((Hero.Value, tile, r.Resource), out var old) && old.Day == day) continue;
            Book.Prospects.Learn(r); n++;
        }
        return n;
    }

    /// <summary>
    /// Starts a prospecting method where the hero stands (spec 4): the tools it needs must be carried, Read the land needs skill 20; it runs its hours (a pan 2, a pit 6, a trial shaft 48) and
    /// its knowledge is his at the hour it ends (stream 0x5C0 for the error, 0x5C1 for the hints). Only one at a time.
    /// </summary>
    public OwnResult StartProspect(ProspectMethod m, int tile, IReadOnlyCollection<string> tools, long sec)
    {
        if (m is not (ProspectMethod.ReadLand or ProspectMethod.Pan or ProspectMethod.TestPit or ProspectMethod.TrialShaft)) return OwnResult.No("px_bad_method");
        if ((uint)tile >= (uint)RegionData.Tiles) return OwnResult.No("px_bad_place");
        if (Book.Prospects.Jobs.Any(j => j.Person == Hero && !j.Done)) return OwnResult.No("px_busy");
        int skill = HeroProspecting();
        if (m == ProspectMethod.ReadLand && skill < PRules.ReadMinSkill) return OwnResult.No("px_need_skill", PRules.ReadMinSkill, skill);
        if (!Prospector.HasTools(m, tools)) return OwnResult.No("px_need_tools", string.Join(" + ", Prospector.Tools(m).Select(g => string.Join("/", g))));
        if (m == ProspectMethod.Pan && !Truth.IsStream(tile)) return OwnResult.No("px_no_stream");
        int hours = Prospector.Hours(m, PRules);
        var j = new ProspectJob { Serial = Book.Prospects.NextJob++, Person = Hero, Method = m, Tile = tile, Skill = skill, StartSec = sec, EndSec = sec + hours * 3600L, Client = Hero };
        Book.Prospects.Jobs.Add(j);
        return new OwnResult(true, "px_started", [HeroText.Of("px_m_" + m.ToString().ToLowerInvariant()), hours], 0);
    }

    public OwnResult CancelProspect()
    {
        int n = Book.Prospects.Jobs.RemoveAll(j => j.Person == Hero && !j.Done && j.Client == Hero && j.FeeMites == 0);
        return n > 0 ? OwnResult.Yes("px_cancelled") : OwnResult.No("px_nothing");
    }

    /// <summary>
    /// Hires a prospector (spec 4): an NPC with the skill does a method at a tile for his fee (paid now, <c>Wage</c>, to his household) and hands over a written survey (an item) when it is done.
    /// </summary>
    public OwnResult HireProspector(int person, ProspectMethod m, int tile, long sec)
    {
        var pt = Pop.People;
        if ((uint)person >= (uint)pt.Count || pt.Status[person] != PersonStatus.Alive) return OwnResult.No("bz_cannot_hire");
        if (m is not (ProspectMethod.ReadLand or ProspectMethod.Pan or ProspectMethod.TestPit or ProspectMethod.TrialShaft) || (uint)tile >= (uint)RegionData.Tiles) return OwnResult.No("px_bad_method");
        int skill = Math.Max(pt.SkillLevelOf(person, Skill.Prospecting), Math.Max(pt.SkillLevelOf(person, Skill.Mining) * 9 / 10, PRules.HireSkill));
        int hours = Prospector.Hours(m, PRules); int days = Math.Max(1, (hours + 9) / 10);
        long fee = (long)days * PRules.HireMitesPerDay;
        int hh = pt.Household[person]; if (hh < 0 || hh >= Econ.HouseholdCount) return OwnResult.No("bz_cannot_hire");
        if (HeroCoin < fee || !Econ.Ledger.Transfer(HeroPurse, Econ.HouseholdPurse[hh], new Coin(fee), TransferReason.Wage)) return new OwnResult(false, "bz_cannot_afford", [M(fee)], fee, CannotAfford: true);
        var j = new ProspectJob { Serial = Book.Prospects.NextJob++, Person = pt.IdOf(person), Method = m, Tile = tile, Skill = skill, StartSec = sec, EndSec = sec + days * 86400L, Client = Hero, FeeMites = fee };
        Book.Prospects.Jobs.Add(j);
        return new OwnResult(true, "px_hired", [pt.FullName(person), HeroText.Of("px_m_" + m.ToString().ToLowerInvariant()), days, M(fee)], fee);
    }

    private void FinishProspecting(long nowSec)
    {
        var jobs = Book.Prospects.Jobs;
        for (int i = 0; i < jobs.Count; i++)
        {
            var j = jobs[i];
            if (j.Done || j.EndSec > nowSec) continue;
            j.Done = true;
            int day = (int)(j.EndSec / 86400);
            var rows = new List<ProspectRow>();
            switch (j.Method)
            {
                case ProspectMethod.ReadLand:
                    var (rr, hints) = Prospector.ReadLand(Truth, PRules, Seed, j.Person, j.Tile, j.Skill, day);
                    rows.AddRange(rr); Book.Prospects.Hints.AddRange(hints);
                    if (j.Client == j.Person) Notices.Add(hints.Count > 0 ? HeroText.Join(hints.Select(hh => HeroText.Of("px_hint", HeroText.Of(ResourceCode.Key(ResourceCode.Ore((MineralType)hh.Mineral))), HeroText.Of("px_dir_" + hh.Direction)))) : HeroText.Of("px_read_nothing"));
                    break;
                case ProspectMethod.Pan:
                    var minerals = Prospector.Pan(Truth, PRules, j.Tile);
                    Book.Prospects.Pans.Add(new PanFinding(j.Person, j.Tile, minerals, day));
                    if (j.Client == j.Person) Notices.Add(minerals.Length > 0 ? HeroText.Join(minerals.Select(m => HeroText.Of("px_pan", HeroText.Of(ResourceCode.Key(ResourceCode.Ore((MineralType)m)))))) : HeroText.Of("px_pan_nothing"));
                    break;
                case ProspectMethod.TestPit: rows.AddRange(Prospector.TestPit(Truth, PRules, Seed, j.Person, j.Tile, j.Skill, day)); break;
                case ProspectMethod.TrialShaft: rows.AddRange(Prospector.TrialShaft(Truth, PRules, Seed, j.Person, j.Tile, j.Skill, day)); break;
            }
            foreach (var r in rows) Book.Prospects.Learn(r);
            if (j.Person == Hero) { TrainHero?.Invoke(Skill.Prospecting, j.StartSec, j.EndSec); if (j.Method is ProspectMethod.TestPit or ProspectMethod.TrialShaft) Notices.Add(rows.Count > 0 ? HeroText.Join(rows.Select(r => HeroText.Of("px_found", HeroText.Of(ResourceCode.Key(r.Resource)), Quotes.Weight(r.EstG), r.ConfidencePm / 10))) : HeroText.Of("px_found_nothing")); }
            else if (j.Client == Hero)
            {
                // the hired prospector's written survey: an item in the hero's pack carrying the knowledge (spec 4: knowledge is property)
                var id = MakeReport(rows.Select(r => r with { Method = ProspectMethod.Hired }).ToArray(), Hero);
                Notices.Add(HeroText.Of("px_report_delivered", j.Tile % RegionData.Size, j.Tile / RegionData.Size));
                _ = id;
            }
            // a strike: an ore body found by a trial shaft is talked of (the rumour system, spec 4)
            foreach (var r in rows)
                if (ResourceCode.IsOre(r.Resource) && r.Method != ProspectMethod.Report)
                {
                    int s = NearestSettlement(j.Tile);
                    Event(day, "ore_strike", [j.Person], [Why.Of(CauseKind.Trade, "ch_why_trial_shaft", ChronParam.Person("who", j.Person))], ("settlement", s >= 0 ? Pop.Region.Settlements[s].Name : ""), ("mineral", ResourceCode.Mineral(r.Resource).ToString().ToLowerInvariant()), ("tonnes", (r.EstG / 1_000_000).ToString()));
                }
        }
        jobs.RemoveAll(j => j.Done);
    }

    public int NearestSettlement(int tile)
    {
        int tx = tile % RegionData.Size * RegionData.TileM, ty = tile / RegionData.Size * RegionData.TileM; int best = -1; long bd = long.MaxValue;
        for (int s = 0; s < Pop.Region.Settlements.Count; s++) { var st = Pop.Region.Settlements[s]; long d = RegionData.Dist2(st.CenterX - tx, st.CenterY - ty); if (d < bd) { bd = d; best = s; } }
        return best;
    }

    // ------------------------------------------------------------------ survey reports (spec 4: knowledge is property; PX-7)

    /// <summary>A written survey: an item row (a sheet of the report shape) in the holder's keeping, and the knowledge it carries.</summary>
    public EntityId MakeReport(ProspectRow[] rows, EntityId holder)
    {
        var id = Econ.Items.Add(Ids, PRules.ReportShape, ItemQuality.Common, holder, [new ItemComponent(PRules.ReportMaterial, Math.Max(1, PRules.ReportMassG), 1000)]);
        Book.Prospects.Reports[id] = [.. rows.Select(r => r with { Person = default })];
        return id;
    }

    /// <summary>The hero writes up what he knows of a tile (the rows of his knowledge there) as a survey report in his pack.</summary>
    public OwnResult WriteReport(int tile, long sec)
    {
        var rows = Book.Prospects.At(Hero, tile).ToArray();
        if (rows.Length == 0) return OwnResult.No("px_nothing_known");
        var id = MakeReport([.. rows.Select(r => r with { Method = ProspectMethod.Report })], Hero);
        return new OwnResult(true, "px_report_written", [tile % RegionData.Size, tile / RegionData.Size, rows.Length], 0, id);
    }

    /// <summary>Reading a survey report (PX-7): whoever reads it knows what it says (at its confidence, as of its day).</summary>
    public OwnResult ReadReport(EntityId item, EntityId reader)
    {
        if (!Book.Prospects.Reports.TryGetValue(item, out var rows) || !Econ.Items.IsLive(item)) return OwnResult.No("px_not_a_report");
        foreach (var r in rows) Book.Prospects.Learn(r with { Person = reader, Method = ProspectMethod.Report });
        return new OwnResult(true, "px_report_read", [rows.Length], 0, item);
    }

    // ------------------------------------------------------------------ rights and licences (spec 5)

    /// <summary>Who holds the right to a mineral under a tile: the crown (the realm) for silver and gold, the lord of the fief for the base metals; null where nobody does (wild land outside a realm).</summary>
    public (GrantorKind Kind, int Index)? RightHolder(int tile, MineralType m)
    {
        var r = Pop.Region;
        if (m is MineralType.Silver or MineralType.Gold)
        {
            int realm = tile < r.RealmMap.Length ? r.RealmMap[tile] : 255;
            return realm == 255 ? null : (GrantorKind.Realm, realm);
        }
        int fief = LandRules.FiefOfTile(r, tile);
        return fief < 0 ? null : (GrantorKind.Fief, fief);
    }

    public long LicenceFee(MineralType m) => m is MineralType.Silver or MineralType.Gold ? PRules.LicenceFeePreciousMites : PRules.LicenceFeeBaseMites;

    /// <summary>
    /// A licence petition at the right holder's officer (spec 5): the fee to the lord's household or the crown's treasury (<c>Rent</c>), a royalty of 10% of what is mined by the ledger after.
    /// The vein is the ore body under the tile; one licence a vein.
    /// </summary>
    public OwnResult PetitionLicence(int tile, int day)
    {
        if (Truth.OreAt(tile) is not { } body) return OwnResult.No("px_no_ore_known");
        if (!Book.Prospects.At(Hero, tile).Any(r => r.Resource == ResourceCode.Ore(body.Mineral))) return OwnResult.No("px_no_ore_known");
        if (Book.Licences.Any(l => l.Vein == body.Vein && !l.Revoked)) return OwnResult.No("px_vein_licensed");
        var holder = RightHolder(tile, body.Mineral);
        if (holder is not { } gh) return OwnResult.No("px_no_right_holder");
        long fee = LicenceFee(body.Mineral);
        var to = GrantorPurse(gh.Kind, gh.Index);
        if (HeroCoin < fee || !Econ.Ledger.Transfer(HeroPurse, to, new Coin(fee), TransferReason.Rent)) return new OwnResult(false, "bz_cannot_afford", [M(fee)], fee, CannotAfford: true);
        var lic = new MiningLicence { Serial = Book.NextSerial++, Holder = Hero, Vein = body.Vein, Tile = tile, Mineral = body.Mineral, Grantor = gh.Kind, GrantorIndex = gh.Index, FeeMites = fee, RoyaltyPm = PRules.RoyaltyPm, Day = day };
        Book.Licences.Add(lic);
        foreach (var h in Book.Holdings.Values) if (h.Vein == body.Vein && h.Licence < 0) { h.Licence = lic.Serial; h.Unlicensed = false; h.Alerts &= ~OwnerAlert.Unlicensed; }
        string grantor = Land?.GrantorName(gh.Kind, gh.Index) ?? "";
        Event(day, "mining_licence", [Hero], [Why.Of(CauseKind.Law, "ch_why_licence_petitioned", ChronParam.Person("who", Hero))], ("grantor", grantor), ("mineral", body.Mineral.ToString().ToLowerInvariant()), ("fee", fee.ToString()));
        return new OwnResult(true, "px_licensed", [HeroText.Of("px_res_" + body.Mineral.ToString().ToLowerInvariant()), grantor, M(fee), PRules.RoyaltyPm / 10], fee);
    }

    // ------------------------------------------------------------------ extraction businesses (spec 6)

    public ExtractionSiteDef? SiteDef(string kind) => PRules.Sites.FirstOrDefault(s => s.Kind == kind);

    /// <summary>The acre tiles a footprint touches (region metres, inclusive), ascending.</summary>
    public static int[] TilesOf(int x0, int y0, int x1, int y1)
    {
        var set = new SortedSet<int>();
        for (int ty = Math.Max(0, y0 / RegionData.TileM); ty <= Math.Min(RegionData.Size - 1, y1 / RegionData.TileM); ty++)
            for (int tx = Math.Max(0, x0 / RegionData.TileM); tx <= Math.Min(RegionData.Size - 1, x1 / RegionData.TileM); tx++) set.Add(RegionData.Index(tx, ty));
        return [.. set];
    }

    /// <summary>
    /// Founds an extraction business where the resource is (spec 6, PX-5): its footprint must touch tiles that hold the resource (refused off it), the hero must know of it (a survey where the kind
    /// needs one), and have the surface right (his deed, lease or claim: the land rule) or, for a mine, the vein's licence (else it is recorded as unlicensed, a crime Phase 6 enforces).
    /// </summary>
    public OwnResult FoundExtraction(string kindId, int x0, int y0, int x1, int y1, long capital, int day, Func<int, int, bool>? mayUse = null)
    {
        var site = SiteDef(kindId); var kind = Prod.Content.Kinds.FirstOrDefault(k => k.Id == kindId);
        if (site == null || kind == null) return OwnResult.No("bz_no_such_kind");
        int row = HeroRow(); if (row < 0) return OwnResult.No("bz_no_hero");
        var tiles = TilesOf(Math.Min(x0, x1), Math.Min(y0, y1), Math.Max(x0, x1), Math.Max(y0, y1));
        if (tiles.Length == 0 || tiles.Length > 16) return OwnResult.No("px_bad_footprint");
        bool mine = site.Resource == "Mine";
        OreBody? body = null; var onTiles = new List<int>();
        if (mine)
        {
            foreach (int t in tiles) if (Truth.OreAt(t) is { } b0) { body = b0; onTiles.Add(t); break; }
            if (body == null) return OwnResult.No("px_off_resource", HeroText.Of("px_res_ore"));
        }
        else
        {
            var res = Enum.Parse<ResourceKind>(site.Resource);
            foreach (int t in tiles) if (Truth.SurfaceG(t, res) > 0) onTiles.Add(t);
            if (onTiles.Count == 0) return OwnResult.No("px_off_resource", HeroText.Of(ResourceCode.Key((int)res)));
        }
        int code = mine ? ResourceCode.Ore(body!.Value.Mineral) : (int)Enum.Parse<ResourceKind>(site.Resource);
        if (site.NeedsSurvey && !onTiles.Any(t => Book.Prospects.Rows.TryGetValue((Hero.Value, t, code), out var r) && r.ConfidencePm >= PRules.PitConfidencePm / 2))
            return OwnResult.No("px_needs_survey", HeroText.Of(ResourceCode.Key(code)));
        if (!mine && mayUse != null && !tiles.All(t => mayUse(t % RegionData.Size * RegionData.TileM + RegionData.TileM / 2, t / RegionData.Size * RegionData.TileM + RegionData.TileM / 2)))
            return OwnResult.No("px_no_surface_right");
        if (mine && Book.Holdings.Values.Any(h => h.Vein == body!.Value.Vein && Prod.ById(h.Business) is { State: BusinessState.Open })) return OwnResult.No("px_vein_worked");
        if (body is { RemainingG: <= 0 }) return OwnResult.No("px_worked_out");
        if (capital < 0 || HeroCoin < capital) return new OwnResult(false, "bz_cannot_afford", [M(capital)], capital, CannotAfford: true);
        int s = NearestSettlement(onTiles[0]);
        var b = Prod.NewBusiness(kind, s, -1, row);
        b.OwnerWorks = false;
        foreach (var (st, n) in kind.Stations) for (int i = 0; i < Math.Max(1, n); i++) b.Stations.Add(new Station { Kind = st });
        if (capital > 0) Econ.Ledger.Transfer(HeroPurse, b.Purse, new Coin(capital), TransferReason.Gift);
        b.Capital = capital; b.CashStart = capital;
        var h = Hold(b); h.FoundedDay = day; h.HandsOn = true; h.Tiles = [.. onTiles];
        if (mine)
        {
            var v = body!.Value;
            var mw = new MineWorks { SiteIndex = -1, SiteId = EntityId.Make(IdKind.Other, (0x5CUL << 40) | (uint)v.Vein), Mineral = v.Mineral, RemainingG = v.RemainingG * PRules.MineShareOfVeinPm / 1000, InitialG = v.RemainingG * PRules.MineShareOfVeinPm / 1000, Levels = PRules.NewMineLevels, State = MineStateKind.Active };
            b.Mine = Prod.AddMine(mw);
            h.Vein = v.Vein;
            var lic = Book.Licences.FirstOrDefault(l => l.Vein == v.Vein && l.Holder == Hero && !l.Revoked);
            if (lic != null) h.Licence = lic.Serial;
            else
            {
                h.Unlicensed = true; h.Alerts |= OwnerAlert.Unlicensed;
                var witnesses = Pop.People.Count > 0 ? new List<EntityId>() : [];
                Event(day, "unlicensed_mine", [Hero, b.Id], [Why.Of(CauseKind.Crime, "ch_why_unlicensed", ChronParam.Person("who", Hero))], ("settlement", Pop.Region.Settlements[s].Name), ("mineral", v.Mineral.ToString().ToLowerInvariant()));
            }
        }
        Event(day, "business_founded", [Hero, b.Id], [Why.Of(CauseKind.Trade, "ch_why_on_resource", ChronParam.Person("who", Hero))], ("settlement", Pop.Region.Settlements[s].Name), ("kind", kind.Id), ("capital", capital.ToString()), ("tiles", onTiles.Count.ToString()));
        return new OwnResult(true, "bz_founded_site", [Name(b), onTiles.Count, M(capital)], capital, b.Id);
    }

    /// <summary>
    /// The new workings' hazards at a season's start (spec 6, stream 0x5C2, key the mine's id and the season's hour): flooding and cave-ins at the Phase 4 sites' rates per level; a flooded mine stops until
    /// pumped (the owner pays a pump crew: Phase 6), a cave-in closes it 15 days.
    /// </summary>
    private void NewMineHazards(int day)
    {
        foreach (var h in Book.Holdings.Values)
        {
            if (Prod.ById(h.Business) is not { Mine: >= 0 } b || b.State != BusinessState.Open) continue;
            var m = Prod.Mines[b.Mine]; if (m.SiteIndex >= 0 || m.State != MineStateKind.Active) continue;
            long hour = (long)day * 24 + 9;
            uint a = DetRng.NextUInt(Seed, hour, m.SiteId.Value, RngStreams.NewMineHazard, 0), c = DetRng.NextUInt(Seed, hour, m.SiteId.Value, RngStreams.NewMineHazard, 1);
            bool flood = (long)a * 1000 < (long)PRules.NewMineFloodPm * m.Levels * 4_294_967_296L;
            bool cave = (long)c * 1000 < (long)PRules.NewMineCaveInPm * m.Levels * 4_294_967_296L;
            if (flood) { m.State = MineStateKind.Flooded; Notices.Add(HeroText.Of("px_mine_flooded", Name(b))); Event(day, "mine_flooded", [b.Id], [Why.Of(CauseKind.MineFlood, "ch_why_water", ChronParam.Place("mine", b.Id))], ("site", Name(b))); }
            if (cave) { m.ClosedUntilDay = day + 15; Notices.Add(HeroText.Of("px_mine_cave_in", Name(b))); }
        }
    }

    /// <summary>Pumps a flooded mine the hero owns out (a day's wages for its staff, paid from the box): it works again.</summary>
    public OwnResult Pump(EntityId biz)
    {
        if (BusinessOf(biz) is not { Mine: >= 0 } b || !IsHeroOwner(b)) return OwnResult.No("bz_not_yours");
        var m = Prod.Mines[b.Mine]; if (m.State != MineStateKind.Flooded) return OwnResult.No("px_not_flooded");
        long cost = Math.Max(1, b.Staff.Count) * (long)RecipeBook.WagePerDay(WageClass.Unskilled) * 3;
        if (Econ.Ledger.Balance(b.Purse).Mites < cost) return new OwnResult(false, "bz_cannot_afford", [M(cost)], cost, CannotAfford: true);
        // the pumping crew is the mine's own staff working three days of their wages: the coin stays as wages owed to them
        for (int i = 0; i < b.Staff.Count; i++) b.Owed[i] += cost / Math.Max(1, b.Staff.Count);
        m.State = MineStateKind.Active;
        return new OwnResult(true, "px_pumped", [Name(b), M(cost)], cost);
    }
}
