using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Webi.World;

namespace Webi.Spider;

/// <summary>Procedural legs: wave gait while walking, poses in the air and on silk, IK targets for the rig.</summary>
public partial class Spider
{
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
                if (_clinging)
                {
                    // Holding on to the pointer: the front legs grip it above, the rest hang still and only
                    // trail behind as it swings (the feet ease toward the pose, so the swing drags them).
                    if (idx <= 1) return new Vector2(side * (4 + idx * 3), -HangOffset - 4);
                    return new Vector2(side * spread * 0.8f, BodyHeight * 0.6f + idx * 3 + Mathf.Sin(_time * 1.2f + idx) * 0.5f);
                }
                // Picked up by the user: kicking about.
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
