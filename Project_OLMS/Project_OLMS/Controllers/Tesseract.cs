using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Project_OLMS.Data;
using Project_OLMS.Models;
using Project_OLMS.Models.DTO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tesseract;

using Project_OLMS.Services;

namespace Project_OLMS.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OcrController(AppDbContext context, IGeminiService geminiService, IAuditLogService auditLogService, IWebHostEnvironment env) : ControllerBase
    {
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        [HttpPost("check-answers/{examId:int}")]
        public async Task<IActionResult> CheckAnswers(int examId, [FromForm(Name = "StudentAnswerSheets")] List<IFormFile>? images, IFormFile? image)
        {
            var uploadedFiles = images ?? new List<IFormFile>();
            if (uploadedFiles.Count == 0 && image != null && image.Length > 0)
            {
                uploadedFiles.Add(image);
            }

            uploadedFiles = uploadedFiles.Where(f => f != null && f.Length > 0).ToList();

            if (uploadedFiles.Count == 0)
                return BadRequest(new { message = "No image files provided." });

            // 1. Fetch the exam to get answer keys
            var exam = await context.ExamQuestionnaires
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == examId);

            if (exam == null)
                return NotFound(new { message = $"Exam questionnaire with Id {examId} was not found." });

            var correctQuestions = JsonSerializer.Deserialize<List<QuestionItem>>(exam.QuestionsJson, _jsonOptions) ?? new();
            if (correctQuestions.Count == 0)
                return BadRequest(new { message = "No valid questions found for this exam." });

            // 2. Read all image bytes & MIME types, and save copies to disk
            var imagePayloads = new List<(byte[] Bytes, string MimeType)>();
            var savedImagePaths = new List<string>();

            string uploadsDir = Path.Combine(env.ContentRootPath, "uploads", "graded-sheets");
            Directory.CreateDirectory(uploadsDir);
            string batchId = $"{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";

            int pageIndex = 0;
            foreach (var file in uploadedFiles)
            {
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms);
                var bytes = ms.ToArray();
                imagePayloads.Add((bytes, file.ContentType));

                // Save the image to disk
                string ext = Path.GetExtension(file.FileName);
                if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";
                string savedName = $"{batchId}-{pageIndex}{ext}";
                string fullPath = Path.Combine(uploadsDir, savedName);
                await System.IO.File.WriteAllBytesAsync(fullPath, bytes);
                savedImagePaths.Add($"graded-sheets/{savedName}");
                pageIndex++;
            }

            var scannedAnswers = new Dictionary<int, string>();
            string extractedText = string.Empty;

            // 3. Primary Scan Strategy: Gemini Vision AI (Multi-page payload)
            try
            {
                var (aiAnswers, aiRawText, aiError) = await geminiService.ScanAnswerSheetsAsync(imagePayloads);
                if (aiAnswers != null && aiAnswers.Count > 0)
                {
                    scannedAnswers = aiAnswers;
                    extractedText = string.IsNullOrWhiteSpace(aiRawText) ? $"Scanned {imagePayloads.Count} page(s) using Gemini Vision AI" : aiRawText;
                }
                else if (!string.IsNullOrWhiteSpace(aiRawText))
                {
                    extractedText = aiRawText;
                }
            }
            catch
            {
                // Fallback to Tesseract OCR below
            }

            // 4. Fallback Scan Strategy: Tesseract OCR (Loop through pages if AI unavailable)
            if (scannedAnswers.Count == 0)
            {
                var rawTextList = new List<string>();
                string tessDataPath = Path.Combine(Directory.GetCurrentDirectory(), "tessdata");

                foreach (var (bytes, _) in imagePayloads)
                {
                    string ocrText = string.Empty;
                    try
                    {
                        using var engine = new TesseractEngine(tessDataPath, "eng", EngineMode.Default);
                        using var img = Pix.LoadFromMemory(bytes);
                        using var page = engine.Process(img);
                        ocrText = page.GetText();
                        if (!string.IsNullOrWhiteSpace(ocrText))
                        {
                            rawTextList.Add(ocrText);
                        }
                    }
                    catch (Exception ex)
                    {
                        rawTextList.Add($"OCR Processing error on page: {ex.Message}");
                    }

                    if (!string.IsNullOrWhiteSpace(ocrText))
                    {
                        var lines = ocrText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        var regexAnswerFirst = new Regex(@"\b([A-Da-d])\s*[\.\_\-\)]*\s*(\d+)\b", RegexOptions.Compiled);
                        var regexNumberFirst = new Regex(@"\b(\d+)\s*[\.\_\-\)]*\s*([A-Da-d])\b", RegexOptions.Compiled);

                        foreach (var line in lines)
                        {
                            var trimmed = line.Trim();

                            var m1 = regexAnswerFirst.Match(trimmed);
                            if (m1.Success && int.TryParse(m1.Groups[2].Value, out int qNum1))
                            {
                                string answer = m1.Groups[1].Value.ToUpper();
                                if (!scannedAnswers.ContainsKey(qNum1))
                                {
                                    scannedAnswers[qNum1] = answer;
                                    continue;
                                }
                            }

                            var m2 = regexNumberFirst.Match(trimmed);
                            if (m2.Success && int.TryParse(m2.Groups[1].Value, out int qNum2))
                            {
                                string answer = m2.Groups[2].Value.ToUpper();
                                if (!scannedAnswers.ContainsKey(qNum2))
                                {
                                    scannedAnswers[qNum2] = answer;
                                }
                            }
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(extractedText))
                {
                    extractedText = string.Join("\n--- Next Page ---\n", rawTextList);
                }
            }

            // 4. Compare answers
            var results = new List<CheckedAnswerItem>();
            int correctCount = 0;

            foreach (var q in correctQuestions)
            {
                string scanned = scannedAnswers.ContainsKey(q.Number) ? scannedAnswers[q.Number] : string.Empty;
                string correct = q.CorrectAnswer?.ToUpper() ?? string.Empty;
                bool isCorrect = !string.IsNullOrEmpty(scanned) && scanned == correct;

                if (isCorrect) correctCount++;

                results.Add(new CheckedAnswerItem
                {
                    QuestionNumber = q.Number,
                    ScannedAnswer = scanned,
                    CorrectAnswer = correct,
                    IsCorrect = isCorrect
                });
            }

            // 5. Read optional student name from form
            string studentName = Request.Form.TryGetValue("studentName", out var nameValues)
                ? (nameValues.FirstOrDefault() ?? string.Empty)
                : string.Empty;

            // 6. Persist to database
            var gradedSheet = new GradedAnswerSheet
            {
                ExamId = examId,
                StudentName = studentName,
                GradedAt = DateTime.UtcNow,
                TotalQuestions = correctQuestions.Count,
                CorrectCount = correctCount,
                ScorePercentage = correctQuestions.Count > 0 ? (double)correctCount / correctQuestions.Count * 100 : 0,
                RawOcrText = extractedText.Trim(),
                AnswersJson = JsonSerializer.Serialize(results),
                ImagePathsJson = JsonSerializer.Serialize(savedImagePaths)
            };

            context.GradedAnswerSheets.Add(gradedSheet);
            await context.SaveChangesAsync();

            string studentDisplay = string.IsNullOrWhiteSpace(studentName) ? "Anonymous" : studentName;
            await auditLogService.LogDocumentExamAsync(
                action: "Sheet Graded",
                details: $"Graded student sheet for '{studentDisplay}' (Score: {correctCount}/{correctQuestions.Count}, {gradedSheet.ScorePercentage:F1}%, Exam ID: {examId})",
                username: studentName,
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            // 7. Construct response
            var response = new CheckAnswersResponse
            {
                ExamId = examId,
                TotalQuestions = correctQuestions.Count,
                CorrectCount = correctCount,
                ScorePercentage = correctQuestions.Count > 0 ? (double)correctCount / correctQuestions.Count * 100 : 0,
                Results = results,
                RawOcrText = extractedText.Trim(),
                ImageUrls = savedImagePaths.Select(p => $"/api/ocr/images/{p}").ToList()
            };

            return Ok(response);
        }

        // ── Get ALL graded sheets ─────────────────────────────────────────────

        /// <summary>
        /// GET api/ocr/graded-sheets
        /// Returns all stored graded answer sheets across all exams, most recent first.
        /// </summary>
        [HttpGet("graded-sheets")]
        public async Task<IActionResult> GetAllGradedSheets()
        {
            var sheets = await context.GradedAnswerSheets
                .AsNoTracking()
                .Include(g => g.Exam)
                    .ThenInclude(e => e!.Document)
                .OrderByDescending(g => g.GradedAt)
                .ToListAsync();

            var result = sheets.Select(g => new GradedAnswerSheetResponse
            {
                Id              = g.Id,
                ExamId          = g.ExamId,
                DocumentFileName = g.Exam?.Document?.FileName ?? $"Exam #{g.ExamId}",
                StudentName     = g.StudentName,
                GradedAt        = g.GradedAt,
                TotalQuestions  = g.TotalQuestions,
                CorrectCount    = g.CorrectCount,
                ScorePercentage = g.ScorePercentage,
                RawOcrText      = g.RawOcrText,
                Results         = JsonSerializer.Deserialize<List<CheckedAnswerItem>>(g.AnswersJson, _jsonOptions) ?? new(),
                ImageUrls       = BuildImageUrls(g.ImagePathsJson)
            });

            return Ok(result);
        }

        // ── Get all graded sheets for a specific exam ─────────────────────────

        /// <summary>
        /// GET api/ocr/graded-sheets/{examId}
        /// Returns all stored graded answer sheets for a specific exam, most recent first.
        /// </summary>
        [HttpGet("graded-sheets/{examId:int}")]
        public async Task<IActionResult> GetGradedSheets(int examId)
        {
            var exam = await context.ExamQuestionnaires
                .AsNoTracking()
                .Include(e => e.Document)
                .FirstOrDefaultAsync(e => e.Id == examId);

            if (exam == null)
                return NotFound(new { message = $"Exam questionnaire with Id {examId} was not found." });

            var sheets = await context.GradedAnswerSheets
                .AsNoTracking()
                .Where(g => g.ExamId == examId)
                .OrderByDescending(g => g.GradedAt)
                .ToListAsync();

            var result = sheets.Select(g => new GradedAnswerSheetResponse
            {
                Id              = g.Id,
                ExamId          = g.ExamId,
                DocumentFileName = exam.Document?.FileName ?? $"Exam #{examId}",
                StudentName     = g.StudentName,
                GradedAt        = g.GradedAt,
                TotalQuestions  = g.TotalQuestions,
                CorrectCount    = g.CorrectCount,
                ScorePercentage = g.ScorePercentage,
                RawOcrText      = g.RawOcrText,
                Results         = JsonSerializer.Deserialize<List<CheckedAnswerItem>>(g.AnswersJson, _jsonOptions) ?? new(),
                ImageUrls       = BuildImageUrls(g.ImagePathsJson)
            });

            return Ok(result);
        }

        // ── Serve saved answer sheet images ───────────────────────────────────

        /// <summary>
        /// GET api/ocr/images/graded-sheets/{fileName}
        /// Serves a previously saved answer sheet image.
        /// </summary>
        [HttpGet("images/graded-sheets/{fileName}")]
        public IActionResult GetAnswerSheetImage(string fileName)
        {
            // Sanitize to prevent path traversal
            fileName = Path.GetFileName(fileName);
            string filePath = Path.Combine(env.ContentRootPath, "uploads", "graded-sheets", fileName);

            if (!System.IO.File.Exists(filePath))
                return NotFound(new { message = "Image not found." });

            string ext = Path.GetExtension(fileName).ToLowerInvariant();
            string contentType = ext switch
            {
                ".png" => "image/png",
                ".jpeg" => "image/jpeg",
                ".jpg" => "image/jpeg",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".bmp" => "image/bmp",
                _ => "application/octet-stream"
            };

            var fileBytes = System.IO.File.ReadAllBytes(filePath);
            return File(fileBytes, contentType);
        }

        // ── Private Helpers ───────────────────────────────────────────────────

        private static List<string> BuildImageUrls(string? imagePathsJson)
        {
            if (string.IsNullOrWhiteSpace(imagePathsJson)) return new();
            try
            {
                var paths = JsonSerializer.Deserialize<List<string>>(imagePathsJson) ?? new();
                return paths.Select(p => $"/api/ocr/images/{p}").ToList();
            }
            catch
            {
                return new();
            }
        }
    }
}
