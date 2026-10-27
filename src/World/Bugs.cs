using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Webi.World;

public enum FlyState { Flying, Resting, Stuck, Leaving }

/// <summary>A fly buzzing around the desktop, resting on window edges or caught in a web.</summary>
public sealed class Fly
{
    public Vector2 Pos, Vel, Home;
    public FlyState State;
    /// <summary>Time left in the current state (resting, wandering) and the whole visit.</summary>
    public float Timer, Life;
    /// <summary>Where it rests: the surface identity, so it rides along with a moving window.</summary>
    public long RestOwner;
    public EdgeSide RestSide;
    public float RestOffset;
    /// <summary>The web it is stuck in and where (in the web's corner space).</summary>
    public Web? Web;
    public Vector2 WebLocal;
    /// <summary>Webs it already flew through without getting caught (so it doesn't get a new try every frame).</summary>
    public readonly HashSet<Web> Dodged = new();
    public float Wing;
    public bool Gone;
}

/// <summary>
/// Flies for Webi to hunt: one shows up every few minutes, wanders around, lands on window edges now and then,
/// and leaves after a couple of minutes. Flying into a corner web gets it stuck there until Webi comes for it.
/// Drawn as tiny pixel-art flies on the art-pixel grid.
/// </summary>
public partial class BugLayer : Node2D
{
    const float MaxSpeed = 170;

    public readonly List<Fly> Flies = new();
    public int PixelScale = 2;
    public bool Enabled = true;
    public SurfaceMap? Map;
    public WebLayer? Webs;
    /// <summary>Visible monitor areas (set by Main).</summary>
    public IReadOnlyList<Rect2> Areas = Array.Empty<Rect2>();

    /// <summary>A fly got caught in a web.</summary>
    public event Action<Fly>? Stuck;

    readonly RandomNumberGenerator _rng = new();
    float _spawnTimer;
    float _time;

    public override void _Ready() => _spawnTimer = _rng.RandfRange(90, 240);

    public void Spawn()
    {
        if (Areas.Count == 0) return;
        var area = Areas[_rng.RandiRange(0, Areas.Count - 1)];
        bool left = _rng.Randf() < 0.5f;
        var pos = new Vector2(left ? area.Position.X + 2 : area.End.X - 2, area.Position.Y + area.Size.Y * _rng.RandfRange(0.15f, 0.6f));
        Flies.Add(new Fly
        {
            Pos = pos,
            Vel = new Vector2(left ? 120 : -120, 0),
            Home = RandomPoint(area),
            State = FlyState.Flying,
            Timer = _rng.RandfRange(8, 20),
            Life = _rng.RandfRange(100, 160),
        });
    }

    Vector2 RandomPoint(Rect2 area) =>
        new(area.Position.X + area.Size.X * _rng.RandfRange(0.1f, 0.9f), area.Position.Y + area.Size.Y * _rng.RandfRange(0.1f, 0.75f));

    /// <summary>The fly is eaten: gone at once.</summary>
    public void Eat(Fly fly)
    {
        fly.Gone = true;
        Flies.Remove(fly);
    }

    /// <summary>Something came at it: it takes off fast, away from <paramref name="from"/>.</summary>
    public void Startle(Fly fly, Vector2 from)
    {
        if (fly.State is FlyState.Stuck or FlyState.Leaving) return;
        fly.State = FlyState.Flying;
        var away = (fly.Pos - from).Normalized();
        if (away == Vector2.Zero) away = Vector2.Up;
        fly.Vel = (away + new Vector2(0, -0.6f)).Normalized() * MaxSpeed * 1.8f;
        fly.Home = fly.Pos + away * 400 + new Vector2(0, -150);
        fly.Timer = _rng.RandfRange(6, 12);
    }

    public void Clear()
    {
        foreach (var f in Flies) f.Gone = true;
        Flies.Clear();
    }

