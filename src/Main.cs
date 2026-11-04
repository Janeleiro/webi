using System.Linq;
using Godot;
using Webi.Assistant;
using Webi.Spider;
using Webi.UI;
using Webi.Win32;
using Webi.World;
using SpiderNode = Webi.Spider.Spider;

namespace Webi;

/// <summary>Wires everything: overlay window, desktop scanning, the spider, UI, reminders and system monitor.</summary>
public partial class Main : Node2D
{
    const float ScanInterval = 0.15f;

    readonly Overlay _overlay = new();
    readonly SurfaceMap _map = new();
    Config _config = null!;
    DesktopScanner _scanner = null!;
    SpiderNode _spider = null!;
    SpiderBrain _brain = null!;
    Reminders _reminders = null!;
    SystemMonitor _monitor = null!;
    AppWatcher _apps = null!;
    readonly DoNotDisturb _dnd = new();
    AppState _state = null!;
    Friendship _friendship = null!;
    QuickTimers _timers = null!;
    SmallTalk _talk = null!;
    readonly SystemEvents _events = new();
    readonly Hotkeys _hotkeys = new();
    Sfx _sfx = null!;
    InputDialog _input = null!;
    StickyNotes _notes = null!;
    BugLayer _bugs = null!;
    /// <summary>The question in the bubble waiting for an answer.</summary>
    Question? _question;
    /// <summary>Hidden with Ctrl+Alt+H or the menu.</summary>
    bool _userHidden;
    float _saveTimer = 60, _seasonTimer, _togetherSeconds;

    SpeechBubble _bubble = null!;
    ShoutBubble _shout = null!;
    WebLayer _webs = null!;
    PopupMenu _menu = null!;
    /// <summary>Radio submenus (size, reminder intervals) and how to read their current value, for refreshing the checks.</summary>
    readonly System.Collections.Generic.List<(PopupMenu menu, System.Func<int> current)> _radioMenus = new();
    PopupMenu? _trayMenu;
    /// <summary>Menus with check items and labels to refresh: the main one, the tray one and their settings submenus.</summary>
    readonly System.Collections.Generic.List<PopupMenu> _checkMenus = new();
    StatusIndicator? _tray;

    float _scanTimer, _topmostTimer, _fullscreenTimer, _layoutTimer, _debugTimer, _tooltipTimer;
    /// <summary>Out of sight: behind a fullscreen app, or parked with no floor anywhere.</summary>
    bool _hidden;
    bool _fullscreenHide;
    bool _pressing, _dragging, _leftWasDown, _rightWasDown;
    Vector2 _pressPos;
    float _lastDelta = 1 / 60f;
    bool _greeted;

    enum MenuId
    {
        Pomodoro, PomodoroStatus, Upcoming, PcStatus, AppComments, MakeWeb, ClearWebs, AutoWebs, Monitor, Autostart, HideOnFullscreen,
        OpenConfig, ReloadConfig, DebugRestart, OpenLog, Quit,
        NewTimer, CancelTimers, NewNote, DaySummary, Friendship, Bugs, Sounds, SmallTalk, Seasonal, Birthday, Hide,
    }

    static readonly int[] ReminderIntervals = { 15, 30, 45, 60, 90 };
    static readonly int[] GiantWebMinutes = { 2, 5, 10, 15, 30 };
    static readonly int[] SleepMinutes = { 2, 5, 10, 15, 30 };
    static readonly int[] CpuAlerts = { 80, 85, 90, 95 };
    static readonly int[] BatteryAlerts = { 10, 15, 20, 30 };

    /// <summary>Frame rate while something is going on, and while asleep or out of sight with the mouse still.</summary>
    const int ActiveFps = 60, RestingFps = 15;
    /// <summary>Ids of the submenu entries, clear of every <see cref="MenuId"/>.</summary>
    const int SubmenuId = 100;

