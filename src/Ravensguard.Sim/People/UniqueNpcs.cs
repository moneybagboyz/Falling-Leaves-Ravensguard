using System.Globalization;
using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.People;

/// <summary>Where an authored NPC was put: the content id, the person row, the settlement they live in and the post (region metres, -1 for none).</summary>
public sealed record NpcPlacement(string Npc, int Row, int Settlement, int X, int Y, string Heirloom);

/// <summary>
/// Unique NPCs (P8-LM-2, landmarks-npcs spec 3.2): about thirty authored characters (<c>unique_npc</c> content) put into a new world as ordinary person rows. A keeper takes the post of a landmark the region
/// really has (<c>landmark.keeper</c> names them back), a wanderer or a rival a town or village that fits, an advisor a realm's seat; the first hour's mentors are placed by the onboarding (<c>role</c>).
/// <para>Placement is a stage after generation, as the lairs are: <see cref="Place"/> changes the populated region and nothing about the region, its people as generated, the history or the continent, so
/// no generation hash moves. Every draw is stream 0x828 keyed on the region's cell and the NPC. At most one of each: a character already in the table (or named in <c>taken</c> by the caller, the
/// characters of the world's other regions) is not placed again. They are people like any other: they age, fight and die; a dead NPC's dialogue is never used again
/// (<see cref="SpeakerOf"/>) and the lore of their landmark says so (<see cref="LoreOf"/>).</para>
/// </summary>
public static class UniqueNpcs
{
    /// <summary>The traits an authored NPC may have, in the order of the bits of the person's <c>Traits</c>.</summary>
    public static readonly string[] TraitNames =
    [
        "bold", "wary", "pious", "greedy", "kind", "cruel", "learned", "cunning", "proud", "humble", "bitter", "merry", "honest", "secretive", "stern", "restless",
    ];

    public static ushort TraitBits(IEnumerable<string> names)
    {
        int bits = 0;
        foreach (var n in names) { int i = Array.IndexOf(TraitNames, n); if (i >= 0) bits |= 1 << i; }
        return (ushort)bits;
    }

    public static IEnumerable<string> TraitsOf(ushort bits) { for (int i = 0; i < TraitNames.Length; i++) if ((bits & (1 << i)) != 0) yield return TraitNames[i]; }

    /// <summary>Every authored NPC, in id order.</summary>
    public static List<UniqueNpcDef> All(ContentDatabase db) => [.. db.All<UniqueNpcDef>().OrderBy(d => d.Id, StringComparer.Ordinal)];

    /// <summary>The dialogue speaker of an NPC: the def's own, or its id.</summary>
    public static string SpeakerId(UniqueNpcDef d) => d.Dialogue.Length > 0 ? d.Dialogue : d.Id;

    private static ulong Fnv(string s) { ulong h = 14695981039346656037UL; foreach (char c in s) h = (h ^ c) * 1099511628211UL; return h; }

    private static ulong RegionKey(RegionData r) => ((ulong)(uint)r.CellX << 20) ^ ((ulong)(uint)r.CellY << 40) ^ 0x4E50433AUL;

    private static bool TryEnum<T>(string name, out T value) where T : struct, Enum => Enum.TryParse(name.Replace("_", ""), true, out value) && Enum.IsDefined(value);

    // ------------------------------------------------------------------ the rule: where an NPC fits

