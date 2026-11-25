namespace WallpaperProfiles.Workspaces;

/// <summary>Easing used by the bar and workspace animations. Progress in and out is 0 to 1, with overshoot allowed.</summary>
internal static class Motion
{
    // Durations in milliseconds at normal speed.
    public const double SlideTime = 500, OpenTime = 440, MoveTime = 440, FadeTime = 210;

    /// <summary>Quick departure, long settle: workspaces sliding past.</summary>
    public static double Slide(double progress) => Bezier(progress, .32, .72, 0, 1);
    /// <summary>Slight overshoot: a window popping in.</summary>
    public static double Open(double progress) => Bezier(progress, .16, 1.3, .3, 1);
    public static double Fade(double progress) => Bezier(progress, .23, 1, .32, 1);
    /// <summary>A lightly damped spring: tiles making room for each other.</summary>
    public static double Move(double progress) => progress >= 1 ? 1 : Spring(progress * MoveTime / 1000, 130, 23);

    public static double Bezier(double x, double x1, double y1, double x2, double y2)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        // The curve gives x and y for a parameter; find the parameter that lands on this x.
        double low = 0, high = 1, t = x;
        for (var i = 0; i < 32; i++)
        {
            t = (low + high) / 2;
            if (Point(t, x1, x2) < x) low = t; else high = t;
        }
        return Point(t, y1, y2);
    }

    private static double Point(double t, double first, double second)
        => 3 * (1 - t) * (1 - t) * t * first + 3 * (1 - t) * t * t * second + t * t * t;

    /// <summary>Position of a unit-mass spring released from 0 toward 1, after a number of seconds.</summary>
    public static double Spring(double seconds, double stiffness, double damping)
    {
        var decay = damping / 2;
        var frequency = stiffness - decay * decay;
        if (frequency > 1e-6)
        {
            var speed = Math.Sqrt(frequency);
            return 1 - Math.Exp(-decay * seconds) * (Math.Cos(speed * seconds) + decay / speed * Math.Sin(speed * seconds));
        }
        var spread = Math.Sqrt(Math.Max(0, -frequency));
        if (spread < 1e-3) return 1 - Math.Exp(-decay * seconds) * (1 + decay * seconds);
        double fast = -decay - spread, slow = -decay + spread;
        return 1 + (fast * Math.Exp(slow * seconds) - slow * Math.Exp(fast * seconds)) / (slow - fast);
    }
}
