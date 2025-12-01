using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ReactAppTest.Server.Data;
using ReactAppTest.Server.Models;
using ReactAppTest.Server.Services;
using Microsoft.EntityFrameworkCore;
using User = ReactAppTest.Server.Models.User;

namespace ReactAppTest.Server.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly JwtService _jwt;
        private readonly PasswordHasher<User> _hasher = new();

        public AuthController(AppDbContext db, JwtService jwt)
        {
            _db = db;
            _jwt = jwt;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register(UserRegisterDto dto)
        {
            if (await _db.Users.AnyAsync(u => u.Email == dto.Email))
                return BadRequest("User already exists");

            var user = new User
            {
                FullName = dto.FullName,
                Email = dto.Email
            };

            user.PasswordHash = _hasher.HashPassword(user, dto.Password);

            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            return Ok("Registered successfully");
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login(UserLoginDto dto)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);

            if (user == null)
                return BadRequest("Invalid credentials");

            var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, dto.Password);

            if (result == PasswordVerificationResult.Failed)
                return BadRequest("Invalid credentials");

            var token = _jwt.Generate(user);

            return Ok(new { token });
        }
    }
}

