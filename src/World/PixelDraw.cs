using System;
using Godot;

namespace Webi.World;

/// <summary>Pixel-art primitives drawn on the art-pixel grid (one art pixel = <c>scale</c> screen pixels).</summary>
public static class PixelDraw
{
    /// <summary>Bresenham line on the art-pixel grid.</summary>
    public static void Line(CanvasItem canvas, Vector2 a, Vector2 b, Color color, int scale)
    {
        int s = scale;
        int x0 = Mathf.RoundToInt(a.X / s), y0 = Mathf.RoundToInt(a.Y / s);
        int x1 = Mathf.RoundToInt(b.X / s), y1 = Mathf.RoundToInt(b.Y / s);
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
        int guard = 0;
        while (guard++ < 4000)
        {
            canvas.DrawRect(new Rect2(x0 * s - s / 2, y0 * s - s / 2, s, s), color);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }
}
