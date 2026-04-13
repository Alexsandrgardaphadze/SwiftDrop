using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SwiftDrop.Server.Data;
using SwiftDrop.Core.Models;
using System.Text.Json;

namespace SwiftDrop.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MessagesController : ControllerBase
{
    private readonly AppDbContext _db;
    public MessagesController(AppDbContext db) => _db = db;

    [HttpGet("conversation/{userId1}/{userId2}")]
    public async Task<IActionResult> GetConversation(Guid userId1, Guid userId2)
    {
        var messages = await _db.Messages
            .Where(m => !m.IsDeleted &&
                ((m.SenderId == userId1 && m.ReceiverId == userId2) ||
                 (m.SenderId == userId2 && m.ReceiverId == userId1)))
            .OrderBy(m => m.SentAt)
            .Select(m => new MessageDto(
                m.Id, m.SenderId, m.ReceiverId, m.Content,
                m.SentAt, m.ReadAt, m.DeliveredAt, m.EditedAt,
                m.ReplyToMessageId, m.Reactions))
            .ToListAsync();
        return Ok(messages);
    }

    [HttpPatch("{messageId}/edit")]
    public async Task<IActionResult> EditMessage(Guid messageId,
        [FromBody] EditMessageRequest req)
    {
        var msg = await _db.Messages.FindAsync(messageId);
        if (msg is null) return NotFound();
        msg.Content = req.Content;
        msg.EditedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("{messageId}")]
    public async Task<IActionResult> DeleteMessage(Guid messageId)
    {
        var msg = await _db.Messages.FindAsync(messageId);
        if (msg is null) return NotFound();
        msg.IsDeleted = true;
        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpPatch("{messageId}/react")]
    public async Task<IActionResult> ReactToMessage(Guid messageId,
        [FromBody] ReactRequest req)
    {
        var msg = await _db.Messages.FindAsync(messageId);
        if (msg is null) return NotFound();

        var reactions = string.IsNullOrEmpty(msg.Reactions)
            ? new Dictionary<string, List<string>>()
            : JsonSerializer.Deserialize<Dictionary<string, List<string>>>(
                msg.Reactions) ?? new();

        if (!reactions.ContainsKey(req.Emoji))
            reactions[req.Emoji] = new List<string>();

        if (reactions[req.Emoji].Contains(req.UserId))
            reactions[req.Emoji].Remove(req.UserId);
        else
            reactions[req.Emoji].Add(req.UserId);

        if (reactions[req.Emoji].Count == 0)
            reactions.Remove(req.Emoji);

        msg.Reactions = JsonSerializer.Serialize(reactions);
        await _db.SaveChangesAsync();
        return Ok(new { reactions = msg.Reactions });
    }

    [HttpPatch("{messageId}/read")]
    public async Task<IActionResult> MarkRead(Guid messageId)
    {
        var msg = await _db.Messages.FindAsync(messageId);
        if (msg is null) return NotFound();
        msg.ReadAt ??= DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok();
    }
}

public record MessageDto(
    Guid Id, Guid SenderId, Guid ReceiverId, string Content,
    DateTime SentAt, DateTime? ReadAt, DateTime? DeliveredAt,
    DateTime? EditedAt, Guid? ReplyToMessageId, string? Reactions);
public record EditMessageRequest(string Content);
public record ReactRequest(string Emoji, string UserId);