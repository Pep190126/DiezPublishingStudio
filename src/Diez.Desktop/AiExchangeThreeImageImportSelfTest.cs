using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace DiezPublishingStudio;

internal static class AiExchangeThreeImageImportSelfTest
{
    // 32x32 pure black/white line-art-like raster: enough ink + white space to pass the deterministic
    // Coloring validator while keeping this importer regression tiny and self contained.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAAaklEQVR42u2XwRIAEAhE+/+f5uRGZKOYbcbF1OzLUJESbEIAAnQ3RY4sE4B7pl8BWI/ZDWAmovnAACvimi8EYBEfxcAA6MXbBkBfRIsnAAEI8G4dSFEJw3tBim6YYh64OhGFDqX8FxDgplV2O053240oowAAAABJRU5ErkJggg==");

    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "DiezThreeImageImport-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var projectPath = Path.Combine(root, "three.diez");
            var project = ProjectFileStore.Create("Three Image Import");
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
            Require(units.Count == 3, "Attese tre Work Unit attive.");

            var packPath = Path.Combine(root, "prompt.zip");
            var pack = await AiExchangePromptPackBuilder.BuildAsync(project, projectPath, state, units.Select(u => u.WorkUnitId), packPath);
            Require(pack.Success, "Prompt Pack non creato.");
            var snapshot = state.RequestSnapshots.Single(s => s.PromptPackId == pack.PromptPackId);

            // Reproduce the user's valid three-image response, deliberately varying path case/slashes
            // for item 3 to prove that a harmless ZIP naming variation cannot become "immagine 3 mancante".
            var response = Path.Combine(root, "response-three.zip");
            await using (var stream = File.Create(response))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                var paths = new[] { "content/IMG-001.png", "content/IMG-002.png", "CONTENT/img-003.PNG" };
                var items = units.Select((unit, i) => new
                {
                    work_unit_id = unit.WorkUnitId,
                    candidate_version = snapshot.Items.Single(s => s.WorkUnitId == unit.WorkUnitId).TargetCandidateVersion,
                    content_type = "IMAGE",
                    status = "COMPLETE",
                    primary_asset = i == 2 ? "content/IMG-003.png" : paths[i],
                    description = $"Coloring page {i + 1} with a jungle animal, pure black line art on white."
                }).ToArray();
                var manifest = new
                {
                    protocol = "diez-response",
                    protocol_version = 1,
                    project_id = project.ProjectId,
                    job_id = snapshot.JobId,
                    prompt_pack_id = pack.PromptPackId,
                    package_id = "THREE-IMAGE-CASE-TEST",
                    partial = false,
                    items
                };
                var manifestEntry = zip.CreateEntry("response-manifest.json");
                await using (var target = manifestEntry.Open())
                await using (var writer = new StreamWriter(target, new UTF8Encoding(false)))
                    await writer.WriteAsync(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
                foreach (var path in paths)
                {
                    var entry = zip.CreateEntry(path);
                    await using var target = entry.Open();
                    await target.WriteAsync(Png);
                }
            }

            var imported = await AiExchangeResponseImportV2.ImportAsync(project, projectPath, state, [response]);
            Require(imported.Summary.Imported == 3, $"Attesi 3 import, ottenuti {imported.Summary.Imported}. {imported.Message}");
            Require(imported.Summary.Incomplete == 0, "Il response completo genera falsi incompleti: " + imported.Message);
            Require(imported.Summary.Failed == 0 && imported.Summary.Conflicts == 0,
                "Il response completo genera errori/conflitti: " + imported.Message);
            Require(imported.Details.Any(d => d.Contains("IMG-003", StringComparison.OrdinalIgnoreCase) &&
                                              d.Contains("Candidate", StringComparison.OrdinalIgnoreCase)),
                "Manca la verifica esplicita di IMG-003.");
            foreach (var unit in units)
            {
                var expected = snapshot.Items.Single(s => s.WorkUnitId == unit.WorkUnitId).TargetCandidateVersion;
                var version = state.Versions.SingleOrDefault(v => v.WorkUnitId == unit.WorkUnitId && v.VersionNumber == expected);
                Require(version?.MaterialId.HasValue == true, $"{unit.Code}: Candidate/asset realmente mancante dopo l'import.");
                var validation = version is null ? null : VisualAssetValidationStore.Get(project, version.VersionId);
                Require(validation?.Status == VisualAssetValidationStatuses.Passed,
                    $"{unit.Code}: la regressione tre immagini non supera la validazione asset reale. {validation?.Message}");
            }
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("THREE IMAGE IMPORT SELF-TEST: " + message);
    }
}
