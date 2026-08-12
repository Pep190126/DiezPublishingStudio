using System.IO.Compression;
using System.Text;

namespace DiezPublishingStudio;

/// <summary>
/// Replaces the legacy mixed-language transport instructions with one English, provider-facing contract.
/// The project/UI may stay localized; the model receives one unambiguous operational language.
/// </summary>
internal static class PromptPackEnglishInstructionService
{
    private const string InstructionsName = "instructions.md";

    public static void Rewrite(string promptPackPath, PreviewProject project)
    {
        if (!File.Exists(promptPackPath)) return;
        var text = BuildText(project);
        using var archive = ZipFile.Open(promptPackPath, ZipArchiveMode.Update);
        archive.GetEntry(InstructionsName)?.Delete();
        var entry = archive.CreateEntry(InstructionsName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(text);
    }

    internal static string BuildText(PreviewProject project)
    {
        var settings = PromptPreparationSettingsStore.Load(project);
        return $"""
# Diez Publishing Studio — Prompt Pack v1

This package describes a Diez production job. Read `prompt-manifest.json` and use only the materials required for the current Work Units under `inputs/`.

## Modes
- `INPUT_ONLY`: use only the supplied content.
- `AI_ONLY`: create new content with AI.
- `INPUT_PLUS_AI`: combine supplied input with newly generated AI content.
- `INPUT_TRANSFORMED_BY_AI`: transform the supplied input.
- `AI_WITH_INPUT_AS_REFERENCE`: create new content using supplied input as a visual reference/paradigm.

## Essential transport rules
1. Preserve every `work_unit_id` and `candidate_version` exactly as assigned by Diez.
2. Shared Context / Consistent semantics are authoritative: `LOCKED` must be preserved, `PREFERRED` should be respected where possible, and `FREE` may vary.
3. Use each paradigm only for its declared roles.
4. For local edits, preserve everything that was not requested to change.
5. Every returned image must include a factual description that matches the actual final image. If that cannot be provided, return the item as `INCOMPLETE`.
6. You may return one or more ZIP packages, including partial packages. Never renumber or replace Diez IDs.
7. Every response ZIP must contain `response-manifest.json` and primary assets under `content/`.
8. Allowed item statuses are `COMPLETE`, `INCOMPLETE`, and `FAILED`.
9. Do not include executable code, scripts, macros, installers or active content. Return only data and requested content assets.
10. Do not duplicate original intake/paradigm files unless they are explicitly requested as output.

## Image-generation integrity — HARD
For image Work Units, use a genuine image-generation/illustration capability appropriate for publication-quality artwork. Do NOT substitute a crude programmatic drawing, primitive SVG/Canvas/Pillow geometry, placeholder icon, tracing sketch, or assembled circles/rectangles merely because that makes exact dimensions or black/white values easier to satisfy.

For Coloring Book work, create a professionally illustrated coloring page FIRST. If technical delivery requires exact dimensions, DPI metadata or pure binary black/white, apply technical normalization to the finished artwork afterward while preserving anatomy, curves, composition and line quality. Technical normalization must never become a geometric redraw of the illustration.

If the current environment cannot actually generate or edit an image at the required professional level, return `INCOMPLETE` or `FAILED` with a concise explanation. Never fabricate a low-effort placeholder and label it `COMPLETE`.

The publication-readiness gate in each `work_units[].instruction` is a HARD Book-Type requirement. Obvious rough-draft, scribble-like, placeholder, primitive-geometric or amateur execution is not acceptable even when dimensions, DPI and pure black/white raster checks are technically correct. Simple Preschool/Bold & Easy artwork is valid only when the simplicity is intentional, polished, balanced, expressive and professionally resolved.

## Minimum response manifest
```json
{{
  "protocol": "diez-response",
  "protocol_version": 1,
  "project_id": "<project id>",
  "job_id": "<job id>",
  "prompt_pack_id": "<prompt pack id>",
  "package_id": "<unique package id>",
  "partial": true,
  "items": [
    {{
      "work_unit_id": "<id>",
      "candidate_version": 1,
      "content_type": "IMAGE|TEXT|STRUCTURED_DATA|DOCUMENT",
      "status": "COMPLETE|INCOMPLETE|FAILED",
      "primary_asset": "content/file.ext",
      "description": "required factual description for image assets"
    }}
  ]
}}
```

Diez reconstructs packages automatically through stable IDs. The user must not have to rename or manually associate returned files.

## Diez Visual Context V3 — AUTHORITATIVE
1. Read `request-context.json` before generating or correcting any image.
2. Use only the visual profile belonging to the active Book Type declared by `active_profile_kind`; never infer or resurrect historical/inactive profiles.
3. Files under `inputs/intake/` are real user assets. Use the actual file together with its role and description; never reconstruct a supplied image from text alone.
4. During a correction/edit, `base_version.file` is the authoritative real base image. Modify that source unless `REGENERATE` is explicitly requested.
5. Resolve together: real base image + current description + relevant intake files/descriptions + paradigms/roles + preserve/change/add/remove + active profile + Consistent rules + current image specifications.
6. `preserve` means keep the named elements visually unchanged. In a local edit, unmentioned elements must also remain unchanged when the contract requires preservation.
7. After every edit, return an updated factual description matching the actual final image.

AUTHORITATIVE IMAGE RULE: user/current descriptions guide the work but never replace a real image file. For corrections, use `base_version.file` as visual authority and apply preserve/change/add/remove to that real asset.

## Diez Prompt Engineering — AUTHORITATIVE
- Semantic engine: {PromptEngineeringEngine.EngineVersion}; provider compiler: {PromptEngineeringCompiler.Version}.
- Active Book Type: {BookTypeProfileService.Get(project)}.
- Target renderer: {settings.ProviderId}.
- Only the active Book Type prompt profile may influence this request; historical/inactive profiles must be ignored.
- Provider-facing operational instructions use English. Localized UI/project metadata is secondary and must never override the authoritative Work Unit instruction.
- Every `work_units[].instruction` requests EXACTLY ONE image. `series_count` is context only and never authorizes a grid, collage, contact sheet or multiple alternatives.
- `output_count_for_this_work_unit = 1` is a hard execution contract.
- Current structured parameters and the current compiler baseline override stale generated prompt text. Only a genuine user-authored manual delta may be carried forward.
- Professional quality gates remain mandatory even when the GUI contains only a few optional parameters.
- For corrections, the actual base/input image plus preserve/change/add/remove are authoritative; descriptions assist but never replace image files.

## Completion check
Before marking an image `COMPLETE`, inspect the actual returned asset rather than the intention behind it. Confirm subject, item-specific constraints, Book-Type fit, professional illustration craft, composition, anatomy/geometry, prohibited content and the requested technical profile. If a HARD requirement is not satisfied, correct/regenerate the asset or return `INCOMPLETE/FAILED`; never describe a visibly non-compliant asset as compliant.
""".Trim();
    }
}
