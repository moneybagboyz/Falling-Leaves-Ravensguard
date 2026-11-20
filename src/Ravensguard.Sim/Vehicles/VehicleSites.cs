using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Crafting;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Carts;

/// <summary>What a vehicle-site action came to (the hero desk turns it into a message).</summary>
public enum SiteAnswer : byte { Ok, NoDesign, Invalid, NotYours, NoPart, WrongPart, Finished, NotFinished, NeedsHands, NoMaker, CannotAfford, NotHere, NotReady, NoMaterials, Afloat, NotABoat }

/// <summary>
/// Building and repairing vehicles part by part (vehicle-designer 4.2, 4.3): a site is ordered from a design; each part's item (made by its recipe at the right station, through the crafting engine) is put
/// on the site's pile; the parts are fitted in stack order (frame, axles, wheels, tyres, the rest) at <c>install_h</c> worker-hours each with the one worker-hour rule (<c>time_mult = 1300 - 6 x skill</c>).
/// Unfinished vehicles are world objects anyone allowed may resume; a boat is launched from the slipway when its hull is whole. Commissions are the cartwright's or shipwright's own work at his shop and yard.
/// </summary>
public static class VehicleSites
{
    /// <summary>The order parts are fitted in: the frame (runners, hull planks, keel) first, then axles, wheels, tyres and brakes, then the bed and the rest.</summary>
    public static int Stage(Content.VehiclePartDef p) =>
        p.Tags.Contains("frame") || p.Tags.Contains("runner") || p.Tags.Contains("hull") || p.Tags.Contains("body") ? 0
        : p.Tags.Contains("axle") ? 1 : p.Tags.Contains("wheel") ? 2 : p.Tags.Contains("tyre") || p.Tags.Contains("brake") ? 3 : p.Tags.Contains("gunwale") || p.Tags.Contains("deck") ? 4 : 5;

    /// <summary>The worker-hour rule (overview 8.3): <c>time_mult = 1300 - 6 x skill</c> permille (skill 50 is 1,000), at least 400.</summary>
    public static int TimeMultPm(int skill) => Math.Max(400, 1300 - 6 * Math.Clamp(skill, 0, 100));

    /// <summary>The worker-milli-hours one hour of a worker of <paramref name="skill"/> puts in (1,000 at skill 50).</summary>
    public static long MilliPerHour(int skill) => 1_000_000L / TimeMultPm(skill);

    /// <summary>A part's install work, worker-milli-hours.</summary>
    public static int InstallMilli(Content.VehiclePartDef p) => (int)Math.Round(p.InstallH * 1000);

    /// <summary>The index of the next part to fit (a ghost or the one in progress, in stage then cell order), or -1 when every part is fitted.</summary>
    public static int Next(VehicleRow v, VehicleCatalog cat)
    {
        int best = -1; int bs = int.MaxValue; long bk = long.MaxValue;
        for (int i = 0; i < v.Parts.Count; i++)
        {
            var p = v.Parts[i]; if (p.State is not (PartState.Ghost or PartState.InProgress)) continue;
            if (p.State == PartState.InProgress) return i;
            int s = Stage(cat.PartList[p.Part]);
            if (s < bs || (s == bs && p.SortKey < bk)) { best = i; bs = s; bk = p.SortKey; }
        }
        return best;
    }

    /// <summary>The part ids still wanted on the site (each once per ghost), in fitting order: the shopping list the site panel shows.</summary>
    public static List<string> Wanted(VehicleRow v, VehicleCatalog cat)
    {
        var l = new List<(int S, long K, string Id)>();
        foreach (var p in v.Parts) if (p.State == PartState.Ghost) { var d = cat.PartList[p.Part]; l.Add((Stage(d), p.SortKey, d.Id)); }
        return [.. l.OrderBy(x => x.S).ThenBy(x => x.K).Select(x => x.Id)];
    }

