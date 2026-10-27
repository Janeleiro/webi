using System;
using Godot;

namespace Webi.Win32;

/// <summary>
/// Whether Windows asks apps to keep quiet: "Do not disturb" / Focus Assist, presentation mode, or the quiet
/// time after setting up Windows. Checked every couple of seconds.
/// </summary>
public sealed class DoNotDisturb
{
    /// <summary>WNF_SHEL_QUIETHOURS_ACTIVE_PROFILE_CHANGED: 0 = off, 1 = priority only, 2 = alarms only.</summary>
    const ulong QuietHoursState = 0x0D83063EA3BF1C75;

    double _timer;
    bool _wnfBroken;

    public bool Active { get; private set; }
    /// <summary>What the last check saw, for the debug log.</summary>
    public string Describe { get; private set; } = "-";

    public void Tick(double dt)
    {
        _timer -= dt;
        if (_timer > 0) return;
        _timer = 2;

        int quns = 0;
        bool shell = Native.SHQueryUserNotificationState(out quns) == 0
            && quns is Native.QUNS_PRESENTATION_MODE or Native.QUNS_QUIET_TIME;
        int profile = FocusProfile();
        Active = shell || profile > 0;
        Describe = $"quns={quns} focus={(profile < 0 ? "?" : profile.ToString())}";
    }

    /// <summary>Focus Assist profile, or -1 when it can't be read (the official API above still works).</summary>
    int FocusProfile()
    {
        if (_wnfBroken) return -1;
        try
        {
            ulong state = QuietHoursState;
            uint size = sizeof(int);
            if (Native.NtQueryWnfStateData(ref state, IntPtr.Zero, IntPtr.Zero, out _, out int profile, ref size) == 0)
                return size == sizeof(int) ? profile : 0; // no data yet: never switched on
        }
        catch (Exception e)
        {
            GD.PushWarning($"não perturbe: {e.Message}");
        }
        _wnfBroken = true;
        return -1;
    }
}
