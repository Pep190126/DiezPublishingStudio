using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DiezPublishingStudio;

internal static class AiExchangeModes
{
    public const string InputOnly = "INPUT_ONLY";
    public const string AiOnly = "AI_ONLY";
    public const string InputPlusAi = "INPUT_PLUS_AI";
    public const string InputTransformedByAi = "INPUT_TRANSFORMED_BY_AI";
    public const string AiWithInputAsReference = "AI_WITH_INPUT_AS_REFERENCE";

    public static readonly IReadOnlyList<string> All =
    [
        InputOnly,
        AiOnly,
        InputPlusAi,
        InputTransformedByAi,
        AiWithInputAsReference
    ];

    public static string UserLabel(string? mode) => mode switch
    {
        InputOnly => "Usa solo i miei contenuti",
        AiOnly => "Crea con l'AI",
        InputPlusAi => "Combina i miei contenuti con l'AI",
        InputTransformedByAi => "Elabora i miei contenuti con l'AI",
        AiWithInputAsReference => "Crea con l'AI usando i miei contenuti come riferimento",
        _ => "Crea con l'AI"
    };

    public static string Normalize(string? mode) =>
        All.FirstOrDefault(x => string.Equals(x, mode, StringComparison.OrdinalIgnoreCase)) ?? AiOnly;
}

internal static class AiExchangeContentTypes
{
    public const string Image = "IMAGE";
    public const string Text = "TEXT";
    public const string StructuredData = "STRUCTURED_DATA";
    public const string Document = "DOCUMENT";
    public const string Other = "OTHER";
}

internal static class AiExchangeOrigins
{
    public const string AiApi = "AI_API";
    public const string AiPromptPack = "AI_PROMPT_PACK";
    public const string UserEdit = "USER_EDIT";
    public const string UserExternalEdit = "USER_EXTERNAL_EDIT";
    public const string Import = "IMPORT";
    public const string DiezProcess = "DIEZ_PROCESS";
}

internal static class AiExchangeVersionStatuses
{
    public const string Candidate = "CANDIDATE";
    public const string Approved = "APPROVED";
    public const string Incomplete = "INCOMPLETE";
    public const string Rejected = "REJECTED";
    public const string Stale = "STALE";
}

internal static class AiExchangeDescriptionStatuses
{
    public const string Valid = "VALID";
    public const string NeedsVerification = "NEEDS_VERIFICATION";
    public const string Missing = "MISSING";
}

internal sealed class AiExchangeState
{
    public int SchemaVersion { get; set; } = 1;
    public List<AiExchangeJob> Jobs { get; set; } = [];
    public List<AiExchangeWorkUnit> WorkUnits { get; set; } = [];
    public List<AiExchangeVersion> Versions { get; set; } = [];
    public List<AiExchangeSharedContext> SharedContexts { get; set; } = [];
    public List<AiExchangeParadigm> Paradigms { get; set; } = [];
    public List<AiExchangeRequestSnapshot> RequestSnapshots { get; set; } = [];
    public List<AiExchangePromptPackRecord> PromptPacks { get; set; } = [];
    public List<string> ImportedPackageIds { get; set; } = [];
}

