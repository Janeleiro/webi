using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Webi.World;

/// <summary>
/// A corner where a web can be built: the top corners of the screen (ceiling meets wall) and the top corners
/// inside each visible window. <see cref="D1"/>/<see cref="D2"/> run along the two edges away from the corner.
/// </summary>
public sealed class WebSpot
{
    public string Key = "";
    public Vector2 Corner, D1, D2;
    public float Radius;
    /// <summary>Window handle for window corners, 0 for screen corners.</summary>
    public long Window;
    public bool LeftCorner;
    /// <summary>Where the spider stands while weaving.</summary>
    public Surface Stand = null!;
    public float StandS;

    /// <summary>Seed of the web that would be woven here (changes after each web, so rebuilt webs differ).</summary>
    public ulong Seed;

    public Vector2 StandPoint => Stand.PointAt(StandS);
    /// <summary>Approximate hanging point (the real one comes from the generated shape).</summary>
    public Vector2 Hub => Corner + (D1 + D2).Normalized() * Radius * 0.42f;
}

public sealed class Web
{
    public string Key = "";
    public Vector2 Corner, D1, D2;
    public float Radius;
    public long Window;
    public bool LeftCorner;
    /// <summary>0..1 while being woven.</summary>
    public float Progress;
    /// <summary>Fades out when the web is removed.</summary>
    public float Alpha = 1;
    public bool Alive = true;
    /// <summary>False while a window above covers it.</summary>
    public bool Visible = true;
    /// <summary>End of the last thread drawn so far (where the spinning silk attaches while weaving).</summary>
    public Vector2 ActivePoint;
    /// <summary>Procedural shape, in corner space (x along <see cref="D1"/>, y along <see cref="D2"/>).</summary>
    public WebShape Shape = null!;
    /// <summary>The screen-covering web woven when the mouse sits still for a long time.</summary>
    public bool Giant;
    /// <summary>Set when torn apart: the threads fall and fade.</summary>
    public bool Torn;
    public float TornTime;

    public Vector2 ToWorld(Vector2 local) => Corner + D1 * local.X + D2 * local.Y;
    public Vector2 ToLocal(Vector2 world) => new((world - Corner).Dot(D1), (world - Corner).Dot(D2));
    public Vector2 Hub => ToWorld(Shape.Hub);

    /// <summary>
    /// The route the spider follows while weaving, in corner space: every thread in order (laying silk) with
    /// walks between them where one thread doesn't start where the last ended (no silk).
    /// </summary>
    readonly List<(Vector2 a, Vector2 b, bool silk)> _route = new();
    public float RouteLength { get; private set; }

    public void PlanRoute(Vector2 startWorld)
    {
        _route.Clear();
        var at = ToLocal(startWorld);
        foreach (var (a, b) in Shape.Threads)
        {
            if (at.DistanceTo(a) > 1) _route.Add((at, a, false));
            _route.Add((a, b, true));
            at = b;
        }
        RouteLength = 0;
        foreach (var (a, b, _) in _route) RouteLength += a.DistanceTo(b);
    }

    /// <summary>
    /// <paramref name="distance"/> along the route: where the spider is (world) and how much silk has been laid,
    /// which also sets <see cref="Progress"/> so the drawing ends exactly at the spider.
    /// </summary>
    public Vector2 WalkRoute(float distance)
    {
        float laid = 0;
        var at = _route.Count > 0 ? _route[0].a : Vector2.Zero;
        foreach (var (a, b, silk) in _route)
        {
            float len = a.DistanceTo(b);
            float step = Mathf.Min(len, distance);
            at = len > 0 ? a.Lerp(b, step / len) : b;
            if (silk) laid += step;
            distance -= step;
            if (distance <= 0) break;
        }
        Progress = Shape.TotalLength > 0 ? Mathf.Min(1, laid / Shape.TotalLength) : 1;
        return ToWorld(at);
    }
}

