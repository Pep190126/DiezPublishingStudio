from pathlib import Path
import re


def read(path: str) -> str:
    return Path(path).read_text(encoding="utf-8")


def write(path: str, text: str) -> None:
    Path(path).write_text(text, encoding="utf-8")


def replace_once(text: str, old: str, new: str, label: str) -> str:
    if old not in text:
        raise SystemExit(f"Round 5.1 patch: expected block not found: {label}")
    return text.replace(old, new, 1)


# -----------------------------------------------------------------------------
# 1. One visual compiler for Phase 2 preview / ready jobs / Prompt Pack.
# -----------------------------------------------------------------------------
path = "src/Diez.Core/DiezVisualBookFrontendBridge.cs"
text = read(path)
text = text.replace("using System.Text;\n", "", 1)

text = replace_once(
    text,
    '''        var plan = VisualBookPlanService.Load(project);\n        var request = PromptEngineeringEngine.BuildRequest(\n            project,\n            plan.ImageCount,\n            mustDo,\n            mustNotDo,\n            providerId,\n            preferAdvancedModel);\n        var master = PromptEngineeringEngine.RenderSeries(request);\n        PromptMasterStateStore.SaveDraft(project, plan.ImageCount, mustDo, mustNotDo, master);\n''',
    '''        var plan = VisualBookPlanService.Load(project);\n        PromptPreparationSettingsStore.Save(project, new PromptPreparationSettings\n        {\n            ProviderId = providerId,\n            PreferAdvancedModel = preferAdvancedModel\n        });\n        var request = PromptEngineeringEngine.BuildRequest(\n            project,\n            plan.ImageCount,\n            mustDo,\n            mustNotDo,\n            providerId,\n            preferAdvancedModel);\n        var master = PromptEngineeringEngine.RenderSeries(request);\n        PromptMasterStateStore.SaveDraft(project, plan.ImageCount, mustDo, mustNotDo, master);\n''',
    "persist prompt preparation settings")

text = replace_once(
    text,
    '''            var code = $"IMG-{position:D3}";\n            var source = BuildAtomicVisualSource(project, request, position);\n            var prompt = PromptPackRendererVisualBriefService.Build(source);\n            items.Add(new DiezVisualPromptItem(position, code, $"Immagine {position:D3}", prompt));\n''',
    '''            var code = $"IMG-{position:D3}";\n            var unit = new AiExchangeWorkUnit\n            {\n                Position = position,\n                Code = code,\n                ContentType = AiExchangeContentTypes.Image\n            };\n            var source = VisualHardPromptContractCompiler.Build(project, unit);\n            var prompt = PromptPackRendererVisualBriefService.Build(source);\n            items.Add(new DiezVisualPromptItem(position, code, $"Immagine {position:D3}", prompt));\n''',
    "use canonical visual compiler")

text, count = re.subn(
    r'\n    private static string BuildAtomicVisualSource\(PreviewProject project, PromptEngineeringRequest request, int position\)\n    \{.*?\n    \}\n(?=\n    private static DiezVisualBookSetupDto ProjectSetup)',
    '\n',
    text,
    count=1,
    flags=re.S)
if count != 1:
    raise SystemExit("Round 5.1 patch: obsolete BuildAtomicVisualSource block not found")

text, count = re.subn(
    r'\n    private static string Join\(string\? first, string\? second\)\n    \{.*?\n    \}\n(?=\n    private static \(JsonObject Root, PreviewProject Project\) Parse)',
    '\n',
    text,
    count=1,
    flags=re.S)
if count != 1:
    raise SystemExit("Round 5.1 patch: obsolete visual Join helper not found")
write(path, text)


# -----------------------------------------------------------------------------
# 2. Renderer boundary: migrate legacy batch routing, keep real layout guard.
# -----------------------------------------------------------------------------
path = "src/Diez.Core/PromptPackRendererVisualBriefService.cs"
text = read(path)

