using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Project_OLMS.Models
{
    public class Auth
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        [StringLength(10)]
        public string Id { get; set; } = string.Empty;

        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Password { get; set; } = string.Empty;

        public bool IsEmailVerified { get; set; } = false;
        public string? VerificationOtp { get; set; }

        // ── ADD THIS LINE FOR FORGOT PASSWORD FEATURE ────────────────────────
        [StringLength(6)] // Constrains the DB column to match your 6-digit DTO rule
        public string? ResetPasswordOtp { get; set; }

        public string? Token { get; set; }
        public string? RefreshToken { get; set; }
    }
}