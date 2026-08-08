namespace DiezPublishingStudio;

internal static class IllustrationPlanService
{
    public const string BeforeHeading = "BeforeHeading";
    public const string AfterHeading = "AfterHeading";
    public const string AfterContent = "AfterContent";
    public const string FullPageAfter = "FullPageAfter";

    private static readonly HashSet<string> AllowedPositions = new(StringComparer.Ordinal)
    {
        BeforeHeading, AfterHeading, AfterContent, FullPageAfter
    };

    private static readonly HashSet<string> DocxExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp"
    };

    public static IllustrationPlanResult Upsert(
        PreviewProject project,
        Guid? placementId,
        Guid materialId,
        Guid contentId,
        string position,
        int widthPercent,
        string? caption)
    {
        project.IllustrationPlacements ??= [];

        var material = project.Materials.FirstOrDefault(m => m.MaterialId == materialId);
        if (material is null || !IsImage(material))
            return new IllustrationPlanResult(null, false, "Seleziona un materiale immagine valido.");
        if (!CanEmbedInDocx(material))
            return new IllustrationPlanResult(null, false, $"{material.FileName}: formato non supportato nel DOCX illustrato. Usa PNG, JPG/JPEG, GIF o BMP; l'originale resta comunque esportabile nello ZIP immagini.");

        var content = project.ContentNodes.FirstOrDefault(n => n.ContentId == contentId);
        if (content is null || !EditableMasterService.CanEdit(project, content))
            return new IllustrationPlanResult(null, false, "Seleziona un capitolo o una sezione modificabile del Master.");

        if (!AllowedPositions.Contains(position))
            return new IllustrationPlanResult(null, false, "Posizione illustrazione non riconosciuta.");
        if (widthPercent is < 25 or > 100)
            return new IllustrationPlanResult(null, false, "La larghezza dell'illustrazione deve essere compresa tra 25% e 100%.");

        IllustrationPlacement placement;
        var changed = false;
        if (placementId.HasValue)
        {
            placement = project.IllustrationPlacements.FirstOrDefault(p => p.PlacementId == placementId.Value)!;
            if (placement is null)
                return new IllustrationPlanResult(null, false, "La collocazione selezionata non esiste più.");

            changed = placement.MaterialId != materialId ||
                      placement.ContentId != contentId ||
                      !string.Equals(placement.Position, position, StringComparison.Ordinal) ||
                      placement.WidthPercent != widthPercent ||
                      !string.Equals(placement.Caption ?? string.Empty, caption?.Trim() ?? string.Empty, StringComparison.Ordinal);

            placement.MaterialId = materialId;
            placement.ContentId = contentId;
            placement.Position = position;
            placement.WidthPercent = widthPercent;
            placement.Caption = caption?.Trim() ?? string.Empty;
        }
        else
        {
            var nextOrdinal = project.IllustrationPlacements.Count == 0 ? 1 : project.IllustrationPlacements.Max(p => p.Ordinal) + 1;
            placement = new IllustrationPlacement
            {
                PlacementId = Guid.NewGuid(),
                MaterialId = materialId,
                ContentId = contentId,
                Position = position,
                WidthPercent = widthPercent,
                Caption = caption?.Trim() ?? string.Empty,
                Ordinal = nextOrdinal
            };
            project.IllustrationPlacements.Add(placement);
            changed = true;
        }

        return new IllustrationPlanResult(
            placement,
            changed,
            changed
                ? $"Piano illustrazioni aggiornato: {material.FileName} → {content.Title}."
                : "La collocazione selezionata non è cambiata.");
    }

    public static bool Remove(PreviewProject project, Guid placementId) =>
        project.IllustrationPlacements.RemoveAll(p => p.PlacementId == placementId) > 0;

    public static bool IsImage(MaterialEntry material) =>
        material.Kind?.StartsWith("Immagine", StringComparison.OrdinalIgnoreCase) == true;

    public static bool CanEmbedInDocx(MaterialEntry material) =>
        IsImage(material) && DocxExtensions.Contains(Path.GetExtension(material.FileName ?? string.Empty));

    public static IReadOnlyList<IllustrationPlacement> OrderedForContent(PreviewProject project, Guid contentId) =>
        project.IllustrationPlacements
            .Where(p => p.ContentId == contentId)
            .OrderBy(p => p.Ordinal)
            .ThenBy(p => p.PlacementId)
            .ToList();

    public static IReadOnlyList<string> Validate(PreviewProject project)
    {
        var errors = new List<string>();
        foreach (var placement in project.IllustrationPlacements.OrderBy(p => p.Ordinal).ThenBy(p => p.PlacementId))
        {
            var material = project.Materials.FirstOrDefault(m => m.MaterialId == placement.MaterialId);
            if (material is null || !IsImage(material))
            {
                errors.Add($"Collocazione {placement.PlacementId:N}: immagine sorgente mancante.");
                continue;
            }
            if (!material.IsEmbedded)
                errors.Add($"{material.FileName}: originale immagine non incorporato nel .diez.");
            if (!CanEmbedInDocx(material))
                errors.Add($"{material.FileName}: formato non supportato nel DOCX illustrato.");

            var content = project.ContentNodes.FirstOrDefault(n => n.ContentId == placement.ContentId);
            if (content is null || !EditableMasterService.CanEdit(project, content))
                errors.Add($"{material.FileName}: capitolo/sezione di destinazione mancante o non modificabile.");
            if (!AllowedPositions.Contains(placement.Position ?? string.Empty))
                errors.Add($"{material.FileName}: posizione illustrazione non valida.");
            if (placement.WidthPercent is < 25 or > 100)
                errors.Add($"{material.FileName}: larghezza {placement.WidthPercent}% non valida.");
        }
        return errors;
    }

    public static string PositionLabel(string position) => position switch
    {
        BeforeHeading => "Prima del titolo",
        AfterHeading => "Dopo il titolo",
        AfterContent => "Dopo il testo",
        FullPageAfter => "Pagina dedicata dopo il testo",
        _ => position
    };
}

internal readonly record struct IllustrationPlanResult(IllustrationPlacement? Placement, bool Changed, string Message);