    public override void _Ready()
    {
        // A restart of itself waits for the old instance to let go; any other second start just leaves.
        bool restarting = OS.HasEnvironment("WEBI_RESTARTING");
        OS.UnsetEnvironment("WEBI_RESTARTING");
        if (!SingleInstance.Acquire(System.TimeSpan.FromSeconds(restarting ? 10 : 0)))
        {
            GD.Print("A Webi já está aberta; saindo.");
            SetProcess(false);
            GetTree().Quit();
            return;
        }

        Engine.MaxFps = ActiveFps;
        _config = Config.Load();
        _state = AppState.Load(AppState.FilePath);
        _friendship = new Friendship(_state);
        _overlay.Setup(this);
        _scanner = new DesktopScanner(_overlay);

        // Webs first so they are drawn under the spider.
        _webs = new WebLayer { PixelScale = _config.Scale };
        AddChild(_webs);
        _bugs = new BugLayer { PixelScale = _config.Scale, Enabled = _config.Bugs, Map = _map, Webs = _webs };
        AddChild(_bugs);
        _spider = new SpiderNode();
        AddChild(_spider);
        _spider.Setup(_map, _overlay.Bounds, PrimaryWork(), _config.Scale);
        RebuildWorld();
        _spider.SetFur(FurColors.Find(_config.FurColor));

        _brain = new SpiderBrain(_spider, _map)
        {
            Webs = _webs, AutoWebs = _config.AutoWebs, MaxWebs = _config.MaxWebs, Bugs = _bugs,
            WorkAreaAt = WorkAreaAt,
            FriendLevel = () => _friendship.Level,
        };
        _brain.Shout += notice =>
        {
            _bubble.Visible = false;
            _shout.Show(notice, SpiderBrain.ShoutSeconds);
            _sfx.Play(Sound.Alarm);
        };
        _brain.FlyEaten += () =>
        {
            _state.Today.Flies++;
            _state.Dirty = true;
            _friendship.Add(1);
            _sfx.Play(Sound.Crunch);
        };
        _bugs.Stuck += _ =>
        {
            _brain.OnFlyStuck();
            _sfx.Play(Sound.Buzz);
        };
        _spider.Carried += speed => _brain.OnCarried(speed);
        _spider.Jumped += () => _sfx.Play(Sound.Boing);
        _spider.Landed += () => _sfx.Play(Sound.Tap);
        _friendship.LevelUp += level =>
        {
            _brain.Alert(Face.Happy);
            _bubble.Show(Lines.LevelUp(level), 7);
            _sfx.Play(Sound.LevelUp);
        };
        _brain.Say += (text, secs) => _bubble.Show(text, secs);
        _spider.Landed += OnFirstLanding;

        _reminders = new Reminders(_config);
        _reminders.Fired += (kind, notice) =>
        {
            // The pomodoro just switched to its break: one more done today.
            if (kind == ReminderKind.Pomodoro && _reminders.PomodoroOnBreak)
            {
                _state.Today.Pomodoros++;
                _state.Dirty = true;
                _friendship.Add(2);
            }
            Notify(notice);
        };
        _timers = new QuickTimers(_state);
        _timers.Fired += Notify;
        _monitor = new SystemMonitor(_config);
        _monitor.Alert += (_, notice) => Notify(notice);
        _apps = new AppWatcher(_overlay)
        {
            Enabled = _config.AppComments,
            PomodoroRunning = () => _reminders.PomodoroRunning,
        };
        _apps.Comment += reaction =>
        {
            if (DebugMode) GD.Print($"[comment] {_apps.Current?.Id}: {reaction.Text}");
            _bubble.Show(_brain.Remark(reaction), 6);
        };
        _talk = new SmallTalk(_state, _friendship) { Enabled = _config.SmallTalk, Birthday = _config.Birthday, Seasonal = _config.Seasonal };
        _talk.Ask += q =>
        {
            _question = q;
            _brain.React(q.Face, 2);
            _bubble.Ask(q.Text, q.Choices.Select(c => c.Label).ToArray());
        };
        _talk.Say += reaction => _bubble.Show(_brain.Remark(reaction), 7);
        _talk.SummaryWanted += () => _bubble.Show(DaySummary.Describe(_state), 12);
        _events.Enabled = _config.SmallTalk;
        _events.Happened += reaction =>
        {
            // Events aren't held back for long: they only make sense right away.
            if (_hidden || _brain.Asleep || _brain.Busy || _shout.Visible || _dnd.Active || _brain.Quiet || _bubble.HasChoices) return;
            _bubble.Show(_brain.Remark(reaction), 4);
        };
        _hotkeys.Call += CallSpider;
        _hotkeys.Hide += ToggleHide;

        BuildUi();
        BuildTray();
        Autostart.Refresh();
        UpdateSeason();

        if (DebugMode)
        {
            foreach (var m in _overlay.Monitors) GD.Print($"[debug] monitor {m.Bounds} work={m.WorkArea} primary={m.Primary}");
            foreach (var surf in _map.All.Where(x => x.Owner < 0))
                GD.Print($"[debug] border {surf} next={(surf.Next != null ? surf.Next.ToString() : "-")}");
        }
    }

    /// <summary>Alerts are shouted from the middle of the screen; the rest goes in the normal bubble.</summary>
    void Notify(Notice notice)
    {
        if (notice.Shout)
        {
            _brain.Announce(notice);
            return;
        }
        _brain.React(notice.Happy ? Face.Happy : Face.Normal, 2);
        _bubble.Show(notice.Plain, 6);
    }

    Rect2 PrimaryWork() => new(_overlay.Primary.WorkArea.Position, _overlay.Primary.WorkArea.Size);

    /// <summary>Work area of the monitor containing the point, or the nearest one.</summary>
    Rect2 WorkAreaAt(Vector2 p)
    {
        var m = _overlay.MonitorAt(p);
        return new Rect2(m.WorkArea.Position, m.WorkArea.Size);
    }

    /// <summary>
    /// Rescans the windows and rebuilds the walkable world from the monitors not taken by a fullscreen app;
    /// if the spider is on a taken one it moves to a free monitor.
    /// </summary>
    void RebuildWorld()
    {
        var windows = _scanner.Scan(_config.HideOnFullscreen);
        var work = new System.Collections.Generic.List<Rect2>();
        Rect2? spawn = null;
        bool onBlocked = false;
        for (int i = 0; i < _overlay.Monitors.Count; i++)
        {
            var m = _overlay.Monitors[i];
            if (_scanner.BlockedMonitors.Contains(i))
            {
                onBlocked |= new Rect2(m.Bounds.Position, m.Bounds.Size).HasPoint(_spider.BodyCenter);
                continue;
            }
            var area = new Rect2(m.WorkArea.Position, m.WorkArea.Size);
            work.Add(area);
            if (m.Primary || spawn == null) spawn = area;
        }
        _map.Rebuild(windows, work);
        _bugs.Areas = work;
        _spider.SetBounds(_overlay.Bounds, spawn ?? PrimaryWork(), work);
        _webs.Sync(_map);
        if (onBlocked) _spider.Relocate();
        _spider.Resync();
    }

    void OnFirstLanding()
    {
        if (_greeted) return;
        _greeted = true;
        _brain.React(Face.Happy, 2);
        _brain.Greet(_overlay.CursorWorld());
        if (_config.LoadError != null) _bubble.Show(ConfigErrorText, 7);
        else if (!SpecialGreeting()) _bubble.Show(Lines.Hello(_friendship.Level), 7);
    }

    /// <summary>Holiday or birthday greeting, once a day. Returns false when there is none to give.</summary>
    bool SpecialGreeting()
    {
        if (!_config.Seasonal) return false;
        var day = Seasons.Today(System.DateTime.Now, _config.Birthday);
        var date = System.DateTime.Now.ToString("yyyy-MM-dd");
        if (day?.Greeting is not { } greeting || _state.LastSpecialGreeting == date) return false;
        _state.LastSpecialGreeting = date;
        _state.Dirty = true;
        _bubble.Show(_brain.Remark(greeting), 8);
        return true;
    }

    /// <summary>The hat of the day (rechecked every minute, so it changes at midnight).</summary>
    void UpdateSeason()
    {
        _spider.Hat = _config.Seasonal ? Seasons.Today(System.DateTime.Now, _config.Birthday)?.Hat ?? Hat.None : Hat.None;
        _talk.Birthday = _config.Birthday;
        _talk.Seasonal = _config.Seasonal;
    }

    // ------------------------------------------------------------------ shortcuts

