using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Webi.Spider;

namespace Webi.Assistant;

/// <summary>Kinds of apps/sites. Every group has its own lines; some apps also have specific ones.</summary>
public enum AppGroup
{
    Social, Video, Music, Chat, Meeting, Code, Office, Email, Games, Design, Shopping, AiChat, Learning, Files, Streaming, Remote,
}

/// <summary>
/// A known app or site. Matched by process name (without .exe, lowercase) and/or by text in the window title
/// (for browser tabs and Store apps). A process ending in '*' matches by prefix (e.g. versioned exe names).
/// </summary>
public sealed record AppInfo(string Id, string Name, AppGroup Group, string[] Processes, string[] Titles, string[] Lines);

/// <summary>
/// All of Webi's comments about what you're doing, in one place to edit. Every line keeps it light and,
/// whenever it fits, makes a little spider joke about the theme.
/// </summary>
public static class AppCatalog
{
    public static readonly HashSet<string> Browsers = new()
    {
        "chrome", "msedge", "firefox", "brave", "opera", "opera_gx", "vivaldi", "arc", "zen", "waterfox", "librewolf",
    };

    /// <summary>
    /// Remote desktop clients (mstsc, Windows App / AVD). What runs inside the session is just a picture for
    /// this PC, so only their window title can tell what is in there.
    /// </summary>
    public static readonly HashSet<string> RemoteClients = new(StringComparer.OrdinalIgnoreCase) { "mstsc", "msrdc" };

    /// <summary>Title endings that RemoteApp windows get ("Livro1 - Excel (Remoto)").</summary>
    static readonly string[] RemoteAppSuffixes = { "(Remoto)", "(Remote)" };

    /// <summary>Apps where Webi keeps quiet: no comments, no giant web, alerts wait.</summary>
    public static bool IsQuiet(AppGroup group) => group is AppGroup.Meeting or AppGroup.Streaming;

    // ------------------------------------------------------------------ group lines

