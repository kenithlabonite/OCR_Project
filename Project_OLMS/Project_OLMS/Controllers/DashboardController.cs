using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Project_OLMS.Data;

namespace Project_OLMS.Controllers
{
    [ApiController]
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
            // Total uploaded PDF documents
            int totalDocuments = await context.Documents.CountAsync();

            // Total distinct batch topics
            var docTitles = await context.Documents
                .AsNoTracking()
                .Select(d => new { d.BatchTitle, d.FileName })
                .ToListAsync();

            int totalTopics = docTitles
                .Select(d => !string.IsNullOrWhiteSpace(d.BatchTitle) ? d.BatchTitle.Trim() : d.FileName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            // Total graded answer sheets
            int totalGradedSheets = await context.GradedAnswerSheets.CountAsync();

            // Total AI-generated exam questionnaires
            int totalExams = await context.ExamQuestionnaires.CountAsync();

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
