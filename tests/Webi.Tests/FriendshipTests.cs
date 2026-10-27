using System;
using Webi;
using Webi.Assistant;
using Xunit;

namespace Webi.Tests;

public class FriendshipTests
{
    [Fact]
    public void Points_are_capped_per_day_and_levels_go_up()
    {
        var state = new AppState();
        var f = new Friendship(state);
        int? leveled = null;
        f.LevelUp += l => leveled = l;
        f.Add(100);
        Assert.Equal(Friendship.DailyCap, f.Points);
        Assert.Equal(0, f.Level);
        state.AffinityToday = 0; // next day
        f.Add(10);
        Assert.Equal(1, f.Level);
        Assert.Equal(1, leveled);
    }

    [Fact]
    public void Losing_points_never_goes_below_zero()
    {
        var f = new Friendship(new AppState());
        f.Add(-5);
        Assert.Equal(0, f.Points);
    }

    [Fact]
    public void New_day_starts_fresh_numbers()
    {
        var state = new AppState();
        state.RollDay(new DateTime(2026, 10, 27, 9, 0, 0));
        state.Today.Flies = 3;
        state.AffinityToday = 7;
        state.RollDay(new DateTime(2026, 10, 27, 23, 0, 0));
        Assert.Equal(3, state.Today.Flies);
        state.RollDay(new DateTime(2026, 10, 28, 0, 1, 0));
        Assert.Equal(0, state.Today.Flies);
        Assert.Equal(0, state.AffinityToday);
    }

    [Fact]
    public void Summary_mentions_the_numbers()
    {
        var state = new AppState();
        state.Today.ActiveSeconds = 3 * 3600 + 12 * 60;
        state.Today.AppSeconds["VS Code"] = 2 * 3600;
        state.Today.Pomodoros = 4;
        var text = DaySummary.Describe(state);
        Assert.Contains("3h12", text);
        Assert.Contains("VS Code 2h00", text);
        Assert.Contains("4 pomodoros", text);
    }
}
