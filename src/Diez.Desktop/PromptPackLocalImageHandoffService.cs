using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiezPublishingStudio;

/// <summary>
/// Final manual-browser handoff for providers whose image renderer may inherit visual state from the
/// current conversation. A generation chat receives ONLY one VISUAL-ONLY prompt and returns ONLY an
/// image file. Identity, response manifests, partial ZIPs and the outer Response Bundle are built
/// locally by the launcher, never by the generation chat.
/// </summary>
internal static class PromptPackLocalImageHandoffService
{
    public const string ProtocolVersion = "1.0";
    private const string QueueFileName = "clean-room-queue.json";
    private const string LauncherFileName = "00-CLEAN-ROOM-LAUNCHER.html";
    private const string StartHereFileName = "00-START-HERE.md";
    private const string InstructionsFileName = "instructions.md";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static void Apply(string promptPackPath)
    {
        if (!File.Exists(promptPackPath))
            throw new FileNotFoundException("Prompt Pack non trovato.", promptPackPath);

        using var archive = ZipFile.Open(promptPackPath, ZipArchiveMode.Update);
        var queue = ReadObject(archive, QueueFileName)
            ?? throw new InvalidOperationException("clean-room-queue.json mancante o non valido.");
        var taskNodes = queue["tasks"] as JsonArray
            ?? throw new InvalidOperationException("clean-room-queue.json senza tasks.");

        var projectId = queue["project_id"]?.ToString() ?? string.Empty;
        var promptPackId = queue["prompt_pack_id"]?.ToString() ?? string.Empty;
        var jobId = queue["job_id"]?.ToString() ?? string.Empty;
        var bookTitle = queue["book_title"]?.ToString() ?? "Diez";
        var bundleFile = queue["bundle_filename"]?.ToString() ?? "diez-response.zip";
        var tasks = new List<TaskInfo>();

        foreach (var node in taskNodes.OfType<JsonObject>().OrderBy(n => IntValue(n["order"], int.MaxValue)))
        {
            var order = IntValue(node["order"], tasks.Count + 1);
            var code = node["work_unit_code"]?.ToString() ?? $"IMG-{order:D3}";
            var promptFile = node["renderer_prompt_file"]?.ToString() ?? string.Empty;
            var prompt = ReadText(archive, promptFile).Trim();
            PromptPackRendererVisualBriefService.EnsureVisualOnly(prompt);

            var info = new TaskInfo(
                order,
                node["work_unit_id"]?.ToString() ?? string.Empty,
                code,
                IntValue(node["candidate_version"], 1),
                node["render_request_id"]?.ToString() ?? string.Empty,
                node["renderer_prompt_sha256"]?.ToString() ?? string.Empty,
                node["partial_response_filename"]?.ToString() ?? $"response-part-{order:D3}.zip",
                node["bundle_entry"]?.ToString() ?? $"parts/response-part-{order:D3}.zip",
                prompt,
                DefaultDescription(code, prompt));
            tasks.Add(info);

            node["chat_policy"] = "USER_OPENS_NEW_BLANK_CHAT";
            node["chat_executor_scope"] = "ONE_WORK_UNIT_IMAGE_ONLY";
            node["chat_executor_output"] = "ONE_IMAGE_FILE_ONLY";
            node["chat_executor_must_not_build_response_zip"] = true;
            node["local_launcher_builds_partial_response"] = true;
        }

        queue["local_image_handoff_protocol"] = "diez-local-image-handoff";
        queue["local_image_handoff_protocol_version"] = ProtocolVersion;
        queue["conversation_orchestration_owner"] = "USER_LOCAL_LAUNCHER";
        queue["chat_executor_scope"] = "ONE_WORK_UNIT_IMAGE_ONLY";
        queue["chat_executor_output"] = "ONE_IMAGE_FILE_ONLY";
        queue["partial_response_packaging_owner"] = "LOCAL_LAUNCHER";
        queue["chat_executor_must_not_build_response_zip"] = true;
        queue["import_mode"] = "SINGLE_RESPONSE_BUNDLE_FROM_LOCAL_IMAGE_HANDOFF";
        ReplaceObject(archive, QueueFileName, queue);

        ReplaceText(archive, LauncherFileName,
            BuildLauncher(bookTitle, projectId, jobId, promptPackId, bundleFile, tasks));
        ReplaceText(archive, StartHereFileName, BuildStartHere(bookTitle, bundleFile, tasks.Count));

        var existingInstructions = ReadText(archive, InstructionsFileName);
        ReplaceText(archive, InstructionsFileName, PrependManualHandoff(existingInstructions));
    }

