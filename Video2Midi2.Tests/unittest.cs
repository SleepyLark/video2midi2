using FluentAssertions;
using Moq;
using OpenCvSharp;
using Video2Midi2.Models;
using Video2Midi2.Services;
using Video2Midi2.Services.Interfaces;
using Xunit;

namespace Video2Midi2.Tests
{
    // 
    // MidiProcessingService Tests
    // 

    public class MidiProcessingServiceTests
    {
        private readonly MidiProcessingService _sut = new();

        //  Key Classification 

        [Theory]
        [InlineData(1, true)]   // C#
        [InlineData(3, true)]   // D#
        [InlineData(6, true)]   // F#
        [InlineData(8, true)]   // G#
        [InlineData(10, true)]   // A#
        [InlineData(0, false)]  // C
        [InlineData(2, false)]  // D
        [InlineData(4, false)]  // E
        [InlineData(5, false)]  // F
        [InlineData(7, false)]  // G
        [InlineData(9, false)]  // A
        [InlineData(11, false)]  // B
        public void IsBlackKey_ReturnsCorrectValue(int semitone, bool expected)
        {
            _sut.IsBlackKey(semitone).Should().Be(expected);
            _sut.IsWhiteKey(semitone).Should().Be(!expected);
        }

        //  Key Position Layout 

        [Fact]
        public void UpdateKeyPositions_Rebuild_GeneratesCorrectCount()
        {
            var prefs = new AppPreferences { KeyCount = 88 };
            _sut.UpdateKeyPositions(prefs, rebuild: true);
            prefs.KeyPositions.Should().HaveCount(88);
        }

        [Fact]
        public void UpdateKeyPositions_BlackKeys_HaveNonZeroYOffset()
        {
            var prefs = new AppPreferences { KeyCount = 12, YOffsetBlackKeys = -30 };
            _sut.UpdateKeyPositions(prefs, rebuild: true);

            // Semitone 1 (C#) should have a negative Y offset
            prefs.KeyPositions[1].RelativeY.Should().NotBe(0);
        }

        [Fact]
        public void UpdateKeyPositions_WhiteKeys_HaveZeroYOffset()
        {
            var prefs = new AppPreferences { KeyCount = 12, YOffsetBlackKeys = -30 };
            _sut.UpdateKeyPositions(prefs, rebuild: true);

            prefs.KeyPositions[0].RelativeY.Should().Be(0);  // C is white
        }

        //  SnapToGrid 

        [Theory]
        [InlineData(0.0, 32, 0.0)]
        [InlineData(1.0, 32, 1.0)]
        [InlineData(0.5, 32, 0.5)]
        [InlineData(0.123, 32, 0.125)]   // nearest 1/32
        [InlineData(0.999, 16, 0.9375)]  // nearest 1/16 below 1.0
        public void SnapToGrid_QuantizesCorrectly(double input, int gridSize, double expected)
        {
            _sut.SnapToGrid(input, gridSize).Should().BeApproximately(expected, precision: 1e-6);
        }

        //  Vector Rotation 

        [Fact]
        public void VRotate_ZeroDegrees_ReturnsOriginalVector()
        {
            var (x, y) = _sut.VRotate(1.0, 0.0, 0.0);
            x.Should().BeApproximately(0.0, 1e-9);
            y.Should().BeApproximately(1.0, 1e-9);  // v_rotate rotates (x,y) — note Python formula uses v[1]*cos - v[0]*sin
        }

        [Fact]
        public void VRotate_90Degrees_SwapsAxes()
        {
            var (x, y) = _sut.VRotate(1.0, 0.0, 90.0);
            // At 90°: x_out = y*cos(90) - x*sin(90) = 0 - 1 = -1
            //         y_out = y*sin(90) + x*cos(90) = 0 + 0 = 0
            x.Should().BeApproximately(-1.0, 1e-9);
            y.Should().BeApproximately(0.0, 1e-9);
        }

        //  color Detection 

        [Fact]
        public void DetectKeyPresses_MatchingColor_ReturnsPressed()
        {
            var prefs = BuildSingleKeyPrefs(lightColor: new RgbColor(100, 200, 50));

            // Mock video service to return that exact color at position (60, 673)
            var mockVideo = new Mock<IVideoService>();
            mockVideo.Setup(v => v.SamplePixel(It.IsAny<Mat>(), 60, 673))
                     .Returns((100, 200, 50));  // R, G, B
            mockVideo.Setup(v => v.VideoWidth).Returns(1280);
            mockVideo.Setup(v => v.VideoHeight).Returns(720);

            var results = _sut.DetectKeyPresses(new Mat(), prefs, mockVideo.Object);

            results.Should().HaveCount(1);
            results[0].State.Should().Be(KeyPressState.Pressed);
        }

