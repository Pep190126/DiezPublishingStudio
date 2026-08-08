using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DiezPublishingStudio;

internal static class RevisionCandidateService
{
    private static readonly RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    public static RevisionCandidateResult CreateForIssue(PreviewProject project, Guid issueId)
    {
        project.RevisionCandidates ??= [];
        var issue = project.ConsistencyIssues.FirstOrDefault(i => i.IssueId == issueId);
        if (issue is null) return new RevisionCandidateResult(null, "Problema di coerenza non trovato.");
        if (!issue.SubjectEntityId.HasValue) return new RevisionCandidateResult(null, "Questo problema non è collegato a un'entità revisionabile.");

        var existing = project.RevisionCandidates
            .Where(c => c.IssueSignature == issue.Signature && c.Status is "Proposed" or "Approved")
            .OrderByDescending(c => c.CreatedAtLocal, StringComparer.Ordinal)
            .FirstOrDefault();
        if (existing is not null)
            return new RevisionCandidateResult(existing, "Esiste già una proposta attiva per questo problema.");

        var entity = project.Entities.FirstOrDefault(e => e.EntityId == issue.SubjectEntityId.Value);
        if (entity is null) return new RevisionCandidateResult(null, "Entità collegata non trovata.");

        var facts = project.ConsistencyFacts
            .Where(f => f.SubjectEntityId == entity.EntityId && string.Equals(f.Key, issue.Key, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (facts.Count == 0) return new RevisionCandidateResult(null, "Non ci sono fatti testuali sufficienti per preparare una proposta.");

        var binding = project.BibleEntries.FirstOrDefault(b =>
            b.SubjectEntityId == entity.EntityId && b.IsActive &&
            string.Equals(b.Authority, "Binding", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(b.Key, issue.Key, StringComparison.OrdinalIgnoreCase));

        var orderedFacts = facts
            .OrderBy(f => ContentOrder(project, f.ContentId))
            .ThenBy(f => f.SourceLocator, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var desiredValue = binding?.Value?.Trim();
        var source = binding is not null ? "Bible vincolante" : "prima occorrenza editoriale";
        if (string.IsNullOrWhiteSpace(desiredValue)) desiredValue = orderedFacts[0].Value.Trim();

        var targetFact = orderedFacts
            .Where(f => !SameValue(f.Value, desiredValue))
            .OrderByDescending(f => ContentOrder(project, f.ContentId))
            .FirstOrDefault();
        if (targetFact is null)
            return new RevisionCandidateResult(null, "Non è stata trovata un'occorrenza divergente da correggere.");

        var targetNode = project.ContentNodes.FirstOrDefault(n => n.ContentId == targetFact.ContentId);
        if (targetNode is null) return new RevisionCandidateResult(null, "Contenuto sorgente della contraddizione non trovato.");

        var proposedBody = ReplaceFactValue(targetNode.Body, entity.Name, issue.Key, targetFact.Value, desiredValue);
        if (string.Equals(proposedBody, targetNode.Body, StringComparison.Ordinal))
            return new RevisionCandidateResult(null, "Diez non riesce a preparare una modifica testuale sicura per questa formulazione.");

        var candidate = new RevisionCandidate
        {
            CandidateId = Guid.NewGuid(),
            IssueId = issue.IssueId,
            IssueSignature = issue.Signature,
            SubjectEntityId = entity.EntityId,
            ContentId = targetNode.ContentId,
            Key = issue.Key,
            OriginalValue = targetFact.Value,
            ProposedValue = desiredValue,
            OriginalBody = targetNode.Body,
            ProposedBody = proposedBody,
            BaseContentSha256 = Hash(targetNode.Body),
            Rationale = $"Proposta automatica non applicata: allineare {DescribeKey(issue.Key)} da '{targetFact.Value}' a '{desiredValue}' usando come riferimento {source}.",
            Status = "Proposed",
            CreatedAtLocal = DateTimeOffset.Now.ToString("O")
        };
        project.RevisionCandidates.Add(candidate);
        return new RevisionCandidateResult(candidate, "Proposta preparata. Il manoscritto non è stato modificato.");
    }

    public static bool Approve(PreviewProject project, Guid candidateId)
    {
        var candidate = project.RevisionCandidates.FirstOrDefault(c => c.CandidateId == candidateId);
        if (candidate is null || candidate.Status != "Proposed") return false;
        candidate.Status = "Approved";
        candidate.ApprovedAtLocal = DateTimeOffset.Now.ToString("O");
        return true;
    }

    public static bool Reject(PreviewProject project, Guid candidateId)
    {
        var candidate = project.RevisionCandidates.FirstOrDefault(c => c.CandidateId == candidateId);
        if (candidate is null || candidate.Status is "Applied" or "Rejected") return false;
        candidate.Status = "Rejected";
        candidate.RejectedAtLocal = DateTimeOffset.Now.ToString("O");
        return true;
    }

    public static RevisionApplyResult ApplyApproved(PreviewProject project, Guid candidateId)
    {
        var candidate = project.RevisionCandidates.FirstOrDefault(c => c.CandidateId == candidateId);
        if (candidate is null) return new RevisionApplyResult(false, "Proposta non trovata.");
        if (candidate.Status != "Approved") return new RevisionApplyResult(false, "La proposta deve essere approvata prima di poter modificare il contenuto.");

        var node = project.ContentNodes.FirstOrDefault(n => n.ContentId == candidate.ContentId);
        if (node is null) return new RevisionApplyResult(false, "Il contenuto da revisionare non esiste più.");
        if (!string.Equals(Hash(node.Body), candidate.BaseContentSha256, StringComparison.Ordinal))
            return new RevisionApplyResult(false, "Il contenuto è cambiato dopo la creazione della proposta. Crea una nuova proposta per evitare sovrascritture.");

        node.Body = candidate.ProposedBody;
        candidate.Status = "Applied";
        candidate.AppliedAtLocal = DateTimeOffset.Now.ToString("O");
        ConsistencyEngine.Rebuild(project);
        return new RevisionApplyResult(true, "Proposta applicata al contenuto editoriale. La sorgente importata incorporata nel .diez non è stata alterata.");
    }

    private static int ContentOrder(PreviewProject project, Guid contentId)
    {
        var node = project.ContentNodes.FirstOrDefault(n => n.ContentId == contentId);
        if (node is null) return int.MaxValue;
        var materialIndex = project.Materials.FindIndex(m => m.MaterialId == node.MaterialId);
        if (materialIndex < 0) materialIndex = 0;
        return checked(materialIndex * 1_000_000 + Math.Max(0, node.Ordinal));
    }

    private static string ReplaceFactValue(string body, string entityName, string key, string oldValue, string newValue)
    {
        if (string.IsNullOrWhiteSpace(body)) return body;
        var name = Regex.Escape(entityName);
        var old = Regex.Escape(oldValue.Trim());
        string pattern = key switch
        {
            "age" => $@"(?<prefix>(?<![\p{{L}}\p{{N}}]){name}(?![\p{{L}}\p{{N}}])\s+(?:ha|aveva)\s+)(?<value>{old})(?<suffix>\s+anni\b)",
            "eye_color" => $@"(?<prefix>(?<![\p{{L}}\p{{N}}]){name}(?![\p{{L}}\p{{N}}])\s+(?:ha|aveva)\s+gli\s+occhi\s+)(?<value>{old})(?<suffix>\b)",
            "hair_color" => $@"(?<prefix>(?<![\p{{L}}\p{{N}}]){name}(?![\p{{L}}\p{{N}}])\s+(?:ha|aveva)\s+i\s+capelli\s+)(?<value>{old})(?<suffix>\b)",
            "birth_place" => $@"(?<prefix>(?<![\p{{L}}\p{{N}}]){name}(?![\p{{L}}\p{{N}}])\s+(?:è|era)\s+nat[oa]\s+a\s+)(?<value>{old})(?<suffix>(?![\p{{L}}\p{{N}}]))",
            "residence" => $@"(?<prefix>(?<![\p{{L}}\p{{N}}]){name}(?![\p{{L}}\p{{N}}])\s+(?:vive|viveva|abita|abitava)\s+(?:a|al|alla|nel|nella)\s+)(?<value>{old})(?<suffix>(?![\p{{L}}\p{{N}}]))",
            _ => string.Empty
        };
        if (pattern.Length == 0) return body;
        var regex = new Regex(pattern, Options);
        return regex.Replace(body, m => m.Groups["prefix"].Value + newValue + m.Groups["suffix"].Value, 1);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)));

    private static bool SameValue(string? left, string? right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);

    private static string Normalize(string? value) =>
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

internal readonly record struct RevisionCandidateResult(RevisionCandidate? Candidate, string Message);
internal readonly record struct RevisionApplyResult(bool Applied, string Message);
