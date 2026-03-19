namespace Video2Midi2.Models
{
    /// <summary>
    /// A single MIDI note event — immutable value type.
    /// Replaces the dictionary entries in the Python midinotes.notes list.
    /// </summary>
    public record MidiNote(
        int Track,
        int Channel,
        int NoteNumber,     // absolute MIDI note (basenote + key index)
        double StartTime,   // in beats (already converted from frames/fps * tempo/60)
        double Duration,    // in beats
        int Volume          // 0-127
    );

}
