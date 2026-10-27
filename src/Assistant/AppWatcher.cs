using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using Webi.Spider;
using Webi.Win32;

namespace Webi.Assistant;

/// <summary>
/// Watches the foreground app/browser tab and makes Webi comment on it: only after a full minute in the same
/// app, never more than once every few minutes, with app-specific or group lines, plus nudges for long
/// sessions and pomodoro suggestions in work apps. Meeting and streaming apps switch on quiet mode.
/// Nothing is stored or sent anywhere: it only reacts in the moment.
/// </summary>
public sealed class AppWatcher
{
    const double CommentAfter = 60;           // seconds in the same app before commenting
    const double GlobalCooldown = 180;        // between any two comments
    const double SameAppCooldown = 20 * 60;   // before commenting on the same app again
    const double PomodoroAfter = 10 * 60;
    const double PomodoroCooldown = 60 * 60;

    readonly Overlay _overlay;
    readonly Dictionary<uint, string> _processNames = new();
    readonly Dictionary<string, double> _lastCommentByApp = new();
    readonly Queue<string> _recentLines = new();
    readonly RandomNumberGenerator _rng = new();

    double _clock, _pollTimer, _since, _lastComment = -GlobalCooldown, _lastPomodoroHint = -PomodoroCooldown;
    bool _commented, _nudged, _pomodoroHinted;

    public AppInfo? Current { get; private set; }
    /// <summary>Process name of the foreground window (for debugging the catalog).</summary>
    public string LastProcess { get; private set; } = "";
    public bool Quiet => Current != null && AppCatalog.IsQuiet(Current.Group);
    /// <summary>Seconds in the current app.</summary>
    public double Dwell => _clock - _since;

    public Func<bool>? PomodoroRunning { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>A line for Webi to say, with the face and gesture that go with it.</summary>
    public event Action<Reaction>? Comment;

    public AppWatcher(Overlay overlay) => _overlay = overlay;

    /// <param name="canTalk">False while asleep, hidden for fullscreen, or busy shouting an alert.</param>
    public void Tick(double dt, double idleSeconds, bool canTalk)
    {
        _clock += dt;
        _pollTimer -= dt;
        if (_pollTimer > 0) return;
        _pollTimer = 1;

        var fg = Native.GetForegroundWindow();
        // Clicking Webi focuses her own windows: keep the previous app in that case.
        if (fg == IntPtr.Zero || _overlay.Handles.Contains(fg)) return;
        var process = ProcessName(fg);
        LastProcess = process;
        var app = AppCatalog.Identify(process, Native.GetTitle(fg));
        if (app?.Id != Current?.Id)
        {
            Current = app;
            _since = _clock;
            _commented = _nudged = _pomodoroHinted = false;
        }
        if (Current == null || !Enabled || Quiet || !canTalk) return;
        // Only while the user is actually there.
        if (idleSeconds > 60) return;

        var group = Current.Group;

        // Long session nudges (they don't wait for the global cooldown).
        if (!_nudged && AppCatalog.LongSession.TryGetValue(group, out var nudge) && Dwell >= nudge.minutes * 60)
        {
            _nudged = true;
            Say(string.Format(Pick(nudge.lines), (int)(Dwell / 60)), Face.Surprised, Gesture.Tap);
            return;
        }

        if (_clock - _lastComment < GlobalCooldown) return;

        if (!_pomodoroHinted && group is AppGroup.Code or AppGroup.Office && Dwell >= PomodoroAfter
            && _clock - _lastPomodoroHint >= PomodoroCooldown && PomodoroRunning?.Invoke() == false)
        {
            _pomodoroHinted = true;
            _lastPomodoroHint = _clock;
            Say(Pick(AppCatalog.PomodoroSuggestion), Face.Happy, Gesture.Wave);
            return;
        }

        if (_commented || Dwell < CommentAfter) return;
        if (_lastCommentByApp.TryGetValue(Current.Id, out var last) && _clock - last < SameAppCooldown) return;

        var groupLines = AppCatalog.GroupLines.GetValueOrDefault(group, Array.Empty<string>());
        var lines = Current.Lines.Length > 0 && (groupLines.Length == 0 || _rng.Randf() < 0.6f) ? Current.Lines : groupLines;
        if (lines.Length == 0) return;
        _commented = true;
        _lastCommentByApp[Current.Id] = _clock;
        var (face, gesture) = AppCatalog.Mood(group);
        Say(Pick(lines), face, gesture);
    }

    void Say(string text, Face face, Gesture gesture)
    {
        _lastComment = _clock;
        Comment?.Invoke(new Reaction(text, face, gesture));
    }

    /// <summary>Random line, avoiding the last few said.</summary>
    string Pick(string[] lines)
    {
        var fresh = lines.Where(l => !_recentLines.Contains(l)).ToArray();
        var line = (fresh.Length > 0 ? fresh : lines)[_rng.RandiRange(0, (fresh.Length > 0 ? fresh : lines).Length - 1)];
        _recentLines.Enqueue(line);
        while (_recentLines.Count > 12) _recentLines.Dequeue();
        return line;
    }

    string ProcessName(IntPtr hwnd)
    {
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        if (_processNames.TryGetValue(pid, out var name)) return name;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            name = process.ProcessName;
        }
        catch (Exception)
        {
            name = ""; // elevated or already gone
        }
        if (_processNames.Count > 500) _processNames.Clear();
        _processNames[pid] = name;
        return name;
    }
}
