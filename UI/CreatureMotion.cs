using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WallpaperProfiles.Engine;
using Image = System.Windows.Controls.Image;
using Point = System.Windows.Point;

namespace WallpaperProfiles.UI;

/// <summary>
/// How a creature gets about. A pack can name one with "motion"; otherwise its types choose. Hang is for bats: it
/// hangs upside down from a ceiling, swinging, and now and then swoops down and back up.
/// </summary>
internal enum MotionStyle { Waddle, Hop, Dash, Float, Fly, Glide, Stomp, Hang }

internal static class CreatureStyles
{
    public static MotionStyle For(Creature creature)
        => creature.Motion is { } named && Enum.TryParse<MotionStyle>(named, true, out var chosen) ? chosen : FromTypes(creature.Types);

    /// <summary>Ghosts float, dragons glide, birds fly, electric ones dart, heavy ones stomp, small ones hop.</summary>
    public static MotionStyle FromTypes(IReadOnlyCollection<string> types)
    {
        bool Has(params string[] names) => types.Any(type => names.Contains(type, StringComparer.OrdinalIgnoreCase));
        if (Has("Ghost", "Psychic", "Shade")) return MotionStyle.Float;
        if (Has("Dragon")) return MotionStyle.Glide;
        if (Has("Flying", "Air")) return MotionStyle.Fly;
        if (Has("Electric", "Spark")) return MotionStyle.Dash;
        if (Has("Rock", "Ground", "Steel", "Fighting", "Stone")) return MotionStyle.Stomp;
        if (Has("Normal", "Fairy", "Bug", "Ice")) return MotionStyle.Hop;
        return MotionStyle.Waddle;
    }
}

/// <summary>How the walker looks this frame: where its feet are, how high it is, and how its sprite is squashed, tilted and turned.</summary>
/// <param name="Hanging">Upside down from a ceiling at <paramref name="Y"/>; <paramref name="Lift"/> is then how far it has dropped below it.</param>
internal readonly record struct WalkerPose(double X, double Y, int Frame, double ScaleX, double ScaleY, double Lift, double Angle = 0, double Opacity = 1, bool Hanging = false);

/// <summary>
/// The walk itself, in pixels and seconds so it looks the same at any frame rate. Each style moves its own way: a
/// waddle that rocks from foot to foot, little hops, darting runs, a ghostly float that sometimes vanishes and
/// reappears, flying with flapping and swoops (landing to rest), a dragon's wavy glide, and heavy stomping steps.
/// Kept apart from the window so it can be tested.
/// </summary>
internal sealed class WalkerMotion
{
    /// <summary>The waddle's speed, stride and bob; the other styles keep their own numbers below.</summary>
    public const double Speed = 44, Stride = 7, Bob = 2.2;
    public const double Gravity = 1800, TurnTime = .16, LandTime = .2;
    public const double HopTime = .34, HopHeight = 11, Cruise = 30;

    private readonly Random _random;
    private readonly int _frames;
    private double _distance, _time, _turn, _land, _vy, _rest, _walkFor;
    private double _hop = -1, _gap, _burst = .6, _altitude, _fade = 1, _vanishIn, _swoop = -1, _swoopIn;
    public const double SwoopTime = 1.5, SwoopDepth = 46;
    private int _direction = 1, _fading;
    private bool _placed, _settle;

    public WalkerMotion(int frames, Random? random = null, MotionStyle style = MotionStyle.Waddle)
    {
        _frames = Math.Max(1, frames);
        _random = random ?? new Random();
        Style = style;
        _walkFor = 3 + _random.NextDouble() * 6;
        _vanishIn = 8 + _random.NextDouble() * 10;
        _swoopIn = 6 + _random.NextDouble() * 8;
        if (style == MotionStyle.Glide) _altitude = 22;
    }

    public MotionStyle Style { get; }
    public double X { get; private set; }
    public double Y { get; private set; }
    public int Direction => _direction;
    public bool Resting => _rest > 0;
    public bool Airborne => _vy != 0;
    private bool Hovers => Style is MotionStyle.Float or MotionStyle.Fly or MotionStyle.Glide or MotionStyle.Hang;
    public bool Swooping => _swoop >= 0;