start = text.index("    private static readonly Regex SeriesLayoutOrRoutingDirective")
end = text.index("\n\n    private static readonly string[] OperationalMarkers", start)
new_regexes = '''    private static readonly Regex LegacySeriesOrchestrationDirective = new(\n        @"(?i)^\\s*(?:(?:\\d+)\\s+(?:images?|immagini|illustrations?|illustrazioni)\\s*|(?:\\d+)\\s+(?:images?|immagini|illustrations?|illustrazioni)\\s*:?\\s*(?:1|one|una?)\\s+(?:(?:for)\\s+(?:each|every)\\s+|(?:per)\\s+(?:ogni\\s+)?)(?:animals?|animali|animale|subjects?|soggetti|soggetto|characters?|personaggi|personaggio)?\\s*|(?:one|una|un['’]?)\\s*(?:image|immagine)\\s+(?:(?:for)\\s+(?:each|every)\\s+|(?:per)\\s+(?:ogni\\s+)?)(?:animals?|animali|animale|subjects?|soggetti|soggetto|characters?|personaggi|personaggio)\\s*|(?:generate|create|produce|render|genera|crea|produci|renderizza|request|richiedi)\\s+\\d+\\s+(?:images?|immagini|illustrations?|illustrazioni)\\s*)[.!]?\\s*$",\n        RegexOptions.CultureInvariant | RegexOptions.Compiled);\n\n    private static readonly Regex ForbiddenLayoutDirective = new(\n        @"(?i)\\b(?:triptych|trittico|contact\\s+sheet|collage|multi[- ]?panel|griglia|grid)\\b",\n        RegexOptions.CultureInvariant | RegexOptions.Compiled);'''
text = text[:start] + new_regexes + text[end:]

text = replace_once(
    text,
    '''        "FRESH GENERATION", "Source-image policy:", "DIEZ RENDER REQUEST ID:",\n        "If the renderer cannot", "SERIES ROLE:", "FINAL CHECK — HARD:"\n''',
    '''        "FRESH GENERATION", "Source-image policy:", "DIEZ RENDER REQUEST ID:",\n        "If the renderer cannot", "SERIES ROLE:", "SERIES SUBJECT ASSIGNMENT — HARD:",\n        "FINAL CHECK — HARD:"\n''',
    "strip series assignment from renderer brief")

text = replace_once(
    text,
    '''        "triptych", "contact sheet", "collage", "multi-panel", "multi panel",\n        "3 images", "3 immagini", "3 illustrations", "3 illustrazioni",\n        "DIEZ RENDER REQUEST ID", "FAILED/INCOMPLETE", "FRESH GENERATION"\n''',
    '''        "triptych", "contact sheet", "collage", "multi-panel", "multi panel",\n        "DIEZ RENDER REQUEST ID", "FAILED/INCOMPLETE", "FRESH GENERATION"\n''',
    "remove brittle hard-coded 3-image tokens")

text = replace_once(
    text,
    '''            var line = raw.Trim();\n            if (line.Length == 0) continue;\n            if (OperationalMarkers.Any(m => line.StartsWith(m, StringComparison.OrdinalIgnoreCase))) continue;\n''',
    '''            var line = StripLegacyBullet(raw.Trim());\n            if (line.Length == 0) continue;\n            if (OperationalMarkers.Any(m => line.StartsWith(m, StringComparison.OrdinalIgnoreCase))) continue;\n\n            if (line.StartsWith("USER EXCLUSION — HARD:", StringComparison.OrdinalIgnoreCase) ||\n                line.StartsWith("USER REQUIREMENT — HARD:", StringComparison.OrdinalIgnoreCase))\n            {\n                var sanitized = SanitizeLegacyRequirementLine(line);\n                if (string.IsNullOrWhiteSpace(sanitized)) continue;\n                line = sanitized;\n            }\n            else if (LegacySeriesOrchestrationDirective.IsMatch(line))\n            {\n                // A standalone legacy quantity/routing sentence belongs to the batch planner.\n                continue;\n            }\n''',
    "sanitize legacy renderer lines")

