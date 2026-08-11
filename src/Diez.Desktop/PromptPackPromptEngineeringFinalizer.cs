using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiezPublishingStudio;

/// <summary>
/// Authoritative final compiler for visual Prompt Packs. Transport code supplies IDs/files;
/// this class resolves current parameters, manual edits, provider strategy and per-item contracts.
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

    private sealed record ResolvedMaster(
        string ExportPrompt,
        string CanonicalPrompt,
        string ManualDelta,
        bool ManualPresent,
        bool ManualCurrent,
        bool Recompiled);

    public static void Finalize(
        string promptPackPath,
        PreviewProject project,
        AiExchangeState state,
        IEnumerable<Guid> workUnitIds)
    {
        if (!File.Exists(promptPackPath)) return;
        var ids = workUnitIds.Distinct().ToHashSet();
        var units = state.WorkUnits
            .Where(u => ids.Contains(u.WorkUnitId))
            .OrderBy(u => u.Position)
            .ThenBy(u => u.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (units.Count == 0) return;

        var settings = PromptPreparationSettingsStore.Load(project);
        var master = ResolveMaster(project, units.Count, settings);

        using var archive = ZipFile.Open(promptPackPath, ZipArchiveMode.Update);
        RewriteManifest(archive, project, units, master, settings);
        RewriteContext(archive, project, master, settings);
        RewriteInstructions(archive, project, master, settings);
    }

    private static ResolvedMaster ResolveMaster(
        PreviewProject project,
        int count,
        PromptPreparationSettings settings)
    {
        var stored = PromptMasterStateStore.LoadForCurrentBook(project);
        var mustDo = stored?.MustDo ?? string.Empty;
        var mustNotDo = stored?.MustNotDo ?? string.Empty;
        var canonical = PromptEngineeringCompiler.BuildSeriesPrompt(
            project,
            count,
            mustDo,
            mustNotDo,
            settings.ProviderId,
            settings.PreferAdvancedModel);

        var metadata = PromptMasterMetadataStore.Load(project);
        var current = PromptMasterMetadataStore.MatchesCurrent(
            project,
            metadata,
            count,
            mustDo,
            mustNotDo,
            settings.ProviderId,
            settings.PreferAdvancedModel);
        var storedPrompt = stored?.Prompt?.Trim() ?? string.Empty;

        if (current && !string.IsNullOrWhiteSpace(storedPrompt))
        {
            return new ResolvedMaster(
                storedPrompt,
                canonical,
                string.Empty,
                metadata?.ManualOverride == true,
                metadata?.ManualOverride == true,
                false);
        }

        if (metadata?.ManualOverride == true && !string.IsNullOrWhiteSpace(storedPrompt))
        {
            var delta = PromptMasterMetadataStore.ExtractManualDelta(metadata, storedPrompt).Trim();
            if (delta.Length == 0)
                return new ResolvedMaster(canonical, canonical, string.Empty, true, false, true);

            var merged = new StringBuilder(canonical.Trim());
            merged.AppendLine();
            merged.AppendLine();
            merged.AppendLine("=== USER MANUAL DELTA — PRESERVED FROM THE EDITOR ===");
            merged.AppendLine("These are the user-added/changed lines detected against the previous generated baseline. Preserve their creative/editorial intent. CURRENT structured parameters, hard Book-Type rules, technical values, item overrides and Consistent rules above take priority on conflict.");
            merged.AppendLine();
            merged.AppendLine(delta);
            return new ResolvedMaster(merged.ToString().Trim(), canonical, delta, true, false, true);
        }

        PromptMasterStateStore.Save(project, new PromptMasterState
        {
            BookType = BookTypeProfileService.Get(project),
            ProviderId = settings.ProviderId,
            PreferAdvancedModel = settings.PreferAdvancedModel,
            SeriesCount = count,
            MustDo = mustDo,
            MustNotDo = mustNotDo,
            Prompt = canonical,
            UpdatedAtLocal = DateTimeOffset.Now.ToString("O")
        });
        PromptMasterMetadataStore.MarkGenerated(
            project, count, mustDo, mustNotDo, settings.ProviderId, settings.PreferAdvancedModel);
        return new ResolvedMaster(canonical, canonical, string.Empty, false, false, true);
    }

    private static void RewriteManifest(
        ZipArchive archive,
        PreviewProject project,
        IReadOnlyList<AiExchangeWorkUnit> units,
        ResolvedMaster master,
        PromptPreparationSettings settings)
    {
        var root = ReadObject(archive, ManifestName);
        var array = root?["work_units"] as JsonArray;
        if (root is null || array is null) return;

        foreach (var node in array.OfType<JsonObject>())
        {
            if (!Guid.TryParse(node["id"]?.ToString(), out var id)) continue;
            var unit = units.FirstOrDefault(u => u.WorkUnitId == id);
            if (unit is null) continue;
            var index = units.IndexOf(unit) + 1;

            node["instruction"] = BuildUnitInstruction(
                project, unit, master.ExportPrompt, units.Count, index, settings);
            node["prompt_engine_version"] = PromptEngineeringEngine.EngineVersion;
            node["prompt_compiler_version"] = PromptEngineeringCompiler.Version;
            node["provider_target"] = settings.ProviderId;
            node["series_position"] = index;
            node["series_count"] = units.Count;
            node["output_count_for_this_work_unit"] = 1;
        }

        root["prompt_engine"] = new JsonObject
        {
            ["engine"] = "diez-prompt-engineering",
            ["semantic_engine_version"] = PromptEngineeringEngine.EngineVersion,
            ["provider_compiler_version"] = PromptEngineeringCompiler.Version,
            ["provider_target"] = settings.ProviderId,
            ["prefer_advanced_model"] = settings.PreferAdvancedModel,
            ["active_book_type"] = BookTypeProfileService.Get(project),
            ["master_prompt"] = master.ExportPrompt,
            ["canonical_prompt"] = master.CanonicalPrompt,
            ["manual_delta"] = master.ManualDelta,
            ["manual_prompt_present"] = master.ManualPresent,
            ["manual_prompt_current"] = master.ManualCurrent,
            ["recompiled_for_current_parameters"] = master.Recompiled,
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

        var mutation = unit.Preserve.Count > 0 || unit.Change.Count > 0 || unit.Add.Count > 0 || unit.Remove.Count > 0 ||
                       !string.IsNullOrWhiteSpace(unit.Instruction) ||
                       string.Equals(unit.Mode, AiExchangeModes.AiWithInputAsReference, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(unit.Mode, AiExchangeModes.InputTransformedByAi, StringComparison.OrdinalIgnoreCase);
        if (!mutation) return sb.ToString().Trim();

        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("=== DIEZ SOURCE-IMAGE / MODIFICATION CONTRACT — HIGHEST PRIORITY ===");
        sb.AppendLine($"Generation mode: {unit.Mode}.");
        if (string.Equals(unit.Mode, AiExchangeModes.AiWithInputAsReference, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(unit.Mode, AiExchangeModes.InputTransformedByAi, StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine("Use the actual base/input image file supplied by Diez as the authoritative visual source. Do NOT reconstruct it from the text description alone.");
            sb.AppendLine("For a local correction, modify that source image rather than inventing a replacement composition unless REGENERATE is explicitly requested.");
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
        sb.AppendLine("Priority: explicit item constraint > preserve/change/add/remove > local exception > CURRENT hard Book-Type rules > LOCKED consistency > PREFERRED consistency > manual/shared intent > creative freedom.");
        sb.AppendLine("The returned description must match the actual edited result; never reuse a stale description of the base image.");
        return sb.ToString().Trim();
    }

    private static void RewriteContext(
        ZipArchive archive,
        PreviewProject project,
        ResolvedMaster master,
        PromptPreparationSettings settings)
    {
        var root = ReadObject(archive, ContextName);
        if (root is null) return;
        var type = BookTypeProfileService.Get(project);
        var presets = root["image_presets"] as JsonObject ?? new JsonObject();

        if (string.Equals(type, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase))
        {
            presets.Remove("illustration_profile");
            presets["active_profile_kind"] = "COLORING_BOOK";
        }
        else
        {
            presets.Remove("coloring_profile");
            presets["active_profile_kind"] = string.Equals(type, BookTypeProfileService.IllustratedBook, StringComparison.OrdinalIgnoreCase)
                ? "ILLUSTRATED_BOOK"
                : "IMAGE_COLLECTION";
        }

        presets["effective_visual_prompt"] = master.ExportPrompt;
        presets["canonical_visual_prompt"] = master.CanonicalPrompt;
        presets["provider_target"] = settings.ProviderId;
        presets["semantic_engine_version"] = PromptEngineeringEngine.EngineVersion;
        presets["provider_compiler_version"] = PromptEngineeringCompiler.Version;
        root["image_presets"] = presets;
        root["active_prompt_profile"] = new JsonObject
        {
            ["book_type"] = type,
            ["provider_target"] = settings.ProviderId,
            ["semantic_engine_version"] = PromptEngineeringEngine.EngineVersion,
            ["provider_compiler_version"] = PromptEngineeringCompiler.Version,
            ["profile_isolation"] = true,
            ["manual_prompt_present"] = master.ManualPresent,
            ["manual_prompt_current"] = master.ManualCurrent,
            ["manual_delta"] = master.ManualDelta,
            ["rule"] = "Only this active Book Type profile may influence the current request. Historical/inactive profiles are excluded. Current hard constraints override stale manual technical values; only actual user-added/changed lines are carried forward as manual delta."
        };
        ReplaceObject(archive, ContextName, root);
    }

    private static void RewriteInstructions(
        ZipArchive archive,
        PreviewProject project,
        ResolvedMaster master,
        PromptPreparationSettings settings)
    {
        var existing = ReadText(archive, InstructionsName);
        if (existing.Contains("## Diez Prompt Engineering", StringComparison.Ordinal)) return;
        var section = $"""

## Diez Prompt Engineering — AUTHORITATIVE
- Semantic engine: {PromptEngineeringEngine.EngineVersion}; provider compiler: {PromptEngineeringCompiler.Version}.
- Active Book Type: {BookTypeProfileService.Get(project)}.
- Target renderer: {settings.ProviderId}.
- Only the active Book Type prompt profile is valid for this request; historical/inactive visual profiles must be ignored.
- Every `work_units[].instruction` requests EXACTLY ONE image. `series_count` is context only and never authorizes a grid, collage or multiple alternatives.
- `output_count_for_this_work_unit = 1` is a hard execution contract.
- Professional quality gates remain mandatory even with very few optional GUI parameters.
- Manual prompt text is preserved exactly while parameters are unchanged. After structured parameters change, only user-added/changed lines are carried into the current canonical prompt as an additive manual delta.
- For corrections, the real base/input image plus preserve/change/add/remove are authoritative; descriptions assist but never replace image files.
""";
        ReplaceText(archive, InstructionsName, existing.TrimEnd() + section);
    }

    private static void AppendList(StringBuilder sb, string title, IReadOnlyCollection<string> values)
    {
        var clean = values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToList();
        if (clean.Count == 0) return;
        sb.AppendLine(title + ":");
        foreach (var value in clean) sb.AppendLine("- " + value);
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
