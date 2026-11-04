using System;
using System.Globalization;

namespace Webi;

/// <summary>
/// Debug switches from environment variables, read once at startup (they don't change while running and
/// several are checked every frame).
/// </summary>
public static class DebugEnv
{
    /// <summary>WEBI_DEBUG: prints the spider's state every second and adds debug items to the menu.</summary>
    public static readonly bool Debug = Has("WEBI_DEBUG");
    /// <summary>WEBI_DEMO: plays every animation in order ("web" starts with a web).</summary>
    public static readonly string? Demo = Get("WEBI_DEMO");
    /// <summary>WEBI_GAIT_LOG: prints which legs are in the air while walking.</summary>
    public static readonly bool GaitLog = Has("WEBI_GAIT_LOG");
    /// <summary>WEBI_SNAPSHOT: folder where rig frames are saved (see Spider.Snapshot).</summary>
    public static readonly string? SnapshotDir = Get("WEBI_SNAPSHOT");
    public static readonly int SnapshotMax = int.TryParse(Get("WEBI_SNAPSHOT_MAX"), out var m) ? m : 40;
    public static readonly float SnapshotFps =
        float.TryParse(Get("WEBI_SNAPSHOT_FPS"), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) && f > 0 ? f : 2;

    static string? Get(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;
    static bool Has(string name) => Environment.GetEnvironmentVariable(name) != null;
}
