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
        public int? SelectedExamId { get; set; }
        public string? ErrorMessage { get; set; }

        // Stats properties
        public int TotalSheets { get; set; }
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
                if (response.IsSuccessStatusCode)
                {
                    GradedSheets = await response.Content.ReadFromJsonAsync<List<GradedSheetDto>>() ?? new();
                    
                    if (GradedSheets.Any())
                    {
                        TotalSheets = GradedSheets.Count;
                        AverageScore = GradedSheets.Average(s => s.ScorePercentage);
                        PassedCount = GradedSheets.Count(s => s.ScorePercentage >= 50.0);
                        FailedCount = GradedSheets.Count(s => s.ScorePercentage < 50.0);
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

        public class GradedSheetDto
        {
            public int Id { get; set; }
            public int ExamId { get; set; }
            public string DocumentFileName { get; set; } = string.Empty;
            public string StudentName { get; set; } = string.Empty;
            public DateTime GradedAt { get; set; }
            public int TotalQuestions { get; set; }
            public int CorrectCount { get; set; }
            public double ScorePercentage { get; set; }
            public string RawOcrText { get; set; } = string.Empty;
            public List<CheckedAnswerItemDto> Results { get; set; } = new();
            public List<string> ImageUrls { get; set; } = new();
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
