using Microsoft.AspNetCore.SignalR;
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

    public override Task OnConnectedAsync()
    {
        Console.WriteLine($"[HUB] Connected: {Context.ConnectionId}");
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = _userConnections.FirstOrDefault(x => x.Value == Context.ConnectionId).Key;
        if (userId != null)
        {
            _userConnections.Remove(userId);
            Console.WriteLine($"[HUB] Removed user {userId}");
        }
        return base.OnDisconnectedAsync(exception);
    }

    public async Task Register(string userId)
    {
        _userConnections[userId] = Context.ConnectionId;
        Console.WriteLine($"[HUB] Registered {userId} -> {Context.ConnectionId}");
        Console.WriteLine($"[HUB] Total users: {_userConnections.Count}");
        await Task.CompletedTask;
    }

    public async Task SendMessage(string senderId, string receiverId, string content)
    {
        var message = new Message
        {
            Id = Guid.NewGuid(),
            SenderId = Guid.Parse(senderId),
            ReceiverId = Guid.Parse(receiverId),
            Content = content,
            SentAt = DateTime.UtcNow
        };
        _db.Messages.Add(message);
        await _db.SaveChangesAsync();
        await Clients.All.SendAsync("ReceiveMessage", senderId, content, message.SentAt);
    }

    public async Task NotifyFileReady(string senderId, string receiverId,
        string transferId, string fileName, long fileSizeBytes)
    {
        Console.WriteLine($"[HUB] NotifyFileReady: sender={senderId} receiver={receiverId}");
        Console.WriteLine($"[HUB] Registered users: {_userConnections.Count}");
        foreach (var kvp in _userConnections)
            Console.WriteLine($"[HUB]   {kvp.Key} -> {kvp.Value}");

        // Notify sender confirmation
        await Clients.Caller.SendAsync("ReceiveMessage", senderId,
            $"📁 You sent '{fileName}' ({fileSizeBytes / 1024.0 / 1024.0:F2} MB) to receiver",
            DateTime.UtcNow);

        if (_userConnections.TryGetValue(receiverId, out var connectionId))
        {
            Console.WriteLine($"[HUB] Sending ReceiveFileNotification to {connectionId}");
            await Clients.Client(connectionId).SendAsync(
                "ReceiveFileNotification",
                senderId,
                transferId,
                fileName,
                fileSizeBytes);
            Console.WriteLine($"[HUB] Sent successfully!");
        }
        else
        {
            Console.WriteLine($"[HUB] ERROR: Receiver {receiverId} not found!");
            // Broadcast to all as emergency fallback so we can confirm client receives it
            Console.WriteLine($"[HUB] Broadcasting to ALL as fallback...");
            await Clients.All.SendAsync("ReceiveFileNotification",
                senderId, transferId, fileName, fileSizeBytes);
        }
    }
}