using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using Video2Midi2.Models;

namespace Video2Midi2.ViewModels;

public partial class ColorMapViewModel : ObservableObject
{
    private readonly AppPreferences _prefs;

    // All channel view-models — up to 16
    public ObservableCollection<ColorEntryViewModel> AllChannels { get; } = new();

    // ── Toolbar row — always shows the first 4 channels ─────────────────────
    public IEnumerable<ColorEntryViewModel> TopChannels =>
        AllChannels.Take(4);

    // ── Expanded panel — channels 5 and above ────────────────────────────────
    public IEnumerable<ColorEntryViewModel> ExpandedChannels =>
        AllChannels.Skip(4);

    // Controls whether the expanded channel panel is visible in the overlay
    [ObservableProperty] private bool _isExpandPanelVisible = false;

    // Whether there are any channels beyond the first 4
    public bool HasExpandedChannels => AllChannels.Count > 4;

    // ── Eyedrop state ────────────────────────────────────────────────────────
    public ColorEntryViewModel? PendingEyedropTarget  { get; private set; }
    public bool                 PendingEyedropIsLight { get; private set; }
    public bool                 IsEyedropping         => PendingEyedropTarget != null;

    // Live preview color shown in the overlay banner while sampling
    [ObservableProperty] private RgbColor _livePreviewColor = RgbColor.Black;
    [ObservableProperty] private string   _livePreviewHex   = "#000000";

    public ColorMapViewModel(AppPreferences prefs)
    {
        _prefs = prefs;
        RebuildChannels();
    }

    // ── Rebuild ───────────────────────────────────────────────────────────────

    private void RebuildChannels()
    {
        AllChannels.Clear();
        for (int i = 0; i < _prefs.Colors.Count; i++)
        {
            var vm = new ColorEntryViewModel(_prefs.Colors[i], i);
            vm.EyedropRequested += OnEyedropRequested;
            AllChannels.Add(vm);
        }
        NotifyChannelCollectionsChanged();
    }

    private void NotifyChannelCollectionsChanged()
    {
        OnPropertyChanged(nameof(TopChannels));
        OnPropertyChanged(nameof(ExpandedChannels));
        OnPropertyChanged(nameof(HasExpandedChannels));
    }

    // ── Add / Remove ──────────────────────────────────────────────────────────

    [RelayCommand]
    public void AddChannel()
    {
        if (_prefs.Colors.Count >= 16) return; // hard cap at 16 channels

        // New channels start DISABLED so they don't accidentally match pixels
        // until the user eyedrops a real color into them.
        int newMidiChannel = Math.Min(_prefs.Colors.Count, 15);
        var entry = new ColorEntry(0, 0, 0, 0, 0, 0,
            midiChannel: newMidiChannel,
            isEnabled:   false);          // ← disabled by default
        _prefs.Colors.Add(entry);

        var vm = new ColorEntryViewModel(entry, _prefs.Colors.Count - 1);
        vm.EyedropRequested += OnEyedropRequested;
        AllChannels.Add(vm);

        // Auto-open the expand panel if the new channel is beyond the top 4
        if (AllChannels.Count > 4)
            IsExpandPanelVisible = true;

        NotifyChannelCollectionsChanged();
    }

    [RelayCommand]
    public void RemoveChannel(ColorEntryViewModel channel)
    {
        if (AllChannels.Count <= 1) return; // always keep at least one

        _prefs.Colors.Remove(channel.Entry);
        AllChannels.Remove(channel);

        // Collapse expand panel if there are now 4 or fewer channels
        if (AllChannels.Count <= 4)
            IsExpandPanelVisible = false;

        RebuildChannels(); // rebuild to fix sequential Index / Label values
    }

    // ── Expand panel toggle ───────────────────────────────────────────────────

    [RelayCommand]
    public void ToggleExpandPanel()
    {
        IsExpandPanelVisible = !IsExpandPanelVisible;
        OnPropertyChanged(nameof(ExpandButtonLabel));
    }

    // Label for the expand toggle button — switches between ▼ More and ▲ Less
    public string ExpandButtonLabel => IsExpandPanelVisible ? "▲ Less" : "▼ More";

    // ── Eyedropper ────────────────────────────────────────────────────────────

    private void OnEyedropRequested(ColorEntryViewModel sender, bool isLight)
    {
        PendingEyedropTarget  = sender;
        PendingEyedropIsLight = isLight;
        OnPropertyChanged(nameof(IsEyedropping));
    }

    /// <summary>
    /// Called by MainWindow when the user clicks the canvas while eyedropping.
    /// Applies the sampled color to whichever swatch (light or dark) was clicked.
    /// </summary>
    public void ApplyEyedropColor(RgbColor sampledColor)
    {
        if (PendingEyedropTarget == null) return;

        if (PendingEyedropIsLight)
            PendingEyedropTarget.Entry.Light = sampledColor;
        else
            PendingEyedropTarget.Entry.Dark  = sampledColor;

        PendingEyedropTarget = null;
        OnPropertyChanged(nameof(IsEyedropping));
    }

    public void CancelEyedrop()
    {
        PendingEyedropTarget = null;
        OnPropertyChanged(nameof(IsEyedropping));
    }

    // Called by MainWindow on every mouse move while eyedropping
    public void UpdateLivePreview(RgbColor color)
    {
        LivePreviewColor = color;
        LivePreviewHex   = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
