using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;

namespace DesktopIsland.Core;

public sealed record WeatherPlace(int Id,string Name,string Region,double Latitude,double Longitude,string TimeZone);
public sealed record WeatherReading(string Date,int Code,bool IsDay,double Minimum,double Maximum,DateTimeOffset FetchedAt);

public sealed class WeatherService : IDisposable
{
    private readonly IslandSettings _settings;
    private readonly HttpClient _http=new() { Timeout=TimeSpan.FromSeconds(12) };
    private readonly DispatcherTimer _timer=new() { Interval=TimeSpan.FromMinutes(15) };
    private readonly Dictionary<int,WeatherReading> _cache=Storage.Read("weather-cache.json",new Dictionary<int,WeatherReading>());
    private CancellationTokenSource? _request;
    private int _generation;
    public event Action? Changed;
    public WeatherService(IslandSettings settings)
    {
        _settings=settings;Normalize(settings);
        _timer.Tick+=async (_,_)=>await RefreshAsync();
    }
    public static void Normalize(IslandSettings settings)
    {
        settings.WeatherPlaces=(settings.WeatherPlaces??[]).Where(p=>p.Id>0&&double.IsFinite(p.Latitude)&&double.IsFinite(p.Longitude)&&Math.Abs(p.Latitude)<=90&&Math.Abs(p.Longitude)<=180)
            .DistinctBy(p=>p.Id).Take(5).ToList();
        if(!settings.WeatherPlaces.Any(p=>p.Id==settings.SelectedWeatherPlace))settings.SelectedWeatherPlace=settings.WeatherPlaces.FirstOrDefault()?.Id??0;
    }
    public WeatherPlace? Selected=>_settings.WeatherPlaces.FirstOrDefault(p=>p.Id==_settings.SelectedWeatherPlace);
    public static string LocalDate(WeatherPlace place,DateTimeOffset now)
    {
        try{return TimeZoneInfo.ConvertTime(now,TimeZoneInfo.FindSystemTimeZoneById(place.TimeZone)).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);}
        catch{return now.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);}
    }
    public static bool IsFresh(WeatherPlace place,WeatherReading reading,DateTimeOffset now)=>reading.Date==LocalDate(place,now)&&(now-reading.FetchedAt).TotalHours is >=0 and <2;
    public WeatherReading? Get(WeatherPlace place)=>_cache.TryGetValue(place.Id,out var reading)&&IsFresh(place,reading,DateTimeOffset.UtcNow)?reading:null;
    public async Task StartAsync(){_timer.Start();Changed?.Invoke();await RefreshAsync();}
    public async Task RefreshAsync()
    {
        _request?.Cancel();_request?.Dispose();_request=new();var token=_request.Token;var generation=++_generation;
        Normalize(_settings);var places=_settings.WeatherPlaces.ToArray();
        var results=await Task.WhenAll(places.Select(async place=>
        {
            try
            {
                string url=FormattableString.Invariant($"https://api.open-meteo.com/v1/forecast?latitude={place.Latitude}&longitude={place.Longitude}&current=weather_code,is_day&daily=temperature_2m_max,temperature_2m_min&timezone={Uri.EscapeDataString(place.TimeZone)}&forecast_days=1");
                var json=await _http.GetStringAsync(url,token);
                return (place.Id,Reading:Parse(json,DateTimeOffset.UtcNow));
            }
            catch(OperationCanceledException){return (place.Id,Reading:(WeatherReading?)null);}
            catch(Exception e){Storage.Log("Weather: "+e.GetType().Name);return (place.Id,Reading:(WeatherReading?)null);}
        }));
        if(generation!=_generation||token.IsCancellationRequested)return;
        foreach(var result in results)if(result.Reading!=null)_cache[result.Id]=result.Reading;
        foreach(var id in _cache.Keys.Except(places.Select(p=>p.Id)).ToArray())_cache.Remove(id);
        Storage.TryWrite("weather-cache.json",_cache);Changed?.Invoke();
    }
    public static WeatherReading? Parse(string json,DateTimeOffset now)
    {
        try
        {
            using var doc=JsonDocument.Parse(json);var root=doc.RootElement;var current=root.GetProperty("current");var daily=root.GetProperty("daily");
            var low=daily.GetProperty("temperature_2m_min")[0].GetDouble();var high=daily.GetProperty("temperature_2m_max")[0].GetDouble();
            if(!double.IsFinite(low)||!double.IsFinite(high)||low>high)return null;
            return new(daily.GetProperty("time")[0].GetString()!,current.GetProperty("weather_code").GetInt32(),current.GetProperty("is_day").GetInt32()==1,low,high,now);
        }
        catch{return null;}
    }
    public async Task<List<WeatherPlace>> SearchAsync(string query)
    {
        query=query.Trim();if(query.Length<2)return [];
        async Task<List<WeatherPlace>> Search(string name)
        {
            var json=await _http.GetStringAsync("https://geocoding-api.open-meteo.com/v1/search?name="+Uri.EscapeDataString(name)+"&count=8&language=zh&format=json");
            using var doc=JsonDocument.Parse(json);if(!doc.RootElement.TryGetProperty("results",out var rows))return [];
            return rows.EnumerateArray().Select(r=>new WeatherPlace(r.GetProperty("id").GetInt32(),r.GetProperty("name").GetString()!,
                string.Join(" · ",new[]{"admin1","admin2","country"}.Select(k=>r.TryGetProperty(k,out var value)?value.GetString():null).Where(s=>!string.IsNullOrEmpty(s))),
                r.GetProperty("latitude").GetDouble(),r.GetProperty("longitude").GetDouble(),r.TryGetProperty("timezone",out var tz)?tz.GetString()!:"UTC")).ToList();
        }
        var places=await Search(query);
        if(places.Count==0&&query.Length>2&&"市县区".Contains(query[^1]))places=await Search(query[..^1]);
        return places;
    }
    public static string Temperature(WeatherReading reading)=>$"{Math.Round(reading.Minimum):0}° / {Math.Round(reading.Maximum):0}°";
    public static string Condition(int code)=>code switch {0=>"晴",1 or 2=>"多云",3=>"阴",45 or 48=>"雾",51 or 53 or 55 or 56 or 57=>"毛毛雨",61 or 63 or 65 or 66 or 67 or 80 or 81 or 82=>"雨",71 or 73 or 75 or 77 or 85 or 86=>"雪",95 or 96 or 99=>"雷雨",_=>"暂无天气"};
    public void Dispose(){_timer.Stop();_request?.Cancel();_request?.Dispose();_http.Dispose();}
}
