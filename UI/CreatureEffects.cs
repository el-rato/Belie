using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Point = System.Windows.Point;
using Pen = System.Windows.Media.Pen;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;

namespace WallpaperProfiles.UI;

/// <summary>
/// What a creature can do besides walk: dragons and fire types breathe fire, electric ones throw lightning, ghosts
/// throw shadow balls, ground types kick up sand, water types shoot a jet, grass types throw leaves, and fliers blow
/// a gust of wind.
/// </summary>
internal enum CreaturePower { None, Fire, Lightning, Shadow, Sand, Water, Leaves, Gust }

internal struct Particle
{
    public double X, Y, VX, VY, Age, Life, Size, Grow;
    /// <summary>0 fire breath, 1 spark, 2 tail flame, 3 ghostly wisp, 4 sand, 5 water, 6 leaf, 7 wind.</summary>
    public byte Kind;
}

/// <summary>
/// The fire, lightning and tail flames around a walking creature, as small square particles that match its pixel art.
/// Only the numbers live here, in window units; <see cref="CreatureEffectsView"/> draws them.
/// </summary>
internal sealed class CreatureEffects
{
    public const double SprayTime = .8;
    public const double BreathTime = .9, BoltTime = .45, ChargeTime = .4, ShadowTime = 1.1, SandTime = .7;
    /// <summary>The level at which effects reach their full size.</summary>
    public const int FullLevel = 50;
    /// <summary>How far fire and lightning reach at most, for a given strength, so the window can make room.</summary>
    public static double ReachFor(double strength) => 100 + 110 * Math.Clamp(strength, 0, 1);
    public static double StrengthFor(int level) => Math.Clamp((level - 1) / (double)(FullLevel - 1), 0, 1);
    /// <summary>The usual mouth and tail, for sprites that face right, when the pack names neither.</summary>
    public static readonly (double X, double Y) DefaultMouth = (.62, .42), DefaultTail = (.2, .72);

    private readonly Random _random;
    private readonly List<Point> _bolt = new();
    private double _cooldown, _burst, _boltTime, _flashIn, _sparkIn, _tailCarry, _breathCarry;
    private double _shadow, _ballVX, _sand, _sandCarry, _wispIn = 1, _spray, _sprayCarry;

    /// <param name="strength">0 for a new creature up to 1 at <see cref="FullLevel"/>; fire, lightning and flames grow with it.</param>
    public CreatureEffects(CreaturePower power, bool tailFlame, Random? random = null, double strength = 0)
    {
        Power = power; TailFlame = tailFlame;
        Strength = Math.Clamp(strength, 0, 1);
        _random = random ?? new Random();
        _cooldown = 3 + _random.NextDouble() * 4;
        _sparkIn = 1;
    }

    public CreaturePower Power { get; }
    public double Strength { get; }
    public bool TailFlame { get; }
    public List<Particle> Particles { get; } = new();
    public IReadOnlyList<Point> Bolt => _bolt;
    /// <summary>Forks off the bolt, while there is one; stronger creatures throw more.</summary>
    public IReadOnlyList<Point[]> Forks { get; private set; } = Array.Empty<Point[]>();
    /// <summary>How bright the bolt is, from 1 just after a flash down to 0.</summary>
    public double BoltGlow { get; private set; }
    /// <summary>The shadow ball, while one is out: where it is, how big, and how strongly it shows.</summary>
    public Point Ball { get; private set; }
    public double BallRadius { get; private set; }
    /// <summary>How long a whole attack takes, so the creature can wait for it.</summary>
    public double AttackTime => Power switch
    {
        CreaturePower.Fire => BreathTime, CreaturePower.Lightning => BoltTime, CreaturePower.Shadow => ChargeTime + .5, CreaturePower.Sand => SandTime,
        CreaturePower.Water or CreaturePower.Leaves or CreaturePower.Gust => SprayTime, _ => 0
    };
    public bool Attacking => _burst > 0 || _boltTime > 0 || _shadow > 0 || _sand > 0 || _spray > 0;
    public bool Empty => Particles.Count == 0 && BoltGlow <= 0 && !TailFlame;

