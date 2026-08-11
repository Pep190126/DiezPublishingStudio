using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DiezPublishingStudio;

internal static class VisionProviderAdapterFactory
{
    public static bool TryCreate(string? providerId, out IAiVisionValidationAdapter? adapter, out string message)
    {
        adapter = null;
        var provider = (providerId ?? string.Empty).Trim().ToLowerInvariant();
        switch (provider)
        {
            case PromptEngineeringProviderIds.OpenAi:
            {
                var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
                if (string.IsNullOrWhiteSpace(key))
                {
                    message = "Per il controllo Vision diretto OpenAI imposta OPENAI_API_KEY. Il flusso ZIP resta disponibile senza credenziali.";
                    return false;
                }
                var model = Environment.GetEnvironmentVariable("DIEZ_OPENAI_VISION_MODEL");
                adapter = new OpenAiVisionValidationAdapter(key.Trim(), model);
                message = $"Vision diretta OpenAI pronta ({adapter.ProviderId}).";
                return true;
            }
            case PromptEngineeringProviderIds.Gemini:
            {
                var key = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
                if (string.IsNullOrWhiteSpace(key))
                {
                    message = "Per il controllo Vision diretto Gemini imposta GEMINI_API_KEY. Il flusso ZIP resta disponibile senza credenziali.";
                    return false;
                }
                var model = Environment.GetEnvironmentVariable("DIEZ_GEMINI_VISION_MODEL");
                adapter = new GeminiVisionValidationAdapter(key.Trim(), model);
                message = $"Vision diretta Gemini pronta ({adapter.ProviderId}).";
                return true;
            }
            default:
                message = "Il provider selezionato non ha ancora un trasporto Vision API diretto. Usa il controllo Vision ZIP/manuale.";
                return false;
        }
    }
}

internal abstract class HttpVisionValidationAdapter : IAiVisionValidationAdapter
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    protected HttpVisionValidationAdapter(HttpClient? httpClient = null)
    {
        Http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
    }

    protected HttpClient Http { get; }
    public abstract string ProviderId { get; }
    public AiExchangeApiCapabilities Capabilities { get; } = new()
    {
        FileInput = true,
        StructuredOutput = true,
        VisionAnalysis = true
    };

    public abstract Task<VisionValidationResult> ValidateAsync(
        VisionValidationRequest request,
        string imagePath,
        CancellationToken cancellationToken = default);

    protected static async Task<(byte[] Bytes, string Mime)> ReadImageAsync(string imagePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            throw new FileNotFoundException("Il file reale della Candidate non è disponibile per Vision.", imagePath);
        var mime = Path.GetExtension(imagePath).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => throw new InvalidOperationException("Vision API diretta supporta PNG, JPEG e WEBP. Usa il flusso ZIP per altri formati.")
        };
        return (await File.ReadAllBytesAsync(imagePath, cancellationToken), mime);
    }

    protected static string BuildInspectorPrompt(VisionValidationRequest request)
    {
        var specification = JsonSerializer.Serialize(new
        {
            expected = request.Expected,
            generation_contract = request.GenerationContract
        }, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = true
        });

        return $"""
You are Diez Publishing Studio's independent semantic Vision QA inspector.
Inspect the REAL candidate image attached to this request. The image bytes are authoritative.
Never infer compliance from a filename, from a generator's description, or from the requested prompt alone.
Compare only what you actually see against the canonical specification below.

Rules:
- Evaluate subject_match, environment_match, must_do, must_not_do, book_type_fit, item_override_match, visible_text_or_watermark, anatomy_geometry, composition_readability, style_quality and publication_quality when applicable.
- HARD means an explicit semantic/content constraint. SOFT means a quality preference or judgement.
- One HARD FAIL requires overall_status = FAIL.
- Use REVIEW for genuine ambiguity. Never turn uncertainty into PASS.
- Deterministic Diez pixel/size checks are authoritative for measurable constraints; do not claim to overrule them.
- observed_description must independently describe what is visibly present in the image.
- Do not return project IDs, version IDs, work-unit IDs, candidate versions or hashes. Diez owns identity binding outside the model response.
- Direct single-image mode does not attach sibling candidates. If series consistency cannot be judged from this image alone, return series_consistency = NA/SOFT rather than inventing evidence.
- Return only data conforming to the requested structured schema.

CANONICAL SPECIFICATION
{specification}
""";
    }

    protected static object SemanticSchema() => new
    {
        type = "object",
        additionalProperties = false,
        required = new[] { "overall_status", "confidence", "observed_description", "summary", "checks" },
        properties = new
        {
            overall_status = new { type = "string", @enum = new[] { "PASS", "FAIL", "REVIEW" } },
            confidence = new { type = "number", minimum = 0, maximum = 1 },
            observed_description = new { type = "string" },
            summary = new { type = "string" },
            checks = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = new[] { "key", "status", "severity", "confidence", "evidence" },
                    properties = new
                    {
                        key = new { type = "string" },
                        status = new { type = "string", @enum = new[] { "PASS", "FAIL", "WARN", "NA" } },
                        severity = new { type = "string", @enum = new[] { "HARD", "SOFT" } },
                        confidence = new { type = "number", minimum = 0, maximum = 1 },
                        evidence = new { type = "string" }
                    }
                }
            }
        }
    };

    protected static VisionValidationResult BindTrustedIdentity(
        VisionValidationRequest request,
        string providerId,
        string semanticJson)
    {
        SemanticResult? semantic;
        try
        {
            semantic = JsonSerializer.Deserialize<SemanticResult>(semanticJson, ReadOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Il provider Vision non ha restituito JSON semantico valido.", ex);
        }
        if (semantic is null)
            throw new InvalidDataException("Il provider Vision ha restituito una risposta semantica vuota.");

        return new VisionValidationResult
        {
            VersionId = request.VersionId,
            WorkUnitId = request.WorkUnitId,
            CandidateVersion = request.CandidateVersion,
            ContentSha256 = request.ContentSha256,
            ProviderId = providerId,
            OverallStatus = semantic.OverallStatus,
            Confidence = semantic.Confidence,
            ObservedDescription = semantic.ObservedDescription,
            Summary = semantic.Summary,
            Checks = semantic.Checks ?? []
        };
    }

    protected static async Task<string> ReadErrorBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        body = body.Replace("\r", " ").Replace("\n", " ").Trim();
        return body.Length > 700 ? body[..700] + "…" : body;
    }

    protected sealed class SemanticResult
    {
        public string OverallStatus { get; set; } = VisionValidationStatuses.Review;
        public double Confidence { get; set; }
        public string ObservedDescription { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public List<VisionValidationCheck> Checks { get; set; } = [];
    }
}

