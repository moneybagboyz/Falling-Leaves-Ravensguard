using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Game.UI;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Diagnostics;

namespace Ravensguard.Game.Diagnostics;

/// <summary>
/// F8, "Report a bug (testers)": a category, what happened, what was expected, "did you understand why?", the list of files with their sizes, and Save report.
/// The report is a zip written on a worker to <c>user://reports</c>; nothing is sent. Pushed on the router like a screen, so the screen below stops (the sim pauses) until it closes.
/// The screenshot was taken before this screen opened.
/// </summary>
public partial class BugReportScreen : Control
{
    static readonly string[] Categories = ["Bug", "Confusing", "I did not understand why", "Balance", "Idea"];
    readonly ScreenRouter _router;
    readonly byte[]? _shot;
    OptionButton _category = null!;
    TextEdit _happened = null!, _expected = null!;
    CheckBox _yes = null!, _partly = null!, _no = null!, _incShot = null!, _incLog = null!, _incSave = null!, _agree = null!;
    Label _files = null!, _status = null!;
    Button _save = null!, _open = null!, _cancel = null!;
    Task<ReportResult>? _task;
    string? _resultPath;

    /// <summary>True while a report screen is on the router (F8 again does nothing).</summary>
    public static bool IsOpen { get; private set; }

    BugReportScreen(ScreenRouter router, byte[]? shot) { _router = router; _shot = shot; }

    /// <summary>Opens the report screen. <paramref name="shot"/> is the picture taken before it opened (null: none).</summary>
    public static BugReportScreen? Open(ScreenRouter router, byte[]? shot)
    {
        if (IsOpen) return null;
        if (router.HasModal) router.CloseModal();
        var s = new BugReportScreen(router, shot);
        router.Push(s);
        GameDiagnostics.Info("report", "F8 report opened", "screenshot=" + (shot != null));
        return s;
    }

    /// <summary>Closes the screen (the smoke test and Esc use this).</summary>
    public void Close() { if (IsOpen && GodotObject.IsInstanceValid(this) && _router.Top == this) _router.Pop(); }

    public override void _EnterTree() => IsOpen = true;
    public override void _ExitTree() => IsOpen = false;