    private double SpeedOf() => Style switch
    {
        MotionStyle.Hop => 62, MotionStyle.Dash => 128, MotionStyle.Float => 24, MotionStyle.Fly => 66,
        MotionStyle.Glide => 34, MotionStyle.Stomp => 30, MotionStyle.Hang => _swoop >= 0 ? 130 : 20, _ => Speed
    };

    private double StrideOf() => Style switch { MotionStyle.Dash => 10, MotionStyle.Stomp => 13, _ => Stride };

    /// <summary>Moves the walker on by <paramref name="seconds"/> along a ground from left to right at height top.</summary>
    /// <param name="scale">Device pixels per unit, so the walk keeps its size on high-DPI screens.</param>
    public WalkerPose Advance(double seconds, double left, double right, double top, double scale)
    {
        seconds = Math.Clamp(seconds, 0, .05); // After a stall, carry on instead of leaping.
        right = Math.Max(left, right);
        if (!_placed) { X = (left + right) / 2; Y = top; _placed = true; }
        _time += seconds;
        _turn = Math.Max(0, _turn - seconds / TurnTime);
        _land = Math.Max(0, _land - seconds / LandTime);

        if (Hovers)
        {
            // Floaters and fliers drift over to new ground instead of hopping.
            Y += (top - Y) * Math.Min(1, seconds * 5);
            if (Math.Abs(top - Y) < .5) Y = top;
        }
        else if (_vy == 0 && Y > top + 2) _vy = -Math.Sqrt(2 * Gravity * scale * (Y - top + 22 * scale)); // Higher ground: hop up.
        else if (_vy == 0 && Y < top - .5) _vy = 1; // The ground fell away: drop.

        var moving = false;
        double lift = 0, angle = 0, stretch = 0;
        if (_vy != 0)
        {
            _vy += Gravity * scale * seconds;
            Y += _vy * seconds;
            X = Math.Clamp(X + _direction * SpeedOf() * .6 * scale * seconds, left, right);
            if (_vy > 0 && Y >= top) { Y = top; _vy = 0; _land = 1; _hop = -1; }
            stretch = Math.Min(.12, Math.Abs(_vy) / (scale * 4000));
        }
        else if (_fading != 0)
        {
            // A ghost slips out of sight and comes back somewhere else.
            _fade += _fading * seconds / .45;
            if (_fade <= 0) { _fade = 0; _fading = 1; X = left + _random.NextDouble() * (right - left); if (_random.NextDouble() < .5) Turn(); }
            else if (_fade >= 1) { _fade = 1; _fading = 0; _vanishIn = 9 + _random.NextDouble() * 12; }
        }
        else if (_rest > 0)
        {
            _rest -= seconds;
            // Now and then it looks the other way while it waits.
            if (_rest > .5 && _random.NextDouble() < seconds * .35) Turn();
        }
        else
        {
            moving = Move(seconds, left, right, scale);
        }

        // Style by style: how high it is, how it tilts, and how it squashes.
        var phase = _distance / StrideOf();
        switch (Style)
        {
            case MotionStyle.Waddle:
                if (moving) { lift = Math.Abs(Math.Sin(phase * Math.PI)) * Bob; angle = Math.Sin(phase * Math.PI) * 6; }
                break;
            case MotionStyle.Stomp:
                if (moving) { lift = Math.Abs(Math.Sin(phase * Math.PI)) * 4; angle = Math.Sin(phase * Math.PI) * 3; }
                break;
            case MotionStyle.Dash:
                if (moving) { lift = Math.Abs(Math.Sin(phase * Math.PI)) * 2.5; angle = _direction * 9; }
                break;
            case MotionStyle.Hop:
                if (_hop >= 0) { lift = HopHeight * 4 * _hop * (1 - _hop); stretch = .1 * Math.Abs(1 - 2 * _hop); angle = _direction * 5 * (1 - 2 * _hop); }
                break;
            case MotionStyle.Float:
                lift = 14 + Math.Sin(_time * Math.PI * 2 / 2.4) * 5;
                angle = Math.Sin(_time * Math.PI * 2 / 3.2) * 6;
                break;
            case MotionStyle.Fly:
            {
                var target = _rest > 0 && _settle ? 0 : Cruise;
                var before = _altitude;
                _altitude = target > _altitude ? Math.Min(target, _altitude + 40 * seconds) : Math.Max(target, _altitude - 34 * seconds);
                if (before > 0 && _altitude == 0) _land = 1;
                var up = _altitude / Cruise;
                lift = _altitude + up * (Math.Sin(_time * Math.PI * 2 * 3) * 2.5 + Math.Sin(_time * Math.PI * 2 / 4.5) * 9);
                angle = moving ? _direction * 7 * up : Math.Sin(_time * Math.PI * 2 * 3) * 2 * up;
                break;
            }
            case MotionStyle.Hang:
                if (_swoop >= 0) { lift = Math.Sin(_swoop * Math.PI) * SwoopDepth; angle = _direction * 18 * Math.Sin(_swoop * Math.PI); }
                else
                {
                    // Swings from its feet like a pendulum, harder just after it moved.
                    lift = 0;
                    angle = Math.Sin(_time * Math.PI * 2 / 2.4) * (moving ? 9 : 6);
                }
                break;
            case MotionStyle.Glide:
            {
                var wave = _distance / 40 * Math.PI * 2;
                lift = _altitude + Math.Sin(wave) * 9 + Math.Sin(_time * Math.PI * 2 / 2.6) * 2;
                // Nose up while it climbs, down as it dives.
                angle = Math.Clamp(-_direction * Math.Atan(Math.Cos(wave) * .7) * 180 / Math.PI, -18, 18);
                break;
            }
        }

        // Steps follow the ground covered, so the feet never slide; everything else keeps its own beat.
        var flying = Style == MotionStyle.Fly && _altitude > 2 || _swoop >= 0;
        var frame = moving && Style is MotionStyle.Waddle or MotionStyle.Stomp or MotionStyle.Dash ? (int)phase % _frames
            : _hop >= 0 || _vy != 0 ? Math.Min(1, _frames - 1)
            : (int)(_time * (flying ? 7 : Style == MotionStyle.Dash ? 4 : 2.2)) % _frames;

        double scaleX = 1, scaleY = 1;
        if (stretch > 0) { scaleY += stretch; scaleX -= stretch * .6; }
        else if (_land > 0) { var squash = Math.Sin(_land * Math.PI / 2) * (Style == MotionStyle.Stomp ? .2 : .16); scaleY -= squash; scaleX += squash * .7; }
        else if (!moving && !Hovers) { var breath = Math.Sin(_time * Math.PI * 2 / 1.8) * .022; scaleY += breath; scaleX -= breath * .5; }
        var opacity = Style == MotionStyle.Float ? (.84 + .1 * Math.Sin(_time * Math.PI * 2 / 2.2)) * _fade : 1;

        // A turn narrows the sprite to its edge and opens it facing the other way.
        var facing = _turn > 0 ? _direction * Math.Abs(1 - 2 * _turn) : _direction;
        return new WalkerPose(X, Y, frame, scaleX * facing, scaleY, lift, angle, opacity, Style == MotionStyle.Hang);
    }

