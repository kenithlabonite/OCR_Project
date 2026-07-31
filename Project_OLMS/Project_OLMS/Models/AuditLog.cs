using System;
using System.ComponentModel.DataAnnotations;

namespace Project_OLMS.Models
{
    public class AuditLog
    {
        [Key]
        public int Id { get; set; }

        public string? UserId { get; set; }
        public string? Username { get; set; }

        [Required]
        [StringLength(50)]
        public string Category { get; set; } = string.Empty; // "UserActivity" or "DocumentExam"

        [Required]
        [StringLength(100)]
        public string Action { get; set; } = string.Empty; // e.g. "User Login", "PDF Uploaded", "Exam Generated", "Questionnaire Downloaded", "Answer Key Downloaded", "Sheet Graded"

        public string Details { get; set; } = string.Empty;

        public string? IpAddress { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
