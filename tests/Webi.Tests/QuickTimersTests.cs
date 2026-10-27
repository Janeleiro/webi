using System;
using Webi.Assistant;
using Xunit;

namespace Webi.Tests;

public class QuickTimersTests
{
    static readonly DateTime Now = new(2026, 10, 27, 10, 0, 0);

    [Theory]
    [InlineData("15 min", 15, "")]
    [InlineData("15", 15, "")]
    [InlineData("15 tirar o bolo", 15, "tirar o bolo")]
    [InlineData("1h30 reunião", 90, "reunião")]
    [InlineData("1h 30min", 90, "")]
    [InlineData("2 horas pra sair", 120, "sair")]
    [InlineData("1 hora e 15 almoço", 75, "almoço")]
    [InlineData("em 10 min ligar", 10, "ligar")]
    [InlineData("daqui a 5 minutos: chá", 5, "chá")]
    public void Durations(string input, int minutes, string text)
    {
        Assert.True(QuickTimers.TryParse(input, Now, out var due, out var what));
        Assert.Equal(Now.AddMinutes(minutes), due);
        Assert.Equal(text, what);
    }

    [Fact]
    public void Seconds() =>
        Assert.True(QuickTimers.TryParse("30 segundos", Now, out var due, out _) && due == Now.AddSeconds(30));

    [Theory]
    [InlineData("14:30 dentista", 14, 30, "dentista")]
    [InlineData("às 14h ligar pro banco", 14, 0, "ligar pro banco")]
    [InlineData("14h30 reunião", 14, 30, "reunião")]
    [InlineData("as 9 acordar", 9, 0, "acordar")]
    public void Clock_times_today_or_tomorrow(string input, int h, int m, string text)
    {
        Assert.True(QuickTimers.TryParse(input, Now, out var due, out var what));
        var expected = Now.Date.AddHours(h).AddMinutes(m);
        if (expected <= Now) expected = expected.AddDays(1);
        Assert.Equal(expected, due);
        Assert.Equal(text, what);
    }

    [Theory]
    [InlineData("")]
    [InlineData("reunião")]
    [InlineData("25:00")]
    [InlineData("0 min")]
    public void Rejects_nonsense(string input) => Assert.False(QuickTimers.TryParse(input, Now, out _, out _));

    [Fact]
    public void Fires_when_due_and_forgets_it()
    {
        var state = new AppState();
        var timers = new QuickTimers(state);
        int fired = 0;
        timers.Fired += _ => fired++;
        timers.Add(Now.AddMinutes(5), "café");
        timers.Tick(1, Now.AddMinutes(4));
        Assert.Equal(0, fired);
        timers.Tick(1, Now.AddMinutes(5));
        Assert.Equal(1, fired);
        Assert.Equal(0, timers.Count);
    }
}
