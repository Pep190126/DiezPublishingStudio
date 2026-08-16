using System.Collections.Concurrent;

namespace DiezPublishingStudio;

/// <summary>
/// Serializes UI-driven project saves for the same .diez path. Event handlers must never
/// launch overlapping atomic package replacements, and I/O failures must never escape an
/// async-void Avalonia event handler.
/// </summary>
internal static class SafeProjectAutosave
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates =
        new(StringComparer.OrdinalIgnoreCase);

    public static async Task<bool> RunAsync(string path, Func<Task> operation, string source)
    {
        var key = Normalize(path);
        var gate = Gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            await operation();
            return true;
        }
        catch (Exception ex)
        {
            CrashDiagnostics.Error("autosave:" + source, ex);
            return false;
        }
        finally
        {
            gate.Release();
        }
    }

    public static Task<bool> SaveAsync(string path, PreviewProject project, string source) =>
        RunAsync(path, () => ProjectFileStore.SaveAsync(path, project), source);

    private static string Normalize(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return path ?? string.Empty; }
    }
}