    /// <summary>Goes on along the ground in the style's own gait; false when it did not move this frame.</summary>
    private bool Move(double seconds, double left, double right, double scale)
    {
        var speed = SpeedOf();
        switch (Style)
        {
            case MotionStyle.Hop:
                // Only moves while in the air: hop, land, a short beat, hop again.
                if (_hop < 0)
                {
                    _gap -= seconds;
                    if (_gap > 0) return false;
                    _hop = 0;
                }
                _hop += seconds / HopTime;
                if (_hop >= 1) { _hop = -1; _gap = .08 + _random.NextDouble() * .22; _land = 1; }
                break;
            case MotionStyle.Dash:
                // Quick bursts with short stops between.
                _burst -= seconds;
                if (_burst <= 0) { _burst = .35 + _random.NextDouble() * .8; Pause(.2 + _random.NextDouble() * .7, false); if (_random.NextDouble() < .25) Turn(); return false; }
                break;
            case MotionStyle.Stomp:
            {
                // Heavy: each step pushes, then plants with a thud.
                var phase = _distance / StrideOf();
                speed *= .25 + 1.5 * Math.Pow(Math.Sin(phase * Math.PI), 2);
                var step = (int)phase;
                if ((int)((_distance + speed * seconds) / StrideOf()) != step) _land = .7;
                break;
            }
            case MotionStyle.Float:
                _vanishIn -= seconds;
                if (_vanishIn <= 0 && right - left > 200) { _fading = -1; return false; }
                break;
            case MotionStyle.Hang:
                // Creeps along the ceiling, and now and then lets go to swoop down and back up.
                if (_swoop >= 0)
                {
                    _swoop += seconds / SwoopTime;
                    if (_swoop >= 1) { _swoop = -1; _swoopIn = 7 + _random.NextDouble() * 10; Pause(.6 + _random.NextDouble(), true); return false; }
                }
                else if ((_swoopIn -= seconds) <= 0 && right - left > 300) _swoop = 0;
                break;
            case MotionStyle.Fly:
                // Takes off before it goes anywhere.
                speed *= Math.Clamp(_altitude / (Cruise / 2), 0, 1);
                break;
        }

        var stepSize = speed * scale * seconds;
        X += _direction * stepSize; _distance += stepSize / scale;
        _walkFor -= seconds;
        if (X <= left || X >= right)
        {
            X = Math.Clamp(X, left, right);
            // Mid-hop it just turns in the air; otherwise it stops at the edge first.
            if (_hop < 0) Pause(.3 + _random.NextDouble() * .6, false);
            Turn();
        }
        else if (_walkFor <= 0 && _hop < 0 && _swoop < 0)
        {
            Pause(1.2 + _random.NextDouble() * 3, true);
            _walkFor = 3 + _random.NextDouble() * 8;
            if (_random.NextDouble() < .5) Turn();
        }
        return stepSize > 0;
    }