internal sealed class AiExchangeJob
{
    public Guid JobId { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string BookType { get; set; } = string.Empty;
    public string Status { get; set; } = "ACTIVE";
    public List<Guid> WorkUnitIds { get; set; } = [];
    public string CreatedAtLocal { get; set; } = string.Empty;
}

internal sealed class AiExchangeWorkUnit
{
    public Guid WorkUnitId { get; set; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public Guid? LegacyAiJobId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string ContentType { get; set; } = AiExchangeContentTypes.Text;
    public string Mode { get; set; } = AiExchangeModes.AiOnly;
    public string Instruction { get; set; } = string.Empty;
    public int Position { get; set; }
    public Guid? ApprovedVersionId { get; set; }
    public List<Guid> CandidateVersionIds { get; set; } = [];
    public List<Guid> ParadigmIds { get; set; } = [];
    public List<Guid> SharedContextIds { get; set; } = [];
    public List<string> Preserve { get; set; } = [];
    public List<string> Change { get; set; } = [];
    public List<string> Add { get; set; } = [];
    public List<string> Remove { get; set; } = [];
}

internal sealed class AiExchangeVersion
{
    public Guid VersionId { get; set; } = Guid.NewGuid();
    public Guid WorkUnitId { get; set; }
    public int VersionNumber { get; set; }
    public string Status { get; set; } = AiExchangeVersionStatuses.Candidate;
    public string Origin { get; set; } = AiExchangeOrigins.Import;
    public Guid? MaterialId { get; set; }
    public string TextContent { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DescriptionStatus { get; set; } = AiExchangeDescriptionStatuses.Valid;
    public string ContentSha256 { get; set; } = string.Empty;
    public Guid? DerivedFromVersionId { get; set; }
    public Guid? SourceSnapshotId { get; set; }
    public int SharedContextVersion { get; set; }
    public string CreatedAtLocal { get; set; } = string.Empty;
}

internal sealed class AiExchangeSharedContext
{
    public Guid SharedContextId { get; set; } = Guid.NewGuid();
    public int Version { get; set; } = 1;
    public string Scope { get; set; } = "COLLECTION";
    public bool ConsistentEnabled { get; set; }
    public string Name { get; set; } = "Contesto condiviso";
    public List<AiExchangeContextRule> Rules { get; set; } = [];
}

internal sealed class AiExchangeContextRule
{
    public string Key { get; set; } = string.Empty;
    public string Level { get; set; } = "PREFERRED";
    public string Value { get; set; } = string.Empty;
}

internal sealed class AiExchangeParadigm
{
    public Guid ParadigmId { get; set; } = Guid.NewGuid();
    public Guid MaterialId { get; set; }
    public string Scope { get; set; } = "ITEM";
    public List<string> Roles { get; set; } = [];
    public string Description { get; set; } = string.Empty;
}

internal sealed class AiExchangeRequestSnapshot
{
    public Guid SnapshotId { get; set; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public Guid PromptPackId { get; set; }
    public string Transport { get; set; } = "PROMPT_PACK";
    public List<AiExchangeSnapshotItem> Items { get; set; } = [];
    public string CreatedAtLocal { get; set; } = string.Empty;
}

internal sealed class AiExchangeSnapshotItem
{
    public Guid WorkUnitId { get; set; }
    public int TargetCandidateVersion { get; set; }
    public Guid? BaseVersionId { get; set; }
    public List<AiExchangeContextRef> SharedContexts { get; set; } = [];
    public List<Guid> ParadigmIds { get; set; } = [];
}

internal sealed class AiExchangeContextRef
{
    public Guid SharedContextId { get; set; }
    public int Version { get; set; }
}

internal sealed class AiExchangePromptPackRecord
{
    public Guid PromptPackId { get; set; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public Guid SnapshotId { get; set; }
    public string CreatedAtLocal { get; set; } = string.Empty;
}

internal sealed class AiExchangeNormalizedResultItem
{
    public Guid WorkUnitId { get; set; }
    public int CandidateVersion { get; set; }
    public string ContentType { get; set; } = AiExchangeContentTypes.Text;
    public string ResultStatus { get; set; } = "COMPLETE";
    public string? PrimaryAssetPath { get; set; }
    public string TextContent { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Origin { get; set; } = AiExchangeOrigins.Import;
    public Guid? SourceSnapshotId { get; set; }
}

internal readonly record struct AiExchangeIngestResult(
    string Status,
    Guid WorkUnitId,
    int CandidateVersion,
    Guid? VersionId,
    string Message);

internal readonly record struct AiExchangeImportSummary(
    bool Success,
    int Imported,
    int Incomplete,
    int Duplicates,
    int Conflicts,
    int Failed,
    string Message);

internal static class AiExchangeStateStore
{
    private const string EntityKind = "DiezAiExchangeState";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static AiExchangeState Load(PreviewProject project)
    {
        var entity = project.Entities.FirstOrDefault(e =>
            string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase));
        AiExchangeState state;
        if (entity is null || string.IsNullOrWhiteSpace(entity.Notes))
        {
            state = new AiExchangeState();
        }
        else
        {
            try
            {
                state = JsonSerializer.Deserialize<AiExchangeState>(entity.Notes, JsonOptions) ?? new AiExchangeState();
            }
            catch
            {
                state = new AiExchangeState();
            }
        }
        Normalize(state);
        EnsureLegacyJobs(project, state);
        return state;
    }

