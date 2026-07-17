using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Project_OLMS.Models
{
    public class Document
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        /// <summary>
        /// Original file name as uploaded by the user.
        /// </summary>
        [Required]
        [StringLength(255)]
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// MIME content type (should always be "application/pdf").
        /// </summary>
        [Required]
        [StringLength(100)]
        public string ContentType { get; set; } = string.Empty;

        /// <summary>
        /// Size of the uploaded file in bytes.
        /// </summary>
        public long FileSizeBytes { get; set; }

        /// <summary>
        /// Path on the server where the PDF is stored.
        /// </summary>
        [Required]
        [StringLength(500)]
        public string StoredFilePath { get; set; } = string.Empty;

        /// <summary>
        /// Optional description provided by the uploader.
        /// </summary>
        [StringLength(1000)]
        public string? Description { get; set; }

        /// <summary>
        /// AI-generated summary produced by Gemini after upload.
        /// Null when AI analysis was skipped or failed.
        /// </summary>
        [StringLength(4000)]
        public string? AiSummary { get; set; }

        /// <summary>
        /// Foreign key — the Id of the user who uploaded the document.
        /// </summary>
        [StringLength(10)]
        public string? UploadedByUserId { get; set; }

        /// <summary>
        /// UTC timestamp when the document was uploaded.
        /// </summary>
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

        // ── Navigation ───────────────────────────────────────────────
        [ForeignKey(nameof(UploadedByUserId))]
        public Auth? UploadedByUser { get; set; }
    }
}
