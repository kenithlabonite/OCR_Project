using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace OCR_Frontend.Pages
{
    public class DashboardModel : PageModel
    {
        private readonly IHttpClientFactory _httpClientFactory;
        public DashboardModel(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        // Stats properties
        public int TotalDocuments { get; set; }
        public int TotalTopics { get; set; }
        public int TotalGradedSheets { get; set; }
        public int TotalExams { get; set; }

        // Backward compatibility
        public int TotalProcessed { get; set; }
        public int CreditsUsed { get; set; }

        // Session display
        public string DisplayName { get; set; } = string.Empty;
        public string DisplayEmail { get; set; } = string.Empty;

        public async Task OnGetAsync()
        {
            // Read display info from cookies set at login
            DisplayName  = Request.Cookies["DisplayName"]  ?? string.Empty;
            DisplayEmail = Request.Cookies["DisplayEmail"] ?? string.Empty;

            var client = _httpClientFactory.CreateClient("BackendClient");
            OCR_Frontend.Helpers.AuthCookieHelper.AttachAuthHeaders(client, Request);
            var response = await client.GetAsync("/api/dashboard");
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                OCR_Frontend.Helpers.AuthCookieHelper.ClearSessionCookies(Response);
                Response.Redirect("/Login");
                return;
            }
            if (response.IsSuccessStatusCode)
            {
                var stats = await response.Content.ReadFromJsonAsync<DashboardStats>();
                if (stats != null)
                {
                    TotalDocuments    = stats.TotalDocuments != 0 ? stats.TotalDocuments : stats.TotalProcessed;
                    TotalTopics       = stats.TotalTopics;
                    TotalGradedSheets = stats.TotalGradedSheets;
                    TotalExams        = stats.TotalExams != 0 ? stats.TotalExams : stats.CreditsUsed;
                    TotalProcessed    = TotalDocuments;
                    CreditsUsed       = TotalExams;
                }
            }
        }

        private class DashboardStats
        {
            public int TotalDocuments { get; set; }
            public int TotalTopics { get; set; }
            public int TotalGradedSheets { get; set; }
            public int TotalExams { get; set; }
            public int TotalProcessed { get; set; }
            public int CreditsUsed { get; set; }
        }
    }
}
