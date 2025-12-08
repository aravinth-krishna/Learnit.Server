using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Learnit.Server.Models;
using Learnit.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Learnit.Server.Controllers
{
    [ApiController]
    [Route("api/friends")]
    [Authorize]
    public class FriendsController : ControllerBase
    {
        private readonly FriendService _friends;

        public FriendsController(FriendService friends)
        {
            _friends = friends;
        }

        private int GetUserId()
        {
            var userIdClaim = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
                throw new UnauthorizedAccessException("Invalid user token");

            return userId;
        }

        [HttpGet]
        public ActionResult<List<FriendDto>> List()
        {
            var userId = GetUserId();
            var friends = _friends.GetFriendsAsync(userId).GetAwaiter().GetResult();
            return Ok(friends);
        }

        [HttpPost]
        public ActionResult<FriendDto> Add([FromBody] FriendDto friend)
        {
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(friend.Email))
                return BadRequest("Email is required to add a friend.");

            try
            {
                var added = _friends.AddFriendAsync(userId, friend.Email).GetAwaiter().GetResult();
                return Ok(added);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpDelete("{id}")]
        public IActionResult Delete(string id)
        {
            var userId = GetUserId();
            var removed = _friends.RemoveFriend(userId, id);
            return removed ? Ok() : NotFound();
        }
    }
}
