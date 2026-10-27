using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Webi.World;

/// <summary>Which edge of a rectangle a surface came from.</summary>
public enum EdgeSide { Top, Right, Bottom, Left }

/// <summary>
/// A walkable, axis-aligned edge. Walking from A to B is "forward"; <see cref="Normal"/> points away
/// from the solid (where the spider stands). Forward is always the normal rotated 90° clockwise, so
/// consecutive edges of a rectangle chain A→B→next.A like a conveyor belt around it.
/// </summary>
public sealed class Surface
{
    public Vector2 A, B, Normal;
    /// <summary>Window handle the edge belongs to, 0 for the screen borders.</summary>
    public long Owner;
    public EdgeSide Side;
    /// <summary>Distance from the original (unclipped) edge start to <see cref="A"/>.</summary>
    public float StartOffset;
    public Surface? Prev, Next;

    public float Length => A.DistanceTo(B);
    public Vector2 Tangent => (B - A).Normalized();
    public Vector2 PointAt(float s) => A + Tangent * s;
    public bool IsFloorLike => Normal.Y < -0.5f;
    public bool IsCeilingLike => Normal.Y > 0.5f;
    public bool IsWall => Mathf.Abs(Normal.X) > 0.5f;

    public float Project(Vector2 p) => Mathf.Clamp((p - A).Dot(Tangent), 0, Length);

    public override string ToString() => $"{Owner:X}/{Side} {A}->{B}";
}

/// <summary>A visible top-level window as seen by the scanner (world coordinates).</summary>
/// <param name="CoversMonitor">Fills a whole monitor (fullscreen): it does not hide that monitor's borders.</param>
public readonly record struct DesktopWindow(long Handle, Rect2 Rect, string Title, bool CoversMonitor = false);

/// <summary>
/// Turns the window stack into walkable edges: every window contributes its 4 outer edges, and the outline of
/// all monitors' work areas contributes the inner borders (floors = taskbar tops; where two monitors meet,
/// the border opens up so the spider can cross). Parts of an edge hidden under a window that is higher in the
/// Z-order are cut away, so the spider never walks on something you cannot see.
/// </summary>
public sealed class SurfaceMap
{
    public readonly List<Surface> All = new();
    public readonly List<DesktopWindow> Windows = new();

    const float MinPiece = 10f;
    /// <summary>Clearance the spider needs on the normal side of an edge for it to be usable.</summary>
    const float Clearance = 14f;

    readonly List<Rect2> _workAreas = new();

    /// <summary>Rebuilds the edges, unless the windows and work areas are exactly as last time (most scans).</summary>
    /// <returns>True if anything changed.</returns>
    public bool Rebuild(IReadOnlyList<DesktopWindow> windows, IReadOnlyList<Rect2> workAreas)
    {
        if (Unchanged(windows, workAreas)) return false;
        All.Clear();
        Windows.Clear();
        Windows.AddRange(windows);
        _workAreas.Clear();
        _workAreas.AddRange(workAreas);

        // Windows come top-most first: window i is occluded by windows 0..i-1.
        for (int i = 0; i < windows.Count; i++)
        {
            var edges = OuterEdges(windows[i].Rect);
            var pieces = Pieces(edges, windows[i].Handle, windows, i, workAreas, -1);
            LinkCorners(edges, pieces);
            foreach (var list in pieces) All.AddRange(list);
        }

        // Screen borders: one owner per monitor; pieces from different monitors join wherever they touch.
        var screen = new List<Surface>();
        for (int m = 0; m < workAreas.Count; m++)
            foreach (var list in Pieces(InnerEdges(workAreas[m]), -(m + 1), windows, windows.Count, workAreas, m))
                screen.AddRange(list);
        LinkByEndpoints(screen);
        All.AddRange(screen);
        return true;
    }

    /// <summary>Same windows in the same Z-order and places (titles don't matter for the edges).</summary>
    bool Unchanged(IReadOnlyList<DesktopWindow> windows, IReadOnlyList<Rect2> workAreas)
    {
        if (All.Count == 0 || windows.Count != Windows.Count || !workAreas.SequenceEqual(_workAreas)) return false;
        for (int i = 0; i < windows.Count; i++)
            if (windows[i].Handle != Windows[i].Handle || windows[i].Rect != Windows[i].Rect
                || windows[i].CoversMonitor != Windows[i].CoversMonitor) return false;
        return true;
    }

    static (Vector2 a, Vector2 b, Vector2 n, EdgeSide side)[] OuterEdges(Rect2 r)
    {
        var tl = r.Position; var br = r.End;
        var tr = new Vector2(br.X, tl.Y); var bl = new Vector2(tl.X, br.Y);
        return new[]
        {
            (tl, tr, Vector2.Up, EdgeSide.Top),
            (tr, br, Vector2.Right, EdgeSide.Right),
            (br, bl, Vector2.Down, EdgeSide.Bottom),
            (bl, tl, Vector2.Left, EdgeSide.Left),
        };
    }

