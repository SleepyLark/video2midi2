using OpenCvSharp;
using Video2Midi2.Models;

namespace Video2Midi2.Services.Interfaces
{
    public interface IMidiProcessingService
    {
        void UpdateKeyPositions(AppPreferences prefs, bool rebuild = false);
        bool IsBlackKey(int semitoneIndex);
        bool IsWhiteKey(int semitoneIndex);

        IReadOnlyList<KeyDetectionResult> DetectKeyPresses(
            Mat frame, AppPreferences prefs, IVideoService videoService, double sparksHeight = 1.0);

        Task<List<MidiNote>> ProcessVideoAsync(
            AppPreferences prefs,
            IVideoService videoService,
            IProgress<ProcessingProgress>? progress = null,
            CancellationToken cancellationToken = default);

        (double X, double Y) VRotate(double x, double y, double angleDegrees);
        double SnapToGrid(double value, int gridSize);
    }
}
