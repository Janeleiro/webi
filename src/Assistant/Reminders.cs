using System;

namespace Webi.Assistant;

public enum ReminderKind { Water, Break, Pomodoro }

/// <summary>
/// Something Webi has to tell. <see cref="Shout"/> notices are alerts: Webi drops down to the middle of the
/// screen and shouts <see cref="Title"/> in a huge bubble, with <see cref="Detail"/> underneath.
/// Other notices go in the regular speech bubble.
/// </summary>
public readonly record struct Notice(string Title, string Detail, bool Shout = true, bool Happy = false)
{
    public string Plain => $"{Title} {Detail}".Trim();
}

/// <summary>Periodic wellness reminders and a pomodoro timer.</summary>
public sealed class Reminders
{
    readonly Config _config;
    double _waterElapsed, _breakElapsed;

    public bool PomodoroRunning { get; private set; }
    public bool PomodoroOnBreak { get; private set; }
    public DateTime PomodoroEnds { get; private set; }

    public event Action<ReminderKind, Notice>? Fired;

    public Reminders(Config config) => _config = config;

    /// <param name="activeSeconds">Elapsed seconds while the user was active (sleeping time doesn't count for wellness reminders).</param>
    public void Tick(double dt, bool userActive)
    {
        if (userActive)
        {
            _waterElapsed += dt;
            _breakElapsed += dt;
        }

        if (_config.WaterReminder && _waterElapsed >= _config.WaterMinutes * 60)
        {
            _waterElapsed = 0;
            Fired?.Invoke(ReminderKind.Water, new Notice("ÁGUAAAA!!! 💧", "Hora de beber água! Até aranhas precisam se hidratar."));
        }
        if (_config.BreakReminder && _breakElapsed >= _config.BreakMinutes * 60 && !PomodoroRunning)
        {
            _breakElapsed = 0;
            Fired?.Invoke(ReminderKind.Break, new Notice("PAUSA!!! 🧘", "Levanta e estica as pernas! Eu estico as minhas oito."));
        }

        if (PomodoroRunning && DateTime.Now >= PomodoroEnds)
        {
            if (PomodoroOnBreak)
            {
                PomodoroOnBreak = false;
                PomodoroEnds = DateTime.Now.AddMinutes(_config.PomodoroWorkMinutes);
                Fired?.Invoke(ReminderKind.Pomodoro, new Notice("BORA FOCAR!!! 🍅", $"A pausa acabou: mais {_config.PomodoroWorkMinutes} minutos de foco.", Happy: true));
            }
            else
            {
                PomodoroOnBreak = true;
                PomodoroEnds = DateTime.Now.AddMinutes(_config.PomodoroBreakMinutes);
                Fired?.Invoke(ReminderKind.Pomodoro, new Notice("POMODORO CONCLUÍDO!!! 🍅", $"Descansa {_config.PomodoroBreakMinutes} minutinhos.", Happy: true));
            }
        }
    }

    public void StartPomodoro()
    {
        PomodoroRunning = true;
        PomodoroOnBreak = false;
        PomodoroEnds = DateTime.Now.AddMinutes(_config.PomodoroWorkMinutes);
    }

    public void StopPomodoro() => PomodoroRunning = false;

    /// <summary>Restart the water countdown (after its interval changed).</summary>
    public void ResetWater() => _waterElapsed = 0;

    /// <summary>Restart the break countdown (after its interval changed).</summary>
    public void ResetBreak() => _breakElapsed = 0;

    static string Left(double seconds) =>
        seconds < 60 ? "menos de 1 min" : $"{Math.Ceiling(seconds / 60):0} min";

    /// <summary>Human summary of what fires next (water, break, pomodoro).</summary>
    public string Upcoming()
    {
        var water = _config.WaterReminder
            ? $"💧 Água: em {Left(_config.WaterMinutes * 60 - _waterElapsed)} (a cada {_config.WaterMinutes} min)"
            : "💧 Água: desligado";
        var pause = !_config.BreakReminder ? "🧘 Pausa: desligado"
            : PomodoroRunning ? "🧘 Pausa: em espera enquanto o pomodoro roda"
            : $"🧘 Pausa: em {Left(_config.BreakMinutes * 60 - _breakElapsed)} (a cada {_config.BreakMinutes} min)";
        return $"{water}\n{pause}\n🍅 {PomodoroStatus()}\n(só conta o tempo em que você está usando o PC)";
    }

    public string PomodoroStatus()
    {
        if (!PomodoroRunning) return "Pomodoro parado.";
        var left = PomodoroEnds - DateTime.Now;
        return $"{(PomodoroOnBreak ? "Pausa" : "Foco")}: faltam {(int)left.TotalMinutes}min{left.Seconds:00}s.";
    }
}
