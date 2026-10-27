using System;
using System.Threading;

namespace Webi.Win32;

/// <summary>Keeps a second Webi (autostart plus a manual start, say) from opening another spider.</summary>
public static class SingleInstance
{
    const string Name = @"Local\Webi.SingleInstance";
    static Mutex? _mutex;

    /// <param name="wait">How long to wait for a previous instance to quit (when restarting itself).</param>
    /// <returns>False if another instance is running.</returns>
    public static bool Acquire(TimeSpan wait)
    {
        _mutex = new Mutex(false, Name);
        try
        {
            return _mutex.WaitOne(wait);
        }
        catch (AbandonedMutexException)
        {
            return true; // the previous instance died without releasing it: it's ours now
        }
    }
}
