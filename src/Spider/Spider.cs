using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Webi.World;

namespace Webi.Spider;

public enum Mode { Grounded, Airborne, Hanging, Dragged, Weaving }

public enum Face { Normal, Happy, Sleepy, Sleeping, Surprised, Sad }

/// <summary>Leg-driven gestures played while standing.</summary>
/// <remarks>
/// Peek: sinks behind the edge it stands on, only the eyes and front feet showing. Stalk: crouched, front legs
/// up and the body wiggling before a pounce. Trip: stumbles and catches itself. Hug: both front legs wrap around.
/// </remarks>
public enum Gesture { None, Wave, Groom, Tap, Stretch, Dance, Peek, Stalk, Yawn, Trip, Hug }

/// <summary>
/// The spider's body: locomotion over desktop edges, jumps, silk, dragging and the procedural legs.
/// Everything here is in world pixels; art-pixel sizes are multiplied by <see cref="PixelScale"/>.
/// Rendering happens in a low-res SubViewport (see <see cref="SpiderRig"/>) so rotation stays pixel art.
/// </summary>
public partial class Spider : Node2D
{
    /// <summary>Art pixels from the surface to the body centre while standing.</summary>
    public const float BodyHeight = 32f;
    /// <summary>Art pixels from the silk attachment point to the body centre while hanging.</summary>
    const float HangOffset = 28f;
    const float Gravity = 2200f;
    const int ViewportSize = 144;

    public int PixelScale { get; private set; } = 3;
    public Mode State { get; private set; } = Mode.Airborne;
    public Surface? Surface { get; private set; }
    public float S { get; private set; }
    public Vector2 BodyCenter { get; private set; }
    public Vector2 Contact { get; private set; }
    /// <summary>Visual "up" of the body (smoothed, follows the surface normal).</summary>
    public Vector2 Up { get; private set; } = Vector2.Up;
    public Vector2 Velocity => _velocity;
    public bool IsWalking => _walkRemaining > 0;
    /// <summary>Crouching before a jump that is about to happen.</summary>
    public bool IsPreparingJump => _jumpQueued;
    public int WalkDir => _walkDir;
    public bool IsClimbingSilk => State == Mode.Hanging && _ropeMode == RopeMode.Climbing;
    public bool IsDescending => State == Mode.Hanging && _ropeMode == RopeMode.Descending;
    public Vector2 Anchor => _anchor;

    public Face Expression { get; set; } = Face.Normal;
    /// <summary>World point the eyes look at; null = look where it walks.</summary>
    public Vector2? LookTarget { get; set; }
    /// <summary>Art px/s.</summary>
    public float WalkSpeed { get; set; } = 24f;
    /// <summary>Slow pedipalp wiggle (grooming).</summary>
    public bool Fidget { get; set; }
    /// <summary>Fast pedipalp wiggle while a speech bubble is being typed.</summary>
    public bool Speaking { get; set; }
    public Gesture CurrentGesture { get; private set; }
    /// <summary>The web it is hanging from, if any.</summary>
    public Web? HangingWeb => State == Mode.Hanging ? _anchorWeb : null;
    /// <summary>No floor anywhere to drop onto (every monitor taken by fullscreen apps): out of sight, waiting.</summary>
    public bool Parked { get; private set; }

    public event Action? Landed;
    public event Action? EdgeReached;
    /// <summary>Took off on a jump (for the sound).</summary>
    public event Action? Jumped;
    /// <summary>The window it stands on moved, with the speed it was carried at (px/s).</summary>
    public event Action<float>? Carried;

    /// <summary>Hat worn for the season (drawn by the rig).</summary>
    public Assistant.Hat Hat { get => _rig?.Hat ?? _pendingHat; set { _pendingHat = value; if (_rig != null) _rig.Hat = value; } }
    Assistant.Hat _pendingHat;

    SurfaceMap _map = null!;
    /// <summary>All monitors' bounding box (falling outside it means lost).</summary>
    Rect2 _bounds;
    /// <summary>Where to (re)appear: the primary monitor's work area.</summary>
    Rect2 _spawn;

    // Surface identity survives map rebuilds (windows move, get covered, close).
    long _keyOwner; EdgeSide _keySide; float _keyOffset;

    float _walkRemaining; int _walkDir = 1; float _walkSpeedCmd;
    /// <summary>Walk to resume after hopping across to another monitor.</summary>
    float _resumeWalk; int _resumeDir; float _resumeSpeed;
    float _crouch, _crouchTimer; Vector2 _pendingJump; bool _jumpQueued;
    float _angle, _bob, _time;
    Vector2 _velocity;

