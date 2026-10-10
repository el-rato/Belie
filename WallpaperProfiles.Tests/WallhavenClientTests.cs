using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WallpaperProfiles.Infrastructure;
using Xunit;

namespace WallpaperProfiles.Tests;

public sealed class WallhavenClientTests
{
    [Fact]
    public async Task Search_EncodesWebsiteQueryAndFilters_AndReadsTagsAndPagination()
    {
        using var handler = new MarketplaceHandler(MarketplaceHandler.CreateImage());
        using var http = new HttpClient(handler);
        var client = new WallhavenClient(http, throttle: false);
        var page = await client.SearchAsync("+nature -anime & sky", "toplist", "3840x2160", 2, default);
        var request = handler.Requests.First();
        Assert.Contains("q=%2Bnature%20-anime%20%26%20sky", request);
        Assert.Contains("purity=100", request);
        Assert.Contains("atleast=3840x2160", request);
        Assert.Contains("sorting=toplist", request);
        Assert.Equal(2, page.CurrentPage);
        Assert.Equal(2, page.LastPage);
        var details = await client.GetWallpaperAsync("aaa111", default);
        Assert.Equal("Forest artist", details.Uploader);
        Assert.Equal(new WallhavenTag(42, "nature"), Assert.Single(details.Tags));
        Assert.Equal("https://wallhaven.cc/w/aaa111", details.PageUrl.AbsoluteUri);
    }

    [Fact]
    public async Task Search_LandscapeOnly_ExcludesPortraitAndSquare_OnEveryPage()
    {
        using var handler = new MarketplaceHandler(MarketplaceHandler.CreateImage());
        using var http = new HttpClient(handler);
        var client = new WallhavenClient(http, throttle: false);
        foreach (var number in new[] { 1, 2 })
        {
            var page = await client.SearchAsync("orientations", "date_added", "", number, default, landscapeOnly: true);
            Assert.Equal(new[] { "2560x1440", "3440x1440", "1600x1200" }, page.Wallpapers.Select(w => w.Resolution));
            Assert.Contains("ratios=landscape", handler.Requests.Last());
            Assert.Equal(number, page.CurrentPage);
        }
        var all = await client.SearchAsync("orientations", "date_added", "", 1, default);
        Assert.Equal(5, all.Wallpapers.Count);
        Assert.DoesNotContain("ratios=", handler.Requests.Last());
    }

