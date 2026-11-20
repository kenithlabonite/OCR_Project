using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;

namespace OCR_Frontend.Pages
{
    public class GradedSheetsModel : PageModel
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        public GradedSheetsModel(IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }

        public string BackendBaseUrl => _configuration["Backend:BaseUrl"] ?? "http://localhost:5296";

        public List<GradedSheetDto> GradedSheets { get; set; } = new();
        public List<BatchGroupDto> BatchGroups { get; set; } = new();
        public int? SelectedExamId { get; set; }
        public string? ErrorMessage { get; set; }

        // Stats properties
        public int TotalSheets { get; set; }
        public int TotalBatches { get; set; }
        public double AverageScore { get; set; }
        public int PassedCount { get; set; }
        public int FailedCount { get; set; }

        public async Task OnGetAsync([FromQuery] int? examId)
        {
            SelectedExamId = examId;
            var client = _httpClientFactory.CreateClient("BackendClient");
            var token = Request.Cookies["AuthToken"];
            if (!string.IsNullOrEmpty(token))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            string endpoint = examId.HasValue ? $"/api/ocr/graded-sheets/{examId.Value}" : "/api/ocr/graded-sheets";

            try
            {
                var response = await client.GetAsync(endpoint);
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    OCR_Frontend.Helpers.AuthCookieHelper.ClearSessionCookies(Response);
                    Response.Redirect("/Login");
                    return;
                }
                if (response.IsSuccessStatusCode)
                {
                    GradedSheets = await response.Content.ReadFromJsonAsync<List<GradedSheetDto>>() ?? new();
                    
                    if (GradedSheets.Any())
                    {
                        TotalSheets = GradedSheets.Count;
                        AverageScore = GradedSheets.Average(s => s.ScorePercentage);
                        PassedCount = GradedSheets.Count(s => s.ScorePercentage >= 50.0);
                        FailedCount = GradedSheets.Count(s => s.ScorePercentage < 50.0);

                        // Group submissions by Batch Title / Subject
                        BatchGroups = GradedSheets
                            .GroupBy(s => s.BatchOrSubject)
                            .Select(g => new BatchGroupDto
                            {
                                BatchTitle = g.Key,
                                ExamId = g.First().ExamId,
                                Sheets = g.OrderByDescending(s => s.GradedAt).ToList()
                            })
                            .OrderByDescending(b => b.LatestGradedAt)
                            .ToList();

                        TotalBatches = BatchGroups.Count;
                    }
                }
                else
                {
                    ErrorMessage = $"Failed to load graded sheets. (Status: {response.StatusCode})";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error connecting to server: {ex.Message}";
            }
        }

        public class BatchGroupDto
        {
            public string BatchTitle { get; set; } = string.Empty;
            public int ExamId { get; set; }
            public List<GradedSheetDto> Sheets { get; set; } = new();
            public int TotalSheets => Sheets.Count;
            public double AverageScore => Sheets.Any() ? Sheets.Average(s => s.ScorePercentage) : 0.0;
            public int PassedCount => Sheets.Count(s => s.ScorePercentage >= 50.0);
            public int FailedCount => Sheets.Count(s => s.ScorePercentage < 50.0);
            public DateTime LatestGradedAt => Sheets.Any() ? Sheets.Max(s => s.GradedAt) : DateTime.MinValue;
        }

        public class GradedSheetDto
        {
            public int Id { get; set; }
            public int ExamId { get; set; }
            public string DocumentFileName { get; set; } = string.Empty;
            public string? BatchTitle { get; set; }
            public string StudentName { get; set; } = string.Empty;
            public DateTime GradedAt { get; set; }
            public int TotalQuestions { get; set; }
            public int CorrectCount { get; set; }
            public double ScorePercentage { get; set; }
            public string RawOcrText { get; set; } = string.Empty;
            public List<CheckedAnswerItemDto> Results { get; set; } = new();
            public List<string> ImageUrls { get; set; } = new();

            /// <summary>
            /// Display name for the Batch or Subject.
            /// </summary>
            public string BatchOrSubject => !string.IsNullOrWhiteSpace(BatchTitle)
                ? BatchTitle.Trim()
                : (!string.IsNullOrWhiteSpace(DocumentFileName) ? DocumentFileName.Trim() : $"Exam #{ExamId}");
        }

        public class CheckedAnswerItemDto
        {
            public int QuestionNumber { get; set; }
            public string ScannedAnswer { get; set; } = string.Empty;
            public string CorrectAnswer { get; set; } = string.Empty;
            public bool IsCorrect { get; set; }
        }
    }
}