    /// <summary>The settlements a "settlement" rule accepts, in the order they are tried: a seat first (a realm's capital), then by size, ties by index.</summary>
    public static List<int> Candidates(UniqueNpcPlaceDef rule, RegionData region, WorldHistory? history)
    {
        var list = new List<int>();
        for (int s = 0; s < region.Settlements.Count; s++)
        {
            var st = region.Settlements[s];
            if (st.Class == SettlementClass.Farmstead || st.TargetPop < rule.MinPop) continue;
            if (rule.Settlement == "town" && st.Class != SettlementClass.Town) continue;
            if (rule.Settlement == "village" && st.Class != SettlementClass.Village) continue;
            list.Add(s);
        }
        if (rule.Seat)
        {
            var seats = list.Where(s => IsSeat(region.Settlements[s], history)).ToList();
            if (seats.Count > 0) return seats;
            var towns = list.Where(s => region.Settlements[s].Class == SettlementClass.Town).ToList();
            if (towns.Count > 0) list = towns;
        }
        return [.. list.OrderByDescending(s => region.Settlements[s].TargetPop).ThenBy(s => s)];
    }

    private static bool IsSeat(RegionSettlement s, WorldHistory? h)
    {
        if (h == null || s.Realm < 0 || s.Realm >= h.Realms.Count || s.HistoryId == EntityId.None) return false;
        int cap = h.Realms[s.Realm].Capital;
        return cap >= 0 && cap < h.Settlements.Count && h.Settlements[cap].Id == s.HistoryId;
    }

    /// <summary>The settlement nearest a point (squared metres in <c>long</c>; a village or town before a farmstead), -1 when the region has none.</summary>
    public static int NearestSettlement(RegionData region, int x, int y)
    {
        int best = -1; long bd = long.MaxValue;
        for (int pass = 0; pass < 2 && best < 0; pass++)
            for (int s = 0; s < region.Settlements.Count; s++)
            {
                var st = region.Settlements[s];
                if (pass == 0 && st.Class == SettlementClass.Farmstead) continue;
                long dx = st.CenterX - x, dy = st.CenterY - y, d = dx * dx + dy * dy;
                if (d < bd) { bd = d; best = s; }
            }
        return best;
    }

    /// <summary>Whether a region fits the NPC's rule, and where: the settlement they will live in and the post (landmark metres, or -1). Null when the region does not fit.</summary>
    public static (int Settlement, int X, int Y)? Fit(UniqueNpcDef d, RegionData region, WorldHistory? history, string heroOrigin, ulong seed)
    {
        var rule = d.Place;
        if (rule.Kind == "role") return null;
        if (rule.Origin.Length > 0 && rule.Origin != heroOrigin) return null;
        if (rule.Kind == "landmark")
        {
            if (region.Landmarks is not { } marks) return null;
            foreach (var want in rule.Landmarks)
                foreach (var m in marks.OrderBy(m => m.Index))
                {
                    if (m.TemplateId != want) continue;
                    int s = NearestSettlement(region, m.X, m.Y);
                    if (s >= 0) return (s, m.X, m.Y);
                }
            return null;
        }
        var cand = Candidates(rule, region, history);
        if (cand.Count == 0) return null;
        // the top of the list when a seat is asked (the capital, or the biggest town); otherwise any that fits, picked by the stream
        int pick = rule.Seat ? 0 : DetRng.Range(seed, 0, Fnv(d.Id), RngStreams.NpcPlacement, 1, 0, cand.Count);
        return (cand[pick], -1, -1);
    }

    // ------------------------------------------------------------------ placement

    /// <summary>
    /// Puts every NPC whose rule fits this region (and whose chance passes) into the population: the person row, a household of their own with no dwelling (a landless one, as vagrants have), their
    /// attributes, skills and traits, the authored record and a heirloom named from the place's culture. <paramref name="taken"/> are the NPCs the world already has elsewhere. Returns what was placed.
    /// </summary>
    public static List<NpcPlacement> Place(Population pop, WorldHistory? history, ContentDatabase db, ulong seed, string heroOrigin = "", IReadOnlySet<string>? taken = null,
        IReadOnlyList<string>? cultureNames = null, IdAllocator? ids = null)
    {
        var placed = new List<NpcPlacement>();
        var pt = pop.People; var hht = pop.Households; var region = pop.Region;
        foreach (var d in All(db))
        {
            if (d.Place.Kind == "role" || taken != null && taken.Contains(d.Id) || pt.RowOfAuthored(d.Id) >= 0) continue;
            var fit = Fit(d, region, history, heroOrigin, seed);
            if (fit == null) continue;
            var (settlement, ax, ay) = fit.Value;
            if (DetRng.Range(seed, (long)RegionKey(region), Fnv(d.Id), RngStreams.NpcPlacement, 0, 0, 1000) >= d.Place.ChancePm) continue;
            placed.Add(Create(pop, d, settlement, ax, ay, db, seed, cultureNames, ids));
        }
        return placed;
    }

