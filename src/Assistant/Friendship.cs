using System;

namespace Webi.Assistant;

/// <summary>
/// Friendship with Webi grows with clicks, answered questions, flies caught and time together (capped per day,
/// so it takes a few weeks to max out). Higher levels unlock new lines and the hug.
/// </summary>
public sealed class Friendship
{
    public static readonly (int points, string name)[] Levels =
    {
        (0, "Conhecida"),
        (25, "Colega"),
        (90, "Amiga"),
        (220, "Melhor amiga"),
    };

    /// <summary>Most points a single day can add.</summary>
    public const int DailyCap = 20;

    readonly AppState _state;

    /// <summary>Reached a new level (the new level).</summary>
    public event Action<int>? LevelUp;

    public Friendship(AppState state) => _state = state;

    public int Points => _state.Affinity;
    public int Level => LevelFor(_state.Affinity);
    public string Name => Levels[Level].name;

    public static int LevelFor(int points)
    {
        int level = 0;
        for (int i = 0; i < Levels.Length; i++)
            if (points >= Levels[i].points) level = i;
        return level;
    }

    public void Add(int points)
    {
        if (points > 0)
        {
            points = Math.Min(points, DailyCap - _state.AffinityToday);
            if (points <= 0) return;
            _state.AffinityToday += points;
        }
        int before = Level;
        _state.Affinity = Math.Max(0, _state.Affinity + points);
        _state.Dirty = true;
        if (Level > before) LevelUp?.Invoke(Level);
    }

    public string Describe()
    {
        var hearts = string.Concat(System.Linq.Enumerable.Repeat("💖", Level + 1))
                     + string.Concat(System.Linq.Enumerable.Repeat("🤍", Levels.Length - 1 - Level));
        var next = Level + 1 < Levels.Length
            ? $"Faltam {Levels[Level + 1].points - Points} pontinhos pra virar \"{Levels[Level + 1].name}\"."
            : "Nível máximo! Você é oficialmente da família das aranhas. 🕷️";
        return $"{hearts}\nNossa amizade: {Name} ({Points} pontos).\n{next}";
    }
}
