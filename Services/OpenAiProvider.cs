using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Learnit.Server.Services
{
    public class OpenAiProvider : IAiProvider
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _config;

        public OpenAiProvider(HttpClient http, IConfiguration config)
        {
            _http = http;
            _config = config;
        }

        public async Task<string> GenerateAsync(string systemPrompt, string userPrompt, IEnumerable<AiMessage>? history = null, CancellationToken cancellationToken = default)
        {
            // Prefer Groq if configured, otherwise fall back to OpenAI keys.
            var apiKey = _config["Groq:ApiKey"]
                         ?? _config["GROQ_API_KEY"]
                         ?? _config["OpenAi:ApiKey"]
                         ?? _config["OPENAI_API_KEY"];

            // Default to a Groq-hosted model; callers can override via config.
            var model = _config["Groq:Model"]
                        ?? _config["OpenAi:Model"]
                        ?? "llama-3.1-8b-instant";

            var baseUrl = _config["Groq:BaseUrl"]
                          ?? "https://api.groq.com/openai/v1/chat/completions";

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return BuildStubResponse(userPrompt);
            }

            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var messages = new List<Dictionary<string, string>>
            {
                new() { { "role", "system" }, { "content", systemPrompt } },
            };

            if (history != null)
            {
                messages.AddRange(history.Select(h => new Dictionary<string, string>
                {
                    { "role", h.Role },
                    { "content", h.Content }
                }));
            }

            messages.Add(new Dictionary<string, string>
            {
                { "role", "user" },
                { "content", userPrompt }
            });

            var isCourseJson = systemPrompt.Contains("MINIFIED JSON", StringComparison.OrdinalIgnoreCase)
                || systemPrompt.Contains("pure JSON", StringComparison.OrdinalIgnoreCase);

            var maxTokens = 600;
            if (int.TryParse(_config["Ai:MaxTokens"], out var configuredMax) && configuredMax > 0)
            {
                maxTokens = configuredMax;
            }
            if (isCourseJson)
            {
                maxTokens = 2000;
                if (int.TryParse(_config["Ai:MaxTokensCourse"], out var configuredCourseMax) && configuredCourseMax > 0)
                {
                    maxTokens = configuredCourseMax;
                }
            }

            var payload = new
            {
                model,
                messages,
                temperature = 0.4,
                max_tokens = maxTokens
            };

            var response = await _http.PostAsync(baseUrl,
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(cancellationToken);
                // Gracefully fall back to stubbed reply when quota is exceeded or we are being throttled.
                if (response.StatusCode == HttpStatusCode.TooManyRequests || err.Contains("insufficient_quota", StringComparison.OrdinalIgnoreCase))
                {
                    return BuildStubResponse(userPrompt);
                }

                throw new InvalidOperationException($"OpenAI error: {response.StatusCode} {err}");
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var content = doc.RootElement.GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return content ?? BuildStubResponse(userPrompt);
        }

        private static string BuildStubResponse(string prompt)
        {
            var trimmed = prompt.Length > 180 ? prompt.Substring(0, 180) + "..." : prompt;
            return $"(Stubbed AI response) Key not set. Asked: '{trimmed}'. Sample answer: Focus on one course, schedule 2h blocks, and finish your next module this week.";
        }
    }
}
