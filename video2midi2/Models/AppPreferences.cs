using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Video2Midi2.Models
{
    /// <summary>
    /// All user-configurable preferences and runtime state.
    /// Replaces the static prefs class from Python - now a proper instance
    /// so it can be injected, observed, and tested.
    ///
    /// Implements INotifyPropertyChanged (via ObservableObject) so that WPF
    /// bindings automatically update when any property changes.
    /// </summary>
    public partial class AppPreferences : ObservableObject
    {
        //  MIDI Output 

        [ObservableProperty] private string _midiTrackName = "Sample Track";
        [ObservableProperty] private int _midiFileFormat = 1;   // 1 or 2
        [ObservableProperty] private int _tempo = 120;

        //  Note Detection 

        /// <summary>color-match tolerance (0-130). Higher = less strict.</summary>
        [ObservableProperty] private int _sensitivity = 90;

        [ObservableProperty] private bool _notesOverlap = false;
        [ObservableProperty] private double _minimalDuration = 0.1;
        [ObservableProperty] private bool _ignoreMinimalDuration = false;
        [ObservableProperty] private bool _rollcheck = false;
        [ObservableProperty] private bool _rollcheckPriority = false;   // false = black key priority
        [ObservableProperty] private bool _syncNotesStartPos = false;
        [ObservableProperty] private double _syncNotesStartPosDeltaMs = 1000;

        //  Per-color sensitivity 

        [ObservableProperty] private bool _usePerColorSensitivity = false;
        public ObservableCollection<double> PerColorDelta { get; } = new(Enumerable.Repeat(90.0, 20));

        //  Key Layout 

        [ObservableProperty] private int _keyCount = 88;
        [ObservableProperty] private double _whiteKeyWidth = 24.6;
        [ObservableProperty] private int _xOffsetWhiteKeys = 60;
        [ObservableProperty] private int _yOffsetWhiteKeys = 673;
        [ObservableProperty] private int _yOffsetBlackKeys = -30;
        [ObservableProperty] private double _blackKeyRelativePosition = 0.4;
        [ObservableProperty] private double _keysAngle = 90.0;
        [ObservableProperty] private int _octave = 3;

        /// <summary>
        /// Computed key positions in video-relative space.
        /// Index maps to semitone offset from the base note.
        /// Each entry is (RelativeX, RelativeY).
        /// </summary>
        public List<KeyPosition> KeyPositions { get; set; } = new();

        //  Frame Range 

        [ObservableProperty] private int _startFrame = 0;
        [ObservableProperty] private int _endFrame = 1;

        //  color Map 

        /// <summary>
        /// Up to 12 color entries. Each color maps to a MIDI channel and
        /// optional MIDI program change. The list is observable so the
        /// ColorMapPanel can bind directly.
        /// </summary>
        public ObservableCollection<ColorEntry> Colors { get; } = new()
        {
            new ColorEntry(166, 250, 103,  58, 146,   0, midiChannel: 0),  // Light/Dark Green
            new ColorEntry(102, 185, 207,   8, 113, 174, midiChannel: 1),  // Light/Dark Blue
            new ColorEntry(255, 255,  85, 254, 210,   0, midiChannel: 2),  // Light/Dark Yellow
            new ColorEntry(255, 212,  85, 255, 138,   0, midiChannel: 3),  // Light/Dark Orange
            new ColorEntry(253, 125, 114, 255,  37,   9, midiChannel: 4),  // Light/Dark Red
            new ColorEntry(0, 0, 0, 0, 0, 0, midiChannel: 5),
            new ColorEntry(0, 0, 0, 0, 0, 0, midiChannel: 6),
            new ColorEntry(0, 0, 0, 0, 0, 0, midiChannel: 7),
            new ColorEntry(0, 0, 0, 0, 0, 0, midiChannel: 8),
            new ColorEntry(0, 0, 0, 0, 0, 0, midiChannel: 9),
            new ColorEntry(0, 0, 0, 0, 0, 0, midiChannel: 10),
            new ColorEntry(0, 0, 0, 0, 0, 0, midiChannel: 11),
        };

        //  Sparks (fade/sustain detection) 

        [ObservableProperty] private bool _useSparks = false;
        [ObservableProperty] private int _sparksYPosition = -110;
        public ObservableCollection<double> SparksColorSensitivity { get; } =
            new(Enumerable.Repeat(50.0, 25));

        //  Alternate Key Mode 

        /// <summary>
        /// When true, detect key presses by color CHANGE from a sampled
        /// baseline rather than by absolute color matching.
        /// </summary>
        [ObservableProperty] private bool _useAlternateKeys = false;

        /// <summary>Per-key baseline colors captured in alternate mode.</summary>
        public List<RgbColor> AlternateKeyColors { get; set; } = new();
        public List<int> AlternateKeySensitivities { get; set; } = new();

        //  App behavior 

        [ObservableProperty] private bool _autoClose = true;
        [ObservableProperty] private bool _resize = false;
        [ObservableProperty] private int _resizeWidth = 1280;
        [ObservableProperty] private int _resizeHeight = 720;
        [ObservableProperty] private bool _savePerChannel = false;
        [ObservableProperty] private bool _debug = false;

        //  Runtime-only (not persisted) 

        /// <summary>Message displayed after save completes.</summary>
        [ObservableProperty] private string _saveToDiskMessage = string.Empty;
    }
}
