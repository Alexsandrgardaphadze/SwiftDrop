using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Threading.Tasks;

namespace SwiftDrop.Desktop.Services;

public class ChatService
{
    private HubConnection? _connection;
    public event Action<string, string, DateTime>? MessageReceived;
    public event Action<string, string, string, long>? FileNotificationReceived;

    public async Task ConnectAsync(string serverUrl, string userId)
    {
        _connection = new HubConnectionBuilder()
            .WithUrl($"{serverUrl}/hubs/chat")
            .WithAutomaticReconnect()
            .Build();

        _connection.On<string, string, DateTime>("ReceiveMessage",
            (senderId, content, sentAt) =>
            {
                MessageReceived?.Invoke(senderId, content, sentAt);
            });

        _connection.On<string, string, string, long>("ReceiveFileNotification",
            (senderId, transferId, fileName, fileSizeBytes) =>
            {
                Console.WriteLine($"[CLIENT] ReceiveFileNotification fired!");
                Console.WriteLine($"[CLIENT] sender={senderId} transfer={transferId} file={fileName}");
                FileNotificationReceived?.Invoke(senderId, transferId, fileName, fileSizeBytes);
            });

        _connection.Reconnecting += error =>
        {
            Console.WriteLine($"[CLIENT] Reconnecting: {error?.Message}");
            return Task.CompletedTask;
        };

        _connection.Reconnected += connectionId =>
        {
            Console.WriteLine($"[CLIENT] Reconnected, re-registering userId={userId}");
            return _connection.InvokeAsync("Register", userId);
        };

        await _connection.StartAsync();
        Console.WriteLine($"[CLIENT] Connected, registering userId={userId}");
        await _connection.InvokeAsync("Register", userId);
    }

    public async Task SendMessageAsync(string senderId, string receiverId, string content)
    {
        if (_connection is null) return;
        await _connection.InvokeAsync("SendMessage", senderId, receiverId, content);
    }

    public async Task NotifyFileReadyAsync(string senderId, string receiverId,
        string transferId, string fileName, long fileSizeBytes)
    {
        if (_connection is null) return;
        Console.WriteLine($"[CLIENT] Calling NotifyFileReady on hub...");
        await _connection.InvokeAsync("NotifyFileReady", senderId, receiverId,
            transferId, fileName, fileSizeBytes);
    }

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;
}