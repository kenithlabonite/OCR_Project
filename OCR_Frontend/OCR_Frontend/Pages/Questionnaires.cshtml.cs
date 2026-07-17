using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace OCR_Frontend.Pages
{
    public class QuestionnairesModel : PageModel
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public QuestionnairesModel(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public List<ExamDto> Exams { get; set; } = new();
        public string? ErrorMessage { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            var client = _httpClientFactory.CreateClient("BackendClient");
            var token = Request.Cookies["AuthToken"];

            if (!string.IsNullOrEmpty(token))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
            else
            {
                return RedirectToPage("/Login");
            }

            var response = await client.GetAsync("/api/exam");
            
            if (response.IsSuccessStatusCode)
            {
                Exams = await response.Content.ReadFromJsonAsync<List<ExamDto>>() ?? new();
            }
            else
            {
                ErrorMessage = "Failed to load questionnaires from the server.";
            }

            return Page();
        }

        public async Task<IActionResult> OnPostDownloadPdfAsync(int id)
        {
             var client = _httpClientFactory.CreateClient("BackendClient");
             var token = Request.Cookies["AuthToken"];

             if (!string.IsNullOrEmpty(token))
             {
                 client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
             }

             var response = await client.GetAsync($"/api/exam/{id}/download-pdf");
             
             if (response.IsSuccessStatusCode)
             {
                 var stream = await response.Content.ReadAsStreamAsync();
                 var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/pdf";
                 
                 var contentDisposition = response.Content.Headers.ContentDisposition;
                 var fileName = contentDisposition?.FileNameStar ?? contentDisposition?.FileName ?? $"Questionnaire_{id}.pdf";
                 
                 return File(stream, contentType, fileName.Trim('"'));
             }
             
             ErrorMessage = "Failed to download the PDF.";
             return RedirectToPage();
        }

        public class ExamDto
        {
            public int ExamId { get; set; }
            public int DocumentId { get; set; }
            public string DocumentFileName { get; set; } = string.Empty;
            public DateTime GeneratedAt { get; set; }
            public List<QuestionItemDto> Questions { get; set; } = new();
        }

        public class QuestionItemDto
        {
            public int Number { get; set; }
            public string QuestionText { get; set; } = string.Empty;
            public string OptionA { get; set; } = string.Empty;
            public string OptionB { get; set; } = string.Empty;
            public string OptionC { get; set; } = string.Empty;
            public string OptionD { get; set; } = string.Empty;
            public string CorrectAnswer { get; set; } = string.Empty;
            public string Explanation { get; set; } = string.Empty;
        }
    }
}