    private static NpcPlacement Create(Population pop, UniqueNpcDef d, int settlement, int ax, int ay, ContentDatabase db, ulong seed, IReadOnlyList<string>? cultureNames, IdAllocator? ids)
    {
        var pt = pop.People; var hht = pop.Households; var region = pop.Region;
        ulong key = Fnv(d.Id);
        int culture = region.Settlements[settlement].Culture;
        ids?.EnsureNext(IdKind.Household, hht.BaseCounter + (ulong)hht.Count); ids?.Allocate(IdKind.Household);
        int hh = hht.Add(settlement, -1, DwellingType.None, 0);
        var cls = Enum.TryParse<SocialClass>(d.Class, true, out var c0) ? c0 : SocialClass.Peasant;
        hht.Class[hh] = cls; hht.Deed[hh] = DeedKind.None;
        ids?.EnsureNext(IdKind.Person, pt.BaseCounter + (ulong)pt.Count); ids?.Allocate(IdKind.Person);
        int row = pt.Add();
        int age = d.AgeMin + DetRng.Range(seed, 0, key, RngStreams.NpcPlacement, 2, 0, Math.Max(1, d.AgeMax - d.AgeMin + 1));
        pt.Given[row] = d.Given; pt.Family[row] = d.Family; pt.Byname[row] = d.Byname; pt.Sex[row] = d.Female ? Sex.Female : Sex.Male;
        pt.BirthDay[row] = PersonTable.BirthDayFor(pop.NowDay, age, DetRng.Range(seed, 0, key, RngStreams.NpcPlacement, 3, 0, TimeConst.DaysPerYear), pt.AgingPace);
        pt.Culture[row] = (byte)Math.Clamp(culture, 0, 255); pt.Household[row] = hh; pt.HomeSettlement[row] = settlement; pt.Class[row] = cls;
        pt.Occupation[row] = TryEnum<Occupation>(d.Occupation, out var occ) ? occ : Occupation.None;
        pt.WorkSettlement[row] = settlement;
        pt.Bed[row] = -1; pt.Homeless[row] = d.Wanderer ? HomelessReason.Vagrant : HomelessReason.NoRoom;
        var bonus = OccupationInfo.Bonus(pt.Occupation[row]);
        for (int a = 0; a < PersonTable.AttrCount; a++)
        {
            int v = d.Attributes.Count > a ? d.Attributes[a] : 45 + (a == 0 ? bonus.Str : a == 1 ? bonus.Tou : a == 2 ? bonus.Dex : a == 3 ? bonus.Per : bonus.Wit);
            pt.Attr[row * PersonTable.AttrCount + a] = (byte)Math.Clamp(v, 1, 100);
        }
        int slot = 0;
        foreach (var (name, level) in d.Skills.OrderByDescending(k => k.Value).ThenBy(k => k.Key, StringComparer.Ordinal))
            if (slot < PersonTable.SkillSlots && TryEnum<Skill>(name, out var sk)) pt.SetSkill(row, slot++, sk, level);
        pt.Traits[row] = TraitBits(d.Traits);
        hht.AddMember(hh, row); hht.Head[hh] = row; hht.Name[hh] = d.Family.Length > 0 ? d.Family : d.Given;
        if (settlement >= 0 && settlement < pop.SettlementPeople.Length) pop.SettlementPeople[settlement]++;
        string cultureName = cultureNames != null && culture >= 0 && culture < cultureNames.Count ? cultureNames[culture] : "";
        string heirloom = d.Heirloom switch
        {
            "relic" => Ravensguard.Sim.Goods.Artefacts.RelicName(db, seed, key, cultureName),
            "weapon" => Ravensguard.Sim.Goods.Artefacts.HeirloomName(db, seed, key, cultureName),
            _ => "",
        };
        pt.SetAuthored(row, new AuthoredInfo(d.Id, ax, ay, heirloom));
        pop.ResetKin();
        return new NpcPlacement(d.Id, row, settlement, ax, ay, heirloom);
    }