    public static readonly Dictionary<AppGroup, string[]> GroupLines = new()
    {
        [AppGroup.Social] = new[]
        {
            "Rolando o feed de novo? Eu também vivo numa rede... a minha é de seda. 🕸️",
            "Cuidado pra não cair na teia do algoritmo. Essa eu não teci!",
            "Se der like em foto de aranha, eu fico sabendo. 👀",
            "Meu feed é só mosca passando. Bem menos drama.",
            "Uma rolagem, duas rolagens, oito rolagens... é assim que eu conto.",
        },
        [AppGroup.Video] = new[]
        {
            "Sessão de vídeo! Vou pegar a pipoca... digo, a mosquinha. 🍿",
            "Posso assistir junto? Prometo não dar spoiler.",
            "Eu assisto tudo com oito olhos. Nenhum detalhe me escapa.",
            "Esse é bom? Me avisa se tiver aranha no elenco.",
            "Lembra de piscar de vez em quando. Eu tenho oito olhos e pisco todos.",
        },
        [AppGroup.Music] = new[]
        {
            "Que som é esse? Eu danço com oito pernas! 🎶",
            "Aumenta! Aranha também curte um som.",
            "Música boa pra tecer. Já já sai uma teia nova no ritmo.",
            "Se tocar 'Dona Aranha', eu choro de emoção.",
            "Fone de ouvido? Eu sinto a música pelas vibrações da teia.",
        },
        [AppGroup.Chat] = new[]
        {
            "Conversando com alguém? Manda um oi da Webi! 👋",
            "Digitando... digitando... apagou tudo. Eu vi. 👀",
            "Fofoca boa? Pode contar, aranhas são discretas.",
            "Responde logo, que a outra pessoa já viu os tracinhos azuis!",
            "Conversa longa, hein? Eu entendo de fios compridos.",
        },
        [AppGroup.Code] = new[]
        {
            "Codando! Se achar um bug, me chama. Comer insetos é comigo. 🐛",
            "Lembra: não é bug, é feature. A teia também é feature.",
            "Funciona na minha máquina... digo, na minha teia.",
            "Commita antes que eu me enrosque no seu código!",
            "Um gole de água a cada cem linhas, combinado?",
        },
        [AppGroup.Office] = new[]
        {
            "Trabalho de escritório! Eu ajudo segurando os papéis com oito patas.",
            "Planilha, documento, apresentação... quanta coisa ao mesmo tempo!",
            "Salva o arquivo! Salvou? Salva de novo, só por garantia.",
            "Concentração máxima. Vou ficar só observando daqui.",
            "Produtividade em alta! Eu fico de torcida organizada.",
        },
        [AppGroup.Email] = new[]
        {
            "E-mails... quantos não lidos? Melhor nem contar.",
            "'Conforme meu último e-mail'... a frase mais passivo-agressiva do mundo.",
            "Caixa de entrada zero é lenda urbana, igual aranha que não é fofa.",
            "Responde com carinho que o dia rende.",
            "Mais um 'Re: Re: Fwd: Re:'? Esses fios eu não sei desembaraçar.",
        },
        [AppGroup.Games] = new[]
        {
            "Jogando! Boa sorte, eu torço com as oito patas. 🎮",
            "GG! Ou ainda não? Eu acredito em você.",
            "Se perder, a culpa foi do lag. Sempre é.",
            "Posso jogar de suporte? Eu sei prender inimigo na teia.",
            "Modo gamer ativado. Vou só assistir daqui do meu canto... da tela.",
        },
        [AppGroup.Design] = new[]
        {
            "Criando arte! Eu posso ser modelo, já tenho pose de aranha.",
            "Ctrl+Z é o melhor amigo de quem cria.",
            "Que capricho! Combina com o acabamento da minha teia.",
            "Pixel por pixel... é assim que se faz uma aranha fofa.",
            "Essa paleta de cores tá linda. Tem marrom-aranha?",
        },
        [AppGroup.Shopping] = new[]
        {
            "Comprinhas? Só não esquece que aranha não cabe no carrinho.",
            "Coloca no carrinho e espera promoção. Eu espero mosca, mesma técnica.",
            "Frete grátis acima de quanto? Eu entrego por fio de seda.",
            "Precisa mesmo? ...tá bom, eu não vi nada. 🙈",
            "Cuidado com o botão 'comprar agora'. Ele é mais rápido que eu.",
        },
        [AppGroup.AiChat] = new[]
        {
            "Conversando com uma IA? Eu sou mais fofa, só dizendo.",
            "Pede pra IA desenhar uma aranha fofa. Duvido que fique melhor que eu.",
            "Pergunta boa, resposta boa. Dica de aranha.",
            "Confere as respostas, viu? Até IA se enrosca às vezes.",
            "Eu também sou um ser digital, mas com muito mais pernas.",
        },
        [AppGroup.Learning] = new[]
        {
            "Estudando! Orgulho define. 📚",
            "Uma pausa a cada 25 minutos ajuda a memorizar, sabia?",
            "Aprendendo algo novo? Depois me ensina.",
            "Cérebro trabalhando, aranha torcendo.",
            "Conhecimento é tipo teia: cada fio novo segura melhor os outros.",
        },
        [AppGroup.Files] = new[]
        {
            "Procurando aquele arquivo perdido? Já olhou em Downloads?",
            "Pasta 'Nova pasta (7)'... organiza isso, vai.",
            "arquivo_final_FINAL_agora_vai.docx? Conheço bem.",
            "Organizando pastas? Eu organizo cantos de parede.",
        },
        [AppGroup.Remote] = new[]
        {
            "Trabalhando lá na máquina remota? Daqui eu só vejo a janela, mas tô torcendo. 🕷️",
            "Área de trabalho dentro da área de trabalho... é teia dentro de teia!",
            "Se a conexão travar, não fui eu que enrosquei o cabo. Juro.",
            "Mandando comandos pra longe! Eu mando vibrações pelo fio, funciona parecido.",
            "Lá dentro não tem aranha, né? Então eu fico aqui fora fazendo companhia.",
            "Conexão remota firme? Se cair, respira fundo e reconecta. Fio partido a gente tece de novo.",
        },
    };

    /// <summary>Long uninterrupted sessions get a gentle nudge ({0} = minutes).</summary>
    public static readonly Dictionary<AppGroup, (int minutes, string[] lines)> LongSession = new()
    {
        [AppGroup.Social] = (30, new[]
        {
            "Já faz {0} min de rolagem infinita... que tal esticar as patas?",
            "{0} minutos de rede social! Nem eu fico tanto tempo na teia.",
            "Alerta de doomscroll: {0} min. Bora uma pausinha?",
            "{0} min no feed. O feed não vai fugir, prometo.",
        }),
        [AppGroup.Video] = (90, new[]
        {
            "Já faz {0} min de vídeo. Pausa pro xixi? Eu seguro a teia.",
            "{0} minutos de tela! Pisca, alonga e volta.",
            "Maratona de {0} min! Respeito, mas bebe uma água.",
        }),
        [AppGroup.Games] = (120, new[]
        {
            "Já são {0} min de jogo! Pausa pra água e alongamento?",
            "{0} minutos de gameplay! Suas mãos merecem um descanso.",
            "{0} min jogando. Eu salvei o progresso da minha teia, salva o seu também.",
        }),
    };