    public static CreaturePower PowerOf(IReadOnlyCollection<string> types)
    {
        bool Has(params string[] names) => types.Any(type => names.Contains(type, StringComparer.OrdinalIgnoreCase));
        // In order: the first type that has a power gives it, so a fire-and-flying creature breathes fire.
        if (Has("Dragon", "Fire", "Ember")) return CreaturePower.Fire;
        if (Has("Electric", "Spark")) return CreaturePower.Lightning;
        if (Has("Ghost", "Shade")) return CreaturePower.Shadow;
        if (Has("Ground", "Rock", "Stone")) return CreaturePower.Sand;
        if (Has("Water", "Tide", "Ice")) return CreaturePower.Water;
        if (Has("Grass", "Leaf", "Bug")) return CreaturePower.Leaves;
        if (Has("Flying", "Air")) return CreaturePower.Gust;
        return CreaturePower.None;
    }

    public static bool HasTailFlame(IReadOnlyCollection<string> types)
        => types.Any(type => type.Equals("Fire", StringComparison.OrdinalIgnoreCase) || type.Equals("Ember", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Moves everything on by <paramref name="seconds"/>. <paramref name="facing"/> is 1 or -1. Returns true when an attack
    /// starts, so the creature can stop to make it.
    /// </summary>
    public bool Update(double seconds, Point mouth, Point tail, int facing)
    {
        seconds = Math.Clamp(seconds, 0, .05);
        facing = facing < 0 ? -1 : 1;
        for (var i = Particles.Count - 1; i >= 0; i--)
        {
            var p = Particles[i];
            p.Age += seconds;
            if (p.Age >= p.Life) { Particles.RemoveAt(i); continue; }
            switch (p.Kind)
            {
                case 1: p.VX *= 1 - 3 * seconds; p.VY += 60 * seconds; break; // Sparks slow and fall.
                case 3: p.VX *= 1 - 2 * seconds; p.VY -= 18 * seconds; break; // Wisps drift up.
                case 4: p.VX *= 1 - 2.4 * seconds; p.VY += 90 * seconds; break; // Sand settles.
                case 5: p.VY += 260 * seconds; break; // Water arcs down.
                case 6: p.VX *= 1 - 1.4 * seconds; p.VY += 25 * seconds; p.X += Math.Cos(p.Age * 11 + p.Life * 40) * 22 * seconds; break; // Leaves flutter.
                case 7: p.VX *= 1 - .8 * seconds; p.VY = Math.Sin(p.Age * 9 + p.Life * 30) * 14; break; // Wind wavers.
                default: p.VY -= 70 * seconds; break; // Flames rise.
            }
            p.X += p.VX * seconds; p.Y += p.VY * seconds;
            p.Size = Math.Max(1, p.Size + p.Grow * seconds);
            Particles[i] = p;
        }

        var grow = 1 + Strength; // Twice the size at full strength.
        if (TailFlame)
            for (_tailCarry += seconds * 42 * grow; _tailCarry >= 1; _tailCarry--)
                Add(2, tail.X + Spread(1.5 * grow), tail.Y + Spread(1), Spread(12 * grow), (-30 - _random.NextDouble() * 30) * (1 + Strength * .6),
                    .25 + _random.NextDouble() * .2, (3.5 + _random.NextDouble() * 1.5) * grow, -6 * grow);

        var started = false;
        if (Power != CreaturePower.None && !Attacking)
        {
            _cooldown -= seconds;
            if (_cooldown <= 0)
            {
                _cooldown = (6 + _random.NextDouble() * 7) * (1 - Strength * .4);
                switch (Power)
                {
                    case CreaturePower.Fire: _burst = BreathTime; break;
                    case CreaturePower.Lightning: _boltTime = BoltTime; _flashIn = 0; break;
                    case CreaturePower.Shadow: _shadow = ShadowTime; _ballVX = facing * 120 * (1 + Strength * .6); break;
                    case CreaturePower.Sand: _sand = SandTime; break;
                    case CreaturePower.Water or CreaturePower.Leaves or CreaturePower.Gust: _spray = SprayTime; break;
                }
                started = true;
            }
        }

        if (_burst > 0)
        {
            _burst -= seconds;
            for (_breathCarry += seconds * 130 * grow; _breathCarry >= 1; _breathCarry--)
                Add(0, mouth.X, mouth.Y + Spread(2 * grow), facing * (105 + _random.NextDouble() * 70) * (1 + Strength * .8), Spread(34 * grow),
                    .38 + _random.NextDouble() * .27, 3.5 * grow, 22 * grow);
        }

        if (_boltTime > 0)
        {
            _boltTime -= seconds; _flashIn -= seconds;
            if (_flashIn <= 0 && _boltTime > 0)
            {
                // A fresh jagged bolt each flash, so it crackles.
                _flashIn = .07 + _random.NextDouble() * .05;
                var end = new Point(mouth.X + facing * (70 + _random.NextDouble() * 25) * grow, mouth.Y + Spread(16));
                _bolt.Clear();
                // Longer with level, but only a little wilder, so it stays a bolt and not a scribble.
                var wild = 1 + Strength * .4;
                _bolt.AddRange(Zigzag(mouth, end, 9 + (int)(4 * Strength), 2.5 * wild, 9 * wild));
                // Forks off the middle: two to start with, up to four at full strength.
                var forks = new List<Point[]>();
                for (var f = 0; f < 2 + (int)Math.Round(Strength * 2); f++)
                {
                    var from = _bolt[2 + _random.Next(_bolt.Count - 4)];
                    var to = new Point(from.X + facing * (20 + _random.NextDouble() * 18) * wild, from.Y + Spread(22 * wild));
                    forks.Add(Zigzag(from, to, 3, 1, 5 * wild).ToArray());
                }
                Forks = forks;
                BoltGlow = 1;
                for (var i = 0; i < 6 + (int)(8 * Strength); i++) Add(1, end.X, end.Y, Spread(110 * grow), Spread(110 * grow), .2 + _random.NextDouble() * .18, 2.5 * grow, 0);
            }
        }
        BoltGlow = Math.Max(0, BoltGlow - seconds * 3.5);
        if (BoltGlow == 0 && _boltTime <= 0) { _bolt.Clear(); Forks = Array.Empty<Point[]>(); }

        if (_shadow > 0)
        {
            // The ball gathers at the mouth, then flies off wobbling and trailing wisps, and bursts.
            var before = _shadow;
            _shadow -= seconds;
            var into = ShadowTime - _shadow;
            var full = 9 * grow;
            if (into < ChargeTime) { Ball = mouth; BallRadius = full * into / ChargeTime; }
            else
            {
                var flying = into - ChargeTime;
                Ball = new Point(Ball.X + _ballVX * seconds, mouth.Y + Math.Sin(flying * 14) * 3 * grow);
                BallRadius = full * (1 + .08 * Math.Sin(flying * 30));
                if (_random.NextDouble() < seconds * 40) Add(3, Ball.X + Spread(BallRadius * .6), Ball.Y + Spread(BallRadius * .6), -_ballVX * .1 + Spread(10), Spread(10), .45, 3 * grow, -3);
            }
            if (_shadow <= 0 && before > 0)
            {
                for (var i = 0; i < 14 + (int)(14 * Strength); i++)
                    Add(3, Ball.X, Ball.Y, Spread(80 * grow), Spread(80 * grow), .4 + _random.NextDouble() * .3, 3 * grow, -2);
                BallRadius = 0;
            }
        }

        if (_spray > 0)
        {
            _spray -= seconds;
            var power = 1 + Strength * .7;
            switch (Power)
            {
                case CreaturePower.Water:
                    // A jet that arcs and falls, with a few big drops in it.
                    for (_sprayCarry += seconds * 180 * grow; _sprayCarry >= 1; _sprayCarry--)
                        Add(5, mouth.X, mouth.Y + Spread(2.5 * grow), facing * (135 + _random.NextDouble() * 40) * power, -60 - _random.NextDouble() * 30 + Spread(18),
                            .42 + _random.NextDouble() * .14, (_random.NextDouble() < .25 ? 5 : 3.5) * grow, -1.5);
                    break;
                case CreaturePower.Leaves:
                    // A flurry of leaves that tumbles forward and drifts down.
                    for (_sprayCarry += seconds * 60 * grow; _sprayCarry >= 1; _sprayCarry--)
                        Add(6, mouth.X, mouth.Y + Spread(6 * grow), facing * (90 + _random.NextDouble() * 70) * power, Spread(40) - 15,
                            .7 + _random.NextDouble() * .4, (3 + _random.NextDouble() * 2) * grow, 0);
                    break;
                case CreaturePower.Gust:
                    // Streaks of wind that rush forward and fade.
                    for (_sprayCarry += seconds * 55 * grow; _sprayCarry >= 1; _sprayCarry--)
                        Add(7, mouth.X + Spread(4), mouth.Y + Spread(14 * grow), facing * (170 + _random.NextDouble() * 80) * power, 0,
                            .35 + _random.NextDouble() * .25, 2 * grow, 0);
                    break;
            }
        }

        if (_sand > 0)
        {
            // A swipe of sand, thrown forward and up in an arc, that settles as it slows.
            _sand -= seconds;
            for (_sandCarry += seconds * 110 * grow; _sandCarry >= 1; _sandCarry--)
                Add(4, mouth.X + Spread(3), mouth.Y + Spread(4 * grow), facing * (70 + _random.NextDouble() * 90) * (1 + Strength * .6),
                    -40 - _random.NextDouble() * 70, .55 + _random.NextDouble() * .35, (1.5 + _random.NextDouble() * 2) * grow, 0);
        }

        // Ghosts trail a wisp or two even when they are not attacking.
        if (Power == CreaturePower.Shadow && (_wispIn -= seconds) <= 0)
        {
            _wispIn = .25 + _random.NextDouble() * .4;
            Add(3, tail.X + Spread(10), tail.Y + Spread(6), Spread(8), -10 - _random.NextDouble() * 10, .8 + _random.NextDouble() * .4, 2.5 * grow, -1.5);
        }

        // Electric ones crackle a little now and then, even between bolts.
        if (Power == CreaturePower.Lightning && (_sparkIn -= seconds) <= 0)
        {
            _sparkIn = .9 + _random.NextDouble() * 1.8;
            for (var i = 0; i < 3 + (int)(3 * Strength); i++)
                Add(1, mouth.X + Spread(5 * grow), mouth.Y + Spread(4 * grow), Spread(45 * grow), Spread(45 * grow) - 10, .16 + _random.NextDouble() * .12, 2 * grow, 0);
        }
        return started;
    }

    /// <summary>Clears everything at once, as when the creature hides.</summary>
    public void Clear() { Particles.Clear(); _bolt.Clear(); Forks = Array.Empty<Point[]>(); _shadow = 0; _sand = 0; _spray = 0; BallRadius = 0; BoltGlow = 0; _burst = 0; _boltTime = 0; }

    private double Spread(double size) => (_random.NextDouble() * 2 - 1) * size;

    /// <summary>A jagged line that zigs up and down unevenly, so it reads as lightning and not a spring.</summary>
    private IEnumerable<Point> Zigzag(Point from, Point to, int segments, double least, double most)
    {
        var sign = _random.NextDouble() < .5 ? 1 : -1;
        for (var i = 0; i <= segments; i++)
        {
            if (i == 0) { yield return from; continue; }
            if (i == segments) { yield return to; continue; }
            var t = i / (double)segments + Spread(.3 / segments);
            var jag = sign * (least + _random.NextDouble() * (most - least));
            if (_random.NextDouble() < .7) sign = -sign;
            yield return new Point(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t + jag);
        }
    }

    private void Add(byte kind, double x, double y, double vx, double vy, double life, double size, double grow)
    {
        if (Particles.Count < 240) Particles.Add(new Particle { Kind = kind, X = x, Y = y, VX = vx, VY = vy, Life = life, Size = size, Grow = grow });
    }

    /// <summary>
    /// Finds a flame drawn in the sprite (bright yellow and orange near its top), so a fire type's tail burns where its
    /// artist put the flame. Null when there is none.
    /// </summary>
    public static (double X, double Y)? FindFlame(BitmapSource sprite)
    {
        try
        {
            var pixels = new FormatConvertedBitmap(sprite, PixelFormats.Bgra32, null, 0);
            int width = pixels.PixelWidth, height = pixels.PixelHeight;
            var data = new byte[width * height * 4];
            pixels.CopyPixels(data, width * 4, 0);
            return FindFlame(data, width, height);
        }
        catch { return null; }
    }

    internal static (double X, double Y)? FindFlame(byte[] bgra, int width, int height)
    {
        double sumX = 0, sumY = 0; var count = 0;
        for (var y = 0; y < height * .45; y++)
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                if (bgra[i + 3] > 128 && bgra[i + 2] > 230 && bgra[i + 1] > 170 && bgra[i] < 120) { sumX += x; sumY += y; count++; }
            }
        return count >= 6 ? ((sumX / count + .5) / width, (sumY / count) / height) : null;
    }
}

/// <summary>Draws a creature's effects as crisp squares, over the window it walks in.</summary>
internal sealed class CreatureEffectsView : FrameworkElement
{
    private readonly CreatureEffects _effects;
    private readonly Dictionary<uint, SolidColorBrush> _brushes = new();
    private readonly Pen[] _glow = new Pen[9], _core = new Pen[9];

