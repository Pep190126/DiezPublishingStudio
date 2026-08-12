using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiezPublishingStudio;

/// <summary>
/// Manual clean-room transport for environments where a chat-based image renderer may inherit visual
/// state from the current conversation. One Prompt Pack remains the user's single transport object,
/// while every AI_ONLY Work Unit gets a self-contained clean-room task and partial-response contract.
/// The launcher guides the user through disposable Temporary/New Chats one at a time; Diez then imports
/// all partial responses together through the existing audited multi-ZIP importer.
/// </summary>
internal static class PromptPackCleanRoomQueueService
{
    public const string QueueProtocolVersion = "1.0";
    public const string QueueFileName = "clean-room-queue.json";
    public const string LauncherFileName = "00-CLEAN-ROOM-LAUNCHER.html";
    private const string TaskDirectory = "clean-room-tasks";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static void Apply(
        ZipArchive archive,
        PreviewProject project,
        int packageVersion,
        JsonObject promptManifest,
        JsonArray calls)
    {
        var projectId = promptManifest["project_id"]?.ToString() ?? project.ProjectId.ToString("D");
        var jobId = promptManifest["job_id"]?.ToString() ?? string.Empty;
        var promptPackId = promptManifest["prompt_pack_id"]?.ToString() ?? string.Empty;
        var bookTitle = BookPackageNamingService.BookTitle(project);
        var tasks = new JsonArray();
        var launcherTasks = new List<LauncherTask>();

        var callObjects = calls.OfType<JsonObject>().OrderBy(c => IntValue(c["order"], int.MaxValue)).ToList();
        for (var i = 0; i < callObjects.Count; i++)
        {
            var call = callObjects[i];
            var order = IntValue(call["order"], i + 1);
            var workUnitId = call["work_unit_id"]?.ToString() ?? string.Empty;
            var workUnitCode = call["work_unit_code"]?.ToString() ?? $"IMG-{order:D3}";
            var candidateVersion = IntValue(call["candidate_version"], 1);
            var renderRequestId = call["render_request_id"]?.ToString() ?? string.Empty;
            var promptFile = call["prompt_file"]?.ToString() ?? string.Empty;
            var promptSha = call["prompt_sha256"]?.ToString() ?? string.Empty;
            var rendererPrompt = ReadText(archive, promptFile).Trim();
            PromptPackRendererVisualBriefService.EnsureVisualOnly(rendererPrompt);

            var responseFile = BookPackageNamingService.ResponsePartFileName(project, packageVersion, order);
            var taskFile = $"{TaskDirectory}/{order:D3}-{SafeCode(workUnitCode, order)}.md";
            var taskText = BuildTaskMarkdown(
                bookTitle,
                order,
                callObjects.Count,
                projectId,
                jobId,
                promptPackId,
                workUnitId,
                workUnitCode,
                candidateVersion,
                renderRequestId,
                promptSha,
                responseFile,
                rendererPrompt);
            ReplaceText(archive, taskFile, taskText);

            tasks.Add(new JsonObject
            {
                ["order"] = order,
                ["work_unit_id"] = workUnitId,
                ["work_unit_code"] = workUnitCode,
                ["candidate_version"] = candidateVersion,
                ["task_file"] = taskFile,
                ["renderer_prompt_file"] = promptFile,
                ["renderer_prompt_sha256"] = promptSha,
                ["render_request_id"] = renderRequestId,
                ["partial_response_filename"] = responseFile,
                ["chat_policy"] = "NEW_TEMPORARY_OR_NEW_BLANK_CHAT",
                ["one_generation_attempt_per_clean_room"] = true,
                ["previous_images_allowed"] = false,
                ["response_partial"] = true
            });

            launcherTasks.Add(new LauncherTask(order, workUnitCode, responseFile, taskText));
        }

        var queue = new JsonObject
        {
            ["protocol"] = "diez-clean-room-queue",
            ["protocol_version"] = QueueProtocolVersion,
            ["project_id"] = projectId,
            ["book_title"] = bookTitle,
            ["prompt_pack_version"] = packageVersion,
            ["prompt_pack_id"] = promptPackId,
            ["job_id"] = jobId,
            ["total_tasks"] = tasks.Count,
            ["user_workflow"] = "ONE_GUIDED_QUEUE",
            ["chat_policy"] = "NEW_TEMPORARY_OR_NEW_BLANK_CHAT_PER_WORK_UNIT",
            ["same_chat_renderer_isolation_certified"] = false,
            ["partial_response_allowed"] = true,
            ["import_mode"] = "MULTI_SELECT_ALL_PARTIAL_RESPONSE_ZIPS_ONCE",
            ["launcher"] = LauncherFileName,
            ["tasks"] = tasks
        };

        ReplaceText(archive, QueueFileName, queue.ToJsonString(JsonOptions));
        ReplaceText(archive, LauncherFileName, BuildLauncher(bookTitle, packageVersion, launcherTasks));
    }

