using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using Godot;
using Webi.Spider;
using Webi.Win32;

namespace Webi.Assistant;

/// <summary>
/// Things happening on the PC that Webi reacts to: a USB drive plugged in or out, the internet dropping or coming
/// back, the sound being muted. Checked in the background every few seconds. Nothing is stored.
/// Screenshots get no reaction on purpose: the line would show up in the picture, or replace the line that was
/// the reason for taking it.
/// </summary>
public sealed class SystemEvents
{
    readonly RandomNumberGenerator _rng = new();
    double _probeTimer = 3;
    /// <summary>The drives / network / sound check running in the background (they can take tens of ms).</summary>
    Task<(HashSet<string> drives, bool online, bool? muted)>? _probe;
    HashSet<string>? _drives;
    bool? _online, _muted;
    /// <summary>Internet state seen on the previous check: a change must hold for two checks in a row.</summary>
    bool? _pendingOnline;

    public bool Enabled { get; set; } = true;

    public event Action<Reaction>? Happened;

    public void Tick(double dt)
    {
        if (!Enabled)
        {
            // Forget what was seen: turned back on, the first check only sets the starting point again.
            _probe = null;
            _drives = null;
            _online = _muted = _pendingOnline = null;
            return;
        }

        if (_probe is { IsCompleted: true } done)
        {
            _probe = null;
            if (done.IsCompletedSuccessfully) Compare(done.Result);
        }
        if (_probe == null && (_probeTimer -= dt) <= 0)
        {
            _probeTimer = 4;
            _probe = Task.Run(() => (RemovableDrives(), Online(), AudioMute.IsMuted()));
        }
    }

    void Compare((HashSet<string> drives, bool online, bool? muted) now)
    {
        if (_drives != null)
        {
            if (now.drives.Except(_drives).Any()) Raise(Pick(Lines.UsbIn), Face.Surprised, Gesture.Tap);
            else if (_drives.Except(now.drives).Any()) Raise(Pick(Lines.UsbOut), Face.Normal, Gesture.Wave);
        }
        _drives = now.drives;

        if (_online == null) _online = now.online;
        else if (now.online != _online)
        {
            if (_pendingOnline == now.online)
            {
                _online = now.online;
                _pendingOnline = null;
                if (now.online) Raise(Pick(Lines.NetBack), Face.Happy, Gesture.Dance);
                else Raise(Pick(Lines.NetDown), Face.Surprised, Gesture.Tap);
            }
            else _pendingOnline = now.online;
        }
        else _pendingOnline = null;

        if (now.muted != null && _muted != null && now.muted != _muted)
            Raise(now.muted.Value ? Pick(Lines.Muted) : Pick(Lines.Unmuted), now.muted.Value ? Face.Sad : Face.Happy, Gesture.None);
        if (now.muted != null) _muted = now.muted;
    }

    void Raise(string text, Face face, Gesture gesture) => Happened?.Invoke(new Reaction(text, face, gesture));

    string Pick(string[] lines) => lines[_rng.RandiRange(0, lines.Length - 1)];

    static HashSet<string> RemovableDrives()
    {
        try
        {
            return DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Removable && d.IsReady).Select(d => d.Name).ToHashSet();
        }
        catch (Exception)
        {
            return new HashSet<string>();
        }
    }

    /// <summary>A real network adapter is up with a default gateway (virtual and loopback ones don't count).</summary>
    static bool Online()
    {
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var d = nic.Description.ToLowerInvariant();
                if (d.Contains("virtual") || d.Contains("hyper-v") || d.Contains("vpn") || d.Contains("loopback")) continue;
                if (nic.GetIPProperties().GatewayAddresses.Any(g => !g.Address.Equals(System.Net.IPAddress.Any) && !g.Address.ToString().StartsWith("0.")))
                    return true;
            }
        }
        catch (Exception)
        {
            return true; // can't tell: don't cry wolf
        }
        return false;
    }
}
