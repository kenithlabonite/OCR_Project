using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace OCR_Frontend.Pages
{
    public class VerifyEmailModel : PageModel
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public VerifyEmailModel(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        [BindProperty]
        public string Email { get; set; } = string.Empty;

        [BindProperty]
        public string Otp { get; set; } = string.Empty;

        public void OnGet(string? email)
        {
            if (!string.IsNullOrEmpty(email))
            {
                Email = email;
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Otp))
            {
                ModelState.AddModelError(string.Empty, "Email and OTP code are required.");
                return Page();
            }

            var client = _httpClientFactory.CreateClient("BackendClient");
            var verifyDto = new
            {
                email = Email,
                otp = Otp
            };

            var response = await client.PostAsJsonAsync("/api/auth/verify-otp", verifyDto);
            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync();
                ModelState.AddModelError(string.Empty, string.IsNullOrWhiteSpace(errorText) ? "Verification failed." : errorText);
                return Page();
            }

            TempData["SuccessMessage"] = "Email verified successfully! You can now log in.";
            return RedirectToPage("/Login");
        }
    }
}