    /// <summary>After a while in a work app with no pomodoro running.</summary>
    public static readonly string[] PomodoroSuggestion =
    {
        "Que tal um pomodoro pra focar? Botão direito em mim → Iniciar pomodoro. 🍅",
        "Foco total, hein! Quer ligar um pomodoro? Eu cuido do relógio.",
        "Trabalhando firme! Um pomodoro ajuda a lembrar das pausas. 🍅",
    };

    // ------------------------------------------------------------------ specific apps and sites

    public static readonly AppInfo[] Apps =
    {
        // Social
        new("instagram", "Instagram", AppGroup.Social, new[] { "instagram" }, new[] { "Instagram" }, new[]
        {
            "Postou story? Meu story é sempre o mesmo: 'mais uma teia pronta'. 🕸️",
            "Close friends é tipo minha teia: só entra quem eu deixo.",
            "Curtiu? Eu daria oito curtidas, uma com cada pata. ❤️",
            "Reels atrás de reels... o Explorar prende mais que teia.",
            "Se postar foto do almoço, me marca: @webi.aranha",
        }),
        new("tiktok", "TikTok", AppGroup.Social, new[] { "tiktok" }, new[] { "TikTok" }, new[]
        {
            "Só mais um vídeo... disse alguém há quarenta vídeos atrás.",
            "Aprendeu alguma dancinha? Eu tenho oito pernas pra coreografia!",
            "O algoritmo do TikTok tece teia melhor que eu. E olha que eu sou profissional.",
            "Se aparecer aranha no Para Você, era eu tentando viralizar.",
        }),
        new("x", "X", AppGroup.Social, Array.Empty<string>(), new[] { " / X", " on X:", " no X:", "Twitter" }, new[]
        {
            "Treta do dia? Eu só observo... com os oito olhos.",
            "280 caracteres? Eu resumo minha vida em três: 🕷️🕸️🦟",
            "Tá pegando fogo aí no X? Aranha não gosta de calor, hein.",
            "Fio enorme, hein? Eu entendo de fios.",
        }),
        new("facebook", "Facebook", AppGroup.Social, new[] { "facebook" }, new[] { "Facebook" }, new[]
        {
            "Facebook! Já desejou feliz aniversário pra alguém que não vê desde 2012?",
            "Cuidado com as correntes do Facebook. Só confio em fios de seda.",
            "Quantas imagens de bom dia com flores brilhantes hoje?",
        }),
        new("reddit", "Reddit", AppGroup.Social, Array.Empty<string>(), new[] { "Reddit" }, new[]
        {
            "Caiu na toca do coelho do Reddit? Eu prefiro a toca da aranha.",
            "r/spiders é a melhor comunidade. Dá um upvote lá por mim?",
            "Lendo um post com três mil comentários? Coragem.",
            "Aposto que tem alguém discutindo se cachorro-quente é sanduíche.",
        }),
        new("linkedin", "LinkedIn", AppGroup.Social, Array.Empty<string>(), new[] { "LinkedIn" }, new[]
        {
            "LinkedIn! Já colocou no perfil: 'Especialista em sobreviver a segundas-feiras'?",
            "No meu LinkedIn: 'Arquitetura de teias, nível sênior, 8+ patas de experiência'.",
            "Mais um post 'o que meu gato me ensinou sobre liderança'?",
            "Networking é só uma teia com crachá.",
            "Me endossa? 'Escalar paredes' e 'Ficar de ponta-cabeça'.",
        }),
        new("pinterest", "Pinterest", AppGroup.Social, Array.Empty<string>(), new[] { "Pinterest" }, new[]
        {
            "Montando pasta de inspiração? Salva uma teia na pasta 'decoração'.",
            "Pinterest é perigoso: você entra pra ver uma receita e sai com três reformas planejadas.",
            "Se tiver pasta 'aranhas fofas', eu quero aparecer nela.",
        }),

        // Video
        new("youtube", "YouTube", AppGroup.Video, Array.Empty<string>(), new[] { "YouTube" }, new[]
        {
            "Só mais um vídeo... dessa vez é sério, né?",
            "Não esquece de dar like e se inscrever no canal da Webi! 🔔",
            "Pulou o anúncio? Eu pulo de janela em janela, a gente se entende.",
            "Vídeo de restaurar faca enferrujada às 3 da manhã é um clássico.",
            "Recomendação do algoritmo: 'aranhas fofas, compilação'. Confia.",
        }),
        new("netflix", "Netflix", AppGroup.Video, new[] { "netflix" }, new[] { "Netflix" }, new[]
        {
            "'Você ainda está assistindo?' Eu tô, e com oito olhos.",
            "Maratona? Eu tenho oito pernas e nem eu aguento uma dessas.",
            "Escolhendo o que ver há vinte minutos? Clássico.",
            "Se for filme de terror com aranha, eu não tenho nada a ver com isso.",
        }),
        new("twitch", "Twitch", AppGroup.Video, Array.Empty<string>(), new[] { "Twitch" }, new[]
        {
            "Live na Twitch! Manda um oi no chat pela Webi. 🕷️",
            "Kappa. Viu? Também falo chatês.",
            "Se o streamer perder, a culpa é do lag. Sempre do lag.",
        }),
        new("crunchyroll", "Crunchyroll", AppGroup.Video, Array.Empty<string>(), new[] { "Crunchyroll" }, new[]
        {
            "Anime! Já viu 'Kumo desu ga, nani ka?' É sobre uma aranha. Recomendo demais.",
            "Episódio novo? Eu grito de emoção igual protagonista de shonen.",
            "Pulou a abertura ou ouviu inteira? Eu ouço inteira, sou dessas.",
        }),
        new("streaming", "Streaming", AppGroup.Video, new[] { "vlc", "mpc-hc64", "potplayer", "plex" },
            new[] { "Prime Video", "Disney+", "Globoplay", "HBO Max", "Paramount+", "Apple TV" }, Array.Empty<string>()),

        // Music
        new("spotify", "Spotify", AppGroup.Music, new[] { "spotify" }, new[] { "Spotify" }, new[]
        {
            "Spotify ligado! Bota uma playlist 'Teias e Chill'?",
            "Se a sua retrospectiva do Spotify tiver aranha, eu exijo créditos.",
            "Pulando música toda hora? Eu pulo janela, você pula faixa.",
            "Essa é boa pra dançar com oito pernas. 💃",
        }),
        new("music", "Música", AppGroup.Music, new[] { "deezer", "applemusic", "tidal", "amazon music" },
            new[] { "YouTube Music", "SoundCloud", "Deezer", "Apple Music" }, Array.Empty<string>()),

        // Chat
        new("whatsapp", "WhatsApp", AppGroup.Chat, new[] { "whatsapp", "whatsapp.root" }, new[] { "WhatsApp" }, new[]
        {
            "WhatsApp! Já chegou áudio de sete minutos hoje?",
            "O grupo da família já mandou bom dia com gif brilhante?",
            "Visualizou e não respondeu? Os tracinhos azuis não perdoam.",
            "Se for mandar figurinha, manda uma de aranha. Eu autorizo.",
        }),
        new("discord", "Discord", AppGroup.Chat, new[] { "discord" }, new[] { "Discord" }, new[]
        {
            "Discord! Alguém já mandou 'alguém on?' hoje?",
            "Se tiver call, eu fico em silêncio. Juro.",
            "Seu servidor tem canal de memes de aranha? Deveria.",
            "Quantos servidores com notificação silenciada? Eu chuto uns vinte.",
        }),
        new("telegram", "Telegram", AppGroup.Chat, new[] { "telegram" }, new[] { "Telegram" }, new[]
        {
            "Telegram! Quantos canais aí você nem lembra que entrou?",
            "Figurinha animada de aranha existe? Se não existe, precisa.",
            "Mensagem salva pra você mesmo? Eu faço isso com moscas.",
        }),
        new("slack", "Slack", AppGroup.Chat, new[] { "slack" }, new[] { "Slack" }, new[]
        {
            "Slack aberto... quantas notificações não lidas? Melhor nem saber.",
            "Uma 'pergunta rápida' que virou thread de quarenta respostas? Conheço fios longos.",
            "Coloca o status 'Ocupado tecendo'. Eu uso direto.",
        }),

        // Meetings and streaming: quiet mode (no lines needed)
        new("zoom", "Zoom", AppGroup.Meeting, new[] { "zoom" }, Array.Empty<string>(), Array.Empty<string>()),
        new("teams", "Teams", AppGroup.Meeting, new[] { "ms-teams", "teams" }, Array.Empty<string>(), Array.Empty<string>()),
        new("meet", "Google Meet", AppGroup.Meeting, Array.Empty<string>(), new[] { "Meet - ", "Google Meet" }, Array.Empty<string>()),
        new("webex", "Webex", AppGroup.Meeting, new[] { "webex", "ciscocollabhost", "atmgr" }, Array.Empty<string>(), Array.Empty<string>()),
        new("skype", "Skype", AppGroup.Meeting, new[] { "skype" }, Array.Empty<string>(), Array.Empty<string>()),
        new("obs", "OBS", AppGroup.Streaming, new[] { "obs64", "obs32", "obs" }, Array.Empty<string>(), Array.Empty<string>()),

        // Code
        new("vscode", "VS Code", AppGroup.Code, new[] { "code", "code - insiders", "cursor", "windsurf" }, Array.Empty<string>(), new[]
        {
            "VS Code! Quantas extensões instaladas? Aposto que mais de trinta.",
            "Ctrl+S, Ctrl+S, Ctrl+S... só por garantia, né?",
            "Tema escuro, né? Combina com cantinho de teia.",
            "Deixa o autocomplete trabalhar, você merece.",
        }),
        new("visualstudio", "Visual Studio", AppGroup.Code, new[] { "devenv" }, Array.Empty<string>(), new[]
        {
            "Visual Studio carregando... dá tempo de tecer uma teia inteira.",
            "Build quebrou? Respira, recompila, resmunga baixinho.",
            "Breakpoint é tipo minha teia: pega o bug no meio do caminho.",
        }),
        new("jetbrains", "JetBrains", AppGroup.Code,
            new[] { "idea64", "pycharm64", "webstorm64", "rider64", "clion64", "goland64", "phpstorm64", "rubymine64", "datagrip64", "studio64" },
            Array.Empty<string>(), new[]
            {
                "Indexando... indexando... vou tirar um cochilo enquanto isso.",
                "Alt+Enter resolve tudo, já reparou?",
                "IDE poderosa! Refatora aí que eu fico de olho.",
            }),
        new("terminal", "Terminal", AppGroup.Code, new[] { "windowsterminal", "cmd", "powershell", "pwsh", "wezterm-gui", "alacritty", "mintty" },
            Array.Empty<string>(), new[]
            {
                "Terminal aberto! Cuidado com o rm -rf, hein. 😬",
                "Modo hacker ativado. Eu fico de vigia.",
                "Seta pra cima, seta pra cima, seta pra cima... procurando o comando de ontem?",
            }),
        new("github", "GitHub", AppGroup.Code, new[] { "githubdesktop" }, new[] { "GitHub" }, new[]
        {
            "GitHub! Bora fazer um PR caprichado?",
            "Quadradinhos verdes de contribuição são tipo teia cheia: dá orgulho.",
            "Revisando código? Seja gentil nos comentários. 🕷️",
        }),
        new("stackoverflow", "Stack Overflow", AppGroup.Code, Array.Empty<string>(), new[] { "Stack Overflow" }, new[]
        {
            "Stack Overflow! A resposta certa nunca é a aceita, é a segunda.",
            "Copia, cola, reza. O ciclo da vida.",
            "Pergunta 'como matar um processo'? Só não confunde com aranha, por favor.",
        }),
        new("godot", "Godot", AppGroup.Code, new[] { "godot*" }, new[] { "Godot Engine" }, new[]
        {
            "Godot?! Ei, eu nasci aí! Que nostalgia. 🥹",
            "Fazendo jogo? Coloca uma aranha fofa, por favor.",
            "Cuidado com a árvore de nós. Eu entendo de nós... e de fios.",
        }),
        new("unity", "Unity", AppGroup.Code, new[] { "unity", "unity hub" }, Array.Empty<string>(), new[]
        {
            "Unity! Compilando scripts... dá tempo de um café.",
            "GameObject 'Aranha' com o componente 'Fofura'? Eu aprovo.",
            "Se o Play não funcionar, tenta o velho truque: salvar e reabrir.",
        }),

        // Office
        new("excel", "Excel", AppGroup.Office, new[] { "excel" }, new[] { "Google Sheets", "Planilhas Google" }, new[]
        {
            "PROCV ou ÍNDICE+CORRESP? Eu fico no time da teia.",
            "Células, linhas, colunas... parece uma teia bem organizada.",
            "Se aparecer #REF!, respira fundo. Eu acredito em você.",
            "Tabela dinâmica é o mais perto de mágica que existe.",
        }),
        new("word", "Word", AppGroup.Office, new[] { "winword" }, new[] { "Google Docs", "Documentos Google" }, new[]
        {
            "Escrevendo! Lembra: aranha se escreve com 'nh'. 🕷️",
            "Mexeu numa imagem e o texto inteiro pulou de página? Clássico.",
            "Comic Sans só se for convite de aniversário de aranha.",
        }),
        new("powerpoint", "PowerPoint", AppGroup.Office, new[] { "powerpnt" }, new[] { "Google Slides", "Apresentações Google" }, new[]
        {
            "Montando apresentação? Coloca uma transição de teia, vai.",
            "Slide com quarenta linhas de texto? Ninguém vai ler, prometo.",
            "Dica: um slide com uma aranha fofa prende a atenção de qualquer plateia.",
        }),
        new("notion", "Notion", AppGroup.Office, new[] { "notion" }, new[] { "Notion" }, new[]
        {
            "Notion! Organizando a vida em blocos? Eu organizo em fios.",
            "Mais um template de produtividade? O meu é: tecer, comer, dormir.",
            "Página dentro de página dentro de página... parece minha teia.",
        }),
        new("obsidian", "Obsidian", AppGroup.Office, new[] { "obsidian" }, Array.Empty<string>(), new[]
        {
            "Obsidian! Esse grafo de notas parece uma teia. Me sinto em casa.",
            "Mais uma nota linkada? Cada link é um fio novo.",
            "Segundo cérebro, é? Eu tenho oito olhos, então percepção não me falta.",
        }),

        // Email
        new("outlook", "Outlook", AppGroup.Email, new[] { "outlook", "olk" }, new[] { "Outlook" }, new[]
        {
            "Outlook! Mais uma reunião que podia ser um e-mail?",
            "Calendário cheio? Meu único compromisso é com os mosquitos.",
            "'Responder a todos'? Pensa duas vezes. Eu penso oito.",
        }),
        new("gmail", "Gmail", AppGroup.Email, Array.Empty<string>(), new[] { "Gmail" }, new[]
        {
            "Gmail! Já arquivou as promoções de 2019?",
            "Aba Promoções com 4 mil e-mails? Eu não julgo.",
            "Rascunho salvo há três semanas? Manda logo, vai.",
        }),

        // Games
        new("steam", "Steam", AppGroup.Games, new[] { "steam", "steamwebhelper" }, Array.Empty<string>(), new[]
        {
            "Steam! Mais um jogo pra pilha do 'um dia eu jogo'?",
            "Promoção da Steam é minha teia: você entra e não sai.",
            "Biblioteca com duzentos jogos e nenhum pra jogar? Conheço.",
        }),
        new("minecraft", "Minecraft", AppGroup.Games, new[] { "minecraft" }, new[] { "Minecraft" }, new[]
        {
            "Minecraft! Cuidado com as aranhas de lá, elas não são fofas como eu.",
            "Me traz um fio de teia daí? Eu faço uma cama.",
            "Creeper atrás de você! ...brincadeira. 😅",
        }),
        new("roblox", "Roblox", AppGroup.Games, new[] { "robloxplayerbeta", "roblox" }, new[] { "Roblox" }, new[]
        {
            "Roblox! Tem obby de aranha? Eu seria imbatível.",
            "Gastou Robux hoje? Eu aceito mosquitos como pagamento.",
            "Se for jogo de esconde-esconde, eu me escondo no canto do teto.",
        }),
        new("lol", "League of Legends", AppGroup.Games, new[] { "league of legends", "leagueclientux", "riotclientux" }, Array.Empty<string>(), new[]
        {
            "LoL! A Elise é a melhor campeã, não aceito discussão. 🕷️",
            "Põe ward na rota, por favor.",
            "Se o jungle não gankou, a culpa não é sua.",
        }),
        new("launchers", "Jogos", AppGroup.Games,
            new[] { "epicgameslauncher", "battle.net", "eadesktop", "ubisoftconnect", "gog galaxy", "xboxapp", "valorant", "fortniteclient-win64-shipping", "cs2" },
            Array.Empty<string>(), Array.Empty<string>()),

        // Design
        new("figma", "Figma", AppGroup.Design, new[] { "figma" }, new[] { "Figma" }, new[]
        {
            "Figma! Alinhando tudo no pixel? Eu respeito.",
            "Auto layout é mágico, igual teia que se arruma sozinha.",
            "Componentes, variantes, protótipos... um dia vira app de aranha?",
        }),
        new("photoshop", "Photoshop", AppGroup.Design, new[] { "photoshop" }, Array.Empty<string>(), new[]
        {
            "Photoshop! Ctrl+Alt+Z até o infinito.",
            "Pode tirar o fundo, só não tira minhas pernas, por favor.",
            "'Camada 47 cópia cópia'... organiza as camadas, vai.",
        }),
        new("blender", "Blender", AppGroup.Design, new[] { "blender" }, Array.Empty<string>(), new[]
        {
            "Blender! Já fez o donut?",
            "Renderizando... dá tempo de tecer três teias.",
            "Modela uma aranha 3D, por favor. Eu poso.",
        }),
        new("canva", "Canva", AppGroup.Design, new[] { "canva" }, new[] { "Canva" }, new[]
        {
            "Canva! Arrastou, soltou, ficou lindo.",
            "Tem elemento de aranha no Canva? Usa, por favor.",
            "Mais um post caprichado? A identidade visual agradece.",
        }),
        new("design", "Design", AppGroup.Design, new[] { "illustrator", "krita", "aseprite", "gimp-2.10", "gimp", "inkscape", "clipstudiopaint", "affinity" },
            Array.Empty<string>(), Array.Empty<string>()),

        // Shopping
        new("amazon", "Amazon", AppGroup.Shopping, Array.Empty<string>(), new[] { "Amazon.com" }, new[]
        {
            "Amazon! Comprando uma aranha de pelúcia? Seria justo.",
            "Entrega em um dia? Eu levo três segundos pra descer do teto.",
            "Lê as avaliações de uma estrela antes. É sempre divertido.",
        }),
        new("mercadolivre", "Mercado Livre", AppGroup.Shopping, new[] { "mercadolivre" }, new[] { "Mercado Livre" }, new[]
        {
            "Mercado Livre! Pergunta pro vendedor se vem teia de brinde.",
            "Frete grátis é minha palavra favorita depois de 'mosquito'.",
            "Olha a reputação verdinha do vendedor antes, hein.",
        }),
        new("shopee", "Shopee", AppGroup.Shopping, new[] { "shopee" }, new[] { "Shopee" }, new[]
        {
            "Shopee! Juntando moedinha igual eu junto mosquito.",
            "Cupom aplicado? Eu aplico teia em cantos.",
            "Chega em 40 dias, mas chega. Paciência de aranha.",
        }),
        new("shops", "Compras", AppGroup.Shopping, Array.Empty<string>(),
            new[] { "AliExpress", "Shein", "eBay", "Magazine Luiza", "Americanas", "Temu" }, Array.Empty<string>()),

        // AI chat
        new("chatgpt", "ChatGPT", AppGroup.AiChat, new[] { "chatgpt" }, new[] { "ChatGPT" }, Array.Empty<string>()),
        new("claude", "Claude", AppGroup.AiChat, new[] { "claude" }, new[] { "Claude" }, new[]
        {
            "Claude! Manda um oi, a gente é da mesma família de criaturas digitais.",
            "Pede pro Claude escrever um poema sobre aranhas. Eu espero.",
            "Dois assistentes na mesma tela? Eu fico com a parte da fofura.",
        }),
        new("gemini", "Gemini", AppGroup.AiChat, Array.Empty<string>(), new[] { "Gemini", "Copilot", "Perplexity" }, Array.Empty<string>()),

        // Learning
        new("wikipedia", "Wikipédia", AppGroup.Learning, Array.Empty<string>(), new[] { "Wikipedia", "Wikipédia" }, new[]
        {
            "Começou lendo uma coisa e já tá em 'Lista de pontes da Mongólia'?",
            "Já leu o artigo sobre aranhas-saltadoras? Recomendo, é sobre mim.",
            "Mais uma aba da Wikipédia aberta... cuidado com a toca do coelho.",
        }),
        new("duolingo", "Duolingo", AppGroup.Learning, new[] { "duolingo" }, new[] { "Duolingo" }, new[]
        {
            "Duolingo! Faz a lição antes que a corujinha apareça.",
            "Ofensiva mantida? A minha é de 300 dias seguidos tecendo.",
            "Como se diz 'aranha' em inglês? Spider! Viu, também estudo.",
        }),
        new("courses", "Cursos", AppGroup.Learning, Array.Empty<string>(),
            new[] { "Coursera", "Udemy", "Khan Academy", "Alura", "edX", "Brilliant" }, Array.Empty<string>()),

        // Remote desktop: whatever runs inside, when the window title doesn't tell (see Identify)
        new("remote", "Área de Trabalho Remota", AppGroup.Remote, RemoteClients.ToArray(), Array.Empty<string>(), Array.Empty<string>()),

        // Files
        new("explorer", "Explorador de Arquivos", AppGroup.Files, new[] { "explorer" }, Array.Empty<string>(), Array.Empty<string>()),
    };

