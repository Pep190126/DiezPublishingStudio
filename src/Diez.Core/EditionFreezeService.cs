using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DiezPublishingStudio;

internal static class EditionFreezeService
{
    public const string FreezeKey = "edition_freeze";
    private const string ManualEditKey = "manual_edit";

    public static EditionFreezeResult CreateFreeze(PreviewProject project, string? note = null)
    {
        project.RevisionCandidates ??= [];
        var editable = project.ContentNodes.Where(n => EditableMasterService.CanEdit(project, n)).ToList();
        var imageOnly = VisualBookPlanService.IsImageOnlyFamily(project);
        var visualProblems = imageOnly ? VisualBookPlanService.ProductionProblems(project) : [];
        if (editable.Count == 0 && (!imageOnly || visualProblems.Count > 0))
        {
            var reason = imageOnly && visualProblems.Count > 0
                ? "Edition Freeze non creato: il libro con immagini non è completo. " + string.Join(" ", visualProblems.Take(3))
                : "Edition Freeze non creato: il Master non contiene ancora capitoli o sezioni modificabili.";
            return new EditionFreezeResult(null, reason);
        }

        var snapshot = BuildCanonicalSnapshot(project);
        var hash = Hash(snapshot);
        var latest = GetLatestFreeze(project);
        if (latest is not null && string.Equals(latest.BaseContentSha256, hash, StringComparison.Ordinal))
            return new EditionFreezeResult(latest, $"Il progetto coincide già con Edition Freeze #{FreezeSequence(latest)}.");

        var sequence = project.RevisionCandidates.Count(c => c.Key == FreezeKey) + 1;
        var now = DateTimeOffset.Now.ToString("O");
        var freeze = new RevisionCandidate
        {
            CandidateId = Guid.NewGuid(),
            IssueId = Guid.Empty,
            IssueSignature = $"FREEZE:{sequence:D4}:{hash[..16]}",
            SubjectEntityId = Guid.Empty,
            ContentId = Guid.Empty,
            Key = FreezeKey,
            OriginalValue = hash,
            ProposedValue = sequence.ToString(),
            OriginalBody = string.Empty,
            ProposedBody = snapshot,
            BaseContentSha256 = hash,
            Rationale = string.IsNullOrWhiteSpace(note)
                ? $"Edition Freeze #{sequence}: snapshot immutabile di metadati, Master, asset visuali finali, piano illustrazioni e Bible prima del preflight."
                : note.Trim(),
            Status = "Applied",
            CreatedAtLocal = now,
            ApprovedAtLocal = now,
            AppliedAtLocal = now
        };
        project.RevisionCandidates.Add(freeze);
        return new EditionFreezeResult(freeze, $"Edition Freeze #{sequence} creato. Modifiche successive a metadati, Master, asset visuali, piano illustrazioni o Bible renderanno questo freeze non corrente.");
    }

    public static RevisionCandidate? GetLatestFreeze(PreviewProject project) =>
        project.RevisionCandidates
            .Where(c => c.Key == FreezeKey && c.Status == "Applied")
            .OrderByDescending(FreezeSequence)
            .ThenByDescending(c => c.CreatedAtLocal, StringComparer.Ordinal)
            .FirstOrDefault();

    public static int FreezeCount(PreviewProject project) =>
        project.RevisionCandidates.Count(c => c.Key == FreezeKey && c.Status == "Applied");

    public static bool IsLatestFreezeCurrent(PreviewProject project)
    {
        var freeze = GetLatestFreeze(project);
        return freeze is not null && string.Equals(freeze.BaseContentSha256, Hash(BuildCanonicalSnapshot(project)), StringComparison.Ordinal);
    }

