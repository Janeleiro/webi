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
        new("Presente! 🙋", Face.Happy, Gesture.Wave),
        new("Opa! Tava distraída olhando um pixel.", Face.Surprised, Gesture.None),
        new("Como vai a vida aí do outro lado da tela?", Face.Normal, Gesture.Wave),
        new("Ombros relaxados, hein! Tô vendo daqui. 👀", Face.Normal, Gesture.Tap),
        new("Respira fundo comigo: inspira... expira... 🌬️", Face.Normal, Gesture.Stretch),
        new("Eu tava ensaiando um passinho novo. Quer ver? 💃", Face.Happy, Gesture.Dance),
        new("Pisca um pouco! Olho seco não é legal.", Face.Normal, Gesture.Tap),
        new("Tô fazendo hora extra de mascote. Cadê meu aumento? 💸", Face.Normal, Gesture.None),
        new("Achei uma migalha no teclado. Era sua?", Face.Surprised, Gesture.Tap),
        new("Psiu... a área de trabalho tá precisando de uma faxina.", Face.Normal, Gesture.Groom),
        new("Já te disseram que você clica com estilo?", Face.Happy, Gesture.Wave),
        new("Se precisar de um lembrete, é só pedir no menu! ⏰", Face.Normal, Gesture.Tap),
        new("Tô aqui se precisar. Sempre. 🕷️", Face.Happy, Gesture.Wave),
        new("Que tal levantar e pegar um ar rapidinho?", Face.Normal, Gesture.Stretch),
        new("Oito olhos, zero óculos. Enxergo tudo! 😎", Face.Happy, Gesture.None),
        new("Penteando os pelinhos... beleza dá trabalho.", Face.Normal, Gesture.Groom),
    };

    /// <summary>Clicking five times in a few seconds.</summary>
    public static readonly string[] TooManyPokes =
    {
        "Tá bom, tá bom, já entendi! 😤",
        "Ai! Assim eu vou embora!",
        "Calma! Uma cutucada de cada vez!",
        "Ei, eu não sou botão de elevador! 🛗",
        "Para! Vou ficar tonta! 😵‍💫",
        "Isso é cutucada ou código Morse?",
        "Tô contando, hein! Já foram muitas!",
        "Assim eu vou chamar as minhas primas. 😠",
    };

    /// <summary>Being picked up and dragged around.</summary>
    public static readonly string[] Dragged =
    {
        "Wiii!", "Ei, me solta!", "Tô voando!", "Eita!", "Pra onde a gente vai?!", "Cuidado com as patinhas!",
        "Uhuuul! 🎢", "Me põe no chão, por favor!", "Voo sem escalas! ✈️", "Aaaah, que altura!",
    };

    public static readonly string[] WakeUp =
    {
        "Hã?! Ah, oi! Eu tava só... descansando os olhos.",
        "Uaaah... voltou! Eu não tava dormindo, juro.",
        "Oi! Senti sua falta.",
        "Hmm? Já é amanhã? 😪",
        "Oi! Sonhei com uma mosca gigante. Foi ótimo.",
        "Eu tava de guarda! Com os olhos fechados, mas de guarda.",
        "Ah, você! Que bom que voltou! 👋",
        "Cinco minutinhos a mais... tá bom, acordei.",
    };

    public static readonly string[] WebStart =
    {
        "Hora de decorar! 🕸️",
        "Esse canto tá pedindo uma teia...",
        "Deixa comigo, sou profissional nisso.",
        "Arquiteta de seda em ação! 👷‍♀️",
        "Vou dar um toque especial aqui.",
        "Um fio pra cá, outro pra lá...",
        "Esse cantinho vai ficar um charme.",
        "Modo artista: ligado! 🎨",
    };

    public static readonly string[] WebDone =
    {
        "Prontinho! Olha que linda ficou. 🕸️",
        "Teia nova, cantinho novo!",
        "Obra-prima. Pode elogiar.",
        "Ficou simétrica? Ficou, né? 😌",
        "Mais uma pro meu portfólio!",
        "Seda de primeira qualidade. Garantia vitalícia.",
        "Pronto! Agora sim esse canto tem personalidade.",
        "Podia estar num museu. 🖼️",
    };

    public static readonly string[] WebHang =
    {
        "Wiii, balançando! 🕷️",
        "Daqui de cima a vista é ótima.",
        "Só um descansinho aqui na teia.",
        "Rede de descanso, versão aranha. 😌",
        "Balança, balança... 🎵",
        "Aqui é meu cantinho da paz.",
        "Tô de boa na teia.",
    };

    public static readonly string[] NoWebSpot =
    {
        "Hmm, não achei nenhum cantinho livre pra uma teia agora.",
        "Tá tudo ocupado! Nem um cantinho pra mim. 😕",
        "Sem espaço pra teia... fica pra depois.",
        "Procurei, procurei e nada de cantinho bom.",
    };

    public static readonly string[] GiantStart =
    {
        "Ninguém mexe no mouse... então vou decorar TUDO. 🕸️",
        "Hmm, tudo parado... hora do projeto grande!",
        "Ninguém tá olhando? Perfeito. Projeto secreto! 🤫",
        "Tela livre = tela minha. 🕸️",
        "Vou fazer a maior teia da minha carreira!",
    };

    public static readonly string[] GiantDone =
    {
        "Pronto. Agora é só esperar a presa... 😴",
        "Minha obra-prima. Vou tirar um cochilo aqui no meio.",
        "Terminei! Nem precisa agradecer. 😌",
        "Ufa! Trabalho de uma vida. Hora do descanso.",
        "Teia gigante concluída. Eu mereço uma soneca.",
    };

    public static readonly string[] GiantHint =
    {
        "Essa teia só sai com uma sacudida BEM forte no mouse! 🕸️",
        "Quer tirar a teia? Chacoalha o mouse com vontade!",
        "Dica: só uma sacudida forte no mouse desmancha essa aqui.",
        "Essa é reforçada! Só chacoalhando bastante o mouse. 💪",
    };

    public static readonly string[] GiantTorn =
    {
        "AAAAH! MINHA TEIA!!!",
        "NÃÃÃO! Horas de trabalho!",
        "Tá bom, tá bom, já entendi... 😭",
        "Minha obra-prima! Destruída! 💔",
        "Tudo bem... arte é efêmera. 😢",
        "Vou fingir que não vi isso.",
    };

    public static readonly string[] WebSwept =
    {
        "Ei! Minha teia!",
        "Poxa, tava tão bonitinha...",
        "Faxina, é? Tá bom...",
        "Era decoração, não sujeira! 😤",
        "Tudo bem, eu faço outra.",
        "Lá se foi meu trabalho...",
        "Ok, entendi: minimalismo. 😔",
    };

    // ------------------------------------------------------------------ hunting

    public static readonly string[] FlySpotted =
    {
        "Psiu... uma mosca. 👀", "Shhh! Jantar à vista.", "Não se mexe... 🪰", "Alvo localizado. 🎯",
        "Tô vendo você, mosquinha...", "Silêncio... modo caçadora.", "Hmm, lanchinho voador!",
    };

    public static readonly string[] FlyCaught =
    {
        "Nhac! 😋", "Peguei! Crocante.", "Proteína do dia: ✅", "Hmm, sabor mosca. Meu favorito.", "Caçadora profissional! 😎",
        "Pulo perfeito! Nota 10. 🏅", "Delícia! Com asinhas e tudo.", "Ninguém escapa da Webi!",
        "Isso que eu chamo de fast food. 🍔", "Mais uma pra conta!",
    };

    public static readonly string[] FlyMissed =
    {
        "Quase! 😤", "Ela é rápida...", "Eu deixei ela fugir. De propósito.", "Foi só um aquecimento.",
        "Na próxima eu pego!", "Ela teve sorte hoje.", "Ninguém viu isso, né?",
    };

    public static readonly string[] FlyInWeb =
    {
        "Opa, caiu alguém na teia! 🕸️", "A teia funcionou! Já volto.", "Delivery na teia! 🪰",
        "Tem visita na teia! Com licença...", "Opa, chegou o almoço!", "Minha teia nunca falha! 😎",
    };

    public static readonly string[] CursorCaught =
    {
        "PEGUEI! 🖱️", "Te peguei, setinha!", "Ha! Sou mais rápida que o mouse!", "Setinha capturada! 🎯",
        "Pega-pega: Webi 1, mouse 0!", "Achou que ia fugir, é?",
    };

    public static readonly string[] CursorEscaped =
    {
        "Eu nem queria mesmo.", "Hmpf. Fugiu.", "Tava só treinando...", "Essa setinha é escorregadia!",
        "Deixa ela, um dia eu pego.", "Tá bom, você venceu. Dessa vez.",
    };

    public static readonly string[] CursorGrab =
    {
        "Lá vou eu! 🕷️", "Carona de mouse!", "Segura aí, setinha!", "Um, dois, três... PULA!",
        "Embarcando! 🚀", "Tchau, chão!",
    };

    public static readonly string[] CursorHanging =
    {
        "Wiii, tô pendurada! 🖱️", "Me leva pra passear!", "Daqui de cima a vista é ótima!",
        "Bora dar uma volta pela tela!", "Que passeio gostoso! 😌", "Não corre muito, hein!", "Próxima parada: ali! 👉",
    };

    public static readonly string[] CursorGrabMissed =
    {
        "Errei o pulo... 😅", "Era pra ter agarrado.", "Ninguém viu isso.", "Calculei errado o vento.",
        "Faltou um tiquinho!", "Era só um teste de pulo.",
    };

    public static readonly string[] CursorLetGo =
    {
        "Cansei, vou descer!", "Valeu pela carona! 👋", "Até a próxima volta!", "Desço aqui, obrigada!",
        "Que passeio! Agora chega. 😄", "Patinhas cansadas, hora de descer.",
    };

    public static readonly string[] CursorClickedOff =
    {
        "Tá bom, desço aqui!", "Fim da linha! 🚏", "Ok, ok, entendi o recado.", "Já tô descendo!",
        "Carona encerrada, entendido. 🫡", "Poxa, tava tão bom...",
    };

    public static readonly string[] CursorShakenOff =
    {
        "Aaaah! Que sacudida! 😵", "Tá bom, tá bom, já desço!", "Ei! Eu tava curtindo!", "Montanha-russa demais! 🎢",
        "Socorro, terremoto de mouse!", "Precisava disso tudo? 😵‍💫",
    };

    // ------------------------------------------------------------------ little scenes

    public static readonly string[] Peekaboo =
    {
        "Achou! 👀", "Bu!", "Tava aqui o tempo todo!", "Te assustei? Hihi.", "Surpresa! 🎉", "Esconde-esconde: eu ganhei!",
    };

    public static readonly string[] Tripped =
    {
        "Isso não aconteceu.", "Foi de propósito. Treino de queda.", "Opa! Pata número sete enroscou.",
        "Oito patas e mesmo assim tropeço. 🙄", "Esse pixel tava solto!", "Tô bem! Tô bem!",
        "Era um passo de dança novo. Sério.",
    };

    public static readonly string[] Yawn =
    {
        "Uaaah... 🥱", "Que soninho...", "Hmm... vou só fechar os olhos um pouquinho.", "Alguém aí tem um café? ☕",
        "Esse silêncio dá um sono...", "Uaaah... foi mal. 😪",
    };

    public static readonly string[] Carried =
    {
        "Wiii, passeio de janela!", "Ei, pra onde a gente vai?", "Uau, tô de carona! 🚗", "Mudança de endereço?",
        "Essa janela é rápida!", "Tô curtindo a viagem! 🛻",
    };

    public static readonly string[] ShakenOff =
    {
        "Ei! Devagar!!! 😵", "Aaaah! Me derrubou!", "Terremoto!!!", "Precisava sacudir assim?!", "Socorro, a janela me jogou! 😱",
        "Tudo girando... 😵‍💫",
    };

    public static readonly string[] Summoned =
    {
        "Chamou? Tô aqui! 🕷️", "Cheguei!", "Pronto, pode falar!", "A seu dispor! 🫡", "Vim correndo! Quer dizer, pulando.",
        "Presente! Em que posso ajudar?",
    };

    public static readonly string[] NoteDone =
    {
        "Feito! ✅ Menos uma coisa.", "Riscado da lista! 📝", "Arrasou! Post-it aposentado.", "Missão cumprida! 🏆",
        "Uma tarefa a menos, uma vitória a mais!", "Que produtividade! 💪", "Pronto! Pode comemorar. 🎉",
    };

    public static readonly string[] Unhidden =
    {
        "Voltei! Sentiu saudade?", "Oi de novo! 👋", "Tava escondidinha. Hehe.", "Ta-da! Apareci! ✨",
        "Achou que tinha se livrado de mim? 😄", "De volta à ativa!",
    };

    // ------------------------------------------------------------------ friendship

    /// <summary>Extra click lines unlocked at "Amiga" (level 2).</summary>
    public static readonly Reaction[] PokesFriend =
    {
        new("Você de novo! Meu humano favorito. 💛", Face.Happy, Gesture.Dance),
        new("Já te disse que você tem ótimos cliques?", Face.Happy, Gesture.Wave),
        new("Guardei uma mosca pra você. Brincadeira. Comi.", Face.Happy, Gesture.Groom),
        new("A gente forma uma boa dupla, né?", Face.Happy, Gesture.Wave),
        new("Contei pras minhas primas que tenho um humano incrível. 🕷️", Face.Happy, Gesture.Dance),
        new("Sabe o que é bom? Ficar aqui com você.", Face.Happy, Gesture.Groom),
        new("Se você precisar de um tempo, eu seguro as pontas aqui.", Face.Normal, Gesture.Wave),
        new("Já fiz uma teia com o formato do seu nome. Tá escondida.", Face.Happy, Gesture.Tap),
    };

    /// <summary>Extra click lines unlocked at "Melhor amiga" (level 3).</summary>
    public static readonly Reaction[] PokesBest =
    {
        new("Abraço de oito patas! 🫂", Face.Happy, Gesture.Hug),
        new("Amizade eterna. Tá na teia, é oficial. 🕸️💖", Face.Happy, Gesture.Hug),
        new("Sabia que eu fico mais feliz quando você liga o PC?", Face.Happy, Gesture.Dance),
        new("Você é a melhor parte do meu dia. Sério! 💖", Face.Happy, Gesture.Hug),
        new("Melhores amigas fazem dancinha juntas. Bora! 💃", Face.Happy, Gesture.Dance),
        new("Se eu tivesse um diário, você tava em todas as páginas.", Face.Happy, Gesture.Hug),
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
        "Espera, eu não tava pronta! 📸", "Manda essa foto pra mim depois!", "Sorria, é print! 😁",
    };

    public static readonly string[] UsbIn =
    {
        "Pendrive novo! O que tem aí dentro? 👀", "Chegou visita na porta USB! 🔌", "Opa, um pendrive! Posso morar nele?",
        "Ding-dong! Alguém plugou alguma coisa.", "Coisa nova conectada! Fofoca? 👀",
    };

    public static readonly string[] UsbOut =
    {
        "Tchau, pendrive! 👋", "Lembrou de ejetar antes de tirar? 😬", "O pendrive foi embora...",
        "Desconectou! Vai fazer falta.", "Foi sem nem se despedir...",
    };

    public static readonly string[] NetDown =
    {
        "Caiu a internet! 😱", "Cadê a internet?! Minha teia mundial caiu!", "Sem internet... vamos olhar pela janela?",
        "Internet foi tomar um café, parece. ☕", "Ih, ficamos offline! Hora de uma pausa?", "Alguém desligou o roteador? 📡",
    };

    public static readonly string[] NetBack =
    {
        "A internet voltou! 🎉", "Conectados de novo! 🌐", "Ufa, internet de volta!", "Voltamos à civilização! 🏙️",
        "A teia mundial tá de pé de novo! 🕸️",
    };

    public static readonly string[] Muted =
    {
        "Ficou tudo em silêncio... 🔇", "Mudo? Tá bom, eu falo baixinho.", "Shhh... modo silencioso ativado. 🤫",
        "Silêncio total. Dá até pra ouvir a teia balançar.", "Modo biblioteca! 📚",
    };

    public static readonly string[] Unmuted =
    {
        "Som de volta! 🔊", "Voltou o som! Bota uma música?", "Ah, agora sim, barulhinho! 🎶",
        "Som ligado! Posso escolher a playlist? 🎧", "Voltou o barulho! Eu gosto.",
    };

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
        "Meus olhos não se mexem: quem se mexe é a retina, lá dentro. Por isso eu viro o corpo todo pra olhar. 👀",
        "Tem uma aranha-saltadora que vive no Everest, a mais de 6 mil metros de altura! 🏔️",
        "As aranhas-pavão da Austrália têm um leque colorido e dançam pra conquistar. Diva! 🦚",
        "Existe uma aranha-saltadora quase vegetariana: ela come pedacinhos de planta da acácia. 🌱",
        "A seda sai líquida de dentro de mim e vira fio quando eu puxo. Mágica? Não, química!",
        "Pra crescer, eu troco de pele inteirinha! Fica uma cópia vazia de mim pra trás.",
        "Uma espécie de aranha-saltadora alimenta os filhotes com um tipo de leite. Pois é! 🍼",
        "Os pelinhos nas minhas patas me deixam andar até em vidro. Por isso eu escalo suas janelas! 🪟",
        "Cientistas viram aranhas-saltadoras mexendo a retina enquanto dormem. Será que a gente sonha? 💭",
    };

    public const string FirstHello ="Oi! Eu sou Webi! 🕷️ Clica em mim pra dar oi, ou usa o botão direito pro menu.";

    static readonly Reaction[] Morning =
    {
        new("Bom dia! ☀️ Bora começar bem!", Face.Happy, Gesture.Wave),
        new("Bom dia! Já tomou café? ☕", Face.Happy, Gesture.Wave),
        new("Manhã fresquinha! Que o dia seja leve. 🌤️", Face.Happy, Gesture.Stretch),
        new("Bom dia! Hoje vai ser um dia ótimo, sinto nas patinhas.", Face.Happy, Gesture.Dance),
    };

    static readonly Reaction[] Afternoon =
    {
        new("Boa tarde! Tá rendendo o dia?", Face.Happy, Gesture.Wave),
        new("Boa tarde! Já almoçou direitinho? 🍽️", Face.Normal, Gesture.Wave),
        new("Tarde chegou! Hora de um alongamento? 🙆", Face.Happy, Gesture.Stretch),
        new("Boa tarde! Metade do dia já foi, você tá indo bem.", Face.Happy, Gesture.Wave),
    };

    static readonly Reaction[] Evening =
    {
        new("Boa noite! 🌙 Não esquece de descansar.", Face.Normal, Gesture.Wave),
        new("Boa noite! Que tal desacelerar um pouquinho? ✨", Face.Normal, Gesture.Stretch),
        new("Noite chegou! Hora boa pra uma série. 📺", Face.Happy, Gesture.Wave),
        new("Boa noite! Foi um bom dia? 🌙", Face.Normal, Gesture.Wave),
    };

    static readonly Reaction[] LateNight =
    {
        new("Já é madrugada... vamos dormir? 😴", Face.Sleepy, Gesture.Stretch),
        new("Corujando, é? 🦉 Não esquece de dormir.", Face.Sleepy, Gesture.Yawn),
        new("Madrugada... até as moscas já foram dormir. 😪", Face.Sleepy, Gesture.Stretch),
        new("Tá tarde! Seu travesseiro tá com saudade. 🛏️", Face.Sleepy, Gesture.Yawn),
    };

    /// <summary>A greeting that depends on the time of day.</summary>
    public static Reaction Greeting()
    {
        int h = DateTime.Now.Hour;
        return Pick(h switch
        {
            >= 5 and < 12 => Morning,
            >= 12 and < 18 => Afternoon,
            >= 18 and < 23 => Evening,
            _ => LateNight,
        });
    }

    static readonly string[] HelloColleague =
    {
        "Oi de novo! 🕷️ Bora pra mais um dia?",
        "Olha quem chegou! Bora trabalhar? 💻",
        "Oi! Já deixei as patinhas aquecidas.",
    };

    static readonly string[] HelloFriend =
    {
        "Voltou! Eu tava te esperando. 💛",
        "Oba, você chegou! O dia ficou melhor. 💛",
        "Oi! Guardei um lugarzinho pra você. 🕷️",
    };

    static readonly string[] HelloBest =
    {
        "Meu humano favorito chegou!!! 💖",
        "AÊÊ, você voltou!!! Abraço de oito patas! 🫂",
        "Meu dia começa agora que você chegou! 💖",
    };

    /// <summary>Hello on start-up, warmer as the friendship grows.</summary>
    public static string Hello(int level) => level switch
    {
        0 => FirstHello,
        1 => Pick(HelloColleague),
        2 => Pick(HelloFriend),
        _ => Pick(HelloBest),
    };

    public static T Pick<T>(T[] options) => options[GD.RandRange(0, options.Length - 1)];
}
