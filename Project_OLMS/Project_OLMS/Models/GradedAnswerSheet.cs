using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Project_OLMS.Models
{
    public class GradedAnswerSheet
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public int ExamId { get; set; }

        [Column(TypeName = "varchar(200)")]
        public string StudentName { get; set; } = string.Empty;

        public DateTime GradedAt { get; set; } = DateTime.UtcNow;

        public int TotalQuestions { get; set; }

        public int CorrectCount { get; set; }

        public double ScorePercentage { get; set; }

        [Required]
        [Column(TypeName = "longtext")]
        public string RawOcrText { get; set; } = string.Empty;

        [Required]
        [Column(TypeName = "longtext")]
        public string AnswersJson { get; set; } = "[]";

        /// <summary>
        /// JSON array of relative file paths for the uploaded answer sheet images.
        /// Example: ["graded-sheets/20260731-abc123-1.jpg","graded-sheets/20260731-abc123-2.jpg"]
        /// </summary>
        [Column(TypeName = "longtext")]
        public string? ImagePathsJson { get; set; }

        // Navigation property
        [ForeignKey(nameof(ExamId))]
        public ExamQuestionnaire? Exam { get; set; }
    }
}
