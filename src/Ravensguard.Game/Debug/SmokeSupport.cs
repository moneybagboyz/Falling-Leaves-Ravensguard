using System;
using System.Collections.Concurrent;
using Godot;

namespace Ravensguard.Game.Debug;

/// <summary>Collects engine errors (GD.PushError, script/C# exceptions logged by the engine) so --smoke can fail on them.</summary>
public partial class ErrorCapture : Logger
{
    public ConcurrentQueue<string> Errors { get; } = new();

    public override void _LogError(string function, string file, int line, string code, string rationale, bool editorNotify, int errorType, Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
    {
        // errorType 1 = warning (ERROR_TYPE_WARNING); everything else is a failure for the smoke run.
        if (errorType == 1) return;
        Errors.Enqueue($"{file}:{line} {function}: {code} {rationale}");
    }

    public override void _LogMessage(string message, bool error) { }

    /// <summary>Installs the logger and the managed unhandled-exception hooks. Dispose via Remove().</summary>
    public static ErrorCapture Install()
    {
        var c = new ErrorCapture();
        OS.AddLogger(c);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            c.Errors.Enqueue("Unhandled exception: " + e.ExceptionObject);
            GD.PrintErr("Unhandled exception: " + e.ExceptionObject);
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            c.Errors.Enqueue("Unobserved task exception: " + e.Exception);
            GD.PrintErr("Unhandled exception (task): " + e.Exception);
        };
        return c;
    }
}
