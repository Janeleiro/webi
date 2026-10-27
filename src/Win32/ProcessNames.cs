using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Webi.Win32;

/// <summary>Process name of a window, cached by process id (looking it up allocates a Process each time).</summary>
public sealed class ProcessNames
{
    readonly Dictionary<uint, string> _names = new();

    public string Of(IntPtr hwnd)
    {
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        if (_names.TryGetValue(pid, out var name)) return name;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            name = process.ProcessName;
        }
        catch (Exception)
        {
            name = ""; // elevated or already gone
        }
        if (_names.Count > 500) _names.Clear();
        _names[pid] = name;
        return name;
    }
}