    // Secondary motion: the body lags/overshoots along the surface and leans.
    float _sway, _swayVel, _tilt, _prevSpeed;
    Vector2? _landingNormal;
    bool _justLanded;

    float _gestureTime, _gestureDuration;
    int _gestureSide = 1;

    enum RopeMode { Hold, Descending, Climbing }
    RopeMode _ropeMode;
    bool _fastDrop;
    /// <summary>Hanging head-down: the thread is held by the feet (the bottom of the sprite, now on top).</summary>
    bool _upsideDown;

    /// <summary>Chance that a leisurely silk descent is done upside down.</summary>
    const float UpsideDownChance = 0.35f;

    /// <summary>Where the silk joins the body: top of the abdomen, or the feet when upside down.</summary>
    Vector2 RopeAttach => _upsideDown
        ? BodyCenter - Up * SpiderRig.BodyBottom * PixelScale
        : BodyCenter + Up * HangOffset * PixelScale;
    Vector2 _anchor, _anchorExtra, _prevBody;
    Web? _anchorWeb;
    float _ropeLen;
    long _anchorOwner; EdgeSide _anchorSide; float _anchorOffset;

    /// <summary>Strands left behind after a silk drop; they fade out.</summary>
    readonly List<(Vector2 a, Vector2 b, float life)> _threads = new();

    readonly Leg[] _legs = new Leg[8];
    SubViewport _viewport = null!;
    SpiderRig _rig = null!;
    Sprite2D _sprite = null!;
    readonly RandomNumberGenerator _rng = new();

