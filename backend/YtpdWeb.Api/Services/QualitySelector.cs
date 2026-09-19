using YoutubeExplode.Videos.Streams;

namespace YtpdWeb.Api.Services;

public static class QualitySelector
{
    public static VideoOnlyStreamInfo? PickVideoStream(StreamManifest manifest, string quality)
    {
        var candidates = manifest.GetVideoOnlyStreams()
            .Where(s => s.Container == Container.Mp4)
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

    public static AudioOnlyStreamInfo? PickAudioStream(StreamManifest manifest)
    {
        return manifest.GetAudioOnlyStreams()
            .OrderByDescending(s => s.Bitrate)
            .FirstOrDefault();
    }
}
