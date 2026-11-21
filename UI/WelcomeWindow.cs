using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopIsland.Core;

namespace DesktopIsland.UI;

public sealed class WelcomeWindow : Window
{
    public WelcomeWindow(IslandWindow island)
    {
        Title="欢迎使用 Desktop Island";Width=470;Height=610;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        Background=new SolidColorBrush(Color.FromRgb(16,17,21));Foreground=Brushes.White;FontFamily=new("Segoe UI, Microsoft YaHei UI");FontSize=14;
        var content=new StackPanel{Margin=new Thickness(28)};Content=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        content.Children.Add(new TextBlock{Text="你的桌面灵动岛",FontSize=26,FontWeight=FontWeights.SemiBold});
        content.Children.Add(new TextBlock{Text="悬停展开 · Ctrl + Alt + I 随时呼出",Foreground=Brushes.DarkGray,Margin=new Thickness(0,8,0,23)});
        content.Children.Add(new TextBlock{Text="音乐播放器"});
        var music=new ComboBox{ItemsSource=new[]{"自动选择正在播放的音乐","QQ 音乐","网易云音乐"},SelectedIndex=island.Settings.MusicSource switch{"qq"=>1,"netease"=>2,_=>0},Margin=new Thickness(0,8,0,17),Padding=new Thickness(7)};
        content.Children.Add(music);
        var weather=new Button{Content="选择天气城市",Padding=new Thickness(12,8,12,8),HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,16)};
        weather.Click+=(_,_)=>island.OpenWeatherSettings();content.Children.Add(weather);
        var startup=Toggle("开机启动",AppPaths.AutoStart);content.Children.Add(startup);
        var fps=Toggle("启用游戏 FPS（首次需 Windows 授权）",true);content.Children.Add(fps);
        var notices=Toggle("显示 Windows 软件通知",island.Settings.NotificationsEnabled);content.Children.Add(notices);
        var codex=Toggle("Codex 任务完成提醒",island.Settings.CodexEnabled);content.Children.Add(codex);
        var online=Toggle("联网查找同步歌词",island.Settings.OnlineLyrics);content.Children.Add(online);
        content.Children.Add(new TextBlock{Text="时间跟随电脑。天气最多保存 5 个城市。\n在线歌词会发送歌曲信息；天气会发送所选城市坐标。",Foreground=Brushes.DarkGray,FontSize=12,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,7,0,18)});
        var status=new TextBlock{Foreground=Brushes.DarkGray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)};content.Children.Add(status);
        var start=new Button{Content="开始使用",Padding=new Thickness(18,9,18,9),Background=new SolidColorBrush(Color.FromRgb(179,246,207)),BorderThickness=new Thickness(0),HorizontalAlignment=HorizontalAlignment.Left};
        content.Children.Add(start);
        start.Click+=async (_,_)=>
        {
            start.IsEnabled=false;status.Text="正在保存设置…";
            island.Settings.MusicSource=music.SelectedIndex switch{1=>"qq",2=>"netease",_=>"auto"};
            island.Settings.OnlineLyrics=online.IsChecked==true;island.Settings.NotificationsEnabled=notices.IsChecked==true;
            AppPaths.AutoStart=startup.IsChecked==true;
            island.SetCodexEnabled(codex.IsChecked==true);
            if(fps.IsChecked==true){status.Text="正在配置游戏帧率…";await island.EnableFpsAsync();}
            if(notices.IsChecked==true)await island.Notifications.StartAsync(true);
            island.Settings.SetupCompleted=true;island.SaveSettings();Close();
        };
    }
    private static CheckBox Toggle(string text,bool value)=>new(){Content=text,IsChecked=value,Foreground=Brushes.White,Margin=new Thickness(0,0,0,13)};
}