    public override void _Process(double delta)
    {
        float dt = Mathf.Min((float)delta, 0.1f);
        _time += dt;
        if (Enabled && (_spawnTimer -= dt) <= 0)
        {
            _spawnTimer = _rng.RandfRange(180, 480);
            if (Flies.Count < 2) Spawn();
        }

        foreach (var fly in Flies.ToList())
        {
            fly.Wing += dt;
            fly.Life -= dt;
            switch (fly.State)
            {
                case FlyState.Flying: Wander(fly, dt); break;
                case FlyState.Resting: Rest(fly, dt); break;
                case FlyState.Stuck: StuckInWeb(fly, dt); break;
                case FlyState.Leaving: Leave(fly, dt); break;
            }
            if (fly.Life <= 0 && fly.State is FlyState.Flying or FlyState.Resting) fly.State = FlyState.Leaving;
        }
        QueueRedraw();
    }

    void Wander(Fly fly, float dt)
    {
        // Steer toward home with a lot of jitter: flies never go in straight lines.
        var toHome = fly.Home - fly.Pos;
        var steer = toHome.LimitLength(1) * 420 + new Vector2(_rng.RandfRange(-1, 1), _rng.RandfRange(-1, 1)) * 900;
        fly.Vel = (fly.Vel + steer * dt).LimitLength(MaxSpeed);
        fly.Pos += fly.Vel * dt;
        if (toHome.Length() < 40) fly.Home = fly.Pos + new Vector2(_rng.RandfRange(-300, 300), _rng.RandfRange(-160, 160));
        KeepInside(fly);

        fly.Timer -= dt;
        if (fly.Timer <= 0)
        {
            fly.Timer = _rng.RandfRange(8, 20);
            if (_rng.Randf() < 0.6f) TryLand(fly);
        }

        CheckWebs(fly);
    }

    /// <summary>Picks a nearby floor (a window's top edge or the taskbar) and lands on it.</summary>
    void TryLand(Fly fly)
    {
        if (Map == null) return;
        var floors = Map.All.Where(s => s.IsFloorLike && s.Length > 60).ToList();
        if (floors.Count == 0) return;
        var near = floors.OrderBy(s => s.PointAt(s.Project(fly.Pos)).DistanceSquaredTo(fly.Pos)).Take(3).ToList();
        var surf = near[_rng.RandiRange(0, near.Count - 1)];
        float at = Mathf.Clamp(surf.Project(fly.Pos) + _rng.RandfRange(-80, 80), 20, surf.Length - 20);
        fly.State = FlyState.Resting;
        fly.RestOwner = surf.Owner;
        fly.RestSide = surf.Side;
        fly.RestOffset = surf.StartOffset + at;
        fly.Pos = surf.PointAt(at) + surf.Normal * 3;
        fly.Timer = _rng.RandfRange(5, 14);
    }

    void Rest(Fly fly, float dt)
    {
        float s = 0;
        var surf = Map?.Find(fly.RestOwner, fly.RestSide, fly.RestOffset, out s);
        if (surf == null) { fly.State = FlyState.Flying; return; }
        fly.Pos = surf.PointAt(s) + surf.Normal * 3;
        fly.Timer -= dt;
        if (fly.Timer <= 0)
        {
            fly.State = FlyState.Flying;
            fly.Vel = new Vector2(_rng.RandfRange(-60, 60), -120);
            fly.Home = fly.Pos + new Vector2(_rng.RandfRange(-300, 300), -_rng.RandfRange(80, 240));
            fly.Timer = _rng.RandfRange(8, 20);
        }
    }

    void StuckInWeb(Fly fly, float dt)
    {
        if (fly.Web == null || !fly.Web.Alive)
        {
            // The web is gone: free again.
            fly.Web = null;
            fly.State = FlyState.Flying;
            fly.Timer = _rng.RandfRange(8, 20);
            fly.Life = Mathf.Max(fly.Life, 40);
            return;
        }
        // Struggling.
        fly.Pos = fly.Web.ToWorld(fly.WebLocal) + new Vector2(Mathf.Sin(_time * 40) * 1.5f, 0);
    }

