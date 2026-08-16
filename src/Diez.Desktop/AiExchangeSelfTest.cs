using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace DiezPublishingStudio;

internal static class AiExchangeSelfTest
{
    private static readonly byte[] PngA = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9ZK1sAAAAASUVORK5CYII=");
    private static readonly byte[] PngB = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "DiezAiExchangeSelfTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var projectPath = Path.Combine(root, "exchange.diez");
            var project = ProjectFileStore.Create("AI Exchange Test");
            BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);
            AiProductionService.CreateJob(project, AiProductionService.TypeImage, "Pagina 1", "Un gatto al parco");
            AiProductionService.CreateJob(project, AiProductionService.TypeImage, "Pagina 2", "Un gatto in bicicletta");
            await ProjectFileStore.SaveAsync(projectPath, project);

            var state = AiExchangeStateStore.Load(project);
            Require(state.WorkUnits.Count == 2, "I job AI legacy non vengono mappati in Work Unit.");
            Require(AiExchangeModes.All.Count == 5, "Le cinque modalità INPUT/AI non sono disponibili.");
            Require(AiExchangeModes.UserLabel(AiExchangeModes.AiWithInputAsReference).Contains("riferimento", StringComparison.OrdinalIgnoreCase),
                "La modalità di riferimento non ha un'etichetta utente chiara.");

            var context = AiExchangeStateStore.EnsureVisualConsistencyContext(project, state, true,
                "Mantieni personaggio, stile e tratto coerenti in tutta la raccolta.");
            Require(context.ConsistentEnabled, "Consistent non è stato attivato.");
            Require(state.WorkUnits.All(w => w.SharedContextIds.Contains(context.SharedContextId)),
                "Consistent non è applicato alle Work Unit immagine.");

            AiExchangeStateStore.Save(project, state);
            await ProjectFileStore.SaveAsync(projectPath, project);
            var reloaded = await ProjectFileStore.LoadAsync(projectPath);
            var persisted = AiExchangeStateStore.Load(reloaded);
            Require(persisted.WorkUnits.Count == 2 && persisted.SharedContexts.Any(c => c.ConsistentEnabled),
                "Lo stato AI Exchange non sopravvive al round-trip .diez.");

            state = persisted;
            project = reloaded;
            var packPath = Path.Combine(root, "prompt-pack.zip");
            var pack = await AiExchangePromptPackBuilder.BuildAsync(project, projectPath, state,
                state.WorkUnits.Select(w => w.WorkUnitId), packPath);
            Require(pack.Success && File.Exists(packPath), "Il Prompt Pack ZIP non è stato creato.");
            using (var zip = ZipFile.OpenRead(packPath))
            {
                Require(zip.GetEntry("prompt-manifest.json") is not null, "prompt-manifest.json mancante.");
                Require(zip.GetEntry("instructions.md") is not null, "instructions.md mancante.");
            }

            var unit = state.WorkUnits.OrderBy(w => w.Position).First();
            var snapshot = state.RequestSnapshots.Single(s => s.PromptPackId == pack.PromptPackId);
            var targetVersion = snapshot.Items.Single(i => i.WorkUnitId == unit.WorkUnitId).TargetCandidateVersion;

            var response1 = Path.Combine(root, "response-1.zip");
            await CreateResponseAsync(response1, project.ProjectId, snapshot.JobId, pack.PromptPackId, "PKG-001",
                unit.WorkUnitId, targetVersion, PngA, description: null);
            var first = await AiExchangeResponseImporter.ImportAsync(project, projectPath, state, [response1]);
            Require(first.Incomplete == 1, "Un'immagine senza descrizione deve essere importata come incompleta.");
            var v1 = state.Versions.Single(v => v.WorkUnitId == unit.WorkUnitId && v.VersionNumber == targetVersion);
            Require(v1.DescriptionStatus == AiExchangeDescriptionStatuses.Missing, "La descrizione mancante non è registrata.");

            var response2 = Path.Combine(root, "response-2.zip");
            await CreateResponseAsync(response2, project.ProjectId, snapshot.JobId, pack.PromptPackId, "PKG-002",
                unit.WorkUnitId, targetVersion, null, "Il gatto gioca al parco vicino a un albero.");
            var second = await AiExchangeResponseImporter.ImportAsync(project, projectPath, state, [response2]);
            Require(second.Imported == 1, "Il secondo ZIP non completa la Candidate esistente.");
            Require(state.Versions.Count(v => v.WorkUnitId == unit.WorkUnitId && v.VersionNumber == targetVersion) == 1,
                "Completare un risultato parziale ha creato una versione duplicata.");
            v1 = state.Versions.Single(v => v.WorkUnitId == unit.WorkUnitId && v.VersionNumber == targetVersion);
            Require(v1.DescriptionStatus == AiExchangeDescriptionStatuses.Valid && v1.Status == AiExchangeVersionStatuses.Candidate,
                "La Candidate completata non è pronta alla revisione.");

            Require(AiExchangeResultIngestor.Approve(project, state, v1.VersionId, out _), "L'approvazione della Candidate valida è fallita.");
            Require(unit.ApprovedVersionId == v1.VersionId, "La versione approvata non è collegata alla Work Unit.");

            var duplicate = await AiExchangeResponseImporter.ImportAsync(project, projectPath, state, [response2]);
            Require(duplicate.Duplicates == 1, "Il package già importato non viene riconosciuto.");

            var conflictZip = Path.Combine(root, "response-conflict.zip");
            await CreateResponseAsync(conflictZip, project.ProjectId, snapshot.JobId, pack.PromptPackId, "PKG-003",
                unit.WorkUnitId, targetVersion, PngB, "Descrizione diversa");
            var conflict = await AiExchangeResponseImporter.ImportAsync(project, projectPath, state, [conflictZip]);
            Require(conflict.Conflicts == 1, "Stessa versione con asset diverso non produce CONFLICT.");

            var editPath = Path.Combine(root, "external-edit.png");
            await File.WriteAllBytesAsync(editPath, PngB);
            var editedMaterial = await MaterialImporter.ImportAsync(editPath);
            project.Materials.Add(editedMaterial);
            var external = AiExchangeResultIngestor.RegisterExternalEdit(project, state, v1.VersionId, editedMaterial);
            Require(external.VersionNumber == targetVersion + 1, "La modifica esterna non crea una nuova versione.");
            Require(external.DescriptionStatus == AiExchangeDescriptionStatuses.NeedsVerification,
                "La modifica esterna immagine non invalida la verifica descrizione.");

            var previousContextVersion = context.Version;
            context = AiExchangeStateStore.EnsureVisualConsistencyContext(project, state, true,
                "Mantieni personaggio, stile, tratto e palette coerenti in tutta la raccolta.");
            Require(context.Version > previousContextVersion, "La modifica di Consistent non incrementa la versione del contesto.");
            AiExchangeResultIngestor.MarkContextDependentsStale(state, context.SharedContextId, previousContextVersion);

            var apiUnit = state.WorkUnits.OrderBy(w => w.Position).Skip(1).First();
            var apiSnapshot = new AiExchangeRequestSnapshot
            {
                JobId = apiUnit.JobId,
                PromptPackId = Guid.NewGuid(),
                Transport = "API",
                CreatedAtLocal = DateTimeOffset.Now.ToString("O"),
                Items =
                [
                    new AiExchangeSnapshotItem
                    {
                        WorkUnitId = apiUnit.WorkUnitId,
                        TargetCandidateVersion = AiExchangeStateStore.NextVersionNumber(state, apiUnit.WorkUnitId)
                    }
                ]
            };
            state.RequestSnapshots.Add(apiSnapshot);
            var apiPath = Path.Combine(root, "api-result.png");
            await File.WriteAllBytesAsync(apiPath, PngA);
            var mock = new AiExchangeMockApiAdapter();
            var timeout = await mock.RunAttemptAsync(project, state, apiSnapshot, apiUnit.WorkUnitId, "TIMEOUT");
            Require(timeout is null, "Il mock TIMEOUT dovrebbe non produrre risultato.");
            var apiResult = await mock.RunAttemptAsync(project, state, apiSnapshot, apiUnit.WorkUnitId, "SUCCESS", apiPath,
                description: "Il gatto va in bicicletta.");
            Require(apiResult?.Status == "IMPORTED", "Il risultato Mock API non passa dallo stesso ResultIngestor.");
            Require(mock.Attempts == 2, "Il mock non registra i retry.");
            var apiVersion = state.Versions.Single(v => v.WorkUnitId == apiUnit.WorkUnitId);
            Require(apiVersion.VersionNumber == apiSnapshot.Items[0].TargetCandidateVersion,
                "Un retry API ha alterato la versione editoriale.");
            Require(apiVersion.Origin == AiExchangeOrigins.AiApi, "La provenance API non è conservata.");

            AiExchangeStateStore.Save(project, state);
            await ProjectFileStore.SaveAsync(projectPath, project);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static async Task CreateResponseAsync(
        string path,
        Guid projectId,
        Guid jobId,
        Guid promptPackId,
        string packageId,
        Guid workUnitId,
        int candidateVersion,
        byte[]? image,
        string? description)
    {
        await using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        var primary = image is null ? string.Empty : "content/result.png";
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
                    status = !string.IsNullOrWhiteSpace(description) ? "COMPLETE" : "INCOMPLETE",
                    primary_asset = primary,
                    description = description ?? string.Empty
                }
            }
        };
        var manifestEntry = zip.CreateEntry("response-manifest.json");
        await using (var target = manifestEntry.Open())
        await using (var writer = new StreamWriter(target, new UTF8Encoding(false)))
            await writer.WriteAsync(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        if (image is not null)
        {
            var imageEntry = zip.CreateEntry(primary);
            await using var target = imageEntry.Open();
            await target.WriteAsync(image);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("AI EXCHANGE SELF-TEST: " + message);
    }
}
