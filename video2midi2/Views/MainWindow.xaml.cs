using System.Windows;
using System.Windows.Input;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using Video2Midi2.ViewModels;

namespace Video2Midi2;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    private bool _isSeeking = false;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = _vm;

        // Trigger canvas redraw whenever the ViewModel signals a new frame
        _vm.RequestCanvasRedraw = () =>
            Dispatcher.InvokeAsync(
                () => MainCanvas.InvalidateVisual(),
                System.Windows.Threading.DispatcherPriority.Render);

        //KeyDown += (_, e) => _vm.OnKeyDown(e.Key, Keyboard.Modifiers);
    }

    private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Ignore programmatic updates 
        if (_isSeeking) return;

        // Check if the user is actually interacting with the mouse
        // This catches both drags and single clicks on the track
        if (Mouse.LeftButton != MouseButtonState.Pressed) return;

        _isSeeking = true;
        _vm.SeekToFrame((int)e.NewValue);
        _isSeeking = false;
    }

    private void MainCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Black);

        if (_vm.CurrentVideoFrame == null || _vm.CurrentVideoFrame.Empty())
            return;

        using var bitmap = MatToSkBitmap(_vm.CurrentVideoFrame);
        if (bitmap == null)
            return;

        // Figure out where to draw the bitmap so it fits the canvas
        // while preserving the video's aspect ratio (letterbox/pillarbox)
        var dest = CalculateAspectFitRect(
            bitmap.Width, bitmap.Height,
            e.Info.Width, e.Info.Height);

        canvas.DrawBitmap(bitmap, dest);
    }

    private static SKBitmap? MatToSkBitmap(OpenCvSharp.Mat mat)
    {
        // We need BGRA (4 channels) — convert from BGR if needed
        OpenCvSharp.Mat rgba = new();
        OpenCvSharp.Cv2.CvtColor(mat, rgba, OpenCvSharp.ColorConversionCodes.BGR2BGRA);

        var info = new SKImageInfo(rgba.Width, rgba.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);

        // Point SkiaSharp directly at OpenCV's pixel buffer — zero copy
        nint pixels = rgba.Data;
        bitmap.InstallPixels(info, pixels, info.RowBytes);

        // InstallPixels doesn't copy — we need to before rgba is disposed
        var copy = bitmap.Copy();

        rgba.Dispose();
        bitmap.Dispose();

        return copy;
    }

    private static SKRect CalculateAspectFitRect(
        int srcW, int srcH, int dstW, int dstH)
    {
        float srcAspect = (float)srcW / srcH;
        float dstAspect = (float)dstW / dstH;

        float drawW, drawH;

        if (srcAspect > dstAspect)
        {
            // Video is wider than canvas — fit to width
            drawW = dstW;
            drawH = dstW / srcAspect;
        }
        else
        {
            // Video is taller than canvas — fit to height
            drawH = dstH;
            drawW = dstH * srcAspect;
        }

        float offsetX = (dstW - drawW) / 2f;
        float offsetY = (dstH - drawH) / 2f;

        return new SKRect(offsetX, offsetY, offsetX + drawW, offsetY + drawH);
    }
}