    public static void Save(PreviewProject project, AiExchangeState state)
    {
        Normalize(state);
        var entity = project.Entities.FirstOrDefault(e =>
            string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
        {
            entity = new GraphEntity
            {
                Kind = EntityKind,
                Name = "AI Exchange",
                IsCandidate = false,
                Notes = string.Empty
            };
            project.Entities.Add(entity);
        }
        entity.IsCandidate = false;
        entity.Notes = JsonSerializer.Serialize(state, JsonOptions);
    }

    public static AiExchangeSharedContext EnsureVisualConsistencyContext(
        PreviewProject project,
        AiExchangeState state,
        bool enabled,
        string? rules)
    {
        var context = state.SharedContexts.FirstOrDefault(c =>
            string.Equals(c.Name, "Consistent immagini", StringComparison.OrdinalIgnoreCase));
        if (context is null)
        {
            context = new AiExchangeSharedContext
            {
                Name = "Consistent immagini",
                Scope = "COLLECTION",
                ConsistentEnabled = enabled
            };
            state.SharedContexts.Add(context);
        }

        var normalizedRules = (rules ?? string.Empty).Trim();
        var oldRules = string.Join("\n", context.Rules.Select(r => r.Value));
        if (context.ConsistentEnabled != enabled || !string.Equals(oldRules, normalizedRules, StringComparison.Ordinal))
        {
            if (context.Rules.Count > 0 || context.ConsistentEnabled != enabled) context.Version++;
            context.ConsistentEnabled = enabled;
            context.Rules = string.IsNullOrWhiteSpace(normalizedRules)
                ? []
                : [new AiExchangeContextRule { Key = "visual_consistency", Level = "LOCKED", Value = normalizedRules }];
        }

        foreach (var unit in state.WorkUnits.Where(u =>
                     string.Equals(u.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase)))
        {
            if (enabled && !unit.SharedContextIds.Contains(context.SharedContextId))
                unit.SharedContextIds.Add(context.SharedContextId);
            if (!enabled) unit.SharedContextIds.Remove(context.SharedContextId);
        }

        ImageCollectionWorkspaceService.SetConsistencyRules(project, normalizedRules);
        return context;
    }

    private static void EnsureLegacyJobs(PreviewProject project, AiExchangeState state)
    {
        if (project.AiProductionJobs.Count == 0) return;
        var bookType = BookTypeProfileService.Get(project);
        var group = state.Jobs.FirstOrDefault(j => string.Equals(j.Title, "Contenuti AI del progetto", StringComparison.OrdinalIgnoreCase));
        if (group is null)
        {
            group = new AiExchangeJob
            {
                Title = "Contenuti AI del progetto",
                BookType = bookType,
                CreatedAtLocal = DateTimeOffset.Now.ToString("O")
            };
            state.Jobs.Add(group);
        }
        group.BookType = bookType;

        foreach (var legacy in project.AiProductionJobs)
        {
            var unit = state.WorkUnits.FirstOrDefault(w => w.LegacyAiJobId == legacy.JobId);
            if (unit is null)
            {
                unit = new AiExchangeWorkUnit
                {
                    WorkUnitId = Guid.NewGuid(),
                    JobId = group.JobId,
                    LegacyAiJobId = legacy.JobId,
                    Code = legacy.Code,
                    Kind = legacy.OutputType,
                    ContentType = MapContentType(legacy.OutputType),
                    Mode = AiExchangeModes.AiOnly,
                    Instruction = string.IsNullOrWhiteSpace(legacy.Prompt) ? legacy.Request : legacy.Prompt,
                    Position = ExtractPosition(legacy.Code)
                };
                state.WorkUnits.Add(unit);
                group.WorkUnitIds.Add(unit.WorkUnitId);
            }
            else
            {
                unit.Code = legacy.Code;
                unit.Kind = legacy.OutputType;
                unit.ContentType = MapContentType(legacy.OutputType);
                if (string.IsNullOrWhiteSpace(unit.Instruction))
                    unit.Instruction = string.IsNullOrWhiteSpace(legacy.Prompt) ? legacy.Request : legacy.Prompt;
                if (!group.WorkUnitIds.Contains(unit.WorkUnitId)) group.WorkUnitIds.Add(unit.WorkUnitId);
            }

            if (legacy.ResultMaterialId.HasValue &&
                !state.Versions.Any(v => v.WorkUnitId == unit.WorkUnitId && v.MaterialId == legacy.ResultMaterialId))
            {
                var description = string.Equals(legacy.OutputType, AiProductionService.TypeImage, StringComparison.OrdinalIgnoreCase)
                    ? ImageCollectionDescriptionService.GetDescription(legacy)
                    : string.Empty;
                var number = NextVersionNumber(state, unit.WorkUnitId);
                var version = new AiExchangeVersion
                {
                    WorkUnitId = unit.WorkUnitId,
                    VersionNumber = number,
                    MaterialId = legacy.ResultMaterialId,
                    Description = description,
                    DescriptionStatus = string.Equals(unit.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase)
                        ? (string.IsNullOrWhiteSpace(description) ? AiExchangeDescriptionStatuses.Missing : AiExchangeDescriptionStatuses.Valid)
                        : AiExchangeDescriptionStatuses.Valid,
                    Status = string.Equals(legacy.Status, AiProductionService.StatusApproved, StringComparison.Ordinal)
                        ? AiExchangeVersionStatuses.Approved
                        : AiExchangeVersionStatuses.Candidate,
                    Origin = AiExchangeOrigins.Import,
                    CreatedAtLocal = DateTimeOffset.Now.ToString("O")
                };
                state.Versions.Add(version);
                if (version.Status == AiExchangeVersionStatuses.Approved) unit.ApprovedVersionId = version.VersionId;
                else if (!unit.CandidateVersionIds.Contains(version.VersionId)) unit.CandidateVersionIds.Add(version.VersionId);
            }
        }
    }

    private static void Normalize(AiExchangeState state)
    {
        if (state.SchemaVersion <= 0) state.SchemaVersion = 1;
        state.Jobs ??= [];
        state.WorkUnits ??= [];
        state.Versions ??= [];
        state.SharedContexts ??= [];
        state.Paradigms ??= [];
        state.RequestSnapshots ??= [];
        state.PromptPacks ??= [];
        state.ImportedPackageIds ??= [];
        foreach (var job in state.Jobs)
        {
            if (job.JobId == Guid.Empty) job.JobId = Guid.NewGuid();
            job.WorkUnitIds ??= [];
            job.Title ??= string.Empty;
            job.BookType ??= string.Empty;
            job.Status ??= "ACTIVE";
            job.CreatedAtLocal ??= string.Empty;
        }
        foreach (var unit in state.WorkUnits)
        {
            if (unit.WorkUnitId == Guid.Empty) unit.WorkUnitId = Guid.NewGuid();
            unit.Code ??= string.Empty;
            unit.Kind ??= string.Empty;
            unit.ContentType ??= AiExchangeContentTypes.Text;
            unit.Mode = AiExchangeModes.Normalize(unit.Mode);
            unit.Instruction ??= string.Empty;
            unit.CandidateVersionIds ??= [];
            unit.ParadigmIds ??= [];
            unit.SharedContextIds ??= [];
            unit.Preserve ??= [];
            unit.Change ??= [];
            unit.Add ??= [];
            unit.Remove ??= [];
        }
        foreach (var version in state.Versions)
        {
            if (version.VersionId == Guid.Empty) version.VersionId = Guid.NewGuid();
            version.Status ??= AiExchangeVersionStatuses.Candidate;
            version.Origin ??= AiExchangeOrigins.Import;
            version.TextContent ??= string.Empty;
            version.Description ??= string.Empty;
            version.DescriptionStatus ??= AiExchangeDescriptionStatuses.Valid;
            version.ContentSha256 ??= string.Empty;
            version.CreatedAtLocal ??= string.Empty;
        }
    }

    internal static int NextVersionNumber(AiExchangeState state, Guid workUnitId) =>
        state.Versions.Where(v => v.WorkUnitId == workUnitId).Select(v => v.VersionNumber).DefaultIfEmpty(0).Max() + 1;

    private static string MapContentType(string? legacyType) => legacyType switch
    {
        AiProductionService.TypeImage => AiExchangeContentTypes.Image,
        AiProductionService.TypeData => AiExchangeContentTypes.StructuredData,
        _ => AiExchangeContentTypes.Text
    };

    private static int ExtractPosition(string? code)
    {
        var digits = new string((code ?? string.Empty).Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var value) ? value : int.MaxValue;
    }
}

internal static class AiExchangePromptPackBuilder
{
    private const string ManifestName = "prompt-manifest.json";
    private const string InstructionsName = "instructions.md";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static async Task<(bool Success, string Message, Guid PromptPackId)> BuildAsync(
        PreviewProject project,
        string projectPath,
        AiExchangeState state,
        IEnumerable<Guid> workUnitIds,
        string outputPath)
    {
        var selectedIds = workUnitIds.Distinct().ToHashSet();
        var units = state.WorkUnits.Where(w => selectedIds.Contains(w.WorkUnitId)).OrderBy(w => w.Position).ToList();
        if (units.Count == 0) return (false, "Non ci sono contenuti selezionati per il Prompt Pack.", Guid.Empty);
        var jobIds = units.Select(u => u.JobId).Distinct().ToList();
        if (jobIds.Count != 1) return (false, "Per ora un Prompt Pack deve appartenere a un solo Job Diez.", Guid.Empty);

        var promptPackId = Guid.NewGuid();
        var snapshot = new AiExchangeRequestSnapshot
        {
            SnapshotId = Guid.NewGuid(),
            JobId = jobIds[0],
            PromptPackId = promptPackId,
            Transport = "PROMPT_PACK",
            CreatedAtLocal = DateTimeOffset.Now.ToString("O")
        };

        foreach (var unit in units)
        {
            var baseVersion = ResolveBaseVersion(state, unit);
            snapshot.Items.Add(new AiExchangeSnapshotItem
            {
                WorkUnitId = unit.WorkUnitId,
                TargetCandidateVersion = AiExchangeStateStore.NextVersionNumber(state, unit.WorkUnitId),
                BaseVersionId = baseVersion?.VersionId,
                ParadigmIds = unit.ParadigmIds.ToList(),
                SharedContexts = unit.SharedContextIds
                    .Select(id => state.SharedContexts.FirstOrDefault(c => c.SharedContextId == id))
                    .Where(c => c is not null)
                    .Select(c => new AiExchangeContextRef { SharedContextId = c!.SharedContextId, Version = c.Version })
                    .ToList()
            });
        }

        var manifest = BuildManifest(project, state, units, snapshot);
        var fullPath = EnsureZip(outputPath);
        var directory = Path.GetDirectoryName(Path.GetFullPath(fullPath));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temp = fullPath + ".tmp";
        if (File.Exists(temp)) File.Delete(temp);

        await using (var stream = File.Open(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            await WriteTextAsync(zip, ManifestName, JsonSerializer.Serialize(manifest, JsonOptions));
            await WriteTextAsync(zip, InstructionsName, MasterInstructions());

            foreach (var paradigm in state.Paradigms.Where(p => units.Any(u => u.ParadigmIds.Contains(p.ParadigmId))))
            {
                var material = project.Materials.FirstOrDefault(m => m.MaterialId == paradigm.MaterialId);
                if (material is null) continue;
                var bytes = await ProjectFileStore.ReadEmbeddedMaterialAsync(projectPath, material);
                if (bytes is null) continue;
                await WriteBytesAsync(zip, $"inputs/paradigms/{paradigm.ParadigmId:D}/{SafeName(material.FileName)}", bytes);
            }

            foreach (var item in snapshot.Items.Where(i => i.BaseVersionId.HasValue))
            {
                var version = state.Versions.FirstOrDefault(v => v.VersionId == item.BaseVersionId);
                if (version?.MaterialId is not Guid materialId) continue;
                var material = project.Materials.FirstOrDefault(m => m.MaterialId == materialId);
                if (material is null) continue;
                var bytes = await ProjectFileStore.ReadEmbeddedMaterialAsync(projectPath, material);
                if (bytes is null) continue;
                await WriteBytesAsync(zip, $"inputs/current/{item.WorkUnitId:D}/{SafeName(material.FileName)}", bytes);
            }
        }
        File.Move(temp, fullPath, true);

        state.RequestSnapshots.Add(snapshot);
        state.PromptPacks.Add(new AiExchangePromptPackRecord
        {
            PromptPackId = promptPackId,
            JobId = snapshot.JobId,
            SnapshotId = snapshot.SnapshotId,
            CreatedAtLocal = DateTimeOffset.Now.ToString("O")
        });
        AiExchangeStateStore.Save(project, state);
        await ProjectFileStore.SaveAsync(projectPath, project);
        return (true, $"Prompt Pack creato con {units.Count} contenuti: {Path.GetFileName(fullPath)}", promptPackId);
    }

    private static object BuildManifest(
        PreviewProject project,
        AiExchangeState state,
        IReadOnlyList<AiExchangeWorkUnit> units,
        AiExchangeRequestSnapshot snapshot)
    {
        return new
        {
            protocol = "diez-prompt-pack",
            protocol_version = 1,
            project_id = project.ProjectId,
            book_type = BookTypeProfileService.Get(project),
            job_id = snapshot.JobId,
            prompt_pack_id = snapshot.PromptPackId,
            request_snapshot_id = snapshot.SnapshotId,
            partial_results_allowed = true,
            shared_contexts = state.SharedContexts
                .Where(c => units.Any(u => u.SharedContextIds.Contains(c.SharedContextId)))
                .Select(c => new
                {
                    id = c.SharedContextId,
                    c.Version,
                    c.Scope,
                    consistent = c.ConsistentEnabled,
                    rules = c.Rules.Select(r => new { r.Key, r.Level, r.Value })
                }),
            paradigms = state.Paradigms
                .Where(p => units.Any(u => u.ParadigmIds.Contains(p.ParadigmId)))
                .Select(p => new
                {
                    id = p.ParadigmId,
                    p.Scope,
                    roles = p.Roles,
                    p.Description,
                    file = $"inputs/paradigms/{p.ParadigmId:D}/"
                }),
            work_units = units.Select(unit =>
            {
                var snap = snapshot.Items.Single(i => i.WorkUnitId == unit.WorkUnitId);
                var baseVersion = snap.BaseVersionId.HasValue
                    ? state.Versions.FirstOrDefault(v => v.VersionId == snap.BaseVersionId)
                    : null;
                return new
                {
                    id = unit.WorkUnitId,
                    code = unit.Code,
                    kind = unit.Kind,
                    content_type = unit.ContentType,
                    mode = unit.Mode,
                    instruction = unit.Instruction,
                    position = unit.Position,
                    target_candidate_version = snap.TargetCandidateVersion,
                    base_version = baseVersion is null ? null : new
                    {
                        version_id = baseVersion.VersionId,
                        version_number = baseVersion.VersionNumber,
                        file = baseVersion.MaterialId.HasValue ? $"inputs/current/{unit.WorkUnitId:D}/" : null
                    },
                    paradigm_ids = unit.ParadigmIds,
                    shared_context_ids = unit.SharedContextIds,
                    preserve = unit.Preserve,
                    change = unit.Change,
                    add = unit.Add,
                    remove = unit.Remove,
                    expected_output = new
                    {
                        primary_asset = true,
                        description = string.Equals(unit.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase)
                    }
                };
            })
        };
    }

    private static AiExchangeVersion? ResolveBaseVersion(AiExchangeState state, AiExchangeWorkUnit unit)
    {
        if (unit.ApprovedVersionId.HasValue)
            return state.Versions.FirstOrDefault(v => v.VersionId == unit.ApprovedVersionId.Value);
        return state.Versions
            .Where(v => v.WorkUnitId == unit.WorkUnitId &&
                        !string.Equals(v.Status, AiExchangeVersionStatuses.Rejected, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefault();
    }

    private static string MasterInstructions() => """
# Diez Publishing Studio — Prompt Pack v1

Questo package descrive un lavoro Diez. Leggi `prompt-manifest.json` e usa soltanto i materiali necessari presenti in `inputs/`.

## Modalità
- `INPUT_ONLY`: usa solo i contenuti forniti.
- `AI_ONLY`: crea con l'AI.
- `INPUT_PLUS_AI`: combina input e nuovo contenuto AI.
- `INPUT_TRANSFORMED_BY_AI`: trasforma l'input fornito.
- `AI_WITH_INPUT_AS_REFERENCE`: crea nuovo contenuto usando l'input come riferimento/paradigma.

## Regole essenziali
1. Mantieni esattamente `work_unit_id` e `candidate_version` assegnati da Diez.
2. Shared Context / Consistent: `LOCKED` va mantenuto, `PREFERRED` va rispettato per quanto possibile, `FREE` può variare.
3. Usa ogni paradigma solo per i ruoli dichiarati.
4. Nelle modifiche locali preserva tutto ciò che non viene chiesto di cambiare.
5. Ogni immagine deve avere una descrizione coerente con l'immagine finale. Se manca, restituisci l'item come `INCOMPLETE`.
6. Puoi restituire il lavoro in uno o più ZIP, anche parziali. Non rinumerare e non cambiare gli ID.
7. Ogni ZIP deve contenere `response-manifest.json` e i file sotto `content/`.
8. Stati ammessi per gli item: `COMPLETE`, `INCOMPLETE`, `FAILED`.
9. Non includere codice eseguibile, script o macro. Restituisci solo dati e contenuti.
10. Non duplicare intake/paradigmi originari salvo che siano esplicitamente richiesti come output.

## Response manifest minimo
```json
{
  "protocol": "diez-response",
  "protocol_version": 1,
  "project_id": "<project id>",
  "job_id": "<job id>",
  "prompt_pack_id": "<prompt pack id>",
  "package_id": "<id univoco del package>",
  "partial": true,
  "items": [
    {
      "work_unit_id": "<id>",
      "candidate_version": 1,
      "content_type": "IMAGE|TEXT|STRUCTURED_DATA|DOCUMENT",
      "status": "COMPLETE|INCOMPLETE|FAILED",
      "primary_asset": "content/file.ext",
      "description": "descrizione obbligatoria per immagini"
    }
  ]
}
```

Diez ricomporrà automaticamente tutti i package tramite gli ID. L'utente non deve rinominare o associare manualmente i file.
""";

    private static string EnsureZip(string path) =>
        path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? path : path + ".zip";

    private static string SafeName(string? name)
    {
        var value = string.IsNullOrWhiteSpace(name) ? "asset.bin" : Path.GetFileName(name);
        return string.Concat(value.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
    }

    private static async Task WriteTextAsync(ZipArchive archive, string path, string text)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(text);
    }

    private static async Task WriteBytesAsync(ZipArchive archive, string path, byte[] bytes)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await stream.WriteAsync(bytes);
    }
}

internal static class AiExchangeResponseImporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static async Task<AiExchangeImportSummary> ImportAsync(
        PreviewProject project,
        string projectPath,
        AiExchangeState state,
        IEnumerable<string> zipPaths)
    {
        var imported = 0;
        var incomplete = 0;
        var duplicates = 0;
        var conflicts = 0;
        var failed = 0;
        var changed = false;

        foreach (var zipPath in zipPaths.Where(File.Exists))
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "DiezAiExchange-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                using var archive = ZipFile.OpenRead(zipPath);
                var manifestEntry = archive.GetEntry("response-manifest.json");
                if (manifestEntry is null) { failed++; continue; }
                AiResponseManifest? manifest;
                await using (var manifestStream = manifestEntry.Open())
                    manifest = await JsonSerializer.DeserializeAsync<AiResponseManifest>(manifestStream, JsonOptions);
                if (manifest is null || !string.Equals(manifest.Protocol, "diez-response", StringComparison.OrdinalIgnoreCase) || manifest.ProtocolVersion != 1)
                {
                    failed++;
                    continue;
                }
                if (manifest.ProjectId != project.ProjectId)
                {
                    failed++;
                    continue;
                }
                if (string.IsNullOrWhiteSpace(manifest.PackageId))
                {
                    failed++;
                    continue;
                }
                if (state.ImportedPackageIds.Contains(manifest.PackageId, StringComparer.OrdinalIgnoreCase))
                {
                    duplicates++;
                    continue;
                }

                var pack = state.PromptPacks.FirstOrDefault(p => p.PromptPackId == manifest.PromptPackId);
                var snapshot = pack is null ? null : state.RequestSnapshots.FirstOrDefault(s => s.SnapshotId == pack.SnapshotId);
                if (pack is null || snapshot is null || snapshot.JobId != manifest.JobId)
                {
                    failed++;
                    continue;
                }

                foreach (var item in manifest.Items ?? [])
                {
                    if (string.Equals(item.Status, "FAILED", StringComparison.OrdinalIgnoreCase))
                    {
                        failed++;
                        continue;
                    }
                    var request = snapshot.Items.FirstOrDefault(x => x.WorkUnitId == item.WorkUnitId);
                    if (request is null || request.TargetCandidateVersion != item.CandidateVersion)
                    {
                        conflicts++;
                        continue;
                    }

                    string? localAssetPath = null;
                    if (!string.IsNullOrWhiteSpace(item.PrimaryAsset))
                    {
                        var safeEntry = FindSafeEntry(archive, item.PrimaryAsset);
                        if (safeEntry is null)
                        {
                            incomplete++;
                            continue;
                        }
                        var ext = Path.GetExtension(safeEntry.Name);
                        localAssetPath = Path.Combine(tempRoot, Guid.NewGuid().ToString("N") + ext);
                        await using var source = safeEntry.Open();
                        await using var destination = File.Create(localAssetPath);
                        await source.CopyToAsync(destination);
                    }

                    var result = await AiExchangeResultIngestor.IngestAsync(project, state, new AiExchangeNormalizedResultItem
                    {
                        WorkUnitId = item.WorkUnitId,
                        CandidateVersion = item.CandidateVersion,
                        ContentType = item.ContentType ?? string.Empty,
                        ResultStatus = item.Status ?? "INCOMPLETE",
                        PrimaryAssetPath = localAssetPath,
                        Description = item.Description ?? string.Empty,
                        Origin = AiExchangeOrigins.AiPromptPack,
                        SourceSnapshotId = snapshot.SnapshotId
                    });
                    changed |= result.Status is "IMPORTED" or "UPDATED" or "INCOMPLETE";
                    switch (result.Status)
                    {
                        case "IMPORTED":
                        case "UPDATED": imported++; break;
                        case "INCOMPLETE": incomplete++; break;
                        case "DUPLICATE": duplicates++; break;
                        case "CONFLICT": conflicts++; break;
                        default: failed++; break;
                    }
                }

                state.ImportedPackageIds.Add(manifest.PackageId);
                changed = true;
                if (changed)
                {
                    AiExchangeStateStore.Save(project, state);
                    await ProjectFileStore.SaveAsync(projectPath, project);
                }
            }
            catch (InvalidDataException)
            {
                failed++;
            }
            finally
            {
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        }

        var success = imported > 0 || incomplete > 0 || duplicates > 0;
        return new AiExchangeImportSummary(
            success,
            imported,
            incomplete,
            duplicates,
            conflicts,
            failed,
            $"Import AI: {imported} pronti/aggiornati · {incomplete} incompleti · {duplicates} duplicati · {conflicts} conflitti · {failed} errori.");
    }

