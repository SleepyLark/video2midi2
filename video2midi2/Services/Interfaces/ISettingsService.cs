using Video2Midi2.Models;

namespace Video2Midi2.Services.Interfaces
{

    public interface ISettingsService
    {
        void Save(AppPreferences prefs, string filePath);
        void Load(AppPreferences prefs, string filePath);
        string GetIniFilePath();
    }
}
