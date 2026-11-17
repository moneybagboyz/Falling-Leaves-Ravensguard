namespace Ravensguard.Sim.Save;

public enum SaveSlotKind { Manual, Autosave, Ironman }

/// <summary>
/// Writes immutable SaveFile snapshots on a background task. Atomic: temp file then rename.
/// Autosave: 3 rotating slots (autosave_1 newest). Ironman: ironman.sav + ironman.bak1/bak2.
/// </summary>
public sealed class SaveWriter
{
    public const int AutosaveSlots = 3;
    public const string Extension = ".sav";

    private readonly string _dir;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Test hook: called after the temp file is fully written and before it is moved into place.</summary>
    public Action<string>? BeforeCommit { get; set; }

    public SaveWriter(string directory) { _dir = directory; Directory.CreateDirectory(directory); }

    public static string AutosavePath(string dir, int slot) => Path.Combine(dir, $"autosave_{slot}{Extension}");
    public static string IronmanPath(string dir) => Path.Combine(dir, $"ironman{Extension}");
    public static string IronmanBackupPath(string dir, int n) => Path.Combine(dir, $"ironman.bak{n}");
    public static string ManualPath(string dir, string name) => Path.Combine(dir, name + Extension);

    public Task<string> WriteManualAsync(SaveFile snapshot, string name) => Run(snapshot, () =>
    {
        var target = ManualPath(_dir, name);
        Commit(snapshot, target, null);
        return target;
    });

    public Task<string> WriteAutosaveAsync(SaveFile snapshot) => Run(snapshot, () =>
    {
        var target = AutosavePath(_dir, 1);
        Commit(snapshot, target, () =>
        {
            for (int i = AutosaveSlots; i >= 2; i--)
            {
                var src = AutosavePath(_dir, i - 1);
                if (File.Exists(src)) File.Move(src, AutosavePath(_dir, i), overwrite: true);
            }
        });
        return target;
    });

    public Task<string> WriteIronmanAsync(SaveFile snapshot) => Run(snapshot, () =>
    {
        var target = IronmanPath(_dir);
        Commit(snapshot, target, () =>
        {
            var b1 = IronmanBackupPath(_dir, 1);
            if (File.Exists(b1)) File.Move(b1, IronmanBackupPath(_dir, 2), overwrite: true);
            if (File.Exists(target)) File.Move(target, b1, overwrite: true);
        });
        return target;
    });

    // The snapshot is already immutable; the caller only pays for capturing it.
    private Task<string> Run(SaveFile snapshot, Func<string> work) => Task.Run(async () =>
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { return work(); } finally { _gate.Release(); }
    });

    private void Commit(SaveFile snapshot, string target, Action? rotate)
    {
        var bytes = SaveCodec.Encode(snapshot);
        var tmp = target + ".tmp";
        try
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes);
                fs.Flush(flushToDisk: true);
            }
            BeforeCommit?.Invoke(tmp);
            rotate?.Invoke();
            File.Move(tmp, target, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
            throw;
        }
    }
}