    private static string BuildLauncher(
        string bookTitle,
        string projectId,
        string jobId,
        string promptPackId,
        string bundleFile,
        IReadOnlyList<TaskInfo> tasks)
    {
        var html = new StringBuilder();
        html.AppendLine("<!doctype html><html lang=\"it\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        html.Append("<title>Diez image handoff — ").Append(WebUtility.HtmlEncode(bookTitle)).AppendLine("</title>");
        html.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;max-width:1100px;margin:28px auto;padding:0 18px;line-height:1.45;color:#171717}h1{margin-bottom:4px}.intro,.bundle{background:#f5f5f5;border:1px solid #ddd;border-radius:12px;padding:16px;margin:18px 0}.task{border:1px solid #d7d7d7;border-radius:12px;padding:16px;margin:18px 0}.taskhead{display:flex;justify-content:space-between;gap:12px;flex-wrap:wrap}.pill{background:#eee;border-radius:999px;padding:5px 10px;font-size:13px}textarea.prompt{width:100%;height:300px;box-sizing:border-box;font-family:Consolas,monospace;font-size:12px;padding:10px;margin:10px 0}button,.button{border:0;border-radius:8px;background:#1f6feb;color:#fff;padding:10px 14px;text-decoration:none;cursor:pointer;font-size:14px}.actions{display:flex;gap:10px;align-items:center;flex-wrap:wrap}.asset{margin-top:14px;padding-top:12px;border-top:1px solid #ddd}.asset input[type=text]{width:100%;box-sizing:border-box;padding:8px;margin:5px 0 10px}.failreason{width:100%;box-sizing:border-box;padding:8px;margin-top:6px}.warn{color:#8a4b00}.hard{color:#a00;font-weight:700}#bundleStatus{font-weight:600;margin-top:10px}</style></head><body>");
        html.AppendLine("<h1>Diez — generazione immagini isolata</h1>");
        html.Append(WebUtility.HtmlEncode(bookTitle)).Append(" · ").Append(tasks.Count).AppendLine(" Work Unit");
        html.AppendLine("<div class=\"intro\"><p><strong>Regola principale:</strong> questa pagina è l'orchestratore. <span class=\"hard\">Non caricare l'intero Prompt Pack in una chat e non chiedere a una sola chat di eseguire tutte le immagini.</span></p><p>Per ogni Work Unit: copia <strong>solo il prompt immagine</strong>, apri una nuova chat vuota, genera una sola immagine, scarica il file e torna qui. La chat non deve conoscere ID, ZIP, manifest, FAILED o audit: il launcher costruisce tutto localmente.</p><p class=\"warn\">Se il render è sbagliato, non allegarlo: marca quella Work Unit come FAILED. La successiva va comunque eseguita in una nuova chat vuota.</p></div>");

        foreach (var task in tasks)
        {
            var id = $"prompt-{task.Order:D3}";
            html.Append("<section class=\"task\"><div class=\"taskhead\"><strong>Task ").Append(task.Order).Append('/').Append(tasks.Count).Append(" · ").Append(WebUtility.HtmlEncode(task.Code)).Append("</strong><span class=\"pill\">chat separata</span></div>");
            html.AppendLine("<p>Questa textarea contiene esclusivamente il testo da inviare al renderer.</p>");
            html.Append("<textarea class=\"prompt\" id=\"").Append(id).Append("\" readonly>").Append(WebUtility.HtmlEncode(task.Prompt)).AppendLine("</textarea>");
            html.AppendLine("<div class=\"actions\">");
            html.Append("<button onclick=\"copyPrompt('").Append(id).AppendLine("',this)\">Copia solo prompt immagine</button>");
            html.AppendLine("<a class=\"button\" href=\"https://chatgpt.com/\" target=\"_blank\" rel=\"noopener noreferrer\">Apri nuova chat</a>");
            html.AppendLine("</div><div class=\"asset\">");
            html.Append("<label><strong>Immagine scaricata:</strong> <input id=\"file-").Append(task.Order).AppendLine("\" type=\"file\" accept=\"image/png,image/jpeg,image/webp,.png,.jpg,.jpeg,.webp\"></label>");
            html.Append("<label>Descrizione verificabile: <input id=\"desc-").Append(task.Order).Append("\" type=\"text\" value=\"").Append(WebUtility.HtmlEncode(task.DefaultDescription)).AppendLine("\"></label>");
            html.Append("<label><input id=\"failed-").Append(task.Order).Append("\" type=\"checkbox\" onchange=\"toggleFailed(").Append(task.Order).AppendLine(")\"> Nessun asset conforme — registra FAILED</label>");
            html.Append("<input class=\"failreason\" id=\"reason-").Append(task.Order).AppendLine("\" type=\"text\" disabled placeholder=\"Motivo del fallimento\">");
            html.AppendLine("</div></section>");
        }

        html.AppendLine("<section class=\"bundle\"><h2>Response finale unico</h2><p>Quando ogni Work Unit ha un'immagine selezionata oppure è marcata FAILED, premi il pulsante. Il browser crea localmente un partial ZIP per Work Unit e poi un unico Response Bundle. Nessuna chat deve costruire ZIP.</p><button onclick=\"buildBundle()\">Crea Response ZIP unico</button><div id=\"bundleStatus\"></div></section>");

        var taskData = tasks.Select(t => new
        {
            order = t.Order,
            work_unit_id = t.WorkUnitId,
            code = t.Code,
            candidate_version = t.CandidateVersion,
            render_request_id = t.RenderRequestId,
            render_prompt_sha256 = t.PromptSha256,
            partial_response_filename = t.PartialResponseFile,
            bundle_entry = t.BundleEntry
        }).ToArray();

        html.AppendLine("<script>");
        html.Append("const projectId=").Append(JsonSerializer.Serialize(projectId)).AppendLine(";");
        html.Append("const jobId=").Append(JsonSerializer.Serialize(jobId)).AppendLine(";");
        html.Append("const promptPackId=").Append(JsonSerializer.Serialize(promptPackId)).AppendLine(";");
        html.Append("const bundleFile=").Append(JsonSerializer.Serialize(bundleFile)).AppendLine(";");
        html.Append("const tasks=").Append(JsonSerializer.Serialize(taskData)).AppendLine(";");
        html.AppendLine("async function copyPrompt(id,b){const e=document.getElementById(id);let ok=false;try{await navigator.clipboard.writeText(e.value);ok=true}catch{e.focus();e.select();try{ok=document.execCommand('copy')}catch{}}b.textContent=ok?'Prompt copiato':'Seleziona e copia';setTimeout(()=>b.textContent='Copia solo prompt immagine',1600)}");
        html.AppendLine("function toggleFailed(i){const f=document.getElementById('failed-'+i).checked;document.getElementById('file-'+i).disabled=f;document.getElementById('reason-'+i).disabled=!f}");
        html.AppendLine("function w16(a,o,v){a[o]=v&255;a[o+1]=(v>>>8)&255}function w32(a,o,v){a[o]=v&255;a[o+1]=(v>>>8)&255;a[o+2]=(v>>>16)&255;a[o+3]=(v>>>24)&255}");
        html.AppendLine("const crcTable=(()=>{const t=new Uint32Array(256);for(let n=0;n<256;n++){let c=n;for(let k=0;k<8;k++)c=(c&1)?(0xedb88320^(c>>>1)):(c>>>1);t[n]=c>>>0}return t})();function crc32(d){let c=0xffffffff;for(const b of d)c=crcTable[(c^b)&255]^(c>>>8);return(c^0xffffffff)>>>0}");
        html.AppendLine("function concat(ps){let n=0;for(const p of ps)n+=p.length;const o=new Uint8Array(n);let x=0;for(const p of ps){o.set(p,x);x+=p.length}return o}function stamp(){const d=new Date(),y=Math.max(1980,d.getFullYear());return{time:(d.getHours()<<11)|(d.getMinutes()<<5)|(d.getSeconds()>>1),date:((y-1980)<<9)|((d.getMonth()+1)<<5)|d.getDate()}}");
        html.AppendLine("function zipBytes(entries){const enc=new TextEncoder(),body=[],central=[];let off=0;const s=stamp();for(const e of entries){const name=enc.encode(e.name),data=e.data,crc=crc32(data),l=new Uint8Array(30);w32(l,0,0x04034b50);w16(l,4,20);w16(l,6,0x0800);w16(l,8,0);w16(l,10,s.time);w16(l,12,s.date);w32(l,14,crc);w32(l,18,data.length);w32(l,22,data.length);w16(l,26,name.length);w16(l,28,0);body.push(l,name,data);const c=new Uint8Array(46);w32(c,0,0x02014b50);w16(c,4,20);w16(c,6,20);w16(c,8,0x0800);w16(c,10,0);w16(c,12,s.time);w16(c,14,s.date);w32(c,16,crc);w32(c,20,data.length);w32(c,24,data.length);w16(c,28,name.length);w32(c,42,off);central.push(c,name);off+=l.length+name.length+data.length}const cb=concat(central),e=new Uint8Array(22);w32(e,0,0x06054b50);w16(e,8,entries.length);w16(e,10,entries.length);w32(e,12,cb.length);w32(e,16,off);return concat([...body,cb,e])}");
        html.AppendLine("function newId(){return crypto.randomUUID?crypto.randomUUID():Date.now().toString(16)+'-'+Math.random().toString(16).slice(2)}function extFor(f){const n=f.name.toLowerCase();for(const e of ['.png','.jpg','.jpeg','.webp'])if(n.endsWith(e))return e;if(f.type==='image/png')return'.png';if(f.type==='image/jpeg')return'.jpg';if(f.type==='image/webp')return'.webp';throw new Error('Formato immagine non supportato: '+f.name)}");
        html.AppendLine("async function buildBundle(){const st=document.getElementById('bundleStatus');st.textContent='';try{const enc=new TextEncoder(),outerParts=[],partRefs=[];for(const t of tasks){const failed=document.getElementById('failed-'+t.order).checked,file=document.getElementById('file-'+t.order).files[0],reason=document.getElementById('reason-'+t.order).value.trim(),desc=document.getElementById('desc-'+t.order).value.trim();if(failed&&file)throw new Error(t.code+': scegli immagine oppure FAILED, non entrambi.');if(!failed&&!file)throw new Error(t.code+': manca l’immagine o la marcatura FAILED.');if(!failed&&!desc)throw new Error(t.code+': descrizione immagine mancante.');let primary=null,entries=[];if(file){const ext=extFor(file);primary='content/'+t.code+'-v'+String(t.candidate_version).padStart(3,'0')+ext;entries.push({name:primary,data:new Uint8Array(await file.arrayBuffer())})}const item={work_unit_id:t.work_unit_id,candidate_version:t.candidate_version,content_type:'IMAGE',status:failed?'FAILED':'COMPLETE',primary_asset:primary,description:failed?'No image asset produced because this Work Unit was marked FAILED in the local Diez launcher.':desc,render_request_id:t.render_request_id,render_prompt_sha256:t.render_prompt_sha256,failure_reason:failed?(reason||'No compliant image selected by the user.'):null};const manifest={protocol:'diez-response',protocol_version:1,project_id:projectId,job_id:jobId,prompt_pack_id:promptPackId,package_id:newId(),partial:true,items:[item]};entries.unshift({name:'response-manifest.json',data:enc.encode(JSON.stringify(manifest,null,2))});const inner=zipBytes(entries);outerParts.push({name:t.bundle_entry,data:inner});partRefs.push({order:t.order,work_unit_id:t.work_unit_id,file_name:t.bundle_entry})}const outerManifest={protocol:'diez-response-bundle',protocol_version:1,project_id:projectId,prompt_pack_id:promptPackId,bundle_id:newId(),expected_parts:tasks.length,parts:partRefs};outerParts.unshift({name:'response-bundle-manifest.json',data:enc.encode(JSON.stringify(outerManifest,null,2))});const bytes=zipBytes(outerParts),blob=new Blob([bytes],{type:'application/zip'}),url=URL.createObjectURL(blob),a=document.createElement('a');a.href=url;a.download=bundleFile;document.body.appendChild(a);a.click();a.remove();setTimeout(()=>URL.revokeObjectURL(url),5000);st.textContent='Creato '+bundleFile+' con '+tasks.length+' partial ZIP interni. Importa questo singolo file in Diez.'}catch(e){st.textContent='Bundle non creato: '+(e&&e.message?e.message:e)}}");
        html.AppendLine("</script></body></html>");
        return html.ToString();
    }

    private static string BuildStartHere(string bookTitle, string bundleFile, int taskCount) => $"""
# START HERE — Diez local image handoff

Book: {bookTitle}
Work Units: {taskCount}
Final Response Bundle: `{bundleFile}`

## Manual ChatGPT workflow — generation chats return IMAGE ONLY
1. Extract this Prompt Pack ZIP to a folder and open `00-CLEAN-ROOM-LAUNCHER.html` in the browser.
2. **Do not upload this entire Prompt Pack to a generation chat and do not ask one chat to execute all Work Units.** A chat cannot create the other isolated chats required by the clean-room policy.
3. For Task 1, click `Copia solo prompt immagine`, then `Apri nuova chat`. Paste only that visual prompt into the new blank chat and generate one image.
4. Download the generated image file. Do not ask the chat to create a Response ZIP, manifest, UUID or audit report.
5. Return to the launcher and select that image for the matching Work Unit. If the image is visibly non-compliant, do not select it; mark the Work Unit FAILED and provide the reason.
6. Repeat with another NEW blank chat for every remaining Work Unit.
7. Back in the launcher, click `Crea Response ZIP unico`. The launcher locally creates one partial Response ZIP per Work Unit and nests them inside `{bundleFile}`.
8. In Diez → `Importa risultati AI`, select only the final outer Response ZIP.

The image-generation conversations are deliberately disposable and know nothing about Diez transport metadata. `render-prompts/*.txt` is the only model-facing content. The browser launcher owns Work Unit identity and Response packaging.
""".Trim();

    private static string PrependManualHandoff(string existing)
    {
        const string marker = "## Diez local image handoff — HARD";
        if ((existing ?? string.Empty).Contains(marker, StringComparison.Ordinal)) return existing;
        var prefix = $"""
{marker}
For manual ChatGPT execution, this Prompt Pack is a LOCAL LAUNCHER CONTAINER, not a request for one conversation to execute the whole batch. Do not generate multiple Work Units in the conversation that received the whole ZIP. Extract the pack and use `00-CLEAN-ROOM-LAUNCHER.html`.

Each generation chat receives ONLY one `render-prompts/NNN-*.txt` visual brief and returns ONLY one image file. It must not create `response-manifest.json`, package ZIPs, generate IDs, reason about bundle transport or execute another Work Unit. The local launcher owns all response manifests, partial ZIP creation and the final outer Response Bundle.
""".Trim();
        return prefix + Environment.NewLine + Environment.NewLine + (existing ?? string.Empty).TrimStart();
    }

    private static string DefaultDescription(string code, string prompt)
    {
        var subject = prompt.Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith("PRIMARY SUBJECT — HARD LOCK:", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(subject))
        {
            var value = subject[(subject.IndexOf(':') + 1)..].Trim();
            var dot = value.IndexOf('.');
            if (dot >= 0) value = value[..dot].Trim();
            if (value.Length > 0) return $"Generated illustration for {code} with primary subject {value}.";
        }
        return $"Generated illustration for {code}.";
    }

    private static JsonObject? ReadObject(ZipArchive archive, string path)
    {
        var text = ReadText(archive, path);
        if (text.Length == 0) return null;
        try { return JsonNode.Parse(text)?.AsObject(); }
        catch { return null; }
    }

    private static string ReadText(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path);
        if (entry is null) return string.Empty;
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        return reader.ReadToEnd();
    }

    private static void ReplaceObject(ZipArchive archive, string path, JsonObject value) =>
        ReplaceText(archive, path, value.ToJsonString(JsonOptions));

    private static void ReplaceText(ZipArchive archive, string path, string text)
    {
        archive.GetEntry(path)?.Delete();
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(text ?? string.Empty);
    }

    private static int IntValue(JsonNode? node, int fallback) => int.TryParse(node?.ToString(), out var value) ? value : fallback;

    private sealed record TaskInfo(
        int Order,
        string WorkUnitId,
        string Code,
        int CandidateVersion,
        string RenderRequestId,
        string PromptSha256,
        string PartialResponseFile,
        string BundleEntry,
        string Prompt,
        string DefaultDescription);
}
