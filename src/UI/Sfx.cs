using System;
using System.Collections.Generic;
using Godot;

namespace Webi.UI;

public enum Sound { Pop, Chirp, Boing, Tap, Alarm, Crunch, Rip, Buzz, LevelUp }

/// <summary>
/// Tiny 8-bit sound effects synthesized at start-up (square/triangle waves and noise with pitch sweeps), so there
/// are no audio files to ship. Off unless <see cref="Enabled"/>; a few players so sounds can overlap.
/// </summary>
public partial class Sfx : Node
{
    const int Rate = 22050;

    readonly Dictionary<Sound, AudioStreamWav> _streams = new();
    readonly List<AudioStreamPlayer> _players = new();
    readonly Dictionary<Sound, double> _lastPlayed = new();
    int _next;

    public bool Enabled { get; set; }

    public override void _Ready()
    {
        _streams[Sound.Pop] = Make(0.07f, t => Square(Sweep(t, 0.07f, 520, 900), t) * Decay(t, 0.07f) * 0.35f);
        _streams[Sound.Chirp] = Make(0.16f, t => Triangle(Sweep(t, 0.16f, 900, 1500) + 120 * MathF.Sin(t * 60), t) * Decay(t, 0.16f) * 0.5f);
        _streams[Sound.Boing] = Make(0.22f, t => Triangle(Sweep(t, 0.22f, 180, 520), t) * Decay(t, 0.22f) * 0.55f);
        _streams[Sound.Tap] = Make(0.04f, t => Noise() * Decay(t, 0.04f) * 0.25f);
        _streams[Sound.Alarm] = Make(0.6f, t => Square(((int)(t * 10) % 2 == 0) ? 880 : 660, t) * Decay(t, 0.6f, 0.4f) * 0.3f);
        _streams[Sound.Crunch] = Make(0.18f, t => (Noise() * 0.7f + Square(120, t) * 0.3f) * Decay(t, 0.18f) * ((int)(t * 40) % 2 == 0 ? 1 : 0.4f) * 0.4f);
        _streams[Sound.Rip] = Make(0.3f, t => Noise() * Decay(t, 0.3f) * (0.5f + 0.5f * MathF.Sin(t * 180)) * 0.35f);
        _streams[Sound.Buzz] = Make(0.5f, t => Square(210 + 25 * MathF.Sin(t * 50), t) * Decay(t, 0.5f, 0.2f) * 0.08f);
        _streams[Sound.LevelUp] = Make(0.5f, t => Square(t < 0.12f ? 523 : t < 0.24f ? 659 : t < 0.36f ? 784 : 1047, t) * Decay(t, 0.5f, 0.3f) * 0.3f);

        for (int i = 0; i < 4; i++)
        {
            var p = new AudioStreamPlayer { VolumeDb = -8 };
            AddChild(p);
            _players.Add(p);
        }
    }

    public void Play(Sound sound)
    {
        if (!Enabled || !_streams.TryGetValue(sound, out var stream)) return;
        // The same sound at most every 80 ms (a burst of landings shouldn't machine-gun).
        double now = Time.GetTicksMsec() / 1000.0;
        if (_lastPlayed.TryGetValue(sound, out var last) && now - last < 0.08) return;
        _lastPlayed[sound] = now;
        var player = _players[_next++ % _players.Count];
        player.Stream = stream;
        player.PitchScale = (float)GD.RandRange(0.95, 1.05);
        player.Play();
    }

    static readonly Random Rng = new(7);
    static float Noise() => (float)(Rng.NextDouble() * 2 - 1);

    /// <summary>Frequency gliding from <paramref name="from"/> to <paramref name="to"/> over the sound.</summary>
    static float Sweep(float t, float length, float from, float to) => from + (to - from) * Math.Clamp(t / length, 0, 1);

    // Phase accumulated per sample, so pitch sweeps stay continuous.
    static double _phase;
    static float _lastT;

    static float Advance(float freq, float t)
    {
        if (t < _lastT) _phase = 0;
        _lastT = t;
        _phase += freq / Rate;
        return (float)(_phase - Math.Floor(_phase));
    }

    static float Square(float freq, float t) => Advance(freq, t) < 0.5f ? 1 : -1;

    static float Triangle(float freq, float t)
    {
        float p = Advance(freq, t);
        return p < 0.5f ? 4 * p - 1 : 3 - 4 * p;
    }

    /// <summary>Volume envelope: a 5 ms attack, held until <paramref name="hold"/> of the way, then fading to 0.</summary>
    static float Decay(float t, float length, float hold = 0)
    {
        float attack = Math.Clamp(t / 0.005f, 0, 1);
        float u = t / length;
        float release = u < hold ? 1 : 1 - (u - hold) / (1 - hold);
        return attack * Math.Clamp(release, 0, 1);
    }

    static AudioStreamWav Make(float seconds, Func<float, float> sample)
    {
        int n = (int)(seconds * Rate);
        var data = new byte[n * 2];
        _phase = 0;
        _lastT = 0;
        for (int i = 0; i < n; i++)
        {
            short v = (short)(Math.Clamp(sample(i / (float)Rate), -1, 1) * short.MaxValue);
            data[i * 2] = (byte)(v & 0xff);
            data[i * 2 + 1] = (byte)((v >> 8) & 0xff);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false, Data = data };
    }
}
