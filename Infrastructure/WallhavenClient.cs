using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WallpaperProfiles.Infrastructure;

internal sealed record WallhavenTag(int Id, string Name);
internal sealed record WallhavenWallpaper(string Id, string Resolution, string Category, long FileSize,
    Uri ImageUrl, Uri ThumbnailUrl, IReadOnlyList<WallhavenTag> Tags, string Uploader)
{
    public string Name => "Wallhaven · " + Id;
    public string Caption => $"{Resolution} · {Category}";
    public Uri PageUrl => new($"https://wallhaven.cc/w/{Id}");
}
internal sealed record WallhavenPage(IReadOnlyList<WallhavenWallpaper> Wallpapers, int CurrentPage, int LastPage, int Total);

internal sealed class WallhavenClient
{
    private static readonly HttpClient SharedHttp = CreateHttpClient();
    private static readonly SemaphoreSlim ApiGate = new(1);
    private static DateTimeOffset _nextApiRequest;
    private readonly HttpClient _http;
    private readonly bool _throttle;
    private const long MaxDownloadBytes = 100 * 1024 * 1024;

    public WallhavenClient(HttpClient? http = null, bool throttle = true)
    {
        _http = http ?? SharedHttp;
        _throttle = throttle;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Belie/2.3 (Wallpaper marketplace)");
        return client;
    }

