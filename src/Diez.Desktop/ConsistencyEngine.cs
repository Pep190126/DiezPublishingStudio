using System.Text.RegularExpressions;

namespace DiezPublishingStudio;

internal static class ConsistencyEngine
{
    private static readonly RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    public static ConsistencyAnalysisResult Rebuild(PreviewProject project)
    {
        project.ConsistencyFacts ??= [];
        project.ConsistencyIssues ??= [];
        project.ConsistencyFacts.Clear();
        project.ConsistencyIssues.Clear();

        var confirmedEntities = project.Entities.Where(e => !e.IsCandidate).ToList();
        var factNodes = SelectFactNodes(project.ContentNodes);

        foreach (var entity in confirmedEntities)
        {
            foreach (var node in factNodes)
                ExtractFacts(project, entity, node);
        }

        AddBibleIntegrityIssues(project);
        AddObservedFactIssues(project);
        AnnotateEntities(project);

        return new ConsistencyAnalysisResult(project.ConsistencyFacts.Count, project.ConsistencyIssues.Count);
    }

    private static List<ContentNode> SelectFactNodes(IReadOnlyList<ContentNode> nodes)
    {
        var detailed = nodes.Where(n => n.Kind is "Chapter" or "Section").ToList();
        if (detailed.Count > 0) return detailed;
        return nodes.Where(n => n.Kind == "Document").ToList();
    }

    private static void ExtractFacts(PreviewProject project, GraphEntity entity, ContentNode node)
    {
        if (string.IsNullOrWhiteSpace(node.Body) || string.IsNullOrWhiteSpace(entity.Name)) return;

        var name = Regex.Escape(entity.Name);
        AddMatches(project, entity, node, "age",
            new Regex($@"(?<![\p{{L}}\p{{N}}]){name}(?![\p{{L}}\p{{N}}])\s+(?:ha|aveva)\s+(?<value>\d{{1,3}})\s+anni\b", Options));

        AddMatches(project, entity, node, "eye_color",
            new Regex($@"(?<![\p{{L}}\p{{N}}]){name}(?![\p{{L}}\p{{N}}])\s+(?:ha|aveva)\s+gli\s+occhi\s+(?<value>[\p{{L}}'-]+)", Options));

        AddMatches(project, entity, node, "hair_color",
            new Regex($@"(?<![\p{{L}}\p{{N}}]){name}(?![\p{{L}}\p{{N}}])\s+(?:ha|aveva)\s+i\s+capelli\s+(?<value>[\p{{L}}'-]+)", Options));

        AddMatches(project, entity, node, "birth_place",
            new Regex($@"(?<![\p{{L}}\p{{N}}]){name}(?![\p{{L}}\p{{N}}])\s+(?:è|era)\s+nat[oa]\s+a\s+(?<value>[A-ZÀ-ÖØ-Ý][\p{{L}}'’-]*(?:\s+[A-ZÀ-ÖØ-Ý][\p{{L}}'’-]*){{0,2}})", Options));

        AddMatches(project, entity, node, "residence",
            new Regex($@"(?<![\p{{L}}\p{{N}}]){name}(?![\p{{L}}\p{{N}}])\s+(?:vive|viveva|abita|abitava)\s+(?:a|al|alla|nel|nella)\s+(?<value>[A-ZÀ-ÖØ-Ý][\p{{L}}'’-]*(?:\s+[A-ZÀ-ÖØ-Ý][\p{{L}}'’-]*){{0,2}})", Options));
    }

    private static void AddMatches(PreviewProject project, GraphEntity entity, ContentNode node, string key, Regex regex)
    {
        foreach (Match match in regex.Matches(node.Body))
        {
            var value = match.Groups["value"].Value.Trim().TrimEnd('.', ',', ';', ':', '!', '?');
            if (string.IsNullOrWhiteSpace(value)) continue;

            if (project.ConsistencyFacts.Any(f =>
                    f.SubjectEntityId == entity.EntityId &&
                    f.ContentId == node.ContentId &&
                    string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(NormalizeValue(f.Value), NormalizeValue(value), StringComparison.Ordinal)))
                continue;

            project.ConsistencyFacts.Add(new ConsistencyFact
            {
                FactId = Guid.NewGuid(),
                SubjectEntityId = entity.EntityId,
                Key = key,
                Value = value,
                ContentId = node.ContentId,
                SourceLocator = node.SourceLocator,
                Evidence = ExtractEvidence(node.Body, match.Index, match.Length)
            });
        }
    }

