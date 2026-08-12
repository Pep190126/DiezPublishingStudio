using System.Text.Json;

namespace DiezPublishingStudio;

internal sealed class CustomStyleLibraryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("D");
    public string Label { get; set; } = string.Empty;
    public string Definition { get; set; } = string.Empty;
}

/// <summary>
/// Local reusable custom-style library. A custom style is always persisted in its current project;
/// it is copied to this cross-project library only after the user explicitly opts in.
/// </summary>
internal static class CustomStyleLibraryService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<CustomStyleLibraryEntry> Load()
    {
        try
        {
            var path = LibraryPath();
            if (!File.Exists(path)) return [];
            var list = JsonSerializer.Deserialize<List<CustomStyleLibraryEntry>>(File.ReadAllText(path), JsonOptions) ?? [];
            return list
                .Where(x => !string.IsNullOrWhiteSpace(x.Definition))
                .GroupBy(x => x.Definition.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => Normalize(g.First()))
                .OrderBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch { return []; }
    }

    public static CustomStyleLibraryEntry Add(string definition)
    {
        var clean = (definition ?? string.Empty).Trim();
        if (clean.Length == 0) throw new InvalidOperationException("Lo stile Custom non può essere vuoto.");
        var list = Load().ToList();
        var existing = list.FirstOrDefault(x => string.Equals(x.Definition.Trim(), clean, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;
        var entry = new CustomStyleLibraryEntry
        {
            Definition = clean,
            Label = MakeLabel(clean)
        };
        list.Add(entry);
        Save(list);
        return entry;
    }

    public static bool TryResolve(string? label, out string definition)
    {
        var clean = (label ?? string.Empty).Trim();
        var entry = Load().FirstOrDefault(x => string.Equals(x.Label, clean, StringComparison.OrdinalIgnoreCase));
        definition = entry?.Definition ?? string.Empty;
        return entry is not null;
    }

    public static IReadOnlyList<string> SelectableLabels() => Load().Select(x => x.Label).ToList();

    private static void Save(IReadOnlyCollection<CustomStyleLibraryEntry> entries)
    {
        var path = LibraryPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(entries, JsonOptions));
    }

    private static CustomStyleLibraryEntry Normalize(CustomStyleLibraryEntry entry)
    {
        entry.Id = string.IsNullOrWhiteSpace(entry.Id) ? Guid.NewGuid().ToString("D") : entry.Id;
        entry.Definition = (entry.Definition ?? string.Empty).Trim();
        entry.Label = string.IsNullOrWhiteSpace(entry.Label) ? MakeLabel(entry.Definition) : entry.Label.Trim();
        return entry;
    }

    private static string MakeLabel(string definition)
    {
        var single = string.Join(" ", definition.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (single.Length > 52) single = single[..49].TrimEnd() + "…";
        return "Custom — " + single;
    }

    private static string LibraryPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DiezPublishingStudio",
        "custom-styles.json");
}
