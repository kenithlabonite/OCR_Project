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

        /// <summary>
        /// Scans an image of a student answer sheet and extracts question numbers and student answers using Gemini Vision.
        /// </summary>
        Task<(Dictionary<int, string>? Answers, string RawText, string? ErrorDetail)> ScanAnswerSheetAsync(
            byte[] imageBytes, string mimeType);

        /// <summary>
        /// Scans multiple images of a multi-page student answer sheet and extracts question numbers and answers across all pages using Gemini Vision.
        /// </summary>
        Task<(Dictionary<int, string>? Answers, string RawText, string? ErrorDetail)> ScanAnswerSheetsAsync(
            List<(byte[] Bytes, string MimeType)> imageFiles);
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

        private async Task<string?> CallGeminiApiDirectlyAsync(string prompt, byte[] fileBytes, string mimeType = "application/pdf")
        {
            return await CallGeminiApiForMultipleImagesAsync(prompt, new List<(byte[] Bytes, string MimeType)> { (fileBytes, mimeType) });
        }

        private async Task<string?> CallGeminiApiForMultipleImagesAsync(string prompt, List<(byte[] Bytes, string MimeType)> files)
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{ModelId}:generateContent?key={_apiKey}";
            
            var partsList = new List<object>
            {
                new { text = prompt }
            };

            foreach (var file in files)
            {
                partsList.Add(new
                {
                    inline_data = new
                    {
                        mime_type = string.IsNullOrWhiteSpace(file.MimeType) ? "image/jpeg" : file.MimeType,
                        data = Convert.ToBase64String(file.Bytes)
                    }
                });
            }

            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = partsList.ToArray()
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
                            int delayMs = (int)(Math.Pow(2, attempt) * 1000) + random.Next(0, 1000);
                            await Task.Delay(delayMs);
                            continue;
                        }

                        _logger.LogError("Gemini API returned HTTP {StatusCode}: {ErrorBody}", statusCode, responseBody);
                        throw new Exception($"Gemini API HTTP Error {statusCode}. View server logs for details.");
                    }

                    using var jsonDoc = JsonDocument.Parse(responseBody);
                    try 
                    {
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

        /// <summary>
        /// Finds the first { … } JSON object in the text, stripping any markdown
        /// fences or extra prose the model may have added.
        /// </summary>
        private static string? ExtractJsonObject(string text)
        {
            text = Regex.Replace(text, @"^```[a-zA-Z]*\s*", "", RegexOptions.Multiline).TrimStart();
            text = Regex.Replace(text, @"```\s*$", "", RegexOptions.Multiline).TrimEnd();

            int start = text.IndexOf('{');
            int end   = text.LastIndexOf('}');

            if (start < 0 || end < 0 || end <= start)
                return null;

            return text[start..(end + 1)].Trim();
        }

        // ── Answer Sheet Scanner (Gemini Vision) ──────────────────────────────

        public async Task<(Dictionary<int, string>? Answers, string RawText, string? ErrorDetail)> ScanAnswerSheetAsync(
            byte[] imageBytes, string mimeType)
        {
            if (imageBytes.Length > MaxInlineSizeBytes)
            {
                return (null, string.Empty, "Image file exceeds maximum allowed size (20 MB).");
            }

            try
            {
                string prompt =
                    "You are an automated exam answer sheet scanner and grader AI. " +
                    "An image of a student's answer sheet or questionnaire is attached. " +
                    "Carefully examine each question item (e.g. 1, 2, 3...) and find the student's handwritten or selected answer letter (A, B, C, or D).\n" +
                    "Note: Answers are written on blank lines before item numbers (e.g. 'B 1.', 'A 2.', 'b 5.'), " +
                    "written next to question text, or marked among the options.\n" +
                    "Return your analysis strictly as a JSON object — no markdown, no code blocks, no prose.\n" +
                    "JSON Structure:\n" +
                    "{\n" +
                    "  \"answers\": [\n" +
                    "    { \"questionNumber\": 1, \"scannedAnswer\": \"B\" },\n" +
                    "    { \"questionNumber\": 2, \"scannedAnswer\": \"B\" }\n" +
                    "  ],\n" +
                    "  \"rawExtractedText\": \"A short transcribed summary of the text and answers recognized on the page.\"\n" +
                    "}\n\n" +
                    "Rules:\n" +
                    "1. scannedAnswer MUST be a single uppercase letter: A, B, C, or D.\n" +
                    "2. If an item has no answer or is unreadable, omit that item or set scannedAnswer to empty string.\n" +
                    "3. Start your response with { and end with }.";

                _logger.LogInformation("[GeminiService] Sending Vision AI answer sheet scan request...");

                string effectiveMime = string.IsNullOrWhiteSpace(mimeType) ? "image/jpeg" : mimeType;
                string? rawResponse = await CallGeminiApiDirectlyAsync(prompt, imageBytes, effectiveMime);

                if (string.IsNullOrWhiteSpace(rawResponse))
                {
                    return (null, string.Empty, "Gemini returned empty response for image scan.");
                }

                string? jsonObj = ExtractJsonObject(rawResponse);
                if (jsonObj == null)
                {
                    _logger.LogWarning("[GeminiService] Could not extract JSON object from Vision response: {Raw}", rawResponse);
                    return (null, rawResponse, "Could not parse JSON output from AI response.");
                }

                using var doc = JsonDocument.Parse(jsonObj);
                var root = doc.RootElement;

                var resultDict = new Dictionary<int, string>();
                string rawText = rawResponse;

                if (root.TryGetProperty("rawExtractedText", out var rawTextProp) && rawTextProp.ValueKind == JsonValueKind.String)
                {
                    rawText = rawTextProp.GetString() ?? rawResponse;
                }

                if (root.TryGetProperty("answers", out var answersProp) && answersProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in answersProp.EnumerateArray())
                    {
                        if (item.TryGetProperty("questionNumber", out var qNumProp) && qNumProp.TryGetInt32(out int qNum))
                        {
                            string ansStr = string.Empty;
                            if (item.TryGetProperty("scannedAnswer", out var ansProp) && ansProp.ValueKind == JsonValueKind.String)
                            {
                                ansStr = ansProp.GetString()?.Trim().ToUpper() ?? string.Empty;
                            }

                            if (!string.IsNullOrEmpty(ansStr) && Regex.IsMatch(ansStr, @"^[A-D]$"))
                            {
                                resultDict[qNum] = ansStr;
                            }
                        }
                    }
                }

                return (resultDict, rawText, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[GeminiService] Answer sheet vision scan failed.");
                return (null, string.Empty, ex.Message);
            }
        }

        public async Task<(Dictionary<int, string>? Answers, string RawText, string? ErrorDetail)> ScanAnswerSheetsAsync(
            List<(byte[] Bytes, string MimeType)> imageFiles)
        {
            if (imageFiles == null || imageFiles.Count == 0)
            {
                return (null, string.Empty, "No images provided for scanning.");
            }

            if (imageFiles.Count == 1)
            {
                return await ScanAnswerSheetAsync(imageFiles[0].Bytes, imageFiles[0].MimeType);
            }

            try
            {
                string prompt =
                    "You are an automated exam answer sheet scanner and grader AI. " +
                    $"Attached are {imageFiles.Count} page image(s) of a student's completed answer sheet / questionnaire. " +
                    "Carefully examine all attached page images together, read every question item (e.g. 1, 2, 3, 4, 5, 6, 7, 8, 9, 10...) across all pages, and identify the student's handwritten or selected answer letter (A, B, C, or D).\n" +
                    "Note: Answers are written on blank lines before item numbers (e.g. 'B 1.', 'A 2.', 'b 5.'), " +
                    "written next to question text, or marked among the options.\n" +
                    "Return your analysis strictly as a JSON object — no markdown, no code blocks, no prose.\n" +
                    "JSON Structure:\n" +
                    "{\n" +
                    "  \"answers\": [\n" +
                    "    { \"questionNumber\": 1, \"scannedAnswer\": \"B\" },\n" +
                    "    { \"questionNumber\": 2, \"scannedAnswer\": \"B\" }\n" +
                    "  ],\n" +
                    "  \"rawExtractedText\": \"A short transcribed summary of all recognized questions and student answers across all pages.\"\n" +
                    "}\n\n" +
                    "Rules:\n" +
                    "1. scannedAnswer MUST be a single uppercase letter: A, B, C, or D.\n" +
                    "2. Combine all question items from all page images into a single complete answers list.\n" +
                    "3. If an item has no answer or is unreadable, omit that item or set scannedAnswer to empty string.\n" +
                    "4. Start your response with { and end with }.";

                _logger.LogInformation("[GeminiService] Sending Vision AI multi-page scan request for {Count} pages...", imageFiles.Count);

                string? rawResponse = await CallGeminiApiForMultipleImagesAsync(prompt, imageFiles);

                if (string.IsNullOrWhiteSpace(rawResponse))
                {
                    return (null, string.Empty, "Gemini returned empty response for multi-image scan.");
                }

                string? jsonObj = ExtractJsonObject(rawResponse);
                if (jsonObj == null)
                {
                    _logger.LogWarning("[GeminiService] Could not extract JSON object from multi-page Vision response: {Raw}", rawResponse);
                    return (null, rawResponse, "Could not parse JSON output from AI response.");
                }

                using var doc = JsonDocument.Parse(jsonObj);
                var root = doc.RootElement;

                var resultDict = new Dictionary<int, string>();
                string rawText = rawResponse;

                if (root.TryGetProperty("rawExtractedText", out var rawTextProp) && rawTextProp.ValueKind == JsonValueKind.String)
                {
                    rawText = rawTextProp.GetString() ?? rawResponse;
                }

                if (root.TryGetProperty("answers", out var answersProp) && answersProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in answersProp.EnumerateArray())
                    {
                        if (item.TryGetProperty("questionNumber", out var qNumProp) && qNumProp.TryGetInt32(out int qNum))
                        {
                            string ansStr = string.Empty;
                            if (item.TryGetProperty("scannedAnswer", out var ansProp) && ansProp.ValueKind == JsonValueKind.String)
                            {
                                ansStr = ansProp.GetString()?.Trim().ToUpper() ?? string.Empty;
                            }

                            if (!string.IsNullOrEmpty(ansStr) && Regex.IsMatch(ansStr, @"^[A-D]$"))
                            {
                                resultDict[qNum] = ansStr;
                            }
                        }
                    }
                }

                return (resultDict, rawText, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[GeminiService] Multi-page answer sheet vision scan failed.");
                return (null, string.Empty, ex.Message);
            }
        }
    }
}
