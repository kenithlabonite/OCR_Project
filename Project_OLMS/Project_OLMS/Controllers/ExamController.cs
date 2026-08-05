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
        IGeminiService geminiService,
        IAuditLogService auditLogService) : ControllerBase
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

            await auditLogService.LogDocumentExamAsync(
                action: "Exam Generated",
                details: $"Generated {questions.Count}-question exam (Exam ID: {exam.Id}) from document '{document.FileName}' (ID: {documentId})",
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            // ── Return response ───────────────────────────────────────────
            return Ok(MapToGenerateResponse(exam, document.FileName, questions));
        }

        // ── Generate by Batch Title ───────────────────────────────────────────

        /// <summary>
        /// POST api/exam/generate-by-title
        /// Receives a batch title, finds all documents sharing that title,
        /// reads their PDFs, and generates one 10-item exam covering all of them.
        /// </summary>
        [HttpPost("generate-by-title")]
        public async Task<IActionResult> GenerateByTitle([FromBody] GenerateByTitleRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.BatchTitle))
                return BadRequest(new { message = "BatchTitle is required." });

            // Find all documents in this batch
            var documents = await context.Documents
                .AsNoTracking()
                .Where(d => (d.BatchTitle != null && d.BatchTitle == request.BatchTitle)
                            || (d.BatchTitle == null && d.FileName == request.BatchTitle))
                .ToListAsync();

            if (documents.Count == 0)
                return NotFound(new { message = $"No documents found for title '{request.BatchTitle}'." });

            // Load all PDFs from disk
            var batch = new List<(byte[] PdfBytes, string FileName)>();
            foreach (var doc in documents)
            {
                if (!System.IO.File.Exists(doc.StoredFilePath))
                {
                    return NotFound(new { message = $"PDF file for '{doc.FileName}' could not be found on the server." });
                }
                byte[] bytes = await System.IO.File.ReadAllBytesAsync(doc.StoredFilePath);
                batch.Add((bytes, doc.FileName));
            }

            // Call Gemini with all PDFs
            var (questions, errorDetail) = await geminiService.GenerateExamFromBatchAsync(batch, request.BatchTitle);

            if (questions == null || questions.Count == 0)
                return StatusCode(502, new
                {
                    message = "AI failed to generate exam questions. Please try again.",
                    detail  = errorDetail ?? "Unknown error — check server logs for the [GeminiService] entries."
                });

            // Use the first document as the primary reference for the ExamQuestionnaire record
            int primaryDocumentId = documents.First().Id;
            string questionsJson  = System.Text.Json.JsonSerializer.Serialize(questions);

            var exam = new ExamQuestionnaire
            {
                DocumentId    = primaryDocumentId,
                GeneratedAt   = DateTime.UtcNow,
                QuestionsJson = questionsJson
            };

            context.ExamQuestionnaires.Add(exam);
            await context.SaveChangesAsync();

            await auditLogService.LogDocumentExamAsync(
                action: "Batch Exam Generated",
                details: $"Generated {questions.Count}-question exam (Exam ID: {exam.Id}) from batch '{request.BatchTitle}' ({documents.Count} file(s))",
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            return Ok(MapToGenerateResponse(exam, request.BatchTitle, questions));
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
                MapToResponse(e, GetExamTitle(e.Document), DeserialiseQuestions(e.QuestionsJson)));

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
            return Ok(MapToResponse(exam, GetExamTitle(exam.Document), questions));
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
            var documentName = GetExamTitle(exam.Document);

            var pdfDocument = QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.MarginHorizontal(1.5f, Unit.Centimetre);
                    page.MarginTop(1.5f, Unit.Centimetre);
                    page.MarginBottom(1.2f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(11).FontFamily("Arial"));

                    page.Content().Element(ComposeContent);
                    page.Footer().Element(ComposeFooter);

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
            string cleanName = Path.GetFileNameWithoutExtension(documentName);
            return File(pdfBytes, "application/pdf", $"{cleanName}_Questionnaire_Full.pdf");
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
            var documentName = GetExamTitle(exam.Document);

            var pdfDocument = QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.MarginHorizontal(1.5f, Unit.Centimetre);
                    page.MarginTop(1.5f, Unit.Centimetre);
                    page.MarginBottom(1.2f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(11).FontFamily("Arial"));

                    page.Content().Element(ComposeQuestionsOnly);
                    page.Footer().Element(ComposeFooter);

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
            string cleanName = Path.GetFileNameWithoutExtension(documentName);

            await auditLogService.LogDocumentExamAsync(
                action: "Questionnaire Downloaded",
                details: $"Downloaded questionnaire PDF for '{documentName}' (Exam ID: {examId})",
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            return File(pdfBytes, "application/pdf", $"{cleanName}_Questionnaire.pdf");
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
            var documentName = GetExamTitle(exam.Document);

            var pdfDocument = QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.MarginHorizontal(1.5f, Unit.Centimetre);
                    page.MarginTop(1.5f, Unit.Centimetre);
                    page.MarginBottom(1.2f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(11).FontFamily("Arial"));

                    page.Content().Element(ComposeAnswersOnly);
                    page.Footer().Element(ComposeFooter);

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
            string cleanName = Path.GetFileNameWithoutExtension(documentName);

            await auditLogService.LogDocumentExamAsync(
                action: "Answer Key Downloaded",
                details: $"Downloaded answer key PDF for '{documentName}' (Exam ID: {examId})",
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            return File(pdfBytes, "application/pdf", $"{cleanName}_AnswerKey.pdf");
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
                MapToResponse(e, GetExamTitle(e.Document), DeserialiseQuestions(e.QuestionsJson)));

            return Ok(result);
        }

        // ── Private Helpers ───────────────────────────────────────────────────

        private static string GetExamTitle(Project_OLMS.Models.Document? doc)
        {
            if (doc == null) return "Questionnaire";
            return !string.IsNullOrWhiteSpace(doc.BatchTitle) ? doc.BatchTitle : doc.FileName;
        }

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
