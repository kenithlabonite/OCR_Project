using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace OCR_Frontend.Pages
{
    public class LoginModel : PageModel
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public LoginModel(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        [BindProperty]
        public string Email { get; set; } = string.Empty;

        [BindProperty]
        public string Password { get; set; } = string.Empty;

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var client = _httpClientFactory.CreateClient("BackendClient");
            var loginDto = new { email = Email, password = Password };
            var response = await client.PostAsJsonAsync("/api/auth/login", loginDto);

            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync();
                ModelState.AddModelError(string.Empty,
                    string.IsNullOrWhiteSpace(errorText) ? "Invalid login credentials." : errorText);
                return Page();
            }

            var result = await response.Content.ReadFromJsonAsync<LoginResponse>();

            if (result?.Token == null)
            {
                ModelState.AddModelError(string.Empty, "Login failed: no token received.");
                return Page();
            }

            // Store JWT in HttpOnly cookie
            Response.Cookies.Append("AuthToken", result.Token, new Microsoft.AspNetCore.Http.CookieOptions
            {
                HttpOnly = true,
                Secure = false,
                SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddHours(1)
            });

            // Store display info in readable cookies (not sensitive)
            var displayCookieOptions = new Microsoft.AspNetCore.Http.CookieOptions
            {
                HttpOnly = false,
                Secure = false,
                SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddHours(1)
            };
            if (!string.IsNullOrEmpty(result.Username))
                Response.Cookies.Append("DisplayName", result.Username, displayCookieOptions);
            if (!string.IsNullOrEmpty(result.Email))
                Response.Cookies.Append("DisplayEmail", result.Email, displayCookieOptions);
            if (!string.IsNullOrEmpty(result.Role))
                Response.Cookies.Append("UserRole", result.Role, displayCookieOptions);

            return RedirectToPage("/Dashboard");
        }

        private class LoginResponse
        {
            public string? Token { get; set; }
            public string? Username { get; set; }
            public string? Email { get; set; }
            public string? Role { get; set; }
        }
    }
}
