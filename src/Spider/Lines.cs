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

    // ------------------------------------------------------------------ hunting

    public static readonly string[] FlySpotted = { "Psiu... uma mosca. 👀", "Shhh! Jantar à vista.", "Não se mexe... 🪰" };

    public static readonly string[] FlyCaught =
    {
        "Nhac! 😋", "Peguei! Crocante.", "Proteína do dia: ✅", "Hmm, sabor mosca. Meu favorito.", "Caçadora profissional! 😎",
    };

    public static readonly string[] FlyMissed = { "Quase! 😤", "Ela é rápida...", "Eu deixei ela fugir. De propósito." };

    public static readonly string[] FlyInWeb = { "Opa, caiu alguém na teia! 🕸️", "A teia funcionou! Já volto.", "Delivery na teia! 🪰" };

    public static readonly string[] CursorCaught = { "PEGUEI! 🖱️", "Te peguei, setinha!", "Ha! Sou mais rápida que o mouse!" };

    public static readonly string[] CursorEscaped = { "Eu nem queria mesmo.", "Hmpf. Fugiu.", "Tava só treinando..." };

    // ------------------------------------------------------------------ little scenes

    public static readonly string[] Peekaboo = { "Achou! 👀", "Bu!", "Tava aqui o tempo todo!" };

    public static readonly string[] Tripped = { "Ninguém viu isso.", "Foi de propósito. Treino de queda.", "Opa! Pata número sete enroscou." };

    public static readonly string[] Yawn = { "Uaaah... 🥱", "Que soninho...", "Hmm... vou só fechar os olhos um pouquinho." };

    public static readonly string[] Carried = { "Wiii, passeio de janela!", "Ei, pra onde a gente vai?", "Uau, tô de carona! 🚗" };

    public static readonly string[] ShakenOff = { "Ei! Devagar!!! 😵", "Aaaah! Me derrubou!", "Terremoto!!!" };

    public static readonly string[] Summoned = { "Chamou? Tô aqui! 🕷️", "Cheguei!", "Pronto, pode falar!" };

    public static readonly string[] NoteDone = { "Feito! ✅ Menos uma coisa.", "Riscado da lista! 📝", "Arrasou! Post-it aposentado." };

    public static readonly string[] Unhidden = { "Voltei! Sentiu saudade?", "Oi de novo! 👋", "Tava escondidinha. Hehe." };

    // ------------------------------------------------------------------ friendship

    /// <summary>Extra click lines unlocked at "Amiga" (level 2).</summary>
    public static readonly Reaction[] PokesFriend =
    {
        new("Você de novo! Meu humano favorito. 💛", Face.Happy, Gesture.Dance),
        new("Já te disse que você tem ótimos cliques?", Face.Happy, Gesture.Wave),
        new("Guardei uma mosca pra você. Brincadeira. Comi.", Face.Happy, Gesture.Groom),
        new("A gente forma uma boa dupla, né?", Face.Happy, Gesture.Wave),
    };

    /// <summary>Extra click lines unlocked at "Melhor amiga" (level 3).</summary>
    public static readonly Reaction[] PokesBest =
    {
        new("Abraço de oito patas! 🫂", Face.Happy, Gesture.Hug),
        new("Amizade eterna. Tá na teia, é oficial. 🕸️💖", Face.Happy, Gesture.Hug),
        new("Sabia que eu fico mais feliz quando você liga o PC?", Face.Happy, Gesture.Dance),
    };

    public static string LevelUp(int level) => level switch
    {
        1 => "Acho que a gente já é colega! 🤝 Obrigada pela companhia.",
        2 => "Agora é oficial: amizade de verdade! 💛 Vou te contar meus segredos de teia.",
        _ => "MELHOR AMIZADE DO MUNDO!!! 💖 Ganhei o direito de te dar abraços de oito patas!",
    };

    // ------------------------------------------------------------------ PC events

    public static readonly string[] Screenshot =
    {
        "Tirou foto minha? Saí bem? 📸", "Xis! 📸 Pega meu lado bom.", "Print! Vou aparecer na foto? 🕷️",
    };

    public static readonly string[] UsbIn = { "Pendrive novo! O que tem aí dentro? 👀", "Chegou visita na porta USB! 🔌", "Opa, um pendrive! Posso morar nele?" };

    public static readonly string[] UsbOut = { "Tchau, pendrive! 👋", "Lembrou de ejetar antes de tirar? 😬", "O pendrive foi embora..." };

    public static readonly string[] NetDown = { "Caiu a internet! 😱", "Cadê a internet?! Minha teia mundial caiu!", "Sem internet... vamos olhar pela janela?" };

    public static readonly string[] NetBack = { "A internet voltou! 🎉", "Conectados de novo! 🌐", "Ufa, internet de volta!" };

    public static readonly string[] Muted = { "Ficou tudo em silêncio... 🔇", "Mudo? Tá bom, eu falo baixinho.", "Shhh... modo silencioso ativado. 🤫" };

    public static readonly string[] Unmuted = { "Som de volta! 🔊", "Voltou o som! Bota uma música?", "Ah, agora sim, barulhinho! 🎶" };

    /// <summary>Spider facts, said now and then.</summary>
    public static readonly string[] Facts =
    {
        "Sabia que aranhas-saltadoras como eu enxergam quase 360°? Por isso tô sempre de olho. 👀",
        "Curiosidade: eu consigo pular até 50 vezes o tamanho do meu corpo!",
        "Sabia que a seda de aranha é, proporcionalmente, mais forte que o aço? 🕸️",
        "Aranhas-saltadoras não fazem teia pra caçar: a gente pula na presa! (Eu faço teia por estilo.)",
        "Meus dois olhos grandes da frente enxergam em alta resolução. Os outros seis cuidam da visão periférica.",
        "Aranhas não são insetos: são aracnídeos. Oito patas, nada de antenas. 🕷️",
        "Antes de pular, eu prendo um fio de seda no chão. É o meu cinto de segurança!",
        "Algumas aranhas-saltadoras fazem dancinhas pra impressionar. Eu faço pra te animar. 💃",
        "Eu não tenho músculos pra esticar as patas: uso pressão do sangue! Chique, né?",
        "Sabia que aranhas-saltadoras conseguem ver luz ultravioleta? 🌈",
        "Existem mais de 6 mil espécies de aranhas-saltadoras. Eu sou a mais fofa, claro.",
        "Aranhas comem muitos mosquitos. De nada! 🦟",
        "Algumas aranhas reciclam a própria teia comendo a seda velha. Sustentabilidade! ♻️",
    };

    public const string FirstHello ="Oi! Eu sou Webi! 🕷️ Clica em mim pra dar oi, ou usa o botão direito pro menu.";

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

    /// <summary>Hello on start-up, warmer as the friendship grows.</summary>
    public static string Hello(int level) => level switch
    {
        0 => FirstHello,
        1 => "Oi de novo! 🕷️ Bora pra mais um dia?",
        2 => "Voltou! Eu tava te esperando. 💛",
        _ => "Meu humano favorito chegou!!! 💖",
    };

    public static T Pick<T>(T[] options) => options[GD.RandRange(0, options.Length - 1)];
}
