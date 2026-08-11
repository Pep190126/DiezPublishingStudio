using System.IO.Compression;
using System.Text.Json;

namespace DiezPublishingStudio;

internal sealed class AiExchangeImportV2Report
{
    public AiExchangeImportSummary Summary { get; init; }
    public List<string> Details { get; init; } = [];
    public string Message => Details.Count == 0
        ? Summary.Message
        : Summary.Message + Environment.NewLine + string.Join(Environment.NewLine, Details);
}

/// <summary>
/// Audited response importer used by the guided visual workflow.
/// It validates manifest/file identity before ingestion and verifies the resulting Candidate afterwards,
/// preventing false "image N missing" messages when the asset is actually present in the ZIP.
/// </summary>
internal static class AiExchangeResponseImportV2
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static async Task<AiExchangeImportV2Report> ImportAsync(
        PreviewProject project,
        string projectPath,
        AiExchangeState state,
        IEnumerable<string> zipPaths)
    {
        var imported = 0;
        var incomplete = 0;
        var duplicates = 0;
        var conflicts = 0;
        var failed = 0;
        var details = new List<string>();
        var changed = false;

        foreach (var zipPath in zipPaths.Where(File.Exists))
        {
            var zipLabel = Path.GetFileName(zipPath);
            var tempRoot = Path.Combine(Path.GetTempPath(), "DiezAiExchangeV2-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                using var archive = ZipFile.OpenRead(zipPath);
                var manifestEntry = FindEntryExactOrCaseInsensitive(archive, "response-manifest.json");
                if (manifestEntry is null)
                {
                    failed++;
                    details.Add($"{zipLabel}: response-manifest.json realmente assente.");
                    continue;
                }

                ResponseManifest? manifest;
                await using (var stream = manifestEntry.Open())
                    manifest = await JsonSerializer.DeserializeAsync<ResponseManifest>(stream, JsonOptions);
                if (!ValidateHeader(project, state, manifest, out var snapshot, out var headerError))
                {
                    failed++;
                    details.Add($"{zipLabel}: {headerError}");
                    continue;
                }
                if (state.ImportedPackageIds.Contains(manifest!.PackageId, StringComparer.OrdinalIgnoreCase))
                {
                    duplicates++;
                    details.Add($"{zipLabel}: package {manifest.PackageId} già importato, nessun dato duplicato.");
                    continue;
                }

                var items = manifest.Items ?? [];
                details.Add($"{zipLabel}: manifest={items.Count} item; file content/ presenti={archive.Entries.Count(e => Normalize(e.FullName).StartsWith("content/", StringComparison.OrdinalIgnoreCase))}.");
                var seen = new HashSet<Guid>();

                foreach (var item in items)
                {
                    var unit = state.WorkUnits.FirstOrDefault(w => w.WorkUnitId == item.WorkUnitId);
                    var code = unit?.Code ?? item.WorkUnitId.ToString("D");
                    if (!seen.Add(item.WorkUnitId))
                    {
                        conflicts++;
                        details.Add($"{code}: Work Unit duplicata nello stesso response-manifest.");
                        continue;
                    }
                    if (string.Equals(item.Status, "FAILED", StringComparison.OrdinalIgnoreCase))
                    {
                        failed++;
                        details.Add($"{code}: provider ha dichiarato FAILED.");
                        continue;
                    }

                    var request = snapshot!.Items.FirstOrDefault(x => x.WorkUnitId == item.WorkUnitId);
                    if (unit is null || request is null)
                    {
                        conflicts++;
                        details.Add($"{code}: Work Unit non appartiene allo snapshot di questo Prompt Pack.");
                        continue;
                    }
                    if (request.TargetCandidateVersion != item.CandidateVersion)
                    {
                        conflicts++;
                        details.Add($"{code}: candidate_version attesa {request.TargetCandidateVersion}, ricevuta {item.CandidateVersion}.");
                        continue;
                    }

                    string? localAssetPath = null;
                    if (!string.IsNullOrWhiteSpace(item.PrimaryAsset))
                    {
                        var resolved = ResolveAsset(archive, item.PrimaryAsset);
                        if (resolved.Entry is null)
                        {
                            incomplete++;
                            details.Add($"{code}: asset realmente non trovato. Manifest chiedeva '{item.PrimaryAsset}'. {resolved.Diagnostic}");
                            continue;
                        }
                        if (resolved.UsedFallback)
                            details.Add($"{code}: asset risolto in modo robusto '{item.PrimaryAsset}' → '{resolved.Entry.FullName}'.");

                        var ext = Path.GetExtension(resolved.Entry.Name);
                        localAssetPath = Path.Combine(tempRoot, Guid.NewGuid().ToString("N") + ext);
                        await using var source = resolved.Entry.Open();
                        await using var destination = File.Create(localAssetPath);
                        await source.CopyToAsync(destination);
                    }
                    else if (string.Equals(unit.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase))
                    {
                        incomplete++;
                        details.Add($"{code}: primary_asset vuoto nel manifest per una Work Unit immagine.");
                        continue;
                    }

                    var ingest = await AiExchangeResultIngestor.IngestAsync(project, state, new AiExchangeNormalizedResultItem
                    {
                        WorkUnitId = item.WorkUnitId,
                        CandidateVersion = item.CandidateVersion,
                        ContentType = item.ContentType ?? string.Empty,
                        ResultStatus = item.Status ?? "INCOMPLETE",
                        PrimaryAssetPath = localAssetPath,
                        Description = item.Description ?? string.Empty,
                        Origin = AiExchangeOrigins.AiPromptPack,
                        SourceSnapshotId = snapshot.SnapshotId
                    });
                    changed |= ingest.Status is "IMPORTED" or "UPDATED" or "INCOMPLETE";

                    switch (ingest.Status)
                    {
                        case "IMPORTED":
                        case "UPDATED": imported++; break;
                        case "INCOMPLETE": incomplete++; break;
                        case "DUPLICATE": duplicates++; break;
                        case "CONFLICT": conflicts++; break;
                        default: failed++; break;
                    }

                    var version = state.Versions.FirstOrDefault(v =>
                        v.WorkUnitId == item.WorkUnitId && v.VersionNumber == item.CandidateVersion);
                    var image = string.Equals(unit.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase);
                    if (version is null)
                    {
                        failed++;
                        details.Add($"{code}: ingest terminato ma Candidate v{item.CandidateVersion} non presente nello stato Diez.");
                    }
                    else if (image && !version.MaterialId.HasValue)
                    {
                        incomplete++;
                        details.Add($"{code}: Candidate v{item.CandidateVersion} creata ma senza asset immagine associato.");
                    }
                    else
                    {
                        details.Add($"{code}: asset presente e Candidate v{item.CandidateVersion} verificata ({version.Status}).");
                    }
                }

                // A complete response should normally cover every requested item. A partial response is legal,
                // but missing snapshot items are reported explicitly instead of inventing a missing file diagnosis.
                var returnedIds = items.Select(i => i.WorkUnitId).ToHashSet();
                var omitted = snapshot!.Items.Where(s => !returnedIds.Contains(s.WorkUnitId)).ToList();
                foreach (var missing in omitted)
                {
                    var unit = state.WorkUnits.FirstOrDefault(w => w.WorkUnitId == missing.WorkUnitId);
                    details.Add($"{unit?.Code ?? missing.WorkUnitId.ToString("D")}: Work Unit non presente nel manifest restituito{(manifest.Partial ? " (package parziale ammesso)" : " (package dichiarato completo)")}.");
                    if (!manifest.Partial) incomplete++;
                }

                state.ImportedPackageIds.Add(manifest.PackageId);
                changed = true;
            }
            catch (InvalidDataException ex)
            {
                failed++;
                details.Add($"{zipLabel}: ZIP non valido: {ex.Message}");
            }
            catch (Exception ex)
            {
                failed++;
                details.Add($"{zipLabel}: errore import: {ex.GetBaseException().Message}");
            }
            finally
            {
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        }

        var promoted = AiExchangeImportPipeline.ReconcileCompletedCandidates(state);
        if (promoted > 0)
        {
            imported += promoted;
            incomplete = Math.Max(0, incomplete - promoted);
            changed = true;
            details.Add($"Riconciliate {promoted} Candidate completate da package parziali.");
        }

        if (changed)
        {
            AiExchangeStateStore.Save(project, state);
            await ProjectFileStore.SaveAsync(projectPath, project);
        }

        var success = imported > 0 || incomplete > 0 || duplicates > 0;
        var summary = new AiExchangeImportSummary(
            success,
            imported,
            incomplete,
            duplicates,
            conflicts,
            failed,
            $"Import AI verificato: {imported} pronti/aggiornati · {incomplete} incompleti · {duplicates} duplicati · {conflicts} conflitti · {failed} errori.");
        return new AiExchangeImportV2Report { Summary = summary, Details = details };
    }

    private static bool ValidateHeader(
        PreviewProject project,
        AiExchangeState state,
        ResponseManifest? manifest,
        out AiExchangeRequestSnapshot? snapshot,
        out string error)
    {
        snapshot = null;
        error = string.Empty;
        if (manifest is null || !string.Equals(manifest.Protocol, "diez-response", StringComparison.OrdinalIgnoreCase) || manifest.ProtocolVersion != 1)
        {
            error = "protocollo response non valido.";
            return false;
        }
        if (manifest.ProjectId != project.ProjectId)
        {
            error = "project_id diverso dal progetto aperto.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(manifest.PackageId))
        {
            error = "package_id mancante.";
            return false;
        }
        var pack = state.PromptPacks.FirstOrDefault(p => p.PromptPackId == manifest.PromptPackId);
        snapshot = pack is null ? null : state.RequestSnapshots.FirstOrDefault(s => s.SnapshotId == pack.SnapshotId);
        if (pack is null || snapshot is null || snapshot.JobId != manifest.JobId)
        {
            error = "Prompt Pack/snapshot/job non corrispondono allo stato Diez corrente.";
            return false;
        }
        return true;
    }

    private static AssetResolution ResolveAsset(ZipArchive archive, string requested)
    {
        string normalized;
        try { normalized = Normalize(Uri.UnescapeDataString(requested)); }
        catch { normalized = Normalize(requested); }
        if (!IsSafe(normalized)) return new AssetResolution(null, false, "Path non sicuro.");

        var exact = archive.Entries.FirstOrDefault(e => string.Equals(Normalize(e.FullName), normalized, StringComparison.Ordinal));
        if (exact is not null) return new AssetResolution(exact, false, "Match esatto.");

        var ignoreCase = archive.Entries.FirstOrDefault(e => string.Equals(Normalize(e.FullName), normalized, StringComparison.OrdinalIgnoreCase));
        if (ignoreCase is not null) return new AssetResolution(ignoreCase, true, "Match case-insensitive.");

        var name = Path.GetFileName(normalized.Replace('/', Path.DirectorySeparatorChar));
        var byName = archive.Entries.Where(e =>
                Normalize(e.FullName).StartsWith("content/", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return byName.Count switch
        {
            1 => new AssetResolution(byName[0], true, "Match univoco per nome file dentro content/.") ,
            > 1 => new AssetResolution(null, false, $"Trovati {byName.Count} file con lo stesso nome: associazione ambigua."),
            _ => new AssetResolution(null, false, "Nessun entry ZIP compatibile trovato.")
        };
    }

    private static ZipArchiveEntry? FindEntryExactOrCaseInsensitive(ZipArchive archive, string path)
    {
        var normalized = Normalize(path);
        return archive.Entries.FirstOrDefault(e => string.Equals(Normalize(e.FullName), normalized, StringComparison.Ordinal))
               ?? archive.Entries.FirstOrDefault(e => string.Equals(Normalize(e.FullName), normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string value) => value.Replace('\\', '/').Trim().TrimStart('/');

    private static bool IsSafe(string normalized) =>
        !string.IsNullOrWhiteSpace(normalized) &&
        !normalized.StartsWith("..", StringComparison.Ordinal) &&
        !normalized.Contains("../", StringComparison.Ordinal) &&
        !Path.IsPathRooted(normalized.Replace('/', Path.DirectorySeparatorChar));

    private readonly record struct AssetResolution(ZipArchiveEntry? Entry, bool UsedFallback, string Diagnostic);

    private sealed class ResponseManifest
    {
        public string Protocol { get; set; } = string.Empty;
        public int ProtocolVersion { get; set; }
        public Guid ProjectId { get; set; }
        public Guid JobId { get; set; }
        public Guid PromptPackId { get; set; }
        public string PackageId { get; set; } = string.Empty;
        public bool Partial { get; set; }
        public List<ResponseItem> Items { get; set; } = [];
    }

    private sealed class ResponseItem
    {
        public Guid WorkUnitId { get; set; }
        public int CandidateVersion { get; set; }
        public string ContentType { get; set; } = string.Empty;
        public string Status { get; set; } = "COMPLETE";
        public string PrimaryAsset { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
