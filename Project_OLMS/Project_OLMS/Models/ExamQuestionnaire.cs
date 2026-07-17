using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Project_OLMS.Models
{
    /// <summary>
    /// Stores one AI-generated multiple-choice exam questionnaire for a document.
    /// The ten <see cref="QuestionItem"/> objects are persisted as a JSON string
    /// in <see cref="QuestionsJson"/> and deserialised on read.
    /// </summary>
    public class ExamQuestionnaire
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        /// <summary>Foreign key — the document this exam was generated from.</summary>
        public int DocumentId { get; set; }

        /// <summary>UTC timestamp when the exam was generated.</summary>
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// JSON-serialised list of <see cref="QuestionItem"/> objects.
        /// Stored as TEXT; deserialised by the service/controller layer.
        /// </summary>
        [Required]
        [Column(TypeName = "longtext")]
        public string QuestionsJson { get; set; } = "[]";

        // ── Navigation ────────────────────────────────────────────────
        [ForeignKey(nameof(DocumentId))]
        public Document? Document { get; set; }
    }
}