/// <summary>
/// A procedurally generated corner web: every web gets its own number of radials and rings, irregular angles
/// and spacing, a few broken or missing threads, sometimes an outer frame and stray anchor lines.
/// Threads are stored in drawing (= weaving) order: radials, frame, rings from the inside out, strays.
/// </summary>
public sealed class WebShape
{
    public readonly List<(Vector2 a, Vector2 b)> Threads = new();
    public Vector2 Hub;
    public float TotalLength;
    public float Opacity;

    /// <param name="gravity">World "down" expressed in corner space, for the sag of the rings.</param>
    public static WebShape Generate(ulong seed, float radius, Vector2 gravity)
    {
        var rng = new RandomNumberGenerator { Seed = seed };
        var shape = new WebShape { Opacity = rng.RandfRange(0.6f, 0.9f) };

        int radials = rng.RandiRange(5, 9);
        var tips = new Vector2[radials];
        var dirs = new Vector2[radials];
        var lengths = new float[radials];
        for (int k = 0; k < radials; k++)
        {
            float t = k / (float)(radials - 1);
            bool edge = k == 0 || k == radials - 1;
            if (!edge) t += rng.RandfRange(-0.3f, 0.3f) / (radials - 1);
            float angle = t * Mathf.Pi / 2;
            dirs[k] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            lengths[k] = radius * (edge ? rng.RandfRange(0.85f, 1.05f) : rng.RandfRange(0.65f, 1f));
            tips[k] = dirs[k] * lengths[k];
            shape.Threads.Add((Vector2.Zero, tips[k]));
        }

        // Outer frame between the tips of neighbouring radials.
        if (rng.Randf() < 0.6f)
            for (int k = 0; k < radials - 1; k++)
                if (rng.Randf() < 0.85f) shape.Threads.Add((tips[k], tips[k + 1]));

        int rings = rng.RandiRange(4, 8);
        float sag = rng.RandfRange(0.01f, 0.05f) * radius;
        for (int r = 0; r < rings; r++)
        {
            float f = Mathf.Lerp(0.16f, 0.9f, (r + rng.RandfRange(-0.3f, 0.3f)) / Mathf.Max(1, rings - 1));
            f = Mathf.Clamp(f, 0.1f, 0.95f);
            for (int k = 0; k < radials - 1; k++)
            {
                if (rng.Randf() < 0.07f) continue; // broken thread
                var a = dirs[k] * lengths[k] * f * rng.RandfRange(0.95f, 1.05f);
                var b = dirs[k + 1] * lengths[k + 1] * f * rng.RandfRange(0.95f, 1.05f);
                var mid = (a + b) / 2 + gravity * sag * (0.5f + f);
                shape.Threads.Add((a, mid));
                shape.Threads.Add((mid, b));
            }
        }

        // A couple of loose anchor lines from the web out to the walls.
        int strays = rng.RandiRange(0, 2);
        for (int i = 0; i < strays; i++)
        {
            var from = tips[rng.RandiRange(1, radials - 2)];
            var to = rng.Randf() < 0.5f
                ? new Vector2(radius * rng.RandfRange(1.05f, 1.3f), 0)
                : new Vector2(0, radius * rng.RandfRange(1.05f, 1.3f));
            shape.Threads.Add((from, to));
        }

        int middle = radials / 2;
        shape.Hub = dirs[middle] * lengths[middle] * 0.6f;
        foreach (var (a, b) in shape.Threads) shape.TotalLength += a.DistanceTo(b);
        return shape;
    }