    /// <summary>Ctrl+Alt+W: comes to the cursor (and out of hiding).</summary>
    void CallSpider()
    {
        if (_userHidden) ToggleHide();
        if (!_brain.Summon(_overlay.CursorWorld())) _bubble.Show("Peraí, tô no meio de um aviso!", 2.5f);
    }

    /// <summary>Ctrl+Alt+H: hides or shows her. Alerts wait while hidden.</summary>
    void ToggleHide()
    {
        _userHidden = !_userHidden;
        if (!_userHidden)
        {
            _brain.WakeUp();
            _bubble.Show(Lines.Pick(Lines.Unhidden), 3);
        }
    }

    const string ConfigErrorText = "O config.json tem um erro, então tô usando o padrão (e não mexo no arquivo até você consertar e recarregar). 🛠️";

    // ------------------------------------------------------------------ UI

    void BuildUi()
    {
        // The bubble is part of the world (not a CanvasLayer) so it shows on whichever monitor the spider is.
        _bubble = new SpeechBubble { Bounds = PrimaryWork(), ZIndex = 10 };
        AddChild(_bubble);
        _shout = new ShoutBubble { Bounds = PrimaryWork(), ZIndex = 11 };
        AddChild(_shout);
        _notes = new StickyNotes
        {
            Notes = _state.Notes, Bounds = PrimaryWork(), PixelScale = _config.Scale, ZIndex = 9,
            WorkAreaAt = WorkAreaAt,
        };
        AddChild(_notes);
        _notes.Moved += () => _state.Dirty = true;
        _notes.Refresh();
        _bubble.Popped += () => _sfx.Play(Sound.Pop);

        _sfx = new Sfx { Enabled = _config.Sounds };
        AddChild(_sfx);
        _input = new InputDialog { Visible = false };
        AddChild(_input);

        _menu = CreateMenu();
        AddChild(_menu);
        _menu.PopupHide += () => CallDeferred(MethodName.ReturnMenuHome);
    }

    /// <summary>Builds a menu (used for the right-click menu and the tray); each gets its own submenus.</summary>
    PopupMenu CreateMenu()
    {
        // Popups are transient children of the (always-on-top) overlay, so they already stay above it.
        // Don't set AlwaysOnTop on them: Godot can't make an on-top window transient, and the submenus then
        // fail to open and take the whole menu down.
        var menu = new PopupMenu();
        _checkMenus.Add(menu);
        menu.AddItem("🍅 Iniciar pomodoro", (int)MenuId.Pomodoro);
        menu.AddItem("⏱ Status do pomodoro", (int)MenuId.PomodoroStatus);
        menu.AddItem("⏰ Próximos lembretes", (int)MenuId.Upcoming);
        menu.AddItem("⏲ Novo timer / lembrete...", (int)MenuId.NewTimer);
        menu.AddItem("🗑 Cancelar timers", (int)MenuId.CancelTimers);
        menu.AddItem("📝 Novo post-it...", (int)MenuId.NewNote);
        menu.AddItem("📊 Resumo do dia", (int)MenuId.DaySummary);
        menu.AddItem("💖 Nossa amizade", (int)MenuId.Friendship);
        menu.AddItem("💻 Como está o PC?", (int)MenuId.PcStatus);
        menu.AddSeparator();
        // Submenus get their own ids: without one Godot uses the item's position, which collided with the
        // MenuId values (the web checkboxes were being checked on the reminder submenus instead).
        menu.AddSubmenuNodeItem("🎨 Cor",
            RadioMenu(FurColors.All.Select((c, i) => (c.Name, i)),
                () => System.Array.FindIndex(FurColors.All, c => c.Id == FurColors.Find(_config.FurColor).Id), SetFur), SubmenuId);
        menu.AddSubmenuNodeItem("📏 Tamanho",
            RadioMenu(new[] { 1, 2, 3, 4 }.Select(n => ($"{n}x", n)), () => _config.Scale, SetScale), SubmenuId + 1);
        var intervals = new[] { ("Desligado", 0) }.Concat(ReminderIntervals.Select(m => ($"A cada {m} min", m))).ToArray();
        menu.AddSubmenuNodeItem("💧 Lembrete de água",
            RadioMenu(intervals, () => _config.WaterReminder ? _config.WaterMinutes : 0, SetWaterInterval), SubmenuId + 2);
        menu.AddSubmenuNodeItem("🧘 Lembrete de pausa",
            RadioMenu(intervals, () => _config.BreakReminder ? _config.BreakMinutes : 0, SetBreakInterval), SubmenuId + 3);
        menu.AddCheckItem("💬 Comentar o que estou fazendo", (int)MenuId.AppComments);
        menu.AddCheckItem("🗨️ Puxar assunto (perguntas, curiosidades, eventos do PC)", (int)MenuId.SmallTalk);
        menu.AddSeparator();
        menu.AddItem("🕸️ Fazer uma teia", (int)MenuId.MakeWeb);
        menu.AddItem("🧹 Limpar teias", (int)MenuId.ClearWebs);
        menu.AddCheckItem("🕷️ Tecer teias por conta própria", (int)MenuId.AutoWebs);
        menu.AddSubmenuNodeItem("🕸️ Teia gigante com o mouse parado",
            RadioMenu(new[] { ("Desligado", 0) }.Concat(GiantWebMinutes.Select(m => ($"Depois de {m} min", m))),
                () => _config.ScreenWebMinutes, SetGiantWeb), SubmenuId + 4);
        menu.AddCheckItem("🪰 Moscas para caçar", (int)MenuId.Bugs);
        menu.AddSeparator();
        menu.AddCheckItem("🔊 Sons", (int)MenuId.Sounds);
        menu.AddCheckItem("🎃 Datas especiais (chapéus e falas)", (int)MenuId.Seasonal);
        menu.AddItem("🎂 Meu aniversário...", (int)MenuId.Birthday);
        menu.AddItem("🙈 Esconder (Ctrl+Alt+H)", (int)MenuId.Hide);
        menu.AddSeparator();
        menu.AddCheckItem("📊 Monitor do sistema", (int)MenuId.Monitor);
        menu.AddCheckItem("🚀 Iniciar com o Windows", (int)MenuId.Autostart);
        menu.AddSubmenuNodeItem("🔧 Ajustes", SettingsMenu(), SubmenuId + 5);
        menu.AddSeparator();
        menu.AddItem("⚙ Abrir config.json", (int)MenuId.OpenConfig);
        menu.AddItem("🔄 Recarregar config", (int)MenuId.ReloadConfig);
        menu.AddItem(DebugMode ? "🐞 Reiniciar em modo normal" : "🐞 Reiniciar em modo debug", (int)MenuId.DebugRestart);
        if (DebugMode) menu.AddItem("📄 Abrir log de debug", (int)MenuId.OpenLog);
        menu.AddSeparator();
        menu.AddItem("👋 Sair", (int)MenuId.Quit);
        menu.IdPressed += id =>
        {
            OnMenu((MenuId)(int)id);
            RefreshMenus();
        };
        menu.AboutToPopup += RefreshMenus;
        return menu;
    }

