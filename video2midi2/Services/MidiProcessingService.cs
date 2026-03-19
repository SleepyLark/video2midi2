using OpenCvSharp;
using Video2Midi2.Models;
using Video2Midi2.Services.Interfaces;

namespace Video2Midi2.Services
{
    /// <summary>
    /// Core frame-by-frame MIDI reconstruction engine.
    /// Ports midi_proc.py / MidiHandler — but with proper async support,
    /// no direct UI references, and clean service boundaries.
    /// </summary>
    public sealed class MidiProcessingService : IMidiProcessingService
    {
        //  Key Layout 

        /// <summary>
        /// Recalculates all key positions based on current preference values.
        /// Direct port of update_key_positions() from Python.
        /// </summary>
        public void UpdateKeyPositions(AppPreferences prefs, bool rebuild = false)
        {
            if (rebuild)
            {
                prefs.KeyPositions.Clear();
                for (int i = 0; i < prefs.KeyCount; i++)
                    prefs.KeyPositions.Add(new KeyPosition(0, 0));
            }

            double currentX = 0;

            for (int keyIndex = 0; keyIndex < prefs.KeyCount; keyIndex++)
            {
                int octaveIdx = keyIndex / 12;
                int semitoneIdx = keyIndex % 12;

                double posX = currentX;
                double posY = 0;

                if (IsBlackKey(semitoneIdx))
                {
                    posY = prefs.YOffsetBlackKeys;
                    currentX -= prefs.WhiteKeyWidth;  // black key shares space with prior white key

                    posX = semitoneIdx switch
                    {
                        1 or 6 => currentX + prefs.WhiteKeyWidth * prefs.BlackKeyRelativePosition,
                        8 => currentX + prefs.WhiteKeyWidth * 0.5,
                        3 or 10 => currentX + prefs.WhiteKeyWidth * (1.0 - prefs.BlackKeyRelativePosition),
                        _ => posX
                    };
                }

                currentX += prefs.WhiteKeyWidth;

                // Apply rotation
                var rotated = VRotate(posX, posY, prefs.KeysAngle);

                int absoluteIndex = octaveIdx * 12 + semitoneIdx;
                if (absoluteIndex < prefs.KeyPositions.Count)
                    prefs.KeyPositions[absoluteIndex] = new KeyPosition(-rotated.X, rotated.Y);
            }
        }

        /// <summary>Returns true if the semitone index (0-11) is a black key.</summary>
        public bool IsBlackKey(int semitoneIndex)
        {
            int j = semitoneIndex % 12;
            return j is 1 or 3 or 6 or 8 or 10;
        }

        public bool IsWhiteKey(int semitoneIndex) => !IsBlackKey(semitoneIndex);

        //  color Detection 

        /// <summary>
        /// Inspects a single video frame and returns the pressed/unpressed state
        /// for every key.  This logic lived in ui.py / detect_key_presses() in
        /// Python — moved here to keep the View free of business logic.
        ///
        /// Returns one <see cref="KeyDetectionResult"/> per key in prefs.KeyPositions.
        /// </summary>
        public IReadOnlyList<KeyDetectionResult> DetectKeyPresses(
            Mat frame,
            AppPreferences prefs,
            IVideoService videoService,
            double sparksHeight = 1.0)
        {
            var results = new List<KeyDetectionResult>(prefs.KeyPositions.Count);

            for (int i = 0; i < prefs.KeyPositions.Count; i++)
            {
                if (i > 144)
                    break;

                var pos = prefs.KeyPositions[i];
                var (absX, absY) = pos.ToVideoPixel(prefs.XOffsetWhiteKeys, prefs.YOffsetWhiteKeys);

                // Bounds check
                if (absX < 0 || absX >= videoService.VideoWidth ||
                    absY < 0 || absY >= videoService.VideoHeight)
                {
                    results.Add(KeyDetectionResult.Unpressed(i));
                    continue;
                }

                var (r, g, b) = videoService.SamplePixel(frame, absX, absY);
                var keyColor = new RgbColor(r, g, b);

                //  Spark (fade/sustain) sampling 
                RgbColor sparkColor = RgbColor.Black;
                if (prefs.UseSparks)
                    sparkColor = SampleSparkColor(frame, i, prefs, videoService, (int)sparksHeight);

                //  Alternate mode (detect color CHANGE from baseline) 
                if (prefs.UseAlternateKeys && i < prefs.AlternateKeyColors.Count)
                {
                    var baseline = prefs.AlternateKeyColors[i];
                    double deltaSens = prefs.Sensitivity +
                        (i < prefs.AlternateKeySensitivities.Count ? prefs.AlternateKeySensitivities[i] : 0);

                    if (Math.Abs(r - baseline.R) > deltaSens &&
                        Math.Abs(g - baseline.G) > deltaSens &&
                        Math.Abs(b - baseline.B) > deltaSens)
                    {
                        results.Add(new KeyDetectionResult(i, KeyPressState.Pressed, baseline, -1));
                        continue;
                    }

                    results.Add(KeyDetectionResult.Unpressed(i));
                    continue;
                }

                //  Normal mode (match against defined color list) 
                int bestColorId = -1;
                double bestDist = double.MaxValue;
                bool isPressed = false;

                for (int j = 0; j < prefs.Colors.Count; j++)
                {
                    var entry = prefs.Colors[j];
                    if (!entry.IsEnabled) continue;

                    double delta = prefs.UsePerColorSensitivity && j < prefs.PerColorDelta.Count
                        ? prefs.PerColorDelta[j]
                        : prefs.Sensitivity;

                    // Check both light and dark variants
                    if (keyColor.IsWithinDelta(entry.Light, delta) ||
                        keyColor.IsWithinDelta(entry.Dark, delta))
                    {
                        double dist = Math.Min(
                            keyColor.DistanceTo(entry.Light),
                            keyColor.DistanceTo(entry.Dark));

                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            bestColorId = j;
                            isPressed = true;
                        }
                    }
                }

                if (!isPressed)
                {
                    results.Add(KeyDetectionResult.Unpressed(i));
                    continue;
                }

                // Determine if this is a sustain (spark fade) state
                var pressState = KeyPressState.Pressed;
                if (prefs.UseSparks && bestColorId >= 0)
                {
                    double sparkDelta = prefs.SparksColorSensitivity.Count > bestColorId
                        ? prefs.SparksColorSensitivity[bestColorId]
                        : 50;

                    var matchColor = prefs.Colors[bestColorId].Light; // approximate
                    bool hasSpark = sparkColor.R - matchColor.R > sparkDelta ||
                                     sparkColor.G - matchColor.G > sparkDelta ||
                                     sparkColor.B - matchColor.B > sparkDelta;

                    if (!hasSpark)
                        pressState = KeyPressState.Sustain;
                }

                results.Add(new KeyDetectionResult(i, pressState, keyColor, bestColorId));
            }

