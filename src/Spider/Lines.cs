using System;
using Godot;

namespace Webi.Spider;

/// <summary>A pre-programmed reaction: what Webi says, with which face and gesture.</summary>
public readonly record struct Reaction(string Text, Face Face, Gesture Gesture);

/// <summary>All of Webi's pre-programmed lines, in one place to edit.</summary>
public static class Lines
{
    /// <summary>Clicking on Webi.</summary>
    public static readonly Reaction[] Pokes =
    {
        new("Oi! 👋", Face.Happy, Gesture.Wave),
        new("Hihi, faz cócegas!", Face.Happy, Gesture.Dance),
        new("Tô de olho em você... nos oito, aliás. 👀", Face.Normal, Gesture.None),
        new("Precisa de alguma coisa? O botão direito abre o menu!", Face.Normal, Gesture.Wave),
        new("Bora uma pausa? Eu danço, você estica as costas.", Face.Happy, Gesture.Dance),
        new("Peraí, deixa eu limpar as patinhas.", Face.Normal, Gesture.Groom),
        new("Ei! Isso foi um peteleco?!", Face.Surprised, Gesture.Tap),
        new("Sabia que aranhas-saltadoras enxergam em cores?", Face.Normal, Gesture.None),
        new("Uaaah... que soneca boa seria agora.", Face.Sleepy, Gesture.Stretch),
        new("Já bebeu água hoje? 💧", Face.Normal, Gesture.Tap),
        new("Você tá indo muito bem, sabia?", Face.Happy, Gesture.Wave),
        new("Oito patas e nenhuma pra segurar um café. Injusto.", Face.Sad, Gesture.None),
        new("Construí uma teia nova ali atrás da janela. Não conta pra ninguém.", Face.Happy, Gesture.Groom),
        new("Pula-pula! 🕷️", Face.Happy, Gesture.Dance),
    };

    /// <summary>Clicking five times in a few seconds.</summary>
    public static readonly string[] TooManyPokes =
    {
        "Tá bom, tá bom, já entendi! 😤",
        "Ai! Assim eu vou embora!",
        "Calma! Uma cutucada de cada vez!",
    };

    /// <summary>Being picked up and dragged around.</summary>
    public static readonly string[] Dragged = { "Wiii!", "Ei, me solta!", "Tô voando!", "Eita!" };

    public static readonly string[] WakeUp =
    {
        "Hã?! Ah, oi! Eu tava só... descansando os olhos.",
        "Uaaah... voltou! Eu não tava dormindo, juro.",
        "Oi! Senti sua falta.",
    };

    public static readonly string[] SleepHint =
    {
        "Eu tiro um cochilo quando você fica um tempinho sem mexer em nada. 😴",
    };

    public static readonly string[] WebStart =
    {
        "Hora de decorar! 🕸️",
        "Esse canto tá pedindo uma teia...",
        "Deixa comigo, sou profissional nisso.",
    };

    public static readonly string[] WebDone =
    {
        "Prontinho! Olha que linda ficou. 🕸️",
        "Teia nova, cantinho novo!",
        "Obra-prima. Pode elogiar.",
    };

    public static readonly string[] WebHang =
    {
        "Wiii, balançando! 🕷️",
        "Daqui de cima a vista é ótima.",
        "Só um descansinho aqui na teia.",
    };

    public static readonly string[] NoWebSpot =
    {
        "Hmm, não achei nenhum cantinho livre pra uma teia agora.",
    };

    public static readonly string[] GiantStart =
    {
        "Ninguém mexe no mouse... então vou decorar TUDO. 🕸️",
        "Hmm, tudo parado... hora do projeto grande!",
    };

    public static readonly string[] GiantDone =
    {
        "Pronto. Agora é só esperar a presa... 😴",
        "Minha obra-prima. Vou tirar um cochilo aqui no meio.",
    };

    public static readonly string[] GiantHint =
    {
        "Essa teia só sai com uma sacudida BEM forte no mouse! 🕸️",
        "Quer tirar a teia? Chacoalha o mouse com vontade!",
    };

    public static readonly string[] GiantTorn =
    {
        "AAAAH! MINHA TEIA!!!",
        "NÃÃÃO! Horas de trabalho!",
        "Tá bom, tá bom, já entendi... 😭",
    };

    public static readonly string[] WebSwept =
    {
        "Ei! Minha teia!",
        "Poxa, tava tão bonitinha...",
        "Faxina, é? Tá bom...",
    };

    public const string FirstHello = "Oi! Eu sou Webi! 🕷️ Clica em mim pra dar oi, ou usa o botão direito pro menu.";

    /// <summary>A greeting that depends on the time of day.</summary>
    public static Reaction Greeting()
    {
        int h = DateTime.Now.Hour;
        return h switch
        {
            >= 5 and < 12 => new("Bom dia! ☀️ Bora começar bem!", Face.Happy, Gesture.Wave),
            >= 12 and < 18 => new("Boa tarde! Tá rendendo o dia?", Face.Happy, Gesture.Wave),
            >= 18 and < 23 => new("Boa noite! 🌙 Não esquece de descansar.", Face.Normal, Gesture.Wave),
            _ => new("Já é madrugada... vamos dormir? 😴", Face.Sleepy, Gesture.Stretch),
        };
    }

    public static T Pick<T>(T[] options) => options[GD.RandRange(0, options.Length - 1)];
}
