using System.Text;
using System.Text.RegularExpressions;

namespace DiezPublishingStudio;

/// <summary>
/// Final boundary before an IMAGE prompt reaches the image model.
/// Routing/session/retry/audit instructions belong to the executor and render-plan, never to the visual model.
/// The renderer receives only a compact positive visual brief for one atomic Work Unit.
/// </summary>
internal static class PromptPackRendererVisualBriefService
{
    private static readonly Regex SeriesLayoutOrRoutingDirective = new(
        @"(?i)(?:\b\d+\s+(?:images?|immagini|illustrations?|illustrazioni|panels?|pannelli)\b|\b(?:one|una|un['’]?unica?)\s+(?:image|immagine)\b.{0,40}\b\d+\s+(?:illustrations?|illustrazioni|images?|immagini)\b|\b(?:one|una|un['’]?)\s*(?:image|immagine)\s+(?:(?:for)\s+(?:each|every)\s+|(?:per)\s+(?:ogni\s+)?)(?:animals?|animali|animale|subjects?|soggetti|soggetto)\b|\b(?:triptych|trittico|contact\s+sheet|collage|multi[- ]?panel|griglia|grid)\b)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly string[] OperationalMarkers =
    [
        "FRESH GENERATION", "Source-image policy:", "DIEZ RENDER REQUEST ID:",
        "If the renderer cannot", "SERIES ROLE:", "FINAL CHECK — HARD:"
    ];

    private static readonly string[] ForbiddenRendererConceptSoup =
    [
        "triptych", "contact sheet", "collage", "multi-panel", "multi panel",
        "3 images", "3 immagini", "3 illustrations", "3 illustrazioni",
        "DIEZ RENDER REQUEST ID", "FAILED/INCOMPLETE", "FRESH GENERATION"
    ];

    public static string Build(string? source)
    {
        var text = (source ?? string.Empty).Replace("\r\n", "\n").Trim();
        if (text.Length == 0) return string.Empty;

        var lines = text.Split('\n');
        var output = new List<string>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (OperationalMarkers.Any(m => line.StartsWith(m, StringComparison.OrdinalIgnoreCase))) continue;

            if (line.StartsWith("COMPOSITION — HARD LOCK:", StringComparison.OrdinalIgnoreCase))
            {
                output.Add("COMPOSITION — HARD LOCK: one continuous unified primary scene filling the canvas, centered on the single atomic subject.");
                continue;
            }

            if (line.StartsWith("STYLE — HARD LOCK:", StringComparison.OrdinalIgnoreCase))
            {
                var style = ExtractStyle(line);
                output.Add($"STYLE — HARD LOCK: {style}. {PositiveStyleDirective(style)}");
                continue;
            }

            if (line.StartsWith("BOLD & EASY — HARD:", StringComparison.OrdinalIgnoreCase))
            {
                var on = line.StartsWith("BOLD & EASY — HARD: ON", StringComparison.OrdinalIgnoreCase);
                output.Add(on
                    ? "BOLD & EASY — HARD: ON. Use large simple readable forms, broad colorable regions, restrained interior detail, low visual clutter and confident easy-to-follow contours."
                    : "BOLD & EASY — HARD: OFF. Keep the selected style, line weight, complexity and density at their normal treatment without a Bold & Easy production profile.");
                continue;
            }

            if (line.StartsWith("COZY — HARD:", StringComparison.OrdinalIgnoreCase))
            {
                var on = line.StartsWith("COZY — HARD: ON", StringComparison.OrdinalIgnoreCase);
                output.Add(on
                    ? "COZY — HARD: ON. Use a warm, comforting, gentle and inviting mood with friendly staging, soft approachable shape language and calm supporting details."
                    : "COZY — HARD: OFF. Follow the selected style and requested scene without adding an independent Cozy treatment.");
                continue;
            }

            if (line.StartsWith("LINE WEIGHT — HARD:", StringComparison.OrdinalIgnoreCase))
            {
                var thin = text.Contains("line weight Thin — Fine", StringComparison.OrdinalIgnoreCase) ||
                           text.Contains("line weight Very thin — Extra Fine", StringComparison.OrdinalIgnoreCase);
                output.Add(thin
                    ? "LINE WEIGHT — HARD: use visibly thin, fine, crisp black contours throughout, with clean print-legible separation and consistent delicate line treatment."
                    : "LINE WEIGHT — HARD: match the selected line weight consistently throughout the illustration with clean print-legible contours.");
                continue;
            }

            if (line.StartsWith("DRAWING CRAFT:", StringComparison.OrdinalIgnoreCase))
            {
                output.Add("DRAWING CRAFT: use smooth intentional organic contours, coherent anatomy, a strong readable silhouette, clean closed colorable regions and a balanced professional composition.");
                continue;
            }

            if (line.StartsWith("COLOR OUTPUT — HARD:", StringComparison.OrdinalIgnoreCase))
            {
                output.Add("COLOR OUTPUT — HARD: final raster uses exactly pure black #000000 and pure white #FFFFFF, with clean colorable regions and print-legible line work.");
                continue;
            }

            if ((line.StartsWith("USER EXCLUSION — HARD:", StringComparison.OrdinalIgnoreCase) ||
                 line.StartsWith("USER REQUIREMENT — HARD:", StringComparison.OrdinalIgnoreCase)) &&
                SeriesLayoutOrRoutingDirective.IsMatch(line))
            {
                // Series orchestration such as "one image per animal" belongs to the batch/work-unit
                // planner. The atomic subject + one-scene locks already express the renderer-visible intent.
                continue;
            }

            output.Add(line);
        }

        var result = PromptEnglishNormalizer.NormalizeProviderFacing(string.Join(Environment.NewLine, output)).Trim();
        EnsureVisualOnly(result);
        return result;
    }

