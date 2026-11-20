using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Project_OLMS.Data;

namespace Project_OLMS.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class DashboardController(AppDbContext context) : ControllerBase
    {
        /// <summary>
        /// GET api/dashboard
        /// Returns live statistics pulled from the database for the dashboard overview.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetStats()
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
            bool isAdmin = User.IsInRole("Admin");

            if (!isAdmin && string.IsNullOrEmpty(userId))
                return Unauthorized(new { message = "Authentication required." });

            var documentsQuery = context.Documents.AsNoTracking();
            var examsQuery = context.ExamQuestionnaires.AsNoTracking();

            if (!isAdmin)
            {
                documentsQuery = documentsQuery.Where(d => d.UploadedByUserId == userId);
                examsQuery = examsQuery.Where(e => e.Document != null && e.Document.UploadedByUserId == userId);
            }

            // Total uploaded PDF documents
            int totalDocuments = await documentsQuery.CountAsync();

            // Total distinct batch topics
            var docTitles = await documentsQuery
                .Select(d => new { d.BatchTitle, d.FileName })
                .ToListAsync();

            int totalTopics = docTitles
                .Select(d => !string.IsNullOrWhiteSpace(d.BatchTitle) ? d.BatchTitle.Trim() : d.FileName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            // Total graded answer sheets
            var gradedSheetsQuery = context.GradedAnswerSheets.AsNoTracking();
            if (!isAdmin)
            {
                gradedSheetsQuery = gradedSheetsQuery.Where(g =>
                    g.Exam != null && g.Exam.Document != null &&
                    g.Exam.Document.UploadedByUserId == userId);
            }
            int totalGradedSheets = await gradedSheetsQuery.CountAsync();

            // Total AI-generated exam questionnaires
            int totalExams = await examsQuery.CountAsync();

            return Ok(new
            {
                TotalDocuments    = totalDocuments,
                TotalTopics       = totalTopics,
                TotalGradedSheets = totalGradedSheets,
                TotalExams        = totalExams,
                // Backward-compatible properties
                TotalProcessed    = totalDocuments,
                CreditsUsed       = totalExams
            });
        }
    }
}