    /// <summary>The items on the site's pile (owned by the vehicle), in id order.</summary>
    public static List<EntityId> Pile(ItemTable items, EntityId vehicle)
    {
        var l = new List<EntityId>();
        foreach (int i in items.LiveRows()) if (items.At(i).Owner == vehicle) l.Add(items.At(i).Id);
        l.Sort();
        return l;
    }

    /// <summary>Orders a vehicle site of a design (Enter in the designer at a yard or slipway): a vehicle row with every part a ghost, and the site. Null when the design is unknown or does not validate.</summary>
    public static (SiteAnswer, VehicleRow?) Order(RideBook b, string design, EntityId owner, long xMm, long yMm)
    {
        var bp = b.Templates.Find(design); if (bp == null) return (SiteAnswer.NoDesign, null);
        if (bp.Warnings.Any(w => w.IsError)) return (SiteAnswer.Invalid, null);
        var v = b.Vehicles.Create(bp, owner, xMm, yMm, ghosts: true);
        b.Works.Sites[v.Id] = new VehicleSite { Vehicle = v.Id, Owner = owner };
        return (SiteAnswer.Ok, v);
    }

    /// <summary>Puts a part's item on the site's pile (it must be a part the vehicle still wants).</summary>
    public static SiteAnswer Deposit(RideBook b, ItemTable items, VehicleRow v, EntityId item, EntityId from)
    {
        if (!items.IsLive(item)) return SiteAnswer.NoPart;
        var row = items.Get(item); string shape = row.Shape;
        if (row.Owner != from) return SiteAnswer.NotYours;
        var wanted = Wanted(v, b.Templates);
        if (!wanted.Contains(shape) || Pile(items, v.Id).Count(id => items.Get(id).Shape == shape) >= wanted.Count(s => s == shape)) return SiteAnswer.WrongPart;
        items.Get(item).Owner = v.Id;
        return SiteAnswer.Ok;
    }

    /// <summary>
    /// Puts <paramref name="milli"/> worker-milli-hours into the site: the next part takes its item from the pile (the item becomes the part: its tier and its condition), then its install hours; when a part's
    /// work is done it is built. Stops when the next part's item is not there. Returns the work used. A hull complete on the slipway waits for its launch.
    /// </summary>
    public static long Work(RideBook b, ItemTable items, VehicleRow v, long milli, long nowSec)
    {
        var cat = b.Templates; long used = 0;
        b.Works.Sites.TryGetValue(v.Id, out var site);
        while (milli > 0)
        {
            int i = Next(v, cat); if (i < 0) break;
            var p = v.Parts[i]; var d = cat.PartList[p.Part];
            if (p.State == PartState.Ghost)
            {
                EntityId it = default; bool found = false;
                foreach (var id in Pile(items, v.Id)) if (items.Get(id).Shape == d.Id) { it = id; found = true; break; }
                if (!found) break;                                                       // waiting for this part to be made
                ref var row = ref items.Get(it);
                p.Tier = row.Quality;
                p.HpMilli = (int)((long)d.Hp * Math.Clamp((int)row.ConditionPermille, 1, 1000));
                items.Free(it);
                p.State = PartState.InProgress; p.WorkMilli = 0;
            }
            int need = InstallMilli(d) - p.WorkMilli;
            long take = Math.Min(milli, Math.Max(0, need));
            p.WorkMilli += (int)take; milli -= take; used += take;
            if (p.WorkMilli >= InstallMilli(d)) p.State = PartState.Built;
            v.Parts[i] = p;
            if (p.State == PartState.Built) v.Touch();
        }
        if (site != null) { site.WorkedMilli += used; if (v.Finished && site.DoneSec == 0) site.DoneSec = nowSec; }
        return used;
    }

    /// <summary>
    /// The hours a design takes to build (VD-4): every part's recipe (its attended steps at the recipe's own base time) and its install, worker-hours in thousandths. The content sums; a maker of skill 50 at
    /// a station of his trade works at this pace, others at the worker-hour rule's.
    /// </summary>
    public static (long RecipeMilli, long InstallMilli) BuildMilli(VehicleBlueprint bp, VehicleCatalog cat, RecipeDb db)
    {
        long rec = 0, ins = 0;
        foreach (var p in bp.Parts)
        {
            var d = cat.PartList[p.Part];
            ins += InstallMilli(d);
            if (db.TryGet(d.Recipe, out var r)) foreach (var s in r.Steps) if (s.Attended) rec += s.Seconds * 1000 / 3600;
        }
        return (rec, ins);
    }