    /// <summary>A pose for a sprite standing in a list or on the bar: no walking, just its own way of idling.</summary>
    public static WalkerPose Idle(MotionStyle style, double time, int frames)
    {
        frames = Math.Max(1, frames);
        double lift = 0, angle = 0, scaleY = 1, opacity = 1, rate = 2.2;
        double Wave(double period) => Math.Sin(time * Math.PI * 2 / period);
        switch (style)
        {
            case MotionStyle.Waddle: angle = Wave(1.4) * 5; lift = Math.Abs(Wave(1.4)) * 1.5; break;
            case MotionStyle.Stomp: scaleY = 1 + Wave(2.2) * .05; break;
            case MotionStyle.Hop:
            {
                var t = time % 2.4 / .36; // A small hop every few seconds.
                if (t < 1) { lift = 7 * 4 * t * (1 - t); scaleY = 1 + .08 * Math.Abs(1 - 2 * t); }
                else if (t < 1.5) scaleY = 1 - .1 * Math.Sin((t - 1) * 2 * Math.PI);
                break;
            }
            case MotionStyle.Dash:
            {
                var t = time % 1.8 / .18; // A twitchy double hop.
                if (t < 2) lift = 3.5 * Math.Abs(Math.Sin(t * Math.PI));
                rate = 4;
                break;
            }
            case MotionStyle.Float: lift = 4 + Wave(2.4) * 3; angle = Wave(3.2) * 5; opacity = .86 + .1 * Wave(2.2); break;
            case MotionStyle.Fly: lift = 4 + Wave(1 / 3.0) * 2 + Wave(3) * 2; angle = Wave(3) * 3; rate = 6; break;
            case MotionStyle.Glide: lift = 3 + Wave(2.6) * 3; angle = Math.Cos(time * Math.PI * 2 / 2.6) * 6; break;
            case MotionStyle.Hang: angle = Wave(2.4) * 6; break;
        }
        if (style is not (MotionStyle.Stomp or MotionStyle.Hop)) scaleY += Wave(1.8) * .015;
        return new WalkerPose(0, 0, (int)(time * rate) % frames, 1, scaleY, lift, angle, opacity);
    }

