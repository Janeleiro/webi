using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Webi.World;

namespace Webi.Spider;

/// <summary>Body tilt, squash and bob, the gestures and the silk threads drawn around the rig.</summary>
public partial class Spider
{
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

        // Peeking: everything below the edge it stands on is hidden (it is "behind" the window or taskbar).
        bool peeking = CurrentGesture == Gesture.Peek && State == Mode.Grounded && Surface is { IsFloorLike: true };
        _sprite.RegionEnabled = peeking;
        if (peeking)
        {
            float line = ViewportSize / 2f + (Contact.Y - snapped.Y) / PixelScale;
            _sprite.RegionRect = new Rect2(0, 0, ViewportSize, Mathf.Clamp(Mathf.Floor(line), 0, ViewportSize));
        }

        _rig.Angle = visualAngle;
        _rig.Expression = Expression != Face.Normal ? Expression : CurrentGesture switch
        {
            Gesture.Stretch or Gesture.Dance or Gesture.Hug => Face.Happy,
            Gesture.Yawn => Face.Sleepy,
            Gesture.Stalk or Gesture.Trip => Face.Surprised,
            _ => Face.Normal,
        };
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
            Gesture.Peek => -20f * env,
            Gesture.Stalk => -5f * env + Mathf.Sin(t * 22) * 0.6f * env,
            Gesture.Yawn => Mathf.Sin(Mathf.Clamp(t / _gestureDuration, 0, 1) * Mathf.Pi) * 3f,
            Gesture.Trip => t < 0.25f ? -7f * (t / 0.25f) : -7f * Mathf.Max(0, 1 - (t - 0.25f) / 0.6f),
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
            // The abdomen wags side to side, like a cat about to pounce.
            Gesture.Stalk => Mathf.Sin(t * 18) * 0.06f * env,
            Gesture.Trip => _gestureSide * 0.5f * (t < 0.2f ? t / 0.2f : Mathf.Max(0, 1 - (t - 0.2f) / 0.7f)) + Mathf.Sin(t * 30) * 0.05f * env,
            Gesture.Yawn => -0.05f * env,
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
            case Gesture.Peek:
            {
                // Front feet grip the edge (the surface line moves up as the body sinks); the rest hang
                // below it, out of sight.
                float line = BodyHeight + GestureBob();
                local = idx == 0
                    ? new Vector2(side * 24, line - 1)
                    : new Vector2(side * RestSpread[idx] * 0.55f, BodyHeight + 6 + idx * 2);
                return true;
            }
            case Gesture.Stalk when idx == 0:
            {
                // Front legs raised in front, twitching.
                var up = new Vector2(side * 26, 12 + Mathf.Sin(t * 16 + side) * 2);
                local = ArcLerp(leg, rest, up, env);
                return true;
            }
            case Gesture.Yawn when idx == 0:
            {
                var up = new Vector2(side * 34, -26);
                local = ArcLerp(leg, rest, up, Mathf.Sin(Mathf.Clamp(t / _gestureDuration, 0, 1) * Mathf.Pi));
                return true;
            }
            case Gesture.Trip when idx <= 1:
            {
                // Flailing for balance.
                float flail = Mathf.Sin(t * 24 + idx * 2 + (side > 0 ? 1 : 0));
                local = ArcLerp(leg, rest, new Vector2(side * (34 + flail * 6), -6 - idx * 8 + flail * 8), env);
                return true;
            }
            case Gesture.Hug when idx == 0:
            {
                // Arms open wide, then close in front of the body and squeeze.
                float u = Mathf.Clamp(t / _gestureDuration, 0, 1);
                float close = Mathf.Clamp((u - 0.25f) / 0.3f, 0, 1);
                var open = new Vector2(side * 38, -18);
                var hug = new Vector2(side * (8 + Mathf.Sin(t * 10) * 1.5f), 6);
                local = ArcLerp(leg, rest, open.Lerp(hug, close), env);
                return true;
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
        DrawZs();
    }

    // ------------------------------------------------------------------ sleeping Zs

    /// <summary>Little "Z"s floating up from the head while asleep.</summary>
    readonly List<(Vector2 pos, float age, int size)> _zs = new();
    float _zTimer;

    // 5x5 pixel "Z".
    static readonly string[] ZGlyph = { "#####", "...#.", "..#..", ".#...", "#####" };

    void UpdateZs(float dt)
    {
        if (Expression == Face.Sleeping && Visible)
        {
            _zTimer -= dt;
            if (_zTimer <= 0)
            {
                _zTimer = 1.4f;
                _zs.Add((BodyCenter + new Vector2(14, -34) * PixelScale, 0, _zs.Count % 2 == 0 ? 1 : 2));
            }
        }
        for (int i = _zs.Count - 1; i >= 0; i--)
        {
            var z = _zs[i];
            z.age += dt;
            z.pos += new Vector2(Mathf.Sin(z.age * 3) * 8, -22) * dt * PixelScale * 0.5f;
            if (z.age > 2.6f) _zs.RemoveAt(i); else _zs[i] = z;
        }
    }

    void DrawZs()
    {
        var ink = new Color("3a2418");
        foreach (var (pos, age, size) in _zs)
        {
            float alpha = Mathf.Clamp(Mathf.Min(age / 0.3f, (2.6f - age) / 0.8f), 0, 1);
            int px = PixelScale * size;
            var origin = (pos / PixelScale).Round() * PixelScale;
            for (int y = 0; y < 5; y++)
                for (int x = 0; x < 5; x++)
                    if (ZGlyph[y][x] == '#')
                    {
                        // Light fill with a dark outline pixel below-right, readable on any background.
                        DrawRect(new Rect2(origin + new Vector2(x + 0.5f, y + 0.5f) * px, new Vector2(px, px)), new Color(ink, alpha * 0.8f));
                        DrawRect(new Rect2(origin + new Vector2(x, y) * px, new Vector2(px, px)), new Color(1, 1, 1, alpha));
                    }
        }
    }

    void DrawPixelLine(Vector2 a, Vector2 b, Color c) => PixelDraw.Line(this, a, b, c, PixelScale);
}
