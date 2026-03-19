using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using Video2Midi2.Models;

namespace Video2Midi2.ViewModels;

public partial class ColorMapViewModel : ObservableObject
{
    private readonly AppPreferences _prefs;
    private const int DefaultVisible = 4;

    public ObservableCollection<ColorEntryViewModel> AllChannels { get; } = new();

    [ObservableProperty] private bool _isExpanded = false;
    [ObservableProperty] private string _toggleLabel = "▼ More";

    [ObservableProperty] private RgbColor _livePreviewColor = RgbColor.Black;
    [ObservableProperty] private string _livePreviewHex = "#000000";

    // The ItemsControl binds to this — filters based on expanded state
    public IEnumerable<ColorEntryViewModel> VisibleChannels =>
        IsExpanded ? AllChannels : AllChannels.Take(DefaultVisible);

    // Eyedrop state — set when the user clicks the eyedropper button
    public ColorEntryViewModel? PendingEyedropTarget { get; private set; }
    public bool PendingEyedropIsLight { get; private set; }
    public bool IsEyedropping => PendingEyedropTarget != null;

    public ColorMapViewModel(AppPreferences prefs)
    {
        _prefs = prefs;
        RebuildChannels();
    }

    private void RebuildChannels()
    {
        AllChannels.Clear();
        for (int i = 0; i < _prefs.Colors.Count; i++)
        {
            var vm = new ColorEntryViewModel(_prefs.Colors[i], i);
            vm.EyedropRequested += OnEyedropRequested;
            AllChannels.Add(vm);
        }
        OnPropertyChanged(nameof(VisibleChannels));
    }

    [RelayCommand]
    public void ToggleExpanded()
    {
        IsExpanded = !IsExpanded;
        ToggleLabel = IsExpanded ? "▲ Less" : "▼ More";
        OnPropertyChanged(nameof(VisibleChannels));
    }

    [RelayCommand]
    public void AddChannel()
    {
        if (_prefs.Colors.Count >= 12) return;

        var entry = new ColorEntry(0, 0, 0, 0, 0, 0, _prefs.Colors.Count / 2);
        _prefs.Colors.Add(entry);

        var vm = new ColorEntryViewModel(entry, _prefs.Colors.Count - 1);
        vm.EyedropRequested += OnEyedropRequested;
        AllChannels.Add(vm);

        // Auto-expand if the new channel would be hidden
        if (!IsExpanded && AllChannels.Count > DefaultVisible)
        {
            IsExpanded = true;
            ToggleLabel = "▲ Less";
        }

        OnPropertyChanged(nameof(VisibleChannels));
    }

    [RelayCommand]
    public void RemoveChannel(ColorEntryViewModel channel)
    {
        if (AllChannels.Count <= 1) return;

        _prefs.Colors.Remove(channel.Entry);
        AllChannels.Remove(channel);
        RebuildChannels(); // rebuild to fix indices
    }

    //  Eyedropper 

    private void OnEyedropRequested(ColorEntryViewModel sender, bool isLight)
    {
        PendingEyedropTarget = sender;
        PendingEyedropIsLight = isLight;
        OnPropertyChanged(nameof(IsEyedropping));
    }

    /// <summary>
    /// Called by MainWindow when the user clicks the canvas while eyedropping.
    /// </summary>
    public void ApplyEyedropColor(RgbColor sampledColor)
    {
        if (PendingEyedropTarget == null) return;

        if (PendingEyedropIsLight)
            PendingEyedropTarget.Entry.Light = sampledColor;
        else
            PendingEyedropTarget.Entry.Dark = sampledColor;

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
        LivePreviewHex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}