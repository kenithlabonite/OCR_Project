using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace OCR_Frontend.Pages
{
    public class ResetPasswordModel : PageModel
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public ResetPasswordModel(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        [BindProperty(SupportsGet = true)]
        public string Email { get; set; } = string.Empty;

        [BindProperty]
        public string Otp { get; set; } = string.Empty;

        [BindProperty]
        public string NewPassword { get; set; } = string.Empty;

        [BindProperty]
        public string ConfirmPassword { get; set; } = string.Empty;

        public void OnGet() { }

        public async Task<IActionResult> OnPostAsync()
        {
            if (NewPassword != ConfirmPassword)
            {
                ModelState.AddModelError(string.Empty, "Passwords do not match.");
                return Page();
            }

            var client = _httpClientFactory.CreateClient("BackendClient");
            var dto = new
            {
                email = Email,
                otp = Otp,
                newPassword = NewPassword
            };

            var response = await client.PostAsJsonAsync("/api/auth/reset-password", dto);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                ModelState.AddModelError(string.Empty, string.IsNullOrWhiteSpace(error) ? "Password reset failed. Check your code and try again." : error);
                return Page();
            }

            return RedirectToPage("/Login");
        }
    }
}