    private static string BuildTaskMarkdown(
        string bookTitle,
        int order,
        int total,
        string projectId,
        string jobId,
        string promptPackId,
        string workUnitId,
        string workUnitCode,
        int candidateVersion,
        string renderRequestId,
        string promptSha,
        string responseFile,
        string rendererPrompt)
    {
        var assetName = $"{SafeCode(workUnitCode, order)}-v{candidateVersion:D3}.png";
        var sb = new StringBuilder();
        sb.AppendLine($"# Diez clean-room task {order}/{total} — {workUnitCode}");
        sb.AppendLine();
        sb.AppendLine($"Book: {bookTitle}");
        sb.AppendLine();
        sb.AppendLine("This task is designed for a NEW Temporary Chat or NEW blank chat. Do not execute it in a conversation that already contains generated images from another Diez Work Unit or project.");
        sb.AppendLine();
        sb.AppendLine("## Execution contract");
        sb.AppendLine("1. Create exactly ONE new image-generation call for this Work Unit.");
        sb.AppendLine("2. Do not attach, reference, edit, continue, restyle or reuse any image from another conversation, project, Work Unit or earlier generation.");
        sb.AppendLine("3. When invoking the image renderer, send ONLY the text between `BEGIN DIEZ VISUAL PROMPT` and `END DIEZ VISUAL PROMPT`. The surrounding transport/audit text is for the chat executor only and must never be forwarded to the image model.");
        sb.AppendLine("4. Inspect the returned image against every HARD lock in the visual prompt. Do not call a wrong style, wrong line weight, wrong composition or wrong subject successful merely because the animal is recognizable.");
        sb.AppendLine("5. Use ONE generation attempt in this clean-room chat. If that attempt is non-compliant, do not edit/reuse it and do not keep retrying in the now-contaminated chat: return this Work Unit as `FAILED` with no asset. Diez can later issue a new clean-room retry only for the failed item.");
        sb.AppendLine($"6. If compliant, package only this one Work Unit as a PARTIAL Diez Response ZIP named `{responseFile}`.");
        sb.AppendLine();
        sb.AppendLine("## BEGIN DIEZ VISUAL PROMPT");
        sb.AppendLine(rendererPrompt);
        sb.AppendLine("## END DIEZ VISUAL PROMPT");
        sb.AppendLine();
        sb.AppendLine("## Partial Response identity — preserve exactly");
        sb.AppendLine($"- project_id: `{projectId}`");
        sb.AppendLine($"- job_id: `{jobId}`");
        sb.AppendLine($"- prompt_pack_id: `{promptPackId}`");
        sb.AppendLine($"- work_unit_id: `{workUnitId}`");
        sb.AppendLine($"- candidate_version: `{candidateVersion}`");
        sb.AppendLine("- content_type: `IMAGE`");
        sb.AppendLine($"- render_request_id: `{renderRequestId}`");
        sb.AppendLine($"- render_prompt_sha256: `{promptSha}`");
        sb.AppendLine($"- response filename: `{responseFile}`");
        sb.AppendLine();
        sb.AppendLine("## Response ZIP contract");
        sb.AppendLine("Create `response-manifest.json` at ZIP root and a `content/` directory. Generate a NEW UUID for `package_id`.");
        sb.AppendLine();
        sb.AppendLine($"If the image passes all HARD locks, use status `SUCCEEDED`, place the image at `content/{assetName}`, set `primary_asset` to that exact path, and set `failure_reason` to null.");
        sb.AppendLine();
        sb.AppendLine("If the image fails any HARD lock or a clean generation is unavailable, use status `FAILED`, set `primary_asset` to null, include no image asset, and explain the exact failure in `failure_reason`.");
        sb.AppendLine();
        sb.AppendLine("The manifest must have this shape (success example; switch asset/status/failure fields as described above for FAILED):");
        sb.AppendLine("```json");
        sb.AppendLine("{");
        sb.AppendLine("  \"protocol\": \"diez-response\",");
        sb.AppendLine("  \"protocol_version\": 1,");
        sb.AppendLine($"  \"project_id\": {JsonSerializer.Serialize(projectId)},");
        sb.AppendLine($"  \"job_id\": {JsonSerializer.Serialize(jobId)},");
        sb.AppendLine($"  \"prompt_pack_id\": {JsonSerializer.Serialize(promptPackId)},");
        sb.AppendLine("  \"package_id\": \"GENERATE-A-NEW-UUID\",");
        sb.AppendLine("  \"partial\": true,");
        sb.AppendLine("  \"items\": [");
        sb.AppendLine("    {");
        sb.AppendLine($"      \"work_unit_id\": {JsonSerializer.Serialize(workUnitId)},");
        sb.AppendLine($"      \"candidate_version\": {candidateVersion},");
        sb.AppendLine("      \"content_type\": \"IMAGE\",");
        sb.AppendLine("      \"status\": \"SUCCEEDED\",");
        sb.AppendLine($"      \"primary_asset\": {JsonSerializer.Serialize("content/" + assetName)},");
        sb.AppendLine("      \"description\": \"short factual description\",");
        sb.AppendLine($"      \"render_request_id\": {JsonSerializer.Serialize(renderRequestId)},");
        sb.AppendLine($"      \"render_prompt_sha256\": {JsonSerializer.Serialize(promptSha)},");
        sb.AppendLine("      \"failure_reason\": null");
        sb.AppendLine("    }");
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("Return only the ZIP for this task as the downloadable artifact. Do not combine other Work Units into this partial response.");
        return sb.ToString().Trim();
    }

