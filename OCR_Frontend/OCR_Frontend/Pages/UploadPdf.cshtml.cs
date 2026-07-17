using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Net.Http.Headers;

namespace OCR_Frontend.Pages
{
    public class UploadPdfModel : PageModel
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public UploadPdfModel(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        [BindProperty]
        public IFormFile? PdfFile { get; set; }

        [BindProperty]
        public string? Description { get; set; }

        public string? SuccessMessage { get; set; }
        public string? ErrorMessage { get; set; }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (PdfFile == null || PdfFile.Length == 0)
            {
                ErrorMessage = "Please select a valid PDF file.";
                return Page();
            }

            if (PdfFile.ContentType != "application/pdf")
            {
                ErrorMessage = "Only PDF files are accepted.";
                return Page();
            }

            var client = _httpClientFactory.CreateClient("BackendClient");

            using var content = new MultipartFormDataContent();
            
            // Add file
            var fileStreamContent = new StreamContent(PdfFile.OpenReadStream());
            fileStreamContent.Headers.ContentType = new MediaTypeHeaderValue(PdfFile.ContentType);
            content.Add(fileStreamContent, "file", PdfFile.FileName);

            // Add metadata
            if (!string.IsNullOrEmpty(Description))
            {
                content.Add(new StringContent(Description), "Description");
            }
            
            // Add Auth token
            var token = Request.Cookies["AuthToken"];
            if (!string.IsNullOrEmpty(token))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            var response = await client.PostAsync("/api/pdfupload", content);

            if (response.IsSuccessStatusCode)
            {
                SuccessMessage = "PDF uploaded successfully and is being analyzed!";
            }
            else
            {
                var errorText = await response.Content.ReadAsStringAsync();
                ErrorMessage = string.IsNullOrWhiteSpace(errorText) ? "Failed to upload PDF." : errorText;
            }

            return Page();
        }
    }
}
