using System;
using System.Collections.Generic;
using Godot;
using Webi.World;

namespace Webi.UI;

/// <summary>
/// Sticky notes hanging on silk threads from the top of the screen, swaying a little. New notes take a free spot
/// on the right side of the primary monitor; they can be dragged anywhere (the thread follows) and only the trash
/// can in the corner takes them down; the pencil in the other corner edits the text. Drawn in world coordinates
/// (on every monitor's window), but saved in Windows desktop coordinates, which don't move when monitors change.
/// </summary>
public partial class StickyNotes : Node2D
{
    internal static readonly Color Silk = new(0.93f, 0.92f, 0.88f, 0.95f);

    internal const float Width = 150;
    public const int Max = 12;
    /// <summary>New notes are laid out in rows of this many, from the right of the primary monitor.</summary>
    const int Columns = 4;
    /// <summary>Shortest thread a note can hang from when dragged up.</summary>
    const float MinThread = 16;
    /// <summary>How far the cursor must move after grabbing a note before it counts as moving it (not a click).</summary>
    const float DragSlop = 4;

    readonly List<StickyCard> _cards = new();
    readonly RandomNumberGenerator _rng = new();
    float _time;
    StickyCard? _held;
    Vector2 _grab, _dragTo, _pressedAt;
    bool _moved;

    /// <summary>The notes shown (the saved list: positions and order are written back into it).</summary>
    public List<StickyNote> Notes = new();
    /// <summary>Work area of the primary monitor, where new notes go (set by Main).</summary>
    public Rect2 Bounds;
    /// <summary>Work area of the monitor at a point (set by Main).</summary>
    public Func<Vector2, Rect2> WorkAreaAt = null!;
    /// <summary>Windows desktop position of world (0, 0) (set by Main); notes are saved relative to the desktop.</summary>
    public Vector2 Origin;
    public Vector2 Cursor;
    public int PixelScale = 2;

    /// <summary>A note was moved: the saved state needs writing.</summary>
    public event Action? Moved;

    public bool Dragging => _held != null;

    /// <summary>Matches the cards to <see cref="Notes"/> after notes were added or removed.</summary>
    public void Refresh()
    {
        for (int i = _cards.Count - 1; i >= 0; i--)
            if (!Notes.Contains(_cards[i].Note))
            {
                if (_held == _cards[i]) _held = null;
                _cards[i].QueueFree();
                _cards.RemoveAt(i);
            }
        foreach (var note in Notes)
        {
            if (_cards.Find(c => c.Note == note) is { } shown)
            {
                shown.SyncText();
                continue;
            }
            var card = new StickyCard(note) { Phase = _rng.RandfRange(0, Mathf.Tau) };
            AddChild(card);
            _cards.Add(card);
        }
        _cards.Sort((a, b) => Notes.IndexOf(a.Note).CompareTo(Notes.IndexOf(b.Note)));
        for (int i = 0; i < _cards.Count; i++) MoveChild(_cards[i], i);
        // _Process stops redrawing once there are no cards, so the last frame would stay on screen.
        QueueRedraw();
    }

    /// <summary>The topmost note under the point.</summary>
    public StickyNote? NoteAt(Vector2 p) => CardAt(p)?.Note;

    /// <summary>The note whose trash can is under the point.</summary>
    public StickyNote? TrashAt(Vector2 p) => CardAt(p) is { } c && c.TrashRect.HasPoint(p) ? c.Note : null;

    /// <summary>The note whose pencil is under the point.</summary>
    public StickyNote? PencilAt(Vector2 p) => CardAt(p) is { } c && c.PencilRect.HasPoint(p) ? c.Note : null;

    StickyCard? CardAt(Vector2 p)
    {
        // Hidden (fullscreen app, Webi hidden): clicks there belong to whatever is underneath.
        if (!Visible) return null;
        for (int i = _cards.Count - 1; i >= 0; i--)
            if (_cards[i].Rect.HasPoint(p)) return _cards[i];
        return null;
    }

    /// <summary>Picks up the note under the cursor and brings it to the front (it only moves once the cursor does).</summary>
    public void StartDrag(Vector2 cursor)
    {
        if (CardAt(cursor) is not { } card) return;
        _held = card;
        // Grabbed where it is drawn (it may still be lowering or mid-sway), so it doesn't jump away from the cursor.
        card.Top = card.Rect.Position + new Vector2(card.Size.X / 2, 0);
        _grab = cursor - card.Top;
        _dragTo = card.Top;
        card.Age = 1; // done lowering
        _pressedAt = cursor;
        _moved = false;
        // Brought to the front: last in the saved order and drawn last.
        if (Notes[^1] != card.Note)
        {
            Notes.Remove(card.Note);
            Notes.Add(card.Note);
            Moved?.Invoke();
        }
        _cards.Remove(card);
        _cards.Add(card);
        MoveChild(card, -1);
    }

    public void DragTo(Vector2 cursor)
    {
        _moved |= cursor.DistanceTo(_pressedAt) > DragSlop;
        if (_moved) _dragTo = cursor - _grab;
    }

