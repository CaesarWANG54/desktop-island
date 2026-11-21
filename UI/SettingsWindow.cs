using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopIsland.Core;
using Microsoft.Win32;

namespace DesktopIsland.UI;

public sealed class SettingsWindow : Window
{
    private readonly IslandWindow _island;
    private readonly TextBlock _status;
    private readonly System.Windows.Threading.DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public SettingsWindow(IslandWindow island)
    {
        _island=island; Title="灵动岛设置"; Width=440; Height=660; MinHeight=540;
        WindowStartupLocation=WindowStartupLocation.CenterScreen; ResizeMode=ResizeMode.CanMinimize;
        Background=new SolidColorBrush(Color.FromRgb(16,17,21)); Foreground=Brushes.White;
        FontFamily=new FontFamily("Segoe UI, Microsoft YaHei UI"); FontSize=13;
        var scroll = new ScrollViewer { VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
        var content=new StackPanel { Margin=new Thickness(26,22,26,20) }; scroll.Content=content; Content=scroll;
        content.Children.Add(new TextBlock { Text="灵动岛", FontSize=24, FontWeight=FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text="悬停快速展开 · Ctrl+Alt+I", Foreground=Brushes.DarkGray, Margin=new Thickness(0,7,0,20) });
        using var key=Registry.CurrentUser.OpenSubKey(RunKey);
        AddToggle(content,"开机启动",key?.GetValue("DesktopIsland") is string,value=>
        {
            using var k=Registry.CurrentUser.CreateSubKey(RunKey);
            if(value) k.SetValue("DesktopIsland",'"'+Environment.ProcessPath+'"'); else k.DeleteValue("DesktopIsland",false);
        });
        AddToggle(content,"自动切换游戏模式",island.Settings.AutomaticGameMode,v=>{island.Settings.AutomaticGameMode=v;island.SaveSettings();});
        AddToggle(content,"游戏中鼠标穿透",island.Settings.GameClickThrough,v=>{island.Settings.GameClickThrough=v;island.SaveSettings();});
        AddToggle(content,"显示其他软件的系统通知",island.Settings.NotificationsEnabled,async v=>{island.Settings.NotificationsEnabled=v;island.SaveSettings();if(v)await island.Notifications.StartAsync(true);else island.Notifications.Stop();});
        AddToggle(content,"Codex 任务完成提醒",island.Settings.CodexEnabled,island.SetCodexEnabled);
        AddToggle(content,"启用游戏帧率采集",island.Settings.FpsEnabled,async v=>{if(v)await island.EnableFpsAsync();else{island.Settings.FpsEnabled=false;island.SaveSettings();}});
        AddToggle(content,"倒计时结束提示音",island.Settings.TimerSound,v=>{island.Settings.TimerSound=v;island.SaveSettings();});
        AddToggle(content,"自动查找在线歌词",island.Settings.OnlineLyrics,async v=>{island.Settings.OnlineLyrics=v;island.SaveSettings();if(island.Media.Info.Active)await island.Lyrics.UpdateAsync(island.Media.Info,v,true);});
        Label(content,"音乐来源");
        var music=new ComboBox{ItemsSource=new[]{"自动选择","QQ 音乐","网易云音乐"},SelectedIndex=island.Settings.MusicSource switch{"qq"=>1,"netease"=>2,_=>0},Margin=new Thickness(0,8,0,14),Padding=new Thickness(6)};
        music.SelectionChanged+=(_,_)=>{island.Settings.MusicSource=music.SelectedIndex switch{1=>"qq",2=>"netease",_=>"auto"};island.SaveSettings();};content.Children.Add(music);
        Label(content,"悬停展开（毫秒）");
        var hover=new Slider{Minimum=100,Maximum=1000,Value=island.Settings.HoverDelayMs,TickFrequency=50,IsSnapToTickEnabled=true,AutoToolTipPlacement=System.Windows.Controls.Primitives.AutoToolTipPlacement.TopLeft,Margin=new Thickness(0,8,0,14)};
        hover.ValueChanged+=(_,_)=>{island.Settings.HoverDelayMs=(int)hover.Value;island.SaveSettings();};content.Children.Add(hover);
        content.Children.Add(ActionButton("管理天气地点",island.OpenWeatherSettings));
        var offset=new Slider { Minimum=0,Maximum=80,Value=island.Settings.TopOffset,Margin=new Thickness(0,8,0,12),TickFrequency=1,IsSnapToTickEnabled=true };
        Label(content,"距屏幕顶部"); content.Children.Add(offset); offset.ValueChanged+=(_,_)=>{island.Settings.TopOffset=offset.Value;island.SaveSettings();};
        Label(content,"歌词提前 / 延后（秒）");
        var lyric=new Slider { Minimum=-5,Maximum=5,Value=island.Settings.LyricOffsetSeconds,TickFrequency=.25,IsSnapToTickEnabled=true,Margin=new Thickness(0,8,0,10),AutoToolTipPlacement=System.Windows.Controls.Primitives.AutoToolTipPlacement.TopLeft,AutoToolTipPrecision=2 };
        content.Children.Add(lyric); lyric.ValueChanged+=(_,_)=>{island.Settings.LyricOffsetSeconds=lyric.Value;island.SaveSettings();};
        var row=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,4,0,14) };
        row.Children.Add(ActionButton("配置 / 重连帧率",()=>island.EnableFps()));
        row.Children.Add(ActionButton("导入歌词",async ()=>
        {
            if(!island.Media.Info.Active){MessageBox.Show(this,"先播放要导入歌词的歌曲。","导入歌词");return;}
            var file=new OpenFileDialog { Filter="LRC 歌词|*.lrc",Title="选择这首歌的歌词" };
            if(file.ShowDialog(this)==true)try{await island.Lyrics.ImportAsync(file.FileName,island.Media.Info);}catch(Exception e){MessageBox.Show(this,e.Message,"导入歌词");}
        })); content.Children.Add(row);
        Label(content,"自定倒计时（分钟）");
        var custom=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,7,0,13) };
        var minutes=new TextBox { Text="25",Width=70,Padding=new Thickness(7),Background=new SolidColorBrush(Color.FromRgb(32,33,38)),Foreground=Brushes.White,BorderThickness=new Thickness(0) };
        custom.Children.Add(minutes); custom.Children.Add(ActionButton("开始",()=>
        {
            if(double.TryParse(minutes.Text,out var value)&&value>0&&value<=10080){island.Timers.StartCountdown(value*60);island.SelectTab("timer");island.Expand();Close();}
        }));content.Children.Add(custom);
        Label(content,"额外游戏进程（逗号分隔）");
        var games=new TextBox { Text=string.Join(", ",island.Settings.GameProcesses),TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,Height=56,Margin=new Thickness(0,7,0,5),Padding=new Thickness(7),Background=new SolidColorBrush(Color.FromRgb(32,33,38)),Foreground=Brushes.White,BorderThickness=new Thickness(0),VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
        content.Children.Add(games);
        content.Children.Add(ActionButton("选择游戏程序",()=>{
            var dialog=new OpenFileDialog{Filter="游戏程序|*.exe",Title="选择游戏实际运行的 EXE"};
            if(dialog.ShowDialog(this)==true){var name=Path.GetFileNameWithoutExtension(dialog.FileName);if(!island.Settings.GameProcesses.Contains(name,StringComparer.OrdinalIgnoreCase))island.Settings.GameProcesses.Add(name);games.Text=string.Join(", ",island.Settings.GameProcesses);island.SaveSettings();}
        }));
        content.Children.Add(ActionButton("保存游戏列表",()=>{island.Settings.GameProcesses=games.Text.Split(new[]{',','，','\n','\r'},StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).ToList();island.SaveSettings();}));
        _status=new TextBlock { Foreground=Brushes.DarkGray,FontSize=12,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,16,0,12) };content.Children.Add(_status);
        content.Children.Add(ActionButton("通知访问设置",async ()=>{await island.Notifications.StartAsync(true);Process.Start(new ProcessStartInfo("ms-settings:privacy-notifications"){UseShellExecute=true});}));
        content.Children.Add(ActionButton("重新打开使用引导",island.ShowWelcome));
        content.Children.Add(ActionButton("设置 Codex 会话目录",()=>{
            var dialog=new OpenFolderDialog{Title="选择包含 sessions 的 Codex 目录"};
            if(dialog.ShowDialog(this)==true){island.Settings.CodexHome=dialog.FolderName;island.SaveSettings();MessageBox.Show(this,"目录已保存，重开 Desktop Island 后生效。","Codex 提醒");}
        }));
        content.Children.Add(ActionButton("使用说明",()=>Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"使用说明.html")){UseShellExecute=true})));
        content.Children.Add(new TextBlock { Text="游戏使用无边框全屏。游戏中按 Ctrl+Alt+I 展开。",Foreground=Brushes.DarkGray,FontSize=12,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,14,0,8) });
        content.Children.Add(ActionButton("退出灵动岛",island.Quit));
        _tick.Tick+=(_,_)=>UpdateStatus();_tick.Start();UpdateStatus();Closed+=(_,_)=>_tick.Stop();
    }
    private void UpdateStatus()=>_status.Text=$"{_island.Codex.Status}\n{_island.Notifications.Status}\n{_island.Fps.Reading.Status}";
    private static void Label(Panel p,string text)=>p.Children.Add(new TextBlock { Text=text,Margin=new Thickness(0,6,0,0) });
    private static Button ActionButton(string title,Action action)
    {
        var button=new Button { Content=title,Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,0,8,0),Background=new SolidColorBrush(Color.FromRgb(42,44,50)),Foreground=Brushes.White,BorderThickness=new Thickness(0),HorizontalAlignment=HorizontalAlignment.Left,Cursor=System.Windows.Input.Cursors.Hand };
        button.Click+=(_,_)=>action(); return button;
    }
    private static void AddToggle(Panel p,string label,bool value,Action<bool> change)
    {
        var control=new CheckBox { Content=label,IsChecked=value,Foreground=Brushes.White,Margin=new Thickness(0,0,0,12),Cursor=System.Windows.Input.Cursors.Hand };
        control.Checked+=(_,_)=>change(true);control.Unchecked+=(_,_)=>change(false);p.Children.Add(control);
    }
}
