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

        public async Task OnGetAsync()
        {
            var client = _httpClientFactory.CreateClient("BackendClient");
            // Retrieve token from cookie
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
                    SuccessRate = stats.SuccessRate;
                    FailedJobs = stats.FailedJobs;
                    CreditsUsed = stats.CreditsUsed;
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
