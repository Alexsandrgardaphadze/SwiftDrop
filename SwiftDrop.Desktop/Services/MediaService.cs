using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace SwiftDrop.Desktop.Services;

public class MediaService
{
    private readonly HttpClient _http;
    private readonly string _cacheDir;

    public MediaService(string baseUrl)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        _http = new HttpClient(handler) { BaseAddress = new Uri(baseUrl) };
        _cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SwiftDrop", "MediaCache");
        Directory.CreateDirectory(_cacheDir);
    }

    public bool IsMediaFile(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".png" or ".gif"
                   or ".webp" or ".mp4" or ".mov";
    }

    public bool IsImageFile(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".png" or ".webp";
    }

    public bool IsGifFile(string filePath)
        => Path.GetExtension(filePath).ToLowerInvariant() == ".gif";

    public bool IsVideoFile(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext is ".mp4" or ".mov";
    }

    public async Task<MediaUploadResult?> UploadMediaAsync(
        string filePath, string senderId, string receiverId)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var mediaType = IsVideoFile(filePath) ? "video"
            : IsGifFile(filePath) ? "gif" : "image";

        using var content = new MultipartFormDataContent();
        var fileBytes = await File.ReadAllBytesAsync(filePath);
        var fileContent = new ByteArrayContent(fileBytes);
        content.Add(fileContent, "file", Path.GetFileName(filePath));
        content.Add(new StringContent(senderId), "senderId");
        content.Add(new StringContent(receiverId), "receiverId");
        content.Add(new StringContent(mediaType), "mediaType");

        var res = await _http.PostAsync("/api/media/upload", content);
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadFromJsonAsync<MediaUploadResult>();
    }

    public async Task<string?> DownloadToCache(string mediaUrl)
    {
        var fileName = Path.GetFileName(mediaUrl);
        var cachePath = Path.Combine(_cacheDir, fileName);
        if (File.Exists(cachePath)) return cachePath;

        var bytes = await _http.GetByteArrayAsync(mediaUrl);
        await File.WriteAllBytesAsync(cachePath, bytes);
        return cachePath;
    }

    public string BaseUrl => _http.BaseAddress?.ToString() ?? "";
}

public record MediaUploadResult(
    Guid MediaId, string Url, string FileName,
    string MediaType, long SizeBytes);