using Video2Midi2.Models;

namespace Video2Midi2.Services.Interfaces
{
    public interface IMidiExportService
    {
        (bool Success, string Message) Save(
            IEnumerable<MidiNote> notes, AppPreferences prefs, string filePath);

        (bool Success, string Message) SavePerChannel(
            IEnumerable<MidiNote> notes, AppPreferences prefs, string filePath);

        List<MidiNote> SyncStartPositions(
            List<MidiNote> notes, double deltaSeconds, AppPreferences prefs, bool useAbsoluteDelta = false);
    }
}
