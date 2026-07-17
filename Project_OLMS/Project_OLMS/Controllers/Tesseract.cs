using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Project_OLMS.Data;
using Project_OLMS.Models;
using Project_OLMS.Models.DTO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tesseract;

namespace Project_OLMS.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OcrController(AppDbContext context) : ControllerBase
    {
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };



        [HttpPost("check-answers/{examId:int}")]
        public async Task<IActionResult> CheckAnswers(int examId, IFormFile image)
        {
            if (image == null || image.Length == 0)
                return BadRequest(new { message = "No image provided." });

            // 1. Fetch the exam to get answer keys
            var exam = await context.ExamQuestionnaires
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == examId);

            if (exam == null)
                return NotFound(new { message = $"Exam questionnaire with Id {examId} was not found." });

            var correctQuestions = JsonSerializer.Deserialize<List<QuestionItem>>(exam.QuestionsJson, _jsonOptions) ?? new();
            if (correctQuestions.Count == 0)
                return BadRequest(new { message = "No valid questions found for this exam." });

            // 2. Process image with OCR
            using var memoryStream = new MemoryStream();
            await image.CopyToAsync(memoryStream);
            var imageBytes = memoryStream.ToArray();

            string extractedText = string.Empty;
            try
            {
                string tessDataPath = Path.Combine(Directory.GetCurrentDirectory(), "tessdata");
                using var engine = new TesseractEngine(tessDataPath, "eng", EngineMode.Default);
                using var img = Pix.LoadFromMemory(imageBytes);
                using var page = engine.Process(img);
                extractedText = page.GetText();
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"OCR Processing failed: {ex.Message}" });
            }

            // 3. Parse extracted text to find answers
            var scannedAnswers = new Dictionary<int, string>();
            var lines = extractedText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            
            // Regex to match patterns like "1. A", "2) b", "3 - C", "4 D"
            var regex = new Regex(@"\b(\d+)\s*[\.\-\)]?\s*([A-Da-d])\b", RegexOptions.Compiled);
            foreach (var line in lines)
            {
                var match = regex.Match(line.Trim());
                if (match.Success)
                {
                    if (int.TryParse(match.Groups[1].Value, out int qNum))
                    {
                        string answer = match.Groups[2].Value.ToUpper();
                        if (!scannedAnswers.ContainsKey(qNum))
                        {
                            scannedAnswers[qNum] = answer;
                        }
                    }
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

            // 5. Construct response
            var response = new CheckAnswersResponse
            {
                ExamId = examId,
                TotalQuestions = correctQuestions.Count,
                CorrectCount = correctCount,
                ScorePercentage = correctQuestions.Count > 0 ? (double)correctCount / correctQuestions.Count * 100 : 0,
                Results = results,
                RawOcrText = extractedText.Trim() // Included for debugging/verification on the frontend
            };

            return Ok(response);
        }
    }
}
