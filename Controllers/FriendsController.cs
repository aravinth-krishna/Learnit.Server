using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Learnit.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Learnit.Server.Controllers
{
    [ApiController]
    [Route("api/friends")]
    [Authorize]
    public class FriendsController : ControllerBase
    {
        private static readonly ConcurrentDictionary<int, List<FriendDto>> Store = new();

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
            return Ok(Store.GetOrAdd(userId, _ => new List<FriendDto>()));
        }

        [HttpPost]
        public ActionResult<FriendDto> Add([FromBody] FriendDto friend)
        {
            var userId = GetUserId();
            var list = Store.GetOrAdd(userId, _ => new List<FriendDto>());
            friend.Id = Guid.NewGuid().ToString();
            list.Add(friend);
            return Ok(friend);
        }

        [HttpDelete("{id}")]
        public IActionResult Delete(string id)
        {
            var userId = GetUserId();
            if (!Store.TryGetValue(userId, out var list)) return NotFound();
            var removed = list.RemoveAll(f => f.Id == id) > 0;
            return removed ? Ok() : NotFound();
        }
    }
}
