using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace OCR_Frontend.Pages
{
    public class GenerateQuestionnaireModel : PageModel
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public GenerateQuestionnaireModel(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public List<DocumentDto> Documents { get; set; } = new();
        
        [BindProperty]
        public int SelectedDocumentId { get; set; }

        public string? SuccessMessage { get; set; }
        public string? ErrorMessage { get; set; }
        public bool IsGenerating { get; set; } = false;

        public async Task<IActionResult> OnGetAsync(int? documentId)
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

            var response = await client.GetAsync("/api/pdfupload");
            if (response.IsSuccessStatusCode)
            {
                Documents = await response.Content.ReadFromJsonAsync<List<DocumentDto>>() ?? new();
            }
            else
            {
                ErrorMessage = "Failed to load uploaded PDF topics.";
            }

            if (documentId.HasValue)
            {
                SelectedDocumentId = documentId.Value;
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
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

            // Reload documents list in case request returns to page on failure
            var docResponse = await client.GetAsync("/api/pdfupload");
            if (docResponse.IsSuccessStatusCode)
            {
                Documents = await docResponse.Content.ReadFromJsonAsync<List<DocumentDto>>() ?? new();
            }

            if (SelectedDocumentId <= 0)
            {
                ErrorMessage = "Please select a valid topic (PDF document).";
                return Page();
            }

            IsGenerating = true;

            var response = await client.PostAsync($"/api/exam/generate/{SelectedDocumentId}", null);
            
            IsGenerating = false;

            if (response.IsSuccessStatusCode)
            {
                SuccessMessage = "Questionnaire generated successfully!";
                return RedirectToPage("/Questionnaires");
            }
            else
            {
                var errorText = await response.Content.ReadAsStringAsync();
                ErrorMessage = string.IsNullOrWhiteSpace(errorText) ? "Failed to generate questionnaire. Please try again." : errorText;
                return Page();
            }
        }

        public class DocumentDto
        {
            public int Id { get; set; }
            public string FileName { get; set; } = string.Empty;
            public string? Description { get; set; }
        }
    }
}