    void Leave(Fly fly, float dt)
    {
        var area = Areas.Count > 0 ? Areas.OrderBy(a => a.GetCenter().DistanceSquaredTo(fly.Pos)).First() : new Rect2(fly.Pos, Vector2.One);
        var exit = new Vector2(fly.Pos.X < area.GetCenter().X ? area.Position.X - 60 : area.End.X + 60, fly.Pos.Y - 80);
        fly.Vel = (fly.Vel + ((exit - fly.Pos).Normalized() * 500 + new Vector2(_rng.RandfRange(-1, 1), _rng.RandfRange(-1, 1)) * 500) * dt).LimitLength(MaxSpeed * 1.3f);
        fly.Pos += fly.Vel * dt;
        if (!Areas.Any(a => a.Grow(30).HasPoint(fly.Pos))) { fly.Gone = true; Flies.Remove(fly); }
    }

    void KeepInside(Fly fly)
    {
        if (Areas.Count == 0 || Areas.Any(a => a.HasPoint(fly.Pos))) return;
        var area = Areas.OrderBy(a => a.GetCenter().DistanceSquaredTo(fly.Pos)).First();
        fly.Pos = new Vector2(Mathf.Clamp(fly.Pos.X, area.Position.X + 4, area.End.X - 4), Mathf.Clamp(fly.Pos.Y, area.Position.Y + 4, area.End.Y - 4));
        fly.Home = RandomPoint(area);
    }

    /// <summary>Flying into a finished, visible web: usually stuck (one chance per web).</summary>
    void CheckWebs(Fly fly)
    {
        if (Webs == null) return;
        foreach (var web in Webs.Webs)
        {
            if (!web.Alive || !web.Visible || web.Progress < 1 || fly.Dodged.Contains(web)) continue;
            var local = web.ToLocal(fly.Pos);
            bool inside = web.Giant
                ? local.X > 0 && local.Y > 0 && local.X < (web.Hub.X - web.Corner.X) * 2 && local.Y < (web.Hub.Y - web.Corner.Y) * 2
                : local.X > 0 && local.Y > 0 && local.LengthSquared() < web.Radius * web.Radius * 0.7f;
            if (!inside) continue;
            if (_rng.Randf() < 0.65f)
            {
                fly.State = FlyState.Stuck;
                fly.Web = web;
                fly.WebLocal = local;
                Stuck?.Invoke(fly);
                return;
            }
            fly.Dodged.Add(web);
        }
    }

    // ------------------------------------------------------------------ drawing

    static readonly Color Body = new("1d1b22");
    static readonly Color Eye = new("b8322a");
    static readonly Color WingColor = new(0.85f, 0.92f, 1f, 0.75f);

    public override void _Draw()
    {
        int s = PixelScale;
        foreach (var fly in Flies)
        {
            var o = (fly.Pos / s).Round() * s;
            int dir = fly.Vel.X < 0 ? -1 : 1;
            void Px(int x, int y, Color c) => DrawRect(new Rect2(o + new Vector2(x * dir * s, y * s) - new Vector2(s / 2f, s / 2f), new Vector2(s, s)), c);
            bool flapping = fly.State is FlyState.Flying or FlyState.Leaving || (fly.State == FlyState.Stuck && (int)(fly.Wing * 20) % 3 == 0);
            bool up = flapping && (int)(fly.Wing * 30) % 2 == 0;
            // Wings: up and back while flapping, folded flat on the back when resting.
            if (up) { Px(-1, -2, WingColor); Px(0, -2, WingColor); Px(-1, -3, WingColor); }
            else { Px(-1, -1, WingColor); Px(-2, -1, WingColor); }
            // Body (3 px), head with a red eye.
            Px(-1, 0, Body); Px(0, 0, Body); Px(-2, 0, Body);
            Px(1, 0, Body); Px(1, -1, Eye);
        }
    }
}
