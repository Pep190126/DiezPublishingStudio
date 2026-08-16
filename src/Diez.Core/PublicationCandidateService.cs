using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DiezPublishingStudio;

internal static class PublicationCandidateService
{
    public const string CandidateKey = "publication_candidate";

    public static PublicationCandidateResult Create(PreviewProject project)
    {
        project.RevisionCandidates ??= [];
        var preflight = EditionFreezeService.RunPreflight(project);
        if (!preflight.Ready)
            return new PublicationCandidateResult(null, "Publication Candidate non creato: il preflight deve essere READY.");

        var freeze = EditionFreezeService.GetLatestFreeze(project);
        if (freeze is null)
            return new PublicationCandidateResult(null, "Publication Candidate non creato: manca un Edition Freeze corrente.");

        var latest = GetLatest(project);
        var freezeId = freeze.CandidateId.ToString("N");
        if (latest is not null && IsCandidateForFreeze(latest, freeze))
            return new PublicationCandidateResult(latest, $"Il Publication Candidate #{Sequence(latest)} rappresenta già l'Edition Freeze corrente.");

        var master = RenderMaster(project);
        if (string.IsNullOrWhiteSpace(master))
            return new PublicationCandidateResult(null, "Publication Candidate non creato: il Master editoriale è vuoto.");

        var sequence = project.RevisionCandidates.Count(c => c.Key == CandidateKey && c.Status == "Applied") + 1;
        var masterHash = Hash(master);
        var now = DateTimeOffset.Now.ToString("O");
        var candidate = new RevisionCandidate
        {
            CandidateId = Guid.NewGuid(),
            IssueId = Guid.Empty,
            IssueSignature = $"PUBLICATION:{sequence:D4}:{masterHash[..16]}",
            SubjectEntityId = Guid.Empty,
            ContentId = Guid.Empty,
            Key = CandidateKey,
            OriginalValue = freezeId,
            ProposedValue = sequence.ToString(),
            OriginalBody = string.Empty,
            ProposedBody = master,
            BaseContentSha256 = freeze.BaseContentSha256,
            Rationale = $"Publication Candidate #{sequence}: copia editoriale immutabile derivata dall'Edition Freeze {freeze.ProposedValue} dopo preflight READY.",
            Status = "Applied",
            CreatedAtLocal = now,
            ApprovedAtLocal = now,
            AppliedAtLocal = now
        };
        project.RevisionCandidates.Add(candidate);
        return new PublicationCandidateResult(candidate,
            $"Publication Candidate #{sequence} creato dall'Edition Freeze corrente. Le modifiche future al progetto editoriale non altereranno questa copia.");
    }

    public static RevisionCandidate? GetLatest(PreviewProject project) =>
        project.RevisionCandidates
            .Where(c => c.Key == CandidateKey && c.Status == "Applied")
            .OrderByDescending(Sequence)
            .ThenByDescending(c => c.CreatedAtLocal, StringComparer.Ordinal)
            .FirstOrDefault();

    public static int Count(PreviewProject project) =>
        project.RevisionCandidates.Count(c => c.Key == CandidateKey && c.Status == "Applied");

    public static bool IsLatestCandidateCurrent(PreviewProject project)
    {
        var candidate = GetLatest(project);
        var freeze = EditionFreezeService.GetLatestFreeze(project);
        return candidate is not null && freeze is not null &&
               EditionFreezeService.IsLatestFreezeCurrent(project) &&
               IsCandidateForFreeze(candidate, freeze);
    }

    public static string SuggestedPackageName(PreviewProject project)
    {
        var candidate = GetLatest(project);
        var sequence = candidate is null ? Count(project) + 1 : Sequence(candidate);
        var title = string.IsNullOrWhiteSpace(project.EditionMetadata?.Title) ? project.Name : project.EditionMetadata.Title;
        return $"{SanitizeFileName(title)}-publication-{sequence:D3}.zip";
    }

    public static async Task<PublicationExportResult> ExportPackageAsync(PreviewProject project, string outputPath)
    {
        var preflight = EditionFreezeService.RunPreflight(project);
        if (!preflight.Ready)
            return new PublicationExportResult(false, "Esportazione bloccata: il preflight non è READY.", null);

        var candidate = GetLatest(project);
        var freeze = EditionFreezeService.GetLatestFreeze(project);
        if (candidate is null || freeze is null || !IsCandidateForFreeze(candidate, freeze) || !EditionFreezeService.IsLatestFreezeCurrent(project))
            return new PublicationExportResult(false,
                "Esportazione bloccata: crea un Publication Candidate dall'Edition Freeze corrente.", null);

        if (string.IsNullOrWhiteSpace(outputPath))
            return new PublicationExportResult(false, "Percorso di esportazione non valido.", null);

        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        if (File.Exists(fullPath)) File.Delete(fullPath);
        await using var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);

