using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace OCR_Frontend.Pages
{
    public class ProfileModel : PageModel
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public ProfileModel(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        [BindProperty]
        public ProfileInputModel Input { get; set; } = new();

        public UserProfileDto? CurrentProfile { get; set; }
        public string? UserId { get; set; }
        public string? UserRole { get; set; }
        public bool IsActiveAccount { get; set; } = true;
        public string? SuccessMessage { get; set; }
        public string? ErrorMessage { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            var authToken = Request.Cookies["AuthToken"];
            if (string.IsNullOrEmpty(authToken))
            {
                return RedirectToPage("/Login");
            }

            var userId = Request.Cookies["UserId"];
            var displayEmail = Request.Cookies["DisplayEmail"];

            string identifier = !string.IsNullOrEmpty(userId) ? userId : displayEmail ?? "";
            if (string.IsNullOrEmpty(identifier))
            {
                return RedirectToPage("/Login");
            }

            var client = _httpClientFactory.CreateClient("BackendClient");
            try
            {
                var response = await client.GetAsync($"/api/auth/profile/{identifier}");
                if (response.IsSuccessStatusCode)
                {
                    var user = await response.Content.ReadFromJsonAsync<UserProfileDto>();
                    if (user != null)
                    {
                        CurrentProfile = user;
                        UserId = user.Id;
                        UserRole = user.Role;
                        IsActiveAccount = user.IsActive;

                        Input = new ProfileInputModel
                        {
                            FirstName = user.FirstName,
                            LastName = user.LastName,
                            Username = user.Username,
                            Email = user.Email,
                            ContactNumber = user.ContactNumber,
                            Address = user.Address,
                            Status = user.Status ?? "Active"
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Unable to load profile data: {ex.Message}";
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var authToken = Request.Cookies["AuthToken"];
            if (string.IsNullOrEmpty(authToken))
            {
                return RedirectToPage("/Login");
            }

            var userId = Request.Cookies["UserId"];
            var displayEmail = Request.Cookies["DisplayEmail"];

            string identifier = !string.IsNullOrEmpty(userId) ? userId : displayEmail ?? "";
            if (string.IsNullOrEmpty(identifier))
            {
                return RedirectToPage("/Login");
            }

            var client = _httpClientFactory.CreateClient("BackendClient");
            var updatePayload = new
            {
                firstName = Input.FirstName,
                lastName = Input.LastName,
                username = Input.Username,
                email = Input.Email,
                newPassword = Input.NewPassword,
                contactNumber = Input.ContactNumber,
                address = Input.Address,
                status = Input.Status
            };

            var response = await client.PutAsJsonAsync($"/api/auth/profile/{identifier}", updatePayload);

            if (!response.IsSuccessStatusCode)
            {
                ErrorMessage = await OCR_Frontend.Helpers.ApiErrorHelper.ExtractErrorMessageAsync(response, "Failed to update profile.");
                return Page();
            }

            var result = await response.Content.ReadFromJsonAsync<ProfileUpdateResult>();
            if (result?.User != null)
            {
                CurrentProfile = result.User;
                UserId = result.User.Id;
                UserRole = result.User.Role;
                IsActiveAccount = result.User.IsActive;

                var displayCookieOptions = new Microsoft.AspNetCore.Http.CookieOptions
                {
                    HttpOnly = false,
                    Secure = false,
                    SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
                    Expires = DateTimeOffset.UtcNow.AddHours(1)
                };

                if (!string.IsNullOrEmpty(result.User.Username))
                    Response.Cookies.Append("DisplayName", result.User.Username, displayCookieOptions);

                if (!string.IsNullOrEmpty(result.User.Email))
                    Response.Cookies.Append("DisplayEmail", result.User.Email, displayCookieOptions);
            }

            SuccessMessage = "Profile updated successfully!";
            return Page();
        }

        public class ProfileInputModel
        {
            public string? FirstName { get; set; }
            public string? LastName { get; set; }
            public string Username { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string? NewPassword { get; set; }
            public string? ContactNumber { get; set; }
            public string? Address { get; set; }
            public string Status { get; set; } = "Active";
        }

        public class UserProfileDto
        {
            public string Id { get; set; } = string.Empty;
            public string? FirstName { get; set; }
            public string? LastName { get; set; }
            public string Username { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string? ContactNumber { get; set; }
            public string? Address { get; set; }
            public string Status { get; set; } = "Active";
            public string Role { get; set; } = "User";
            public bool IsActive { get; set; } = true;
        }

        public class ProfileUpdateResult
        {
            public string? Message { get; set; }
            public UserProfileDto? User { get; set; }
        }
    }
}
