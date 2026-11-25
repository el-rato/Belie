using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using WallpaperProfiles.Infrastructure;

namespace WallpaperProfiles.Engine;

internal sealed record WeatherDay(DateOnly Date, int Code, double High, double Low);
internal sealed record WeatherReport(string Place, double Temperature, double FeelsLike, int Humidity, double Wind, int Code, bool IsDay,
    bool Fahrenheit, IReadOnlyList<WeatherDay> Days)
{
    public string Degrees(double value) => $"{Math.Round(value).ToString(CultureInfo.CurrentCulture)}°";
    public string WindText => $"{Math.Round(Wind)} {(Fahrenheit ? "mph" : "km/h")}";
}

/// <summary>Current weather from open-meteo.com. Only the place the user typed is sent, and only when a weather widget is shown.</summary>
internal static class WeatherSource
{
    private static readonly TimeSpan Fresh = TimeSpan.FromMinutes(30), RetryAfter = TimeSpan.FromMinutes(2);
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(12) };
    private static readonly ConcurrentDictionary<(string Place, bool Fahrenheit), Entry> Cache = new();

    private sealed class Entry
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public WeatherReport? Report;
        public DateTime Asked;
        public bool Failed;
    }

    /// <summary>Null when the place is unknown or there is no connection. The last report is kept while offline.</summary>
    public static async Task<WeatherReport?> GetAsync(string place, bool fahrenheit)
    {
        place = place.Trim();
        if (place.Length == 0) return null;
        var entry = Cache.GetOrAdd((place.ToLowerInvariant(), fahrenheit), _ => new Entry());
        await entry.Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (DateTime.UtcNow - entry.Asked < (entry.Failed ? RetryAfter : Fresh)) return entry.Report;
            entry.Asked = DateTime.UtcNow;
            try
            {
                using var found = await Read("https://geocoding-api.open-meteo.com/v1/search?count=1&format=json&name=" + Uri.EscapeDataString(place)).ConfigureAwait(false);
                if (!found.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
                { entry.Failed = true; entry.Report = null; return null; }
                var match = results[0];
                string Number(string name) => match.GetProperty(name).GetDouble().ToString("0.####", CultureInfo.InvariantCulture);
                using var forecast = await Read($"https://api.open-meteo.com/v1/forecast?latitude={Number("latitude")}&longitude={Number("longitude")}"
                    + "&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,is_day,wind_speed_10m"
                    + "&daily=weather_code,temperature_2m_max,temperature_2m_min&forecast_days=5&timezone=auto"
                    + (fahrenheit ? "&temperature_unit=fahrenheit&wind_speed_unit=mph" : "")).ConfigureAwait(false);
                entry.Report = Parse(match.GetProperty("name").GetString() ?? place, forecast.RootElement, fahrenheit);
                entry.Failed = false;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
            {
                entry.Failed = true;
                Logger.Warn("The weather could not be read: " + ex.Message);
            }
            return entry.Report;
        }
        finally { entry.Gate.Release(); }
    }

    private static async Task<JsonDocument> Read(string address)
    {
        await using var stream = await Client.GetStreamAsync(address).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
    }

    internal static WeatherReport Parse(string place, JsonElement root, bool fahrenheit)
    {
        var now = root.GetProperty("current");
        var daily = root.GetProperty("daily");
        var dates = daily.GetProperty("time");
        var days = new List<WeatherDay>();
        for (var i = 0; i < dates.GetArrayLength(); i++)
            days.Add(new WeatherDay(DateOnly.ParseExact(dates[i].GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                daily.GetProperty("weather_code")[i].GetInt32(), daily.GetProperty("temperature_2m_max")[i].GetDouble(), daily.GetProperty("temperature_2m_min")[i].GetDouble()));
        return new WeatherReport(place, now.GetProperty("temperature_2m").GetDouble(), now.GetProperty("apparent_temperature").GetDouble(),
            (int)Math.Round(now.GetProperty("relative_humidity_2m").GetDouble()), now.GetProperty("wind_speed_10m").GetDouble(),
            now.GetProperty("weather_code").GetInt32(), now.GetProperty("is_day").GetInt32() != 0, fahrenheit, days);
    }

    /// <summary>A symbol for a WMO weather code.</summary>
    public static string Symbol(int code, bool day = true) => code switch
    {
        0 => day ? "☀" : "☾", 1 or 2 => "⛅", 3 => "☁", 45 or 48 => "≋", >= 51 and <= 67 or >= 80 and <= 82 => "☂",
        >= 71 and <= 77 or 85 or 86 => "❄", >= 95 => "⚡", _ => "☁"
    };

    public static string Describe(int code) => code switch
    {
        0 => "Clear", 1 => "Mostly clear", 2 => "Partly cloudy", 3 => "Cloudy", 45 or 48 => "Fog", >= 51 and <= 57 => "Drizzle",
        >= 61 and <= 67 => "Rain", >= 71 and <= 77 => "Snow", >= 80 and <= 82 => "Showers", 85 or 86 => "Snow showers", >= 95 => "Thunderstorm", _ => "Cloudy"
    };
}
