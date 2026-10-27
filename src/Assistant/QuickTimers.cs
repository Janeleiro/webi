using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Webi.Assistant;

/// <summary>
/// One-off timers and reminders typed in plain Portuguese: "15 min", "1h30 reunião", "às 14h ligar pro João",
/// "14:30 dentista". Kept in <see cref="AppState.Timers"/> so they survive a restart, and shouted when due.
/// </summary>
public sealed class QuickTimers
{
    readonly AppState _state;
    double _checkTimer;

    public event Action<Notice>? Fired;

    public QuickTimers(AppState state) => _state = state;

    // A clock time: "14:30", "14h", "14h30", "às 9", "as 9h".
    static readonly Regex Clock = new(
        @"^(?<at>(?:às|as|a)\s+)?(?<h>\d{1,2})(?:(?<colon>:)(?<m>\d{2})|(?<hh>h)(?<m>\d{2})?)?(?!\d)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static readonly Regex Amount = new(@"^(?<n>\d+)\s*(?<unit>[a-zà-ú]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Reads "when" and "what" from <paramref name="input"/>. "às 9", "14:30" and "14h" are clock times (past
    /// ones are for tomorrow); "15", "15 min", "1h30", "2 horas", "em 10 min" are durations. A bare "Nh" counts
    /// as a clock time from 5h on, so "1h30" is a duration and "14h" a time. Returns false with no time found.
    /// </summary>
    public static bool TryParse(string input, DateTime now, out DateTime due, out string text)
    {
        due = default;
        text = "";
        var s = input.Trim();
        if (s.Length == 0) return false;

        var c = Clock.Match(s);
        if (c.Success)
        {
            int h = int.Parse(c.Groups["h"].Value, CultureInfo.InvariantCulture);
            bool clock = c.Groups["at"].Success || c.Groups["colon"].Success || (c.Groups["hh"].Success && h >= 5);
            if (clock)
            {
                int m = c.Groups["m"].Success ? int.Parse(c.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
                if (h > 23 || m > 59) return false;
                due = now.Date.AddHours(h).AddMinutes(m);
                if (due <= now) due = due.AddDays(1);
                text = Clean(s[c.Length..]);
                return true;
            }
        }

        // Durations: numbers with units, one after the other ("1h 30min", "1 hora e 15").
        var rest = Regex.Replace(s, @"^(?:em|daqui\s+a|daqui)\s+", "", RegexOptions.IgnoreCase);
        var span = TimeSpan.Zero;
        bool any = false;
        while (true)
        {
            var a = Amount.Match(rest);
            if (!a.Success) break;
            int n = int.Parse(a.Groups["n"].Value, CultureInfo.InvariantCulture);
            string unit = a.Groups["unit"].Value.ToLowerInvariant();
            if (unit is "h" or "hr" or "hrs" or "hora" or "horas") span += TimeSpan.FromHours(n);
            else if (unit is "s" or "seg" or "segundo" or "segundos") span += TimeSpan.FromSeconds(n);
            else if (unit is "" or "m" or "min" or "mins" or "minuto" or "minutos") span += TimeSpan.FromMinutes(n);
            else
            {
                // "15 reunião": the number is minutes, the word starts the description.
                span += TimeSpan.FromMinutes(n);
                rest = rest[a.Groups["n"].Length..];
                any = true;
                break;
            }
            any = true;
            rest = rest[a.Length..].TrimStart();
            rest = Regex.Replace(rest, @"^e\s+(?=\d)", "", RegexOptions.IgnoreCase);
        }
        if (!any || span <= TimeSpan.Zero || span > TimeSpan.FromDays(2)) return false;
        due = now + span;
        text = Clean(rest);
        return true;
    }

    /// <summary>Drops joining words left at the start of the description ("para", "de", ":", "-").</summary>
    static string Clean(string rest)
    {
        var t = rest.Trim().TrimStart(':', '-', ',', '.').Trim();
        t = Regex.Replace(t, @"^(?:pra|para|p/|de|do|da)\s+", "", RegexOptions.IgnoreCase);
        return t;
    }

    public void Add(DateTime due, string text)
    {
        _state.Timers.Add(new QuickTimer { Due = due, Text = text });
        _state.Timers.Sort((a, b) => a.Due.CompareTo(b.Due));
        _state.Dirty = true;
    }

    public int Count => _state.Timers.Count;

    public void Clear()
    {
        _state.Timers.Clear();
        _state.Dirty = true;
    }

    public void Tick(double dt, DateTime now)
    {
        _checkTimer -= dt;
        if (_checkTimer > 0) return;
        _checkTimer = 1;
        foreach (var t in _state.Timers.Where(t => t.Due <= now).ToList())
        {
            _state.Timers.Remove(t);
            _state.Dirty = true;
            var late = now - t.Due > TimeSpan.FromMinutes(2) ? $" (era às {t.Due:HH:mm})" : "";
            Fired?.Invoke(t.Text.Length > 0
                ? new Notice("LEMBRETE!!! ⏰", Capitalize(t.Text) + late)
                : new Notice("TEMPO ESGOTADO!!! ⏲", "O timer que você pediu acabou." + late));
        }
    }

    static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];

    /// <summary>"in 12 min" / "at 14:30" for each pending timer.</summary>
    public string Describe(DateTime now)
    {
        if (_state.Timers.Count == 0) return "⏲ Nenhum timer ativo.";
        return string.Join("\n", _state.Timers.Select(t =>
        {
            var left = t.Due - now;
            var when = left.TotalMinutes < 60 ? $"em {Math.Max(1, (int)Math.Ceiling(left.TotalMinutes))} min" : $"às {t.Due:HH:mm}";
            if (t.Due.Date > now.Date) when += " (amanhã)";
            return $"⏲ {when}{(t.Text.Length > 0 ? ": " + t.Text : "")}";
        }));
    }

    /// <summary>What Webi says after a timer is set.</summary>
    public static string Confirmation(DateTime due, string text, DateTime now)
    {
        var left = due - now;
        string when = left.TotalMinutes < 60
            ? $"daqui a {Math.Max(1, (int)Math.Round(left.TotalMinutes))} min"
            : $"às {due:HH:mm}{(due.Date > now.Date ? " de amanhã" : "")}";
        return text.Length > 0 ? $"Combinado! Te lembro {when}: {text}. ⏰" : $"Timer ligado! Eu grito {when}. ⏲";
    }
}
