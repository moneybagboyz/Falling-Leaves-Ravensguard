using System.Security.Cryptography;
using System.Text;
using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Diagnostics;

/// <summary>
/// A short scripted Phase 5 trail as one hash (phase-5 review S14, overview 2.2): a new game of the seed's world with its starting estate and the hero's household, a floor on one post built
/// on open ground by the start place (the land rule lifted for the stage, as the dev console does) and knocked out, then ten game days run through the whole runtime; the hash covers the sections
/// the Phase 5 systems own (land and its deeds, sites, construction and its rooms, designs, ownership and the businesses, crafting, wills) and the chronicle. CI prints it on Linux and Windows
/// (<c>hashes</c>' <c>phase5_trail</c> line) and fails when they differ; it is pinned in the golden-hash test. Phase 6's own line (overview OV-8) extends it.
/// </summary>
public static class Phase5Trail
{
    public const int Days = 10;

    private static readonly string[] Sections =
    [
        LandSave.SectionName, SiteSave.SectionName, ConstructionSave.SectionName, DesignsSave.SectionName, ProductionSave.SectionName, OwnershipSave.SectionName,
        WorldSave.CraftingSection, WillsSave.SectionName, WorldSave.ChronicleSection,
    ];

    /// <summary>The trail's hash for a seed's world; <paramref name="pop"/> is cloned, never changed.</summary>
    public static string Hash(ContentDatabase db, Continent continent, WorldHistory history, ContinentSettings cs, HistorySettings hs, RegionSettings rs, int cellX, int cellY, Population pop)
    {
        var origin = db.All<OriginDef>().OrderBy(o => o.Order).First();
        var hero = HeroRecord.FromOrigin("Aldric Vane", false, origin);
        var st = NewGameWorld.Create(db, continent, history, cs, hs, rs, cellX, cellY, pop.Clone(), hero, null);
        st.Content = db;
        using var rt = new WorldRuntime(st, db, new MemoryRegionPager());
        long sec = (rt.HourDone + 1) * 3600;
        var cw = st.HomeRecords!.Sim.Get<ConstructionWorld>()!;
        var home = st.Population.Region.Settlements[st.Population.People.HomeSettlement[st.Hero!.PersonIndex]];
        int ox = home.CenterX + 40, oy = home.CenterY + 40;
        var may = cw.May; cw.May = null;
        var cells = new List<PlaceCell> { new(ox + 1, oy + 1, 0, "post_timber") };
        for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++) cells.Add(new PlaceCell(ox + x, oy + y, 1, "floor_plank"));
        var placed = cw.Place(st.Party!.Hero, PurseKind.Person, -1, cells, sec);
        cw.May = may;
        if (placed.Ok) { cw.DevStock(placed.Site, sec); cw.DevFinish(placed.Site, 99, 0, 0, sec); cw.Destroy(ox + 1, oy + 1, 0, sec, st.Party.Hero, "knocked out"); }
        rt.AdvanceTo(GameTime.FromGameHours(rt.HourDone + 1 + Days * 24L));
        var (file, standing, law) = rt.AtBarrier(() => (WorldSave.Capture(rt.State, "h", "0.1"), rt.State.Standing?.Hash() ?? 0UL, rt.State.Law?.Hash() ?? 0UL)).GetAwaiter().GetResult();   // P6-A3: the standing book joins the trail
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(BitConverter.GetBytes(placed.Ok));
        foreach (var name in Sections.Order(StringComparer.Ordinal))
        {
            sha.AppendData(Encoding.UTF8.GetBytes(name)); sha.AppendData([0]);
            if (file.Sections.TryGetValue(name, out var bytes)) { sha.AppendData(BitConverter.GetBytes(bytes.Length)); sha.AppendData(bytes.Span); }
        }
        sha.AppendData(BitConverter.GetBytes(standing));
        if (rt.State.Fire is { IsEmpty: false } fire) sha.AppendData(BitConverter.GetBytes(fire.Hash()));      // P6-B5: FireHash, only once something has burnt (a fire-free trail keeps its pinned hash)
        if (law != 0) sha.AppendData(BitConverter.GetBytes(law));          // P6-B2: the LawHash joins the trail only once a crime exists, so a world without crime keeps its pinned hash
        if (rt.AtBarrier(() => rt.State.Justice is { IsEmpty: false } jb ? jb.Hash() : 0UL).GetAwaiter().GetResult() is var justice and not 0) sha.AppendData(BitConverter.GetBytes(justice));          // P6-C1: the courts' hash joins the trail only once someone is held or sentenced
        if (rt.AtBarrier(() => rt.State.Contracts is { IsEmpty: false } ctb ? ctb.Hash() : 0UL).GetAwaiter().GetResult() is var contracts and not 0) sha.AppendData(BitConverter.GetBytes(contracts));          // P6-C2: the ContractHash joins the trail only once a contract, tournament or slander exists
        if (rt.AtBarrier(() => rt.State.Heists is { IsEmpty: false } hj ? hj.Hash() : 0UL).GetAwaiter().GetResult() is var heists and not 0) sha.AppendData(BitConverter.GetBytes(heists));          // P6-D3: the HeistHash joins the trail only once a site has been visited
        if (rt.AtBarrier(() => rt.State.Family?.Hash() ?? 0UL).GetAwaiter().GetResult() is var family and not 0) sha.AppendData(BitConverter.GetBytes(family));          // P6-B3: the FamilyHash joins the trail only once a family exists
        if (rt.AtBarrier(() => rt.State.Disease is { IsBlank: false } d ? d.Hash() : 0UL).GetAwaiter().GetResult() is var disease and not 0) sha.AppendData(BitConverter.GetBytes(disease));          // P6-C4: the DiseaseHash joins the trail only once anything has happened
        if (rt.AtBarrier(() => rt.State.Soil is { IsEmpty: false } sb ? sb.ComputeHash() : "").GetAwaiter().GetResult() is { Length: > 0 } soil) sha.AppendData(Encoding.UTF8.GetBytes(soil));          // P6-D4: the soil works' hash joins the trail only once a job exists (the streets are in the growth hash)
        return Convert.ToHexString(sha.GetHashAndReset())[..16];
    }
}
