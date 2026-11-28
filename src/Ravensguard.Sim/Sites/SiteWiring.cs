using Ravensguard.Content;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Weather;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Sites;

/// <summary>Installs the sites' services on the home region's sim (P4-H): the book (the world state's, saved in the "sites" section), the content and the weather above.</summary>
public static class SiteWiring
{
    /// <summary>The site content of a database, built once per database.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ContentDatabase, SiteContent> _content = new();
    public static SiteContent ContentOf(ContentDatabase db) => _content.GetValue(db, d => new SiteContent(d));

    public static void Attach(RegionWorld home, WorldState state, ContentDatabase content, RegionWeather w)
    {
        var book = state.Sites ??= new SiteBook();
        home.Sim.Set(book);
        home.Sim.Set(ContentOf(content));
        home.Sim.Set(new SiteHistoryRef(state.History));
        int cell = (state.EmbarkCellY + 8) * Continent.Size + state.EmbarkCellX + 8;
        home.Sim.Set(new SiteWeather(h => w.AtLite(cell, h).State is WeatherState.HeavyRain or WeatherState.Thunderstorm));
    }
}

/// <summary>The world's history, for the lairs' legends (P8-LM-3): a lair is tied to an event of it.</summary>
public sealed record SiteHistoryRef(Ravensguard.Sim.WorldGen.History.WorldHistory? History);
