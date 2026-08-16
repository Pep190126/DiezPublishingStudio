using System.Text;
using System.Text.Json;

namespace DiezPublishingStudio;

internal sealed class MultiSubjectProfile
{
    public int SchemaVersion { get; set; } = 1;
    public bool Enabled { get; set; }
    public int RequestedCount { get; set; } = 1;
    public string ActiveSubjectId { get; set; } = string.Empty;
    public string GroupDescription { get; set; } = string.Empty;
    public List<MultiSubjectDefinition> Subjects { get; set; } = [];
}

internal sealed class MultiSubjectDefinition
{
    public string SubjectId { get; set; } = Guid.NewGuid().ToString("D");
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Included { get; set; } = true;
    public bool Archived { get; set; }
    public Dictionary<string, SubjectConsistencyRule> Consistency { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? "Soggetto" : Name.Trim();
}

internal sealed class SubjectConsistencyRule
{
    public string Level { get; set; } = "PREFERRED";
    public string Strategy { get; set; } = "AI";
    public string Variation { get; set; } = string.Empty;
}

/// <summary>
/// Optional explicit cast/subject model. When disabled, Diez keeps the legacy free theme/group field.
/// When enabled, Work Units are structurally linked to stable SubjectIds instead of parsing comma-separated text.
/// </summary>
internal static class MultiSubjectProfileService
{
    private const string EntityKind = "DiezMultiSubjectProfile";
    public const int MaxSubjects = 12;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static MultiSubjectProfile Load(PreviewProject project)
    {
        var entity = project.Entities.FirstOrDefault(e => string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase));
        MultiSubjectProfile model;
        if (entity is null || string.IsNullOrWhiteSpace(entity.Notes))
        {
            model = new MultiSubjectProfile();
            if (string.Equals(BookTypeProfileService.Get(project), BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase))
                model.GroupDescription = BookTypePromptProfileService.LoadColoring(project).SubjectDescription;
            else
                model.GroupDescription = ImageCollectionPromptProfileService.Load(project).SubjectDescription;
        }
        else
        {
            try { model = JsonSerializer.Deserialize<MultiSubjectProfile>(entity.Notes, JsonOptions) ?? new MultiSubjectProfile(); }
            catch { model = new MultiSubjectProfile(); }
        }

        Normalize(model);
        return model;
    }

