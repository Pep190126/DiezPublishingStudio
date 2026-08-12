using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;

namespace DiezPublishingStudio;

/// <summary>
/// Provider-facing boundary for visual Prompt Packs.
/// Keeps long orchestration/QA contracts separate from the concise text actually sent to an image renderer,
/// strips legacy generated technical blocks that were incorrectly classified as user edits, and normalizes
/// duplicated request-context payloads so the ZIP has one effective source of truth.
/// </summary>
internal static class PromptPackProviderFacingService
{
    private static readonly string[] JungleAnimalSeries =
    [
        "monkey", "tiger", "elephant", "toucan", "sloth", "jaguar", "parrot", "crocodile"
    ];

    private static readonly Regex LegacyTechnicalSection = new(
        @"(?ims)^\s*(?:SPECIFICHE TECNICHE|TECHNICAL SPECIFICATIONS?)\s*:\s*$.*?(?=^\s*===|\z)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex LegacyEditorialSection = new(
        @"(?ims)^\s*(?:PROFILO EDITORIALE COLORING BOOK|COLORING BOOK EDITORIAL PROFILE)\s*:\s*$.*?(?=^\s*===|\z)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex NegativeVisualSoup = new(
        @"(?im)^NEGATIVE VISUAL CONSTRAINTS\s*\r?\nAvoid:[^\r\n]*(?:\r?\n)?",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SeriesCountDirective = new(
        @"(?i)(?:\b\d+\s+(?:images?|immagini)\b|\bone\s+per\s+(?:animal|item|subject)\b|\buna\s+per\s+(?:animale|elemento|soggetto)\b)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string SanitizeManualDelta(string? delta)
    {
        var text = (delta ?? string.Empty).Replace("\r\n", "\n").Trim();
        if (text.Length == 0) return string.Empty;

        text = LegacyTechnicalSection.Replace(text, string.Empty);
        text = LegacyEditorialSection.Replace(text, string.Empty);

        var kept = new List<string>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd();
            if (LooksLikeGeneratedTechnicalLine(line)) continue;
            kept.Add(line);
        }

        return string.Join(Environment.NewLine, kept).Trim();
    }

    public static string DecontaminateLongPrompt(string? prompt)
    {
        var text = (prompt ?? string.Empty).Replace("\r\n", "\n");
        text = NegativeVisualSoup.Replace(text,
            "OUTPUT EXCLUSION GUARD\n- Reject any result whose primary subject differs from the requested item, whose rendering mode conflicts with the selected Book Type, or whose layout contains more than one composition.\n");
        return PromptEnglishNormalizer.NormalizeProviderFacing(text);
    }

    public static string BuildImageGenerationPrompt(
        PreviewProject project,
        AiExchangeWorkUnit unit,
        int total,
        int index,
        PromptPreparationSettings settings)
    {
        var master = PromptMasterStateStore.LoadForCurrentBook(project);
        var request = PromptEngineeringEngine.BuildRequest(
            project,
            Math.Max(1, total),
            master?.MustDo ?? string.Empty,
            master?.MustNotDo ?? string.Empty,
            settings.ProviderId,
            settings.PreferAdvancedModel);
        var item = request.ItemOverrides.FirstOrDefault(x => x.ItemIndex == index);

        var rawSubject = !string.IsNullOrWhiteSpace(item?.Subject) ? item!.Subject : request.Subject;
        var subject = ResolveConcreteSubject(rawSubject, request.BookType, index, total);
        var environment = PromptEnglishNormalizer.NormalizeProviderFacing(
            !string.IsNullOrWhiteSpace(item?.Environment) ? item!.Environment : request.Environment);

        var sb = new StringBuilder();
        if (string.Equals(request.BookType, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine("Create ONE finished, publication-quality coloring-book illustration.");
            sb.AppendLine($"PRIMARY SUBJECT — HARD LOCK: {subject}. The subject must be the dominant focal element, large, clearly recognizable, anatomically coherent and more visually important than the background.");
            if (!string.IsNullOrWhiteSpace(environment))
                sb.AppendLine($"SETTING — SUPPORTING ONLY: {environment}. Keep the setting secondary, uncluttered and clearly subordinate to the main subject.");
            sb.AppendLine($"STYLE: {Norm(request.Style, "clean professional coloring-book line art")}; audience: {Norm(request.Audience, "general audience")}; difficulty: {Norm(request.Difficulty, "Medium")}; line weight: {Norm(request.LineWeight, "Medium")}; complexity: {Norm(request.Complexity, "Medium")}; element density: {Norm(request.Density, "Low to medium")}; background treatment: {Norm(request.Background, "Simple contextual background")}.");
            sb.AppendLine("DRAWING CRAFT: smooth intentional organic contours, coherent pose and anatomy, strong silhouette, clean closed colorable regions, balanced composition and a friendly professional finish. Simple child-friendly art must still look deliberately illustrated, never rough, primitive or placeholder-like.");
            sb.AppendLine("COLOR OUTPUT — HARD: pure black #000000 line work on pure white #FFFFFF only in the final raster. Use no intermediate gray or color values. Keep regions comfortably colorable and print-legible.");
            sb.AppendLine("VISIBLE CONTENT — HARD: no text, letters, numbers, labels, signatures, watermarks, IDs or filenames inside the artwork.");
        }
        else
        {
            sb.AppendLine("Create ONE finished, publication-quality editorial image.");
            sb.AppendLine($"PRIMARY SUBJECT — HARD LOCK: {subject}. Make the requested subject immediately readable and dominant in the composition.");
            if (!string.IsNullOrWhiteSpace(environment))
                sb.AppendLine($"SETTING — SUPPORTING ONLY: {environment}. The setting must support the subject rather than replace it.");
            sb.AppendLine($"RENDERING: {Norm(request.RenderingStyle, "professional illustration")}; color treatment: {Norm(request.ColorMode, "appropriate professional color treatment")}; detail: {Norm(request.DetailLevel, "Medium")}; background: {Norm(request.Background, "contextually appropriate")}.");
            sb.AppendLine("CRAFT: coherent composition, plausible geometry/anatomy/perspective, clean edges and publication-ready finish.");
            if (request.NoTextInsideImage)
                sb.AppendLine("VISIBLE CONTENT — HARD: no text, labels, captions, IDs, signatures or watermarks inside the image unless explicitly requested for this item.");
        }

        var itemMustDo = PromptEnglishNormalizer.NormalizeProviderFacing(item?.MustDo);
        if (!string.IsNullOrWhiteSpace(itemMustDo))
            sb.AppendLine("ITEM REQUIREMENT — HARD: " + itemMustDo);
        else
        {
            var generalMustDo = PromptEnglishNormalizer.NormalizeProviderFacing(request.MustDo);
            if (!string.IsNullOrWhiteSpace(generalMustDo) && !SeriesCountDirective.IsMatch(generalMustDo))
                sb.AppendLine("USER REQUIREMENT — HARD: " + generalMustDo);
        }

        var itemMustNot = PromptEnglishNormalizer.NormalizeProviderFacing(item?.MustNotDo);
        var generalMustNot = PromptEnglishNormalizer.NormalizeProviderFacing(request.MustNotDo);
        var exclusion = !string.IsNullOrWhiteSpace(itemMustNot) ? itemMustNot : generalMustNot;
        if (!string.IsNullOrWhiteSpace(exclusion))
            sb.AppendLine("USER EXCLUSION — HARD: " + exclusion);

        AppendTechnical(sb, request.Technical);
        sb.AppendLine($"SERIES ROLE: this is item {index} of {Math.Max(1, total)}, but render ONLY this one composition. Do not represent the series count visually.");
        sb.AppendLine("FINAL CHECK: the returned image must visibly match the PRIMARY SUBJECT before any technical compliance is considered. If the requested subject is not the dominant visible content, regenerate instead of returning the asset.");

        return PromptEnglishNormalizer.NormalizeProviderFacing(sb.ToString()).Trim();
    }

    public static void NormalizeRequestContext(JsonObject root)
    {
        root["critical_rule"] = "For corrections/edits, the real base image file is the authoritative visual source. Descriptions guide the AI but never replace the actual image file.";
        root["profile_isolation_rule"] = "Only the active Book Type profile belongs to this request; historical visual profiles and profiles from other Book Types are excluded.";

        if (root["image_presets"] is JsonObject presets)
        {
            NormalizeStrings(presets);
            if (presets["technical_image_specs"] is JsonObject technical)
                NormalizeStrings(technical);
        }
    }

    private static void AppendTechnical(StringBuilder sb, PromptEngineeringTechnicalSpec technical)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(technical.AspectRatio))
            parts.Add("aspect ratio " + technical.AspectRatio);
        if (!string.IsNullOrWhiteSpace(technical.PixelWidth) && !string.IsNullOrWhiteSpace(technical.PixelHeight))
            parts.Add($"target raster {technical.PixelWidth} × {technical.PixelHeight} px");
        if (!string.IsNullOrWhiteSpace(technical.Dpi))
            parts.Add(technical.Dpi + " DPI print context");
        if (!string.IsNullOrWhiteSpace(technical.Quality))
            parts.Add("quality " + PromptEnglishNormalizer.NormalizeProviderFacing(technical.Quality));
        if (!string.IsNullOrWhiteSpace(technical.TechnicalDetail))
            parts.Add("technical detail " + PromptEnglishNormalizer.NormalizeProviderFacing(technical.TechnicalDetail));
        if (parts.Count > 0)
            sb.AppendLine("TECHNICAL OUTPUT: " + string.Join("; ", parts) + ". Preserve aspect ratio and never stretch the artwork.");
    }

