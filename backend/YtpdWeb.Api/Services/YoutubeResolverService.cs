using YoutubeExplode;
using YoutubeExplode.Common;
using YtpdWeb.Api.Models;

namespace YtpdWeb.Api.Services;

public class YoutubeResolverService
{
    // Keep responses bounded so a huge channel/playlist doesn't stall the request
    // or blow up the response payload. Users can re-resolve to page further later.
    private const int MaxItems = 300;

    private static readonly YoutubeClient Client = new();

    public async Task<ResolveResponseDto> ResolveAsync(string url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL is required.");

        if (UrlParsing.TryParsePlaylistId(url, out var playlistId))
            return await ResolvePlaylistAsync(playlistId, ct);

        if (UrlParsing.TryParseHandle(url, out var handle))
            return await ResolveChannelAsync(await Client.Channels.GetByHandleAsync("@" + handle, ct), ct);

        if (UrlParsing.TryParseChannelId(url, out var channelId))
            return await ResolveChannelAsync(await Client.Channels.GetAsync(channelId, ct), ct);

        if (UrlParsing.TryParseUsername(url, out var username))
            return await ResolveChannelAsync(await Client.Channels.GetByUserAsync(username, ct), ct);

        if (UrlParsing.TryParseVideoId(url, out var videoId))
        {
            var video = await Client.Videos.GetAsync(videoId, ct);
            return new ResolveResponseDto(
                "Video",
                video.Title,
                video.Author.ChannelTitle,
                video.Thumbnails.TryGetWithHighestResolution()?.Url ?? "",
                new List<ResolvedVideoDto>
                {
                    new(video.Id.Value, video.Title, video.Author.ChannelTitle,
                        video.Thumbnails.TryGetWithHighestResolution()?.Url ?? "",
                        video.Duration?.TotalSeconds),
                },
                false
            );
        }

        throw new ArgumentException("Could not recognize this as a YouTube video, playlist, or channel URL.");
    }

    private async Task<ResolveResponseDto> ResolvePlaylistAsync(string playlistId, CancellationToken ct)
    {
        var playlist = await Client.Playlists.GetAsync(playlistId, ct);
        var items = new List<ResolvedVideoDto>();
        var truncated = false;

        await foreach (var video in Client.Playlists.GetVideosAsync(playlistId, ct))
        {
            if (items.Count >= MaxItems)
            {
                truncated = true;
                break;
            }

            items.Add(new ResolvedVideoDto(
                video.Id.Value,
                video.Title,
                video.Author.ChannelTitle,
                video.Thumbnails.TryGetWithHighestResolution()?.Url ?? "",
                video.Duration?.TotalSeconds
            ));
        }

        return new ResolveResponseDto(
            "Playlist",
            playlist.Title,
            playlist.Author?.ChannelTitle,
            playlist.Thumbnails.TryGetWithHighestResolution()?.Url ?? "",
            items,
            truncated
        );
    }

    private async Task<ResolveResponseDto> ResolveChannelAsync(YoutubeExplode.Channels.Channel channel, CancellationToken ct)
    {
        var items = new List<ResolvedVideoDto>();
        var truncated = false;

        await foreach (var video in Client.Channels.GetUploadsAsync(channel.Id, ct))
        {
            if (items.Count >= MaxItems)
            {
                truncated = true;
                break;
            }

            items.Add(new ResolvedVideoDto(
                video.Id.Value,
                video.Title,
                video.Author.ChannelTitle,
                video.Thumbnails.TryGetWithHighestResolution()?.Url ?? "",
                video.Duration?.TotalSeconds
            ));
        }

        return new ResolveResponseDto(
            "Channel",
            channel.Title,
            channel.Title,
            channel.Thumbnails.TryGetWithHighestResolution()?.Url ?? "",
            items,
            truncated
        );
    }
}