    public static PreflightResult RunPreflight(PreviewProject project)
    {
        ConsistencyEngine.Rebuild(project);
        var freeze = GetLatestFreeze(project);
        var checks = new List<PreflightCheck>();
        var metadata = project.EditionMetadata ?? new EditionMetadata();
        var visualFamily = VisualBookPlanService.IsVisualFamily(project);
        var imageOnly = VisualBookPlanService.IsImageOnlyFamily(project);
        var visualProblems = visualFamily ? VisualBookPlanService.ProductionProblems(project).ToList() : [];

        checks.Add(new PreflightCheck(
            "FREEZE_EXISTS",
            "Error",
            freeze is not null,
            freeze is null ? "Manca un Edition Freeze del progetto editoriale." : $"Edition Freeze #{FreezeSequence(freeze)} disponibile."));

        var freezeCurrent = freeze is not null && IsLatestFreezeCurrent(project);
        checks.Add(new PreflightCheck(
            "FREEZE_CURRENT",
            "Error",
            freezeCurrent,
            freeze is null
                ? "Impossibile verificare il progetto senza Edition Freeze."
                : freezeCurrent ? "Metadati, Master, asset visuali, piano illustrazioni e Bible coincidono con l'ultimo Edition Freeze." : "Il progetto editoriale è cambiato dopo l'ultimo Edition Freeze: crea un nuovo freeze."));

        checks.Add(new PreflightCheck(
            "EDITION_TITLE",
            "Error",
            !string.IsNullOrWhiteSpace(metadata.Title),
            string.IsNullOrWhiteSpace(metadata.Title) ? "Manca il titolo dell'edizione." : $"Titolo edizione: {metadata.Title}."));

        checks.Add(new PreflightCheck(
            "EDITION_LANGUAGE",
            "Error",
            !string.IsNullOrWhiteSpace(metadata.Language),
            string.IsNullOrWhiteSpace(metadata.Language) ? "Manca la lingua dell'edizione." : $"Lingua edizione: {metadata.Language}."));

        var isbnValid = string.IsNullOrWhiteSpace(metadata.Isbn) || EditionMetadataService.IsValidIsbn(metadata.Isbn);
        checks.Add(new PreflightCheck(
            "ISBN_VALID",
            "Error",
            isbnValid,
            isbnValid
                ? string.IsNullOrWhiteSpace(metadata.Isbn) ? "ISBN non indicato: campo opzionale." : $"ISBN valido: {metadata.Isbn}."
                : "L'ISBN indicato non è valido."));

        checks.Add(new PreflightCheck(
            "EDITION_CREATOR",
            "Warning",
            !string.IsNullOrWhiteSpace(metadata.Creator),
            string.IsNullOrWhiteSpace(metadata.Creator) ? "Autore/creatore non indicato." : $"Autore/creatore: {metadata.Creator}."));

        var editable = project.ContentNodes.Where(n => EditableMasterService.CanEdit(project, n)).ToList();
        var contentPresent = imageOnly ? visualProblems.Count == 0 : editable.Count > 0;
        checks.Add(new PreflightCheck(
            "CONTENT_PRESENT",
            "Error",
            contentPresent,
            imageOnly
                ? contentPresent
                    ? $"Libro visuale completo: {VisualBookPlanService.AppliedImageJobs(project).Count} immagini finali applicate."
                    : "Il libro visuale non contiene ancora l'insieme completo di immagini finali applicate."
                : editable.Count > 0 ? $"{editable.Count} contenuti editoriali modificabili presenti." : "Nessun capitolo o sezione editoriale da pubblicare."));

        var empty = editable.Where(n => string.IsNullOrWhiteSpace(n.Body)).ToList();
        checks.Add(new PreflightCheck(
            "NO_EMPTY_CONTENT",
            "Error",
            imageOnly || empty.Count == 0,
            imageOnly ? "Il libro visuale non richiede contenuti testuali nel Master." : empty.Count == 0 ? "Nessun contenuto editoriale vuoto." : $"{empty.Count} contenuti editoriali sono vuoti."));

        if (visualFamily)
        {
            checks.Add(new PreflightCheck(
                "VISUAL_BOOK_COMPLETE",
                "Error",
                visualProblems.Count == 0,
                visualProblems.Count == 0
                    ? $"Percorso immagini completo: {VisualBookPlanService.Load(project).ImageCount} immagini pianificate, approvate e applicate senza duplicati."
                    : "Percorso immagini incompleto: " + string.Join(" ", visualProblems.Take(3))));
        }

        var notEmbedded = project.Materials.Where(m => !m.IsEmbedded).ToList();
        checks.Add(new PreflightCheck(
            "MATERIALS_EMBEDDED",
            "Error",
            project.Materials.Count > 0 && notEmbedded.Count == 0,
            project.Materials.Count == 0
                ? "Nessun materiale sorgente incorporato nel progetto."
                : notEmbedded.Count == 0 ? "Tutti i materiali sorgente risultano incorporati nel .diez." : $"{notEmbedded.Count} materiali non risultano incorporati nel .diez."));

        var illustrationErrors = IllustrationPlanService.Validate(project);
        checks.Add(new PreflightCheck(
            "ILLUSTRATION_PLAN_VALID",
            "Error",
            illustrationErrors.Count == 0,
            illustrationErrors.Count == 0
                ? $"Piano illustrazioni valido: {project.IllustrationPlacements.Count} collocazioni."
                : $"Piano illustrazioni non valido: {string.Join("; ", illustrationErrors.Take(3))}"));

        var imageMaterials = project.Materials.Where(IllustrationPlanService.IsImage).ToList();
        var placedMaterialIds = project.IllustrationPlacements.Select(p => p.MaterialId).ToHashSet();
        var unplacedImages = imageMaterials.Count(m => !placedMaterialIds.Contains(m.MaterialId));
        checks.Add(new PreflightCheck(
            "ILLUSTRATIONS_REVIEW",
            "Warning",
            imageOnly || unplacedImages == 0,
            imageOnly
                ? "Coloring/Raccolta immagini: gli asset finali appartengono alla raccolta del libro e non richiedono una collocazione DOCX."
                : imageMaterials.Count == 0
                    ? "Nessuna immagine editoriale nel progetto."
                    : unplacedImages == 0 ? "Tutte le immagini del progetto hanno almeno una collocazione DOCX." : $"{unplacedImages} immagini non hanno una collocazione DOCX; possono essere asset, copertine o immagini destinate solo allo ZIP."));

        var activeProposals = project.RevisionCandidates.Count(c =>
            c.Key != ManualEditKey && c.Key != FreezeKey && c.Status is "Proposed" or "Approved");
        checks.Add(new PreflightCheck(
            "NO_ACTIVE_PROPOSALS",
            "Error",
            activeProposals == 0,
            activeProposals == 0 ? "Nessuna proposta di revisione in attesa." : $"{activeProposals} proposte di revisione sono ancora in attesa di decisione o applicazione."));

        var blockingIssues = project.ConsistencyIssues.Where(i =>
            string.Equals(i.Status, "Open", StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(i.Severity, "Critical", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(i.Severity, "Error", StringComparison.OrdinalIgnoreCase))).ToList();
        checks.Add(new PreflightCheck(
            "NO_BLOCKING_CONSISTENCY",
            "Error",
            blockingIssues.Count == 0,
            blockingIssues.Count == 0 ? "Nessun problema di coerenza critico o errore aperto." : $"{blockingIssues.Count} problemi di coerenza bloccanti sono ancora aperti."));

        var candidateEntities = project.Entities.Count(e => e.IsCandidate);
        checks.Add(new PreflightCheck(
            "ENTITY_REVIEW",
            "Warning",
            candidateEntities == 0,
            candidateEntities == 0 ? "Tutte le entità rilevate sono state decise." : $"{candidateEntities} entità sono ancora candidate; non bloccano il preflight ma richiedono attenzione editoriale."));

        var ready = checks.Where(c => c.Severity == "Error").All(c => c.Passed);
        return new PreflightResult(ready, freeze?.CandidateId, checks);
    }

    public static string BuildCanonicalSnapshot(PreviewProject project)
    {
        var metadata = project.EditionMetadata ?? new EditionMetadata();
        var freezeMetadata = new FreezeMetadata(
            metadata.Title ?? string.Empty,
            metadata.Subtitle ?? string.Empty,
            metadata.Creator ?? string.Empty,
            metadata.Language ?? string.Empty,
            metadata.Publisher ?? string.Empty,
            metadata.Isbn ?? string.Empty,
            metadata.Description ?? string.Empty);

        var orderedContent = project.ContentNodes
            .OrderBy(n => MaterialOrder(project, n.MaterialId))
            .ThenBy(n => n.Ordinal)
            .ThenBy(n => n.ContentId)
            .Select(n => new FreezeContent(
                n.ContentId,
                n.MaterialId,
                n.ParentId,
                n.Kind ?? string.Empty,
                n.Title ?? string.Empty,
                n.SourceLocator ?? string.Empty,
                n.Ordinal,
                n.Body ?? string.Empty))
            .ToList();

        var illustrations = project.IllustrationPlacements
            .OrderBy(p => p.Ordinal)
            .ThenBy(p => p.PlacementId)
            .Select(p => new FreezeIllustration(
                p.PlacementId,
                p.MaterialId,
                p.ContentId,
                p.Position ?? string.Empty,
                p.WidthPercent,
                p.Caption ?? string.Empty,
                p.Ordinal))
            .ToList();

        var bible = project.BibleEntries
            .Where(b => b.IsActive)
            .OrderBy(b => b.SubjectEntityId)
            .ThenBy(b => b.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(b => b.Value, StringComparer.OrdinalIgnoreCase)
            .Select(b => new FreezeBible(b.SubjectEntityId, b.Key ?? string.Empty, b.Value ?? string.Empty, b.Authority ?? string.Empty))
            .ToList();

        var visual = BuildVisualFreeze(project);
        return JsonSerializer.Serialize(new FreezeSnapshot(project.ProjectId, freezeMetadata, orderedContent, illustrations, bible, visual));
    }

    private static FreezeVisualPlan? BuildVisualFreeze(PreviewProject project)
    {
        if (!VisualBookPlanService.IsVisualFamily(project)) return null;
        var plan = VisualBookPlanService.Load(project);
        var type = BookTypeProfileService.Get(project);
        var profile = string.Equals(type, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase)
            ? JsonSerializer.Serialize(new
            {
                Prompt = BookTypePromptProfileService.LoadColoring(project),
                Hard = ColoringIndependentHardProfileService.Resolve(project)
            })
            : JsonSerializer.Serialize(ImageCollectionPromptProfileService.Load(project));
        var assets = new List<FreezeVisualAsset>();
        var order = 0;
        foreach (var job in VisualBookPlanService.AppliedImageJobs(project))
        {
            if (!job.ResultMaterialId.HasValue) continue;
            var material = project.Materials.FirstOrDefault(m => m.MaterialId == job.ResultMaterialId.Value);
            if (material is null) continue;
            assets.Add(new FreezeVisualAsset(
                ++order,
                job.Code ?? string.Empty,
                material.MaterialId,
                material.FileName ?? string.Empty,
                material.Sha256 ?? string.Empty,
                job.TargetContentId));
        }
        return new FreezeVisualPlan(
            type,
            plan.ImageCount,
            plan.Consistent,
            ImageCollectionWorkspaceService.GetConsistencyRules(project),
            profile,
            assets);
    }

    private static int FreezeSequence(RevisionCandidate freeze) =>
        int.TryParse(freeze.ProposedValue, out var value) ? value : 0;

    private static int MaterialOrder(PreviewProject project, Guid materialId)
    {
        var index = project.Materials.FindIndex(m => m.MaterialId == materialId);
        return index < 0 ? int.MaxValue : index;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)));