internal sealed class OpenAiVisionValidationAdapter : HttpVisionValidationAdapter
{
    private readonly string _apiKey;
    private readonly string _model;

    public OpenAiVisionValidationAdapter(string apiKey, string? model = null, HttpClient? httpClient = null)
        : base(httpClient)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("OPENAI_API_KEY mancante.", nameof(apiKey));
        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? "gpt-5" : model.Trim();
    }

    public override string ProviderId => "openai:" + _model;

    public override async Task<VisionValidationResult> ValidateAsync(
        VisionValidationRequest request,
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        var (bytes, mime) = await ReadImageAsync(imagePath, cancellationToken);
        var dataUrl = $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
        var payload = new
        {
            model = _model,
            input = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "input_text", text = BuildInspectorPrompt(request) },
                        new { type = "input_image", image_url = dataUrl, detail = "high" }
                    }
                }
            },
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "diez_vision_validation",
                    strict = true,
                    schema = SemanticSchema()
                }
            }
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        message.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await Http.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await ReadErrorBodyAsync(response, cancellationToken);
            throw new HttpRequestException($"OpenAI Vision HTTP {(int)response.StatusCode} ({response.StatusCode}): {detail}", null, response.StatusCode);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var semanticJson = ExtractOpenAiOutputText(body);
        return BindTrustedIdentity(request, ProviderId, semanticJson);
    }

    private static string ExtractOpenAiOutputText(string body)
    {
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("output_text", out var direct) && direct.ValueKind == JsonValueKind.String)
            return direct.GetString() ?? string.Empty;
        if (doc.RootElement.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) continue;
                foreach (var part in content.EnumerateArray())
                {
                    var type = part.TryGetProperty("type", out var typeNode) ? typeNode.GetString() : null;
                    if (!string.Equals(type, "output_text", StringComparison.OrdinalIgnoreCase)) continue;
                    if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                        return text.GetString() ?? string.Empty;
                }
            }
        }
        throw new InvalidDataException("OpenAI Responses non contiene output_text Vision utilizzabile.");
    }
}

internal sealed class GeminiVisionValidationAdapter : HttpVisionValidationAdapter
{
    private readonly string _apiKey;
    private readonly string _model;