    /// <summary>
    /// A full orb web spanning a whole rectangle (a monitor): radials from the centre to the corners and
    /// along the edges, a frame around, and a spiral of rings woven from the inside out.
    /// </summary>
    public static WebShape GenerateOrb(ulong seed, Vector2 size)
    {
        var rng = new RandomNumberGenerator { Seed = seed };
        var shape = new WebShape { Opacity = rng.RandfRange(0.7f, 0.85f), Hub = size / 2 };
        var hub = shape.Hub;

        // Anchors around the border, ordered by angle around the hub: the 4 corners plus a few per edge.
        var anchors = new List<Vector2> { Vector2.Zero, new(size.X, 0), size, new(0, size.Y) };
        int perEdge = rng.RandiRange(2, 4);
        for (int i = 1; i <= perEdge; i++)
        {
            float t = (i + rng.RandfRange(-0.3f, 0.3f)) / (perEdge + 1);
            anchors.Add(new Vector2(size.X * t, 0));
            anchors.Add(new Vector2(size.X * t, size.Y));
            anchors.Add(new Vector2(0, size.Y * t));
            anchors.Add(new Vector2(size.X, size.Y * t));
        }
        anchors = anchors.OrderBy(a => (a - hub).Angle()).ToList();

        foreach (var a in anchors) shape.Threads.Add((hub, a));
        for (int k = 0; k < anchors.Count; k++) shape.Threads.Add((anchors[k], anchors[(k + 1) % anchors.Count]));

        // Round rings around the hub, flattened against the border where the screen is too short for them.
        int rings = rng.RandiRange(10, 15);
        float sag = rng.RandfRange(4f, 12f);
        float farthest = anchors.Max(x => x.DistanceTo(hub));
        Vector2 Ring(int k, float radius)
        {
            var d = anchors[k] - hub;
            return hub + d.Normalized() * Mathf.Min(radius, d.Length() * 0.97f) * rng.RandfRange(0.97f, 1.03f);
        }
        for (int r = 0; r < rings; r++)
        {
            float f = Mathf.Clamp((r + 1 + rng.RandfRange(-0.25f, 0.25f)) / (rings + 1), 0.04f, 0.97f);
            for (int k = 0; k < anchors.Count; k++)
            {
                if (rng.Randf() < 0.04f) continue; // broken thread
                var a = Ring(k, f * farthest);
                var b = Ring((k + 1) % anchors.Count, f * farthest);
                var mid = (a + b) / 2 + Vector2.Down * sag * f;
                shape.Threads.Add((a, mid));
                shape.Threads.Add((mid, b));
            }
        }
        foreach (var (a, b) in shape.Threads) shape.TotalLength += a.DistanceTo(b);
        return shape;
    }
}

/// <summary>Owns the webs: finds corners, follows windows, and draws them (in world space, under the spider).</summary>
public partial class WebLayer : Node2D
{
    public readonly List<Web> Webs = new();
    public int PixelScale = 2;

    static readonly Color Silk = new(0.94f, 0.93f, 0.9f);
    /// <summary>Per corner: how many webs were woven there, so the next one gets a different shape and size.</summary>
    readonly Dictionary<string, int> _generation = new();

    ulong SeedFor(string key)
    {
        _generation.TryGetValue(key, out int gen);
        return (ulong)HashCode.Combine(key, gen) * 2654435761UL + 1;
    }

    /// <summary>Web size for a corner, in screen pixels: varies from web to web (150-320 px).</summary>
    float SizeFor(string key)
    {
        var rng = new RandomNumberGenerator { Seed = SeedFor(key) ^ 0x9E3779B97F4A7C15UL };
        return rng.RandfRange(150, 320);
    }

