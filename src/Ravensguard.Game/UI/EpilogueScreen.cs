using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Content;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Endings;
using Ravensguard.Sim.Save;

namespace Ravensguard.Game.UI;

/// <summary>
/// The end of a tale (endings spec 5, P8-EN-1): the epilogue as a scroll of panels, each paragraph written from the saved beats in the language set and linked to the chronicle entries it came from. Read-only: it
/// is what a run that took its ending shows, right after the choice and whenever the ended save is loaded. The library keeps the world; Back returns to the menu. (The text export and the library's legend cards are P8-EN-2.)
/// </summary>
public partial class EpilogueScreen : Control
{
    /// <summary>One paragraph and the chronicle entry it links to.</summary>
    public sealed record Para(string Text, EntityId? Entry);

    readonly ScreenRouter _router;
    readonly string _title, _subtitle;
    readonly IReadOnlyList<Para> _paras;
    readonly Action<EntityId>? _openEntry;

    public EpilogueScreen(ScreenRouter router, string title, string subtitle, IReadOnlyList<Para> paras, Action<EntityId>? openEntry)
    { _router = router; _title = title; _subtitle = subtitle; _paras = paras; _openEntry = openEntry; }

    /// <summary>The paragraphs of an ended world's epilogue, in the language set (the chronicle read through <paramref name="view"/>, which names the ids and the genders).</summary>
    public static List<Para> Paragraphs(WorldState st, ContentDatabase db, ChronicleView view)
    {
        var book = st.Ending ?? throw new InvalidOperationException("the world has no ending");
        var text = new EpilogueText
        {
            Tr = L.T,
            NameOf = (kind, id) => kind is ChronParamKind.Person or ChronParamKind.Place or ChronParamKind.Item or ChronParamKind.Entry ? (view.NameOf((ulong)id) is { Length: > 0 } n ? n : L.T("ep_someone")) : id.ToString(),
            GenderOf = id => view.Genders.TryGetValue((ulong)id, out var c) ? c : '\0',
            Chronicle = st.Chronicle,
            ContentName = key =>
            {
                int c = key.IndexOf(':'); string kind = key[..c], id = key[(c + 1)..];
                if (kind == "ambition" && db.TryGet<AmbitionDef>(id, out var a)) return a.Name.ToLowerInvariant();
                if (kind == "origin" && db.TryGet<OriginDef>(id, out var o)) return o.Name.ToLowerInvariant();
                return id.Replace('_', ' ');
            },
        };
        return book.Beats.Select(b => new Para(Epilogue.Render(b, text), b.Entries.Length > 0 ? new EntityId(b.Entries[0]) : null)).ToList();
    }

    /// <summary>Opens the screen over the menu for an ended world (the router's stack is the main menu, so Back returns there).</summary>
    public static void Open(ScreenRouter router, WorldState st, ContentDatabase db, ChronicleView? seen = null)
    {
        var book = st.Ending!;
        var view = seen ?? ChronicleView.Of(st, -1);
        var types = ChronicleTypes.From(db);
        string hero = view.NameOf(book.HeroPerson) is { Length: > 0 } n ? n : st.Hero?.Name ?? "";
        router.Push(new EpilogueScreen(router, L.F("ep_title", hero), L.F("ep_subtitle", L.T("ep_name_" + book.EndingId), book.Day / TimeConst.DaysPerYear),
            Paragraphs(st, db, view),
            id => router.Push(new ChronicleScreen(router, () => view, () => { }, types) { Focus = id, StartScope = ChronicleScope.MyLife })));
    }

    public override void _Ready()
    {
        var margin = UiKit.Margin(this, 20);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 8); margin.AddChild(root);
        root.AddChild(UiKit.Title(_title));
        root.AddChild(UiKit.Hint(_subtitle));
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; list.AddThemeConstantOverride("separation", 8);
        foreach (var p in _paras)
        {
            var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 4);
            box.AddChild(UiKit.Body(p.Text));
            if (p.Entry is { } id && _openEntry != null)
            {
                var link = new Button { Text = L.T("ep_chronicle"), CustomMinimumSize = new Vector2(220, 26), SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
                link.Pressed += () => _openEntry(id);
                box.AddChild(link);
            }
            list.AddChild(UiKit.Panel(box, 0, 0));
        }
        var scroll = UiKit.Scroll(list); scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.AddChild(scroll);
        root.AddChild(UiKit.Hint(L.T("ep_ended_readonly")));
        var back = UiKit.Key(L.T("ep_back"), "menu.back", 260);
        back.Pressed += _router.Pop;
        root.AddChild(back);
    }
}