    private static ZipArchiveEntry? FindSafeEntry(ZipArchive archive, string requested)
    {
        var normalized = requested.Replace('\\', '/').TrimStart('/');
        if (normalized.Contains("../", StringComparison.Ordinal) || normalized.StartsWith("..", StringComparison.Ordinal)) return null;
        return archive.Entries.FirstOrDefault(e => string.Equals(e.FullName.Replace('\\', '/'), normalized, StringComparison.Ordinal));
    }

    private sealed class AiResponseManifest
    {
        public string Protocol { get; set; } = string.Empty;
        public int ProtocolVersion { get; set; }
        public Guid ProjectId { get; set; }
        public Guid JobId { get; set; }
        public Guid PromptPackId { get; set; }
        public string PackageId { get; set; } = string.Empty;
        public bool Partial { get; set; }
        public List<AiResponseItem> Items { get; set; } = [];
    }

    private sealed class AiResponseItem
    {
        public Guid WorkUnitId { get; set; }
        public int CandidateVersion { get; set; }
        public string ContentType { get; set; } = string.Empty;
        public string Status { get; set; } = "COMPLETE";
        public string PrimaryAsset { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}

internal static class AiExchangeResultIngestor
{
    public static async Task<AiExchangeIngestResult> IngestAsync(
        PreviewProject project,
        AiExchangeState state,
        AiExchangeNormalizedResultItem result)
    {
        var unit = state.WorkUnits.FirstOrDefault(w => w.WorkUnitId == result.WorkUnitId);
        if (unit is null)
            return new("INVALID", result.WorkUnitId, result.CandidateVersion, null, "Work Unit sconosciuta.");

        var existing = state.Versions.FirstOrDefault(v =>
            v.WorkUnitId == result.WorkUnitId && v.VersionNumber == result.CandidateVersion);

        MaterialEntry? material = null;
        string hash = string.Empty;
        if (!string.IsNullOrWhiteSpace(result.PrimaryAssetPath) && File.Exists(result.PrimaryAssetPath))
        {
            hash = await Sha256Async(result.PrimaryAssetPath);
            if (existing is not null && !string.IsNullOrWhiteSpace(existing.ContentSha256) &&
                !string.Equals(existing.ContentSha256, hash, StringComparison.OrdinalIgnoreCase))
                return new("CONFLICT", result.WorkUnitId, result.CandidateVersion, existing.VersionId,
                    "Stessa Work Unit e stessa versione, ma contenuto differente.");

            material = project.Materials.FirstOrDefault(m => string.Equals(m.Sha256, hash, StringComparison.OrdinalIgnoreCase));
            if (material is null)
            {
                material = await MaterialImporter.ImportAsync(result.PrimaryAssetPath);
                material.Summary = $"AI Exchange {unit.Code} v{result.CandidateVersion} · {material.Summary}";
                project.Materials.Add(material);
            }
        }

        if (existing is not null)
        {
            var changed = false;
            if (!existing.MaterialId.HasValue && material is not null)
            {
                existing.MaterialId = material.MaterialId;
                existing.ContentSha256 = hash;
                changed = true;
            }
            if (string.IsNullOrWhiteSpace(existing.Description) && !string.IsNullOrWhiteSpace(result.Description))
            {
                existing.Description = result.Description.Trim();
                changed = true;
            }
            if (string.IsNullOrWhiteSpace(existing.TextContent) && !string.IsNullOrWhiteSpace(result.TextContent))
            {
                existing.TextContent = result.TextContent;
                changed = true;
            }
            UpdateCompleteness(unit, existing, result.ResultStatus);
            SyncLegacy(project, unit, existing);
            return new(changed ? "UPDATED" : "DUPLICATE", unit.WorkUnitId, result.CandidateVersion, existing.VersionId,
                changed ? "Candidate completata/aggiornata senza creare una nuova versione." : "Risultato già presente.");
        }

        var version = new AiExchangeVersion
        {
            WorkUnitId = unit.WorkUnitId,
            VersionNumber = result.CandidateVersion,
            Origin = result.Origin,
            MaterialId = material?.MaterialId,
            TextContent = result.TextContent ?? string.Empty,
            Description = (result.Description ?? string.Empty).Trim(),
            DescriptionStatus = string.Equals(unit.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase)
                ? (string.IsNullOrWhiteSpace(result.Description) ? AiExchangeDescriptionStatuses.Missing : AiExchangeDescriptionStatuses.Valid)
                : AiExchangeDescriptionStatuses.Valid,
            ContentSha256 = hash,
            SourceSnapshotId = result.SourceSnapshotId,
            DerivedFromVersionId = ResolveBaseVersion(state, unit)?.VersionId,
            SharedContextVersion = unit.SharedContextIds
                .Select(id => state.SharedContexts.FirstOrDefault(c => c.SharedContextId == id)?.Version ?? 0)
                .DefaultIfEmpty(0).Max(),
            CreatedAtLocal = DateTimeOffset.Now.ToString("O")
        };
        UpdateCompleteness(unit, version, result.ResultStatus);
        state.Versions.Add(version);
        if (!unit.CandidateVersionIds.Contains(version.VersionId)) unit.CandidateVersionIds.Add(version.VersionId);
        SyncLegacy(project, unit, version);
        return new(version.Status == AiExchangeVersionStatuses.Incomplete ? "INCOMPLETE" : "IMPORTED",
            unit.WorkUnitId, version.VersionNumber, version.VersionId,
            version.Status == AiExchangeVersionStatuses.Incomplete ? "Candidate importata ma incompleta." : "Candidate importata e pronta da controllare.");
    }