    /// <summary>Launches a boat from the slipway when its hull is whole (vehicle-designer 4.2): four people for ten minutes, or a horse team. Returns the seconds it takes.</summary>
    public static (SiteAnswer, long Seconds) Launch(RideBook b, VehicleRow v, int people, bool horseTeam)
    {
        if (!b.Vehicles.StatsOf(v).Boat && !b.Vehicles.DesignOf(v).Stats.Boat) return (SiteAnswer.NotABoat, 0);
        if (v.Water != WaterState.Slipway) return (SiteAnswer.Afloat, 0);
        foreach (var p in v.Parts) if (p.State != PartState.Built && b.Templates.PartList[p.Part].Tags.Contains("hull")) return (SiteAnswer.NotFinished, 0);
        if (people < 4 && !horseTeam) return (SiteAnswer.NeedsHands, 0);
        v.Water = WaterState.Afloat;
        return (SiteAnswer.Ok, 600);
    }

    /// <summary>
    /// Repairs a damaged part with its recipe's repair line (vehicle-designer 4.3): the components come out of <paramref name="from"/> (a field repair needs them at hand; a wheelwright's yard has them),
    /// +300 permille of the part's hit points. Returns the hours of work the repair is (the recipe's attended steps).
    /// </summary>
    public static (SiteAnswer, long Seconds) Repair(RideBook b, RecipeDb db, VehicleRow v, int part, Store from)
    {
        if (part < 0 || part >= v.Parts.Count) return (SiteAnswer.Invalid, 0);
        var p = v.Parts[part]; var d = b.Templates.PartList[p.Part];
        if (p.State is PartState.Ghost or PartState.InProgress) return (SiteAnswer.NotFinished, 0);
        string rid = d.Repair.Length > 0 ? d.Repair : d.Tags.Contains("wheel") ? "repair_cart_wheel" : "repair_vehicle_frame";
        if (!db.TryGet(rid, out var r)) return (SiteAnswer.Invalid, 0);
        foreach (var slot in r.Slots) { var a = slot.Alts[0]; if (from.Qty(a.Good) < a.Qty) return (SiteAnswer.NoMaterials, 0); }
        foreach (var slot in r.Slots) { var a = slot.Alts[0]; from.Destroy(a.Good, a.Qty, DestroyReason.Consumed); }
        b.Vehicles.RepairPart(v, part, 300);
        long sec = 0; foreach (var s in r.Steps) if (s.Attended) sec += s.Seconds;
        return (SiteAnswer.Ok, sec);
    }

    /// <summary>
    /// Replaces a broken part (vehicle-designer 4.3): the broken part comes off (its row back to a ghost; the wreck is scrap, gone) and the new part's item goes on the pile; the site's work fits it.
    /// </summary>
    public static SiteAnswer Replace(RideBook b, ItemTable items, VehicleRow v, int part, EntityId item, EntityId from)
    {
        if (part < 0 || part >= v.Parts.Count || v.Parts[part].State != PartState.Broken) return SiteAnswer.Invalid;
        var p = v.Parts[part]; p.State = PartState.Ghost; p.HpMilli = 0; p.WorkMilli = 0; v.Parts[part] = p; v.Touch();
        if (!b.Works.Sites.ContainsKey(v.Id)) b.Works.Sites[v.Id] = new VehicleSite { Vehicle = v.Id, Owner = v.Owner };
        return Deposit(b, items, v, item, from);
    }

    // ------------------------------------------------------------------ commissions

