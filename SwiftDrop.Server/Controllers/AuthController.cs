using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SwiftDrop.Server.Data;
using SwiftDrop.Core.Models;
using System.Security.Cryptography;
using System.Text;

namespace SwiftDrop.Server.Controllers;

[ApiController]
[Route("api/[controller]")]

public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    public AuthController(AppDbContext db) => _db = db;

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers()
    {
        var users = await _db.Users
            .Select(u => new { u.Id, u.Username, u.Email })
            .ToListAsync();
        return Ok(users);
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest req)
    {
        if (await _db.Users.AnyAsync(u => u.Email == req.Email))
            return BadRequest("Email already in use.");

        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = req.Username,
            Email = req.Email,
            PasswordHash = HashPassword(req.Password),
            CreatedAt = DateTime.UtcNow
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return Ok(new { user.Id, user.Username, user.Email });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest req)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == req.Email);
        if (user == null || user.PasswordHash != HashPassword(req.Password))
            return Unauthorized("Invalid credentials.");

        return Ok(new { user.Id, user.Username, user.Email });
    }

    private static string HashPassword(string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes);
    }
}

public record RegisterRequest(string Username, string Email, string Password);
public record LoginRequest(string Email, string Password);