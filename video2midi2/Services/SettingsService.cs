using System.IO;
using IniParser;
using IniParser.Model;
using Video2Midi2.Models;
using Video2Midi2.Services.Interfaces;

namespace Video2Midi2.Services
{
    /// <summary>
    /// Loads and saves <see cref="AppPreferences"/> to/from an INI file.
    /// Ports settings.py — now a proper injected service instead of global functions.
    ///
    /// INI format is deliberately kept compatible with the Python v2m.ini files
    /// so existing user config files continue to work.
    /// </summary>
    public sealed class SettingsService : ISettingsService
    {
        private const string Section = "options";
        private readonly FileIniDataParser _parser = new();

        //  Public API 

        public void Save(AppPreferences prefs, string filePath)
        {
            var data = new IniData();
            var s = data[Section];

            s["midi_track_name"] = prefs.MidiTrackName;
            s["debug"] = B(prefs.Debug);
            s["notes_overlap"] = B(prefs.NotesOverlap);
            s["resize"] = B(prefs.Resize);
            s["resize_width"] = prefs.ResizeWidth.ToString();
            s["resize_height"] = prefs.ResizeHeight.ToString();
            s["minimal_note_duration"] = prefs.MinimalDuration.ToString("F3");
            s["ignore_notes_with_minimal_duration"] = B(prefs.IgnoreMinimalDuration);
            s["sensitivity"] = prefs.Sensitivity.ToString();
            s["octave"] = prefs.Octave.ToString();
            s["output_midi_tempo"] = prefs.Tempo.ToString();
            s["frame_start"] = prefs.StartFrame.ToString();
            s["frame_end"] = prefs.EndFrame.ToString();
            s["blackkey_relative_position"] = prefs.BlackKeyRelativePosition.ToString("F3");
            s["keys_pos_count"] = prefs.KeyCount.ToString();
            s["keyp_spark_y_pos"] = prefs.SparksYPosition.ToString();
            s["use_sparks"] = B(prefs.UseSparks);
            s["rollcheck"] = B(prefs.Rollcheck);
            s["rollcheck_priority"] = B(prefs.RollcheckPriority);
            s["use_alternate_keys"] = B(prefs.UseAlternateKeys);
            s["autoclose"] = B(prefs.AutoClose);
            s["xoffset_whitekeys"] = prefs.XOffsetWhiteKeys.ToString();
            s["yoffset_whitekeys"] = prefs.YOffsetWhiteKeys.ToString();
            s["yoffset_blackkeys"] = prefs.YOffsetBlackKeys.ToString();
            s["whitekey_width"] = ((int)prefs.WhiteKeyWidth).ToString();
            s["use_percolor_sensitivity"] = B(prefs.UsePerColorSensitivity);

            // color channels — "0,0, 1,1, 2,2, ..." format (light,dark per color)
            s["color_channel_accordance"] = string.Join(",",
                prefs.Colors.SelectMany(c => new[] { c.MidiChannel, c.MidiChannel }));
            s["channel_prog_accordance"] = string.Join(",",
                prefs.Colors.SelectMany(c => new[] { c.MidiProgram, c.MidiProgram }));

            // colors — "R:G:B,R:G:B,..." format
            s["keyp_colors"] = string.Join(",",
                prefs.Colors.SelectMany(c => new[]
                {
                    $"{c.Light.R}:{c.Light.G}:{c.Light.B}",
                    $"{c.Dark.R}:{c.Dark.G}:{c.Dark.B}"
                }));

            // Key positions
            s["keys_pos"] = string.Join(",",
                prefs.KeyPositions.Select(p => $"{(int)p.RelativeX}:{(int)p.RelativeY}"));

            // Per-color sensitivity
            s["percolor_sensitivity"] = string.Join(",",
                prefs.PerColorDelta.Select(d => d.ToString("F2")));

            // Sparks sensitivity
            s["keyp_colors_sparks_sensitivity"] = string.Join(",",
                prefs.SparksColorSensitivity.Select(d => d.ToString("F2")));

            _parser.WriteFile(filePath, data);
        }

