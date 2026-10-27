using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Webi.World;

namespace Webi.Spider;

public enum Mode { Grounded, Airborne, Hanging, Dragged, Weaving }

public enum Face { Normal, Happy, Sleepy, Sleeping, Surprised, Sad }

/// <summary>Leg-driven gestures played while standing.</summary>
public enum Gesture { None, Wave, Groom, Tap, Stretch, Dance }

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

    public event Action? Landed;
    public event Action? EdgeReached;

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
        _rig = new SpiderRig { Position = new Vector2(ViewportSize / 2, ViewportSize / 2) };
        _viewport.AddChild(_rig);
        _sprite = new Sprite2D { Texture = _viewport.GetTexture(), TextureFilter = TextureFilterEnum.Nearest };
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

    /// <summary>Call after the surface map is rebuilt: follows moving windows, falls if the ground vanished.</summary>
    public void Resync()
    {
        if (State == Mode.Grounded)
        {
            var surf = _map.Find(_keyOwner, _keySide, _keyOffset, out float s);
            if (surf == null) { Fall(); return; }
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

    // ------------------------------------------------------------------ simulation

    public override void _Process(double delta)
    {
        float dt = Mathf.Min((float)delta, 1f / 20f);
        if (_pendingFur != null && _rig.IsInsideTree()) { _rig.SetFur(_pendingFur); _pendingFur = null; }
        _time += dt;

        switch (State)
        {
            case Mode.Grounded: UpdateGrounded(dt); break;
            case Mode.Airborne: UpdateAirborne(dt); break;
            case Mode.Hanging:
            case Mode.Dragged: UpdateRope(dt); break;
            case Mode.Weaving: UpdateWeaving(dt); break;
        }

        UpdateLegs(dt);
        _legsBodyRef = BodyCenter;

        for (int i = _threads.Count - 1; i >= 0; i--)
        {
            var t = _threads[i];
            t.life -= dt;
            if (t.life <= 0) _threads.RemoveAt(i); else _threads[i] = t;
        }

        UpdateVisuals(dt);
        QueueRedraw();
        Snapshot(dt);
    }

    float _snapTimer = 1f;
    int _snapCount;

    /// <summary>
    /// Debug: with WEBI_SNAPSHOT=&lt;dir&gt; saves the rig viewport (x2) at WEBI_SNAPSHOT_FPS (default 2),
    /// up to WEBI_SNAPSHOT_MAX frames (default 40), named after the state/gesture.
    /// </summary>
    void Snapshot(float dt)
    {
        var dir = OS.GetEnvironment("WEBI_SNAPSHOT");
        if (string.IsNullOrEmpty(dir)) return;
        int max = int.TryParse(OS.GetEnvironment("WEBI_SNAPSHOT_MAX"), out var m) ? m : 40;
        float fps = float.TryParse(OS.GetEnvironment("WEBI_SNAPSHOT_FPS"), System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : 2;
        if (_snapCount >= max || (_snapTimer -= dt) > 0) return;
        _snapTimer = 1f / fps;
        var img = _viewport.GetTexture().GetImage();
        img.Resize(img.GetWidth() * 2, img.GetHeight() * 2, Image.Interpolation.Nearest);
        string label = CurrentGesture != Gesture.None ? CurrentGesture.ToString()
            : State == Mode.Hanging ? $"Hanging{_ropeMode}" : State == Mode.Grounded && IsWalking ? "Walk" : State.ToString();
        img.SavePng($"{dir}/snap_{_snapCount++:000}_{label}.png");
    }

    void UpdateGrounded(float dt)
    {
        if (Surface == null) { Fall(); return; }

        if (_jumpQueued)
        {
            _crouchTimer -= dt;
            _crouch = Mathf.MoveToward(_crouch, 5f, dt * 60);
            if (_crouchTimer <= 0)
            {
                _jumpQueued = false;
                var p0 = Contact;
                float dist = p0.DistanceTo(_pendingJump);
                float T = Mathf.Clamp(dist / 750f, 0.32f, 0.85f);
                var g = new Vector2(0, Gravity);
                var v = (_pendingJump - p0 - 0.5f * g * T * T) / T;
                // Push off a little along the normal so the launch surface is cleared.
                BodyCenter += Surface.Normal * 2;
                State = Mode.Airborne;
                Surface = null;
                _velocity = v;
                _crouch = 0;
                Contact = p0 + v.Normalized() * 2;
                return;
            }
        }
        else
        {
            _crouch = Mathf.MoveToward(_crouch, 0, dt * 25);
        }

        if (CurrentGesture != Gesture.None)
        {
            _gestureTime += dt;
            if (_gestureTime >= _gestureDuration) CurrentGesture = Gesture.None;
        }

        if (_walkRemaining > 0 && HopToNextMonitor()) return;

        if (_walkRemaining > 0)
        {
            float step = Mathf.Min(_walkRemaining, _walkSpeedCmd * PixelScale * dt);
            _walkRemaining -= step;
            Advance(step * _walkDir);
            _gaitPhase += step / (PixelScale * StrideFor(_walkSpeedCmd));
        }

        if (Surface == null) return;
        Contact = Surface.PointAt(S);
        _keyOwner = Surface.Owner; _keySide = Surface.Side; _keyOffset = Surface.StartOffset + S;
        // Walking: a gentle dip each time a pair of feet lands (4 per cycle), no bouncing.
        _bob = IsWalking ? Mathf.Cos(_gaitPhase * Mathf.Tau * 4) * 0.5f : Mathf.Sin(_time * 1.8f) * 0.6f;
        _bob += GestureBob();
    }

    /// <summary>
    /// Approaching the border with another monitor: instead of walking across the bezel, jump over to the
    /// other monitor's side and keep walking from there.
    /// </summary>
    bool HopToNextMonitor()
    {
        if (Surface == null || Surface.Owner >= 0) return false;
        var link = _walkDir > 0 ? Surface.Next : Surface.Prev;
        if (link == null || !CrossesMonitors(Surface, link)) return false;
        float toEdge = _walkDir > 0 ? Surface.Length - S : S;
        // Hop before the outermost feet would reach the border.
        if (toEdge > (RestSpread[^1] + 8) * PixelScale) return false;

        // Land far enough in that the feet don't reach back over the border.
        float land = Mathf.Min(link.Length * 0.5f, (RestSpread[^1] + 60) * PixelScale);
        var target = link.PointAt(_walkDir > 0 ? land : link.Length - land);
        float resume = Mathf.Max(0, _walkRemaining - toEdge - land);
        int dir = _walkDir;
        float speed = _walkSpeedCmd;
        JumpTo(target);
        _resumeWalk = resume;
        _resumeDir = dir;
        _resumeSpeed = speed;
        return true;
    }

    /// <summary>
    /// Inner corners (floor meeting a wall) are taken early, one body height before the end, so the body never
    /// pokes into the wall; outer corners (around a window) are walked right up to the edge and wrapped around.
    /// </summary>
    float CornerMargin(Surface? other, bool forward)
    {
        if (other == null || Surface == null) return 0;
        float dot = other.Tangent.Dot(Surface.Normal);
        bool concave = forward ? dot > 0.5f : dot < -0.5f;
        return concave ? BodyHeight * PixelScale : 0;
    }

    void Advance(float d)
    {
        int guard = 0;
        while (Mathf.Abs(d) > 0.0001f && Surface != null && guard++ < 8)
        {
            float endMargin = CornerMargin(Surface.Next, true);
            float startMargin = CornerMargin(Surface.Prev, false);
            float end = Surface.Length - endMargin;
            float target = S + d;
            if (target > end)
            {
                if (Surface.Next != null)
                {
                    d = target - end;
                    var next = Surface.Next;
                    Surface = next;
                    S = endMargin;
                    continue;
                }
                S = Surface.Length; StopWalking(); EdgeReached?.Invoke(); return;
            }
            if (target < startMargin)
            {
                if (Surface.Prev != null)
                {
                    d = target - startMargin;
                    var prev = Surface.Prev;
                    Surface = prev;
                    S = prev.Length - startMargin;
                    continue;
                }
                S = 0; StopWalking(); EdgeReached?.Invoke(); return;
            }
            S = target;
            d = 0;
        }
    }

    /// <summary>Keeps the body out of inner corners after landing or a map rebuild.</summary>
    void KeepOutOfCorners()
    {
        if (Surface == null) return;
        float lo = CornerMargin(Surface.Prev, false), hi = Surface.Length - CornerMargin(Surface.Next, true);
        if (hi > lo) S = Mathf.Clamp(S, lo, hi);
    }

    void UpdateAirborne(float dt)
    {
        _velocity.Y = Mathf.Min(_velocity.Y + Gravity * dt, 2600);
        var p1 = Contact + _velocity * dt;
        var hit = _map.Sweep(Contact, p1, out float t, out float s);
        if (hit != null)
        {
            Surface = hit;
            S = s;
            State = Mode.Grounded;
            KeepOutOfCorners();
            s = S;
            Contact = hit.PointAt(s);
            _keyOwner = hit.Owner; _keySide = hit.Side; _keyOffset = hit.StartOffset + s;
            _crouch = 5f; // landing squash
            _justLanded = true;
            _landingNormal = null;
            _swayVel += Mathf.Clamp(_velocity.Dot(new Vector2(-hit.Normal.Y, hit.Normal.X)) / PixelScale * 0.02f, -6, 6);
            _velocity = Vector2.Zero;
            if (_resumeWalk > 0)
            {
                Walk(_resumeWalk, _resumeDir, _resumeSpeed);
                _resumeWalk = 0;
            }
            Landed?.Invoke();
            return;
        }
        Contact = p1;

        // Anticipate the landing: turn toward the surface that is about to be hit.
        var ahead = _map.Sweep(Contact, Contact + _velocity * 0.14f, out _, out _);
        _landingNormal = ahead?.Normal;

        // Fell out of the world (e.g. through a gap between monitors): drop back in on the primary monitor.
        if (Contact.Y > _bounds.End.Y + 200 || Contact.X < _bounds.Position.X - 200 || Contact.X > _bounds.End.X + 200
            || Contact.Y < _bounds.Position.Y - 1500)
        {
            Contact = new Vector2(Mathf.Clamp(Contact.X, _spawn.Position.X + 40, _spawn.End.X - 40), _spawn.Position.Y + 30);
            _velocity = Vector2.Zero;
        }
    }

    void UpdateWeaving(float dt)
    {
        _weaveVelocity = (WeavePoint - _prevWeavePoint) / Mathf.Max(dt, 0.001f);
        _prevWeavePoint = WeavePoint;
        var desired = WeavePoint - Up * HangOffset * 0.6f * PixelScale;
        BodyCenter = BodyCenter.Lerp(desired, 1 - Mathf.Exp(-30 * dt));
    }

    void UpdateRope(float dt)
    {
        float hang = (State == Mode.Hanging && _upsideDown ? SpiderRig.BodyBottom : HangOffset) * PixelScale;
        if (State == Mode.Hanging)
        {
            if (_ropeMode == RopeMode.Descending) _ropeLen += (_fastDrop ? 700f : 90f) * dt;
            else if (_ropeMode == RopeMode.Climbing) _ropeLen -= 75f * dt;
            if (_ropeMode != RopeMode.Descending) _fastDrop = false;

            if (_anchorWeb != null)
            {
                if (_ropeMode == RopeMode.Climbing && _ropeLen <= 8 * PixelScale) _ropeMode = RopeMode.Hold;
                _ropeLen = Mathf.Max(_ropeLen, 8 * PixelScale);
            }
            else if (_ropeMode == RopeMode.Climbing && _ropeLen <= 2 * PixelScale)
            {
                var surf = _map.Find(_anchorOwner, _anchorSide, _anchorOffset, out float s);
                if (surf != null)
                {
                    Surface = surf; S = s; State = Mode.Grounded;
                    _upsideDown = false;
                    Contact = surf.PointAt(s);
                    _keyOwner = surf.Owner; _keySide = surf.Side; _keyOffset = surf.StartOffset + s;
                    Landed?.Invoke();
                    return;
                }
                Fall();
                return;
            }
        }

        // Verlet point mass on a rope.
        var vel = (BodyCenter - _prevBody) * 0.985f;
        _prevBody = BodyCenter;
        var body = BodyCenter + vel + new Vector2(0, Gravity) * dt * dt;
        float maxLen = _ropeLen + hang;
        var off = body - _anchor;
        if (off.Length() > maxLen) body = _anchor + off.Normalized() * maxLen;
        BodyCenter = body;

        // Touch down while lowering.
        if (State == Mode.Hanging && _ropeMode == RopeMode.Descending)
        {
            var floor = _map.FloorBelow(BodyCenter, out float dist, out float s);
            // Turn the right way up a little before landing, so it touches down on its feet.
            if (_upsideDown && floor != null && dist <= BodyHeight * 4 * PixelScale) _upsideDown = false;
            if (floor != null && dist <= BodyHeight * PixelScale)
            {
                _threads.Add((_anchor, RopeAttach, 1.6f));
                _anchorWeb = null;
                _upsideDown = false;
                Surface = floor; S = Mathf.Clamp(s, 0, floor.Length); State = Mode.Grounded;
                Contact = floor.PointAt(S);
                _keyOwner = floor.Owner; _keySide = floor.Side; _keyOffset = floor.StartOffset + S;
                Landed?.Invoke();
                return;
            }
            if (_ropeLen > _bounds.Size.Y * 1.2f) Fall();
        }
    }

    // ------------------------------------------------------------------ visuals

    void UpdateVisuals(float dt)
    {
        Vector2 targetUp = State switch
        {
            Mode.Grounded when Surface != null => Surface.Normal,
            Mode.Hanging when _upsideDown => (BodyCenter - _anchor).Normalized(),
            Mode.Hanging or Mode.Dragged => (_anchor - BodyCenter).Normalized(),
            Mode.Weaving => Vector2.Up, // face the viewer while crawling over the web
            _ => _landingNormal ?? Vector2.Up,
        };
        if (targetUp == Vector2.Zero) targetUp = Vector2.Up;
        float targetAngle = Mathf.Atan2(targetUp.X, -targetUp.Y);
        float rate = State == Mode.Grounded ? 16f : _landingNormal != null ? 14f : 6f;
        _angle = Mathf.LerpAngle(_angle, targetAngle, 1 - Mathf.Exp(-rate * dt));
        Up = new Vector2(Mathf.Sin(_angle), -Mathf.Cos(_angle));

        UpdateSecondaryMotion(dt);
        var right = new Vector2(-Up.Y, Up.X);
        if (State is Mode.Grounded or Mode.Airborne)
            BodyCenter = Contact + (Up * (BodyHeight - _crouch + _bob) + right * (State == Mode.Grounded ? _sway : 0)) * PixelScale;
        float visualAngle = _angle + _tilt;

        // Never let the body leave the screen (hanging off an edge, weaving into a corner, being flung...).
        var onScreen = KeepOnScreen(BodyCenter);
        if (onScreen != BodyCenter)
        {
            if (State is Mode.Hanging or Mode.Dragged) _prevBody += onScreen - BodyCenter; // no bounce
            BodyCenter = onScreen;
        }

        // Pixel-grid aligned display so silk, body and legs share one grid.
        var snapped = (BodyCenter / PixelScale).Round() * PixelScale;
        _sprite.Position = snapped;
        _sprite.Scale = new Vector2(PixelScale, PixelScale);

        _rig.Angle = visualAngle;
        _rig.Expression = Expression == Face.Normal && CurrentGesture is Gesture.Stretch or Gesture.Dance ? Face.Happy : Expression;
        _rig.Fidget = Fidget || CurrentGesture == Gesture.Groom || State == Mode.Weaving;
        _rig.Speaking = Speaking;
        _rig.Time = _time;
        _rig.Pose = _crouch > 1.2f ? BodyPose.Squash
            : State == Mode.Airborne && _velocity.Y < -250 ? BodyPose.Stretch
            : BodyPose.Normal;

        Vector2 look;
        if (LookTarget is Vector2 lt) look = (lt - BodyCenter).Rotated(-visualAngle);
        else if (State == Mode.Hanging) look = new Vector2(0, 60);
        else if (State == Mode.Airborne) look = _velocity.Rotated(-visualAngle);
        else if (IsWalking) look = new Vector2(_walkDir * 60, 0);
        else look = Vector2.Zero;
        _rig.Look = look.Length() > 1 ? look.Normalized() * Mathf.Clamp(look.Length() / 220f, 0.3f, 1f) : Vector2.Zero;

        for (int i = 0; i < 8; i++)
            _rig.Feet[i] = (_legs[i].Foot - snapped).Rotated(-visualAngle) / PixelScale;
    }

    /// <summary>Spring-damped sway along the surface driven by speed changes, plus lean.</summary>
    void UpdateSecondaryMotion(float dt)
    {
        float speed = State == Mode.Grounded && IsWalking ? _walkDir * _walkSpeedCmd : 0;
        float accel = (speed - _prevSpeed) / Mathf.Max(dt, 0.001f);
        _prevSpeed = speed;
        // Body lags behind when starting, overshoots when stopping (art px).
        _swayVel += (-90f * _sway - 10f * _swayVel - Mathf.Clamp(accel, -3000, 3000) * 0.012f) * dt;
        _sway = Mathf.Clamp(_sway + _swayVel * dt, -6, 6);

        float targetTilt = State switch
        {
            Mode.Grounded => (IsWalking ? _walkDir * 0.06f + Mathf.Sin(_gaitPhase * Mathf.Tau * 2) * 0.03f : 0)
                + _sway * 0.025f + GestureTilt(),
            Mode.Airborne => Mathf.Clamp(_velocity.X / 2600f, -0.35f, 0.35f),
            Mode.Weaving => Mathf.Clamp(_weaveVelocity.X / 4000f, -0.25f, 0.25f),
            _ => 0,
        };
        _tilt = Mathf.Lerp(_tilt, targetTilt, 1 - Mathf.Exp(-12 * dt));
    }

    // ------------------------------------------------------------------ gestures

    /// <summary>0→1→0 envelope over the gesture so limbs ease in and out.</summary>
    float GestureEnvelope() =>
        Mathf.Clamp(Mathf.Min(_gestureTime / 0.25f, (_gestureDuration - _gestureTime) / 0.3f), 0, 1);

    float GestureBob()
    {
        float t = _gestureTime, env = GestureEnvelope();
        return CurrentGesture switch
        {
            Gesture.Dance => -Mathf.Abs(Mathf.Sin(t * 8)) * 3f * env,
            Gesture.Stretch => Mathf.Sin(t / _gestureDuration * Mathf.Pi) * 3f - (t < 0.4f ? Mathf.Sin(t / 0.4f * Mathf.Pi) * 3 : 0),
            Gesture.Groom => -1.5f * env,
            _ => 0,
        };
    }

    float GestureTilt()
    {
        float t = _gestureTime, env = GestureEnvelope();
        return CurrentGesture switch
        {
            Gesture.Wave => -_gestureSide * 0.09f * env,
            Gesture.Dance => Mathf.Sin(t * 8) * 0.12f * env,
            Gesture.Tap => _gestureSide * 0.04f * env,
            _ => 0,
        };
    }

    /// <summary>
    /// Moves a foot between two poses along an arc around its hip (angle and radius interpolated, radius kept
    /// large). A straight line would pass close to the hip, where the long tibia forces the leg to fold across the face.
    /// </summary>
    static Vector2 ArcLerp(in Leg leg, Vector2 from, Vector2 to, float t)
    {
        var hip = new Vector2(SpiderRig.Hips[leg.Index].X * leg.Side, SpiderRig.Hips[leg.Index].Y);
        var a = from - hip;
        var b = to - hip;
        float angle = Mathf.LerpAngle(a.Angle(), b.Angle(), t);
        float radius = Mathf.Max(Mathf.Lerp(a.Length(), b.Length(), t), 0.62f * (SpiderRig.FemurLength[leg.Index] + SpiderRig.TibiaLength[leg.Index]));
        if (t <= 0) return from;
        return hip + Vector2.FromAngle(angle) * radius;
    }

    /// <summary>Local (art px, body frame) foot target when the current gesture drives this leg.</summary>
    bool GestureFoot(in Leg leg, out Vector2 local)
    {
        float t = _gestureTime, env = GestureEnvelope();
        int side = leg.Side, idx = leg.Index;
        var rest = new Vector2(side * RestSpread[idx], BodyHeight);
        local = rest;
        switch (CurrentGesture)
        {
            case Gesture.Wave when idx == 0 && side == _gestureSide:
            {
                // Raised up and out to the side (never across the face), waving back and forth.
                var up = new Vector2(side * (36 + Mathf.Sin(t * 11) * 6), -34 + Mathf.Cos(t * 11) * 3);
                local = ArcLerp(leg, rest, up, env);
                return true;
            }
            case Gesture.Groom when idx == 0:
            {
                // Front feet rub the pedipalps, low and close to the body.
                var face = new Vector2(side * (13 + Mathf.Sin(t * 15 + side) * 2.5f), 25 + Mathf.Cos(t * 15) * 3);
                local = rest.Lerp(face, env);
                return true;
            }
            case Gesture.Tap when idx == 1 && side == _gestureSide:
                local = rest + new Vector2(side * 2, -Mathf.Max(0, Mathf.Sin(t * 13)) * 11 * env);
                return true;
            case Gesture.Stretch:
            {
                // Legs reach out one pair at a time, front to back.
                float u = t / _gestureDuration;
                float phase = Mathf.Clamp((u - idx * 0.14f) / 0.38f, 0, 1);
                float amount = Mathf.Sin(phase * Mathf.Pi);
                local = rest + new Vector2(side * 14 * amount, -16 * amount);
                return amount > 0.001f;
            }
            case Gesture.Dance when idx <= 1:
            {
                float beat = Mathf.Sin(t * 8 + (side > 0 ? Mathf.Pi : 0));
                float lift = Mathf.Max(0, beat) * (idx == 0 ? 1 : 0.5f);
                local = ArcLerp(leg, rest, new Vector2(side * (37 - idx * 1), -28 - idx * 4), lift * env);
                return true;
            }
        }
        return false;
    }

    public override void _Draw()
    {
        var silk = new Color(0.93f, 0.92f, 0.88f, 0.95f);
        if (State is Mode.Hanging or Mode.Dragged)
            DrawPixelLine(_anchor, State == Mode.Hanging ? RopeAttach : BodyCenter + Up * HangOffset * PixelScale, silk);
        // While weaving, the thread being laid runs into the spinnerets (they can be a bit off when the body is
        // kept on screen near an edge).
        if (State == Mode.Weaving && Spinnerets.DistanceTo(WeavePoint) > PixelScale)
            DrawPixelLine(WeavePoint, Spinnerets, silk);
        foreach (var (a, b, life) in _threads)
            DrawPixelLine(a, b, new Color(silk, Mathf.Clamp(life, 0, 1) * 0.8f));
    }

    void DrawPixelLine(Vector2 a, Vector2 b, Color c) => PixelDraw.Line(this, a, b, c, PixelScale);

    // ------------------------------------------------------------------ legs

    struct Leg
    {
        public int Side, Index;
        /// <summary>Offset of this leg in the gait cycle (0..1).</summary>
        public float Phase;
        public Vector2 Foot, From, To;
        public float T;
        public bool Stepping;
    }

    /// <summary>Where each leg's foot rests, in art px along the surface from the contact point.</summary>
    static readonly float[] RestSpread = { 30f, 38f, 46f, 54f };

    /// <summary>
    /// Gait: a wave that runs back to front on each side, the two sides half a cycle apart
    /// (L4 R2 · L3 R1 · L2 R4 · L1 R3), so exactly two feet are in the air at any moment.
    /// </summary>
    static readonly float[] GaitOffset = { 0.25f, 0.5f, 0.75f, 0f };
    /// <summary>Fraction of the cycle a foot spends in the air.</summary>
    const float SwingFraction = 0.25f;

    /// <summary>Distance the body travels per gait cycle (art px): longer strides when going faster.</summary>
    static float StrideFor(float speed) => Mathf.Lerp(30f, 40f, Mathf.Clamp((speed - 20f) / 44f, 0, 1));

    float _gaitPhase;

    void UpdateLegs(float dt)
    {
        int s = PixelScale;
        if (State == Mode.Grounded && Surface != null)
        {
            float speed = IsWalking ? _walkSpeedCmd : WalkSpeed;
            // Planted feet sweep from half a stance ahead of their rest spot to half a stance behind it.
            float stanceHalf = (1 - SwingFraction) * StrideFor(speed) * s / 2;
            float lift = Mathf.Lerp(7f, 10f, Mathf.Clamp((speed - 20f) / 44f, 0, 1)) * s;
            var bodyAngle = _angle + _tilt;
            int stepping = 0;
            for (int i = 0; i < 8; i++) if (_legs[i].Stepping) stepping++;

            int settleCandidate = -1;
            float worstError = 4f * s;

            for (int i = 0; i < 8; i++)
            {
                ref var leg = ref _legs[i];
                float restS = S + leg.Side * RestSpread[leg.Index] * s;
                if (_justLanded)
                {
                    // Impact: legs splay out a bit wider than rest, then settle.
                    leg.Foot = ChainPoint(Surface, S + leg.Side * RestSpread[leg.Index] * 1.1f * s);
                    leg.Stepping = false;
                    continue;
                }
                if (GestureFoot(leg, out var gestureLocal))
                {
                    leg.Stepping = false;
                    var world = BodyCenter + gestureLocal.Rotated(bodyAngle) * s;
                    leg.Foot = leg.Foot.Lerp(world, 1 - Mathf.Exp(-22 * dt));
                    continue;
                }

                if (IsWalking)
                {
                    float ph = Mathf.PosMod(_gaitPhase + leg.Phase, 1f);
                    if (ph < SwingFraction)
                    {
                        if (!leg.Stepping) { leg.Stepping = true; leg.From = leg.Foot; }
                        leg.T = ph / SwingFraction;
                        leg.To = ChainPoint(Surface, restS + _walkDir * stanceHalf);
                        leg.Foot = SwingPosition(leg, lift);
                    }
                    else if (leg.Stepping)
                    {
                        leg.Stepping = false;
                        leg.Foot = leg.To; // plant
                    }
                    else if (leg.Foot.DistanceTo(ChainPoint(Surface, restS)) > 2.2f * stanceHalf + 6 * s)
                    {
                        leg.Foot = ChainPoint(Surface, restS); // teleported (window moved): replant
                    }
                    continue;
                }

                // Standing: finish any step in the air, then let feet settle one at a time.
                var rest = ChainPoint(Surface, restS);
                if (leg.Stepping)
                {
                    leg.T += dt / 0.11f;
                    leg.To = rest;
                    if (leg.T >= 1) { leg.Stepping = false; leg.Foot = rest; }
                    else leg.Foot = SwingPosition(leg, lift * 0.7f);
                    continue;
                }
                float error = leg.Foot.DistanceTo(rest);
                if (error > 70 * s) leg.Foot = rest; // teleported
                else if (error > worstError) { worstError = error; settleCandidate = i; }
            }

            if (!IsWalking && stepping == 0 && settleCandidate >= 0)
            {
                ref var leg = ref _legs[settleCandidate];
                leg.Stepping = true; leg.T = 0; leg.From = leg.Foot;
                leg.To = ChainPoint(Surface, S + leg.Side * RestSpread[leg.Index] * s);
            }
            if (OS.HasEnvironment("WEBI_GAIT_LOG") && IsWalking)
            {
                var air = new System.Text.StringBuilder();
                for (int i = 0; i < 8; i++) if (_legs[i].Stepping) air.Append(i < 4 ? $"L{_legs[i].Index + 1} " : $"R{_legs[i].Index + 1} ");
                GD.Print($"[gait] {_gaitPhase:0.000} air={air}");
            }
            _justLanded = false;
        }
        else
        {
            var bodyAngle = _angle + _tilt;
            var carried = BodyCenter - _legsBodyRef; // feet travel with the body, then ease toward the pose
            for (int i = 0; i < 8; i++)
            {
                ref var leg = ref _legs[i];
                leg.Stepping = false;
                leg.Foot += carried;
                var local = AirLegPose(leg);
                var world = BodyCenter + local.Rotated(bodyAngle) * s;
                leg.Foot = leg.Foot.Lerp(world, 1 - Mathf.Exp(-(State == Mode.Airborne ? 14 : 18) * dt));
            }
        }
    }

    /// <summary>Leg poses off the ground, in local art px.</summary>
    Vector2 AirLegPose(in Leg leg)
    {
        int side = leg.Side, idx = leg.Index;
        float spread = RestSpread[idx];
        float phase = _time + idx * 0.9f + (side > 0 ? 0.45f : 0);
        switch (State)
        {
            case Mode.Airborne:
            {
                // Tucked on the way up, spread wide and reaching down to land on the way down.
                float fall = Mathf.Clamp(_velocity.Y / 900f, -1, 1) * 0.5f + 0.5f;
                var tucked = new Vector2(side * spread * 0.8f, 22 + idx * 1.5f);
                var spreadOut = new Vector2(side * spread * 1.1f, BodyHeight + 2 - idx * 3);
                return tucked.Lerp(spreadOut, fall) + new Vector2(0, Mathf.Sin(phase * 9) * 1.5f);
            }
            case Mode.Dragged:
                return new Vector2(side * (spread * 0.9f + Mathf.Sin(phase * 14) * 6),
                    BodyHeight * 0.5f + idx * 3 + Mathf.Cos(phase * 14) * 6);
            case Mode.Weaving:
            {
                // Clambering over the threads: legs reach out in a quick alternating rhythm, faster when moving.
                float speed = Mathf.Clamp(_weaveVelocity.Length() / 300f, 0.3f, 3f);
                float beat = _time * 9 * speed + idx * 1.6f + (side > 0 ? Mathf.Pi : 0);
                return new Vector2(side * (spread * 0.95f + Mathf.Sin(beat) * 5),
                    BodyHeight * 0.35f + idx * 2 + Mathf.Cos(beat) * 5 - (idx <= 1 ? 6 : 0));
            }
            default: // hanging on silk
                if (_upsideDown)
                {
                    // Head down: the front legs hold the thread above, the others wave up around it.
                    if (idx <= 1)
                    {
                        float grip = Mathf.Sin(_time * 3 + (side > 0 ? Mathf.Pi : 0) + idx) * 2;
                        return new Vector2(side * (4 + idx * 3), SpiderRig.BodyBottom + 4 + grip);
                    }
                    return new Vector2(side * (spread * 0.8f + Mathf.Sin(phase * 2.5f) * 3),
                        BodyHeight * 0.7f + idx * 2 + Mathf.Cos(phase * 2.5f) * 3);
                }
                if (_ropeMode == RopeMode.Climbing && idx <= 1)
                {
                    // Front legs pull the thread hand over hand.
                    float pull = Mathf.Sin(_time * 10 + (side > 0 ? Mathf.Pi : 0) + idx * 1.3f);
                    return new Vector2(side * (5 + idx * 3), -HangOffset - 4 - pull * 6);
                }
                if (_ropeMode == RopeMode.Descending)
                    return new Vector2(side * spread * 0.95f, BodyHeight * 0.55f + idx * 3 + Mathf.Sin(phase * 3) * 2);
                // Holding: slow paddling.
                return new Vector2(side * (spread * 0.85f + Mathf.Sin(phase * 2.5f) * 3),
                    BodyHeight * 0.65f + idx * 3 + Mathf.Cos(phase * 2.5f) * 3);
        }
    }

    Vector2 _legsBodyRef;

    /// <summary>Foot in the air: eases toward the target, lifts off the surface and reaches out a little mid-step.</summary>
    Vector2 SwingPosition(in Leg leg, float lift)
    {
        float u = Mathf.Clamp(leg.T, 0, 1);
        var p = leg.From.Lerp(leg.To, Ease(u));
        float arc = Mathf.Sin(u * Mathf.Pi);
        var outward = new Vector2(-Up.Y, Up.X) * leg.Side;
        return p + Up * arc * lift + outward * arc * 3f * PixelScale;
    }

    static float Ease(float t) => t * t * (3 - 2 * t);

    /// <summary>Point at arc length <paramref name="s"/> along the surface chain (follows linked corners).</summary>
    /// <remarks>Never crosses from one monitor to another: feet stop at the border instead of reaching over the bezel.</remarks>
    static Vector2 ChainPoint(Surface surf, float s)
    {
        for (int i = 0; i < 3; i++)
        {
            if (s > surf.Length && surf.Next != null && !CrossesMonitors(surf, surf.Next)) { s -= surf.Length; surf = surf.Next; continue; }
            if (s < 0 && surf.Prev != null && !CrossesMonitors(surf, surf.Prev)) { surf = surf.Prev; s += surf.Length; continue; }
            break;
        }
        return surf.PointAt(Mathf.Clamp(s, 0, surf.Length));
    }

    /// <summary>Two screen-border pieces of different monitors (owners are -(monitor + 1)).</summary>
    static bool CrossesMonitors(Surface a, Surface b) => a.Owner < 0 && b.Owner < 0 && a.Owner != b.Owner;
}
