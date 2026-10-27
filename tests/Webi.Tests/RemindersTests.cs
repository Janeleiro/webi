using System.Collections.Generic;
using Webi.Assistant;
using Xunit;

namespace Webi.Tests;

public class RemindersTests
{
    static (Reminders reminders, List<ReminderKind> fired) Make(Config config)
    {
        var reminders = new Reminders(config);
        var fired = new List<ReminderKind>();
        reminders.Fired += (kind, _) => fired.Add(kind);
        return (reminders, fired);
    }

    /// <summary>Advances in one-second ticks, like the game loop would (in bigger steps).</summary>
    static void Run(Reminders reminders, double seconds, bool active = true)
    {
        for (double t = 0; t < seconds; t++) reminders.Tick(1, active);
    }

    [Fact]
    public void Water_fires_once_per_interval_of_use()
    {
        var (reminders, fired) = Make(new Config { WaterMinutes = 10, BreakReminder = false });
        Run(reminders, 10 * 60 - 1);
        Assert.Empty(fired);
        Run(reminders, 1);
        Assert.Equal(new[] { ReminderKind.Water }, fired);
        Run(reminders, 10 * 60 - 1);
        Assert.Single(fired);
    }

    [Fact]
    public void Idle_time_does_not_count()
    {
        var (reminders, fired) = Make(new Config { WaterMinutes = 1, BreakReminder = false });
        Run(reminders, 10 * 60, active: false);
        Assert.Empty(fired);
    }

    [Fact]
    public void Break_waits_while_a_pomodoro_runs()
    {
        var (reminders, fired) = Make(new Config { WaterReminder = false, BreakMinutes = 1, PomodoroWorkMinutes = 25 });
        reminders.StartPomodoro();
        Run(reminders, 120);
        Assert.Empty(fired);
        reminders.StopPomodoro();
        Run(reminders, 1);
        Assert.Equal(new[] { ReminderKind.Break }, fired);
    }

    [Fact]
    public void Snooze_fires_again_after_the_snooze_minutes()
    {
        var (reminders, fired) = Make(new Config { WaterMinutes = 45, BreakReminder = false });
        Run(reminders, 45 * 60);
        Assert.Single(fired);
        reminders.Snooze(ReminderKind.Water);
        Run(reminders, Reminders.SnoozeMinutes * 60 - 1);
        Assert.Single(fired);
        Run(reminders, 1);
        Assert.Equal(2, fired.Count);
    }

    [Fact]
    public void Snooze_longer_than_the_interval_waits_a_full_interval()
    {
        var (reminders, fired) = Make(new Config { WaterMinutes = 5, BreakReminder = false });
        reminders.Snooze(ReminderKind.Water);
        Run(reminders, 5 * 60 - 1);
        Assert.Empty(fired);
        Run(reminders, 1);
        Assert.Single(fired);
    }

    [Fact]
    public void Water_and_break_notices_offer_snooze()
    {
        var reminders = new Reminders(new Config { WaterMinutes = 1, BreakMinutes = 1 });
        var notices = new List<Notice>();
        reminders.Fired += (_, notice) => notices.Add(notice);
        Run(reminders, 60);
        Assert.Contains(notices, n => n.Snooze == ReminderKind.Water);
        Assert.Contains(notices, n => n.Snooze == ReminderKind.Break);
    }
}
