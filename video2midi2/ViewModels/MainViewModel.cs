using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Video2Midi2.Models;
using Video2Midi2.Services;
using Video2Midi2.Services.Interfaces;
using Video2Midi2.Views;

namespace Video2Midi2.ViewModels
{
    /// <summary>
    /// Primary ViewModel — ports controller.py / AppController.
    ///
    /// Key differences from the Python version:
    ///  • Commands (IRelayCommand) replace callback functions.
    ///  • AppPreferences is injected, not a global static.
    ///  • No direct pygame references — keyboard/mouse events arrive as
    ///    calls from the View's code-behind.
    ///  • Processing runs async so the UI never freezes.
    /// </summary>
    public partial class MainViewModel : ObservableObject
    {
        //  Dependencies 

        private readonly IMidiProcessingService _midiProc;
        private readonly IMidiExportService _midiExport;
        private readonly ISettingsService _settings;
        private readonly IVideoService _video;
        private readonly IFileService _fileService;

        //  Observable State 

        public AppPreferences Prefs { get; }
		public IVideoService Video => _video;
		public IMidiProcessingService MidiProc => _midiProc;

		public Action? RequestCanvasRedraw { get; set; }

        [ObservableProperty] private string _windowTitle = "Video2Midi";
        [ObservableProperty] private bool _isProcessing = false;
        [ObservableProperty] private double _processingProgress = 0;
        [ObservableProperty] private string _statusMessage = string.Empty;

        // Key drag state — replaces three separate fields from Python
        [ObservableProperty] private KeyDragState _dragState = new(DragMode.Idle);

        // Last clicked key (for per-key UI operations)
        [ObservableProperty] private int _selectedKeyIndex = -1;

        // Key used for channel split (K_p in Python)
        [ObservableProperty] private int _separateNoteKeyIndex = -1;

        // Snap to grid
        [ObservableProperty] private bool _snapToGrid = false;
        [ObservableProperty] private int _gridSize = 32;

        // Line height for visual overlays
        [ObservableProperty] private double _lineHeight = 20;

        // Floating panel visibility — bound to ToggleButton.IsChecked in the toggle bar
        [ObservableProperty] private bool _isDetectionPanelVisible  = false;
        [ObservableProperty] private bool _isKeyLayoutPanelVisible  = false;
        [ObservableProperty] private bool _isSparksPanelVisible     = false;
        [ObservableProperty] private bool _isExtraPanelVisible      = false;

        // Sparks panel bindings
        [ObservableProperty] private double _sparksHeight = 1;
        [ObservableProperty] private double _sparksDelta  = 50;

        // Extra panel bindings
        [ObservableProperty] private double _alternateSensitivityOffset = 0;
        [ObservableProperty] private double _selectedColorDelta         = 50;

        [ObservableProperty] private string _videoResolution = string.Empty;
        [ObservableProperty] private double _videoFps;
        [ObservableProperty] private string _frameRangeDisplay = "F: 0 — 0";

        [ObservableProperty] private int _seekMax = 1;
        [ObservableProperty] private string _frameCounterDisplay = "0 / 0";

        //  Frame Binding 

        [ObservableProperty] private OpenCvSharp.Mat? _currentVideoFrame;
        [ObservableProperty] private int _currentFrameIndex;


        private CancellationTokenSource? _processingCts;
        private HelpWindow _helpWindow = null;


        public ColorMapViewModel ColorMap { get; }

        //  Constructor 
        public MainViewModel(
            AppPreferences prefs,
            IMidiProcessingService midiProc,
            IMidiExportService midiExport,
            ISettingsService settings,
            IVideoService video,
            IFileService fileService)
        {
            Prefs = prefs;
            _midiProc = midiProc;
            _midiExport = midiExport;
            _settings = settings;
            _video = video;
            _fileService = fileService;
            ColorMap = new ColorMapViewModel(prefs);
        }

        //  Initialisation 

        public void Initialize(string videoPath)
        {
            _video.Initialize(videoPath);

            VideoResolution = $"{_video.VideoWidth} × {_video.VideoHeight}";
            VideoFps = _video.Fps;
            FrameRangeDisplay = $"F: {Prefs.StartFrame} — {Prefs.EndFrame}";

            WindowTitle = Path.GetFileName(videoPath);

            string iniPath = _settings.GetIniFilePath();
            LoadSettingsFromFile(iniPath);

            if (Prefs.EndFrame <= Prefs.StartFrame)
                Prefs.EndFrame = _video.FrameCount;

            _midiProc.UpdateKeyPositions(Prefs, rebuild: true);

        }

