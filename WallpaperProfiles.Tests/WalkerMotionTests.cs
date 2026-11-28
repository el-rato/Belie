using WallpaperProfiles.Engine;
using WallpaperProfiles.UI;
using Xunit;

namespace WallpaperProfiles.Tests;

public sealed class WalkerMotionTests
{
    private static List<WalkerPose> Walk(WalkerMotion motion, double seconds, double fps, double left = 0, double right = 2000, double top = 500)
    {
        var poses = new List<WalkerPose>();
        for (var i = 0; i < seconds * fps; i++) poses.Add(motion.Advance(1 / fps, left, right, top, 1));
        return poses;
    }

    [Fact]
    public void The_walk_covers_the_same_ground_at_any_frame_rate()
    {
        var slow = Walk(new WalkerMotion(2, new Random(1)), 1, 30).Last();
        var fast = Walk(new WalkerMotion(2, new Random(1)), 1, 144).Last();
        Assert.Equal(slow.X, fast.X, 0);
        Assert.Equal(1000 + WalkerMotion.Speed, fast.X, 0);
    }

    [Fact]
    public void Steps_follow_the_distance_and_the_body_bobs_with_them()
    {
        var poses = Walk(new WalkerMotion(2, new Random(1)), 1, 60);
        Assert.Contains(poses, pose => pose.Frame == 1);
        Assert.Contains(poses, pose => pose.Frame == 0);
        Assert.All(poses, pose => Assert.InRange(pose.Lift, 0, WalkerMotion.Bob));
        Assert.Contains(poses, pose => pose.Lift > WalkerMotion.Bob / 2);
        // Frames change once per stride, not every tick.
        var changes = poses.Zip(poses.Skip(1)).Count(pair => pair.First.Frame != pair.Second.Frame);
        Assert.InRange(changes, WalkerMotion.Speed / WalkerMotion.Stride - 2, WalkerMotion.Speed / WalkerMotion.Stride + 2);
    }

    [Fact]
    public void At_the_edge_it_stops_and_turns_through_its_side_instead_of_flipping()
    {
        var motion = new WalkerMotion(2, new Random(1));
        var poses = Walk(motion, 3, 60, left: 0, right: 40);
        Assert.Contains(poses, pose => pose.ScaleX < -.9); // It ends up facing the other way.
        Assert.All(poses, pose => Assert.InRange(pose.X, 0, 40));
        // Mid-turn the sprite is narrow, never a sudden mirror.
        Assert.Contains(poses, pose => Math.Abs(pose.ScaleX) < .5);
    }

    [Fact]
    public void It_hops_up_onto_higher_ground_and_squashes_when_it_lands()
    {
        var motion = new WalkerMotion(2, new Random(1));
        Walk(motion, .5, 60, top: 500);
        var hop = Walk(motion, 1.5, 60, top: 400);
        Assert.Contains(hop, pose => pose.Y < 400 && pose.ScaleY > 1); // Stretched in the air, above the new ground.
        Assert.Contains(hop, pose => pose.Y == 400 && pose.ScaleY < .95); // Squashed on landing.
        Assert.Equal(400, hop.Last().Y);
        Assert.False(motion.Airborne);
    }

    [Fact]
    public void A_long_stall_does_not_make_it_leap()
    {
        var motion = new WalkerMotion(2, new Random(1));
        var before = motion.Advance(.016, 0, 2000, 500, 1).X;
        var after = motion.Advance(5, 0, 2000, 500, 1).X;
        Assert.InRange(after - before, 0, WalkerMotion.Speed * .06);
    }

    [Fact]
    public void A_full_screen_window_sends_it_away()
    {
        var screen = new System.Drawing.Rectangle(0, 0, 1920, 1080);
        Assert.True(DesktopWalker.CoversScreen(new System.Drawing.Rectangle(0, 0, 1920, 1080), screen));
        Assert.True(DesktopWalker.CoversScreen(new System.Drawing.Rectangle(-8, -8, 1936, 1096), screen));
        Assert.False(DesktopWalker.CoversScreen(new System.Drawing.Rectangle(0, 0, 1920, 1040), screen));
        Assert.False(DesktopWalker.CoversScreen(new System.Drawing.Rectangle(200, 100, 900, 700), screen));
    }
}

public sealed class MotionStyleTests
{
    private static List<WalkerPose> Run(MotionStyle style, double seconds, int seed = 3)
    {
        var motion = new WalkerMotion(2, new Random(seed), style);
        var poses = new List<WalkerPose>();
        for (var i = 0; i < seconds * 60; i++) poses.Add(motion.Advance(1 / 60.0, 0, 2000, 500, 1));
        return poses;
    }

    [Theory]
    [InlineData(new[] { "Ghost", "Poison" }, "Float")]
    [InlineData(new[] { "Dragon", "Flying" }, "Glide")]
    [InlineData(new[] { "Fire", "Flying" }, "Fly")]
    [InlineData(new[] { "Electric", "Flying" }, "Fly")]
    [InlineData(new[] { "Electric" }, "Dash")]
    [InlineData(new[] { "Normal" }, "Hop")]
    [InlineData(new[] { "Rock" }, "Stomp")]
    [InlineData(new[] { "Water" }, "Waddle")]
    public void Types_choose_how_a_creature_moves(string[] types, string style)
        => Assert.Equal(Enum.Parse<MotionStyle>(style), CreatureStyles.FromTypes(types));