            return results;
        }

        //  Main Processing Loop 

        /// <summary>
        /// Processes the video from start_frame to end_frame and collects MIDI notes.
        /// Async with progress reporting and cancellation support.
        ///
        /// This is the port of process_midi() from Python, minus all UI references.
        /// </summary>
        public async Task<List<MidiNote>> ProcessVideoAsync(
            AppPreferences prefs,
            IVideoService videoService,
            IProgress<ProcessingProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                var notes = new List<MidiNote>();
                int basenote = prefs.Octave * 12;
                int track = 0;
                int volume = 100;
                double firstNoteTime = 0;

                // Per-key state arrays (indexed by key position, max 144)
                int numKeys = prefs.KeyPositions.Count;
                var noteState = new int[numKeys];    // 0=off, 1=pressed, 2=sustain
                var noteStart = new int[numKeys];    // frame when note started
                var noteChannel = new int[numKeys];    // MIDI channel for this note
                var noteTmp = new int[numKeys];    // current-frame press state

                // Seek to start
                var frame = videoService.GetFrame(prefs.StartFrame);
                int currentFrame = prefs.StartFrame;

                while (frame != null && currentFrame <= prefs.EndFrame)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Report progress every 10 frames
                    if (currentFrame % 10 == 0)
                    {
                        progress?.Report(new ProcessingProgress(
                            currentFrame - prefs.StartFrame,
                            prefs.EndFrame - prefs.StartFrame,
                            $"Frame {currentFrame} / {prefs.EndFrame}"));
                    }

                    //  Detect key presses this frame 
                    var detected = DetectKeyPresses(frame, prefs, videoService);

                    // Fill noteTmp
                    foreach (var det in detected)
                        if (det.KeyIndex < numKeys)
                            noteTmp[det.KeyIndex] = (int)det.State;

                    //  Rollcheck filter 
                    if (prefs.Rollcheck)
                        ApplyRollcheck(noteState, noteTmp, prefs);

                    //  Note on / off logic 
                    for (int i = 0; i < numKeys; i++)
                    {
                        int currentDetectedChannel = detected.Count > i
                            ? GetChannelForDetection(detected[i], prefs)
                            : 0;

                        int pressed = noteTmp[i];

                        if (pressed != 0)
                        {
                            // Handle overlap (same key, different channel = restrike)
                            if (noteState[i] != 0 && noteChannel[i] != currentDetectedChannel
                                && prefs.NotesOverlap)
                            {
                                var note = BuildNote(track, noteChannel[i], basenote + i,
                                    noteStart[i], currentFrame, videoService.Fps,
                                    prefs, ref firstNoteTime, volume);
                                if (note != null) notes.Add(note);

                                noteStart[i] = currentFrame;
                                noteChannel[i] = currentDetectedChannel;
                            }

                            if (noteState[i] == 0)
                            {
                                // Note on
                                noteStart[i] = currentFrame;
                                noteChannel[i] = currentDetectedChannel;
                                if (firstNoteTime == 0)
                                    firstNoteTime = currentFrame / videoService.Fps;
                            }

                            noteState[i] = pressed;
                        }
                        else
                        {
                            if (noteState[i] != 0)
                            {
                                // Note off
                                var note = BuildNote(track, noteChannel[i], basenote + i,
                                    noteStart[i], currentFrame, videoService.Fps,
                                    prefs, ref firstNoteTime, volume);
                                if (note != null) notes.Add(note);

                                noteState[i] = 0;

                                // Sustain restart (keypressed==2)
                                if (pressed == 2)
                                {
                                    noteState[i] = pressed;
                                    noteStart[i] = currentFrame;
                                    noteChannel[i] = currentDetectedChannel;
                                }
                            }
                        }
                    }

                    currentFrame++;
                    frame = currentFrame <= prefs.EndFrame
                        ? videoService.ReadNextFrame()
                        : null;
                }