        //  File Commands 

        [RelayCommand]
        public void OpenFile()
        {
            string? path = _fileService.OpenVideoFileDialog();
            if (path is null) return;

            LoadVideo(path);
        }

        [RelayCommand]
        public async Task OpenUrl()
        {
            // For now open a simple input dialog — swap for a proper dialog later
            string? url = await PromptForUrlAsync();
            if (url is null) return;

            StatusMessage = "Downloading...";
            var progress = new Progress<double>(p =>
                StatusMessage = $"Downloading... {p * 100:F0}%");

            string? path = await _fileService.DownloadVideoAsync(url, progress);

            if (path is null)
            {
                StatusMessage = "Download failed.";
                return;
            }

            LoadVideo(path);
        }

        [RelayCommand]
        public void Exit() => System.Windows.Application.Current.Shutdown();

        [RelayCommand]
        public void ShowHelpWindow()
        {
            // Check if the window is null or has been closed
            if (_helpWindow == null || !_helpWindow.IsLoaded)
            {
                _helpWindow = new HelpWindow();
                _helpWindow.Owner = App.Current.MainWindow; // Keeps it on top of the main window
                _helpWindow.Show(); // Or ShowDialog() if you want to block the main window
            }
            else
            {
                // If it's already open, just bring it to the front and focus it
                _helpWindow.Activate();
                _helpWindow.Focus();
            }
        }

        //  Private Helpers 

        private void LoadVideo(string path)
        {
            try
            {
                _video.Initialize(path);
                WindowTitle = System.IO.Path.GetFileName(path);
                    
                SeekMax = _video.FrameCount - 1;
                FrameCounterDisplay = $"0 / {_video.FrameCount}";



                // Load .ini sidecar if one exists next to the video
                string sidecar = path + ".ini";
                string globalIni = _fileService.GetIniFilePath();
                _settings.Load(Prefs, File.Exists(sidecar) ? sidecar : globalIni);

                if (Prefs.EndFrame <= Prefs.StartFrame)
                    Prefs.EndFrame = _video.FrameCount;

                CurrentFrameIndex = Prefs.StartFrame;

                _midiProc.UpdateKeyPositions(Prefs, rebuild: true);

                // Update status bar bindings
                VideoResolution = $"{_video.VideoWidth} × {_video.VideoHeight}";
                VideoFps = _video.Fps;
                FrameRangeDisplay = $"S: {Prefs.StartFrame} - E: {Prefs.EndFrame}";

                // Load and display the first frame
                CurrentVideoFrame = _video.GetFrame(Prefs.StartFrame);
                StatusMessage = "Ready";
                RequestCanvasRedraw?.Invoke();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to load video: {ex.Message}";
            }
        }

        private static Task<string?> PromptForUrlAsync()
        {
            // Simple WPF input dialog — good enough for now
            var dialog = new Window
            {
                Title = "Open from URL",
                Width = 480,
                Height = 130,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                Background = new System.Windows.Media.SolidColorBrush(
                                   System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x2D))
            };

            var stack = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            var label = new System.Windows.Controls.TextBlock
            {
                Text = "Enter a YouTube URL:",
                Foreground = System.Windows.Media.Brushes.LightGray,
                Margin = new Thickness(0, 0, 0, 8)
            };
            var input = new System.Windows.Controls.TextBox
            {
                Height = 28,
                Background = System.Windows.Media.Brushes.DimGray,
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0)
            };
            var btn = new System.Windows.Controls.Button
            {
                Content = "OK",
                Width = 80,
                Margin = new Thickness(0, 10, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right
            };

            string? result = null;
            btn.Click += (_, _) => { result = input.Text; dialog.Close(); };

            stack.Children.Add(label);
            stack.Children.Add(input);
            stack.Children.Add(btn);
            dialog.Content = stack;
            dialog.ShowDialog();

            return Task.FromResult(string.IsNullOrWhiteSpace(result) ? null : result);
        }




        //  Video Navigation Commands 

        [RelayCommand]
        public void ScrollToStart() => SeekAndUpdate(0);

