namespace WallpaperProfiles.Models;

/// <summary>How one wallpaper picture gives way to the next.</summary>
public enum WallpaperTransitionStyle
{
    /// <summary>Draws the subject's outline when the picture has a clear one, otherwise spreads in colonies.</summary>
    Automatic,
    Colonies,
    None,
    Grow,
    Shrink,
    Wipe,
    Wave,
    Blinds,
    Spiral,
    Dissolve,
    Fade,
    /// <summary>A different one each time.</summary>
    Random
}

public enum SwitcherLayout { Slanted, Carousel, Filmstrip, Grid }
/// <summary>What is behind the cards.</summary>
public enum SwitcherBackdrop { None, Dimmed, Preview, Blurred }

/// <summary>Everything about how a transition looks.</summary>
internal sealed record TransitionOptions(WallpaperTransitionStyle Style, double Seconds = 1.8, int Cell = 4, bool Glow = true, string GlowColor = "", double GlowLength = .05, double Angle = 30, double Dim = .45)
{
    public static implicit operator TransitionOptions(WallpaperTransitionStyle style) => new(style);
}

public class AppSettings
{
    public string UiTheme { get; set; } = "Sage";

    public WallpaperTransitionStyle WallpaperTransition { get; set; }
    public double TransitionSeconds { get; set; } = 1.8;
    /// <summary>The size of the squares, in pixels.</summary>
    public int TransitionCell { get; set; } = 4;
    public bool TransitionGlow { get; set; } = true;
    /// <summary>Empty takes the color from the picture.</summary>
    public string TransitionGlowColor { get; set; } = "";
    /// <summary>How long each square glows, as a share of the transition.</summary>
    public double TransitionGlowLength { get; set; } = .05;
    /// <summary>The direction wipes and waves travel, in degrees.</summary>
    public double TransitionAngle { get; set; } = 30;
    /// <summary>How much the old wallpaper darkens while the outline is drawn.</summary>
    public double TransitionDim { get; set; } = .45;

    internal TransitionOptions Transition => new(WallpaperTransition, TransitionSeconds, TransitionCell, TransitionGlow, TransitionGlowColor, TransitionGlowLength, TransitionAngle, TransitionDim);

    /// <summary>The wallpaper last picked by hand in each profile, which it opens with.</summary>
    public Dictionary<Guid, string> ChosenWallpapers { get; set; } = new();
    public SwitcherLayout SwitcherLayout { get; set; }
    public SwitcherBackdrop SwitcherBackdrop { get; set; }
    /// <summary>The height of the wallpapers, as a share of the screen's.</summary>
    public double SwitcherSize { get; set; } = .37;
    /// <summary>How far the cards lean, in degrees.</summary>
    public double SwitcherSlant { get; set; } = 18;
    public double SwitcherSpacing { get; set; }
    public double SwitcherRadius { get; set; }
    public double SwitcherOutline { get; set; } = 1.5;
    public string SwitcherOutlineColor { get; set; } = "";
    /// <summary>How many cards are seen on each side.</summary>
    public int SwitcherSides { get; set; } = 2;
    /// <summary>0 at the top of the screen, 1 at the bottom.</summary>
    public double SwitcherPosition { get; set; } = .5;
    public double SwitcherSpeed { get; set; } = 1;
    public bool SwitcherNames { get; set; }
    public bool SwitcherColors { get; set; }
    public bool SwitcherStaysOpen { get; set; }
    /// <summary>Empty turns the keys off.</summary>
    public string SwitcherKeys { get; set; } = "Ctrl+Alt+W";
    public string NextWallpaperKeys { get; set; } = "Ctrl+Alt+PageDown";
    public string PreviousWallpaperKeys { get; set; } = "Ctrl+Alt+PageUp";

    public bool StartWithWindows { get; set; }

    public bool StartMinimizedToTray { get; set; }

    public string LastActiveProfileId { get; set; } = "";

    public string PreferredProfileId { get; set; } = "";
}
