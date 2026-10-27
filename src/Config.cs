using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace Webi;

/// <summary>User settings, persisted as JSON in the Godot user dir (%APPDATA%\Godot\app_userdata\Webi\config.json).</summary>
public sealed class Config
{
    /// <summary>Screen pixels per art pixel.</summary>
    public int Scale { get; set; } = 2;

    public string Name { get; set; } = "Webi";

    /// <summary>Coat colour id (see FurColors): marrom, preto, branco, cinza, vermelho, amarelo, laranja, verde, azul, roxo, rosa.</summary>
    public string FurColor { get; set; } = "marrom";

    public bool WaterReminder { get; set; } = true;
    public int WaterMinutes { get; set; } = 45;
    public bool BreakReminder { get; set; } = true;
    public int BreakMinutes { get; set; } = 60;
    public int PomodoroWorkMinutes { get; set; } = 25;
    public int PomodoroBreakMinutes { get; set; } = 5;

    public bool SystemMonitor { get; set; } = true;

    /// <summary>Weave webs in corners on its own (the menu can always ask for one).</summary>
    public bool AutoWebs { get; set; } = true;
    public int MaxWebs { get; set; } = 5;
    /// <summary>Comment on the app / site in the foreground (after a minute in it).</summary>
    public bool AppComments { get; set; } = true;

    /// <summary>Minutes with the mouse still before Webi webs over the whole monitor (0 = never).</summary>
    public int ScreenWebMinutes { get; set; } = 5;
    public int CpuAlertPercent { get; set; } = 90;
    public int BatteryAlertPercent { get; set; } = 20;

    /// <summary>Minutes without keyboard/mouse input before Webi falls asleep.</summary>
    public int SleepAfterIdleMinutes { get; set; } = 5;
    /// <summary>Hide while a fullscreen app (game, video) is in the foreground.</summary>
    public bool HideOnFullscreen { get; set; } = true;

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string FilePath => ProjectSettings.GlobalizePath("user://config.json");

    /// <summary>
    /// Why the file couldn't be read (invalid JSON), or null. The defaults are used meanwhile and nothing is
    /// saved, so the file the user is fixing isn't overwritten.
    /// </summary>
    [JsonIgnore]
    public string? LoadError { get; private set; }

    public static Config Load()
    {
        if (!File.Exists(FilePath))
        {
            var fresh = new Config();
            fresh.Save();
            return fresh;
        }
        try
        {
            var config = JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath), Json) ?? new Config();
            config.Validate();
            return config;
        }
        catch (Exception e)
        {
            // The file is left untouched so a typo doesn't cost the user their settings.
            GD.PushWarning($"config.json inválido, usando padrões: {e.Message}");
            return new Config { LoadError = e.Message };
        }
    }

    /// <summary>Brings hand-edited values back into a usable range (a 0-minute reminder would fire every frame).</summary>
    public void Validate()
    {
        Scale = Math.Clamp(Scale, 1, 8);
        WaterMinutes = Math.Clamp(WaterMinutes, 1, 24 * 60);
        BreakMinutes = Math.Clamp(BreakMinutes, 1, 24 * 60);
        PomodoroWorkMinutes = Math.Clamp(PomodoroWorkMinutes, 1, 240);
        PomodoroBreakMinutes = Math.Clamp(PomodoroBreakMinutes, 1, 120);
        MaxWebs = Math.Clamp(MaxWebs, 0, 50);
        ScreenWebMinutes = Math.Clamp(ScreenWebMinutes, 0, 24 * 60);
        CpuAlertPercent = Math.Clamp(CpuAlertPercent, 30, 100);
        BatteryAlertPercent = Math.Clamp(BatteryAlertPercent, 1, 99);
        SleepAfterIdleMinutes = Math.Clamp(SleepAfterIdleMinutes, 1, 24 * 60);
    }

    public void Save()
    {
        if (LoadError != null) return;
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception e)
        {
            GD.PushWarning($"não consegui salvar config.json: {e.Message}");
        }
    }
}
