using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Persistence;
using WallpaperProfiles.UI;
using Xunit;

namespace WallpaperProfiles.Tests;

public sealed class ClockWidgetTests
{
    [Fact]
    public void Spectrum_SeparatesFrequencyBands_AndClearsAfterSilence()
    {
        var bass = AnalyzeTone(150);
        var treble = AnalyzeTone(4000);
        Assert.True(bass.Max() > .5f);
        Assert.True(treble.Max() > .5f);
        Assert.True(Array.IndexOf(bass, bass.Max()) < Array.IndexOf(treble, treble.Max()));
        var analyzer = new SpectrumAnalyzer();
        var samples = Tone(1000);
        // Partial buffers must accumulate into complete analysis windows.
        analyzer.AddPcm16Stereo(samples[..4000], 4000, 1000);
        analyzer.AddPcm16Stereo(samples[4000..], samples.Length - 4000, 1000);
        Assert.True(analyzer.Read(1000).Max() > .5f);
        Assert.All(analyzer.Read(1251), value => Assert.Equal(0f, value));
        analyzer.AddPcm16Stereo(new byte[samples.Length], samples.Length, 1300);
        Assert.All(analyzer.Read(1300), value => Assert.Equal(0f, value));
    }

    private static float[] AnalyzeTone(int frequency)
    {
        var analyzer = new SpectrumAnalyzer();
        var buffer = Tone(frequency);
        analyzer.AddPcm16Stereo(buffer, buffer.Length, 1000);
        return analyzer.Read(1000);
    }

    private static byte[] Tone(int frequency)
    {
        var bytes = new byte[2048 * 4];
        for (var i = 0; i < 2048; i++)
        {
            var value = (short)(Math.Sin(2 * Math.PI * frequency * i / SpectrumAnalyzer.SampleRate) * 24000);
            BitConverter.GetBytes(value).CopyTo(bytes, i * 4);
            // Opposite phase should not silence the visualizer.
            BitConverter.GetBytes((short)-value).CopyTo(bytes, i * 4 + 2);
        }
        return bytes;
    }

    [Fact]
    public void ClockSettings_SurviveSaveMoveAndDuplicate()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var store = new DesktopCanvasStore(path);
            var widget = new DesktopWidget { Kind = DesktopWidgetKind.Clock, ClockUse24Hour = true,
                ClockShowGreeting = false, ClockShowVisualizer = false, ShowBackground = false, ShowBorder = false, ShowHeader = false };
            store.Save(new DesktopCanvasDocument { Widgets = new() { widget } });
            store.UpdateLayout(widget.Id, 320, 240, 520, 310);
            var copy = Assert.Single(store.Load().Widgets).Duplicate();
            Assert.Equal(DesktopWidgetKind.Clock, copy.Kind);
            Assert.True(copy.ClockUse24Hour);
            Assert.False(copy.ClockShowGreeting); Assert.False(copy.ClockShowVisualizer);
            Assert.False(copy.ShowBackground); Assert.False(copy.ShowBorder); Assert.False(copy.ShowHeader);
            Assert.Equal(520, copy.Width);
        }
        finally { System.IO.File.Delete(path); }
    }

    [Fact]
    public void Clock_UpdatesAcrossMidnight_AndAppliesOptions()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
                var widget = new DesktopWidget { Kind = DesktopWidgetKind.Clock, FontFamily = ClockWidgetView.ScriptFont, FontSize = 76, Alignment = WidgetTextAlignment.Center };
                var view = new ClockWidgetView(widget);
                view.Measure(new Size(488, 276)); view.Arrange(new Rect(0, 0, 488, 276));
                var text = Descendants(view).OfType<TextBlock>().ToDictionary(t => t.Name);
                Assert.All(text.Values, item => Assert.Equal(TextAlignment.Center, item.TextAlignment));
                view.RefreshTime(new DateTime(2026, 10, 10, 23, 59, 0));
                Assert.Equal("Saturday", text["ClockDay"].Text);
                Assert.Contains("P\u2009M", text["ClockTime"].Text);
                view.RefreshTime(new DateTime(2026, 10, 11, 0, 0, 0));
                Assert.Equal("Sunday", text["ClockDay"].Text);
                Assert.Equal("GOOD MORNING!", text["ClockGreetingText"].Text);
                widget.ClockUse24Hour = true; widget.ClockShowGreeting = false; widget.ClockShowVisualizer = false;
                view.UpdateAppearance(widget);
                view.RefreshTime(new DateTime(2026, 10, 11, 19, 9, 0));
                Assert.Equal("1\u20099\u2009:\u20090\u20099", text["ClockTime"].Text);
                Assert.Equal(Visibility.Collapsed, text["ClockGreetingText"].Visibility);
                Assert.Equal(Visibility.Collapsed, Assert.Single(Descendants(view).OfType<AudioSpectrumView>()).Visibility);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
