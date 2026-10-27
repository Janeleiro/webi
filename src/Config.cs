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

    public static Config Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath), Json) ?? new Config();
        }
        catch (Exception e)
        {
            GD.PushWarning($"config.json inválido, usando padrões: {e.Message}");
        }
        var config = new Config();
        config.Save();
        return config;
    }

    public void Save()
    {
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
