using Godot;
using Webi.Assistant;

namespace Webi.UI;

/// <summary>
/// The comically huge "shout" bubble used for alerts: a jagged starburst with a giant headline and a smaller
/// detail line. It pops in (overshooting), shakes, and wobbles while it is up. World coordinates, like
/// <see cref="SpeechBubble"/>, so it shows on the monitor the spider is on.
/// </summary>
public partial class ShoutBubble : Control
{
    static readonly Color Burst = new("fff3c4");
    static readonly Color Ink = PixelTheme.Ink;
    static readonly Color Shout = new("d6331f");

    readonly Label _title = new();
    readonly Label _detail = new();
    readonly VBoxContainer _box = new();
    float _time, _hideTimer;
    Vector2[] _burst = System.Array.Empty<Vector2>();
    readonly RandomNumberGenerator _rng = new();

    public Vector2 Target;
    public Rect2 Bounds;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;

        // Same rounded system font as the speech bubble, extra bold for the shout.
        _title.AddThemeFontOverride("font", PixelTheme.RoundFont(weight: 900));
        _title.AddThemeFontSizeOverride("font_size", 64);
        _title.AddThemeColorOverride("font_color", Shout);
        _title.AddThemeColorOverride("font_outline_color", Ink);
        _title.AddThemeConstantOverride("outline_size", 10);
        _title.HorizontalAlignment = HorizontalAlignment.Center;

        _detail.AddThemeFontOverride("font", PixelTheme.RoundFont());
        _detail.AddThemeFontSizeOverride("font_size", 22);
        _detail.AddThemeColorOverride("font_color", Ink);
        _detail.HorizontalAlignment = HorizontalAlignment.Center;
        _detail.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _detail.CustomMinimumSize = new Vector2(420, 0);

        _box.AddThemeConstantOverride("separation", 4);
        _box.AddChild(_title);
        _box.AddChild(_detail);
        AddChild(_box);
    }

    public void Show(Notice notice, float seconds)
    {
        _title.Text = notice.Title;
        _detail.Text = notice.Detail;
        _detail.Visible = notice.Detail.Length > 0;
        _box.ResetSize();
        var content = _box.GetCombinedMinimumSize();
        // Room around the text for the spikes.
        var pad = new Vector2(90, 70);
        Size = content + pad * 2;
        _box.Position = pad;
        _box.Size = content;
        PivotOffset = Size / 2;
        BuildBurst();
        _time = 0;
        _hideTimer = seconds;
        Visible = true;
    }

    /// <summary>A jagged starburst around the text, snapped to a coarse grid so it reads as pixel art.</summary>
    void BuildBurst()
    {
        int spikes = 22;
        var c = Size / 2;
        var r = Size / 2;
        _burst = new Vector2[spikes * 2];
        for (int i = 0; i < spikes * 2; i++)
        {
            float a = i / (float)(spikes * 2) * Mathf.Tau;
            float k = i % 2 == 0 ? _rng.RandfRange(0.98f, 1.12f) : _rng.RandfRange(0.74f, 0.82f);
            var p = c + new Vector2(Mathf.Cos(a) * r.X, Mathf.Sin(a) * r.Y) * k;
            _burst[i] = (p / 4).Round() * 4;
        }
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        float dt = (float)delta;
        _time += dt;
        _hideTimer -= dt;
        if (_hideTimer <= 0) { Visible = false; return; }

        // Pop in with an overshoot, then a slow wobble; shake hard at first.
        float pop = _time < 0.12f ? Mathf.Lerp(0.2f, 1.18f, _time / 0.12f)
            : _time < 0.25f ? Mathf.Lerp(1.18f, 1f, (_time - 0.12f) / 0.13f)
            : 1f + Mathf.Sin(_time * 6) * 0.02f;
        Scale = new Vector2(pop, pop);
        float shake = _time < 0.8f ? 7 * (1 - _time / 0.8f) : 1.5f;
        var jitter = new Vector2(_rng.RandfRange(-shake, shake), _rng.RandfRange(-shake, shake));
        Rotation = Mathf.Sin(_time * 3.1f) * 0.03f;

        // Above the spider when there is room, otherwise below; kept inside the monitor.
        var pos = new Vector2(Target.X - Size.X / 2, Target.Y - Size.Y - 30);
        if (pos.Y < Bounds.Position.Y + 8) pos.Y = Target.Y + 120;
        pos.X = Mathf.Clamp(pos.X, Bounds.Position.X + 8, Bounds.End.X - Size.X - 8);
        pos.Y = Mathf.Clamp(pos.Y, Bounds.Position.Y + 8, Bounds.End.Y - Size.Y - 8);
        Position = (pos + jitter).Round();
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_burst.Length == 0) return;
        // Chunky shadow, fill, thick ink outline.
        var shadow = new Vector2[_burst.Length];
        for (int i = 0; i < _burst.Length; i++) shadow[i] = _burst[i] + new Vector2(8, 8);
        DrawColoredPolygon(shadow, new Color(0, 0, 0, 0.3f));
        DrawColoredPolygon(_burst, Burst);
        var outline = new Vector2[_burst.Length + 1];
        _burst.CopyTo(outline, 0);
        outline[^1] = _burst[0];
        DrawPolyline(outline, Ink, 6, false);
    }
}