    public static bool Approve(PreviewProject project, AiExchangeState state, Guid versionId, out string message)
    {
        var version = state.Versions.FirstOrDefault(v => v.VersionId == versionId);
        if (version is null) { message = "Versione non trovata."; return false; }
        var unit = state.WorkUnits.FirstOrDefault(w => w.WorkUnitId == version.WorkUnitId);
        if (unit is null) { message = "Work Unit non trovata."; return false; }
        if (version.Status == AiExchangeVersionStatuses.Incomplete ||
            (string.Equals(unit.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase) &&
             version.DescriptionStatus != AiExchangeDescriptionStatuses.Valid))
        {
            message = "Completa e verifica prima il risultato e la descrizione.";
            return false;
        }

        if (unit.ApprovedVersionId.HasValue)
        {
            var previous = state.Versions.FirstOrDefault(v => v.VersionId == unit.ApprovedVersionId.Value);
            if (previous is not null && previous.VersionId != version.VersionId)
                previous.Status = AiExchangeVersionStatuses.Stale;
        }
        version.Status = AiExchangeVersionStatuses.Approved;
        unit.ApprovedVersionId = version.VersionId;
        unit.CandidateVersionIds.Remove(version.VersionId);
        SyncLegacy(project, unit, version, approved: true);
        message = $"{unit.Code} v{version.VersionNumber} approvata.";
        return true;
    }

