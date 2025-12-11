using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace UniMarket.Services
{
    /// <summary>
    /// Thin HTTP client wrapper to call external LLM providers (OpenAI / Gemini-like endpoints).
    /// Config via appsettings.json under `AI` section:
    /// - Provider: "OpenAI" (default) or "Gemini"
    /// - OpenAiApiKey, OpenAiModel
    /// - GeminiApiKey, GeminiEndpoint
    /// </summary>
    public class AiClient
    {
        private readonly IHttpClientFactory _httpFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<AiClient> _logger;

        public AiClient(IHttpClientFactory httpFactory, IConfiguration config, ILogger<AiClient> logger)
        {
            _httpFactory = httpFactory;
            _config = config;
            _logger = logger;
        }

        public async Task<string> SendPromptAsync(string prompt)
        {
            var provider = _config["AI:Provider"] ?? "OpenAI";

            if (provider.Equals("Demo", StringComparison.OrdinalIgnoreCase))
            {
                // Demo/mock mode for testing without API keys
                _logger.LogInformation("Using Demo mode for AI - returning mock response");
                
                // Return a mock intent response for testing
                var mockResponse = @"{
  ""keywords"": [""iphone 13 pro max""],
  ""minPrice"": null,
  ""maxPrice"": null,
  ""requireVideo"": false,
  ""condition"": null,
  ""sort"": ""price_desc"",
  ""categoryId"": null,
  ""confidence"": 0.85,
  ""shouldSearch"": true,
  ""userReply"": ""Dạ em hiểu, để em tìm những chiếc iPhone 13 Pro Max cho bạn nhé."",
  ""clarifyingQuestion"": null
}";
                
                // Simulate API delay
                await Task.Delay(500);
                return mockResponse;
            }

            if (provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
            {
                var key = _config["AI:OpenAiApiKey"];
                var model = _config["AI:OpenAiModel"] ?? "gpt-3.5-turbo";

                if (string.IsNullOrWhiteSpace(key))
                    throw new InvalidOperationException("OpenAI API key is not configured (AI:OpenAiApiKey).");

                var client = _httpFactory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

                var payload = new
                {
                    model = model,
                    messages = new[] { new { role = "user", content = prompt } },
                    max_tokens = 512,
                    temperature = 0.2
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var resp = await client.PostAsync("https://api.openai.com/v1/chat/completions", content);
                var body = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogError("OpenAI call failed: {Status} {Body}", resp.StatusCode, body);
                    throw new HttpRequestException($"OpenAI request failed: {resp.StatusCode}");
                }

                // Try to extract textual answer from known fields
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                    {
                        var first = choices[0];
                        if (first.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var contentEl))
                            return contentEl.GetString() ?? body;

                        if (first.TryGetProperty("text", out var textEl))
                            return textEl.GetString() ?? body;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed parsing OpenAI response, returning raw body.");
                }

                return body;
            }

            if (provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
            {
                // Google Generative AI (Gemini) API call. Uses v1beta/generateContent endpoint.
                var key = _config["AI:GeminiApiKey"];
                var endpoint = _config["AI:GeminiEndpoint"]; // e.g. https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash

                if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(key))
                    throw new InvalidOperationException("Gemini endpoint/key not configured (AI:GeminiEndpoint/AI:GeminiApiKey)");

                var client = _httpFactory.CreateClient();

                // Build request URL: append :generateContent if not already present, then add API key
                var endpointWithAction = endpoint.EndsWith(":generateContent") ? endpoint : $"{endpoint}:generateContent";
                var urlWithKey = endpointWithAction.Contains("?") ? $"{endpointWithAction}&key={key}" : $"{endpointWithAction}?key={key}";

                _logger.LogInformation("[AI] Calling Gemini at: {endpoint}", endpointWithAction.Replace(key, "***"));

                // Gemini API expects: { "contents": [{"role": "user", "parts": [{"text": "..."}]}] }
                var payload = new
                {
                    contents = new[]
                    {
                        new
                        {
                            role = "user",
                            parts = new[] { new { text = prompt } }
                        }
                    },
                    generationConfig = new
                    {
                        temperature = 0.2f,
                        topK = 40,
                        topP = 0.95f,
                        maxOutputTokens = 512
                    }
                };

                var jsonPayload = JsonSerializer.Serialize(payload);
                _logger.LogDebug("Gemini request payload: {payload}", jsonPayload);
                
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                var resp = await client.PostAsync(urlWithKey, content);
                var body = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogError("Gemini call failed: Status={Status} Body={Body}", resp.StatusCode, body);
                    throw new HttpRequestException($"Gemini request failed: {resp.StatusCode} - {body}");
                }

                // Attempt to parse Gemini response. Expected format:
                // {
                //   "candidates": [{
                //     "content": {
                //       "parts": [{"text": "..."}],
                //       "role": "model"
                //     }
                //   }]
                // }
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    
                    // Try Gemini format: candidates[0].content.parts[0].text
                    if (doc.RootElement.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                    {
                        var cand = candidates[0];
                        if (cand.TryGetProperty("content", out var contentEl) && contentEl.TryGetProperty("parts", out var parts) && parts.GetArrayLength() > 0)
                        {
                            var firstPart = parts[0];
                            if (firstPart.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
                            {
                                var result = textEl.GetString();
                                _logger.LogDebug("Gemini response text extracted: {length} chars", result?.Length ?? 0);
                                return result ?? body;
                            }
                        }
                    }

                    // Fallback for older Gemini format or alternate response structure
                    if (doc.RootElement.TryGetProperty("candidates", out candidates) && candidates.GetArrayLength() > 0)
                    {
                        var cand = candidates[0];
                        if (cand.TryGetProperty("content", out var contentEl))
                        {
                            if (contentEl.ValueKind == JsonValueKind.String)
                                return contentEl.GetString() ?? body;
                        }
                    }
                    
                    if (doc.RootElement.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.String)
                        return output.GetString() ?? body;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed parsing Gemini response, returning raw body. Body length: {length}", body?.Length ?? 0);
                }

                return body ?? string.Empty;
            }

            throw new InvalidOperationException($"Unknown AI provider: {provider}");
        }
    }
}
