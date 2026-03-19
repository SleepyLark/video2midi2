namespace Video2Midi2.Models
{

    /// <summary>
    /// Computed screen/video-space position for one piano key.
    /// X and Y are relative to the white-key offset origin (same coordinate
    /// space as prefs.keys_pos in Python).
    /// </summary>
    public record KeyPosition(double RelativeX, double RelativeY)
    {
        /// <summary>Returns the absolute video-space pixel coordinate.</summary>
        public (int X, int Y) ToVideoPixel(int xOffset, int yOffset) =>
            (xOffset + (int)RelativeX, yOffset + (int)RelativeY);
    }

    /// <summary>Drag state machine — replaces three separate fields in AppController.</summary>
    public enum DragMode { Idle, DragSingle, DragAll }

    public record KeyDragState(DragMode Mode, int KeyId = -1, double OffsetX = 0);

    /// <summary>Progress report emitted during MIDI reconstruction.</summary>
    public record ProcessingProgress(int CurrentFrame, int TotalFrames, string Message)
    {
        public double Fraction => TotalFrames > 0 ? (double)CurrentFrame / TotalFrames : 0;
    }
}
