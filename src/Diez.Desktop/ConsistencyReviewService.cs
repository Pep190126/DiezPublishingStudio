namespace DiezPublishingStudio;

internal static class ConsistencyReviewService
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Open", "Reviewed", "AcceptedException", "Resolved"
    };

    public static bool MarkReviewed(PreviewProject project, Guid issueId, string? note = null) =>
        ChangeStatus(project, issueId, "Reviewed", "Reviewed", note);

    public static bool AcceptException(PreviewProject project, Guid issueId, string? note = null) =>
        ChangeStatus(project, issueId, "AcceptedException", "AcceptException", note);

    public static bool MarkResolved(PreviewProject project, Guid issueId, string? note = null) =>
        ChangeStatus(project, issueId, "Resolved", "Resolve", note);

    public static bool Reopen(PreviewProject project, Guid issueId, string? note = null) =>
        ChangeStatus(project, issueId, "Open", "Reopen", note);

    private static bool ChangeStatus(PreviewProject project, Guid issueId, string newStatus, string action, string? note)
    {
        if (!AllowedStatuses.Contains(newStatus)) return false;
        project.ConsistencyIssues ??= [];
        project.ConsistencyResolutions ??= [];

        var issue = project.ConsistencyIssues.FirstOrDefault(i => i.IssueId == issueId);
        if (issue is null) return false;

        var previous = string.IsNullOrWhiteSpace(issue.Status) ? "Open" : issue.Status;
        if (string.Equals(previous, newStatus, StringComparison.OrdinalIgnoreCase)) return true;

        issue.Status = newStatus;
        project.ConsistencyResolutions.Add(new ConsistencyResolution
        {
            ResolutionId = Guid.NewGuid(),
            IssueId = issue.IssueId,
            IssueSignature = issue.Signature,
            PreviousStatus = previous,
            NewStatus = newStatus,
            Action = action,
            Note = note?.Trim() ?? string.Empty,
            CreatedAtLocal = DateTimeOffset.Now.ToString("O")
        });

        ConsistencyEngine.RefreshAnnotations(project);
        return true;
    }
}