    [Fact]
    public async Task Download_CommitsDecodedOriginal_AndReusesExistingFile()
    {
        var folder = Path.Combine(Path.GetTempPath(), "belie-market-" + Guid.NewGuid().ToString("N"));
        var bytes = MarketplaceHandler.CreateImage();
        try
        {
            using var handler = new MarketplaceHandler(bytes);
            using var http = new HttpClient(handler);
            var client = new WallhavenClient(http, throttle: false);
            var wallpaper = await client.GetWallpaperAsync("aaa111", default);
            var path = await client.DownloadAsync(wallpaper, folder, null, default);
            Assert.Equal(bytes, File.ReadAllBytes(path));
            Assert.Equal(path, await client.DownloadAsync(wallpaper, folder, null, default));
            Assert.Single(Directory.GetFiles(folder));
            Assert.Single(handler.Requests, r => r.Contains("/full/"));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("text/html")]
    public async Task Download_RejectsInvalidImages_AndRemovesPartialFiles(string contentType)
    {
        var folder = Path.Combine(Path.GetTempPath(), "belie-market-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var handler = new MarketplaceHandler(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, contentType);
            using var http = new HttpClient(handler);
            var client = new WallhavenClient(http, throttle: false);
            var wallpaper = await client.GetWallpaperAsync("aaa111", default);
            await Assert.ThrowsAnyAsync<Exception>(() => client.DownloadAsync(wallpaper, folder, null, default));
            Assert.Empty(Directory.GetFiles(folder));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public async Task Download_CancelDuringTransfer_RemovesPartialFile()
    {
        var folder = Path.Combine(Path.GetTempPath(), "belie-market-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var cancellation = new CancellationTokenSource();
            using var handler = new MarketplaceHandler(MarketplaceHandler.CreateImage()) { CancelTransfer = cancellation };
            using var http = new HttpClient(handler);
            var client = new WallhavenClient(http, throttle: false);
            var wallpaper = await client.GetWallpaperAsync("aaa111", default);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.DownloadAsync(wallpaper, folder, null, cancellation.Token));
            Assert.Empty(Directory.GetFiles(folder));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public async Task Search_RateLimit_HasActionableError_AndUnsafeAddressesAreRejected()
    {
        using var handler = new MarketplaceHandler(MarketplaceHandler.CreateImage());
        using var http = new HttpClient(handler);
        var client = new WallhavenClient(http, throttle: false);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.SearchAsync("rate", "relevance", "", 1, default));
        Assert.Contains("Wait a minute", error.Message);
        await Assert.ThrowsAsync<InvalidDataException>(() => client.GetWallpaperAsync("../bad", default));
        await Assert.ThrowsAsync<InvalidDataException>(() => client.GetThumbnailAsync(new Uri("https://example.com/preview.png"), default));
        Assert.Single(handler.Requests);
    }
}

internal sealed class MarketplaceHandler : HttpMessageHandler
{
    private readonly byte[] _image;
    private readonly string _contentType;
    public ConcurrentQueue<string> Requests { get; } = new();
    public CancellationTokenSource? CancelTransfer { get; init; }
    public MarketplaceHandler(byte[] image, string contentType = "image/png") { _image = image; _contentType = contentType; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        Requests.Enqueue(uri.AbsoluteUri);
        if (uri.Query.Contains("q=slow")) await Task.Delay(Timeout.Infinite, cancellationToken);
        if (uri.Query.Contains("q=rate")) return new(HttpStatusCode.TooManyRequests);
        if (uri.AbsolutePath.Contains("/api/"))
        {
            object result = uri.AbsolutePath.Contains("/search")
                ? new { data = uri.Query.Contains("q=orientations")
                        ? new[] { Wallpaper("aaa111"), Wallpaper("bbb222", "3440x1440"), Wallpaper("ccc333", "1600x1200"), Wallpaper("ddd444", "1080x1920"), Wallpaper("eee555", "1440x1440") }
                        : uri.Query.Contains("page=2") ? new[] { Wallpaper("aaa111"), Wallpaper("bbb222") } : new[] { Wallpaper("aaa111") },
                    meta = new { current_page = uri.Query.Contains("page=2") ? 2 : 1, last_page = 2, total = 2 } }
                : new { data = Wallpaper(uri.Segments.Last()) };
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(result)) };
        }
        HttpContent content = CancelTransfer != null && uri.Host == "w.wallhaven.cc"
            ? new StreamContent(new CancelingStream(_image, CancelTransfer)) : new ByteArrayContent(_image);
        content.Headers.ContentType = new(_contentType);
        return new(HttpStatusCode.OK) { Content = content };
    }

    private object Wallpaper(string id, string resolution = "2560x1440") => new
    {
        id, purity = "sfw", resolution, category = "general", file_size = _image.Length,
        file_type = "image/png", path = $"https://w.wallhaven.cc/full/aa/wallhaven-{id}.png",
        thumbs = new { large = $"https://th.wallhaven.cc/lg/aa/{id}.jpg" },
        tags = new[] { new { id = 42, name = "nature" } }, uploader = new { username = "Forest artist" }
    };

    public static byte[] CreateImage()
    {
        var image = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgr24, null, new byte[12], 6);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private sealed class CancelingStream : MemoryStream
    {
        private readonly CancellationTokenSource _cancellation;
        public CancelingStream(byte[] bytes, CancellationTokenSource cancellation) : base(bytes) => _cancellation = cancellation;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position >= 8) _cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return base.ReadAsync(buffer, cancellationToken);
        }
    }
}
