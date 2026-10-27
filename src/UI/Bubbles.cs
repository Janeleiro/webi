using Godot;

namespace Webi.UI;

/// <summary>Shared pixel-art look for the bubbles.</summary>
public static class PixelTheme
{
    public static readonly Color Paper = new("fff8ec");
    public static readonly Color PaperShade = new("f1dfc6");
    public static readonly Color Ink = new("3a2418");
    public static readonly Color Shadow = new(0.1f, 0.05f, 0.02f, 0.28f);

    /// <summary>One "pixel" of the bubble art, in screen pixels.</summary>
    public const int U = 3;

    /// <summary>A soft, rounded system font (no font file to ship).</summary>
    public static Font RoundFont(int weight = 600) => new SystemFont
    {
        FontNames = new[] { "Segoe UI Variable Text", "Segoe UI", "Arial" },
        FontWeight = weight,
    };

    /// <summary>
    /// Rectangle with pixel-stepped rounded corners (<paramref name="radius"/> in art pixels), drawn as
    /// horizontal bands so it stays on the pixel grid.
    /// </summary>
    public static void RoundRect(CanvasItem canvas, Rect2 r, int radius, Color color)
    {
        for (int i = 0; i < radius; i++)
        {
            float dy = radius - i - 0.5f;
            float inset = Mathf.Max(0, Mathf.Ceil(radius - Mathf.Sqrt(radius * radius - dy * dy) - 0.2f)) * U;
            canvas.DrawRect(new Rect2(r.Position.X + inset, r.Position.Y + i * U, r.Size.X - 2 * inset, U), color);
            canvas.DrawRect(new Rect2(r.Position.X + inset, r.End.Y - (i + 1) * U, r.Size.X - 2 * inset, U), color);
        }
        canvas.DrawRect(new Rect2(r.Position.X, r.Position.Y + radius * U, r.Size.X, r.Size.Y - 2 * radius * U), color);
    }
}

/// <summary>
/// Speech bubble with typewriter effect that follows the spider (world coordinates): pixel-art rounded
/// body with a chunky outline, highlight and shade bands, a drop shadow and a stepped tail pointing at the
/// spider. Pops in from the tail and floats gently while up.
/// </summary>
public partial class SpeechBubble : PanelContainer
{
    const int Radius = 5;

    readonly Label _label = new();
    float _shown;
    float _hideTimer;
    float _age;
    /// <summary>World point the tail points at (above the spider's head).</summary>
    public Vector2 Target;
    public Rect2 Bounds;

    /// <summary>True while the typewriter effect is still revealing text.</summary>
    public bool IsTyping => Visible && _shown < _label.GetTotalCharacterCount();