        [RelayCommand]
        public void ScrollToEnd() => SeekAndUpdate(Math.Max(0, _video.FrameCount));

        [RelayCommand]
        public void ScrollForwardOneFrame() => SeekAndUpdate(_currentFrameIndex + 1);

        [RelayCommand]
        public void ScrollBackOneFrame() => SeekAndUpdate(_currentFrameIndex - 1);

        [RelayCommand]
        public void ScrollFastForward() => SeekAndUpdate(_currentFrameIndex + 100);

        [RelayCommand]
        public void ScrollFastBack() => SeekAndUpdate(_currentFrameIndex - 100);


        // Make SeekAndUpdate internal and add a public entry point for the slider
        public void SeekToFrame(int frame)
        {
            SeekAndUpdate(frame);
        }

        private void SeekAndUpdate(int targetFrame)
        {
            int clamped = Math.Clamp(targetFrame, 0, _video.FrameCount-1);
            CurrentVideoFrame = _video.GetFrame(clamped);
            CurrentFrameIndex = clamped;
            FrameCounterDisplay = $"{clamped} / {_video.FrameCount}";
            RequestCanvasRedraw?.Invoke();
        }

        //  Key Layout Commands 

        [RelayCommand]
        public void RaiseOctave()
        {
            Prefs.Octave = Math.Min(Prefs.Octave + 1, 7);
        }

        [RelayCommand]
        public void LowerOctave()
        {
            Prefs.Octave = Math.Max(Prefs.Octave - 1, 0);
        }

        [RelayCommand]
        public void RotateClockwise()
        {
            Prefs.KeysAngle -= 5;
            _midiProc.UpdateKeyPositions(Prefs);
        }

        [RelayCommand]
        public void RotateCounterClockwise()
        {
            Prefs.KeysAngle += 5;
            _midiProc.UpdateKeyPositions(Prefs);
        }

        public void AdjustWhiteKeyWidth(double delta)
        {
            Prefs.WhiteKeyWidth += delta;
            _midiProc.UpdateKeyPositions(Prefs);
        }

        public void AdjustBlackKeyOffset(int delta)
        {
            Prefs.YOffsetBlackKeys += delta;
            _midiProc.UpdateKeyPositions(Prefs);
        }

        [RelayCommand]
        public void UpdateKeyCount()
        {
            _midiProc.UpdateKeyPositions(Prefs, rebuild: true);
        }

        //  Mouse / Drag Handling 

        /// <summary>
        /// Called by the View when the user presses the left mouse button.
        /// videoX/Y are already in video coordinate space.
        /// </summary>
        public void OnLeftMouseDown(double videoX, double videoY, bool ctrlHeld)
        {
            if (ctrlHeld)
            {
                // color pick — sampled by the View and passed back via OnColorPicked
                SelectedKeyIndex = -1;
                return;
            }

            int hitKey = HitTestKey(videoX, videoY);
            if (hitKey >= 0)
            {
                DragState = new KeyDragState(DragMode.DragSingle, hitKey);
                SelectedKeyIndex = hitKey;
            }
            else
            {
                SelectedKeyIndex = -1;
            }
        }

        public void OnRightMouseDown(double videoX, double videoY)
        {
            int hitKey = HitTestKey(videoX, videoY);
            double offsetX = hitKey >= 0 ? Prefs.KeyPositions[hitKey].RelativeX : 0;
            DragState = new KeyDragState(DragMode.DragAll, hitKey, offsetX);

            // Quick move all keys without dragging first
            Prefs.XOffsetWhiteKeys = (int)(videoX - DragState.OffsetX);
            Prefs.YOffsetWhiteKeys = (int)videoY;
        }

        public void OnMouseUp()
        {
            DragState = new(DragMode.Idle);
        }

        public void OnMouseMove(double videoX, double videoY)
        {
            switch (DragState.Mode)
            {
                case DragMode.DragSingle when DragState.KeyId >= 0:
                    var current = Prefs.KeyPositions[DragState.KeyId];
                    Prefs.KeyPositions[DragState.KeyId] = current with
                    {
                        RelativeX = videoX - Prefs.XOffsetWhiteKeys,
                        RelativeY = videoY - Prefs.YOffsetWhiteKeys
                    };
                    break;

                case DragMode.DragAll:
                    Prefs.XOffsetWhiteKeys = (int)(videoX - DragState.OffsetX);
                    Prefs.YOffsetWhiteKeys = (int)videoY;
                    break;
            }
        }

