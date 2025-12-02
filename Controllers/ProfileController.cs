using Learnit.Server.Models;
using Learnit.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Learnit.Server.Controllers
{
    [ApiController]
    [Route("api/profile")]
    [Authorize]
    public class ProfileController : ControllerBase
    {
        private readonly IProfileService _service;

        public ProfileController(IProfileService service)
        {
            _service = service;
        }

        private int UserId =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        [HttpGet]
        public async Task<IActionResult> GetProfile()
        {
            var profile = await _service.GetProfileAsync(UserId);
            return Ok(profile);
        }

        [HttpPut]
        public async Task<IActionResult> UpdateProfile(UpdateProfileDto dto)
        {
            await _service.UpdateProfileAsync(UserId, dto);
            return NoContent();
        }

        [HttpPut("password")]
        public async Task<IActionResult> ChangePassword(ChangePasswordDto dto)
        {
            await _service.ChangePasswordAsync(UserId, dto);
            return NoContent();
        }
    }
}
