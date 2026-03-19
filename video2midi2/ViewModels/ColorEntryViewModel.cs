using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Video2Midi2.Models;

namespace Video2Midi2.ViewModels;

public partial class ColorEntryViewModel : ObservableObject
{
    public ColorEntry Entry { get; }
    public int Index { get; }

    public event Action<ColorEntryViewModel, bool>? EyedropRequested;

    public ColorEntryViewModel(ColorEntry entry, int index)
    {
        Entry = entry;
        Index = index;
    }

    public string Label => $"Ch {Index + 1}";

    [RelayCommand]
    public void PickLightColor() => EyedropRequested?.Invoke(this, true);

    [RelayCommand]
    public void PickDarkColor() => EyedropRequested?.Invoke(this, false);
}