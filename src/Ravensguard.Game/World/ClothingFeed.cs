using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Content;
using Ravensguard.Sim.Clothing;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.People;

namespace Ravensguard.Game.World;

/// <summary>
/// The street view's side of the clothing and exposure model (docs/specs/phase3/clothing-warmth-exposure.md): the <see cref="ClothingBook"/> the people bridge and the hero share, the weather turned
/// into an <see cref="ExposureEnv"/>, the hero's body stepped once a game minute, and the text of the HUD condition line. Holds no state of its own beyond the book.
/// </summary>
public sealed class ClothingFeed
{
    /// <summary>The hero's id in the book: a person id in the wardrobe's own region, which no world person has.</summary>
    public static readonly EntityId HeroId = EntityId.MakeIn(IdKind.Person, ClothingBook.WardrobeRegion, 1);

    public ClothingBook Book { get; }
    public Tracked? Hero { get; private set; }

    public ClothingFeed(ContentDatabase db) { Book = new ClothingBook(db); }

    /// <summary>Weather and place as the body model reads them (centi-degrees, cm/s, tenths of mm an hour).</summary>
    public static ExposureEnv EnvOf(in WeatherView w) => new((int)MathF.Round(w.TempC * 100f), (int)MathF.Round(w.WindMps * 100f), (int)MathF.Round(w.Mm * 10f), w.Snowing, w.Sunny);

    /// <summary>The ambient answer the people bridge asks once a game minute per person: the open-air weather at the continent millimetre position, at the minute's own second.</summary>
    public Func<long, long, long, ExposureEnv> Ambient(WeatherFeed feed, long originXM, long originYM)
        => (sec, xMm, yMm) => EnvOf(feed.Raw(sec, (int)(xMm / 1000 - originXM), (int)(yMm / 1000 - originYM)));

    /// <summary>Dresses the hero from the wardrobe of their origin's class (a minor heir like gentry, a peddler like a burgher) the first time it is called.</summary>
    public Tracked EnsureHero(Ravensguard.Sim.Characters.HeroRecord hero, long gameSec)
    {
        if (Hero != null) return Hero;
        var cls = hero.Origin is "minor_heir" or "disgraced_squire" ? SocialClass.Gentry : hero.Origin is "peddler" or "discharged_soldier" ? SocialClass.Burgher : SocialClass.Peasant;
        Hero = Book.Track(HeroId, hero.Female ? Sex.Female : Sex.Male, cls, Occupation.None, hero.Attribute(Ravensguard.Sim.Characters.HeroAttr.Toughness), (ulong)hero.Name.GetHashCode() ^ 0x48455230UL, gameSec);
        return Hero;
    }

    /// <summary>Steps the hero's body to <paramref name="gameSec"/>'s minute (a no-op within a minute).</summary>
    public void StepHero(long gameSec, in WeatherView w, bool indoors, bool fire, Activity act, bool inBed = false)
    {
        if (Hero == null) return;
        long minute = gameSec / 60;
        if (Hero.LastMinute == minute) return;
        var env = EnvOf(w) with { Roofed = indoors, Fire = fire, InBed = inBed };
        Book.StepTo(Hero, minute, env, act);
    }

    /// <summary>The HUD's condition line for the hero, or empty when all is well (spec 6: a chill is announced hours before it matters).</summary>
    public string HudLine()
    {
        var t = Hero; if (t == null) return "";
        var r = t.Last; string core = $"{t.CoreCenti / 100.0:0.0} °C";
        string clo = $"warmth {r.IEffCloX100 / 100.0:0.00} clo, need {r.IReqCloX100 / 100.0:0.00}";
        switch (t.State)
        {
            case BodyState.Normal:
                if (r.ColdStressPm > 300)
                {
                    int m = Book.MinutesTo(t, Book.Tuning.Def.ChilledCc);
                    return m > 0 ? $"Cold: you will be chilled in {Hours(m)} ({clo})" : $"Cold ({clo})";
                }
                if (r.HeatStressPm > 300) return $"Hot: shade and water ({clo})";
                if (r.WetAvgPm > 400) return "Wet clothes lose warmth: find a fire";
                return "";
            case BodyState.Chilled: return $"Chilled {core}: shivering, find a fire ({clo})";
            case BodyState.Moderate: return $"Cold {core}: clumsy and slow, get warm now";
            case BodyState.Severe: case BodyState.Lethal: return t.Collapsed ? $"Hypothermia {core}: unconscious" : $"Hypothermia {core}: you will collapse";
            case BodyState.Warm: return $"Hot {core}: rest in the shade";
            case BodyState.Exhausted: return $"Heat exhaustion {core}: slow and clumsy";
            default: return $"Heatstroke {core}!";
        }
    }

