using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace Webi;

/// <summary>What happened today: time per app, pomodoros, water, flies. Starts over every day.</summary>
public sealed class DailyStats
{
    /// <summary>yyyy-MM-dd of the day these numbers belong to.</summary>
    public string Date { get; set; } = "";
    /// <summary>Seconds of active use per app name (only apps Webi knows).</summary>
    public Dictionary<string, double> AppSeconds { get; set; } = new();
    /// <summary>Seconds with the user at the PC (mouse or keyboard in the last 2 min).</summary>
    public double ActiveSeconds { get; set; }
    public int Pomodoros { get; set; }
    public int Waters { get; set; }
    public int Flies { get; set; }
    public int Pokes { get; set; }
    /// <summary>The "8 hours at the PC" nudge was already given today.</summary>
    public bool LongDayNudged { get; set; }
    /// <summary>The end-of-day summary was already offered today.</summary>
    public bool SummaryOffered { get; set; }
}

/// <summary>A one-off timer or reminder set from the menu ("15 min", "14:00 reunião").</summary>
public sealed class QuickTimer
{
    public DateTime Due { get; set; }
    public string Text { get; set; } = "";
}

/// <summary>A sticky note Webi keeps hanging on a thread.</summary>
public sealed class StickyNote
{
    public string Text { get; set; } = "";
    public DateTime Created { get; set; }
}

/// <summary>
/// What Webi remembers between runs (friendship, today's numbers, timers, sticky notes), kept apart from the
/// settings in %APPDATA%\Godot\app_userdata\Webi\state.json. It is written now and then and on exit.
/// Nothing leaves the PC.
/// </summary>
public sealed class AppState
{
    /// <summary>Friendship points (see <see cref="Assistant.Friendship"/>).</summary>
    public int Affinity { get; set; }
    /// <summary>Friendship points earned per day, to cap how fast it grows.</summary>
    public int AffinityToday { get; set; }
    public DailyStats Today { get; set; } = new();
    public List<QuickTimer> Timers { get; set; } = new();
    public List<StickyNote> Notes { get; set; } = new();
    /// <summary>yyyy-MM-dd of the last holiday/birthday greeting, so it is said once a day.</summary>
    public string LastSpecialGreeting { get; set; } = "";

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = null,
    };

    [JsonIgnore]
    public bool Dirty { get; set; }

    /// <summary>Where it is saved; null keeps it in memory only (tests).</summary>
    [JsonIgnore]
    public string? Path { get; set; }

    public static string FilePath => ProjectSettings.GlobalizePath("user://state.json");

    public static AppState Load(string path)
    {
        AppState state;
        try
        {
            state = File.Exists(path) ? JsonSerializer.Deserialize<AppState>(File.ReadAllText(path), Json) ?? new AppState() : new AppState();
        }
        catch (Exception e)
        {
            // A broken state file only costs the friendship points and today's numbers: start over.
            GD.PushWarning($"state.json inválido, começando do zero: {e.Message}");
            state = new AppState();
        }
        state.Path = path;
        state.RollDay(DateTime.Now);
        return state;
    }

    /// <summary>Starts a fresh <see cref="Today"/> when the date changed (also drops timers far in the past).</summary>
    public void RollDay(DateTime now)
    {
        var date = now.ToString("yyyy-MM-dd");
        if (Today.Date == date) return;
        Today = new DailyStats { Date = date };
        AffinityToday = 0;
        Timers.RemoveAll(t => now - t.Due > TimeSpan.FromHours(12));
        Dirty = true;
    }

    public void Save()
    {
        Dirty = false;
        if (Path == null) return;
        try
        {
            File.WriteAllText(Path, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception e)
        {
            GD.PushWarning($"não consegui salvar state.json: {e.Message}");
        }
    }

    /// <summary>The apps used the most today, longest first.</summary>
    public IEnumerable<(string app, double seconds)> TopApps(int count) =>
        Today.AppSeconds.OrderByDescending(kv => kv.Value).Take(count).Select(kv => (kv.Key, kv.Value));
}
