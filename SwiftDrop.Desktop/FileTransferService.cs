using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftDrop.Desktop.Services;

public class FileTransferService
{
    private readonly HttpClient _http;
    private const int ChunkSize = 4 * 1024 * 1024; // 4MB chunks

    public FileTransferService(string baseUrl)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        _http = new HttpClient(handler) { BaseAddress = new Uri(baseUrl) };
    }

    public async Task<Guid> SendFileAsync(string filePath, Guid senderId, Guid receiverId,
        Action<double> onProgress, CancellationToken ct = default)
    {
        var fileInfo = new FileInfo(filePath);
        var totalChunks = (int)Math.Ceiling((double)fileInfo.Length / ChunkSize);

        var initRes = await _http.PostAsJsonAsync("/api/file/init", new
        {
            senderId,
            receiverId,
            fileName = fileInfo.Name,
            fileSizeBytes = fileInfo.Length,
            totalChunks
        });

        var init = await initRes.Content.ReadFromJsonAsync<InitResponse>();
        if (init is null) throw new Exception("Failed to initialize transfer.");

        using var fs = File.OpenRead(filePath);
        var buffer = new byte[ChunkSize];
        for (int i = 0; i < totalChunks; i++)
        {
            ct.ThrowIfCancellationRequested();
            int bytesRead = await fs.ReadAsync(buffer, 0, ChunkSize, ct);
            var chunk = buffer[..bytesRead];

            using var content = new MultipartFormDataContent();
            var byteContent = new ByteArrayContent(chunk);
            byteContent.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            content.Add(byteContent, "chunk", $"chunk_{i}");

            await _http.PostAsync($"/api/file/chunk/{init.Id}/{i}", content, ct);
            onProgress((double)(i + 1) / totalChunks * 100);
        }

        return init.Id;
    }

    public async Task DownloadFileAsync(Guid transferId, string saveToPath,
        Action<double> onProgress, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/file/download/{transferId}",
            HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? 1;
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var file = File.Create(saveToPath);

        var buffer = new byte[81920];
        long downloaded = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), ct);
            downloaded += read;
            onProgress((double)downloaded / total * 100);
        }
    }

    private record InitResponse(Guid Id, int TotalChunks);
}