using System;
using Godot;
using Microsoft.Win32;

namespace Webi.Win32;

/// <summary>"Start with Windows" through the current user's Run key (no admin rights needed).</summary>
public static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "Webi";

    /// <summary>
    /// The command Windows runs at logon. From an exported build it is just the .exe; when running the
    /// project through the Godot editor binary, the project path is passed too. The console variant of
    /// the Godot binary is swapped for the windowed one so no console pops up at logon.
    /// </summary>
    public static string Command()
    {
        var exe = OS.GetExecutablePath().Replace('/', '\\').Replace("_console.exe", ".exe");
        if (OS.HasFeature("template")) return $"\"{exe}\"";
        var project = ProjectSettings.GlobalizePath("res://").TrimEnd('/').Replace('/', '\\');
        return $"\"{exe}\" --path \"{project}\"";
    }

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        catch (Exception e)
        {
            GD.PushWarning($"autostart: {e.Message}");
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled) key.SetValue(ValueName, Command());
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception e)
        {
            GD.PushWarning($"autostart: {e.Message}");
        }
    }

    /// <summary>Keeps the stored command pointing at the current executable (e.g. after a Godot update or export).</summary>
    public static void Refresh()
    {
        if (IsEnabled()) SetEnabled(true);
    }
}
