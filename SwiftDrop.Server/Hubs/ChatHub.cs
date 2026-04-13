using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SwiftDrop.Core.Models;
using SwiftDrop.Server.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SwiftDrop.Server.Hubs;

public class ChatHub : Hub
{
    private readonly AppDbContext _db;
    private static readonly Dictionary<string, string> _userConnections = new();

    public ChatHub(AppDbContext db) => _db = db;

    public async Task SendTyping(string senderId, string receiverId, bool isTyping)
    {
        if (_userConnections.TryGetValue(receiverId, out var conn))
            await Clients.Client(conn).SendAsync("UserTyping", senderId, isTyping);
    }

    public override Task OnConnectedAsync()
    {
        Console.WriteLine($"[HUB] Connected: {Context.ConnectionId}");
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = _userConnections
            .FirstOrDefault(x => x.Value == Context.ConnectionId).Key;
        if (userId != null)
        {
            _userConnections.Remove(userId);
            Console.WriteLine($"[HUB] Removed: {userId}");
        }
        return base.OnDisconnectedAsync(exception);
    }

    public async Task Register(string userId)
    {
        _userConnections[userId] = Context.ConnectionId;
        Console.WriteLine($"[HUB] Registered {userId}");

        // Fix: parse to Guid first so EF Core can translate the query properly
        if (!Guid.TryParse(userId, out var userGuid)) return;

        // Fix: materialize with ToListAsync() before iterating
        var pending = await _db.Messages
            .Where(m => m.ReceiverId == userGuid
                && m.DeliveredAt == null
                && !m.IsDeleted)
            .ToListAsync();

        foreach (var m in pending)
            m.DeliveredAt = DateTime.UtcNow;

        if (pending.Count > 0)
            await _db.SaveChangesAsync();
    }

    public async Task SendMessage(string senderId, string receiverId,
        string content, string? replyToMessageId = null)
    {
        if (!Guid.TryParse(senderId, out var senderGuid)) return;
        if (!Guid.TryParse(receiverId, out var receiverGuid)) return;

        var message = new Message
        {
            Id = Guid.NewGuid(),
            SenderId = senderGuid,
            ReceiverId = receiverGuid,
            Content = content,
            SentAt = DateTime.UtcNow,
            DeliveredAt = _userConnections.ContainsKey(receiverId)
                ? DateTime.UtcNow : null,
            ReplyToMessageId = replyToMessageId != null
                && Guid.TryParse(replyToMessageId, out var replyGuid)
                ? replyGuid : null
        };
        _db.Messages.Add(message);
        await _db.SaveChangesAsync();

        var msgData = new
        {
            id = message.Id,
            senderId,
            receiverId,
            content,
            sentAt = message.SentAt,
            replyToMessageId,
            isDelivered = message.DeliveredAt != null
        };

        if (_userConnections.TryGetValue(receiverId, out var receiverConn))
            await Clients.Client(receiverConn).SendAsync("ReceiveMessage", msgData);

        await Clients.Caller.SendAsync("MessageSent", msgData);
    }

    public async Task NotifyFileReady(string senderId, string receiverId,
        string transferId, string fileName, long fileSizeBytes)
    {
        Console.WriteLine($"[HUB] NotifyFileReady: {senderId} -> {receiverId}");
        if (_userConnections.TryGetValue(receiverId, out var connectionId))
            await Clients.Client(connectionId).SendAsync("ReceiveFileNotification",
                senderId, transferId, fileName, fileSizeBytes);

        await Clients.Caller.SendAsync("MessageSent", new
        {
            id = Guid.NewGuid(),
            senderId,
            receiverId,
            content = $"📁 Sent '{fileName}'",
            sentAt = DateTime.UtcNow,
            isDelivered = _userConnections.ContainsKey(receiverId)
        });
    }

    public async Task NotifyMediaReady(string senderId, string receiverId,
        string mediaUrl, string fileName, string mediaType, long sizeBytes)
    {
        if (_userConnections.TryGetValue(receiverId, out var connectionId))
            await Clients.Client(connectionId).SendAsync("ReceiveMediaNotification",
                senderId, mediaUrl, fileName, mediaType, sizeBytes);
    }

    public async Task EditMessage(string messageId, string newContent)
    {
        if (!Guid.TryParse(messageId, out var msgGuid)) return;
        var msg = await _db.Messages.FindAsync(msgGuid);
        if (msg is null) return;
        msg.Content = newContent;
        msg.EditedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var receiverId = msg.ReceiverId.ToString();
        if (_userConnections.TryGetValue(receiverId, out var conn))
            await Clients.Client(conn).SendAsync("MessageEdited",
                messageId, newContent);
        await Clients.Caller.SendAsync("MessageEdited", messageId, newContent);
    }

    public async Task DeleteMessage(string messageId)
    {
        if (!Guid.TryParse(messageId, out var msgGuid)) return;
        var msg = await _db.Messages.FindAsync(msgGuid);
        if (msg is null) return;
        msg.IsDeleted = true;
        await _db.SaveChangesAsync();

        var receiverId = msg.ReceiverId.ToString();
        if (_userConnections.TryGetValue(receiverId, out var conn))
            await Clients.Client(conn).SendAsync("MessageDeleted", messageId);
        await Clients.Caller.SendAsync("MessageDeleted", messageId);
    }

    public async Task MarkRead(string messageId, string readerId)
    {
        if (!Guid.TryParse(messageId, out var msgGuid)) return;
        var msg = await _db.Messages.FindAsync(msgGuid);
        if (msg is null) return;
        msg.ReadAt ??= DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var senderId = msg.SenderId.ToString();
        if (_userConnections.TryGetValue(senderId, out var conn))
            await Clients.Client(conn).SendAsync("MessageRead", messageId);
    }
}