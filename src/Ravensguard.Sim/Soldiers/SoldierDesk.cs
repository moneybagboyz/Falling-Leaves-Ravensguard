using System.Globalization;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Soldiers;

/// <summary>
/// The hero's side of soldiers at the barrier (P7-SC-A; the hero desk calls it for <see cref="HeroCommandKind.Soldier"/>). The tag names the verb ("so:verb|..."): <c>enlist|person|role</c> (a man of 16 or more who meets the
/// role's skills, paid by the hero), <c>fill|armoury</c> (the filling rule for all the hero's soldiers from an armoury, by the key of its room's origin cell), <c>equip|person|slot|item|armoury</c> (one piece, from his own
/// things when the armoury is -1), <c>strip|person|slot</c>, <c>role|person|role|template</c> and <c>discharge|person</c> (issued kit goes back to its armoury). Soldiers are named by person id value (the hero's is 0).
/// Every command moves items between an armoury and a man; none makes or spends coin or goods.
/// </summary>
public static class SoldierDesk
{
    public const string Prefix = "so:";
    /// <summary>The hero as the lord who pays: person 0.</summary>
    public const long HeroLord = 0;

    private static string Tag(params object[] parts) => Prefix + string.Join('|', parts.Select(p => Convert.ToString(p, CultureInfo.InvariantCulture)));
    private static HeroCommand Cmd(long sec, string tag) => new() { Kind = HeroCommandKind.Soldier, IssuedSec = sec, Tag = tag, Origin = CommandOrigin.Human };

    public static HeroCommand Enlist(ulong person, string role, long sec) => Cmd(sec, Tag("enlist", person, role));
    public static HeroCommand Fill(long armoury, long sec) => Cmd(sec, Tag("fill", armoury));
    public static HeroCommand Equip(ulong person, string slot, ulong item, long armoury, long sec) => Cmd(sec, Tag("equip", person, slot, item, armoury));
    public static HeroCommand Strip(ulong person, string slot, long sec) => Cmd(sec, Tag("strip", person, slot));
    public static HeroCommand SetRole(ulong person, string role, string template, long sec) => Cmd(sec, Tag("role", person, role, template));
    public static HeroCommand Discharge(ulong person, long sec) => Cmd(sec, Tag("discharge", person));
    /// <summary>P7-SC-B: answer a promotion offer (accept or refuse), dub a man-at-arms, pay the wage roll for the day.</summary>
    public static HeroCommand Promote(ulong person, long sec) => Cmd(sec, Tag("promote", person));
    public static HeroCommand Refuse(ulong person, long sec) => Cmd(sec, Tag("refuse", person));
    public static HeroCommand Dub(ulong person, long sec) => Cmd(sec, Tag("dub", person));
    public static HeroCommand Pay(long sec) => Cmd(sec, Tag("pay"));

    private static HeroResult R(HeroCommand c, HeroResultCode code, HeroText text) => new(c.Seq, c.Kind, code, text, 0, 0, c.IssuedSec);

