using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using WallpaperProfiles.Engine;
using Xunit;
using Xunit.Abstractions;

namespace WallpaperProfiles.Tests;

/// <summary>Downloads the real model and cuts out real pictures. Does nothing unless BELIE_MODEL_TESTS names a folder of pictures.</summary>
public class SubjectModelLiveTests
{
    private readonly ITestOutputHelper _output;
    public SubjectModelLiveTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task CutsOutRealPictures()
    {
        var folder = Environment.GetEnvironmentVariable("BELIE_MODEL_TESTS");
        if (string.IsNullOrEmpty(folder)) return;
        if (!SubjectModel.IsInstalled)
        {
            var clock = Stopwatch.StartNew();
            await SubjectModel.DownloadAsync(new Progress<double>(), CancellationToken.None);
            _output.WriteLine($"Downloaded in {clock.Elapsed.TotalSeconds:0} s");
        }
        _output.WriteLine("Card index " + GraphicsCards.Roomiest());
        var output = Environment.GetEnvironmentVariable("BELIE_MODEL_OUT") ?? Path.GetTempPath();
        foreach (var path in Directory.EnumerateFiles(folder).Where(WallpaperEngine.IsImageFile).Take(6))
        {
            var clock = Stopwatch.StartNew();
            var map = SubjectModel.Cut(path);
            var took = clock.Elapsed.TotalSeconds;
            var mask = SubjectFinder.FromCutout(map, new RectangleF(0, 0, 1, 1), 192, 108);
            _output.WriteLine($"{Path.GetFileName(path)}: {took:0.0} s on {SubjectModel.Device}, subject {(mask == null ? "refused" : $"{mask.Count(inside => inside) / (192.0 * 108):P0} of the screen")}");
            using var picture = new Bitmap(SubjectModel.MapSize, SubjectModel.MapSize, PixelFormat.Format32bppRgb);
            for (var y = 0; y < SubjectModel.MapSize; y++)
                for (var x = 0; x < SubjectModel.MapSize; x++) { var v = map[y * SubjectModel.MapSize + x]; picture.SetPixel(x, y, Color.FromArgb(v, v, v)); }
            picture.Save(Path.Combine(output, Path.GetFileNameWithoutExtension(path) + ".mask.png"));
        }
    }
}
