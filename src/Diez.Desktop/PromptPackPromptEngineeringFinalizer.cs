using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiezPublishingStudio;

/// <summary>
/// Final authoritative prompt compiler for visual Prompt Packs.
/// The transport builder owns IDs/snapshots/assets; this compiler owns prompt semantics:
/// one active Book Type, current parameter fingerprint, provider rendering, one output per Work Unit,
/// manual-edit preservation and source-image mutation grammar.
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

    private sealed record ResolvedMasterPrompt(
        string ExportPrompt,
        string CanonicalPrompt,
        bool ManualPromptPresent,
        bool ManualPromptCurrent,
        bool RegeneratedForCurrentParameters);

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
        var resolved = ResolveMasterPrompt(project, units.Count, settings);

        using var archive = ZipFile.Open(promptPackPath, ZipArchiveMode.Update);
        RewriteManifest(archive, project, units, resolved, settings);
        RewriteContext(archive, project, resolved, settings);
        RewriteInstructions(archive, project, resolved, settings);
    }

    private static ResolvedMasterPrompt ResolveMasterPrompt(
        PreviewProject project,
        int count,
        PromptPreparationSettings settings)
    {
        var stored = PromptMasterStateStore.LoadForCurrentBook(project);
        var mustDo = stored?.MustDo ?? string.Empty;
        var mustNotDo = stored?.MustNotDo ?? string.Empty;
        var canonical = PromptEngineeringEngine.BuildSeriesPrompt(
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
            return new ResolvedMasterPrompt(
                storedPrompt,
                canonical,
                metadata?.ManualOverride == true,
                metadata?.ManualOverride == true,
                false);
        }

        if (metadata?.ManualOverride == true && !string.IsNullOrWhiteSpace(storedPrompt))
        {
            // Parameters changed after an explicit manual prompt edit. Never erase the user's text,
            // but never let stale technical/book constraints override the current GUI either.
            // Export the freshly compiled canonical specification first, then preserve the old manual
            // text verbatim as an additive intent layer subordinate to current hard constraints.
            var merged = new StringBuilder(canonical.Trim());
            merged.AppendLine();
            merged.AppendLine();
            merged.AppendLine("=== USER MANUAL PROMPT LAYER — PRESERVED VERBATIM ===");
            merged.AppendLine("The following text was manually edited by the user before one or more structured Diez parameters changed. Preserve its creative/editorial intent, but CURRENT hard Book-Type constraints, current technical output settings, current item overrides and current Consistent rules above have priority wherever the texts conflict.");
            merged.AppendLine("Do not copy obsolete counts, dimensions, provider names or technical values from this preserved layer when they differ from the current canonical specification.");
            merged.AppendLine();
            merged.AppendLine(storedPrompt);

            PromptMasterStateStore.Save(project, new PromptMasterState
            {
                BookType = BookTypeProfileService.Get(project),
                ProviderId = settings.ProviderId,
                PreferAdvancedModel = settings.PreferAdvancedModel,
                SeriesCount = count,
                MustDo = mustDo,
                MustNotDo = mustNotDo,
                Prompt = storedPrompt,
                UpdatedAtLocal = DateTimeOffset.Now.ToString("O")
            });
            // Keep metadata manual/stale on purpose: the editor still contains the user's original
            // manual text and must not be silently replaced when the page is reopened.
            return new ResolvedMasterPrompt(merged.ToString().Trim(), canonical, true, false, true);
        }

        // No trusted current manual edit: legacy/obsolete/generated text is replaced by the current
        // canonical compiler output. This is the migration path that removes weak legacy prompts.
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
        return new ResolvedMasterPrompt(canonical, canonical, false, false, true);
    }

    private static void RewriteManifest(
        ZipArchive archive,
        PreviewProject project,
        IReadOnlyList<AiExchangeWorkUnit> units,
        ResolvedMasterPrompt resolved,
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
            node["instruction"] = BuildUnitInstruction(project, unit, resolved.ExportPrompt, units.Count, index, settings);
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
            ["master_prompt"] = resolved.ExportPrompt,
            ["canonical_prompt"] = resolved.CanonicalPrompt,
            ["manual_prompt_present"] = resolved.ManualPromptPresent,
            ["manual_prompt_current"] = resolved.ManualPromptCurrent,
            ["regenerated_for_current_parameters"] = resolved.RegeneratedForCurrentParameters,
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
        sb.AppendLine("Priority inside this Work Unit: explicit item constraint > preserve/change/add/remove > local exception > CURRENT hard Book-Type rules > LOCKED consistency > PREFERRED consistency > manual/shared intent > creative freedom.");
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
        ResolvedMasterPrompt resolved,
        PromptPreparationSettings settings)
    {
        var root = ReadObject(archive, ContextName);
        if (root is null) return;
        var bookType = BookTypeProfileService.Get(project);
        var presets = root["image_presets"] as JsonObject ?? new JsonObject();

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

        presets["effective_visual_prompt"] = resolved.ExportPrompt;
        presets["canonical_visual_prompt"] = resolved.CanonicalPrompt;
        presets["provider_target"] = settings.ProviderId;
        presets["prompt_engine_version"] = PromptEngineeringEngine.EngineVersion;
        root["image_presets"] = presets;
        root["active_prompt_profile"] = new JsonObject
        {
            ["book_type"] = bookType,
            ["provider_target"] = settings.ProviderId,
            ["engine_version"] = PromptEngineeringEngine.EngineVersion,
            ["profile_isolation"] = true,
            ["manual_prompt_present"] = resolved.ManualPromptPresent,
            ["manual_prompt_current"] = resolved.ManualPromptCurrent,
            ["rule"] = "Only this active Book Type profile may influence the current request. Historical/inactive visual profiles are excluded. Current hard constraints override stale manual technical values without deleting the user's manual text."
        };
        ReplaceObject(archive, ContextName, root);
    }

    private static void RewriteInstructions(
        ZipArchive archive,
        PreviewProject project,
        ResolvedMasterPrompt resolved,
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
- Manual prompt text is preserved. If its parameter fingerprint is stale, current canonical Book-Type/technical constraints take priority while the manual text remains an additive creative/editorial layer.
- Manual prompt present: {resolved.ManualPromptPresent}; current fingerprint: {resolved.ManualPromptCurrent}.
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