    static string Hours(int minutes) => minutes >= 90 ? $"{minutes / 60.0:0.0} h" : $"{minutes} min";

    // ------------------------------------------------------------------ what a person looks like

    /// <summary>The rig layers a person's worn clothes draw (a cloak or hood only if one is worn), their tints, and which baked outline to draw with.</summary>
    public readonly record struct Look(string Outfit, string[] Layers, Dictionary<string, Color> Tints, string Key);

    static readonly string[] BaseLayers = ["body", "hose", "linen", "tunic"];

    /// <summary>
    /// The look of a person from what they wear: the base layers of a peasant (body, hose, shirt, tunic), plus the cloak, hood or gambeson layers of the pieces actually worn, each tinted by the piece's dye or,
    /// undyed, by its material. The outline is the traveller's when a cloak is worn (its silhouette is bigger), else the peasant's. A person with no clothes record gets the plain peasant.
    /// </summary>
    public Look LookOf(Tracked? t, Occupation occ)
    {
        if (occ == Occupation.Guard) return new Look("spearman", [], new(), "spearman");
        var layers = new List<string>(BaseLayers); var tints = new Dictionary<string, Color>(); var key = new System.Text.StringBuilder();
        bool cloak = false;
        if (t != null)
        {
            foreach (var w in t.Worn)
            {
                var g = Book.GarmentOf(w); if (g.RigLayer == "") continue;
                ref var row = ref Book.Items.Get(w); var ex = Book.ExtraOf(w);
                string mat = Book.Items.Component(row.ComponentStart).Material;
                if (!layers.Contains(g.RigLayer)) layers.Add(g.RigLayer);
                if (g.RigLayer == "cloak") cloak = true;
                if (g.RigLayer == "linen") { key.Append(g.Id).Append(','); continue; }                  // the shirt keeps its baked linen
                var c = ex.Dye != 0 ? FromRgb(ex.Dye) : MaterialColour(mat);
                // a piece of the same rig layer over another (a gambeson over a tunic): the outer wins the colour; a threadbare piece is drawn paler
                if (g.RigLayer != "gambeson" || !tints.ContainsKey("gambeson")) tints[g.RigLayer] = row.ConditionPermille < 400 ? c.Lerp(new Color(0.55f, 0.52f, 0.48f), 0.35f) : c;
                key.Append(g.Id).Append(ex.Dye).Append(row.ConditionPermille < 400 ? 'x' : '.').Append(',');
            }
        }
        if (!tints.ContainsKey("hose")) tints["hose"] = new Color(0.32f, 0.27f, 0.22f);
        return new Look(cloak ? "traveller" : "peasant", [.. layers], tints, key.ToString());
    }

    static Color FromRgb(uint rgb) => new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

    /// <summary>The colour of an undyed piece by its material: bleached linen, grey-brown wool, brown leather and fur.</summary>
    static Color MaterialColour(string material) => material switch
    {
        "linen" => new Color(0.86f, 0.80f, 0.68f), "wool" => new Color(0.58f, 0.50f, 0.40f), "leather" => new Color(0.46f, 0.32f, 0.21f), "fur" => new Color(0.52f, 0.40f, 0.30f), _ => new Color(0.6f, 0.6f, 0.6f),
    };
}
