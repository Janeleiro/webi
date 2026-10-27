using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Webi.Win32;

/// <summary>A connected monitor, in world coordinates.</summary>
public readonly record struct Monitor(Rect2I Bounds, Rect2I WorkArea, bool Primary);

/// <summary>
/// One transparent, always-on-top, click-through window per monitor. They all render the same World2D,
/// each offset to its monitor, so the spider can walk across the bezel. Clicks only stop passing through
/// over something interactive.
///
/// World coordinates are Win32 virtual-desktop pixels shifted so the top-left of all monitors is (0, 0).
/// Windows are placed with Win32 directly so nothing depends on Godot's screen coordinate mapping.
/// </summary>
public sealed class Overlay
{
    sealed class Surface
    {
        public Godot.Window Window = null!;
        public IntPtr Hwnd;
        public Rect2I Rect;
        public bool ClickThrough;
    }

    readonly List<Surface> _surfaces = new();
    Node _root = null!;
    string _layoutKey = "";

    public IReadOnlyList<Monitor> Monitors { get; private set; } = Array.Empty<Monitor>();

    /// <summary>Win32 virtual-desktop position of world (0, 0).</summary>
    public Vector2I WorldOrigin { get; private set; }

    /// <summary>Bounding box of all monitors, in world coordinates.</summary>
    public Rect2 Bounds { get; private set; }

    public IEnumerable<IntPtr> Handles => _surfaces.Select(s => s.Hwnd);

    public void Setup(Node root)
    {
        _root = root;
        // Popups and the extra windows become real OS windows instead of being drawn inside the main one.
        root.GetViewport().GuiEmbedSubwindows = false;
        Rebuild(ReadMonitors());
    }

    /// <summary>Call every few seconds. Returns true when monitors were plugged/unplugged/rearranged and the windows rebuilt.</summary>
    public bool CheckLayout()
    {
        var raw = ReadMonitors();
        if (raw.Count == 0 || Key(raw) == _layoutKey) return false;
        Rebuild(raw);
        return true;
    }

