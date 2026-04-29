using System.Text.Json;
using Cloris.Aion2Flow.Services.Logging;

namespace Cloris.Aion2Flow.Services.Settings;

public sealed class SettingsService
{
    private const string SettingsFileName = "settings.json";
    private readonly Lock _writeLock = new();
    private AppSettings _current;

    public AppSettings Current => _current;

    public string FilePath { get; }

    public SettingsService()
        : this(Path.Combine(WorkingDirectoryResolver.GetWorkingDirectory(), SettingsFileName))
    {
    }

    public SettingsService(string filePath)
    {
        FilePath = filePath;
        _current = Load();
    }


    public void Update(Action<AppSettings> mutate)
    {
        var snapshot = Clone(_current);
        mutate(snapshot);
        _current = snapshot;
        Save(snapshot);
    }

    private AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new AppSettings();
            }

            using var stream = File.OpenRead(FilePath);
            var settings = JsonSerializer.Deserialize(stream, AppSettingsJsonContext.Default.AppSettings);
            return settings ?? new AppSettings();
        }
        catch (Exception ex)
        {
            AppLog.Write(AppLogLevel.Warning, $"Failed to load settings from '{FilePath}': {ex}");
            return new AppSettings();
        }
    }

    private void Save(AppSettings settings)
    {
        try
        {
            lock (_writeLock)
            {
                var directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var tempPath = FilePath + ".tmp";
                using (var stream = File.Create(tempPath))
                {
                    JsonSerializer.Serialize(stream, settings, AppSettingsJsonContext.Default.AppSettings);
                }

                File.Move(tempPath, FilePath, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            AppLog.Write(AppLogLevel.Warning, $"Failed to save settings to '{FilePath}': {ex}");
        }
    }

    private static AppSettings Clone(AppSettings source) => new()
    {
        TopmostMode = source.TopmostMode,
        MaxVisibleCombatantRows = source.MaxVisibleCombatantRows,
        Language = source.Language,
        BattleResetHotkeyModifiers = source.BattleResetHotkeyModifiers,
        BattleResetHotkeyVirtualKey = source.BattleResetHotkeyVirtualKey
    };
}
