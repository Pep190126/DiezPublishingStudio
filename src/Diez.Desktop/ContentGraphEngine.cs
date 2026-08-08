using System.Text.RegularExpressions;

namespace DiezPublishingStudio;

internal static class ContentGraphEngine
{
    private static readonly Regex SentenceSplit = new(@"[.!?\r\n]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ProperWord = new(@"\b[A-ZÀ-ÖØ-Ý][\p{L}'’-]{2,}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex LocationCue = new(@"\b(?:a|al|alla|alle|nel|nella|nelle|verso|da|dal|dalla|dalle)\s+(?<name>[A-ZÀ-ÖØ-Ý][\p{L}'’-]{2,})\b", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Capitolo", "Parte", "Sezione", "Prologo", "Epilogo", "Introduzione",
        "Il", "Lo", "La", "Le", "Gli", "Un", "Uno", "Una", "Nel", "Nella", "Nelle",
        "Al", "Alla", "Alle", "Dal", "Dalla", "Dalle", "Con", "Per", "Tra", "Fra",
        "Quando", "Dopo", "Prima", "Poi", "Quella", "Quello", "Questa", "Questo"
    };

    public static GraphAnalysisResult Analyze(
        PreviewProject project,
        MaterialEntry material,
        IReadOnlyList<ContentNode> nodes)
    {
        if (string.IsNullOrWhiteSpace(material.ExtractedText))
            return new GraphAnalysisResult(0, 0);

        project.Entities ??= [];
        project.Relations ??= [];
        project.BibleEntries ??= [];

        var sentences = SentenceSplit.Split(material.ExtractedText)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var locations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sentence in sentences)
        {
            foreach (Match match in ProperWord.Matches(sentence))
            {
                var word = match.Value.Trim();
                if (StopWords.Contains(word) || word.Length < 3) continue;
                counts[word] = counts.TryGetValue(word, out var current) ? current + 1 : 1;
            }

            foreach (Match match in LocationCue.Matches(sentence))
            {
                var name = match.Groups["name"].Value.Trim();
                if (!StopWords.Contains(name)) locations.Add(name);
            }
        }

        var candidates = counts
            .Where(pair => pair.Value >= 2 || locations.Contains(pair.Key))
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var createdEntities = 0;
        var createdRelations = 0;
        var resolved = new Dictionary<string, GraphEntity>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            var kind = locations.Contains(candidate.Key) ? "Location" : "Character";
            var entity = project.Entities.FirstOrDefault(e =>
                string.Equals(e.Name, candidate.Key, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.Kind, kind, StringComparison.OrdinalIgnoreCase));

            if (entity is null)
            {
                var firstNode = nodes.FirstOrDefault(n => ContainsName(n.Title, candidate.Key) || ContainsName(n.Body, candidate.Key));
                entity = new GraphEntity
                {
                    EntityId = Guid.NewGuid(),
                    Kind = kind,
                    Name = candidate.Key,
                    IsCandidate = true,
                    SourceMaterialId = material.MaterialId,
                    FirstSourceContentId = firstNode?.ContentId,
                    Notes = $"Rilevato automaticamente ({candidate.Value} occorrenze)"
                };
                project.Entities.Add(entity);
                createdEntities++;
            }

            resolved[candidate.Key] = entity;
        }

        foreach (var entity in resolved.Values)
        {
            foreach (var node in nodes)
            {
                if (!ContainsName(node.Title, entity.Name) && !ContainsName(node.Body, entity.Name)) continue;
                if (AddRelation(project, "Entity", entity.EntityId, "AppearsIn", "Content", node.ContentId, true,
                    $"{entity.Name} compare in {node.Title}"))
                    createdRelations++;
            }
        }

        var characters = resolved.Values.Where(e => string.Equals(e.Kind, "Character", StringComparison.OrdinalIgnoreCase)).ToList();
        var locationEntities = resolved.Values.Where(e => string.Equals(e.Kind, "Location", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var sentence in sentences)
        {
            var sentenceCharacters = characters.Where(e => ContainsName(sentence, e.Name)).ToList();
            var sentenceLocations = locationEntities.Where(e => ContainsName(sentence, e.Name)).ToList();
            foreach (var character in sentenceCharacters)
            foreach (var location in sentenceLocations)
            {
                if (AddRelation(project, "Entity", character.EntityId, "LocatedIn", "Entity", location.EntityId, true,
                    TrimEvidence(sentence)))
                    createdRelations++;
            }
        }

        return new GraphAnalysisResult(createdEntities, createdRelations);
    }

    public static bool ConfirmEntity(PreviewProject project, Guid entityId)
    {
        var entity = project.Entities.FirstOrDefault(e => e.EntityId == entityId);
        if (entity is null) return false;

        entity.IsCandidate = false;
        foreach (var relation in project.Relations.Where(r =>
                     (r.FromKind == "Entity" && r.FromId == entityId) ||
                     (r.ToKind == "Entity" && r.ToId == entityId)))
            relation.IsCandidate = false;

        UpsertBible(project, entity, "canonical_name", entity.Name, "Binding");
        UpsertBible(project, entity, "entity_kind", entity.Kind, "Binding");
        return true;
    }

    public static bool IgnoreEntity(PreviewProject project, Guid entityId)
    {
        var removed = project.Entities.RemoveAll(e => e.EntityId == entityId) > 0;
        if (!removed) return false;
        project.Relations.RemoveAll(r =>
            (r.FromKind == "Entity" && r.FromId == entityId) ||
            (r.ToKind == "Entity" && r.ToId == entityId));
        project.BibleEntries.RemoveAll(b => b.SubjectEntityId == entityId);
        return true;
    }

    private static void UpsertBible(PreviewProject project, GraphEntity entity, string key, string value, string authority)
    {
        var existing = project.BibleEntries.FirstOrDefault(b =>
            b.SubjectEntityId == entity.EntityId &&
            string.Equals(b.Key, key, StringComparison.OrdinalIgnoreCase) &&
            b.IsActive);
        if (existing is null)
        {
            project.BibleEntries.Add(new BibleEntry
            {
                BibleEntryId = Guid.NewGuid(),
                SubjectEntityId = entity.EntityId,
                Key = key,
                Value = value,
                Authority = authority,
                IsActive = true,
                SourceContentId = entity.FirstSourceContentId
            });
        }
        else
        {
            existing.Value = value;
            existing.Authority = authority;
        }
    }

    private static bool AddRelation(
        PreviewProject project,
        string fromKind,
        Guid fromId,
        string type,
        string toKind,
        Guid toId,
        bool isCandidate,
        string evidence)
    {
        if (fromId == Guid.Empty || toId == Guid.Empty || (fromKind == toKind && fromId == toId)) return false;
        if (project.Relations.Any(r =>
                r.FromKind == fromKind && r.FromId == fromId &&
                r.ToKind == toKind && r.ToId == toId &&
                string.Equals(r.Type, type, StringComparison.OrdinalIgnoreCase)))
            return false;

        project.Relations.Add(new ContentRelation
        {
            RelationId = Guid.NewGuid(),
            FromKind = fromKind,
            FromId = fromId,
            Type = type,
            ToKind = toKind,
            ToId = toId,
            IsCandidate = isCandidate,
            Evidence = evidence
        });
        return true;
    }

    private static bool ContainsName(string? text, string name)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(name)) return false;
        return Regex.IsMatch(text, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(name)}(?![\p{{L}}\p{{N}}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string TrimEvidence(string value)
    {
        var clean = Regex.Replace(value, @"\s+", " ").Trim();
        return clean.Length <= 240 ? clean : clean[..237] + "...";
    }
}

internal readonly record struct GraphAnalysisResult(int EntitiesCreated, int RelationsCreated);
