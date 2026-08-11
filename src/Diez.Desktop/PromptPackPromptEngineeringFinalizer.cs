using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiezPublishingStudio;

/// <summary>
/// Final authoritative prompt stage for visual Prompt Packs.
/// The core builder owns IDs/snapshots/assets; this finalizer owns prompt engineering:
/// - one active Book Type profile only (no Coloring/Illustration cross-contamination);
/// - provider-specific professional master prompt;
/// - exactly one generated image per Work Unit;
/// - exact manual master-prompt edits preserved as the common specification;
/// - correction/edit grammar preserved at item level.
/// </summary>
internal static class PromptPackPromptEngineeringFinalizer
{
    private const string ManifestName = "prompt-manifest.json";
    private const string ContextName = "request-context.json";
    private const string InstructionsName = "instructions.md";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static void Finalize(
        string promptPackPath,
        PreviewProject project,
        AiExchangeState state,
        IEnumerable<Guid> workUnitIds)
    {
        var ids = workUnitIds.Distinct().ToHashSet();
        var units = state.WorkUnits
            .Where(u => ids.Contains(u.WorkUnitId))
            .OrderBy(u => u.Position)
            .ThenBy(u => u.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (units.Count == 0 || !File.Exists(promptPackPath)) return;

        var settings = PromptPreparationSettingsStore.Load(project);
        var masterState = PromptMasterStateStore.LoadForCurrentBook(project);
        var masterPrompt = masterState?.Prompt?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(masterPrompt))
        {
            masterPrompt = PromptEngineeringEngine.BuildSeriesPrompt(
                project,
                units.Count,
                masterState?.MustDo ?? string.Empty,
                masterState?.MustNotDo ?? string.Empty,
                settings.ProviderId,
                settings.PreferAdvancedModel);
            PromptMasterStateStore.SaveDraft(project, units.Count, masterState?.MustDo, masterState?.MustNotDo, masterPrompt);
        }

        using var archive = ZipFile.Open(promptPackPath, ZipArchiveMode.Update);
        RewriteManifest(archive, project, units, masterPrompt, settings);
        RewriteContext(archive, project, masterPrompt, settings);
        RewriteInstructions(archive, project, settings);
    }

    private static void RewriteManifest(
        ZipArchive archive,
        PreviewProject project,
        IReadOnlyList<AiExchangeWorkUnit> units,
        string masterPrompt,
        PromptPreparationSettings settings)
    {
        var root = ReadObject(archive, ManifestName);
        if (root is null) return;
        var array = root["work_units"] as JsonArray;
        if (array is null) return;

        foreach (var node in array.OfType<JsonObject>())
        {
            if (!Guid.TryParse(node["id"]?.ToString(), out var id)) continue;
            var unit = units.FirstOrDefault(u => u.WorkUnitId == id);
            if (unit is null) continue;
            var index = units.IndexOf(unit) + 1;
            node["instruction"] = BuildUnitInstruction(project, unit, masterPrompt, units.Count, index, settings);
            node["prompt_engine_version"] = PromptEngineeringEngine.EngineVersion;
            node["provider_target"] = settings.ProviderId;
            node["series_position"] = index;
            node["series_count"] = units.Count;
            node["output_count_for_this_work_unit"] = 1;
        }

        root["prompt_engine"] = new JsonObject
        {
            ["engine"] = "diez-prompt-engineering",
            ["version"] = PromptEngineeringEngine.EngineVersion,
            ["provider_target"] = settings.ProviderId,
            ["prefer_advanced_model"] = settings.PreferAdvancedModel,
            ["active_book_type"] = BookTypeProfileService.Get(project),
            ["master_prompt"] = masterPrompt,
            ["work_unit_output_count"] = 1
        };
        ReplaceObject(archive, ManifestName, root);
    }

