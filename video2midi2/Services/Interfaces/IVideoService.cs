using OpenCvSharp;

namespace Video2Midi2.Services.Interfaces
{
    public interface IVideoService
    {
        string FilePath { get; }
        int FrameCount { get; }
        int VideoWidth { get; }
        int VideoHeight { get; }
        double Fps { get; }
        Mat? CurrentFrame { get; }

        void Initialize(string filePath);
        Mat? GetFrame(int frameNumber);
        Mat? ReadNextFrame();
        void SeekToFrame(int frameNumber);
        int GetCurrentFrameIndex();
        (byte R, byte G, byte B) SamplePixel(Mat frame, int x, int y);
    }



    



}
