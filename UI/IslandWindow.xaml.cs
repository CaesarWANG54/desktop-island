using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopIsland.Core;
using Microsoft.Win32;

namespace DesktopIsland.UI;

public partial class IslandWindow : Window
{
    public IslandSettings Settings { get; } = Storage.Read("settings.json", new IslandSettings());
    public MediaService Media { get; }
    public LyricsService Lyrics { get; } = new();
    public NoticeHub Notices { get; } = new();
    public CodexWatcher Codex { get; } = new();
    public NotificationService Notifications { get; } = new();
    public FpsService Fps { get; }
    public WeatherService Weather { get; }
    public TimerEngine Timers { get; } = new(Storage.Read("timers.json", new TimerState()));
    private readonly LocalBridge _bridge = new();
    private readonly GameDetector _games = new();
    private readonly DispatcherTimer _hover = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer _leave = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private System.Windows.Forms.NotifyIcon? _tray;
    private IntPtr _handle;
    private bool _expanded, _game, _stopwatch, _closing;
    private int _minutes = 25;
    private string _tab = "music", _lastArt = "";
    private double _targetWidth, _targetHeight;
    private DateTime _lastPoll = DateTime.MinValue, _lastDiagnostic = DateTime.MinValue;
    private ForegroundApp _foreground = new(0,"",false);
    private ImageSource? _codexIcon;
    private readonly bool _diagnostics;
    private int _testPid;
    private DateTime _testUntil;
    private DateTime _manualUntil;
    private SettingsWindow? _settingsWindow;
    private WeatherLocationsWindow? _weatherWindow;
    private WelcomeWindow? _welcomeWindow;
    private bool _configuringFps;
    public IslandWindow(bool diagnostics = false)
    {
        _diagnostics = diagnostics;
        Media=new(()=>Settings.MusicSource);
        Fps=new(()=>Settings.FpsEnabled);
        Weather=new WeatherService(Settings);
        InitializeComponent();
        _hover.Interval=TimeSpan.FromMilliseconds(Math.Clamp(Settings.HoverDelayMs,100,1000));
        Weather.Changed+=()=>{RefreshWeather();Refresh();};
        Media.ControlFailed+=text=>Notices.Add(new Notice("音乐",text));
        _hover.Tick += (_, _) => { _hover.Stop(); if (Island.IsMouseOver && !_game) Expand(); };
        _leave.Tick += (_, _) => { _leave.Stop(); if (!Island.IsMouseOver && DateTime.Now >= _manualUntil) Collapse(); };
        _tick.Tick += (_, _) => Tick();
        Timers.Changed += () => Storage.Write("timers.json", Timers.State);
        Timers.Elapsed += () =>
        {
            Notices.Add(new Notice("计时器", "倒计时结束", "时间到了", Guid.NewGuid().ToString()));
            if (Settings.TimerSound && !_game) System.Media.SystemSounds.Exclamation.Play();
        };
        Notices.Changed += RefreshNotices;
        Media.MediaUpdated += async info =>
        {
            if (info.Active) await Lyrics.UpdateAsync(info, Settings.OnlineLyrics);
            UpdateArtwork(); Refresh();
        };
        _bridge.Received += n => Dispatcher.BeginInvoke(() => OnBridge(n));
        Codex.Completed += n => Dispatcher.BeginInvoke(() => {if(Settings.CodexEnabled)Notices.Add(n with { Icon = _codexIcon });});
        Notifications.Received += n => { if (Settings.NotificationsEnabled) Notices.Add(n); };
        Loaded += async (_, _) => await StartAsync();
        Closed += (_, _) => DisposeServices();
    }
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e); _handle = new WindowInteropHelper(this).Handle;
        Native.SetWindowLong(_handle, -20, Native.GetWindowLong(_handle,-20) | 0x08000000 | 0x80);
        HwndSource.FromHwnd(_handle)?.AddHook(WindowProc);
        Native.RegisterHotKey(_handle, 1, 0x4003, 0x49); // Ctrl+Alt+I, no autorepeat.
        Position();
    }
    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == 0x21) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE
        if (msg == 0x0312 && w.ToInt32() == 1) { handled = true; if (_expanded) Collapse(); else Expand(); }
        if (msg == 0x007E || msg == 0x02E0) Dispatcher.BeginInvoke(Position);
        return IntPtr.Zero;
    }
    private async Task StartAsync()
    {
        try
        {
            var pkg = new Windows.Management.Deployment.PackageManager().FindPackagesForUser("").FirstOrDefault(p => p.Id.Name == "OpenAI.Codex");
            if (pkg != null) _codexIcon = Native.Icon(Path.Combine(pkg.InstalledLocation.Path,"app","ChatGPT.exe"));
        }
        catch { }
        BuildTray(); _bridge.Start(); _tick.Start(); RefreshNotices(); Refresh();
        await Task.WhenAll(Media.InitAsync(),Settings.NotificationsEnabled?Notifications.StartAsync():Task.CompletedTask,
            Settings.CodexEnabled?Task.Run(() => Codex.Start(Settings.CodexHome)):Task.CompletedTask,Weather.StartAsync());
        if(!Settings.SetupCompleted)ShowWelcome();
        Storage.Log("Island started");
    }
    private void BuildTray()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("展开灵动岛", null, (_, _) => Dispatcher.Invoke(Expand));
        menu.Items.Add("设置", null, (_, _) => Dispatcher.Invoke(OpenSettings));
        menu.Items.Add("启用 / 重连游戏帧率", null, (_, _) => Dispatcher.Invoke(EnableFps));
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(Quit));
        _tray = new System.Windows.Forms.NotifyIcon { Text = "灵动岛 · Ctrl+Alt+I 展开", Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application, Visible = true, ContextMenuStrip = menu };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(Expand);
    }
    private void Tick()
    {
        Timers.Tick();
        if ((DateTime.Now - _lastPoll).TotalSeconds >= 1)
        {
            _lastPoll = DateTime.Now;
            _foreground = _games.GetForeground();
            var game = Settings.AutomaticGameMode && Settings.FpsEnabled && GameDetector.IsGame(_foreground, Settings);
            if (_testPid > 0 && DateTime.Now < _testUntil) { game = true; _foreground = new(_testPid, "RenderProbe", true); }
            if (_game != game) { _game = game; _hover.Stop(); if (game) Collapse(); RefreshNotices(); }
            Fps.Update(_game ? _foreground.Pid : 0);
            Native.ClickThrough(_handle, _game && !_expanded && Settings.GameClickThrough);
            Native.Topmost(_handle);
        }
        Refresh();
        if (_expanded && _manualUntil != DateTime.MinValue && DateTime.Now > _manualUntil && !Island.IsMouseOver) Collapse();
        _tick.Interval = TimeSpan.FromMilliseconds(_game || (!Media.Info.Playing && !_expanded) ? 1000 : _stopwatch && _expanded ? 100 : 250);
        if (_diagnostics&&(DateTime.Now - _lastDiagnostic).TotalSeconds > 3)
        {
            _lastDiagnostic = DateTime.Now;
            Storage.TryWrite("diagnostics.json", new { processId = Environment.ProcessId, mode = _game ? "game" : Media.Info.Playing ? "music" : "clock", expanded = _expanded,
                width = Width, height = Height, left = Left, top = Top, notices = Notices.Items.Count, media = Media.Status,
                codex = Codex.Status, notifications = Notifications.Status, fps = Fps.Reading,collectorPid=Fps.CollectorPid, countdown = Timers.Remaining, stopwatch = Timers.StopwatchElapsed, tab = _tab,
                caption = PillMain.Text, lyricLines = Lyrics.Lines.Count, lyricStatus = Lyrics.Status,lyricSource=Lyrics.Source,
                hoverMs=_hover.Interval.TotalMilliseconds,track=Media.Info.Title,artist=Media.Info.Artist,album=Media.Info.Album,position=Media.Position,duration=Media.Info.Dur,playing=Media.Info.Playing,songId=Media.Info.SongId,mediaSource=Media.Info.Aumid,
                weatherPlace=Weather.Selected?.Name,weather=Weather.Selected is {} place?Weather.Get(place):null });
        }
    }
    private void Refresh()
    {
        var info = Media.Info;
        var lyrics = Lrc.At(Lyrics.Lines, Media.Position + Settings.LyricOffsetSeconds);
        string main, next = "", glyph; double width, height;
        PillArt.Visibility = Visibility.Collapsed;
        PillWeather.Visibility=Visibility.Collapsed;
        PillGlyph.Visibility = Visibility.Visible; PillIconHost.Visibility = Visibility.Visible;
        if (_game)
        {
            var fps = Fps.ForProcess(_foreground.Pid);
            main = fps is { } v ? $"{Math.Round(v):0} FPS" : "-- FPS";
            glyph = ""; width = 96; height = 26;
            PillIconHost.Visibility = Visibility.Collapsed;
            PillMain.FontSize = 12; PillMain.TextAlignment = TextAlignment.Center;
            Pill.Margin = new Thickness(10,0,10,0);
        }
        else
        {
            PillMain.FontSize = info.Playing ? 13 : 14; PillMain.TextAlignment = TextAlignment.Left;
            Pill.Margin = new Thickness(14,0,14,0);
            if (info.Playing)
            {
                main = lyrics.Current.Length > 0 ? lyrics.Current : info.Title;
                next = lyrics.Next.Length > 0 ? lyrics.Next : (Lyrics.Lines.Count > 0 ? "♪" : info.Artist);
                glyph = "\uE8D6"; width = 340; height = 58;
                if (PillArt.Source != null) { PillArt.Visibility = Visibility.Visible; PillGlyph.Visibility = Visibility.Collapsed; }
            }
            else if (Timers.CountdownActive)
            {
                main = Timers.State.Alarm ? "时间到了" : TimerEngine.Format(Timers.Remaining);
                glyph = "\uE916"; width = 164; height = 40;
            }
            else if (Timers.State.StopwatchStarted != null)
            {
                main = TimerEngine.Format(Timers.StopwatchElapsed); glyph = "\uE916"; width = 164; height = 40;
            }
            else
            {
                main = DateTime.Now.ToString("HH:mm"); glyph = "\uE823"; width = 164; height = 40;
                if(Weather.Selected is {} place&&Weather.Get(place) is {} reading)
                {
                    PillWeather.Visibility=Visibility.Visible;width=240;
                    PillWeatherIcon.Set(reading);Text(PillWeatherTemperature,WeatherService.Temperature(reading));
                    PillWeather.ToolTip=$"{place.Name} · {WeatherService.Condition(reading.Code)} · 最低 / 最高（℃）";
                }
            }
        }
        if (Notices.Items.Count > 0) width += _game ? 21 : Math.Min(2,Notices.Items.Count) * 22;
        Text(PillMain, main); Text(PillNext, next); Text(PillGlyph, glyph);
        PillNext.Visibility = next.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Pill.ToolTip = _game ? "Ctrl+Alt+I 展开" : null;
        var expandedHeight=_tab=="weather"?138+Math.Max(1,Settings.WeatherPlaces.Count)*32:282;
        ResizeIsland(_expanded ? 380 : width, _expanded ? expandedHeight : height);
        if (!_expanded) return;
        Text(PanelClock, DateTime.Now.ToString("HH:mm"));
        Text(TrackTitle, info.Active ? info.Title : "让音乐住进灵动岛");
        Text(TrackArtist, info.Active ? info.Artist : "播放网易云或 QQ 音乐后自动显示");
        Text(LyricCurrent, info.Active ? (lyrics.Current.Length > 0 ? lyrics.Current : Lyrics.Status) : "");
        Text(LyricNext, lyrics.Next);
        Text(TrackPosition, info.Active ? TimerEngine.Format(Math.Floor(Media.Position)) : "");
        Text(TrackDuration, info.Dur > 0 ? TimerEngine.Format(Math.Floor(info.Dur)) : "");
        PlayButton.Content = info.Playing ? "\uE769" : "\uE768";
        PlayButton.IsEnabled = info.Active;
        var seconds = _stopwatch ? Timers.StopwatchElapsed : Timers.CountdownActive ? Timers.Remaining : _minutes * 60;
        Text(TimerDisplay, TimerEngine.Format(seconds));
        if (_stopwatch) StartTimerButton.Content = Timers.State.StopwatchStarted != null ? "暂停" : Timers.StopwatchElapsed > 0 ? "继续" : "开始";
        else StartTimerButton.Content = Timers.State.Alarm ? "知道了" : Timers.State.Deadline != null ? "暂停" : Timers.State.CountdownSeconds > 0 ? "继续" : "开始";
        TimerPresets.Visibility = _stopwatch ? Visibility.Hidden : Visibility.Visible;
    }
    private static void Text(TextBlock target, string value) { if (target.Text != value) target.Text = value; }
    private void ResizeIsland(double width, double height)
    {
        if (_targetWidth == width && _targetHeight == height) return;
        _targetWidth = width; _targetHeight = height;
        var duration = TimeSpan.FromMilliseconds(_game ? 100 : 150);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var widthAnimation = new DoubleAnimation(Width, width + 16, duration) { EasingFunction = ease };
        widthAnimation.Completed += (_, _) => Position();
        BeginAnimation(WidthProperty, widthAnimation);
        BeginAnimation(HeightProperty, new DoubleAnimation(Height, height + 16, duration) { EasingFunction = ease });
        Island.CornerRadius = new CornerRadius(_expanded ? 26 : height / 2);
        SizeChanged -= Recenter; SizeChanged += Recenter;
        Position();
    }
    private void Recenter(object sender, SizeChangedEventArgs e) => Position();
    private void Position()
    {
        if (_handle == IntPtr.Zero) return;
        var primary = System.Windows.Forms.Screen.PrimaryScreen!;
        var source = PresentationSource.FromVisual(this);
        var scale = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var origin = scale.Transform(new Point(primary.Bounds.Left, primary.Bounds.Top));
        var size = scale.Transform(new Point(primary.Bounds.Width, primary.Bounds.Height));
        Left = origin.X + (size.X - Width) / 2;
        Top = origin.Y + Math.Clamp(Settings.TopOffset, 0, 200) - 8;
    }
    private void OnEnter(object sender, MouseEventArgs e) { _manualUntil = DateTime.MinValue; _leave.Stop(); if (!_expanded && !_game) { _hover.Stop(); _hover.Start(); } }
    private void OnLeave(object sender, MouseEventArgs e) { _hover.Stop(); if (_expanded) { _leave.Stop(); _leave.Start(); } }
    private void OnPillClick(object sender, MouseButtonEventArgs e) { if (!_game) Expand(); }
    private void OnContext(object sender, MouseButtonEventArgs e) { OpenSettings(); e.Handled = true; }
    public void Expand()
    {
        _hover.Stop(); _leave.Stop(); _expanded = true;
        _manualUntil = Island.IsMouseOver ? DateTime.MinValue : DateTime.Now.AddSeconds(15);
        Native.ClickThrough(_handle, false);
        Pill.Visibility = Visibility.Collapsed; Expanded.Visibility = Visibility.Visible;
        Refresh();
    }
    public void Collapse()
    {
        _hover.Stop(); _leave.Stop(); _expanded = false;
        Expanded.Visibility = Visibility.Collapsed; Pill.Visibility = Visibility.Visible;
        Native.ClickThrough(_handle, _game && Settings.GameClickThrough); Refresh();
    }
    private void OnTab(object sender, RoutedEventArgs e) => SelectTab((string)((Button)sender).Tag);
    public void SelectTab(string tab)
    {
        _tab = tab;
        MusicPage.Visibility = tab == "music" ? Visibility.Visible : Visibility.Collapsed;
        TimerPage.Visibility = tab == "timer" ? Visibility.Visible : Visibility.Collapsed;
        NoticePage.Visibility = tab == "notices" ? Visibility.Visible : Visibility.Collapsed;
        WeatherPage.Visibility=tab=="weather"?Visibility.Visible:Visibility.Collapsed;
        foreach (var b in new[]{MusicTab,TimerTab,NoticeTab,WeatherTab}) b.Background = (Brush)new BrushConverter().ConvertFrom((string)b.Tag == tab ? "#30333A" : "#141519")!;
        PanelTitle.Text = tab == "timer" ? "留一点时间" : tab == "notices" ? "消息" : tab=="weather"?"天气":"此刻";
        if(tab=="weather")RefreshWeather();
        Refresh();
    }
    private void OnSettings(object sender, RoutedEventArgs e) => OpenSettings();
    public void OpenSettings()
    {
        if (_settingsWindow != null) { _settingsWindow.Activate(); return; }
        _settingsWindow = new SettingsWindow(this); _settingsWindow.Closed += (_, _) => _settingsWindow = null; _settingsWindow.Show();
    }
    public void SaveSettings() { WeatherService.Normalize(Settings);_hover.Interval=TimeSpan.FromMilliseconds(Math.Clamp(Settings.HoverDelayMs,100,1000));Storage.Write("settings.json", Settings); Position(); Refresh(); }
    private void OnWeatherSettings(object sender,RoutedEventArgs e)=>OpenWeatherSettings();
    public void OpenWeatherSettings()
    {
        if(_weatherWindow!=null){_weatherWindow.Activate();return;}
        _weatherWindow=new WeatherLocationsWindow(this);_weatherWindow.Closed+=(_,_)=>_weatherWindow=null;_weatherWindow.Show();
    }
    public void RefreshWeather()
    {
        WeatherRows.Children.Clear();
        foreach(var place in Settings.WeatherPlaces)
        {
            var reading=Weather.Get(place);var row=new Grid {Height=31,Width=320};
            row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=new GridLength(30)});row.ColumnDefinitions.Add(new(){Width=new GridLength(100)});
            row.Children.Add(new TextBlock {Text=place.Name,FontSize=13,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis,
                Foreground=place.Id==Settings.SelectedWeatherPlace?(Brush)Resources["Mint"]:Brushes.White});
            var icon=new WeatherIcon {Width=21,Height=21,VerticalAlignment=VerticalAlignment.Center};icon.Set(reading);Grid.SetColumn(icon,1);row.Children.Add(icon);
            var temperature=new TextBlock {Text=reading!=null?WeatherService.Temperature(reading):"-- / --",FontSize=14,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center};
            Grid.SetColumn(temperature,2);row.Children.Add(temperature);
            var button=new Button {Content=row,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(6,0,6,0),Background=Brushes.Transparent,
                ToolTip=reading!=null?$"{place.Region} · {WeatherService.Condition(reading.Code)} · 最低 / 最高（℃）\n{reading.FetchedAt.LocalDateTime:HH:mm} 更新":"暂未取得天气，稍后自动重试"};
            button.Click+=(_,_)=>{Settings.SelectedWeatherPlace=place.Id;SaveSettings();RefreshWeather();};WeatherRows.Children.Add(button);
        }
        if(Settings.WeatherPlaces.Count==0)WeatherRows.Children.Add(new TextBlock {Text="添加本地天气",Foreground=(Brush)Resources["Quiet"],Margin=new Thickness(0,30,0,0),HorizontalAlignment=HorizontalAlignment.Center});
    }
    public async void EnableFps()=>await EnableFpsAsync();
    public async Task<bool> EnableFpsAsync()
    {
        if(_configuringFps)return false;
        _configuringFps=true;
        try
        {
            if(!Fps.Installed)
            {
                var error=await FpsSetup.InstallAsync();
                if(error!=null){Notices.Add(new Notice("帧率",error));return false;}
            }
            Settings.FpsEnabled=true;SaveSettings();Fps.Enable(out _);return true;
        }
        finally{_configuringFps=false;}
    }
    public void ShowWelcome(){if(_welcomeWindow!=null){_welcomeWindow.Activate();return;}_welcomeWindow=new WelcomeWindow(this);_welcomeWindow.Closed+=(_,_)=>_welcomeWindow=null;_welcomeWindow.Show();}
    public void SetCodexEnabled(bool enabled)
    {
        Settings.CodexEnabled=enabled;SaveSettings();_ = Task.Run(()=>Codex.SetEnabled(enabled,Settings.CodexHome));
    }
    private void OnPrevious(object sender, RoutedEventArgs e) => Media.Prev();
    private void OnNext(object sender, RoutedEventArgs e) => Media.Next();
    private void OnPlay(object sender, RoutedEventArgs e) => Media.PlayPause();
    private void OnCountdownMode(object sender, RoutedEventArgs e) { _stopwatch = false; CountdownTab.Foreground = (Brush)Resources["Mint"]; StopwatchTab.Foreground = (Brush)Resources["Quiet"]; Refresh(); }
    private void OnStopwatchMode(object sender, RoutedEventArgs e) { _stopwatch = true; StopwatchTab.Foreground = (Brush)Resources["Mint"]; CountdownTab.Foreground = (Brush)Resources["Quiet"]; Refresh(); }
    private void OnPreset(object sender, RoutedEventArgs e) { _minutes = int.Parse((string)((Button)sender).Tag); Timers.ResetCountdown(); Refresh(); }
    private void OnMinutes(object sender, RoutedEventArgs e) { _minutes = Math.Clamp(_minutes + int.Parse((string)((Button)sender).Tag),1,1440); Timers.ResetCountdown(); Refresh(); }
    private void OnStartTimer(object sender, RoutedEventArgs e)
    {
        if (_stopwatch) Timers.ToggleStopwatch();
        else if (Timers.State.Alarm) Timers.ResetCountdown();
        else if (Timers.State.Deadline != null) Timers.PauseCountdown();
        else if (Timers.State.CountdownSeconds > 0) Timers.ResumeCountdown();
        else Timers.StartCountdown(_minutes * 60);
        Refresh();
    }
    private void OnResetTimer(object sender, RoutedEventArgs e) { if (_stopwatch) Timers.ResetStopwatch(); else Timers.ResetCountdown(); Refresh(); }
    private void OnClearNotices(object sender, RoutedEventArgs e) { Notices.Clear(); if (Timers.State.Alarm) Timers.ResetCountdown(); }
    private UIElement NoticeIcon(Notice n, double size)
    {
        var icon = n.Icon ?? (n.App.Equals("Codex",StringComparison.OrdinalIgnoreCase) ? _codexIcon : null);
        if (icon != null) return new Image { Source = icon, Width = size, Height = size, ToolTip = n.App };
        return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size/3), Background = new SolidColorBrush(Color.FromRgb(61,68,80)), ToolTip = n.App,
            Child = new TextBlock { Text = n.App[..Math.Min(1,n.App.Length)], FontSize = size * .6, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
    }
    private void RefreshNotices()
    {
        PillBadges.Children.Clear();
        foreach (var n in Notices.Items.DistinctBy(n=>n.App).Take(_game ? 1 : 2))
        {
            var holder = new Border { Margin = new Thickness(3,0,0,0), Child = NoticeIcon(n,_game?14:19) };
            PillBadges.Children.Add(holder);
        }
        PillBadges.Visibility = Notices.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        NoticeTab.Content = Notices.Items.Count > 0 ? $"消息 {Notices.Items.Count}" : "消息";
        NoticeList.Children.Clear(); EmptyNotices.Visibility = Notices.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var notice in Notices.Items)
        {
            var row = new Grid { Margin = new Thickness(0,0,0,8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(29) });
            row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            row.Children.Add(NoticeIcon(notice,20));
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = notice.App + " · " + notice.Title, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
            if (notice.Body.Length > 0) content.Children.Add(new TextBlock { Text = notice.Body, FontSize = 11, Foreground = (Brush)Resources["Quiet"], Margin = new Thickness(0,4,0,0), TextTrimming = TextTrimming.CharacterEllipsis });
            var open = new Button { Content = content, Background = Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(0,5,0,5) };
            open.Click += (_, _) => { Native.FocusApp(notice.App); Notices.Dismiss(notice); };
            Grid.SetColumn(open,1); row.Children.Add(open);
            var dismiss = new Button { Content = "×", Background = Brushes.Transparent, Padding = new Thickness(3), FontSize = 15, ToolTip = "已读" };
            dismiss.Click += (_, _) => Notices.Dismiss(notice); Grid.SetColumn(dismiss,2); row.Children.Add(dismiss);
            NoticeList.Children.Add(row);
        }
        Refresh();
    }
    private void UpdateArtwork()
    {
        var info = Media.Info; var key = info.Aumid + info.Title + info.Artist;
        if (key == _lastArt) return; _lastArt = key;
        try
        {
            if (info.Art == null) { AlbumArt.Source = PillArt.Source = null; return; }
            using var stream = new MemoryStream(info.Art); var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.DecodePixelWidth = 80; image.EndInit(); image.Freeze();
            AlbumArt.Source = PillArt.Source = image;
        }
        catch { AlbumArt.Source = PillArt.Source = null; }
    }
    private void OnBridge(Notice notice)
    {
        if (notice.App != "__island_control") { Notices.Add(notice); return; }
        switch (notice.Title)
        {
            case "expand": Expand(); break;
            case "collapse": Collapse(); break;
            case "settings": OpenSettings(); break;
            case "quit": Quit(); break;
            case "fps-enable": EnableFps(); break;
            case "capture": if(_diagnostics)Capture(); break;
            case "capture-welcome": if(_diagnostics&&_welcomeWindow!=null)Capture(_welcomeWindow,"welcome-preview.png");break;
            case "welcome": ShowWelcome();break;
            case "clear": Notices.Clear(); Timers.ResetCountdown(); Timers.ResetStopwatch(); break;
            case "timer": SelectTab("timer"); Expand(); break;
            case "notices": SelectTab("notices"); Expand(); break;
            case "weather": SelectTab("weather"); Expand(); break;
            case "music": SelectTab("music"); Expand(); break;
            default:
                if (_diagnostics && notice.Title.StartsWith("countdown:") && int.TryParse(notice.Title[10..],out var sec)) Timers.StartCountdown(sec);
                if (_diagnostics && notice.Title.StartsWith("capture-process:") && int.TryParse(notice.Title[16..],out var pid)) { _testPid=pid; _testUntil=DateTime.Now.AddSeconds(60); }
                if(_diagnostics&&notice.Title=="media-play")Media.PlayPause();
                if(_diagnostics&&notice.Title=="media-next")Media.Next();
                if(_diagnostics&&notice.Title=="media-prev")Media.Prev();
                break;
        }
    }
    private void Capture()=>Capture(this,"preview.png");
    private static void Capture(Window target,string file)
    {
        var scale = VisualTreeHelper.GetDpi(target);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(target.ActualWidth*scale.DpiScaleX), (int)Math.Ceiling(target.ActualHeight*scale.DpiScaleY), scale.PixelsPerInchX,scale.PixelsPerInchY,PixelFormats.Pbgra32);
        bitmap.Render(target); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output=File.Create(Path.Combine(Storage.Root,file)); encoder.Save(output);
    }
    public void Quit() { if (_closing) return; _closing = true; _settingsWindow?.Close();_weatherWindow?.Close();_welcomeWindow?.Close(); Close(); Application.Current.Shutdown(); }
    private void DisposeServices()
    {
        _tick.Stop(); _hover.Stop(); _leave.Stop(); Native.UnregisterHotKey(_handle,1);
        Media.Dispose(); Lyrics.Dispose(); Codex.Dispose(); Notifications.Dispose(); _bridge.Dispose(); Fps.Dispose();Weather.Dispose();
        Storage.Write("timers.json", Timers.State);
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
    }
}