anchor = '''            if (line.StartsWith("LINE WEIGHT — HARD:", StringComparison.OrdinalIgnoreCase))\n            {\n                var thin = text.Contains("line weight Thin — Fine", StringComparison.OrdinalIgnoreCase) ||\n                           text.Contains("line weight Very thin — Extra Fine", StringComparison.OrdinalIgnoreCase);\n                output.Add(thin\n                    ? "LINE WEIGHT — HARD: use visibly thin, fine, crisp black contours throughout, with clean print-legible separation and consistent delicate line treatment."\n                    : "LINE WEIGHT — HARD: match the selected line weight consistently throughout the illustration with clean print-legible contours.");\n                continue;\n            }\n'''
addition = '''\n            if (line.StartsWith("RENDERING METHOD — HARD:", StringComparison.OrdinalIgnoreCase))\n            {\n                output.Add("RENDERING METHOD — HARD: use the provider's native generative image capability to create a finished organic illustration; coded geometric, vector or diagrammatic construction is not an acceptable substitute.");\n                continue;\n            }\n\n            if (line.StartsWith("QUALITY FIRST — HARD:", StringComparison.OrdinalIgnoreCase))\n            {\n                output.Add("QUALITY FIRST — HARD: create a professional organic illustration first; simplified Coloring artwork must remain authored illustration rather than icon, diagram or geometric assembly.");\n                continue;\n            }\n\n            if (line.StartsWith("COLORING APPEARANCE — HARD:", StringComparison.OrdinalIgnoreCase))\n            {\n                output.Add("COLORING APPEARANCE — HARD: use clean black line art on a white background without intentional gray shading, color, gradients, shadows or tonal texture; preserve organic illustration quality.");\n                continue;\n            }\n'''
if anchor not in text:
    raise SystemExit("Round 5.1 patch: line-weight anchor not found")
text = text.replace(anchor, anchor + addition, 1)

old_user_guard = '''            if ((line.StartsWith("USER EXCLUSION — HARD:", StringComparison.OrdinalIgnoreCase) ||\n                 line.StartsWith("USER REQUIREMENT — HARD:", StringComparison.OrdinalIgnoreCase)) &&\n                SeriesLayoutOrRoutingDirective.IsMatch(line))\n            {\n                // Series orchestration such as "one image per animal/character" belongs to the batch/work-unit\n                // planner. The atomic subject + one-scene locks already express the renderer-visible intent.\n                continue;\n            }\n\n'''
if old_user_guard not in text:
    raise SystemExit("Round 5.1 patch: old user orchestration guard not found")
text = text.replace(old_user_guard, "", 1)