    [Fact]
    public void A_pack_can_name_the_motion_itself()
    {
        Assert.Equal(MotionStyle.Hop, CreatureStyles.For(new Creature("a", "A", new[] { "Ghost" }, new[] { "a.png" }, "hop")));
        Assert.Equal(MotionStyle.Float, CreatureStyles.For(new Creature("a", "A", new[] { "Ghost" }, new[] { "a.png" }, "moonwalk")));
        var parsed = CreaturePack.Parse("""{ "creatures": [ { "id": "a", "name": "A", "frames": ["a-0.png"], "motion": "Glide" } ] }""");
        Assert.Equal(MotionStyle.Glide, CreatureStyles.For(Assert.Single(parsed)));
    }

    [Fact]
    public void Floaters_and_fliers_leave_the_ground_and_walkers_do_not()
    {
        Assert.All(Run(MotionStyle.Float, 3), pose => Assert.InRange(pose.Lift, 8, 20));
        Assert.Contains(Run(MotionStyle.Fly, 3), pose => pose.Lift > 25);
        Assert.Contains(Run(MotionStyle.Glide, 3), pose => Math.Abs(pose.Angle) > 8);
        Assert.All(Run(MotionStyle.Waddle, 3), pose => Assert.InRange(pose.Lift, 0, WalkerMotion.Bob));
    }

    [Fact]
    public void Hoppers_move_only_in_the_air()
    {
        var poses = Run(MotionStyle.Hop, 2);
        foreach (var (before, after) in poses.Zip(poses.Skip(1)))
            if (after.X != before.X) Assert.True(after.Lift > 0 || before.Lift > 0);
        Assert.Contains(poses, pose => pose.Lift > WalkerMotion.HopHeight * .9);
        Assert.True(poses.Last().X > 1000);
    }

    [Fact]
    public void Dashers_are_quicker_than_waddlers_and_lean_in()
    {
        var dash = Run(MotionStyle.Dash, 1, 5);
        Assert.True(dash.Max(pose => pose.X) - 1000 > WalkerMotion.Speed);
        Assert.Contains(dash, pose => Math.Abs(pose.Angle) >= 9);
    }

    [Fact]
    public void A_ghost_sometimes_vanishes_and_comes_back_elsewhere()
    {
        var poses = Run(MotionStyle.Float, 25);
        Assert.Contains(poses, pose => pose.Opacity < .05);
        Assert.True(poses.Last().Opacity > .7);
        Assert.Contains(poses.Zip(poses.Skip(1)), pair => Math.Abs(pair.Second.X - pair.First.X) > 20);
    }

    [Fact]
    public void Fliers_land_to_rest_and_take_off_again()
    {
        var poses = Run(MotionStyle.Fly, 30);
        var landed = poses.FindIndex(pose => pose.Lift == 0);
        Assert.True(landed > 0);
        Assert.Contains(poses.Skip(landed), pose => pose.Lift > 25);
    }

    [Theory]
    [InlineData("Waddle")] [InlineData("Hop")] [InlineData("Dash")] [InlineData("Float")]
    [InlineData("Fly")] [InlineData("Glide")] [InlineData("Stomp")]
    public void Every_style_idles_with_some_life_and_stays_small(string name)
    {
        var style = Enum.Parse<MotionStyle>(name);
        var poses = Enumerable.Range(0, 300).Select(i => WalkerMotion.Idle(style, i / 50.0, 2)).ToList();
        Assert.Contains(poses, pose => pose.Frame == 1);
        Assert.True(poses.Select(pose => (pose.Lift, pose.Angle, pose.ScaleY)).Distinct().Count() > 20);
        Assert.All(poses, pose => { Assert.InRange(pose.Lift, -1, 9); Assert.InRange(Math.Abs(pose.Angle), 0, 8); });
    }

    [Fact]
    public void A_bat_hangs_upside_down_swings_and_swoops()
    {
        Assert.Equal(MotionStyle.Hang, CreatureStyles.For(new Creature("g", "Gliscor", new[] { "Ground", "Flying" }, new[] { "g.png" }, "hang")));
        var poses = Run(MotionStyle.Hang, 30);
        Assert.All(poses, pose => Assert.True(pose.Hanging));
        Assert.All(poses, pose => Assert.Equal(500, pose.Y)); // Its feet stay on the ceiling,
        Assert.Contains(poses, pose => Math.Abs(pose.Angle) > 4); // it swings,
        Assert.Contains(poses, pose => pose.Lift > WalkerMotion.SwoopDepth * .9); // and swoops down and back.
        Assert.Equal(0, poses.Last(pose => pose.Lift == 0).Lift);
    }

    [Fact]
    public void The_bar_strip_is_found_inside_its_window()
    {
        var settings = new WallpaperProfiles.Models.TopBarSettings { Height = 38, BarMargin = 6, Edge = WallpaperProfiles.Models.TopBarEdge.Top };
        var strip = DesktopWalker.BarStrip(new System.Drawing.Rectangle(0, 0, 1920, 120), settings, 1.5)!.Value;
        Assert.Equal(new System.Drawing.Rectangle(9, 9, 1902, 57), strip);
        settings.Edge = WallpaperProfiles.Models.TopBarEdge.Bottom;
        strip = DesktopWalker.BarStrip(new System.Drawing.Rectangle(0, 960, 1920, 120), settings, 1.5)!.Value;
        Assert.Equal(1080 - 9, strip.Bottom);
    }
}
