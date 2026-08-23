using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;

namespace DiezPublishingStudio;

/// <summary>
/// Adds the human/AI entry point to the canonical manual Prompt Pack ZIP.
///
/// One ZIP represents one batch delivery to the AI. Diez still keeps one Work Unit per requested
/// image so versioning, Vision and editorial promotion remain independently auditable. Technical
/// identifiers stay in prompt-manifest.json and are not injected into the visual renderer prompts.
/// </summary>
public static class DiezPromptPackBatchFrontendBridge
{
    public const string PromptEntryName = "PROMPT.md";

    private sealed record PublisherMaterialPromptInfo(
        string FileName,
        string IntentCode,
        string IntentLabel,
        string Instruction,
        string AiUsePolicy,
        string Fidelity);

    public static string BuildPackagePrompt(string projectJson, IEnumerable<Guid>? workUnitIds = null)
    {
        var items = DiezPromptPackFrontendBridge.Preview(projectJson, workUnitIds)
            .Where(x => string.Equals(x.ContentType, "Image", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (items.Count == 0)
            throw new InvalidOperationException("Non ci sono Prompt immagine pronti per il Prompt Pack.");

        var expectedResponse = ExpectedResponseFileName(projectJson);
        var publisherMaterials = PublisherMaterials(projectJson);
        var sb = new StringBuilder();
        sb.AppendLine("# DIEZ ∞ PUBLISHING STUDIO — IMAGE PROMPT PACK");
        sb.AppendLine();
        sb.AppendLine("This ZIP is the complete execution package. Process the whole batch in this session; do not ask the publisher to paste each Work Unit separately.");
        sb.AppendLine($"The batch contains EXACTLY {items.Count} separate image assets, one final asset per Work Unit, in manifest order.");
        if (!string.IsNullOrWhiteSpace(expectedResponse))
            sb.AppendLine($"Required final Response ZIP filename: `{expectedResponse}`.");
        sb.AppendLine();
        sb.AppendLine("## Batch execution contract");
        sb.AppendLine("1. Each DIEZ VISUAL PROMPT is already semantically frozen by Diez. Render it independently; do not reinterpret the batch as one canvas.");
        sb.AppendLine("2. Produce exactly ONE final image per Work Unit. Never create a collage, grid, contact sheet, split panel, multiple alternatives, or multiple sibling illustrations in one asset.");
        sb.AppendLine("3. SUBJECT PLANNING IS ALREADY DONE BY DIEZ. The concrete primary subject in each Work Unit is authoritative. Do not choose, replace, broaden, rotate, or invent a different batch subject. If a Work Unit still appears to contain an unresolved aggregate subject, STOP that Work Unit and return FAILED/INCOMPLETE instead of deciding it yourself.");
        sb.AppendLine("4. SCENE PLANNING IS ALREADY DONE BY DIEZ when scenes are required. Keep the Work Unit scene and participants exactly as frozen; do not invent a different scenario merely to diversify the series.");
        sb.AppendLine("5. Preserve only the Consistent locks explicitly present in the Work Unit. Do not carry pose, props, framing, or scene details from sibling images unless a lock requires it.");
        sb.AppendLine("6. RENDERING METHOD — HARD: use a real native generative IMAGE model/tool. Python, Pillow, SVG, Canvas, plotting, programmatic vector primitives, geometric assembly, diagrams, or coded drawing fallbacks are forbidden. If native image generation is unavailable, return FAILED/INCOMPLETE; never fabricate a surrogate.");
        sb.AppendLine("7. QUALITY GATE — HARD: reject and regenerate drafts, scribbles, geometric primitive assemblies, placeholders, icon sheets, diagrams, malformed anatomy/structure, unrelated imagery, or work that is not commercially publishable for the requested book type.");
        sb.AppendLine("8. `prompt-manifest.json` is the authoritative transport contract. Technical identifiers belong only in the Response manifest; never render them into artwork or inject them into the image-model visual brief.");
        sb.AppendLine("9. RESPONSE HEADER — HARD: copy `project_id`, `job_id`, `prompt_pack_id`, and `request_snapshot_id` EXACTLY from `prompt-manifest.json`; set `transport` to `MANUAL`. These fields are mandatory even when every Work Unit is FAILED/INCOMPLETE. Never omit or regenerate them.");
        sb.AppendLine("10. For every result, preserve the exact `work_unit_id` and `target_candidate_version` from the corresponding manifest Work Unit. A FAILED/INCOMPLETE result has no fabricated primary asset.");
        sb.AppendLine("11. Files under `inputs/` may be used only according to the structured role/policy/fidelity declared in the manifest. Do not infer broader permission.");
        sb.AppendLine("12. Results return to Diez as Candidates or incomplete provider attempts. Do not approve or apply anything to the book; Vision/review and `Porta nel libro` remain separate Diez actions.");
        sb.AppendLine(string.IsNullOrWhiteSpace(expectedResponse)
            ? "13. Return ONE `diez-response` v1 ZIP containing one result record per Work Unit."
            : $"13. Return ONE `diez-response` v1 ZIP named EXACTLY `{expectedResponse}`, containing one result record per Work Unit.");
        sb.AppendLine();

        if (publisherMaterials.Count > 0)
        {
            sb.AppendLine("## Publisher materials");
            sb.AppendLine("The following files were intentionally included. Their machine-readable intent code, AI-use policy and fidelity are authoritative; do not expand their role.");
            sb.AppendLine();
            foreach (var material in publisherMaterials)
            {
                sb.AppendLine($"- `{material.FileName}` — intent `{material.IntentCode}`, policy `{material.AiUsePolicy}`, fidelity `{material.Fidelity}`.");
            }
            sb.AppendLine();
        }

        for (var i = 0; i < items.Count; i++)
        {
            sb.AppendLine($"## Image {i + 1:D3} of {items.Count:D3}");
            sb.AppendLine("<<< DIEZ VISUAL PROMPT START >>>");
            sb.AppendLine(items[i].Prompt.Trim());
            sb.AppendLine("<<< DIEZ VISUAL PROMPT END >>>");
            sb.AppendLine();
        }

        sb.AppendLine("## Delivery check");
        sb.AppendLine($"Return exactly {items.Count} result records, one per Work Unit. No Diez ID, filename, watermark, prompt fragment, or protocol label may appear inside generated artwork.");
        if (!string.IsNullOrWhiteSpace(expectedResponse))
            sb.AppendLine($"The final ZIP filename is `{expectedResponse}` unless the platform technically prevents renaming; even then, preserve the complete Diez manifest identity exactly.");
        sb.AppendLine("If the platform demonstrably contaminates a render with previous images, use the historical clean-room fallback; this is an execution fallback, never permission to change the frozen Work Unit semantics.");
        return sb.ToString().Trim();
    }

    public static async Task<DiezPromptPackBuildResult> BuildManualPackageAsync(string projectJson, string? projectPackagePath, IEnumerable<Guid>? workUnitIds, string outputPath)
    {
        var ids = workUnitIds?.Where(x => x != Guid.Empty).Distinct().ToList();
        var effectiveJson = projectJson;
        var refreshed = DiezVisualHardPromptFrontendBridge.Recompile(projectJson, ids);
        if (refreshed.Success) effectiveJson = refreshed.ProjectJson;

        var packagePrompt = BuildPackagePrompt(effectiveJson, ids);
        var built = await DiezPromptPackFrontendBridge.BuildManualAsync(effectiveJson, projectPackagePath, ids, outputPath);
        if (!built.Success || string.IsNullOrWhiteSpace(built.OutputPath) || !File.Exists(built.OutputPath)) return built;
        try
        {
            using var archive = ZipFile.Open(built.OutputPath, ZipArchiveMode.Update);
            archive.GetEntry(PromptEntryName)?.Delete();
            var entry = archive.CreateEntry(PromptEntryName, CompressionLevel.Optimal);
            await using var stream = entry.Open();
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            await writer.WriteAsync(packagePrompt);
        }
        catch (Exception ex)
        {
            return built with
            {
                Success = false,
                Status = "PROMPT_ENTRY_FAILED",
                Message = "Lo ZIP canonico è stato creato ma manca PROMPT.md; il Prompt Pack non viene considerato consegnabile: " + ex.GetBaseException().Message
            };
        }
        return built with
        {
            Message = $"Prompt Pack ZIP pronto: {built.WorkUnitCount} immagini in un'unica consegna · {Path.GetFileName(built.OutputPath)} · ingresso AI: {PromptEntryName}."
        };
    }

    private static string ExpectedResponseFileName(string projectJson)
    {
        try
        {
            var root = JsonNode.Parse(projectJson) as JsonObject;
            if (root?["AiExchangeNaming"] is not JsonObject naming) return string.Empty;
            if (naming["ExpectedResponseFileName"] is JsonValue value && value.TryGetValue<string>(out var name))
                return name ?? string.Empty;
        }
        catch { }
        return string.Empty;
    }

    private static IReadOnlyList<PublisherMaterialPromptInfo> PublisherMaterials(string projectJson)
    {
        try
        {
            var root = JsonNode.Parse(projectJson) as JsonObject;
            if (root?["Materials"] is not JsonArray materials) return [];
            var result = new List<PublisherMaterialPromptInfo>();
            foreach (var material in materials.OfType<JsonObject>())
            {
                if (material["PublisherIntent"] is not JsonObject intent) continue;
                var code = ReadString(intent, "IntentCode");
                var policy = ReadString(intent, "AiUsePolicy");
                if (string.IsNullOrWhiteSpace(code) ||
                    string.Equals(code, "UNASSIGNED", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(policy, "NEVER_SEND", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(policy, "DIRECT_ASSET", StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(new PublisherMaterialPromptInfo(
                    ReadString(material, "FileName"),
                    code,
                    ReadString(intent, "IntentLabel"),
                    ReadString(intent, "Instruction"),
                    policy,
                    ReadString(intent, "Fidelity")));
            }
            return result;
        }
        catch { return []; }
    }

    private static string ReadString(JsonObject obj, string name) =>
        obj[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text ?? string.Empty : string.Empty;
}
