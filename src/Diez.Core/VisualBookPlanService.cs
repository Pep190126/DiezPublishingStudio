using System.Text.Json;

namespace DiezPublishingStudio;

internal sealed class VisualBookPlan
{
    public int SchemaVersion { get; set; } = 1;
    public int ImageCount { get; set; } = 1;
    public bool Consistent { get; set; }
    public string UpdatedAtLocal { get; set; } = string.Empty;
}

/// <summary>
/// Canonical production plan for books whose primary deliverables are images.
/// Quantity and Consistent belong to the book, not to a particular frontend.
/// </summary>
internal static class VisualBookPlanService
{
    internal const string EntityKind = "DiezVisualBookPlan";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static VisualBookPlan Load(PreviewProject project)
    {
        var entity = project.Entities.FirstOrDefault(e => string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null || string.IsNullOrWhiteSpace(entity.Notes))
        {
            var inferred = project.AiProductionJobs.Count(j => string.Equals(j.OutputType, AiProductionService.TypeImage, StringComparison.OrdinalIgnoreCase));
            return new VisualBookPlan
            {
                ImageCount = Math.Clamp(inferred > 0 ? inferred : 1, 1, 500),
                Consistent = !string.IsNullOrWhiteSpace(ImageCollectionWorkspaceService.GetConsistencyRules(project))
            };
        }

        try
        {
            var plan = JsonSerializer.Deserialize<VisualBookPlan>(entity.Notes, JsonOptions) ?? new VisualBookPlan();
            plan.ImageCount = Math.Clamp(plan.ImageCount, 1, 500);
            return plan;
        }
        catch
        {
            return new VisualBookPlan();
        }
    }

    public static void Save(PreviewProject project, int imageCount, bool consistent)
    {
        var entity = project.Entities.FirstOrDefault(e => string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
        {
            entity = new GraphEntity
            {
                Kind = EntityKind,
                Name = "Piano produzione immagini",
                IsCandidate = false
            };
            project.Entities.Add(entity);
        }

        entity.IsCandidate = false;
        entity.Notes = JsonSerializer.Serialize(new VisualBookPlan
        {
            ImageCount = Math.Clamp(imageCount, 1, 500),
            Consistent = consistent,
            UpdatedAtLocal = DateTimeOffset.Now.ToString("O")
        }, JsonOptions);
    }

    public static bool IsVisualFamily(PreviewProject project) => BookTypeCatalog.IsVisual(BookTypeProfileService.Get(project));

    public static bool IsImageOnlyFamily(PreviewProject project)
    {
        var type = BookTypeProfileService.Get(project);
        return string.Equals(type, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(type, BookTypeProfileService.ImageCollection, StringComparison.OrdinalIgnoreCase);
    }

    public static List<AiProductionJob> AppliedImageJobs(PreviewProject project) => project.AiProductionJobs
        .Where(j => string.Equals(j.OutputType, AiProductionService.TypeImage, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(j.Status, AiProductionService.StatusApplied, StringComparison.Ordinal) &&
                    j.ResultMaterialId.HasValue)
        .OrderBy(j => Number(j.Code))
        .ThenBy(j => j.Code, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public static List<MaterialEntry> AppliedImageMaterials(PreviewProject project)
    {
        var ids = AppliedImageJobs(project)
            .Select(j => j.ResultMaterialId!.Value)
            .Distinct()
            .ToHashSet();
        return project.Materials
            .Where(m => ids.Contains(m.MaterialId) && IllustrationPlanService.IsImage(m))
            .ToList();
    }

    public static IReadOnlyList<string> ProductionProblems(PreviewProject project)
    {
        if (!IsVisualFamily(project)) return [];
        var plan = Load(project);
        var jobs = project.AiProductionJobs
            .Where(j => string.Equals(j.OutputType, AiProductionService.TypeImage, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var applied = AppliedImageJobs(project);
        var materials = AppliedImageMaterials(project);
        var issues = new List<string>();

        if (jobs.Count != plan.ImageCount)
            issues.Add($"Il piano richiede {plan.ImageCount} immagini ma esistono {jobs.Count} job immagine.");
        if (applied.Count != plan.ImageCount)
            issues.Add($"Il piano richiede {plan.ImageCount} immagini applicate al libro ma ne risultano {applied.Count}.");
        if (materials.Count != plan.ImageCount)
            issues.Add($"Le immagini applicate devono avere {plan.ImageCount} file distinti e non duplicati; ne risultano {materials.Count}.");

        var duplicateHashes = materials
            .Where(m => !string.IsNullOrWhiteSpace(m.Sha256))
            .GroupBy(m => m.Sha256, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();
        if (duplicateHashes.Count > 0)
            issues.Add($"Sono presenti {duplicateHashes.Count} gruppi di immagini duplicate identiche nel libro.");

        return issues;
    }

    private static int Number(string? code)
    {
        var digits = new string((code ?? string.Empty).Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var value) ? value : int.MaxValue;
    }
}
