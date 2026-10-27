using System.Linq;
using Godot;
using Webi.World;
using Xunit;

namespace Webi.Tests;

public class SurfaceMapTests
{
    static readonly Rect2[] Screen = { new(0, 0, 1920, 1040) };

    [Fact]
    public void Empty_desktop_has_the_taskbar_as_floor()
    {
        var map = new SurfaceMap();
        map.Rebuild(System.Array.Empty<DesktopWindow>(), Screen);
        var floor = map.FloorBelow(new Vector2(960, 100), out float distance, out _);
        Assert.NotNull(floor);
        Assert.Equal(940, distance, 1);
    }

    [Fact]
    public void A_window_top_is_a_floor()
    {
        var map = new SurfaceMap();
        map.Rebuild(new[] { new DesktopWindow(1, new Rect2(500, 400, 800, 400), "app") }, Screen);
        var floor = map.FloorBelow(new Vector2(900, 100), out float distance, out _);
        Assert.Equal(1, floor?.Owner);
        Assert.Equal(300, distance, 1);
    }

    [Fact]
    public void Edges_hidden_under_a_higher_window_are_cut()
    {
        var map = new SurfaceMap();
        var top = new DesktopWindow(1, new Rect2(400, 300, 400, 400), "top");
        var below = new DesktopWindow(2, new Rect2(200, 400, 1000, 400), "below");
        map.Rebuild(new[] { top, below }, Screen);
        // The top edge of the lower window (y = 400) has no piece between x = 400 and 800.
        var hidden = map.All.Where(s => s.Owner == 2 && s.Side == EdgeSide.Top)
            .Where(s => Mathf.Max(s.A.X, s.B.X) > 410 && Mathf.Min(s.A.X, s.B.X) < 790);
        Assert.Empty(hidden);
        Assert.Contains(map.All, s => s.Owner == 2 && s.Side == EdgeSide.Top);
    }

    [Fact]
    public void Rebuild_is_skipped_when_nothing_moved()
    {
        var map = new SurfaceMap();
        var windows = new[] { new DesktopWindow(1, new Rect2(500, 400, 800, 400), "app") };
        Assert.True(map.Rebuild(windows, Screen));
        // A new title (browser tab) doesn't change the edges.
        Assert.False(map.Rebuild(new[] { windows[0] with { Title = "other tab" } }, Screen));
        Assert.True(map.Rebuild(new[] { windows[0] with { Rect = new Rect2(510, 400, 800, 400) } }, Screen));
        Assert.True(map.Rebuild(windows, new[] { new Rect2(0, 0, 1920, 1080) }));
        Assert.True(map.Rebuild(System.Array.Empty<DesktopWindow>(), Screen));
    }
}
