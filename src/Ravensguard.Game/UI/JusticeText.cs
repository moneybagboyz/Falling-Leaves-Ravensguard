using Ravensguard.Sim.Law;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Game.UI;

/// <summary>The words of the courts and the cell (crime-and-justice 6): what a sentence comes to, why it is that step, the proof; shared by the captivity screen's cell view and the court panel.</summary>
public static class JusticeText
{
    public static string Crime(string kind) => L.T("jl_crime_" + kind);

    /// <summary>What the sentence does, with its numbers: "a fine, 580 mites", "flogging, 20 lashes", "a term in the gaol, 15 days".</summary>
    public static string What(Expected e)
    {
        string step = L.T(e.TextKey);
        return e.Kind switch
        {
            "fine" or "kin_pays" or "confiscation" or "restitution" or "weregild" or "blood_price" or "penance" => L.F("jl_what_money", step, Quotes.Money(e.Mites)),
            "flogging" => L.F("jl_what_lashes", step, e.Lashes),
            "stocks" or "gaol" or "bond_labour" or "exile" => L.F("jl_what_days", step, System.Math.Max(1, e.Days)),
            _ => step,
        };
    }

    /// <summary>The line the cell shows: "Theft, second conviction: Valeholt law (Stocks, then the hand): flogging, 20 lashes".</summary>
    public static string Line(string crimeKind, Expected e)
    {
        string nth = L.T("jl_conviction_" + System.Math.Min(e.Priors, 3));
        string law = e.Clergy ? L.T("jl_by_clergy") : e.LordsLaw ? L.F("jl_by_lords_law", L.T(e.KeyText)) : L.F("jl_by_law", L.T(e.KeyText));
        string mercy = e.Mercy < 0 ? "  " + L.T("jl_mercy_down") : e.Mercy > 0 ? "  " + L.T("jl_mercy_up") : "";
        return L.F("jl_charge_line", Crime(crimeKind), nth, law, What(e)) + mercy;
    }

    public static string Proof(int witnesses, int attending, bool inAct, bool confessed) =>
        confessed ? L.T("jl_proof_confessed") : inAct ? L.T("jl_proof_in_act") : attending > 0 ? L.F("jl_proof_witness", attending, witnesses) : L.T("jl_proof_none");
}