    public override void _Ready()
    {
        // The body is drawn by hand in _Draw; the empty style box only sets the padding around the text.
        AddThemeStyleboxOverride("panel", new StyleBoxEmpty
        {
            ContentMarginLeft = 20, ContentMarginRight = 20, ContentMarginTop = 12, ContentMarginBottom = 14,
        });
        _label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _label.AddThemeFontOverride("font", PixelTheme.RoundFont());
        _label.AddThemeFontSizeOverride("font_size", 16);
        _label.AddThemeColorOverride("font_color", PixelTheme.Ink);
        _label.AddThemeConstantOverride("line_spacing", 1);
        AddChild(_label);
        Visible = false;
        // Lives in the shared world canvas (drawn on every monitor's window); clicks are handled by Main.
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Show(string text, float seconds)
    {
        _shown = 0;
        _age = 0;
        _label.Text = text;
        _label.VisibleCharacters = 0;
        // Long text gets a wider bubble and more reading time.
        float width = Mathf.Clamp(text.Length * 7.8f, 70, 300);
        _label.CustomMinimumSize = new Vector2(width, 0);
        Size = Vector2.Zero; // shrink-wrap to the new text
        _hideTimer = Mathf.Max(seconds, 2.5f + text.Length * 0.055f);
        Visible = true;
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        float dt = (float)delta;
        _age += dt;
        int total = _label.GetTotalCharacterCount();
        if (_shown < total)
        {
            _shown = Mathf.Min(total, _shown + dt * 55);
            _label.VisibleCharacters = (int)_shown;
        }
        else
        {
            _hideTimer -= dt;
            if (_hideTimer <= 0) Visible = false;
        }

        // Snap the size to the art grid so the outline steps line up.
        var snapped = (Size / PixelTheme.U).Ceil() * PixelTheme.U;
        if (snapped != Size) Size = snapped;

        float bob = Mathf.Round(Mathf.Sin(_age * 2.2f) * 1.5f);
        Position = Place(Target, Size, Bounds) + new Vector2(0, bob);

        // Elastic pop from the tail.
        float pop = _age < 0.1f ? Mathf.Lerp(0.5f, 1.08f, _age / 0.1f)
            : _age < 0.2f ? Mathf.Lerp(1.08f, 1f, (_age - 0.1f) / 0.1f)
            : 1f;
        var (tailX, below) = Tail();
        PivotOffset = new Vector2(tailX, below ? 0 : Size.Y);
        Scale = new Vector2(pop, pop);
        QueueRedraw();
    }

    /// <summary>Above the target when there is room, otherwise below; clamped to the screen.</summary>
    public static Vector2 Place(Vector2 target, Vector2 size, Rect2 bounds)
    {
        var pos = new Vector2(target.X - size.X / 2, target.Y - size.Y - 22);
        if (pos.Y < bounds.Position.Y + 4) pos.Y = target.Y + 90;
        pos.X = Mathf.Clamp(pos.X, bounds.Position.X + 4, bounds.End.X - size.X - 4);
        pos.Y = Mathf.Clamp(pos.Y, bounds.Position.Y + 4, bounds.End.Y - size.Y - 4);
        return pos.Round();
    }

    /// <summary>Where the tail sits along the bubble (local x, on the art grid) and whether it points up.</summary>
    (float x, bool up) Tail()
    {
        var local = Target - Position;
        int u = PixelTheme.U;
        float x = Mathf.Clamp(local.X, (Radius + 4) * u, Size.X - (Radius + 4) * u);
        return (Mathf.Round(x / u) * u, local.Y < 0);
    }

    public override void _Draw()
    {
        int u = PixelTheme.U;
        var body = new Rect2(Vector2.Zero, Size);
        var inner = body.Grow(-u);
        var (tailX, up) = Tail();
        // Lean the tail a little toward the spider when it is off to one side.
        int lean = Mathf.Sign(Mathf.Round((Target.X - Position.X - tailX) / 40f));

        // Drop shadow.
        PixelTheme.RoundRect(this, new Rect2(body.Position + new Vector2(u, 2 * u), body.Size), Radius, PixelTheme.Shadow);
        DrawTail(tailX + u, up, lean, PixelTheme.Shadow, null, 2 * u);

        // Ink outline, then the paper body on top of it.
        PixelTheme.RoundRect(this, body, Radius, PixelTheme.Ink);
        DrawTail(tailX, up, lean, PixelTheme.Ink, null, 0);
        PixelTheme.RoundRect(this, inner, Radius - 1, PixelTheme.Paper);

        // Shade band at the bottom and a bright highlight at the top give it some volume.
        float bandX = inner.Position.X + Radius * u, bandW = inner.Size.X - 2 * Radius * u;
        DrawRect(new Rect2(inner.Position.X + (Radius - 1) * u, inner.End.Y - u, inner.Size.X - 2 * (Radius - 1) * u, u), PixelTheme.PaperShade);
        DrawRect(new Rect2(bandX, inner.Position.Y + u, bandW * 0.55f, u), new Color(1, 1, 1, 0.9f));
        DrawRect(new Rect2(bandX + bandW * 0.55f + 2 * u, inner.Position.Y + u, u, u), new Color(1, 1, 1, 0.9f));

        // Paper inside the tail, overwriting the outline where it joins the body.
        DrawTail(tailX, up, lean, null, PixelTheme.Paper, 0);
    }

    /// <summary>A stepped wedge: rows 7, 5, 3, 2, 1 art pixels wide, outlined, leaning toward the spider.</summary>
    void DrawTail(float x, bool up, int lean, Color? ink, Color? paper, float yOffset)
    {
        int u = PixelTheme.U;
        int[] widths = { 7, 5, 3, 2, 1 };
        for (int row = 0; row < widths.Length; row++)
        {
            float y = up ? -(row + 1) * u + u : Size.Y - u + row * u;
            y += up ? -yOffset : yOffset;
            float cx = x + lean * row * u;
            float w = widths[row] * u;
            if (ink is Color i) DrawRect(new Rect2(cx - w / 2 - u, y, w + 2 * u, u), i);
            if (ink is Color i2 && row == widths.Length - 1)
                DrawRect(new Rect2(cx - w / 2, y + (up ? -u : u), w, u), i2); // tip
            if (paper is Color p && row < widths.Length - 1) DrawRect(new Rect2(cx - w / 2, y, w, u), p);
        }
        if (paper is Color pb)
        {
            // The first row of paper sits on the body's outline row: open the joint.
            float jointY = up ? 0 : Size.Y - u;
            DrawRect(new Rect2(x - widths[0] * u / 2f, jointY, widths[0] * u, u), pb);
        }
    }
}
