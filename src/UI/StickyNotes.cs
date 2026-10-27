using System;
using System.Collections.Generic;
using Godot;
using Webi.World;

namespace Webi.UI;

/// <summary>
/// Sticky notes hanging on silk threads from the top of the screen, on the right side of the primary monitor,
/// swaying a little. World coordinates (drawn on every monitor's window). Clicking a note takes it down.
/// </summary>
public partial class StickyNotes : Node2D
{
    static readonly Color Paper = new("ffe98a");
    static readonly Color PaperShade = new("f2cf55");
    static readonly Color Ink = PixelTheme.Ink;
    static readonly Color Silk = new(0.93f, 0.92f, 0.88f, 0.95f);

    const float Width = 150;
    public const int Max = 4;

    sealed class Card
    {
        public Label Label = null!;
        public Vector2 Anchor, Size;
        public float Phase, Age;
        public Rect2 Rect;
    }

    readonly List<Card> _cards = new();
    readonly RandomNumberGenerator _rng = new();
    float _time;

    /// <summary>Work area of the primary monitor (set by Main).</summary>
    public Rect2 Bounds;
    public int PixelScale = 2;

    /// <summary>Replaces the notes shown with <paramref name="texts"/>.</summary>
    public void SetNotes(IReadOnlyList<string> texts)
    {
        foreach (var c in _cards) c.Label.QueueFree();
        _cards.Clear();
        for (int i = 0; i < texts.Count && i < Max; i++)
        {
            var label = new Label
            {
                Text = texts[i],
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(Width - 24, 0),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            label.AddThemeFontOverride("font", PixelTheme.RoundFont(700));
            label.AddThemeFontSizeOverride("font_size", 15);
            label.AddThemeColorOverride("font_color", Ink);
            AddChild(label);
            _cards.Add(new Card { Label = label, Phase = _rng.RandfRange(0, Mathf.Tau), Age = 0 });
        }
        Layout();
    }

    void Layout()
    {
        for (int i = 0; i < _cards.Count; i++)
        {
            var c = _cards[i];
            c.Label.ResetSize();
            var text = c.Label.GetCombinedMinimumSize();
            c.Size = new Vector2(Width, Mathf.Max(70, text.Y + 30));
            // Side by side from the right, on threads of different lengths; well below the title bars.
            float x = Bounds.End.X - 40 - Width / 2 - i * (Width + 18);
            c.Anchor = new Vector2(x, Bounds.Position.Y);
        }
    }

    /// <summary>Index of the note under the point, or -1.</summary>
    public int NoteAt(Vector2 p)
    {
        for (int i = 0; i < _cards.Count; i++)
            if (_cards[i].Rect.HasPoint(p)) return i;
        return -1;
    }

    public override void _Process(double delta)
    {
        if (_cards.Count == 0) return;
        float dt = (float)delta;
        _time += dt;
        for (int i = 0; i < _cards.Count; i++)
        {
            var c = _cards[i];
            c.Age += dt;
            float drop = Bounds.Size.Y * (0.28f + (i % 2) * 0.06f);
            // Lowered down on the thread when it appears.
            float lowered = Mathf.Min(1, c.Age / 0.8f);
            float sway = Mathf.Sin(_time * 1.3f + c.Phase) * 6;
            var top = c.Anchor + new Vector2(sway, drop * Ease(lowered));
            var pos = (top - new Vector2(c.Size.X / 2, 0)).Round();
            c.Rect = new Rect2(pos, c.Size);
            c.Label.Position = pos + new Vector2(12, 18);
            c.Label.Size = new Vector2(c.Size.X - 24, c.Size.Y - 26);
        }
        QueueRedraw();
    }

    static float Ease(float t) => 1 - (1 - t) * (1 - t);

    public override void _Draw()
    {
        int u = PixelTheme.U;
        foreach (var c in _cards)
        {
            var r = c.Rect;
            var pin = new Vector2(r.GetCenter().X, r.Position.Y + u * 2);
            PixelDraw.Line(this, c.Anchor, pin, Silk, PixelScale);
            // Shadow, outline, paper, a shaded band at the bottom and a folded corner.
            DrawRect(new Rect2(r.Position + new Vector2(u * 2, u * 2), r.Size), PixelTheme.Shadow);
            DrawRect(r, Ink);
            var inner = r.Grow(-u);
            DrawRect(inner, Paper);
            DrawRect(new Rect2(inner.Position.X, inner.End.Y - u * 2, inner.Size.X, u * 2), PaperShade);
            for (int k = 0; k < 4; k++)
                DrawRect(new Rect2(inner.End.X - (4 - k) * u, inner.End.Y - (k + 1) * u, (4 - k) * u, u), k == 0 ? Ink : PaperShade);
            // A blob of silk holding it.
            DrawRect(new Rect2(pin - new Vector2(u, u), new Vector2(u * 2, u * 2)), Silk);
        }
    }
}
