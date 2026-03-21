using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Video2Midi2.Models;

namespace Video2Midi2.ViewModels;

public partial class ColorEntryViewModel : ObservableObject
{
    public ColorEntry Entry { get; }
    public int Index { get; }

    // Raised when the user clicks a swatch to start eyedropping
    public event Action<ColorEntryViewModel, bool>? EyedropRequested;

    public ColorEntryViewModel(ColorEntry entry, int index)
    {
        Entry = entry;
        Index = index;
    }

    // Display label — shown in the pill header
    public string Label => $"Ch {Index + 1}";

    // ── MIDI Channel number (1-indexed for display, 0-indexed in model) ─────────
    // Multiple color entries can share the same MIDI channel number so that
    // different on-screen colors all map to the same output track.

    public int MidiChannelDisplay
    {
        get => Entry.MidiChannel + 1;
        set
        {
            int clamped = Math.Clamp(value, 1, 16);
            if (Entry.MidiChannel != clamped - 1)
            {
                Entry.MidiChannel = clamped - 1;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MidiChannelLabel));
            }
        }
    }

    public string MidiChannelLabel => $"→ {MidiChannelDisplay}";

    [RelayCommand]
    public void IncrementChannel()
    {
        MidiChannelDisplay = MidiChannelDisplay < 16 ? MidiChannelDisplay + 1 : 1; // wraps 16→1
        OnPropertyChanged(nameof(MidiChannelDisplay));
        OnPropertyChanged(nameof(MidiChannelLabel));
    }

    [RelayCommand]
    public void DecrementChannel()
    {
        MidiChannelDisplay = MidiChannelDisplay > 1 ? MidiChannelDisplay - 1 : 16; // wraps 1→16
        OnPropertyChanged(nameof(MidiChannelDisplay));
        OnPropertyChanged(nameof(MidiChannelLabel));
    }

    // ── Enable / disable toggle ───────────────────────────────────────────────
    // A disabled channel is skipped by the detection service.
    // Toggling is non-destructive — colors are preserved.

    [RelayCommand]
    public void ToggleEnabled()
    {
        Entry.IsEnabled = !Entry.IsEnabled;
        OnPropertyChanged(nameof(EnabledLabel));
    }

    // Short label for the toggle button in the pill
    public string EnabledLabel => Entry.IsEnabled ? "●" : "○";

    // ── Color swatch clicks — both delegate to eyedrop mode ─────────────────

    [RelayCommand]
    public void PickLightColor() => EyedropRequested?.Invoke(this, true);

    [RelayCommand]
    public void PickDarkColor() => EyedropRequested?.Invoke(this, false);
}
