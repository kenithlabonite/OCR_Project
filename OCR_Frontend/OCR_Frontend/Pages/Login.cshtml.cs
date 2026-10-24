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

        [BindProperty]
        public bool RememberMe { get; set; }

        public IActionResult OnGet()
        {
            // If the user already has a valid AuthToken, redirect straight to Dashboard
            if (Request.Cookies.TryGetValue("AuthToken", out var token) && !string.IsNullOrWhiteSpace(token))
            {
                return RedirectToPage("/Dashboard");
            }

            // Pre-fill remembered email and check RememberMe if previously saved
            if (Request.Cookies.TryGetValue("RememberedEmail", out var rememberedEmail) && !string.IsNullOrWhiteSpace(rememberedEmail))
            {
                Email = rememberedEmail;
                RememberMe = true;
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var client = _httpClientFactory.CreateClient("BackendClient");
            var loginDto = new { email = Email, password = Password };
            var response = await client.PostAsJsonAsync("/api/auth/login", loginDto);

            if (!response.IsSuccessStatusCode)
            {
                var errorText = await OCR_Frontend.Helpers.ApiErrorHelper.ExtractErrorMessageAsync(response, "Invalid login credentials.");
                ModelState.AddModelError(string.Empty, errorText);
                return Page();
            }

            var result = await response.Content.ReadFromJsonAsync<LoginResponse>();

            if (result?.Token == null)
            {
                ModelState.AddModelError(string.Empty, "Login failed: no token received.");
                return Page();
            }

            // Normal session cookie (persists while browser is open) or 14-day persistent cookie if Remember Me is checked
            DateTimeOffset? cookieExpires = RememberMe ? DateTimeOffset.UtcNow.AddDays(14) : null;

            // Store JWT in HttpOnly cookie
            var authCookieOptions = new Microsoft.AspNetCore.Http.CookieOptions
            {
                HttpOnly = true,
                Secure = false,
                SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
                Expires = cookieExpires
            };
            Response.Cookies.Append("AuthToken", result.Token, authCookieOptions);

            // Store display info in readable cookies
            var displayCookieOptions = new Microsoft.AspNetCore.Http.CookieOptions
            {
                HttpOnly = false,
                Secure = false,
                SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
                Expires = cookieExpires
            };
            if (!string.IsNullOrEmpty(result.Id))
                Response.Cookies.Append("UserId", result.Id, displayCookieOptions);
            if (!string.IsNullOrEmpty(result.Username))
                Response.Cookies.Append("DisplayName", result.Username, displayCookieOptions);
            if (!string.IsNullOrEmpty(result.Email))
                Response.Cookies.Append("DisplayEmail", result.Email, displayCookieOptions);
            if (!string.IsNullOrEmpty(result.Role))
                Response.Cookies.Append("UserRole", result.Role, displayCookieOptions);

            // Remember or forget email for future logins
            if (RememberMe)
            {
                Response.Cookies.Append("RememberedEmail", Email, new Microsoft.AspNetCore.Http.CookieOptions
                {
                    HttpOnly = false,
                    Secure = false,
                    SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
                    Expires = DateTimeOffset.UtcNow.AddDays(30)
                });
            }
            else
            {
                Response.Cookies.Delete("RememberedEmail");
            }

            return RedirectToPage("/Dashboard");
        }

        private class LoginResponse
        {
            public string? Id { get; set; }
            public string? Token { get; set; }
            public string? Username { get; set; }
            public string? Email { get; set; }
            public string? Role { get; set; }
        }
    }
}
