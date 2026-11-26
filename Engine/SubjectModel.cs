using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using WallpaperProfiles.Infrastructure;

namespace WallpaperProfiles.Engine;

/// <summary>
/// Cuts a wallpaper's subject out with BiRefNet (general, lite). The model is an optional download, and each
/// picture is cut out once in the background, because one run takes seconds. Transitions only read the saved cut-out.
/// </summary>
internal static class SubjectModel
{
    /// <summary>The cut-out's side, in samples; it covers the whole picture, squeezed to a square.</summary>
    public const int MapSize = 256;
    private const int InputSize = 1024;
    internal const string ModelUrl = "https://github.com/danielgatis/rembg/releases/download/v0.0.0/BiRefNet-general-bb_swin_v1_tiny-epoch_232.onnx";
    internal const long ModelBytes = 224_005_088;
    private const string ModelMd5 = "4fab47adc4ff364be1713e97b7e66334";

    private static readonly ConcurrentQueue<string> Waiting = new();
    private static readonly ConcurrentDictionary<string, byte> Queued = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();
    private static Thread? _worker;
    private static InferenceSession? _session;
    private static bool _onGpu;

    public static string ModelPath => Path.Combine(AppPaths.BaseDir, "models", "birefnet-general-lite.onnx");
    private static string CutoutDir => Path.Combine(AppPaths.CacheDir, "cutouts");
    public static bool IsInstalled => File.Exists(ModelPath);
    private static string CpuOnlyFile => ModelPath + ".processor";
    internal static string Device => _session == null ? "none" : _onGpu ? "graphics card" : "processor";

    /// <summary>Raised on a worker thread after each picture is cut out, with how many are still waiting.</summary>
    public static event Action<int>? Progress;
    public static int Pending => Waiting.Count;

