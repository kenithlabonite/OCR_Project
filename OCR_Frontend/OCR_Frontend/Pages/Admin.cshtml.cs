using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace OCR_Frontend.Pages
{
    public class AdminModel : PageModel
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public AdminModel(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public List<UserDto> Users { get; set; } = new();
        public List<AuditLogDto> UserActivityLogs { get; set; } = new();
        public List<AuditLogDto> DocumentExamLogs { get; set; } = new();

        public string? ErrorMessage { get; set; }
        public string? SuccessMessage { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            // Check admin role
            var role = Request.Cookies["UserRole"];
            if (role != "Admin")
            {
                return RedirectToPage("/Dashboard");
            }

            await LoadAllDataAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostToggleActiveAsync(string userId)
        {
            var role = Request.Cookies["UserRole"];
            if (role != "Admin") return RedirectToPage("/Dashboard");

            var client = CreateAuthClient();
            var response = await client.PutAsync($"/api/admin/users/{userId}/toggle-active", null);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<ActionResultDto>();
                SuccessMessage = result?.Message ?? "User status updated.";
            }
            else
            {
                ErrorMessage = await response.Content.ReadAsStringAsync();
            }

            await LoadAllDataAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostChangeRoleAsync(string userId, string newRole)
        {
            var role = Request.Cookies["UserRole"];
            if (role != "Admin") return RedirectToPage("/Dashboard");

            var client = CreateAuthClient();
            var response = await client.PutAsJsonAsync($"/api/admin/users/{userId}/change-role", new { role = newRole });
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<ActionResultDto>();
                SuccessMessage = result?.Message ?? "Role updated.";
            }
            else
            {
                ErrorMessage = await response.Content.ReadAsStringAsync();
            }

            await LoadAllDataAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostDeleteUserAsync(string userId)
        {
            var role = Request.Cookies["UserRole"];
            if (role != "Admin") return RedirectToPage("/Dashboard");

            var client = CreateAuthClient();
            var response = await client.DeleteAsync($"/api/admin/users/{userId}");
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<ActionResultDto>();
                SuccessMessage = result?.Message ?? "User deleted.";
            }
            else
            {
                ErrorMessage = await response.Content.ReadAsStringAsync();
            }

            await LoadAllDataAsync();
            return Page();
        }

        private HttpClient CreateAuthClient()
        {
            var client = _httpClientFactory.CreateClient("BackendClient");
            var token = Request.Cookies["AuthToken"];
            if (!string.IsNullOrEmpty(token))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
            return client;
        }

        private async Task LoadAllDataAsync()
        {
            var client = CreateAuthClient();

            // Load Users
            var usersResponse = await client.GetAsync("/api/admin/users");
            if (usersResponse.IsSuccessStatusCode)
            {
                Users = await usersResponse.Content.ReadFromJsonAsync<List<UserDto>>() ?? new();
            }

            // Load User Activity Logs
            var userLogsResp = await client.GetAsync("/api/admin/logs?category=UserActivity");
            if (userLogsResp.IsSuccessStatusCode)
            {
                UserActivityLogs = await userLogsResp.Content.ReadFromJsonAsync<List<AuditLogDto>>() ?? new();
            }

            // Load Document & Exam Logs
            var docLogsResp = await client.GetAsync("/api/admin/logs?category=DocumentExam");
            if (docLogsResp.IsSuccessStatusCode)
            {
                DocumentExamLogs = await docLogsResp.Content.ReadFromJsonAsync<List<AuditLogDto>>() ?? new();
            }
        }

        public class UserDto
        {
            public string Id { get; set; } = string.Empty;
            public string Username { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string Role { get; set; } = string.Empty;
            public bool IsActive { get; set; }
            public bool IsEmailVerified { get; set; }
        }

        public class AuditLogDto
        {
            public int Id { get; set; }
            public string? UserId { get; set; }
            public string? Username { get; set; }
            public string Category { get; set; } = string.Empty;
            public string Action { get; set; } = string.Empty;
            public string Details { get; set; } = string.Empty;
            public string? IpAddress { get; set; }
            public DateTime Timestamp { get; set; }
        }

        private class ActionResultDto
        {
            public string? Message { get; set; }
        }
    }
}
