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

    private static readonly string[] GeneralAnimalSeries =
    [
        "cat", "dog", "rabbit", "bear", "fox", "owl", "horse", "lion"
    ];

    private static readonly string[] KnownAnimalSpecies =
    [
        "monkey", "tiger", "elephant", "toucan", "sloth", "jaguar", "parrot", "crocodile",
        "lion", "giraffe", "zebra", "hippo", "rhinoceros", "rhino", "gorilla", "chimpanzee",
        "cat", "dog", "rabbit", "bear", "fox", "owl", "horse"
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

    private static readonly Regex NonAtomicSubjectDirective = new(
        @"(?i)(?:\b\d+\s+(?:images?|immagini|animals?|animali|subjects?|soggetti|items?|elements?|characters?)\b|\b(?:images?|immagini)\b|\b(?:triptych|contact\s+sheet|collage|multi[- ]?panel|panels?)\b)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex HardSubjectLine = new(
        @"(?im)^PRIMARY SUBJECT\s+—\s+HARD LOCK:\s*(?<subject>.+?)(?:\.\s+The subject\b|\r?$)",
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

        var subject = ResolveAtomicSubject(request, index);
        var environment = PromptEnglishNormalizer.NormalizeProviderFacing(
            !string.IsNullOrWhiteSpace(item?.Environment) ? item!.Environment : request.Environment);

        var sb = new StringBuilder();
        if (string.Equals(request.BookType, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase))
        {
            var hardProfile = ColoringIndependentHardProfileService.Resolve(project);
            var style = Norm(hardProfile.Style, "Clean Line Art");
            sb.AppendLine("Create ONE finished, publication-quality coloring-book illustration.");
            sb.AppendLine($"PRIMARY SUBJECT — HARD LOCK: {subject}. The subject must be the dominant focal element, large, clearly recognizable, anatomically coherent and more visually important than the background.");
            sb.AppendLine("COMPOSITION — HARD LOCK: exactly ONE unified composition with exactly ONE primary scene filling the canvas. Keep the canvas as one continuous scene; do not subdivide it into separate framed, side-by-side or stacked regions, and do not show multiple alternative compositions or visually represent the series count.");
            if (!string.IsNullOrWhiteSpace(environment))
                sb.AppendLine($"SETTING — SUPPORTING ONLY: {environment}. Keep the setting secondary, uncluttered and clearly subordinate to the main subject.");
            sb.AppendLine($"STYLE — HARD LOCK: {style}. {BookTypePromptProfileService.StyleHardDirectiveEnglish(style)} A polished image in a different visual style is non-compliant and must be regenerated.");
            sb.AppendLine(ColoringIndependentHardProfileService.BoldEasyDirective(hardProfile.BoldEasy));
            sb.AppendLine(ColoringIndependentHardProfileService.CozyDirective(hardProfile.Cozy));
            sb.AppendLine($"EDITORIAL TARGET: audience {Norm(request.Audience, "general audience")}; difficulty {Norm(request.Difficulty, "Medium")}; line weight {Norm(hardProfile.LineWeight, "Medium")}; complexity {Norm(request.Complexity, "Medium")}; element density {Norm(request.Density, "Low to medium")}; background treatment {Norm(request.Background, "Simple contextual background")}.");
            sb.AppendLine("LINE WEIGHT — HARD: the selected line weight is authoritative. Thin/Fine and Very thin/Extra Fine contours must remain visibly thin and may not be thickened into a Bold & Easy treatment.");
            sb.AppendLine("DRAWING CRAFT: smooth intentional organic contours, coherent pose and anatomy, strong silhouette, clean closed colorable regions, balanced composition and a professional finish appropriate to the selected style and independent HARD profiles. Simple child-friendly art must still look deliberately illustrated, never rough, primitive or placeholder-like.");
            sb.AppendLine("COLOR OUTPUT — HARD: pure black #000000 line work on pure white #FFFFFF only in the final raster. Use no intermediate gray or color values. Keep regions comfortably colorable and print-legible.");
            sb.AppendLine("VISIBLE CONTENT — HARD: no text, letters, numbers, labels, signatures, watermarks, IDs or filenames inside the artwork.");
        }
        else
        {
            var style = Norm(request.RenderingStyle, "professional illustration");
            sb.AppendLine("Create ONE finished, publication-quality editorial image.");
            sb.AppendLine($"PRIMARY SUBJECT — HARD LOCK: {subject}. Make the requested subject immediately readable and dominant in the composition.");
            sb.AppendLine("COMPOSITION — HARD LOCK: exactly ONE unified composition with one primary scene filling the canvas. Keep the canvas continuous rather than subdividing it into separate framed, side-by-side or stacked regions; do not show multiple alternative compositions unless this exact Work Unit explicitly requests them.");
            if (!string.IsNullOrWhiteSpace(environment))
                sb.AppendLine($"SETTING — SUPPORTING ONLY: {environment}. The setting must support the subject rather than replace it.");
            sb.AppendLine($"STYLE — HARD LOCK: {style}. The visible rendering must clearly match this selected style; a professionally rendered image in a different style is non-compliant.");
            sb.AppendLine($"RENDERING: color treatment {Norm(request.ColorMode, "appropriate professional color treatment")}; detail {Norm(request.DetailLevel, "Medium")}; background {Norm(request.Background, "contextually appropriate")}.");
            sb.AppendLine("CRAFT: coherent composition, plausible geometry/anatomy/perspective, clean edges and publication-ready finish.");
            if (request.NoTextInsideImage)
                sb.AppendLine("VISIBLE CONTENT — HARD: no text, labels, captions, IDs, signatures or watermarks inside the image unless explicitly requested for this item.");
        }

        var itemMustDo = PromptEnglishNormalizer.NormalizeProviderFacing(item?.MustDo);
        if (!string.IsNullOrWhiteSpace(itemMustDo) && !SeriesCountDirective.IsMatch(itemMustDo))
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
        sb.AppendLine(string.Equals(request.BookType, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase)
            ? "FINAL CHECK — HARD: the returned image must visibly match PRIMARY SUBJECT, STYLE, BOLD & EASY ON/OFF, COZY ON/OFF, LINE WEIGHT and single-composition hard locks before any technical compliance is considered. If any one fails, regenerate instead of returning the asset."
            : "FINAL CHECK — HARD: the returned image must visibly match BOTH the PRIMARY SUBJECT and STYLE hard locks and must contain only one unified composition before any technical compliance is considered. If any of these fail, regenerate instead of returning the asset.");

        var renderer = PromptEnglishNormalizer.NormalizeProviderFacing(sb.ToString()).Trim();
        EnsureRendererPromptReady(renderer, unit.Code);
        return renderer;
    }

    /// <summary>
    /// Resolve the concrete subject for the current Work Unit. This is shared by generation and Vision QA,
    /// so a series-level phrase can never be treated as the semantic subject of one candidate.
    /// </summary>
    public static string ResolveAtomicSubject(PromptEngineeringRequest request, int index)
    {
        index = Math.Max(1, index);
        var item = request.ItemOverrides.FirstOrDefault(x => x.ItemIndex == index);
        var rawSubject = !string.IsNullOrWhiteSpace(item?.Subject) ? item!.Subject : request.Subject;
        var normalizedSubject = PromptEnglishNormalizer.NormalizeProviderFacing(rawSubject);
        var combined = PromptEnglishNormalizer.NormalizeProviderFacing(string.Join("\n", new[]
        {
            rawSubject ?? string.Empty,
            request.Subject ?? string.Empty,
            request.Environment ?? string.Empty,
            request.MustDo ?? string.Empty,
            item?.MustDo ?? string.Empty
        }));

        var explicitSpecies = ExtractSpeciesInOrder(combined);
        if (explicitSpecies.Count >= index)
            return "one " + explicitSpecies[index - 1];

        var animalTheme = combined.Contains("animal", StringComparison.OrdinalIgnoreCase) || explicitSpecies.Count > 0;
        var jungleContext = combined.Contains("jungle", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(request.BookType, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase) &&
            request.SeriesCount > 1 && animalTheme)
        {
            var series = jungleContext ? JungleAnimalSeries : GeneralAnimalSeries;
            return "one " + series[(index - 1) % series.Length];
        }

        var cleaned = StripSeriesNoise(normalizedSubject);
        if (!string.IsNullOrWhiteSpace(cleaned) && !NonAtomicSubjectDirective.IsMatch(cleaned))
            return cleaned;

        return string.Equals(request.BookType, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase)
            ? $"one concrete, recognizable coloring-book subject for item {index}"
            : $"one concrete, recognizable editorial subject for item {index}";
    }

    public static void EnsureRendererPromptReady(string? prompt, string? code = null)
    {
        var text = (prompt ?? string.Empty).Trim();
        var label = string.IsNullOrWhiteSpace(code) ? "Work Unit" : code.Trim();
        if (text.Length == 0)
            throw new InvalidOperationException($"{label}: renderer prompt vuoto.");

        var match = HardSubjectLine.Match(text);
        if (!match.Success)
            throw new InvalidOperationException($"{label}: PRIMARY SUBJECT — HARD LOCK mancante o non leggibile.");
        var subject = match.Groups["subject"].Value.Trim();
        if (NonAtomicSubjectDirective.IsMatch(subject))
            throw new InvalidOperationException($"{label}: PRIMARY SUBJECT non atomico ('{subject}'). La quantità della serie non può entrare nel soggetto di una singola Work Unit.");

        if (text.Contains("coloring-book", StringComparison.OrdinalIgnoreCase))
        {
            if (!text.Contains("STYLE — HARD LOCK:", StringComparison.Ordinal))
                throw new InvalidOperationException($"{label}: STYLE — HARD LOCK mancante nel renderer prompt Coloring.");
            if (!text.Contains("BOLD & EASY — HARD:", StringComparison.Ordinal))
                throw new InvalidOperationException($"{label}: BOLD & EASY — HARD ON/OFF mancante nel renderer prompt Coloring.");
            if (!text.Contains("COZY — HARD:", StringComparison.Ordinal))
                throw new InvalidOperationException($"{label}: COZY — HARD ON/OFF mancante nel renderer prompt Coloring.");
            if ((text.Contains("Thin — Fine", StringComparison.OrdinalIgnoreCase) ||
                 text.Contains("Very thin — Extra Fine", StringComparison.OrdinalIgnoreCase)) &&
                !text.Contains("BOLD & EASY — HARD: OFF", StringComparison.Ordinal))
                throw new InvalidOperationException($"{label}: linee sottili/fini richiedono BOLD & EASY — HARD: OFF.");
        }

        if (PromptEnglishNormalizer.ContainsKnownItalianVisualVocabulary(text))
            throw new InvalidOperationException($"{label}: il renderer prompt contiene ancora vocabolario provider-facing italiano noto.");
    }

    public static void NormalizeRequestContext(JsonObject root)
    {
        root["critical_rule"] = "For corrections/edits, the real base image file is the authoritative visual source. Descriptions guide the AI but never replace the actual image file.";
        root["profile_isolation_rule"] = "Only the active Book Type profile belongs to this request; historical visual profiles and profiles from other Book Types are excluded.";

        if (root["image_presets"] is not JsonObject presets) return;

        NormalizeStrings(presets);

        // The enhancer creates a human-readable prompt before the final compiler runs. Once the finalizer has
        // produced effective_visual_prompt, every secondary prompt copy must converge on that exact final text.
        var effective = presets["effective_visual_prompt"]?.ToString() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(effective))
        {
            effective = DecontaminateLongPrompt(effective);
            presets["effective_visual_prompt"] = effective;
            if (presets["effective_presets"] is JsonObject effectivePresets)
                effectivePresets["human_readable_visual_prompt"] = effective;
        }

        var canonical = presets["canonical_visual_prompt"]?.ToString() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(canonical))
            presets["canonical_visual_prompt"] = DecontaminateLongPrompt(canonical);

        if (presets["technical_image_specs"] is JsonObject technical)
            NormalizeStrings(technical);
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

    private static List<string> ExtractSpeciesInOrder(string text)
    {
        var hits = new List<(int Position, string Species)>();
        foreach (var species in KnownAnimalSpecies)
        {
            var match = Regex.Match(text ?? string.Empty,
                $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(species)}(?![\p{{L}}\p{{N}}])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (match.Success) hits.Add((match.Index, species == "rhino" ? "rhinoceros" : species));
        }
        return hits.OrderBy(x => x.Position).Select(x => x.Species).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string StripSeriesNoise(string value)
    {
        var text = value ?? string.Empty;
        text = Regex.Replace(text, @"(?i)\b\d+\s+(?:images?|animals?|subjects?|items?|elements?|characters?)\b", " ");
        text = Regex.Replace(text, @"(?i)\b(?:different|separate|series|set|images?|items?)\b", " ");
        text = Regex.Replace(text, @"\s+", " ").Trim(' ', ',', ';', ':', '-', '–', '—', '.');
        return text;
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
            {
                node[key] = key.Contains("prompt", StringComparison.OrdinalIgnoreCase)
                    ? DecontaminateLongPrompt(text)
                    : PromptEnglishNormalizer.NormalizeProviderFacing(text);
            }
            else if (node[key] is JsonObject child)
            {
                NormalizeStrings(child);
            }
            else if (node[key] is JsonArray array)
            {
                foreach (var item in array.OfType<JsonObject>()) NormalizeStrings(item);
            }
        }
    }
}