    public static async Task DownloadAsync(IProgress<double> progress, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ModelPath)!);
        var part = ModelPath + ".part";
        using (var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan })
        {
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Belie (subject model)");
            using var response = await http.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } length && length != ModelBytes)
                throw new InvalidDataException("The model on the server is not the one Belie expects.");
            await using var source = await response.Content.ReadAsStreamAsync(token);
            await using var target = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true);
            using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
            var buffer = new byte[1 << 16];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, token)) > 0)
            {
                if ((done += read) > ModelBytes) throw new InvalidDataException("The model download is larger than expected.");
                md5.AppendData(buffer, 0, read);
                await target.WriteAsync(buffer.AsMemory(0, read), token);
                progress.Report(done / (double)ModelBytes);
            }
            if (done != ModelBytes || Convert.ToHexString(md5.GetHashAndReset()).ToLowerInvariant() != ModelMd5)
            {
                target.Close();
                File.Delete(part);
                throw new InvalidDataException("The model download was damaged. Try again.");
            }
        }
        File.Move(part, ModelPath, overwrite: true);
        Logger.Info("The subject model was downloaded.");
    }

    public static void Remove()
    {
        lock (Gate) { _session?.Dispose(); _session = null; }
        Waiting.Clear(); Queued.Clear();
        File.Delete(ModelPath);
        File.Delete(CpuOnlyFile);
        try { if (Directory.Exists(CutoutDir)) Directory.Delete(CutoutDir, true); } catch (IOException) { }
    }

    /// <summary>The saved cut-out of a picture: 0 is background, 255 is subject. Null when there is none yet.</summary>
    public static byte[]? Cached(string path)
    {
        try
        {
            var file = CutoutFile(path);
            if (file == null || !File.Exists(file)) return null;
            var map = File.ReadAllBytes(file);
            return map.Length == MapSize * MapSize ? map : null;
        }
        catch (Exception ex) { Logger.Warn("A saved cut-out could not be read: " + ex.Message); return null; }
    }

    /// <summary>Cuts out the pictures that have no cut-out yet, one at a time, in the background.</summary>
    public static void Warm(IEnumerable<string> paths)
    {
        if (!IsInstalled) return;
        foreach (var path in paths)
        {
            if (!WallpaperEngine.IsImageFile(path) || !File.Exists(path) || Cached(path) != null || !Queued.TryAdd(path, 0)) continue;
            Waiting.Enqueue(path);
        }
        lock (Gate)
        {
            if (Waiting.IsEmpty || _worker is { IsAlive: true }) return;
            _worker = new Thread(Work) { IsBackground = true, Name = "SubjectModel", Priority = ThreadPriority.BelowNormal };
            _worker.Start();
        }
    }

    private static void Work()
    {
        while (Waiting.TryDequeue(out var path))
        {
            try
            {
                var file = CutoutFile(path);
                if (file != null && !File.Exists(file))
                {
                    var map = Cut(path);
                    Directory.CreateDirectory(CutoutDir);
                    File.WriteAllBytes(file + ".tmp", map);
                    File.Move(file + ".tmp", file, true);
                }
            }
            catch (Exception ex) { Logger.Warn($"Cutting out '{Path.GetFileName(path)}' failed: {ex.Message}"); }
            finally { Queued.TryRemove(path, out _); }
            Progress?.Invoke(Waiting.Count);
        }
        // The model holds hundreds of megabytes; nothing needs it until the next picture.
        lock (Gate)
        {
            if (!Waiting.IsEmpty) { _worker = new Thread(Work) { IsBackground = true, Name = "SubjectModel", Priority = ThreadPriority.BelowNormal }; _worker.Start(); return; }
            _session?.Dispose(); _session = null;
        }
        MemoryTrim.Soon();
    }

    /// <summary>Runs the model on one picture.</summary>
    internal static byte[] Cut(string path)
    {
        var input = new DenseTensor<float>(new[] { 1, 3, InputSize, InputSize });
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var image = Image.FromStream(stream, false, false))
        using (var square = new Bitmap(InputSize, InputSize, PixelFormat.Format32bppRgb))
        {
            using (var graphics = Graphics.FromImage(square))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(image, new Rectangle(0, 0, InputSize, InputSize));
            }
            var data = square.LockBits(new Rectangle(0, 0, InputSize, InputSize), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            var pixels = new int[InputSize * InputSize];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            square.UnlockBits(data);
            var buffer = input.Buffer.Span;
            const int plane = InputSize * InputSize;
            for (var i = 0; i < plane; i++)
            {
                var pixel = pixels[i];
                buffer[i] = ((pixel >> 16 & 255) / 255f - .485f) / .229f;
                buffer[plane + i] = ((pixel >> 8 & 255) / 255f - .456f) / .224f;
                buffer[2 * plane + i] = ((pixel & 255) / 255f - .406f) / .225f;
            }
        }
        float[] logits;
        lock (Gate)
        {
            try { logits = Run(Session(), input); }
            catch (OnnxRuntimeException) when (_onGpu)
            {
                // Some graphics drivers cannot run every layer; the processor always can.
                Logger.Warn("The subject model failed on the graphics card; using the processor from now on.");
                _session?.Dispose(); _session = null;
                try { File.WriteAllText(CpuOnlyFile, OrtEnv.Instance().GetVersionString()); } catch (IOException) { }
                logits = Run(Session(forceCpu: true), input);
            }
        }
        return Shrink(logits, (int)Math.Round(Math.Sqrt(logits.Length)));
    }

    private static float[] Run(InferenceSession session, DenseTensor<float> input)
    {
        var name = session.InputMetadata.Keys.First();
        using var results = session.Run(new[] { NamedOnnxValue.CreateFromTensor(name, input) });
        return results.First().AsEnumerable<float>().ToArray();
    }

    private static InferenceSession Session(bool forceCpu = false)
    {
        if (_session != null) return _session;
        // A card that ran out of memory once will again; a new ONNX Runtime may do better.
        if (!forceCpu && !(File.Exists(CpuOnlyFile) && File.ReadAllText(CpuOnlyFile) == OrtEnv.Instance().GetVersionString()))
            try
            {
                var gpu = new SessionOptions { EnableMemoryPattern = false, ExecutionMode = ExecutionMode.ORT_SEQUENTIAL };
                gpu.AppendExecutionProvider_DML(GraphicsCards.Roomiest());
                _session = new InferenceSession(ModelPath, gpu);
                _onGpu = true;
                return _session;
            }
            catch (Exception ex) { Logger.Warn("The graphics card cannot run the subject model: " + ex.Message); }
        // Without the arena, memory goes back after each run instead of staying reserved for the next.
        var cpu = new SessionOptions { IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2), EnableCpuMemArena = false };
        _session = new InferenceSession(ModelPath, cpu);
        _onGpu = false;
        return _session;
    }

    /// <summary>Turns the model's scores into a small map, stretched to 0–255 as rembg does.</summary>
    internal static byte[] Shrink(float[] logits, int side)
    {
        var chance = logits.Select(value => 1 / (1 + MathF.Exp(-value))).ToArray();
        float least = chance.Min(), most = Math.Max(least + 1e-6f, chance.Max());
        var map = new byte[MapSize * MapSize];
        for (var y = 0; y < MapSize; y++)
            for (var x = 0; x < MapSize; x++)
            {
                int left = x * side / MapSize, right = Math.Max(left + 1, (x + 1) * side / MapSize);
                int top = y * side / MapSize, bottom = Math.Max(top + 1, (y + 1) * side / MapSize);
                float sum = 0; var count = 0;
                for (var sy = top; sy < bottom; sy++)
                    for (var sx = left; sx < right; sx++) { sum += chance[sy * side + sx]; count++; }
                map[y * MapSize + x] = (byte)Math.Round(255 * ((sum / count) - least) / (most - least));
            }
        return map;
    }

    /// <summary>Changes when the picture does, so an edited file is cut out again.</summary>
    private static string? CutoutFile(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) return null;
        var key = $"{info.FullName.ToLowerInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        return Path.Combine(CutoutDir, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32] + ".mask");
    }
}

