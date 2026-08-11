using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiezPublishingStudio;

/// <summary>
/// Material/context layer for visual Prompt Packs. This layer exports real files and the structured
/// context that actually belongs to the ACTIVE Book Type. It intentionally does not emit historical
/// visual profiles or layout-stage fields and does not own final prompt compilation.
/// </summary>
internal static class AiExchangeImageRequestContextService
{
    private const string IntakeEntityKind = "DiezAiImageIntake";
    private const string ManifestName = "prompt-manifest.json";
    private const string InstructionsName = "instructions.md";
    private const string ContextName = "request-context.json";
    private const string IntakeIndexName = "inputs/intake/intake-index.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static readonly string[] IntakeRoles =
    [
        "REFERENCE",
        "SOURCE",
        "CONTENT_TO_TRANSFORM",
        "CONTENT_TO_COMBINE"
    ];

    internal sealed class IntakeState
    {
        public int SchemaVersion { get; set; } = 1;
        public List<IntakeImage> Images { get; set; } = [];
    }

    internal sealed class IntakeImage
    {
        public Guid IntakeId { get; set; } = Guid.NewGuid();
        public Guid MaterialId { get; set; }
        public string Type { get; set; } = "IMAGE";
        public string Role { get; set; } = "REFERENCE";
        public string Description { get; set; } = string.Empty;
        public string Scope { get; set; } = "COLLECTION";
        public List<Guid> WorkUnitIds { get; set; } = [];
        public string CreatedAtLocal { get; set; } = string.Empty;
    }

    internal readonly record struct EnhanceResult(bool Success, string Message, int IntakeImages, int BaseImages);