    public async Task<WallhavenPage> SearchAsync(string query, string sorting, string minimumResolution,
        int page, CancellationToken token)
    {
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page));
        if (sorting is not ("relevance" or "date_added" or "favorites" or "toplist"))
            throw new ArgumentException("Unsupported sort order.", nameof(sorting));
        if (minimumResolution is not ("" or "1920x1080" or "2560x1440" or "3840x2160"))
            throw new ArgumentException("Unsupported resolution.", nameof(minimumResolution));
        var url = "https://wallhaven.cc/api/v1/search?purity=100&categories=111&order=desc"
            + $"&q={Uri.EscapeDataString(query.Trim())}&sorting={sorting}&page={page}&topRange=1M"
            + (minimumResolution.Length > 0 ? "&atleast=" + minimumResolution : "");
        using var json = await GetJsonAsync(url, token);
        var root = json.RootElement;
        var meta = root.GetProperty("meta");
        var wallpapers = root.GetProperty("data").EnumerateArray()
            .Where(IsSupported).Select(ParseWallpaper).ToArray();
        return new(wallpapers, meta.GetProperty("current_page").GetInt32(),
            meta.GetProperty("last_page").GetInt32(), meta.GetProperty("total").GetInt32());
    }

    public async Task<WallhavenWallpaper> GetWallpaperAsync(string id, CancellationToken token)
    {
        ValidateId(id);
        using var json = await GetJsonAsync("https://wallhaven.cc/api/v1/w/" + id, token);
        var data = json.RootElement.GetProperty("data");
        if (!IsSupported(data)) throw new InvalidDataException("This wallpaper is not available in the marketplace.");
        var result = ParseWallpaper(data);
        if (result.Id != id) throw new InvalidDataException("Wallhaven returned a different wallpaper.");
        return result;
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken token)
    {
        if (_throttle) await ApiGate.WaitAsync(token);
        try
        {
            if (_throttle)
            {
                var delay = _nextApiRequest - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero) await Task.Delay(delay, token);
                _nextApiRequest = DateTimeOffset.UtcNow.AddSeconds(2);
            }
            using var response = await _http.GetAsync(url, token);
            if (_throttle && response.StatusCode == HttpStatusCode.TooManyRequests)
                _nextApiRequest = DateTimeOffset.UtcNow.AddMinutes(1);
            CheckResponse(response);
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            return await JsonDocument.ParseAsync(stream, cancellationToken: token);
        }
        finally { if (_throttle) ApiGate.Release(); }
    }

    public async Task<byte[]> GetThumbnailAsync(Uri url, CancellationToken token)
    {
        ValidateMediaUrl(url, "th.wallhaven.cc");
        return await GetImageAsync(url, 5 * 1024 * 1024, token);
    }

    public async Task<byte[]> GetPreviewAsync(Uri url, CancellationToken token)
    {
        ValidateMediaUrl(url, "w.wallhaven.cc");
        return await GetImageAsync(url, MaxDownloadBytes, token);
    }

    private async Task<byte[]> GetImageAsync(Uri url, long limit, CancellationToken token)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        CheckResponse(response);
        using var output = new MemoryStream();
        await CopyImageAsync(response, output, limit, null, token);
        return output.ToArray();
    }

    public async Task<string> DownloadAsync(WallhavenWallpaper wallpaper, string folder,
        IProgress<int>? progress, CancellationToken token)
    {
        ValidateId(wallpaper.Id);
        ValidateMediaUrl(wallpaper.ImageUrl, "w.wallhaven.cc");
        var extension = Path.GetExtension(wallpaper.ImageUrl.AbsolutePath).ToLowerInvariant();
        if (extension is not (".jpg" or ".jpeg" or ".png"))
            throw new InvalidDataException("This image format is not supported.");
        Directory.CreateDirectory(folder);
        var destination = Path.Combine(folder, "wallhaven-" + wallpaper.Id + extension);
        // Reuse a completed download without overwriting a user's existing file.
        if (File.Exists(destination)) return destination;
        var temporary = Path.Combine(folder, "." + Guid.NewGuid().ToString("N") + ".part");
        try
        {
            using var response = await _http.GetAsync(wallpaper.ImageUrl, HttpCompletionOption.ResponseHeadersRead, token);
            CheckResponse(response);
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await CopyImageAsync(response, output, MaxDownloadBytes, progress, token);
            token.ThrowIfCancellationRequested();
            // Decode before committing so an error page or damaged image never enters the library.
            await Task.Run(() =>
            {
                using var input = File.OpenRead(temporary);
                var image = new System.Windows.Media.Imaging.BitmapImage();
                image.BeginInit();
                image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 32;
                image.StreamSource = input;
                image.EndInit();
                image.Freeze();
            }, token);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: false);
            return destination;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static async Task CopyImageAsync(HttpResponseMessage response, Stream output, long limit,
        IProgress<int>? progress, CancellationToken token)
    {
        var length = response.Content.Headers.ContentLength;
        if (length > limit) throw new InvalidDataException("This wallpaper is too large to download.");
        var type = response.Content.Headers.ContentType?.MediaType;
        if (type is not ("image/jpeg" or "image/png" or "application/octet-stream"))
            throw new InvalidDataException("The website did not return a supported image. Try again later.");
        await using var input = await response.Content.ReadAsStreamAsync(token);
        var header = new byte[8];
        var headerLength = 0;
        while (headerLength < header.Length)
        {
            var count = await input.ReadAsync(header.AsMemory(headerLength), token);
            if (count == 0) break;
            headerLength += count;
        }
        var jpeg = headerLength >= 3 && header[0] == 0xff && header[1] == 0xd8 && header[2] == 0xff;
        var png = headerLength == 8 && header.AsSpan().SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        if (!jpeg && !png) throw new InvalidDataException("The website returned an invalid image.");
        await output.WriteAsync(header.AsMemory(0, headerLength), token);
        long received = headerLength;
        var buffer = new byte[81920];
        int read;
        var lastPercent = -1;
        while ((read = await input.ReadAsync(buffer, token)) > 0)
        {
            received += read;
            if (received > limit) throw new InvalidDataException("This wallpaper is too large to download.");
            await output.WriteAsync(buffer.AsMemory(0, read), token);
            var percent = length > 0 ? (int)Math.Min(100, received * 100 / length.Value) : 0;
            if (percent != lastPercent) { progress?.Report(percent); lastPercent = percent; }
        }
        if (length.HasValue && received != length.Value) throw new InvalidDataException("The download was incomplete. Please try again.");
        progress?.Report(100);
    }

    private static bool IsSupported(JsonElement item) => item.GetProperty("purity").GetString() == "sfw"
        && item.GetProperty("file_type").GetString() is "image/jpeg" or "image/png";

    private static WallhavenWallpaper ParseWallpaper(JsonElement item)
    {
        var id = item.GetProperty("id").GetString()!;
        ValidateId(id);
        var image = new Uri(item.GetProperty("path").GetString()!);
        var thumbnail = new Uri(item.GetProperty("thumbs").GetProperty("large").GetString()!);
        ValidateMediaUrl(image, "w.wallhaven.cc");
        ValidateMediaUrl(thumbnail, "th.wallhaven.cc");
        var tags = item.TryGetProperty("tags", out var values)
            ? values.EnumerateArray().Select(t => new WallhavenTag(t.GetProperty("id").GetInt32(), t.GetProperty("name").GetString()!)).ToArray()
            : Array.Empty<WallhavenTag>();
        var uploader = item.TryGetProperty("uploader", out var user) ? user.GetProperty("username").GetString() ?? "" : "";
        return new(id, item.GetProperty("resolution").GetString()!, item.GetProperty("category").GetString()!,
            item.GetProperty("file_size").GetInt64(), image, thumbnail, tags, uploader);
    }

    private static void ValidateId(string id)
    {
        if (!Regex.IsMatch(id, "\\A[a-z0-9]{6}\\z")) throw new InvalidDataException("Invalid Wallhaven wallpaper ID.");
    }

    private static void ValidateMediaUrl(Uri url, string host)
    {
        if (!url.IsAbsoluteUri || url.Scheme != "https" || url.Host != host || !url.IsDefaultPort || url.UserInfo.Length > 0)
            throw new InvalidDataException("Wallhaven returned an unexpected image address.");
    }

    private static void CheckResponse(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new HttpRequestException("Wallhaven is receiving too many requests. Wait a minute, then retry.");
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new HttpRequestException("Wallhaven is currently blocking the connection. Please try again later.");
        response.EnsureSuccessStatusCode();
    }
}
