from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def replace(path: str, old: str, new: str, count: int = 1):
    p = ROOT / path
    text = p.read_text(encoding="utf-8")
    actual = text.count(old)
    if actual < count:
        raise SystemExit(f"{path}: expected at least {count} occurrence(s), found {actual}: {old[:120]!r}")
    text = text.replace(old, new, count)
    p.write_text(text, encoding="utf-8")


# 1) Visual compiler: never delegate aggregate subject choice to the image renderer.
visual = "src/Diez.Core/DiezVisualHardPromptFrontendBridge.cs"
replace(
    visual,
    """        if (participants.Length > 0 &&\n            (focal is null || participants.All(x => !string.Equals(x.SubjectId, focal.SubjectId, StringComparison.OrdinalIgnoreCase))))\n            focal = participants[0];\n\n        var subject = focal?.Name?.Trim();\n""",
    """        if (participants.Length > 0 &&\n            (focal is null || participants.All(x => !string.Equals(x.SubjectId, focal.SubjectId, StringComparison.OrdinalIgnoreCase))))\n            focal = participants[0];\n\n        VisualSemanticResolutionGuard.EnsureResolvedForPosition(\n            project, request, position, item?.Subject, focal, participants);\n\n        var subject = focal?.Name?.Trim();\n""",
)
replace(
    visual,
    """        var seriesSubject = (request.Subject ?? string.Empty).Trim();\n        var aggregateSeriesSubject = !hasAtomicSubject && IsAggregateSeriesSubject(seriesSubject, request.SeriesCount);\n        if (!hasAtomicSubject && aggregateSeriesSubject)\n            subject = AtomicSubjectFromSeriesTheme(seriesSubject);\n        else if (!hasAtomicSubject)\n            subject = seriesSubject;\n        if (string.IsNullOrWhiteSpace(subject)) subject = \"the requested focal subject\";\n""",
    """        var seriesSubject = (request.Subject ?? string.Empty).Trim();\n        if (!hasAtomicSubject) subject = seriesSubject;\n        if (string.IsNullOrWhiteSpace(subject)) subject = \"the requested focal subject\";\n""",
)
start = """        if (aggregateSeriesSubject)\n        {\n            sb.AppendLine($\"SERIES SUBJECT ASSIGNMENT — HARD: the user phrase '{seriesSubject}' describes the SERIES, not the contents of this single canvas. This is item {position} of {request.SeriesCount}. Assign exactly ONE concrete, specific, immediately recognizable subject to this Work Unit before rendering. Across the batch, use a different concrete subject for each sibling Work Unit unless the user explicitly requests repetition. Never draw the numeric series count, multiple alternative subjects, a contact sheet or several sibling illustrations on this canvas.\");\n        }\n"""
replace(visual, start, "")

