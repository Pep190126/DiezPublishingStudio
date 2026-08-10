using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiezPublishingStudio;

/// <summary>
/// Adds a normalized, provider-neutral effective_presets object to request-context.json.
/// Raw profile JSON remains available, but critical visual choices are also exposed directly
/// so an adapter/AI never has to infer them from UI-specific persistence structures.
/// </summary>
internal static class AiExchangeExplicitVisualPresetContext
{
    private const string ContextName = "request-context.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static void Ensure(string promptPackPath, PreviewProject project)
    {
        using var archive = ZipFile.Open(promptPackPath, ZipArchiveMode.Update);
        var entry = archive.GetEntry(ContextName) ?? throw new InvalidDataException("request-context.json mancante.");
        string text;
        using (var stream = entry.Open())
        using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            text = reader.ReadToEnd();
        var root = JsonNode.Parse(text)?.AsObject() ?? throw new InvalidDataException("request-context.json non leggibile.");

        var type = BookTypeProfileService.Get(project);
        var effective = new JsonObject { ["book_type"] = type };

        if (string.Equals(type, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase))
        {
            var p = BookTypePromptProfileService.LoadColoring(project);
            effective["subject"] = p.SubjectDescription;
            effective["environment"] = p.EnvironmentDescription;
            effective["style"] = p.Style;
            effective["target_audience"] = p.TargetAudience;
            effective["difficulty"] = p.Difficulty;
            effective["line_weight"] = p.LineWeight;
            effective["complexity"] = p.Complexity;
            effective["element_density"] = p.ElementDensity;
            effective["background"] = p.Background;
            effective["white_space"] = p.WhiteSpace;
            effective["color_mode"] = "Bianco e nero puro — esclusivamente #000000 e #FFFFFF";
            effective["closed_areas"] = p.ClosedAreas;
            effective["clean_contours"] = p.CleanContours;
            effective["avoid_tiny_areas"] = p.AvoidTinyAreas;
        }
        else if (string.Equals(type, BookTypeProfileService.ImageCollection, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(type, BookTypeProfileService.IllustratedBook, StringComparison.OrdinalIgnoreCase))
        {
            var p = ImageCollectionPromptProfileService.Load(project);
            effective["subject"] = p.SubjectDescription;
            effective["environment"] = p.EnvironmentDescription;
            effective["editorial_use"] = p.EditorialUse;
            effective["color_mode"] = p.ColorMode;
            effective["detail_level"] = p.DetailLevel;
            effective["line_treatment"] = p.LineTreatment;
            effective["rendering_style"] = p.RenderingStyle;
            effective["background"] = p.Background;
            effective["viewpoint"] = p.Viewpoint;
        }

        var technical = ReadEntityObject(project, "DiezImageGenerationSpecs");
        effective["page_preset"] = Value(technical, "PresetId");
        effective["width"] = Value(technical, "Width");
        effective["height"] = Value(technical, "Height");
        effective["unit"] = Value(technical, "Unit");
        effective["orientation"] = Value(technical, "Orientation");
        effective["aspect_ratio"] = Value(technical, "AspectRatio");
        var resolutionId = Value(technical, "ResolutionClassId");
        effective["resolution_class_id"] = resolutionId;
        effective["resolution_class"] = ResolutionLabel(resolutionId);
        effective["pixel_width"] = Value(technical, "PixelWidth");
        effective["pixel_height"] = Value(technical, "PixelHeight");
        effective["dpi"] = Value(technical, "Dpi");
        effective["render_quality"] = Value(technical, "Quality");
        effective["technical_detail"] = Value(technical, "LineDetail");
        effective["safe_margin"] = Value(technical, "SafeMargin");
        effective["bleed"] = BoolValue(technical, "Bleed");
        effective["bleed_amount"] = Value(technical, "BleedAmount");
        effective["consistent_rules"] = ImageCollectionWorkspaceService.GetConsistencyRules(project) ?? string.Empty;
        effective["human_readable_visual_prompt"] = AiExchangeImageRequestContextService.BuildEffectiveVisualPrompt(project);

        var imagePresets = root["image_presets"] as JsonObject ?? new JsonObject();
        imagePresets["effective_presets"] = effective;
        root["image_presets"] = imagePresets;

        entry.Delete();
        var replacement = archive.CreateEntry(ContextName, CompressionLevel.Optimal);
        using var target = replacement.Open();
        using var writer = new StreamWriter(target, new UTF8Encoding(false));
        writer.Write(root.ToJsonString(JsonOptions));
    }

    private static JsonObject ReadEntityObject(PreviewProject project, string kind)
    {
        var notes = project.Entities.FirstOrDefault(e => string.Equals(e.Kind, kind, StringComparison.OrdinalIgnoreCase))?.Notes;
        if (string.IsNullOrWhiteSpace(notes)) return new JsonObject();
        try { return JsonNode.Parse(notes)?.AsObject() ?? new JsonObject(); }
        catch { return new JsonObject(); }
    }

    private static string Value(JsonObject obj, string key)
    {
        var node = Find(obj, key);
        return node?.ToString() ?? string.Empty;
    }

    private static bool BoolValue(JsonObject obj, string key)
    {
        var text = Value(obj, key);
        return bool.TryParse(text, out var value) && value;
    }

    private static JsonNode? Find(JsonObject obj, string key) =>
        obj.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)).Value;

    private static string ResolutionLabel(string id) => id.Trim().ToLowerInvariant() switch
    {
        "hd" => "HD — lato lungo 1280 px",
        "fhd" or "fullhd" => "Full HD — lato lungo 1920 px",
        "2k" => "2K — lato lungo 2560 px",
        "4k" => "4K UHD — lato lungo 3840 px",
        "8k" => "8K UHD — lato lungo 7680 px",
        "print" => "Stampa — dimensioni fisiche × DPI",
        "custom" => "Personalizzata — usa i pixel indicati",
        _ => id
    };
}