    private static string BuildLauncher(string bookTitle, int version, IReadOnlyList<LauncherTask> tasks)
    {
        var html = new StringBuilder();
        html.AppendLine("<!doctype html>");
        html.AppendLine("<html lang=\"it\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        html.Append("<title>Diez clean-room queue — ").Append(WebUtility.HtmlEncode(bookTitle)).AppendLine("</title>");
        html.AppendLine("<style>");
        html.AppendLine("body{font-family:Segoe UI,Arial,sans-serif;max-width:1100px;margin:32px auto;padding:0 18px;line-height:1.45;color:#171717}");
        html.AppendLine("h1{margin-bottom:6px}.intro{background:#f5f5f5;border:1px solid #ddd;border-radius:12px;padding:16px;margin:18px 0}");
        html.AppendLine(".task{border:1px solid #d7d7d7;border-radius:12px;padding:16px;margin:18px 0;box-shadow:0 1px 3px rgba(0,0,0,.05)}");
        html.AppendLine(".task-head{display:flex;gap:12px;justify-content:space-between;align-items:center;flex-wrap:wrap;font-size:18px}.pill{font-size:13px;background:#f0f0f0;border-radius:999px;padding:5px 10px}");
        html.AppendLine("textarea{width:100%;height:260px;box-sizing:border-box;font-family:Consolas,monospace;font-size:12px;margin:10px 0;padding:10px}.actions{display:flex;gap:12px;align-items:center;flex-wrap:wrap}");
        html.AppendLine("button,.button{border:0;border-radius:8px;background:#1f6feb;color:white;padding:10px 14px;text-decoration:none;cursor:pointer;font-size:14px}label{padding:8px 0}#progress{font-weight:600}.warn{color:#8a4b00}");
        html.AppendLine("</style></head><body>");
        html.AppendLine("<h1>Diez clean-room queue</h1>");
        html.Append(WebUtility.HtmlEncode(bookTitle)).Append(" · Prompt Pack v").Append(version.ToString("D3")).AppendLine();
        html.AppendLine("<div class=\"intro\">");
        html.AppendLine("<p><strong>Un solo workflow, clean room reali.</strong> Questa pagina gestisce la sequenza; non usare una sola conversazione contenente tutte le immagini. Apri un task alla volta in una Temporary Chat (preferita) o nuova chat vuota, scarica il relativo Response ZIP e chiudi/abbandona quella chat prima del task successivo.</p>");
        html.AppendLine("<p class=\"warn\">Non serve costruire un Response finale a mano. Alla fine, in Diez premi <strong>Importa risultati AI</strong> e seleziona insieme tutti i Response <code>part-...zip</code>: l'importer li aggrega sullo stesso Prompt Pack.</p>");
        html.Append("<div id=\"progress\">0/").Append(tasks.Count).AppendLine(" Response parziali segnati come scaricati.</div></div>");

        foreach (var task in tasks)
        {
            var id = $"task-{task.Order:D3}";
            html.Append("<section class=\"task\" data-order=\"").Append(task.Order).AppendLine("\">");
            html.Append("<div class=\"task-head\"><strong>Task ").Append(task.Order).Append('/').Append(tasks.Count).Append(" · ").Append(WebUtility.HtmlEncode(task.WorkUnitCode)).Append("</strong><span class=\"pill\">Response: ").Append(WebUtility.HtmlEncode(task.ResponseFile)).AppendLine("</span></div>");
            html.AppendLine("<p>Apri una <strong>Temporary Chat</strong> (preferita) o una nuova chat vuota, poi copia e incolla il task completo. Quando hai scaricato lo ZIP parziale, torna qui e segna il task come completato.</p>");
            html.Append("<textarea id=\"").Append(id).Append("\" readonly>").Append(WebUtility.HtmlEncode(task.TaskText)).AppendLine("</textarea>");
            html.AppendLine("<div class=\"actions\">");
            html.Append("<button onclick=\"copyTask('").Append(id).AppendLine("',this)\">Copia task completo</button>");
            html.AppendLine("<a class=\"button\" href=\"https://chatgpt.com/\" target=\"_blank\" rel=\"noopener noreferrer\">Apri ChatGPT</a>");
            html.Append("<label><input type=\"checkbox\" onchange=\"markDone(").Append(task.Order).Append(",this.checked)\" id=\"done-").Append(task.Order).AppendLine("\"> ZIP parziale scaricato</label>");
            html.AppendLine("</div></section>");
        }

        var storageKey = $"diez-clean-room-{BookPackageNamingService.Slug(bookTitle)}-v{version:D3}";
        html.AppendLine("<script>");
        html.Append("const key=").Append(JsonSerializer.Serialize(storageKey)).AppendLine(";");
        html.Append("const total=").Append(tasks.Count).AppendLine(";");
        html.AppendLine("function load(){let s={};try{s=JSON.parse(localStorage.getItem(key)||'{}')}catch{};for(let i=1;i<=total;i++){const e=document.getElementById('done-'+i);if(e)e.checked=!!s[i]}update()}");
        html.AppendLine("function markDone(i,v){let s={};try{s=JSON.parse(localStorage.getItem(key)||'{}')}catch{};s[i]=v;localStorage.setItem(key,JSON.stringify(s));update()}");
        html.AppendLine("function update(){let n=0;for(let i=1;i<=total;i++){const e=document.getElementById('done-'+i);if(e&&e.checked)n++}document.getElementById('progress').textContent=n+'/'+total+' Response parziali segnati come scaricati.'}");
        html.AppendLine("async function copyTask(id,button){const el=document.getElementById(id);let ok=false;try{await navigator.clipboard.writeText(el.value);ok=true}catch{el.focus();el.select();try{ok=document.execCommand('copy')}catch{}}button.textContent=ok?'Copiato':'Seleziona e copia manualmente';setTimeout(()=>button.textContent='Copia task completo',1800)}");
        html.AppendLine("load();</script></body></html>");
        return html.ToString().Trim();
    }

    private static string ReadText(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path) ?? throw new InvalidOperationException("Prompt clean-room mancante: " + path);
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        return reader.ReadToEnd();
    }

    private static void ReplaceText(ZipArchive archive, string path, string text)
    {
        archive.GetEntry(path)?.Delete();
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(text ?? string.Empty);
    }

    private static int IntValue(JsonNode? node, int fallback) => int.TryParse(node?.ToString(), out var value) ? value : fallback;

    private static string SafeCode(string? code, int index)
    {
        var source = string.IsNullOrWhiteSpace(code) ? $"IMG-{index:D3}" : code.Trim();
        var chars = source.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-').ToArray();
        return new string(chars).Trim('-');
    }

    private sealed record LauncherTask(int Order, string WorkUnitCode, string ResponseFile, string TaskText);
}