# 2) Provider-facing PROMPT.md: English execution contract and resolved-subject invariant.
batch = ROOT / "src/Diez.Core/DiezPromptPackBatchFrontendBridge.cs"
text = batch.read_text(encoding="utf-8")
method_start = text.index("    public static string BuildPackagePrompt(")
method_end = text.index("    public static async Task<DiezPromptPackBuildResult> BuildManualPackageAsync", method_start)
new_method = r'''    public static string BuildPackagePrompt(string projectJson, IEnumerable<Guid>? workUnitIds = null)
    {
        var items = DiezPromptPackFrontendBridge.Preview(projectJson, workUnitIds)
            .Where(x => string.Equals(x.ContentType, "Image", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (items.Count == 0)
            throw new InvalidOperationException("Non ci sono Prompt immagine pronti per il Prompt Pack.");

        var expectedResponse = ExpectedResponseFileName(projectJson);
        var publisherMaterials = PublisherMaterials(projectJson);
        var sb = new StringBuilder();
        sb.AppendLine("# DIEZ ∞ PUBLISHING STUDIO — IMAGE PROMPT PACK");
        sb.AppendLine();
        sb.AppendLine("This ZIP is the complete execution package. Process the whole batch in this session; do not ask the publisher to paste each Work Unit separately.");
        sb.AppendLine($"The batch contains EXACTLY {items.Count} separate image assets, one final asset per Work Unit, in manifest order.");
        if (!string.IsNullOrWhiteSpace(expectedResponse))
            sb.AppendLine($"Required final Response ZIP filename: `{expectedResponse}`.");
        sb.AppendLine();
        sb.AppendLine("## Batch execution contract");
        sb.AppendLine("1. Each DIEZ VISUAL PROMPT is already semantically frozen by Diez. Render it independently; do not reinterpret the batch as one canvas.");
        sb.AppendLine("2. Produce exactly ONE final image per Work Unit. Never create a collage, grid, contact sheet, split panel, multiple alternatives, or multiple sibling illustrations in one asset.");
        sb.AppendLine("3. SUBJECT PLANNING IS ALREADY DONE BY DIEZ. The concrete primary subject in each Work Unit is authoritative. Do not choose, replace, broaden, rotate, or invent a different batch subject. If a Work Unit still appears to contain an unresolved aggregate subject, STOP that Work Unit and return FAILED/INCOMPLETE instead of deciding it yourself.");
        sb.AppendLine("4. SCENE PLANNING IS ALREADY DONE BY DIEZ when scenes are required. Keep the Work Unit scene and participants exactly as frozen; do not invent a different scenario merely to diversify the series.");
        sb.AppendLine("5. Preserve only the Consistent locks explicitly present in the Work Unit. Do not carry pose, props, framing, or scene details from sibling images unless a lock requires it.");
        sb.AppendLine("6. RENDERING METHOD — HARD: use a real native generative IMAGE model/tool. Python, Pillow, SVG, Canvas, plotting, programmatic vector primitives, geometric assembly, diagrams, or coded drawing fallbacks are forbidden. If native image generation is unavailable, return FAILED/INCOMPLETE; never fabricate a surrogate.");
        sb.AppendLine("7. QUALITY GATE — HARD: reject and regenerate drafts, scribbles, geometric primitive assemblies, placeholders, icon sheets, diagrams, malformed anatomy/structure, unrelated imagery, or work that is not commercially publishable for the requested book type.");
        sb.AppendLine("8. `prompt-manifest.json` is the authoritative transport contract. Technical identifiers belong only in the Response manifest; never render them into artwork or inject them into the image-model visual brief.");
        sb.AppendLine("9. RESPONSE HEADER — HARD: copy `project_id`, `job_id`, `prompt_pack_id`, and `request_snapshot_id` EXACTLY from `prompt-manifest.json`; set `transport` to `MANUAL`. These fields are mandatory even when every Work Unit is FAILED/INCOMPLETE. Never omit or regenerate them.");
        sb.AppendLine("10. For every result, preserve the exact `work_unit_id` and `target_candidate_version` from the corresponding manifest Work Unit. A FAILED/INCOMPLETE result has no fabricated primary asset.");
        sb.AppendLine("11. Files under `inputs/` may be used only according to the structured role/policy/fidelity declared in the manifest. Do not infer broader permission.");
        sb.AppendLine("12. Results return to Diez as Candidates or incomplete provider attempts. Do not approve or apply anything to the book; Vision/review and `Porta nel libro` remain separate Diez actions.");
        sb.AppendLine(string.IsNullOrWhiteSpace(expectedResponse)
            ? "13. Return ONE `diez-response` v1 ZIP containing one result record per Work Unit."
            : $"13. Return ONE `diez-response` v1 ZIP named EXACTLY `{expectedResponse}`, containing one result record per Work Unit.");
        sb.AppendLine();

        if (publisherMaterials.Count > 0)
        {
            sb.AppendLine("## Publisher materials");
            sb.AppendLine("The following files were intentionally included. Their machine-readable intent code, AI-use policy and fidelity are authoritative; do not expand their role.");
            sb.AppendLine();
            foreach (var material in publisherMaterials)
            {
                sb.AppendLine($"- `{material.FileName}` — intent `{material.IntentCode}`, policy `{material.AiUsePolicy}`, fidelity `{material.Fidelity}`.");
            }
            sb.AppendLine();
        }

        for (var i = 0; i < items.Count; i++)
        {
            sb.AppendLine($"## Image {i + 1:D3} of {items.Count:D3}");
            sb.AppendLine("<<< DIEZ VISUAL PROMPT START >>>");
            sb.AppendLine(items[i].Prompt.Trim());
            sb.AppendLine("<<< DIEZ VISUAL PROMPT END >>>");
            sb.AppendLine();
        }

        sb.AppendLine("## Delivery check");
        sb.AppendLine($"Return exactly {items.Count} result records, one per Work Unit. No Diez ID, filename, watermark, prompt fragment, or protocol label may appear inside generated artwork.");
        if (!string.IsNullOrWhiteSpace(expectedResponse))
            sb.AppendLine($"The final ZIP filename is `{expectedResponse}` unless the platform technically prevents renaming; even then, preserve the complete Diez manifest identity exactly.");
        sb.AppendLine("If the platform demonstrably contaminates a render with previous images, use the historical clean-room fallback; this is an execution fallback, never permission to change the frozen Work Unit semantics.");
        return sb.ToString().Trim();
    }

'''
text = text[:method_start] + new_method + text[method_end:]
batch.write_text(text, encoding="utf-8")

