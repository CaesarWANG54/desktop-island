using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DesktopIsland.Core;

public sealed record LyricLine(double Time, string Text);
public static class Lrc
{
    private static readonly Regex Stamp = new(@"\[(\d{1,3}):(\d{2})(?:[\.:](\d{1,3}))?\]", RegexOptions.Compiled);
    public static List<LyricLine> Parse(string text)
    {
        var offsetMatch = Regex.Match(text, @"\[offset:([+-]?\d+)\]", RegexOptions.IgnoreCase);
        double offset = offsetMatch.Success && double.TryParse(offsetMatch.Groups[1].Value, out var o) ? o / 1000 : 0;
        var lines = new List<LyricLine>();
        foreach (var raw in text.Split('\n'))
        {
            var matches = Stamp.Matches(raw);
            if (matches.Count == 0) continue;
            var content = Stamp.Replace(raw, "").Trim();
            if (string.IsNullOrWhiteSpace(content)) continue;
            foreach (Match m in matches)
            {
                var fraction = m.Groups[3].Success ? double.Parse("0." + m.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
                lines.Add(new LyricLine(Math.Max(0, int.Parse(m.Groups[1].Value) * 60 + int.Parse(m.Groups[2].Value) + fraction + offset), content));
            }
        }
        return lines.OrderBy(l => l.Time).DistinctBy(l => l.Time).ToList();
    }
    public static (string Current, string Next) At(IReadOnlyList<LyricLine> lines, double seconds)
    {
        if (lines.Count == 0) return ("", "");
        int lo = 0, hi = lines.Count - 1, found = -1;
        while (lo <= hi) { int mid = (lo + hi) / 2; if (lines[mid].Time <= seconds) { found = mid; lo = mid + 1; } else hi = mid - 1; }
        return (found >= 0 ? lines[found].Text : "♪", found + 1 < lines.Count ? lines[found + 1].Text : "");
    }
}

public sealed class LyricsService : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private CancellationTokenSource? _request;
    private string _key = "";
    public IReadOnlyList<LyricLine> Lines { get; private set; } = [];
    public string Status { get; private set; } = "";
    public string Source { get; private set; } = "";
    public static string Key(string title, string artist) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(title + "\n" + artist)));
    public static string CacheKey(MediaInfo media)=>Key(QqMusicLyrics.IsQq(media)?$"QQMusic\n{media.Title}\n{Math.Round(media.Dur)}\n{media.Album}":media.Title,media.Artist);
    public static string Normalize(string s) => Regex.Replace(s.Normalize(NormalizationForm.FormKC).ToLowerInvariant(), @"[\s\p{P}\p{S}]", "");
    public LyricsService()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("DesktopIsland/1.0 (Windows desktop lyrics)");
        _http.DefaultRequestHeaders.Referrer = new Uri("https://music.163.com/");
    }
    public async Task UpdateAsync(MediaInfo media, bool online, bool force = false)
    {
        var key = CacheKey(media);
        if (!force && _key == key) return;
        _key = key; _request?.Cancel(); _request?.Dispose(); _request = new();
        var token = _request.Token;
        Lines = []; Source = ""; Status = "正在找歌词";
        var dir = Path.Combine(Storage.Root, "lyrics");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, key + ".lrc");
        try
        {
            if (File.Exists(path))
            {
                var cached = Lrc.Parse(await File.ReadAllTextAsync(path, token));
                if (_key == key && cached.Count > 0) { Lines = cached; Source = "本地歌词"; Status = ""; return; }
            }
            if (!online) { Status = "可导入本地 LRC"; return; }
            string? lrc = null;
            var source = "QQ 音乐";
            if(QqMusicLyrics.IsQq(media))
                try { lrc=await QqMusicLyrics.FetchAsync(_http,media,token); } catch(Exception e) when(e is not OperationCanceledException){Storage.Log("QQ lyrics: "+e.GetType().Name);}
            if(string.IsNullOrWhiteSpace(lrc))
            {
                source="网易云音乐";
                try { lrc = await NeteaseAsync(media, token); } catch (Exception e) when (e is not OperationCanceledException) { Storage.Log("NetEase lyrics: " + e.GetType().Name); }
            }
            if (string.IsNullOrWhiteSpace(lrc))
            {
                source = "LRCLIB";
                try { lrc = await LrclibAsync(media, token); } catch (Exception e) when (e is not OperationCanceledException) { Storage.Log("LRCLIB: " + e.GetType().Name); }
            }
            token.ThrowIfCancellationRequested();
            if (_key != key) return;
            var parsed = Lrc.Parse(lrc ?? "");
            Lines = parsed; Source = parsed.Count > 0 ? source : ""; Status = parsed.Count > 0 ? "" : "暂无同步歌词";
            if (parsed.Count > 0) await File.WriteAllTextAsync(path, lrc, token);
        }
        catch (OperationCanceledException) { if (_key == key && !token.IsCancellationRequested) Status = "歌词连接超时"; }
        catch (Exception e) { if (_key == key) Status = "歌词暂不可用"; Storage.Log("Lyrics: " + e.GetType().Name); }
    }
    private async Task<string?> NeteaseAsync(MediaInfo media, CancellationToken ct)
    {
        if(long.TryParse(media.SongId,out var exactId)&&exactId>0)
            return await FetchNeteaseLyrics(exactId,ct);
        var title=QqMusicLyrics.IsQq(media)?QqMusicLyrics.BaseTitle(media.Title):media.Title;
        var query = Uri.EscapeDataString(title + " " + media.Artist);
        using var response = await _http.GetAsync("https://music.163.com/api/search/get/web?s=" + query + "&type=1&offset=0&limit=8", ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("result", out var result) || !result.TryGetProperty("songs", out var songs)) return null;
        foreach (var song in songs.EnumerateArray())
        {
            if (Normalize(song.GetProperty("name").GetString() ?? "") != Normalize(title)) continue;
            if (!song.TryGetProperty("artists", out var artists)) continue;
            var artistMatch = artists.EnumerateArray().Any(a =>
            {
                var artist = Normalize(a.GetProperty("name").GetString() ?? "");
                return artist.Length > 0 && Normalize(media.Artist).Contains(artist);
            });
            if (!artistMatch) continue;
            if (media.Dur > 0 && song.TryGetProperty("duration", out var d) && Math.Abs(d.GetDouble() / 1000 - media.Dur) > 5) continue;
            var id = song.GetProperty("id").GetInt64();
            return await FetchNeteaseLyrics(id,ct);
        }
        return null;
    }
    private async Task<string?> FetchNeteaseLyrics(long id,CancellationToken ct)
    {
        using var lyrics = await _http.GetAsync($"https://music.163.com/api/song/lyric?os=pc&id={id}&lv=-1&tv=-1", ct);
        lyrics.EnsureSuccessStatusCode();
        using var data = JsonDocument.Parse(await lyrics.Content.ReadAsStringAsync(ct));
        return data.RootElement.TryGetProperty("lrc", out var l) && l.TryGetProperty("lyric", out var txt) ? txt.GetString() : null;
    }
    private async Task<string?> LrclibAsync(MediaInfo media, CancellationToken ct)
    {
        var uri = "https://lrclib.net/api/get?track_name=" + Uri.EscapeDataString(media.Title) + "&artist_name=" + Uri.EscapeDataString(media.Artist);
        if (media.Dur > 0) uri += "&duration=" + Math.Round(media.Dur).ToString(CultureInfo.InvariantCulture);
        using var response = await _http.GetAsync(uri, ct);
        if (!response.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return doc.RootElement.TryGetProperty("syncedLyrics", out var lyrics) ? lyrics.GetString() : null;
    }
    public async Task ImportAsync(string filename, MediaInfo media)
    {
        var raw = await File.ReadAllTextAsync(filename);
        if (Lrc.Parse(raw).Count == 0) throw new InvalidDataException("没有找到带时间的 LRC 歌词");
        var path = Path.Combine(Storage.Root, "lyrics", CacheKey(media) + ".lrc");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllTextAsync(path, raw);
        await UpdateAsync(media, false, true);
    }
    public void Dispose() { _request?.Cancel(); _request?.Dispose(); _http.Dispose(); }
}
