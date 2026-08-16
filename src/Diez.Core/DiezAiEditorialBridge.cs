using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiezPublishingStudio;

public sealed record DiezEditorialDestination(
    Guid ContentId,
    string Kind,
    string Title);

public sealed record DiezEditorialPromotionResult(
    string ProjectJson,
    string Status,
    string Message,
    bool Changed,
    Guid VersionId,
    string BookType,
    string OutputType,
    Guid? ContentId,
    Guid? PlacementId,
    Guid? MaterialId,
    string Surface);

/// <summary>
/// Public UI-neutral boundary between AI Exchange approval and the actual editorial model.
/// Approval and application are intentionally separate actions: only an APPROVED version may be
/// promoted, and the same Work Unit keeps the same editorial destination across later versions.
/// </summary>
public static class DiezAiEditorialBridge
{
    private const string PromotionEntityKind = "DiezAiEditorialPromotion";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static IReadOnlyList<DiezEditorialDestination> ReadDestinations(string projectJson)
    {
        var (_, project) = Parse(projectJson);
        return project.ContentNodes
            .Where(node => EditableMasterService.CanEdit(project, node))
            .OrderBy(node => node.Ordinal)
            .ThenBy(node => node.Title, StringComparer.OrdinalIgnoreCase)
            .Select(node => new DiezEditorialDestination(
                node.ContentId,
                node.Kind ?? string.Empty,
                string.IsNullOrWhiteSpace(node.Title) ? "Contenuto senza titolo" : node.Title))
            .ToList();
    }

    public static DiezEditorialPromotionResult PromoteApprovedVersion(
        string projectJson,
        Guid versionId,
        Guid? targetContentId = null)
    {
        var (root, project) = Parse(projectJson);
        var exchange = AiExchangeStateStore.Load(project);
        var version = exchange.Versions.FirstOrDefault(v => v.VersionId == versionId);
        var unit = version is null ? null : exchange.WorkUnits.FirstOrDefault(w => w.WorkUnitId == version.WorkUnitId);
        var bookType = BookTypeProfileService.Get(project);

        if (version is null || unit is null)
            return Result(root, project, "INVALID", "Versione AI non trovata.", false, versionId, bookType, string.Empty, null, null, null, string.Empty);

        if (!string.Equals(version.Status, AiExchangeVersionStatuses.Approved, StringComparison.OrdinalIgnoreCase) ||
            unit.ApprovedVersionId != version.VersionId)
        {
            return Result(
                root,
                project,
                "NOT_APPROVED",
                "Prima approva questa versione. L'approvazione e l'applicazione al libro restano due azioni separate.",
                false,
                version.VersionId,
                bookType,
                unit.ContentType,
                null,
                null,
                version.MaterialId,
                string.Empty);
        }

        var legacy = unit.LegacyAiJobId is Guid legacyId
            ? project.AiProductionJobs.FirstOrDefault(j => j.JobId == legacyId)
            : null;
        var state = LoadPromotionState(project);
        var record = state.Records.FirstOrDefault(r => r.WorkUnitId == unit.WorkUnitId);
        if (record is null)
        {
            record = new AiEditorialPromotionRecord { WorkUnitId = unit.WorkUnitId };
            state.Records.Add(record);
        }

        if (record.ActiveVersionId == version.VersionId && PromotionStillPresent(project, unit, version, legacy, record))
        {
            return Result(
                root,
                project,
                "ALREADY_APPLIED",
                $"{unit.Code} v{version.VersionNumber} è già applicata al libro.",
                false,
                version.VersionId,
                bookType,
                unit.ContentType,
                record.ContentIds.FirstOrDefault() is var existingId && existingId != Guid.Empty ? existingId : null,
                record.PlacementId,
                record.MaterialId,
                record.Surface);
        }

        PromotionOutcome outcome;
        if (string.Equals(unit.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase))
        {
            outcome = PromoteImage(project, bookType, unit, version, legacy, record, targetContentId);
        }
        else if (string.Equals(bookType, BookTypeProfileService.WordSearch, StringComparison.OrdinalIgnoreCase) &&
                 TryPromoteWordSearch(project, unit, version, legacy, record, out var wordSearchOutcome))
        {
            outcome = wordSearchOutcome;
        }
        else if (string.Equals(bookType, BookTypeProfileService.Crossword, StringComparison.OrdinalIgnoreCase) &&
                 TryPromoteCrossword(project, unit, version, legacy, record, out var crosswordOutcome))
        {
            outcome = crosswordOutcome;
        }
        else
        {
            outcome = PromoteTextOrData(project, bookType, unit, version, legacy, record, targetContentId);
        }

        if (!outcome.Success)
        {
            return Result(
                root,
                project,
                outcome.Status,
                outcome.Message,
                false,
                version.VersionId,
                bookType,
                unit.ContentType,
                outcome.ContentId,
                outcome.PlacementId,
                outcome.MaterialId,
                outcome.Surface);
        }

        record.ActiveVersionId = version.VersionId;
        record.BookType = bookType;
        record.OutputType = unit.ContentType;
        record.Surface = outcome.Surface;
        record.PlacementId = outcome.PlacementId;
        record.MaterialId = outcome.MaterialId;
        if (outcome.ContentId.HasValue && !record.ContentIds.Contains(outcome.ContentId.Value))
            record.ContentIds.Insert(0, outcome.ContentId.Value);
        record.AppliedAtLocal = DateTimeOffset.Now.ToString("O");
        SavePromotionState(project, state);

        if (legacy is not null)
        {
            legacy.Status = AiProductionService.StatusApplied;
            if (outcome.ContentId.HasValue) legacy.TargetContentId = outcome.ContentId.Value;
            if (outcome.MaterialId.HasValue) legacy.ResultMaterialId = outcome.MaterialId.Value;
            legacy.UpdatedAtLocal = DateTimeOffset.Now.ToString("O");
        }

        MergeProject(root, project);
        return new DiezEditorialPromotionResult(
            Write(root),
            "APPLIED",
            outcome.Message,
            outcome.Changed,
            version.VersionId,
            bookType,
            unit.ContentType,
            outcome.ContentId,
            outcome.PlacementId,
            outcome.MaterialId,
            outcome.Surface);
    }