        [Fact]
        public void DetectKeyPresses_ColorOutsideDelta_ReturnsUnpressed()
        {
            var prefs = BuildSingleKeyPrefs(lightColor: new RgbColor(100, 200, 50));
            prefs.Sensitivity = 10;  // tight tolerance

            var mockVideo = new Mock<IVideoService>();
            // Return a color 50 away — well outside tolerance of 10
            mockVideo.Setup(v => v.SamplePixel(It.IsAny<Mat>(), 60, 673))
                     .Returns((150, 200, 50));
            mockVideo.Setup(v => v.VideoWidth).Returns(1280);
            mockVideo.Setup(v => v.VideoHeight).Returns(720);

            var results = _sut.DetectKeyPresses(new Mat(), prefs, mockVideo.Object);

            results[0].State.Should().Be(KeyPressState.Unpressed);
        }

        [Fact]
        public void DetectKeyPresses_BlackPixel_AndColorIsBlack_IsSkipped()
        {
            // Black [0,0,0] colors are "disabled" and should never match
            var prefs = BuildSingleKeyPrefs(lightColor: RgbColor.Black);

            var mockVideo = new Mock<IVideoService>();
            mockVideo.Setup(v => v.SamplePixel(It.IsAny<Mat>(), It.IsAny<int>(), It.IsAny<int>()))
                     .Returns((0, 0, 0));
            mockVideo.Setup(v => v.VideoWidth).Returns(1280);
            mockVideo.Setup(v => v.VideoHeight).Returns(720);

            var results = _sut.DetectKeyPresses(new Mat(), prefs, mockVideo.Object);

            results[0].State.Should().Be(KeyPressState.Unpressed);
        }

        //  ProcessVideoAsync 

