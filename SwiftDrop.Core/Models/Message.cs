namespace SwiftDrop.Core.Models;

public class Message
{
    public Guid Id { get; set; }
    public Guid SenderId { get; set; }
    public Guid ReceiverId { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? EditedAt { get; set; }
    public bool IsDeleted { get; set; } = false;
    public Guid? ReplyToMessageId { get; set; }
    public string? Reactions { get; set; } // JSON string: {"👍":["userId1"],"❤️":["userId2"]}
}