    public static HeroResult Apply(HeroContext ctx, HeroCommand c)
    {
        if (!c.Tag.StartsWith(Prefix, StringComparison.Ordinal)) return R(c, HeroResultCode.Invalid, HeroText.Of("HERO_UNKNOWN_COMMAND"));
        var p = c.Tag[Prefix.Length..].Split('|');
        long N(int i) => i < p.Length && long.TryParse(p[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : long.MinValue;
        ulong U(int i) => i < p.Length && ulong.TryParse(p[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong v) ? v : ulong.MaxValue;
        string S(int i) => i < p.Length ? p[i] : "";
        var sw = SoldierWiring.For(ctx.State, ctx.Content);
        int day = (int)(c.IssuedSec / 86400);
        SoldierRow? Mine(ulong person) => sw.Book.Of(person) is { } r && r.Lord == HeroLord ? r : null;
        (bool Ok, string Key, object[] Args) res;
        switch (p[0])
        {
            case "enlist" when p.Length >= 3:
            {
                ulong who = U(1);
                if (sw.Roles.Role(S(2)) == null) { res = (false, "so_no_such_role", []); break; }
                var pt = sw.People(); int row = pt?.IndexOf(new EntityId(who)) ?? -1;
                if (row < 0 || !pt!.IsAlive(row) || pt.AgeYears(row, day) < 16) { res = (false, "so_no_such_person", []); break; }
                if (sw.Book.Rows.ContainsKey(who)) { res = (false, "so_already_soldier", []); break; }
                var cap = Retinues.CanEnlist(sw, HeroLord, who);
                if (!cap.Ok) { res = (false, "so_retinue_full", [cap.Holding, cap.Cap]); break; }
                if (!sw.Roles.Meets(S(2), s => sw.SkillOf(who, s))) { res = (false, "so_not_qualified", [sw.Roles.Role(S(2))!.Name]); break; }
                sw.Book.Rows[who] = new SoldierRow { Person = who, Role = S(2), Lord = HeroLord, PromotedDay = day };
                res = (true, "so_enlisted", [sw.Roles.Role(S(2))!.Name]);
                break;
            }
            case "fill" when p.Length >= 2 && N(1) != long.MinValue:
            {
                var soldiers = sw.SoldiersOf(HeroLord);
                if (soldiers.Count == 0) { res = (false, "so_no_soldiers", []); break; }
                var r = sw.FillFrom(N(1), soldiers, day, HeroLord);
                res = (true, "so_filled", [r.Issued, r.RefitsQueued, r.Required.Values.Sum()]);
                break;
            }
            case "equip" when p.Length >= 5 && N(4) != long.MinValue:
                res = Mine(U(1)) == null ? (false, "so_not_yours", []) : sw.Fill.Equip(sw.Book, U(1), S(2), U(3), N(4), day) ? (true, "so_equipped", [S(2)]) : (false, "so_cannot_equip", [S(2)]);
                break;
            case "strip" when p.Length >= 3:
                res = Mine(U(1)) == null ? (false, "so_not_yours", []) : sw.Fill.Strip(sw.Book, U(1), S(2)) ? (true, "so_stripped", [S(2)]) : (false, "so_nothing_there", [S(2)]);
                break;
            case "role" when p.Length >= 4:
            {
                var row = Mine(U(1));
                if (row == null) { res = (false, "so_not_yours", []); break; }
                if (sw.Roles.Role(S(2)) == null || (S(3).Length > 0 && sw.Roles.Template(S(3)) == null)) { res = (false, "so_no_such_role", []); break; }
                if (!sw.Roles.Meets(S(2), s => sw.SkillOf(row.Person, s))) { res = (false, "so_not_qualified", [sw.Roles.Role(S(2))!.Name]); break; }
                row.Role = S(2); row.Template = S(3); row.PromotedDay = day;
                res = (true, "so_role_set", [sw.Roles.Role(S(2))!.Name]);
                break;
            }
            case "discharge" when p.Length >= 2:
            {
                if (Mine(U(1)) == null) { res = (false, "so_not_yours", []); break; }
                int back = sw.Fill.Discharge(sw.Book, U(1));
                res = (true, "so_discharged", [back]);
                break;
            }
            case "promote" when p.Length >= 2:
            {
                if (Mine(U(1)) == null) { res = (false, "so_not_yours", []); break; }
                var chron = Ravensguard.Sim.Rule.RuleChron.Of(ctx.State.Chronicle, ctx.State.Ids);
                if (!Promotions.Accept(sw, U(1), day, chron, ctx.Party.Hero)) { res = (false, "so_no_promotion", []); break; }
                res = (true, "so_promoted", [sw.Roles.Role(sw.Book.Of(U(1))!.Role)!.Name]);
                break;
            }
            case "refuse" when p.Length >= 2:
                res = Mine(U(1)) != null && Promotions.Refuse(sw, U(1)) ? (true, "so_discharged_offer", []) : (false, "so_no_promotion", []);
                break;
            case "dub" when p.Length >= 2:
            {
                if (Mine(U(1)) == null) { res = (false, "so_not_yours", []); break; }
                var chron = Ravensguard.Sim.Rule.RuleChron.Of(ctx.State.Chronicle, ctx.State.Ids);
                res = Promotions.Dub(sw, U(1), day, chron, ctx.Party.Hero) ? (true, "so_dubbed", []) : (false, "so_not_dubbable", []);
                break;
            }
            case "pay":
            {
                long paid = WageRoll.PayDay(sw, HeroLord, ctx.Purse, ctx.Econ.Ledger);
                res = (true, "so_paid", [Ravensguard.Sim.Core.MoneyText.Format(paid), Ravensguard.Sim.Core.MoneyText.Format(WageRoll.Rows(sw, HeroLord).Sum(l => l.ArrearsMites))]);
                break;
            }
            default: return R(c, HeroResultCode.Invalid, HeroText.Of("HERO_MAKES_NO_SENSE"));
        }
        return R(c, res.Ok ? HeroResultCode.Ok : HeroResultCode.Refused, HeroText.Of(res.Key, res.Args));
    }
}
