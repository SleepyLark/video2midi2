using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Video2Midi2.Models;
using Video2Midi2.Services;
using Video2Midi2.Services.Interfaces;
using Video2Midi2.ViewModels;

namespace Video2Midi2;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Configure Serilog
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File("logs/v2m.log", rollingInterval: RollingInterval.Day)
            .CreateLogger();

        // Build DI container
        var services = new ServiceCollection();
        RegisterServices(services);
        Services = services.BuildServiceProvider();

        // Show main window
        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private static void RegisterServices(IServiceCollection services)
    {
        // Models
        services.AddSingleton<AppPreferences>();

        // Services
        services.AddSingleton<IVideoService, VideoService>();
        services.AddSingleton<IMidiProcessingService, MidiProcessingService>();
        services.AddSingleton<IMidiExportService, MidiExportService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IFileService, FileService>();

        // ViewModels
        services.AddSingleton<MainViewModel>();

        // Views
        services.AddSingleton<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}