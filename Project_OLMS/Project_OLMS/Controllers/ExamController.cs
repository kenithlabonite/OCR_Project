using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Project_OLMS.Data;
using Project_OLMS.Models;
using Project_OLMS.Models.DTO;
using Project_OLMS.Services;
using System.Text.Json;

using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Project_OLMS.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ExamController(
        AppDbContext context,
        IGeminiService geminiService) : ControllerBase
    {
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        // ── Generate ──────────────────────────────────────────────────────────

        /// <summary>
        /// POST api/exam/generate/{documentId}
        /// Reads the stored PDF for the given document, calls Gemini to generate
        /// a 10-item multiple-choice questionnaire, persists the result, and
        /// returns it to the caller.
        /// </summary>
        [HttpPost("generate/{documentId:int}")]
        public async Task<IActionResult> Generate(int documentId)
        {
            // ── Verify document exists ────────────────────────────────────
            var document = await context.Documents
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == documentId);

            if (document == null)
                return NotFound(new { message = $"Document with Id {documentId} was not found." });

            // ── Verify file exists on disk ────────────────────────────────
            if (!System.IO.File.Exists(document.StoredFilePath))
                return NotFound(new { message = "The PDF file could not be found on the server." });

            // ── Read PDF bytes ────────────────────────────────────────────
            byte[] pdfBytes = await System.IO.File.ReadAllBytesAsync(document.StoredFilePath);

            // ── Call Gemini ───────────────────────────────────────────────
            var (questions, errorDetail) = await geminiService.GenerateExamAsync(pdfBytes, document.FileName);

            if (questions == null || questions.Count == 0)
                return StatusCode(502, new
                {
                    message = "AI failed to generate exam questions. Please try again.",
                    detail  = errorDetail ?? "Unknown error — check server logs for the [GeminiService] entries."
                });

            // ── Persist to database ───────────────────────────────────────
            string questionsJson = JsonSerializer.Serialize(questions);

            var exam = new ExamQuestionnaire
            {
                DocumentId    = documentId,
                GeneratedAt   = DateTime.UtcNow,
                QuestionsJson = questionsJson
            };

            context.ExamQuestionnaires.Add(exam);
            await context.SaveChangesAsync();

            // ── Return response ───────────────────────────────────────────
            return Ok(MapToGenerateResponse(exam, document.FileName, questions));
        }

        // ── Get All ───────────────────────────────────────────────────────────

        /// <summary>
        /// GET api/exam
        /// Returns all exam questionnaires ordered by most recent.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var exams = await context.ExamQuestionnaires
                .AsNoTracking()
                .Include(e => e.Document)
                .OrderByDescending(e => e.GeneratedAt)
                .ToListAsync();

            var result = exams.Select(e =>
                MapToResponse(e, e.Document?.FileName ?? string.Empty, DeserialiseQuestions(e.QuestionsJson)));

            return Ok(result);
        }

        // ── Get By Exam Id ────────────────────────────────────────────────────

        /// <summary>
        /// GET api/exam/{examId}
        /// Returns a previously generated exam questionnaire by its own Id.
        /// </summary>
        [HttpGet("{examId:int}")]
        public async Task<IActionResult> GetById(int examId)
        {
            var exam = await context.ExamQuestionnaires
                .AsNoTracking()
                .Include(e => e.Document)
                .FirstOrDefaultAsync(e => e.Id == examId);

            if (exam == null)
                return NotFound(new { message = $"Exam questionnaire with Id {examId} was not found." });

            var questions = DeserialiseQuestions(exam.QuestionsJson);
            return Ok(MapToResponse(exam, exam.Document?.FileName ?? string.Empty, questions));
        }

        // ── Download PDF ──────────────────────────────────────────────────────

        /// <summary>
        /// GET api/exam/{examId}/download-pdf
        /// Generates a PDF document for the questionnaire and returns it as a file download.
        /// </summary>
        [HttpGet("{examId:int}/download-pdf")]
        public async Task<IActionResult> DownloadPdf(int examId)
        {
            var exam = await context.ExamQuestionnaires
                .AsNoTracking()
                .Include(e => e.Document)
                .FirstOrDefaultAsync(e => e.Id == examId);

            if (exam == null)
                return NotFound(new { message = $"Exam questionnaire with Id {examId} was not found." });

            var questions = DeserialiseQuestions(exam.QuestionsJson);
            var documentName = exam.Document?.FileName ?? "Document";

            var pdfDocument = QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(11).FontFamily("Arial"));

                    page.Header().Element(ComposeHeader);
                    page.Content().Element(ComposeContent);
                    page.Footer().Element(ComposeFooter);

                    void ComposeHeader(IContainer container)
                    {
                        container.Row(row =>
                        {
                            row.RelativeItem().Column(column =>
                            {
                                column.Item().Text("NexusOCR Questionnaire").FontSize(20).SemiBold().FontColor(Colors.Blue.Darken2);
                                column.Item().Text($"Source: {documentName}").FontSize(14).FontColor(Colors.Grey.Darken3);
                                column.Item().Text($"Generated: {exam.GeneratedAt.ToLocalTime():MMM dd, yyyy HH:mm}").FontSize(10).FontColor(Colors.Grey.Medium);
                            });
                        });
                    }

                    void ComposeContent(IContainer container)
                    {
                        container.PaddingVertical(1, Unit.Centimetre).Column(column =>
                        {
                            column.Spacing(20);

                            foreach (var q in questions)
                            {
                                column.Item().Column(questionCol =>
                                {
                                    questionCol.Spacing(5);
                                    questionCol.Item().Text($"Q{q.Number}: {q.QuestionText}").SemiBold().FontSize(12);

                                    var optionsList = new[]
                                    {
                                        new { Key = "A", Value = q.OptionA },
                                        new { Key = "B", Value = q.OptionB },
                                        new { Key = "C", Value = q.OptionC },
                                        new { Key = "D", Value = q.OptionD }
                                    };

                                    foreach (var opt in optionsList)
                                    {
                                        bool isCorrect = opt.Key.Equals(q.CorrectAnswer, StringComparison.OrdinalIgnoreCase);

                                        if (isCorrect)
                                        {
                                            questionCol.Item().PaddingLeft(15).Text($"{opt.Key}. {opt.Value} (Correct Answer)").FontColor(Colors.Green.Darken2).SemiBold();
                                        }
                                        else
                                        {
                                            questionCol.Item().PaddingLeft(15).Text($"{opt.Key}. {opt.Value}");
                                        }
                                    }

                                    questionCol.Item().PaddingTop(5).PaddingLeft(15).Text(text =>
                                    {
                                        text.Span("Explanation: ").SemiBold().FontColor(Colors.Blue.Darken2);
                                        text.Span(q.Explanation).FontColor(Colors.Grey.Darken3);
                                    });
                                });
                            }
                        });
                    }

                    void ComposeFooter(IContainer container)
                    {
                        container.AlignCenter().Text(x =>
                        {
                            x.Span("Page ");
                            x.CurrentPageNumber();
                            x.Span(" of ");
                            x.TotalPages();
                        });
                    }
                });
            });

            byte[] pdfBytes = pdfDocument.GeneratePdf();
            return File(pdfBytes, "application/pdf", $"Questionnaire_{examId}.pdf");
        }

        // New endpoint: Download questionnaire without answer keys (questions only)
        /// <summary>
        /// GET api/exam/{examId}/download-questions-pdf
        /// Generates a PDF containing only the questions and options, without correct answer markings or explanations.
        /// </summary>
        [HttpGet("{examId:int}/download-questions-pdf")]
        public async Task<IActionResult> DownloadQuestionsPdf(int examId)
        {
            var exam = await context.ExamQuestionnaires
                .AsNoTracking()
                .Include(e => e.Document)
                .FirstOrDefaultAsync(e => e.Id == examId);

            if (exam == null)
                return NotFound(new { message = $"Exam questionnaire with Id {examId} was not found." });

            var questions = DeserialiseQuestions(exam.QuestionsJson);
            var documentName = exam.Document?.FileName ?? "Document";

            var pdfDocument = QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(11).FontFamily("Arial"));

                    page.Header().Element(ComposeHeader);
                    page.Content().Element(ComposeQuestionsOnly);
                    page.Footer().Element(ComposeFooter);

                    void ComposeHeader(IContainer container)
                    {
                        container.Row(row =>
                        {
                            row.RelativeItem().Column(column =>
                            {
                                column.Item().Text("NexusOCR Questionnaire (Questions Only)").FontSize(20).SemiBold().FontColor(Colors.Blue.Darken2);
                                column.Item().Text($"Source: {documentName}").FontSize(14).FontColor(Colors.Grey.Darken3);
                                column.Item().Text($"Generated: {exam.GeneratedAt.ToLocalTime():MMM dd, yyyy HH:mm}").FontSize(10).FontColor(Colors.Grey.Medium);
                            });
                        });
                    }

                    void ComposeQuestionsOnly(IContainer container)
                    {
                        container.PaddingVertical(1, Unit.Centimetre).Column(column =>
                        {
                            column.Spacing(20);

                            // Instructions block
                            column.Item().Background(Colors.Grey.Lighten4).Padding(10).Column(instrCol =>
                            {
                                instrCol.Spacing(3);
                                instrCol.Item().Text("INSTRUCTIONS:").Bold().FontSize(11).FontColor(Colors.Red.Darken2);
                                instrCol.Item().Text("Write your final answer BEFORE the item number for each question (e.g., A 1.). Answers written anywhere else will not be counted.").FontSize(11).Italic();
                            });

                            foreach (var q in questions)
                            {
                                column.Item().Column(questionCol =>
                                {
                                    questionCol.Spacing(5);
                                    questionCol.Item().Text($"_______ {q.Number}. {q.QuestionText}").SemiBold().FontSize(12);

                                    var optionsList = new[]
                                    {
                                        new { Key = "A", Value = q.OptionA },
                                        new { Key = "B", Value = q.OptionB },
                                        new { Key = "C", Value = q.OptionC },
                                        new { Key = "D", Value = q.OptionD }
                                    };

                                    foreach (var opt in optionsList)
                                    {
                                        questionCol.Item().PaddingLeft(45).Text($"{opt.Key}. {opt.Value}");
                                    }
                                });
                            }
                        });
                    }

                    void ComposeFooter(IContainer container)
                    {
                        container.AlignCenter().Text(x =>
                        {
                            x.Span("Page ");
                            x.CurrentPageNumber();
                            x.Span(" of ");
                            x.TotalPages();
                        });
                    }
                });
            });

            byte[] pdfBytes = pdfDocument.GeneratePdf();
            return File(pdfBytes, "application/pdf", $"Questionnaire_Questions_{examId}.pdf");
        }

        // New endpoint: Download answer key PDF (answers only)
        /// <summary>
        /// GET api/exam/{examId}/download-answers-pdf
        /// Generates a PDF containing only the answer key (question numbers and correct answer) with optional explanations.
        /// </summary>
        [HttpGet("{examId:int}/download-answers-pdf")]
        public async Task<IActionResult> DownloadAnswersPdf(int examId)
        {
            var exam = await context.ExamQuestionnaires
                .AsNoTracking()
                .Include(e => e.Document)
                .FirstOrDefaultAsync(e => e.Id == examId);

            if (exam == null)
                return NotFound(new { message = $"Exam questionnaire with Id {examId} was not found." });

            var questions = DeserialiseQuestions(exam.QuestionsJson);
            var documentName = exam.Document?.FileName ?? "Document";

            var pdfDocument = QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(11).FontFamily("Arial"));

                    page.Header().Element(ComposeHeader);
                    page.Content().Element(ComposeAnswersOnly);
                    page.Footer().Element(ComposeFooter);

                    void ComposeHeader(IContainer container)
                    {
                        container.Row(row =>
                        {
                            row.RelativeItem().Column(column =>
                            {
                                column.Item().Text("NexusOCR Answer Key").FontSize(20).SemiBold().FontColor(Colors.Blue.Darken2);
                                column.Item().Text($"Source: {documentName}").FontSize(14).FontColor(Colors.Grey.Darken3);
                                column.Item().Text($"Generated: {exam.GeneratedAt.ToLocalTime():MMM dd, yyyy HH:mm}").FontSize(10).FontColor(Colors.Grey.Medium);
                            });
                        });
                    }

                    void ComposeAnswersOnly(IContainer container)
                    {
                        container.PaddingVertical(1, Unit.Centimetre).Column(column =>
                        {
                            column.Spacing(20);

                            foreach (var q in questions)
                            {
                                column.Item().Text($"Q{q.Number}: {q.CorrectAnswer}").SemiBold().FontSize(12);
                                if (!string.IsNullOrWhiteSpace(q.Explanation))
                                {
                                    column.Item().PaddingLeft(15).Text(text =>
                                    {
                                        text.Span("Explanation: ").SemiBold().FontColor(Colors.Blue.Darken2);
                                        text.Span(q.Explanation).FontColor(Colors.Grey.Darken3);
                                    });
                                }
                            }
                        });
                    }

                    void ComposeFooter(IContainer container)
                    {
                        container.AlignCenter().Text(x =>
                        {
                            x.Span("Page ");
                            x.CurrentPageNumber();
                            x.Span(" of ");
                            x.TotalPages();
                        });
                    }
                });
            });

            byte[] pdfBytes = pdfDocument.GeneratePdf();
            return File(pdfBytes, "application/pdf", $"AnswerKey_{examId}.pdf");
        }

        // ── Get By Document Id ────────────────────────────────────────────────

        /// <summary>
        /// GET api/exam/document/{documentId}
        /// Returns all exam questionnaires generated for a specific document,
        /// ordered most-recent first.
        /// </summary>
        [HttpGet("document/{documentId:int}")]
        public async Task<IActionResult> GetByDocument(int documentId)
        {
            // Verify document exists
            bool docExists = await context.Documents.AnyAsync(d => d.Id == documentId);
            if (!docExists)
                return NotFound(new { message = $"Document with Id {documentId} was not found." });

            var exams = await context.ExamQuestionnaires
                .AsNoTracking()
                .Include(e => e.Document)
                .Where(e => e.DocumentId == documentId)
                .OrderByDescending(e => e.GeneratedAt)
                .ToListAsync();

            var result = exams.Select(e =>
                MapToResponse(e, e.Document?.FileName ?? string.Empty, DeserialiseQuestions(e.QuestionsJson)));

            return Ok(result);
        }

        // ── Private Helpers ───────────────────────────────────────────────────

        private static List<QuestionItem> DeserialiseQuestions(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<List<QuestionItem>>(json, _jsonOptions) ?? new();
            }
            catch
            {
                return new();
            }
        }

        private static GenerateExamResponse MapToGenerateResponse(
            ExamQuestionnaire exam, string fileName, List<QuestionItem> questions) => new()
        {
            ExamId           = exam.Id,
            DocumentId       = exam.DocumentId,
            DocumentFileName = fileName,
            GeneratedAt      = exam.GeneratedAt,
            Questions        = questions
        };

        private static ExamQuestionnaireResponse MapToResponse(
            ExamQuestionnaire exam, string fileName, List<QuestionItem> questions) => new()
        {
            ExamId           = exam.Id,
            DocumentId       = exam.DocumentId,
            DocumentFileName = fileName,
            GeneratedAt      = exam.GeneratedAt,
            Questions        = questions
        };
    }
}
