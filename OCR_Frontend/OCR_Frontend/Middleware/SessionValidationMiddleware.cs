using System;
using System.Net;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using OCR_Frontend.Helpers;
using OCR_Frontend.Services;

namespace OCR_Frontend.Middleware
{
    public class SessionValidationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<SessionValidationMiddleware> _logger;

        public SessionValidationMiddleware(RequestDelegate next, IHttpClientFactory httpClientFactory, ILogger<SessionValidationMiddleware> logger)
        {
            _next = next;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var path = context.Request.Path.Value ?? "";

            // Ignore static assets
            if (IsStaticAsset(path))
            {
                await _next(context);
                return;
            }

            // Prevent caching on all application pages so browser Back button cannot reveal protected views after logout
            context.Response.OnStarting(() =>
            {
                context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate, max-age=0";
                context.Response.Headers["Pragma"] = "no-cache";
                context.Response.Headers["Expires"] = "-1";
                return Task.CompletedTask;
            });

            // Identify public / auth pages
            bool isAuthPage = IsAuthPage(path);

            var authToken = context.Request.Cookies[AuthCookieHelper.AuthTokenCookie];
            var frontendInstanceId = context.Request.Cookies[AuthCookieHelper.FrontendInstanceIdCookie];

            // 1. If user is on an auth / public page (like /Login, /Register, etc.)
            if (isAuthPage)
            {
                // If they are visiting /Login or / with an existing AuthToken, verify if session is actually still alive
                if ((path.Equals("/Login", StringComparison.OrdinalIgnoreCase) || path.Equals("/", StringComparison.OrdinalIgnoreCase))
                    && !string.IsNullOrWhiteSpace(authToken))
                {
                    bool isValid = await ValidateSessionAsync(authToken, frontendInstanceId);
                    if (isValid)
                    {
                        context.Response.Redirect("/Dashboard");
                        return;
                    }
                    else
                    {
                        // Stale session (system stopped or session expired): purge cookies
                        AuthCookieHelper.ClearSessionCookies(context.Response);
                    }
                }

                await _next(context);
                return;
            }

            // 2. Protected page requested — check for AuthToken
            if (string.IsNullOrWhiteSpace(authToken))
            {
                AuthCookieHelper.ClearSessionCookies(context.Response);
                context.Response.Redirect("/Login");
                return;
            }

            // 3. Verify frontend system instance (did the frontend stop/restart?)
            if (string.IsNullOrEmpty(frontendInstanceId) || frontendInstanceId != FrontendSessionService.FrontendInstanceId)
            {
                _logger.LogInformation("[SessionValidation] Frontend system stopped or restarted. Invalidation triggered.");
                AuthCookieHelper.ClearSessionCookies(context.Response);
                context.Response.Redirect("/Login");
                return;
            }

            // 4. Verify token and backend system instance against backend
            bool sessionValid = await ValidateSessionAsync(authToken, frontendInstanceId);
            if (!sessionValid)
            {
                _logger.LogInformation("[SessionValidation] Backend session expired or backend stopped/restarted. Invalidation triggered.");
                AuthCookieHelper.ClearSessionCookies(context.Response);
                context.Response.Redirect("/Login");
                return;
            }

            await _next(context);
        }

        private async Task<bool> ValidateSessionAsync(string token, string? frontendInstanceId)
        {
            if (string.IsNullOrEmpty(frontendInstanceId) || frontendInstanceId != FrontendSessionService.FrontendInstanceId)
            {
                return false;
            }

            try
            {
                var client = _httpClientFactory.CreateClient("BackendClient");
                using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/validate-session");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                using var response = await client.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[SessionValidation] Could not contact backend: {Message}", ex.Message);
                return false;
            }
        }

        private static bool IsStaticAsset(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return path.StartsWith("/css", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/js", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/lib", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/favicon", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".woff", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAuthPage(string path)
        {
            if (string.IsNullOrEmpty(path) || path == "/") return true;
            return path.Equals("/Login", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/Register", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/ForgotPassword", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/ResetPassword", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/VerifyEmail", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/Privacy", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/Error", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/Logout", StringComparison.OrdinalIgnoreCase);
        }
    }
}
