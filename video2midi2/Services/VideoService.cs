using OpenCvSharp;
using Video2Midi2.Services.Interfaces;

namespace Video2Midi2.Services
{
    /// <summary>
    /// Wraps OpenCvSharp VideoCapture.
    /// Direct port of video_io.py / VideoHandler.
    /// </summary>
    public sealed class VideoService : IVideoService, IDisposable
    {
        private VideoCapture _capture;
        private Mat _currentFrame = new();
        private bool _hasFrame;

        //  Properties 

        public string FilePath { get; private set; } = string.Empty;
        public int FrameCount { get; private set; }
        public int VideoWidth { get; private set; }
        public int VideoHeight { get; private set; }
        public double Fps { get; private set; }

        /// <summary>The most recently read frame (BGR, same as OpenCV default).</summary>
        public Mat? CurrentFrame => _hasFrame ? _currentFrame : null;

        //  Initialisation 

        public void Initialize(string filePath)
        {
            FilePath = filePath;
            _capture?.Dispose();
            _capture = new VideoCapture(filePath);

            if (!_capture.IsOpened())
                throw new InvalidOperationException($"Cannot open video file: {filePath}");

            FrameCount = (int)_capture.Get(VideoCaptureProperties.FrameCount);
            VideoWidth = (int)_capture.Get(VideoCaptureProperties.FrameWidth);
            VideoHeight = (int)_capture.Get(VideoCaptureProperties.FrameHeight);
            Fps = _capture.Get(VideoCaptureProperties.Fps);

            _capture.Set(VideoCaptureProperties.BufferSize, 2);
            SeekToFrame(0);
        }

        //  Frame Access 

        /// <summary>
        /// Seek to a specific frame and return it.
        /// Equivalent to get_image() in Python.
        /// </summary>
        public Mat? GetFrame(int frameNumber)
        {
            _capture.Set(VideoCaptureProperties.PosFrames, frameNumber);
            _hasFrame = _capture.Read(_currentFrame);
            return _hasFrame ? _currentFrame : null;
        }

        /// <summary>
        /// Read the next frame sequentially (cheaper than a seek).
        /// Equivalent to read_next_frame() in Python.
        /// </summary>
        public Mat? ReadNextFrame()
        {
            _hasFrame = _capture.Read(_currentFrame);
            return _hasFrame ? _currentFrame : null;
        }

        /// <summary>Seek without returning the frame.</summary>
        public void SeekToFrame(int frameNumber) =>
            _capture.Set(VideoCaptureProperties.PosFrames, frameNumber);

        public int GetCurrentFrameIndex() =>
            (int)Math.Round(_capture.Get(VideoCaptureProperties.PosFrames));

        /// <summary>
        /// Sample the pixel color at (pixelX, pixelY) in the current frame.
        /// Returns (R, G, B) — converts from OpenCV's BGR storage.
        /// </summary>
        public (byte R, byte G, byte B) SamplePixel(Mat frame, int pixelX, int pixelY)
        {
            if (frame.Empty() || pixelX < 0 || pixelX >= frame.Width ||
                pixelY < 0 || pixelY >= frame.Height)
                return (0, 0, 0);

            // OpenCV stores as BGR
            Vec3b bgr = frame.At<Vec3b>(pixelY, pixelX);
            return (bgr.Item2, bgr.Item1, bgr.Item0);   // R, G, B
        }

        //  IDisposable 

        public void Dispose()
        {
            _currentFrame.Dispose();
            _capture?.Dispose();
        }
    }
}
