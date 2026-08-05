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

        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Password { get; set; } = string.Empty;

        public string? ContactNumber { get; set; }
        public string? Address { get; set; }
        public string Status { get; set; } = "Active";

        public bool IsEmailVerified { get; set; } = false;
        public string? VerificationOtp { get; set; }

        // ── ADD THIS LINE FOR FORGOT PASSWORD FEATURE ────────────────────────
        [StringLength(6)] // Constrains the DB column to match your 6-digit DTO rule
        public string? ResetPasswordOtp { get; set; }

        // ── Role & account status for admin management ───────────────────────
        [StringLength(20)]
        public string Role { get; set; } = "User"; // "User" or "Admin"

        public bool IsActive { get; set; } = true;

        public string? Token { get; set; }
        public string? RefreshToken { get; set; }
    }
}