    public static AiExchangeVersion RegisterExternalEdit(
        PreviewProject project,
        AiExchangeState state,
        Guid sourceVersionId,
        MaterialEntry editedMaterial)
    {
        var source = state.Versions.Single(v => v.VersionId == sourceVersionId);
        var unit = state.WorkUnits.Single(w => w.WorkUnitId == source.WorkUnitId);
        var version = new AiExchangeVersion
        {
            WorkUnitId = source.WorkUnitId,
            VersionNumber = AiExchangeStateStore.NextVersionNumber(state, source.WorkUnitId),
            Origin = AiExchangeOrigins.UserExternalEdit,
            Status = AiExchangeVersionStatuses.Candidate,
            MaterialId = editedMaterial.MaterialId,
            ContentSha256 = editedMaterial.Sha256,
            Description = source.Description,
            DescriptionStatus = string.Equals(unit.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase)
                ? AiExchangeDescriptionStatuses.NeedsVerification
                : source.DescriptionStatus,
            DerivedFromVersionId = source.VersionId,
            CreatedAtLocal = DateTimeOffset.Now.ToString("O")
        };
        state.Versions.Add(version);
        unit.CandidateVersionIds.Add(version.VersionId);
        return version;
    }

    public static void MarkContextDependentsStale(AiExchangeState state, Guid sharedContextId, int previousVersion)
    {
        var affectedUnits = state.WorkUnits.Where(w => w.SharedContextIds.Contains(sharedContextId)).Select(w => w.WorkUnitId).ToHashSet();
        foreach (var version in state.Versions.Where(v => affectedUnits.Contains(v.WorkUnitId) &&
                                                          v.SharedContextVersion > 0 &&
                                                          v.SharedContextVersion <= previousVersion &&
                                                          v.Status == AiExchangeVersionStatuses.Approved))
            version.Status = AiExchangeVersionStatuses.Stale;
    }