    private static void AddBibleIntegrityIssues(PreviewProject project)
    {
        foreach (var group in project.BibleEntries
                     .Where(b => b.IsActive && string.Equals(b.Authority, "Binding", StringComparison.OrdinalIgnoreCase))
                     .GroupBy(b => new { b.SubjectEntityId, Key = b.Key.ToLowerInvariant() }))
        {
            var values = group.Select(b => NormalizeValue(b.Value)).Where(v => v.Length > 0).Distinct().ToList();
            if (values.Count <= 1) continue;

            var entity = project.Entities.FirstOrDefault(e => e.EntityId == group.Key.SubjectEntityId);
            AddIssue(project, "Critical", "BIBLE_BINDING_CONFLICT", group.Key.SubjectEntityId, group.Key.Key,
                $"La Bible contiene più valori vincolanti per {entity?.Name ?? "un'entità"}: {string.Join(" / ", group.Select(b => b.Value).Distinct(StringComparer.OrdinalIgnoreCase))}.",
                group.Where(b => b.SourceContentId.HasValue).Select(b => b.SourceContentId!.Value));
        }

        foreach (var entry in project.BibleEntries.Where(b => b.IsActive))
        {
            var entity = project.Entities.FirstOrDefault(e => e.EntityId == entry.SubjectEntityId);
            if (entity is null)
            {
                AddIssue(project, "Warning", "BIBLE_ORPHAN", entry.SubjectEntityId, entry.Key,
                    $"Voce Bible senza entità collegata: {entry.Key} = {entry.Value}.",
                    entry.SourceContentId.HasValue ? [entry.SourceContentId.Value] : []);
                continue;
            }

            if (!string.Equals(entry.Authority, "Binding", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(entry.Key, "canonical_name", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(NormalizeValue(entry.Value), NormalizeValue(entity.Name), StringComparison.Ordinal))
            {
                AddIssue(project, "Critical", "CANONICAL_NAME_MISMATCH", entity.EntityId, entry.Key,
                    $"Nome dell'entità '{entity.Name}' diverso dal nome canonico Bible '{entry.Value}'.",
                    entry.SourceContentId.HasValue ? [entry.SourceContentId.Value] : []);
            }

            if (string.Equals(entry.Key, "entity_kind", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(NormalizeValue(entry.Value), NormalizeValue(entity.Kind), StringComparison.Ordinal))
            {
                AddIssue(project, "Critical", "ENTITY_KIND_MISMATCH", entity.EntityId, entry.Key,
                    $"Tipo dell'entità '{entity.Kind}' diverso dal tipo vincolante Bible '{entry.Value}'.",
                    entry.SourceContentId.HasValue ? [entry.SourceContentId.Value] : []);
            }
        }
    }

    private static void AddObservedFactIssues(PreviewProject project)
    {
        foreach (var group in project.ConsistencyFacts.GroupBy(f => new { f.SubjectEntityId, Key = f.Key.ToLowerInvariant() }))
        {
            var byValue = group.GroupBy(f => NormalizeValue(f.Value)).Where(g => g.Key.Length > 0).ToList();
            var entity = project.Entities.FirstOrDefault(e => e.EntityId == group.Key.SubjectEntityId);

            if (byValue.Count > 1)
            {
                var readableValues = byValue.Select(g => g.First().Value).ToList();
                AddIssue(project, "Error", "FACT_CONTRADICTION", group.Key.SubjectEntityId, group.Key.Key,
                    $"Possibile contraddizione per {entity?.Name ?? "entità"}, {DescribeKey(group.Key.Key)}: {string.Join(" / ", readableValues)}.",
                    group.Select(f => f.ContentId));
            }

            var binding = project.BibleEntries.FirstOrDefault(b =>
                b.SubjectEntityId == group.Key.SubjectEntityId && b.IsActive &&
                string.Equals(b.Authority, "Binding", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(b.Key, group.Key.Key, StringComparison.OrdinalIgnoreCase));
            if (binding is null) continue;

            var canonical = NormalizeValue(binding.Value);
            var conflicts = group.Where(f => NormalizeValue(f.Value) != canonical).ToList();
            if (conflicts.Count == 0) continue;

            AddIssue(project, "Critical", "BIBLE_FACT_CONFLICT", group.Key.SubjectEntityId, group.Key.Key,
                $"Il testo contraddice la Bible per {entity?.Name ?? "entità"}, {DescribeKey(group.Key.Key)}: Bible '{binding.Value}', testo '{string.Join(" / ", conflicts.Select(f => f.Value).Distinct(StringComparer.OrdinalIgnoreCase))}'.",
                conflicts.Select(f => f.ContentId));
        }
    }

    private static void AddIssue(PreviewProject project, string severity, string code, Guid? subjectEntityId, string key, string message, IEnumerable<Guid> contentIds)
    {
        var ids = contentIds.Where(id => id != Guid.Empty).Distinct().ToList();
        project.ConsistencyIssues.Add(new ConsistencyIssue
        {
            IssueId = Guid.NewGuid(),
            Severity = severity,
            Code = code,
            SubjectEntityId = subjectEntityId,
            Key = key,
            Message = message,
            ContentIds = ids,
            Status = "Open",
            DetectedAtLocal = DateTimeOffset.Now.ToString("O")
        });
    }

    private static void AnnotateEntities(PreviewProject project)
    {
        const string marker = "\n[Coerenza]";
        foreach (var entity in project.Entities)
        {
            var baseNotes = entity.Notes ?? string.Empty;
            var markerIndex = baseNotes.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex >= 0) baseNotes = baseNotes[..markerIndex].TrimEnd();

            var issues = project.ConsistencyIssues.Where(i => i.SubjectEntityId == entity.EntityId && i.Status == "Open").ToList();
            if (issues.Count == 0)
            {
                entity.Notes = baseNotes;
                continue;
            }

            var critical = issues.Count(i => i.Severity == "Critical");
            var errors = issues.Count(i => i.Severity == "Error");
            var warnings = issues.Count(i => i.Severity == "Warning");
            var summary = $"{issues.Count} problemi aperti";
            if (critical > 0) summary += $", {critical} critici";
            if (errors > 0) summary += $", {errors} errori";
            if (warnings > 0) summary += $", {warnings} avvisi";
            var first = issues[0].Message;
            entity.Notes = $"{baseNotes}{marker} {summary}. {first}".Trim();
        }
    }

    private static string ExtractEvidence(string text, int index, int length)
    {
        var start = text.LastIndexOfAny(['.', '!', '?', '\n', '\r'], Math.Max(0, index - 1));
        start = start < 0 ? 0 : start + 1;
        var searchFrom = Math.Min(text.Length, index + length);
        var endCandidates = new[]
        {
            text.IndexOf('.', searchFrom), text.IndexOf('!', searchFrom), text.IndexOf('?', searchFrom),
            text.IndexOf('\n', searchFrom), text.IndexOf('\r', searchFrom)
        }.Where(i => i >= 0).ToList();
        var end = endCandidates.Count == 0 ? text.Length : endCandidates.Min() + 1;
        var evidence = Regex.Replace(text[start..end], @"\s+", " ").Trim();
        return evidence.Length <= 280 ? evidence : evidence[..277] + "...";
    }

    private static string NormalizeValue(string? value) =>
        Regex.Replace((value ?? string.Empty).Trim().ToLowerInvariant(), @"\s+", " ");

    private static string DescribeKey(string key) => key switch
    {
        "age" => "età",
        "eye_color" => "colore degli occhi",
        "hair_color" => "colore dei capelli",
        "birth_place" => "luogo di nascita",
        "residence" => "residenza",
        _ => key
    };
}

internal readonly record struct ConsistencyAnalysisResult(int FactsDetected, int IssuesDetected);