    public static void EnsureVisualOnly(string? prompt)
    {
        var text = (prompt ?? string.Empty).Trim();
        if (text.Length == 0) throw new InvalidOperationException("Renderer visual brief vuoto.");
        foreach (var forbidden in ForbiddenRendererConceptSoup)
            if (text.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Renderer visual brief contaminato da istruzione orchestrativa/layout: " + forbidden);
        if (SeriesLayoutOrRoutingDirective.IsMatch(text))
            throw new InvalidOperationException("Renderer visual brief contaminato da direttiva di serie/per-item.");
    }

    private static string ExtractStyle(string line)
    {
        var value = line[(line.IndexOf(':') + 1)..].Trim();
        var dot = value.IndexOf('.');
        if (dot >= 0) value = value[..dot];
        return string.IsNullOrWhiteSpace(value) ? "Clean Line Art" : value.Trim();
    }

    private static string PositiveStyleDirective(string style)
    {
        if (string.Equals(style, "Kawaii", StringComparison.OrdinalIgnoreCase))
            return "Use unmistakably cute Kawaii design: simplified rounded forms, a relatively large expressive head and eyes where appropriate, tiny simple facial features, gentle child-friendly charm and clear colorable shapes.";
        if (string.Equals(style, "Cartoon", StringComparison.OrdinalIgnoreCase))
            return "Use unmistakably cartoon design: simplified stylized anatomy, expressive features, clear shape language, readable proportions and clean colorable forms.";
        if (string.Equals(style, "Chibi", StringComparison.OrdinalIgnoreCase))
            return "Use clear chibi proportions with an intentionally oversized expressive head, compact simplified body, cute face and reduced anatomical detail.";

        var directive = BookTypePromptProfileService.StyleHardDirectiveEnglish(style).Trim();
        directive = Regex.Replace(directive, @"(?i);\s*(?:reject|avoid|do not)\b.*$", string.Empty).Trim();
        directive = Regex.Replace(directive, @"(?i)\s+(?:rather than|instead of)\s+[^.]+", string.Empty).Trim();
        return directive.Length == 0
            ? $"Use the recognizable visual language of {style} consistently throughout the subject and supporting scene."
            : directive;
    }
}
