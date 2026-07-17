using Project_OLMS.Models;
using System.ComponentModel.DataAnnotations;

namespace Project_OLMS.Models.DTO
{
    public class LoginRequest
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Invalid email format.")] // Optional: Added to ensure they input a valid email structure
        public string Email { get; set; } = string.Empty;

        [Required(AllowEmptyStrings = false, ErrorMessage = "Password is required.")]
        [MinLength(8, ErrorMessage = "Password must be at least 8 characters long.")]
        [RegularExpression(@".*[A-Z].*", ErrorMessage = "Password must contain at least one capital letter.")]
        public string Password { get; set; } = string.Empty;
    }

    public class RegisterRequest
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "Username is required.")]
        [StringLength(50, MinimumLength = 3, ErrorMessage = "Username must be between 3 and 50 characters.")]
        public string Username { get; set; } = string.Empty;

        [Required(AllowEmptyStrings = false, ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Invalid email address format.")]
        public string Email { get; set; } = string.Empty;

        [Required(AllowEmptyStrings = false, ErrorMessage = "Password is required.")]
        // Protects your server's BCrypt engine from processing massive, malicious string payloads
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be between 8 and 100 characters.")]
        [RegularExpression(@".*[A-Z].*", ErrorMessage = "Password must contain at least one capital letter.")]
        public string Password { get; set; } = string.Empty;
    }

    // New DTO class added to handle your 4-digit OTP incoming validation
    public class VerifyOtpRequest
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Invalid email address format.")]
        public string Email { get; set; } = string.Empty;

        [Required(AllowEmptyStrings = false, ErrorMessage = "OTP is required.")]
        [RegularExpression(@"^\d{4}$", ErrorMessage = "OTP must be exactly 4 numeric digits.")]
        public string Otp { get; set; } = string.Empty;
    }

    public class ForgotPasswordRequest
    {
        public string Email { get; set; } = string.Empty;
    }

    public class ResetPasswordRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Otp { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }

    // ── PDF Upload DTOs ──────────────────────────────────────────────────────

    /// <summary>
    /// Metadata sent alongside the PDF file in a multipart/form-data request.
    /// The actual file is bound via IFormFile in the controller action parameter.
    /// </summary>
    public class UploadPdfRequest
    {
        /// <summary>Optional human-readable description for the document.</summary>
        [StringLength(1000, ErrorMessage = "Description must not exceed 1000 characters.")]
        public string? Description { get; set; }

        /// <summary>
        /// Id of the user uploading the file. Treated as optional here so the
        /// endpoint can also be called without authentication during development.
        /// </summary>
        [StringLength(10)]
        public string? UploadedByUserId { get; set; }
    }

    /// <summary>
    /// Returned to the caller after a successful PDF upload.
    /// </summary>
    public class DocumentResponse
    {
        public int Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public string? Description { get; set; }
        public string? AiSummary { get; set; }
        public string? UploadedByUserId { get; set; }
        public DateTime UploadedAt { get; set; }
    }

    // ── Exam Questionnaire DTOs ──────────────────────────────────────────────

    /// <summary>
    /// Returned immediately after a new exam questionnaire is generated.
    /// </summary>
    public class GenerateExamResponse
    {
        /// <summary>Id of the newly created ExamQuestionnaire record.</summary>
        public int ExamId { get; set; }

        /// <summary>Id of the source document.</summary>
        public int DocumentId { get; set; }

        /// <summary>Original file name of the source document.</summary>
        public string DocumentFileName { get; set; } = string.Empty;

        /// <summary>UTC timestamp when the exam was generated.</summary>
        public DateTime GeneratedAt { get; set; }

        /// <summary>The 10 multiple-choice questions with answer keys.</summary>
        public List<Models.QuestionItem> Questions { get; set; } = new();
    }

    /// <summary>
    /// Returned when retrieving a previously stored exam questionnaire.
    /// </summary>
    public class ExamQuestionnaireResponse
    {
        public int ExamId { get; set; }
        public int DocumentId { get; set; }
        public string DocumentFileName { get; set; } = string.Empty;
        public DateTime GeneratedAt { get; set; }
        public List<Models.QuestionItem> Questions { get; set; } = new();
    }

    // ── OCR Checking DTOs ────────────────────────────────────────────────────

    public class CheckedAnswerItem
    {
        public int QuestionNumber { get; set; }
        public string ScannedAnswer { get; set; } = string.Empty;
        public string CorrectAnswer { get; set; } = string.Empty;
        public bool IsCorrect { get; set; }
    }

    public class CheckAnswersResponse
    {
        public int ExamId { get; set; }
        public int TotalQuestions { get; set; }
        public int CorrectCount { get; set; }
        public double ScorePercentage { get; set; }
        public string RawOcrText { get; set; } = string.Empty;
        public List<CheckedAnswerItem> Results { get; set; } = new();
    }
}