    /// <summary>Corners the spider can stand next to; by default only the ones without a web yet.</summary>
    public List<WebSpot> Spots(SurfaceMap map, bool freeOnly = true)
    {
        var spots = new List<WebSpot>();
        int s = PixelScale;

        // Screen: concave corners where a ceiling meets a wall (stand on the wall, just below the ceiling).
        foreach (var p in map.All.Where(x => x.Owner < 0 && x.Next != null))
        {
            var q = p.Next!;
            bool concave = q.Tangent.Dot(p.Normal) > 0.5f;
            if (!concave) continue;
            Surface wall, ceiling;
            if (p.IsWall && q.IsCeilingLike) { wall = p; ceiling = q; }
            else if (p.IsCeilingLike && q.IsWall) { wall = q; ceiling = p; }
            else continue;
            var corner = p.B;
            string key = $"s{corner.X:0},{corner.Y:0}";
            float radius = Mathf.Min(SizeFor(key), wall.Length * 0.6f);
            var spot = new WebSpot
            {
                Key = key,
                Seed = SeedFor(key),
                Corner = corner,
                D1 = -p.Tangent, // along both edges, away from the corner
                D2 = q.Tangent,
                Radius = radius,
                // On the wall at the web's lower edge, so it weaves beside the web rather than on top of it.
                Stand = wall,
                StandS = wall == p ? Mathf.Max(0, p.Length - radius * 0.8f) : Mathf.Min(q.Length, radius * 0.8f),
            };
            spots.Add(spot);
        }

        // Windows: the two top corners, seen from inside the window, standing on its top edge.
        for (int i = 0; i < map.Windows.Count; i++)
        {
            var w = map.Windows[i];
            foreach (var top in map.All.Where(x => x.Owner == w.Handle && x.Side == EdgeSide.Top))
            {
                bool left = top.StartOffset < 1;
                bool right = top.StartOffset + top.Length > w.Rect.Size.X - 1;
                foreach (var isLeft in new[] { true, false })
                {
                    if (isLeft ? !left : !right) continue;
                    var corner = isLeft ? w.Rect.Position : new Vector2(w.Rect.End.X, w.Rect.Position.Y);
                    string key = $"w{w.Handle:X}{(isLeft ? "L" : "R")}";
                    float radius = Mathf.Min(SizeFor(key), Mathf.Min(w.Rect.Size.X, w.Rect.Size.Y) * 0.42f);
                    if (radius < 60) continue;
                    var spot = new WebSpot
                    {
                        Key = key,
                        Seed = SeedFor(key),
                        Corner = corner,
                        D1 = isLeft ? Vector2.Right : Vector2.Left,
                        D2 = Vector2.Down,
                        Radius = radius,
                        Window = w.Handle,
                        LeftCorner = isLeft,
                        Stand = top,
                        // On the top edge right above the web.
                        StandS = isLeft ? Mathf.Min(top.Length, radius * 0.55f) : Mathf.Max(0, top.Length - radius * 0.55f),
                    };
                    if (!Covered(map, i, spot.Hub)) spots.Add(spot);
                }
            }
        }

        return freeOnly ? spots.Where(sp => Webs.All(w => w.Key != sp.Key || !w.Alive)).ToList() : spots;
    }

    /// <summary>True when a window above window <paramref name="index"/> in the Z-order covers the point.</summary>
    static bool Covered(SurfaceMap map, int index, Vector2 p)
    {
        for (int j = 0; j < index; j++)
            if (map.Windows[j].Rect.HasPoint(p)) return true;
        return false;
    }

    public Web Begin(WebSpot spot)
    {
        // World "down" in the corner's frame, so the rings sag with gravity whatever the corner's orientation.
        var gravity = new Vector2(spot.D1.Dot(Vector2.Down), spot.D2.Dot(Vector2.Down));
        var web = new Web
        {
            Key = spot.Key, Corner = spot.Corner, D1 = spot.D1, D2 = spot.D2, Radius = spot.Radius,
            Window = spot.Window, LeftCorner = spot.LeftCorner, Progress = 0, ActivePoint = spot.Corner,
            Shape = WebShape.Generate(spot.Seed, spot.Radius, gravity),
        };
        _generation[spot.Key] = _generation.GetValueOrDefault(spot.Key) + 1;
        Webs.Add(web);
        return web;
    }

    /// <summary>Starts a web covering the whole rectangle (a monitor), with its hub in the middle.</summary>
    public Web BeginGiant(Rect2 area)
    {
        string key = $"giant{area.Position.X:0},{area.Position.Y:0}";
        var web = new Web
        {
            Key = key, Corner = area.Position, D1 = Vector2.Right, D2 = Vector2.Down,
            Radius = area.Size.Length() / 2, Giant = true, ActivePoint = area.GetCenter(),
            Shape = WebShape.GenerateOrb(SeedFor(key), area.Size),
        };
        _generation[key] = _generation.GetValueOrDefault(key) + 1;
        Webs.Add(web);
        return web;
    }

    public Web? Giant => Webs.FirstOrDefault(w => w.Giant && w.Alive);