text = replace_once(
    text,
    '''        if (SeriesLayoutOrRoutingDirective.IsMatch(text))\n            throw new InvalidOperationException("Renderer visual brief contaminato da direttiva di serie/per-item.");\n    }\n\n    private static string ExtractStyle(string line)\n''',
    '''        if (ForbiddenLayoutDirective.IsMatch(text))\n            throw new InvalidOperationException("Renderer visual brief contaminato da direttiva layout multi-immagine.");\n        foreach (var raw in text.Replace("\\r\\n", "\\n").Split('\\n'))\n            if (ContainsLegacySeriesDirective(raw))\n                throw new InvalidOperationException("Renderer visual brief contaminato da direttiva di serie/per-item.");\n    }\n\n    private static string StripLegacyBullet(string line) =>\n        Regex.Replace(line ?? string.Empty, @"^\\s*(?:[-*•]\\s+|\\d+[.)]\\s+)", string.Empty).Trim();\n\n    private static string SanitizeLegacyRequirementLine(string line)\n    {\n        var colon = line.IndexOf(':');\n        if (colon < 0) return line;\n        var prefix = line[..(colon + 1)];\n        var payload = line[(colon + 1)..];\n        var kept = payload.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)\n            .Where(part => !LegacySeriesOrchestrationDirective.IsMatch(part))\n            .ToArray();\n        return kept.Length == 0 ? string.Empty : prefix + " " + string.Join("; ", kept);\n    }\n\n    private static bool ContainsLegacySeriesDirective(string line)\n    {\n        var normalized = StripLegacyBullet(line);\n        if (normalized.StartsWith("USER EXCLUSION — HARD:", StringComparison.OrdinalIgnoreCase) ||\n            normalized.StartsWith("USER REQUIREMENT — HARD:", StringComparison.OrdinalIgnoreCase))\n        {\n            var colon = normalized.IndexOf(':');\n            if (colon >= 0)\n            {\n                var payload = normalized[(colon + 1)..];\n                return payload.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)\n                    .Any(part => LegacySeriesOrchestrationDirective.IsMatch(part));\n            }\n        }\n        return LegacySeriesOrchestrationDirective.IsMatch(normalized);\n    }\n\n    private static string ExtractStyle(string line)\n''',
    "strict post-migration renderer guard")
write(path, text)


# -----------------------------------------------------------------------------
# 3. Core Prompt Pack freeze refreshes current visual Work Units too.
# -----------------------------------------------------------------------------
path = "src/Diez.Core/DiezPromptPackBatchFrontendBridge.cs"
text = read(path)
text = replace_once(
    text,
    '''        var ids = workUnitIds?.Where(x => x != Guid.Empty).Distinct().ToList();\n        var packagePrompt = BuildPackagePrompt(projectJson, ids);\n        var built = await DiezPromptPackFrontendBridge.BuildManualAsync(projectJson, projectPackagePath, ids, outputPath);\n''',
    '''        var ids = workUnitIds?.Where(x => x != Guid.Empty).Distinct().ToList();\n        var effectiveJson = projectJson;\n        var refreshed = DiezVisualHardPromptFrontendBridge.Recompile(projectJson, ids);\n        if (refreshed.Success) effectiveJson = refreshed.ProjectJson;\n\n        var packagePrompt = BuildPackagePrompt(effectiveJson, ids);\n        var built = await DiezPromptPackFrontendBridge.BuildManualAsync(effectiveJson, projectPackagePath, ids, outputPath);\n''',
    "refresh visual work units at freeze")
write(path, text)