        public void OnMouseWheel(double delta)
        {
            Prefs.WhiteKeyWidth += delta > 0 ? 0.05 : -0.05;
            _midiProc.UpdateKeyPositions(Prefs);
        }

        private int HitTestKey(double videoX, double videoY, int hitbox = 10)
        {
            for (int i = 0; i < Prefs.KeyPositions.Count; i++)
            {
                double kx = Prefs.XOffsetWhiteKeys + Prefs.KeyPositions[i].RelativeX;
                double ky = Prefs.YOffsetWhiteKeys + Prefs.KeyPositions[i].RelativeY;
                if (Math.Abs(videoX - kx) < hitbox && Math.Abs(videoY - ky) <= hitbox)
                    return i;
            }
            return -1;
        }

        //  Frame Markers 

        [RelayCommand]
        public void SetStartFrameToCurrent()
        {
            Prefs.StartFrame = _video.GetCurrentFrameIndex();
        }

        [RelayCommand]
        public void ResetStartFrame() => Prefs.StartFrame = 0;

        [RelayCommand]
        public void SetEndFrameToCurrent()
        {
            Prefs.EndFrame = _video.GetCurrentFrameIndex();
        }

        [RelayCommand]
        public void ResetEndFrame() => Prefs.EndFrame = _video.FrameCount;

        //  Settings Commands 

        [RelayCommand]
        public void SaveSettings()
        {
            string path = VideoSettingsPath();
            _settings.Save(Prefs, path);
            StatusMessage = $"Settings saved to {path}";
        }

        [RelayCommand]
        public void LoadSettings()
        {
            LoadSettingsFromFile(VideoSettingsPath());
        }

        private void LoadSettingsFromFile(string path)
        {
            _settings.Load(Prefs, path);
            _midiProc.UpdateKeyPositions(Prefs);
            // Re-seek so the displayed frame reflects the loaded start position
            SeekAndUpdate(Prefs.StartFrame);
            CurrentFrameIndex = Prefs.StartFrame;
        }

        private string VideoSettingsPath() =>
            (_video.FilePath ?? "video") + ".ini";

        //  MIDI Reconstruction 

        [RelayCommand(CanExecute = nameof(CanStartProcessing))]
        public async Task StartReconstructionAsync()
        {
            IsProcessing = true;
            ProcessingProgress = 0;
            StartReconstructionCommand.NotifyCanExecuteChanged();

            _processingCts = new CancellationTokenSource();
            var progress = new Progress<ProcessingProgress>(p =>
            {
                ProcessingProgress = p.Fraction;
                StatusMessage = p.Message;
            });

            try
            {
                var notes = await _midiProc.ProcessVideoAsync(
                    Prefs, _video, progress, _processingCts.Token);

                if (Prefs.SyncNotesStartPos)
                    notes = _midiExport.SyncStartPositions(
                        notes, Prefs.SyncNotesStartPosDeltaMs, Prefs);

                string outputPath = MidiExportService.GenerateOutputPath(_video.FilePath!);

                var (success, message) = Prefs.SavePerChannel
                    ? _midiExport.SavePerChannel(notes, Prefs, outputPath)
                    : _midiExport.Save(notes, Prefs, outputPath);

                Prefs.SaveToDiskMessage = message;
                StatusMessage = message;
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Processing cancelled.";
            }
            finally
            {
                IsProcessing = false;
                _processingCts?.Dispose();
                _processingCts = null;
                StartReconstructionCommand.NotifyCanExecuteChanged();

                // Return to start frame
                SeekAndUpdate(Prefs.StartFrame);
            }
        }

        [RelayCommand]
        public void CancelProcessing() => _processingCts?.Cancel();

        private bool CanStartProcessing() => !IsProcessing;

        //  Toggle Helpers 