                return notes;

            }, cancellationToken);
        }

        //  Helpers 

        private MidiNote? BuildNote(
            int track, int channel, int noteNumber,
            int startFrame, int endFrame, double fps,
            AppPreferences prefs, ref double firstNoteTime, int volume)
        {
            double timeVal = startFrame / fps;
            double duration = (endFrame - startFrame) / fps;

            if (firstNoteTime == 0) firstNoteTime = timeVal;

            if (duration < prefs.MinimalDuration)
            {
                if (prefs.IgnoreMinimalDuration) return null;
                duration = prefs.MinimalDuration;
            }

            double beatsStart = timeVal * prefs.Tempo / 60.0;
            double beatsDuration = duration * prefs.Tempo / 60.0;

            return new MidiNote(track, channel, noteNumber, beatsStart, beatsDuration, volume);
        }

        private int GetChannelForDetection(KeyDetectionResult det, AppPreferences prefs)
        {
            if (det.ColorIndex < 0 || det.ColorIndex >= prefs.Colors.Count)
                return 0;
            return prefs.Colors[det.ColorIndex].MidiChannel;
        }

        private void ApplyRollcheck(int[] noteState, int[] noteTmp, AppPreferences prefs)
        {
            for (int i = 1; i < noteTmp.Length - 1; i++)
            {
                if (noteState[i] == 0) continue;

                bool isBlack = IsBlackKey(i);

                // rollcheckPriority false = black keys win (suppress adjacent white)
                bool shouldSuppress = prefs.RollcheckPriority
                    ? isBlack                    // white keys win → suppress black
                    : !isBlack;                  // black keys win → suppress white

                if (shouldSuppress)
                {
                    if (noteTmp[i + 1] > 0 && noteTmp[i] > 0) noteTmp[i] = 0;
                    if (noteTmp[i - 1] > 0 && noteTmp[i] > 0) noteTmp[i] = 0;
                }
            }
        }

        /// <summary>Port of v_rotate() from Python.</summary>
        public (double X, double Y) VRotate(double x, double y, double angleDegrees)
        {
            double rad = angleDegrees * Math.PI / 180.0;
            return (
                X: y * Math.Cos(rad) - x * Math.Sin(rad),
                Y: y * Math.Sin(rad) + x * Math.Cos(rad)
            );
        }

        /// <summary>Port of snap_to_grid() from Python.</summary>
        public double SnapToGrid(double value, int gridSize)
        {
            double quantized = (int)((value - (int)value) * gridSize) / (double)gridSize;
            return quantized + (int)value;
        }

        private RgbColor SampleSparkColor(Mat frame, int keyIndex, AppPreferences prefs,
            IVideoService videoService, int height)
        {
            int totalR = 0, totalG = 0, totalB = 0;
            int count = 0;

            var pos = prefs.KeyPositions[keyIndex];

            for (int dy = 0; dy < height; dy++)
            {
                int absX = prefs.XOffsetWhiteKeys + (int)pos.RelativeX;
                int absY = prefs.SparksYPosition - dy;

                if (absX < 0 || absX >= videoService.VideoWidth ||
                    absY < 0 || absY >= videoService.VideoHeight)
                    continue;

                var (r, g, b) = videoService.SamplePixel(frame, absX, absY);
                totalR += r; totalG += g; totalB += b;
                count++;
            }

            if (count == 0) return RgbColor.Black;
            return new RgbColor((byte)(totalR / count), (byte)(totalG / count), (byte)(totalB / count));
        }
    }

    //  Supporting types 

    public enum KeyPressState { Unpressed = 0, Pressed = 1, Sustain = 2 }

    public record KeyDetectionResult(int KeyIndex, KeyPressState State, RgbColor Color, int ColorIndex)
    {
        public static KeyDetectionResult Unpressed(int keyIndex) =>
            new(keyIndex, KeyPressState.Unpressed, RgbColor.Black, -1);
    }
}
