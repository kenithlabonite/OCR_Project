using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

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
        public bool IsAdmin { get; set; }
        public string? SuccessMessage { get; set; }
        public string? ErrorMessage { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            var role = Request.Cookies["UserRole"];
            IsAdmin = string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase);

            if (TempData["SuccessMessage"] != null)
            {
                SuccessMessage = TempData["SuccessMessage"]?.ToString();
            }
            if (TempData["ErrorMessage"] != null)
            {
                ErrorMessage = TempData["ErrorMessage"]?.ToString();
            }

            var client = _httpClientFactory.CreateClient("BackendClient");
            var token = Request.Cookies["AuthToken"];

            if (string.IsNullOrEmpty(token))
            {
                return RedirectToPage("/Login");
            }
            OCR_Frontend.Helpers.AuthCookieHelper.AttachAuthHeaders(client, Request);

            var response = await client.GetAsync("/api/pdfupload");
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                OCR_Frontend.Helpers.AuthCookieHelper.ClearSessionCookies(Response);
                return RedirectToPage("/Login");
            }
            
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

        public async Task<IActionResult> OnPostUpdateBatchTitleAsync(string oldTitle, string newTitle)
        {
            var role = Request.Cookies["UserRole"];
            if (!string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "Access Denied: Only administrators are authorized to edit batch titles.";
                return RedirectToPage();
            }

            if (string.IsNullOrWhiteSpace(newTitle))
            {
                TempData["ErrorMessage"] = "Batch title cannot be empty.";
                return RedirectToPage();
            }

            var client = _httpClientFactory.CreateClient("BackendClient");
            OCR_Frontend.Helpers.AuthCookieHelper.AttachAuthHeaders(client, Request);

            var payload = new { OldTitle = oldTitle, NewTitle = newTitle.Trim() };
            var response = await client.PutAsJsonAsync("/api/pdfupload/batch-title", payload);

            if (response.IsSuccessStatusCode)
            {
                TempData["SuccessMessage"] = $"Batch title successfully updated to '{newTitle.Trim()}'.";
            }
            else
            {
                var errorObj = await response.Content.ReadFromJsonAsync<ApiMessageResponse>();
                TempData["ErrorMessage"] = errorObj?.Message ?? "Failed to update batch title.";
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostUpdateDocumentBatchTitleAsync(int id, string batchTitle)
        {
            var role = Request.Cookies["UserRole"];
            if (!string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "Access Denied: Only administrators are authorized to edit batch titles.";
                return RedirectToPage();
            }

            var client = _httpClientFactory.CreateClient("BackendClient");
            OCR_Frontend.Helpers.AuthCookieHelper.AttachAuthHeaders(client, Request);

            var payload = new { BatchTitle = batchTitle?.Trim() ?? string.Empty };
            var response = await client.PutAsJsonAsync($"/api/pdfupload/{id}/batch-title", payload);

            if (response.IsSuccessStatusCode)
            {
                TempData["SuccessMessage"] = "Document batch title updated successfully.";
            }
            else
            {
                var errorObj = await response.Content.ReadFromJsonAsync<ApiMessageResponse>();
                TempData["ErrorMessage"] = errorObj?.Message ?? "Failed to update document batch title.";
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            var client = _httpClientFactory.CreateClient("BackendClient");
            OCR_Frontend.Helpers.AuthCookieHelper.AttachAuthHeaders(client, Request);
            
            await client.DeleteAsync($"/api/pdfupload/{id}");
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDownloadAsync(int id)
        {
             var client = _httpClientFactory.CreateClient("BackendClient");
             OCR_Frontend.Helpers.AuthCookieHelper.AttachAuthHeaders(client, Request);

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

        public class ApiMessageResponse
        {
            public string? Message { get; set; }
        }
    }
}
