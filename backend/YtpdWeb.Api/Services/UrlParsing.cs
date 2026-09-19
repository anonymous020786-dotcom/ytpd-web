using System.Text.RegularExpressions;
using YoutubeExplode.Playlists;
using YoutubeExplode.Videos;

namespace YtpdWeb.Api.Services;

// Ported from the original desktop app's YoutubeHelpers.cs (parsing logic only,
// framework-agnostic) so URL handling behavior matches the WPF app exactly.
public static partial class UrlParsing
{
    public static bool TryParseVideoId(string url, out string videoId)
    {
        videoId = "";
        var result = VideoId.TryParse(url);
        if (result is null) return false;
        videoId = result.Value.Value;
        return true;
    }

    public static bool TryParsePlaylistId(string url, out string playlistId)
    {
        playlistId = "";
        if (string.IsNullOrWhiteSpace(url)) return false;

        foreach (var regex in new[] { RegularRegex(), CompositeRegex(), ShortLinkRegex(), EmbedRegex() })
        {
            var match = regex.Match(url).Groups[1].Value;
            if (!string.IsNullOrWhiteSpace(match) && PlaylistId.TryParse(match) is not null)
            {
                playlistId = match;
                return true;
            }
        }

        return false;
    }

    public static bool TryParseChannelId(string url, out string channelId)
    {
        channelId = "";
        var match = ChannelRegex().Match(url).Groups[1].Value;
        if (!string.IsNullOrWhiteSpace(match) && match.StartsWith("UC", StringComparison.Ordinal) && match.Length == 24)
        {
            channelId = match;
            return true;
        }
        return false;
    }

    public static bool TryParseHandle(string url, out string handle)
    {
        handle = "";
        var match = HandleRegex().Match(url).Groups[1].Value;
        if (string.IsNullOrWhiteSpace(match)) return false;
        handle = match.TrimEnd('/');
        return true;
    }

    public static bool TryParseUsername(string url, out string username)
    {
        username = "";
        var match = UserRegex().Match(url).Groups[1].Value;
        if (string.IsNullOrWhiteSpace(match) || match.Length > 20) return false;
        username = match;
        return true;
    }

    [GeneratedRegex(@"youtube\..+?/channel/(.*?)(?:\?|&|/|$)")]
    private static partial Regex ChannelRegex();

    [GeneratedRegex(@"youtube\..+?/playlist.*?list=(.*?)(?:&|/|$)")]
    private static partial Regex RegularRegex();

    [GeneratedRegex(@"youtube\..+?/watch.*?list=(.*?)(?:&|/|$)")]
    private static partial Regex CompositeRegex();

    [GeneratedRegex(@"youtu\.be/.*?/.*?list=(.*?)(?:&|/|$)")]
    private static partial Regex ShortLinkRegex();

    [GeneratedRegex(@"youtube\..+?/embed/.*?/.*?list=(.*?)(?:&|/|$)")]
    private static partial Regex EmbedRegex();

    [GeneratedRegex(@"youtube\..+?/user/(.*?)(?:\?|&|/|$)")]
    private static partial Regex UserRegex();

    [GeneratedRegex(@"youtube\..+?/@(.+)")]
    private static partial Regex HandleRegex();
}
