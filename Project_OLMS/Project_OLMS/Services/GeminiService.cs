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
        /// Generates a 10-item multiple-choice examination questionnaire from multiple PDFs
        /// (a batch/topic). All PDFs are sent together so Gemini covers the full quarter's lessons.
        /// Returns a tuple: Questions (null on failure) and ErrorDetail (null on success).
        /// </summary>
        Task<(List<QuestionItem>? Questions, string? ErrorDetail)> GenerateExamFromBatchAsync(
            List<(byte[] PdfBytes, string FileName)> batch, string batchTitle);

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
        private readonly string _primaryModelId;
        private readonly ILogger<GeminiService> _logger;
        private const int MaxInlineSizeBytes = 100 * 1024 * 1024;
        
        // We use IHttpClientFactory if available, otherwise fallback to a static client for simplicity
        // in this refactor to avoid changing DI registration right now.
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5) // 300 seconds per attempt to accommodate longer Gemini processing
        };

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
            _primaryModelId = string.IsNullOrWhiteSpace(configuration["Gemini:Model"]) 
                       ? "gemini-3.8-flash" 
                       : configuration["Gemini:Model"]!;
        }

        // ── Direct HTTP Call to Gemini (Bypassing Mscc.GenerativeAI length validation) ──

        private async Task<string?> CallGeminiApiDirectlyAsync(string prompt, byte[] fileBytes, string mimeType = "application/pdf")
        {
            return await CallGeminiApiForMultipleImagesAsync(prompt, new List<(byte[] Bytes, string MimeType)> { (fileBytes, mimeType) });
        }

        private async Task<string?> CallGeminiApiForMultipleImagesAsync(string prompt, List<(byte[] Bytes, string MimeType)> files)
        {
            // Build fallback model chain: primary model -> gemini-3.5-flash -> gemini-2.5-flash
            var candidateModels = new List<string> { _primaryModelId };
            if (!candidateModels.Contains("gemini-3.5-flash")) candidateModels.Add("gemini-3.5-flash");
            if (!candidateModels.Contains("gemini-2.5-flash")) candidateModels.Add("gemini-2.5-flash");

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
            var random = new Random();

            foreach (var modelId in candidateModels)
            {
                var url = $"https://generativelanguage.googleapis.com/v1beta/models/{modelId}:generateContent?key={_apiKey}";
                const int maxAttemptsPerModel = 2;

                for (int attempt = 1; attempt <= maxAttemptsPerModel; attempt++)
                {
                    string? responseBody = null;
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        _logger.LogInformation("[GeminiService] Calling Gemini API [{Model}] (attempt {Attempt}/{MaxAttempts}, payload: {Size:F1} KB)...", 
                            modelId, attempt, maxAttemptsPerModel, jsonPayload.Length / 1024.0);

                        using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                        var response = await _httpClient.PostAsync(url, content);
                        sw.Stop();

                        _logger.LogInformation("[GeminiService] Gemini API [{Model}] responded in {Elapsed:F1}s with HTTP {StatusCode}.", 
                            modelId, sw.Elapsed.TotalSeconds, response.StatusCode);
                        responseBody = await response.Content.ReadAsStringAsync();

                        if (!response.IsSuccessStatusCode)
                        {
                            var statusCode = response.StatusCode;
                            _logger.LogWarning("Gemini API [{Model}] returned HTTP {StatusCode} on attempt {Attempt}/{MaxAttempts}.", 
                                modelId, statusCode, attempt, maxAttemptsPerModel);

                            bool isRetryable = statusCode == System.Net.HttpStatusCode.TooManyRequests 
                                               || ((int)statusCode >= 500 && (int)statusCode <= 599);

                            if (isRetryable)
                            {
                                if (attempt < maxAttemptsPerModel)
                                {
                                    int delayMs = (attempt * 2000) + random.Next(500, 1500);
                                    _logger.LogInformation("[GeminiService] Transient error {StatusCode} on [{Model}]. Retrying in {DelayMs}ms...", statusCode, modelId, delayMs);
                                    await Task.Delay(delayMs);
                                    continue;
                                }
                                else
                                {
                                    // Exhausted retries for this model, break to try next model in candidateModels
                                    _logger.LogWarning("[GeminiService] Model [{Model}] exhausted retries due to {StatusCode}. Falling back to next available model...", modelId, statusCode);
                                    break;
                                }
                            }

                            _logger.LogError("Gemini API [{Model}] returned HTTP {StatusCode}: {ErrorBody}", modelId, statusCode, responseBody);
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
                            _logger.LogError(ex, "Failed to parse Gemini success response from [{Model}]: {ResponseBody}", modelId, responseBody);
                            throw new Exception("Could not parse the Gemini API response structure.");
                        }
                    }
                    catch (Exception ex) when (attempt < maxAttemptsPerModel && (ex is HttpRequestException || ex is TaskCanceledException))
                    {
                        sw.Stop();
                        _logger.LogWarning(ex, "[GeminiService] Request to [{Model}] failed after {Elapsed:F1}s on attempt {Attempt}/{MaxAttempts}: {Msg}", 
                            modelId, sw.Elapsed.TotalSeconds, attempt, maxAttemptsPerModel, ex.Message);
                        int delayMs = (attempt * 2000) + random.Next(500, 1500);
                        await Task.Delay(delayMs);
                    }
                    catch (Exception ex) when (attempt >= maxAttemptsPerModel && candidateModels.IndexOf(modelId) < candidateModels.Count - 1 && (ex is HttpRequestException || ex is TaskCanceledException))
                    {
                        _logger.LogWarning(ex, "[GeminiService] Model [{Model}] failed. Falling back to next model...", modelId);
                        break; // Fallback to next candidate model
                    }
                }
            }

            throw new Exception("Gemini API call failed across all candidate models (due to service unavailability or high demand). Please try again in a few moments.");
        }

        // ── Existing: PDF Summary ─────────────────────────────────────────────

        public async Task<string?> AnalyzePdfAsync(byte[] pdfBytes, string fileName)
        {
            if (pdfBytes.Length > MaxInlineSizeBytes)
                return "File is too large for inline AI analysis (> 100 MB).";

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
                string sizeMsg = $"File '{fileName}' exceeds the 100 MB inline limit.";
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

        // ── Batch Exam Generator (multiple PDFs / quarter) ──────────────────────

        public async Task<(List<QuestionItem>? Questions, string? ErrorDetail)> GenerateExamFromBatchAsync(
            List<(byte[] PdfBytes, string FileName)> batch, string batchTitle)
        {
            if (batch == null || batch.Count == 0)
                return (null, "No PDF files were provided in the batch.");

            // Validate total size
            long totalBytes = batch.Sum(b => (long)b.PdfBytes.Length);
            if (totalBytes > MaxInlineSizeBytes * batch.Count)
            {
                string msg = $"Batch '{batchTitle}' total size {totalBytes / (1024 * 1024)} MB exceeds limits.";
                _logger.LogWarning("[GeminiService] {Msg}", msg);
                return (null, msg);
            }

            string? rawResponse = null;

            try
            {
                string fileList = string.Join(", ", batch.Select(b => $"'{b.FileName}'"));
                string prompt =
                    "You are an expert educator and examination writer. " +
                    $"The following {batch.Count} PDF document(s) from the topic/quarter titled '{batchTitle}' are attached: {fileList}. " +
                    "Read all of them carefully and create exactly 10 multiple-choice questions that comprehensively test " +
                    "understanding across ALL the provided documents in this batch. " +
                    "Cover a range of topics from the different documents — do not focus on just one file. " +
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
                    "[GeminiService] Sending batch exam-generation request for title '{BatchTitle}' ({Count} files)...",
                    batchTitle, batch.Count);

                var files = batch.Select(b => (b.PdfBytes, (string)"application/pdf")).ToList();
                rawResponse = await CallGeminiApiForMultipleImagesAsync(prompt, files);

                _logger.LogInformation(
                    "[GeminiService] Raw batch AI response for '{BatchTitle}': {Preview}",
                    batchTitle,
                    rawResponse?.Length > 400 ? rawResponse[..400] + "…" : rawResponse ?? "(null)");

                if (string.IsNullOrWhiteSpace(rawResponse))
                {
                    const string emptyMsg = "Gemini returned an empty response for the batch.";
                    _logger.LogWarning("[GeminiService] {Msg} BatchTitle='{BatchTitle}'", emptyMsg, batchTitle);
                    return (null, emptyMsg);
                }

                string? jsonArray = ExtractJsonArray(rawResponse);
                if (jsonArray == null)
                {
                    string noArrMsg = $"Could not locate a JSON array in the AI response. " +
                                     $"First 200 chars: {rawResponse[..Math.Min(200, rawResponse.Length)]}";
                    _logger.LogWarning("[GeminiService] {Msg}", noArrMsg);
                    return (null, noArrMsg);
                }

                var questions = JsonSerializer.Deserialize<List<QuestionItem>>(jsonArray, _jsonOptions);

                if (questions == null || questions.Count == 0)
                {
                    const string emptyListMsg = "JSON parsed OK but the question list is empty.";
                    _logger.LogWarning("[GeminiService] {Msg} BatchTitle='{BatchTitle}'", emptyListMsg, batchTitle);
                    return (null, emptyListMsg);
                }

                _logger.LogInformation(
                    "[GeminiService] Generated {Count} questions for batch '{BatchTitle}'",
                    questions.Count, batchTitle);

                return (questions, null);
            }
            catch (JsonException jsonEx)
            {
                string detail = $"JSON parse error: {jsonEx.Message} | Raw: " +
                                (rawResponse?.Length > 300
                                    ? rawResponse[..300] + "…"
                                    : rawResponse ?? "(null)");
                _logger.LogError(jsonEx,
                    "[GeminiService] JSON parse failed for batch '{BatchTitle}'. {Detail}", batchTitle, detail);
                return (null, detail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[GeminiService] Batch exam generation threw an exception for '{BatchTitle}'", batchTitle);
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
                return (null, string.Empty, "Image file exceeds maximum allowed size (100 MB).");
            }

            try
            {
                string prompt =
                    "You are an automated exam answer sheet scanner and grader AI. " +
                    "An image of a student's answer sheet or questionnaire is attached. " +
                    "Carefully scan the ENTIRE image from top to bottom including page edges, margins, and near page numbers. " +
                    "Find the student's answer letter (A, B, C, or D) for EVERY question item (1, 2, 3, 4, 5, 6, 7, 8, 9, 10...).\n" +
                    "CRITICAL RULES FOR FINDING ANSWERS:\n" +
                    "- Answers are ALWAYS written on the blank line BEFORE the item number (e.g. '_B_ 6.' means question 6 answer is B).\n" +
                    "- Look at the very bottom and very top of the page — answers near page footers (e.g. 'Page 1 of 2') are still valid.\n" +
                    "- A question whose text is cut off at the bottom of the page still has its answer written before its number on that same page.\n" +
                    "- Do NOT skip any question number. If you see a number 1-10, you MUST report its answer (or empty string if truly unreadable).\n" +
                    "Return ONLY a JSON object — no markdown, no code blocks, no prose.\n" +
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
                    "2. If an item has no answer or is truly unreadable, set scannedAnswer to empty string — do NOT omit the item.\n" +
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

                // Trust Gemini Vision directly - skip raw-text re-validation which discards valid answers.
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
                    "Carefully scan ALL pages from top to bottom including page edges, margins, and near page numbers. " +
                    "Find the student's answer letter (A, B, C, or D) for EVERY question item (1, 2, 3, 4, 5, 6, 7, 8, 9, 10...) across all pages.\n" +
                    "CRITICAL RULES FOR FINDING ANSWERS:\n" +
                    "- Answers are ALWAYS written on the blank line BEFORE the item number (e.g. '_B_ 6.' means question 6 answer is B).\n" +
                    "- Look at the very bottom and very top of EACH page — answers near page footers (e.g. 'Page 1 of 2') are still valid.\n" +
                    "- A question whose text is cut off at the bottom of one page and continues at the top of the next page still has its answer written before its number on the FIRST page.\n" +
                    "- Do NOT skip any question number. If you see numbers 1-10, report ALL their answers (or empty string if truly unreadable).\n" +
                    "- Combine all answers from all pages into one single list.\n" +
                    "Return ONLY a JSON object — no markdown, no code blocks, no prose.\n" +
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
                    "3. If an item has no answer or is truly unreadable, set scannedAnswer to empty string — do NOT omit the item.\n" +
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

                // Trust Gemini Vision directly - skip raw-text re-validation which discards valid answers.
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
