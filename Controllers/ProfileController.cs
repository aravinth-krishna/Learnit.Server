using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Learnit.Server.Data;
using Learnit.Server.Models;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

namespace Learnit.Server.Controllers
{
    [ApiController]
    [Route("api/profile")]
    [Authorize]
    public class ProfileController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly PasswordHasher<User> _hasher = new();

        public ProfileController(AppDbContext db)
        {
            _db = db;
        }

        private int GetUserId()
        {
            var userIdClaim = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
            {
                throw new UnauthorizedAccessException("Invalid user token");
            }

            return userId;
        }

        [HttpGet]
        public async Task<IActionResult> GetProfile()
        {
            var userId = GetUserId();
            var user = await _db.Users.FindAsync(userId);

            if (user == null)
                return NotFound();

            var profile = new UserProfileDto
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                CreatedAt = user.CreatedAt
            };

            var preferences = new UserPreferencesDto
            {
                StudySpeed = user.StudySpeed,
                MaxSessionMinutes = user.MaxSessionMinutes,
                WeeklyStudyLimitHours = user.WeeklyStudyLimitHours,
                DarkMode = user.DarkMode
            };

            return Ok(new { profile, preferences });
        }

        [HttpPut("info")]
        public async Task<IActionResult> UpdateProfile(UpdateProfileDto dto)
        {
            var userId = GetUserId();
            var user = await _db.Users.FindAsync(userId);

            if (user == null)
                return NotFound();

            // Check if email is already taken by another user
            if (dto.Email != user.Email && await _db.Users.AnyAsync(u => u.Email == dto.Email && u.Id != userId))
                return BadRequest(new { message = "Email is already taken" });

            user.FullName = dto.FullName;
            user.Email = dto.Email;

            await _db.SaveChangesAsync();

            return Ok(new { message = "Profile updated successfully" });
        }

        [HttpPut("preferences")]
        public async Task<IActionResult> UpdatePreferences(UserPreferencesDto dto)
        {
            var userId = GetUserId();
            var user = await _db.Users.FindAsync(userId);

            if (user == null)
                return NotFound();

            user.StudySpeed = dto.StudySpeed;
            user.MaxSessionMinutes = dto.MaxSessionMinutes;
            user.WeeklyStudyLimitHours = dto.WeeklyStudyLimitHours;
            user.DarkMode = dto.DarkMode;

            await _db.SaveChangesAsync();

            return Ok(new { message = "Preferences updated successfully" });
        }

        [HttpPut("password")]
        public async Task<IActionResult> ChangePassword(ChangePasswordDto dto)
        {
            var userId = GetUserId();
            var user = await _db.Users.FindAsync(userId);

            if (user == null)
                return NotFound();

            // Verify current password
            var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, dto.CurrentPassword);
            if (result == PasswordVerificationResult.Failed)
                return BadRequest(new { message = "Current password is incorrect" });

            // Validate new password
            if (string.IsNullOrWhiteSpace(dto.NewPassword) || dto.NewPassword.Length < 6)
                return BadRequest(new { message = "New password must be at least 6 characters long" });

            // Check password confirmation
            if (dto.NewPassword != dto.ConfirmPassword)
                return BadRequest(new { message = "Password confirmation does not match" });

            // Hash and save new password
            user.PasswordHash = _hasher.HashPassword(user, dto.NewPassword);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Password changed successfully" });
        }
    }
}
