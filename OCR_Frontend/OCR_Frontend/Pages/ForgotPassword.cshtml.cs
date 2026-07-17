using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace OCR_Frontend.Pages
{
    public class ForgotPasswordModel : PageModel
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public ForgotPasswordModel(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        [BindProperty]
        public string Email { get; set; } = string.Empty;

        public string? SuccessMessage { get; set; }

        public void OnGet() { }

        public async Task<IActionResult> OnPostAsync()
        {
            var client = _httpClientFactory.CreateClient("BackendClient");
            var dto = new { email = Email };
            var response = await client.PostAsJsonAsync("/api/auth/forgot-password", dto);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                ModelState.AddModelError(string.Empty, string.IsNullOrWhiteSpace(error) ? "Something went wrong." : error);
                return Page();
            }

            // Redirect to reset page with the email pre-filled
            return RedirectToPage("/ResetPassword", new { email = Email });
        }
    }
}
