namespace Video2Midi2.Models
{
    /// <summary>
    /// One entry in the color map — a light+dark color pair that represents
    /// one "pressed key" appearance, mapped to a MIDI channel and program.
    /// Replaces the parallel lists keyp_colors / keyp_colors_channel /
    /// keyp_colors_channel_prog in Python.
    /// </summary>
    public class ColorEntry : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        public ColorEntry(
            byte lightR, byte lightG, byte lightB,
            byte darkR, byte darkG, byte darkB,
            int midiChannel = 0, int midiProgram = 0)
        {
            Light = new RgbColor(lightR, lightG, lightB);
            Dark = new RgbColor(darkR, darkG, darkB);
            MidiChannel = midiChannel;
            MidiProgram = midiProgram;
        }

        public RgbColor Light { get; set; }
        public RgbColor Dark { get; set; }

        private int _midiChannel;
        public int MidiChannel
        {
            get => _midiChannel;
            set => SetProperty(ref _midiChannel, Math.Clamp(value, 0, 15));
        }

        private int _midiProgram;
        public int MidiProgram
        {
            get => _midiProgram;
            set => SetProperty(ref _midiProgram, Math.Clamp(value, 0, 127));
        }

        /// <summary>A zeroed-out color entry is treated as disabled.</summary>
        public bool IsEnabled => Light != RgbColor.Black || Dark != RgbColor.Black;

        public void Disable() { Light = RgbColor.Black; Dark = RgbColor.Black; }
    }

    /// <summary>
    /// Simple RGB color triplet. Replaces the [r,g,b] lists in Python.
    /// </summary>
    public record RgbColor(byte R, byte G, byte B)
    {
        public static readonly RgbColor Black = new(0, 0, 0);

        /// <summary>
        /// Returns the sum of absolute channel differences — used as
        /// a "closest color" metric in color matching.
        /// </summary>
        public int DistanceTo(RgbColor other) =>
            Math.Abs(R - other.R) + Math.Abs(G - other.G) + Math.Abs(B - other.B);

        /// <summary>Checks whether all channels are within the given delta.</summary>
        public bool IsWithinDelta(RgbColor other, double delta) =>
            Math.Abs(R - other.R) < delta &&
            Math.Abs(G - other.G) < delta &&
            Math.Abs(B - other.B) < delta;
    }
}
