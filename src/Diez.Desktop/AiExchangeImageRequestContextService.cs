using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiezPublishingStudio;

/// <summary>
/// Adds the complete visual request context to a Prompt Pack after the core builder
/// has created it. Real images remain authoritative; text descriptions are guidance.
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
        catch
        {
            return new IntakeState();
        }
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

    public static string BuildEffectiveVisualPrompt(PreviewProject project)
    {
        var type = BookTypeProfileService.Get(project);
        var sb = new StringBuilder();

        if (string.Equals(type, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine(BookTypePromptProfileService.BuildColoringBlock(BookTypePromptProfileService.LoadColoring(project)));
        }
        else if (string.Equals(type, BookTypeProfileService.ImageCollection, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(type, BookTypeProfileService.IllustratedBook, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(type, BookTypeProfileService.IllustratedBook, StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine("CONTESTO LIBRO ILLUSTRATO:");
                sb.AppendLine("- Le immagini sono illustrazioni interne al libro e devono sostenere il contenuto editoriale/narrativo, non comportarsi come una raccolta scollegata.");
                sb.AppendLine();
            }
            sb.AppendLine(ImageCollectionPromptProfileService.BuildPromptBlock(project));
        }
        else
        {
            sb.AppendLine("PROFILO EDITORIALE DEL TIPO LIBRO:");
            sb.AppendLine($"- Tipo libro: {type}.");
            sb.AppendLine("- Le immagini devono essere coerenti con funzione, struttura e tono del libro.");
        }

        if (BookTypeProfileService.IsImageCollection(project))
        {
            sb.AppendLine();
            sb.AppendLine(SingleWindowImageSpecsUi.BuildPromptBlock(project));
        }

        var consistency = ImageCollectionWorkspaceService.GetConsistencyRules(project)?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(consistency))
        {
            sb.AppendLine();
            sb.AppendLine("CONSISTENT / CONTESTO CONDIVISO EFFETTIVO:");
            sb.AppendLine(consistency);
        }
        return sb.ToString().Trim();
    }

    /// <summary>
    /// Reopens a Prompt Pack built by AiExchangePromptPackBuilder and adds:
    /// - exact real intake images + intake-index.json;
    /// - exact base image path + current description;
    /// - paradigms metadata;
    /// - instruction/preserve/change/add/remove;
    /// - every effective image preset in structured and human-readable form.
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
        using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false))
        {
            var text = await reader.ReadToEndAsync();
            manifest = JsonNode.Parse(text)?.AsObject()
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

            var relevantIntakeIds = intake
                .Where(x => x.WorkUnitIds.Count == 0 || x.WorkUnitIds.Contains(workUnitId))
                .Select(x => JsonValue.Create(x.IntakeId.ToString("D"))).ToArray();
            node["intake_ids"] = new JsonArray(relevantIntakeIds);
            node["request_context_file"] = ContextName;
            node["instruction"] = unit.Instruction;
            node["preserve"] = new JsonArray(unit.Preserve.Select(x => JsonValue.Create(x)).ToArray());
            node["change"] = new JsonArray(unit.Change.Select(x => JsonValue.Create(x)).ToArray());
            node["add"] = new JsonArray(unit.Add.Select(x => JsonValue.Create(x)).ToArray());
            node["remove"] = new JsonArray(unit.Remove.Select(x => JsonValue.Create(x)).ToArray());

            var baseVersion = ResolveBaseVersion(exchangeState, unit);
            if (baseVersion?.MaterialId is Guid materialId)
            {
                var material = project.Materials.FirstOrDefault(m => m.MaterialId == materialId);
                if (material is not null)
                {
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
            }
        }

        manifest["intake"] = intakeArray.DeepClone();
        manifest["request_context_file"] = ContextName;
        manifest["visual_context_protocol_version"] = 2;

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
            $"Contesto immagini V2 aggiunto: {intakeArray.Count} foto intake reali, {baseCount} immagini base, preset completi e descrizioni.",
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
            ["coloring_profile"] = EntityNotesJson(project, "DiezColoringPromptProfile"),
            ["illustration_profile"] = EntityNotesJson(project, "DiezImageCollectionPromptProfile"),
            ["technical_image_specs"] = EntityNotesJson(project, "DiezImageGenerationSpecs"),
            ["consistent_rules"] = ImageCollectionWorkspaceService.GetConsistencyRules(project) ?? string.Empty,
            ["effective_visual_prompt"] = BuildEffectiveVisualPrompt(project)
        };

        return new JsonObject
        {
            ["schema"] = "diez-visual-request-context",
            ["schema_version"] = 2,
            ["project_id"] = project.ProjectId.ToString("D"),
            ["book_type"] = type,
            ["priority"] = new JsonArray(
                "explicit_work_unit_instruction",
                "preserve_change_add_remove",
                "base_image_real_file",
                "intake_real_files_and_user_descriptions",
                "paradigms_and_roles",
                "consistent_shared_context",
                "image_presets",
                "ai_creative_freedom"),
            ["critical_rule"] = "Per correzioni/modifiche l'immagine base reale è la sorgente visiva autoritativa. Le descrizioni utente e correnti guidano l'AI ma non sostituiscono il file immagine.",
            ["image_presets"] = presets,
            ["intake"] = intake.DeepClone(),
            ["paradigms"] = paradigms,
            ["work_units"] = workUnits.DeepClone()
        };
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
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
            existing = reader.ReadToEnd();
            entry.Delete();
        }

        var addition = """

## Contesto visuale Diez V2 — OBBLIGATORIO
1. Leggi `request-context.json` prima di generare o correggere qualsiasi immagine.
2. Le foto sotto `inputs/intake/` sono file reali dell'utente. Usa il file reale insieme a ruolo e descrizione presenti in `inputs/intake/intake-index.json`; non ricostruire la foto dalla sola descrizione.
3. In una correzione/modifica, `base_version.file` è l'immagine base reale autoritativa. Devi modificarla, non rigenerarla liberamente, salvo istruzione `REGENERATE` esplicita.
4. Considera insieme: immagine base reale + descrizione corrente + foto intake pertinenti + relative descrizioni + paradigmi e ruoli + istruzione + preserve/change/add/remove + tutti i preset immagini.
5. I preset immagini in `request-context.json` sono vincoli effettivi della richiesta: Tipo libro, profilo visuale, soggetto/ambiente, colore o B/N/grigi, dettaglio, spessore/contorno, qualità HD/FHD/2K/4K/8K/personalizzata, pixel, aspect ratio, DPI, formato, margini, bleed e Consistent.
6. `preserve` significa lasciare visivamente invariati gli elementi indicati. Gli elementi non citati vanno preservati quando la richiesta è una modifica locale.
7. Dopo ogni modifica, restituisci una descrizione aggiornata che corrisponda all'immagine finale effettiva.
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
