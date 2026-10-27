using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Webi.Spider;

namespace Webi.Assistant;

/// <summary>One answer button of a question, with what Webi replies and what it changes.</summary>
public sealed record Choice(string Label, Reaction Reply, Action? Effect = null);

/// <summary>A question in the speech bubble, answered by clicking one of its buttons.</summary>
public sealed record Question(string Text, Face Face, Choice[] Choices);

/// <summary>
/// Webi starting a conversation now and then (every 15-30 min of use): questions with buttons, spider facts,
/// lines about the special day, the "8 hours at the PC" nudge and the offer of the day's summary.
/// </summary>
public sealed class SmallTalk
{
    readonly AppState _state;
    readonly Friendship _friendship;
    readonly RandomNumberGenerator _rng = new();
    readonly Queue<string> _recent = new();
    double _untilNext;
    double _checkTimer;

    public bool Enabled { get; set; } = true;
    /// <summary>The user's birthday ("dd/mm"), for the special-day lines.</summary>
    public string Birthday { get; set; } = "";
    public bool Seasonal { get; set; } = true;

    public event Action<Question>? Ask;
    public event Action<Reaction>? Say;
    /// <summary>The user asked for the day's summary.</summary>
    public event Action? SummaryWanted;

    public SmallTalk(AppState state, Friendship friendship)
    {
        _state = state;
        _friendship = friendship;
        _untilNext = _rng.RandfRange(6, 12) * 60;
    }

    /// <param name="activeDt">Seconds of this frame while the user is at the PC (0 when idle).</param>
    /// <param name="canTalk">Nothing else is being said, she is awake, visible and not in a meeting.</param>
    public void Tick(double activeDt, DateTime now, bool canTalk)
    {
        _untilNext -= activeDt;
        _checkTimer -= activeDt;
        if (!Enabled || !canTalk || _checkTimer > 0) return;
        _checkTimer = 5;

        var today = _state.Today;
        if (!today.LongDayNudged && today.ActiveSeconds >= 8 * 3600)
        {
            today.LongDayNudged = true;
            _state.Dirty = true;
            Say?.Invoke(new($"Já são {today.ActiveSeconds / 3600:0} horas de PC hoje! 😵 Que tal encerrar por aqui (ou pelo menos esticar as pernas)?", Face.Surprised, Gesture.Tap));
            return;
        }
        if (!today.SummaryOffered && now.Hour >= 18 && today.ActiveSeconds >= 2 * 3600)
        {
            today.SummaryOffered = true;
            _state.Dirty = true;
            Ask?.Invoke(new("O dia tá acabando... quer ver o resumo do que você fez hoje? 📊", Face.Happy, new[]
            {
                new Choice("📊 Quero!", new("Olha só:", Face.Happy, Gesture.None), () => SummaryWanted?.Invoke()),
                new Choice("Agora não", new("Tá bom! Fica no menu, em \"Resumo do dia\".", Face.Normal, Gesture.None)),
            }));
            return;
        }

        if (_untilNext > 0) return;
        _untilNext = _rng.RandfRange(15, 30) * 60;

        var special = Seasonal ? Seasons.Today(now, Birthday) : null;
        float roll = _rng.Randf();
        if (special is { Lines.Length: > 0 } day && roll < 0.25f)
            Say?.Invoke(new(Pick(day.Lines), Face.Happy, Gesture.Dance));
        else if (roll < 0.6f)
            Ask?.Invoke(PickQuestion(now));
        else
            Say?.Invoke(new(Pick(Lines.Facts), Face.Normal, Gesture.Groom));
    }

    /// <summary>A question that fits the time of day, not one asked recently.</summary>
    Question PickQuestion(DateTime now)
    {
        var pool = Questions(now).ToList();
        var fresh = pool.Where(q => !_recent.Contains(q.Text)).ToList();
        var q = (fresh.Count > 0 ? fresh : pool)[_rng.RandiRange(0, (fresh.Count > 0 ? fresh : pool).Count - 1)];
        Remember(q.Text);
        return q;
    }

    string Pick(string[] lines)
    {
        var fresh = lines.Where(l => !_recent.Contains(l)).ToArray();
        var pool = fresh.Length > 0 ? fresh : lines;
        var line = pool[_rng.RandiRange(0, pool.Length - 1)];
        Remember(line);
        return line;
    }

    void Remember(string text)
    {
        _recent.Enqueue(text);
        while (_recent.Count > 10) _recent.Dequeue();
    }

    /// <summary>Answering anything is a little friendship point.</summary>
    Action Answered(Action? also = null) => () =>
    {
        _friendship.Add(1);
        also?.Invoke();
    };

