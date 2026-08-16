using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace DiezPublishingStudio;

internal static class VisualAssetValidationSelfTest
{
    private static readonly byte[] GoodColoringPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAAaklEQVR42u2XwRIAEAhE+/+f5uRGZKOYbcbF1OzLUJESbEIAAnQ3RY4sE4B7pl8BWI/ZDWAmovnAACvimi8EYBEfxcAA6MXbBkBfRIsnAAEI8G4dSFEJw3tBim6YYh64OhGFDqX8FxDgplV2O053240oowAAAABJRU5ErkJggg==");

    // 32x32 full-color gradient. The manifest in the integration test lies and describes it as
    // pure B/W coloring line art; Diez must trust the real pixels instead of that description.
    private static readonly byte[] ColorLandscapeLikePng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAEr0lEQVR42hXOIQi2iAEA0L8MBldcmuVAls4yEK545TBccWWzDGTJNAxXvHIYLsgl04EsmYZhQS5ZjUaj1Wg0Wt37wuvvy5cvf3//SEBIRExCSkZOQUlFTUNLR8/AyMTMwsrGzsHJxc3Dy5cvfxAgICQiJiElI6egpKKmoaWjZ2BkYmZhZWPn4OTi5uH9wyfwlQABIRExCSkZOQUlFTUNLR09AyMTMwsrGzsHJxc3D+9Xn8CfBAgIiYhJSMnIKSipqGlo6egZGJmYWVjZ2Dk4ubh5eP/0CfxZgICQiJiElIycgpKKmoaWjp6BkYmZhZWNnYOTi5uH98+fwNcCBIRExCSkZOQUlFTUNLR09AyMTMwsrGzsHJxc3Dy8X38CfxEgICQiJiElI6egpKKmoaWjZ2BkYmZhZWPn4OTi5uH9yyfwjQABIRExCSkZOQUlFTUNLR09AyMTMwsrGzsHJxc3D+83n8BfBQgIiYhJSMnIKSipqGlo6egZGJmYWVjZ2Dk4ubh5eP/6CXwrQEBIRExCSkZOQUlFTUNLR8/AyMTMwsrGzsHJxc3D++0n8J0AASERMQkpGTkFJRU1DS0dPQMjEzMLKxs7BycXNw/vd5/A9wIEhETEJKRk5BSUVNQ0tHT0DIxMzCysbOwcnFzcPLzffwI/CBAQEhGTkJKRU1BSUdPQ0tEzMDIxs7CysXNwcnHz8P7wCfxNgICQiJiElIycgpKKmoaWjp6BkYmZhZWNnYOTi5uH92+fwD8ECAiJiElIycgpKKmoaWjp6BkYmZhZWNnYOTi5uHl4//EJ/FOAgJCImISUjJyCkoqahpaOnoGRiZmFlY2dg5OLm4f3n5/AvwQICImISUjJyCkoqahpaOnoGRiZmFlY2dg5OLm4eXj/9QlUAgSERMQkpGTkFJRU1DS0dPQMjEzMLKxs7BycXNw8vNUn8G8BAkIiYhJSMnIKSipqGlo6egZGJmYWVjZ2Dk4ubh7ef38CPwoQEBIRk5CSkVNQUlHT0NLRMzAyMbOwsrFzcHJx8/D++An8JEBASERMQkpGTkFJRU1DS0fPwMjEzMLKxs7BycXNw/vTJ/CzAAEhETEJKRk5BSUVNQ0tHT0DIxMzCysbOwcnFzcP78+fwC8CBIRExCSkZOQUlFTUNLR09AyMTMwsrGzsHJxc3Dy8v3wCvwoQEBIRk5CSkVNQUlHT0NLRMzAyMbOwsrFzcHJx8/D++gn0AgSERMQkpGTkFJRU1DS0dPQMjEzMLKxs7BycXNw8vP0n8JsAASERMQkpGTkFJRU1DS0dPQMjEzMLKxs7BycXNw/vb5/AfwQICImISUjJyCkoqahpaOnoGRiZmFlY2dg5OLm4eXj/8wmMAgSERMQkpGTkFJRU1DS0dPQMjEzMLKxs7BycXNw8vOMn8F8BAkIiYhJSMnIKSipqGlo6egZGJmYWVjZ2Dk4ubh7e/34C/xMgICQiJiElI6egpKKmoaWjZ2BkYmZhZWPn4OTi5uH93yfwuwABIRExCSkZOQUlFTUNLR09AyMTMwsrGzsHJxc3D+/vn8AiQEBIRExCSkZOQUlFTUNLR8/AyMTMwsrGzsHJxc3Dy/8BeDWeuUU9UtsAAAAASUVORK5CYII=");

    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "DiezVisualValidation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var good = Path.Combine(root, "good.png");
            var bad = Path.Combine(root, "bad.png");
            await File.WriteAllBytesAsync(good, GoodColoringPng);
            await File.WriteAllBytesAsync(bad, ColorLandscapeLikePng);