    public override void _Ready()
    {
        var m = UiKit.Margin(this, 32);
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        m.AddChild(scroll);
        var v = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        v.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(v);
        v.AddChild(UiKit.Title(L.T("REPORT_TITLE")));
        v.AddChild(UiKit.Hint(L.T("REPORT_LOCAL_ONLY"), true));

        var row = new HBoxContainer(); v.AddChild(row);
        row.AddChild(new Label { Text = L.T("REPORT_CATEGORY"), SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _category = new OptionButton();
        foreach (var c in Categories) _category.AddItem(c);
        row.AddChild(_category);

        v.AddChild(new Label { Text = L.T("REPORT_HAPPENED") });
        _happened = Box(); v.AddChild(_happened);
        v.AddChild(new Label { Text = L.T("REPORT_EXPECTED") });
        _expected = Box(); v.AddChild(_expected);

        v.AddChild(new Label { Text = L.T("REPORT_UNDERSTOOD") });
        var radio = new ButtonGroup(); var r = new HBoxContainer(); v.AddChild(r);
        _yes = Radio(L.T("REPORT_YES"), radio, r); _partly = Radio(L.T("REPORT_PARTLY"), radio, r); _no = Radio(L.T("REPORT_NO"), radio, r);

        var c0 = GameDiagnostics.Consent.Current;
        _incShot = Check(L.T("REPORT_INC_SCREENSHOT"), _shot != null && (!c0.Accepted || c0.IncludeScreenshot), v); _incShot.Disabled = _shot == null;
        _incLog = Check(L.T("REPORT_INC_LOG"), !c0.Accepted || c0.IncludeLog, v);
        _incSave = Check(L.T("REPORT_INC_AUTOSAVE"), !c0.Accepted || c0.IncludeSave, v);
        foreach (var cb in new[] { _incShot, _incLog, _incSave }) cb.Toggled += _ => RefreshFiles();

        _files = new Label { ThemeTypeVariation = "HintLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        v.AddChild(_files);

        if (!GameDiagnostics.Consent.IsGranted)
        {
            v.AddChild(new Label { Text = L.T("REPORT_CONSENT_TITLE"), ThemeTypeVariation = "TitleLabel" });
            v.AddChild(UiKit.Body(string.Join("\n", ConsentText.Contents.Select(c => "- " + c)) + "\n\n" + ConsentText.Statement));
            _agree = Check(L.T("REPORT_CONSENT_AGREE"), false, v);
            _agree.Toggled += _ => RefreshButtons();
        }

        _status = new Label { ThemeTypeVariation = "HintLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        v.AddChild(_status);
        var buttons = new HBoxContainer(); v.AddChild(buttons);
        _save = new Button { Text = L.T("REPORT_SAVE") }; _save.Pressed += Save; buttons.AddChild(_save);
        _open = new Button { Text = L.T("REPORT_OPEN_FOLDER"), Visible = false }; _open.Pressed += () => OS.ShellOpen(ProjectSettings.GlobalizePath("user://reports")); buttons.AddChild(_open);
        _cancel = new Button { Text = L.T("REPORT_CANCEL") }; _cancel.Pressed += Close; buttons.AddChild(_cancel);
        RefreshFiles(); RefreshButtons();
        _happened.GrabFocus();
    }

    static TextEdit Box()
    {
        var t = new TextEdit { CustomMinimumSize = new Vector2(0, 72), WrapMode = TextEdit.LineWrappingMode.Boundary, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        t.TextChanged += () => { if (t.Text.Length > 2000) { int l = t.GetCaretLine(); t.Text = t.Text[..2000]; t.SetCaretLine(l); } };
        return t;
    }

    static CheckBox Radio(string text, ButtonGroup g, Control parent) { var c = new CheckBox { Text = text, ButtonGroup = g }; parent.AddChild(c); return c; }
    static CheckBox Check(string text, bool on, Control parent) { var c = new CheckBox { Text = text, ButtonPressed = on }; parent.AddChild(c); return c; }

    string Understood => _yes.ButtonPressed ? "yes" : _partly.ButtonPressed ? "partly" : _no.ButtonPressed ? "no" : "";

    ReportRequest Request() => GameDiagnostics.BuildRequest(ReportKind.Bug, null, _incShot.ButtonPressed ? _shot : null, Categories[Math.Max(0, _category.Selected)],
        _happened.Text, _expected.Text, Understood, _incSave.ButtonPressed, _incLog.ButtonPressed);

    void RefreshFiles()
    {
        try
        {
            var req = Request();
            var list = GameDiagnostics.Reports.Preview(new ReportRequest
            {
                Kind = req.Kind, Session = req.Session, LogPath = req.LogPath, IncludeLog = _incLog.ButtonPressed, AutosavePath = req.AutosavePath, IncludeAutosave = _incSave.ButtonPressed,
                Screenshot = req.Screenshot, ReplayTrace = req.ReplayTrace, HashesCsv = req.HashesCsv, SettingsJson = req.SettingsJson, ContentManifestJson = req.ContentManifestJson,
            });
            long total = list.Sum(f => f.Bytes);
            _files.Text = L.T("REPORT_FILES") + " " + string.Join(", ", list.Select(f => $"{f.Name} {Bytes(f.Bytes)}")) + $"  ({L.T("REPORT_TOTAL")} {Bytes(total)})";
        }
        catch (Exception ex) { _files.Text = ex.Message; }
    }

    static string Bytes(long b) => b >= 1 << 20 ? $"{b / (1024.0 * 1024):F1} MB" : b >= 1024 ? $"{b / 1024.0:F0} KB" : $"{b} B";

    void RefreshButtons() { if (_task == null) _save.Disabled = _agree != null && !_agree.ButtonPressed; }

    void Save()
    {
        if (_task != null) return;
        if (_agree != null && !_agree.ButtonPressed) return;
        if (!GameDiagnostics.Consent.IsGranted) GameDiagnostics.Consent.Accept(_incSave.ButtonPressed, _incShot.ButtonPressed, _incLog.ButtonPressed);
        var req = Request();
        _save.Disabled = true; _status.Text = L.T("REPORT_WRITING");
        GameDiagnostics.Info("report", "F8 report saving", "category=" + req.Category);
        var builder = GameDiagnostics.Reports;
        _task = Task.Run(() => builder.Write(req));
    }

    public override void _Process(double delta)
    {
        if (_task is not { IsCompleted: true } t) return;
        _task = null;
        if (t.IsFaulted) { _status.Text = L.F("REPORT_FAILED", GameDiagnostics.Redactor.Redact(t.Exception!.GetBaseException().Message)); _save.Disabled = false; GameDiagnostics.Warn("report", "report failed: " + t.Exception.GetBaseException().Message); return; }
        _resultPath = t.Result.Path;
        _status.Text = L.F("REPORT_SAVED", GameDiagnostics.Redactor.Redact(_resultPath)) + (t.Result.Notes.Count > 0 ? "\n" + string.Join("\n", t.Result.Notes) : "");
        _open.Visible = true; _cancel.Text = L.T("REPORT_CLOSE");
        GameDiagnostics.Info("report", "F8 report saved", "bytes=" + new FileInfo(_resultPath).Length);
    }

    /// <summary>The path of the zip written by this screen (null until saved); for the self-test.</summary>
    public string? ResultPath => _resultPath;
}