    public static IntakeState Load(PreviewProject project)
    {
        var entity = project.Entities.FirstOrDefault(e =>
            string.Equals(e.Kind, IntakeEntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null || string.IsNullOrWhiteSpace(entity.Notes)) return new IntakeState();
        try
        {
            var state = JsonSerializer.Deserialize<IntakeState>(entity.Notes, JsonOptions) ?? new IntakeState();
            state.Images ??= [];
            foreach (var image in state.Images)
            {
                if (image.IntakeId == Guid.Empty) image.IntakeId = Guid.NewGuid();
                image.Type = "IMAGE";
                image.Role = NormalizeRole(image.Role);
                image.Description ??= string.Empty;
                image.Scope = string.IsNullOrWhiteSpace(image.Scope) ? "COLLECTION" : image.Scope;
                image.WorkUnitIds ??= [];
            }
            return state;
        }
        catch { return new IntakeState(); }
    }

    public static void Save(PreviewProject project, IntakeState state)
    {
        state.SchemaVersion = 1;
        state.Images ??= [];
        var entity = project.Entities.FirstOrDefault(e =>
            string.Equals(e.Kind, IntakeEntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
        {
            entity = new GraphEntity
            {
                Kind = IntakeEntityKind,
                Name = "Intake immagini AI",
                IsCandidate = false,
                Notes = string.Empty
            };
            project.Entities.Add(entity);
        }
        entity.IsCandidate = false;
        entity.Notes = JsonSerializer.Serialize(state, JsonOptions);
    }

    public static IntakeImage Add(
        PreviewProject project,
        Guid materialId,
        string role,
        string description,
        IEnumerable<Guid>? workUnitIds = null)
    {
        var state = Load(project);
        var ids = workUnitIds?.Distinct().ToList() ?? [];
        var image = new IntakeImage
        {
            MaterialId = materialId,
            Role = NormalizeRole(role),
            Description = (description ?? string.Empty).Trim(),
            Scope = ids.Count == 0 ? "COLLECTION" : "ITEM",
            WorkUnitIds = ids,
            CreatedAtLocal = DateTimeOffset.Now.ToString("O")
        };
        state.Images.Add(image);
        Save(project, state);
        return image;
    }

    public static bool Remove(PreviewProject project, Guid intakeId)
    {
        var state = Load(project);
        var removed = state.Images.RemoveAll(x => x.IntakeId == intakeId) > 0;
        if (removed) Save(project, state);
        return removed;
    }

    public static IReadOnlyList<IntakeImage> Relevant(PreviewProject project, IEnumerable<Guid> workUnitIds)
    {
        var ids = workUnitIds.ToHashSet();
        return Load(project).Images
            .Where(x => x.WorkUnitIds.Count == 0 || x.WorkUnitIds.Any(ids.Contains))
            .ToList();
    }

    /// <summary>
    /// Compatibility entry point used by older callers. It now delegates to the same canonical
    /// provider compiler used by the prompt page and final Prompt Pack compiler.
    /// </summary>
    public static string BuildEffectiveVisualPrompt(PreviewProject project)
    {
        var settings = PromptPreparationSettingsStore.Load(project);
        var master = PromptMasterStateStore.LoadForCurrentBook(project);
        var count = Math.Max(1,
            master?.SeriesCount ?? VisualPromptSessionService.ActiveImageJobs(project).Count);
        return PromptEngineeringCompiler.BuildSeriesPrompt(
            project,
            count,
            master?.MustDo ?? string.Empty,
            master?.MustNotDo ?? string.Empty,
            settings.ProviderId,
            settings.PreferAdvancedModel);
    }

    /// <summary>
    /// Adds exact real intake/base images, descriptions, paradigms and structured visual context.
    /// PromptPackPromptEngineeringFinalizer subsequently compiles the final per-item instructions.
    /// </summary>
    public static async Task<EnhanceResult> EnhancePromptPackAsync(
        PreviewProject project,
        string projectPath,
        AiExchangeState exchangeState,
        IEnumerable<Guid> workUnitIds,
        string promptPackPath)
    {
        var selectedIds = workUnitIds.Distinct().ToList();
        if (!File.Exists(promptPackPath))
            return new EnhanceResult(false, "Prompt Pack non trovato dopo la creazione.", 0, 0);

        var intake = Relevant(project, selectedIds);
        var baseCount = 0;

        using var archive = ZipFile.Open(promptPackPath, ZipArchiveMode.Update);
        var manifestEntry = archive.GetEntry(ManifestName);
        if (manifestEntry is null)
            return new EnhanceResult(false, "Prompt Pack privo di prompt-manifest.json.", 0, 0);

        JsonObject manifest;
        await using (var stream = manifestEntry.Open())
        using (var reader = new StreamReader(stream, Encoding.UTF8, true, leaveOpen: false))
        {
            manifest = JsonNode.Parse(await reader.ReadToEndAsync())?.AsObject()
                ?? throw new InvalidDataException("prompt-manifest.json non leggibile.");
        }
        manifestEntry.Delete();

        var intakeArray = new JsonArray();
        foreach (var item in intake)
        {
            var material = project.Materials.FirstOrDefault(m => m.MaterialId == item.MaterialId);
            if (material is null) continue;
            var bytes = await ProjectFileStore.ReadEmbeddedMaterialAsync(projectPath, material);
            if (bytes is null || bytes.Length == 0) continue;

            var file = $"inputs/intake/{item.IntakeId:D}/{SafeName(material.FileName)}";
            ReplaceBinaryEntry(archive, file, bytes);
            intakeArray.Add(new JsonObject
            {
                ["intake_id"] = item.IntakeId.ToString("D"),
                ["type"] = "IMAGE",
                ["role"] = item.Role,
                ["description"] = item.Description,
                ["scope"] = item.Scope,
                ["work_unit_ids"] = new JsonArray(item.WorkUnitIds.Select(x => JsonValue.Create(x.ToString("D"))).ToArray()),
                ["material_id"] = item.MaterialId.ToString("D"),
                ["file"] = file,
                ["sha256"] = material.Sha256 ?? string.Empty,
                ["file_name"] = material.FileName ?? string.Empty
            });
        }

        var workUnitsArray = manifest["work_units"] as JsonArray ?? new JsonArray();
        foreach (var node in workUnitsArray.OfType<JsonObject>())
        {
            if (!Guid.TryParse(node["id"]?.ToString(), out var workUnitId)) continue;
            var unit = exchangeState.WorkUnits.FirstOrDefault(x => x.WorkUnitId == workUnitId);
            if (unit is null) continue;

            node["intake_ids"] = new JsonArray(intake
                .Where(x => x.WorkUnitIds.Count == 0 || x.WorkUnitIds.Contains(workUnitId))
                .Select(x => JsonValue.Create(x.IntakeId.ToString("D"))).ToArray());
            node["request_context_file"] = ContextName;
            node["instruction"] = unit.Instruction;
            node["preserve"] = new JsonArray(unit.Preserve.Select(x => JsonValue.Create(x)).ToArray());
            node["change"] = new JsonArray(unit.Change.Select(x => JsonValue.Create(x)).ToArray());
            node["add"] = new JsonArray(unit.Add.Select(x => JsonValue.Create(x)).ToArray());
            node["remove"] = new JsonArray(unit.Remove.Select(x => JsonValue.Create(x)).ToArray());

            var baseVersion = ResolveBaseVersion(exchangeState, unit);
            if (baseVersion?.MaterialId is not Guid materialId) continue;
            var material = project.Materials.FirstOrDefault(m => m.MaterialId == materialId);
            if (material is null) continue;

            var baseFile = $"inputs/current/{workUnitId:D}/{SafeName(material.FileName)}";
            var baseObject = node["base_version"] as JsonObject ?? new JsonObject();
            baseObject["version_id"] = baseVersion.VersionId.ToString("D");
            baseObject["version_number"] = baseVersion.VersionNumber;
            baseObject["file"] = baseFile;
            baseObject["description"] = baseVersion.Description ?? string.Empty;
            baseObject["description_status"] = baseVersion.DescriptionStatus ?? string.Empty;
            baseObject["sha256"] = material.Sha256 ?? baseVersion.ContentSha256 ?? string.Empty;
            baseObject["authoritative_visual_source"] = true;
            node["base_version"] = baseObject;
            baseCount++;
        }

        manifest["intake"] = intakeArray.DeepClone();
        manifest["request_context_file"] = ContextName;
        manifest["visual_context_protocol_version"] = 3;

        var requestContext = BuildRequestContext(project, exchangeState, selectedIds, intakeArray, workUnitsArray);
        ReplaceTextEntry(archive, ContextName, requestContext.ToJsonString(JsonOptions));
        ReplaceTextEntry(archive, IntakeIndexName, new JsonObject
        {
            ["schema"] = "diez-image-intake",
            ["schema_version"] = 1,
            ["rule"] = "Ogni descrizione accompagna il file reale; non sostituisce mai l'immagine allegata.",
            ["images"] = intakeArray.DeepClone()
        }.ToJsonString(JsonOptions));
        ReplaceTextEntry(archive, ManifestName, manifest.ToJsonString(JsonOptions));
        AppendInstructions(archive);

        return new EnhanceResult(
            true,
            $"Contesto immagini V3 aggiunto: {intakeArray.Count} foto intake reali, {baseCount} immagini base, un solo profilo attivo e specifiche tecniche correnti.",
            intakeArray.Count,
            baseCount);
    }

    private static JsonObject BuildRequestContext(
        PreviewProject project,
        AiExchangeState exchangeState,
        IReadOnlyCollection<Guid> selectedIds,
        JsonArray intake,
        JsonArray workUnits)
    {
        var type = BookTypeProfileService.Get(project);
        var paradigms = new JsonArray();
        foreach (var paradigm in exchangeState.Paradigms.Where(p =>
                     exchangeState.WorkUnits.Any(u => selectedIds.Contains(u.WorkUnitId) && u.ParadigmIds.Contains(p.ParadigmId))))
        {
            var material = project.Materials.FirstOrDefault(m => m.MaterialId == paradigm.MaterialId);
            paradigms.Add(new JsonObject
            {
                ["paradigm_id"] = paradigm.ParadigmId.ToString("D"),
                ["material_id"] = paradigm.MaterialId.ToString("D"),
                ["scope"] = paradigm.Scope,
                ["roles"] = new JsonArray(paradigm.Roles.Select(x => JsonValue.Create(x)).ToArray()),
                ["description"] = paradigm.Description,
                ["file"] = $"inputs/paradigms/{paradigm.ParadigmId:D}/{SafeName(material?.FileName)}",
                ["sha256"] = material?.Sha256 ?? string.Empty
            });
        }

        var presets = new JsonObject
        {
            ["book_type"] = type,
            ["active_profile_kind"] = ActiveProfileKind(type),
            ["technical_image_specs"] = TechnicalImageSpecsJson(project),
            ["consistent_rules"] = ImageCollectionWorkspaceService.GetConsistencyRules(project) ?? string.Empty,
            ["effective_visual_prompt"] = BuildEffectiveVisualPrompt(project)
        };
        if (string.Equals(type, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase))
            presets["coloring_profile"] = EntityNotesJson(project, "DiezColoringPromptProfile");
        else if (string.Equals(type, BookTypeProfileService.ImageCollection, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(type, BookTypeProfileService.IllustratedBook, StringComparison.OrdinalIgnoreCase))
            presets["illustration_profile"] = EntityNotesJson(project, "DiezImageCollectionPromptProfile");

        return new JsonObject
        {
            ["schema"] = "diez-visual-request-context",
            ["schema_version"] = 3,
            ["project_id"] = project.ProjectId.ToString("D"),
            ["book_type"] = type,
            ["active_profile_kind"] = ActiveProfileKind(type),
            ["priority"] = new JsonArray(
                "explicit_work_unit_instruction",
                "preserve_change_add_remove",
                "base_image_real_file",
                "intake_real_files_and_user_descriptions",
                "paradigms_and_roles",
                "consistent_shared_context",
                "current_book_type_profile",
                "technical_image_specs",
                "ai_creative_freedom"),
            ["critical_rule"] = "Per correzioni/modifiche l'immagine base reale è la sorgente visiva autoritativa. Le descrizioni guidano l'AI ma non sostituiscono mai il file immagine.",
            ["profile_isolation_rule"] = "Solo il profilo del Tipo libro attivo appartiene a questa richiesta; i profili visuali storici o di altri Tipi libro non vengono esportati.",
            ["image_presets"] = presets,
            ["intake"] = intake.DeepClone(),
            ["paradigms"] = paradigms,
            ["work_units"] = workUnits.DeepClone()
        };
    }

    private static string ActiveProfileKind(string type) =>
        string.Equals(type, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase) ? "COLORING_BOOK" :
        string.Equals(type, BookTypeProfileService.IllustratedBook, StringComparison.OrdinalIgnoreCase) ? "ILLUSTRATED_BOOK" :
        string.Equals(type, BookTypeProfileService.ImageCollection, StringComparison.OrdinalIgnoreCase) ? "IMAGE_COLLECTION" :
        "GENERIC_VISUAL";

    private static JsonNode TechnicalImageSpecsJson(PreviewProject project)
    {
        var node = EntityNotesJson(project, "DiezImageGenerationSpecs");
        if (node is not JsonObject obj) return node;
        foreach (var key in new[]
                 {
                     "Orientation", "orientation", "SafeMargin", "safe_margin",
                     "Bleed", "bleed", "BleedAmount", "bleed_amount"
                 })
            obj.Remove(key);
        return obj;
    }

    private static JsonNode EntityNotesJson(PreviewProject project, string kind)
    {
        var text = project.Entities.FirstOrDefault(e => string.Equals(e.Kind, kind, StringComparison.OrdinalIgnoreCase))?.Notes;
        if (string.IsNullOrWhiteSpace(text)) return new JsonObject();
        try { return JsonNode.Parse(text) ?? new JsonObject(); }
        catch { return new JsonObject { ["raw"] = text }; }
    }

    private static AiExchangeVersion? ResolveBaseVersion(AiExchangeState state, AiExchangeWorkUnit unit)
    {
        if (unit.ApprovedVersionId is Guid approvedId)
        {
            var approved = state.Versions.FirstOrDefault(v => v.VersionId == approvedId);
            if (approved is not null) return approved;
        }
        return state.Versions
            .Where(v => v.WorkUnitId == unit.WorkUnitId &&
                        !string.Equals(v.Status, AiExchangeVersionStatuses.Rejected, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefault();
    }

    private static void AppendInstructions(ZipArchive archive)
    {
        var existing = string.Empty;
        var entry = archive.GetEntry(InstructionsName);
        if (entry is not null)
        {
            using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8, true, leaveOpen: false);
            existing = reader.ReadToEnd();
            entry.Delete();
        }

        var addition = """

## Contesto visuale Diez V3 — OBBLIGATORIO
1. Leggi `request-context.json` prima di generare o correggere qualsiasi immagine.
2. Usa esclusivamente il profilo visuale del Tipo libro attivo dichiarato in `active_profile_kind`; non inferire o recuperare profili storici.
3. Le foto sotto `inputs/intake/` sono file reali dell'utente. Usa il file reale insieme a ruolo e descrizione; non ricostruire una foto dalla sola descrizione.
4. In una correzione/modifica, `base_version.file` è l'immagine base reale autoritativa. Modifica quella sorgente salvo istruzione `REGENERATE` esplicita.
5. Considera insieme: base reale + descrizione corrente + intake pertinenti + relative descrizioni + paradigmi/ruoli + preserve/change/add/remove + profilo attivo + Consistent + specifiche immagine correnti.
6. `preserve` significa lasciare visivamente invariati gli elementi indicati. Gli elementi non citati vanno preservati nelle modifiche locali quando richiesto.
7. Dopo ogni modifica restituisci una descrizione aggiornata che corrisponda all'immagine finale effettiva.
""";
        ReplaceTextEntry(archive, InstructionsName, existing.TrimEnd() + addition);
    }

    private static string NormalizeRole(string? role) =>
        IntakeRoles.FirstOrDefault(x => string.Equals(x, role, StringComparison.OrdinalIgnoreCase)) ?? "REFERENCE";

    private static string SafeName(string? name)
    {
        var value = string.IsNullOrWhiteSpace(name) ? "image.bin" : Path.GetFileName(name);
        return string.Concat(value.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
    }

    private static void ReplaceTextEntry(ZipArchive archive, string path, string text)
    {
        archive.GetEntry(path)?.Delete();
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(text);
    }

    private static void ReplaceBinaryEntry(ZipArchive archive, string path, byte[] bytes)
    {
        archive.GetEntry(path)?.Delete();
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(bytes, 0, bytes.Length);
    }
}
