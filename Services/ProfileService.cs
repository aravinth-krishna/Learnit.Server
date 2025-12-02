using Learnit.Server.Data;
using Learnit.Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using User = Learnit.Server.Models.User;

namespace Learnit.Server.Services
{
    public class ProfileService : IProfileService
    {
        private readonly AppDbContext _db;
        private readonly PasswordHasher<User> _hasher = new();

        public ProfileService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<UserProfileDto> GetProfileAsync(int userId)
        {
            var user = await _db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
                throw new Exception("User not found");

            return new UserProfileDto
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                StudySpeed = user.StudySpeed,
                MaxSessionMinutes = user.MaxSessionMinutes,
                WeeklyLimitHours = user.WeeklyLimitHours,
                DarkMode = user.DarkMode
            };
        }

        public async Task UpdateProfileAsync(int userId, UpdateProfileDto dto)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
                throw new Exception("User not found");

            user.FullName = dto.FullName;
            user.StudySpeed = dto.StudySpeed;
            user.MaxSessionMinutes = dto.MaxSessionMinutes;
            user.WeeklyLimitHours = dto.WeeklyLimitHours;
            user.DarkMode = dto.DarkMode;

            await _db.SaveChangesAsync();
        }

        public async Task ChangePasswordAsync(int userId, ChangePasswordDto dto)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
                throw new Exception("User not found");

            var verify = _hasher.VerifyHashedPassword(user, user.PasswordHash, dto.CurrentPassword);

            if (verify == PasswordVerificationResult.Failed)
                throw new Exception("Incorrect current password");

            user.PasswordHash = _hasher.HashPassword(user, dto.NewPassword);

            await _db.SaveChangesAsync();
        }
    }
}
