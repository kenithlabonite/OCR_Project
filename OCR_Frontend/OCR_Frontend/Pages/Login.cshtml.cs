using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OCR_Frontend.Helpers;
using OCR_Frontend.Services;

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
            // If the user has a valid active session, redirect straight to Dashboard
            if (Request.Cookies.TryGetValue(AuthCookieHelper.AuthTokenCookie, out var token) && !string.IsNullOrWhiteSpace(token))
            {
                return RedirectToPage("/Dashboard");
            }

            // Pre-fill remembered email and check RememberMe if previously saved
            if (Request.Cookies.TryGetValue(AuthCookieHelper.RememberedEmailCookie, out var rememberedEmail) && !string.IsNullOrWhiteSpace(rememberedEmail))
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
                var errorText = await ApiErrorHelper.ExtractErrorMessageAsync(response, "Invalid login credentials.");
                ModelState.AddModelError(string.Empty, errorText);
                return Page();
            }

            var result = await response.Content.ReadFromJsonAsync<LoginResponse>();

            if (result?.Token == null)
            {
                ModelState.AddModelError(string.Empty, "Login failed: no token received.");
                return Page();
            }

            // Strict session cookies: Expires = null.
            // The session lasts for the day while browser and system are running.
            // Once the browser or system stops running, the session is invalidated.
            AuthCookieHelper.SetSessionCookie(Response, AuthCookieHelper.AuthTokenCookie, result.Token, httpOnly: true);

            // Store instance markers to detect system restarts
            AuthCookieHelper.SetSessionCookie(Response, AuthCookieHelper.FrontendInstanceIdCookie, FrontendSessionService.FrontendInstanceId, httpOnly: true);
            if (!string.IsNullOrEmpty(result.SystemInstanceId))
            {
                AuthCookieHelper.SetSessionCookie(Response, AuthCookieHelper.BackendInstanceIdCookie, result.SystemInstanceId, httpOnly: true);
            }

            // Store display info in readable session cookies
            if (!string.IsNullOrEmpty(result.Id))
                AuthCookieHelper.SetSessionCookie(Response, AuthCookieHelper.UserIdCookie, result.Id, httpOnly: false);
            if (!string.IsNullOrEmpty(result.Username))
                AuthCookieHelper.SetSessionCookie(Response, AuthCookieHelper.DisplayNameCookie, result.Username, httpOnly: false);
            if (!string.IsNullOrEmpty(result.Email))
                AuthCookieHelper.SetSessionCookie(Response, AuthCookieHelper.DisplayEmailCookie, result.Email, httpOnly: false);
            if (!string.IsNullOrEmpty(result.Role))
                AuthCookieHelper.SetSessionCookie(Response, AuthCookieHelper.UserRoleCookie, result.Role, httpOnly: false);

            // Remember Me ONLY preserves the email address on the login form for future visits
            if (RememberMe)
            {
                Response.Cookies.Append(AuthCookieHelper.RememberedEmailCookie, Email, new Microsoft.AspNetCore.Http.CookieOptions
                {
                    HttpOnly = false,
                    Secure = false,
                    SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
                    Expires = DateTimeOffset.UtcNow.AddDays(30)
                });
            }
            else
            {
                Response.Cookies.Delete(AuthCookieHelper.RememberedEmailCookie);
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
            public string? SystemInstanceId { get; set; }
        }
    }
}
