using System.Text.Json;

namespace DiezPublishingStudio;

/// <summary>
/// Keeps visual AI work isolated when the user changes Book Type inside the same .diez project.
/// Previous visual jobs are moved out of the active legacy job list and stored as session history;
/// materials/results remain in the project and no source file is deleted.
/// </summary>
internal static class VisualPromptSessionService
{
    private const string EntityKind = "DiezVisualPromptSessions";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    internal sealed class State
    {
        public int SchemaVersion { get; set; } = 2;
        public Guid ActiveSessionId { get; set; }
        public string ActiveBookType { get; set; } = string.Empty;
        public List<Session> Sessions { get; set; } = [];
    }

    internal sealed class Session
    {
        public Guid SessionId { get; set; } = Guid.NewGuid();
        public string BookType { get; set; } = string.Empty;
        public bool Archived { get; set; }
        public string CreatedAtLocal { get; set; } = string.Empty;
        public string ArchivedAtLocal { get; set; } = string.Empty;
        public List<Guid> LegacyAiJobIds { get; set; } = [];
        public List<ArchivedVisualJob> ArchivedJobs { get; set; } = [];
    }

    internal sealed class ArchivedVisualJob
    {
        public Guid JobId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string OutputType { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Request { get; set; } = string.Empty;
        public string Prompt { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string ResultText { get; set; } = string.Empty;
        public Guid? ResultMaterialId { get; set; }
        public Guid? TargetContentId { get; set; }
        public string CreatedAtLocal { get; set; } = string.Empty;
        public string UpdatedAtLocal { get; set; } = string.Empty;
    }

    public static void OnBookTypeChanging(PreviewProject project, string? previousBookType, string? nextBookType)
    {
        var previous = BookTypeProfileService.Normalize(previousBookType);
        var next = BookTypeProfileService.Normalize(nextBookType);
        if (string.Equals(previous, next, StringComparison.OrdinalIgnoreCase)) return;

        var state = Load(project);
        var current = ActiveSession(state);
        if (current is null)
        {
            current = new Session
            {
                SessionId = Guid.NewGuid(),
                BookType = previous,
                Archived = false,
                CreatedAtLocal = DateTimeOffset.Now.ToString("O")
            };
            foreach (var job in project.AiProductionJobs.Where(IsImageJob))
                current.LegacyAiJobIds.Add(job.JobId);
            state.Sessions.Add(current);
        }

        ArchiveOperationalJobs(project, current);
        current.Archived = true;
        current.ArchivedAtLocal = DateTimeOffset.Now.ToString("O");

        var fresh = new Session
        {
            SessionId = Guid.NewGuid(),
            BookType = next,
            Archived = false,
            CreatedAtLocal = DateTimeOffset.Now.ToString("O")
        };
        state.Sessions.Add(fresh);
        state.ActiveSessionId = fresh.SessionId;
        state.ActiveBookType = next;
        Save(project, state);
    }

    public static Session EnsureActive(PreviewProject project)
    {
        var bookType = BookTypeProfileService.Get(project);
        var state = Load(project);
        var active = ActiveSession(state);

        if (active is null)
        {
            active = new Session
            {
                SessionId = Guid.NewGuid(),
                BookType = bookType,
                Archived = false,
                CreatedAtLocal = DateTimeOffset.Now.ToString("O")
            };
            // Backward compatibility: before sessions existed, current visual image jobs
            // belonged to the only active workflow. Adopt them exactly once.
            foreach (var job in project.AiProductionJobs.Where(IsImageJob))
                if (!active.LegacyAiJobIds.Contains(job.JobId)) active.LegacyAiJobIds.Add(job.JobId);
            state.Sessions.Add(active);
            state.ActiveSessionId = active.SessionId;
            state.ActiveBookType = bookType;
            Save(project, state);
            return active;
        }

        if (!string.Equals(active.BookType, bookType, StringComparison.OrdinalIgnoreCase))
        {
            ArchiveOperationalJobs(project, active);
            active.Archived = true;
            active.ArchivedAtLocal = DateTimeOffset.Now.ToString("O");
            active = new Session
            {
                SessionId = Guid.NewGuid(),
                BookType = bookType,
                Archived = false,
                CreatedAtLocal = DateTimeOffset.Now.ToString("O")
            };
            state.Sessions.Add(active);
            state.ActiveSessionId = active.SessionId;
            state.ActiveBookType = bookType;
            Save(project, state);
        }

        // Jobs created by the current legacy workflow after the session was opened are adopted.
        // Previous-session jobs cannot reappear here because they were removed from the operational list.
        var knownHistorical = state.Sessions.Where(s => s.SessionId != active.SessionId)
            .SelectMany(s => s.LegacyAiJobIds)
            .ToHashSet();
        var adopted = false;
        foreach (var job in project.AiProductionJobs.Where(IsImageJob))
        {
            if (knownHistorical.Contains(job.JobId) || active.LegacyAiJobIds.Contains(job.JobId)) continue;
            active.LegacyAiJobIds.Add(job.JobId);
            adopted = true;
        }
        if (adopted) Save(project, state);
        return active;
    }

    public static IReadOnlyList<AiProductionJob> ActiveImageJobs(PreviewProject project)
    {
        var session = EnsureActive(project);
        var ids = session.LegacyAiJobIds.ToHashSet();
        return project.AiProductionJobs
            .Where(j => IsImageJob(j) && ids.Contains(j.JobId))
            .OrderBy(j => j.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static HashSet<Guid> ActiveLegacyJobIds(PreviewProject project) =>
        ActiveImageJobs(project).Select(j => j.JobId).ToHashSet();

    public static void RegisterNewJobs(PreviewProject project, IEnumerable<Guid> jobIds)
    {
        var ids = jobIds.Distinct().ToList();
        if (ids.Count == 0) return;
        var state = Load(project);
        var active = ActiveSession(state) ?? EnsureActive(project);
        foreach (var id in ids)
            if (!active.LegacyAiJobIds.Contains(id)) active.LegacyAiJobIds.Add(id);
        Save(project, state);
    }

    public static bool IsActiveJob(PreviewProject project, Guid legacyJobId) =>
        EnsureActive(project).LegacyAiJobIds.Contains(legacyJobId);

    public static int ArchivedJobCount(PreviewProject project) =>
        Load(project).Sessions.Sum(s => s.ArchivedJobs.Count);

    private static void ArchiveOperationalJobs(PreviewProject project, Session session)
    {
        var ids = session.LegacyAiJobIds.Count > 0
            ? session.LegacyAiJobIds.ToHashSet()
            : project.AiProductionJobs.Where(IsImageJob).Select(j => j.JobId).ToHashSet();
        var jobs = project.AiProductionJobs.Where(j => IsImageJob(j) && ids.Contains(j.JobId)).ToList();
        foreach (var job in jobs)
        {
            if (!session.LegacyAiJobIds.Contains(job.JobId)) session.LegacyAiJobIds.Add(job.JobId);
            if (session.ArchivedJobs.All(x => x.JobId != job.JobId)) session.ArchivedJobs.Add(Snapshot(job));
            project.AiProductionJobs.Remove(job);
        }
    }

    private static ArchivedVisualJob Snapshot(AiProductionJob job) => new()
    {
        JobId = job.JobId,
        Code = job.Code,
        OutputType = job.OutputType,
        Title = job.Title,
        Request = job.Request,
        Prompt = job.Prompt,
        Status = job.Status,
        ResultText = job.ResultText,
        ResultMaterialId = job.ResultMaterialId,
        TargetContentId = job.TargetContentId,
        CreatedAtLocal = job.CreatedAtLocal,
        UpdatedAtLocal = job.UpdatedAtLocal
    };

    private static State Load(PreviewProject project)
    {
        var entity = project.Entities.FirstOrDefault(e =>
            string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null || string.IsNullOrWhiteSpace(entity.Notes)) return new State();
        try
        {
            var state = JsonSerializer.Deserialize<State>(entity.Notes, JsonOptions) ?? new State();
            state.SchemaVersion = Math.Max(2, state.SchemaVersion);
            state.Sessions ??= [];
            foreach (var session in state.Sessions)
            {
                if (session.SessionId == Guid.Empty) session.SessionId = Guid.NewGuid();
                session.BookType ??= string.Empty;
                session.LegacyAiJobIds ??= [];
                session.ArchivedJobs ??= [];
                session.CreatedAtLocal ??= string.Empty;
                session.ArchivedAtLocal ??= string.Empty;
            }
            return state;
        }
        catch { return new State(); }
    }

    private static void Save(PreviewProject project, State state)
    {
        state.SchemaVersion = 2;
        var entity = project.Entities.FirstOrDefault(e =>
            string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
        {
            entity = new GraphEntity
            {
                Kind = EntityKind,
                Name = "Sessioni prompt visuali",
                IsCandidate = false
            };
            project.Entities.Add(entity);
        }
        entity.IsCandidate = false;
        entity.Notes = JsonSerializer.Serialize(state, JsonOptions);
    }

    private static Session? ActiveSession(State state) =>
        state.ActiveSessionId == Guid.Empty
            ? null
            : state.Sessions.FirstOrDefault(s => s.SessionId == state.ActiveSessionId && !s.Archived);

    private static bool IsImageJob(AiProductionJob job) =>
        string.Equals(job.OutputType, AiProductionService.TypeImage, StringComparison.OrdinalIgnoreCase);
}