    /// <summary>The less used settings that used to need config.json: sleep, alert thresholds, fullscreen.</summary>
    PopupMenu SettingsMenu()
    {
        var menu = new PopupMenu();
        _checkMenus.Add(menu);
        menu.AddSubmenuNodeItem("😴 Dormir sem uso",
            RadioMenu(SleepMinutes.Select(m => ($"Depois de {m} min", m)), () => _config.SleepAfterIdleMinutes, SetSleep), SubmenuId);
        menu.AddSubmenuNodeItem("🔥 Alerta de CPU",
            RadioMenu(CpuAlerts.Select(n => ($"Acima de {n}%", n)), () => _config.CpuAlertPercent, SetCpuAlert), SubmenuId + 1);
        if (_monitor.HasBattery)
            menu.AddSubmenuNodeItem("🔋 Alerta de bateria",
                RadioMenu(BatteryAlerts.Select(n => ($"Abaixo de {n}%", n)), () => _config.BatteryAlertPercent, SetBatteryAlert), SubmenuId + 2);
        menu.AddCheckItem("🙈 Sumir com app em tela cheia", (int)MenuId.HideOnFullscreen);
        menu.IdPressed += id =>
        {
            if (id >= SubmenuId) return;
            OnMenu((MenuId)(int)id);
            RefreshMenus();
        };
        menu.AboutToPopup += RefreshMenus;
        return menu;
    }

    void SetGiantWeb(int minutes)
    {
        _config.ScreenWebMinutes = minutes;
        _config.Save();
        _bubble.Show(minutes > 0
            ? $"Se o mouse ficar parado {minutes} min, eu cubro a tela de teia. Hehe. 🕸️"
            : "Tá bom, nada de teia gigante.", 4);
    }

    void SetSleep(int minutes)
    {
        _config.SleepAfterIdleMinutes = minutes;
        _config.Save();
        _bubble.Show($"Combinado: depois de {minutes} min sem você mexer no PC eu tiro um cochilo. 😴", 4);
    }

    void SetCpuAlert(int percent)
    {
        _config.CpuAlertPercent = percent;
        _config.Save();
        _bubble.Show($"Eu grito se a CPU passar de {percent}% por um tempinho. 🔥", 4);
    }

    void SetBatteryAlert(int percent)
    {
        _config.BatteryAlertPercent = percent;
        _config.Save();
        _bubble.Show($"Eu aviso quando a bateria cair para {percent}%. 🔋", 4);
    }

    PopupMenu RadioMenu(System.Collections.Generic.IEnumerable<(string label, int id)> items, System.Func<int> current, System.Action<int> apply)
    {
        var sub = new PopupMenu();
        foreach (var (label, id) in items) sub.AddRadioCheckItem(label, id);
        // Refresh every menu right away: the tray menu doesn't always announce that it's opening.
        sub.IdPressed += id =>
        {
            apply((int)id);
            RefreshMenus();
        };
        sub.AboutToPopup += RefreshMenus;
        _radioMenus.Add((sub, current));
        return sub;
    }

    void SetFur(int index)
    {
        var fur = FurColors.All[index];
        _config.FurColor = fur.Id;
        _config.Save();
        _spider.SetFur(fur);
        UpdateTrayIcon();
        _brain.React(Face.Happy, 2);
        _bubble.Show($"Pelagem nova: {fur.Name.ToLowerInvariant()}! Combinou comigo? 🎨", 4);
    }

    void UpdateTrayIcon()
    {
        if (_tray == null) return;
        var icon = SpiderRig.LoadTexture("res://art/parts/icon.png");
        if (icon == null) return;
        _tray.Icon = ImageTexture.CreateFromImage(FurColors.RecolorFur(icon.GetImage(), FurColors.Find(_config.FurColor)));
    }

    void SetWaterInterval(int minutes)
    {
        _config.WaterReminder = minutes > 0;
        if (minutes > 0) _config.WaterMinutes = minutes;
        _config.Save();
        _reminders.ResetWater();
        _bubble.Show(minutes > 0
            ? $"Combinado! Te lembro de beber água a cada {minutes} min de uso do PC. 💧"
            : "Lembrete de água desligado.", 5);
    }

    void SetBreakInterval(int minutes)
    {
        _config.BreakReminder = minutes > 0;
        if (minutes > 0) _config.BreakMinutes = minutes;
        _config.Save();
        _reminders.ResetBreak();
        _bubble.Show(minutes > 0
            ? $"Combinado! A cada {minutes} min de uso do PC eu te chamo pra uma pausa. 🧘"
            : "Lembrete de pausa desligado.", 5);
    }

    void BuildTray()
    {
        if (!DisplayServer.HasFeature(DisplayServer.Feature.StatusIndicator)) return;
        _trayMenu = CreateMenu();
        AddChild(_trayMenu);

        _tray = new StatusIndicator { Tooltip = "Webi" };
        AddChild(_tray);
        UpdateTrayIcon();
        _tray.Menu = _tray.GetPathTo(_trayMenu);
        _tray.Pressed += (button, _) =>
        {
            if (button == (long)MouseButton.Left) Poke();
        };
    }

