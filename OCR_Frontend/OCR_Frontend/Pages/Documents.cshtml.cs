using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace OCR_Frontend.Pages
{
    public class DocumentsModel : PageModel
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public DocumentsModel(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public List<DocumentDto> Documents { get; set; } = new List<DocumentDto>();
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

            var response = await client.GetAsync("/api/pdfupload");
            
            if (response.IsSuccessStatusCode)
            {
                Documents = await response.Content.ReadFromJsonAsync<List<DocumentDto>>() ?? new List<DocumentDto>();
            }
            else
            {
                ErrorMessage = "Failed to load documents from the server.";
            }

            return Page();
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            var client = _httpClientFactory.CreateClient("BackendClient");
            var token = Request.Cookies["AuthToken"];

            if (!string.IsNullOrEmpty(token))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
            
            await client.DeleteAsync($"/api/pdfupload/{id}");
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDownloadAsync(int id)
        {
             var client = _httpClientFactory.CreateClient("BackendClient");
             var token = Request.Cookies["AuthToken"];

             if (!string.IsNullOrEmpty(token))
             {
                 client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
             }

             var response = await client.GetAsync($"/api/pdfupload/{id}/download");
             
             if (response.IsSuccessStatusCode)
             {
                 var stream = await response.Content.ReadAsStreamAsync();
                 var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/pdf";
                 
                 var contentDisposition = response.Content.Headers.ContentDisposition;
                 var fileName = contentDisposition?.FileNameStar ?? contentDisposition?.FileName ?? $"document_{id}.pdf";
                 
                 return File(stream, contentType, fileName.Trim('"'));
             }
             
             return RedirectToPage();
        }

        public class DocumentDto
        {
            public int Id { get; set; }
            public string FileName { get; set; } = string.Empty;
            public long FileSizeBytes { get; set; }
            public string? Description { get; set; }
            public string? BatchTitle { get; set; }
            public string? AiSummary { get; set; }
            public DateTime UploadedAt { get; set; }
        }
    }
}
