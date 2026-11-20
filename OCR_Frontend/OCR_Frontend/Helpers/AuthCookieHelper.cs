using Microsoft.AspNetCore.Http;

namespace OCR_Frontend.Helpers
{
    public static class AuthCookieHelper
    {
        public const string AuthTokenCookie = "AuthToken";
        public const string UserIdCookie = "UserId";
        public const string DisplayNameCookie = "DisplayName";
        public const string DisplayEmailCookie = "DisplayEmail";
        public const string UserRoleCookie = "UserRole";
        public const string FrontendInstanceIdCookie = "FrontendInstanceId";
        public const string BackendInstanceIdCookie = "BackendInstanceId";
        public const string RememberedEmailCookie = "RememberedEmail";

        /// <summary>
        /// Writes a strict browser session cookie (no persistent Expires/Max-Age header).
        /// When the browser or system stops running, the browser automatically discards it.
        /// </summary>
        public static void SetSessionCookie(HttpResponse response, string key, string value, bool httpOnly = false)
        {
            var options = new CookieOptions
            {
                HttpOnly = httpOnly,
                Secure = false,
                SameSite = SameSiteMode.Lax,
                Path = "/",
                Expires = null,   // Explicitly null so browser does not persist to disk across sessions
                MaxAge = null
            };
            response.Cookies.Append(key, value, options);
        }

        /// <summary>
        /// Clears all authentication and session-tracking cookies.
        /// </summary>
        public static void ClearSessionCookies(HttpResponse response)
        {
            var expiredOptions = new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddDays(-1),
                HttpOnly = true,
                Secure = false,
                SameSite = SameSiteMode.Lax,
                Path = "/"
            };

            response.Cookies.Delete(AuthTokenCookie, expiredOptions);
            response.Cookies.Delete(UserIdCookie, expiredOptions);
            response.Cookies.Delete(DisplayNameCookie, expiredOptions);
            response.Cookies.Delete(DisplayEmailCookie, expiredOptions);
            response.Cookies.Delete(UserRoleCookie, expiredOptions);
            response.Cookies.Delete(FrontendInstanceIdCookie, expiredOptions);
            response.Cookies.Delete(BackendInstanceIdCookie, expiredOptions);
        }
    }
}
