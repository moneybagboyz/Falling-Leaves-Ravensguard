namespace Ravensguard.Sim.Save;

public enum SaveSlotKind { Manual, Autosave, Ironman }

/// <summary>
/// Writes immutable SaveFile snapshots on a background task. Atomic: temp file then rename; rotation never removes the current slot before the new one is in place.
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

    /// <summary>Test hook: called after rotation and just before the final atomic move that publishes the new current slot.</summary>
    public Action<string>? AfterRotate { get; set; }

    /// <summary>Raised from the background worker when a write fails (the returned task faults too). Handlers must not throw.</summary>
    public event Action<Exception>? OnError;

    public SaveWriter(string directory) { _dir = directory; Directory.CreateDirectory(directory); }

    public static string AutosavePath(string dir, int slot) => Path.Combine(dir, $"autosave_{slot}{Extension}");
    public static string IronmanPath(string dir) => Path.Combine(dir, $"ironman{Extension}");
    public static string IronmanBackupPath(string dir, int n) => Path.Combine(dir, $"ironman.bak{n}");
    public static string ManualPath(string dir, string name) => Path.Combine(dir, name + Extension);

    /// <summary>Throws ArgumentException unless <paramref name="name"/> is a bare file name (no directories, no "..").</summary>
    public static void ValidateManualName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Save name is empty.", nameof(name));
        if (name is "." or ".." || name.Contains("..") || name.IndexOfAny(['/', '\\']) >= 0 || Path.IsPathRooted(name)
            || name != Path.GetFileName(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.') || name.EndsWith(' '))
            throw new ArgumentException($"'{name}' is not a plain file name.", nameof(name));
    }

    public Task<string> WriteManualAsync(SaveFile snapshot, string name)
    {
        ValidateManualName(name);
        return Run(snapshot, () =>
        {
            var target = ManualPath(_dir, name);
            Commit(snapshot, target, null);
            return target;
        });
    }

    public Task<string> WriteAutosaveAsync(SaveFile snapshot) => Run(snapshot, () =>
    {
        var target = AutosavePath(_dir, 1);
        Commit(snapshot, target, () =>
        {
            // Older slots shift down by moving; the current slot is COPIED so it stays in place until the new file replaces it.
            for (int i = AutosaveSlots; i >= 3; i--)
            {
                var src = AutosavePath(_dir, i - 1);
                if (File.Exists(src)) File.Move(src, AutosavePath(_dir, i), overwrite: true);
            }
            if (File.Exists(target)) File.Copy(target, AutosavePath(_dir, 2), overwrite: true);
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
            if (File.Exists(target)) File.Copy(target, b1, overwrite: true);
        });
        return target;
    });

    // The snapshot is already immutable; the caller only pays for capturing it.
    private Task<string> Run(SaveFile snapshot, Func<string> work) => Task.Run(async () =>
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { return work(); }
        catch (Exception e)
        {
            try { OnError?.Invoke(e); } catch { /* a faulty handler must not mask the original failure */ }
            throw;
        }
        finally { _gate.Release(); }
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
            AfterRotate?.Invoke(tmp);
            File.Move(tmp, target, overwrite: true); // atomic publish; the previous current slot was untouched until now
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
            throw;
        }
    }
}
