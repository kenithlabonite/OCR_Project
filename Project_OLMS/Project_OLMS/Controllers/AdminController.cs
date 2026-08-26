using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Project_OLMS.Data;

using Project_OLMS.Services;

namespace Project_OLMS.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AdminController(AppDbContext context, IAuditLogService auditLogService) : ControllerBase
    {
        // ── List all users ────────────────────────────────────────────────────

        /// <summary>
        /// GET api/admin/users
        /// Returns all registered users with their account status.
        /// </summary>
        [HttpGet("users")]
        public async Task<IActionResult> GetAllUsers()
        {
            var users = await context.Users
                .AsNoTracking()
                .OrderByDescending(u => u.Id)
                .Select(u => new
                {
                    u.Id,
                    u.Username,
                    u.Email,
                    u.Role,
                    u.IsActive,
                    u.IsEmailVerified
                })
                .ToListAsync();

            return Ok(users);
        }

        // ── Get Logs (UserActivity or DocumentExam) ──────────────────────────

        /// <summary>
        /// GET api/admin/logs?category=UserActivity
        /// Returns logs filtered by category ("UserActivity" or "DocumentExam").
        /// </summary>
        [HttpGet("logs")]
        public async Task<IActionResult> GetLogs([FromQuery] string? category)
        {
            var query = context.AuditLogs.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(category))
            {
                query = query.Where(l => l.Category == category);
            }

            var logs = await query
                .OrderByDescending(l => l.Timestamp)
                .Take(500)
                .ToListAsync();

            return Ok(logs);
        }

        // ── Toggle active/deactivate ──────────────────────────────────────────

        /// <summary>
        /// PUT api/admin/users/{userId}/toggle-active
        /// Activates or deactivates a user account.
        /// </summary>
        [HttpPut("users/{userId}/toggle-active")]
        public async Task<IActionResult> ToggleActive(string userId)
        {
            var user = await context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
                return NotFound(new { message = $"User with Id '{userId}' was not found." });

            user.IsActive = !user.IsActive;
            context.Users.Update(user);
            await context.SaveChangesAsync();

            string statusMsg = user.IsActive ? "Activated" : "Deactivated";
            await auditLogService.LogUserActivityAsync(
                action: $"Account {statusMsg}",
                details: $"Admin changed account status for user '{user.Username}' (ID: {user.Id}) to {statusMsg}.",
                userId: user.Id,
                username: user.Username,
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            return Ok(new
            {
                message = user.IsActive
                    ? $"User '{user.Username}' has been activated."
                    : $"User '{user.Username}' has been deactivated.",
                userId  = user.Id,
                isActive = user.IsActive
            });
        }

        // ── Change user role ──────────────────────────────────────────────────

        /// <summary>
        /// PUT api/admin/users/{userId}/change-role
        /// Changes a user's role between "User" and "Admin".
        /// </summary>
        [HttpPut("users/{userId}/change-role")]
        public async Task<IActionResult> ChangeRole(string userId, [FromBody] ChangeRoleRequest request)
        {
            if (request.Role != "User" && request.Role != "Admin")
                return BadRequest(new { message = "Role must be either 'User' or 'Admin'." });

            var user = await context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
                return NotFound(new { message = $"User with Id '{userId}' was not found." });

            string oldRole = user.Role;
            user.Role = request.Role;
            context.Users.Update(user);
            await context.SaveChangesAsync();

            await auditLogService.LogUserActivityAsync(
                action: "Role Changed",
                details: $"Admin updated user '{user.Username}' role from '{oldRole}' to '{request.Role}'.",
                userId: user.Id,
                username: user.Username,
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            return Ok(new
            {
                message = $"User '{user.Username}' role changed to '{request.Role}'.",
                userId  = user.Id,
                role    = user.Role
            });
        }

        // ── Delete user ───────────────────────────────────────────────────────

        /// <summary>
        /// DELETE api/admin/users/{userId}
        /// Permanently deletes a user account.
        /// </summary>
        [HttpDelete("users/{userId}")]
        public async Task<IActionResult> DeleteUser(string userId)
        {
            var user = await context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
                return NotFound(new { message = $"User with Id '{userId}' was not found." });

            string deletedUsername = user.Username;
            string deletedId = user.Id;

            context.Users.Remove(user);
            await context.SaveChangesAsync();

            await auditLogService.LogUserActivityAsync(
                action: "User Deleted",
                details: $"Admin permanently deleted user '{deletedUsername}' (ID: {deletedId}).",
                userId: deletedId,
                username: deletedUsername,
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            return Ok(new { message = $"User '{deletedUsername}' has been permanently deleted." });
        }

        // ── Create User Account ───────────────────────────────────────────────

        /// <summary>
        /// POST api/admin/users/create
        /// Creates a new user account directly from the admin panel.
        /// </summary>
        [HttpPost("users/create")]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new { message = "Username, Email, and Password are required." });
            }

            bool emailExists = await context.Users.AnyAsync(u => u.Email == request.Email);
            if (emailExists)
            {
                return BadRequest(new { message = "Email is already registered." });
            }

            string generatedId = await GenerateCustomIdAsync();
            string hashedPassword = BCrypt.Net.BCrypt.HashPassword(request.Password);

            var newUser = new Project_OLMS.Models.Auth
            {
                Id = generatedId,
                Username = request.Username,
                Email = request.Email,
                Password = hashedPassword,
                Role = string.Equals(request.Role, "Admin", StringComparison.OrdinalIgnoreCase) ? "Admin" : "User",
                IsActive = true,
                IsEmailVerified = request.IsEmailVerified
            };

            context.Users.Add(newUser);
            await context.SaveChangesAsync();

            await auditLogService.LogUserActivityAsync(
                action: "User Created by Admin",
                details: $"Admin created new account '{newUser.Username}' ({newUser.Email}) with Role '{newUser.Role}' and ID '{generatedId}'",
                userId: generatedId,
                username: newUser.Username,
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            return Ok(new
            {
                message = $"User account '{newUser.Username}' (ID: {generatedId}) created successfully!",
                userId = generatedId,
                username = newUser.Username,
                email = newUser.Email,
                role = newUser.Role
            });
        }

        private async Task<string> GenerateCustomIdAsync()
        {
            string yearPrefix = DateTime.UtcNow.ToString("yy");

            var lastUserThisYear = await context.Users
                .Where(u => u.Id.StartsWith(yearPrefix + "-"))
                .OrderByDescending(u => u.Id)
                .FirstOrDefaultAsync();

            int nextSequence = 0;

            if (lastUserThisYear != null)
            {
                var parts = lastUserThisYear.Id.Split('-');
                if (parts.Length > 1 && int.TryParse(parts[1], out int lastSequence))
                {
                    nextSequence = lastSequence + 1;
                }
            }

            return $"{yearPrefix}-{nextSequence:D4}";
        }

        public class ChangeRoleRequest
        {
            public string Role { get; set; } = "User";
        }

        public class CreateUserRequest
        {
            public string Username { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
            public string Role { get; set; } = "User";
            public bool IsEmailVerified { get; set; } = true;
        }
    }
}