    public void EndDrag()
    {
        if (_held == null) return;
        // A plain click keeps the saved spot: the drawn one includes the sway, so saving it would creep sideways.
        if (_moved)
        {
            (_held.Note.X, _held.Note.Y) = (_held.Top.X + Origin.X, _held.Top.Y + Origin.Y);
            Moved?.Invoke();
        }
        _held = null;
    }

    /// <summary>Where a note hangs in world coordinates (its saved spot, kept inside the monitor it falls on); null if not placed yet.</summary>
    Vector2? Placed(StickyCard c, Vector2 size)
    {
        if (c.Note.X is not float x || c.Note.Y is not float y) return null;
        var saved = new Vector2(x, y) - Origin;
        return Clamp(saved, size, WorkAreaAt(saved));
    }

    /// <summary>Keeps a note (by its top center) inside a monitor's work area, below the top edge.</summary>
    static Vector2 Clamp(Vector2 top, Vector2 size, Rect2 area) => new(
            Mathf.Clamp(top.X, area.Position.X + size.X / 2, area.End.X - size.X / 2),
            Mathf.Clamp(top.Y, area.Position.Y + MinThread, Mathf.Max(area.Position.Y + MinThread, area.End.Y - size.Y)));

    /// <summary>
    /// First free spot for a new note: side by side from the right of the primary monitor, on threads of
    /// different lengths, skipping spots another note is already on.
    /// </summary>
    Vector2 FreeSpot(StickyCard card)
    {
        Vector2 Slot(int slot) => new(Bounds.End.X - 40 - Width / 2 - (slot % Columns) * (Width + 18),
            Bounds.Position.Y + Bounds.Size.Y * (0.18f + (slot % 2) * 0.04f) + slot / Columns * (StickyCard.MinHeight + 30));

        for (int slot = 0; ; slot++)
        {
            var top = Slot(slot);
            var rect = new Rect2(top - new Vector2(Width / 2, 0), new Vector2(Width, MinHeight(card)));
            // Past a few rows (or off the bottom) it gives up and stacks on the first spot.
            if (slot >= Columns * 5 || top.Y + MinHeight(card) > Bounds.End.Y) return Slot(0);
            // Compared with where the other notes are drawn (clamped), not their raw saved spots.
            if (!_cards.Exists(c => c != card && Placed(c, new Vector2(Width, MinHeight(c))) is Vector2 p
                    && new Rect2(p.X - Width / 2, p.Y, Width, MinHeight(c)).Intersects(rect))) return top;
        }
    }

    // A card not measured yet still takes at least the smallest note's height.
    static float MinHeight(StickyCard c) => Mathf.Max(StickyCard.MinHeight, c.Size.Y);

    public override void _Process(double delta)
    {
        if (_cards.Count == 0) return;
        float dt = (float)delta;
        _time += dt;
        foreach (var c in _cards)
        {
            c.Age += dt;
            // Measured every frame: a wrapped label only knows its line count once it has its width.
            c.Size = c.Measure();
            Rect2 area;
            if (c == _held)
            {
                // Kept on the monitor the cursor is on (the note's own top can fall in a gap between monitors).
                area = WorkAreaAt(Cursor);
                c.Top = Clamp(_dragTo, c.Size, area);
                c.Swing = 0;
            }
            else
            {
                if (c.Note.X == null || c.Note.Y == null)
                {
                    var spot = FreeSpot(c) + Origin;
                    (c.Note.X, c.Note.Y) = (spot.X, spot.Y);
                    Moved?.Invoke();
                }
                // Clamped again in case the monitors changed since it was put there.
                c.Top = Placed(c, c.Size)!.Value;
                area = WorkAreaAt(c.Top);
                // Starts swaying again little by little after being let go, instead of jumping sideways.
                c.Swing = Mathf.Min(1, c.Swing + dt / 1.5f);
            }
            c.Anchor = new Vector2(c.Top.X, area.Position.Y);
            // Lowered down on the thread when it appears.
            float lowered = Ease(Mathf.Min(1, c.Age / 0.8f));
            float sway = Mathf.Sin(_time * 1.3f + c.Phase) * 6 * c.Swing;
            var top = c.Anchor.Lerp(c.Top, lowered) + new Vector2(sway, 0);
            c.Rect = new Rect2((top - new Vector2(c.Size.X / 2, 0)).Round(), c.Size);
            c.Place();
        }
        // After every card has moved, so overlapping notes agree on which one is on top.
        var hovered = CardAt(Cursor);
        foreach (var c in _cards)
        {
            c.Hover = c == hovered && c.TrashRect.HasPoint(Cursor);
            c.PencilHover = c == hovered && c.PencilRect.HasPoint(Cursor);
        }
        QueueRedraw();
    }

    static float Ease(float t) => 1 - (1 - t) * (1 - t);

    public override void _Draw()
    {
        // Threads behind every note.
        int u = PixelTheme.U;
        foreach (var c in _cards)
            PixelDraw.Line(this, c.Anchor, new Vector2(c.Rect.GetCenter().X, c.Rect.Position.Y + u * 2), Silk, PixelScale);
    }
}