    static List<(Native.RECT bounds, Native.RECT work, bool primary)> ReadMonitors()
    {
        var list = new List<(Native.RECT, Native.RECT, bool)>();
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr _, ref Native.RECT _, IntPtr _) =>
        {
            var info = new Native.MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
            if (Native.GetMonitorInfo(h, ref info))
                list.Add((info.rcMonitor, info.rcWork, (info.dwFlags & Native.MONITORINFOF_PRIMARY) != 0));
            return true;
        }, IntPtr.Zero);
        // Primary first: it gets the main Godot window.
        return list.OrderByDescending(m => m.Item3).ThenBy(m => m.Item1.Left).ToList();
    }

    static string Key(List<(Native.RECT bounds, Native.RECT work, bool primary)> raw) =>
        string.Join(";", raw.Select(m =>
            $"{m.bounds.Left},{m.bounds.Top},{m.bounds.Right},{m.bounds.Bottom}/{m.work.Left},{m.work.Top},{m.work.Right},{m.work.Bottom}"));

    void Rebuild(List<(Native.RECT bounds, Native.RECT work, bool primary)> raw)
    {
        _layoutKey = Key(raw);
        int minX = raw.Min(m => m.bounds.Left), minY = raw.Min(m => m.bounds.Top);
        WorldOrigin = new Vector2I(minX, minY);
        Rect2I ToWorld(Native.RECT r) => new(r.Left - minX, r.Top - minY, r.Width, r.Height);
        Monitors = raw.Select(m => new Monitor(ToWorld(m.bounds), ToWorld(m.work), m.primary)).ToList();
        var union = Monitors[0].Bounds;
        foreach (var m in Monitors) union = union.Merge(m.Bounds);
        Bounds = new Rect2(union.Position, union.Size);

        // Drop the extra windows of the previous layout; the main window is reused for the first monitor.
        foreach (var s in _surfaces.Skip(1)) s.Window.QueueFree();
        _surfaces.Clear();

        for (int i = 0; i < Monitors.Count; i++)
        {
            var window = i == 0
                ? _root.GetWindow()
                : new Godot.Window
                {
                    Title = "Webi",
                    Unresizable = true,
                    // Transparency flags at creation time, like the main window gets from the project settings.
                    Borderless = true,
                    Transparent = true,
                    TransparentBg = true,
                    AlwaysOnTop = true,
                    World2D = _root.GetWindow().World2D, // same canvas as the main window
                };
            // Added to the tree first: the canvas transform needs the shared canvas attached to the window.
            if (i > 0) _root.AddChild(window);
            ConfigureWindow(window, Monitors[i].Bounds);
            // Extra windows come up hidden after their flags change; make sure every one is shown.
            window.Show();
            AddSurface(window, Monitors[i].Bounds);
        }
    }

    static void ConfigureWindow(Godot.Window window, Rect2I rect)
    {
        window.Borderless = true;
        window.Transparent = true;
        window.TransparentBg = true;
        window.AlwaysOnTop = true;
        // One pixel shorter than the monitor: a borderless window that exactly covers a monitor gets
        // promoted to "fullscreen" by DWM, which disables per-pixel transparency.
        window.Size = new Vector2I(rect.Size.X, rect.Size.Y - 1);
        window.Position = rect.Position;
        // Each window looks at its own monitor's piece of the shared world.
        window.CanvasTransform = new Transform2D(0, -(Vector2)rect.Position);
    }

    void AddSurface(Godot.Window window, Rect2I rect)
    {
        var hwnd = new IntPtr(DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, window.GetWindowId()));
        // Hide from the taskbar / Alt+Tab and prepare for click-through.
        long ex = Native.GetExStyle(hwnd);
        ex = (ex | Native.WS_EX_TOOLWINDOW | Native.WS_EX_LAYERED) & ~Native.WS_EX_APPWINDOW;
        Native.SetExStyle(hwnd, ex);
        Native.SetLayeredWindowAttributes(hwnd, 0, 255, Native.LWA_ALPHA);
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, rect.Position.X + WorldOrigin.X, rect.Position.Y + WorldOrigin.Y,
            rect.Size.X, rect.Size.Y - 1, Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED | Native.SWP_SHOWWINDOW);

        var surface = new Surface { Window = window, Hwnd = hwnd, Rect = rect };
        _surfaces.Add(surface);
        SetClickThrough(surface, true);
    }

    static void SetClickThrough(Surface s, bool enabled)
    {
        s.ClickThrough = enabled;
        long ex = Native.GetExStyle(s.Hwnd);
        ex = enabled ? ex | Native.WS_EX_TRANSPARENT : ex & ~Native.WS_EX_TRANSPARENT;
        Native.SetExStyle(s.Hwnd, ex);
    }

    /// <summary>Only the window under the cursor stops passing clicks through, and only when <paramref name="interactive"/>.</summary>
    public void UpdateClickThrough(Vector2 cursor, bool interactive)
    {
        foreach (var s in _surfaces)
        {
            bool through = !(interactive && new Rect2(s.Rect.Position, s.Rect.Size).HasPoint(cursor));
            if (through != s.ClickThrough) SetClickThrough(s, through);
        }
    }

    /// <summary>Other "always on top" apps can push us down; call this now and then.</summary>
    public void ReassertTopmost()
    {
        foreach (var s in _surfaces)
            Native.SetWindowPos(s.Hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    /// <summary>Cursor position in world coordinates (works even while the windows are click-through).</summary>
    public Vector2 CursorWorld()
    {
        Native.GetCursorPos(out var p);
        return new Vector2(p.X - WorldOrigin.X, p.Y - WorldOrigin.Y);
    }

    /// <summary>The monitor containing the point, or the nearest one.</summary>
    public Monitor MonitorAt(Vector2 p)
    {
        foreach (var m in Monitors)
            if (new Rect2(m.Bounds.Position, m.Bounds.Size).HasPoint(p)) return m;
        return Monitors.OrderBy(m => new Rect2(m.Bounds.Position, m.Bounds.Size).GetCenter().DistanceSquaredTo(p)).First();
    }

    /// <summary>The overlay window of the monitor containing the point (popups opened from it stay on that monitor).</summary>
    public Godot.Window WindowAt(Vector2 p)
    {
        foreach (var s in _surfaces)
            if (new Rect2(s.Rect.Position, s.Rect.Size).HasPoint(p)) return s.Window;
        return _surfaces[0].Window;
    }

    public Monitor Primary => Monitors.FirstOrDefault(m => m.Primary, Monitors[0]);

    public IReadOnlyList<Rect2> WorkAreas => Monitors.Select(m => new Rect2(m.WorkArea.Position, m.WorkArea.Size)).ToList();
}
