using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Project_OLMS.Data;
using Project_OLMS.Services;
using MyDTOs = Project_OLMS.Models;
using System.Security.Cryptography;
using BC = BCrypt.Net.BCrypt;

namespace Project_OLMS.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController(AppDbContext context, IEmailService emailService) : ControllerBase
    {
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] MyDTOs.DTO.RegisterRequest request)
        {
            bool emailExists = await context.Users.AnyAsync(u => u.Email == request.Email);
            if (emailExists)
            {
                return BadRequest("Email is already registered.");
            }

            // 1. GENERATE THE CUSTOM FORMATTED ID (YY-0000)
            string generatedId = await GenerateCustomIdAsync();

            // Generate a secure 4-digit OTP number
            string generatedOtp = RandomNumberGenerator.GetInt32(1000, 10000).ToString();

            // Hash password
            string hashedPassword = BC.HashPassword(request.Password);

            var newUser = new MyDTOs.Auth
            {
                Id = generatedId, // Assign the custom generated ID here
                Username = request.Username,
                Email = request.Email,
                Password = hashedPassword,
                IsEmailVerified = false,
                VerificationOtp = generatedOtp
            };

            context.Users.Add(newUser);
            await context.SaveChangesAsync();

            // Send email logic...
            string emailSubject = "Your Account Verification Code - Project OLMS";
            string emailBody = $@"
<div style='font-family: Arial, sans-serif; max-width: 480px; margin: 0 auto; padding: 24px; border: 1px solid #e0e0e0; border-radius: 8px;'>
  <h2 style='color: #4f46e5;'>Welcome to Project OLMS!</h2>
  <p>Hello <strong>{newUser.Username}</strong>,</p>
  <p>Your account has been created. Please use the OTP code below to verify your email address:</p>
  <div style='text-align: center; margin: 24px 0;'>
    <span style='font-size: 36px; font-weight: bold; letter-spacing: 8px; color: #4f46e5;'>{generatedOtp}</span>
  </div>
  <p>Your Account ID: <strong>{generatedId}</strong></p>
  <p style='color: #888; font-size: 12px;'>This code is valid for one-time use. Do not share it with anyone.</p>
</div>";
            try
            {
                await emailService.SendEmailAsync(newUser.Email, emailSubject, emailBody);
            }
            catch (Exception ex)
            {
                // Log the error but don't fail registration — user is already created
                Console.WriteLine($"[EmailService] Failed to send OTP email to {newUser.Email}: {ex.Message}");
            }

            return Ok(new
            {
                message = "Registration successful!",
                id = generatedId, // Return the custom format ID to the user
                email = newUser.Email,
                debugOtpCode = generatedOtp
            });
        }

        // Helper method to safely compute the next ID sequence
        private async Task<string> GenerateCustomIdAsync()
        {
            // Get the last two digits of the current calendar year (e.g., 2026 -> "26")
            string yearPrefix = DateTime.UtcNow.ToString("yy");

            // Find users whose IDs start with the current year prefix (e.g., "26-")
            var lastUserThisYear = await context.Users
                .Where(u => u.Id.StartsWith(yearPrefix + "-"))
                .OrderByDescending(u => u.Id)
                .FirstOrDefaultAsync();

            int nextSequence = 0;

            if (lastUserThisYear != null)
            {
                // Extract the "0000" portion from the string (everything after the dash)
                string currentSequenceStr = lastUserThisYear.Id.Split('-')[1];
                if (int.TryParse(currentSequenceStr, out int lastSequence))
                {
                    nextSequence = lastSequence + 1;
                }
            }

            // Combine prefix and sequence padded out to 4 digits (e.g., "26-0000")
            return $"{yearPrefix}-{nextSequence:D4}";
        }

        [HttpPost("verify-otp")]
        public async Task<IActionResult> VerifyOtp([FromBody] MyDTOs.DTO.VerifyOtpRequest request)
        {
            var user = await context.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
            if (user == null)
            {
                return BadRequest("User record not found.");
            }

            if (user.IsEmailVerified)
            {
                return BadRequest("This account has already been verified.");
            }

            if (user.VerificationOtp != request.Otp)
            {
                return BadRequest("Invalid or incorrect verification code.");
            }

            user.IsEmailVerified = true;
            user.VerificationOtp = null;

            context.Users.Update(user);
            await context.SaveChangesAsync();

            return Ok(new { message = "Email verified successfully! Your account is activated and you can now log in." });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] MyDTOs.DTO.LoginRequest request)
        {
            var user = await context.Users.FirstOrDefaultAsync(u => u.Email == request.Email);

            // 3. VERIFY HASHED PASSWORD HERE
            // BC.Verify reads the embedded salt from 'user.Password' and safely validates 'request.Password'
            if (user == null || !BC.Verify(request.Password, user.Password))
            {
                return Unauthorized("Invalid email or password.");
            }

            if (!user.IsEmailVerified)
            {
                return Unauthorized("Please verify your account using your 4-digit OTP code before logging in.");
            }

            string jwtToken = GenerateJwtToken(user);
            string newRefreshToken = GenerateRefreshToken();

            user.Token = jwtToken;
            user.RefreshToken = newRefreshToken;

            context.Users.Update(user);
            await context.SaveChangesAsync();

            return Ok(new
            {
                id = user.Id,
                email = user.Email,
                token = jwtToken,
                refreshToken = newRefreshToken
            });
        }

        // ==========================================
        // NEW: FORGOT & RESET PASSWORD ENDPOINTS
        // ==========================================

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] MyDTOs.DTO.ForgotPasswordRequest request)
        {
            var user = await context.Users.FirstOrDefaultAsync(u => u.Email == request.Email);

            if (user == null)
            {
                return Ok(new { message = "If the email is registered, a password reset code has been sent." });
            }

            string resetOtp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

            // Replaced VerificationOtp with ResetPasswordOtp
            user.ResetPasswordOtp = resetOtp;

            context.Users.Update(user);
            await context.SaveChangesAsync();

            string emailSubject = "Password Reset Code - Project OLMS";
            string emailBody = $@"
<div style='font-family: Arial, sans-serif; max-width: 480px; margin: 0 auto; padding: 24px; border: 1px solid #e0e0e0; border-radius: 8px;'>
  <h2 style='color: #4f46e5;'>Password Reset Request</h2>
  <p>Hello <strong>{user.Username}</strong>,</p>
  <p>We received a request to reset your password. Use the code below:</p>
  <div style='text-align: center; margin: 24px 0;'>
    <span style='font-size: 36px; font-weight: bold; letter-spacing: 8px; color: #4f46e5;'>{resetOtp}</span>
  </div>
  <p>Enter this code on the Reset Password page to set a new password.</p>
  <p style='color: #888; font-size: 12px;'>If you didn't request this, you can safely ignore this email.</p>
</div>";
            try
            {
                await emailService.SendEmailAsync(user.Email, emailSubject, emailBody);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EmailService] Failed to send reset email to {user.Email}: {ex.Message}");
            }

            return Ok(new
            {
                message = "If the email is registered, a password reset code has been sent.",
                debugOtpCode = resetOtp
            });
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] MyDTOs.DTO.ResetPasswordRequest request)
        {
            var user = await context.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
            if (user == null)
            {
                return BadRequest("Invalid request.");
            }

            // Replaced VerificationOtp check with ResetPasswordOtp
            if (string.IsNullOrEmpty(user.ResetPasswordOtp) || user.ResetPasswordOtp != request.Otp)
            {
                return BadRequest("Invalid or expired password reset token.");
            }

            user.Password = BC.HashPassword(request.NewPassword);
            user.ResetPasswordOtp = null; // Clear the token out safely

            context.Users.Update(user);
            await context.SaveChangesAsync();

            return Ok(new { message = "Your password has been reset successfully. You can now log in." });
        }

        private static string GenerateRefreshToken()
        {
            var randomNumber = new byte[64];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomNumber);
            return Convert.ToBase64String(randomNumber);
        }

        private static string GenerateJwtToken(MyDTOs.Auth user)
        {
            _ = user;
            return "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.MockJwtTokenStringGoesHere";
        }
    }
}