    /// <summary>Identifies the app from the foreground window's process and title.</summary>
    public static AppInfo? Identify(string process, string title)
    {
        process = process.ToLowerInvariant();
        bool browser = Browsers.Contains(process);
        bool storeApp = process is "applicationframehost";
        if (RemoteClients.Contains(process)) return IdentifyRemote(title);

        // A real process match wins (desktop apps); in browsers and Store frames the title tells.
        if (!browser && !storeApp)
        {
            var byProcess = Apps.FirstOrDefault(a => a.Processes.Any(p =>
                p.EndsWith('*') ? process.StartsWith(p[..^1], StringComparison.Ordinal) : p == process));
            if (byProcess != null) return byProcess;
            // Some apps run under a generic runtime (Minecraft under javaw): title only for those.
            if (process is not ("javaw" or "java")) return null;
        }

        return ByTitle(title);
    }

    /// <summary>
    /// A remote desktop window. Only a RemoteApp's title names the app inside ("Livro1 - Excel (Remoto)"); any other
    /// title is a whole desktop's, named after the machine or the workspace ("10.0.0.5 - Conexão de Área de Trabalho
    /// Remota", "TEAMS-HOST01"), which could happen to contain a hint, so it isn't matched.
    /// A RemoteApp's title is the app's own: the app is its last part ("Planilha Teams - Excel" is Excel, whatever the
    /// document is called), except in a browser, where the tab before it tells (as in <see cref="Identify"/>).
    /// </summary>
    static AppInfo IdentifyRemote(string title)
    {
        title = title.Trim();
        var suffix = RemoteAppSuffixes.FirstOrDefault(x => title.EndsWith(x, StringComparison.OrdinalIgnoreCase));
        if (suffix == null) return Remote.Value;
        title = title[..^suffix.Length].Replace("\u200b", "").TrimEnd(); // Edge writes "Microsoft\u200bEdge"
        int dash = title.LastIndexOf(" - ", StringComparison.Ordinal);
        var last = dash >= 0 ? title[(dash + 3)..].Trim() : title;
        if (BrowserTitles.Any(b => last.Equals(b, StringComparison.OrdinalIgnoreCase)))
            return ByTitle(title[..Math.Max(dash, 0)]) ?? Remote.Value;
        // Several names in it: the longest one is the most specific.
        return AppNames.Value.Where(n => n.pattern.IsMatch(last)).OrderByDescending(n => n.app.Name.Length)
            .Select(n => n.app).FirstOrDefault() ?? Remote.Value;
    }