    /// <summary>
    /// Opens the right-click menu at the cursor. Godot keeps a popup inside the window that owns it, so the menu
    /// is moved under the overlay window of the monitor the cursor is on (and back home once closed, so it
    /// survives the extra windows being rebuilt when monitors change).
    /// </summary>
    void OpenMenu(Vector2 at)
    {
        var host = _overlay.WindowAt(at);
        if (_menu.GetParent() != host) _menu.Reparent(host, keepGlobalTransform: false);
        _menu.Position = new Vector2I((int)at.X, (int)at.Y);
        _menu.ResetSize();
        _menu.Popup();
    }

    void ReturnMenuHome()
    {
        if (_menu.GetParent() != this) _menu.Reparent(this, keepGlobalTransform: false);
    }

    bool AnyMenuOpen() =>
        _menu.Visible || (_trayMenu?.Visible ?? false) || _radioMenus.Any(r => r.menu.Visible);

    void RefreshMenus()
    {
        foreach (var m in _checkMenus)
        {
            void Check(MenuId id, bool on) { int i = m.GetItemIndex((int)id); if (i >= 0) m.SetItemChecked(i, on); }
            void Text(MenuId id, string t) { int i = m.GetItemIndex((int)id); if (i >= 0) m.SetItemText(i, t); }
            Check(MenuId.Monitor, _config.SystemMonitor);
            Check(MenuId.AutoWebs, _config.AutoWebs);
            Check(MenuId.AppComments, _config.AppComments);
            Check(MenuId.HideOnFullscreen, _config.HideOnFullscreen);
            Check(MenuId.Autostart, Autostart.IsEnabled());
            Check(MenuId.Bugs, _config.Bugs);
            Check(MenuId.Sounds, _config.Sounds);
            Check(MenuId.SmallTalk, _config.SmallTalk);
            Check(MenuId.Seasonal, _config.Seasonal);
            Text(MenuId.Hide, _userHidden ? "🙉 Mostrar (Ctrl+Alt+H)" : "🙈 Esconder (Ctrl+Alt+H)");
            Text(MenuId.Pomodoro, _reminders.PomodoroRunning ? "🍅 Parar pomodoro" : "🍅 Iniciar pomodoro");
        }
        foreach (var (sub, current) in _radioMenus)
        {
            int value = current();
            for (int i = 0; i < sub.ItemCount; i++)
                sub.SetItemChecked(i, sub.GetItemId(i) == value);
        }
    }

    void OnMenu(MenuId id)
    {
        switch (id)
        {
            case MenuId.Pomodoro:
                if (_reminders.PomodoroRunning)
                {
                    _reminders.StopPomodoro();
                    _bubble.Show("Pomodoro parado. Quando quiser, a gente recomeça!", 4);
                }
                else
                {
                    _reminders.StartPomodoro();
                    _brain.Alert(Face.Happy);
                    _bubble.Show($"Pomodoro iniciado: {_config.PomodoroWorkMinutes} minutos de foco! Fico de olho (nos oito). 🍅", 6);
                }
                break;
            case MenuId.PomodoroStatus: _bubble.Show(_reminders.PomodoroStatus(), 5); break;
            case MenuId.PcStatus: _bubble.Show(_monitor.Describe(), 6); break;
            case MenuId.Upcoming:
                _bubble.Show(_reminders.Upcoming() + (_timers.Count > 0 ? "\n" + _timers.Describe(System.DateTime.Now) : ""), 8);
                break;
            case MenuId.NewTimer: AskTimer(); break;
            case MenuId.CancelTimers:
                _bubble.Show(_timers.Count > 0 ? $"Cancelei {(_timers.Count == 1 ? "o timer" : $"os {_timers.Count} timers")}. ⏲" : "Não tinha nenhum timer ligado.", 3);
                _timers.Clear();
                break;
            case MenuId.NewNote: AskNote(); break;
            case MenuId.DaySummary: _bubble.Show(DaySummary.Describe(_state), 12); break;
            case MenuId.Friendship: _bubble.Show(_friendship.Describe(), 8); break;
            case MenuId.Bugs:
                _config.Bugs = !_config.Bugs;
                _config.Save();
                _bugs.Enabled = _config.Bugs;
                if (_config.Bugs) _bugs.Spawn();
                else _bugs.Clear();
                _bubble.Show(_config.Bugs ? "Olha, uma mosca! Hora da caça. 🪰" : "Sem moscas por aqui, então. 🧹", 3);
                break;
            case MenuId.Sounds:
                _config.Sounds = !_config.Sounds;
                _config.Save();
                _sfx.Enabled = _config.Sounds;
                _sfx.Play(Sound.Chirp);
                _bubble.Show(_config.Sounds ? "Pi-pi! Agora eu faço barulhinho. 🔊" : "Modo silencioso. 🔇", 3);
                break;
            case MenuId.SmallTalk:
                _config.SmallTalk = !_config.SmallTalk;
                _config.Save();
                _talk.Enabled = _events.Enabled = _config.SmallTalk;
                _bubble.Show(_config.SmallTalk ? "Oba! De vez em quando eu puxo assunto. 🗨️" : "Tá bom, só falo quando você me chamar.", 4);
                break;
            case MenuId.Seasonal:
                _config.Seasonal = !_config.Seasonal;
                _config.Save();
                UpdateSeason();
                _bubble.Show(_config.Seasonal ? "Datas especiais ligadas! Já separei meus chapéus. 🎩" : "Tá bom, sem chapéu. 🙂", 3);
                break;
            case MenuId.Birthday: AskBirthday(); break;
            case MenuId.Hide: ToggleHide(); break;
            case MenuId.MakeWeb:
                _brain.WakeUp();
                if (_spider.State != Mode.Grounded) _bubble.Show("Deixa eu pousar primeiro!", 3);
                else _brain.StartWeb();
                break;
            case MenuId.ClearWebs:
                _webs.ClearAll();
                _brain.OnGiantTorn();
                _bubble.Show("Faxina feita! 🧹 Sem teias por aqui.", 3);
                break;
            case MenuId.AppComments:
                _config.AppComments = !_config.AppComments;
                _config.Save();
                _apps.Enabled = _config.AppComments;
                _bubble.Show(_config.AppComments
                    ? "Vou dar uns pitacos no que você estiver fazendo. 👀"
                    : "Tá bom, fico de quelíceras fechadas. 🤐", 4);
                break;
            case MenuId.HideOnFullscreen:
                _config.HideOnFullscreen = !_config.HideOnFullscreen;
                _config.Save();
                _bubble.Show(_config.HideOnFullscreen
                    ? "Quando um jogo ou vídeo ocupar a tela, eu saio de fininho. 🙈"
                    : "Vou ficar por aqui até em tela cheia! 👀", 4);
                break;
            case MenuId.AutoWebs:
                _config.AutoWebs = !_config.AutoWebs;
                _config.Save();
                _brain.AutoWebs = _config.AutoWebs;
                _bubble.Show(_config.AutoWebs ? "Vou tecer umas teias de vez em quando. 🕸️" : "Combinado, só teço quando você pedir.", 4);
                break;
            case MenuId.Monitor:
                _config.SystemMonitor = !_config.SystemMonitor;
                _config.Save();
                _bubble.Show(_config.SystemMonitor
                    ? "Fico de olho na CPU, na memória e na bateria. 📊"
                    : "Tá bom, não grito mais sobre o PC.", 4);
                break;
            case MenuId.Autostart:
                bool on = !Autostart.IsEnabled();
                Autostart.SetEnabled(on);
                _bubble.Show(on ? "Combinado! Eu apareço sempre que você ligar o PC. 🕷️" : "Tá bom, não apareço mais por conta própria... só quando você me chamar.", 4);
                break;
            case MenuId.OpenConfig: OS.ShellOpen(Config.FilePath); break;
            case MenuId.ReloadConfig:
                CopyConfig(Config.Load(), _config);
                _spider.SetPixelScale(_config.Scale);
                _spider.SetFur(FurColors.Find(_config.FurColor));
                UpdateTrayIcon();
                _webs.PixelScale = _config.Scale;
                _brain.AutoWebs = _config.AutoWebs;
                _brain.MaxWebs = _config.MaxWebs;
                _apps.Enabled = _config.AppComments;
                _bugs.Enabled = _config.Bugs;
                _bugs.PixelScale = _notes.PixelScale = _config.Scale;
                _sfx.Enabled = _config.Sounds;
                _talk.Enabled = _events.Enabled = _config.SmallTalk;
                UpdateSeason();
                _bubble.Show(_config.LoadError != null ? ConfigErrorText : "Configurações recarregadas!", _config.LoadError != null ? 7 : 3);
                break;
            case MenuId.DebugRestart: Restart(debug: !DebugMode); break;
            case MenuId.OpenLog: OS.ShellOpen(ProjectSettings.GlobalizePath("user://logs/godot.log")); break;
            case MenuId.Quit: GetTree().Quit(); break;
        }
    }