    static (Vector2 a, Vector2 b, Vector2 n, EdgeSide side)[] InnerEdges(Rect2 r)
    {
        var tl = r.Position; var br = r.End;
        var tr = new Vector2(br.X, tl.Y); var bl = new Vector2(tl.X, br.Y);
        return new[]
        {
            (bl, br, Vector2.Up, EdgeSide.Bottom),   // floor
            (br, tr, Vector2.Left, EdgeSide.Right),  // right wall
            (tr, tl, Vector2.Down, EdgeSide.Top),    // ceiling
            (tl, bl, Vector2.Right, EdgeSide.Left),  // left wall
        };
    }

    /// <param name="monitor">Index of the work area these are the inner borders of, or -1 for a window.</param>
    List<Surface>[] Pieces((Vector2 a, Vector2 b, Vector2 n, EdgeSide side)[] edges, long owner,
        IReadOnlyList<DesktopWindow> windows, int occluderCount, IReadOnlyList<Rect2> workAreas, int monitor)
    {
        var pieces = new List<Surface>[edges.Length];
        for (int e = 0; e < edges.Length; e++)
        {
            var (a, b, n, side) = edges[e];
            pieces[e] = new List<Surface>();
            float len = a.DistanceTo(b);
            var t = (b - a) / len;
            bool horizontal = Mathf.Abs(t.X) > 0.5f;

            List<(float s0, float s1)> visible;
            if (monitor < 0)
            {
                // A window edge counts where it lies on any monitor.
                visible = new List<(float, float)>();
                foreach (var work in workAreas)
                {
                    var part = new List<(float s0, float s1)> { (0, len) };
                    Clip(part, a, t, horizontal, work, keepInside: true);
                    visible.AddRange(part);
                }
                visible = Merge(visible);
            }
            else
            {
                // A monitor border opens up where another monitor continues right behind it.
                visible = new List<(float, float)> { (0, len) };
                for (int k = 0; k < workAreas.Count; k++)
                    if (k != monitor)
                        Clip(visible, a - n * 2, t, horizontal, workAreas[k], keepInside: false);
            }
            for (int w = 0; w < occluderCount; w++)
            {
                // A fullscreen window (e.g. remote desktop) would otherwise leave its monitor without a floor.
                if (monitor >= 0 && windows[w].CoversMonitor) continue;
                Clip(visible, a, t, horizontal, windows[w].Rect, keepInside: false);
            }

            foreach (var (s0, s1) in visible)
            {
                if (s1 - s0 < MinPiece) continue;
                // The standing side must be on screen with some room.
                var probe = a + t * ((s0 + s1) / 2) + n * Clearance;
                if (!workAreas.Any(w => w.Grow(1).HasPoint(probe))) continue;
                pieces[e].Add(new Surface
                {
                    A = a + t * s0, B = a + t * s1, Normal = n, Owner = owner, Side = side, StartOffset = s0,
                });
            }
        }
        return pieces;
    }

    /// <summary>
    /// Chains the pieces of one rectangle: inside an edge pieces are separated by gaps (no link); across a
    /// corner they link only if both ends survived the clipping.
    /// </summary>
    static void LinkCorners((Vector2 a, Vector2 b, Vector2 n, EdgeSide side)[] edges, List<Surface>[] pieces)
    {
        for (int e = 0; e < edges.Length; e++)
        {
            var cur = pieces[e];
            var nxt = pieces[(e + 1) % edges.Length];
            if (cur.Count == 0 || nxt.Count == 0) continue;
            var last = cur[^1];
            var first = nxt[0];
            if (last.B.DistanceTo(edges[e].b) < 0.5f && first.A.DistanceTo(edges[(e + 1) % edges.Length].a) < 0.5f)
            {
                last.Next = first;
                first.Prev = last;
            }
        }
    }

    /// <summary>Links every piece whose end touches another piece's start (straight continuations first).</summary>
    static void LinkByEndpoints(List<Surface> pieces)
    {
        foreach (var p in pieces)
        {
            Surface? best = null;
            foreach (var q in pieces)
            {
                if (q == p || q.Prev != null || q.A.DistanceTo(p.B) > 1.5f) continue;
                if (best == null || q.Normal.Dot(p.Normal) > best.Normal.Dot(p.Normal)) best = q;
            }
            if (best == null) continue;
            p.Next = best;
            best.Prev = p;
        }
    }

