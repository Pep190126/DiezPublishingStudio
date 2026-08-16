using System.Net;
using System.Text;
using System.Text.Json;

namespace DiezPublishingStudio;

internal static class VisionProviderAdapterSelfTest
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "DiezVisionProviderSelfTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var image = Path.Combine(root, "candidate.png");
            await File.WriteAllBytesAsync(image, [137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3, 4]);
            var request = new VisionValidationRequest
            {
                ProjectId = Guid.NewGuid(),
                ValidationPackId = Guid.NewGuid(),
                VersionId = Guid.NewGuid(),
                WorkUnitId = Guid.NewGuid(),
                CandidateVersion = 3,
                ContentSha256 = "abc123",
                CandidateFile = image,
                ProviderTarget = PromptEngineeringProviderIds.OpenAi,
                GenerationContract = "Create exactly one elephant coloring page in a jungle.",
                Expected = new VisionExpectedSpecification
                {
                    BookType = BookTypeProfileService.ColoringBook,
                    Code = "IMG-003",
                    Subject = "elephant",
                    Environment = "jungle",
                    MustNotDo = "no text",
                    HardCriteria = ["Visible subject must be an elephant."]
                }
            };

            var semantic = JsonSerializer.Serialize(new
            {
                overall_status = "FAIL",
                confidence = 0.98,
                observed_description = "A lakeside village at sunset; no elephant is visible.",
                summary = "Wrong subject and environment.",
                version_id = Guid.NewGuid(),
                content_sha256 = "provider-must-not-control-this",
                checks = new object[]
                {
                    new
                    {
                        key = "subject_match",
                        status = "FAIL",
                        severity = "HARD",
                        confidence = 0.99,
                        evidence = "No elephant is visible."
                    },
                    new
                    {
                        key = "series_consistency",
                        status = "NA",
                        severity = "SOFT",
                        confidence = 0.5,
                        evidence = "No sibling images were attached."
                    }
                }
            });

            string? openAiBody = null;
            string? openAiAuth = null;
            var openAiHandler = new CapturingHandler(async message =>
            {
                openAiAuth = message.Headers.Authorization?.ToString();
                openAiBody = await message.Content!.ReadAsStringAsync();
                var envelope = JsonSerializer.Serialize(new
                {
                    output = new object[]
                    {
                        new
                        {
                            type = "message",
                            content = new object[] { new { type = "output_text", text = semantic } }
                        }
                    }
                });
                return Json(envelope);
            });
            var openAi = new OpenAiVisionValidationAdapter("test-openai-secret", "vision-test-openai", new HttpClient(openAiHandler));
            var openAiResult = await openAi.ValidateAsync(request, image);
            Require(openAiHandler.LastRequestUri == "https://api.openai.com/v1/responses", "OpenAI Vision non usa Responses API.");
            Require(string.Equals(openAiAuth, "Bearer test-openai-secret", StringComparison.Ordinal), "OpenAI API key non inviata nell'Authorization header previsto.");
            Require(openAiBody?.Contains("\"input_image\"", StringComparison.Ordinal) == true &&
                    openAiBody.Contains("data:image/png;base64,", StringComparison.Ordinal),
                "OpenAI Vision non allega i byte reali come input_image.");
            Require(openAiBody.Contains("\"json_schema\"", StringComparison.Ordinal) &&
                    openAiBody.Contains("Never infer compliance", StringComparison.Ordinal),
                "OpenAI Vision non richiede structured output o perde il trust boundary semantico.");
            AssertTrustedIdentity(request, openAiResult, "OpenAI");
            Require(openAiResult.BlocksApproval, "OpenAI HARD FAIL non blocca l'approvazione.");

            string? geminiBody = null;
            string? geminiKey = null;
            var geminiHandler = new CapturingHandler(async message =>
            {
                geminiKey = message.Headers.TryGetValues("x-goog-api-key", out var values) ? values.SingleOrDefault() : null;
                geminiBody = await message.Content!.ReadAsStringAsync();
                var envelope = JsonSerializer.Serialize(new
                {
                    steps = new object[]
                    {
                        new
                        {
                            type = "model_output",
                            content = new object[] { new { type = "text", text = semantic } }
                        }
                    }
                });
                return Json(envelope);
            });
            var gemini = new GeminiVisionValidationAdapter("test-gemini-secret", "vision-test-gemini", new HttpClient(geminiHandler));
            var geminiResult = await gemini.ValidateAsync(request, image);
            Require(geminiHandler.LastRequestUri == "https://generativelanguage.googleapis.com/v1beta/interactions", "Gemini Vision non usa Interactions API.");
            Require(string.Equals(geminiKey, "test-gemini-secret", StringComparison.Ordinal), "Gemini API key non inviata nell'x-goog-api-key header previsto.");
            Require(geminiBody?.Contains("\"type\":\"image\"", StringComparison.Ordinal) == true &&
                    geminiBody.Contains("\"mime_type\":\"image/png\"", StringComparison.Ordinal) &&
                    geminiBody.Contains("\"response_format\"", StringComparison.Ordinal),
                "Gemini Vision non allega l'immagine inline o non richiede structured output.");
            AssertTrustedIdentity(request, geminiResult, "Gemini");
            Require(geminiResult.BlocksApproval, "Gemini HARD FAIL non blocca l'approvazione.");

            Require(!openAiBody!.Contains(request.VersionId.ToString("D"), StringComparison.OrdinalIgnoreCase) &&
                    !geminiBody!.Contains(request.VersionId.ToString("D"), StringComparison.OrdinalIgnoreCase),
                "L'identità interna della Candidate è stata inviata inutilmente al modello Vision.");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static void AssertTrustedIdentity(VisionValidationRequest expected, VisionValidationResult actual, string provider)
    {
        Require(actual.VersionId == expected.VersionId, $"{provider}: il modello ha potuto alterare version_id.");
        Require(actual.WorkUnitId == expected.WorkUnitId, $"{provider}: il modello ha potuto alterare work_unit_id.");
        Require(actual.CandidateVersion == expected.CandidateVersion, $"{provider}: il modello ha potuto alterare candidate_version.");
        Require(string.Equals(actual.ContentSha256, expected.ContentSha256, StringComparison.Ordinal), $"{provider}: il modello ha potuto alterare content_sha256.");
        Require(actual.Checks.Any(c => c.Key == "subject_match" && c.Status == VisionCheckStatuses.Fail && c.Severity == VisionSeverity.Hard),
            $"{provider}: il FAIL semantico strutturato non è stato normalizzato.");
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Vision provider self-test: " + message);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;
        public string LastRequestUri { get; private set; } = string.Empty;

        public CapturingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) => _handler = handler;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri?.ToString() ?? string.Empty;
            return await _handler(request);
        }
    }
}
