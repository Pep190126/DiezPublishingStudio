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
/// visual-only prompt file, one unique render request id and an executor-owned fresh-call policy.
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

            // Final image-model boundary: remove routing/retry/audit language and forbidden-layout concept soup.
            // Freshness is enforced by the executor/call boundary, not by priming the visual model with process prose.
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
                ["candidate_version"] = IntValue(manifestUnit["candidate_version"], 1),
                ["mode"] = unit.Mode,
                ["render_request_id"] = renderRequestId.ToString("D"),
                ["prompt_file"] = promptFile,
                ["prompt_sha256"] = promptSha,
                ["renderer_prompt_scope"] = "VISUAL_ONLY",
                ["fresh_generation_required"] = true,
                ["fresh_context_owner"] = "EXECUTOR",
                ["chat_session_policy"] = "NEW_RENDERER_CALL_NO_PRIOR_IMAGE_REFERENCE",
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
            ["renderer_prompt_source"] = "render-prompts/*.txt",
            ["renderer_prompt_scope"] = "VISUAL_ONLY",
            ["one_work_unit_per_renderer_call"] = true,
            ["fresh_generation_required"] = true,
            ["fresh_context_owner"] = "EXECUTOR",
            ["chat_session_policy"] = "NEW_RENDERER_CALL_NO_PRIOR_IMAGE_REFERENCE",
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
            ["renderer_prompt_source"] = "Read each prompt_file verbatim as VISUAL-ONLY model input; enforce routing/call isolation outside the renderer prompt.",
            ["renderer_prompt_scope"] = "VISUAL_ONLY",
            ["one_work_unit_per_renderer_call"] = true,
            ["fresh_generation_required"] = true,
            ["fresh_context_owner"] = "EXECUTOR",
            ["chat_session_policy"] = "NEW_RENDERER_CALL_NO_PRIOR_IMAGE_REFERENCE",
            ["reuse_prior_generated_images_forbidden"] = true,
            ["atomic_subject_required"] = true,
            ["selected_style_is_hard"] = true,
            ["calls"] = calls
        };

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
Expected Prompt Pack name: `{promptPackFileName}`
Required Response ZIP name: `{responseFileName}`

This file is the entry point for manual execution. Do not infer the renderer prompt from conversation history.

1. Read `render-plan.json`.
2. Process its `calls` strictly in order and independently.
3. For each call, read the referenced `prompt_file` under `render-prompts/` VERBATIM. It is VISUAL-ONLY model input: never prepend/append routing, retry, IDs, audit text, manifest prose or conversation history to it.
4. Freshness is the EXECUTOR'S responsibility. For every `AI_ONLY` Work Unit, start a NEW image-generation invocation and send only that Work Unit's visual prompt, with NO image from a prior Work Unit attached, referenced, edited, continued or restyled. The same orchestration chat may be used only when the platform gives each image-generation invocation an isolated no-input renderer context. If the platform automatically carries prior visual state between calls, use a new chat/session or equivalent isolated context for that Work Unit.
5. For `AI_ONLY` / `BLANK_CANVAS_NO_INPUT_IMAGES`, no prior conversation image is an authorized renderer input. A new text message that implicitly continues or references the previous generated image does NOT satisfy fresh isolation.
6. Before rendering, verify that `PRIMARY SUBJECT — HARD LOCK` names one atomic subject for this Work Unit. Series quantity/layout language must not exist in the visual prompt. If it does, return FAILED with a prompt-routing error instead of rendering.
7. After rendering, inspect the actual image against `PRIMARY SUBJECT — HARD LOCK`, `STYLE — HARD LOCK`, independent HARD profile states, line weight and `COMPOSITION — HARD LOCK`. A correct animal in the wrong selected style is NOT compliant.
8. If a hard lock fails, discard the image and retry once with another new no-prior-image renderer invocation. Never repair a wrong result by editing the previous wrong render. If the platform cannot provide isolated no-input generation, return FAILED/INCOMPLETE and include no non-compliant asset.
9. Preserve Diez `work_unit_id`, candidate version, `render_request_id` and `render_prompt_sha256` in the response manifest when possible. These are executor/audit metadata and must NOT be inserted into the image-model prompt or artwork.
10. Return the package as `{responseFileName}`. File naming is for the user; Diez continues to bind results using stable internal IDs.

The long `instruction`, manifest, request-context, render routing and QA contract are orchestration context. They are NOT renderer prompts.
""".Trim();

    private static string AppendExecutionInstructions(string existing, string responseFileName)
    {
        const string marker = "## Diez renderer isolation — AUTHORITATIVE";
        if ((existing ?? string.Empty).Contains(marker, StringComparison.Ordinal)) return existing;
        return (existing ?? string.Empty).TrimEnd() + "\n\n" + $"""
{marker}
- Start with `00-START-HERE.md`, then execute `render-plan.json`.
- `render-prompts/*.txt` is VISUAL-ONLY image-model input. Routing/session/retry/audit metadata stays outside the renderer prompt.
- Every AI_ONLY Work Unit requires a new image-generation invocation with no prior Work Unit image attached or referenced. The orchestration chat may remain the same only if the platform truly isolates renderer calls; when visual state is carried automatically, use a new chat/session or equivalent isolated context.
- For AI_ONLY, use a blank no-input renderer context: never edit, continue, restyle or reference a previous Work Unit image.
- `PRIMARY SUBJECT — HARD LOCK` must be one atomic subject for the current Work Unit. Never visualize the series count.
- `STYLE — HARD LOCK` and the independent Coloring HARD profiles are explicit editorial requirements, not soft preferences.
- `COMPOSITION — HARD LOCK` requires one unified scene. Layout-family exclusion wording is intentionally kept out of the visual prompt to avoid priming the model with forbidden concepts.
- Never paste the long manifest, `instruction`, request-context, render request ID, retry rules or QA contract into the image renderer.
- If isolated no-input image generation is unavailable, return FAILED/INCOMPLETE rather than editing/reusing a previous image.
- Name the returned ZIP `{responseFileName}`; stable IDs remain authoritative internally.
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
