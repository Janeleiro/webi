using System;
using Godot;

namespace Webi.Spider;

/// <summary>A coat colour: a dark-to-light ramp for the fur, plus eye colours chosen to go with it.</summary>
public sealed record FurColor(string Id, string Name, Color[] Ramp, Color Outline, Color EyeRim, Color Iris)
{
    /// <summary>The original art (no recolouring).</summary>
    public bool IsOriginal => Id == "marrom";
}

/// <summary>
/// Recolours the spider sprites with a gradient map: each fur pixel's brightness picks a colour on the coat's
/// ramp, so all the shading survives and even black, white and grey work (a hue shift can't do those).
/// Eyes are handled separately: the iris and the rim get their own colours, highlights stay, eyelids follow
/// the fur. Done once when the colour changes, so it costs nothing per frame.
/// </summary>
public static class FurColors
{
    static Color C(string hex) => new(hex);

    public static readonly FurColor[] All =
    {
        new("marrom", "Marrom", new[] { C("4a2412"), C("8a4a1c"), C("cd7a34"), C("f5c893") }, C("2a1810"), C("cd732a"), C("050202")),
        new("preto", "Preto", new[] { C("0c0b0f"), C("26232c"), C("45404f"), C("6c6578") }, C("000000"), C("f2b21e"), C("060606")),
        new("branco", "Branco", new[] { C("8d8884"), C("c4c0bb"), C("e9e6e1"), C("ffffff") }, C("4e4844"), C("3aa6e8"), C("080a10")),
        new("cinza", "Cinza", new[] { C("34373c"), C("63686f"), C("959ba3"), C("c9cdd3") }, C("16181b"), C("f2b21e"), C("060606")),
        new("vermelho", "Vermelho", new[] { C("3a0808"), C("861818"), C("c83428"), C("f2805e") }, C("1e0404"), C("ffd23f"), C("060404")),
        new("amarelo", "Amarelo", new[] { C("6a4805"), C("bc8c10"), C("efc630"), C("fff27e") }, C("2e1f02"), C("2f56d6"), C("05060c")),
        new("laranja", "Laranja", new[] { C("5a2205"), C("ac4e0e"), C("ef8a24"), C("ffc274") }, C("2a0f02"), C("1fc4b2"), C("040606")),
        new("verde", "Verde", new[] { C("0f3812"), C("287628"), C("5ab63a"), C("aae274") }, C("061a08"), C("f2e23e"), C("050805")),
        new("azul", "Azul", new[] { C("0a1a48"), C("1e4e9e"), C("4a8ede"), C("a2d2ff") }, C("040a22"), C("ff9c30"), C("040610")),
        new("roxo", "Roxo", new[] { C("280a3e"), C("5c2a8e"), C("985ace"), C("d8aaff") }, C("12041e"), C("8ef046"), C("06040a")),
        new("rosa", "Rosa", new[] { C("5a1a3a"), C("ae4a80"), C("ef8ab8"), C("ffd2e6") }, C("2a0a1a"), C("ffffff"), C("2a0620")),
    };

    public static FurColor Find(string id) =>
        Array.Find(All, c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? All[0];

    // Brightness range of the fur in the original art (measured on its palette).
    const float FurMin = 0.22f, FurMax = 0.86f;
    const float Dark = 0.17f;

    static float Luma(Color c) => 0.299f * c.R + 0.587f * c.G + 0.114f * c.B;

    static Color SampleRamp(Color[] ramp, float t)
    {
        t = Mathf.Clamp(t, 0, 1) * (ramp.Length - 1);
        int i = Mathf.Min((int)t, ramp.Length - 2);
        return ramp[i].Lerp(ramp[i + 1], t - i);
    }

    static Color Fur(Color px, FurColor fur)
    {
        float l = Luma(px);
        if (l < Dark) return new Color(fur.Outline.Lerp(fur.Ramp[0], l / Dark * 0.5f), px.A);
        return new Color(SampleRamp(fur.Ramp, (l - FurMin) / (FurMax - FurMin)), px.A);
    }

    /// <summary>Recolours a fur part (body, legs, pedipalps, icon).</summary>
    public static Image RecolorFur(Image source, FurColor fur)
    {
        var img = (Image)source.Duplicate();
        if (fur.IsOriginal) return img;
        img.Convert(Image.Format.Rgba8);
        for (int y = 0; y < img.GetHeight(); y++)
            for (int x = 0; x < img.GetWidth(); x++)
            {
                var px = img.GetPixel(x, y);
                if (px.A > 0) img.SetPixel(x, y, Fur(px, fur));
            }
        return img;
    }

    /// <summary>
    /// Recolours an eye sprite. The rim shares its colours with the fur in the original palette, so it is told
    /// apart by position: the outer ring of the eye disc (only the lower half when an eyelid covers the top).
    /// </summary>
    /// <param name="lid">How the eye is covered: 0 open, 1 half-closed (lid over the top), 2 fully closed (all lid).</param>
    public static Image RecolorEye(Image source, FurColor fur, int lid)
    {
        var img = (Image)source.Duplicate();
        if (fur.IsOriginal) return img;
        img.Convert(Image.Format.Rgba8);
        float w = img.GetWidth(), h = img.GetHeight();
        var center = new Vector2(w / 2, h / 2);
        float radius = Mathf.Min(w, h) / 2;
        for (int y = 0; y < img.GetHeight(); y++)
            for (int x = 0; x < img.GetWidth(); x++)
            {
                var px = img.GetPixel(x, y);
                if (px.A <= 0) continue;
                float l = Luma(px);
                float dist = new Vector2(x + 0.5f, y + 0.5f).DistanceTo(center) / radius;
                Color result;
                if (l < Dark && lid < 2)
                    // Iris / pupil: keep the glossy variation, in the eye colour.
                    result = fur.Iris.Lerp(fur.Iris.Lightened(0.35f), l / Dark);
                else if (l > 0.88f && px.S < 0.45f && lid < 2)
                    result = Colors.White.Lerp(px, 0.3f); // highlight
                else if (lid < 2 && dist > 0.66f && (lid == 0 || y + 0.5f > center.Y))
                    // Rim: brightness drives a small ramp around the rim colour.
                    result = fur.EyeRim.Darkened(0.45f).Lerp(fur.EyeRim.Lightened(0.2f), Mathf.Clamp((l - 0.2f) / 0.45f, 0, 1));
                else
                    result = Fur(px, fur); // eyelid fur / closed-eye line
                img.SetPixel(x, y, new Color(result, px.A));
            }
        return img;
    }
}
