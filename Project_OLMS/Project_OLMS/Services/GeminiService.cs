using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Project_OLMS.Models;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;

namespace Project_OLMS.Services
{
    public interface IGeminiService
    {
        /// <summary>Generates a concise AI summary of the PDF.</summary>
        Task<string?> AnalyzePdfAsync(byte[] pdfBytes, string fileName);

        /// <summary>
        /// Generates a 10-item multiple-choice examination questionnaire from the PDF.
        /// Returns a tuple: Questions (null on failure) and ErrorDetail (null on success).
        /// </summary>
        Task<(List<QuestionItem>? Questions, string? ErrorDetail)> GenerateExamAsync(
            byte[] pdfBytes, string fileName);
    }

    public class GeminiService : IGeminiService
    {
        private readonly string _apiKey;
        private readonly ILogger<GeminiService> _logger;
        private const string ModelId = "gemini-3.5-flash";
        private const int MaxInlineSizeBytes = 20 * 1024 * 1024;
        
        // We use IHttpClientFactory if available, otherwise fallback to a static client for simplicity
        // in this refactor to avoid changing DI registration right now.
        private static readonly HttpClient _httpClient = new HttpClient();

        // Case-insensitive deserialization to tolerate slight field-name variations from the AI
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public GeminiService(IConfiguration configuration, ILogger<GeminiService> logger)
        {
            _logger = logger;
            _apiKey = configuration["Gemini:ApiKey"]
                      ?? throw new InvalidOperationException("Gemini API key is not configured.");
        }

        // ── Direct HTTP Call to Gemini (Bypassing Mscc.GenerativeAI length validation) ──

