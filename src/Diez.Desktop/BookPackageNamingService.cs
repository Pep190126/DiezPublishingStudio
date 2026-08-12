using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DiezPublishingStudio;

internal static class BookPackageNamingService
{
    private const string EntityKind = "DiezPackageNamingState";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class NamingState
    {
        public int LastPromptPackVersion { get; set; }
    }

    public static string BookTitle(PreviewProject project)
    {
        var title = (project.EditionMetadata?.Title ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(title)) return title;
        var projectName = (project.Name ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(projectName) && !string.Equals(projectName, "Nuovo progetto", StringComparison.OrdinalIgnoreCase))
            return projectName;
        return "book";
    }

    public static int PeekNextVersion(PreviewProject project) => Math.Max(1, Load(project).LastPromptPackVersion + 1);

    public static void CommitVersion(PreviewProject project, int version)
    {
        var state = Load(project);
        if (version <= state.LastPromptPackVersion) return;
        state.LastPromptPackVersion = version;
        Save(project, state);
    }

    public static string PromptPackFileName(PreviewProject project, int version) =>
        $"diez-{Slug(BookTitle(project))}-prompt-pack-v{Math.Max(1, version):D3}.zip";

    public static string ResponseFileName(PreviewProject project, int version) =>
        $"diez-{Slug(BookTitle(project))}-response-v{Math.Max(1, version):D3}.zip";

    public static string ResponsePartFileName(PreviewProject project, int version, int part) =>
        $"diez-{Slug(BookTitle(project))}-response-v{Math.Max(1, version):D3}-part-{Math.Max(1, part):D3}.zip";

    internal static string Slug(string? value)
    {
        var source = (value ?? string.Empty).Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        var dash = false;
        foreach (var ch in source)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
                dash = false;
                continue;
            }
            if (sb.Length > 0 && !dash)
            {
                sb.Append('-');
                dash = true;
            }
        }
        var slug = sb.ToString().Trim('-');
        if (slug.Length == 0) slug = "book";
        return slug.Length <= 72 ? slug : slug[..72].TrimEnd('-');
    }

    private static NamingState Load(PreviewProject project)
    {
        var entity = project.Entities.FirstOrDefault(e => string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null || string.IsNullOrWhiteSpace(entity.Notes)) return new NamingState();
        try { return JsonSerializer.Deserialize<NamingState>(entity.Notes, JsonOptions) ?? new NamingState(); }
        catch { return new NamingState(); }
    }

    private static void Save(PreviewProject project, NamingState state)
    {
        var entity = project.Entities.FirstOrDefault(e => string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
        {
            entity = new GraphEntity { Kind = EntityKind, Name = "Package naming state", IsCandidate = false };
            project.Entities.Add(entity);
        }
        entity.IsCandidate = false;
        entity.Notes = JsonSerializer.Serialize(state, JsonOptions);
    }
}
