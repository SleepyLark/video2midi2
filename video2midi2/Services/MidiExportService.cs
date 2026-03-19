using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Composing;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Video2Midi2.Models;
using Video2Midi2.Services.Interfaces;
using System.IO;

namespace Video2Midi2.Services
{
    /// <summary>
    /// Converts a list of <see cref="MidiNote"/> records into a MIDI file and
    /// writes it to disk.  Ports midi.py / midinotes using DryWetMidi instead
    /// of the Python midiutil library.
    /// </summary>
    public sealed class MidiExportService : IMidiExportService
    {
        //  Public API 

        /// <summary>
        /// Saves all notes to a single MIDI file.
        /// Returns (success, statusMessage).
        /// </summary>
        public (bool Success, string Message) Save(
            IEnumerable<MidiNote> notes,
            AppPreferences prefs,
            string filePath)
        {
            var noteList = notes.ToList();
            if (noteList.Count == 0)
                return (false, "No notes to save.");

            try
            {
                var midiFile = BuildMidiFile(noteList, prefs);
                midiFile.Write(filePath, overwriteFile: true);
                return (true, $"Saved to: {filePath}");
            }
            catch (Exception ex)
            {
                return (false, $"Error saving: {ex.Message}");
            }
        }

        /// <summary>
        /// Splits notes by MIDI channel and writes one file per channel.
        /// Equivalent to save_to_disk_per_channel() in Python.
        /// </summary>
        public (bool Success, string Message) SavePerChannel(
            IEnumerable<MidiNote> notes,
            AppPreferences prefs,
            string filePath)
        {
            var noteList = notes.ToList();
            if (noteList.Count == 0)
                return (false, "No notes to save.");

            string baseName = Path.GetFileNameWithoutExtension(filePath);
            string directory = Path.GetDirectoryName(filePath) ?? ".";
            string ext = Path.GetExtension(filePath);

            int savedCount = 0;

            for (int channel = 0; channel < 16; channel++)
            {
                var channelNotes = noteList.Where(n => n.Channel == channel).ToList();
                if (channelNotes.Count == 0) continue;

                try
                {
                    var midiFile = BuildMidiFile(channelNotes, prefs, singleChannel: channel);
                    string channelPath = Path.Combine(directory, $"{baseName}_ch{channel + 1}{ext}");
                    midiFile.Write(channelPath, overwriteFile: true);
                    savedCount++;
                }
                catch (Exception ex)
                {
                    // Log and continue writing other channels
                    Console.WriteLine($"Error saving channel {channel}: {ex.Message}");
                }
            }

            return (savedCount > 0,
                $"Saved {savedCount} channel files to: {directory}");
        }

        /// <summary>
        /// Synchronises notes that start very close together so they share an
        /// identical start time.  Port of sync_start_pos() from Python.
        /// </summary>
        public List<MidiNote> SyncStartPositions(
            List<MidiNote> notes,
            double deltaSeconds,
            AppPreferences prefs,
            bool useAbsoluteDelta = false)
        {
            // Convert delta from ms to beats
            double deltaBeats = deltaSeconds / 1000.0 * prefs.Tempo / 60.0;

            var result = notes.ToList();

            for (int i = 0; i < result.Count; i++)
            {
                for (int j = i + 1; j < result.Count; j++)
                {
                    double diff = result[j].StartTime - result[i].StartTime;

                    bool shouldSync = useAbsoluteDelta
                        ? Math.Abs(diff) < deltaBeats
                        : diff < deltaBeats && diff >= 0;

                    if (shouldSync)
                        result[j] = result[j] with { StartTime = result[i].StartTime };
                }
            }

            return result;
        }

        //  Private Helpers 

        private MidiFile BuildMidiFile(
            List<MidiNote> notes,
            AppPreferences prefs,
            int? singleChannel = null)
        {
            // DryWetMidi works with ticks.  We use the tempo map to convert
            // beats → ticks automatically via the Interaction layer.
            var midiFile = new MidiFile();
            var trackChunk = new TrackChunk();
            midiFile.Chunks.Add(trackChunk);

            //  Tempo & track name 
            using (var manager = new TimedObjectsManager<TimedEvent>(trackChunk.Events))
            {
                manager.Objects.Add(new TimedEvent(
                    new SetTempoEvent(Tempo.FromBeatsPerMinute(prefs.Tempo).MicrosecondsPerQuarterNote),
                    time: 0));

                manager.Objects.Add(new TimedEvent(
                    new SequenceTrackNameEvent(prefs.MidiTrackName),
                    time: 0));

                //  Program changes 
                foreach (var colorEntry in prefs.Colors)
                {
                    if (!colorEntry.IsEnabled) continue;
                    int ch = colorEntry.MidiChannel;
                    if (singleChannel.HasValue && ch != singleChannel.Value) continue;

                    manager.Objects.Add(new TimedEvent(
                        new ProgramChangeEvent((SevenBitNumber)colorEntry.MidiProgram)
                        { Channel = (FourBitNumber)ch },
                        time: 0));
                }
            }

            //  Notes 
            // DryWetMidi's NotesManager is the cleanest API for note on/off pairs.
            var tempoMap = TempoMap.Create(Tempo.FromBeatsPerMinute(prefs.Tempo));

            using (var timedEventsManager = new TimedObjectsManager<TimedEvent>(trackChunk.Events))
            using (var notesManager = new TimedObjectsManager<Note>(trackChunk.Events))
            {
                foreach (var n in notes)
                {
                    if (singleChannel.HasValue && n.Channel != singleChannel.Value)
                        continue;

                    // Convert beats to microseconds for DryWetMidi ticks
                    long startTicks = TimeConverter.ConvertFrom(
                        new MetricTimeSpan((long)(n.StartTime * 60.0 / prefs.Tempo * 1_000_000)),
                        tempoMap);

                    long durationTicks = LengthConverter.ConvertFrom(
                        new MetricTimeSpan((long)(n.Duration * 60.0 / prefs.Tempo * 1_000_000)),
                        startTicks,
                        tempoMap);

                    notesManager.Objects.Add(new Note(
                        (SevenBitNumber)Math.Clamp(n.NoteNumber, 0, 127))
                    {
                        Channel = (FourBitNumber)n.Channel,
                        Time = startTicks,
                        Length = durationTicks,
                        Velocity = (SevenBitNumber)Math.Clamp(n.Volume, 0, 127)
                    });
                }
            }

            return midiFile;
        }

        /// <summary>
        /// Generates a unique output filename based on the source video path,
        /// incrementing a counter if the file already exists.
        /// Port of the filename logic in process_midi() from Python.
        /// </summary>
        public static string GenerateOutputPath(string videoFilePath)
        {
            string baseName = Path.GetFileName(videoFilePath) + "_output.mid";
            if (!File.Exists(baseName)) return baseName;

            for (int i = 0; i < 1000; i++)
            {
                string candidate = Path.GetFileName(videoFilePath) + $"_{i}_output.mid";
                if (!File.Exists(candidate)) return candidate;
            }

            return baseName; // fallback — overwrite
        }
    }
}
