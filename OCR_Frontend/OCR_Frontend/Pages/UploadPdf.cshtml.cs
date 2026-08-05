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
        public List<IFormFile> PdfFiles { get; set; } = new();

        [BindProperty]
        public string? BatchTitle { get; set; }

        [BindProperty]
        public string? Description { get; set; }

        public string? SuccessMessage { get; set; }
        public string? ErrorMessage { get; set; }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (PdfFiles == null || PdfFiles.Count == 0)
            {
                ErrorMessage = "Please select at least one PDF file.";
                return Page();
            }

            foreach (var file in PdfFiles)
            {
                if (file.ContentType != "application/pdf")
                {
                    ErrorMessage = $"Only PDF files are accepted. Invalid file: '{file.FileName}'";
                    return Page();
                }
            }

            var client = _httpClientFactory.CreateClient("BackendClient");

            using var content = new MultipartFormDataContent();
            
            // Add files
            foreach (var file in PdfFiles)
            {
                var fileStreamContent = new StreamContent(file.OpenReadStream());
                fileStreamContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
                content.Add(fileStreamContent, "files", file.FileName);
            }

            // Add metadata
            if (!string.IsNullOrEmpty(Description))
            {
                content.Add(new StringContent(Description), "Description");
            }
            if (!string.IsNullOrEmpty(BatchTitle))
            {
                content.Add(new StringContent(BatchTitle), "BatchTitle");
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
                SuccessMessage = "PDF(s) uploaded successfully and are being analyzed!";
            }
            else
            {
                ErrorMessage = await OCR_Frontend.Helpers.ApiErrorHelper.ExtractErrorMessageAsync(response, "Failed to upload PDF(s).");
            }

            return Page();
        }
    }
}
