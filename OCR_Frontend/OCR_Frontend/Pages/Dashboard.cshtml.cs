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
        public int TotalProcessed { get; set; }
        public double SuccessRate { get; set; }
        public int FailedJobs { get; set; }
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
            var token = Request.Cookies["AuthToken"];
            if (!string.IsNullOrEmpty(token))
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }
            var response = await client.GetAsync("/api/dashboard");
            if (response.IsSuccessStatusCode)
            {
                var stats = await response.Content.ReadFromJsonAsync<DashboardStats>();
                if (stats != null)
                {
                    TotalProcessed = stats.TotalProcessed;
                    SuccessRate    = stats.SuccessRate;
                    FailedJobs     = stats.FailedJobs;
                    CreditsUsed    = stats.CreditsUsed;
                }
            }
        }

        private class DashboardStats
        {
            public int TotalProcessed { get; set; }
            public double SuccessRate { get; set; }
            public int FailedJobs { get; set; }
            public int CreditsUsed { get; set; }
        }
    }
}
