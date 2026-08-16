using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace DiezPublishingStudio;

internal static class AiExchangeResponseBundleSelfTest
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAAaklEQVR42u2XwRIAEAhE+/+f5uRGZKOYbcbF1OzLUJESbEIAAnQ3RY4sE4B7pl8BWI/ZDWAmovnAACvimi8EYBEfxcAA6MXbBkBfRIsnAAEI8G4dSFEJw3tBim6YYh64OhGFDqX8FxDgplV2O053240oowAAAABJRU5ErkJggg==");

    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "DiezResponseBundleTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var projectPath = Path.Combine(root, "bundle.diez");
            var project = ProjectFileStore.Create("Response Bundle Test");
            project.EditionMetadata.Title = "Jungle Bundle";
            BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);
            var jobs = AiImageBatchService.CreateImageSeries(project, 3, "jungle animals", "Page").ToList();
            VisualPromptSessionService.EnsureActive(project);
            await ProjectFileStore.SaveAsync(projectPath, project);

            var state = AiExchangeStateStore.Load(project);
            var activeIds = VisualPromptSessionService.ActiveLegacyJobIds(project);
            var units = state.WorkUnits
                .Where(u => u.LegacyAiJobId.HasValue && activeIds.Contains(u.LegacyAiJobId.Value))
                .OrderBy(u => u.Position)
                .ToList();
            Require(units.Count == 3, "Attese tre Work Unit.");

            var promptPath = Path.Combine(root, "prompt.zip");
            var pack = await AiExchangePromptPackBuilder.BuildAsync(
                project, projectPath, state, units.Select(u => u.WorkUnitId), promptPath);
            Require(pack.Success, "Prompt Pack non creato.");
            var snapshot = state.RequestSnapshots.Single(s => s.PromptPackId == pack.PromptPackId);

            var parts = new List<(int Order, Guid WorkUnitId, string FileName, string Path)>();
            for (var i = 0; i < units.Count; i++)
            {
                var order = i + 1;
                var unit = units[i];
                var fileName = BookPackageNamingService.ResponsePartFileName(project, 1, order);
                var partPath = Path.Combine(root, fileName);
                var candidate = snapshot.Items.Single(s => s.WorkUnitId == unit.WorkUnitId).TargetCandidateVersion;
                await CreatePartialResponseAsync(
                    partPath,
                    project.ProjectId,
                    snapshot.JobId,
                    pack.PromptPackId,
                    $"BUNDLE-PART-{order:D3}-{Guid.NewGuid():N}",
                    unit.WorkUnitId,
                    candidate,
                    $"content/IMG-{order:D3}.png");
                parts.Add((order, unit.WorkUnitId, fileName, partPath));
            }

            var bundlePath = Path.Combine(root, BookPackageNamingService.ResponseFileName(project, 1));
            await using (var stream = File.Create(bundlePath))
            using (var bundle = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                var manifest = new
                {
                    protocol = AiExchangeResponseBundleService.Protocol,
                    protocol_version = AiExchangeResponseBundleService.ProtocolVersion,
                    project_id = project.ProjectId,
                    prompt_pack_id = pack.PromptPackId,
                    bundle_id = Guid.NewGuid().ToString("D"),
                    expected_parts = parts.Count,
                    parts = parts.Select(p => new
                    {
                        order = p.Order,
                        work_unit_id = p.WorkUnitId,
                        file_name = AiExchangeResponseBundleService.PartsDirectory + p.FileName
                    }).ToArray()
                };
                var manifestEntry = bundle.CreateEntry(AiExchangeResponseBundleService.ManifestFileName, CompressionLevel.Optimal);
                await using (var target = manifestEntry.Open())
                await using (var writer = new StreamWriter(target, new UTF8Encoding(false)))
                    await writer.WriteAsync(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

                foreach (var part in parts)
                {
                    var entry = bundle.CreateEntry(AiExchangeResponseBundleService.PartsDirectory + part.FileName, CompressionLevel.NoCompression);
                    await using var target = entry.Open();
                    await using var source = File.OpenRead(part.Path);
                    await source.CopyToAsync(target);
                }
            }

            var imported = await AiExchangeResponseBundleService.ImportAsync(project, projectPath, state, [bundlePath]);
            Require(imported.Summary.Imported == 3,
                $"Il bundle unico non ha importato tre Candidate: {imported.Summary.Imported}. {imported.Message}");
            Require(imported.Summary.Incomplete == 0 && imported.Summary.Conflicts == 0 && imported.Summary.Failed == 0,
                "Il bundle valido genera incompleti/conflitti/fallimenti: " + imported.Message);
            Require(imported.Details.Any(d => d.Contains("Response Bundle", StringComparison.OrdinalIgnoreCase) &&
                                              d.Contains("3 Response parziali", StringComparison.OrdinalIgnoreCase)),
                "Manca la diagnostica di espansione del bundle annidato.");

            foreach (var unit in units)
            {
                var expected = snapshot.Items.Single(s => s.WorkUnitId == unit.WorkUnitId).TargetCandidateVersion;
                var version = state.Versions.SingleOrDefault(v => v.WorkUnitId == unit.WorkUnitId && v.VersionNumber == expected);
                Require(version?.MaterialId.HasValue == true, $"{unit.Code}: asset mancante dopo import bundle.");
                var validation = version is null ? null : VisualAssetValidationStore.Get(project, version.VersionId);
                Require(validation?.Status == VisualAssetValidationStatuses.Passed,
                    $"{unit.Code}: asset interno al bundle non supera validazione reale. {validation?.Message}");
            }

            // Compatibility: the same wrapper must still accept a raw partial Response ZIP directly.
            var duplicate = await AiExchangeResponseBundleService.ImportAsync(project, projectPath, state, [parts[0].Path]);
            Require(duplicate.Summary.Duplicates == 1,
                "Il wrapper bundle ha rotto la compatibilità con un Response ZIP ordinario/parziale diretto.");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static async Task CreatePartialResponseAsync(
        string path,
        Guid projectId,
        Guid jobId,
        Guid promptPackId,
        string packageId,
        Guid workUnitId,
        int candidateVersion,
        string assetPath)
    {
        await using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);
        var manifest = new
        {
            protocol = "diez-response",
            protocol_version = 1,
            project_id = projectId,
            job_id = jobId,
            prompt_pack_id = promptPackId,
            package_id = packageId,
            partial = true,
            items = new[]
            {
                new
                {
                    work_unit_id = workUnitId,
                    candidate_version = candidateVersion,
                    content_type = "IMAGE",
                    status = "COMPLETE",
                    primary_asset = assetPath,
                    description = "Pure black-and-white jungle animal coloring page.",
                    render_request_id = (string?)null,
                    render_prompt_sha256 = (string?)null,
                    failure_reason = (string?)null
                }
            }
        };

        var manifestEntry = zip.CreateEntry("response-manifest.json", CompressionLevel.Optimal);
        await using (var target = manifestEntry.Open())
        await using (var writer = new StreamWriter(target, new UTF8Encoding(false)))
            await writer.WriteAsync(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        var assetEntry = zip.CreateEntry(assetPath, CompressionLevel.NoCompression);
        await using var asset = assetEntry.Open();
        await asset.WriteAsync(Png);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("RESPONSE BUNDLE SELF-TEST: " + message);
    }
}