    private sealed record FreezeSnapshot(Guid ProjectId, FreezeMetadata Metadata, List<FreezeContent> Contents, List<FreezeIllustration> Illustrations, List<FreezeBible> Bible, FreezeVisualPlan? Visual);
    private sealed record FreezeMetadata(string Title, string Subtitle, string Creator, string Language, string Publisher, string Isbn, string Description);
    private sealed record FreezeContent(Guid ContentId, Guid MaterialId, Guid? ParentId, string Kind, string Title, string SourceLocator, int Ordinal, string Body);
    private sealed record FreezeIllustration(Guid PlacementId, Guid MaterialId, Guid ContentId, string Position, int WidthPercent, string Caption, int Ordinal);
    private sealed record FreezeBible(Guid SubjectEntityId, string Key, string Value, string Authority);
    private sealed record FreezeVisualPlan(string BookType, int ImageCount, bool Consistent, string ConsistencyRules, string Profile, List<FreezeVisualAsset> Assets);
    private sealed record FreezeVisualAsset(int Order, string JobCode, Guid MaterialId, string FileName, string Sha256, Guid? TargetContentId);
}

internal readonly record struct EditionFreezeResult(RevisionCandidate? Freeze, string Message);
internal readonly record struct PreflightCheck(string Code, string Severity, bool Passed, string Message);
internal sealed record PreflightResult(bool Ready, Guid? FreezeId, IReadOnlyList<PreflightCheck> Checks)
{
    public string Summary => Ready
        ? "PREFLIGHT READY: l'Edition Freeze corrente ha superato tutti i controlli bloccanti."
        : $"PREFLIGHT BLOCCATO: {Checks.Count(c => c.Severity == "Error" && !c.Passed)} controlli bloccanti non superati.";
}
