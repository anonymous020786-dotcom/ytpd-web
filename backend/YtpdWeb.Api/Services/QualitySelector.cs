using YoutubeExplode.Videos.Streams;

namespace YtpdWeb.Api.Services;

public static class QualitySelector
{
    public static VideoOnlyStreamInfo? PickVideoStream(StreamManifest manifest, string quality, Container preferredContainer)
    {
        var candidates = manifest.GetVideoOnlyStreams()
            .Where(s => s.Container == preferredContainer)
            .OrderByDescending(s => s.VideoQuality)
            .ToList();

        if (candidates.Count == 0)
            candidates = manifest.GetVideoOnlyStreams().OrderByDescending(s => s.VideoQuality).ToList();

        if (candidates.Count == 0) return null;

        if (string.Equals(quality, "best", StringComparison.OrdinalIgnoreCase))
            return candidates.First();

        // quality like "1080p" - pick the closest available at or below the request
        if (int.TryParse(quality.TrimEnd('p'), out var requestedHeight))
        {
            var match = candidates
                .Where(s => s.VideoQuality.MaxHeight <= requestedHeight)
                .OrderByDescending(s => s.VideoQuality)
                .FirstOrDefault();
            return match ?? candidates.Last();
        }

        return candidates.First();
    }

    // preferredContainer matters when the result gets ffmpeg `-c copy` muxed
    // alongside a video stream of that same container (e.g. WebM+Opus) -
    // mismatched containers can't be copy-muxed together.
    public static AudioOnlyStreamInfo? PickAudioStream(StreamManifest manifest, Container? preferredContainer = null)
    {
        var streams = manifest.GetAudioOnlyStreams();

        if (preferredContainer is { } container)
        {
            var matched = streams.Where(s => s.Container == container).OrderByDescending(s => s.Bitrate).FirstOrDefault();
            if (matched is not null) return matched;
        }

        return streams.OrderByDescending(s => s.Bitrate).FirstOrDefault();
    }
}