# 3) Manual instructions: provider-facing English and mandatory response identity.
front = ROOT / "src/Diez.Core/DiezPromptPackFrontendBridge.cs"
text = front.read_text(encoding="utf-8")
old_start = text.index("    private static string ManualInstructions(int publisherMaterialCount) =>")
old_end = text.index("    private static string EnsureZip", old_start)
new_instr = r'''    private static string ManualInstructions(int publisherMaterialCount) => $"""
# Diez ∞ Publishing Studio — manual Prompt Pack

This ZIP is the official manual transport boundary between Diez and the selected AI system.

1. Read `prompt-manifest.json` first; it is the authoritative transport contract.
2. Execute every `work_unit` using exactly its `instruction` as the provider-facing content instruction.
3. Work Unit semantics are already frozen by Diez. Do not perform unresolved subject/scene planning inside the image renderer.
4. IDs, codes, hashes and version numbers are transport metadata only. Never render them into generated content.
5. Use files under `inputs/` only for the roles explicitly declared by the manifest. Publisher materials included in this package: {publisherMaterialCount}.
6. For image results, return a faithful factual description of the actual pixels; the description is not evidence of visual compliance.
7. Do not approve anything implicitly. Results return to Diez as Candidate versions or incomplete provider attempts and remain subject to the appropriate review.
8. Manual and API transports must address the same canonical Work Units. Transport changes must not change editorial semantics.
9. RESPONSE HEADER — mandatory: copy `project_id`, `job_id`, `prompt_pack_id`, and `request_snapshot_id` exactly from `prompt-manifest.json`; set `transport` to `MANUAL`. Never omit or regenerate these identifiers, including all-FAILED/all-INCOMPLETE responses.
10. Each result must copy the exact `work_unit_id` and `target_candidate_version` for that Work Unit. Use `status` COMPLETE/CANDIDATE only when a real primary asset exists; use FAILED or INCOMPLETE without fabricating an asset when generation cannot satisfy the contract.

Expected response protocol: `diez-response` v1 with the mandatory header above and one result per Work Unit containing `work_unit_id`, `candidate_version`, `content_type`, `status`, optional `primary_asset`, `description`, and `failure_reason` when applicable.
""";

'''
text = text[:old_start] + new_instr + text[old_end:]
front.write_text(text, encoding="utf-8")

