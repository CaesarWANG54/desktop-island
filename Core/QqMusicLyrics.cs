using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DesktopIsland.Core;

// QQ's public SmartBox + song-detail + plain-LRC interfaces. Protocol reference:
// GotKiCry/QQMusicMonitor (MIT); no account cookies or encrypted QRC are needed.
public static class QqMusicLyrics
{
    public static bool IsQq(MediaInfo media)=>media.Aumid.Contains("QQMusic",StringComparison.OrdinalIgnoreCase);
    public static string BaseTitle(string title)
    {
        var suffix=Regex.Match(title,@"^(.*?)\s*[（(]([^()（）]+)[）)]\s*$");
        if(!suffix.Success)return title.Trim();
        var main=suffix.Groups[1].Value;var note=suffix.Groups[2].Value;
        if(Regex.IsMatch(main,@"[A-Za-z\u3040-\u30ff\uac00-\ud7af]")&&Regex.IsMatch(note,@"[\u4e00-\u9fff]")&&!Regex.IsMatch(note,@"live|remix|demo|acoustic|version|伴奏|翻唱|现场|纯音乐|混音|版|录音|重制|加速|慢速|合唱",RegexOptions.IgnoreCase))return main.Trim();
        return title.Trim();
    }
    private static string BaseArtist(string artist)
    {
        var alias=Regex.Match(artist,@"^([A-Za-z0-9 ._'!+-]+?)\s*[（(]([\u3040-\u30ff\u4e00-\u9fff\uac00-\ud7af\s·]+)[）)]\s*$");
        return alias.Success?alias.Groups[1].Value.Trim():artist.Trim();
    }
    public static string ArtistQuery(string artist)=>string.Join("/",artist.Split(['/', '&',';',',','，','、'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Select(BaseArtist));
    public static string ArtistKey(string artist)=>string.Join("/",ArtistQuery(artist).Split('/').Select(LyricsService.Normalize).Order(StringComparer.Ordinal));
    public static bool Matches(MediaInfo media,string title,string artist,double seconds,string album)
    {
        bool sameAlbum=!string.IsNullOrWhiteSpace(media.Album)&&LyricsService.Normalize(media.Album)==LyricsService.Normalize(album);
        // QQ reports a 60-second timeline for some previews of a full recording.
        // Only relax duration when the album, title and complete singer list agree.
        bool durationMatches=media.Dur<=0||Math.Abs(media.Dur-seconds)<=3||(media.Dur is >=59 and <=61&&seconds>media.Dur&&sameAlbum);
        return LyricsService.Normalize(BaseTitle(media.Title))==LyricsService.Normalize(BaseTitle(title))
            &&ArtistKey(media.Artist).Length>0&&ArtistKey(media.Artist)==ArtistKey(artist)
            &&durationMatches&&(string.IsNullOrWhiteSpace(media.Album)||sameAlbum);
    }
    private static async Task<JsonDocument> JsonAsync(HttpClient http,string url,object? payload,CancellationToken token)
    {
        using var request=new HttpRequestMessage(payload==null?HttpMethod.Get:HttpMethod.Post,url);
        request.Headers.Referrer=new Uri("https://y.qq.com/");
        if(payload!=null)request.Content=new StringContent(JsonSerializer.Serialize(payload),Encoding.UTF8,"application/json");
        using var response=await http.SendAsync(request,token);response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
    }
    public static async Task<string?> FetchAsync(HttpClient http,MediaInfo media,CancellationToken token)
    {
        var checkedMids=new HashSet<string>(StringComparer.Ordinal);
        var baseTitle=BaseTitle(media.Title);
        foreach(var query in new[]{baseTitle+" "+ArtistQuery(media.Artist),baseTitle}.Distinct())
        {
            using var search=await JsonAsync(http,"https://c.y.qq.com/splcloud/fcgi-bin/smartbox_new.fcg?format=json&key="+Uri.EscapeDataString(query),null,token);
            if(!search.RootElement.TryGetProperty("data",out var data)||!data.TryGetProperty("song",out var song)||!song.TryGetProperty("itemlist",out var hits))continue;
            foreach(var hit in hits.EnumerateArray())
            {
                string name=hit.GetProperty("name").GetString()??"",singer=hit.GetProperty("singer").GetString()??"",mid=hit.GetProperty("mid").GetString()??"";
                if(LyricsService.Normalize(BaseTitle(name))!=LyricsService.Normalize(baseTitle)||ArtistKey(singer)!=ArtistKey(media.Artist)||!checkedMids.Add(mid))continue;
                var lyrics=await FetchCandidateAsync(http,media,mid,token);
                if(lyrics!=null)return lyrics;
            }
        }
        // SmartBox has a short result list. Album-bearing search resolves editions
        // outside that list, while the same identity checks still apply.
        using var legacy=await JsonAsync(http,"https://c.y.qq.com/soso/fcgi-bin/client_search_cp?format=json&p=1&n=10&w="+Uri.EscapeDataString(baseTitle+" "+ArtistQuery(media.Artist)),null,token);
        if(legacy.RootElement.TryGetProperty("data",out var legacyData)&&legacyData.TryGetProperty("song",out var legacySong)&&legacySong.TryGetProperty("list",out var list))
            foreach(var hit in list.EnumerateArray())
            {
                var mid=hit.GetProperty("songmid").GetString()??"";
                var singers=string.Join("/",hit.GetProperty("singer").EnumerateArray().Select(s=>s.GetProperty("name").GetString()));
                if(!Matches(media,hit.GetProperty("songname").GetString()??"",singers,hit.GetProperty("interval").GetDouble(),hit.GetProperty("albumname").GetString()??"")||!checkedMids.Add(mid))continue;
                var lyrics=await FetchCandidateAsync(http,media,mid,token);
                if(lyrics!=null)return lyrics;
            }
        return null;
    }
    private static async Task<string?> FetchCandidateAsync(HttpClient http,MediaInfo media,string mid,CancellationToken token)
    {
        if(!Regex.IsMatch(mid,@"^[A-Za-z0-9]{8,32}$"))return null;
        var detailBody=new {comm=new {ct=24,cv=4747474,format="json"},req_1=new {module="music.pf_song_detail_svr",method="get_song_detail_yqq",param=new {song_mid=mid,song_type=0,song_id=0}}};
        using var detail=await JsonAsync(http,"https://u.y.qq.com/cgi-bin/musicu.fcg",detailBody,token);
        if(!detail.RootElement.TryGetProperty("req_1",out var request)||!request.TryGetProperty("data",out var data)||!data.TryGetProperty("track_info",out var track))return null;
        string title=track.GetProperty("title").GetString()??"";
        string artists=string.Join("/",track.GetProperty("singer").EnumerateArray().Select(s=>s.GetProperty("name").GetString()));
        if(!Matches(media,title,artists,track.GetProperty("interval").GetDouble(),track.GetProperty("album").GetProperty("name").GetString()??""))return null;
        var lyricBody=new {comm=new {ct=24,cv=4747474,format="json",uin=0},req_1=new {module="music.musichallSong.PlayLyricInfo",method="GetPlayLyricInfo",param=new {songMID=mid,songID=0,qrc=0,trans=0,roma=0}}};
        using var response=await JsonAsync(http,"https://u.y.qq.com/cgi-bin/musicu.fcg",lyricBody,token);
        if(!response.RootElement.TryGetProperty("req_1",out var lyricRequest)||!lyricRequest.TryGetProperty("data",out var lyricData)||!lyricData.TryGetProperty("lyric",out var lyric))return null;
        return Decode(lyric.GetString()??"");
    }
    public static string? Decode(string content)
    {
        try
        {
            string text=content.TrimStart().StartsWith('[')?content:Encoding.UTF8.GetString(Convert.FromBase64String(content));
            text=WebUtility.HtmlDecode(text);
            return Lrc.Parse(text).Count>0?text:null;
        }
        catch{return null;}
    }
}