    public void Setup(SurfaceMap map, Rect2 bounds, Rect2 spawn, int scale)
    {
        _map = map;
        _bounds = bounds;
        _spawn = spawn;
        PixelScale = Math.Clamp(scale, 1, 8);

        _viewport = new SubViewport
        {
            Size = new Vector2I(ViewportSize, ViewportSize),
            TransparentBg = true,
            Disable3D = true,
            CanvasItemDefaultTextureFilter = Viewport.DefaultCanvasItemTextureFilter.Nearest,
            Snap2DTransformsToPixel = true,
            Snap2DVerticesToPixel = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(_viewport);
        _rig = new SpiderRig { Position = new Vector2(ViewportSize / 2, ViewportSize / 2), Hat = _pendingHat };
        _viewport.AddChild(_rig);
        // Not centred, so cropping the region (peeking over an edge) keeps the body where it is.
        _sprite = new Sprite2D
        {
            Texture = _viewport.GetTexture(), TextureFilter = TextureFilterEnum.Nearest,
            Centered = false, Offset = new Vector2(-ViewportSize / 2, -ViewportSize / 2),
        };
        AddChild(_sprite);

        for (int i = 0; i < 8; i++)
            _legs[i] = new Leg { Side = i < 4 ? -1 : 1, Index = i % 4, Phase = GaitOffset[i % 4] + (i < 4 ? 0 : 0.5f) };

        // Start by dropping from the top of the work area, somewhere in the right third.
        BodyCenter = new Vector2(spawn.Position.X + spawn.Size.X * 0.7f, spawn.Position.Y + 40);
        Contact = BodyCenter + Vector2.Down * BodyHeight * PixelScale;
        State = Mode.Airborne;
        for (int i = 0; i < 8; i++) _legs[i].Foot = BodyCenter;
    }

    public void SetPixelScale(int scale) => PixelScale = Math.Clamp(scale, 1, 8);

    /// <summary>Coat colour (recolours the sprites; takes effect immediately).</summary>
    public void SetFur(FurColor fur)
    {
        _pendingFur = fur;
        if (_rig != null && _rig.IsInsideTree()) { _rig.SetFur(fur); _pendingFur = null; }
    }

    FurColor? _pendingFur;

    public void SetBounds(Rect2 bounds, Rect2 spawn, IReadOnlyList<Rect2> workAreas)
    {
        _bounds = bounds;
        _spawn = spawn;
        _workAreas = workAreas;
    }

    /// <summary>Visible areas of the monitors: the body is always kept inside them.</summary>
    IReadOnlyList<Rect2> _workAreas = Array.Empty<Rect2>();

    /// <summary>Half the size of the body (art px), with a little room for the fur.</summary>
    const float BodyExtent = 31f;

    /// <summary>
    /// Keeps the whole body on screen: the square around it must be covered by the monitors' work areas
    /// (it may straddle two monitors that touch). Otherwise it is pushed back inside the nearest one.
    /// </summary>
    Vector2 KeepOnScreen(Vector2 center)
    {
        if (_workAreas.Count == 0) return center;
        float m = BodyExtent * PixelScale;
        bool Covered(Vector2 p) => _workAreas.Any(w => w.Grow(0.5f).HasPoint(p));
        bool fits = true;
        for (int i = -1; i <= 1 && fits; i++)
            for (int j = -1; j <= 1 && fits; j++)
                fits = Covered(center + new Vector2(i * m, j * m));
        if (fits) return center;

        Vector2 best = center;
        float bestDist = float.MaxValue;
        foreach (var w in _workAreas)
        {
            var inner = w.Grow(-m);
            if (inner.Size.X <= 0 || inner.Size.Y <= 0) continue;
            var p = new Vector2(Mathf.Clamp(center.X, inner.Position.X, inner.End.X), Mathf.Clamp(center.Y, inner.Position.Y, inner.End.Y));
            float d = p.DistanceSquaredTo(center);
            if (d < bestDist) { bestDist = d; best = p; }
        }
        return best;
    }

    public Rect2 HitRect()
    {
        float r = 30 * PixelScale;
        return new Rect2(BodyCenter - new Vector2(r, r), new Vector2(r * 2, r * 2));
    }

    // ------------------------------------------------------------------ commands

    /// <param name="speed">Art px/s; default is a relaxed stroll, ~60 is a scurry.</param>
    public void Walk(float distance, int dir, float speed = 0)
    {
        if (State != Mode.Grounded) return;
        CurrentGesture = Gesture.None;
        _walkRemaining = Mathf.Max(0, distance);
        _walkDir = dir >= 0 ? 1 : -1;
        _walkSpeedCmd = speed > 0 ? speed : WalkSpeed;
    }

    /// <param name="side">-1 left, 1 right, 0 random (for one-sided gestures).</param>
    public void PlayGesture(Gesture gesture, int side = 0)
    {
        if (State != Mode.Grounded || _jumpQueued) return;
        StopWalking();
        CurrentGesture = gesture;
        _gestureTime = 0;
        _gestureSide = side != 0 ? Math.Sign(side) : (_rng.Randf() < 0.5f ? -1 : 1);
        _gestureDuration = gesture switch
        {
            Gesture.Wave => 1.8f,
            Gesture.Groom => 2.4f,
            Gesture.Tap => 1.8f,
            Gesture.Stretch => 2.6f,
            Gesture.Dance => 2.8f,
            Gesture.Peek => 5f,
            Gesture.Stalk => 1.6f,
            Gesture.Yawn => 2.2f,
            Gesture.Trip => 1.3f,
            Gesture.Hug => 2.4f,
            _ => 0,
        };
    }

    public void StopGesture() => CurrentGesture = Gesture.None;

    /// <summary>
    /// Walks along the chain of linked surfaces to a point on <paramref name="target"/>. Returns false when the
    /// target isn't on the same chain (e.g. another window): the caller can jump instead.
    /// </summary>
    public bool WalkTo(Surface target, float s, float speed = 0)
    {
        if (State != Mode.Grounded || Surface == null) return false;
        if (target == Surface)
        {
            Walk(Mathf.Abs(s - S), s >= S ? 1 : -1, speed);
            return true;
        }
        // Inner corners are cut short by one body height on each side (see CornerMargin).
        float cut = 2 * BodyHeight * PixelScale;
        float forward = Surface.Length - S, backward = S;
        bool foundF = false, foundB = false;
        var prev = Surface;
        var cur = Surface.Next;
        for (int hops = 0; cur != null && hops < 40 && cur != Surface; hops++, prev = cur, cur = cur.Next)
        {
            if (cur.Tangent.Dot(prev.Normal) > 0.5f) forward -= cut;
            if (cur == target) { forward += s; foundF = true; break; }
            forward += cur.Length;
        }
        prev = Surface;
        cur = Surface.Prev;
        for (int hops = 0; cur != null && hops < 40 && cur != Surface; hops++, prev = cur, cur = cur.Prev)
        {
            if (cur.Tangent.Dot(prev.Normal) < -0.5f) backward -= cut;
            if (cur == target) { backward += cur.Length - s; foundB = true; break; }
            backward += cur.Length;
        }
        forward = Mathf.Max(0, forward);
        backward = Mathf.Max(0, backward);
        if (!foundF && !foundB) return false;
        bool goForward = foundF && (!foundB || forward <= backward);
        Walk(goForward ? forward : backward, goForward ? 1 : -1, speed);
        return true;
    }

    /// <summary>
    /// Leaves the surface to crawl around a web being woven. The body follows <see cref="WeavePoint"/>,
    /// with the spinnerets (abdomen) right on it so the thread being laid ends at the spider.
    /// </summary>
    public void BeginWeaving(Vector2 at)
    {
        StopWalking();
        CurrentGesture = Gesture.None;
        _jumpQueued = false;
        _anchorWeb = null;
        _upsideDown = false;
        WeavePoint = at;
        _prevWeavePoint = at;
        State = Mode.Weaving;
        Surface = null;
    }

    /// <summary>Where the spinnerets are while weaving (world).</summary>
    public Vector2 WeavePoint { get; set; }
    Vector2 _prevWeavePoint, _weaveVelocity;

    /// <summary>World position of the spinnerets (top of the abdomen in the frontal view).</summary>
    public Vector2 Spinnerets => BodyCenter + Up * HangOffset * 0.6f * PixelScale;

    /// <summary>Hangs from the hub of a web on a short thread.</summary>
    public void HangFrom(Web web)
    {
        StopWalking();
        CurrentGesture = Gesture.None;
        _anchorWeb = web;
        _upsideDown = false;
        _anchor = web.Hub;
        _ropeLen = Mathf.Max(4 * PixelScale, BodyCenter.DistanceTo(_anchor) - HangOffset * PixelScale);
        _prevBody = BodyCenter;
        _ropeMode = RopeMode.Climbing; // reel in to a short thread, then hold
        State = Mode.Hanging;
        Surface = null;
    }

    public void StopWalking() => _walkRemaining = 0;

    /// <summary>Jump so the feet land on <paramref name="target"/> (world). Crouches briefly first.</summary>
    public void JumpTo(Vector2 target)
    {
        if (State != Mode.Grounded || _jumpQueued) return;
        StopWalking();
        CurrentGesture = Gesture.None;
        _pendingJump = target;
        _resumeWalk = 0;
        _jumpQueued = true;
        // Bigger jumps get a longer wind-up.
        _crouchTimer = Mathf.Clamp(0.12f + Contact.DistanceTo(target) / 3000f, 0.12f, 0.3f);
    }

    /// <summary>Lowers on a silk thread from the current spot (ceilings, walls, or past the end of a ledge).</summary>
    public bool DropOnSilk(bool fast = false)
    {
        if (State != Mode.Grounded || Surface == null) return false;
        StopWalking();
        CurrentGesture = Gesture.None;
        var anchor = Contact;
        if (Surface.IsFloorLike)
        {
            // Only from an edge with nothing linked: swing over it.
            bool atEnd = S >= Surface.Length - 2 && Surface.Next == null;
            bool atStart = S <= 2 && Surface.Prev == null;
            if (!atEnd && !atStart) return false;
            anchor = Contact + Surface.Tangent * (atEnd ? 2 : -2);
        }
        // Never hang on the bezel between two monitors: the body would be split across both screens.
        if (SeamDistance(anchor.X) < SeamMargin) return false;
        _anchor = anchor;
        _anchorWeb = null;
        _anchorExtra = anchor - Contact;
        _anchorOwner = Surface.Owner; _anchorSide = Surface.Side; _anchorOffset = Surface.StartOffset + S;
        _ropeLen = Mathf.Max(4 * PixelScale, BodyCenter.DistanceTo(anchor) - HangOffset * PixelScale);
        _prevBody = BodyCenter;
        _ropeMode = RopeMode.Descending;
        _fastDrop = fast;
        _upsideDown = !fast && _rng.Randf() < UpsideDownChance;
        State = Mode.Hanging;
        Surface = null;
        return true;
    }

    /// <summary>How far (world px) a silk drop must keep from the border between side-by-side monitors.</summary>
    public float SeamMargin => (BodyExtent + 30) * PixelScale;

    /// <summary>
    /// Horizontal distance from <paramref name="x"/> to the nearest border between two monitors standing side by
    /// side (one's right edge touching the other's left edge, with overlapping heights). Infinity with one monitor.
    /// </summary>
    public float SeamDistance(float x)
    {
        float best = float.PositiveInfinity;
        foreach (var a in _workAreas)
            foreach (var b in _workAreas)
            {
                if (a == b || Mathf.Abs(a.End.X - b.Position.X) > 4) continue;
                if (a.Position.Y >= b.End.Y || b.Position.Y >= a.End.Y) continue;
                best = Mathf.Min(best, Mathf.Abs(x - a.End.X));
            }
        return best;
    }

    /// <summary>
    /// Direction along the current surface (1 forward, -1 back) that leads away from a nearby monitor border,
    /// or 0 when not near one or when the surface doesn't run sideways (a wall).
    /// </summary>
    public int AwayFromSeam()
    {
        if (Surface == null || SeamDistance(Contact.X) >= SeamMargin || Mathf.Abs(Surface.Tangent.X) < 0.5f) return 0;
        float d = SeamDistance(Contact.X + Surface.Tangent.X * 10);
        return d > SeamDistance(Contact.X) ? 1 : -1;
    }

    public void ClimbSilk()
    {
        if (State == Mode.Hanging) _ropeMode = RopeMode.Climbing;
    }

    /// <param name="fast">Plummet (alerts) instead of the leisurely lowering.</param>
    public void Descend(bool fast = false)
    {
        if (State != Mode.Hanging) return;
        // Resuming a leisurely descent: sometimes flip over and go down head first.
        if (_ropeMode != RopeMode.Descending && !fast && _rng.Randf() < UpsideDownChance) _upsideDown = true;
        if (fast) _upsideDown = false;
        _ropeMode = RopeMode.Descending;
        _fastDrop = fast;
    }

    public void HoldSilk()
    {
        if (State == Mode.Hanging) _ropeMode = RopeMode.Hold;
    }

    public void StartDrag(Vector2 cursor)
    {
        StopWalking();
        CurrentGesture = Gesture.None;
        _jumpQueued = false;
        _anchorWeb = null;
        _upsideDown = false;
        _anchor = cursor;
        // A good length of silk, so it dangles and swings well below the cursor.
        _ropeLen = 110 + 15 * PixelScale;
        _prevBody = BodyCenter;
        State = Mode.Dragged;
        Surface = null;
    }

    public void DragTo(Vector2 cursor)
    {
        if (State == Mode.Dragged) _anchor = cursor;
    }

    public void EndDrag(float dt)
    {
        if (State != Mode.Dragged) return;
        var v = (BodyCenter - _prevBody) / Mathf.Max(dt, 1f / 240f);
        LaunchAirborne(v.LimitLength(1800));
    }

    /// <summary>Lets go of everything and falls.</summary>
    public void Fall() => LaunchAirborne(Vector2.Zero);

    /// <summary>Thrown off whatever it stands on (a window shaken hard).</summary>
    public void Fling(Vector2 velocity)
    {
        if (State is Mode.Dragged or Mode.Weaving) return;
        BodyCenter += Up * 6 * PixelScale;
        LaunchAirborne(velocity);
    }

    void LaunchAirborne(Vector2 velocity)
    {
        if (State == Mode.Hanging)
            _threads.Add((_anchor, RopeAttach, 1.2f));
        _upsideDown = false;
        State = Mode.Airborne;
        Surface = null;
        _velocity = velocity;
        Contact = BodyCenter - Up * BodyHeight * PixelScale;
    }

    // ------------------------------------------------------------------ world sync

    double _lastResync;

    /// <summary>Call after the surface map is rebuilt: follows moving windows, falls if the ground vanished.</summary>
    public void Resync()
    {
        if (Parked) Respawn();
        else if (State == Mode.Grounded)
        {
            var surf = _map.Find(_keyOwner, _keySide, _keyOffset, out float s);
            if (surf == null) { Fall(); return; }
            // Carried by a moving window: how fast (the map is rebuilt a few times a second).
            double now = Time.GetTicksMsec() / 1000.0;
            if (surf.Owner >= 0 && surf.Owner == Surface?.Owner && !IsWalking)
            {
                float moved = surf.PointAt(s).DistanceTo(Surface.PointAt(S));
                float elapsed = (float)Math.Max(now - _lastResync, 0.05);
                if (moved > 2) Carried?.Invoke(moved / elapsed);
            }
            _lastResync = now;
            Surface = surf;
            S = s;
            KeepOutOfCorners();
        }
        else if (State == Mode.Hanging && _anchorWeb != null)
        {
            if (!_anchorWeb.Alive) { Fall(); return; }
            _anchor = _anchorWeb.Hub;
        }
        else if (State == Mode.Hanging)
        {
            var surf = _map.Find(_anchorOwner, _anchorSide, _anchorOffset, out float s);
            if (surf == null) { Fall(); return; }
            _anchor = surf.PointAt(s) + _anchorExtra;
        }
    }
}