    static bool DebugMode => OS.HasEnvironment("WEBI_DEBUG");

    /// <summary>Starts a fresh instance (same exe and arguments) with or without WEBI_DEBUG, then quits.</summary>
    void Restart(bool debug)
    {
        if (debug) OS.SetEnvironment("WEBI_DEBUG", "1");
        else OS.UnsetEnvironment("WEBI_DEBUG");
        OS.SetEnvironment("WEBI_RESTARTING", "1");
        if (OS.CreateProcess(OS.GetExecutablePath(), OS.GetCmdlineArgs()) < 0)
        {
            OS.UnsetEnvironment("WEBI_RESTARTING");
            _bubble.Show("Não consegui me reiniciar... 😵", 4);
            return;
        }
        GetTree().Quit();
    }

    static void CopyConfig(Config from, Config to)
    {
        foreach (var p in typeof(Config).GetProperties().Where(p => p.CanWrite))
            p.SetValue(to, p.GetValue(from));
    }

    void SetScale(int scale)
    {
        _config.Scale = scale;
        _config.Save();
        _spider.SetPixelScale(scale);
        _webs.PixelScale = scale;
        _bugs.PixelScale = scale;
        _notes.PixelScale = scale;
    }

    void Poke()
    {
        if (_userHidden) ToggleHide();
        var text = _brain.Poke(_overlay.CursorWorld());
        _state.Today.Pokes++;
        _friendship.Add(Lines.TooManyPokes.Contains(text) ? -2 : 1);
        _sfx.Play(Sound.Chirp);
        _bubble.Show(text, 4);
    }

    // ------------------------------------------------------------------ text input (timers, notes, birthday)

    void AskTimer()
    {
        _input.Ask("Quando eu te aviso? Exemplos: \"15 min\", \"1h30 reunião\", \"às 14h ligar pro banco\", \"14:30 dentista\".",
            "25 min tirar o bolo do forno", "", text =>
            {
                var now = System.DateTime.Now;
                if (QuickTimers.TryParse(text, now, out var due, out var what))
                {
                    _timers.Add(due, what);
                    _brain.React(Face.Happy, 1.5f);
                    _bubble.Show(QuickTimers.Confirmation(due, what, now), 5);
                }
                else _bubble.Show("Hmm, não entendi quando. Tenta algo como \"15 min\" ou \"às 14h reunião\". 🤔", 5);
            }, 140);
    }

    void AskNote()
    {
        if (_state.Notes.Count >= StickyNotes.Max)
        {
            _bubble.Show($"Já tem {StickyNotes.Max} post-its pendurados! Clica na lixeira de um que já foi resolvido pra tirar. 📝", 5);
            return;
        }
        _input.Ask("O que eu anoto no post-it?", "comprar café", "", text =>
        {
            _state.Notes.Add(new StickyNote { Text = text, Created = System.DateTime.Now });
            _state.Dirty = true;
            _notes.Refresh();
            _brain.React(Face.Happy, 1.5f);
            _bubble.Show("Anotado! Pendurei ali do lado. Arrasta pra onde quiser e clica na lixeira quando resolver. 📝", 5);
        }, 120);
    }

    void EditNote(StickyNote note)
    {
        _input.Ask("Editar o post-it:", "comprar café", note.Text, text =>
        {
            // It may have gone to the trash while the dialog was open.
            if (!_state.Notes.Contains(note) || text == note.Text) return;
            note.Text = text;
            _state.Dirty = true;
            _notes.Refresh();
            _brain.React(Face.Happy, 1.5f);
            _bubble.Show("Corrigido! ✏️", 3);
        }, 120);
    }

