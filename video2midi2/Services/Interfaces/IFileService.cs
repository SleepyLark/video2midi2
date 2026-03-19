namespace Video2Midi2.Services.Interfaces
{
    public interface IFileService
    {
        /// <summary>Opens the native file picker and returns the selected path, or null.</summary>
        string? OpenVideoFileDialog();

        /// <summary>Optionally download a video from a URL (YoutubeExplode).</summary>
        Task<string?> DownloadVideoAsync(string url, IProgress<double>? progress = null,
            CancellationToken token = default);

        /// <summary>
        /// Returns the path to the settings INI file.
        /// Prefers a local v2m.ini, falls back to ~/.v2m.ini
        /// Mirrors get_ini_filepath() from the Python cli.py
        /// </summary>
        string GetIniFilePath();
    }
}
