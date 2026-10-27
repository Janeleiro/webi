using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Webi.Win32;

namespace Webi.World;

/// <summary>Enumerates the real top-level windows (top-most first) in world coordinates.</summary>
public sealed class DesktopScanner
{
    static readonly HashSet<string> IgnoredClasses = new()
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow",
        "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow", "TopLevelWindowForOverflowXamlIsland",
    };

    readonly Overlay _overlay;
    readonly List<DesktopWindow> _result = new();
    readonly HashSet<IntPtr> _own = new();

    public DesktopScanner(Overlay overlay) => _overlay = overlay;

    public IReadOnlyList<DesktopWindow> Scan()
    {
        _result.Clear();
        _own.Clear();
        foreach (var h in _overlay.Handles) _own.Add(h);
        Native.EnumWindows(Visit, IntPtr.Zero);
        return _result;
    }

    bool Visit(IntPtr hwnd, IntPtr _)
    {
        if (_own.Contains(hwnd)) return true;
        if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd) || Native.IsCloaked(hwnd)) return true;

        long ex = Native.GetExStyle(hwnd);
        if ((ex & Native.WS_EX_TOOLWINDOW) != 0 && (ex & Native.WS_EX_APPWINDOW) == 0) return true;
        // Other click-through overlays (including other desktop pets) are not solid.
        if ((ex & Native.WS_EX_TRANSPARENT) != 0 && (ex & Native.WS_EX_LAYERED) != 0) return true;
        if (Native.GetWindowTextLength(hwnd) == 0) return true;
        if (IgnoredClasses.Contains(Native.GetClass(hwnd))) return true;
        if (!Native.GetVisibleBounds(hwnd, out var r) || r.Width < 120 || r.Height < 60) return true;

        var rect = new Rect2(r.Left - _overlay.WorldOrigin.X, r.Top - _overlay.WorldOrigin.Y, r.Width, r.Height);
        _result.Add(new DesktopWindow(hwnd.ToInt64(), rect, Native.GetTitle(hwnd)));
        return true;
    }

    /// <summary>The monitor (world rect) fully covered by the foreground app (game, video, presentation), if any.</summary>
    public Rect2? FullscreenMonitor()
    {
        var fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero || _overlay.Handles.Contains(fg)) return null;
        string cls = Native.GetClass(fg);
        if (cls is "Progman" or "WorkerW") return null;
        if (!Native.GetWindowRect(fg, out var r)) return null;
        var rect = new Rect2(r.Left - _overlay.WorldOrigin.X, r.Top - _overlay.WorldOrigin.Y, r.Width, r.Height);
        foreach (var m in _overlay.Monitors)
        {
            var b = new Rect2(m.Bounds.Position, m.Bounds.Size);
            if (rect.Encloses(b)) return b;
        }
        return null;
    }
}