    /// <summary>How browsers end their window titles ("Gatinhos - YouTube - Google Chrome").</summary>
    static readonly string[] BrowserTitles =
    {
        "Google Chrome", "Microsoft Edge", "Mozilla Firefox", "Firefox", "Brave", "Opera", "Vivaldi", "Arc", "Zen Browser",
    };

    static readonly Lazy<AppInfo> Remote = new(() => Apps.First(a => a.Group == AppGroup.Remote));

    /// <summary>Each app's name as a whole word ("Word", not in "Password"), built once.</summary>
    static readonly Lazy<(AppInfo app, Regex pattern)[]> AppNames = new(() => Apps
        .Where(a => a.Group != AppGroup.Remote && a.Name.Length > 2)
        .Select(a => (a, new Regex($@"(?<!\w){Regex.Escape(a.Name)}(?!\w)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
        .ToArray());

    /// <summary>The app whose longest title hint is in the title ("YouTube Music" over "YouTube").</summary>
    static AppInfo? ByTitle(string title)
    {
        AppInfo? best = null;
        int bestLength = 0;
        foreach (var app in Apps)
            foreach (var hint in app.Titles)
                if (hint.Length > bestLength && title.Contains(hint, StringComparison.OrdinalIgnoreCase))
                {
                    best = app;
                    bestLength = hint.Length;
                }
        return best;
    }

    /// <summary>Gesture and face that go with each group's lines.</summary>
    public static (Face face, Gesture gesture) Mood(AppGroup group) => group switch
    {
        AppGroup.Music or AppGroup.Games => (Face.Happy, Gesture.Dance),
        AppGroup.Social or AppGroup.Chat => (Face.Happy, Gesture.Wave),
        AppGroup.Code or AppGroup.Design => (Face.Normal, Gesture.Groom),
        AppGroup.Office or AppGroup.Email or AppGroup.Files or AppGroup.Remote => (Face.Normal, Gesture.Tap),
        AppGroup.Shopping => (Face.Surprised, Gesture.Tap),
        _ => (Face.Happy, Gesture.None),
    };
}