    /// <summary>Stops where it is for a moment, as while breathing fire; fliers stay in the air.</summary>
    public void Hold(double seconds)
    {
        if (_fading != 0 || _vy != 0 || _swoop >= 0 || _rest >= seconds) return;
        Pause(seconds, false);
    }

    private void Turn() { _direction = -_direction; _turn = 1; }
    private void Pause(double seconds, bool settle) { _rest = seconds; _settle = settle; }
}

/// <summary>Plays a creature's frames and idle motion on an image, only while the image is on screen.</summary>
internal static class CreatureSprite
{
    private static readonly ConditionalWeakTable<Image, Player> Players = new();

    /// <summary>Loads a creature's frames; empty when they cannot be read.</summary>
    public static BitmapSource[] Frames(Creature creature)
    {
        try
        {
            return creature.Frames.Select(frame =>
            {
                var image = new BitmapImage();
                image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(CreaturePack.FramePath(frame)); image.EndInit();
                image.Freeze();
                return (BitmapSource)image;
            }).ToArray();
        }
        catch { return Array.Empty<BitmapSource>(); }
    }

    /// <param name="lively">False keeps to swapping frames, slowly, for places that should stay calm, like the bar.</param>
    public static void Animate(Image image, Creature? creature, bool lively = true)
    {
        if (Players.TryGetValue(image, out var old)) { old.Stop(); Players.Remove(image); }
        var frames = creature == null ? Array.Empty<BitmapSource>() : Frames(creature);
        image.Source = frames.FirstOrDefault();
        if (creature == null || frames.Length == 0) return;
        Players.Add(image, new Player(image, frames, CreatureStyles.For(creature), lively, creature.Id));
    }

    private sealed class Player
    {
        private readonly Image _image;
        private readonly BitmapSource[] _frames;
        private readonly MotionStyle _style;
        private readonly bool _lively;
        private readonly double _offset;
        private readonly DispatcherTimer _timer;
        private readonly ScaleTransform _shape = new();
        private readonly RotateTransform _tilt = new();
        private readonly TranslateTransform _lift = new();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private int _shown;

        public Player(Image image, BitmapSource[] frames, MotionStyle style, bool lively, string id)
        {
            _image = image; _frames = frames; _style = style; _lively = lively;
            // Each creature starts at its own point, so a row of them does not move in step.
            _offset = (uint)id.Aggregate(17, (hash, letter) => hash * 31 + letter) % 1000 / 100.0;
            _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(lively ? 40 : 330) };
            _timer.Tick += Tick;
            if (lively)
            {
                image.RenderTransformOrigin = new Point(.5, 1);
                image.RenderTransform = new TransformGroup { Children = { _shape, _tilt, _lift } };
            }
            image.IsVisibleChanged += Visible;
            if (image.IsVisible) _timer.Start();
        }

        private void Visible(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_image.IsVisible) _timer.Start(); else _timer.Stop();
        }

        private void Tick(object? sender, EventArgs e)
        {
            var pose = WalkerMotion.Idle(_style, _clock.Elapsed.TotalSeconds + _offset, _frames.Length);
            var frame = _lively ? pose.Frame : (int)((_clock.Elapsed.TotalSeconds + _offset) * 3) % _frames.Length;
            if (frame != _shown) { _shown = frame; _image.Source = _frames[frame]; }
            if (!_lively) return;
            _shape.ScaleY = pose.ScaleY; _shape.ScaleX = 2 - pose.ScaleY;
            _tilt.Angle = pose.Angle;
            _lift.Y = -Math.Round(pose.Lift * 2) / 2;
            _image.Opacity = pose.Opacity;
        }

        public void Stop()
        {
            _timer.Stop();
            _image.IsVisibleChanged -= Visible;
            _image.RenderTransform = Transform.Identity;
            _image.Opacity = 1;
        }
    }
}
