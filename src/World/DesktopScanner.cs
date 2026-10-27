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

    /// <summary>Remote desktop clients: even in fullscreen the spider stays and walks over them.</summary>
    static readonly HashSet<string> RemoteDesktopProcesses = new(StringComparer.OrdinalIgnoreCase) { "mstsc", "msrdc" };

    readonly Overlay _overlay;
    readonly List<DesktopWindow> _result = new();
    readonly HashSet<IntPtr> _own = new();
    readonly ProcessNames _processNames = new();
    bool _avoidFullscreen;

    /// <summary>Monitors (indices into <see cref="Overlay.Monitors"/>) taken by a fullscreen app (game, video) in the last scan.</summary>
    public readonly HashSet<int> BlockedMonitors = new();

    public DesktopScanner(Overlay overlay) => _overlay = overlay;

    /// <param name="avoidFullscreen">Mark monitors covered by a fullscreen app (other than remote desktop) as blocked.</param>
    public IReadOnlyList<DesktopWindow> Scan(bool avoidFullscreen)
    {
        _result.Clear();
        _own.Clear();
        BlockedMonitors.Clear();
        _avoidFullscreen = avoidFullscreen;
        foreach (var h in _overlay.Handles) _own.Add(h);
        Native.EnumWindows(Visit, IntPtr.Zero);
        return _result;
    }

    bool Visit(IntPtr hwnd, IntPtr _)
    {
        if (_own.Contains(hwnd)) return true;
        if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd) || Native.IsCloaked(hwnd)) return true;

        long ex = Native.GetExStyle(hwnd);
        // Other click-through overlays (including other desktop pets) are not solid.
        if ((ex & Native.WS_EX_TRANSPARENT) != 0 && (ex & Native.WS_EX_LAYERED) != 0) return true;
        if (Native.GetWindowTextLength(hwnd) == 0) return true;
        if (IgnoredClasses.Contains(Native.GetClass(hwnd))) return true;

        int covered = CoveredMonitor(hwnd);
        // A borderless window over a whole monitor is a game/video: the spider leaves that monitor alone.
        // A maximized window keeps its caption, and remote desktop is fine to walk over.
        if (covered >= 0 && _avoidFullscreen && !HasCaption(hwnd) && !IsRemoteDesktop(hwnd)) BlockedMonitors.Add(covered);

        if ((ex & Native.WS_EX_TOOLWINDOW) != 0 && (ex & Native.WS_EX_APPWINDOW) == 0) return true;
        if (!Native.GetVisibleBounds(hwnd, out var r) || r.Width < 120 || r.Height < 60) return true;

        var rect = new Rect2(r.Left - _overlay.WorldOrigin.X, r.Top - _overlay.WorldOrigin.Y, r.Width, r.Height);
        _result.Add(new DesktopWindow(hwnd.ToInt64(), rect, Native.GetTitle(hwnd), CoversMonitor: covered >= 0));
        return true;
    }

    /// <summary>Index of the monitor the window fully covers, or -1.</summary>
    int CoveredMonitor(IntPtr hwnd)
    {
        if (!Native.GetWindowRect(hwnd, out var r)) return -1;
        var rect = new Rect2(r.Left - _overlay.WorldOrigin.X, r.Top - _overlay.WorldOrigin.Y, r.Width, r.Height);
        for (int i = 0; i < _overlay.Monitors.Count; i++)
        {
            var b = _overlay.Monitors[i].Bounds;
            if (rect.Encloses(new Rect2(b.Position, b.Size))) return i;
        }
        return -1;
    }

    static bool HasCaption(IntPtr hwnd) =>
        (Native.GetWindowLongPtr(hwnd, Native.GWL_STYLE).ToInt64() & Native.WS_CAPTION) == Native.WS_CAPTION;

    bool IsRemoteDesktop(IntPtr hwnd) => RemoteDesktopProcesses.Contains(_processNames.Of(hwnd));

    /// <summary>The monitor (world rect) fully covered by the foreground app (game, video, presentation), if any.</summary>
    public Rect2? FullscreenMonitor()
    {
        var fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero || _overlay.Handles.Contains(fg) || IsRemoteDesktop(fg)) return null;
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
