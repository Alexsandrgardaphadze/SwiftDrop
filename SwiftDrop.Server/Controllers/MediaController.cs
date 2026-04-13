using Microsoft.AspNetCore.Mvc;
using SwiftDrop.Server.Data;
using SwiftDrop.Core.Models;

namespace SwiftDrop.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MediaController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly string _mediaPath;

    public MediaController(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _mediaPath = Path.Combine(
            config["Storage:Path"] ?? "C:\\SwiftDropStorage", "Media");
        Directory.CreateDirectory(_mediaPath);
    }

    [HttpPost("upload")]
    [RequestSizeLimit(100 * 1024 * 1024)] // 100MB for media
    public async Task<IActionResult> Upload(
        IFormFile file,
        [FromForm] string senderId,
        [FromForm] string receiverId,
        [FromForm] string mediaType)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var allowed = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".mp4", ".mov" };
        if (!allowed.Contains(ext))
            return BadRequest("File type not supported.");

        var mediaId = Guid.NewGuid();
        var fileName = $"{mediaId}{ext}";
        var savePath = Path.Combine(_mediaPath, fileName);

        using var stream = System.IO.File.Create(savePath);
        await file.CopyToAsync(stream);

        return Ok(new
        {
            MediaId = mediaId,
            Url = $"/api/media/view/{fileName}",
            FileName = file.FileName,
            MediaType = mediaType,
            SizeBytes = file.Length
        });
    }

    [HttpGet("view/{fileName}")]
    public IActionResult View(string fileName)
    {
        var filePath = Path.Combine(_mediaPath, fileName);
        if (!System.IO.File.Exists(filePath)) return NotFound();

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var contentType = ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".mp4" => "video/mp4",
            ".mov" => "video/quicktime",
            _ => "application/octet-stream"
        };

        return PhysicalFile(filePath, contentType);
    }
}