        [Fact]
        public async Task ProcessVideoAsync_CancellationRequested_ThrowsOrReturnsClean()
        {
            var prefs = new AppPreferences { StartFrame = 0, EndFrame = 1000 };
            _sut.UpdateKeyPositions(prefs, rebuild: true);

            var mockVideo = new Mock<IVideoService>();
            mockVideo.Setup(v => v.GetFrame(It.IsAny<int>())).Returns((Mat?)null);  // no frames
            mockVideo.Setup(v => v.Fps).Returns(30.0);
            mockVideo.Setup(v => v.VideoWidth).Returns(1280);
            mockVideo.Setup(v => v.VideoHeight).Returns(720);

            using var cts = new CancellationTokenSource();
            cts.Cancel();   // cancel immediately

            Func<Task> act = () => _sut.ProcessVideoAsync(prefs, mockVideo.Object,
                cancellationToken: cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        //  Helpers 

        private static AppPreferences BuildSingleKeyPrefs(RgbColor lightColor)
        {
            var prefs = new AppPreferences
            {
                Sensitivity = 50,
                XOffsetWhiteKeys = 60,
                YOffsetWhiteKeys = 673
            };

            prefs.KeyPositions.Add(new KeyPosition(0, 0));
            prefs.Colors[0].Light = lightColor;
            prefs.Colors[0].Dark = lightColor;

            return prefs;
        }
    }

    // 
    // MidiExportService Tests
    // 

    public class MidiExportServiceTests
    {
        private readonly MidiExportService _sut = new();

        [Fact]
        public void Save_WithNotes_CreatesFile()
        {
            var prefs = new AppPreferences { Tempo = 120, MidiTrackName = "Test" };
            var notes = new List<MidiNote>
            {
                new(0, 0, 60, 0.0, 1.0, 100),
                new(0, 0, 64, 1.0, 1.0, 100)
            };

            string path = Path.GetTempFileName() + ".mid";
            try
            {
                var (success, _) = _sut.Save(notes, prefs, path);
                success.Should().BeTrue();
                File.Exists(path).Should().BeTrue();
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void Save_WithNoNotes_ReturnsFalse()
        {
            var prefs = new AppPreferences();
            var (success, msg) = _sut.Save(Enumerable.Empty<MidiNote>(), prefs, "out.mid");
            success.Should().BeFalse();
            msg.Should().Contain("No notes");
        }

        [Fact]
        public void SavePerChannel_CreatesOneFilePerUsedChannel()
        {
            var prefs = new AppPreferences { Tempo = 120 };
            var notes = new List<MidiNote>
            {
                new(0, 0, 60, 0.0, 1.0, 100),
                new(0, 1, 64, 0.0, 1.0, 100),
                new(0, 2, 67, 0.0, 1.0, 100),
            };

            string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "test.mid");

            try
            {
                var (success, _) = _sut.SavePerChannel(notes, prefs, path);
                success.Should().BeTrue();

                var files = Directory.GetFiles(dir, "*.mid");
                files.Should().HaveCount(3, "one file per active channel");
            }
            finally { Directory.Delete(dir, recursive: true); }
        }

        [Fact]
        public void SyncStartPositions_CloseNotes_AlignedToFirst()
        {
            var prefs = new AppPreferences { Tempo = 120 };
            var notes = new List<MidiNote>
            {
                new(0, 0, 60, 0.000, 1.0, 100),
                new(0, 0, 64, 0.005, 1.0, 100),  // 5ms later — within default 1000ms delta
                new(0, 0, 67, 2.000, 1.0, 100),  // far away — should NOT be synced
            };

            var result = _sut.SyncStartPositions(notes, deltaSeconds: 1000, prefs);

            result[0].StartTime.Should().Be(result[1].StartTime, "close notes snap to same start");
            result[2].StartTime.Should().BeApproximately(2.0, 1e-6, "far note is unchanged");
        }
    }

    // 
    // SettingsService Tests
    // 

    public class SettingsServiceTests
    {
        private readonly SettingsService _sut = new();

        [Fact]
        public void SaveThenLoad_RoundTripsAllScalarValues()
        {
            var original = new AppPreferences
            {
                MidiTrackName = "My Track",
                Tempo = 140,
                Octave = 4,
                Sensitivity = 75,
                StartFrame = 30,
                EndFrame = 900,
                WhiteKeyWidth = 26.5,
                Rollcheck = true,
                UseSparks = true,
            };

            string path = Path.GetTempFileName();
            try
            {
                _sut.Save(original, path);

                var loaded = new AppPreferences();
                _sut.Load(loaded, path);

                loaded.MidiTrackName.Should().Be(original.MidiTrackName);
                loaded.Tempo.Should().Be(original.Tempo);
                loaded.Octave.Should().Be(original.Octave);
                loaded.Sensitivity.Should().Be(original.Sensitivity);
                loaded.StartFrame.Should().Be(original.StartFrame);
                loaded.EndFrame.Should().Be(original.EndFrame);
                loaded.WhiteKeyWidth.Should().BeApproximately(original.WhiteKeyWidth, 0.5);
                loaded.Rollcheck.Should().Be(original.Rollcheck);
                loaded.UseSparks.Should().Be(original.UseSparks);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Load_MissingFile_LeavesDefaultsUntouched()
        {
            var prefs = new AppPreferences { Tempo = 120 };
            _sut.Load(prefs, "/nonexistent/path/v2m.ini");
            prefs.Tempo.Should().Be(120, "missing file should not change defaults");
        }

        [Fact]
        public void Load_PartialFile_OnlyOverridesPresentKeys()
        {
            string path = Path.GetTempFileName();
            File.WriteAllText(path, "[options]\ntempo=200\n");

            var prefs = new AppPreferences { Tempo = 120, Sensitivity = 80 };
            _sut.Load(prefs, path);

            prefs.Tempo.Should().Be(200, "key present in file should be updated");
            prefs.Sensitivity.Should().Be(80, "key absent from file should be unchanged");

            File.Delete(path);
        }
    }

    // 
    // AppPreferences Tests
    // 

    public class AppPreferencesTests
    {
        [Fact]
        public void PropertyChanged_RaisedOnSet()
        {
            var prefs = new AppPreferences();
            bool fired = false;
            prefs.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AppPreferences.Tempo)) fired = true;
            };

            prefs.Tempo = 160;
            fired.Should().BeTrue("INotifyPropertyChanged should fire for Tempo");
        }

        [Fact]
        public void DefaultValues_AreReasonable()
        {
            var prefs = new AppPreferences();
            prefs.Tempo.Should().Be(120);
            prefs.Octave.Should().Be(3);
            prefs.Sensitivity.Should().Be(90);
            prefs.KeyCount.Should().Be(88);
            prefs.Colors.Should().HaveCount(12);
        }

        [Fact]
        public void MidiChannelClamp_NeverExceedsBounds()
        {
            var entry = new ColorEntry(100, 100, 100, 50, 50, 50);
            entry.MidiChannel = 99;   // over max
            entry.MidiChannel.Should().Be(15);

            entry.MidiChannel = -5;   // under min
            entry.MidiChannel.Should().Be(0);
        }
    }
}