    public GeminiVisionValidationAdapter(string apiKey, string? model = null, HttpClient? httpClient = null)
        : base(httpClient)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("GEMINI_API_KEY mancante.", nameof(apiKey));
        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? "gemini-3.6-flash" : model.Trim();
    }

    public override string ProviderId => "gemini:" + _model;

    public override async Task<VisionValidationResult> ValidateAsync(
        VisionValidationRequest request,
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        var (bytes, mime) = await ReadImageAsync(imagePath, cancellationToken);
        var payload = new
        {
            model = _model,
            store = false,
            input = new object[]
            {
                new { type = "text", text = BuildInspectorPrompt(request) },
                new { type = "image", data = Convert.ToBase64String(bytes), mime_type = mime }
            },
            response_format = new
            {
                type = "text",
                mime_type = "application/json",
                schema = SemanticSchema()
            }
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "https://generativelanguage.googleapis.com/v1beta/interactions");
        message.Headers.TryAddWithoutValidation("x-goog-api-key", _apiKey);
        message.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await Http.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await ReadErrorBodyAsync(response, cancellationToken);
            throw new HttpRequestException($"Gemini Vision HTTP {(int)response.StatusCode} ({response.StatusCode}): {detail}", null, response.StatusCode);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var semanticJson = ExtractGeminiText(body);
        return BindTrustedIdentity(request, ProviderId, semanticJson);
    }

    private static string ExtractGeminiText(string body)
    {
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("output_text", out var direct) && direct.ValueKind == JsonValueKind.String)
            return direct.GetString() ?? string.Empty;
        if (doc.RootElement.TryGetProperty("steps", out var steps) && steps.ValueKind == JsonValueKind.Array)
        {
            foreach (var step in steps.EnumerateArray().Reverse())
            {
                if (!step.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) continue;
                foreach (var part in content.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                        return text.GetString() ?? string.Empty;
                }
            }
        }
        throw new InvalidDataException("Gemini Interactions non contiene testo Vision strutturato utilizzabile.");
    }
}

internal static class VisionValidationDirectService
{
    public static async Task<VisionValidationStore.Record> ValidateAsync(
        PreviewProject project,
        string projectPath,
        AiExchangeState exchange,
        Guid versionId,
        IAiVisionValidationAdapter adapter,
        CancellationToken cancellationToken = default)
    {
        if (!adapter.Capabilities.VisionAnalysis || !adapter.Capabilities.FileInput)
            throw new InvalidOperationException("L'adapter selezionato non dichiara VisionAnalysis + FileInput.");

        var version = exchange.Versions.FirstOrDefault(v => v.VersionId == versionId)
            ?? throw new InvalidOperationException("Candidate Vision non trovata nello stato AI Exchange.");
        var unit = exchange.WorkUnits.FirstOrDefault(u => u.WorkUnitId == version.WorkUnitId)
            ?? throw new InvalidOperationException("Work Unit della Candidate Vision non trovata.");
        if (!string.Equals(unit.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Il controllo Vision diretto richiede una Work Unit immagine.");
        if (version.MaterialId is not Guid materialId)
            throw new InvalidOperationException("La Candidate non contiene un file immagine reale.");
        var material = project.Materials.FirstOrDefault(m => m.MaterialId == materialId)
            ?? throw new InvalidOperationException("Il materiale immagine della Candidate non è presente nel progetto.");
        var bytes = await ProjectFileStore.ReadEmbeddedMaterialAsync(projectPath, material);
        if (bytes is null || bytes.Length == 0)
            throw new InvalidOperationException("Il file immagine reale della Candidate non è leggibile dal progetto.");

        var actualSha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(version.ContentSha256) ||
            !string.Equals(actualSha, version.ContentSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Hash della Candidate non coerente con i byte reali: Vision non eseguita su un'identità non certa.");

        var activeLegacy = VisualPromptSessionService.ActiveLegacyJobIds(project);
        var seriesCount = Math.Max(1, exchange.WorkUnits.Count(u =>
            string.Equals(u.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase) &&
            (!u.LegacyAiJobId.HasValue || activeLegacy.Contains(u.LegacyAiJobId.Value))));
        var providerTarget = PromptPreparationSettingsStore.Load(project).ProviderId;
        var tempRoot = Path.Combine(Path.GetTempPath(), "DiezVisionDirect-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var ext = NormalizeExtension(material.FileName);
        var tempFile = Path.Combine(tempRoot, "candidate" + ext);
        try
        {
            await File.WriteAllBytesAsync(tempFile, bytes, cancellationToken);
            var request = VisionValidationSpecificationBuilder.Build(
                project, exchange, unit, version, Guid.NewGuid(), tempFile, seriesCount, providerTarget);
            var result = await adapter.ValidateAsync(request, tempFile, cancellationToken);
            if (result.VersionId != request.VersionId ||
                result.WorkUnitId != request.WorkUnitId ||
                result.CandidateVersion != request.CandidateVersion ||
                !string.Equals(result.ContentSha256, request.ContentSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("L'adapter Vision ha restituito un'identità Candidate diversa da quella affidata da Diez.");

            VisionValidationStore.Apply(project, exchange, result);
            AiExchangeStateStore.Save(project, exchange);
            await ProjectFileStore.SaveAsync(projectPath, project);
            return VisionValidationStore.Get(project, version.VersionId)
                ?? throw new InvalidOperationException("Esito Vision non persistito.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    private static string NormalizeExtension(string? fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".png" => ".png",
        ".jpg" or ".jpeg" => ".jpg",
        ".webp" => ".webp",
        var other when !string.IsNullOrWhiteSpace(other) => other,
        _ => ".bin"
    };
}