    private static PromotionOutcome PromoteImage(
        PreviewProject project,
        string bookType,
        AiExchangeWorkUnit unit,
        AiExchangeVersion version,
        AiProductionJob? legacy,
        AiEditorialPromotionRecord record,
        Guid? requestedTarget)
    {
        if (!version.MaterialId.HasValue)
            return PromotionOutcome.Blocked("INCOMPLETE", "La versione approvata non contiene un'immagine collegata.");
        var material = project.Materials.FirstOrDefault(m => m.MaterialId == version.MaterialId.Value);
        if (material is null || !IllustrationPlanService.IsImage(material))
            return PromotionOutcome.Blocked("INCOMPLETE", "Il materiale immagine approvato non è più disponibile nel progetto.");

        if (string.Equals(bookType, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(bookType, BookTypeProfileService.ImageCollection, StringComparison.OrdinalIgnoreCase))
        {
            return PromotionOutcome.Applied(
                null,
                null,
                material.MaterialId,
                string.Equals(bookType, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase)
                    ? "Raccolta pagine Coloring"
                    : "Raccolta immagini",
                $"{unit.Code} v{version.VersionNumber} è ora parte della raccolta approvata del libro.",
                true);
        }

        var target = ResolveContentTarget(project, requestedTarget, legacy?.TargetContentId, record.ContentIds.FirstOrDefault());
        if (target is null)
        {
            target = CreateAiOwnedContent(project, unit, legacy, material.MaterialId, version.Description, "Section");
            record.ContentIds.Insert(0, target.ContentId);
        }
        else if (!EditableMasterService.CanEdit(project, target))
        {
            return PromotionOutcome.Blocked("INVALID_TARGET", "La destinazione scelta non è un capitolo o una sezione modificabile del Master.", target.ContentId);
        }

        var placement = IllustrationPlanService.Upsert(
            project,
            record.PlacementId,
            material.MaterialId,
            target.ContentId,
            IllustrationPlanService.AfterContent,
            80,
            version.Description);
        if (placement.Placement is null)
            return PromotionOutcome.Blocked("BLOCKED", placement.Message, target.ContentId, record.PlacementId, material.MaterialId);

        return PromotionOutcome.Applied(
            target.ContentId,
            placement.Placement.PlacementId,
            material.MaterialId,
            "Piano illustrazioni",
            placement.Message,
            placement.Changed || record.ActiveVersionId != version.VersionId);
    }

    private static PromotionOutcome PromoteTextOrData(
        PreviewProject project,
        string bookType,
        AiExchangeWorkUnit unit,
        AiExchangeVersion version,
        AiProductionJob? legacy,
        AiEditorialPromotionRecord record,
        Guid? requestedTarget)
    {
        var body = version.TextContent ?? string.Empty;
        if (string.IsNullOrWhiteSpace(body))
            return PromotionOutcome.Blocked("INCOMPLETE", "La versione approvata non contiene testo o dati applicabili.");

        var target = ResolveContentTarget(project, requestedTarget, legacy?.TargetContentId, record.ContentIds.FirstOrDefault());
        var changed = false;
        if (target is null)
        {
            var material = EnsureGeneratedTextMaterial(project, record, unit, legacy, body);
            target = CreateAiOwnedContent(project, unit, legacy, material.MaterialId, body, "Section");
            record.ContentIds.Insert(0, target.ContentId);
            changed = true;
            ConsistencyEngine.Rebuild(project);
            return PromotionOutcome.Applied(
                target.ContentId,
                null,
                material.MaterialId,
                SurfaceForGeneric(bookType, unit.ContentType),
                $"{unit.Code} v{version.VersionNumber} applicata come nuovo contenuto editoriale nel Master.",
                true);
        }

        if (!EditableMasterService.CanEdit(project, target))
            return PromotionOutcome.Blocked("INVALID_TARGET", "La destinazione scelta non è modificabile nel Master.", target.ContentId);

        if (!string.Equals(target.Body ?? string.Empty, body, StringComparison.Ordinal))
        {
            var edit = EditableMasterService.ApplyManualEdit(
                project,
                target.ContentId,
                body,
                $"Risultato AI {unit.Code} v{version.VersionNumber} applicato esplicitamente dopo approvazione.");
            if (!edit.Changed)
                return PromotionOutcome.Blocked("BLOCKED", edit.Message, target.ContentId);
            changed = true;
        }

        return PromotionOutcome.Applied(
            target.ContentId,
            null,
            target.MaterialId == Guid.Empty ? null : target.MaterialId,
            SurfaceForGeneric(bookType, unit.ContentType),
            changed
                ? $"{unit.Code} v{version.VersionNumber} applicata alla destinazione editoriale selezionata."
                : $"{unit.Code} v{version.VersionNumber} coincide già con il contenuto della destinazione.",
            changed);
    }

    private static bool TryPromoteWordSearch(
        PreviewProject project,
        AiExchangeWorkUnit unit,
        AiExchangeVersion version,
        AiProductionJob? legacy,
        AiEditorialPromotionRecord record,
        out PromotionOutcome outcome)
    {
        outcome = default;
        var text = version.TextContent ?? string.Empty;
        if (!TryParseTable(text, out var table)) return false;

        var headers = table.Headers.Select(NormalizeHeader).ToList();
        var wordColumns = headers.Count(h => h.StartsWith("parola", StringComparison.OrdinalIgnoreCase) || h.StartsWith("word", StringComparison.OrdinalIgnoreCase));
        var compactWords = FindHeader(headers, "parole", "words", "wordlist", "listaparole");
        var titleIndex = FindHeader(headers, "titolo", "title", "puzzletitle", "nomepuzzle", "nome");
        var themeIndex = FindHeader(headers, "tema", "theme", "categoria", "category", "argomento");
        var idIndex = FindHeader(headers, "id", "puzzleid", "codice", "codicepuzzle");
        if (wordColumns == 0 && compactWords < 0) return false;
        if (titleIndex < 0 && themeIndex < 0 && idIndex < 0) return false;

        var owned = record.ContentIds
            .Select(id => project.ContentNodes.FirstOrDefault(n => n.ContentId == id && string.Equals(n.Kind, WordSearchWorkspaceService.NodeKind, StringComparison.OrdinalIgnoreCase)))
            .Where(n => n is not null)
            .Cast<ContentNode>()
            .ToList();
        var promoted = new List<Guid>();
        var added = 0;
        var updated = 0;
        var rowIndex = 0;

        foreach (var row in table.Rows)
        {
            var incomingId = Cell(row, idIndex);
            var title = Cell(row, titleIndex);
            var theme = Cell(row, themeIndex);
            var words = new List<string>();
            if (compactWords >= 0) words.AddRange(SplitWords(Cell(row, compactWords)));
            for (var i = 0; i < headers.Count; i++)
            {
                if (i == compactWords) continue;
                if (!headers[i].StartsWith("parola", StringComparison.OrdinalIgnoreCase) &&
                    !headers[i].StartsWith("word", StringComparison.OrdinalIgnoreCase)) continue;
                var value = Cell(row, i);
                if (!string.IsNullOrWhiteSpace(value)) words.Add(value);
            }
            words = words.Select(w => w.Trim()).Where(w => w.Length > 0).ToList();
            if (string.IsNullOrWhiteSpace(incomingId) && string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(theme) && words.Count == 0) continue;

            ContentNode? existing = null;
            if (!string.IsNullOrWhiteSpace(incomingId))
                existing = owned.FirstOrDefault(n => string.Equals(n.SourceLocator, incomingId, StringComparison.OrdinalIgnoreCase));
            if (existing is null && rowIndex < owned.Count) existing = owned[rowIndex];

            var recordRow = new WordSearchRecord
            {
                ContentId = existing?.ContentId ?? Guid.NewGuid(),
                SourceMaterialId = Guid.Empty,
                Order = existing?.Ordinal ?? 0,
                Id = incomingId,
                Title = title,
                Theme = theme,
                Words = words,
                Status = WordSearchWorkspaceService.StatusToReview,
                Origin = "Creato con AI · approvato e promosso",
                Notes = $"Fonte: {unit.Code} v{version.VersionNumber}",
                UpdatedAtLocal = DateTimeOffset.Now.ToString("O")
            };
            WordSearchWorkspaceService.SaveRecord(project, recordRow);
            promoted.Add(recordRow.ContentId);
            if (existing is null) added++; else updated++;
            rowIndex++;
        }

        if (promoted.Count == 0) return false;
        foreach (var id in promoted.Where(id => !record.ContentIds.Contains(id))) record.ContentIds.Add(id);
        outcome = PromotionOutcome.Applied(
            promoted[0],
            null,
            null,
            "Database Word Search",
            $"{unit.Code} v{version.VersionNumber} promossa nel database Word Search: {added} puzzle nuovi · {updated} aggiornati.",
            true);
        return true;
    }

    private static bool TryPromoteCrossword(
        PreviewProject project,
        AiExchangeWorkUnit unit,
        AiExchangeVersion version,
        AiProductionJob? legacy,
        AiEditorialPromotionRecord record,
        out PromotionOutcome outcome)
    {
        outcome = default;
        if (!TryParseTable(version.TextContent ?? string.Empty, out var table)) return false;
        var headers = table.Headers.Select(NormalizeHeader).ToList();
        var wordIndex = FindHeader(headers, "parola", "word", "soluzione", "answer");
        if (wordIndex < 0) return false;
        var definitionIndexes = Enumerable.Range(1, 4)
            .Select(i => FindHeader(headers, $"definizione{i}", $"definition{i}", $"clue{i}"))
            .ToArray();
        var noteIndex = FindHeader(headers, "note", "notes");
        if (definitionIndexes.All(i => i < 0) && noteIndex < 0) return false;

        var touched = 0;
        foreach (var row in table.Rows)
        {
            var word = CrosswordService.NormalizeGridWord(Cell(row, wordIndex));
            if (word.Length < 2) continue;
            var entity = CrosswordService.EnsureWord(project, word, $"AI {unit.Code} v{version.VersionNumber}");
            for (var i = 0; i < definitionIndexes.Length; i++)
            {
                var value = Cell(row, definitionIndexes[i]);
                if (!string.IsNullOrWhiteSpace(value)) CrosswordService.SetDefinitionCell(project, entity.EntityId, i + 1, value);
            }
            var note = Cell(row, noteIndex);
            if (!string.IsNullOrWhiteSpace(note)) CrosswordService.SetNotes(project, entity.EntityId, note);
            touched++;
        }
        if (touched == 0) return false;

        outcome = PromotionOutcome.Applied(
            null,
            null,
            null,
            "Cruciverba · definizioni",
            $"{unit.Code} v{version.VersionNumber} promossa nelle definizioni del Cruciverba: {touched} parole elaborate.",
            true);
        return true;
    }

    private static ContentNode? ResolveContentTarget(
        PreviewProject project,
        Guid? requested,
        Guid? legacyTarget,
        Guid remembered)
    {
        foreach (var id in new[] { requested, legacyTarget, remembered == Guid.Empty ? null : remembered })
        {
            if (!id.HasValue || id.Value == Guid.Empty) continue;
            var node = project.ContentNodes.FirstOrDefault(n => n.ContentId == id.Value);
            if (node is not null) return node;
        }
        return null;
    }

    private static ContentNode CreateAiOwnedContent(
        PreviewProject project,
        AiExchangeWorkUnit unit,
        AiProductionJob? legacy,
        Guid materialId,
        string body,
        string kind)
    {
        var ordinal = project.ContentNodes.Count == 0 ? 1 : project.ContentNodes.Max(n => n.Ordinal) + 1;
        var title = !string.IsNullOrWhiteSpace(legacy?.Title)
            ? legacy!.Title.Trim()
            : string.IsNullOrWhiteSpace(unit.Code) ? "Contenuto AI" : unit.Code;
        var node = new ContentNode
        {
            ContentId = Guid.NewGuid(),
            MaterialId = materialId,
            Kind = kind,
            Title = title,
            Body = body ?? string.Empty,
            Ordinal = ordinal,
            SourceLocator = $"ai:{unit.WorkUnitId:D}"
        };
        project.ContentNodes.Add(node);
        return node;
    }

    private static MaterialEntry EnsureGeneratedTextMaterial(
        PreviewProject project,
        AiEditorialPromotionRecord record,
        AiExchangeWorkUnit unit,
        AiProductionJob? legacy,
        string body)
    {
        var material = record.MaterialId.HasValue
            ? project.Materials.FirstOrDefault(m => m.MaterialId == record.MaterialId.Value)
            : null;
        material ??= project.Materials.FirstOrDefault(m => string.Equals(m.SourcePath, $"ai:{unit.WorkUnitId:D}", StringComparison.OrdinalIgnoreCase));
        var bytes = Encoding.UTF8.GetBytes(body);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (material is null)
        {
            material = new MaterialEntry
            {
                MaterialId = Guid.NewGuid(),
                FileName = $"{(string.IsNullOrWhiteSpace(unit.Code) ? "AI" : unit.Code)}-contenuto.txt",
                SourcePath = $"ai:{unit.WorkUnitId:D}",
                Kind = "Testo generato AI",
                ImportedAtLocal = DateTimeOffset.Now.ToString("O")
            };
            project.Materials.Add(material);
        }
        material.SizeBytes = bytes.LongLength;
        material.Sha256 = hash;
        material.Summary = $"Contenuto approvato da {unit.Code}.";
        material.Preview = body[..Math.Min(body.Length, 1200)];
        material.ExtractedText = body;
        material.IsEmbedded = false;
        material.EmbeddedPath = string.Empty;
        record.MaterialId = material.MaterialId;
        return material;
    }

    private static bool PromotionStillPresent(
        PreviewProject project,
        AiExchangeWorkUnit unit,
        AiExchangeVersion version,
        AiProductionJob? legacy,
        AiEditorialPromotionRecord record)
    {
        if (legacy is not null && !string.Equals(legacy.Status, AiProductionService.StatusApplied, StringComparison.Ordinal)) return false;
        if (string.Equals(unit.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase))
        {
            if (record.PlacementId.HasValue)
                return project.IllustrationPlacements.Any(p => p.PlacementId == record.PlacementId.Value && p.MaterialId == version.MaterialId);
            return record.MaterialId == version.MaterialId;
        }
        if (string.Equals(record.Surface, "Database Word Search", StringComparison.OrdinalIgnoreCase))
            return record.ContentIds.Count > 0 && record.ContentIds.All(id => project.ContentNodes.Any(n => n.ContentId == id));
        if (string.Equals(record.Surface, "Cruciverba · definizioni", StringComparison.OrdinalIgnoreCase)) return true;
        var contentId = record.ContentIds.FirstOrDefault();
        return contentId != Guid.Empty && project.ContentNodes.Any(n => n.ContentId == contentId && string.Equals(n.Body, version.TextContent, StringComparison.Ordinal));
    }

    private static string SurfaceForGeneric(string bookType, string contentType)
    {
        if (string.Equals(bookType, BookTypeProfileService.Novel, StringComparison.OrdinalIgnoreCase)) return "Master · Romanzo / racconto";
        if (string.Equals(bookType, BookTypeProfileService.EssayManual, StringComparison.OrdinalIgnoreCase)) return "Master · Saggio / manuale";
        if (string.Equals(bookType, BookTypeProfileService.IllustratedBook, StringComparison.OrdinalIgnoreCase)) return "Master · Libro illustrato";
        if (string.Equals(bookType, BookTypeProfileService.Quiz, StringComparison.OrdinalIgnoreCase)) return "Master · Quiz / trivia";
        if (string.Equals(bookType, BookTypeProfileService.DataCollection, StringComparison.OrdinalIgnoreCase)) return "Master · Catalogo / raccolta dati";
        return "Editable Master";
    }

    private static AiEditorialPromotionState LoadPromotionState(PreviewProject project)
    {
        var entity = project.Entities.FirstOrDefault(e => string.Equals(e.Kind, PromotionEntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null || string.IsNullOrWhiteSpace(entity.Notes)) return new AiEditorialPromotionState();
        try
        {
            var state = JsonSerializer.Deserialize<AiEditorialPromotionState>(entity.Notes, JsonOptions) ?? new AiEditorialPromotionState();
            state.Records ??= [];
            foreach (var record in state.Records) record.ContentIds ??= [];
            return state;
        }
        catch { return new AiEditorialPromotionState(); }
    }

    private static void SavePromotionState(PreviewProject project, AiEditorialPromotionState state)
    {
        var entity = project.Entities.FirstOrDefault(e => string.Equals(e.Kind, PromotionEntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
        {
            entity = new GraphEntity
            {
                EntityId = Guid.NewGuid(),
                Kind = PromotionEntityKind,
                Name = "Applicazioni AI al libro",
                IsCandidate = false
            };
            project.Entities.Add(entity);
        }
        entity.IsCandidate = false;
        entity.Notes = JsonSerializer.Serialize(state, JsonOptions);
    }

    private static (JsonObject Root, PreviewProject Project) Parse(string projectJson)
    {
        var root = JsonNode.Parse(projectJson) as JsonObject
            ?? throw new InvalidDataException("Il JSON del progetto Diez non è valido.");
        var project = JsonSerializer.Deserialize<PreviewProject>(projectJson, JsonOptions)
            ?? throw new InvalidDataException("Il progetto Diez non può essere letto dal Core.");
        project.AiProduction ??= new AiProductionSettings();
        project.AiProductionJobs ??= [];
        project.Materials ??= [];
        project.ContentNodes ??= [];
        project.IllustrationPlacements ??= [];
        project.Entities ??= [];
        project.Relations ??= [];
        project.BibleEntries ??= [];
        project.ConsistencyFacts ??= [];
        project.ConsistencyIssues ??= [];
        project.ConsistencyResolutions ??= [];
        project.RevisionCandidates ??= [];
        return (root, project);
    }

    private static void MergeProject(JsonObject root, PreviewProject project)
    {
        MergeArray(root, "AiProductionJobs", project.AiProductionJobs, "JobId");
        MergeArray(root, "Materials", project.Materials, "MaterialId");
        MergeArray(root, "ContentNodes", project.ContentNodes, "ContentId");
        MergeArray(root, "IllustrationPlacements", project.IllustrationPlacements, "PlacementId");
        MergeArray(root, "Entities", project.Entities, "EntityId");
        MergeArray(root, "BibleEntries", project.BibleEntries, "BibleEntryId");
        MergeArray(root, "RevisionCandidates", project.RevisionCandidates, "CandidateId");
        MergeArray(root, "ConsistencyFacts", project.ConsistencyFacts, "FactId", removeMissing: true);
        MergeArray(root, "ConsistencyIssues", project.ConsistencyIssues, "IssueId", removeMissing: true);
        MergeArray(root, "ConsistencyResolutions", project.ConsistencyResolutions, "ResolutionId");
    }

    private static void MergeArray<T>(
        JsonObject root,
        string property,
        IEnumerable<T> typedItems,
        string idProperty,
        bool removeMissing = false)
    {
        var raw = root[property] as JsonArray ?? new JsonArray();
        root[property] = raw;
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in typedItems)
        {
            if (JsonSerializer.SerializeToNode(item, JsonOptions) is not JsonObject typed) continue;
            var id = Scalar(typed[idProperty]);
            if (string.IsNullOrWhiteSpace(id)) continue;
            ids.Add(id);
            var existing = raw.OfType<JsonObject>().FirstOrDefault(x => string.Equals(Scalar(x[idProperty]), id, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                raw.Add(typed);
                continue;
            }
            foreach (var pair in typed)
                existing[pair.Key] = pair.Value?.DeepClone();
        }

        if (!removeMissing) return;
        for (var i = raw.Count - 1; i >= 0; i--)
        {
            if (raw[i] is not JsonObject obj) continue;
            var id = Scalar(obj[idProperty]);
            if (!string.IsNullOrWhiteSpace(id) && !ids.Contains(id)) raw.RemoveAt(i);
        }
    }

    private static DiezEditorialPromotionResult Result(
        JsonObject root,
        PreviewProject project,
        string status,
        string message,
        bool changed,
        Guid versionId,
        string bookType,
        string outputType,
        Guid? contentId,
        Guid? placementId,
        Guid? materialId,
        string surface)
    {
        MergeProject(root, project);
        return new DiezEditorialPromotionResult(
            Write(root), status, message, changed, versionId, bookType, outputType,
            contentId, placementId, materialId, surface);
    }

    private static bool TryParseTable(string text, out ParsedTable table)
    {
        table = new ParsedTable([], []);
        var lines = (text ?? string.Empty)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n')
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();
        if (lines.Count < 2) return false;
        var delimiter = new[] { ';', '\t', ',' }
            .OrderByDescending(c => CountDelimiter(lines[0], c))
            .First();
        if (CountDelimiter(lines[0], delimiter) == 0) return false;
        var rows = lines.Select(line => ParseDelimitedLine(line, delimiter)).ToList();
        table = new ParsedTable(rows[0], rows.Skip(1).ToList());
        return table.Headers.Count > 1 && table.Rows.Count > 0;
    }

    private static int FindHeader(IReadOnlyList<string> headers, params string[] names)
    {
        for (var i = 0; i < headers.Count; i++)
            if (names.Contains(headers[i], StringComparer.OrdinalIgnoreCase)) return i;
        return -1;
    }

    private static string Cell(IReadOnlyList<string> row, int index) =>
        index >= 0 && index < row.Count ? row[index].Trim() : string.Empty;

    private static List<string> SplitWords(string value) =>
        (value ?? string.Empty).Split(new[] { '|', ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static string NormalizeHeader(string? value)
    {
        var formD = (value ?? string.Empty).Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        foreach (var ch in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch)) builder.Append(char.ToLowerInvariant(ch));
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static int CountDelimiter(string line, char delimiter)
    {
        var count = 0;
        var quoted = false;
        foreach (var ch in line)
        {
            if (ch == '"') quoted = !quoted;
            else if (ch == delimiter && !quoted) count++;
        }
        return count;
    }

    private static List<string> ParseDelimitedLine(string line, char delimiter)
    {
        var values = new List<string>();
        var builder = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { builder.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (ch == delimiter && !quoted)
            {
                values.Add(builder.ToString());
                builder.Clear();
            }
            else builder.Append(ch);
        }
        values.Add(builder.ToString());
        return values;
    }

    private static string Scalar(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var text)) return text ?? string.Empty;
        return node?.ToJsonString().Trim('"') ?? string.Empty;
    }

    private static string Write(JsonObject root) =>
        root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    private sealed record ParsedTable(List<string> Headers, List<List<string>> Rows);

    private sealed class AiEditorialPromotionState
    {
        public int SchemaVersion { get; set; } = 1;
        public List<AiEditorialPromotionRecord> Records { get; set; } = [];
    }

    private sealed class AiEditorialPromotionRecord
    {
        public Guid WorkUnitId { get; set; }
        public Guid ActiveVersionId { get; set; }
        public string BookType { get; set; } = string.Empty;
        public string OutputType { get; set; } = string.Empty;
        public string Surface { get; set; } = string.Empty;
        public List<Guid> ContentIds { get; set; } = [];
        public Guid? PlacementId { get; set; }
        public Guid? MaterialId { get; set; }
        public string AppliedAtLocal { get; set; } = string.Empty;
    }

    private readonly record struct PromotionOutcome(
        bool Success,
        string Status,
        string Message,
        bool Changed,
        Guid? ContentId,
        Guid? PlacementId,
        Guid? MaterialId,
        string Surface)
    {
        public static PromotionOutcome Applied(
            Guid? contentId,
            Guid? placementId,
            Guid? materialId,
            string surface,
            string message,
            bool changed) =>
            new(true, "APPLIED", message, changed, contentId, placementId, materialId, surface);

        public static PromotionOutcome Blocked(
            string status,
            string message,
            Guid? contentId = null,
            Guid? placementId = null,
            Guid? materialId = null) =>
            new(false, status, message, false, contentId, placementId, materialId, string.Empty);
    }
}