        [RelayCommand] public void ToggleNotesOverlap() => Prefs.NotesOverlap = !Prefs.NotesOverlap;
        [RelayCommand] public void ToggleIgnoreMinimal() => Prefs.IgnoreMinimalDuration = !Prefs.IgnoreMinimalDuration;
        [RelayCommand] public void ToggleResize() => Prefs.Resize = !Prefs.Resize;
        [RelayCommand] public void ToggleAlternateKeys() => Prefs.UseAlternateKeys = !Prefs.UseAlternateKeys;
        [RelayCommand] public void ToggleSparks() => Prefs.UseSparks = !Prefs.UseSparks;
        [RelayCommand] public void ToggleRollcheck() => Prefs.Rollcheck = !Prefs.Rollcheck;
        [RelayCommand] public void ToggleRollcheckPriority() => Prefs.RollcheckPriority = !Prefs.RollcheckPriority;

        //  color Map 

        public void DisableColor(int colorIndex)
        {
            if (colorIndex >= 0 && colorIndex < Prefs.Colors.Count)
                Prefs.Colors[colorIndex].Disable();
        }

        public void IncrementColorChannel(int colorIndex)
        {
            if (colorIndex >= 0 && colorIndex < Prefs.Colors.Count)
                Prefs.Colors[colorIndex].MidiChannel++;
        }

        public void DecrementColorChannel(int colorIndex)
        {
            if (colorIndex >= 0 && colorIndex < Prefs.Colors.Count)
                Prefs.Colors[colorIndex].MidiChannel--;
        }

        //  Alternate Key color Sampling 

        public void ReadAllKeyColors()
        {
            if (CurrentVideoFrame == null) return;
            for (int i = 0; i < Prefs.KeyPositions.Count; i++)
                ReadKeyColor(i);
        }

        public void ReadKeyColor(int keyIndex)
        {
            if (CurrentVideoFrame == null || keyIndex >= Prefs.KeyPositions.Count) return;
            var pos = Prefs.KeyPositions[keyIndex];
            var (absX, absY) = pos.ToVideoPixel(Prefs.XOffsetWhiteKeys, Prefs.YOffsetWhiteKeys);

            if (absX < 0 || absX >= _video.VideoWidth || absY < 0 || absY >= _video.VideoHeight) return;

            var (r, g, b) = _video.SamplePixel(CurrentVideoFrame, absX, absY);

            while (Prefs.AlternateKeyColors.Count <= keyIndex)
                Prefs.AlternateKeyColors.Add(RgbColor.Black);

            Prefs.AlternateKeyColors[keyIndex] = new RgbColor(r, g, b);
        }

        //  Panel-specific Commands 

        [RelayCommand]
        public void VerticalAlignKeys() => AlignKeys(vertical: true);

        [RelayCommand]
        public void HorizontalAlignKeys() => AlignKeys(vertical: false);

        [RelayCommand]
        public void MoveSparksUp() => Prefs.SparksYPosition -= 1;

        [RelayCommand]
        public void MoveSparksDown() => Prefs.SparksYPosition += 1;

        // The existing ReadAllKeyColors() is used internally, so this relay command wrapper
        // is named SampleAllKeyColors — bound in XAML as SampleAllKeyColorsCommand.
        // The Extra panel binds to ReadAllKeyColorsCommand — handled as a click handler
        // in the code-behind calling _vm.ReadAllKeyColors() directly instead.
        [RelayCommand]
        public void SampleAllKeyColors()
        {
            ReadAllKeyColors();
            StatusMessage = "Baseline colors sampled for all keys.";
        }

        [RelayCommand]
        public void UpdateSelectedKeyColor()
        {
            if (SelectedKeyIndex >= 0)
            {
                ReadKeyColor(SelectedKeyIndex);
                StatusMessage = $"Color updated for key {SelectedKeyIndex}.";
            }
        }

        //  Key Alignment Helpers 

        public void AlignKeys(bool vertical)
        {
            if (SelectedKeyIndex < 0 || SelectedKeyIndex >= Prefs.KeyPositions.Count) return;

            var reference = Prefs.KeyPositions[SelectedKeyIndex];
            bool selectedIsBlack = _midiProc.IsBlackKey(SelectedKeyIndex);

            for (int i = 0; i < Prefs.KeyPositions.Count; i++)
            {
                bool targetIsBlack = _midiProc.IsBlackKey(i);
                if (targetIsBlack != selectedIsBlack) continue;

                var current = Prefs.KeyPositions[i];
                Prefs.KeyPositions[i] = vertical
                    ? current with { RelativeY = reference.RelativeY }
                    : current with { RelativeX = reference.RelativeX };
            }
        }
    }
}
