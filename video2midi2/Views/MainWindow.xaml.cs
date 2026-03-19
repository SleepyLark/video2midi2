using System.Windows;
using System.Windows.Input;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using Video2Midi2.Models;
using Video2Midi2.Services;
using Video2Midi2.ViewModels;

namespace Video2Midi2;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    private bool _isSeeking = false;
    private SKRect _videoRect;

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
        MainCanvas.MouseDown += MainCanvas_MouseDown;
        MainCanvas.MouseMove += MainCanvas_MouseMove;
        MainCanvas.MouseUp += MainCanvas_MouseUp;
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

        // Draw video frame
        using var bitmap = MatToSkBitmap(_vm.CurrentVideoFrame);
        if (bitmap == null) return;

        _videoRect = CalculateAspectFitRect(
            bitmap.Width, bitmap.Height,
            e.Info.Width, e.Info.Height);

        canvas.DrawBitmap(bitmap, _videoRect);

        // Run color detection on the current frame and draw results
        var detectedKeys = _vm.MidiProc.DetectKeyPresses(
            _vm.CurrentVideoFrame,
            _vm.Prefs,
            _vm.Video);

        DrawKeyOverlays(canvas, detectedKeys);
    }

    private void DrawKeyOverlays(SKCanvas canvas, IReadOnlyList<KeyDetectionResult> detectedKeys)
    {
        var prefs = _vm.Prefs;
        if (prefs.KeyPositions.Count == 0) return;

        // Build a lookup so we don't iterate the full list for every key
        var stateMap = detectedKeys.ToDictionary(d => d.KeyIndex);

        using var fillPaint = new SKPaint { IsAntialias = true };
        using var outlinePaint = new SKPaint { IsAntialias = true, IsStroke = true, StrokeWidth = 1.5f };
        using var linePaint = new SKPaint { IsAntialias = true, IsStroke = true, StrokeWidth = 1f };
        using var selectedPaint = new SKPaint
        {
            IsAntialias = true,
            IsStroke = true,
            StrokeWidth = 2f,
            Color = SKColors.DodgerBlue
        };
        using var dotPaint = new SKPaint { IsAntialias = true, Color = SKColors.White };

        for (int i = 0; i < prefs.KeyPositions.Count; i++)
        {
            var pos = prefs.KeyPositions[i];
            var (absX, absY) = pos.ToVideoPixel(prefs.XOffsetWhiteKeys, prefs.YOffsetWhiteKeys);
            var screen = VideoToScreen(absX, absY);
            float sx = screen.X;
            float sy = screen.Y;

            bool isBlack = _vm.MidiProc.IsBlackKey(i);
            bool isSelected = i == _vm.SelectedKeyIndex;

            // Get detection result for this key
            stateMap.TryGetValue(i, out var detection);
            bool isPressed = detection?.State == KeyPressState.Pressed;
            bool isSustain = detection?.State == KeyPressState.Sustain;

            //  Vertical guide line 
            linePaint.Color = isBlack
                ? new SKColor(255, 255, 255, 80)
                : new SKColor(180, 180, 180, 60);
            canvas.DrawLine(sx, sy - 40, sx, sy + 40, linePaint);

            //  Box 
            float boxSize = isPressed ? 7f : 6f;   // slightly bigger when pressed
            float outlineSize = isPressed ? 9f : 7f;

            if (isPressed || isSustain)
            {
                // Color comes from the matched color entry
                SKColor pressColor = SKColors.White;
                if (detection!.ColorIndex >= 0 && detection.ColorIndex < prefs.Colors.Count)
                {
                    var entry = prefs.Colors[detection.ColorIndex];
                    // Use the sampled pixel color for the fill — matches what the video shows
                    pressColor = new SKColor(
                        detection.Color.R,
                        detection.Color.G,
                        detection.Color.B);
                }

                // Fill with the detected color
                fillPaint.Color = pressColor.WithAlpha(isSustain ? (byte)140 : (byte)220);
                canvas.DrawRect(sx - boxSize, sy - boxSize, boxSize * 2, boxSize * 2, fillPaint);

                // Outline — thicker for full press, thinner for sustain
                outlinePaint.Color = SKColors.Black.WithAlpha(200);
                outlinePaint.StrokeWidth = isSustain ? 1.5f : 2.5f;
                canvas.DrawRect(sx - outlineSize, sy - outlineSize,
                                outlineSize * 2, outlineSize * 2, outlinePaint);
            }
            else
            {
                // Unpressed — hollow box with subtle fill
                fillPaint.Color = new SKColor(80, 200, 255, 40);
                canvas.DrawRect(sx - 6, sy - 6, 12, 12, fillPaint);

                outlinePaint.Color = new SKColor(255, 255, 255, 140);
                outlinePaint.StrokeWidth = 1f;
                canvas.DrawRect(sx - 6, sy - 6, 12, 12, outlinePaint);
            }

            //  Selected key highlight 
            if (isSelected)
                canvas.DrawRect(sx - 9, sy - 9, 18, 18, selectedPaint);

            //  Octave root marker 
            if (prefs.Octave * 12 == i)
            {
                using var markerPaint = new SKPaint
                {
                    Color = SKColors.OrangeRed,
                    IsStroke = true,
                    StrokeWidth = 2f
                };
                canvas.DrawLine(sx - 6, sy + 9, sx + 6, sy + 9, markerPaint);
            }

            //  Center dot 
            canvas.DrawCircle(sx, sy, 1.5f, dotPaint);
        }
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

    private SKPoint VideoToScreen(double videoX, double videoY)
    {
        if (_vm.Video.VideoWidth == 0 || _vm.Video.VideoHeight == 0)
            return new SKPoint(0, 0);

        float scaleX = _videoRect.Width / _vm.Video.VideoWidth;
        float scaleY = _videoRect.Height / _vm.Video.VideoHeight;

        return new SKPoint(
            _videoRect.Left + (float)videoX * scaleX,
            _videoRect.Top + (float)videoY * scaleY);
    }

    private (double VideoX, double VideoY) ScreenToVideo(double screenX, double screenY)
    {
        if (_videoRect.Width == 0 || _videoRect.Height == 0)
            return (0, 0);

        float scaleX = _vm.Video.VideoWidth / _videoRect.Width;
        float scaleY = _vm.Video.VideoHeight / _videoRect.Height;

        return (
            (screenX - _videoRect.Left) * scaleX,
            (screenY - _videoRect.Top) * scaleY);
    }

    private double GetDpiScale()
    {
        var source = PresentationSource.FromVisual(MainCanvas);
        return source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
    }

    private void MainCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(MainCanvas);
        double dpi = GetDpiScale();
        var (vx, vy) = ScreenToVideo(pos.X * dpi, pos.Y * dpi);

        // If eyedropper is active, sample the pixel and apply it
        if (_vm.ColorMap.IsEyedropping)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                SampleAndApplyEyedropColor((int)vx, (int)vy);
                e.Handled = true;
            }
            return;
        }

        // Normal mouse handling
        bool ctrlHeld = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (e.ChangedButton == MouseButton.Left)
            _vm.OnLeftMouseDown(vx, vy, ctrlHeld);
        else if (e.ChangedButton == MouseButton.Right)
            _vm.OnRightMouseDown(vx, vy);

        MainCanvas.CaptureMouse();
        _vm.RequestCanvasRedraw?.Invoke();
    }

    private void SampleAndApplyEyedropColor(int videoX, int videoY)
    {
        if (_vm.CurrentVideoFrame == null || _vm.CurrentVideoFrame.Empty()) return;

        // Clamp to frame bounds
        int x = Math.Clamp(videoX, 0, _vm.Video.VideoWidth - 1);
        int y = Math.Clamp(videoY, 0, _vm.Video.VideoHeight - 1);

        var (r, g, b) = _vm.Video.SamplePixel(_vm.CurrentVideoFrame, x, y);
        _vm.ColorMap.ApplyEyedropColor(new RgbColor(r, g, b));
    }

    private void CancelEyedrop_Click(object sender, RoutedEventArgs e)
    {
        _vm.ColorMap.CancelEyedrop();
    }

    // Add to MainCanvas_MouseMove
    private void MainCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(MainCanvas);
        double dpi = GetDpiScale();
        var (vx, vy) = ScreenToVideo(pos.X * dpi, pos.Y * dpi);

        // Live preview while eyedropping — no click needed
        if (_vm.ColorMap.IsEyedropping)
        {
            SampleLivePreview((int)vx, (int)vy);
            return;  // don't process drag while eyedropping
        }

        if (e.LeftButton != MouseButtonState.Pressed &&
            e.RightButton != MouseButtonState.Pressed)
            return;

        _vm.OnMouseMove(vx, vy);
        _vm.RequestCanvasRedraw?.Invoke();
    }

    private void SampleLivePreview(int videoX, int videoY)
    {
        if (_vm.CurrentVideoFrame == null || _vm.CurrentVideoFrame.Empty()) return;

        int x = Math.Clamp(videoX, 0, _vm.Video.VideoWidth - 1);
        int y = Math.Clamp(videoY, 0, _vm.Video.VideoHeight - 1);

        var (r, g, b) = _vm.Video.SamplePixel(_vm.CurrentVideoFrame, x, y);
        _vm.ColorMap.UpdateLivePreview(new RgbColor(r, g, b));
    }

    private void MainCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _vm.OnMouseUp();
        MainCanvas.ReleaseMouseCapture();
        _vm.RequestCanvasRedraw?.Invoke();
    }
}