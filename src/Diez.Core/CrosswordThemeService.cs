namespace DiezPublishingStudio;

internal static class CrosswordThemeService
{
    private const string RoleKey = "crossword_role";

    public const string Required = "Obbligatoria";
    public const string Preferred = "Preferita";
    public const string Normal = "Normale";
    public const string Fallback = "Soccorso";

    public static IReadOnlyList<string> Roles { get; } = [Required, Preferred, Normal, Fallback];

    public static string GetRole(PreviewProject project, Guid wordId)
    {
        var value = project.BibleEntries.FirstOrDefault(b =>
            b.SubjectEntityId == wordId && b.IsActive &&
            string.Equals(b.Key, RoleKey, StringComparison.OrdinalIgnoreCase))?.Value;
        return NormalizeRole(value);
    }

    public static void SetRole(PreviewProject project, Guid wordId, string? role)
    {
        var normalized = NormalizeRole(role);
        var existing = project.BibleEntries.FirstOrDefault(b =>
            b.SubjectEntityId == wordId && b.IsActive &&
            string.Equals(b.Key, RoleKey, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            project.BibleEntries.Add(new BibleEntry
            {
                SubjectEntityId = wordId,
                Key = RoleKey,
                Value = normalized,
                Authority = "Binding",
                IsActive = true
            });
        }
        else
        {
            existing.Value = normalized;
            existing.Authority = "Binding";
        }
    }

    public static IReadOnlyList<GraphEntity> ByRole(PreviewProject project, string role)
    {
        var normalized = NormalizeRole(role);
        return CrosswordService.Words(project)
            .Where(w => string.Equals(GetRole(project, w.EntityId), normalized, StringComparison.Ordinal))
            .OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string DecoratedLabel(PreviewProject project, GraphEntity word)
    {
        var role = GetRole(project, word.EntityId);
        return role switch
        {
            Required => $"◆ {word.Name} · obbligatoria",
            Preferred => $"★ {word.Name} · preferita",
            Fallback => $"↳ {word.Name} · soccorso",
            _ => word.Name
        };
    }

    private static string NormalizeRole(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (string.Equals(text, Required, StringComparison.OrdinalIgnoreCase)) return Required;
        if (string.Equals(text, Preferred, StringComparison.OrdinalIgnoreCase)) return Preferred;
        if (string.Equals(text, Fallback, StringComparison.OrdinalIgnoreCase)) return Fallback;
        return Normal;
    }
}