    void AskBirthday()
    {
        _input.Ask("Qual o dia do seu aniversário? (dia/mês, ex.: 27/10). Deixe vazio e cancele pra não mudar.", "dd/mm", _config.Birthday, text =>
        {
            if (!Seasons.TryParseBirthday(text, out int d, out int m))
            {
                _bubble.Show("Hmm, não entendi a data. Usa dia/mês, tipo 27/10. 🎂", 4);
                return;
            }
            _config.Birthday = $"{d:00}/{m:00}";
            _config.Save();
            UpdateSeason();
            var now = System.DateTime.Now;
            if (now.Day == d && now.Month == m) SpecialGreeting();
            else _bubble.Show($"Anotado: {d:00}/{m:00}! Nesse dia vai ter festa. 🎉", 4);
        }, 10);
    }

    // ------------------------------------------------------------------ loop & input

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _lastDelta = dt;
        var cursor = _overlay.CursorWorld();

        _scanTimer -= dt;
        if (_scanTimer <= 0)
        {
            _scanTimer = ScanInterval;
            RebuildWorld();
        }

        _layoutTimer -= dt;
        if (_layoutTimer <= 0)
        {
            _layoutTimer = 3;
            if (_overlay.CheckLayout()) RebuildWorld();
        }

        // Re-raise the overlay now and then, but never over an open menu.
        _topmostTimer -= dt;
        if (_topmostTimer <= 0 && !AnyMenuOpen())
        {
            _topmostTimer = 2;
            _overlay.ReassertTopmost();
        }

        _fullscreenTimer -= dt;
        if (_fullscreenTimer <= 0)
        {
            _fullscreenTimer = 1;
            _fullscreenHide = _config.HideOnFullscreen && _scanner.FullscreenMonitor() is Rect2 full && full.HasPoint(_spider.BodyCenter);
        }
        bool hide = _fullscreenHide || _spider.Parked || _userHidden;
        if (hide != _hidden)
        {
            _hidden = hide;
            _spider.Visible = !hide;
            if (hide) _bubble.Visible = false;
        }

        double idle = Native.IdleMilliseconds() / 1000.0;
        _hotkeys.Tick();
        TrackDay(delta, idle);
        _timers.Tick(delta, System.DateTime.Now);
        _reminders.Tick(delta, idle < 120);
        _monitor.Tick(delta);
        bool canTalk = !_hidden && !_brain.Asleep && !_brain.Busy && !_shout.Visible && !_dnd.Active && !_bubble.HasChoices;
        _apps.Tick(delta, idle, canTalk);
        _talk.Tick(idle < 120 ? delta : 0, System.DateTime.Now, canTalk && !_apps.Quiet && !_bubble.Visible);
        _events.Tick(delta);
        _dnd.Tick(delta);
        _brain.Quiet = _apps.Quiet || _dnd.Active;
        if (!_hidden)
            _brain.Tick(dt, cursor, idle, _config.SleepAfterIdleMinutes * 60);

        PollMouse(cursor);
        if (_dragging) _spider.DragTo(cursor);
        _spider.Speaking = _bubble.IsTyping;

        // Little moves while asleep or out of sight; any mouse or keyboard input brings the full rate back
        // right away (shaking the giant web down needs it).
        int fps = (_hidden || _brain.Asleep) && !_dragging && idle > 1 ? RestingFps : ActiveFps;
        if (Engine.MaxFps != fps) Engine.MaxFps = fps;

        if (_tray != null && (_tooltipTimer -= dt) <= 0)
        {
            _tooltipTimer = 1;
            var tip = _reminders.PomodoroRunning ? $"Webi · 🍅 {_reminders.PomodoroStatus()}" : "Webi";
            if (_tray.Tooltip != tip) _tray.Tooltip = tip;
        }

        if (DebugMode && (_debugTimer -= dt) <= 0)
        {
            _debugTimer = 1;
            GD.Print($"[debug] {_spider.State} body={_spider.BodyCenter.Round()} surf={_spider.Surface} walking={_spider.IsWalking} " +
                     $"surfaces={_map.All.Count} windows={_map.Windows.Count} face={_spider.Expression} asleep={_brain.Asleep} " +
                     $"app={_apps.Current?.Id ?? "-"} proc={_apps.LastProcess} dwell={_apps.Dwell:0}s quiet={_apps.Quiet} " +
                     $"blocked=[{string.Join(",", _scanner.BlockedMonitors)}] parked={_spider.Parked} hidden={_hidden} " +
                     $"dnd={_dnd.Active} ({_dnd.Describe}) fps={Engine.MaxFps}");
        }

        var head = _spider.BodyCenter - new Vector2(0, 34 * _spider.PixelScale);
        _bubble.Target = head;
        _shout.Target = head;
        var monitor = _overlay.MonitorAt(_spider.BodyCenter);
        _bubble.Bounds = new Rect2(monitor.WorkArea.Position, monitor.WorkArea.Size);
        _shout.Bounds = _bubble.Bounds;
        _notes.Bounds = PrimaryWork();
        _notes.Cursor = cursor;

        TrackMouse(dt, cursor);

