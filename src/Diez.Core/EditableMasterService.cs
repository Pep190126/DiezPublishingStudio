using System.Security.Cryptography;
using System.Text;

namespace DiezPublishingStudio;

internal static class EditableMasterService
{
    private const string ManualEditKey = "manual_edit";

    public static bool CanEdit(PreviewProject project, ContentNode node)
    {
        if (string.Equals(node.Kind, "Chapter", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(node.Kind, "Section", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.Equals(node.Kind, "Document", StringComparison.OrdinalIgnoreCase)) return false;
        return !project.ContentNodes.Any(n => n.MaterialId == node.MaterialId && n.ContentId != node.ContentId &&
            (string.Equals(n.Kind, "Chapter", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(n.Kind, "Section", StringComparison.OrdinalIgnoreCase)));
    }

    public static MasterEditResult ApplyManualEdit(PreviewProject project, Guid contentId, string editedBody, string? note = null)
    {
        var node = project.ContentNodes.FirstOrDefault(n => n.ContentId == contentId);
        if (node is null) return new MasterEditResult(false, "Contenuto editoriale non trovato.");
        if (!CanEdit(project, node))
            return new MasterEditResult(false, "Questo nodo è strutturale. Modifica un capitolo o una sezione del Master.");

        editedBody ??= string.Empty;
        if (string.Equals(node.Body, editedBody, StringComparison.Ordinal))
            return new MasterEditResult(false, "Nessuna modifica da salvare.");

        var before = node.Body;
        var now = DateTimeOffset.Now.ToString("O");
        project.RevisionCandidates ??= [];

        foreach (var candidate in project.RevisionCandidates.Where(c =>
                     c.ContentId == node.ContentId && c.Key != ManualEditKey && c.Status is "Proposed" or "Approved"))
        {
            candidate.Status = "Rejected";
            candidate.RejectedAtLocal = now;
            candidate.Rationale = string.IsNullOrWhiteSpace(candidate.Rationale)
                ? "Proposta superata da una modifica manuale del Master."
                : candidate.Rationale + " Proposta superata da una modifica manuale del Master.";
        }

        node.Body = editedBody;
        project.RevisionCandidates.Add(new RevisionCandidate
        {
            CandidateId = Guid.NewGuid(),
            IssueId = Guid.Empty,
            IssueSignature = $"MASTER:{node.ContentId:N}:{Guid.NewGuid():N}",
            SubjectEntityId = Guid.Empty,
            ContentId = node.ContentId,
            Key = ManualEditKey,
            OriginalBody = before,
            ProposedBody = editedBody,
            BaseContentSha256 = Hash(before),
            Rationale = string.IsNullOrWhiteSpace(note) ? "Modifica manuale del Master editoriale." : note.Trim(),
            Status = "Applied",
            CreatedAtLocal = now,
            ApprovedAtLocal = now,
            AppliedAtLocal = now
        });

        ConsistencyEngine.Rebuild(project);
        return new MasterEditResult(true, "Modifica salvata nel Master. L'originale importato incorporato nel .diez resta invariato.");
    }

    public static MasterEditResult RestoreImportedSnapshot(PreviewProject project, Guid contentId)
    {
        var node = project.ContentNodes.FirstOrDefault(n => n.ContentId == contentId);
        if (node is null) return new MasterEditResult(false, "Contenuto editoriale non trovato.");
        if (!CanEdit(project, node))
            return new MasterEditResult(false, "Questo nodo strutturale non è ripristinabile direttamente.");

        var material = project.Materials.FirstOrDefault(m => m.MaterialId == node.MaterialId);
        if (material is null) return new MasterEditResult(false, "Materiale sorgente del contenuto non trovato.");
        if (string.IsNullOrWhiteSpace(material.ExtractedText))
            return new MasterEditResult(false, "Il materiale non contiene uno snapshot testuale da ripristinare.");

        var originalNodes = ContentStructureAnalyzer.Analyze(material);
        var original = originalNodes.FirstOrDefault(n =>
            string.Equals(n.Kind, node.Kind, StringComparison.OrdinalIgnoreCase) &&
            n.Ordinal == node.Ordinal &&
            string.Equals(n.SourceLocator, node.SourceLocator, StringComparison.OrdinalIgnoreCase));
        original ??= originalNodes.FirstOrDefault(n =>
            string.Equals(n.Kind, node.Kind, StringComparison.OrdinalIgnoreCase) && n.Ordinal == node.Ordinal);

        if (original is null)
            return new MasterEditResult(false, "Non riesco a ricostruire questo nodo dallo snapshot importato.");

        return ApplyManualEdit(project, contentId, original.Body, "Ripristino esplicito dallo snapshot importato incorporato nel progetto.");
    }

    public static int ManualRevisionCount(PreviewProject project, Guid contentId) =>
        project.RevisionCandidates.Count(c => c.ContentId == contentId && c.Key == ManualEditKey && c.Status == "Applied");

    public static IReadOnlyList<RevisionCandidate> ManualHistory(PreviewProject project, Guid contentId) =>
        project.RevisionCandidates
            .Where(c => c.ContentId == contentId && c.Key == ManualEditKey && c.Status == "Applied")
            .OrderBy(c => c.AppliedAtLocal, StringComparer.Ordinal)
            .ToList();

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)));
}

internal readonly record struct MasterEditResult(bool Changed, string Message);
