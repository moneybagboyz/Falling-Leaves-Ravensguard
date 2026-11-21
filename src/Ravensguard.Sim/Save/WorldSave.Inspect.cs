using System.Text.Json;
using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Save;

public static partial class WorldSave
{
    /// <summary>
    /// The pre-load report of a (migrated) save against the content it would load with: what the content change does (<see cref="ContentRemap"/>'s own notes, from a dry run: its
    /// rewrite is thrown away), the mods added, gone or changed, and, when the content changed, whether every regenerated layer still verifies against the hashes the save holds.
    /// Reads nothing into the world and changes nothing; <see cref="Restore"/> does the same checks again and refuses with the same causes. A continent and history already generated
    /// (a world library supplies them) are verified rather than generated twice.
    /// </summary>
    public static LoadReport Inspect(SaveFile file, ContentDatabase content, string currentContentHash, IReadOnlyList<ModEntry>? installedMods = null,
        Continent? knownContinent = null, WorldHistory? knownHistory = null)
    {
        var compat = new SaveReader().CheckCompatibility(file.Header, currentContentHash, installedMods ?? file.Header.Mods);
        var refusals = new List<string>(); IReadOnlyList<string> changes = []; bool checkedLayers = false;
        if (!compat.FormatSupported) refusals.Add("this save was made by a newer version of the game.");
        else
        {
            SaveFile? mapped = null;
            try { var r = ContentRemap.Apply(file, content); mapped = r.File; changes = r.Notes; }
            catch (SaveException e)
            {
                string mods = compat.MissingMods.Count == 0 ? "" : " Mods missing since the save was made: " + string.Join(", ", compat.MissingMods.Select(m => m.Id)) + ".";
                refusals.Add(e.Message + mods);
            }
            if (mapped != null && !compat.ContentHashMatches)
            {
                checkedLayers = true;
                try { VerifyLayers(mapped, content, knownContinent, knownHistory); }
                catch (SaveException e) { refusals.Add(e.Message); }
            }
        }
        return new LoadReport
        {
            ContentChanged = !compat.ContentHashMatches, NeedsMigration = compat.NeedsMigration && compat.FormatSupported,
            MissingMods = compat.MissingMods, ChangedMods = compat.ChangedMods, ExtraMods = compat.ExtraMods,
            Changes = changes, LayersChecked = checkedLayers, Refusals = refusals,
            Notices = compat.FormatSupported && LoadReport.IsBeforePhase7(file.Header.GameVersion) ? [LoadReport.CoinageNotice] : [],
        };
    }

    /// <summary>The layers a load regenerates rather than reads, each compared with its stored hash: continent, history, region, landmarks and the people a diff-saved region is stored against. Throws <see cref="SaveException"/> naming the first that differs.</summary>
    private static void VerifyLayers(SaveFile file, ContentDatabase content, Continent? knownContinent, WorldHistory? knownHistory)
    {
        var meta = Decode<WorldMeta>(file, MetaSection, compressed: false);
        var cs = JsonSerializer.Deserialize<ContinentSettings>(meta.ContinentSettingsJson, Json) ?? throw new SaveException("Corrupt continent settings in save.");
        var hs = JsonSerializer.Deserialize<HistorySettings>(meta.HistorySettingsJson, Json) ?? throw new SaveException("Corrupt history settings in save.");
        var rs = JsonSerializer.Deserialize<RegionSettings>(meta.RegionSettingsJson, Json) ?? throw new SaveException("Corrupt region settings in save.");
        var continent = knownContinent ?? ContinentGenerator.Generate(cs);
        Verify("continent", meta.ContinentHash, LayerHashes.Of(continent));
        var history = knownHistory ?? HistoryGenerator.Generate(continent, hs);
        Verify("history", meta.HistoryHash, LayerHashes.Of(history));
        if (meta.EmbarkCellX % RegionCoord.CellsPerSide != 0 || meta.EmbarkCellY % RegionCoord.CellsPerSide != 0) return;   // Restore refuses it with its own cause
        var region = RegionGenerator.Generate(continent, history, meta.EmbarkCellX, meta.EmbarkCellY, rs);
        Verify("region", meta.RegionHash, LayerHashes.Of(region));
        if (meta.LandmarkHash.Length > 0) Verify("landmark", meta.LandmarkHash, LayerHashes.OfLandmarks(LandmarkPlacer.Place(region, history, content)));
        var landWorks = file.Sections.ContainsKey(LandWorksSave.SectionName) ? Decode<LandWorksSave>(file, LandWorksSave.SectionName, compressed: true) : null;
        if (file.Sections.ContainsKey(GrowthSection) || landWorks is { Streets.Length: > 0 })
        {
            var g = file.Sections.ContainsKey(GrowthSection) ? Decode<GrowthSave>(file, GrowthSection, compressed: true).Restore() : new Ravensguard.Sim.Growth.GrowthState();
            if (landWorks != null) g.Streets.AddRange(landWorks.RestoreStreets());
            Ravensguard.Sim.Growth.RegionGrowth.Replay(region, g);
        }
        var people = Decode<PeopleData>(file, PeopleSection, true);
        if (people.Diff != null) BaselineGuard.Verify("The home region", meta.BaselineHash, meta.GeneratorVersion, Populator.Populate(region, history, content));
    }
}
