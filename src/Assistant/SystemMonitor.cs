using System;
using System.Runtime.InteropServices;
using Webi.Win32;

namespace Webi.Assistant;

public enum SystemAlert { CpuHot, CpuCalm, BatteryLow, Charging, MemoryHigh }

/// <summary>Samples CPU, memory and battery every couple of seconds and raises rate-limited alerts.</summary>
public sealed class SystemMonitor
{
    readonly Config _config;
    double _sampleTimer;
    ulong _lastIdle, _lastTotal;
    double _hotSeconds;
    bool _wasHot, _wasCharging = true;
    DateTime _lastCpuAlert = DateTime.MinValue, _lastBatteryAlert = DateTime.MinValue, _lastMemAlert = DateTime.MinValue;

    public double Cpu { get; private set; }
    public double Memory { get; private set; }
    public int Battery { get; private set; } = -1;
    public bool Charging { get; private set; }
    public bool HasBattery => Battery >= 0;

    public event Action<SystemAlert, Notice>? Alert;

    public SystemMonitor(Config config)
    {
        _config = config;
        // Memory and battery are known right away; CPU usage needs a second sample (two seconds later).
        Sample();
    }

    public void Tick(double dt)
    {
        _sampleTimer -= dt;
        if (_sampleTimer > 0) return;
        const double period = 2.0;
        _sampleTimer = period;
        Sample();
        if (!_config.SystemMonitor) return;

        var now = DateTime.Now;
        _hotSeconds = Cpu >= _config.CpuAlertPercent ? _hotSeconds + period : 0;
        if (_hotSeconds >= 20 && !_wasHot && now - _lastCpuAlert > TimeSpan.FromMinutes(10))
        {
            _wasHot = true;
            _lastCpuAlert = now;
            Alert?.Invoke(SystemAlert.CpuHot, CpuHotNotice()!.Value);
        }
        else if (_wasHot && Cpu < _config.CpuAlertPercent - 25)
        {
            _wasHot = false;
            Alert?.Invoke(SystemAlert.CpuCalm, new Notice("", "Ahh, esfriou. Bem melhor.", Shout: false, Happy: true));
        }

        if (Memory >= MemoryAlertPercent && now - _lastMemAlert > TimeSpan.FromMinutes(15))
        {
            _lastMemAlert = now;
            Alert?.Invoke(SystemAlert.MemoryHigh, MemoryNotice()!.Value);
        }

        if (HasBattery)
        {
            if (!Charging && Battery <= _config.BatteryAlertPercent && now - _lastBatteryAlert > TimeSpan.FromMinutes(15))
            {
                _lastBatteryAlert = now;
                Alert?.Invoke(SystemAlert.BatteryLow, BatteryNotice()!.Value);
            }
            if (Charging && !_wasCharging)
                Alert?.Invoke(SystemAlert.Charging, new Notice("", "Carregando! Valeu! ⚡", Shout: false, Happy: true));
            _wasCharging = Charging;
        }
    }

    // The alerts are rebuilt by their own Recheck right before being shouted (they may have waited in the queue
    // through a meeting): fresh numbers, or null once the problem is gone. Thresholds have some slack so a
    // reading that hovers around the limit doesn't drop the alert.
    const double MemoryAlertPercent = 92;

    Notice? CpuHotNotice() => _wasHot && _config.SystemMonitor
        ? new Notice("TÁ QUENTE AQUI!!! 🔥", $"A CPU tá em {Cpu:0}% faz um tempinho.", Key: "cpu", Recheck: CpuHotNotice)
        : null;

    Notice? MemoryNotice() => Memory >= MemoryAlertPercent - 5 && _config.SystemMonitor
        ? new Notice("MEMÓRIA LOTADA!!! 🧠", $"Tá em {Memory:0}%... minha teia não aguenta tanta coisa!", Key: "memory", Recheck: MemoryNotice)
        : null;

    Notice? BatteryNotice() => HasBattery && !Charging && Battery <= _config.BatteryAlertPercent + 5 && _config.SystemMonitor
        ? new Notice("BATERIA FRACA!!! 🔋", $"Só {Battery}%! Pluga o carregador, por favor?", Key: "battery", Recheck: BatteryNotice)
        : null;

    void Sample()
    {
        if (Native.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            ulong idleT = idle.Value, total = kernel.Value + user.Value; // kernel time includes idle
            if (_lastTotal != 0 && total > _lastTotal)
                Cpu = 100.0 * (1.0 - (double)(idleT - _lastIdle) / (total - _lastTotal));
            _lastIdle = idleT;
            _lastTotal = total;
        }

        var mem = new Native.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<Native.MEMORYSTATUSEX>() };
        if (Native.GlobalMemoryStatusEx(ref mem)) Memory = mem.dwMemoryLoad;

        if (Native.GetSystemPowerStatus(out var power))
        {
            bool noBattery = (power.BatteryFlag & 128) != 0 || power.BatteryLifePercent == 255;
            Battery = noBattery ? -1 : power.BatteryLifePercent;
            Charging = power.ACLineStatus == 1;
        }
    }

    public string Describe()
    {
        // Desktops have no battery: then it isn't mentioned at all.
        var battery = HasBattery ? $", bateria em {Battery}%{(Charging ? " (carregando)" : "")}" : "";
        return $"CPU em {Cpu:0}%, memória em {Memory:0}%{battery}. Agora são {DateTime.Now:HH:mm}.";
    }
}
