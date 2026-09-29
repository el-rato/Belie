using System.Text.Json;
using System.Text.Json.Serialization;

namespace WallpaperProfiles.Persistence;

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Shared = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };
}