    /// <summary>
    /// Commissions a vehicle from a cartwright or shipwright (vehicle-designer 4.2, VD-17): one P5-E commission per part at his shop (a part he cannot make goes to another maker of the place who can), all
    /// with the makers' materials, the vehicle site as their client with its own purse; the client pays the parts' price into it now and the install labour (his install hours at his wage class and the
    /// solver's margin) on collection. The site stands at his yard (<paramref name="xMm"/>, <paramref name="yMm"/>). Nothing changes on a refusal.
    /// </summary>
    public static (SiteAnswer, VehicleCommission?) Commission(RideBook b, CraftContext w, string design, EntityId client, EntityId maker, long xMm, long yMm, long nowSec, IdAllocator ids)
    {
        var bp = b.Templates.Find(design); if (bp == null) return (SiteAnswer.NoDesign, null);
        if (bp.Warnings.Any(x => x.IsError)) return (SiteAnswer.Invalid, null);
        var db = w.Runner.Db; var cat = b.Templates;
        int settlement = w.SettlementOf(maker);
        // who makes each part, and the quotes
        var plan = new List<(CraftRecipe R, EntityId Maker)>(); long parts = 0;
        foreach (var p in bp.Parts.OrderBy(p => Stage(cat.PartList[p.Part])).ThenBy(p => p.SortKey))
        {
            var d = cat.PartList[p.Part];
            if (!db.TryGet(d.Recipe, out var r)) return (SiteAnswer.NoMaker, null);
            var m = Commissions.MakerKnows(w, r, maker) && w.SiteOf(maker, r) != null ? maker : Commissions.Makers(w, r, settlement).FirstOrDefault();
            if (m == EntityId.None) return (SiteAnswer.NoMaker, null);
            var q = Commissions.Quote(w, r, client, m, false, nowSec); if (q == null) return (SiteAnswer.NoMaker, null);
            parts += q.PriceMites; plan.Add((r, m));
        }
        int skill = w.Runner.State.Book.Level(maker, People.Skill.Carpentry);
        long installH = (BuildMilli(bp, cat, db).InstallMilli + 999) / 1000;
        long install = Commissions.LabourMites(installH, Commissions.WagePerHour(skill), db.Settings.CommissionMarginPm);
        if (w.PurseOf(client) is not { } cp || w.PurseOf(maker) is null) return (SiteAnswer.NoMaker, null);
        if (w.Ledger.Balance(cp).Mites < parts + install) return (SiteAnswer.CannotAfford, null);
        var v = b.Vehicles.Create(bp, client, xMm, yMm, ghosts: true);
        var vp = w.Ledger.Open(PurseKind.Person, v.Id);
        if (parts > 0 && !w.Ledger.Transfer(cp, vp, new Coin(parts), TransferReason.Trade)) { b.Vehicles.Remove(v); return (SiteAnswer.CannotAfford, null); }
        var com = new VehicleCommission { Vehicle = v.Id, Client = client, Maker = maker, Settlement = settlement, PartsMites = parts, InstallMites = install, OrderedSec = nowSec };
        foreach (var (r, m) in plan)
        {
            var (a, c) = Commissions.Order(w, r, v.Id, m, false, new CraftSources(), nowSec, ids);
            if (a != CommissionAnswer.Ok || c == null) continue;                          // cannot happen after the quotes; a part left unordered shows as missing
            com.Parts.Add(c.Id);
        }
        b.Works.Commissions[v.Id] = com;
        b.Works.Sites[v.Id] = new VehicleSite { Vehicle = v.Id, Owner = client, Commission = v.Id, Crew = [maker], CrewSkill = [skill] };
        return (SiteAnswer.Ok, com);
    }

