using System;

namespace Webi.Win32;

/// <summary>
/// Global shortcuts, read straight from the keyboard state each frame (Godot owns the window procedure, so
/// RegisterHotKey's messages would never reach us): Ctrl+Alt+W calls Webi to the cursor, Ctrl+Alt+H hides her.
/// </summary>
public sealed class Hotkeys
{
    const int VK_CONTROL = 0x11, VK_LMENU = 0xA4, VK_W = 0x57, VK_H = 0x48;

    bool _wasCall, _wasHide;

    public event Action? Call;
    public event Action? Hide;

    public void Tick()
    {
        // Left Alt only: AltGr (right Alt) is sent as Ctrl+Alt, and AltGr+W types "?" on ABNT2 keyboards.
        bool mods = Native.IsKeyDown(VK_CONTROL) && Native.IsKeyDown(VK_LMENU);
        bool call = mods && Native.IsKeyDown(VK_W);
        bool hide = mods && Native.IsKeyDown(VK_H);
        if (call && !_wasCall) Call?.Invoke();
        if (hide && !_wasHide) Hide?.Invoke();
        _wasCall = call;
        _wasHide = hide;
    }
}
