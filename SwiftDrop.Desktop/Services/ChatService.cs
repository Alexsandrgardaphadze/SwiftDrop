using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace SwiftDrop.Desktop.Services;

public class ChatService
{
    private HubConnection? _connection;

    public event Action<MessagePayload>? MessageReceived;
    public event Action<MessagePayload>? MessageSentConfirmed;
    public event Action<string, string, string, long>? FileNotificationReceived;
    public event Action<string, string, string, string, long>? MediaNotificationReceived;
    public event Action<string, string>? MessageEditedReceived;
    public event Action<string>? MessageDeletedReceived;
    public event Action<string>? MessageReadReceived;
    public event Action<string, bool>? UserTypingReceived;

    public async Task ConnectAsync(string serverUrl, string userId)
    {
        _connection = new HubConnectionBuilder()
            .WithUrl($"{serverUrl}/hubs/chat")
            .WithAutomaticReconnect()
            .Build();

        _connection.On<JsonElement>("ReceiveMessage", data =>
        {
            var payload = ParsePayload(data);
            if (payload != null) MessageReceived?.Invoke(payload);
        });

        _connection.On<JsonElement>("MessageSent", data =>
        {
            var payload = ParsePayload(data);
            if (payload != null) MessageSentConfirmed?.Invoke(payload);
        });

        _connection.On<string, string, string, long>("ReceiveFileNotification",
            (sid, tid, fn, size) =>
                FileNotificationReceived?.Invoke(sid, tid, fn, size));

        _connection.On<string, string, string, string, long>(
            "ReceiveMediaNotification",
            (sid, url, fn, mt, size) =>
                MediaNotificationReceived?.Invoke(sid, url, fn, mt, size));

        _connection.On<string, string>("MessageEdited",
            (msgId, newContent) =>
                MessageEditedReceived?.Invoke(msgId, newContent));

        _connection.On<string>("MessageDeleted",
            msgId => MessageDeletedReceived?.Invoke(msgId));

        _connection.On<string>("MessageRead",
            msgId => MessageReadReceived?.Invoke(msgId));

        _connection.On<string, bool>("UserTyping",
            (senderId, isTyping) =>
                UserTypingReceived?.Invoke(senderId, isTyping));

        _connection.Reconnected += _ =>
            _connection.InvokeAsync("Register", userId);

        await _connection.StartAsync();
        await _connection.InvokeAsync("Register", userId);
    }

    private MessagePayload? ParsePayload(JsonElement data)
    {
        try
        {
            return new MessagePayload(
                data.GetProperty("id").GetGuid(),
                data.GetProperty("senderId").GetString() ?? "",
                data.GetProperty("receiverId").GetString() ?? "",
                data.GetProperty("content").GetString() ?? "",
                data.GetProperty("sentAt").GetDateTime(),
                data.TryGetProperty("replyToMessageId", out var r)
                    ? r.ValueKind != JsonValueKind.Null
                        ? r.GetString() : null
                    : null,
                data.TryGetProperty("isDelivered", out var d) && d.GetBoolean()
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CLIENT] ParsePayload error: {ex.Message}");
            return null;
        }
    }

    public async Task SendMessageAsync(string senderId, string receiverId,
        string content, string? replyToMessageId = null)
    {
        if (_connection is null) return;
        await _connection.InvokeAsync("SendMessage", senderId, receiverId,
            content, replyToMessageId);
    }

    public async Task SendTypingAsync(string senderId, string receiverId,
        bool isTyping)
    {
        if (_connection is null) return;
        try
        {
            await _connection.InvokeAsync("SendTyping", senderId,
                receiverId, isTyping);
        }
        catch { /* ignore typing errors */ }
    }

    public async Task NotifyFileReadyAsync(string senderId, string receiverId,
        string transferId, string fileName, long fileSizeBytes)
    {
        if (_connection is null) return;
        await _connection.InvokeAsync("NotifyFileReady", senderId, receiverId,
            transferId, fileName, fileSizeBytes);
    }

    public async Task NotifyMediaReadyAsync(string senderId, string receiverId,
        string mediaUrl, string fileName, string mediaType, long sizeBytes)
    {
        if (_connection is null) return;
        await _connection.InvokeAsync("NotifyMediaReady", senderId, receiverId,
            mediaUrl, fileName, mediaType, sizeBytes);
    }

    public async Task EditMessageAsync(string messageId, string newContent)
    {
        if (_connection is null) return;
        await _connection.InvokeAsync("EditMessage", messageId, newContent);
    }

    public async Task DeleteMessageAsync(string messageId)
    {
        if (_connection is null) return;
        await _connection.InvokeAsync("DeleteMessage", messageId);
    }

    public async Task MarkReadAsync(string messageId, string readerId)
    {
        if (_connection is null) return;
        await _connection.InvokeAsync("MarkRead", messageId, readerId);
    }

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;
}

public record MessagePayload(
    Guid Id, string SenderId, string ReceiverId,
    string Content, DateTime SentAt,
    string? ReplyToMessageId, bool IsDelivered);