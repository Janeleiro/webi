using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Webi.World;

namespace Webi.Spider;

/// <summary>Simulation: walking along edges, hopping monitors, flying, respawning, weaving and the silk rope.</summary>
public partial class Spider
{
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
        UpdateZs(dt);
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
        if (DebugEnv.SnapshotDir is not { } dir) return;
        if (_snapCount >= DebugEnv.SnapshotMax || (_snapTimer -= dt) > 0) return;
        _snapTimer = 1f / DebugEnv.SnapshotFps;
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
                Jumped?.Invoke();
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
        if (Parked) return;
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

        // Fell out of the world (e.g. through a gap between monitors): drop back in where there is ground.
        if (Contact.Y > _bounds.End.Y + 200 || Contact.X < _bounds.Position.X - 200 || Contact.X > _bounds.End.X + 200
            || Contact.Y < _bounds.Position.Y - 1500)
            Respawn();
    }

    /// <summary>Where along a work area's width to try dropping in, best first.</summary>
    static readonly float[] DropSpots = { 0.7f, 0.5f, 0.3f, 0.85f, 0.15f };

    /// <summary>Leaves wherever it is and drops in again (e.g. its monitor was taken by a fullscreen game).</summary>
    public void Relocate()
    {
        if (State is Mode.Dragged or Mode.Weaving) return;
        Respawn();
    }

    /// <summary>
    /// Drops in from the top of a work area (the spawn one first) above a floor that really exists, so it can
    /// never fall forever. With no floor anywhere it parks out of sight until one appears.
    /// </summary>
    void Respawn()
    {
        if (State != Mode.Airborne) LaunchAirborne(Vector2.Zero);
        _velocity = Vector2.Zero;
        _landingNormal = null;
        _walkRemaining = 0;
        _resumeWalk = 0;
        foreach (var area in _workAreas.Prepend(_spawn))
            foreach (float f in DropSpots)
            {
                var p = new Vector2(area.Position.X + area.Size.X * f, area.Position.Y + 30);
                if (!_workAreas.Any(w => w.HasPoint(p)) || _map.FloorBelow(p, out _, out _) == null) continue;
                Contact = p;
                Parked = false;
                return;
            }
        Parked = true;
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
}