            var direct = ProjectFileStore.Create("Direct Pixel Validation");
            BookTypeProfileService.Set(direct, BookTypeProfileService.ColoringBook);
            var directJobs = AiImageBatchService.CreateImageSeries(direct, 1, "jungle animal", "Page").ToList();
            VisualPromptSessionService.EnsureActive(direct);
            var directState = AiExchangeStateStore.Load(direct);
            var unit = directState.WorkUnits.Single(w => w.LegacyAiJobId == directJobs[0].JobId);

            var goodResult = VisualAssetValidationService.Validate(direct, unit, good);
            Require(goodResult.Status == VisualAssetValidationStatuses.Passed && !goodResult.BlocksApproval,
                "Un raster B/N realmente colorabile viene rifiutato: " + goodResult.Message);
            var badResult = VisualAssetValidationService.Validate(direct, unit, bad);
            Require(badResult.Status == VisualAssetValidationStatuses.Failed && badResult.BlocksApproval,
                "Un raster a colori non viene respinto dal profilo Coloring: " + badResult.Message);
            Require(badResult.ChromaticRatio > 0.10,
                "Il test a colori non produce sufficiente evidenza cromatica per validare il detector.");

            // Full importer integration: the provider manifest claims B/W compliance, but the file is colorful.
            var projectPath = Path.Combine(root, "misleading.diez");
            var project = ProjectFileStore.Create("Misleading Response Validation");
            BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);
            _ = AiImageBatchService.CreateImageSeries(project, 1, "jungle animal", "Page").ToList();
            VisualPromptSessionService.EnsureActive(project);
            await ProjectFileStore.SaveAsync(projectPath, project);
            var state = AiExchangeStateStore.Load(project);
            var active = VisualPromptSessionService.ActiveLegacyJobIds(project);
            var activeUnit = state.WorkUnits.Single(w => w.LegacyAiJobId.HasValue && active.Contains(w.LegacyAiJobId.Value));

            var packPath = Path.Combine(root, "prompt.zip");
            var pack = await AiExchangePromptPackBuilder.BuildAsync(project, projectPath, state, [activeUnit.WorkUnitId], packPath);
            Require(pack.Success, "Prompt Pack di test non creato.");
            var snapshot = state.RequestSnapshots.Single(s => s.PromptPackId == pack.PromptPackId);
            var candidate = snapshot.Items.Single().TargetCandidateVersion;

            var responsePath = Path.Combine(root, "lying-response.zip");
            await using (var stream = File.Create(responsePath))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                var manifest = new
                {
                    protocol = "diez-response",
                    protocol_version = 1,
                    project_id = project.ProjectId,
                    job_id = snapshot.JobId,
                    prompt_pack_id = pack.PromptPackId,
                    package_id = "MISLEADING-COLOR-AS-BW",
                    partial = false,
                    items = new[]
                    {
                        new
                        {
                            work_unit_id = activeUnit.WorkUnitId,
                            candidate_version = candidate,
                            content_type = "IMAGE",
                            status = "COMPLETE",
                            primary_asset = "content/IMG-001.png",
                            description = "Professional pure black and white jungle-animal coloring page. Only #000000 and #FFFFFF."
                        }
                    }
                };
                var manifestEntry = zip.CreateEntry("response-manifest.json");
                await using (var target = manifestEntry.Open())
                await using (var writer = new StreamWriter(target, new UTF8Encoding(false)))
                    await writer.WriteAsync(JsonSerializer.Serialize(manifest));
                var imageEntry = zip.CreateEntry("content/IMG-001.png");
                await using var imageTarget = imageEntry.Open();
                await imageTarget.WriteAsync(ColorLandscapeLikePng);
            }

            var imported = await AiExchangeResponseImportV2.ImportAsync(project, projectPath, state, [responsePath]);
            Require(imported.Summary.Imported == 0,
                "L'asset a colori è stato contato come pronto nonostante il fallimento pixel-level. " + imported.Message);
            Require(imported.Summary.Incomplete == 1,
                "L'asset a colori deve diventare esattamente una Candidate incompleta/da correggere. " + imported.Message);
            var version = state.Versions.Single(v => v.WorkUnitId == activeUnit.WorkUnitId && v.VersionNumber == candidate);
            Require(version.MaterialId.HasValue,
                "Il risultato non conforme deve essere conservato per la revisione visuale, non scartato.");
            Require(version.Status == AiExchangeVersionStatuses.Incomplete &&
                    version.DescriptionStatus == AiExchangeDescriptionStatuses.NeedsVerification,
                "La Candidate non conforme non viene bloccata correttamente.");
            var validation = VisualAssetValidationStore.Get(project, version.VersionId);
            Require(validation?.Status == VisualAssetValidationStatuses.Failed && validation.BlocksApproval,
                "Il report persistente di validazione non registra il fallimento reale.");
            Require(imported.Message.Contains("pixel", StringComparison.OrdinalIgnoreCase) ||
                    imported.Message.Contains("cromatic", StringComparison.OrdinalIgnoreCase),
                "Il messaggio import non spiega perché il file reale è stato bloccato.");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("VISUAL ASSET VALIDATION SELF-TEST: " + message);
    }
}
