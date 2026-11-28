using WallpaperProfiles.Engine;
using WallpaperProfiles.UI;
using Xunit;
using Point = System.Windows.Point;

namespace WallpaperProfiles.Tests;

public sealed class CreatureEffectsTests
{
    private static readonly Point Mouth = new(100, 50), Tail = new(80, 60);

    private static void Run(CreatureEffects effects, double seconds, int facing = 1, Action? each = null)
    {
        for (var i = 0; i < seconds * 60; i++) { effects.Update(1 / 60.0, Mouth, Tail, facing); each?.Invoke(); }
    }

    [Fact]
    public void Types_choose_the_power()
    {
        Assert.Equal(CreaturePower.Fire, CreatureEffects.PowerOf(new[] { "Dragon", "Flying" }));
        Assert.Equal(CreaturePower.Lightning, CreatureEffects.PowerOf(new[] { "Electric", "Flying" }));
        Assert.Equal(CreaturePower.None, CreatureEffects.PowerOf(new[] { "Fire", "Flying" }));
        Assert.Equal(CreaturePower.Shadow, CreatureEffects.PowerOf(new[] { "Ghost", "Poison" }));
        Assert.Equal(CreaturePower.Sand, CreatureEffects.PowerOf(new[] { "Ground", "Flying" }));
        Assert.True(CreatureEffects.HasTailFlame(new[] { "Fire", "Flying" }));
        Assert.False(CreatureEffects.HasTailFlame(new[] { "Electric" }));
    }

    [Fact]
    public void Dragons_breathe_fire_the_way_they_face()
    {
        var effects = new CreatureEffects(CreaturePower.Fire, false, new Random(2));
        var started = false;
        for (var i = 0; i < 60 * 8 && !started; i++) started = effects.Update(1 / 60.0, Mouth, Tail, -1);
        Assert.True(started);
        Run(effects, .5, -1);
        Assert.NotEmpty(effects.Particles);
        Assert.All(effects.Particles, p => Assert.True(p.X < Mouth.X + 2)); // Facing left, the fire goes left.
        Run(effects, 2, -1);
        Assert.Empty(effects.Particles); // A burst, not a stream.
    }

    [Fact]
    public void Lightning_is_a_jagged_bolt_that_flickers()
    {
        var effects = new CreatureEffects(CreaturePower.Lightning, false, new Random(4));
        var shapes = new HashSet<string>();
        Run(effects, 9, 1, () => { if (effects.Bolt.Count > 0) shapes.Add(string.Join(";", effects.Bolt)); });
        Assert.True(shapes.Count >= 3);
        Run(effects, 0, 1);
        var bolt = shapes.First().Split(';').Select(Point.Parse).ToList();
        Assert.Equal(Mouth, bolt[0]);
        Assert.InRange(bolt[^1].X - Mouth.X, 70, 95);
        Assert.Contains(bolt, point => point.Y != Mouth.Y);
    }

    [Fact]
    public void A_fire_tail_keeps_burning_and_rises()
    {
        var effects = new CreatureEffects(CreaturePower.None, true, new Random(1));
        Run(effects, 1);
        Assert.InRange(effects.Particles.Count, 5, 40);
        Assert.All(effects.Particles, p => Assert.InRange(p.Y, Tail.Y - 40, Tail.Y + 2));
        Assert.Contains(effects.Particles, p => p.Y < Tail.Y - 5);
        Assert.False(effects.Attacking);
    }

    [Fact]
    public void The_flame_drawn_on_a_sprite_is_found()
    {
        var bgra = new byte[10 * 10 * 4];
        for (var y = 1; y < 3; y++)
            for (var x = 6; x < 9; x++) { var i = (y * 10 + x) * 4; bgra[i] = 40; bgra[i + 1] = 210; bgra[i + 2] = 255; bgra[i + 3] = 255; }
        var flame = CreatureEffects.FindFlame(bgra, 10, 10);
        Assert.NotNull(flame);
        Assert.InRange(flame!.Value.X, .7, .8);
        Assert.InRange(flame.Value.Y, .1, .2);
        Assert.Null(CreatureEffects.FindFlame(new byte[400], 10, 10));
    }

    [Fact]
    public void A_pack_can_place_the_mouth_and_tail()
    {
        var creature = Assert.Single(CreaturePack.Parse("""{ "creatures": [ { "id": "a", "name": "A", "frames": ["a-0.png"], "mouth": [0.8, 0.3], "tail": [2, 0.5] } ] }"""));
        Assert.Equal((.8, .3), creature.Mouth);
        Assert.Null(creature.Tail); // Out of range is ignored.
    }

    [Fact]
    public void Effects_grow_with_level()
    {
        Assert.Equal(0, CreatureEffects.StrengthFor(1));
        Assert.Equal(1, CreatureEffects.StrengthFor(CreatureEffects.FullLevel));
        Assert.Equal(1, CreatureEffects.StrengthFor(200));
        double Longest(double strength)
        {
            var effects = new CreatureEffects(CreaturePower.Lightning, false, new Random(4), strength);
            var most = 0.0;
            Run(effects, 12, 1, () => { if (effects.Bolt.Count > 0) most = Math.Max(most, effects.Bolt[^1].X - Mouth.X); });
            return most;
        }
        Assert.True(Longest(1) > Longest(0) * 1.6);
        Assert.True(Longest(1) <= CreatureEffects.ReachFor(1)); // The window has room for it.
    }

    [Fact]
    public void A_ghost_charges_a_shadow_ball_that_flies_off_and_bursts()
    {
        var effects = new CreatureEffects(CreaturePower.Shadow, false, new Random(2));
        var balls = new List<(Point Where, double Radius)>();
        Run(effects, 9, 1, () => { if (effects.BallRadius > 0) balls.Add((effects.Ball, effects.BallRadius)); });
        Assert.NotEmpty(balls);
        Assert.Equal(Mouth, balls[0].Where); // It gathers at the mouth first,
        Assert.True(balls[0].Radius < balls[^1].Radius);
        Assert.True(balls[^1].Where.X > Mouth.X + 40); // then flies off the way it faces.
        Assert.True(balls[^1].Where.X - Mouth.X < CreatureEffects.ReachFor(0));
        Assert.Contains(effects.Particles, p => p.Kind == 3);
    }

    [Fact]
    public void Ground_types_throw_sand_that_settles()
    {
        var effects = new CreatureEffects(CreaturePower.Sand, false, new Random(5));
        var sand = new List<Particle>();
        Run(effects, 9, -1, () => sand.AddRange(effects.Particles.Where(p => p.Kind == 4)));
        Assert.NotEmpty(sand);
        Assert.All(sand, p => Assert.True(p.X <= Mouth.X + 4)); // Facing left.
        Assert.Contains(sand, p => p.VY > 0); // Falls back down.
    }
}
