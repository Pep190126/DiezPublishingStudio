using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiezPublishingStudio;

/// <summary>
/// Makes the manual Prompt-Pack transport executable without asking the receiving agent to infer
/// which orchestration fields should be sent to the image renderer. Every image Work Unit gets one
/// visual-only prompt file, one unique render request id and a guided clean-room queue task.
/// </summary>
internal static class PromptPackExecutionPlanService
{
    public const string ProtocolVersion = "1.3";
    private const string ManifestName = "prompt-manifest.json";
    private const string ContextName = "request-context.json";
    private const string InstructionsName = "instructions.md";
    private const string PlanName = "render-plan.json";
    private const string StartName = "00-START-HERE.md";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    internal readonly record struct ApplyResult(int WorkUnits, string PromptPackFileName, string ResponseFileName);

    public static ApplyResult Apply(
        string promptPackPath,
        PreviewProject project,
        AiExchangeState state,
        IEnumerable<Guid> workUnitIds,
        int packageVersion)
    {
        if (!File.Exists(promptPackPath))
            throw new FileNotFoundException("Prompt Pack non trovato.", promptPackPath);

        var ids = workUnitIds.Distinct().ToHashSet();
        var units = state.WorkUnits
            .Where(u => ids.Contains(u.WorkUnitId))
            .OrderBy(u => u.Position)
            .ThenBy(u => u.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (units.Count == 0) throw new InvalidOperationException("Nessuna Work Unit disponibile per il render plan.");

        var promptPackFileName = BookPackageNamingService.PromptPackFileName(project, packageVersion);
        var responseFileName = BookPackageNamingService.ResponseFileName(project, packageVersion);

        using var archive = ZipFile.Open(promptPackPath, ZipArchiveMode.Update);
        var manifest = ReadObject(archive, ManifestName) ?? throw new InvalidOperationException("prompt-manifest.json non valido.");
        var context = ReadObject(archive, ContextName) ?? throw new InvalidOperationException("request-context.json non valido.");
        var manifestUnits = manifest["work_units"] as JsonArray ?? throw new InvalidOperationException("work_units mancanti nel manifest.");
        var contextUnits = context["work_units"] as JsonArray ?? throw new InvalidOperationException("work_units mancanti nel request-context.");

        var calls = new JsonArray();
        for (var index = 0; index < units.Count; index++)
        {
            var unit = units[index];
            var manifestUnit = FindUnit(manifestUnits, unit.WorkUnitId)
                ?? throw new InvalidOperationException("Work Unit mancante nel manifest: " + unit.WorkUnitId);
            var contextUnit = FindUnit(contextUnits, unit.WorkUnitId)
                ?? throw new InvalidOperationException("Work Unit mancante nel request-context: " + unit.WorkUnitId);
            var basePrompt = manifestUnit["image_generation_prompt"]?.ToString()?.Trim() ?? string.Empty;
            if (basePrompt.Length == 0)
                throw new InvalidOperationException("image_generation_prompt mancante per " + unit.WorkUnitId);

            var authoritative = PromptPackRendererVisualBriefService.Build(basePrompt);
            PromptPackProviderFacingService.EnsureRendererPromptReady(authoritative, unit.Code);
            PromptPackRendererVisualBriefService.EnsureVisualOnly(authoritative);

            var renderRequestId = Guid.NewGuid();
            var sourcePolicy = string.Equals(unit.Mode, "AI_ONLY", StringComparison.OrdinalIgnoreCase)
                ? "BLANK_CANVAS_NO_INPUT_IMAGES"
                : "ONLY_EXPLICIT_FILES_PACKAGED_FOR_THIS_WORK_UNIT";
            var promptBytes = Encoding.UTF8.GetBytes(authoritative);
            var promptSha = Convert.ToHexString(SHA256.HashData(promptBytes)).ToLowerInvariant();
            var safeCode = SafeCode(unit.Code, index + 1);
            var promptFile = $"render-prompts/{index + 1:D3}-{safeCode}.txt";
            ReplaceBytes(archive, promptFile, promptBytes);

            foreach (var node in new[] { manifestUnit, contextUnit })
            {
                node["image_generation_prompt"] = authoritative;
                node["image_generation_prompt_authoritative"] = true;
                node["renderer_prompt_scope"] = "VISUAL_ONLY";
                node["render_request_id"] = renderRequestId.ToString("D");
                node["render_prompt_file"] = promptFile;
                node["render_prompt_sha256"] = promptSha;
                node["fresh_generation_required"] = true;
                node["fresh_context_owner"] = "EXECUTOR";
                node["reuse_prior_generated_images_forbidden"] = true;
                node["source_image_policy"] = sourcePolicy;
            }

            calls.Add(new JsonObject
            {
                ["order"] = index + 1,
                ["work_unit_id"] = unit.WorkUnitId.ToString("D"),
                ["work_unit_code"] = unit.Code,
                ["candidate_version"] = IntValue(manifestUnit["target_candidate_version"] ?? manifestUnit["candidate_version"], 1),
                ["mode"] = unit.Mode,
                ["render_request_id"] = renderRequestId.ToString("D"),
                ["prompt_file"] = promptFile,
                ["prompt_sha256"] = promptSha,
                ["renderer_prompt_scope"] = "VISUAL_ONLY",
                ["fresh_generation_required"] = true,
                ["fresh_context_owner"] = "EXECUTOR",
                ["chat_session_policy"] = "NEW_RENDERER_CALL_NO_PRIOR_IMAGE_REFERENCE",
                ["clean_room_chat_policy"] = "NEW_TEMPORARY_OR_NEW_BLANK_CHAT",
                ["reuse_prior_generated_images_forbidden"] = true,
                ["source_image_policy"] = sourcePolicy,
                ["renderer_prompt_source"] = "prompt_file_verbatim",
                ["hard_subject_guard"] = "PRIMARY SUBJECT — HARD LOCK",
                ["hard_style_guard"] = "STYLE — HARD LOCK",
                ["hard_composition_guard"] = "COMPOSITION — HARD LOCK"
            });
        }

        var identity = new JsonObject
        {
            ["project_id"] = project.ProjectId.ToString("D"),
            ["book_title"] = BookPackageNamingService.BookTitle(project)
        };
        var naming = new JsonObject
        {
            ["version"] = packageVersion,
            ["prompt_pack_filename"] = promptPackFileName,
            ["response_filename"] = responseFileName
        };
        var manualExecution = new JsonObject
        {
            ["protocol"] = "diez-manual-render-plan",
            ["protocol_version"] = ProtocolVersion,
            ["start_here"] = StartName,
            ["render_plan"] = PlanName,
            ["clean_room_queue"] = PromptPackCleanRoomQueueService.QueueFileName,
            ["clean_room_queue_version"] = PromptPackCleanRoomQueueService.QueueProtocolVersion,
            ["clean_room_launcher"] = PromptPackCleanRoomQueueService.LauncherFileName,
            ["renderer_prompt_source"] = "render-prompts/*.txt",
            ["renderer_prompt_scope"] = "VISUAL_ONLY",
            ["one_work_unit_per_renderer_call"] = true,
            ["fresh_generation_required"] = true,
            ["fresh_context_owner"] = "EXECUTOR",
            ["chat_session_policy"] = "NEW_RENDERER_CALL_NO_PRIOR_IMAGE_REFERENCE",
            ["clean_room_chat_policy"] = "GUIDED_TEMPORARY_OR_NEW_BLANK_CHAT_PER_WORK_UNIT",
            ["same_chat_renderer_isolation_certified"] = false,
            ["partial_response_allowed"] = true,
            ["response_bundle_protocol"] = AiExchangeResponseBundleService.Protocol,
            ["response_bundle_protocol_version"] = AiExchangeResponseBundleService.ProtocolVersion,
            ["response_bundle_manifest"] = AiExchangeResponseBundleService.ManifestFileName,
            ["response_bundle_filename"] = responseFileName,
            ["partial_response_import"] = "BUNDLE_ONE_OUTER_ZIP_PREFERRED_OR_MULTI_SELECT_PARTS",
            ["reuse_prior_generated_images_forbidden"] = true,
            ["atomic_subject_required"] = true,
            ["selected_style_is_hard"] = true
        };

        manifest["book_identity"] = identity.DeepClone();
        manifest["package_naming"] = naming.DeepClone();
        manifest["manual_execution"] = manualExecution.DeepClone();
        context["book_identity"] = identity.DeepClone();
        context["package_naming"] = naming.DeepClone();
        context["manual_execution"] = manualExecution.DeepClone();

        var plan = new JsonObject
        {
            ["protocol"] = "diez-render-plan",
            ["protocol_version"] = ProtocolVersion,
            ["project_id"] = project.ProjectId.ToString("D"),
            ["book_title"] = BookPackageNamingService.BookTitle(project),
            ["package_version"] = packageVersion,
            ["prompt_pack_filename"] = promptPackFileName,
            ["response_filename"] = responseFileName,
            ["clean_room_queue"] = PromptPackCleanRoomQueueService.QueueFileName,
            ["clean_room_queue_version"] = PromptPackCleanRoomQueueService.QueueProtocolVersion,
            ["clean_room_launcher"] = PromptPackCleanRoomQueueService.LauncherFileName,
            ["response_bundle_protocol"] = AiExchangeResponseBundleService.Protocol,
            ["response_bundle_protocol_version"] = AiExchangeResponseBundleService.ProtocolVersion,
            ["response_bundle_manifest"] = AiExchangeResponseBundleService.ManifestFileName,
            ["renderer_prompt_source"] = "Read each prompt_file verbatim as VISUAL-ONLY model input; enforce clean-room routing outside the renderer prompt.",
            ["renderer_prompt_scope"] = "VISUAL_ONLY",
            ["one_work_unit_per_renderer_call"] = true,
            ["fresh_generation_required"] = true,
            ["fresh_context_owner"] = "EXECUTOR",
            ["chat_session_policy"] = "NEW_RENDERER_CALL_NO_PRIOR_IMAGE_REFERENCE",
            ["clean_room_chat_policy"] = "GUIDED_TEMPORARY_OR_NEW_BLANK_CHAT_PER_WORK_UNIT",
            ["same_chat_renderer_isolation_certified"] = false,
            ["partial_response_allowed"] = true,
            ["partial_response_import"] = "BUNDLE_ONE_OUTER_ZIP_PREFERRED_OR_MULTI_SELECT_PARTS",
            ["reuse_prior_generated_images_forbidden"] = true,
            ["atomic_subject_required"] = true,
            ["selected_style_is_hard"] = true,
            ["calls"] = calls
        };

        PromptPackCleanRoomQueueService.Apply(archive, project, packageVersion, manifest, calls);

        ReplaceObject(archive, ManifestName, manifest);
        ReplaceObject(archive, ContextName, context);
        ReplaceObject(archive, PlanName, plan);
        ReplaceText(archive, StartName, BuildStartHere(project, packageVersion, promptPackFileName, responseFileName));
        var instructions = ReadText(archive, InstructionsName);
        ReplaceText(archive, InstructionsName, AppendExecutionInstructions(instructions, responseFileName));

        return new ApplyResult(units.Count, promptPackFileName, responseFileName);
    }

    private static string BuildStartHere(PreviewProject project, int version, string promptPackFileName, string responseFileName) => $"""
# START HERE — Diez manual image execution

Book: {BookPackageNamingService.BookTitle(project)}
Internal project ID: {project.ProjectId:D}
Package version: v{version:D3}
Prompt Pack: `{promptPackFileName}`
Final Response Bundle: `{responseFileName}`

## Recommended manual workflow — ONE guided clean-room queue
Open `{PromptPackCleanRoomQueueService.LauncherFileName}`. It presents Task 1/N → Task 2/N → Task N/N in one local launcher, so the user does not have to manage separate projects or reconstruct manifests by hand.

For each task:
1. Open a NEW Temporary Chat when available, otherwise a NEW blank chat. The current physical tests proved that the same image conversation cannot be certified as clean because prior-project visuals may leak into a later renderer call.
2. Copy the complete task from the launcher and send it in that clean chat.
3. The chat executor must send ONLY the enclosed VISUAL-ONLY block to the image renderer. Routing IDs, response packaging and audit text remain outside the image-model prompt.
4. Use exactly ONE image-generation attempt in that clean-room chat. If it violates a HARD lock, return that task as FAILED without an asset; do not contaminate the same chat with repeated edit/retry cycles.
5. Download the partial Response ZIP named `diez-...-response-v{version:D3}-part-NNN.zip`, close/abandon that clean chat, then return to the launcher for the next task.
6. When all tasks are done, use the launcher's `Crea Response ZIP unico`: select the N partial ZIPs and it creates `{responseFileName}` locally. The outer ZIP contains `{AiExchangeResponseBundleService.ManifestFileName}` plus `parts/*.zip`, one inner ZIP per image/Work Unit.
7. In Diez → `Importa risultati AI`, select only `{responseFileName}`. Diez opens the bundle, audits each inner Response independently, reconciles them on the same Prompt Pack/snapshot and opens one unified Review page. Multi-selecting the raw part-NNN ZIPs remains supported only as a compatibility fallback.

Compatibility/audit note: the same orchestration chat may be used only when a platform truly gives each image-generation invocation an isolated no-input renderer context. The physical ChatGPT tests showed that a platform may automatically carry prior visual state, so the clean-room queue is the default manual path here. Every task still represents a NEW image-generation invocation at the renderer boundary.

`render-plan.json` and `{PromptPackCleanRoomQueueService.QueueFileName}` remain the machine-readable audit contracts. `render-prompts/*.txt` remains VISUAL-ONLY image-model input. The long manifest/instructions/request-context are never renderer prompts.
""".Trim();

    private static string AppendExecutionInstructions(string existing, string responseFileName)
    {
        const string marker = "## Diez clean-room queue — AUTHORITATIVE";
        if ((existing ?? string.Empty).Contains(marker, StringComparison.Ordinal)) return existing;
        return (existing ?? string.Empty).TrimEnd() + "\n\n" + $"""
{marker}
- Prefer `{PromptPackCleanRoomQueueService.LauncherFileName}` as the human entry point; it guides the entire batch as one queue.
- Every AI_ONLY Work Unit requires a new image-generation invocation with no prior Work Unit image attached or referenced. For the manual ChatGPT path, use a NEW Temporary Chat or NEW blank chat per Work Unit because same-chat renderer isolation is not certified by the physical tests.
- `render-prompts/*.txt` is VISUAL-ONLY image-model input. Never forward routing/session/retry/audit metadata to the image renderer.
- One clean-room task performs one image-generation attempt. A non-compliant result becomes a partial FAILED response rather than an edit/retry inside a contaminated visual conversation.
- Each task returns a PARTIAL Response ZIP named `diez-...-response-vNNN-part-NNN.zip` with exactly one Work Unit and `partial=true`.
- After all clean-room tasks, the launcher locally creates one outer `{responseFileName}` using protocol `{AiExchangeResponseBundleService.Protocol}` v{AiExchangeResponseBundleService.ProtocolVersion}. It contains `{AiExchangeResponseBundleService.ManifestFileName}` and `parts/*.zip`, one nested partial Response per Work Unit.
- The preferred Diez import is that single outer Response Bundle. Direct multi-select of the raw part-NNN files remains backward-compatible.
- Stable ProjectId/WorkUnitId/candidate version/render_request_id/SHA remain authoritative inside every nested Response; filenames are human-facing only.
- An optional provider may still return one ordinary aggregate `{responseFileName}` when it can genuinely guarantee stateless renderer calls; the importer distinguishes an ordinary Response ZIP from a nested Response Bundle by its root manifest.
""".Trim();
    }

    private static JsonObject? FindUnit(JsonArray array, Guid id) => array.OfType<JsonObject>().FirstOrDefault(node =>
        Guid.TryParse(node["id"]?.ToString() ?? node["work_unit_id"]?.ToString(), out var value) && value == id);

    private static int IntValue(JsonNode? node, int fallback) => int.TryParse(node?.ToString(), out var value) ? value : fallback;

    private static string SafeCode(string? code, int index)
    {
        var source = string.IsNullOrWhiteSpace(code) ? $"WU-{index:D3}" : code.Trim();
        var chars = source.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-').ToArray();
        return new string(chars).Trim('-');
    }

    private static JsonObject? ReadObject(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path);
        if (entry is null) return null;
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        try { return JsonNode.Parse(reader.ReadToEnd())?.AsObject(); }
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

    private static void ReplaceText(ZipArchive archive, string path, string text) =>
        ReplaceBytes(archive, path, Encoding.UTF8.GetBytes(text ?? string.Empty));

    private static void ReplaceBytes(ZipArchive archive, string path, byte[] bytes)
    {
        archive.GetEntry(path)?.Delete();
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(bytes, 0, bytes.Length);
    }
}