        public void Load(AppPreferences prefs, string filePath)
        {
            if (!File.Exists(filePath))
                return;

            var data = _parser.ReadFile(filePath);
            var s = data[Section];

            TrySet(s, "midi_track_name", v => prefs.MidiTrackName = v);
            TrySet(s, "debug", v => prefs.Debug = ToBool(v));
            TrySet(s, "notes_overlap", v => prefs.NotesOverlap = ToBool(v));
            TrySet(s, "resize", v => prefs.Resize = ToBool(v));
            TrySet(s, "resize_width", v => prefs.ResizeWidth = int.Parse(v));
            TrySet(s, "resize_height", v => prefs.ResizeHeight = int.Parse(v));
            TrySet(s, "minimal_note_duration", v => prefs.MinimalDuration = double.Parse(v));
            TrySet(s, "ignore_notes_with_minimal_duration", v => prefs.IgnoreMinimalDuration = ToBool(v));
            TrySet(s, "sensitivity", v => prefs.Sensitivity = int.Parse(v));
            TrySet(s, "octave", v => prefs.Octave = int.Parse(v));
            TrySet(s, "output_midi_tempo", v => prefs.Tempo = int.Parse(v));
            TrySet(s, "frame_start", v => prefs.StartFrame = int.Parse(v));
            TrySet(s, "frame_end", v => prefs.EndFrame = int.Parse(v));
            TrySet(s, "blackkey_relative_position", v => prefs.BlackKeyRelativePosition = double.Parse(v));
            TrySet(s, "keys_pos_count", v => prefs.KeyCount = int.Parse(v));
            TrySet(s, "keyp_spark_y_pos", v => prefs.SparksYPosition = int.Parse(v));
            TrySet(s, "use_sparks", v => prefs.UseSparks = ToBool(v));
            TrySet(s, "rollcheck", v => prefs.Rollcheck = ToBool(v));
            TrySet(s, "rollcheck_priority", v => prefs.RollcheckPriority = ToBool(v));
            TrySet(s, "use_alternate_keys", v => prefs.UseAlternateKeys = ToBool(v));
            TrySet(s, "autoclose", v => prefs.AutoClose = ToBool(v));
            TrySet(s, "xoffset_whitekeys", v => prefs.XOffsetWhiteKeys = int.Parse(v));
            TrySet(s, "yoffset_whitekeys", v => prefs.YOffsetWhiteKeys = int.Parse(v));
            TrySet(s, "yoffset_blackkeys", v => prefs.YOffsetBlackKeys = int.Parse(v));
            TrySet(s, "whitekey_width", v => prefs.WhiteKeyWidth = double.Parse(v));
            TrySet(s, "use_percolor_sensitivity", v => prefs.UsePerColorSensitivity = ToBool(v));

            // color channels — flat list: ch_for_light_0, ch_for_dark_0, ch_for_light_1, ...
            TrySet(s, "color_channel_accordance", v =>
            {
                var vals = ParseIntList(v);
                for (int i = 0; i < prefs.Colors.Count && i * 2 + 1 < vals.Count; i++)
                    prefs.Colors[i].MidiChannel = vals[i * 2];
            });

            TrySet(s, "channel_prog_accordance", v =>
            {
                var vals = ParseIntList(v);
                for (int i = 0; i < prefs.Colors.Count && i * 2 + 1 < vals.Count; i++)
                    prefs.Colors[i].MidiProgram = vals[i * 2];
            });

            // keyp_colors — flat list of "R:G:B" tokens, pairs → (light, dark)
            TrySet(s, "keyp_colors", v =>
            {
                var tokens = v.Split(',', StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i + 1 < tokens.Length && i / 2 < prefs.Colors.Count; i += 2)
                {
                    prefs.Colors[i / 2].Light = ParseRgb(tokens[i]);
                    prefs.Colors[i / 2].Dark = ParseRgb(tokens[i + 1]);
                }
            });

            // Key positions
            TrySet(s, "keys_pos", v =>
            {
                prefs.KeyPositions.Clear();
                foreach (var token in v.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = token.Split(':');
                    if (parts.Length == 2)
                        prefs.KeyPositions.Add(new KeyPosition(int.Parse(parts[0]), int.Parse(parts[1])));
                }
            });

            // Per-color sensitivity
            TrySet(s, "percolor_sensitivity", v =>
            {
                var vals = v.Split(',').Select(double.Parse).ToList();
                prefs.PerColorDelta.Clear();
                foreach (var d in vals) prefs.PerColorDelta.Add(d);
            });

            // Sparks sensitivity
            TrySet(s, "keyp_colors_sparks_sensitivity", v =>
            {
                var vals = v.Split(',').Select(double.Parse).ToList();
                prefs.SparksColorSensitivity.Clear();
                foreach (var d in vals) prefs.SparksColorSensitivity.Add(d);
            });
        }

        /// <summary>
        /// Returns the path to the INI file to use, preferring a local
        /// v2m.ini over the global ~/.v2m.ini.
        /// Ports get_ini_filepath() from cli.py.
        /// </summary>
        public string GetIniFilePath()
        {
            if (File.Exists("v2m.ini")) return "v2m.ini";
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".v2m.ini");
        }

        //  Private Helpers 

        private static void TrySet(KeyDataCollection section, string key, Action<string> setter)
        {
            var val = section[key];
            if (!string.IsNullOrWhiteSpace(val))
            {
                try { setter(val.Trim()); }
                catch { /* ignore malformed values */ }
            }
        }

        private static bool ToBool(string v) => v.Trim() is "1" or "true" or "True";
        private static string B(bool v) => v ? "1" : "0";

        private static List<int> ParseIntList(string v) =>
            v.Split(',', StringSplitOptions.RemoveEmptyEntries)
             .Select(x => int.TryParse(x.Trim(), out int n) ? n : 0)
             .ToList();

        private static RgbColor ParseRgb(string token)
        {
            var parts = token.Trim().Split(':');
            return parts.Length == 3
                ? new RgbColor(byte.Parse(parts[0]), byte.Parse(parts[1]), byte.Parse(parts[2]))
                : RgbColor.Black;
        }
    }
}
