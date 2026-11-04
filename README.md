# Webi 🕷️

Uma aranha-saltadora chibi em pixel art que vive no desktop do Windows: anda por cima das janelas,
contorna as bordas, escala as paredes da tela, desce por fios de teia, pula entre janelas e faz
companhia com reações pré-programadas, lembretes, pomodoro e monitor do sistema.

## Baixar e usar

Baixe o `Webi.exe` da [versão mais recente](https://github.com/Janeleiro/webi/releases/latest) e abra. É um arquivo único
que já traz tudo dentro: não precisa instalar nada, nem Godot, nem .NET. Funciona no Windows 10/11 64 bits.

- O exe não é assinado digitalmente, então o Windows pode mostrar "O Windows protegeu o computador".
  Clique em **Mais informações → Executar assim mesmo**.
- Configurações e estado ficam em `%APPDATA%\Godot\app_userdata\Webi\`. Para desinstalar, desligue
  "Iniciar com o Windows" no menu, saia da Webi e apague o exe e essa pasta.
- A versão aparece no fim do menu (botão direito na aranha ou na bandeja).

## Rodar (desenvolvimento)

- `webi.bat`: inicia direto, sem editor (aponta para o Godot instalado pelo winget).
- **Iniciar com o Windows:** opção no menu (botão direito ou bandeja). Ela grava a entrada `Webi` em
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, sem precisar de administrador, e o app mantém o
  caminho atualizado a cada início.
- Só abre uma Webi por vez: abrir de novo (pelo autostart e à mão, por exemplo) não cria uma segunda aranha.
- Para desenvolver, abra a pasta no Godot 4.7 .NET ou rode `dotnet build` e depois o Godot com `--path C:\dev\webi`.
- `WEBI_DEBUG=1` imprime o estado da aranha uma vez por segundo, no console do Godot.
- Para economizar, ela roda a 60 FPS e cai para 15 FPS dormindo ou escondida, voltando na hora a 60 com
  qualquer movimento do mouse ou tecla.

## Testes

```
dotnet test tests\Webi.Tests
```

Testes xUnit da lógica que não depende do motor: config, lembretes e adiamento, identificação de apps,
bordas das janelas (`SurfaceMap`), leitura dos timers, calendário (Páscoa, feriados, aniversário), amizade
e resumo do dia. O projeto de testes fica fora do build do Godot (`.gdignore` e
`Compile Remove` no `Webi.csproj`).

## Gerar o .exe

```
Godot_v4.7.2-stable_mono_win64_console.exe --headless --path C:\dev\webi --export-release "Windows Desktop" C:\dev\webi\build\Webi.exe
```

O resultado é um único `build\Webi.exe` (~190 MB, com o runtime .NET e os assets embutidos) que roda sem o
Godot instalado. É preciso ter os templates de exportação do Godot 4.7.2 .NET em
`%APPDATA%\Godot\export_templates\4.7.2.stable.mono`. O preset fica em `export_presets.cfg`, e o Godot
exige o `Webi.sln` para exportar projetos C#. Ao abrir o exe, a opção "Iniciar com o Windows" (se ligada)
passa a apontar para ele.

## Versão e release

A versão fica num lugar só: `application/config/version` no `project.godot` (ex.: `0.1.0`, no formato
[SemVer](https://semver.org/lang/pt-BR/)). O export grava esse número nas propriedades do exe (`file_version` e
`product_version` ficam vazios no preset de propósito, e o Godot usa a versão do projeto), e o app mostra a
versão no menu e a imprime no log ao iniciar.

Para publicar uma versão:

1. Atualize `config/version` no `project.godot` e faça o commit.
2. Gere o exe (seção acima) e teste.
3. Crie a tag: `git tag v0.1.0` e `git push origin v0.1.0`.
4. No GitHub, crie o release a partir da tag e anexe o `build\Webi.exe`.

## Teias

- **Teias de canto:** a Webi tece teias nos cantos de cima da tela (onde o teto encontra a parede) e
  nos cantos de cima de dentro das janelas. Ela vai até lá andando pelas bordas, ou pulando se o canto
  fica em outra janela, e tece com as patas da frente, com um fio saindo do abdômen até o ponto que está
  sendo tecido. Depois às vezes se pendura no meio da teia. Cada teia é gerada proceduralmente, com tamanho
  (150-320 px), número de raios e anéis, fios quebrados, moldura, fios soltos e caimento próprios. As teias
  de janela acompanham a janela, somem quando ela fecha e ficam ocultas quando outra janela as cobre.
- **Varrer:** clicar e arrastar o mouse por cima de uma teia de canto a arrebenta. O clique continua
  chegando ao app embaixo; as teias não bloqueiam cliques.
- **Teia gigante:** com o mouse parado por `screen_web_minutes` (5), ela pula até o teto, desce até o
  centro do monitor onde está o cursor, tece uma teia orbital cobrindo o monitor inteiro e dorme pendurada
  no meio. Mexer o mouse só a acorda. A teia só sai **chacoalhando o mouse** com força (muito deslocamento
  e várias idas e vindas em menos de 1 s); aí os fios se rompem e caem e ela despenca junto.
- **Menu:** "Fazer uma teia", "Limpar teias", "Tecer teias por conta própria" e "Teia gigante com o mouse
  parado" (desligado / 2 / 5 / 10 / 15 / 30 min). O config tem `auto_webs`, `max_webs` e `screen_web_minutes`. As teias não são salvas entre execuções.

## Alertas

Lembretes (água, pausa, pomodoro) e alertas do sistema (CPU, memória, bateria) são **gritados**: a Webi
dá um superpulo até o teto, despenca pelo fio até o centro da tela e grita num balão gigante em forma de
explosão (título enorme e uma linha de detalhe), que estoura ao aparecer e treme. Depois ela desce até o
chão. Vários alertas seguidos formam uma fila. Avisos positivos ("esfriou", "carregando") e respostas às
suas ações usam o balão normal. Clicar num balão o fecha. Os lembretes de água e de pausa trazem um botão
**⏰ Adiar 10 min**, que os faz voltar depois de mais 10 min de uso do PC.

**Não perturbe:** com o "Não perturbe"/Assistente de foco do Windows ligado, em modo de apresentação ou no
período de silêncio logo após instalar o Windows, ela segura os alertas gritados, não comenta os apps e não
tece a teia gigante, como no modo silencioso das reuniões. O "Não perturbe" é lido de um estado interno
(WNF) do Windows, que não é documentado: se a leitura falhar, vale só a API oficial
(`SHQueryUserNotificationState`). Com `WEBI_DEBUG=1`, o log mostra `dnd=` e o que foi lido.

## Comentários sobre o que você está fazendo

A Webi olha qual app está em primeiro plano (pelo processo) e, nos navegadores, qual site está na aba
ativa (pelo título da janela). Depois de **1 minuto seguido no mesmo app**, e com você usando o PC, ela solta
um comentário bem-humorado com expressão e gesto. Para não virar spam: no máximo um comentário a cada 3 min,
e o mesmo app só volta a ser comentado depois de 20 min. Ela não comenta dormindo, em tela cheia ou durante
um alerta.

- **Grupos:** redes sociais, vídeo, música, chat, código, escritório, e-mail, jogos, design, compras, IA,
  estudo e arquivos, cada um com 4-5 falas.
- **Apps e sites específicos:** cerca de 50, com 3-5 falas próprias cada (Instagram, TikTok, X, Facebook,
  Reddit, LinkedIn, YouTube, Netflix, Twitch, Spotify, WhatsApp, Discord, VS Code, Excel, Steam,
  Minecraft, Figma, Amazon, Mercado Livre, Wikipédia, Duolingo...). Ela usa a fala específica em 60% das
  vezes e a do grupo no resto, sem repetir as últimas.
- **Sessões longas:** 30 min de rede social, 90 min de vídeo e 2 h de jogo geram uma cutucada com os minutos.
- **Pomodoro:** depois de 10 min num app de trabalho sem pomodoro rodando, ela sugere ligar um.
- **Modo silencioso:** em apps de reunião (Zoom, Teams, Google Meet, Webex, Skype) e no OBS, ela não
  comenta, não tece a teia gigante e segura os alertas gritados até a reunião acabar.
- Tudo fica em memória, nada é salvo nem enviado. Liga e desliga em "💬 Comentar o que estou fazendo"
  (config `app_comments`). As falas e as regras de identificação ficam em `src/Assistant/AppCatalog.cs`.
  Com `WEBI_DEBUG=1`, o log mostra o app identificado e o processo em primeiro plano.

## Caça, cenas e reações

- **Moscas:** de tempos em tempos (3-8 min) aparece uma mosca em pixel art que voa pela tela, pousa nas
  bordas das janelas e vai embora depois de uns 2 min. A Webi chega perto, faz o **bote** (agachada, patas da
  frente erguidas, abdômen rebolando) e pula nela. Às vezes a mosca percebe e foge. Mosca que voa numa teia de
  canto fica presa: a Webi vai até a teia e come (se ela não vier em uns 3 min, a mosca se solta e vai embora). Menu "🪰 Moscas para caçar" (config `bugs`).
- **Caçar o cursor:** com o mouse parado perto dela, às vezes ela faz o bote e pula nele. Se o cursor se mexer
  antes, ela disfarça limpando as patinhas.
- **Carona no mouse:** com o cursor parado no ar acima dela (de vez em quando, a cada 2–5 min no máximo), ela
  pula, agarra a setinha e fica pendurada num fio curto, balançando com o mouse. Solta sozinha depois de 8–20 s,
  ou antes se você chacoalhar o mouse ou clicar nela, e cai com o embalo. Não acontece no modo "não perturbe".
- **Espiar:** em cima de uma janela, ela se esconde atrás da borda e só os olhos e as patas da frente aparecem.
  Chegar perto com o mouse a faz pular com um "Achou!".
- **Tropeço** (às vezes, correndo), **bocejo** alguns segundos antes de dormir e **"Z"s** em pixel art subindo
  enquanto dorme (no lugar do balão "z z z").
- **Carona:** arrastar a janela em que ela está rende um comentário; sacudir com força (vários movimentos
  rápidos seguidos) a derruba. Maximizar ou encaixar a janela não conta como sacudida.

## Datas especiais

Menu "🎃 Datas especiais" (config `seasonal`): chapéu de bruxa na semana do Halloween, gorro de Papai Noel em
dezembro, chapéu de festa no Ano Novo, no Carnaval e no **seu aniversário** ("🎂 Meu aniversário...", config
`birthday` como `dd/mm`), chapéu de palha na festa junina, orelhas na Páscoa e coração no Dia dos Namorados.
No dia em si ela cumprimenta uma vez (feriados nacionais, sexta e segunda também) e solta falas temáticas.
Carnaval e Páscoa são calculados. Tudo em `src/Assistant/Seasons.cs`.

## Conversa e amizade

- **Puxar assunto** (menu "🗨️", config `small_talk`): a cada 15-30 min de uso ela faz uma **pergunta com
  botões** no balão ("Bebeu água?", "Como você tá?", almoço, café, postura, "vai dormir?"...), conta uma
  **curiosidade de aranha** ou solta uma fala da data. A resposta muda a reação; "💧 Bebi!" conta no resumo.
- **Eventos do PC** (mesma opção): Print Screen ou Win+Shift+S, pendrive conectado/removido, internet caiu/voltou
  (precisa se repetir em duas checagens) e som mudo/desmudo.
- **8 horas de PC** no dia geram um aviso, e depois das 18h (com 2h de uso) ela oferece o resumo do dia.
- **Amizade** ("💖 Nossa amizade"): cliques, respostas, moscas, pomodoros e meia hora de companhia dão pontos
  (no máximo 20 por dia); clicar demais tira. Níveis: Conhecida, Colega (25), Amiga (90) e Melhor amiga (220).
  Cada nível muda o "oi" do início e libera falas novas; no último ela dá **abraço** de oito patas.

## Timers, post-its e resumo

- **⏲ Novo timer / lembrete...:** abre uma caixa de texto. "15", "15 min", "1h30 reunião", "2 horas", "em 10 min
  ligar" são durações; "14:30 dentista", "às 9", "14h" são horários (horário que já passou vale para amanhã).
  "Nh" sem "às" é horário a partir de 5h, então "1h30" é duração. Ela grita na hora, como os lembretes. Os
  timers sobrevivem a reiniciar o app; "⏰ Próximos lembretes" os lista e "🗑 Cancelar timers" apaga todos.
- **📝 Novo post-it...:** até 4 notas penduradas em fios de seda, que aparecem à direita do monitor principal.
  Dá pra arrastar cada nota pra qualquer lugar (o fio vai junto e a posição é lembrada); só a lixeira no canto
  superior direito da nota a tira.
- **📊 Resumo do dia:** tempo de PC, apps mais usados, pomodoros, copos d'água, moscas e cutucadas.

## Atalhos e sons

- **Ctrl+Alt+W** (Alt esquerdo) chama a Webi até embaixo do cursor; **Ctrl+Alt+H** esconde e mostra. O Alt
  direito não conta porque AltGr+W digita "?" no teclado ABNT2. Escondida, os alertas esperam.
- **🔊 Sons** (config `sounds`, desligado por padrão): efeitos 8-bit sintetizados no código (balão, pulo,
  pouso, grito, mosca, teia rasgando, nível novo), sem arquivos de áudio.

## O que ela lembra

`%APPDATA%\Godot\app_userdata\Webi\state.json` guarda a amizade, os números do dia (zerados a cada dia), os
timers e os post-its. É gravado a cada minuto e ao sair, e nada sai do PC. A gravação é feita num arquivo
temporário e depois trocada, para não corromper. Se mesmo assim o arquivo estiver inválido, ela começa do zero e
guarda o antigo como `state.json.bad`.

## Vários monitores

A Webi usa todos os monitores conectados. Cada monitor tem sua própria janela transparente, e todas
desenham o mesmo mundo. Ela nunca desce de seda colada na divisa entre dois monitores lado a lado (o corpo ficaria cortado entre
as telas): perto da divisa ela primeiro se afasta uns 120 px (na escala 2) e só então desce. Ao chegar à divisa entre monitores, a aranha **pula** para o outro lado e continua andando e o balão aparece no monitor
onde ela está. A borda da área de trabalho é o contorno da **união** dos monitores: onde dois monitores se
encostam, o chão continua; onde as alturas diferem, vira degrau ou parede. Se você conectar, desconectar ou
reorganizar monitores com o app aberto, as janelas se refazem em até 3 s. Ao entrar em tela cheia, ela só
some se estiver no monitor do app em tela cheia. `WEBI_DEBUG=1` também lista os monitores e as bordas no início.

## Interação

- **Clique** na aranha: ela responde com uma fala aleatória, uma expressão e um gesto. Às vezes é um
  cumprimento que depende da hora do dia. Clicar demais a deixa brava. **Arrastar**: ela fica pendurada no
  cursor por um fio; ao soltar, é arremessada.
- **Botão direito**: menu com pomodoro, "próximos lembretes", "como está o PC?", tamanho, intervalo dos
  lembretes de água e de pausa (desligado / 15 / 30 / 45 / 60 / 90 min), monitor do sistema, config e sair.
  Em **🔧 Ajustes**: tempo até dormir, limite do alerta de CPU, limite do alerta de bateria (só com bateria)
  e "Sumir com app em tela cheia". Com um pomodoro rodando, o tooltip da bandeja mostra o tempo que falta.
  Os lembretes contam só o tempo em que você está usando o PC (mouse ou teclado nos últimos 2 min), e o de
  pausa fica em espera enquanto um pomodoro roda. O mesmo menu aparece no ícone da bandeja, e o clique esquerdo no ícone equivale a cutucar a aranha.
- Todas as falas ficam em `src/Spider/Lines.cs`.
- Ela dorme depois de alguns minutos sem você mexer no mouse e no teclado, e some quando um app está em tela cheia.

## Cores

Menu "🎨 Cor" (config `fur_color`): marrom (original), preto, branco, cinza, vermelho, amarelo, laranja,
verde, azul, roxo e rosa. A recoloração é um **mapa de gradiente** aplicado uma vez, na troca de cor: a
luminosidade de cada pixel do pelo escolhe uma cor numa rampa (do escuro ao claro) da pelagem. O
sombreado se mantém e funciona até com preto, branco e cinza, que um giro de matiz não alcança. Os olhos
têm cores próprias por pelagem (preto: aro dourado; branco: aro azul; cinza: dourado...). O
aro é identificado pela posição (anel externo do olho), porque compartilha as cores da paleta do pelo. O
ícone da bandeja acompanha a cor. Para criar uma cor, acrescente uma entrada em `src/Spider/FurColors.cs`.

## Configuração

`%APPDATA%\Godot\app_userdata\Webi\config.json` (pelo menu: *Abrir config.json* e depois *Recarregar config*):

| chave | padrão | |
|---|---|---|
| `scale` | 2 | pixels de tela por pixel de arte (1-8) |
| `fur_color` | `marrom` | cor da pelagem (ver "Cores") |
| `water_minutes`, `break_minutes`, `pomodoro_*` | 45, 60, 25/5 | mínimo de 1 min |
| `cpu_alert_percent`, `battery_alert_percent` | 90, 20 | |
| `sleep_after_idle_minutes` | 5 | |
| `hide_on_fullscreen` | `true` | sumir com app em tela cheia |
| `sounds` | `false` | efeitos sonoros |
| `bugs` | `true` | moscas para caçar |
| `small_talk` | `true` | perguntas, curiosidades e eventos do PC |
| `seasonal` | `true` | chapéus e falas de datas especiais |
| `birthday` | `""` | aniversário, `dd/mm` |

Valores fora da faixa são corrigidos ao carregar (um lembrete de 0 min, por exemplo, vira 1 min). Se o
arquivo tiver um erro de JSON, ela avisa, usa os padrões e **não grava nada** até o arquivo ser consertado
e recarregado, para não apagar o que você escreveu.

## Arquitetura

```
src/Main.cs                 liga tudo; click-through dinâmico; menus
src/Win32/Autostart.cs      "iniciar com o Windows" (chave Run do usuário)
src/Win32/Overlay.cs        uma janela transparente por monitor (mundo 2D compartilhado), topmost, fora da taskbar,
                            click-through por janela, detecção de mudança de monitores
src/World/DesktopScanner.cs EnumWindows + DWM bounds (janelas visíveis, ordem Z)
src/World/Surfaces.cs       bordas das janelas e do contorno dos monitores, recortadas pelo que está por cima e encadeadas
src/Win32/DoNotDisturb.cs   "Não perturbe" / apresentação do Windows
src/Win32/SingleInstance.cs uma Webi por vez (mutex)
src/Spider/Spider*.cs       a aranha, em partes: estado e comandos (Spider.cs), simulação de andar, pular e fio
                            (Motion), corpo e gestos (Visuals), pernas com IK e passada (Legs)
src/Spider/SpiderRig.cs     desenho em pixels num SubViewport de baixa resolução (corpo, olhos animados, pernas)
src/Spider/SpiderBrain.cs   comportamento: passear, pular, descer de teia, curiosidade pelo cursor, susto, sono, cutucadas
src/Spider/Lines.cs         todas as falas e reações pré-programadas
src/World/Webs.cs           teias: cantos possíveis, geração procedural (canto e orbital gigante), rasgo e varredura
src/UI/ShoutBubble.cs       balão de grito gigante dos alertas
src/Assistant/*             lembretes/pomodoro, monitor do sistema, app em primeiro plano (AppWatcher + AppCatalog),
                            timers (QuickTimers), datas (Seasons), amizade, conversa (SmallTalk), eventos do PC
src/State.cs                state.json: amizade, números do dia, timers, post-its
src/World/Bugs.cs           moscas: voo, pouso, presas na teia
src/UI/*                    balões (com botões de resposta), post-its, caixa de texto, sons sintetizados
src/Win32/Hotkeys.cs        atalhos globais (lidos do estado do teclado)
tests/Webi.Tests            testes xUnit
```

## Arte

As partes da aranha (3 poses de corpo, 4 coxas, 4 canelas, pedipalpos e 6 olhos) foram geradas
numa prancha pelo GPT Image, usando o render do ComfyUI como referência. Depois foram recortadas e
reduzidas a pixel art com paleta comum por:

```
python tools/slice_parts.py art/raw/gpt_parts.png art/raw/gpt_anim.png art/parts
```

O script limpa a franja vermelha da exportação transparente, endireita as pernas (PCA) e gera os PNGs
em `art/parts/` mais o `parts.json` (pivôs, comprimentos dos segmentos, posição dos olhos). O rig
posiciona coxa e canela de cada perna com IK, troca o corpo entre normal, achatado e esticado e alterna
os sprites de olho para piscar e mostrar expressões. Para trocar a arte, gere uma prancha nova no mesmo
layout e rode o script de novo. Comprimentos e espessuras das pernas ficam no topo do script.

Depuração visual: `WEBI_SNAPSHOT=<pasta>` salva quadros do rig em PNG (`WEBI_SNAPSHOT_FPS` e
`WEBI_SNAPSHOT_MAX` controlam frequência e quantidade). `WEBI_DEMO=1` troca o comportamento por uma
sequência fixa de todas as animações: aceno, limpeza, bater o pé, espreguiçar, dança, bote, bocejo, abraço, tropeço, espiar, andar, correr
e pular.

## Animação

- **Locomoção:** ciclo de passada em onda de trás para frente (L4 R2 → L3 R1 → L2 R4 → L1 R3), com
  sempre só 2 patas no ar e 6 apoiadas. O ciclo é guiado pela distância percorrida, e a passada fica
  mais longa e mais alta quanto maior a velocidade. A pata se estica para fora no meio do passo.
  Às vezes ela dá uma corridinha rápida. Ao parar, as patas se reacomodam uma por vez.
  `WEBI_GAIT_LOG=1` imprime quais patas estão no ar em cada quadro.
- **Corpo:** balança como mola ao arrancar e parar, inclina na direção do movimento e respira
  quando está parada.
- **Olhos:** sempre acompanham o cursor (menos dormindo).
- **Gestos:** aceno (para o cursor e ao ser cutucada), limpeza dos pedipalpos, bater o pé
  (impaciente), espreguiçar (ao acordar), dança (pomodoro e alertas felizes), bote (antes de pular numa
  presa), bocejo, tropeço, abraço e espiar (o sprite é recortado na linha da borda onde ela está).
- **Chapéus:** desenhados pixel a pixel no rig (`SpiderRig.DrawHat`), por cima da cabeça. As patas
  dos gestos seguem um arco em volta do quadril, para nunca cruzarem o rosto.
- **Pulo:** agachamento proporcional à distância, pernas recolhidas na subida e abertas na descida,
  inclinação conforme a velocidade, giro antecipado para a superfície de pouso e pernas abertas no impacto.
- **Tecendo:** ela percorre a teia de verdade. Estica cada raio com o fio saindo do abdômen, volta pelo
  caminho, faz a moldura e os anéis de dentro para fora, com as patas trabalhando no ritmo do movimento.
  Depois se pendura no centro.
- **Sempre na tela:** o corpo nunca sai da área visível dos monitores (pendurada na borda, tecendo num
  canto, sendo arremessada...). Só atravessa a divisa entre dois monitores encostados.
- **Teia:** pernas abertas na descida, remando devagar quando parada e puxando o fio com as patas
  da frente na subida.

## Licença

- **Código** (inclusive os sons, sintetizados em `src/UI/Sfx.cs`): [GNU GPL v3](LICENSE). Você pode usar, estudar,
  modificar e redistribuir; versões modificadas que forem distribuídas também precisam ser GPL v3, com o código aberto.
- **Arte** (`art/`): [CC BY-NC 4.0](art/LICENSE.md). Uso e adaptação com crédito, sem fins comerciais. As folhas
  de origem foram criadas com auxílio de IA.

Copyright © 2026 Jota Carvalho.
