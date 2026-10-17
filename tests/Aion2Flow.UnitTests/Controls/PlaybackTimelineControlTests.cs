using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Cloris.Aion2Flow.Controls;
using Cloris.Aion2Flow.Presentation;
using SkiaSharp;

namespace Cloris.Aion2Flow.Tests.Controls;

[Collection(AvaloniaTestCollection.Name)]
public sealed class PlaybackTimelineControlTests
{
    [Fact]
    public void TimelineControls_RenderInheritedPlayheadChanges()
    {
        AvaloniaTestHost.Run(() =>
        {
            PlaybackTimelineControl[] controls =
            [
                new PlaybackTimelineView(),
                new PlaybackTimelineStripView(),
                new PlaybackTimelineRulerView { TickBrush = Brushes.Transparent, Foreground = Brushes.Transparent }
            ];
            foreach (var control in controls)
            {
                control.SetValue(PlaybackTimelineControl.ViewportProperty, new PlaybackTimelineViewport(1_000, 5_000));
                control.SetValue(PlaybackTimelineControl.PositionMillisecondsProperty, 3_000);
                control.PlayheadBrush = Brushes.Magenta;
                control.Measure(new Size(80, 20));
                control.Arrange(new Rect(0, 0, 80, 20));
                using (var bitmap = Render(control))
                    Assert.Equal(SKColors.Magenta, bitmap.GetPixel(40, 2));

                control.PositionMilliseconds = 4_000;
                control.PlayheadBrush = Brushes.Lime;
                using (var bitmap = Render(control))
                {
                    Assert.Equal(SKColors.Lime, bitmap.GetPixel(60, 2));
                    Assert.Equal(0, bitmap.GetPixel(40, 2).Alpha);
                }

                control.IsPlayheadVisible = false;
                using (var bitmap = Render(control))
                    Assert.Equal(0, bitmap.GetPixel(60, 2).Alpha);
            }
        });
    }

    private static SKBitmap Render(PlaybackTimelineControl control)
    {
        using var target = new RenderTargetBitmap(new PixelSize(80, 20));
        target.Render(control);
        using var stream = new MemoryStream();
        target.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        return SKBitmap.Decode(stream);
    }
}
