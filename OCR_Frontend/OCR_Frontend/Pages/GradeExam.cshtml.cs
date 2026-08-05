using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using static OCR_Frontend.Pages.QuestionnairesModel;

namespace OCR_Frontend.Pages
{
    public class GradeExamModel : PageModel
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public GradeExamModel(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public ExamDto? Exam { get; set; }
        public string? ErrorMessage { get; set; }
        public CheckAnswersResponseDto? GradingResult { get; set; }

        [BindProperty]
        public IFormFile? StudentAnswerSheet { get; set; }

        [BindProperty]
        public List<IFormFile> StudentAnswerSheets { get; set; } = new();

        [BindProperty]
        public string StudentName { get; set; } = string.Empty;

        public async Task<IActionResult> OnGetAsync(int examId)
        {
            await LoadExamAsync(examId);
            if (Exam == null)
            {
                return RedirectToPage("/Questionnaires");
            }
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(int examId)
        {
            await LoadExamAsync(examId);
            if (Exam == null)
            {
                return RedirectToPage("/Questionnaires");
            }

            var filesToUpload = new List<IFormFile>();
            if (StudentAnswerSheets != null && StudentAnswerSheets.Count > 0)
            {
                filesToUpload.AddRange(StudentAnswerSheets.Where(f => f != null && f.Length > 0));
            }
            if (filesToUpload.Count == 0 && StudentAnswerSheet != null && StudentAnswerSheet.Length > 0)
            {
                filesToUpload.Add(StudentAnswerSheet);
            }

            if (filesToUpload.Count == 0)
            {
                ErrorMessage = "Please select at least one valid image file to upload.";
                return Page();
            }

            var client = _httpClientFactory.CreateClient("BackendClient");
            var token = Request.Cookies["AuthToken"];
            if (!string.IsNullOrEmpty(token))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            using var content = new MultipartFormDataContent();
            var openStreams = new List<Stream>();
            try
            {
                foreach (var file in filesToUpload)
                {
                    var stream = file.OpenReadStream();
                    openStreams.Add(stream);
                    var fileContent = new StreamContent(stream);
                    fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
                    content.Add(fileContent, "StudentAnswerSheets", file.FileName);
                }

                if (!string.IsNullOrWhiteSpace(StudentName))
                {
                    content.Add(new StringContent(StudentName), "studentName");
                }

                var response = await client.PostAsync($"/api/ocr/check-answers/{examId}", content);
                if (response.IsSuccessStatusCode)
                {
                    GradingResult = await response.Content.ReadFromJsonAsync<CheckAnswersResponseDto>();
                }
                else
                {
                    ErrorMessage = await OCR_Frontend.Helpers.ApiErrorHelper.ExtractErrorMessageAsync(response, "Failed to grade the answer sheet. Please ensure the image has clear text.");
                }
            }
            finally
            {
                foreach (var s in openStreams)
                {
                    s.Dispose();
                }
            }

            return Page();
        }

        private async Task LoadExamAsync(int examId)
        {
            var client = _httpClientFactory.CreateClient("BackendClient");
            var token = Request.Cookies["AuthToken"];
            if (!string.IsNullOrEmpty(token))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            var response = await client.GetAsync($"/api/exam/{examId}");
            if (response.IsSuccessStatusCode)
            {
                Exam = await response.Content.ReadFromJsonAsync<ExamDto>();
            }
        }

        public class CheckAnswersResponseDto
        {
            public int ExamId { get; set; }
            public int TotalQuestions { get; set; }
            public int CorrectCount { get; set; }
            public double ScorePercentage { get; set; }
            public string RawOcrText { get; set; } = string.Empty;
            public List<CheckedAnswerItemDto> Results { get; set; } = new();
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
