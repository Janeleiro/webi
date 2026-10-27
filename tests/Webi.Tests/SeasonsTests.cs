using System;
using Webi;
using Webi.Assistant;
using Xunit;

namespace Webi.Tests;

public class SeasonsTests
{
    [Theory]
    [InlineData(2024, 3, 31)]
    [InlineData(2025, 4, 20)]
    [InlineData(2026, 4, 5)]
    [InlineData(2027, 3, 28)]
    public void Easter(int y, int m, int d) => Assert.Equal(new DateTime(y, m, d), Seasons.Easter(y));

    [Fact]
    public void Halloween_week_and_day_get_the_witch_hat()
    {
        Assert.Equal(Hat.Witch, Seasons.Today(new DateTime(2026, 10, 27), "")?.Hat);
        var day = Seasons.Today(new DateTime(2026, 10, 31), "");
        Assert.Equal("halloween", day?.Id);
        Assert.NotNull(day?.Greeting);
    }

    [Fact]
    public void Birthday_wins()
    {
        var day = Seasons.Today(new DateTime(2026, 10, 31), "31/10");
        Assert.Equal("birthday", day?.Id);
        Assert.Equal(Hat.Party, day?.Hat);
    }

    [Theory]
    [InlineData(2026, 12, 10, Hat.Santa)]
    [InlineData(2026, 6, 15, Hat.Straw)]
    [InlineData(2026, 2, 14, Hat.Party)] // carnival saturday 2026
    [InlineData(2026, 4, 5, Hat.Bunny)]
    public void Season_hats(int y, int m, int d, Hat hat) => Assert.Equal(hat, Seasons.Today(new DateTime(y, m, d), "")?.Hat);

    [Theory]
    [InlineData("27/10", true)]
    [InlineData("29-02", true)]
    [InlineData("31/04", false)]
    [InlineData("10", false)]
    [InlineData("aa/bb", false)]
    public void Birthday_format(string text, bool ok) => Assert.Equal(ok, Seasons.TryParseBirthday(text, out _, out _));

    [Fact]
    public void Config_normalizes_birthday()
    {
        var c = new Config { Birthday = "5/3" };
        c.Validate();
        Assert.Equal("05/03", c.Birthday);
        c.Birthday = "lixo";
        c.Validate();
        Assert.Equal("", c.Birthday);
    }
}