    public static void Save(PreviewProject project, MultiSubjectProfile model)
    {
        Normalize(model);
        var entity = project.Entities.FirstOrDefault(e => string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
        {
            entity = new GraphEntity { Kind = EntityKind, Name = "Soggetti / personaggi strutturati", IsCandidate = false };
            project.Entities.Add(entity);
        }
        entity.IsCandidate = false;
        entity.Notes = JsonSerializer.Serialize(model, JsonOptions);
    }

    public static IReadOnlyList<MultiSubjectDefinition> ActiveSubjects(MultiSubjectProfile model) =>
        model.Subjects.Where(x => !x.Archived && x.Included).Take(MaxSubjects).ToList();

    public static MultiSubjectDefinition? ActiveSubject(MultiSubjectProfile model)
    {
        var active = ActiveSubjects(model);
        return active.FirstOrDefault(x => string.Equals(x.SubjectId, model.ActiveSubjectId, StringComparison.OrdinalIgnoreCase))
               ?? active.FirstOrDefault();
    }

    public static void SetCount(MultiSubjectProfile model, int requested)
    {
        requested = Math.Clamp(requested, 1, MaxSubjects);
        model.RequestedCount = requested;
        var available = model.Subjects.Where(x => !x.Archived).ToList();
        while (available.Count < requested)
        {
            var created = NewSubject(model.Subjects.Count + 1);
            model.Subjects.Add(created);
            available.Add(created);
        }

        for (var i = 0; i < available.Count; i++)
            available[i].Included = i < requested;

        var active = ActiveSubjects(model);
        if (active.Count > 0 && active.All(x => !string.Equals(x.SubjectId, model.ActiveSubjectId, StringComparison.OrdinalIgnoreCase)))
            model.ActiveSubjectId = active[0].SubjectId;
    }

    public static MultiSubjectDefinition Add(MultiSubjectProfile model)
    {
        var active = ActiveSubjects(model);
        if (active.Count >= MaxSubjects) return active[^1];
        var reusable = model.Subjects.FirstOrDefault(x => !x.Archived && !x.Included);
        if (reusable is not null)
        {
            reusable.Included = true;
            model.RequestedCount = ActiveSubjects(model).Count;
            model.ActiveSubjectId = reusable.SubjectId;
            return reusable;
        }
        var created = NewSubject(model.Subjects.Count + 1);
        model.Subjects.Add(created);
        model.RequestedCount = ActiveSubjects(model).Count;
        model.ActiveSubjectId = created.SubjectId;
        return created;
    }

    public static void RemoveFromActiveCast(MultiSubjectProfile model, string? subjectId)
    {
        var active = ActiveSubjects(model);
        if (active.Count <= 1) return;
        var target = active.FirstOrDefault(x => string.Equals(x.SubjectId, subjectId, StringComparison.OrdinalIgnoreCase));
        if (target is null) return;
        target.Included = false; // Keep the stable ID/history; this is not destructive deletion.
        model.RequestedCount = ActiveSubjects(model).Count;
        model.ActiveSubjectId = ActiveSubjects(model).First().SubjectId;
    }

    public static bool TryRename(MultiSubjectProfile model, MultiSubjectDefinition subject, string? name, out string error)
    {
        var clean = (name ?? string.Empty).Trim();
        if (clean.Length == 0)
        {
            error = "Il nome del soggetto/personaggio non può essere vuoto.";
            return false;
        }
        if (model.Subjects.Any(x => !x.Archived && !ReferenceEquals(x, subject) && string.Equals(x.Name.Trim(), clean, StringComparison.OrdinalIgnoreCase)))
        {
            error = "Esiste già un soggetto/personaggio con questo nome.";
            return false;
        }
        subject.Name = clean;
        error = string.Empty;
        return true;
    }

    public static MultiSubjectDefinition SubjectForItem(PreviewProject project, int itemIndex)
    {
        var model = Load(project);
        var active = ActiveSubjects(model);
        if (!model.Enabled || active.Count == 0)
            throw new InvalidOperationException("Multi-soggetto non attivo o senza soggetti disponibili.");
        return active[(Math.Max(1, itemIndex) - 1) % active.Count];
    }

    public static string BuildConsistencyRules(MultiSubjectDefinition subject)
    {
        EnsureConsistencyDefaults(subject);
        var sb = new StringBuilder();
        sb.AppendLine($"Subject identity [{subject.Name}] — LOCKED: preserve the same recognizable identity, core physical traits and silhouette across appearances.");
        Append(sb, "Physical appearance / distinguishing traits", subject.Consistency["identity"]);
        Append(sb, "Outfit / accessories", subject.Consistency["outfit"]);
        Append(sb, "Expression", subject.Consistency["expression"]);
        Append(sb, "Pose / action", subject.Consistency["action"]);
        Append(sb, "Framing / viewpoint", subject.Consistency["framing"]);
        Append(sb, "Participation in scenes with other subjects", subject.Consistency["co_scene"]);
        return sb.ToString().Trim();
    }

    public static void EnsureConsistencyDefaults(MultiSubjectDefinition subject)
    {
        Ensure(subject, "identity", "LOCKED", "USER");
        Ensure(subject, "outfit", "PREFERRED", "USER");
        Ensure(subject, "expression", "PREFERRED", "AI");
        Ensure(subject, "action", "FREE", "AI");
        Ensure(subject, "framing", "FREE", "AI");
        Ensure(subject, "co_scene", "FREE", "AI");
    }

    private static void Ensure(MultiSubjectDefinition subject, string key, string level, string strategy)
    {
        if (!subject.Consistency.TryGetValue(key, out var rule))
        {
            subject.Consistency[key] = new SubjectConsistencyRule { Level = level, Strategy = strategy };
            return;
        }
        rule.Level = NormalizeLevel(rule.Level, level);
        rule.Strategy = NormalizeStrategy(rule.Strategy, strategy);
    }

    private static void Append(StringBuilder sb, string label, SubjectConsistencyRule rule)
    {
        var level = NormalizeLevel(rule.Level, "PREFERRED");
        if (level == "FREE")
        {
            var strategy = NormalizeStrategy(rule.Strategy, "AI");
            var variation = (rule.Variation ?? string.Empty).Trim();
            sb.Append($"{label} — FREE — decision owner: {strategy}");
            if (variation.Length > 0) sb.Append(" — guidance: ").Append(variation);
            sb.AppendLine();
            return;
        }
        sb.AppendLine($"{label} — {level}");
    }

    private static void Normalize(MultiSubjectProfile model)
    {
        model.Subjects ??= [];
        model.RequestedCount = Math.Clamp(model.RequestedCount <= 0 ? 1 : model.RequestedCount, 1, MaxSubjects);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var subject in model.Subjects)
        {
            if (string.IsNullOrWhiteSpace(subject.SubjectId) || !ids.Add(subject.SubjectId))
                subject.SubjectId = Guid.NewGuid().ToString("D");
            ids.Add(subject.SubjectId);
            subject.Name = string.IsNullOrWhiteSpace(subject.Name) ? $"Soggetto {model.Subjects.IndexOf(subject) + 1}" : subject.Name.Trim();
            subject.Description ??= string.Empty;
            subject.Consistency ??= new Dictionary<string, SubjectConsistencyRule>(StringComparer.OrdinalIgnoreCase);
            EnsureConsistencyDefaults(subject);
        }
        if (model.Enabled)
            SetCount(model, model.RequestedCount);
    }

    private static MultiSubjectDefinition NewSubject(int number)
    {
        var subject = new MultiSubjectDefinition { Name = $"Soggetto {Math.Max(1, number)}" };
        EnsureConsistencyDefaults(subject);
        return subject;
    }

    private static string NormalizeLevel(string? value, string fallback) => (value ?? string.Empty).Trim().ToUpperInvariant() switch
    {
        "LOCKED" => "LOCKED",
        "PREFERRED" => "PREFERRED",
        "FREE" => "FREE",
        _ => fallback
    };

    private static string NormalizeStrategy(string? value, string fallback) => (value ?? string.Empty).Trim().ToUpperInvariant() switch
    {
        "USER" => "USER",
        "AI" => "AI",
        "MIXED" => "MIXED",
        _ => fallback
    };
}
