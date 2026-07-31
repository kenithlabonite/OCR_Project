using System;
using System.Threading.Tasks;
using Project_OLMS.Data;
using Project_OLMS.Models;

namespace Project_OLMS.Services
{
    public interface IAuditLogService
    {
        Task LogUserActivityAsync(string action, string details, string? userId = null, string? username = null, string? ipAddress = null);
        Task LogDocumentExamAsync(string action, string details, string? userId = null, string? username = null, string? ipAddress = null);
    }

    public class AuditLogService(AppDbContext dbContext) : IAuditLogService
    {
        public async Task LogUserActivityAsync(string action, string details, string? userId = null, string? username = null, string? ipAddress = null)
        {
            await LogAsync("UserActivity", action, details, userId, username, ipAddress);
        }

        public async Task LogDocumentExamAsync(string action, string details, string? userId = null, string? username = null, string? ipAddress = null)
        {
            await LogAsync("DocumentExam", action, details, userId, username, ipAddress);
        }

        private async Task LogAsync(string category, string action, string details, string? userId, string? username, string? ipAddress)
        {
            try
            {
                var log = new AuditLog
                {
                    Category = category,
                    Action = action,
                    Details = details,
                    UserId = userId,
                    Username = username,
                    IpAddress = ipAddress,
                    Timestamp = DateTime.UtcNow
                };

                dbContext.AuditLogs.Add(log);
                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // Silent fail to avoid disrupting core user operations
                Console.WriteLine($"[AuditLogService Error]: {ex.Message}");
            }
        }
    }
}
