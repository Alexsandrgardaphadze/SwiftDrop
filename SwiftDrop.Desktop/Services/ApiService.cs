using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace SwiftDrop.Desktop.Services;

public class ApiService
{
    private readonly HttpClient _http;

    public ApiService(string baseUrl)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        _http = new HttpClient(handler) { BaseAddress = new Uri(baseUrl) };
    }

    public async Task<UserDto?> RegisterAsync(string username, string email, string password)
    {
        var res = await _http.PostAsJsonAsync("/api/auth/register",
            new { username, email, password });
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadFromJsonAsync<UserDto>();
    }

    public async Task<UserDto?> LoginAsync(string email, string password)
    {
        var res = await _http.PostAsJsonAsync("/api/auth/login",
            new { email, password });
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadFromJsonAsync<UserDto>();
    }

    public async Task<List<UserDto>> GetUsersAsync()
    {
        var res = await _http.GetFromJsonAsync<List<UserDto>>("/api/auth/users");
        return res ?? new List<UserDto>();
    }

    public async Task<List<MessageHistoryDto>> GetConversationAsync(
    Guid userId1, Guid userId2)
    {
        try
        {
            var res = await _http.GetFromJsonAsync<List<MessageHistoryDto>>(
                $"/api/messages/conversation/{userId1}/{userId2}");
            return res ?? new();
        }
        catch { return new(); }
    }

    public async Task ReactToMessageAsync(Guid messageId, string emoji, string userId)
    {
        await _http.PatchAsJsonAsync($"/api/messages/{messageId}/react",
            new { emoji, userId });
    }

    public async Task EditMessageAsync(Guid messageId, string content)
    {
        await _http.PatchAsJsonAsync($"/api/messages/{messageId}/edit",
            new { content });
    }

    public async Task DeleteMessageAsync(Guid messageId)
    {
        await _http.DeleteAsync($"/api/messages/{messageId}");
    }

    public record MessageHistoryDto(
        Guid Id, Guid SenderId, Guid ReceiverId, string Content,
        DateTime SentAt, DateTime? ReadAt, DateTime? DeliveredAt,
        DateTime? EditedAt, Guid? ReplyToMessageId, string? Reactions);
}

public record UserDto(Guid Id, string Username, string Email);