        var masterText = candidate.ProposedBody ?? string.Empty;
        var sourceMetadata = project.EditionMetadata ?? new EditionMetadata();
        var metadata = new PublicationMetadataDocument(
            sourceMetadata.Title ?? string.Empty,
            sourceMetadata.Subtitle ?? string.Empty,
            sourceMetadata.Creator ?? string.Empty,
            sourceMetadata.Language ?? string.Empty,
            sourceMetadata.Publisher ?? string.Empty,
            sourceMetadata.Isbn ?? string.Empty,
            sourceMetadata.Description ?? string.Empty);
        var manifest = new PublicationManifest(
            project.ProjectId,
            project.Name,
            candidate.CandidateId,
            Sequence(candidate),
            freeze.CandidateId,
            freeze.ProposedValue,
            candidate.CreatedAtLocal,
            candidate.BaseContentSha256,
            Hash(masterText),
            metadata,
            project.Materials.Count,
            project.ContentNodes.Count(n => EditableMasterService.CanEdit(project, n)),
            project.BibleEntries.Count(b => b.IsActive),
            preflight.Checks.Select(c => new PublicationManifestCheck(c.Code, c.Severity, c.Passed, c.Message)).ToList());

        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        await WriteEntryAsync(archive, "master.txt", masterText);
        await WriteEntryAsync(archive, "metadata.json", JsonSerializer.Serialize(metadata, jsonOptions));
        await WriteEntryAsync(archive, "edition-manifest.json", JsonSerializer.Serialize(manifest, jsonOptions));
        await WriteEntryAsync(archive, "preflight.txt", BuildPreflightReport(preflight));

        return new PublicationExportResult(true,
            $"Pacchetto editoriale esportato: {Path.GetFileName(fullPath)}", fullPath);
    }

    public static string RenderMaster(PreviewProject project)
    {
        var nodes = project.ContentNodes
            .Where(n => EditableMasterService.CanEdit(project, n))
            .OrderBy(n => MaterialOrder(project, n.MaterialId))
            .ThenBy(n => n.Ordinal)
            .ThenBy(n => n.ContentId)
            .ToList();

        var builder = new StringBuilder();
        foreach (var node in nodes)
        {
            if (builder.Length > 0) builder.AppendLine().AppendLine();
            if (!string.IsNullOrWhiteSpace(node.Title))
            {
                var title = node.Title.Trim();
                builder.AppendLine(title);
                builder.AppendLine(new string('=', Math.Clamp(title.Length, 3, 72)));
                builder.AppendLine();
            }
            builder.Append((node.Body ?? string.Empty).Trim());
        }
        return builder.ToString().Trim();
    }

    private static bool IsCandidateForFreeze(RevisionCandidate candidate, RevisionCandidate freeze) =>
        candidate.Key == CandidateKey && candidate.Status == "Applied" &&
        string.Equals(candidate.OriginalValue, freeze.CandidateId.ToString("N"), StringComparison.OrdinalIgnoreCase) &&
        string.Equals(candidate.BaseContentSha256, freeze.BaseContentSha256, StringComparison.Ordinal);

    private static int Sequence(RevisionCandidate candidate) =>
        int.TryParse(candidate.ProposedValue, out var value) ? value : 0;

    private static int MaterialOrder(PreviewProject project, Guid materialId)
    {
        var index = project.Materials.FindIndex(m => m.MaterialId == materialId);
        return index < 0 ? int.MaxValue : index;
    }

    private static string BuildPreflightReport(PreflightResult preflight)
    {
        var builder = new StringBuilder();
        builder.AppendLine(preflight.Summary);
        builder.AppendLine();
        foreach (var check in preflight.Checks)
            builder.AppendLine($"{(check.Passed ? "PASS" : "FAIL")} [{check.Severity}] {check.Code} - {check.Message}");
        return builder.ToString().TrimEnd();
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var entryStream = entry.Open();
        await using var writer = new StreamWriter(entryStream, new UTF8Encoding(false));
        await writer.WriteAsync(content ?? string.Empty);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)));

    private static string SanitizeFileName(string value)
    {
        var name = string.IsNullOrWhiteSpace(value) ? "Diez-Edition" : value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '-');
        return name.Replace(' ', '-');
    }

    private sealed record PublicationManifest(
        Guid ProjectId,
        string ProjectName,
        Guid PublicationCandidateId,
        int PublicationCandidateSequence,
        Guid EditionFreezeId,
        string EditionFreezeSequence,
        string CreatedAtLocal,
        string EditionFreezeSha256,
        string MasterSha256,
        PublicationMetadataDocument Metadata,
        int MaterialCount,
        int EditableContentCount,
        int ActiveBibleEntryCount,
        List<PublicationManifestCheck> PreflightChecks);

    private sealed record PublicationMetadataDocument(
        string Title,
        string Subtitle,
        string Creator,
        string Language,
        string Publisher,
        string Isbn,
        string Description);

    private sealed record PublicationManifestCheck(string Code, string Severity, bool Passed, string Message);
}

internal readonly record struct PublicationCandidateResult(RevisionCandidate? Candidate, string Message);
internal readonly record struct PublicationExportResult(bool Exported, string Message, string? OutputPath);