    /// <summary>Breaks a web: its threads fall and fade out.</summary>
    public void Tear(Web web)
    {
        if (web.Torn) return;
        web.Torn = true;
        web.Alive = false;
        web.TornTime = 0;
    }

    /// <summary>
    /// A corner web swept by a mouse drag from <paramref name="a"/> to <paramref name="b"/>, if any
    /// (the giant web only breaks by shaking the mouse).
    /// </summary>
    public Web? SweptBy(Vector2 a, Vector2 b)
    {
        foreach (var web in Webs)
        {
            if (!web.Alive || web.Giant || !web.Visible || web.Progress < 0.2f) continue;
            // Sample the drag segment against the web's quarter-disc.
            for (int i = 0; i <= 4; i++)
            {
                var p = a.Lerp(b, i / 4f) - web.Corner;
                float u = p.Dot(web.D1), v = p.Dot(web.D2);
                if (u > 0 && v > 0 && u * u + v * v < web.Radius * web.Radius * 0.8f) return web;
            }
        }
        return null;
    }


    public Web? Find(string key) => Webs.FirstOrDefault(w => w.Key == key && w.Alive);

    /// <summary>Follows moving windows; webs of closed/minimized windows fade away; hidden while covered.</summary>
    public void Sync(SurfaceMap map)
    {
        foreach (var web in Webs.Where(w => w.Alive))
        {
            if (web.Window == 0) continue;
            int index = map.Windows.FindIndex(w => w.Handle == web.Window);
            if (index < 0) { web.Alive = false; continue; }
            var rect = map.Windows[index].Rect;
            web.Corner = web.LeftCorner ? rect.Position : new Vector2(rect.End.X, rect.Position.Y);
            // A window shrunk below the web's size tears it down.
            if (Mathf.Min(rect.Size.X, rect.Size.Y) < web.Radius * 1.6f) { web.Alive = false; continue; }
            web.Visible = !Covered(map, index, web.Hub);
        }
    }

    /// <summary>Removes webs, oldest first, beyond <paramref name="max"/>.</summary>
    public void Trim(int max)
    {
        var alive = Webs.Where(w => w.Alive && !w.Giant && w.Progress >= 1).ToList();
        for (int i = 0; i < alive.Count - max; i++) alive[i].Alive = false;
    }

    public void ClearAll()
    {
        foreach (var w in Webs) w.Alive = false;
    }

    public override void _Process(double delta)
    {
        foreach (var w in Webs)
        {
            if (w.Torn) { w.TornTime += (float)delta; w.Alpha = 1 - w.TornTime / 1.4f; }
            else if (!w.Alive) w.Alpha -= (float)delta * 1.5f;
        }
        Webs.RemoveAll(w => w.Alpha <= 0);
        QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (var web in Webs)
            if (web.Visible) DrawWeb(web);
    }

    /// <summary>Draws the threads in weaving order up to <see cref="Web.Progress"/> of the total length.</summary>
    void DrawWeb(Web web)
    {
        var color = new Color(Silk, web.Shape.Opacity * Mathf.Clamp(web.Alpha, 0, 1));
        float budget = web.Shape.TotalLength * Mathf.Clamp(web.Progress, 0, 1);
        int index = 0;
        foreach (var (la, lb) in web.Shape.Threads)
        {
            if (budget <= 0) break;
            var a = web.ToWorld(la);
            var b = web.ToWorld(lb);
            if (web.Torn)
            {
                // Each broken thread falls at its own pace and drifts a little sideways.
                float t = web.TornTime, k = 0.6f + (index * 0.618f % 1f) * 0.8f;
                var drop = new Vector2(Mathf.Sin(index * 2.3f) * 40 * t, 700 * k * t * t);
                a += drop;
                b += drop + new Vector2(0, 30 * t * Mathf.Sin(index));
            }
            index++;
            float len = la.DistanceTo(lb);
            var end = budget >= len ? b : a.Lerp(b, budget / len);
            PixelDraw.Line(this, a, end, color, PixelScale);
            web.ActivePoint = end;
            budget -= len;
        }
    }
}