    /// <summary>
    /// The commission's hour (in the hourly pass, after the makers' own): parts made are collected onto the site's pile (the site is their client: the rest of each price comes out of its purse); in his
    /// working hours, when no part of it is still at his bench, the maker fits what is on the pile. A finished vehicle is Ready for the client.
    /// </summary>
    public static void CommissionHour(RideBook b, CraftContext w, VehicleCommission com, long hour)
    {
        if (com.State != VehicleCommissionState.Making) return;
        var st = w.Runner.State;
        var v = b.Vehicles.Find(com.Vehicle); if (v == null) { com.State = VehicleCommissionState.Cancelled; return; }
        bool open = false;
        foreach (var id in com.Parts)
        {
            if (!st.Commissions.TryGetValue(id, out var c)) continue;
            if (c.State == CommissionState.Ready) Commissions.Collect(w, c, v.Id, w.SettlementOf(c.Maker), v.Cargo);
            else if (c.State is CommissionState.Queued or CommissionState.Working && c.Maker == com.Maker) open = true;
        }
        int hod = (int)(hour % 24);
        var set = w.Runner.Db.Settings;
        bool working = hod >= set.MakerDayStartHour && hod < set.MakerDayStartHour + set.MakerHoursPerDay;
        if (working && !open && b.Works.Sites.TryGetValue(v.Id, out var site))
            Work(b, w.Econ.Items, v, MilliPerHour(site.CrewSkill.Count > 0 ? site.CrewSkill[0] : 50), (hour + 1) * 3600);
        if (v.Finished) { com.State = VehicleCommissionState.Ready; com.ReadySec = (hour + 1) * 3600; }
    }

    /// <summary>The client collects the finished vehicle in person at the maker's yard (within 20 m of it): he pays the install labour, any coin left in the site's purse comes back to him, the site closes.</summary>
    public static SiteAnswer Collect(RideBook b, CraftContext w, VehicleCommission com, EntityId who, long xMm, long yMm)
    {
        if (who != com.Client) return SiteAnswer.NotYours;
        if (com.State != VehicleCommissionState.Ready) return SiteAnswer.NotReady;
        var v = b.Vehicles.Find(com.Vehicle); if (v == null) return SiteAnswer.Invalid;
        long dx = v.XMm - xMm, dy = v.YMm - yMm; if (dx * dx + dy * dy > 20_000L * 20_000) return SiteAnswer.NotHere;
        if (w.PurseOf(who) is not { } cp || w.PurseOf(com.Maker) is not { } mp) return SiteAnswer.NoMaker;
        if (w.Ledger.Balance(cp).Mites < com.InstallMites) return SiteAnswer.CannotAfford;
        if (com.InstallMites > 0) w.Ledger.Transfer(cp, mp, new Coin(com.InstallMites), TransferReason.Wage);
        var vp = w.Ledger.Open(PurseKind.Person, v.Id);
        long left = w.Ledger.Balance(vp).Mites; if (left > 0) w.Ledger.Transfer(vp, cp, new Coin(left), TransferReason.Trade);
        com.State = VehicleCommissionState.Collected;
        b.Works.Sites.Remove(v.Id);
        v.Owner = who;
        return SiteAnswer.Ok;
    }

    /// <summary>The hourly pass of the owners' own sites (H2): each crew member works an hour in the working hours (08:00-18:00) where the next part is on the pile.</summary>
    public static void SiteHour(RideBook b, ItemTable items, VehicleSite site, long hour, Economy.Ledger? ledger = null)
    {
        if (site.Paused || site.Commission != EntityId.None || site.DoneSec > 0) return;
        int hod = (int)(hour % 24); if (hod < 8 || hod >= 18) return;
        var v = b.Vehicles.Find(site.Vehicle); if (v == null) return;
        for (int k = 0; k < site.Crew.Count; k++)
        {
            // P5-I: the owner pays each hand's hour through the ledger (the skill's hourly wage, as a construction crew's); a site whose owner cannot pay stands that hour. The owner's own hours are free.
            if (ledger != null && site.Crew[k] != site.Owner)
            {
                long wage = Commissions.WagePerHour(site.CrewSkill[k]);
                if (!ledger.TryFind(PurseKind.Person, site.Owner, out var from) || !ledger.TryFind(PurseKind.Person, site.Crew[k], out var to)) continue;
                if (ledger.Balance(from).Mites < wage || !ledger.Transfer(from, to, new Coin(wage), TransferReason.Wage)) continue;
            }
            Work(b, items, v, MilliPerHour(site.CrewSkill[k]), (hour + 1) * 3600);
        }
    }
}