    private static void UpdateCompleteness(AiExchangeWorkUnit unit, AiExchangeVersion version, string? resultStatus)
    {
        var image = string.Equals(unit.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase);
        version.DescriptionStatus = image
            ? (string.IsNullOrWhiteSpace(version.Description) ? AiExchangeDescriptionStatuses.Missing : AiExchangeDescriptionStatuses.Valid)
            : AiExchangeDescriptionStatuses.Valid;
        var hasPrimary = version.MaterialId.HasValue || !string.IsNullOrWhiteSpace(version.TextContent);
        var incomplete = string.Equals(resultStatus, "INCOMPLETE", StringComparison.OrdinalIgnoreCase) || !hasPrimary ||
                         (image && version.DescriptionStatus != AiExchangeDescriptionStatuses.Valid);
        version.Status = incomplete ? AiExchangeVersionStatuses.Incomplete : AiExchangeVersionStatuses.Candidate;
    }

    private static void SyncLegacy(PreviewProject project, AiExchangeWorkUnit unit, AiExchangeVersion version, bool approved = false)
    {
        if (unit.LegacyAiJobId is not Guid legacyId) return;
        var job = project.AiProductionJobs.FirstOrDefault(j => j.JobId == legacyId);
        if (job is null) return;
        if (version.MaterialId.HasValue) job.ResultMaterialId = version.MaterialId;
        if (string.Equals(unit.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase))
            ImageCollectionDescriptionService.SetDescription(job, version.Description);
        else if (!string.IsNullOrWhiteSpace(version.TextContent))
            job.ResultText = version.TextContent;
        job.Status = approved ? AiProductionService.StatusApproved :
            version.Status == AiExchangeVersionStatuses.Incomplete ? AiProductionService.StatusNeedsRevision : AiProductionService.StatusToReview;
        job.UpdatedAtLocal = DateTimeOffset.Now.ToString("O");
    }

    private static AiExchangeVersion? ResolveBaseVersion(AiExchangeState state, AiExchangeWorkUnit unit)
    {
        if (unit.ApprovedVersionId.HasValue)
            return state.Versions.FirstOrDefault(v => v.VersionId == unit.ApprovedVersionId.Value);
        return state.Versions.Where(v => v.WorkUnitId == unit.WorkUnitId)
            .OrderByDescending(v => v.VersionNumber).FirstOrDefault();
    }

    private static async Task<string> Sha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
