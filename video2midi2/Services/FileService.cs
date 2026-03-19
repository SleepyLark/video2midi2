using Microsoft.Win32;
using System.IO;
using Video2Midi2.Services.Interfaces;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace Video2Midi2.Services
{
    public sealed class FileService : IFileService
    {
        //  File Dialog 

        public string? OpenVideoFileDialog()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select a video file",
                Filter = "Video Files|*.mp4;*.mkv;*.avi;*.webm;*.mpg|All Files|*.*"
            };

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        //  YouTube Download 

        public async Task<string?> DownloadVideoAsync(
            string url,
            IProgress<double>? progress = null,
            CancellationToken token = default)
        {
            try
            {
                var youtube = new YoutubeClient();

                // Fetch video metadata
                var video = await youtube.Videos.GetAsync(url, token);

                // Get all available streams
                var manifest = await youtube.Videos.Streams.GetManifestAsync(url, token);

                // Prefer muxed (video+audio in one file) streams, highest resolution first
                var streamInfo = manifest
                    .GetMuxedStreams()
                    .OrderByDescending(s => s.VideoResolution.Area)
                    .FirstOrDefault();

                // Fall back to video-only if no muxed stream exists
                streamInfo = (MuxedStreamInfo?)(manifest
                        .GetVideoStreams()
                        .OrderByDescending(static s => s.VideoResolution.Area)
                        .FirstOrDefault() as IStreamInfo);

                if (streamInfo is null)
                    return null;

                // Build a safe filename from the video title
                string safeTitle = string.Concat(
                    video.Title.Split(Path.GetInvalidFileNameChars())
                );
                string fileName = $"{safeTitle}.{streamInfo.Container.Name}";

                // Download with progress reporting
                await youtube.Videos.Streams.DownloadAsync(
                    streamInfo,
                    fileName,
                    progress,
                    token
                );

                return File.Exists(fileName) ? fileName : null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Download failed: {ex.Message}");
                return null;
            }
        }

        public string GetIniFilePath()
        {
            if (File.Exists("v2m.ini"))
                return "v2m.ini";

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".v2m.ini"
            );
        }
    }
}