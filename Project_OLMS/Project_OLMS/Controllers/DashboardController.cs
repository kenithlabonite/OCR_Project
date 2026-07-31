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
            int totalProcessed = await context.Documents.CountAsync();

            // Average score percentage across all graded answer sheets
            double successRate = 0;
            bool anySheets = await context.GradedAnswerSheets.AnyAsync();
            if (anySheets)
            {
                successRate = await context.GradedAnswerSheets
                    .AverageAsync(g => g.ScorePercentage);
            }

            // Failed detections: sheets where OCR could not detect any valid answers
            // (TotalQuestions > 0 but CorrectCount == 0 AND no scanned answers found)
            int failedJobs = await context.GradedAnswerSheets
                .Where(g => g.TotalQuestions > 0 && g.CorrectCount == 0)
                .CountAsync();

            // Credits: number of AI-generated exam questionnaires (each one consumed AI tokens)
            int creditsUsed = await context.ExamQuestionnaires.CountAsync();

            return Ok(new
            {
                TotalProcessed = totalProcessed,
                SuccessRate    = Math.Round(successRate, 1),
                FailedJobs     = failedJobs,
                CreditsUsed    = creditsUsed
            });
        }
    }
}