    static List<(float s0, float s1)> Merge(List<(float s0, float s1)> intervals)
    {
        var sorted = intervals.OrderBy(i => i.s0).ToList();
        var result = new List<(float s0, float s1)>();
        foreach (var iv in sorted)
        {
            if (result.Count > 0 && iv.s0 <= result[^1].s1 + 0.5f)
                result[^1] = (result[^1].s0, Mathf.Max(result[^1].s1, iv.s1));
            else
                result.Add(iv);
        }
        return result;
    }

    /// <summary>Intersects (keepInside) or subtracts the rectangle from the visible intervals.</summary>
    static void Clip(List<(float s0, float s1)> intervals, Vector2 a, Vector2 t, bool horizontal, Rect2 r, bool keepInside)
    {
        float fixedCoord = horizontal ? a.Y : a.X;
        float lo = horizontal ? r.Position.Y : r.Position.X;
        float hi = horizontal ? r.End.Y : r.End.X;

        // Interval of s where the edge is inside the rectangle's span along the edge axis.
        float axisA = horizontal ? a.X : a.Y;
        float dir = horizontal ? t.X : t.Y;
        float r0 = horizontal ? r.Position.X : r.Position.Y;
        float r1 = horizontal ? r.End.X : r.End.Y;
        float sA = (r0 - axisA) / dir, sB = (r1 - axisA) / dir;
        float inS0 = Mathf.Min(sA, sB), inS1 = Mathf.Max(sA, sB);

        bool crosses;
        if (keepInside)
            crosses = fixedCoord >= lo - 0.5f && fixedCoord <= hi + 0.5f;
        else
            crosses = fixedCoord > lo + 1f && fixedCoord < hi - 1f; // strictly inside: touching edges stay visible

        var result = new List<(float, float)>();
        foreach (var (s0, s1) in intervals)
        {
            if (keepInside)
            {
                if (!crosses) continue;
                float c0 = Mathf.Max(s0, inS0), c1 = Mathf.Min(s1, inS1);
                if (c1 > c0) result.Add((c0, c1));
            }
            else
            {
                if (!crosses || inS1 <= s0 || inS0 >= s1) { result.Add((s0, s1)); continue; }
                if (inS0 > s0) result.Add((s0, inS0));
                if (inS1 < s1) result.Add((inS1, s1));
            }
        }
        intervals.Clear();
        intervals.AddRange(result);
    }

    /// <summary>Finds the piece of (owner, side) that contains the given offset along the original edge.</summary>
    public Surface? Find(long owner, EdgeSide side, float originalOffset, out float s)
    {
        foreach (var surf in All)
        {
            if (surf.Owner != owner || surf.Side != side) continue;
            float local = originalOffset - surf.StartOffset;
            if (local >= -1f && local <= surf.Length + 1f)
            {
                s = Mathf.Clamp(local, 0, surf.Length);
                return surf;
            }
        }
        s = 0;
        return null;
    }

    public DesktopWindow? WindowOf(long handle)
    {
        foreach (var w in Windows)
            if (w.Handle == handle) return w;
        return null;
    }

    /// <summary>
    /// First surface hit by moving a point from p0 to p1, approaching from its normal side.
    /// Returns the hit fraction along the motion.
    /// </summary>
    public Surface? Sweep(Vector2 p0, Vector2 p1, out float t, out float s)
    {
        Surface? best = null;
        t = float.MaxValue; s = 0;
        foreach (var surf in All)
        {
            float d0 = (p0 - surf.A).Dot(surf.Normal);
            float d1 = (p1 - surf.A).Dot(surf.Normal);
            if (d0 < -0.5f || d1 > 0) continue;
            float f = d0 <= 0 ? 0 : d0 / (d0 - d1);
            var hit = p0.Lerp(p1, f);
            float along = (hit - surf.A).Dot(surf.Tangent);
            if (along < -2f || along > surf.Length + 2f) continue;
            if (f < t)
            {
                t = f; best = surf; s = Mathf.Clamp(along, 0, surf.Length);
            }
        }
        return best;
    }

    /// <summary>Nearest floor-like surface straight below the point (for silk drops).</summary>
    public Surface? FloorBelow(Vector2 p, out float distance, out float s)
    {
        Surface? best = null;
        distance = float.MaxValue; s = 0;
        foreach (var surf in All)
        {
            if (!surf.IsFloorLike) continue;
            float minX = Mathf.Min(surf.A.X, surf.B.X), maxX = Mathf.Max(surf.A.X, surf.B.X);
            if (p.X < minX + 4 || p.X > maxX - 4) continue;
            float d = surf.A.Y - p.Y;
            if (d > 8 && d < distance)
            {
                distance = d; best = surf; s = p.X - surf.A.X;
            }
        }
        return best;
    }
}
