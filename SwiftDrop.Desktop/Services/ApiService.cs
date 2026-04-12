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
        var res = await _http.PostAsJsonAsync("/api/auth/register", new { username, email, password });
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadFromJsonAsync<UserDto>();
    }

    public async Task<UserDto?> LoginAsync(string email, string password)
    {
        var res = await _http.PostAsJsonAsync("/api/auth/login", new { email, password });
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadFromJsonAsync<UserDto>();
    }

    public async Task<List<UserDto>> GetUsersAsync()
    {
        var res = await _http.GetFromJsonAsync<List<UserDto>>("/api/auth/users");
        return res ?? new List<UserDto>();
    }
}

public record UserDto(Guid Id, string Username, string Email);