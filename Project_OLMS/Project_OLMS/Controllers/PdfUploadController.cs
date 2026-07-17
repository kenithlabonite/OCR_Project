using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Project_OLMS.Data;
using Project_OLMS.Models;
using Project_OLMS.Models.DTO;
using Project_OLMS.Services;

namespace Project_OLMS.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PdfUploadController(
        AppDbContext context,
        IWebHostEnvironment env,
        IGeminiService geminiService) : ControllerBase
    {
        // Maximum allowed file size: 20 MB
        private const long MaxFileSizeBytes = 20 * 1024 * 1024;

        // ── Upload ────────────────────────────────────────────────────────────

        /// <summary>
        /// POST api/pdfupload
        /// Accepts a multipart/form-data request with:
        ///   • file         — the PDF file (required)
        ///   • Description  — optional text description
        ///   • UploadedByUserId — optional user id
        /// </summary>
        [HttpPost]
        [RequestSizeLimit(MaxFileSizeBytes)]
        public async Task<IActionResult> Upload(
            IFormFile file,
            [FromForm] UploadPdfRequest metadata)
        {
            // ── Validate file presence ────────────────────────────────────
            if (file == null || file.Length == 0)
                return BadRequest(new { message = "No file was provided or the file is empty." });

            // ── Validate MIME type ────────────────────────────────────────
            if (!string.Equals(file.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "Only PDF files are accepted." });

            // ── Validate file size ────────────────────────────────────────
            if (file.Length > MaxFileSizeBytes)
                return BadRequest(new { message = $"File exceeds the maximum allowed size of {MaxFileSizeBytes / (1024 * 1024)} MB." });

            // ── Validate optional UserId exists if supplied ───────────────
            if (!string.IsNullOrWhiteSpace(metadata.UploadedByUserId))
            {
                bool userExists = await context.Users.AnyAsync(u => u.Id == metadata.UploadedByUserId);
                if (!userExists)
                    return BadRequest(new { message = $"User with Id '{metadata.UploadedByUserId}' does not exist." });
            }

            // ── Persist file to disk ──────────────────────────────────────
            string storedPath = await SaveFileToDiskAsync(file);

            // ── AI analysis via Gemini ────────────────────────────────────
            // Read the saved bytes back for the Gemini request
            byte[] pdfBytes = await System.IO.File.ReadAllBytesAsync(storedPath);
            string? aiSummary = await geminiService.AnalyzePdfAsync(pdfBytes, file.FileName);

            // ── Persist metadata to database ──────────────────────────────
            var document = new Document
            {
                FileName           = file.FileName,
                ContentType        = file.ContentType,
                FileSizeBytes      = file.Length,
                StoredFilePath     = storedPath,
                Description        = metadata.Description,
                AiSummary          = aiSummary,
                UploadedByUserId   = metadata.UploadedByUserId,
                UploadedAt         = DateTime.UtcNow
            };

            context.Documents.Add(document);
            await context.SaveChangesAsync();

            return Ok(MapToResponse(document));
        }

        // ── Get All ───────────────────────────────────────────────────────────

        /// <summary>GET api/pdfupload — returns all uploaded documents.</summary>
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var documents = await context.Documents
                .AsNoTracking()
                .OrderByDescending(d => d.UploadedAt)
                .ToListAsync();

            return Ok(documents.Select(MapToResponse));
        }

        // ── Get By Id ─────────────────────────────────────────────────────────

        /// <summary>GET api/pdfupload/{id} — returns metadata for a single document.</summary>
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var document = await context.Documents
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == id);

            if (document == null)
                return NotFound(new { message = $"Document with Id {id} was not found." });

            return Ok(MapToResponse(document));
        }

        // ── Download ──────────────────────────────────────────────────────────

        /// <summary>GET api/pdfupload/{id}/download — streams the PDF file to the caller.</summary>
        [HttpGet("{id:int}/download")]
        public async Task<IActionResult> Download(int id)
        {
            var document = await context.Documents
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == id);

            if (document == null)
                return NotFound(new { message = $"Document with Id {id} was not found." });

            if (!System.IO.File.Exists(document.StoredFilePath))
                return NotFound(new { message = "The file could not be found on the server." });

            var fileStream = new FileStream(document.StoredFilePath, FileMode.Open, FileAccess.Read);
            return File(fileStream, "application/pdf", document.FileName);
        }

        // ── Delete ────────────────────────────────────────────────────────────

        /// <summary>DELETE api/pdfupload/{id} — removes the record and the file from disk.</summary>
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var document = await context.Documents.FindAsync(id);

            if (document == null)
                return NotFound(new { message = $"Document with Id {id} was not found." });

            // Remove physical file
            if (System.IO.File.Exists(document.StoredFilePath))
                System.IO.File.Delete(document.StoredFilePath);

            context.Documents.Remove(document);
            await context.SaveChangesAsync();

            return Ok(new { message = $"Document '{document.FileName}' was deleted successfully." });
        }

        // ── Private Helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Saves the uploaded form-file to wwwroot/uploads/pdfs/ and returns
        /// the full physical path so it can be stored in the database.
        /// </summary>
        private async Task<string> SaveFileToDiskAsync(IFormFile file)
        {
            // Resolve the uploads directory (created on demand)
            string uploadsDir = Path.Combine(env.WebRootPath ?? env.ContentRootPath, "uploads", "pdfs");
            Directory.CreateDirectory(uploadsDir);

            // Build a collision-proof file name: {timestamp}-{guid}.pdf
            string uniqueName = $"{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid()}.pdf";
            string fullPath   = Path.Combine(uploadsDir, uniqueName);

            await using var stream = new FileStream(fullPath, FileMode.Create);
            await file.CopyToAsync(stream);

            return fullPath;
        }

        /// <summary>Maps a <see cref="Document"/> entity to a <see cref="DocumentResponse"/> DTO.</summary>
        private static DocumentResponse MapToResponse(Document doc) => new()
        {
            Id               = doc.Id,
            FileName         = doc.FileName,
            ContentType      = doc.ContentType,
            FileSizeBytes    = doc.FileSizeBytes,
            Description      = doc.Description,
            AiSummary        = doc.AiSummary,
            UploadedByUserId = doc.UploadedByUserId,
            UploadedAt       = doc.UploadedAt
        };
    }
}