# -----------------------------------------------------------------------------
# 4. Regression gate: exact old-project symptom + Round 5 execution + true layout.
# -----------------------------------------------------------------------------
test_path = Path("tests/Diez.VisualBook.Pianist/Round51LegacyPromptMigrationRegression.cs")
test_path.write_text(r'''using System.Runtime.CompilerServices;
using System.Text.Json;
using DiezPublishingStudio;

internal static class Round51LegacyPromptMigrationRegression
{
    [ModuleInitializer]
    internal static void Run()
    {
        LegacyRequirementIsMigrated();
        Round5ExecutionMethodIsCompatible();
        RealMultiImageLayoutStillFails();
        VisualBridgeUsesCanonicalCompiler();
    }

    private static void LegacyRequirementIsMigrated()
    {
        var source = """
Create ONE finished, publication-quality coloring-book illustration.
PRIMARY SUBJECT — HARD LOCK: friendly Halloween ghost.
- USER REQUIREMENT — HARD: 3 images: 1 per ogni soggetto; friendly smiling expression
STYLE — HARD LOCK: Cute & Playful.
COMPOSITION — HARD LOCK: exactly ONE unified continuous composition with one primary scene.
""";
        var prompt = PromptPackRendererVisualBriefService.Build(source);
        if (prompt.Contains("3 images", StringComparison.OrdinalIgnoreCase))
            throw new Exception("ROUND51_REGRESSION: legacy '3 images' survived renderer migration.");
        if (!prompt.Contains("friendly smiling expression", StringComparison.OrdinalIgnoreCase))
            throw new Exception("ROUND51_REGRESSION: meaningful sibling HARD requirement was lost.");
        if (!prompt.Contains("friendly Halloween ghost", StringComparison.OrdinalIgnoreCase))
            throw new Exception("ROUND51_REGRESSION: atomic subject was lost.");
        PromptPackRendererVisualBriefService.EnsureVisualOnly(prompt);
    }

    private static void Round5ExecutionMethodIsCompatible()
    {
        var source = """
Create ONE finished, publication-quality coloring-book illustration.
PRIMARY SUBJECT — HARD LOCK: cute black cat.
RENDERING METHOD — HARD: use a native generative IMAGE model/tool capable of producing finished illustration artwork. If native image generation is unavailable, return FAILED/INCOMPLETE instead of fabricating an approximate drawing.
QUALITY FIRST — HARD: create the page as a professional organic illustration first.
COLORING APPEARANCE — HARD: visually use black ink on a clean white background.
""";
        var prompt = PromptPackRendererVisualBriefService.Build(source);
        if (prompt.Contains("FAILED/INCOMPLETE", StringComparison.OrdinalIgnoreCase))
            throw new Exception("ROUND51_REGRESSION: transport status leaked into renderer brief.");
        if (!prompt.Contains("native generative image capability", StringComparison.OrdinalIgnoreCase))
            throw new Exception("ROUND51_REGRESSION: native image-generation requirement was lost.");
        PromptPackRendererVisualBriefService.EnsureVisualOnly(prompt);
    }

    private static void RealMultiImageLayoutStillFails()
    {
        var source = """
Create ONE finished, publication-quality coloring-book illustration.
PRIMARY SUBJECT — HARD LOCK: haunted house.
USER REQUIREMENT — HARD: arrange the result as a triptych composition.
""";
        try
        {
            _ = PromptPackRendererVisualBriefService.Build(source);
            throw new Exception("ROUND51_REGRESSION: true triptych layout contamination was accepted.");
        }
        catch (InvalidOperationException)
        {
            // Expected: migration must not weaken the genuine atomic-layout gate.
        }
    }

    private static void VisualBridgeUsesCanonicalCompiler()
    {
        var project = new PreviewProject { Name = "Round51 regression" };
        BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);
        VisualBookPlanService.Save(project, 3, false);
        var profile = BookTypePromptProfileService.LoadColoring(project);
        profile.SubjectDescription = "3 soggetti di Halloween";
        profile.EnvironmentDescription = "simple Halloween porch";
        profile.Style = "Cute & Playful";
        profile.BoldEasy = true;
        profile.LineWeight = "Thick";
        profile.ClosedAreas = true;
        profile.CleanContours = true;
        profile.NoTextInsideImage = true;
        profile.SubjectClearlySeparated = true;
        BookTypePromptProfileService.SaveColoring(project, profile);
        ColoringIndependentHardProfileService.PersistResolvedState(project, "Cute & Playful", "Thick", true, false);

        var json = JsonSerializer.Serialize(project);
        var pack = DiezVisualBookFrontendBridge.BuildPromptPack(
            json,
            "3 images: 1 per ogni soggetto",
            string.Empty,
            PromptEngineeringProviderIds.Generic,
            true);
        if (pack.Items.Count != 3)
            throw new Exception("ROUND51_REGRESSION: visual bridge did not build the expected three atomic prompts.");
        foreach (var item in pack.Items)
        {
            if (item.Prompt.Contains("3 images", StringComparison.OrdinalIgnoreCase))
                throw new Exception($"ROUND51_REGRESSION: {item.Code} still contains legacy batch quantity.");
            PromptPackRendererVisualBriefService.EnsureVisualOnly(item.Prompt);
        }
    }
}
''', encoding="utf-8")

print("Round 5.1 legacy Prompt Pack migration patch applied successfully.")
