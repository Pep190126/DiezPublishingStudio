using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiezPublishingStudio;

internal static class PromptPackLocalImageHandoffSelfTest
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "DiezLocalImageHandoff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var zipPath = Path.Combine(root, "pack.zip");
            var projectId = Guid.NewGuid().ToString("D");
            var jobId = Guid.NewGuid().ToString("D");
            var packId = Guid.NewGuid().ToString("D");
            var tasks = new JsonArray();

            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                for (var i = 1; i <= 3; i++)
                {
                    var code = $"IMG-{i:D3}";
                    var unitId = Guid.NewGuid().ToString("D");
                    var promptFile = $"render-prompts/{i:D3}-{code}.txt";
                    WriteText(zip, promptFile, $"""
Create ONE finished, publication-quality coloring-book illustration.
PRIMARY SUBJECT — HARD LOCK: one {(i == 1 ? "cat" : i == 2 ? "dog" : "rabbit")}.
COMPOSITION — HARD LOCK: one continuous unified primary scene filling the canvas, centered on the single atomic subject.
STYLE — HARD LOCK: Cute & Playful.
BOLD & EASY — HARD: ON.
COZY — HARD: ON.
""".Trim());
                    tasks.Add(new JsonObject
                    {
                        ["order"] = i,
                        ["work_unit_id"] = unitId,
                        ["work_unit_code"] = code,
                        ["candidate_version"] = 1,
                        ["renderer_prompt_file"] = promptFile,
                        ["renderer_prompt_sha256"] = new string((char)('a' + i - 1), 64),
                        ["render_request_id"] = Guid.NewGuid().ToString("D"),
                        ["partial_response_filename"] = $"response-part-{i:D3}.zip",
                        ["bundle_entry"] = $"parts/response-part-{i:D3}.zip",
                        ["chat_policy"] = "NEW_TEMPORARY_OR_NEW_BLANK_CHAT"
                    });
                }

                var queue = new JsonObject
                {
                    ["protocol"] = "diez-clean-room-queue",
                    ["protocol_version"] = "1.0",
                    ["project_id"] = projectId,
                    ["job_id"] = jobId,
                    ["prompt_pack_id"] = packId,
                    ["book_title"] = "Tre Animali",
                    ["bundle_filename"] = "diez-tre-animali-response-v001.zip",
                    ["tasks"] = tasks
                };
                WriteText(zip, "clean-room-queue.json", queue.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                WriteText(zip, "00-CLEAN-ROOM-LAUNCHER.html", "legacy launcher");
                WriteText(zip, "00-START-HERE.md", "legacy start");
                WriteText(zip, "instructions.md", "legacy instructions");
            }

            PromptPackLocalImageHandoffService.Apply(zipPath);

            using var result = ZipFile.OpenRead(zipPath);
            var queueText = await ReadAsync(result, "clean-room-queue.json");
            using var queueDoc = JsonDocument.Parse(queueText);
            var queueRoot = queueDoc.RootElement;
            Require(queueRoot.GetProperty("local_image_handoff_protocol").GetString() == "diez-local-image-handoff",
                "Protocollo handoff locale mancante.");
            Require(queueRoot.GetProperty("conversation_orchestration_owner").GetString() == "USER_LOCAL_LAUNCHER",
                "Il launcher non è proprietario dell'orchestrazione tra chat.");
            Require(queueRoot.GetProperty("partial_response_packaging_owner").GetString() == "LOCAL_LAUNCHER",
                "Il packaging Response non è stato spostato fuori dalla chat.");
            Require(queueRoot.GetProperty("chat_executor_must_not_build_response_zip").GetBoolean(),
                "La chat può ancora essere incaricata di costruire ZIP.");

            foreach (var task in queueRoot.GetProperty("tasks").EnumerateArray())
            {
                Require(task.GetProperty("chat_policy").GetString() == "USER_OPENS_NEW_BLANK_CHAT",
                    "La nuova chat non è responsabilità esplicita dell'utente/launcher.");
                Require(task.GetProperty("chat_executor_output").GetString() == "ONE_IMAGE_FILE_ONLY",
                    "La chat non è limitata a un solo asset immagine.");
                Require(task.GetProperty("local_launcher_builds_partial_response").GetBoolean(),
                    "Il partial Response non è costruito localmente.");
            }

            var launcher = await ReadAsync(result, "00-CLEAN-ROOM-LAUNCHER.html");
            Require(launcher.Contains("Copia solo prompt immagine", StringComparison.OrdinalIgnoreCase),
                "Launcher senza boundary prompt-only.");
            Require(launcher.Contains("Apri nuova chat", StringComparison.OrdinalIgnoreCase),
                "Launcher non guida l'apertura della chat separata.");
            Require(launcher.Contains("Crea Response ZIP unico", StringComparison.OrdinalIgnoreCase),
                "Launcher non crea il bundle finale.");
            Require(launcher.Contains("response-manifest.json", StringComparison.OrdinalIgnoreCase) &&
                    launcher.Contains("diez-response-bundle", StringComparison.OrdinalIgnoreCase),
                "Packaging locale incompleto.");
            Require(!launcher.Contains("Copia task completo", StringComparison.OrdinalIgnoreCase),
                "Il vecchio task con trasporto/audit è ancora proposto alla chat.");
            Require(launcher.Contains("Non caricare l'intero Prompt Pack", StringComparison.OrdinalIgnoreCase),
                "Launcher non impedisce l'esecuzione wholesale del pack.");

            var start = await ReadAsync(result, "00-START-HERE.md");
            Require(start.Contains("Do not upload this entire Prompt Pack", StringComparison.OrdinalIgnoreCase),
                "START HERE non vieta l'upload wholesale in una generation chat.");
            Require(start.Contains("generation chats return IMAGE ONLY", StringComparison.OrdinalIgnoreCase),
                "START HERE non dichiara image-only output.");

            var instructions = await ReadAsync(result, "instructions.md");
            Require(instructions.Contains("Diez local image handoff — HARD", StringComparison.OrdinalIgnoreCase),
                "Instructions senza override manuale autorevole.");
            Require(instructions.Contains("returns ONLY one image file", StringComparison.OrdinalIgnoreCase),
                "Instructions non separano generazione e packaging.");
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
        if (!condition) throw new InvalidOperationException("LOCAL IMAGE HANDOFF SELF-TEST: " + message);
    }
}
