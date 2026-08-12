using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiezPublishingStudio;

/// <summary>
/// Makes the manual Prompt-Pack transport executable without asking the receiving agent to infer
/// which of several orchestration fields should be sent to the image renderer. Every image Work Unit
/// gets one standalone prompt file, one unique render request id and an explicit fresh-generation policy.
/// </summary>
internal static class PromptPackExecutionPlanService
{
    public const string ProtocolVersion = "1.1";
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

            var renderRequestId = Guid.NewGuid();
            var sourcePolicy = string.Equals(unit.Mode, "AI_ONLY", StringComparison.OrdinalIgnoreCase)
                ? "BLANK_CANVAS_NO_INPUT_IMAGES"
                : "ONLY_EXPLICIT_FILES_PACKAGED_FOR_THIS_WORK_UNIT";
            var authoritative = BuildIsolatedPrompt(basePrompt, renderRequestId, sourcePolicy);
            var promptBytes = Encoding.UTF8.GetBytes(authoritative);
            var promptSha = Convert.ToHexString(SHA256.HashData(promptBytes)).ToLowerInvariant();
            var safeCode = SafeCode(unit.Code, index + 1);
            var promptFile = $"render-prompts/{index + 1:D3}-{safeCode}.txt";
            ReplaceBytes(archive, promptFile, promptBytes);

            foreach (var node in new[] { manifestUnit, contextUnit })
            {
                node["image_generation_prompt"] = authoritative;
                node["image_generation_prompt_authoritative"] = true;
                node["render_request_id"] = renderRequestId.ToString("D");
                node["render_prompt_file"] = promptFile;
                node["render_prompt_sha256"] = promptSha;
                node["fresh_generation_required"] = true;
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
                ["fresh_generation_required"] = true,
                ["reuse_prior_generated_images_forbidden"] = true,
                ["source_image_policy"] = sourcePolicy,
                ["renderer_prompt_source"] = "prompt_file_verbatim",
                ["hard_subject_guard"] = "PRIMARY SUBJECT — HARD LOCK"
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
            ["one_work_unit_per_renderer_call"] = true,
            ["fresh_generation_required"] = true,
            ["reuse_prior_generated_images_forbidden"] = true
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
            ["renderer_prompt_source"] = "Read each prompt_file verbatim; do not reconstruct prompts from the long manifest/instruction text.",
            ["one_work_unit_per_renderer_call"] = true,
            ["fresh_generation_required"] = true,
            ["reuse_prior_generated_images_forbidden"] = true,
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

    private static string BuildIsolatedPrompt(string basePrompt, Guid renderRequestId, string sourcePolicy) => $"""
FRESH GENERATION — HARD RESET
This Work Unit is a NEW image-generation request. Start from a blank visual generation context. Do NOT edit, continue, transform, extend, restyle, imitate, reference or reuse any image generated for an earlier Work Unit, earlier attempt, earlier Prompt Pack or earlier conversation turn.
Source-image policy: {sourcePolicy}. When the policy is BLANK_CANVAS_NO_INPUT_IMAGES, no image from the conversation or previous generation is authorized as an input/reference.
DIEZ RENDER REQUEST ID: {renderRequestId:D}. This ID is metadata only: never draw, print, caption or encode it inside the artwork.
If the renderer cannot guarantee a fresh generation context for this call, do not reuse a previous image. Return this Work Unit as FAILED/INCOMPLETE with a renderer-routing explanation.

{basePrompt.Trim()}
""".Trim();

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
3. For each call, read the referenced `prompt_file` under `render-prompts/` VERBATIM. That file is the only text to use as the renderer prompt.
4. Start a NEW/FRESH image generation for every Work Unit. Never use, edit, extend, restyle or reference an image generated by a previous Work Unit or previous attempt unless the current Work Unit explicitly packages that image as an allowed input/reference.
5. For `AI_ONLY` / `BLANK_CANVAS_NO_INPUT_IMAGES`, no prior conversation image is an authorized renderer input.
6. After rendering, inspect the actual image against `PRIMARY SUBJECT — HARD LOCK`. If the subject is wrong, discard the image and retry from a blank generation once; never repair a wrong subject by editing the wrong previous image. If fresh routing still cannot be guaranteed, return FAILED/INCOMPLETE and include no non-compliant asset.
7. Preserve Diez `work_unit_id`, candidate version, `render_request_id` and `render_prompt_sha256` in the response manifest when possible; they are audit metadata and must never appear in the artwork.
8. Return the package as `{responseFileName}`. File naming is for the user; Diez continues to bind results using stable internal IDs.

The long `instruction`, manifest and request-context are orchestration/QA context. They are NOT renderer prompts.
""".Trim();

    private static string AppendExecutionInstructions(string existing, string responseFileName)
    {
        const string marker = "## Diez renderer isolation — AUTHORITATIVE";
        if ((existing ?? string.Empty).Contains(marker, StringComparison.Ordinal)) return existing;
        return (existing ?? string.Empty).TrimEnd() + "\n\n" + $"""
{marker}
- Start with `00-START-HERE.md`, then execute `render-plan.json`.
- For IMAGE generation, the referenced `render-prompts/*.txt` file is the sole renderer prompt and must be read verbatim.
- Every Work Unit requires a fresh generation context. Reuse of images generated by previous Work Units/attempts is forbidden unless the current Work Unit explicitly packages that file as an authorized input/reference.
- For AI_ONLY, start from a blank canvas/context with no prior conversation image attached or referenced.
- Never paste the long manifest, `instruction`, request-context or QA contract into the image renderer.
- If fresh renderer routing is unavailable, return FAILED/INCOMPLETE rather than editing/reusing a previous unrelated image.
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