/// <summary>
/// Picks the graphics card for the model. DirectML's first card is Windows' default one, which on a laptop is often
/// the built-in chip with little memory, while the separate card sits idle.
/// </summary>
internal static class GraphicsCards
{
    /// <summary>The DXGI index of the hardware card with the most memory of its own; 0 when that cannot be told.</summary>
    public static int Roomiest()
    {
        try
        {
            var id = typeof(IDXGIFactory1).GUID;
            if (CreateDXGIFactory1(ref id, out var created) != 0) return 0;
            var factory = (IDXGIFactory1)created;
            try
            {
                var best = 0; ulong most = 0;
                for (uint index = 0; factory.EnumAdapters1(index, out var adapter) == 0; index++)
                {
                    try
                    {
                        if (adapter.GetDesc1(out var description) != 0 || (description.Flags & 2) != 0) continue; // 2: software
                        var memory = (ulong)description.DedicatedVideoMemory;
                        if (memory > most) { most = memory; best = (int)index; }
                    }
                    finally { Marshal.ReleaseComObject(adapter); }
                }
                return best;
            }
            finally { Marshal.ReleaseComObject(factory); }
        }
        catch (Exception ex) { Logger.Warn("The graphics cards could not be listed: " + ex.Message); return 0; }
    }

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object factory);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubSysId, Revision;
        public nuint DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow; public int LuidHigh;
        public uint Flags;
    }

    [ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIFactory1
    {
        void SetPrivateData(); void SetPrivateDataInterface(); void GetPrivateData(); void GetParent();
        void EnumAdapters(); void MakeWindowAssociation(); void GetWindowAssociation(); void CreateSwapChain(); void CreateSoftwareAdapter();
        [PreserveSig] int EnumAdapters1(uint index, out IDXGIAdapter1 adapter);
        [PreserveSig] bool IsCurrent();
    }

    [ComImport, Guid("29038f61-3839-4626-91fd-086879011a05"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIAdapter1
    {
        void SetPrivateData(); void SetPrivateDataInterface(); void GetPrivateData(); void GetParent();
        void EnumOutputs(); void GetDesc(); void CheckInterfaceSupport();
        [PreserveSig] int GetDesc1(out AdapterDescription description);
    }
}