/// <summary>One sticky note: paper, text and the trash can, drawn in its own node so overlapping notes stack.</summary>
public partial class StickyCard : Node2D
{
    static readonly Color Paper = new("ffe98a");
    static readonly Color PaperShade = new("f2cf55");
    static readonly Color Ink = PixelTheme.Ink;
    static readonly Color TrashHover = new("e86a5c");
    static readonly Color PencilHoverColor = new("7cc4e8");

    /// <summary>Room above the text for the trash can and the pencil.</summary>
    const float TopBand = 26;
    /// <summary>Trash can, in 2-pixel dots.</summary>
    static readonly string[] Trash =
    {
        "..###..",
        "#######",
        ".......",
        ".#####.",
        ".#.#.#.",
        ".#.#.#.",
        ".#.#.#.",
        ".#####.",
    };
    /// <summary>Pencil (tip at the bottom left), in 2-pixel dots.</summary>
    static readonly string[] Pencil =
    {
        ".....##",
        "....#.#",
        "...###.",
        "..###..",
        ".###...",
        ".##....",
        "#......",
    };
    const int Dot = 2;

    public readonly StickyNote Note;
    readonly Label _label;
    public float Phase, Age;
    /// <summary>How much it sways (0 while held, back to 1 after being let go).</summary>
    public float Swing = 1;
    /// <summary>Where it hangs (top center) and where its thread is tied.</summary>
    public Vector2 Top, Anchor, Size;
    public Rect2 Rect;
    public bool Hover, PencilHover;

    public StickyCard(StickyNote note)
    {
        Note = note;
        _label = new Label
        {
            Text = note.Text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(StickyNotes.Width - 24, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _label.AddThemeFontOverride("font", PixelTheme.RoundFont(700));
        _label.AddThemeFontSizeOverride("font_size", 15);
        _label.AddThemeColorOverride("font_color", Ink);
        AddChild(_label);
    }

    /// <summary>Shows the note's text again after it was edited.</summary>
    public void SyncText()
    {
        if (_label.Text != Note.Text) _label.Text = Note.Text;
    }

    // Godot needs a parameterless constructor to recreate the node (hot reload); never used otherwise.
    public StickyCard() : this(new StickyNote()) { }

    public const float MinHeight = 80;

    public Vector2 Measure() => new(StickyNotes.Width, Mathf.Max(MinHeight, _label.GetCombinedMinimumSize().Y + TopBand + 18));

    /// <summary>The trash can in the top right corner (world coordinates), a little bigger than drawn to be easy to hit.</summary>
    public Rect2 TrashRect => new Rect2(Rect.End.X - PixelTheme.U * 2 - Trash[0].Length * Dot, Rect.Position.Y + PixelTheme.U * 2,
        Trash[0].Length * Dot, Trash.Length * Dot).Grow(4);

    /// <summary>The pencil in the top left corner (world coordinates), grown like the trash can.</summary>
    public Rect2 PencilRect => new Rect2(Rect.Position.X + PixelTheme.U * 2, Rect.Position.Y + PixelTheme.U * 2,
        Pencil[0].Length * Dot, Pencil.Length * Dot).Grow(4);

    public void Place()
    {
        Position = Rect.Position;
        _label.Position = new Vector2(12, TopBand);
        _label.Size = new Vector2(Size.X - 24, Size.Y - TopBand - 10);
        QueueRedraw();
    }

    public override void _Draw()
    {
        int u = PixelTheme.U;
        var r = new Rect2(Vector2.Zero, Size);
        var pin = new Vector2(r.GetCenter().X, u * 2);
        // Shadow, outline, paper, a shaded band at the bottom and a folded corner.
        DrawRect(new Rect2(new Vector2(u * 2, u * 2), r.Size), PixelTheme.Shadow);
        DrawRect(r, Ink);
        var inner = r.Grow(-u);
        DrawRect(inner, Paper);
        DrawRect(new Rect2(inner.Position.X, inner.End.Y - u * 2, inner.Size.X, u * 2), PaperShade);
        for (int k = 0; k < 4; k++)
            DrawRect(new Rect2(inner.End.X - (4 - k) * u, inner.End.Y - (k + 1) * u, (4 - k) * u, u), k == 0 ? Ink : PaperShade);
        // A blob of silk holding it.
        DrawRect(new Rect2(pin - new Vector2(u, u), new Vector2(u * 2, u * 2)), StickyNotes.Silk);

        DrawIcon(Trash, TrashRect, Hover, TrashHover);
        DrawIcon(Pencil, PencilRect, PencilHover, PencilHoverColor);
    }

    void DrawIcon(string[] dots, Rect2 rect, bool hover, Color hoverColor)
    {
        rect.Position -= Rect.Position;
        if (hover) PixelTheme.RoundRect(this, rect, 1, hoverColor);
        var origin = rect.Position + new Vector2(4, 4);
        for (int y = 0; y < dots.Length; y++)
            for (int x = 0; x < dots[y].Length; x++)
                if (dots[y][x] == '#')
                    DrawRect(new Rect2(origin + new Vector2(x * Dot, y * Dot), new Vector2(Dot, Dot)), Ink);
    }
}
