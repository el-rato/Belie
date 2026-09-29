namespace WallpaperProfiles.Models;

public enum FitMode
{
    Fill,
    Fit,
    Stretch,
    Tile,
    Center,
    Span,
}

public static class FitModeRegistry
{
    public static (string Style, string Tile) ToRegistryValues(this FitMode mode) => mode switch
    {
        FitMode.Fill => ("10", "0"),
        FitMode.Fit => ("6", "0"),
        FitMode.Stretch => ("2", "0"),
        FitMode.Tile => ("0", "1"),
        FitMode.Center => ("0", "0"),
        FitMode.Span => ("22", "0"),
        _ => ("10", "0"),
    };
}