    private static string BuildUnitInstruction(
        PreviewProject project,
        AiExchangeWorkUnit unit,
        string masterPrompt,
        int total,
        int index,
        PromptPreparationSettings settings)
    {
        var sb = new StringBuilder(PromptEngineeringEngine.BuildItemPrompt(
            project,
            masterPrompt,
            total,
            index,
            unit.Code,
            settings.ProviderId,
            settings.PreferAdvancedModel));

        var hasMutation = unit.Preserve.Count > 0 || unit.Change.Count > 0 || unit.Add.Count > 0 || unit.Remove.Count > 0 ||
                          !string.IsNullOrWhiteSpace(unit.Instruction) ||
                          string.Equals(unit.Mode, AiExchangeModes.AiWithInputAsReference, StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(unit.Mode, AiExchangeModes.InputTransformedByAi, StringComparison.OrdinalIgnoreCase);
        if (!hasMutation) return sb.ToString().Trim();

        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("=== DIEZ SOURCE-IMAGE / MODIFICATION CONTRACT — HIGHEST PRIORITY ===");
        sb.AppendLine($"Generation mode: {unit.Mode}.");
        if (string.Equals(unit.Mode, AiExchangeModes.AiWithInputAsReference, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(unit.Mode, AiExchangeModes.InputTransformedByAi, StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine("Use the actual base/input image file supplied by Diez as the authoritative visual source. Do NOT recreate it from the textual description alone.");
            sb.AppendLine("For a local correction, modify the supplied source image rather than inventing a replacement composition unless the instruction explicitly requests regeneration.");
        }
        AppendList(sb, "PRESERVE — keep visually unchanged unless physically impossible", unit.Preserve);
        AppendList(sb, "CHANGE — modify exactly these elements", unit.Change);
        AppendList(sb, "ADD — introduce these elements", unit.Add);
        AppendList(sb, "REMOVE — eliminate these elements", unit.Remove);
        if (!string.IsNullOrWhiteSpace(unit.Instruction))
        {
            sb.AppendLine("EXPLICIT WORK-UNIT INSTRUCTION:");
            sb.AppendLine(unit.Instruction.Trim());
        }
        sb.AppendLine("Priority inside this Work Unit: explicit item constraint > preserve/change/add/remove > local exception > LOCKED consistency > PREFERRED consistency > shared master prompt > creative freedom.");
        sb.AppendLine("After editing, the returned description must describe the actual final image, including the requested change; never copy a stale description of the base image.");
        return sb.ToString().Trim();
    }

    private static void AppendList(StringBuilder sb, string title, IReadOnlyCollection<string> values)
    {
        var clean = values.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
        if (clean.Count == 0) return;
        sb.AppendLine(title + ":");
        foreach (var value in clean) sb.AppendLine("- " + value);
    }

    private static void RewriteContext(
        ZipArchive archive,
        PreviewProject project,
        string masterPrompt,
        PromptPreparationSettings settings)
    {
        var root = ReadObject(archive, ContextName);
        if (root is null) return;
        var bookType = BookTypeProfileService.Get(project);
        var presets = root["image_presets"] as JsonObject ?? new JsonObject();

        // Only one semantic profile is active. Historical profiles may remain in .diez,
        // but they must never be exported together to the provider.
        if (string.Equals(bookType, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase))
        {
            presets.Remove("illustration_profile");
            presets["active_profile_kind"] = "COLORING_BOOK";
        }
        else
        {
            presets.Remove("coloring_profile");
            presets["active_profile_kind"] = string.Equals(bookType, BookTypeProfileService.IllustratedBook, StringComparison.OrdinalIgnoreCase)
                ? "ILLUSTRATED_BOOK"
                : "IMAGE_COLLECTION";
        }

        presets["effective_visual_prompt"] = masterPrompt;
        presets["provider_target"] = settings.ProviderId;
        presets["prompt_engine_version"] = PromptEngineeringEngine.EngineVersion;
        root["image_presets"] = presets;
        root["active_prompt_profile"] = new JsonObject
        {
            ["book_type"] = bookType,
            ["provider_target"] = settings.ProviderId,
            ["engine_version"] = PromptEngineeringEngine.EngineVersion,
            ["profile_isolation"] = true,
            ["rule"] = "Only this active Book Type profile may influence the current request. Historical/inactive visual profiles are excluded."
        };
        ReplaceObject(archive, ContextName, root);
    }

    private static void RewriteInstructions(
        ZipArchive archive,
        PreviewProject project,
        PromptPreparationSettings settings)
    {
        var existing = ReadText(archive, InstructionsName);
        if (existing.Contains("## Diez Prompt Engineering v", StringComparison.Ordinal)) return;
        var section = $"""

## Diez Prompt Engineering v{PromptEngineeringEngine.EngineVersion} — AUTHORITATIVE
- Active Book Type: {BookTypeProfileService.Get(project)}.
- Target renderer: {settings.ProviderId}.
- Only the active Book Type prompt profile is valid for this request. Ignore historical/inactive profiles even if the source .diez project contains them.
- Every `work_units[].instruction` is self-contained and requests EXACTLY ONE image for that Work Unit.
- `series_count` is context only; it never authorizes a single Work Unit to render the whole series, a collage, a grid or multiple alternatives.
- Treat `output_count_for_this_work_unit = 1` as a hard execution contract.
- Professional quality gates are mandatory even when the user provided only a few optional GUI parameters.
- For corrections, the real base/input image plus preserve/change/add/remove are authoritative; descriptions assist but never replace image files.
""";
        ReplaceText(archive, InstructionsName, existing.TrimEnd() + section);
    }

    private static JsonObject? ReadObject(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path);
        if (entry is null) return null;
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        var text = reader.ReadToEnd();
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

    private static void ReplaceObject(ZipArchive archive, string path, JsonObject root) =>
        ReplaceText(archive, path, root.ToJsonString(JsonOptions));

    private static void ReplaceText(ZipArchive archive, string path, string text)
    {
        archive.GetEntry(path)?.Delete();
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(text ?? string.Empty);
    }
}

internal static class PromptEngineeringListExtensions
{
    public static int IndexOf<T>(this IReadOnlyList<T> list, T value)
    {
        for (var i = 0; i < list.Count; i++)
            if (EqualityComparer<T>.Default.Equals(list[i], value)) return i;
        return -1;
    }
}