        private async Task<string?> CallGeminiApiDirectlyAsync(string prompt, byte[] pdfBytes)
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{ModelId}:generateContent?key={_apiKey}";
            
            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new { text = prompt },
                            new
                            {
                                inline_data = new
                                {
                                    mime_type = "application/pdf",
                                    data = Convert.ToBase64String(pdfBytes)
                                }
                            }
                        }
                    }
                }
            };

            var jsonPayload = JsonSerializer.Serialize(payload);
            const int maxAttempts = 6;
            var random = new Random();

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                string? responseBody = null;
                try
                {
                    using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                    var response = await _httpClient.PostAsync(url, content);
                    responseBody = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        var statusCode = response.StatusCode;
                        _logger.LogWarning("Gemini API returned HTTP {StatusCode} on attempt {Attempt}/{MaxAttempts}.", statusCode, attempt, maxAttempts);

                        bool isRetryable = statusCode == System.Net.HttpStatusCode.TooManyRequests 
                                           || ((int)statusCode >= 500 && (int)statusCode <= 599);

                        if (isRetryable && attempt < maxAttempts)
                        {
                            int? parsedDelayMs = null;
                            if (statusCode == System.Net.HttpStatusCode.TooManyRequests && !string.IsNullOrEmpty(responseBody))
                            {
                                try
                                {
                                    using var errorDoc = JsonDocument.Parse(responseBody);
                                    if (errorDoc.RootElement.TryGetProperty("error", out var errorEl) &&
                                        errorEl.TryGetProperty("details", out var detailsEl) &&
                                        detailsEl.ValueKind == JsonValueKind.Array)
                                    {
                                        foreach (var detail in detailsEl.EnumerateArray())
                                        {
                                            if (detail.TryGetProperty("@type", out var typeEl) &&
                                                typeEl.GetString() == "type.googleapis.com/google.rpc.RetryInfo" &&
                                                detail.TryGetProperty("retryDelay", out var delayEl))
                                            {
                                                var delayStr = delayEl.GetString();
                                                if (!string.IsNullOrEmpty(delayStr) && delayStr.EndsWith("s"))
                                                {
                                                    var secondsStr = delayStr.Substring(0, delayStr.Length - 1);
                                                    if (double.TryParse(secondsStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double seconds))
                                                    {
                                                        parsedDelayMs = (int)(seconds * 1000);
                                                        break;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "Failed to parse RetryInfo from error body: {ResponseBody}", responseBody);
                                }
                            }

                            int delayMs;
                            if (parsedDelayMs.HasValue)
                            {
                                delayMs = parsedDelayMs.Value + random.Next(500, 1500);
                                _logger.LogWarning("Gemini API requested retry delay. Retrying in {DelayMs}ms...", delayMs);
                            }
                            else
                            {
                                delayMs = (int)(Math.Pow(2, attempt) * 1000) + random.Next(0, 1000);
                                _logger.LogWarning("Retrying Gemini API call (exponential backoff) in {DelayMs}ms...", delayMs);
                            }

                            await Task.Delay(delayMs);
                            continue;
                        }

                        _logger.LogError("Gemini API returned HTTP {StatusCode}: {ErrorBody}", statusCode, responseBody);
                        throw new Exception($"Gemini API HTTP Error {statusCode}. View server logs for details.");
                    }

                    using var jsonDoc = JsonDocument.Parse(responseBody);
                    try 
                    {
                        // Parse standard Gemini REST response
                        var text = jsonDoc.RootElement
                            .GetProperty("candidates")[0]
                            .GetProperty("content")
                            .GetProperty("parts")[0]
                            .GetProperty("text")
                            .GetString();
                            
                        return text?.Trim();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to parse Gemini success response: {ResponseBody}", responseBody);
                        throw new Exception("Could not parse the Gemini API response structure.");
                    }
                }
                catch (Exception ex) when (attempt < maxAttempts && (ex is HttpRequestException || ex is TaskCanceledException))
                {
                    _logger.LogWarning(ex, "HTTP request failed on attempt {Attempt}/{MaxAttempts}.", attempt, maxAttempts);
                    int delayMs = (int)(Math.Pow(2, attempt) * 1000) + random.Next(0, 1000);
                    _logger.LogWarning("Retrying Gemini API call after network/timeout error in {DelayMs}ms...", delayMs);
                    await Task.Delay(delayMs);
                }
            }

            throw new Exception("Gemini API call failed after multiple attempts.");
        }

        // ── Existing: PDF Summary ─────────────────────────────────────────────

        public async Task<string?> AnalyzePdfAsync(byte[] pdfBytes, string fileName)
        {
            if (pdfBytes.Length > MaxInlineSizeBytes)
                return "File is too large for inline AI analysis (> 20 MB).";

            try
            {
                string prompt = 
                    $"You are a document analyst. The user uploaded a PDF named '{fileName}'. " +
                    "Please provide a concise summary (3–5 sentences) covering " +
                    "the document's main content, key topics, and any important details.";

                return await CallGeminiApiDirectlyAsync(prompt, pdfBytes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[GeminiService] AI analysis failed for '{FileName}'", fileName);
                return null;
            }
        }

        // ── Exam Question Generator ───────────────────────────────────────────

        public async Task<(List<QuestionItem>? Questions, string? ErrorDetail)> GenerateExamAsync(
            byte[] pdfBytes, string fileName)
        {
            if (pdfBytes.Length > MaxInlineSizeBytes)
            {
                string sizeMsg = $"File '{fileName}' exceeds the 20 MB inline limit.";
                _logger.LogWarning("[GeminiService] {Msg}", sizeMsg);
                return (null, sizeMsg);
            }

            string? rawResponse = null;

            try
            {
                string prompt =
                    "You are an expert educator and examination writer. " +
                    $"A PDF document named '{fileName}' is attached. " +
                    "Read it carefully and create exactly 10 multiple-choice questions that test " +
                    "understanding of the document's content. " +
                    "Do not include phrases like 'According to the document', 'Based on the text', 'According to the PDF', or similar in the question text. Ask the questions directly. " +
                    "Your response must be ONLY a raw JSON array — no markdown, " +
                    "no code fences, no explanatory text before or after. " +
                    "Each element of the array must have EXACTLY these fields:\n" +
                    "  number        : integer 1-10\n" +
                    "  questionText  : string\n" +
                    "  optionA       : string\n" +
                    "  optionB       : string\n" +
                    "  optionC       : string\n" +
                    "  optionD       : string\n" +
                    "  correctAnswer : string — exactly one of: A, B, C, or D\n" +
                    "  explanation   : string — one sentence explaining why the answer is correct\n\n" +
                    "Start your response with [ and end with ].";

                _logger.LogInformation(
                    "[GeminiService] Sending exam-generation request for '{FileName}' directly to REST API...", fileName);

                rawResponse = await CallGeminiApiDirectlyAsync(prompt, pdfBytes);

                _logger.LogInformation(
                    "[GeminiService] Raw AI response for '{FileName}': {Preview}",
                    fileName,
                    rawResponse?.Length > 400 ? rawResponse[..400] + "…" : rawResponse ?? "(null)");

                // ── Guard: empty response ─────────────────────────────────
                if (string.IsNullOrWhiteSpace(rawResponse))
                {
                    const string emptyMsg = "Gemini returned an empty response.";
                    _logger.LogWarning("[GeminiService] {Msg} File='{FileName}'", emptyMsg, fileName);
                    return (null, emptyMsg);
                }

                // ── Extract the JSON array robustly ───────────────────────
                string? jsonArray = ExtractJsonArray(rawResponse);
                if (jsonArray == null)
                {
                    string noArrMsg = $"Could not locate a JSON array in the AI response. " +
                                      $"First 200 chars: {rawResponse[..Math.Min(200, rawResponse.Length)]}";
                    _logger.LogWarning("[GeminiService] {Msg}", noArrMsg);
                    return (null, noArrMsg);
                }

                // ── Parse ─────────────────────────────────────────────────
                var questions = JsonSerializer.Deserialize<List<QuestionItem>>(jsonArray, _jsonOptions);

                if (questions == null || questions.Count == 0)
                {
                    const string emptyListMsg = "JSON parsed OK but the question list is empty.";
                    _logger.LogWarning("[GeminiService] {Msg} File='{FileName}'", emptyListMsg, fileName);
                    return (null, emptyListMsg);
                }

                _logger.LogInformation(
                    "[GeminiService] Generated {Count} questions for '{FileName}'",
                    questions.Count, fileName);

                return (questions, null);
            }
            catch (JsonException jsonEx)
            {
                string detail = $"JSON parse error: {jsonEx.Message} | Raw: " +
                                (rawResponse?.Length > 300
                                    ? rawResponse[..300] + "…"
                                    : rawResponse ?? "(null)");
                _logger.LogError(jsonEx,
                    "[GeminiService] JSON parse failed for '{FileName}'. {Detail}", fileName, detail);
                return (null, detail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[GeminiService] Exam generation threw an exception for '{FileName}'", fileName);
                return (null, ex.Message);
            }
        }

        // ── Private Helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Finds the first [ … ] JSON array in the text, stripping any markdown
        /// fences or extra prose the model may have added.
        /// </summary>
        private static string? ExtractJsonArray(string text)
        {
            // Strip markdown fences: ```json ... ``` or ``` ... ```
            text = Regex.Replace(text, @"^```[a-zA-Z]*\s*", "", RegexOptions.Multiline).TrimStart();
            text = Regex.Replace(text, @"```\s*$", "", RegexOptions.Multiline).TrimEnd();

            int start = text.IndexOf('[');
            int end   = text.LastIndexOf(']');

            if (start < 0 || end < 0 || end <= start)
                return null;

            return text[start..(end + 1)].Trim();
        }
    }
}