    IEnumerable<Question> Questions(DateTime now)
    {
        int h = now.Hour;
        yield return new("Bebeu água na última hora? 💧", Face.Normal, new[]
        {
            new Choice("💧 Bebi!", new("Isso aí! Hidratação em dia. 🎉", Face.Happy, Gesture.Dance),
                Answered(() => { _state.Today.Waters++; _state.Dirty = true; })),
            new Choice("Ainda não", new("Vai lá pegar um copo, eu espero aqui! 💧", Face.Normal, Gesture.Tap), Answered()),
        });
        yield return new("Como você tá agora?", Face.Happy, new[]
        {
            new Choice("😄 Bem", new("Que bom! Isso me deixa com as oito patas felizes. 🕷️", Face.Happy, Gesture.Dance), Answered()),
            new Choice("😐 Mais ou menos", new("Dias assim acontecem. Uma pausa curtinha às vezes ajuda. 💛", Face.Normal, Gesture.Wave), Answered()),
            new Choice("😩 No limite", new("Ei, respira fundo. Levanta, toma uma água, olha pela janela. Eu fico de guarda. 🫂", Face.Sad, Gesture.Wave), Answered()),
        });
        yield return new("Como tá essa postura aí? 🪑", Face.Normal, new[]
        {
            new Choice("Arrumei!", new("Coluna reta, igual um fio de seda bem esticado. 👌", Face.Happy, Gesture.Stretch), Answered()),
            new Choice("Tô um ponto de interrogação", new("Hehe. Endireita as costas e solta os ombros! Eu espero. 🙆", Face.Happy, Gesture.Stretch), Answered()),
        });
        yield return new("Quer uma dancinha pra animar? 💃", Face.Happy, new[]
        {
            new Choice("Quero!", new("Solta o som! 🎶", Face.Happy, Gesture.Dance), Answered()),
            new Choice("Agora não", new("Tá bom, guardo os passos pra depois. 🕺", Face.Normal, Gesture.None), Answered()),
        });
        yield return new("Quando foi a última vez que você olhou pra longe da tela? 👀", Face.Normal, new[]
        {
            new Choice("Agorinha", new("Ótimo! Seus olhos agradecem. Os meus oito também. 👀", Face.Happy, Gesture.Wave), Answered()),
            new Choice("Nem lembro", new("Então olha pela janela 20 segundinhos. Eu conto: 1, 2, 3... 🪟", Face.Normal, Gesture.Tap), Answered()),
        });
        if (h is >= 11 and < 15)
            yield return new("Já almoçou? 🍽️", Face.Normal, new[]
            {
                new Choice("Já!", new("Barriguinha cheia, código melhor. 😋", Face.Happy, Gesture.Dance), Answered()),
                new Choice("Ainda não", new("Vai comer! Eu cuido da tela enquanto isso. 🕷️", Face.Normal, Gesture.Tap), Answered()),
            });
        if (h is >= 14 and < 18)
            yield return new("Hora de um cafezinho? ☕", Face.Happy, new[]
            {
                new Choice("Bora!", new("Traz um pra mim também. Bem pequenininho. ☕", Face.Happy, Gesture.Dance), Answered()),
                new Choice("Agora não", new("Tá certo, foco total. 💪", Face.Normal, Gesture.None), Answered()),
            });
        if (h >= 23 || h < 5)
            yield return new("Tá tarde, hein... vai dormir logo? 🌙", Face.Sleepy, new[]
            {
                new Choice("Já já", new("Promete? Eu vou ficar de olho no relógio. 😴", Face.Sleepy, Gesture.Stretch), Answered()),
                new Choice("Ainda não", new("Tá bom, mas não exagera. Amanhã você me agradece. 🌙", Face.Sleepy, Gesture.Tap), Answered()),
            });
        if (_state.Today.Flies > 0)
            yield return new($"Viu que eu já peguei {_state.Today.Flies} mosca{(_state.Today.Flies > 1 ? "s" : "")} hoje? 🪰", Face.Happy, new[]
            {
                new Choice("Arrasou!", new("Obrigada! Sou uma caçadora nata. 😎", Face.Happy, Gesture.Dance), Answered()),
                new Choice("Eca!", new("Ei! É proteína! 😤", Face.Surprised, Gesture.Tap), Answered()),
            });
    }
}

/// <summary>The day in numbers, for the "Resumo do dia" bubble.</summary>
public static class DaySummary
{
    public static string Describe(AppState state)
    {
        var t = state.Today;
        static string Time(double seconds)
        {
            int min = (int)(seconds / 60);
            return min >= 60 ? $"{min / 60}h{min % 60:00}" : $"{Math.Max(min, 0)} min";
        }
        var lines = new List<string> { $"📊 Hoje: {Time(t.ActiveSeconds)} de PC" };
        var apps = state.TopApps(4).Where(a => a.seconds >= 60).ToList();
        if (apps.Count > 0) lines.Add("🖥️ " + string.Join(" · ", apps.Select(a => $"{a.app} {Time(a.seconds)}")));
        var extra = new List<string>();
        if (t.Pomodoros > 0) extra.Add($"🍅 {t.Pomodoros} pomodoro{(t.Pomodoros > 1 ? "s" : "")}");
        if (t.Waters > 0) extra.Add($"💧 {t.Waters} copo{(t.Waters > 1 ? "s" : "")} de água");
        if (t.Flies > 0) extra.Add($"🪰 {t.Flies} mosca{(t.Flies > 1 ? "s" : "")} caçada{(t.Flies > 1 ? "s" : "")}");
        if (t.Pokes > 0) extra.Add($"👆 {t.Pokes} cutucada{(t.Pokes > 1 ? "s" : "")}");
        if (extra.Count > 0) lines.Add(string.Join(" · ", extra));
        if (apps.Count == 0 && extra.Count == 0) lines.Add("Ainda não tem muita coisa pra contar. O dia tá só começando!");
        return string.Join("\n", lines);
    }
}