    // ------------------------------------------------------------------ the living and the dead

    /// <summary>The dialogue speaker of the person at <paramref name="row"/>: their authored id while they live; null for anyone else and for a dead NPC (their lines are never used again).</summary>
    public static string? SpeakerOf(PersonTable pt, ContentDatabase? db, int row)
    {
        if (row < 0 || row >= pt.Count || pt.AuthoredOf(row) is not { } a || pt.Status[row] != PersonStatus.Alive) return null;
        return db != null && db.TryGet<UniqueNpcDef>(a.Id, out var d) ? SpeakerId(d) : a.Id;
    }

    /// <summary>The name of an authored person as the lore says it: given name and byname ("Osgar the Ferryman").</summary>
    public static string LoreName(PersonTable pt, int row) => pt.FullName(row);

    /// <summary>
    /// The lore of a placed landmark as it stands now: the placed text, and, once the keeper who was put at this very place has died, the template's "since the death" sentence (or the default one).
    /// </summary>
    public static string LoreOf(PlacedLandmark m, ContentDatabase db, PersonTable pt)
    {
        if (!db.TryGet<LandmarkDef>(m.TemplateId, out var def) || def.Keeper.Length == 0) return m.Lore;
        foreach (var (row, a) in pt.Authored)
        {
            if (a.Id != def.Keeper || a.X != m.X || a.Y != m.Y || pt.Status[row] != PersonStatus.Dead) continue;
            string text = def.LoreKeeperDead.Length > 0 ? def.LoreKeeperDead : "Since the death of {person}, no one keeps the " + def.Name.ToLowerInvariant() + ".";
            return m.Lore + " " + text.Replace("{person}", LoreName(pt, row)).Replace("{place}", m.Place);
        }
        return m.Lore;
    }

    /// <summary>Rewrites the Codex pages of the region's landmarks with their lore as it stands now (a keeper may have died since the book was built).</summary>
    public static void RefreshCodex(Ravensguard.Sim.Codex.CodexBook codex, RegionData region, ContentDatabase db, PersonTable pt)
    {
        if (region.Landmarks is not { } marks || pt.Authored.Count == 0) return;
        foreach (var m in marks)
            if (codex.Get("landmark." + m.Index) is { } e) e.Text = LoreOf(m, db, pt);
    }

    /// <summary>The post of an authored NPC in region millimetres, when they keep one (a keeper's landmark); false for anyone else.</summary>
    public static bool TryPost(PersonTable pt, int row, out long xMm, out long yMm)
    {
        xMm = yMm = 0;
        if (pt.AuthoredOf(row) is not { X: >= 0, Y: >= 0 } a) return false;
        xMm = a.X * 1000L; yMm = a.Y * 1000L; return true;
    }

    /// <summary>The ids of the NPCs already in a table (the characters a world has), for <c>taken</c>.</summary>
    public static HashSet<string> Present(PersonTable pt) => [.. pt.Authored.Values.Select(a => a.Id)];

    /// <summary>A short line for logs and tests.</summary>
    public static string Describe(NpcPlacement p) => string.Create(CultureInfo.InvariantCulture, $"{p.Npc}@{p.Settlement}({p.X},{p.Y})");
}
