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
        if (editable.Count == 0)
            return new EditionFreezeResult(null, "Edition Freeze non creato: il Master non contiene ancora capitoli o sezioni modificabili.");

        var snapshot = BuildCanonicalSnapshot(project);
        var hash = Hash(snapshot);
        var latest = GetLatestFreeze(project);
        if (latest is not null && string.Equals(latest.BaseContentSha256, hash, StringComparison.Ordinal))
            return new EditionFreezeResult(latest, $"Il Master coincide già con Edition Freeze #{FreezeSequence(latest)}.");

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
                ? $"Edition Freeze #{sequence}: snapshot immutabile del Master e della Bible prima del preflight."
                : note.Trim(),
            Status = "Applied",
            CreatedAtLocal = now,
            ApprovedAtLocal = now,
            AppliedAtLocal = now
        };
        project.RevisionCandidates.Add(freeze);
        return new EditionFreezeResult(freeze, $"Edition Freeze #{sequence} creato. Il Master resta modificabile, ma ogni modifica successiva renderà questo freeze non corrente.");
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

        checks.Add(new PreflightCheck(
            "FREEZE_EXISTS",
            "Error",
            freeze is not null,
            freeze is null ? "Manca un Edition Freeze del Master." : $"Edition Freeze #{FreezeSequence(freeze)} disponibile."));

        var freezeCurrent = freeze is not null && IsLatestFreezeCurrent(project);
        checks.Add(new PreflightCheck(
            "FREEZE_CURRENT",
            "Error",
            freezeCurrent,
            freeze is null
                ? "Impossibile verificare il Master senza Edition Freeze."
                : freezeCurrent ? "Il Master coincide con l'ultimo Edition Freeze." : "Il Master è cambiato dopo l'ultimo Edition Freeze: crea un nuovo freeze."));

        var editable = project.ContentNodes.Where(n => EditableMasterService.CanEdit(project, n)).ToList();
        checks.Add(new PreflightCheck(
            "CONTENT_PRESENT",
            "Error",
            editable.Count > 0,
            editable.Count > 0 ? $"{editable.Count} contenuti editoriali modificabili presenti." : "Nessun capitolo o sezione editoriale da pubblicare."));

        var empty = editable.Where(n => string.IsNullOrWhiteSpace(n.Body)).ToList();
        checks.Add(new PreflightCheck(
            "NO_EMPTY_CONTENT",
            "Error",
            empty.Count == 0,
            empty.Count == 0 ? "Nessun contenuto editoriale vuoto." : $"{empty.Count} contenuti editoriali sono vuoti."));

        var notEmbedded = project.Materials.Where(m => !m.IsEmbedded).ToList();
        checks.Add(new PreflightCheck(
            "MATERIALS_EMBEDDED",
            "Error",
            project.Materials.Count > 0 && notEmbedded.Count == 0,
            project.Materials.Count == 0
                ? "Nessun materiale sorgente incorporato nel progetto."
                : notEmbedded.Count == 0 ? "Tutti i materiali sorgente risultano incorporati nel .diez." : $"{notEmbedded.Count} materiali non risultano incorporati nel .diez."));

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

        var bible = project.BibleEntries
            .Where(b => b.IsActive)
            .OrderBy(b => b.SubjectEntityId)
            .ThenBy(b => b.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(b => b.Value, StringComparer.OrdinalIgnoreCase)
            .Select(b => new FreezeBible(b.SubjectEntityId, b.Key ?? string.Empty, b.Value ?? string.Empty, b.Authority ?? string.Empty))
            .ToList();

        return JsonSerializer.Serialize(new FreezeSnapshot(project.ProjectId, orderedContent, bible));
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

    private sealed record FreezeSnapshot(Guid ProjectId, List<FreezeContent> Contents, List<FreezeBible> Bible);
    private sealed record FreezeContent(Guid ContentId, Guid MaterialId, Guid? ParentId, string Kind, string Title, string SourceLocator, int Ordinal, string Body);
    private sealed record FreezeBible(Guid SubjectEntityId, string Key, string Value, string Authority);
}

internal readonly record struct EditionFreezeResult(RevisionCandidate? Freeze, string Message);
internal readonly record struct PreflightCheck(string Code, string Severity, bool Passed, string Message);
internal sealed record PreflightResult(bool Ready, Guid? FreezeId, IReadOnlyList<PreflightCheck> Checks)
{
    public string Summary => Ready
        ? "PREFLIGHT READY: l'Edition Freeze corrente ha superato tutti i controlli bloccanti."
        : $"PREFLIGHT BLOCCATO: {Checks.Count(c => c.Severity == "Error" && !c.Passed)} controlli bloccanti non superati.";
}