        // Clicks pass through the overlay except over the spider and open UI.
        bool interactive = _pressing || _dragging || _notes.Dragging
            || (!_hidden && _spider.HitRect().HasPoint(cursor))
            || (_bubble.Visible && _bubble.GetGlobalRect().HasPoint(cursor))
            || (_shout.Visible && _shout.GetGlobalRect().HasPoint(cursor))
            || _notes.NoteAt(cursor) != null;
        _overlay.UpdateClickThrough(cursor, interactive);
    }

    /// <summary>Today's numbers (time at the PC and per app), friendship for time together, saving now and then.</summary>
    void TrackDay(double dt, double idle)
    {
        if ((_seasonTimer -= (float)dt) <= 0)
        {
            _seasonTimer = 60;
            _state.RollDay(System.DateTime.Now);
            UpdateSeason();
        }
        if (idle < 120)
        {
            _state.Today.ActiveSeconds += dt;
            if (idle < 60 && _apps.Current is { } app)
                _state.Today.AppSeconds[app.Name] = System.Collections.Generic.CollectionExtensions.GetValueOrDefault(_state.Today.AppSeconds, app.Name) + dt;
            // A friendship point for every half hour together.
            _togetherSeconds += (float)dt;
            if (_togetherSeconds >= 30 * 60)
            {
                _togetherSeconds = 0;
                _friendship.Add(1);
            }
            _state.Dirty = true;
        }
        if ((_saveTimer -= (float)dt) <= 0)
        {
            _saveTimer = 60;
            if (_state.Dirty) _state.Save();
        }
    }

    public override void _ExitTree()
    {
        // Only the instance that got going has something to save (a second copy quits right away).
        if (_state != null && _timers != null) _state.Save();
    }

    /// <summary>
    /// Mouse buttons are read straight from Windows: the overlay is several OS windows and the click can land
    /// on any of them, so per-window input events would have to be merged anyway.
    /// </summary>
    void PollMouse(Vector2 cursor)
    {
        bool left = Native.IsMouseDown(primary: true), right = Native.IsMouseDown(primary: false);
        bool overSpider = !_hidden && _spider.HitRect().HasPoint(cursor);

        if (left && !_leftWasDown)
        {
            if (overSpider)
            {
                _pressing = true;
                _pressPos = cursor;
            }
            else if (_bubble.ChoiceAt(cursor) is var choice and >= 0 && _question != null)
            {
                var picked = _question.Choices[choice];
                _question = null;
                _bubble.Show(_brain.Remark(picked.Reply), 5);
                picked.Effect?.Invoke();
            }
            else if (_bubble.Visible && _bubble.GetGlobalRect().HasPoint(cursor))
            {
                _bubble.Visible = false;
            }
            else if (_notes.TrashAt(cursor) is { } done)
            {
                _state.Notes.Remove(done);
                _state.Dirty = true;
                _notes.Refresh();
                _brain.React(Face.Happy, 1.5f);
                _bubble.Show(Lines.Pick(Lines.NoteDone), 3);
            }
            else if (_notes.PencilAt(cursor) is { } editing)
            {
                EditNote(editing);
            }
            else if (_notes.NoteAt(cursor) != null)
            {
                _notes.StartDrag(cursor);
            }
            else if (_shout.OverSnooze(cursor) && _shout.Snoozable is { } kind)
            {
                _reminders.Snooze(kind);
                _shout.Visible = false;
                _brain.DismissShout();
                _brain.React(Face.Happy, 1.5f);
                _bubble.Show($"Tá bom, te lembro de novo daqui a {Reminders.SnoozeMinutes} min. ⏰", 4);
            }
            else if (_shout.Visible && _shout.GetGlobalRect().HasPoint(cursor))
            {
                _shout.Visible = false;
                _brain.DismissShout();
            }
        }
        else if (left && _notes.Dragging)
        {
            _notes.DragTo(cursor);
        }
        else if (!left && _notes.Dragging)
        {
            _notes.EndDrag();
        }
        else if (!left && _leftWasDown && _pressing)
        {
            _pressing = false;
            if (_dragging)
            {
                _dragging = false;
                _spider.EndDrag(_lastDelta);
                _brain.React(Face.Surprised, 0.8f);
            }
            else Poke();
        }
        else if (left && _pressing && !_dragging && cursor.DistanceTo(_pressPos) > 8)
        {
            _dragging = true;
            _brain.WakeUp();
            _brain.PickedUp();
            _spider.StartDrag(cursor);
            _bubble.Show(Lines.Pick(Lines.Dragged), 1.5f);
        }

        if (right && !_rightWasDown && overSpider) OpenMenu(cursor);

        // Dragging the mouse through a corner web (not while holding the spider) sweeps it away.
        if (left && _leftWasDown && !_pressing && !_notes.Dragging && cursor != _lastCursor && _webs.SweptBy(_lastCursor, cursor) is { } swept)
        {
            bool wasHanging = _spider.HangingWeb == swept;
            _webs.Tear(swept);
            _sfx.Play(Sound.Rip);
            if (wasHanging) _spider.Fall();
            _brain.React(Face.Sad, 2);
            _bubble.Show(Lines.Pick(Lines.WebSwept), 3);
        }

        _leftWasDown = left;
        _rightWasDown = right;
    }

    // ------------------------------------------------------------------ mouse idle / shaking

    Vector2 _lastCursor;
    double _mouseStill;
    readonly System.Collections.Generic.Queue<(double t, Vector2 p)> _trail = new();

    /// <summary>
    /// Mouse still for a long time: Webi webs over the monitor under the cursor. Shaking the mouse hard
    /// (lots of distance with several back-and-forths in under a second) tears that web down.
    /// </summary>
    void TrackMouse(float dt, Vector2 cursor)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        _mouseStill = cursor.DistanceTo(_lastCursor) > 1 ? 0 : _mouseStill + dt;

        _trail.Enqueue((now, cursor));
        while (_trail.Count > 0 && now - _trail.Peek().t > 0.8) _trail.Dequeue();
        float length = 0;
        int reversals = 0, lastSign = 0;
        Vector2? prev = null;
        foreach (var (_, p) in _trail)
        {
            if (prev is Vector2 q)
            {
                length += q.DistanceTo(p);
                float dx = p.X - q.X;
                if (Mathf.Abs(dx) > 6)
                {
                    int sign = Mathf.Sign(dx);
                    if (lastSign != 0 && sign != lastSign) reversals++;
                    lastSign = sign;
                }
            }
            prev = p;
        }
        // Not while carrying a post-it around: that's moving it, not shaking.
        if (length > 2400 && reversals >= 4 && !_notes.Dragging && _webs.Giant is { } giant)
        {
            _webs.Tear(giant);
            _sfx.Play(Sound.Rip);
            _brain.OnGiantTorn();
            _trail.Clear();
        }

        if (_config.ScreenWebMinutes > 0 && _mouseStill > _config.ScreenWebMinutes * 60 && _webs.Giant == null && !_brain.Quiet)
        {
            var m = _overlay.MonitorAt(cursor);
            if (_brain.StartGiantWeb(new Rect2(m.Bounds.Position, m.Bounds.Size))) _mouseStill = 0;
        }
        _lastCursor = cursor;
    }
}
