using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DesktopIsland.Core;

namespace DesktopIsland.UI;

public sealed class WeatherLocationsWindow : Window
{
    private readonly IslandWindow _island;
    private readonly StackPanel _saved=new(),_results=new();
    private readonly TextBox _query=new(){Height=32,Padding=new Thickness(9,5,9,5),Background=new SolidColorBrush(Color.FromRgb(34,37,43)),Foreground=Brushes.White,BorderThickness=new Thickness(0)};
    private readonly TextBlock _status=new(){Foreground=Brushes.DarkGray,Margin=new Thickness(0,10,0,8),FontSize=12};
    private readonly Button _search;
    private bool _closed;
    public WeatherLocationsWindow(IslandWindow island)
    {
        _island=island;Title="天气地点";Width=460;Height=570;MinHeight=420;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        Background=new SolidColorBrush(Color.FromRgb(16,17,21));Foreground=Brushes.White;FontFamily=new("Segoe UI, Microsoft YaHei UI");FontSize=13;
        var content=new StackPanel{Margin=new Thickness(24,22,24,20)};
        Content=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        content.Children.Add(new TextBlock{Text="天气地点",FontSize=23,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,15)});
        content.Children.Add(_saved);
        var row=new Grid{Margin=new Thickness(0,18,0,0)};row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=new GridLength(76)});
        _query.ToolTip="输入城市或区县";System.Windows.Automation.AutomationProperties.SetName(_query,"城市或区县");row.Children.Add(_query);
        _search=Button("搜索",async ()=>await SearchAsync());_search.Margin=new Thickness(8,0,0,0);Grid.SetColumn(_search,1);row.Children.Add(_search);content.Children.Add(row);
        _query.KeyDown+=async (_,e)=>{if(e.Key==Key.Enter){e.Handled=true;await SearchAsync();}};
        content.Children.Add(_status);content.Children.Add(_results);
        var source=Button("天气数据：Open-Meteo",()=>Process.Start(new ProcessStartInfo("https://open-meteo.com/"){UseShellExecute=true}));
        source.Background=Brushes.Transparent;source.Foreground=Brushes.DarkGray;source.FontSize=11;source.Margin=new Thickness(0,18,0,0);source.HorizontalAlignment=HorizontalAlignment.Left;content.Children.Add(source);
        Closed+=(_,_)=>_closed=true;RenderSaved();
    }
    private static Button Button(string text,Action action)
    {
        var button=new Button{Content=text,Padding=new Thickness(10,5,10,5),Background=new SolidColorBrush(Color.FromRgb(40,44,51)),Foreground=Brushes.White,BorderThickness=new Thickness(0),Cursor=Cursors.Hand};
        button.Click+=(_,_)=>action();return button;
    }
    private void RenderSaved()
    {
        _saved.Children.Clear();
        foreach(var place in _island.Settings.WeatherPlaces)
        {
            var row=new Grid{Margin=new Thickness(0,0,0,8)};row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new(){Width=new GridLength(30)});
            row.Children.Add(new TextBlock{Text=place.Name,VerticalAlignment=VerticalAlignment.Center,ToolTip=place.Region,TextTrimming=TextTrimming.CharacterEllipsis});
            var select=Button(place.Id==_island.Settings.SelectedWeatherPlace?"默认":"设为默认",()=>{_island.Settings.SelectedWeatherPlace=place.Id;Save();});Grid.SetColumn(select,1);row.Children.Add(select);
            var remove=Button("×",()=>{_island.Settings.WeatherPlaces.RemoveAll(p=>p.Id==place.Id);Save();});remove.Background=Brushes.Transparent;remove.ToolTip="移除 "+place.Name;Grid.SetColumn(remove,2);row.Children.Add(remove);_saved.Children.Add(row);
        }
        _status.Text=$"已保存 {_island.Settings.WeatherPlaces.Count} / 5";
        _search.IsEnabled=_island.Settings.WeatherPlaces.Count<5;
    }
    private void Save()
    {
        _island.SaveSettings();_island.RefreshWeather();RenderSaved();_ = _island.Weather.RefreshAsync();
    }
    private async Task SearchAsync()
    {
        if(!_search.IsEnabled)return;var query=_query.Text.Trim();if(query.Length<2){_status.Text="请输入城市或区县";return;}
        _search.IsEnabled=false;_results.Children.Clear();_status.Text="搜索中…";
        try
        {
            var places=await _island.Weather.SearchAsync(query);if(_closed)return;
            foreach(var place in places)
            {
                var row=new Grid{Margin=new Thickness(0,4,0,4)};row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=new GridLength(58)});
                var label=new StackPanel();label.Children.Add(new TextBlock{Text=place.Name,TextTrimming=TextTrimming.CharacterEllipsis});
                label.Children.Add(new TextBlock{Text=place.Region,FontSize=11,Foreground=Brushes.DarkGray,Margin=new Thickness(0,3,0,0),TextTrimming=TextTrimming.CharacterEllipsis});row.Children.Add(label);
                var add=Button("添加",()=>
                {
                    if(_island.Settings.WeatherPlaces.Count>=5){_status.Text="最多保存 5 个地点";return;}
                    if(!_island.Settings.WeatherPlaces.Any(p=>p.Id==place.Id))_island.Settings.WeatherPlaces.Add(place);
                    _results.Children.Clear();Save();
                });Grid.SetColumn(add,1);row.Children.Add(add);_results.Children.Add(row);
            }
            _status.Text=places.Count==0?"未找到，请试试去掉市、区或输入拼音":"选择地点添加";
        }
        catch(Exception e){if(!_closed)_status.Text="搜索暂不可用，请稍后重试";Storage.Log("Weather search: "+e.GetType().Name);}
        finally{if(!_closed)_search.IsEnabled=_island.Settings.WeatherPlaces.Count<5;}
    }
}