    public CreatureEffectsView(CreatureEffects effects)
    {
        _effects = effects;
        IsHitTestVisible = false;
        for (var i = 0; i < 9; i++)
        {
            var width = 1 + effects.Strength * .5;
            _glow[i] = new Pen(Brush(255, 214, 50, (byte)(i * 18)), 8 * width) { LineJoin = PenLineJoin.Miter, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            _core[i] = new Pen(Brush(255, 255, 225, (byte)Math.Min(255, i * 40)), 2.6 * width) { LineJoin = PenLineJoin.Miter };
            _glow[i].Freeze(); _core[i].Freeze();
        }
    }

    protected override void OnRender(DrawingContext context)
    {
        foreach (var p in _effects.Particles)
        {
            var t = p.Age / p.Life;
            var brush = p.Kind switch
            {
                1 => Brush(255, 240, 120, Fade(1 - t, 255)),
                3 => t < .3 ? Brush(196, 130, 255, Fade(1 - t, 210)) : t < .7 ? Brush(128, 64, 206, Fade(1 - t, 210)) : Brush(64, 26, 112, Fade(1 - t, 210)),
                4 => t < .3 ? Brush(232, 206, 150, Fade(1 - t, 235)) : t < .7 ? Brush(198, 166, 106, Fade(1 - t, 235)) : Brush(150, 120, 80, Fade(1 - t, 235)),
                5 => t < .25 ? Brush(214, 240, 255, Fade(1 - t, 240)) : t < .6 ? Brush(110, 180, 245, Fade(1 - t, 235)) : Brush(60, 120, 215, Fade(1 - t, 220)),
                6 => t < .4 ? Brush(160, 222, 110, Fade(1 - t * .6, 250)) : t < .75 ? Brush(104, 178, 76, Fade(1 - t * .6, 250)) : Brush(70, 130, 56, Fade(1 - t, 250)),
                7 => Brush(236, 242, 255, Fade((1 - t) * Math.Min(1, t * 6), 200)),
                _ => t < .2 ? Brush(255, 248, 200, Fade(1 - t, 235)) : t < .45 ? Brush(255, 186, 60, Fade(1 - t, 235))
                    : t < .7 ? Brush(238, 98, 32, Fade(1 - t, 235)) : Brush(170, 48, 30, Fade(1 - t, 235))
            };
            var size = Math.Round(p.Size);
            if (p.Kind == 7)
            {
                var length = Math.Round(Math.Min(22, Math.Abs(p.VX) * .07) * (1 + p.Size / 4));
                context.DrawRectangle(brush, null, new Rect(Math.Round(p.X - (p.VX > 0 ? length : 0)), Math.Round(p.Y), length, Math.Max(1, size / 2)));
                continue;
            }
            context.DrawRectangle(brush, null, new Rect(Math.Round(p.X - size / 2), Math.Round(p.Y - size / 2), size, size));
        }
        if (_effects.BallRadius > .5)
        {
            // A dark core in a purple glow, in steps so it keeps the pixel look.
            var r = _effects.BallRadius;
            var c = _effects.Ball;
            context.DrawEllipse(Brush(150, 80, 230, 70), null, c, r * 1.6, r * 1.6);
            context.DrawEllipse(Brush(120, 50, 200, 200), null, c, r, r);
            context.DrawEllipse(Brush(40, 12, 64, 255), null, c, r * .62, r * .62);
            context.DrawEllipse(Brush(220, 170, 255, 180), null, new Point(c.X - r * .3, c.Y - r * .3), r * .18, r * .18);
        }
        if (_effects.Bolt.Count > 1 && _effects.BoltGlow > 0)
        {
            var geometry = new StreamGeometry();
            using (var line = geometry.Open())
            {
                foreach (var stroke in _effects.Forks.Prepend(_effects.Bolt))
                {
                    if (stroke.Count < 2) continue;
                    line.BeginFigure(stroke[0], false, false);
                    for (var i = 1; i < stroke.Count; i++) line.LineTo(stroke[i], true, false);
                }
            }
            geometry.Freeze();
            var level = (int)Math.Round(_effects.BoltGlow * 8);
            context.DrawGeometry(null, _glow[level], geometry);
            context.DrawGeometry(null, _core[level], geometry);
        }
    }

    private static byte Fade(double amount, double most) => (byte)Math.Clamp(amount * most, 0, 255);

    /// <summary>Brushes are kept and reused; alpha comes in sixteen steps so there are few of them.</summary>
    private SolidColorBrush Brush(byte r, byte g, byte b, byte a)
    {
        a = (byte)(a / 16 * 16 + (a >= 240 ? 15 : 0));
        var key = (uint)(a << 24 | r << 16 | g << 8 | b);
        if (_brushes.TryGetValue(key, out var brush)) return brush;
        brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return _brushes[key] = brush;
    }
}
