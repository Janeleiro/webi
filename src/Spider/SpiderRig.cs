using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace Webi.Spider;

public enum BodyPose { Normal, Squash, Stretch }

/// <summary>
/// Draws the spider from the sprite parts in art/parts (cut by tools/slice_parts.py) inside the low-res
/// SubViewport: back legs, body, front legs, eyes. Origin is the body centre, +Y points toward the surface
/// (the node is rotated by <see cref="Angle"/>). Legs are femur + tibia sprites posed by two-bone IK.
/// </summary>
public partial class SpiderRig : Node2D
{
    public float Angle;
    public Face Expression;
    public bool Fidget;
    public bool Speaking;
    public float Time;
    public BodyPose Pose;
    /// <summary>Eye direction in local space, length 0..1.</summary>
    public Vector2 Look;
    /// <summary>Foot positions in local art pixels (0-3 left, 4-7 right; 0/4 = front pair).</summary>
    public readonly Vector2[] Feet = new Vector2[8];

    /// <summary>Body box bottom in local px: every body pose is drawn standing on this line.</summary>
    public const float BodyBottom = 30f;

    // Hip positions (right side; left mirrors X), front pair first.
    public static readonly Vector2[] Hips = { new(21, 14), new(22, 7), new(22, 0), new(20, -7) };
    public static float[] FemurLength { get; } = { 18, 21, 23, 25 };
    public static float[] TibiaLength { get; } = { 34, 42, 49, 56 };

    // Neutral darkening for the far legs, so it works with any coat colour.
    static readonly Color BackTint = new(0.7f, 0.7f, 0.72f);

    readonly Dictionary<string, Texture2D> _tex = new();
    /// <summary>Original (brown) part images, recoloured into <see cref="_tex"/> for the chosen coat.</summary>
    readonly Dictionary<string, Image> _source = new();
    FurColor _fur = FurColors.All[0];
    readonly Dictionary<string, Vector2> _pivot = new();
    Vector2 _eyeL = new(0.345f, 0.6f), _eyeR = new(0.655f, 0.6f);
    Vector2 _sideL = new(0.13f, 0.55f), _sideR = new(0.87f, 0.55f);
    Vector2 _palpL = new(0.37f, 0.89f), _palpR = new(0.63f, 0.89f);

