using Microsoft.AspNetCore.Mvc;
using SwiftDrop.Server.Data;
using SwiftDrop.Core.Models;
using System.Security.Cryptography;

namespace SwiftDrop.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FileController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly string _storagePath;

    public FileController(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _storagePath = config["Storage:Path"] ?? Path.Combine(Directory.GetCurrentDirectory(), "Uploads");
        Directory.CreateDirectory(_storagePath);
    }

    // Step 1: Sender calls this to initialize a transfer
    [HttpPost("init")]
    public async Task<IActionResult> InitTransfer(InitTransferRequest req)
    {
        var transfer = new FileTransfer
        {
            Id = Guid.NewGuid(),
            SenderId = req.SenderId,
            ReceiverId = req.ReceiverId,
            FileName = req.FileName,
            FileSizeBytes = req.FileSizeBytes,
            TotalChunks = req.TotalChunks,
            UploadedChunks = 0,
            Status = "Uploading",
            CreatedAt = DateTime.UtcNow
        };

        _db.FileTransfers.Add(transfer);
        await _db.SaveChangesAsync();

        // Create a folder for this transfer's chunks
        Directory.CreateDirectory(Path.Combine(_storagePath, transfer.Id.ToString()));

        return Ok(new { transfer.Id, transfer.TotalChunks });
    }

    // Step 2: Sender uploads chunks one by one
    [RequestSizeLimit(500 * 1024 * 1024)] // 500MB limit
    [HttpPost("chunk/{transferId}/{chunkIndex}")]
    public async Task<IActionResult> UploadChunk(Guid transferId, int chunkIndex, IFormFile chunk)
    {
        var transfer = await _db.FileTransfers.FindAsync(transferId);
        if (transfer is null) return NotFound();

        var chunkDir = Path.Combine(_storagePath, transferId.ToString());
        var chunkPath = Path.Combine(chunkDir, $"chunk_{chunkIndex}");

        using var stream = System.IO.File.Create(chunkPath);
        await chunk.CopyToAsync(stream);

        transfer.UploadedChunks++;
        if (transfer.UploadedChunks >= transfer.TotalChunks)
        {
            transfer.Status = "Assembling";
            await _db.SaveChangesAsync();
            await AssembleChunksAsync(transfer);
        }
        else
        {
            await _db.SaveChangesAsync();
        }

        return Ok(new { transfer.UploadedChunks, transfer.TotalChunks, transfer.Status });
    }

    // Step 3: Receiver downloads the assembled file
    [HttpGet("download/{transferId}")]
    public async Task<IActionResult> Download(Guid transferId)
    {
        var transfer = await _db.FileTransfers.FindAsync(transferId);
        if (transfer is null || transfer.Status != "Complete") return NotFound();

        var filePath = Path.Combine(_storagePath, transferId.ToString(), "assembled_" + transfer.FileName);
        if (!System.IO.File.Exists(filePath)) return NotFound();

        var stream = System.IO.File.OpenRead(filePath);
        return File(stream, "application/octet-stream", transfer.FileName);
    }

    [HttpGet("status/{transferId}")]
    public async Task<IActionResult> GetStatus(Guid transferId)
    {
        var transfer = await _db.FileTransfers.FindAsync(transferId);
        if (transfer is null) return NotFound();
        return Ok(new { transfer.Status, transfer.UploadedChunks, transfer.TotalChunks, transfer.FileName });
    }

    private async Task AssembleChunksAsync(FileTransfer transfer)
    {
        var chunkDir = Path.Combine(_storagePath, transfer.Id.ToString());
        var outputPath = Path.Combine(chunkDir, "assembled_" + transfer.FileName);

        using var output = System.IO.File.Create(outputPath);
        for (int i = 0; i < transfer.TotalChunks; i++)
        {
            var chunkPath = Path.Combine(chunkDir, $"chunk_{i}");
            using var chunkStream = System.IO.File.OpenRead(chunkPath);
            await chunkStream.CopyToAsync(output);
        }

        transfer.Status = "Complete";
        transfer.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }
}

public record InitTransferRequest(Guid SenderId, Guid ReceiverId, string FileName, long FileSizeBytes, int TotalChunks);