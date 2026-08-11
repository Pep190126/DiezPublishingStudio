using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DiezPublishingStudio;

internal sealed class VisionValidationImportReport
{
    public int Passed { get; init; }
    public int Failed { get; init; }
    public int Review { get; init; }
    public int Duplicates { get; init; }
    public int Invalid { get; init; }
    public List<string> Details { get; init; } = [];
    public bool Success => Passed + Failed + Review + Duplicates > 0;
    public string Message =>
        $"Vision: {Passed} coerenti · {Failed} bloccate · {Review} da rivedere · {Duplicates} duplicate · {Invalid} non valide." +
        (Details.Count == 0 ? string.Empty : Environment.NewLine + string.Join(Environment.NewLine, Details));
}

internal static class VisionValidationPromptPackService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static async Task<(bool Success, string Message, Guid ValidationPackId)> BuildAsync(
        PreviewProject project,
        string projectPath,
        AiExchangeState exchange,
        IEnumerable<Guid> versionIds,
        string outputPath)
    {
        var ids = versionIds.Distinct().ToHashSet();
        var versions = exchange.Versions
            .Where(v => ids.Contains(v.VersionId) && v.MaterialId.HasValue)
            .OrderBy(v => exchange.WorkUnits.FirstOrDefault(w => w.WorkUnitId == v.WorkUnitId)?.Position ?? int.MaxValue)
            .ToList();
        if (versions.Count == 0)
            return (false, "Nessuna Candidate immagine con file reale da verificare con Vision.", Guid.Empty);

        var provider = PromptPreparationSettingsStore.Load(project).ProviderId;
        var packId = Guid.NewGuid();
        var activeLegacy = VisualPromptSessionService.ActiveLegacyJobIds(project);
        var activeUnits = exchange.WorkUnits
            .Where(u => string.Equals(u.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase) &&
                        (!u.LegacyAiJobId.HasValue || activeLegacy.Contains(u.LegacyAiJobId.Value)))
            .OrderBy(u => u.Position)
            .ThenBy(u => u.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var seriesCount = Math.Max(1, activeUnits.Count);
        var requests = new List<VisionValidationRequest>();
        var bytesByPath = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var version in versions)
        {
            var unit = exchange.WorkUnits.FirstOrDefault(w => w.WorkUnitId == version.WorkUnitId);
            if (unit is null || !string.Equals(unit.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase)) continue;
            var material = project.Materials.FirstOrDefault(m => m.MaterialId == version.MaterialId);
            if (material is null) continue;
            var bytes = await ProjectFileStore.ReadEmbeddedMaterialAsync(projectPath, material);
            if (bytes is null || bytes.Length == 0) continue;

            var candidatePath = $"inputs/candidates/{version.VersionId:D}/{SafeName(material.FileName)}";
            bytesByPath[candidatePath] = bytes;
            requests.Add(VisionValidationSpecificationBuilder.Build(
                project, exchange, unit, version, packId, candidatePath, seriesCount, provider));
        }

        if (requests.Count == 0)
            return (false, "Le Candidate selezionate non contengono file immagine leggibili dal progetto.", Guid.Empty);

        // Paradigms are included as optional visual evidence, never as substitutes for the candidate itself.
        var selectedUnitIds = requests.Select(r => r.WorkUnitId).ToHashSet();
        foreach (var paradigm in exchange.Paradigms.Where(p => exchange.WorkUnits.Any(u =>
                     selectedUnitIds.Contains(u.WorkUnitId) && u.ParadigmIds.Contains(p.ParadigmId))))
        {
            var material = project.Materials.FirstOrDefault(m => m.MaterialId == paradigm.MaterialId);
            if (material is null) continue;
            var bytes = await ProjectFileStore.ReadEmbeddedMaterialAsync(projectPath, material);
            if (bytes is null || bytes.Length == 0) continue;
            bytesByPath[$"inputs/paradigms/{paradigm.ParadigmId:D}/{SafeName(material.FileName)}"] = bytes;
        }

        // If Consistent is active, sibling candidates are evidence for cross-item consistency checks.
        if (!string.IsNullOrWhiteSpace(ImageCollectionWorkspaceService.GetConsistencyRules(project)))
        {
            foreach (var sibling in activeUnits)
            {
                var latest = exchange.Versions.Where(v => v.WorkUnitId == sibling.WorkUnitId && v.MaterialId.HasValue)
                    .OrderByDescending(v => v.VersionNumber).FirstOrDefault();
                if (latest?.MaterialId is not Guid materialId) continue;
                if (requests.Any(r => r.VersionId == latest.VersionId)) continue;
                var material = project.Materials.FirstOrDefault(m => m.MaterialId == materialId);
                if (material is null) continue;
                var bytes = await ProjectFileStore.ReadEmbeddedMaterialAsync(projectPath, material);
                if (bytes is null || bytes.Length == 0) continue;
                bytesByPath[$"inputs/series-context/{latest.VersionId:D}/{SafeName(material.FileName)}"] = bytes;
            }
        }

        var manifest = new
        {
            protocol = "diez-vision-validation",
            protocol_version = 1,
            project_id = project.ProjectId,
            validation_pack_id = packId,
            provider_target = provider,
            active_book_type = BookTypeProfileService.Get(project),
            policy = new
            {
                candidate_image_is_authoritative = true,
                provider_description_is_untrusted_evidence = true,
                deterministic_checks_take_priority = true,
                fail_hard_criterion_blocks_approval = true,
                review_status_requires_human_judgment = true
            },
            items = requests
        };

        var fullPath = EnsureZip(outputPath);
        var temp = fullPath + ".tmp";
        var directory = Path.GetDirectoryName(Path.GetFullPath(fullPath));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        if (File.Exists(temp)) File.Delete(temp);

        await using (var stream = File.Create(temp))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
        {
            await WriteTextAsync(zip, "vision-manifest.json", JsonSerializer.Serialize(manifest, JsonOptions));
            await WriteTextAsync(zip, "instructions.md", Instructions(provider));
            await WriteTextAsync(zip, "schemas/vision-response.schema.json", ResponseSchema());
            foreach (var pair in bytesByPath)
                await WriteBytesAsync(zip, pair.Key, pair.Value);
        }
        File.Move(temp, fullPath, true);

        VisionValidationStore.SavePack(project, new VisionValidationStore.PackRecord
        {
            ValidationPackId = packId,
            ProjectId = project.ProjectId,
            ProviderTarget = provider,
            CreatedAtLocal = DateTimeOffset.Now.ToString("O"),
            Items = requests.Select(r => new VisionValidationStore.PackItem
            {
                VersionId = r.VersionId,
                WorkUnitId = r.WorkUnitId,
                CandidateVersion = r.CandidateVersion,
                ContentSha256 = r.ContentSha256
            }).ToList()
        });
        await ProjectFileStore.SaveAsync(projectPath, project);

        return (true,
            $"Controllo Vision pronto per {requests.Count} Candidate · provider {ProviderLabel(provider)}. Il modello deve guardare i file reali e restituire diez-vision-response.",
            packId);
    }

    public static async Task<VisionValidationImportReport> ImportAsync(
        PreviewProject project,
        string projectPath,
        AiExchangeState exchange,
        IEnumerable<string> zipPaths)
    {
        var passed = 0;
        var failed = 0;
        var review = 0;
        var duplicates = 0;
        var invalid = 0;
        var details = new List<string>();
        var changed = false;

        foreach (var zipPath in zipPaths.Where(File.Exists))
        {
            var label = Path.GetFileName(zipPath);
            try
            {
                using var zip = ZipFile.OpenRead(zipPath);
                var entry = zip.Entries.FirstOrDefault(e =>
                    string.Equals(Normalize(e.FullName), "vision-response.json", StringComparison.OrdinalIgnoreCase));
                if (entry is null)
                {
                    invalid++;
                    details.Add($"{label}: vision-response.json assente.");
                    continue;
                }

                VisionResponseManifest? response;
                await using (var source = entry.Open())
                    response = await JsonSerializer.DeserializeAsync<VisionResponseManifest>(source, JsonOptions);
                if (response is null ||
                    !string.Equals(response.Protocol, "diez-vision-response", StringComparison.OrdinalIgnoreCase) ||
                    response.ProtocolVersion != 1 ||
                    response.ProjectId != project.ProjectId ||
                    response.ValidationPackId == Guid.Empty ||
                    string.IsNullOrWhiteSpace(response.PackageId))
                {
                    invalid++;
                    details.Add($"{label}: header Vision non valido o non appartenente al progetto aperto.");
                    continue;
                }
                if (VisionValidationStore.IsImportedPackage(project, response.PackageId))
                {
                    duplicates++;
                    details.Add($"{label}: package Vision già importato.");
                    continue;
                }

                var pack = VisionValidationStore.GetPack(project, response.ValidationPackId);
                if (pack is null || pack.ProjectId != project.ProjectId)
                {
                    invalid++;
                    details.Add($"{label}: validation_pack_id sconosciuto.");
                    continue;
                }

                foreach (var item in response.Items ?? [])
                {
                    var expected = pack.Items.FirstOrDefault(p => p.VersionId == item.VersionId);
                    var version = exchange.Versions.FirstOrDefault(v => v.VersionId == item.VersionId);
                    var unit = version is null ? null : exchange.WorkUnits.FirstOrDefault(w => w.WorkUnitId == version.WorkUnitId);
                    var code = unit?.Code ?? item.VersionId.ToString("D");
                    if (expected is null || version is null || unit is null ||
                        expected.WorkUnitId != item.WorkUnitId ||
                        expected.CandidateVersion != item.CandidateVersion ||
                        !string.Equals(expected.ContentSha256, item.ContentSha256, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(version.ContentSha256, item.ContentSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        invalid++;
                        details.Add($"{code}: esito Vision non legato esattamente alla Candidate/file corrente; ignorato.");
                        continue;
                    }

                    var result = new VisionValidationResult
                    {
                        VersionId = item.VersionId,
                        WorkUnitId = item.WorkUnitId,
                        CandidateVersion = item.CandidateVersion,
                        ContentSha256 = item.ContentSha256 ?? string.Empty,
                        ProviderId = string.IsNullOrWhiteSpace(response.ProviderId) ? pack.ProviderTarget : response.ProviderId,
                        OverallStatus = item.OverallStatus ?? VisionValidationStatuses.Review,
                        Confidence = item.Confidence,
                        ObservedDescription = item.ObservedDescription ?? string.Empty,
                        Summary = item.Summary ?? string.Empty,
                        Checks = item.Checks ?? []
                    };
                    VisionValidationStore.Apply(project, exchange, result);
                    changed = true;
                    var record = VisionValidationStore.Get(project, result.VersionId)!;
                    if (record.BlocksApproval)
                    {
                        failed++;
                        details.Add($"{code}: Vision FAIL — {record.Summary}");
                    }
                    else if (record.OverallStatus == VisionValidationStatuses.Pass)
                    {
                        passed++;
                        details.Add($"{code}: Vision PASS — {record.Summary}");
                    }
                    else
                    {
                        review++;
                        details.Add($"{code}: Vision REVIEW — {record.Summary}");
                    }
                }

                VisionValidationStore.MarkImportedPackage(project, response.PackageId);
                changed = true;
            }
            catch (InvalidDataException ex)
            {
                invalid++;
                details.Add($"{label}: ZIP Vision non valido: {ex.Message}");
            }
            catch (Exception ex)
            {
                invalid++;
                details.Add($"{label}: errore Vision: {ex.GetBaseException().Message}");
            }
        }

        if (changed)
        {
            AiExchangeStateStore.Save(project, exchange);
            await ProjectFileStore.SaveAsync(projectPath, project);
        }

        return new VisionValidationImportReport
        {
            Passed = passed,
            Failed = failed,
            Review = review,
            Duplicates = duplicates,
            Invalid = invalid,
            Details = details
        };
    }

    private static string Instructions(string provider) => $"""
# Diez Publishing Studio — Independent Vision QA v1

TARGET VALIDATOR: {ProviderLabel(provider)}

You are the independent visual QA inspector for publication assets. Your job is NOT to generate, improve, reinterpret or replace the candidate. Inspect the REAL candidate image file under `inputs/candidates/` and compare what is actually visible against `vision-manifest.json`.

## Non-negotiable method
1. Open and inspect each real candidate image. Never infer compliance from filenames, the generator's description, or the requested prompt alone.
2. Read the complete `expected` specification and `generation_contract` for that exact item.
3. Evaluate semantic subject match, environment match, MUST DO, MUST NOT DO, Book-Type fitness, item-specific overrides, visible text/artifacts, anatomy/geometry, composition/readability and publication quality.
4. If Consistent rules or series-context images are present, evaluate only the dimensions that are actually constrained. Do not demand identical compositions when variation is allowed.
5. A HARD FAIL means the visible image materially violates an explicit hard requirement or is the wrong visual/content for the requested item. One HARD FAIL makes `overall_status = FAIL`.
6. Use `REVIEW` for genuine ambiguity or a soft-quality concern that needs a human decision. Do not convert uncertainty into PASS.
7. Use `PASS` only when the candidate is semantically aligned and no HARD criterion fails.
8. The deterministic Diez raster checks are authoritative for measurable pixel/size constraints. Vision must not overrule them.
9. `observed_description` must describe the image you actually see, even when it contradicts the requested content.
10. Return ONLY `vision-response.json` in a ZIP. Do not rename IDs, versions or hashes.

## Required checks per item
Use at least these keys when applicable:
- `subject_match` — HARD
- `environment_match` — HARD when explicitly constrained
- `must_do` — HARD
- `must_not_do` — HARD
- `book_type_fit` — HARD
- `item_override_match` — HARD when present
- `visible_text_or_watermark` — HARD unless requested
- `anatomy_geometry` — HARD only for obvious unusable defects; otherwise SOFT/WARN
- `composition_readability` — SOFT
- `style_quality` — SOFT
- `publication_quality` — SOFT
- `series_consistency` — HARD only for LOCKED constraints; otherwise SOFT

Confidence is a number from 0.0 to 1.0. Evidence must be concise and visual/factual.

The response format is defined by `schemas/vision-response.schema.json`.
""";

    private static string ResponseSchema() => """
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "type": "object",
  "required": ["protocol", "protocol_version", "project_id", "validation_pack_id", "package_id", "provider_id", "items"],
  "properties": {
    "protocol": { "const": "diez-vision-response" },
    "protocol_version": { "const": 1 },
    "project_id": { "type": "string", "format": "uuid" },
    "validation_pack_id": { "type": "string", "format": "uuid" },
    "package_id": { "type": "string", "minLength": 1 },
    "provider_id": { "type": "string" },
    "items": {
      "type": "array",
      "items": {
        "type": "object",
        "required": ["version_id", "work_unit_id", "candidate_version", "content_sha256", "overall_status", "confidence", "observed_description", "summary", "checks"],
        "properties": {
          "version_id": { "type": "string", "format": "uuid" },
          "work_unit_id": { "type": "string", "format": "uuid" },
          "candidate_version": { "type": "integer", "minimum": 1 },
          "content_sha256": { "type": "string" },
          "overall_status": { "enum": ["PASS", "FAIL", "REVIEW"] },
          "confidence": { "type": "number", "minimum": 0, "maximum": 1 },
          "observed_description": { "type": "string" },
          "summary": { "type": "string" },
          "checks": {
            "type": "array",
            "items": {
              "type": "object",
              "required": ["key", "status", "severity", "confidence", "evidence"],
              "properties": {
                "key": { "type": "string" },
                "status": { "enum": ["PASS", "FAIL", "WARN", "NA"] },
                "severity": { "enum": ["HARD", "SOFT"] },
                "confidence": { "type": "number", "minimum": 0, "maximum": 1 },
                "evidence": { "type": "string" }
              }
            }
          }
        }
      }
    }
  }
}
""";

    private static string ProviderLabel(string provider) => provider switch
    {
        PromptEngineeringProviderIds.OpenAi => "OpenAI / ChatGPT vision-capable model",
        PromptEngineeringProviderIds.Gemini => "Gemini multimodal vision-capable model",
        PromptEngineeringProviderIds.Other => "Other vision-capable multimodal model",
        _ => "Generic vision-capable multimodal model"
    };

    private static string EnsureZip(string path) =>
        path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? path : path + ".zip";

    private static string SafeName(string? name)
    {
        var value = string.IsNullOrWhiteSpace(name) ? "candidate.png" : Path.GetFileName(name);
        return string.Concat(value.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
    }

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');

    private static async Task WriteTextAsync(ZipArchive zip, string path, string text)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(text);
    }

    private static async Task WriteBytesAsync(ZipArchive zip, string path, byte[] bytes)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await stream.WriteAsync(bytes);
    }

    private sealed class VisionResponseManifest
    {
        public string Protocol { get; set; } = string.Empty;
        public int ProtocolVersion { get; set; }
        public Guid ProjectId { get; set; }
        public Guid ValidationPackId { get; set; }
        public string PackageId { get; set; } = string.Empty;
        public string ProviderId { get; set; } = string.Empty;
        public List<VisionResponseItem> Items { get; set; } = [];
    }

    private sealed class VisionResponseItem
    {
        public Guid VersionId { get; set; }
        public Guid WorkUnitId { get; set; }
        public int CandidateVersion { get; set; }
        public string ContentSha256 { get; set; } = string.Empty;
        public string OverallStatus { get; set; } = VisionValidationStatuses.Review;
        public double Confidence { get; set; }
        public string ObservedDescription { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public List<VisionValidationCheck> Checks { get; set; } = [];
    }
}
