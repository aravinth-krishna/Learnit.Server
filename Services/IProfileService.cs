using Learnit.Server.Models;

namespace Learnit.Server.Services
{
    public interface IProfileService
    {
        Task<UserProfileDto> GetProfileAsync(int userId);
        Task UpdateProfileAsync(int userId, UpdateProfileDto dto);
        Task ChangePasswordAsync(int userId, ChangePasswordDto dto);
    }
}
