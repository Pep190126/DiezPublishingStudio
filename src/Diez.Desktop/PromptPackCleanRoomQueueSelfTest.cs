using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiezPublishingStudio;

internal static class PromptPackCleanRoomQueueSelfTest
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "DiezCleanRoomQueueTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var project = ProjectFileStore.Create("Internal");
            project.EditionMetadata.Title = "Animali della Giungla";
            var projectId = project.ProjectId.ToString("D");
            var jobId = Guid.NewGuid().ToString("D");
            var packId = Guid.NewGuid().ToString("D");
            var zipPath = Path.Combine(root, "pack.zip");
            var subjects = new[] { "monkey", "tiger", "elephant" };
            var calls = new JsonArray();

            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                for (var i = 0; i < subjects.Length; i++)
                {
                    var order = i + 1;
                    var code = $"IMG-{order:D3}";
                    var unitId = Guid.NewGuid().ToString("D");
                    var renderId = Guid.NewGuid().ToString("D");
                    var promptFile = $"render-prompts/{order:D3}-{code}.txt";
                    var prompt = $"""
Create ONE finished, publication-quality coloring-book illustration.
PRIMARY SUBJECT — HARD LOCK: one {subjects[i]}.
COMPOSITION — HARD LOCK: one continuous unified primary scene filling the canvas, centered on the single atomic subject.
STYLE — HARD LOCK: Kawaii. Use unmistakably cute Kawaii design with simplified rounded forms and friendly expressive features.
BOLD & EASY — HARD: OFF.
COZY — HARD: ON.
LINE WEIGHT — HARD: use visibly thin, fine, crisp black contours throughout.
COLOR OUTPUT — HARD: final raster uses exactly pure black #000000 and pure white #FFFFFF.
TECHNICAL OUTPUT: target raster 2550 × 3300 px; 300 DPI print context.
""".Trim();
                    WriteText(zip, promptFile, prompt);
                    calls.Add(new JsonObject
                    {
                        ["order"] = order,
                        ["work_unit_id"] = unitId,
                        ["work_unit_code"] = code,
                        ["candidate_version"] = 1,
                        ["render_request_id"] = renderId,
                        ["prompt_file"] = promptFile,
                        ["prompt_sha256"] = new string((char)('a' + i), 64)
                    });
                }
            }

            var manifest = new JsonObject
            {
                ["project_id"] = projectId,
                ["job_id"] = jobId,
                ["prompt_pack_id"] = packId
            };

            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update))
                PromptPackCleanRoomQueueService.Apply(zip, project, 1, manifest, calls);

            using var result = ZipFile.OpenRead(zipPath);
            Require(result.GetEntry(PromptPackCleanRoomQueueService.QueueFileName) is not null, "clean-room-queue.json mancante.");
            Require(result.GetEntry(PromptPackCleanRoomQueueService.LauncherFileName) is not null, "launcher HTML mancante.");

            var queueText = await ReadAsync(result, PromptPackCleanRoomQueueService.QueueFileName);
            using var queue = JsonDocument.Parse(queueText);
            var rootNode = queue.RootElement;
            Require(rootNode.GetProperty("protocol").GetString() == "diez-clean-room-queue", "Protocollo queue errato.");
            Require(rootNode.GetProperty("protocol_version").GetString() == PromptPackCleanRoomQueueService.QueueProtocolVersion, "Versione queue errata.");
            Require(rootNode.GetProperty("total_tasks").GetInt32() == 3, "La queue non contiene tre task.");
            Require(rootNode.GetProperty("user_workflow").GetString() == "ONE_GUIDED_QUEUE", "Workflow utente non dichiarato come coda unica.");
            Require(rootNode.GetProperty("chat_policy").GetString() == "NEW_TEMPORARY_OR_NEW_BLANK_CHAT_PER_WORK_UNIT", "Policy clean-room non esplicita.");
            Require(!rootNode.GetProperty("same_chat_renderer_isolation_certified").GetBoolean(), "Same-chat isolation non deve risultare certificato.");
            Require(rootNode.GetProperty("partial_response_allowed").GetBoolean(), "Response parziali non abilitate.");
            Require(rootNode.GetProperty("bundle_protocol").GetString() == AiExchangeResponseBundleService.Protocol, "Protocollo Response Bundle errato.");
            Require(rootNode.GetProperty("bundle_protocol_version").GetInt32() == AiExchangeResponseBundleService.ProtocolVersion, "Versione Response Bundle errata.");
            Require(rootNode.GetProperty("bundle_manifest").GetString() == AiExchangeResponseBundleService.ManifestFileName, "Manifest Response Bundle errato.");
            Require(rootNode.GetProperty("bundle_filename").GetString() == BookPackageNamingService.ResponseFileName(project, 1), "Nome bundle finale errato.");
            Require(rootNode.GetProperty("final_transport").GetString() == "ONE_OUTER_ZIP_WITH_N_PARTIAL_RESPONSE_ZIPS", "Trasporto finale non dichiarato come ZIP annidato unico.");
            Require(rootNode.GetProperty("import_mode").GetString() == "SINGLE_RESPONSE_BUNDLE_PREFERRED_OR_MULTI_SELECT_PARTS", "Import bundle-preferred non dichiarato.");

            var tasks = rootNode.GetProperty("tasks").EnumerateArray().ToList();
            for (var i = 0; i < tasks.Count; i++)
            {
                var order = i + 1;
                var expectedResponse = BookPackageNamingService.ResponsePartFileName(project, 1, order);
                Require(tasks[i].GetProperty("partial_response_filename").GetString() == expectedResponse, "Naming Response parziale errato.");
                Require(tasks[i].GetProperty("bundle_entry").GetString() == AiExchangeResponseBundleService.PartsDirectory + expectedResponse, "Entry annidata errata.");
                Require(tasks[i].GetProperty("one_generation_attempt_per_clean_room").GetBoolean(), "Il task deve limitarsi a un tentativo per clean room.");
                Require(!tasks[i].GetProperty("previous_images_allowed").GetBoolean(), "Immagini precedenti non devono essere ammesse.");
                Require(tasks[i].GetProperty("response_partial").GetBoolean(), "Il task deve richiedere partial=true.");

                var taskFile = tasks[i].GetProperty("task_file").GetString() ?? string.Empty;
                var text = await ReadAsync(result, taskFile);
                Require(text.Contains("NEW Temporary Chat or NEW blank chat", StringComparison.OrdinalIgnoreCase), "Task non richiede clean room reale.");
                Require(text.Contains("send ONLY the text between", StringComparison.OrdinalIgnoreCase), "Boundary executor→renderer non esplicito.");
                Require(text.Contains("ONE generation attempt", StringComparison.OrdinalIgnoreCase), "Task non impedisce retry contaminanti nella stessa chat.");
                Require(text.Contains($"PRIMARY SUBJECT — HARD LOCK: one {subjects[i]}", StringComparison.OrdinalIgnoreCase), "Prompt visuale sbagliato nel task.");
                Require(text.Contains("\"partial\": true", StringComparison.Ordinal), "Contratto partial response assente.");
                Require(text.Contains(expectedResponse, StringComparison.Ordinal), "Nome ZIP parziale assente dal task.");
                for (var j = 0; j < subjects.Length; j++)
                    if (j != i)
                        Require(!text.Contains($"PRIMARY SUBJECT — HARD LOCK: one {subjects[j]}", StringComparison.OrdinalIgnoreCase), "Task contaminato dal soggetto di un'altra Work Unit.");
            }

            var launcher = await ReadAsync(result, PromptPackCleanRoomQueueService.LauncherFileName);
            Require(launcher.Contains("Diez clean-room queue", StringComparison.OrdinalIgnoreCase), "Launcher non identificabile.");
            Require(launcher.Contains("https://chatgpt.com/", StringComparison.OrdinalIgnoreCase), "Launcher non apre ChatGPT.");
            Require(launcher.Contains("Temporary Chat", StringComparison.OrdinalIgnoreCase), "Launcher non guida alla Temporary Chat.");
            Require(launcher.Contains("Crea Response ZIP unico", StringComparison.OrdinalIgnoreCase), "Launcher non crea il bundle finale unico.");
            Require(launcher.Contains("response-bundle-manifest.json", StringComparison.OrdinalIgnoreCase), "Launcher non genera il manifest bundle.");
            Require(launcher.Contains("diez-response-bundle", StringComparison.OrdinalIgnoreCase), "Launcher non dichiara il protocollo bundle.");
            Require(launcher.Contains("makeStoredZip", StringComparison.Ordinal), "Launcher non contiene il writer ZIP locale senza dipendenze.");
            Require(launcher.Contains("localStorage", StringComparison.Ordinal), "Launcher non conserva il progresso locale della coda.");
            Require(launcher.Contains(BookPackageNamingService.ResponseFileName(project, 1), StringComparison.Ordinal), "Launcher non mostra il Response Bundle finale.");
            Require(launcher.Contains(BookPackageNamingService.ResponsePartFileName(project, 1, 1), StringComparison.Ordinal), "Launcher non mostra il primo Response parziale.");
            Require(launcher.Contains(BookPackageNamingService.ResponsePartFileName(project, 1, 3), StringComparison.Ordinal), "Launcher non mostra l'ultimo Response parziale.");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static void WriteText(ZipArchive zip, string path, string text)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(text);
    }

    private static async Task<string> ReadAsync(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path) ?? throw new InvalidOperationException("Entry mancante: " + path);
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        return await reader.ReadToEndAsync();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("CLEAN ROOM QUEUE SELF-TEST: " + message);
    }
}