    float _blink, _nextBlink = 2f;
    readonly RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.Nearest;
        LoadParts("res://art/parts");
        SetFur(_fur);
    }

    /// <summary>Loads the part textures plus pivots/lengths from parts.json.</summary>
    void LoadParts(string dir)
    {
        foreach (var name in new[]
                 {
                     "body", "body_squash", "body_stretch", "palp_l", "palp_r",
                     "eye_open", "eye_half", "eye_closed", "eye_happy", "eye_wide", "eye_side",
                     "femur0", "femur1", "femur2", "femur3", "tibia0", "tibia1", "tibia2", "tibia3",
                 })
        {
            var tex = LoadTexture($"{dir}/{name}.png");
            if (tex != null) _source[name] = tex.GetImage();
            else GD.PushWarning($"parte da aranha não encontrada: {dir}/{name}.png");
        }

        var json = $"{dir}/parts.json";
        if (!FileAccess.FileExists(json)) return;
        try
        {
            using var doc = JsonDocument.Parse(FileAccess.GetFileAsString(json));
            var root = doc.RootElement;
            static Vector2 V(JsonElement e) => new(e[0].GetSingle(), e[1].GetSingle());
            foreach (var part in root.GetProperty("parts").EnumerateObject())
            {
                if (part.Value.TryGetProperty("pivot", out var pv)) _pivot[part.Name] = V(pv);
                if (part.Value.TryGetProperty("length", out var len))
                {
                    int i = part.Name[^1] - '0';
                    if (part.Name.StartsWith("femur")) FemurLength[i] = len.GetSingle();
                    else if (part.Name.StartsWith("tibia")) TibiaLength[i] = len.GetSingle();
                }
            }
            if (root.TryGetProperty("eyes", out var eyes))
            {
                _eyeL = V(eyes.GetProperty("left")); _eyeR = V(eyes.GetProperty("right"));
                _sideL = V(eyes.GetProperty("side_left")); _sideR = V(eyes.GetProperty("side_right"));
            }
            if (root.TryGetProperty("palps", out var palps))
            {
                _palpL = V(palps.GetProperty("left")); _palpR = V(palps.GetProperty("right"));
            }
        }
        catch (Exception e)
        {
            GD.PushWarning($"parts.json: {e.Message}");
        }
    }

    /// <summary>
    /// The imported texture when there is one (always the case in an exported build, where the source PNG is
    /// not packed), otherwise the PNG read directly (a fresh art file the editor has not imported yet).
    /// </summary>
    /// <summary>Recolours every part for the coat (eyes get the coat's own eye colours).</summary>
    public void SetFur(FurColor fur)
    {
        _fur = fur;
        foreach (var (name, image) in _source)
        {
            var recolored = name switch
            {
                "eye_open" or "eye_wide" or "eye_side" => FurColors.RecolorEye(image, fur, lid: 0),
                "eye_half" => FurColors.RecolorEye(image, fur, lid: 1),
                "eye_closed" or "eye_happy" => FurColors.RecolorEye(image, fur, lid: 2),
                _ => FurColors.RecolorFur(image, fur),
            };
            _tex[name] = ImageTexture.CreateFromImage(recolored);
        }
    }

    public static Texture2D? LoadTexture(string path)
    {
        if (ResourceLoader.Exists(path)) return GD.Load<Texture2D>(path);
        if (!FileAccess.FileExists(path)) return null;
        var img = Image.LoadFromFile(path);
        return img == null ? null : ImageTexture.CreateFromImage(img);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _nextBlink -= dt;
        if (_nextBlink <= 0)
        {
            _blink = 1;
            _nextBlink = _rng.RandfRange(2.5f, 6f);
            if (_rng.Randf() < 0.2f) _nextBlink = 0.3f; // double blink
        }
        _blink = Mathf.MoveToward(_blink, 0, dt * 6);
        Rotation = Angle;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!_tex.TryGetValue("body", out var normalBody))
        {
            DrawCircle(Vector2.Zero, 24, new Color("b07a45"));
            return;
        }

        for (int idx = 3; idx >= 2; idx--) { DrawLeg(idx, -1, true); DrawLeg(idx, 1, true); }

        var body = Pose switch
        {
            BodyPose.Squash when _tex.TryGetValue("body_squash", out var sq) => sq,
            BodyPose.Stretch when _tex.TryGetValue("body_stretch", out var st) => st,
            _ => normalBody,
        };
        var size = body.GetSize();
        var box = new Rect2(new Vector2(-Mathf.Floor(size.X / 2), BodyBottom - size.Y), size);
        DrawTexture(body, box.Position);

        DrawEyes(box);
        DrawPalps(box);

        for (int idx = 1; idx >= 0; idx--) { DrawLeg(idx, -1, false); DrawLeg(idx, 1, false); }
    }

    // ------------------------------------------------------------------ face

    static Vector2 At(Rect2 box, Vector2 frac) => box.Position + box.Size * frac;

    void DrawEyes(Rect2 box)
    {
        string eye = Expression switch
        {
            Face.Sleeping => "eye_closed",
            Face.Sleepy or Face.Sad => _blink > 0.5f ? "eye_closed" : "eye_half",
            Face.Happy => "eye_happy",
            Face.Surprised => "eye_wide",
            _ => _blink > 0.66f ? "eye_closed" : _blink > 0.2f ? "eye_half" : "eye_open",
        };
        if (!_tex.TryGetValue(eye, out var tex)) return;

        // Open eyes follow the gaze by a pixel; closed eyes stay put.
        var shift = eye is "eye_open" or "eye_wide" ? (Look * 1.4f).Round() : Vector2.Zero;
        foreach (var anchor in new[] { _eyeL, _eyeR })
        {
            var c = At(box, anchor) + shift;
            DrawTexture(tex, (c - tex.GetSize() / 2).Floor());
        }

        if (_tex.TryGetValue("eye_side", out var side) && eye != "eye_closed")
            foreach (var anchor in new[] { _sideL, _sideR })
                DrawTexture(side, (At(box, anchor) + shift * 0.5f - side.GetSize() / 2).Floor());
    }

    void DrawPalps(Rect2 box)
    {
        if (!(Fidget || Speaking)) return;
        float w = Mathf.Sin(Time * (Speaking ? 20 : 8));
        foreach (var (name, anchor, sgn) in new[] { ("palp_l", _palpL, -1), ("palp_r", _palpR, 1) })
        {
            if (!_tex.TryGetValue(name, out var tex)) continue;
            var c = At(box, anchor) + new Vector2(0, sgn * w > 0 ? -2 : 0);
            DrawTexture(tex, (c - tex.GetSize() / 2).Floor());
        }
    }

    // ------------------------------------------------------------------ legs

    void DrawLeg(int idx, int side, bool back)
    {
        int i = side < 0 ? idx : idx + 4;
        var hip = new Vector2(Hips[idx].X * side, Hips[idx].Y);
        float l1 = FemurLength[idx], l2 = TibiaLength[idx];

        var d = Feet[i] - hip;
        float dist = Mathf.Clamp(d.Length(), Mathf.Abs(l1 - l2) + 0.5f, l1 + l2 - 0.01f);
        var dir = d.LengthSquared() > 0.0001f ? d.Normalized() : new Vector2(side, 1).Normalized();
        var foot = hip + dir * dist;

        // Law of cosines; of the two knee solutions take the one that is up and outward, so a raised
        // leg bends out to the side instead of folding across the face.
        float a = Mathf.Acos(Mathf.Clamp((l1 * l1 + dist * dist - l2 * l2) / (2 * l1 * dist), -1, 1));
        var k1 = hip + dir.Rotated(a) * l1;
        var k2 = hip + dir.Rotated(-a) * l1;
        var knee = side * k1.X - k1.Y > side * k2.X - k2.Y ? k1 : k2;

        var tint = back ? BackTint : Colors.White;
        DrawSegment($"femur{idx}", hip, knee, tint);
        DrawSegment($"tibia{idx}", knee, foot, tint);
    }

    /// <summary>Limb sprites are horizontal with the joint ball on the left; flipped vertically when pointing left so the lighting stays on top.</summary>
    void DrawSegment(string name, Vector2 from, Vector2 to, Color tint)
    {
        if (!_tex.TryGetValue(name, out var tex))
        {
            DrawLine(from, to, new Color("5b3820"), 3, false);
            return;
        }
        var pivot = _pivot.TryGetValue(name, out var p) ? p : new Vector2(tex.GetHeight() / 2f, tex.GetHeight() / 2f);
        float angle = (to - from).Angle();
        bool flip = Mathf.Cos(angle) < 0;
        DrawSetTransform(from, angle, new Vector2(1, flip ? -1 : 1));
        DrawTexture(tex, -pivot, tint);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }
}
