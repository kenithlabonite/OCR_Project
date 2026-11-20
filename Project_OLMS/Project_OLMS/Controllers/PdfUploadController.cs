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
        IGeminiService geminiService,
        IAuditLogService auditLogService) : ControllerBase
    {
        // Maximum allowed file size: 100 MB
        private const long MaxFileSizeBytes = 100 * 1024 * 1024;

        // ── Upload ────────────────────────────────────────────────────────────

        /// <summary>
        /// POST api/pdfupload
        /// Accepts a multipart/form-data request with:
        ///   • file         — the PDF file (required)
        ///   • Description  — optional text description
        ///   • UploadedByUserId — optional user id
        /// </summary>
        [HttpPost]
        [RequestSizeLimit(MaxFileSizeBytes * 5)]
        public async Task<IActionResult> Upload(
            List<IFormFile> files,
            [FromForm] UploadPdfRequest metadata)
        {
            // ── Validate file presence ────────────────────────────────────
            if (files == null || files.Count == 0)
                return BadRequest(new { message = "No files were provided or the upload list is empty." });

            var responses = new List<DocumentResponse>();

            foreach (var file in files)
            {
                if (file.Length == 0)
                    continue;

                // ── Validate MIME type ────────────────────────────────────────
                if (!string.Equals(file.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
                    return BadRequest(new { message = $"Only PDF files are accepted. Invalid file: '{file.FileName}'" });

                // ── Validate file size ────────────────────────────────────────
                if (file.Length > MaxFileSizeBytes)
                    return BadRequest(new { message = $"File '{file.FileName}' exceeds the maximum allowed size of {MaxFileSizeBytes / (1024 * 1024)} MB." });
            }

            // ── Validate optional UserId exists if supplied ───────────────
            if (!string.IsNullOrWhiteSpace(metadata.UploadedByUserId))
            {
                bool userExists = await context.Users.AnyAsync(u => u.Id == metadata.UploadedByUserId);
                if (!userExists)
                    return BadRequest(new { message = $"User with Id '{metadata.UploadedByUserId}' does not exist." });
            }

            foreach (var file in files)
            {
                if (file.Length == 0)
                    continue;

                // ── Persist file to disk ──────────────────────────────────────
                string storedPath = await SaveFileToDiskAsync(file);

                // ── AI analysis via Gemini ────────────────────────────────────
                // Read the saved bytes back for the Gemini request
                byte[] pdfBytes = await System.IO.File.ReadAllBytesAsync(storedPath);
                string? aiSummary = await geminiService.AnalyzePdfAsync(pdfBytes, file.FileName);

                // ── Persist metadata to database ──────────────────────────────
                var effectiveUserId = GetCurrentUserId() ?? metadata.UploadedByUserId;
                var effectiveUsername = GetCurrentUsername();

                var document = new Document
                {
                    FileName           = file.FileName,
                    ContentType        = file.ContentType,
                    FileSizeBytes      = file.Length,
                    StoredFilePath     = storedPath,
                    Description        = metadata.Description,
                    BatchTitle         = metadata.BatchTitle,
                    AiSummary          = aiSummary,
                    UploadedByUserId   = effectiveUserId,
                    UploadedAt         = DateTime.UtcNow
                };

                context.Documents.Add(document);
                await context.SaveChangesAsync();

                await auditLogService.LogDocumentExamAsync(
                    action: "PDF Uploaded",
                    details: $"Uploaded document '{file.FileName}' ({(file.Length / 1024.0):F1} KB, ID: {document.Id})",
                    userId: effectiveUserId,
                    username: effectiveUsername,
                    ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString()
                );

                responses.Add(MapToResponse(document));
            }

            return Ok(responses);
        }

        // ── Get Distinct Titles ───────────────────────────────────────────────

        /// <summary>
        /// GET api/pdfupload/titles
        /// Returns a list of distinct batch titles. Documents without a BatchTitle
        /// are grouped under their file name so they still appear in the list.
        /// Each entry includes the title and the IDs of all documents in that group.
        /// </summary>
        [HttpGet("titles")]
        public async Task<IActionResult> GetTitles()
        {
            var documents = await context.Documents
                .AsNoTracking()
                .OrderByDescending(d => d.UploadedAt)
                .ToListAsync();

            // Group: if BatchTitle is set use it, otherwise fall back to FileName
            var groups = documents
                .GroupBy(d => !string.IsNullOrWhiteSpace(d.BatchTitle) ? d.BatchTitle : d.FileName)
                .Select(g => new
                {
                    Title       = g.Key,
                    DocumentIds = g.Select(d => d.Id).ToList(),
                    FileNames   = g.Select(d => d.FileName).ToList(),
                    FileCount   = g.Count(),
                    UploadedAt  = g.Max(d => d.UploadedAt)
                })
                .OrderByDescending(g => g.UploadedAt)
                .ToList();

            return Ok(groups);
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

            string fileName = document.FileName;
            int docId = document.Id;

            context.Documents.Remove(document);
            await context.SaveChangesAsync();

            await auditLogService.LogDocumentExamAsync(
                action: "PDF Deleted",
                details: $"Deleted document '{fileName}' (ID: {docId})",
                userId: GetCurrentUserId(),
                username: GetCurrentUsername(),
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            return Ok(new { message = $"Document '{fileName}' was deleted successfully." });
        }

        // ── Update Batch Title (Admin Only) ──────────────────────────────────

        /// <summary>
        /// PUT api/pdfupload/batch-title
        /// Renames all documents sharing a batch title (or fallback file name).
        /// Restricted strictly to the Administrator role; teachers / standard users receive 403 Forbidden.
        /// </summary>
        [HttpPut("batch-title")]
        public async Task<IActionResult> UpdateBatchTitle([FromBody] UpdateBatchTitleRequest request)
        {
            if (!IsCurrentUserAdmin())
                return StatusCode(403, new { message = "Access Denied: Only administrators are authorized to edit batch titles." });

            if (string.IsNullOrWhiteSpace(request?.NewTitle))
                return BadRequest(new { message = "NewTitle is required." });

            string oldTitle = request.OldTitle?.Trim() ?? string.Empty;
            string newTitle = request.NewTitle.Trim();

            var matchingDocs = await context.Documents
                .Where(d => (d.BatchTitle != null && d.BatchTitle == oldTitle)
                         || (d.BatchTitle == null && d.FileName == oldTitle))
                .ToListAsync();

            if (matchingDocs.Count == 0)
                return NotFound(new { message = $"No documents found matching batch title '{oldTitle}'." });

            foreach (var doc in matchingDocs)
            {
                doc.BatchTitle = newTitle;
            }

            await context.SaveChangesAsync();

            await auditLogService.LogDocumentExamAsync(
                action: "Batch Title Updated",
                details: $"Admin updated batch title from '{oldTitle}' to '{newTitle}' across {matchingDocs.Count} document(s).",
                userId: GetCurrentUserId(),
                username: GetCurrentUsername(),
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            return Ok(new
            {
                message = $"Successfully updated batch title to '{newTitle}'.",
                updatedCount = matchingDocs.Count
            });
        }

        /// <summary>
        /// PUT api/pdfupload/{id:int}/batch-title
        /// Updates the batch title for a single document.
        /// Restricted strictly to the Administrator role.
        /// </summary>
        [HttpPut("{id:int}/batch-title")]
        public async Task<IActionResult> UpdateDocumentBatchTitle(int id, [FromBody] UpdateDocumentBatchTitleRequest request)
        {
            if (!IsCurrentUserAdmin())
                return StatusCode(403, new { message = "Access Denied: Only administrators are authorized to edit batch titles." });

            var doc = await context.Documents.FindAsync(id);
            if (doc == null)
                return NotFound(new { message = $"Document with Id {id} was not found." });

            string oldTitle = doc.BatchTitle ?? "(None)";
            string? newTitle = string.IsNullOrWhiteSpace(request?.BatchTitle) ? null : request.BatchTitle.Trim();

            doc.BatchTitle = newTitle;
            await context.SaveChangesAsync();

            await auditLogService.LogDocumentExamAsync(
                action: "Document Batch Title Updated",
                details: $"Admin updated document '{doc.FileName}' (ID: {id}) batch title from '{oldTitle}' to '{newTitle ?? "(None)"}'.",
                userId: GetCurrentUserId(),
                username: GetCurrentUsername(),
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            return Ok(new
            {
                message = $"Document batch title updated to '{newTitle ?? "(None)"}'.",
                document = MapToResponse(doc)
            });
        }

        // ── Private Helpers ───────────────────────────────────────────────────

        private bool IsCurrentUserAdmin()
        {
            var roleClaim = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value
                         ?? User.FindFirst("role")?.Value;

            if (string.Equals(roleClaim, "Admin", StringComparison.OrdinalIgnoreCase))
                return true;

            var userId = GetCurrentUserId();
            if (!string.IsNullOrEmpty(userId))
            {
                var user = context.Users.AsNoTracking().FirstOrDefault(u => u.Id == userId);
                if (user != null && string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private string? GetCurrentUserId() =>
            User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;

        private string? GetCurrentUsername() =>
            User.FindFirst("username")?.Value;

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
            BatchTitle       = doc.BatchTitle,
            AiSummary        = doc.AiSummary,
            UploadedByUserId = doc.UploadedByUserId,
            UploadedAt       = doc.UploadedAt
        };
    }
}
