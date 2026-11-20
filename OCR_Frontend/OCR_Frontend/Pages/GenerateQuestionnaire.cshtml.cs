using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace OCR_Frontend.Pages
{
    public class GenerateQuestionnaireModel : PageModel
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public GenerateQuestionnaireModel(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>Distinct batch titles (each title may represent many PDFs).</summary>
        public List<BatchTitleDto> BatchTitles { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public string SelectedBatchTitle { get; set; } = string.Empty;

        public string? SuccessMessage { get; set; }
        public string? ErrorMessage { get; set; }
        public bool IsGenerating { get; set; } = false;

        public async Task<IActionResult> OnGetAsync([FromQuery] string? topic = null, [FromQuery] string? SelectedBatchTitle = null)
        {
            string? suggestedTopic = !string.IsNullOrWhiteSpace(topic) ? topic : SelectedBatchTitle;
            if (!string.IsNullOrWhiteSpace(suggestedTopic))
            {
                this.SelectedBatchTitle = suggestedTopic.Trim();
            }

            var client = _httpClientFactory.CreateClient("BackendClient");
            var token  = Request.Cookies["AuthToken"];

            if (!string.IsNullOrEmpty(token))
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            else
                return RedirectToPage("/Login");

            var response = await client.GetAsync("/api/pdfupload/titles");
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                OCR_Frontend.Helpers.AuthCookieHelper.ClearSessionCookies(Response);
                return RedirectToPage("/Login");
            }
            if (response.IsSuccessStatusCode)
            {
                BatchTitles = await response.Content.ReadFromJsonAsync<List<BatchTitleDto>>() ?? new();

                // If a topic was pre-selected, ensure exact casing matches the option in BatchTitles
                if (!string.IsNullOrWhiteSpace(SelectedBatchTitle) && BatchTitles.Any())
                {
                    var matching = BatchTitles.FirstOrDefault(b => string.Equals(b.Title, SelectedBatchTitle, StringComparison.OrdinalIgnoreCase));
                    if (matching != null)
                    {
                        SelectedBatchTitle = matching.Title;
                    }
                }
            }
            else
            {
                ErrorMessage = "Failed to load uploaded PDF topics.";
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var client = _httpClientFactory.CreateClient("BackendClient");
            var token  = Request.Cookies["AuthToken"];

            if (!string.IsNullOrEmpty(token))
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            else
                return RedirectToPage("/Login");

            // Reload titles in case we return to page on failure
            var titlesResponse = await client.GetAsync("/api/pdfupload/titles");
            if (titlesResponse.IsSuccessStatusCode)
                BatchTitles = await titlesResponse.Content.ReadFromJsonAsync<List<BatchTitleDto>>() ?? new();

            if (string.IsNullOrWhiteSpace(SelectedBatchTitle))
            {
                ErrorMessage = "Please select a valid topic.";
                return Page();
            }

            IsGenerating = true;

            var payload = new { BatchTitle = SelectedBatchTitle };
            var jsonContent = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            try
            {
                var response = await client.PostAsync("/api/exam/generate-by-title", jsonContent);

                if (response.IsSuccessStatusCode)
                {
                    SuccessMessage = "Questionnaire generated successfully!";
                    return RedirectToPage("/Questionnaires");
                }
                else
                {
                    ErrorMessage = await OCR_Frontend.Helpers.ApiErrorHelper.ExtractErrorMessageAsync(response, "Failed to generate questionnaire. Please try again.");
                    return Page();
                }
            }
            catch (TaskCanceledException)
            {
                ErrorMessage = "The exam generation request took longer than expected and timed out. Multi-PDF batch analysis can take several minutes. Please check the Questionnaires page to see if it finished, or try generating with fewer documents.";
                return Page();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"An unexpected error occurred while generating the questionnaire: {ex.Message}";
                return Page();
            }
            finally
            {
                IsGenerating = false;
            }
        }

        public class BatchTitleDto
        {
            public string Title      { get; set; } = string.Empty;
            public List<int> DocumentIds { get; set; } = new();
            public List<string> FileNames { get; set; } = new();
            public int FileCount     { get; set; }
            public DateTime UploadedAt { get; set; }
        }
    }
}