# 4) Response parser: request_snapshot_id + transport are mandatory and must match.
response = "src/Diez.Core/DiezVisualResponsePackFrontendBridge.cs"
replace(
    response,
    """            if (manifest.JobId == Guid.Empty || manifest.PromptPackId == Guid.Empty)\n                return Failure(\"HEADER_INCOMPLETE\", \"Job o Prompt Pack non identificabili nel Response ZIP.\");\n\n            var packageId = manifest.PackageId;\n""",
    """            if (manifest.JobId == Guid.Empty || manifest.PromptPackId == Guid.Empty || manifest.RequestSnapshotId == Guid.Empty)\n                return Failure(\"HEADER_INCOMPLETE\", \"Job, Prompt Pack o request snapshot non identificabili nel Response ZIP.\");\n            if (!string.Equals(manifest.Transport, \"MANUAL\", StringComparison.OrdinalIgnoreCase))\n                return Failure(\"TRANSPORT_MISMATCH\", \"Il Response manuale deve dichiarare transport=MANUAL.\");\n\n            var packageId = manifest.PackageId;\n""",
)
replace(
    response,
    """            if (pack is null || snapshot is null || snapshot.JobId != manifest.JobId)\n                return Failure(\"PROMPT_PACK_MISMATCH\", \"Prompt Pack, snapshot o Job non corrispondono allo stato del progetto aperto.\");\n\n            var result = new List<DiezVisualResponsePackItem>();\n""",
    """            if (pack is null || snapshot is null || snapshot.JobId != manifest.JobId)\n                return Failure(\"PROMPT_PACK_MISMATCH\", \"Prompt Pack, snapshot o Job non corrispondono allo stato del progetto aperto.\");\n            if (snapshot.SnapshotId != manifest.RequestSnapshotId)\n                return Failure(\"REQUEST_SNAPSHOT_MISMATCH\", \"request_snapshot_id del Response non corrisponde allo snapshot congelato dal Prompt Pack.\");\n\n            var result = new List<DiezVisualResponsePackItem>();\n""",
)
replace(
    response,
    """                var failed = string.Equals(normalizedStatus, \"FAILED\", StringComparison.Ordinal);\n""",
    """                var failed = string.Equals(normalizedStatus, \"FAILED\", StringComparison.Ordinal) ||\n                             string.Equals(normalizedStatus, \"INCOMPLETE\", StringComparison.Ordinal);\n""",
)
replace(
    response,
    """        return new NormalizedManifest(\n            ReadString(root, \"protocol\"),\n            ReadInt(root, \"protocol_version\"),\n            ReadGuid(root, \"project_id\"),\n            ReadGuid(root, \"job_id\"),\n            promptPackId,\n            ReadString(root, \"package_id\"),\n            ReadBool(root, \"partial\"),\n            items);\n""",
    """        return new NormalizedManifest(\n            ReadString(root, \"protocol\"),\n            ReadInt(root, \"protocol_version\"),\n            ReadGuid(root, \"project_id\"),\n            ReadGuid(root, \"job_id\"),\n            promptPackId,\n            ReadGuid(root, \"request_snapshot_id\"),\n            ReadString(root, \"transport\"),\n            ReadString(root, \"package_id\"),\n            ReadBool(root, \"partial\"),\n            items);\n""",
)
replace(
    response,
    """    private sealed record NormalizedManifest(\n        string Protocol,\n        int ProtocolVersion,\n        Guid ProjectId,\n        Guid JobId,\n        Guid PromptPackId,\n        string PackageId,\n        bool Partial,\n        IReadOnlyList<NormalizedItem> Items);\n""",
    """    private sealed record NormalizedManifest(\n        string Protocol,\n        int ProtocolVersion,\n        Guid ProjectId,\n        Guid JobId,\n        Guid PromptPackId,\n        Guid RequestSnapshotId,\n        string Transport,\n        string PackageId,\n        bool Partial,\n        IReadOnlyList<NormalizedItem> Items);\n""",
)

# 5) Uno importer: FAILED and INCOMPLETE are both assetless provider attempts.
adapter = "src/Diez.Uno/VisualBookDocumentAdapter.cs"
replace(
    adapter,
    """                if (string.Equals(item.Status, \"FAILED\", StringComparison.OrdinalIgnoreCase))\n""",
    """                if (string.Equals(item.Status, \"FAILED\", StringComparison.OrdinalIgnoreCase) ||\n                    string.Equals(item.Status, \"INCOMPLETE\", StringComparison.OrdinalIgnoreCase))\n""",
)

# 6) Candidate gate runs the new semantic regression.
workflow = ".github/workflows/uno-windows-candidate-trace.yml"
replace(
    workflow,
    """      - 'tests/Diez.VisualBook.Pianist/**'\n""",
    """      - 'tests/Diez.VisualBook.Pianist/**'\n      - 'tests/Diez.VisualSemanticRegression/**'\n""",
)
replace(
    workflow,
    """      - name: Visual book gate\n        id: gate\n        run: dotnet run --project tests/Diez.VisualBook.Pianist/Diez.VisualBook.Pianist.csproj -c Release\n""",
    """      - name: Visual book gate\n        id: gate\n        shell: powershell\n        run: |\n          dotnet run --project tests/Diez.VisualBook.Pianist/Diez.VisualBook.Pianist.csproj -c Release\n          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }\n          dotnet run --project tests/Diez.VisualSemanticRegression/Diez.VisualSemanticRegression.csproj -c Release\n          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }\n""",
)

print("ROUND53_PATCH_APPLIED")