    private static string ResolveConcreteSubject(string? rawSubject, string bookType, int index, int total)
    {
        var normalized = PromptEnglishNormalizer.NormalizeProviderFacing(rawSubject);
        if (string.Equals(bookType, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase) &&
            normalized.Contains("jungle animals", StringComparison.OrdinalIgnoreCase))
        {
            var species = JungleAnimalSeries[(Math.Max(1, index) - 1) % JungleAnimalSeries.Length];
            return $"one {species}";
        }

        if (!string.IsNullOrWhiteSpace(normalized)) return normalized;
        return string.Equals(bookType, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase)
            ? $"one concrete, recognizable coloring-book subject for item {Math.Max(1, index)}"
            : $"one concrete, recognizable editorial subject for item {Math.Max(1, index)}";
    }

    private static string Norm(string? value, string fallback)
    {
        var normalized = PromptEnglishNormalizer.NormalizeProviderFacing(value);
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private static bool LooksLikeGeneratedTechnicalLine(string line)
    {
        var value = line.TrimStart('-', ' ', '\t');
        if (value.Length == 0) return false;
        var prefixes = new[]
        {
            "Tipo libro / uso", "Formato pagina / trim finale", "Dimensioni pagina", "Aspect ratio image",
            "Coerenza trim/aspect ratio", "Non deformare mai", "Classe risoluzione / qualità",
            "Risoluzione target effettiva", "DPI di destinazione", "Qualità rendering",
            "Livello tecnico di dettaglio", "Output Coloring Book", "Vietati senza eccezioni",
            "Evita testo tecnico", "Bleed e margini di sicurezza", "Book type / image use",
            "Page format / final trim", "Target effective resolution", "Rendering quality"
        };
        return prefixes.Any(p => value.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    private static void NormalizeStrings(JsonObject node)
    {
        foreach (var key in node.Select(x => x.Key).ToList())
        {
            if (node[key] is JsonValue value && value.TryGetValue<string>(out var text))
                node[key] = PromptEnglishNormalizer.NormalizeProviderFacing(text);
            else if (node[key] is JsonObject child)
                NormalizeStrings(child);
            else if (node[key] is JsonArray array)
            {
                foreach (var item in array.OfType<JsonObject>()) NormalizeStrings(item);
            }
        }
    }
}
