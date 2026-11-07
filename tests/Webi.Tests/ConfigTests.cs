using Xunit;

namespace Webi.Tests;

public class ConfigTests
{
    [Fact]
    public void Validate_keeps_sane_values()
    {
        var config = new Config();
        config.Validate();
        Assert.Equal(2, config.Scale);
        Assert.Equal(45, config.WaterMinutes);
        Assert.Equal(5, config.ScreenWebMinutes);
    }

    [Fact]
    public void Validate_clamps_hand_edited_values()
    {
        var config = new Config
        {
            Scale = 0, WaterMinutes = 0, BreakMinutes = -5, PomodoroWorkMinutes = 0, PomodoroBreakMinutes = 0,
            MaxWebs = -1, ScreenWebMinutes = -3, CpuAlertPercent = 500, BatteryAlertPercent = 0, SleepAfterIdleMinutes = 0,
        };
        config.Validate();
        Assert.Equal(1, config.Scale);
        Assert.Equal(1, config.WaterMinutes);
        Assert.Equal(1, config.BreakMinutes);
        Assert.Equal(1, config.PomodoroWorkMinutes);
        Assert.Equal(1, config.PomodoroBreakMinutes);
        Assert.Equal(0, config.MaxWebs);
        Assert.Equal(0, config.ScreenWebMinutes); // 0 still means "never"
        Assert.Equal(100, config.CpuAlertPercent);
        Assert.Equal(1, config.BatteryAlertPercent);
        Assert.Equal(1, config.SleepAfterIdleMinutes);
    }

    [Fact]
    public void Json_round_trip()
    {
        var config = Config.FromJson("""{ "water_minutes": 30, "fur_color": "ROXO", "screen_web_minutes": -3 }""");
        Assert.Equal(30, config.WaterMinutes);
        Assert.Equal("roxo", config.FurColor);
        Assert.Equal(0, config.ScreenWebMinutes);
        Assert.Equal(config.ToJson(), Config.FromJson(config.ToJson()).ToJson());
    }
}
